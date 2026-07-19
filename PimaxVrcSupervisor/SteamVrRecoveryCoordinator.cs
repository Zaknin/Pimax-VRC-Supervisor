internal enum SteamVrRecoveryState
{
    NotOwned,
    WaitingForRuntime,
    Running,
    LossDetected,
    RecoveryPending,
    ReplacementAdopted,
    SessionEnding,
    Completed
}

internal enum SteamVrRecoveryClassification
{
    None,
    SupervisorExit,
    NormalExit,
    RestartEvidence,
    AmbiguousLoss,
    ReplacementRuntime,
    ChainedReplacement,
    RecoveryTimedOut,
    RecoveryLimitReached
}

/// <summary>
/// States the recovery coordinator's monitor policy without claiming that a
/// requested restore succeeded. Program remains the sole owner of the actual
/// monitor operation and records its result separately.
/// </summary>
internal enum SteamVrMonitorDisposition
{
    PreserveCurrentState,
    RestoreIfOwned,
    RestoreAlreadyAttempted,
    NotOwned
}

internal sealed record SteamVrRecoveryTiming(
    TimeSpan FastReplacementWindow,
    TimeSpan AmbiguousRecoveryWindow,
    TimeSpan ReplacementGapWindow,
    TimeSpan StableReplacementThreshold,
    TimeSpan MaximumUnstableRecoveryPeriod,
    int MaximumConsecutiveReplacementAdoptions)
{
    public static SteamVrRecoveryTiming Default { get; } = new(
        FastReplacementWindow: TimeSpan.FromSeconds(3),
        AmbiguousRecoveryWindow: TimeSpan.FromSeconds(20),
        ReplacementGapWindow: TimeSpan.FromSeconds(5),
        StableReplacementThreshold: TimeSpan.FromSeconds(30),
        MaximumUnstableRecoveryPeriod: TimeSpan.FromSeconds(60),
        MaximumConsecutiveReplacementAdoptions: 3);
}

