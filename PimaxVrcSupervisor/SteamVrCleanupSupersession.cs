using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Declares whether cleanup is committed (graceful/emergency/exception shutdown)
/// or owned by continued SteamVR absence. Only runtime-absence cleanup can be
/// superseded by a replacement SteamVR runtime appearing mid-cleanup.
/// </summary>
internal enum CleanupOwnershipMode
{
    /// <summary>
    /// Committed global cleanup (graceful shutdown, emergency close, exception cleanup).
    /// Replacement-runtime appearance does NOT supersede this mode.
    /// </summary>
    CommittedGlobal,

    /// <summary>
    /// Runtime-absence-owned cleanup (normal lifecycle cleanup triggered by SteamVR loss).
    /// Validity condition: no SteamVR runtime currently exists.
    /// Any observed SteamVR runtime supersedes remaining cleanup work.
    /// </summary>
    RuntimeAbsence
}

/// <summary>
/// The outcome of a cleanup operation.
/// </summary>
internal enum CleanupOutcomeKind
{
    /// <summary>
    /// The applicable cleanup sequence completed normally.
    /// </summary>
    Completed,

    /// <summary>
    /// Cleanup was superseded because a replacement SteamVR runtime was observed.
    /// </summary>
    SupersededByRuntime
}

/// <summary>
/// Result returned by a cleanup operation.
/// </summary>
internal sealed record CleanupOutcome
{
    public static CleanupOutcome Completed()
        => new(CleanupOutcomeKind.Completed, null);

    public static CleanupOutcome Superseded(SteamVrRuntimeSnapshot? runtime)
        => new(CleanupOutcomeKind.SupersededByRuntime, runtime);

    public CleanupOutcomeKind Kind { get; init; }
    public SteamVrRuntimeSnapshot? SupersedingRuntime { get; init; }

    private CleanupOutcome(CleanupOutcomeKind kind, SteamVrRuntimeSnapshot? supersedingRuntime)
    {
        Kind = kind;
        SupersedingRuntime = supersedingRuntime;
    }
}

/// <summary>
/// Human-readable labels for the cleanup phases used in journaling.
/// </summary>
internal static class CleanupPhaseLabel
{
    public const string InitialProbe = "initialProbe";
    public const string MonitorRestore = "monitorRestore";
    public const string BaseStationPowerDown = "baseStationPowerDown";
    public const string LovenseAppStop = "lovenseAppStop";
    public const string ManagedAppStop = "managedAppStop";
}

/// <summary>
/// Injectable delegates for the normal-cleanup supersession workflow.
/// </summary>
internal sealed class SteamVrCleanupSupersessionContext
{
    /// <summary>Capture the current SteamVR runtime (null if absent).</summary>
    public Func<SteamVrRuntimeSnapshot?> CaptureRuntime { get; init; } = null!;

    /// <summary>Restore the Supervisor-owned monitor layout (synchronous).</summary>
    public Action RestoreMonitors { get; init; } = null!;

    /// <summary>Power down base stations for the session.</summary>
    public Func<CancellationToken, Task> PowerDownBaseStations { get; init; } = null!;

    /// <summary>Stop Lovense-related applications.</summary>
    public Func<CancellationToken, Task> StopLovenseApps { get; init; } = null!;

    /// <summary>Stop managed applications (face tracking, auto-launch, etc).</summary>
    public Func<CancellationToken, Task> StopManagedApps { get; init; } = null!;

    /// <summary>
    /// Delay used between replacement-runtime probes (injectable for tests).
    /// Contract: the delegate receives the monitor's shutdown CancellationToken.
    /// A cooperative delay exits promptly when the token is cancelled.
    /// The outer WaitAsync wrapper handles abandoned-fault observation,
    /// so a token-ignoring delay cannot hang finalization.
    /// </summary>
    public Func<CancellationToken, Task> SupersessionPollDelay { get; init; } = null!;

    /// <summary>Write a lifecycle journal event.</summary>
    public Action<string, string?, string?, IReadOnlyDictionary<string, string?>?> WriteLifecycleEvent { get; init; } = null!;
}

// =============================================================================
// CleanupOperationCoordinator
// =============================================================================

