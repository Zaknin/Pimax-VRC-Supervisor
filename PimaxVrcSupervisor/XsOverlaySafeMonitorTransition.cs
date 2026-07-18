using System.Diagnostics;

namespace PimaxVrcSupervisor;

internal sealed record XsOverlayProcessSnapshot(
    int ProcessId,
    int SessionId,
    string ProcessName,
    DateTime StartTimeUtc,
    string? ExecutablePath);

internal sealed record XsOverlayRestartInformation(
    string ExecutablePath,
    string WorkingDirectory,
    string RestartMechanism,
    bool? AppearsSteamLaunched,
    XsOverlayLaunchTarget LaunchTarget);

internal interface IXsOverlayProcessPlatform
{
    Task<IReadOnlyList<XsOverlayProcessSnapshot>> FindRunningAsync(CancellationToken cancellationToken);
    Task<bool> RequestGracefulCloseAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken);
    Task<bool> IsSameProcessRunningAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken);
    Task<bool> ForceTerminateAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken);
}

internal sealed record XsOverlayMonitorTransitionEvent
{
    public string OperationName { get; init; } = XsOverlaySafeMonitorTransitionCoordinator.OperationName;
    public string OperationId { get; init; } = "";
    public string CorrelationId { get; init; } = "";
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string EventType { get; init; } = "";
    public int? OriginalPid { get; init; }
    public int? RestartedPid { get; init; }
    public int? SessionId { get; init; }
    public string? ExecutableIdentity { get; init; }
    public string? SafeExecutablePath { get; init; }
    public string? RestartMechanism { get; init; }
    public string? LaunchRoute { get; init; }
    public string? ValidatedAppId { get; init; }
    public string? RegistrationSource { get; init; }
    public string? WindowVerificationResult { get; init; }
    public string? StopMechanism { get; init; }
    public double? StopElapsedMilliseconds { get; init; }
    public string? MonitorResult { get; init; }
    public double? TopologySettleElapsedMilliseconds { get; init; }
    public string? RestartResult { get; init; }
    public string? Outcome { get; init; }
    public string? SkipReason { get; init; }
}

internal sealed record XsOverlayMonitorTransitionResult(
    string Outcome,
    bool MonitorShutdownAttempted,
    bool MonitorShutdownSucceeded,
    bool XsOverlayWasRunning,
    bool XsOverlayStoppedBySupervisor,
    bool RestartAttempted,
    bool RestartSucceeded,
    int? OriginalPid,
    int? RestartedPid,
    XsOverlayLaunchRoute? LaunchRoute,
    XsOverlayWindowVerificationResult WindowVerificationResult);

internal sealed class XsOverlaySafeMonitorTransitionCoordinator
{
    internal const string OperationName = "xsOverlaySafeMonitorShutdown";

    private readonly IXsOverlayProcessPlatform _processes;
    private readonly IXsOverlayLauncher _launcher;
    private readonly IXsOverlayWindowObserver _windowObserver;
    private readonly Func<CancellationToken, Task> _disableSecondaryMonitorsAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Action<XsOverlayMonitorTransitionEvent> _diagnostics;
    private readonly Action<string> _message;
    private readonly TimeSpan _gracefulStopTimeout;
    private readonly TimeSpan _processPollInterval;
    private readonly TimeSpan _topologySettleDuration;
    private readonly TimeSpan _restartVerificationTimeout;
    private readonly Func<string> _operationIdProvider;

