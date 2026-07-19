namespace PimaxVrcSupervisor;

internal sealed record SteamVrRestartTimeouts(
    TimeSpan OldRuntimeExit,
    TimeSpan ReplacementAppearance,
    TimeSpan ReplacementReadiness,
    TimeSpan VrChatResume);

internal enum SteamVrRestartOutcomeKind
{
    Succeeded,
    SucceededWithVrChat,
    VrChatResumeFailed,
    ShutdownRequestFailed,
    OldRuntimeExitTimedOut,
    ReplacementStartFailed,
    ReplacementReadinessTimedOut
}

internal sealed record SteamVrRestartOutcome(
    SteamVrRestartOutcomeKind Kind,
    string Result,
    string? Error,
    bool OldRuntimeExited,
    SteamVrRuntimeSnapshot? ReplacementRuntime)
{
    public bool Succeeded => Kind is SteamVrRestartOutcomeKind.Succeeded or SteamVrRestartOutcomeKind.SucceededWithVrChat;
    public bool IsWarning => Kind == SteamVrRestartOutcomeKind.VrChatResumeFailed;
}

internal interface ISteamVrRestartRuntime
{
    Task<SteamVrShutdownRequestResult> RequestGracefulShutdownAsync(CancellationToken cancellationToken);
    Task<bool> WaitForOldRuntimeExitAsync(
        SteamVrRuntimeIdentity oldRuntime,
        TimeSpan timeout,
        CancellationToken cancellationToken);
    void StartSteamVr();
    Task<SteamVrRuntimeSnapshot?> WaitForReplacementRuntimeAsync(
        SteamVrRuntimeIdentity oldRuntime,
        TimeSpan timeout,
        CancellationToken cancellationToken);
    Task<bool> WaitForReplacementReadinessAsync(
        SteamVrRuntimeIdentity replacementRuntime,
        TimeSpan timeout,
        CancellationToken cancellationToken);
    void AdoptReplacementRuntime(SteamVrRuntimeSnapshot replacementRuntime);
    Task PrepareForVrChatResumeAsync(CancellationToken cancellationToken);
    bool IsVrChatRunning();
    void StartVrChat();
    Task<bool> WaitForVrChatAsync(TimeSpan timeout, CancellationToken cancellationToken);
    Task RestoreManagedAppsAsync(CancellationToken cancellationToken);
    void ReportProgress(string progress);
    void WriteDiagnostic(string message);
}

internal sealed class SteamVrRestartCoordinator
{
    private readonly ISteamVrRestartRuntime _runtime;
    private readonly SteamVrRestartTimeouts _timeouts;

    public SteamVrRestartCoordinator(ISteamVrRestartRuntime runtime, SteamVrRestartTimeouts timeouts)
    {
        _runtime = runtime;
        _timeouts = timeouts;
    }