/// <summary>
/// Coordinates shared cleanup operations so that exactly one owner runs while
/// concurrent callers join as waiters on the same Task&lt;CleanupOutcome&gt;.
///
/// Invariants:
///  1. Under one short state lock: if active, capture its Task; otherwise publish TCS.
///  2. State lock is released before running or awaiting work.
///  3. Joined callers await sharedTask.WaitAsync(theirOwnCancellationToken).
///  4. Owner operation is wrapped from before its first await through all exits.
///  5. Owner success settles TCS with exact CleanupOutcome.
///  6. Owner cancellation settles coherently with OperationCanceledException(callerToken).
///  7. Owner fault settles with exact exception.
///  8. Active-task field cleared only for that exact operation (ReferenceEquals guard).
///  9. TCS completion occurs outside all locks.
/// 10. A waiter that already captured the Task still receives its exact result
///     after the active field is cleared.
/// 11. No busy polling.
/// 12. No Task.Result under a lock.
/// 13. Exactly one owner operation runs.
/// </summary>
internal sealed class CleanupOperationCoordinator
{
    private readonly object _stateLock = new();
    private TaskCompletionSource<CleanupOutcome>? _activeTcs;

    /// <summary>
    /// Run or join a shared cleanup operation.
    /// </summary>
    /// <param name="ownerOperation">
    /// The destructive cleanup work, executed by exactly one caller.
    /// This delegate runs outside all locks and must be safe to cancel.
    /// </param>
    /// <param name="callerToken">This caller's cancellation token.</param>
    /// <param name="onJoinedWaiter">
    /// Optional callback invoked when this caller joins an in-flight operation
    /// rather than becoming the owner. Runs outside the state lock.
    /// </param>
    public async Task<CleanupOutcome> RunAsync(
        Func<CancellationToken, Task<CleanupOutcome>> ownerOperation,
        CancellationToken callerToken,
        Action? onJoinedWaiter = null)
    {
        Task<CleanupOutcome>? waiterTask = null;
        TaskCompletionSource<CleanupOutcome>? ownerTcs = null;

        // 1. Short state lock: admission decision + TCS publication.
        lock (_stateLock)
        {
            if (_activeTcs is not null)
            {
                // Join the existing in-flight operation.
                waiterTask = _activeTcs.Task;
            }
            else
            {
                // Become the owner.
                ownerTcs = new TaskCompletionSource<CleanupOutcome>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _activeTcs = ownerTcs;
            }
        }

        // 2. State lock already released.

        // Waiter path: join the owner's in-flight operation.
        if (waiterTask is not null)
        {
            onJoinedWaiter?.Invoke();
            // 3. Waiter awaits with their own cancellation token.
            return await waiterTask.WaitAsync(callerToken);
        }

        // Owner path: run the operation, ensuring TCS is always settled.
        Debug.Assert(ownerTcs is not null);

        try
        {
            // 4. Owner operation runs outside all locks.
            var outcome = await ownerOperation(callerToken);

            // 5. Owner success: settle TCS with exact CleanupOutcome (outside lock).
            ownerTcs.TrySetResult(outcome);
            return outcome;
        }
        catch (OperationCanceledException)
        {
            // 6. Owner cancellation: settle coherently with callerToken.
            ownerTcs.TrySetException(new OperationCanceledException(callerToken));
            throw;
        }
        catch (Exception ex)
        {
            // 7. Owner fault: settle with exact exception.
            ownerTcs.TrySetException(ex);
            throw;
        }
        finally
        {
            // 8. Clear active task for this exact operation only (outside lock for TCS,
            //    but we need the lock to clear the field safely).
            lock (_stateLock)
            {
                if (ReferenceEquals(_activeTcs, ownerTcs))
                {
                    _activeTcs = null;
                }
            }
        }
    }
}

// =============================================================================
// CleanupLifecycleOutcomeHandler
// =============================================================================

/// <summary>
/// Production-wired handler for cleanup outcomes used by
/// ApplySteamVrLifecycleDecisionAsync. Accepts injected delegates so tests
/// can verify exact behavior without duplicating Program.cs logic.
///
/// Returns true to exit the main loop (cleanup completed), false to continue
/// (supersession adopted — session continues).
/// </summary>
internal sealed class CleanupLifecycleOutcomeHandler
{
    private readonly Action _markCompleted;
    private readonly Action _establishBaseline;
    private readonly Action<SteamVrRuntimeIdentity, DateTimeOffset> _adoptExplicitReplacement;
    private readonly Action<SupervisorLifecyclePhase> _setLifecyclePhase;
    private readonly Action<string, string?, string?, IReadOnlyDictionary<string, string?>?> _writeLifecycleEvent;