    public XsOverlaySafeMonitorTransitionCoordinator(
        IXsOverlayProcessPlatform processes,
        IXsOverlayLauncher launcher,
        IXsOverlayWindowObserver windowObserver,
        Func<CancellationToken, Task> disableSecondaryMonitorsAsync,
        Action<XsOverlayMonitorTransitionEvent> diagnostics,
        Action<string> message,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? gracefulStopTimeout = null,
        TimeSpan? processPollInterval = null,
        TimeSpan? topologySettleDuration = null,
        TimeSpan? restartVerificationTimeout = null,
        Func<string>? operationIdProvider = null)
    {
        _processes = processes;
        _launcher = launcher;
        _windowObserver = windowObserver;
        _disableSecondaryMonitorsAsync = disableSecondaryMonitorsAsync;
        _diagnostics = diagnostics;
        _message = message;
        _delayAsync = delayAsync ?? Task.Delay;
        _gracefulStopTimeout = gracefulStopTimeout ?? TimeSpan.FromSeconds(2);
        _processPollInterval = processPollInterval ?? TimeSpan.FromMilliseconds(100);
        _topologySettleDuration = topologySettleDuration ?? TimeSpan.FromSeconds(2);
        _restartVerificationTimeout = restartVerificationTimeout ?? TimeSpan.FromSeconds(3);
        _operationIdProvider = operationIdProvider ?? (() => $"xso-monitor-{Guid.NewGuid():N}");
    }

