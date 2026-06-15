using System.Diagnostics;

internal enum SteamVrSessionState
{
    NotOwned,
    WaitingForSession,
    Running,
    ShutdownRequested,
    ExternalTerminationObserved,
    CleanupStarted,
    Completed
}

internal enum SteamVrTerminationClassification
{
    None,
    SupervisorRequested,
    NormalExternalExit,
    AmbiguousExternalExit,
    ConfirmedAbnormalTermination,
    OperatingSystemShutdown
}

internal enum SteamVrCleanupPolicy
{
    None,
    NormalCleanup,
    BestEffortCleanup,
    ManualExitRequired
}

internal enum SteamVrProcessOrigin
{
    ExistingAtManagedSessionStart,
    ExternalAfterManagedSessionStart,
    SupervisorOpenVrProbe
}

internal sealed record SteamVrProcessDiagnostic(
    int Pid,
    string ProcessName,
    DateTimeOffset? ProcessStartTime,
    SteamVrProcessOrigin Origin,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    bool HasExited,
    bool ExitCodeAvailable,
    int? ExitCode,
    string? ExitCodeError);

internal sealed record SteamVrTerminationDecision(
    Guid SessionId,
    int SupervisorPid,
    bool RuntimeSessionOwned,
    SteamVrSessionState StateBefore,
    SteamVrSessionState StateAfter,
    SteamVrTerminationClassification Classification,
    string Reason,
    SteamVrCleanupPolicy CleanupPolicy,
    bool ShowPersistentWarning,
    string? ShutdownIntentSource,
    bool ProbeActive,
    IReadOnlyList<SteamVrProcessDiagnostic> ObservedProcesses,
    string Caller,
    DateTimeOffset ClassificationWindowStartedAt,
    DateTimeOffset ClassificationWindowCompletedAt);

internal sealed class SteamVrLifecycleCoordinator : IDisposable
{
    private readonly bool _runtimeSessionOwned;
    private readonly int _supervisorPid;
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Dictionary<int, ObservedSteamVrProcessState> _processes = new();
    private int _probeDepth;
    private bool _hasObservedNonProbeProcess;
    private SteamVrSessionState _state;
    private string? _shutdownIntentSource;

    public SteamVrLifecycleCoordinator(bool runtimeSessionOwned, int supervisorPid)
    {
        _runtimeSessionOwned = runtimeSessionOwned;
        _supervisorPid = supervisorPid;
        _state = runtimeSessionOwned ? SteamVrSessionState.WaitingForSession : SteamVrSessionState.NotOwned;
    }

    public bool RuntimeSessionOwned => _runtimeSessionOwned;

    public SteamVrSessionState State => _state;

    public bool ProbeActive => _probeDepth > 0;

    public IDisposable BeginOpenVrProbe() => new ProbeScope(this);

    public void MarkSupervisorShutdownRequested(string source)
    {
        _shutdownIntentSource = source;
        if (_runtimeSessionOwned)
        {
            _state = SteamVrSessionState.ShutdownRequested;
        }
    }

    public void MarkCleanupStarted()
    {
        if (_runtimeSessionOwned)
        {
            _state = SteamVrSessionState.CleanupStarted;
        }
    }

    public void MarkCompleted()
    {
        if (_runtimeSessionOwned)
        {
            _state = SteamVrSessionState.Completed;
        }
    }

    public SteamVrTerminationDecision Observe(IReadOnlyList<Process> currentProcesses, string caller)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stateBefore = _state;
        var probeActive = ProbeActive;

        if (currentProcesses.Count > 0)
        {
            foreach (var process in currentProcesses)
            {
                ObserveRunningProcess(process, probeActive, startedAt);
            }

            if (_runtimeSessionOwned && _hasObservedNonProbeProcess && _state != SteamVrSessionState.ShutdownRequested)
            {
                _state = SteamVrSessionState.Running;
            }

            return NoDecision(stateBefore, caller, startedAt, probeActive);
        }

        if (!_runtimeSessionOwned)
        {
            return NoDecision(stateBefore, caller, startedAt, probeActive);
        }

        if (!_hasObservedNonProbeProcess)
        {
            return NoDecision(stateBefore, caller, startedAt, probeActive);
        }

        CaptureExitInformation();
        if (_state is SteamVrSessionState.ExternalTerminationObserved or SteamVrSessionState.CleanupStarted or SteamVrSessionState.Completed)
        {
            return NoDecision(stateBefore, caller, startedAt, probeActive);
        }

        if (_state == SteamVrSessionState.ShutdownRequested)
        {
            _state = SteamVrSessionState.ExternalTerminationObserved;
            return new SteamVrTerminationDecision(
                _sessionId,
                _supervisorPid,
                _runtimeSessionOwned,
                stateBefore,
                _state,
                SteamVrTerminationClassification.SupervisorRequested,
                "SteamVR disappeared after an explicit Supervisor shutdown request.",
                SteamVrCleanupPolicy.NormalCleanup,
                ShowPersistentWarning: false,
                _shutdownIntentSource,
                probeActive,
                SnapshotProcesses(),
                caller,
                startedAt,
                DateTimeOffset.UtcNow);
        }