    public CleanupLifecycleOutcomeHandler(
        Action markCompleted,
        Action establishBaseline,
        Action<SteamVrRuntimeIdentity, DateTimeOffset> adoptExplicitReplacement,
        Action<SupervisorLifecyclePhase> setLifecyclePhase,
        Action<string, string?, string?, IReadOnlyDictionary<string, string?>?> writeLifecycleEvent)
    {
        _markCompleted = markCompleted;
        _establishBaseline = establishBaseline;
        _adoptExplicitReplacement = adoptExplicitReplacement;
        _setLifecyclePhase = setLifecyclePhase;
        _writeLifecycleEvent = writeLifecycleEvent;
    }

    /// <summary>
    /// Handle the result of a cleanup operation and return the main-loop decision.
    /// </summary>
    /// <returns>
    /// true  = exit main loop (cleanup completed normally).
    /// false = continue main loop (supersession adopted or fallback).
    /// </returns>
    public bool Handle(CleanupOutcome outcome)
    {
        if (outcome.Kind == CleanupOutcomeKind.Completed)
        {
            _markCompleted();
            return true;
        }

        if (outcome.Kind == CleanupOutcomeKind.SupersededByRuntime
            && outcome.SupersedingRuntime is { } supersedingRuntime)
        {
            _writeLifecycleEvent(
                "cleanup.supersession",
                "adoption",
                "replacement-adopted",
                new Dictionary<string, string?>
                {
                    ["supersedingRuntime"] = supersedingRuntime.Identity.ToString(),
                });

            _establishBaseline();
            _adoptExplicitReplacement(supersedingRuntime.Identity, DateTimeOffset.UtcNow);
            _setLifecyclePhase(SupervisorLifecyclePhase.VrChatRunning);
            return false;
        }

        // Fallback: should not occur with valid outcomes, but be safe.
        return false;
    }
}

// =============================================================================
// CleanupSupersessionArbiter
// =============================================================================

/// <summary>
/// Terminal states for the single-winner arbiter.
/// </summary>
internal enum SupersessionArbiterState
{
    /// <summary>No event has committed yet.</summary>
    Pending,
    /// <summary>Caller cancellation committed first — runtime cannot win.</summary>
    CallerCancelled,
    /// <summary>Runtime B committed first — cancellation cannot replace it.</summary>
    RuntimeSuperseded
}

/// <summary>
/// The committed result of the arbiter.
/// </summary>
internal sealed class SupersessionArbiterResult
{
    public SupersessionArbiterState State { get; init; }
    public SteamVrRuntimeSnapshot? WinningRuntime { get; init; }

    public SupersessionArbiterResult(SupersessionArbiterState state, SteamVrRuntimeSnapshot? winningRuntime)
    {
        State = state;
        WinningRuntime = winningRuntime;
    }
}

/// <summary>
/// Single-winner arbiter for caller cancellation vs runtime supersession.
///
/// FIRST COMMITTED EVENT WINS. Both cancellation and runtime observation
/// attempt state transitions under the same lock. Exactly one terminal winner.
///
/// Invariants:
///  1. Register caller cancellation before the first CaptureRuntime call.
///  2. If the caller token is already cancelled, cancellation commits synchronously
///     before any runtime capture.
///  3. Caller cancellation and runtime B use the exact same state transition mechanism.
///  4. If cancellation commits first, later B cannot win.
///  5. If B commits first, later cancellation cannot replace it.
///  6. Null never commits.
///  7. Runtime C cannot replace B.
///  8. Exactly one terminal winner is committed.
///  9. Cancellation callback and registrations are disposed safely.
/// </summary>
internal sealed class CleanupSupersessionArbiter : IDisposable
{
    private readonly object _lock = new();
    private SupersessionArbiterState _state = SupersessionArbiterState.Pending;
    private SteamVrRuntimeSnapshot? _winningRuntime;

    // Cancellation registration for caller-token callback.
    private CancellationTokenRegistration? _callerRegistration;

    // Signal token to stop remaining cleanup and monitor work when arbiter commits.
    private readonly CancellationTokenSource _signalCts = new();

    /// <summary>
    /// Token that is cancelled when the arbiter commits any terminal state.
    /// Use this to stop cleanup phases and the monitor.
    /// </summary>
    public CancellationToken SignalToken => _signalCts.Token;