    public async Task<XsOverlayMonitorTransitionResult> RunAsync(
        bool turnOffSecondaryMonitors,
        CancellationToken cancellationToken)
    {
        if (!turnOffSecondaryMonitors)
        {
            return Result("disabled", monitorAttempted: false);
        }

        var operationId = _operationIdProvider();
        Write(operationId, "xsOverlayDetectionStarted", outcome: "started");
        IReadOnlyList<XsOverlayProcessSnapshot> running;
        try
        {
            running = await _processes.FindRunningAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Write(operationId, "complete", outcome: "cancelled", skipReason: "cancelled before process detection completed");
            return Result("cancelled", monitorAttempted: false);
        }
        catch (Exception ex)
        {
            Write(operationId, "complete", outcome: "detectionFailed", skipReason: ex.Message);
            _message($"XSOverlay detection failed; secondary-monitor shutdown was skipped: {ex.Message}");
            return Result("detectionFailed", monitorAttempted: false);
        }

        if (running.Count == 0)
        {
            Write(operationId, "xsOverlayNotRunning", outcome: "notRunning");
            _message("XSOverlay was not running; no overlay restart is required.");
            return await RunMonitorOnlyAsync(operationId, cancellationToken);
        }

        if (running.Count != 1)
        {
            const string reason = "more than one exact XSOverlay process was detected; ownership is ambiguous";
            Write(operationId, "xsOverlayRestartInformationUnavailable", outcome: "skipped", skipReason: reason);
            Write(operationId, "complete", outcome: "skipped", skipReason: reason);
            _message("Multiple XSOverlay instances were detected; secondary-monitor shutdown was skipped to avoid unsafe ownership.");
            return Result("ambiguousOwnership", monitorAttempted: false, xsOverlayWasRunning: true);
        }

        var original = running[0];
        Write(operationId, "xsOverlayDetected", original, outcome: "detected");
        Write(operationId, "xsOverlayLaunchTargetDiscoveryStarted", original, outcome: "started");
        var launchTarget = _launcher.Discover(original);
        var restart = new XsOverlayRestartInformation(
            launchTarget.ExecutableIdentity ?? original.ExecutablePath ?? "",
            Path.GetDirectoryName(launchTarget.ExecutableIdentity ?? original.ExecutablePath ?? "") ?? "",
            launchTarget.RouteType.ToString(),
            AppearsSteamLaunched: null,
            launchTarget);
        if (!launchTarget.SafeForAutomaticRestart)
        {
            var reason = launchTarget.FailureReason ?? "no safe registered XSOverlay launch route was found";
            Write(operationId, "xsOverlayLaunchTargetUnavailable", original, restart, outcome: "skipped", skipReason: reason);
            Write(operationId, "xsOverlayDirectRestartRefused", original, restart, outcome: "skipped", skipReason: reason);
            Write(operationId, "complete", original, restart, outcome: "skipped", skipReason: reason);
            _message("A safe registered XSOverlay launch route could not be found; XSOverlay was not restarted automatically.");
            return Result("restartInformationUnavailable", monitorAttempted: false, xsOverlayWasRunning: true, originalPid: original.ProcessId);
        }

        Write(
            operationId,
            "xsOverlayLaunchTargetDiscovered",
            original,
            restart,
            outcome: "captured");
        _message("XSOverlay is running; restarting it safely around the secondary-monitor transition.");

        var stopStartedAt = Stopwatch.GetTimestamp();
        var stoppedBySupervisor = false;
        var gracefulRequested = false;
        var forcedTerminationRequested = false;
        var stopMechanism = "none";
        Write(operationId, "xsOverlayStopStarted", original, restart, outcome: "started");
        _message("Stopping XSOverlay before changing monitor topology...");
        try
        {
            if (await _processes.IsSameProcessRunningAsync(original, cancellationToken))
            {
                gracefulRequested = await _processes.RequestGracefulCloseAsync(original, cancellationToken);
                if (gracefulRequested
                    && await WaitForOriginalExitAsync(original, _gracefulStopTimeout, cancellationToken))
                {
                    stoppedBySupervisor = true;
                    stopMechanism = "gracefulClose";
                    Write(
                        operationId,
                        "xsOverlayGracefulStopCompleted",
                        original,
                        restart,
                        stopMechanism,
                        Stopwatch.GetElapsedTime(stopStartedAt).TotalMilliseconds,
                        outcome: "stopped");
                }
                else if (await _processes.IsSameProcessRunningAsync(original, cancellationToken))
                {
                    stopMechanism = "pidSpecificForce";
                    forcedTerminationRequested = true;
                    Write(operationId, "xsOverlayForcedStopStarted", original, restart, stopMechanism, outcome: "started");
                    if (!await _processes.ForceTerminateAsync(original, cancellationToken)
                        || !await WaitForOriginalExitAsync(original, _gracefulStopTimeout, cancellationToken))
                    {
                        Write(
                            operationId,
                            "xsOverlayStopFailed",
                            original,
                            restart,
                            stopMechanism,
                            Stopwatch.GetElapsedTime(stopStartedAt).TotalMilliseconds,
                            outcome: "failed",
                            skipReason: "the exact original PID did not exit");
                        Write(operationId, "complete", original, restart, stopMechanism, outcome: "skipped", skipReason: "XSOverlay could not be stopped safely");
                        _message("XSOverlay could not be stopped safely; secondary-monitor shutdown was skipped.");
                        return Result("stopFailed", monitorAttempted: false, xsOverlayWasRunning: true, originalPid: original.ProcessId);
                    }

                    stoppedBySupervisor = true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!stoppedBySupervisor
                && (gracefulRequested || forcedTerminationRequested)
                && !await _processes.IsSameProcessRunningAsync(original, CancellationToken.None))
            {
                stoppedBySupervisor = true;
                stopMechanism = forcedTerminationRequested ? "pidSpecificForce" : "gracefulClose";
            }

            if (!stoppedBySupervisor)
            {
                Write(operationId, "complete", original, restart, stopMechanism, outcome: "cancelled", skipReason: "cancelled before XSOverlay was stopped");
                return Result("cancelled", monitorAttempted: false, xsOverlayWasRunning: true, originalPid: original.ProcessId);
            }
        }
        catch (Exception ex)
        {
            Write(
                operationId,
                "xsOverlayStopFailed",
                original,
                restart,
                stopMechanism,
                Stopwatch.GetElapsedTime(stopStartedAt).TotalMilliseconds,
                outcome: "failed",
                skipReason: ex.Message);
            _message("XSOverlay could not be stopped safely; secondary-monitor shutdown was skipped.");
            return Result("stopFailed", monitorAttempted: false, xsOverlayWasRunning: true, originalPid: original.ProcessId);
        }

        Write(
            operationId,
            "xsOverlayStopCompleted",
            original,
            restart,
            stopMechanism,
            Stopwatch.GetElapsedTime(stopStartedAt).TotalMilliseconds,
            outcome: stoppedBySupervisor ? "stopped" : "exitedIndependently");

        var monitorAttempted = false;
        var monitorSucceeded = false;
        var restartAttempted = false;
        var restartSucceeded = false;
        int? restartedPid = null;
        var windowVerificationResult = XsOverlayWindowVerificationResult.Unavailable;
        var outcome = "completed";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            monitorAttempted = true;
            Write(operationId, "secondaryMonitorShutdownStarted", original, restart, stopMechanism, monitorResult: "started", outcome: "started");
            _message("XSOverlay stopped. Disabling secondary monitors...");
            await _disableSecondaryMonitorsAsync(cancellationToken);
            monitorSucceeded = true;
            Write(operationId, "secondaryMonitorShutdownCompleted", original, restart, stopMechanism, monitorResult: "succeeded", outcome: "completed");

            if (stoppedBySupervisor)
            {
                _message("Secondary monitors disabled; waiting for display topology to settle...");
                var settleStartedAt = Stopwatch.GetTimestamp();
                Write(operationId, "displayTopologySettleStarted", original, restart, stopMechanism, monitorResult: "succeeded", outcome: "started");
                await _delayAsync(_topologySettleDuration, cancellationToken);
                Write(
                    operationId,
                    "displayTopologySettled",
                    original,
                    restart,
                    stopMechanism,
                    monitorResult: "succeeded",
                    topologySettleElapsedMilliseconds: Stopwatch.GetElapsedTime(settleStartedAt).TotalMilliseconds,
                    outcome: "settled");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            Write(
                operationId,
                monitorAttempted && monitorSucceeded ? "displayTopologySettleFailed" : "secondaryMonitorShutdownFailed",
                original,
                restart,
                stopMechanism,
                monitorResult: monitorSucceeded ? "succeeded" : "cancelled",
                outcome: "cancelled",
                skipReason: "operation cancelled");
        }
        catch (Exception ex)
        {
            outcome = "monitorFailed";
            Write(
                operationId,
                "secondaryMonitorShutdownFailed",
                original,
                restart,
                stopMechanism,
                monitorResult: "failed",
                outcome: "failed",
                skipReason: ex.Message);
            _message($"Secondary-monitor shutdown failed after XSOverlay stopped; attempting to restart XSOverlay: {ex.Message}");
        }
        finally
        {
            if (stoppedBySupervisor)
            {
                restartAttempted = true;
                (restartSucceeded, restartedPid, windowVerificationResult) = await RestartOnceAsync(operationId, original, restart, stopMechanism);
                if (!restartSucceeded)
                {
                    outcome = "restartFailed";
                    _message("XSOverlay restart failed after the monitor transition; SteamVR and VRChat were left running.");
                }
                else if (windowVerificationResult == XsOverlayWindowVerificationResult.UnwantedDesktopWindowDetected)
                {
                    outcome = "completedWithDesktopWindowRegression";
                    _message("XSOverlay restarted, but an unexpected desktop window was detected.");
                }
            }
            else
            {
                Write(operationId, "xsOverlayRestartSkipped", original, restart, stopMechanism, restartResult: "notOwned", outcome: "skipped", skipReason: "the original process exited independently");
            }
        }

        Write(
            operationId,
            "complete",
            original,
            restart,
            stopMechanism,
            monitorResult: monitorSucceeded ? "succeeded" : monitorAttempted ? "failed" : "notAttempted",
            restartResult: restartSucceeded ? "succeeded" : restartAttempted ? "failed" : "notRequired",
            restartedPid: restartedPid,
            outcome: outcome);
        return new(
            outcome,
            monitorAttempted,
            monitorSucceeded,
            XsOverlayWasRunning: true,
            stoppedBySupervisor,
            restartAttempted,
            restartSucceeded,
            original.ProcessId,
            restartedPid,
            restart.LaunchTarget.RouteType,
            windowVerificationResult);
    }

    private async Task<XsOverlayMonitorTransitionResult> RunMonitorOnlyAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            Write(operationId, "secondaryMonitorShutdownStarted", monitorResult: "started", outcome: "started");
            await _disableSecondaryMonitorsAsync(cancellationToken);
            Write(operationId, "secondaryMonitorShutdownCompleted", monitorResult: "succeeded", outcome: "completed");
            Write(operationId, "xsOverlayRestartSkipped", restartResult: "notPreviouslyRunning", outcome: "skipped", skipReason: "XSOverlay was not running before the transition");
            Write(operationId, "complete", monitorResult: "succeeded", restartResult: "notRequired", outcome: "completed");
            return Result("completed", monitorAttempted: true, monitorSucceeded: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Write(operationId, "secondaryMonitorShutdownFailed", monitorResult: "cancelled", outcome: "cancelled", skipReason: "operation cancelled");
            Write(operationId, "complete", monitorResult: "cancelled", outcome: "cancelled");
            return Result("cancelled", monitorAttempted: true);
        }
        catch (Exception ex)
        {
            Write(operationId, "secondaryMonitorShutdownFailed", monitorResult: "failed", outcome: "failed", skipReason: ex.Message);
            Write(operationId, "complete", monitorResult: "failed", outcome: "monitorFailed");
            return Result("monitorFailed", monitorAttempted: true);
        }
    }