    public async Task<SteamVrRestartOutcome> RunAsync(
        SteamVrRuntimeSnapshot oldRuntime,
        bool resumeVrChat,
        CancellationToken cancellationToken)
    {
        _runtime.ReportProgress("Requesting SteamVR shutdown...");
        _runtime.WriteDiagnostic(
            $"graceful shutdown request issued; oldSteamVr={oldRuntime.Identity}; resumeVrChat={resumeVrChat}");
        var shutdownResult = await _runtime.RequestGracefulShutdownAsync(cancellationToken);
        _runtime.WriteDiagnostic(
            "graceful shutdown request result"
            + $"; success={shutdownResult.Succeeded}"
            + $"; timedOut={shutdownResult.TimedOut}"
            + $"; mechanism={shutdownResult.Mechanism}"
            + $"; executable={shutdownResult.ExecutablePath ?? "unavailable"}"
            + $"; exitCode={shutdownResult.ExitCode?.ToString() ?? "unavailable"}"
            + $"; error={shutdownResult.Error ?? "none"}");
        if (!shutdownResult.Succeeded)
        {
            return new SteamVrRestartOutcome(
                SteamVrRestartOutcomeKind.ShutdownRequestFailed,
                "SteamVR shutdown request failed.",
                shutdownResult.Error,
                OldRuntimeExited: false,
                ReplacementRuntime: null);
        }

        _runtime.ReportProgress("Waiting for the old SteamVR runtime to exit...");
        _runtime.WriteDiagnostic($"waiting for old runtime; oldSteamVr={oldRuntime.Identity}");
        if (!await _runtime.WaitForOldRuntimeExitAsync(
                oldRuntime.Identity,
                _timeouts.OldRuntimeExit,
                cancellationToken))
        {
            return new SteamVrRestartOutcome(
                SteamVrRestartOutcomeKind.OldRuntimeExitTimedOut,
                "SteamVR did not shut down within the restart timeout.",
                $"Old runtime remained present: {oldRuntime.Identity}.",
                OldRuntimeExited: false,
                ReplacementRuntime: null);
        }

        _runtime.WriteDiagnostic($"old runtime exited; oldSteamVr={oldRuntime.Identity}");
        _runtime.ReportProgress("Starting SteamVR...");
        try
        {
            _runtime.StartSteamVr();
        }
        catch (Exception ex)
        {
            return new SteamVrRestartOutcome(
                SteamVrRestartOutcomeKind.ReplacementStartFailed,
                "SteamVR shut down, but the replacement runtime did not start.",
                ex.Message,
                OldRuntimeExited: true,
                ReplacementRuntime: null);
        }

        _runtime.WriteDiagnostic("replacement launch invoked");
        _runtime.ReportProgress("Waiting for the replacement SteamVR runtime...");
        var replacement = await _runtime.WaitForReplacementRuntimeAsync(
            oldRuntime.Identity,
            _timeouts.ReplacementAppearance,
            cancellationToken);
        if (replacement is null || replacement.Identity == oldRuntime.Identity)
        {
            return new SteamVrRestartOutcome(
                SteamVrRestartOutcomeKind.ReplacementStartFailed,
                "SteamVR shut down, but the replacement runtime did not start.",
                replacement is null
                    ? "No new vrserver identity appeared within the replacement timeout."
                    : $"The old vrserver identity was returned as the replacement: {oldRuntime.Identity}.",
                OldRuntimeExited: true,
                ReplacementRuntime: null);
        }

        _runtime.WriteDiagnostic(
            $"new runtime identity detected; oldSteamVr={oldRuntime.Identity}; newSteamVr={replacement.Identity}");
        _runtime.ReportProgress("Waiting for the replacement SteamVR runtime to become ready...");
        if (!await _runtime.WaitForReplacementReadinessAsync(
                replacement.Identity,
                _timeouts.ReplacementReadiness,
                cancellationToken))
        {
            return new SteamVrRestartOutcome(
                SteamVrRestartOutcomeKind.ReplacementReadinessTimedOut,
                "SteamVR replacement runtime did not become ready within the restart timeout.",
                $"Replacement runtime did not remain healthy: {replacement.Identity}.",
                OldRuntimeExited: true,
                ReplacementRuntime: replacement);
        }

        _runtime.WriteDiagnostic($"replacement ready; newSteamVr={replacement.Identity}");
        _runtime.AdoptReplacementRuntime(replacement);
        if (!resumeVrChat)
        {
            return new SteamVrRestartOutcome(
                SteamVrRestartOutcomeKind.Succeeded,
                "SteamVR restarted.",
                null,
                OldRuntimeExited: true,
                ReplacementRuntime: replacement);
        }

        _runtime.ReportProgress("Restoring VRChat...");
        _runtime.WriteDiagnostic("conditional VRChat resume initiated");
        try
        {
            await _runtime.PrepareForVrChatResumeAsync(cancellationToken);
            if (!_runtime.IsVrChatRunning())
            {
                _runtime.StartVrChat();
            }

            if (!await _runtime.WaitForVrChatAsync(_timeouts.VrChatResume, cancellationToken))
            {
                return VrChatResumeFailure(replacement, "VRChat did not become running within the resume timeout.");
            }

            await _runtime.RestoreManagedAppsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VrChatResumeFailure(replacement, ex.Message);
        }

        return new SteamVrRestartOutcome(
            SteamVrRestartOutcomeKind.SucceededWithVrChat,
            "SteamVR restarted and VRChat resumed.",
            null,
            OldRuntimeExited: true,
            ReplacementRuntime: replacement);
    }

    private static SteamVrRestartOutcome VrChatResumeFailure(
        SteamVrRuntimeSnapshot replacement,
        string error)
        => new(
            SteamVrRestartOutcomeKind.VrChatResumeFailed,
            "SteamVR restarted, but VRChat could not be resumed.",
            error,
            OldRuntimeExited: true,
            ReplacementRuntime: replacement);
}