    /// <summary>
    /// Register a callback that fires when the caller's cancellation token is cancelled.
    /// Must be called before any TryCommitRuntime() invocation.
    /// If the token is already cancelled, commits CallerCancelled synchronously.
    /// </summary>
    public void RegisterCallerCancellation(CancellationToken callerToken)
    {
        // If already cancelled, commit synchronously before returning.
        if (callerToken.IsCancellationRequested)
        {
            TryCommitCallerCancellation();
            return;
        }

        // Register callback — when caller cancels, it attempts to commit under the same lock.
        _callerRegistration = callerToken.Register(() => TryCommitCallerCancellation(), useSynchronizationContext: false);
    }

    /// <summary>
    /// Attempt to commit caller cancellation as the terminal winner.
    /// Returns true if cancellation was committed; false if runtime already won.
    /// </summary>
    public bool TryCommitCallerCancellation()
    {
        lock (_lock)
        {
            if (_state != SupersessionArbiterState.Pending)
            {
                // Already committed — runtime B won first.
                return false;
            }

            _state = SupersessionArbiterState.CallerCancelled;
        }

        // Signal outside the lock.
        _signalCts.Cancel();
        return true;
    }

    /// <summary>
    /// Attempt to commit a runtime snapshot as the terminal winner.
    /// Returns true if runtime was committed; false if cancellation already won
    /// or another runtime already committed.
    /// Null snapshots never commit.
    /// </summary>
    public bool TryCommitRuntime(SteamVrRuntimeSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return false;
        }

        lock (_lock)
        {
            if (_state != SupersessionArbiterState.Pending)
            {
                // Already committed — cancellation or earlier runtime won.
                return false;
            }

            // Store the winning snapshot BEFORE publishing the winner.
            _winningRuntime = snapshot;
            _state = SupersessionArbiterState.RuntimeSuperseded;
        }

        // Signal outside the lock.
        _signalCts.Cancel();
        return true;
    }

    /// <summary>
    /// Get the committed terminal result.
    /// </summary>
    public SupersessionArbiterResult GetResult()
    {
        lock (_lock)
        {
            return new SupersessionArbiterResult(_state, _winningRuntime);
        }
    }

    /// <summary>
    /// Current arbiter state (thread-safe snapshot).
    /// </summary>
    public SupersessionArbiterState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Dispose cancellation registration and signal source.
    /// </summary>
    public void Dispose()
    {
        _callerRegistration?.Dispose();
        _signalCts.Dispose();
    }
}

// =============================================================================
// Awaits an injected task with abandoned-fault observation.
// =============================================================================

/// <summary>
/// Static helper that awaits an injected task through WaitAsync(cancellationToken).
/// If the underlying task is abandoned (cancellation fires before the task completes),
/// attaches an OnlyOnFaulted continuation so eventual faults are observed and do not
/// become UnobservedTaskException. Normal completion and expected cancellation remain quiet.
/// </summary>
internal static class InjectedTaskAwaiter
{
    public static Task AwaitWithAbandonedObservationAsync(
        Task underlyingTask,
        CancellationToken cancellationToken)
    {
        return AwaitWithAbandonedObservationAsync(
            underlyingTask,
            cancellationToken,
            observedException: null);
    }

    /// <summary>
    /// Production-facing awaiter. Delegates to the core method but intentionally
    /// discards the returned observation continuation Task — production never waits
    /// for an abandoned underlying task.
    /// </summary>
    internal static async Task AwaitWithAbandonedObservationAsync(
        Task underlyingTask,
        CancellationToken cancellationToken,
        Action<Exception>? observedException)
    {
        // Await the core far enough to obtain its result, then discard the
        // observation continuation Task. Production does not wait for abandoned tasks.
        await AwaitWithAbandonedObservationCoreAsync(
            underlyingTask,
            cancellationToken,
            observedException);
    }