    private async Task<bool> WaitForOriginalExitAsync(
        XsOverlayProcessSnapshot original,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (await _processes.IsSameProcessRunningAsync(original, cancellationToken))
        {
            var remaining = timeout - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await _delayAsync(remaining < _processPollInterval ? remaining : _processPollInterval, cancellationToken);
        }

        return true;
    }

    private async Task<(bool Succeeded, int? ProcessId, XsOverlayWindowVerificationResult WindowResult)> RestartOnceAsync(
        string operationId,
        XsOverlayProcessSnapshot original,
        XsOverlayRestartInformation restart,
        string stopMechanism)
    {
        try
        {
            var running = await _processes.FindRunningAsync(CancellationToken.None);
            if (running.Count == 1)
            {
                Write(operationId, "xsOverlayRestartSkipped", original, restart, stopMechanism, restartResult: "alreadyRunning", restartedPid: running[0].ProcessId, outcome: "completed", skipReason: "an exact XSOverlay instance reappeared independently");
                _message("XSOverlay is already running after the monitor transition; a duplicate launch was avoided.");
                return (true, running[0].ProcessId, XsOverlayWindowVerificationResult.Unavailable);
            }

            if (running.Count > 1)
            {
                Write(operationId, "xsOverlayRestartFailed", original, restart, stopMechanism, restartResult: "multipleInstances", outcome: "failed", skipReason: "multiple exact XSOverlay instances appeared before restart");
                return (false, null, XsOverlayWindowVerificationResult.Unavailable);
            }

            Write(operationId, "xsOverlayRestartStarted", original, restart, stopMechanism, restartResult: "started", outcome: "started");
            Write(operationId, "xsOverlayBrokeredRestartStarted", original, restart, stopMechanism, restartResult: "started", outcome: "started");
            _message("Restarting XSOverlay through its registered Steam launch route...");
            var launchRequest = await _launcher.RequestLaunchAsync(restart.LaunchTarget, CancellationToken.None);
            if (!launchRequest.Requested)
            {
                Write(operationId, "xsOverlayBrokeredRestartFailed", original, restart, stopMechanism, restartResult: "requestFailed", outcome: "failed", skipReason: launchRequest.FailureReason);
                return (false, null, XsOverlayWindowVerificationResult.Unavailable);
            }

            Write(operationId, "xsOverlayBrokeredRestartRequested", original, restart, stopMechanism, restartResult: launchRequest.RequestMethod, outcome: "requested");
            var startedAt = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(startedAt) < _restartVerificationTimeout)
            {
                running = await _processes.FindRunningAsync(CancellationToken.None);
                if (running.Count == 1)
                {
                    var pid = running[0].ProcessId;
                    Write(operationId, "xsOverlayWindowVerificationStarted", original, restart, stopMechanism, restartedPid: pid, outcome: "started");
                    var windowResult = await _windowObserver.ObserveAsync(running[0], CancellationToken.None);
                    Write(
                        operationId,
                        "xsOverlayWindowVerificationCompleted",
                        original,
                        restart,
                        stopMechanism,
                        restartedPid: pid,
                        restartResult: windowResult.ToString(),
                        windowVerificationResult: windowResult,
                        outcome: "completed");
                    if (windowResult == XsOverlayWindowVerificationResult.UnwantedDesktopWindowDetected)
                    {
                        Write(operationId, "xsOverlayUnwantedDesktopWindowDetected", original, restart, stopMechanism, restartedPid: pid, outcome: "regression");
                    }

                    Write(operationId, "xsOverlayBrokeredRestartCompleted", original, restart, stopMechanism, restartResult: "oneInstanceVerified", restartedPid: pid, outcome: "completed");
                    Write(operationId, "xsOverlayRestartCompleted", original, restart, stopMechanism, restartResult: "oneInstanceVerified", restartedPid: pid, outcome: "completed");
                    _message(restart.LaunchTarget.RouteType == XsOverlayLaunchRoute.SteamApp
                        ? "XSOverlay restarted successfully through Steam."
                        : "XSOverlay restarted successfully through its registered launch route.");
                    return (true, pid, windowResult);
                }

                if (running.Count > 1)
                {
                    break;
                }

                await _delayAsync(_processPollInterval, CancellationToken.None);
            }

            Write(operationId, "xsOverlayBrokeredRestartFailed", original, restart, stopMechanism, restartResult: "verificationFailed", outcome: "failed", skipReason: "exactly one XSOverlay process was not verified");
            Write(operationId, "xsOverlayRestartFailed", original, restart, stopMechanism, restartResult: "verificationFailed", outcome: "failed", skipReason: "exactly one XSOverlay process was not verified");
            return (false, null, XsOverlayWindowVerificationResult.Unavailable);
        }
        catch (Exception ex)
        {
            Write(operationId, "xsOverlayRestartFailed", original, restart, stopMechanism, restartResult: "launchFailed", outcome: "failed", skipReason: ex.Message);
            return (false, null, XsOverlayWindowVerificationResult.Unavailable);
        }
    }

    private void Write(
        string operationId,
        string eventType,
        XsOverlayProcessSnapshot? original = null,
        XsOverlayRestartInformation? restart = null,
        string? stopMechanism = null,
        double? stopElapsedMilliseconds = null,
        string? monitorResult = null,
        double? topologySettleElapsedMilliseconds = null,
        string? restartResult = null,
        int? restartedPid = null,
        XsOverlayWindowVerificationResult? windowVerificationResult = null,
        string? outcome = null,
        string? skipReason = null)
        => _diagnostics(new XsOverlayMonitorTransitionEvent
        {
            OperationId = operationId,
            CorrelationId = operationId,
            EventType = eventType,
            OriginalPid = original?.ProcessId,
            RestartedPid = restartedPid,
            SessionId = original?.SessionId,
            ExecutableIdentity = original?.ProcessName,
            SafeExecutablePath = restart is null ? null : SafePath(restart.ExecutablePath),
            RestartMechanism = restart?.RestartMechanism,
            LaunchRoute = restart?.LaunchTarget.RouteType.ToString(),
            ValidatedAppId = restart?.LaunchTarget.ValidatedAppId,
            RegistrationSource = restart?.LaunchTarget.RegistrationSource is { Length: > 0 } source ? SafePath(source) : null,
            WindowVerificationResult = windowVerificationResult?.ToString(),
            StopMechanism = stopMechanism,
            StopElapsedMilliseconds = stopElapsedMilliseconds,
            MonitorResult = monitorResult,
            TopologySettleElapsedMilliseconds = topologySettleElapsedMilliseconds,
            RestartResult = restartResult,
            Outcome = outcome,
            SkipReason = skipReason
        });

    private static string SafePath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        return directory is null
            ? Path.GetFileName(path)
            : Path.Combine("...", Path.GetFileName(directory), Path.GetFileName(path));
    }

    private static XsOverlayMonitorTransitionResult Result(
        string outcome,
        bool monitorAttempted,
        bool monitorSucceeded = false,
        bool xsOverlayWasRunning = false,
        int? originalPid = null)
        => new(
            outcome,
            monitorAttempted,
            monitorSucceeded,
            xsOverlayWasRunning,
            XsOverlayStoppedBySupervisor: false,
            RestartAttempted: false,
            RestartSucceeded: false,
            originalPid,
            RestartedPid: null,
            LaunchRoute: null,
            WindowVerificationResult: XsOverlayWindowVerificationResult.Unavailable);
}