        _state = SteamVrSessionState.ExternalTerminationObserved;
        return new SteamVrTerminationDecision(
            _sessionId,
            _supervisorPid,
            _runtimeSessionOwned,
            stateBefore,
            _state,
            SteamVrTerminationClassification.AmbiguousExternalExit,
            "SteamVR server process disappeared without reliable abnormal termination evidence.",
            SteamVrCleanupPolicy.NormalCleanup,
            ShowPersistentWarning: false,
            _shutdownIntentSource,
            probeActive,
            SnapshotProcesses(),
            caller,
            startedAt,
            DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<SteamVrProcessDiagnostic> SnapshotProcesses()
    {
        CaptureExitInformation();
        return _processes.Values
            .OrderBy(process => process.FirstObservedAt)
            .Select(process => process.ToDiagnostic())
            .ToArray();
    }

    public void Dispose()
    {
        foreach (var process in _processes.Values)
        {
            process.Dispose();
        }

        _processes.Clear();
    }

    private SteamVrTerminationDecision NoDecision(
        SteamVrSessionState stateBefore,
        string caller,
        DateTimeOffset startedAt,
        bool probeActive)
        => new(
            _sessionId,
            _supervisorPid,
            _runtimeSessionOwned,
            stateBefore,
            _state,
            SteamVrTerminationClassification.None,
            "No decisive SteamVR lifecycle transition.",
            SteamVrCleanupPolicy.None,
            ShowPersistentWarning: false,
            _shutdownIntentSource,
            probeActive,
            SnapshotProcesses(),
            caller,
            startedAt,
            DateTimeOffset.UtcNow);

    private void ObserveRunningProcess(Process process, bool probeActive, DateTimeOffset observedAt)
    {
        if (_processes.TryGetValue(process.Id, out var existing))
        {
            existing.LastObservedAt = observedAt;
            process.Dispose();
            return;
        }

        var origin = probeActive
            ? SteamVrProcessOrigin.SupervisorOpenVrProbe
            : _hasObservedNonProbeProcess
                ? SteamVrProcessOrigin.ExternalAfterManagedSessionStart
                : SteamVrProcessOrigin.ExistingAtManagedSessionStart;
        var observed = new ObservedSteamVrProcessState(process, origin, observedAt);
        _processes[process.Id] = observed;
        if (origin != SteamVrProcessOrigin.SupervisorOpenVrProbe)
        {
            _hasObservedNonProbeProcess = true;
        }
    }

    private void CaptureExitInformation()
    {
        foreach (var process in _processes.Values)
        {
            process.CaptureExitInformation();
        }
    }

    private void EnterProbe()
    {
        _probeDepth++;
    }

    private void ExitProbe()
    {
        if (_probeDepth > 0)
        {
            _probeDepth--;
        }
    }

    private sealed class ProbeScope : IDisposable
    {
        private SteamVrLifecycleCoordinator? _owner;

        public ProbeScope(SteamVrLifecycleCoordinator owner)
        {
            _owner = owner;
            owner.EnterProbe();
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null)
            {
                return;
            }

            _owner = null;
            owner.ExitProbe();
        }
    }

    private sealed class ObservedSteamVrProcessState : IDisposable
    {
        private readonly Process _process;
        private bool _exitCaptured;

        public ObservedSteamVrProcessState(Process process, SteamVrProcessOrigin origin, DateTimeOffset observedAt)
        {
            _process = process;
            Origin = origin;
            FirstObservedAt = observedAt;
            LastObservedAt = observedAt;
            ProcessName = SafeGetProcessName(process);
            ProcessStartTime = SafeGetStartTime(process);
        }

        public string ProcessName { get; }

        public DateTimeOffset? ProcessStartTime { get; }

        public SteamVrProcessOrigin Origin { get; }

        public DateTimeOffset FirstObservedAt { get; }

        public DateTimeOffset LastObservedAt { get; set; }

        public bool HasExited { get; private set; }

        public bool ExitCodeAvailable { get; private set; }

        public int? ExitCode { get; private set; }

        public string? ExitCodeError { get; private set; }

        public void CaptureExitInformation()
        {
            if (_exitCaptured)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Refresh();
                    if (!_process.HasExited)
                    {
                        return;
                    }
                }

                _process.WaitForExit(milliseconds: 250);
                HasExited = true;
                ExitCode = _process.ExitCode;
                ExitCodeAvailable = true;
                _exitCaptured = true;
            }
            catch (Exception ex)
            {
                HasExited = true;
                ExitCodeAvailable = false;
                ExitCodeError = ex.GetType().Name + ": " + ex.Message;
                _exitCaptured = true;
            }
        }

        public SteamVrProcessDiagnostic ToDiagnostic()
            => new(
                _process.Id,
                ProcessName,
                ProcessStartTime,
                Origin,
                FirstObservedAt,
                LastObservedAt,
                HasExited,
                ExitCodeAvailable,
                ExitCode,
                ExitCodeError);

        public void Dispose() => _process.Dispose();

        private static string SafeGetProcessName(Process process)
        {
            try
            {
                return process.ProcessName;
            }
            catch
            {
                return "unknown";
            }
        }

        private static DateTimeOffset? SafeGetStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return null;
            }
        }
    }
}