internal sealed record SteamVrRuntimeIdentity(int Pid, DateTimeOffset? StartTime)
{
    public override string ToString() => StartTime is { } startTime
        ? $"{Pid}@{startTime:O}"
        : Pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed record SteamVrRuntimeSnapshot(
    int Pid,
    DateTimeOffset? StartTime,
    string? CommandLine = null)
{
    public SteamVrRuntimeIdentity Identity => new(Pid, StartTime);

    public bool HasRestartStartupReason => CommandLine?.Contains("-startupreason steamvr_restart", StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed record SteamVrLifecycleEvidence(
    bool ShutdownRequested,
    bool RestartRequested,
    string? Marker)
{
    public static SteamVrLifecycleEvidence None { get; } = new(false, false, null);
}

internal sealed record SteamVrRecoveryDecision(
    SteamVrRecoveryState StateBefore,
    SteamVrRecoveryState StateAfter,
    SteamVrRecoveryClassification Classification,
    string Reason,
    SteamVrMonitorDisposition MonitorDisposition,
    bool LossDetected,
    bool DeferBaseStationShutdown,
    bool RunNormalCleanup,
    bool ReplacementAdopted,
    SteamVrRuntimeIdentity? PreviousRuntime,
    SteamVrRuntimeIdentity? CurrentRuntime,
    DateTimeOffset? RecoveryDeadline,
    int ConsecutiveReplacementAdoptions,
    string? EvidenceMarker);

internal static class SteamVrRequestedRestartExitClassifier
{
    public static bool IsExpectedOldRuntimeExit(
        bool restartActive,
        SteamVrRuntimeIdentity? expectedOldRuntime,
        SteamVrRecoveryDecision decision)
        => restartActive
           && expectedOldRuntime is not null
           && decision.LossDetected
           && !decision.ReplacementAdopted
           && decision.Classification != SteamVrRecoveryClassification.SupervisorExit
           && decision.PreviousRuntime == expectedOldRuntime
           && decision.CurrentRuntime is null;
}

/// <summary>
/// Owns the bounded decision-making around a managed SteamVR runtime disappearance.
/// It never launches a process or changes hardware state; Program performs those actions
/// only from the explicit decision returned here.
/// </summary>
internal sealed class SteamVrRecoveryCoordinator
{
    private readonly bool _managedSession;
    private readonly SteamVrRecoveryTiming _timing;
    private SteamVrRecoveryState _state;
    private SteamVrRuntimeIdentity? _currentRuntime;
    private SteamVrRuntimeIdentity? _lostRuntime;
    private DateTimeOffset? _recoveryStartedAt;
    private DateTimeOffset? _recoveryDeadline;
    private DateTimeOffset? _fastClassificationDeadline;
    private DateTimeOffset? _replacementAdoptedAt;
    private int _consecutiveReplacementAdoptions;
    private bool _supervisorExitRequested;
    private bool _ambiguousMonitorRestoreSelected;

    public SteamVrRecoveryCoordinator(bool managedSession, SteamVrRecoveryTiming? timing = null)
    {
        _managedSession = managedSession;
        _timing = timing ?? SteamVrRecoveryTiming.Default;
        _state = managedSession ? SteamVrRecoveryState.WaitingForRuntime : SteamVrRecoveryState.NotOwned;
    }

    public SteamVrRecoveryState State => _state;

    public bool IsRecoveryPending => _state is SteamVrRecoveryState.LossDetected or SteamVrRecoveryState.RecoveryPending;

    public void MarkSupervisorExitRequested()
    {
        _supervisorExitRequested = true;
        if (_managedSession)
        {
            _state = SteamVrRecoveryState.SessionEnding;
        }
    }

    public void MarkCompleted()
    {
        if (_managedSession)
        {
            _state = SteamVrRecoveryState.Completed;
        }
    }

    public void AdoptExplicitReplacement(SteamVrRuntimeIdentity replacementRuntime, DateTimeOffset now)
    {
        if (!_managedSession || _supervisorExitRequested)
        {
            return;
        }

        _lostRuntime = _currentRuntime ?? _lostRuntime;
        _currentRuntime = replacementRuntime;
        _replacementAdoptedAt = now;
        _consecutiveReplacementAdoptions = 0;
        _recoveryStartedAt = null;
        _recoveryDeadline = null;
        _fastClassificationDeadline = null;
        _ambiguousMonitorRestoreSelected = false;
        _state = SteamVrRecoveryState.Running;
    }

    public SteamVrRecoveryDecision Observe(
        IReadOnlyList<SteamVrRuntimeSnapshot> runtimes,
        SteamVrLifecycleEvidence evidence,
        DateTimeOffset now)
    {
        var before = _state;
        if (!_managedSession)
        {
            return NoDecision(before, "SteamVR session is not managed.");
        }

        var runtime = runtimes.OrderBy(candidate => candidate.StartTime ?? DateTimeOffset.MaxValue).ThenBy(candidate => candidate.Pid).FirstOrDefault();
        if (runtime is not null)
        {
            return ObserveRuntime(before, runtime, evidence, now);
        }

        return ObserveLoss(before, evidence, now);
    }

    private SteamVrRecoveryDecision ObserveRuntime(
        SteamVrRecoveryState before,
        SteamVrRuntimeSnapshot runtime,
        SteamVrLifecycleEvidence evidence,
        DateTimeOffset now)
    {
        var identity = runtime.Identity;
        if (_state is SteamVrRecoveryState.LossDetected or SteamVrRecoveryState.RecoveryPending)
        {
            if (identity == _lostRuntime)
            {
                return NoDecision(before, "Original SteamVR identity re-observed.", current: identity);
            }

            _currentRuntime = identity;
            _replacementAdoptedAt = now;
            _consecutiveReplacementAdoptions++;
            _state = SteamVrRecoveryState.ReplacementAdopted;
            var chained = _consecutiveReplacementAdoptions > 1;
            return new SteamVrRecoveryDecision(
                before,
                _state,
                chained ? SteamVrRecoveryClassification.ChainedReplacement : SteamVrRecoveryClassification.ReplacementRuntime,
                chained ? "SteamVR restarted again before stabilizing." : "Replacement SteamVR runtime detected.",
                RecoveryMonitorDisposition(),
                LossDetected: false,
                DeferBaseStationShutdown: true,
                RunNormalCleanup: false,
                ReplacementAdopted: true,
                _lostRuntime,
                identity,
                null,
                _consecutiveReplacementAdoptions,
                runtime.HasRestartStartupReason ? "startupreason-steamvr_restart" : evidence.Marker);
        }

        if (_currentRuntime is null)
        {
            _currentRuntime = identity;
            _state = SteamVrRecoveryState.Running;
            return NoDecision(before, "Managed SteamVR runtime observed.", current: identity);
        }

        _currentRuntime = identity;
        if (_state == SteamVrRecoveryState.ReplacementAdopted
            && _replacementAdoptedAt is { } adoptedAt
            && now - adoptedAt >= _timing.StableReplacementThreshold)
        {
            _consecutiveReplacementAdoptions = 0;
            _recoveryStartedAt = null;
            _recoveryDeadline = null;
            _fastClassificationDeadline = null;
            _ambiguousMonitorRestoreSelected = false;
            _state = SteamVrRecoveryState.Running;
            return new SteamVrRecoveryDecision(
                before,
                _state,
                SteamVrRecoveryClassification.None,
                "Replacement SteamVR runtime is stable.",
                RecoveryMonitorDisposition(),
                false,
                true,
                false,
                false,
                _lostRuntime,
                identity,
                null,
                0,
                null);
        }

        return NoDecision(before, "Managed SteamVR runtime remains active.", current: identity);
    }

    private SteamVrRecoveryDecision ObserveLoss(
        SteamVrRecoveryState before,
        SteamVrLifecycleEvidence evidence,
        DateTimeOffset now)
    {
        if (_state is SteamVrRecoveryState.Completed or SteamVrRecoveryState.SessionEnding)
        {
            return NoDecision(before, "SteamVR loss was observed after Supervisor session ending.");
        }

        if (_state is SteamVrRecoveryState.Running or SteamVrRecoveryState.ReplacementAdopted)
        {
            _lostRuntime = _currentRuntime;
            _currentRuntime = null;
            var isChainedLoss = _state == SteamVrRecoveryState.ReplacementAdopted;
            _state = SteamVrRecoveryState.LossDetected;

            if (_supervisorExitRequested)
            {
                _state = SteamVrRecoveryState.SessionEnding;
                return CleanupDecision(before, SteamVrRecoveryClassification.SupervisorExit, "SteamVR disappeared after explicit Supervisor exit.", now, null);
            }

            if (evidence.ShutdownRequested && !evidence.RestartRequested)
            {
                _state = SteamVrRecoveryState.SessionEnding;
                return CleanupDecision(before, SteamVrRecoveryClassification.NormalExit, "SteamVR normal exit was confirmed by current-session evidence.", now, evidence.Marker);
            }

            var restartEvidence = evidence.RestartRequested;
            var timeout = isChainedLoss || restartEvidence
                ? isChainedLoss ? _timing.ReplacementGapWindow : _timing.FastReplacementWindow
                : _timing.AmbiguousRecoveryWindow;
            _recoveryStartedAt ??= now;
            _recoveryDeadline = now + timeout;
            _fastClassificationDeadline = !isChainedLoss && !restartEvidence
                ? now + _timing.FastReplacementWindow
                : null;
            _state = SteamVrRecoveryState.RecoveryPending;
            return new SteamVrRecoveryDecision(
                before,
                _state,
                restartEvidence ? SteamVrRecoveryClassification.RestartEvidence : SteamVrRecoveryClassification.AmbiguousLoss,
                restartEvidence ? "SteamVR restart evidence was observed." : "SteamVR stopped without a reliable exit classification.",
                RecoveryMonitorDisposition(),
                LossDetected: true,
                DeferBaseStationShutdown: true,
                RunNormalCleanup: false,
                ReplacementAdopted: false,
                _lostRuntime,
                null,
                _recoveryDeadline,
                _consecutiveReplacementAdoptions,
                evidence.Marker);
        }

        if (_state == SteamVrRecoveryState.RecoveryPending)
        {
            if (evidence.ShutdownRequested && !evidence.RestartRequested)
            {
                _state = SteamVrRecoveryState.SessionEnding;
                return CleanupDecision(before, SteamVrRecoveryClassification.NormalExit, "SteamVR normal exit was confirmed by current-session evidence.", now, evidence.Marker);
            }

            if (evidence.RestartRequested && _fastClassificationDeadline is not null)
            {
                _fastClassificationDeadline = null;
                _recoveryDeadline = now + _timing.FastReplacementWindow;
                return new SteamVrRecoveryDecision(
                    before,
                    _state,
                    SteamVrRecoveryClassification.RestartEvidence,
                    "SteamVR restart evidence was observed during ambiguous recovery.",
                    RecoveryMonitorDisposition(),
                    LossDetected: false,
                    DeferBaseStationShutdown: true,
                    RunNormalCleanup: false,
                    ReplacementAdopted: false,
                    _lostRuntime,
                    null,
                    _recoveryDeadline,
                    _consecutiveReplacementAdoptions,
                    evidence.Marker);
            }

            if (!_ambiguousMonitorRestoreSelected
                && _fastClassificationDeadline is { } fastDeadline
                && now >= fastDeadline)
            {
                _ambiguousMonitorRestoreSelected = true;
                _fastClassificationDeadline = null;
                return new SteamVrRecoveryDecision(
                    before,
                    _state,
                    SteamVrRecoveryClassification.AmbiguousLoss,
                    "SteamVR remains ambiguous after the fast replacement window; restoring owned monitors while recovery continues.",
                    SteamVrMonitorDisposition.RestoreIfOwned,
                    LossDetected: false,
                    DeferBaseStationShutdown: true,
                    RunNormalCleanup: false,
                    ReplacementAdopted: false,
                    _lostRuntime,
                    null,
                    _recoveryDeadline,
                    _consecutiveReplacementAdoptions,
                    null);
            }

            if (_recoveryDeadline is { } deadline && now >= deadline)
            {
                var limitReached = _consecutiveReplacementAdoptions >= _timing.MaximumConsecutiveReplacementAdoptions
                    || (_recoveryStartedAt is { } startedAt && now - startedAt >= _timing.MaximumUnstableRecoveryPeriod);
                _state = SteamVrRecoveryState.SessionEnding;
                return CleanupDecision(
                    before,
                    limitReached ? SteamVrRecoveryClassification.RecoveryLimitReached : SteamVrRecoveryClassification.RecoveryTimedOut,
                    limitReached ? "SteamVR recovery remained unstable." : "SteamVR did not return within the recovery window.",
                    now,
                    null);
            }
        }

        return NoDecision(before, "SteamVR recovery is still pending.", previous: _lostRuntime, deadline: _recoveryDeadline);
    }

    private SteamVrRecoveryDecision CleanupDecision(
        SteamVrRecoveryState before,
        SteamVrRecoveryClassification classification,
        string reason,
        DateTimeOffset now,
        string? marker)
        => new(
            before,
            _state,
            classification,
            reason,
            _ambiguousMonitorRestoreSelected
                ? SteamVrMonitorDisposition.RestoreAlreadyAttempted
                : SteamVrMonitorDisposition.RestoreIfOwned,
            LossDetected: true,
            DeferBaseStationShutdown: false,
            RunNormalCleanup: true,
            ReplacementAdopted: false,
            _lostRuntime,
            null,
            null,
            _consecutiveReplacementAdoptions,
            marker);

    private SteamVrRecoveryDecision NoDecision(
        SteamVrRecoveryState before,
        string reason,
        SteamVrRuntimeIdentity? previous = null,
        SteamVrRuntimeIdentity? current = null,
        DateTimeOffset? deadline = null)
        => new(
            before,
            _state,
            SteamVrRecoveryClassification.None,
            reason,
            SteamVrMonitorDisposition.PreserveCurrentState,
            LossDetected: false,
            DeferBaseStationShutdown: IsRecoveryPending || _state is SteamVrRecoveryState.Running or SteamVrRecoveryState.ReplacementAdopted,
            RunNormalCleanup: false,
            ReplacementAdopted: false,
            previous,
            current,
            deadline,
            _consecutiveReplacementAdoptions,
            null);

    private SteamVrMonitorDisposition RecoveryMonitorDisposition()
        => _ambiguousMonitorRestoreSelected
            ? SteamVrMonitorDisposition.RestoreAlreadyAttempted
            : SteamVrMonitorDisposition.PreserveCurrentState;
}