internal sealed class WindowsXsOverlayProcessPlatform : IXsOverlayProcessPlatform
{
    private const string ProcessName = "XSOverlay";
    private readonly int _sessionId;

    public WindowsXsOverlayProcessPlatform()
    {
        using var current = Process.GetCurrentProcess();
        _sessionId = current.SessionId;
    }

    public Task<IReadOnlyList<XsOverlayProcessSnapshot>> FindRunningAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<XsOverlayProcessSnapshot>();
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    if (!IsExactProcessName(process.ProcessName)
                        || process.SessionId != _sessionId
                        || process.HasExited)
                    {
                        continue;
                    }

                    result.Add(new(
                        process.Id,
                        process.SessionId,
                        process.ProcessName,
                        process.StartTime.ToUniversalTime(),
                        TryGetExecutablePath(process)));
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }
        }

        return Task.FromResult<IReadOnlyList<XsOverlayProcessSnapshot>>(result);
    }

    public Task<bool> RequestGracefulCloseAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryOpenSameProcess(process, out var current))
        {
            return Task.FromResult(false);
        }

        using (current)
        {
            return Task.FromResult(current.CloseMainWindow());
        }
    }

    public Task<bool> IsSameProcessRunningAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryOpenSameProcess(process, out var current))
        {
            return Task.FromResult(false);
        }

        current.Dispose();
        return Task.FromResult(true);
    }

    public Task<bool> ForceTerminateAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryOpenSameProcess(process, out var current))
        {
            return Task.FromResult(false);
        }

        using (current)
        {
            current.Kill(entireProcessTree: false);
        }

        return Task.FromResult(true);
    }

    private static bool TryOpenSameProcess(XsOverlayProcessSnapshot expected, out Process process)
    {
        process = null!;
        try
        {
            var candidate = Process.GetProcessById(expected.ProcessId);
            if (candidate.HasExited
                || candidate.SessionId != expected.SessionId
                || !string.Equals(candidate.ProcessName, expected.ProcessName, StringComparison.OrdinalIgnoreCase)
                || candidate.StartTime.ToUniversalTime() != expected.StartTimeUtc)
            {
                candidate.Dispose();
                return false;
            }

            var candidatePath = TryGetExecutablePath(candidate);
            if (!string.IsNullOrWhiteSpace(expected.ExecutablePath)
                && !string.Equals(candidatePath, expected.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                candidate.Dispose();
                return false;
            }

            process = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    internal static bool IsExactProcessName(string processName)
        => string.Equals(processName, ProcessName, StringComparison.OrdinalIgnoreCase);

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
