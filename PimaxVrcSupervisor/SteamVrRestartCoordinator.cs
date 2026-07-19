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
    SteamVrRuntimeSnapshot? CaptureReplacementRuntime(SteamVrRuntimeIdentity oldRuntime);
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
            $"captured old SteamVR runtime; oldSteamVr={oldRuntime.Identity}; resumeVrChat={resumeVrChat}");
        var shutdownResult = await _runtime.RequestGracefulShutdownAsync(cancellationToken);
        if (!shutdownResult.RequestIssued)
        {
            _runtime.WriteDiagnostic(
                "SteamVR shutdown invocation failed"
                + $"; mechanism={shutdownResult.Mechanism}"
                + $"; executable={shutdownResult.ExecutablePath ?? "unavailable"}"
                + $"; error={shutdownResult.Error ?? "unknown"}");
            return new SteamVrRestartOutcome(
                SteamVrRestartOutcomeKind.ShutdownRequestFailed,
                "SteamVR shutdown request failed.",
                shutdownResult.Error,
                OldRuntimeExited: false,
                ReplacementRuntime: null);
        }

        _runtime.WriteDiagnostic(
            "SteamVR shutdown request issued"
            + $"; mechanism={shutdownResult.Mechanism}"
            + $"; executable={shutdownResult.ExecutablePath ?? "unavailable"}");
        ObserveHelperCompletion(shutdownResult.HelperCompletion);
        _runtime.ReportProgress("Waiting for SteamVR to close...");
        _runtime.WriteDiagnostic($"waiting for old SteamVR runtime to exit; oldSteamVr={oldRuntime.Identity}");
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

        _runtime.WriteDiagnostic($"old SteamVR runtime exited; oldSteamVr={oldRuntime.Identity}");
        var replacement = _runtime.CaptureReplacementRuntime(oldRuntime.Identity);
        if (replacement is null)
        {
            _runtime.ReportProgress("Starting SteamVR...");
            _runtime.WriteDiagnostic("starting SteamVR through Steam");
            try
            {
                _runtime.StartSteamVr();
            }
            catch (Exception ex)
            {
                return new SteamVrRestartOutcome(
                    SteamVrRestartOutcomeKind.ReplacementStartFailed,
                    "SteamVR shut down, but could not be started again.",
                    ex.Message,
                    OldRuntimeExited: true,
                    ReplacementRuntime: null);
            }

            _runtime.ReportProgress("Waiting for SteamVR...");
            replacement = await _runtime.WaitForReplacementRuntimeAsync(
                oldRuntime.Identity,
                _timeouts.ReplacementAppearance,
                cancellationToken);
        }
        else
        {
            _runtime.WriteDiagnostic(
                $"replacement SteamVR runtime already present; launch skipped; replacement={replacement.Identity}");
        }

        if (replacement is null || replacement.Identity == oldRuntime.Identity)
        {
            return new SteamVrRestartOutcome(
                SteamVrRestartOutcomeKind.ReplacementStartFailed,
                "SteamVR shut down, but could not be started again.",
                replacement is null
                    ? "No new vrserver identity appeared within the replacement timeout."
                    : $"The old vrserver identity was returned as the replacement: {oldRuntime.Identity}.",
                OldRuntimeExited: true,
                ReplacementRuntime: null);
        }

        _runtime.WriteDiagnostic(
            $"replacement SteamVR runtime detected; oldSteamVr={oldRuntime.Identity}; newSteamVr={replacement.Identity}");
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

        _runtime.WriteDiagnostic($"SteamVR replacement runtime ready; newSteamVr={replacement.Identity}");
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

        _runtime.ReportProgress("Resuming VRChat...");
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

    private void ObserveHelperCompletion(Task<SteamVrShutdownHelperDiagnostics>? completion)
    {
        if (completion is null)
        {
            return;
        }

        _ = RecordHelperCompletionAsync(completion);
    }

    private async Task RecordHelperCompletionAsync(Task<SteamVrShutdownHelperDiagnostics> completion)
    {
        try
        {
            var diagnostics = await completion;
            _runtime.WriteDiagnostic(
                "vrstartup shutdown helper completed"
                + $"; exitCode={diagnostics.ExitCode?.ToString() ?? "unavailable"}"
                + $"; error={diagnostics.Error ?? "none"}"
                + "; authoritativeCompletion=old-runtime-observation");
        }
        catch (Exception ex)
        {
            _runtime.WriteDiagnostic(
                "vrstartup shutdown helper diagnostics unavailable"
                + $"; error={ex.Message}"
                + "; authoritativeCompletion=old-runtime-observation");
        }
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