    /// <summary>
    /// Internal core method that exposes the observation continuation Task
    /// for deterministic test verification.
    ///
    /// Returns:
    ///   null  — the underlying task completed normally (no abandonment occurred).
    ///   Task? — cancellation abandoned an incomplete underlying task;
    ///           this is the exact OnlyOnFaulted observation continuation attached to it.
    /// </summary>
    internal static async Task<Task?> AwaitWithAbandonedObservationCoreAsync(
        Task underlyingTask,
        CancellationToken cancellationToken,
        Action<Exception>? observedException)
    {
        if (underlyingTask.IsCompleted)
        {
            // Already done — just await normally (propagates faults).
            await underlyingTask;
            return null;
        }

        // Create a cancellation task using a linked TaskCompletionSource.
        var cancellationTcs = new TaskCompletionSource<bool>();
        using var reg = cancellationToken.Register(() => cancellationTcs.TrySetResult(true));
        try
        {
            var completed = await Task.WhenAny(underlyingTask, cancellationTcs.Task);

            if (completed == underlyingTask)
            {
                // Underlying task finished first — await it normally (propagates any fault).
                await underlyingTask;
                return null;
            }
            else
            {
                // Cancellation won — the underlying task is still incomplete.
                // Attach a continuation to observe any eventual fault so it doesn't
                // become an UnobservedTaskException later.
                Task? observationContinuation = underlyingTask.ContinueWith(
                    t =>
                    {
                        // 1. Always read task.Exception first (forces observation).
                        var aggregate = t.Exception;
                        if (aggregate is null)
                        {
                            return;
                        }

                        // 2. Invoke the optional observer inside try/catch so the
                        //    continuation itself cannot fault if the observer throws.
                        try
                        {
                            observedException?.Invoke(aggregate);
                        }
                        catch
                        {
                            // Observer fault must not propagate — the continuation
                            // is responsible only for observing, not for the observer's
                            // correctness.
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);

                return observationContinuation;
            }
        }
        finally
        {
            reg.Dispose();
        }
    }
}

// =============================================================================
// SteamVrCleanupSupersession
// =============================================================================

/// <summary>
/// Orchestrates runtime-absence-owned normal cleanup with replacement-runtime supersession.
/// This component is testable via injected delegates and does not touch real processes,
/// monitors, Bluetooth devices, or SteamVR.
/// </summary>
internal sealed class SteamVrCleanupSupersession : IDisposable
{
    private readonly SteamVrCleanupSupersessionContext _context;
    private readonly CleanupSupersessionArbiter _arbiter;

    public SteamVrCleanupSupersession(
        SteamVrCleanupSupersessionContext context,
        CancellationToken callerToken)
    {
        _context = context;

        // Create the single-winner arbiter.
        _arbiter = new CleanupSupersessionArbiter();

        // Register caller cancellation BEFORE any CaptureRuntime call.
        _arbiter.RegisterCallerCancellation(callerToken);
    }

    public void Dispose()
    {
        _arbiter.Dispose();
    }

    /// <summary>
    /// Run the supersedable normal-cleanup sequence.
    /// Returns <see cref="CleanupOutcomeKind.Completed"/> if cleanup finished,
    /// or <see cref="CleanupOutcomeKind.SupersededByRuntime"/> if a replacement
    /// SteamVR runtime was observed during cleanup.
    /// </summary>
    public async Task<CleanupOutcome> RunAsync()
    {
        // Arbiter already registered caller cancellation before any capture.
        // If pre-cancelled, the callback committed synchronously during construction.
        if (_arbiter.State == SupersessionArbiterState.CallerCancelled)
        {
            throw new OperationCanceledException();
        }

        // Dedicated monitor shutdown token — cancelled in the finally block
        // so the monitor always stops, even on normal (non-supersession) completion.
        using var monitorShutdownCts = new CancellationTokenSource();
        var monitorShutdownToken = monitorShutdownCts.Token;

        // Track the phase at which supersession was detected.
        string? supersessionPhase = null;
        bool irreversiblePhaseBegan = false;

        // --- Phase 0: Initial probe ---
        _context.WriteLifecycleEvent(
            "cleanup.supersession",
            "initialProbe",
            "started",
            new Dictionary<string, string?>
            {
                ["phase"] = CleanupPhaseLabel.InitialProbe,
            });

        var initialRuntime = _context.CaptureRuntime();
        if (initialRuntime is not null)
        {
            // Attempt atomic commit through the arbiter.
            if (_arbiter.TryCommitRuntime(initialRuntime))
            {
                _context.WriteLifecycleEvent(
                    "cleanup.supersession",
                    "superseded",
                    "runtime-present-at-initial-probe",
                    new Dictionary<string, string?>
                    {
                        ["phase"] = CleanupPhaseLabel.InitialProbe,
                        ["supersedingRuntime"] = initialRuntime.Identity.ToString(),
                    });
                return CleanupOutcome.Superseded(initialRuntime);
            }

            // Arbiter already committed (caller cancellation). Throw immediately.
            throw new OperationCanceledException();
        }

        // Start the short-lived supersession monitor.
        var monitorTask = StartSupersessionMonitorAsync(monitorShutdownCts.Token);

        try
        {
            // --- Phase 1: Monitor restore ---
            _arbiter.SignalToken.ThrowIfCancellationRequested();
            CheckSupersessionAndThrow(ref supersessionPhase, CleanupPhaseLabel.MonitorRestore);

            _context.WriteLifecycleEvent(
                "cleanup.supersession",
                "phaseStarted",
                "monitorRestore",
                new Dictionary<string, string?>
                {
                    ["phase"] = CleanupPhaseLabel.MonitorRestore,
                });

            _context.RestoreMonitors();
            irreversiblePhaseBegan = true;

            // Probe after monitor restore.
            CheckSupersessionAndThrow(ref supersessionPhase, CleanupPhaseLabel.MonitorRestore);

            // --- Phase 2: Base-station power-down ---
            _arbiter.SignalToken.ThrowIfCancellationRequested();
            CheckSupersessionAndThrow(ref supersessionPhase, CleanupPhaseLabel.BaseStationPowerDown);

            _context.WriteLifecycleEvent(
                "cleanup.supersession",
                "phaseStarted",
                "baseStationPowerDown",
                new Dictionary<string, string?>
                {
                    ["phase"] = CleanupPhaseLabel.BaseStationPowerDown,
                });

            try
            {
                await _context.PowerDownBaseStations(_arbiter.SignalToken);
            }
            catch (OperationCanceledException) when (_arbiter.SignalToken.IsCancellationRequested)
            {
                throw;
            }

            // Probe after base-station power-down.
            CheckSupersessionAndThrow(ref supersessionPhase, CleanupPhaseLabel.BaseStationPowerDown);

            // --- Phase 3: Lovense app stop ---
            _arbiter.SignalToken.ThrowIfCancellationRequested();
            CheckSupersessionAndThrow(ref supersessionPhase, CleanupPhaseLabel.LovenseAppStop);

            _context.WriteLifecycleEvent(
                "cleanup.supersession",
                "phaseStarted",
                "lovenseAppStop",
                new Dictionary<string, string?>
                {
                    ["phase"] = CleanupPhaseLabel.LovenseAppStop,
                });

            try
            {
                await _context.StopLovenseApps(_arbiter.SignalToken);
            }
            catch (OperationCanceledException) when (_arbiter.SignalToken.IsCancellationRequested)
            {
                throw;
            }

            // Probe after Lovense stop.
            CheckSupersessionAndThrow(ref supersessionPhase, CleanupPhaseLabel.LovenseAppStop);

            // --- Phase 4: Managed-app stop ---
            _arbiter.SignalToken.ThrowIfCancellationRequested();
            CheckSupersessionAndThrow(ref supersessionPhase, CleanupPhaseLabel.ManagedAppStop);

            _context.WriteLifecycleEvent(
                "cleanup.supersession",
                "phaseStarted",
                "managedAppStop",
                new Dictionary<string, string?>
                {
                    ["phase"] = CleanupPhaseLabel.ManagedAppStop,
                });

            try
            {
                await _context.StopManagedApps(_arbiter.SignalToken);
            }
            catch (OperationCanceledException) when (_arbiter.SignalToken.IsCancellationRequested)
            {
                throw;
            }

            // Final probe after all cleanup phases.
            CheckSupersessionAndThrow(ref supersessionPhase, CleanupPhaseLabel.ManagedAppStop);

            // All phases completed successfully.
            _context.WriteLifecycleEvent(
                "cleanup.supersession",
                "completed",
                "all-phases-done",
                new Dictionary<string, string?>
                {
                    ["irreversiblePhaseBegan"] = irreversiblePhaseBegan.ToString(),
                });

            return CleanupOutcome.Completed();
        }
        catch (OperationCanceledException)
        {
            // FIRST COMMITTED EVENT WINS:
            // Check arbiter state to determine if runtime or cancellation won.
            var result = _arbiter.GetResult();
            if (result.State == SupersessionArbiterState.RuntimeSuperseded
                && result.WinningRuntime is { } winningRuntime)
            {
                _context.WriteLifecycleEvent(
                    "cleanup.supersession",
                    "superseded",
                    "replacement-runtime-detected",
                    new Dictionary<string, string?>
                    {
                        ["phase"] = supersessionPhase,
                        ["supersedingRuntime"] = winningRuntime.Identity.ToString(),
                        ["irreversiblePhaseBegan"] = irreversiblePhaseBegan.ToString(),
                    });
                return CleanupOutcome.Superseded(winningRuntime);
            }

            // Caller cancellation committed first — re-throw.
            throw;
        }
        finally
        {
            // Ensure the monitor is stopped and awaited.
            // Cancel the dedicated monitor shutdown token so the monitor exits
            // even on normal (non-supersession) completion.
            monitorShutdownCts.Cancel();
            try
            {
                await monitorTask;
            }
            catch (OperationCanceledException)
            {
                // Expected: monitor cancelled (supersession or normal completion).
            }
            catch (Exception ex)
            {
                // Monitor fault: unexpected exceptions are NOT silently swallowed.
                _context.WriteLifecycleEvent(
                    "cleanup.supersession",
                    "monitorFault",
                    "unexpected-exception",
                    new Dictionary<string, string?>
                    {
                        ["exceptionType"] = ex.GetType().FullName,
                        ["exceptionMessage"] = ex.Message,
                    });
                throw;
            }
        }
    }

    /// <summary>
    /// A short-lived polling loop that probes for a replacement SteamVR runtime
    /// during cleanup. Uses the arbiter's signal token for supersession detection
    /// and a separate shutdown token for monitor lifecycle management.
    /// </summary>
    private async Task StartSupersessionMonitorAsync(CancellationToken shutdownToken)
    {
        var monitorSignal = _arbiter.SignalToken;
        try
        {
            while (!shutdownToken.IsCancellationRequested && !monitorSignal.IsCancellationRequested)
            {
                // Await the injected delay with abandoned-fault observation.
                // If the delay delegate ignores cancellation, finalization still completes.
                var delayTask = _context.SupersessionPollDelay(shutdownToken);
                await InjectedTaskAwaiter.AwaitWithAbandonedObservationAsync(delayTask, shutdownToken);

                if (shutdownToken.IsCancellationRequested || monitorSignal.IsCancellationRequested)
                {
                    break;
                }

                var runtime = _context.CaptureRuntime();
                if (runtime is not null)
                {
                    // Attempt atomic commit through the arbiter.
                    if (_arbiter.TryCommitRuntime(runtime))
                    {
                        return;
                    }
                    // Arbiter already committed; monitor exits on next loop check.
                }
            }
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested || monitorSignal.IsCancellationRequested)
        {
            // Expected monitor shutdown.
        }
        catch (OperationCanceledException ex)
        {
            // OCE thrown by the delay or capture delegate while neither token is cancelled
            // — wrap in InvalidOperationException so the finally block can distinguish it
            // from expected cancellation OCE.
            throw new InvalidOperationException(
                "Monitor delay or capture delegate threw OperationCanceledException while monitor tokens were not cancelled.", ex);
        }
    }

    /// <summary>
    /// Probe the arbiter signal and directly check for a replacement runtime.
    /// Records the current phase for journaling.
    ///
    /// If a runtime is observed and supersession can be committed, throws OCE.
    /// If a runtime is observed but caller cancellation already committed,
    /// does NOT throw — the linked token check in the next phase will propagate it.
    /// </summary>
    private void CheckSupersessionAndThrow(
        ref string? supersessionPhase,
        string phaseLabel)
    {
        supersessionPhase = phaseLabel;
        if (_arbiter.SignalToken.IsCancellationRequested)
        {
            throw new OperationCanceledException();
        }

        // Direct synchronous probe: the background monitor polls asynchronously,
        // but synchronous cleanup phases (e.g. RestoreMonitors) have no await points,
        // so the monitor cannot interleave. We must probe here directly.
        var runtime = _context.CaptureRuntime();
        if (runtime is not null)
        {
            // Attempt atomic commit through the arbiter.
            if (_arbiter.TryCommitRuntime(runtime))
            {
                throw new OperationCanceledException();
            }
            // Arbiter already committed (caller cancellation); don't throw here —
            // the linked token check in the next phase will propagate it.
        }
    }
}
