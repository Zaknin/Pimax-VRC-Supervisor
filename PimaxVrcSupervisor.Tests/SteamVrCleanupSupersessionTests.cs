using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class SteamVrCleanupSupersessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-26T02:00:00Z");

    // =========================================================================
    // CleanupOperationCoordinator — admission and waiter semantics
    // =========================================================================

    [Fact]
    public async Task Coordinator_FirstCaller_BecomesOwner()
    {
        var coordinator = new CleanupOperationCoordinator();
        int ownerCount = 0;

        var outcome = await coordinator.RunAsync(
            token =>
            {
                ownerCount++;
                return Task.FromResult(CleanupOutcome.Completed());
            },
            CancellationToken.None);

        Assert.Equal(CleanupOutcomeKind.Completed, outcome.Kind);
        Assert.Equal(1, ownerCount);
    }

    [Fact]
    public async Task Coordinator_SecondCaller_JoinsAsWaiter()
    {
        var coordinator = new CleanupOperationCoordinator();
        int ownerCount = 0;
        bool joinedWaiterCalled = false;

        var ownerStarted = new TaskCompletionSource();
        var ownerCanComplete = new TaskCompletionSource();

        var taskA = coordinator.RunAsync(
            async token =>
            {
                ownerCount++;
                ownerStarted.SetResult();
                await ownerCanComplete.Task;
                return CleanupOutcome.Completed();
            },
            CancellationToken.None);

        var taskB = coordinator.RunAsync(
            token =>
            {
                return Task.FromException<CleanupOutcome>(
                    new InvalidOperationException("waiter should not run owner delegate"));
            },
            CancellationToken.None,
            onJoinedWaiter: () => joinedWaiterCalled = true);

        await ownerStarted.Task;

        Assert.Equal(1, ownerCount);
        Assert.True(joinedWaiterCalled);

        ownerCanComplete.SetResult();

        var outcomeA = await taskA;
        var outcomeB = await taskB;

        Assert.Equal(CleanupOutcomeKind.Completed, outcomeA.Kind);
        Assert.Equal(CleanupOutcomeKind.Completed, outcomeB.Kind);
        Assert.Equal(1, ownerCount);
    }

    [Fact]
    public async Task Coordinator_OwnerCancelledBeforeSerializationLock_WaiterReleasedCoherently()
    {
        var coordinator = new CleanupOperationCoordinator();
        var ownerCts = new CancellationTokenSource();
        int ownerOperationInvoked = 0;
        int destructiveCount = 0;

        var ownerEntered = new TaskCompletionSource();
        var serializationGate = new TaskCompletionSource();

        var ownerTask = coordinator.RunAsync(
            async token =>
            {
                ownerOperationInvoked++;
                ownerEntered.SetResult();
                await serializationGate.Task.WaitAsync(token);
                destructiveCount++;
                return CleanupOutcome.Completed();
            },
            ownerCts.Token);

        await ownerEntered.Task;

        var waiterCts = new CancellationTokenSource();
        var waiterTask = coordinator.RunAsync(
            token =>
                Task.FromException<CleanupOutcome>(new InvalidOperationException("waiter should not run")),
            waiterCts.Token);

        ownerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ownerTask);
        Assert.Equal(1, ownerOperationInvoked);
        Assert.Equal(0, destructiveCount);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiterTask);

        var newOwnerTask = coordinator.RunAsync(
            token => Task.FromResult(CleanupOutcome.Completed()),
            CancellationToken.None);
        var newOutcome = await newOwnerTask;
        Assert.Equal(CleanupOutcomeKind.Completed, newOutcome.Kind);
    }

    [Fact]
    public async Task Coordinator_CompletedByIndependentPath_PostLockStartedReturnsCompleted()
    {
        var coordinator = new CleanupOperationCoordinator();
        bool cleanupStarted = false;

        var ownerEntered = new TaskCompletionSource();
        var serializationGate = new TaskCompletionSource();

        var ownerTask = coordinator.RunAsync(
            async token =>
            {
                ownerEntered.SetResult();
                await serializationGate.Task.WaitAsync(token);

                if (cleanupStarted)
                {
                    return CleanupOutcome.Completed();
                }

                return CleanupOutcome.Completed();
            },
            CancellationToken.None);

        await ownerEntered.Task;

        var waiterTask = coordinator.RunAsync(
            token =>
                Task.FromException<CleanupOutcome>(new InvalidOperationException("should not run")),
            CancellationToken.None);

        cleanupStarted = true;
        serializationGate.SetResult();

        var outcome = await ownerTask;
        Assert.Equal(CleanupOutcomeKind.Completed, outcome.Kind);

        var waiterOutcome = await waiterTask;
        Assert.Equal(CleanupOutcomeKind.Completed, waiterOutcome.Kind);
    }

    [Fact]
    public async Task Coordinator_WaiterCancellationIsolation_DoesNotAffectOwner()
    {
        var coordinator = new CleanupOperationCoordinator();
        int destructiveCount = 0;

        var ownerBlocked = new TaskCompletionSource();
        var ownerCanComplete = new TaskCompletionSource();

        var ownerTask = coordinator.RunAsync(
            async token =>
            {
                destructiveCount++;
                ownerBlocked.SetResult();
                await ownerCanComplete.Task;
                return CleanupOutcome.Completed();
            },
            CancellationToken.None);

        await ownerBlocked.Task;

        var waiter1Cts = new CancellationTokenSource();
        var waiter1Task = coordinator.RunAsync(
            token =>
                Task.FromException<CleanupOutcome>(new InvalidOperationException("should not run")),
            waiter1Cts.Token);

        var waiter2Task = coordinator.RunAsync(
            token =>
                Task.FromException<CleanupOutcome>(new InvalidOperationException("should not run")),
            CancellationToken.None);

        waiter1Cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter1Task);

        Assert.Equal(1, destructiveCount);

        ownerCanComplete.SetResult();

        var ownerOutcome = await ownerTask;
        Assert.Equal(CleanupOutcomeKind.Completed, ownerOutcome.Kind);

        var waiter2Outcome = await waiter2Task;
        Assert.Equal(CleanupOutcomeKind.Completed, waiter2Outcome.Kind);

        Assert.Equal(1, destructiveCount);
    }

    [Fact]
    public async Task Coordinator_ConcurrentSupersession_WaiterReceivesExactSnapshot()
    {
        var coordinator = new CleanupOperationCoordinator();
        var runtimeB = Runtime(200);
        int destructiveCount = 0;

        var ownerEntered = new TaskCompletionSource();
        var ownerCanComplete = new TaskCompletionSource();

        var ownerTask = coordinator.RunAsync(
            async token =>
            {
                destructiveCount++;
                ownerEntered.SetResult();
                await ownerCanComplete.Task;
                return CleanupOutcome.Superseded(runtimeB);
            },
            CancellationToken.None);

        await ownerEntered.Task;

        var waiterTask = coordinator.RunAsync(
            token =>
                Task.FromException<CleanupOutcome>(new InvalidOperationException("should not run")),
            CancellationToken.None);

        ownerCanComplete.SetResult();

        var ownerOutcome = await ownerTask;
        var waiterOutcome = await waiterTask;

        Assert.Equal(CleanupOutcomeKind.SupersededByRuntime, ownerOutcome.Kind);
        Assert.Same(runtimeB, ownerOutcome.SupersedingRuntime);
        Assert.Equal(CleanupOutcomeKind.SupersededByRuntime, waiterOutcome.Kind);
        Assert.Same(runtimeB, waiterOutcome.SupersedingRuntime);
        Assert.Equal(1, destructiveCount);

        var laterOutcome = await coordinator.RunAsync(
            token => Task.FromResult(CleanupOutcome.Completed()),
            CancellationToken.None);
        Assert.Equal(CleanupOutcomeKind.Completed, laterOutcome.Kind);
    }

    [Fact]
    public async Task Coordinator_OwnerFault_WaiterReceivesExactException()
    {
        var coordinator = new CleanupOperationCoordinator();

        // Pre-created exception instance — owner and waiter must receive the same one.
        var ownerException = new InvalidOperationException("owner fault");
        int ownerInvocationCount = 0;

        var ownerEntered = new TaskCompletionSource();
        var ownerCanFault = new TaskCompletionSource();

        // 0. Invocation count is zero before any owner starts.
        Assert.Equal(0, ownerInvocationCount);

        // Owner publishes, enters, and signals readiness.
        var ownerTask = coordinator.RunAsync(
            async token =>
            {
                ownerInvocationCount++;
                ownerEntered.SetResult();
                // Block until the test is ready to release the owner to fault.
                await ownerCanFault.Task;
                throw ownerException;
            },
            CancellationToken.None);

        // 1. Owner delegate invocation count starts at zero, then becomes one.
        await ownerEntered.Task;
        Assert.Equal(1, ownerInvocationCount);

        // 2. Second caller joins while the owner is blocked.
        var waiterTask = coordinator.RunAsync(
            token =>
                Task.FromException<CleanupOutcome>(new InvalidOperationException("waiter delegate must not run")),
            CancellationToken.None);

        // 3. Invocation count remains exactly one after waiter admission.
        Assert.Equal(1, ownerInvocationCount);

        // 4. Release the owner to fault.
        ownerCanFault.SetResult();

        // 5. Owner receives that exact exception instance.
        var caughtOwner = await Assert.ThrowsAsync<InvalidOperationException>(() => ownerTask);
        Assert.Same(ownerException, caughtOwner);

        // 6. Waiter receives that exact same exception instance.
        var caughtWaiter = await Assert.ThrowsAsync<InvalidOperationException>(() => waiterTask);
        Assert.Same(ownerException, caughtWaiter);

        // 7. Invocation count remains exactly one.
        Assert.Equal(1, ownerInvocationCount);

        // 8. Active operation clears — a later caller can become a new owner.
        var laterOwnerTask = coordinator.RunAsync(
            token =>
            {
                ownerInvocationCount++;
                return Task.FromResult(CleanupOutcome.Completed());
            },
            CancellationToken.None);
        var laterOutcome = await laterOwnerTask;
        Assert.Equal(CleanupOutcomeKind.Completed, laterOutcome.Kind);

        // 9. Later owner invocation is separately counted.
        Assert.Equal(2, ownerInvocationCount);
    }

    // =========================================================================
    // CleanupSupersessionArbiter — standalone tests
    // =========================================================================

    [Fact]
    public void Arbiter_PreCancelledCaller_CommitsSynchronously()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(cts.Token);

        Assert.Equal(SupersessionArbiterState.CallerCancelled, arbiter.State);
        Assert.False(arbiter.TryCommitRuntime(Runtime(200)));
        Assert.Equal(SupersessionArbiterState.CallerCancelled, arbiter.State);
    }

    [Fact]
    public void Arbiter_BWins_ThenCancellationCannotReplace()
    {
        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(CancellationToken.None);

        var runtimeB = Runtime(200);
        Assert.True(arbiter.TryCommitRuntime(runtimeB));
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, arbiter.State);

        Assert.False(arbiter.TryCommitCallerCancellation());
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, arbiter.State);
    }

    [Fact]
    public void Arbiter_CannotReplaceB_WithC()
    {
        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(CancellationToken.None);

        var runtimeB = Runtime(200);
        var runtimeC = Runtime(300);

        Assert.True(arbiter.TryCommitRuntime(runtimeB));
        Assert.False(arbiter.TryCommitRuntime(runtimeC));

        var result = arbiter.GetResult();
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, result.State);
        Assert.Same(runtimeB, result.WinningRuntime);
    }

    [Fact]
    public void Arbiter_NullNeverCommits()
    {
        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(CancellationToken.None);

        Assert.False(arbiter.TryCommitRuntime(null));
        Assert.Equal(SupersessionArbiterState.Pending, arbiter.State);
    }

    [Fact]
    public void Arbiter_SignalToken_CancelledOnCommit()
    {
        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(CancellationToken.None);

        Assert.False(arbiter.SignalToken.IsCancellationRequested);

        arbiter.TryCommitRuntime(Runtime(200));
        Assert.True(arbiter.SignalToken.IsCancellationRequested);
    }

    [Fact]
    public void Arbiter_SignalToken_CancelledOnCallerCancellation()
    {
        using var arbiter = new CleanupSupersessionArbiter();
        var cts = new CancellationTokenSource();
        arbiter.RegisterCallerCancellation(cts.Token);

        Assert.False(arbiter.SignalToken.IsCancellationRequested);

        cts.Cancel();
        Assert.True(arbiter.SignalToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Arbiter_SimultaneousCompetition_ExactlyOneWinner()
    {
        using var arbiter = new CleanupSupersessionArbiter();
        var cts = new CancellationTokenSource();
        arbiter.RegisterCallerCancellation(cts.Token);

        var runtimeB = Runtime(200);

        // Controlled start barrier: both competitors reach this before release.
        var bothReady = new TaskCompletionSource();
        int cancellationReachedBarrier = 0;
        int runtimeReachedBarrier = 0;

        // Caller-cancellation commit task.
        var cancellationCommitTask = Task.Run(async () =>
        {
            Interlocked.Increment(ref cancellationReachedBarrier);
            await bothReady.Task;
            return arbiter.TryCommitCallerCancellation();
        });

        // Runtime-B commit task.
        var runtimeCommitTask = Task.Run(async () =>
        {
            Interlocked.Increment(ref runtimeReachedBarrier);
            await bothReady.Task;
            return arbiter.TryCommitRuntime(runtimeB);
        });

        // Prove both competitors reached the start barrier.
        while (cancellationReachedBarrier + runtimeReachedBarrier < 2)
        {
            await Task.Yield();
        }

        // Release both.
        bothReady.SetResult();

        // Await both asynchronously (no blocking .Wait()).
        var cancellationWon = await cancellationCommitTask;
        var runtimeWon = await runtimeCommitTask;

        // Both competitors executed.
        Assert.Equal(1, cancellationReachedBarrier);
        Assert.Equal(1, runtimeReachedBarrier);

        // Exactly one commit succeeded.
        Assert.True(cancellationWon ^ runtimeWon,
            $"Expected exactly one winner, but cancellation={cancellationWon}, runtime={runtimeWon}");

        var result = arbiter.GetResult();
        if (cancellationWon)
        {
            Assert.Equal(SupersessionArbiterState.CallerCancelled, result.State);
            Assert.Null(result.WinningRuntime);
        }
        else
        {
            Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, result.State);
            Assert.Same(runtimeB, result.WinningRuntime);
        }

        // The loser cannot replace the winner.
        if (cancellationWon)
        {
            Assert.False(arbiter.TryCommitRuntime(runtimeB));
        }
        else
        {
            Assert.False(arbiter.TryCommitCallerCancellation());
        }
    }

    // =========================================================================
    // B-1: First runtime identity — deterministic tests
    // =========================================================================

    [Fact]
    public async Task InitialProbe_RuntimePresent_ReturnsSupersededImmediately()
    {
        var runtimeB = Runtime(200);
        var counts = new SideEffectCounts();
        var context = CreateContext(
            captureRuntime: () => runtimeB,
            counts: counts);

        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);
        var outcome = await supersession.RunAsync();

        Assert.Equal(CleanupOutcomeKind.SupersededByRuntime, outcome.Kind);
        Assert.Same(runtimeB, outcome.SupersedingRuntime);
        Assert.Equal(0, counts.RestoreMonitorsCount);
        Assert.Equal(0, counts.PowerDownBaseStationsCount);
        Assert.Equal(0, counts.StopLovenseAppsCount);
        Assert.Equal(0, counts.StopManagedAppsCount);
    }

    [Fact]
    public async Task ReplacementBeforeBaseStation_MonitorsRestoredButNoStationPowerDown()
    {
        var runtimeB = Runtime(200);
        var counts = new SideEffectCounts();
        var shouldAppear = false;

        var context = CreateContext(
            captureRuntime: () => shouldAppear ? runtimeB : null,
            counts: counts,
            restoreMonitors: () =>
            {
                counts.RestoreMonitorsCount++;
                shouldAppear = true;
            });

        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);
        var outcome = await supersession.RunAsync();

        Assert.Equal(CleanupOutcomeKind.SupersededByRuntime, outcome.Kind);
        Assert.Same(runtimeB, outcome.SupersedingRuntime);
        Assert.Equal(1, counts.RestoreMonitorsCount);
        Assert.Equal(0, counts.PowerDownBaseStationsCount);
        Assert.Equal(0, counts.StopLovenseAppsCount);
        Assert.Equal(0, counts.StopManagedAppsCount);
    }

    [Fact]
    public async Task ReplacementDuringBaseStation_StationPhaseEnteredButNoLovenseOrManagedApps()
    {
        var runtimeB = Runtime(200);
        var counts = new SideEffectCounts();
        var shouldAppear = false;
        var baseStationCompleted = new TaskCompletionSource();

        var context = CreateContext(
            captureRuntime: () => shouldAppear ? runtimeB : null,
            counts: counts,
            powerDownBaseStations: async token =>
            {
                counts.PowerDownBaseStationsCount++;
                shouldAppear = true;
                await baseStationCompleted.Task.WaitAsync(token);
            });

        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);
        var outcome = await supersession.RunAsync();

        baseStationCompleted.SetResult();

        Assert.Equal(CleanupOutcomeKind.SupersededByRuntime, outcome.Kind);
        Assert.Same(runtimeB, outcome.SupersedingRuntime);
        Assert.Equal(1, counts.RestoreMonitorsCount);
        Assert.Equal(1, counts.PowerDownBaseStationsCount);
        Assert.Equal(0, counts.StopLovenseAppsCount);
        Assert.Equal(0, counts.StopManagedAppsCount);
    }

    [Fact]
    public async Task ReplacementBeforeManagedApps_BaseStationsAndLovenseCompleteButNoManagedAppStop()
    {
        var runtimeB = Runtime(200);
        var counts = new SideEffectCounts();
        var shouldAppear = false;

        var context = CreateContext(
            captureRuntime: () => shouldAppear ? runtimeB : null,
            counts: counts,
            stopLovenseApps: token =>
            {
                counts.StopLovenseAppsCount++;
                shouldAppear = true;
                return Task.CompletedTask;
            });

        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);
        var outcome = await supersession.RunAsync();

        Assert.Equal(CleanupOutcomeKind.SupersededByRuntime, outcome.Kind);
        Assert.Same(runtimeB, outcome.SupersedingRuntime);
        Assert.Equal(1, counts.RestoreMonitorsCount);
        Assert.Equal(1, counts.PowerDownBaseStationsCount);
        Assert.Equal(1, counts.StopLovenseAppsCount);
        Assert.Equal(0, counts.StopManagedAppsCount);
    }

    [Fact]
    public async Task NoReplacement_AllPhasesExecuteExactlyOnce()
    {
        var counts = new SideEffectCounts();
        var context = CreateContext(
            captureRuntime: () => null,
            counts: counts);

        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);
        var outcome = await supersession.RunAsync();

        Assert.Equal(CleanupOutcomeKind.Completed, outcome.Kind);
        Assert.Null(outcome.SupersedingRuntime);
        Assert.Equal(1, counts.RestoreMonitorsCount);
        Assert.Equal(1, counts.PowerDownBaseStationsCount);
        Assert.Equal(1, counts.StopLovenseAppsCount);
        Assert.Equal(1, counts.StopManagedAppsCount);
    }

    // =========================================================================
    // First-runtime identity — deterministic latch tests
    // =========================================================================

    [Fact]
    public void Arbiter_NullOfferedAfterB_BRemainsWinner()
    {
        // Direct arbiter test: B commits, then null is explicitly offered and rejected.
        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(CancellationToken.None);

        var runtimeB = Runtime(200);

        // B commits first.
        Assert.True(arbiter.TryCommitRuntime(runtimeB));
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, arbiter.State);

        // Null is explicitly offered afterward — must be rejected.
        Assert.False(arbiter.TryCommitRuntime(null));

        // Arbiter remains RuntimeSuperseded with exact B.
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, arbiter.State);
        var result = arbiter.GetResult();
        Assert.Same(runtimeB, result.WinningRuntime);
    }

    [Fact]
    public void Arbiter_COfferedAfterB_BRemainsWinner()
    {
        // Direct arbiter test: B commits, then C is explicitly offered and rejected.
        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(CancellationToken.None);

        var runtimeB = Runtime(200);
        var runtimeC = Runtime(300);

        // B commits first.
        Assert.True(arbiter.TryCommitRuntime(runtimeB));
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, arbiter.State);

        // C is explicitly offered afterward — must be rejected.
        Assert.False(arbiter.TryCommitRuntime(runtimeC));

        var result = arbiter.GetResult();
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, result.State);
        Assert.Same(runtimeB, result.WinningRuntime);
    }

    [Fact]
    public void Arbiter_BWins_ThenCallerCancellationOccurs_ResultRemainsB()
    {
        // Direct arbiter test: B commits, then caller is cancelled afterward.
        using var arbiter = new CleanupSupersessionArbiter();
        var callerCts = new CancellationTokenSource();
        arbiter.RegisterCallerCancellation(callerCts.Token);

        var runtimeB = Runtime(200);

        // B commits.
        Assert.True(arbiter.TryCommitRuntime(runtimeB));
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, arbiter.State);

        // Caller is cancelled afterward — the registered callback fires synchronously.
        callerCts.Cancel();

        // Cancellation callback executed but could not replace B.
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, arbiter.State);

        var result = arbiter.GetResult();
        Assert.Same(runtimeB, result.WinningRuntime);

        // Signal token remains cancelled (terminal).
        Assert.True(arbiter.SignalToken.IsCancellationRequested);

        // Explicit cancellation attempt also fails.
        Assert.False(arbiter.TryCommitCallerCancellation());
    }

    // =========================================================================
    // Direct/monitor race — controlled commit test
    // =========================================================================

    [Fact]
    public async Task Arbiter_ConcurrentRuntimeOffers_ExactlyOneSnapshotWins()
    {
        // Direct arbiter test: two runtime commits race under controlled barriers.
        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(CancellationToken.None);

        var runtimeB = Runtime(200);
        var runtimeC = Runtime(300);

        // Both competitors reach a controlled pre-commit barrier.
        var bothReady = new TaskCompletionSource();
        bool bReached = false;
        bool cReached = false;

        var bCommitTask = Task.Run(async () =>
        {
            bReached = true;
            await bothReady.Task;
            return arbiter.TryCommitRuntime(runtimeB);
        });

        var cCommitTask = Task.Run(async () =>
        {
            cReached = true;
            await bothReady.Task;
            return arbiter.TryCommitRuntime(runtimeC);
        });

        // Both reached the barrier.
        while (!bReached || !cReached)
        {
            await Task.Yield();
        }

        // Release both.
        bothReady.SetResult();

        // Await both asynchronously.
        var bWon = await bCommitTask;
        var cWon = await cCommitTask;

        // Exactly one wins.
        Assert.True(bWon ^ cWon,
            $"Expected exactly one winner: b={bWon}, c={cWon}");

        var result = arbiter.GetResult();
        Assert.Equal(SupersessionArbiterState.RuntimeSuperseded, result.State);

        // Exact winner is retained.
        if (bWon)
        {
            Assert.Same(runtimeB, result.WinningRuntime);
        }
        else
        {
            Assert.Same(runtimeC, result.WinningRuntime);
        }

        // The loser cannot replace the winner.
        if (bWon)
        {
            Assert.False(arbiter.TryCommitRuntime(runtimeC));
        }
        else
        {
            Assert.False(arbiter.TryCommitRuntime(runtimeB));
        }
    }

    // =========================================================================
    // Caller cancellation precedence tests
    // =========================================================================

    [Fact]
    public async Task CallerCancelledBeforeEntry_CaptureRuntimeNotCalled_ThrowsOCE()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var counts = new SideEffectCounts();
        int captureCount = 0;
        var context = CreateContext(
            captureRuntime: () =>
            {
                captureCount++;
                return Runtime(200);
            },
            counts: counts);

        using var supersession = new SteamVrCleanupSupersession(context, cts.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => supersession.RunAsync());

        Assert.Equal(0, captureCount);
        Assert.Equal(0, counts.RestoreMonitorsCount);
    }

    [Fact]
    public async Task CallerCancelledAfterMonitorStart_ThrowsOCENotSupersession()
    {
        var callerCts = new CancellationTokenSource();
        var counts = new SideEffectCounts();
        int pollCount = 0;

        var context = CreateContext(
            captureRuntime: () => null,
            counts: counts,
            supersessionPollDelay: async token =>
            {
                pollCount++;
                if (pollCount == 1)
                {
                    callerCts.Cancel();
                }
                // Cooperative delay: respects the token now passed to the delegate.
                await Task.Delay(1, token);
            });

        using var supersession = new SteamVrCleanupSupersession(context, callerCts.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => supersession.RunAsync());

        Assert.Equal(0, counts.RestoreMonitorsCount);
    }

    // =========================================================================
    // Cancellation-versus-latch arbiter — deterministic tests
    // =========================================================================

    [Fact]
    public async Task Arbiter_PreCancelledCallerWins_BeforeAnyCaptureRuntime()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        int captureCount = 0;
        var context = CreateContext(
            captureRuntime: () =>
            {
                captureCount++;
                return Runtime(200);
            },
            counts: new SideEffectCounts());

        using var supersession = new SteamVrCleanupSupersession(context, cts.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => supersession.RunAsync());
        Assert.Equal(0, captureCount);
    }

    [Fact]
    public async Task Arbiter_CallerWins_AfterMonitorStartButBeforeB()
    {
        var callerCts = new CancellationTokenSource();
        var counts = new SideEffectCounts();
        int pollCount = 0;

        var context = CreateContext(
            captureRuntime: () => null,
            counts: counts,
            supersessionPollDelay: async token =>
            {
                pollCount++;
                if (pollCount == 1)
                {
                    callerCts.Cancel();
                }
                await Task.Delay(1, token);
            });

        using var supersession = new SteamVrCleanupSupersession(context, callerCts.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => supersession.RunAsync());
        Assert.Equal(0, counts.RestoreMonitorsCount);
    }

    // =========================================================================
    // Simultaneous caller/B integration — removed as weak duplicate
    // =========================================================================
    // The standalone Arbiter_SimultaneousCompetition_ExactlyOneWinner already
    // proves the race with controlled barriers. An integration-level race test
    // cannot control the exact timing of the arbiter commit from the integration
    // seam, and the outcome depends on internal scheduling that varies between
    // Debug and Release builds. The integration evidence is not additive.
    // =========================================================================

    // =========================================================================
    // Monitor finalization tests
    // =========================================================================

    [Fact]
    public async Task Monitor_DelayDelegateThrowsOCEWhileMonitorNotCancelled_ExceptionNotSwallowed()
    {
        var counts = new SideEffectCounts();

        var context = CreateContext(
            captureRuntime: () => null,
            counts: counts,
            supersessionPollDelay: token =>
            {
                // The monitor token is NOT cancelled — this OCE is unexpected.
                throw new OperationCanceledException("delay fault");
            },
            stopManagedApps: async token =>
            {
                counts.StopManagedAppsCount++;
                // Give the monitor a chance to poll and throw.
                await Task.Delay(50, CancellationToken.None);
            });

        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => supersession.RunAsync());
    }

    [Fact]
    public async Task Monitor_DelayDelegateIgnoresToken_FinalizationStillCompletes()
    {
        var counts = new SideEffectCounts();
        var neverCompleting = new TaskCompletionSource().Task;

        var context = CreateContext(
            captureRuntime: () => null,
            counts: counts,
            supersessionPollDelay: token =>
            {
                // Ignores cancellation — returns a never-completing task.
                return neverCompleting;
            });

        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);

        var runTask = supersession.RunAsync();

        // Cleanup completes all phases synchronously (no runtime ever appears),
        // so the monitor's delay is irrelevant — the finalization cancels the monitor.
        var completedTask = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(runTask, completedTask);

        var outcome = await runTask;
        Assert.Equal(CleanupOutcomeKind.Completed, outcome.Kind);
    }

    [Fact]
    public async Task Monitor_ExpectedCancellation_RemainsQuiet()
    {
        var counts = new SideEffectCounts();
        var context = CreateContext(
            captureRuntime: () => null,
            counts: counts);

        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);
        var outcome = await supersession.RunAsync();

        Assert.Equal(CleanupOutcomeKind.Completed, outcome.Kind);
    }

    [Fact]
    public async Task Monitor_DelayThrowsUnexpectedException_ExceptionPropagates()
    {
        var counts = new SideEffectCounts();
        var context = CreateContext(
            captureRuntime: () => null,
            counts: counts,
            supersessionPollDelay: token => throw new InvalidOperationException("monitor fault"));

        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => supersession.RunAsync());
    }

    [Fact]
    public async Task Monitor_CaptureThrowsUnexpectedException_ExceptionPropagates()
    {
        var counts = new SideEffectCounts();
        var monitorCaptureStarted = new TaskCompletionSource();
        var captureException = new InvalidOperationException("capture fault");
        int captureCount = 0;

        // The monitor's capture (captureCount >= 3) throws.
        var contextWithFault = CreateContext(
            captureRuntime: () =>
            {
                captureCount++;
                if (captureCount <= 2)
                {
                    return null;
                }
                if (captureCount == 3)
                {
                    // This is the monitor's capture — signal and throw.
                    monitorCaptureStarted.SetResult();
                    throw captureException;
                }
                return null;
            },
            counts: counts,
            supersessionPollDelay: token => Task.CompletedTask);

        using var supersession = new SteamVrCleanupSupersession(contextWithFault, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => supersession.RunAsync());

        // The monitor capture actually started.
        Assert.True(monitorCaptureStarted.Task.IsCompleted);
    }

    // =========================================================================
    // InjectedTaskAwaiter — abandoned-fault observation tests
    // =========================================================================

    [Fact]
    public async Task InjectedTaskAwaiter_CooperativeCancellation_IsQuiet()
    {
        var delayCts = new CancellationTokenSource();
        var delayTask = Task.Delay(1000, delayCts.Token);

        var helperCts = new CancellationTokenSource();

        var awaitTask = InjectedTaskAwaiter.AwaitWithAbandonedObservationAsync(delayTask, helperCts.Token);

        // Cancel the helper — the delay is cooperative, so it should cancel promptly.
        helperCts.Cancel();
        delayCts.Cancel();

        // Should complete without throwing (OCE is expected when cancellation wins).
        // The delay task itself will throw OCE, but the helper catches it via WhenAny.
        await awaitTask;
    }

    [Fact]
    public async Task InjectedTaskAwaiter_TokenIgnoringDelay_DoesNotHang()
    {
        var neverCompleting = new TaskCompletionSource().Task;
        var helperCts = new CancellationTokenSource();

        var awaitTask = InjectedTaskAwaiter.AwaitWithAbandonedObservationAsync(neverCompleting, helperCts.Token);

        // Cancel the helper — should return immediately despite the never-completing delay.
        helperCts.Cancel();

        var completed = await Task.WhenAny(awaitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(awaitTask, completed);
    }

    [Fact]
    public async Task InjectedTaskAwaiter_OrdinaryFaultBeforeAbandonment_Propagates()
    {
        var faultTask = Task.FromException(new InvalidOperationException("fault"));
        var helperCts = new CancellationTokenSource();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InjectedTaskAwaiter.AwaitWithAbandonedObservationAsync(faultTask, helperCts.Token));
    }

    [Fact]
    public async Task InjectedTaskAwaiter_CaptureFaultPropagates()
    {
        var faultTask = Task.FromException(new ArgumentException("capture error"));
        var helperCts = new CancellationTokenSource();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            InjectedTaskAwaiter.AwaitWithAbandonedObservationAsync(faultTask, helperCts.Token));
    }

    [Fact]
    public async Task InjectedTaskAwaiter_AbandonedLateFault_IsObserved()
    {
        // 1. Create an incomplete TCS-backed underlying task (RunContinuationsAsynchronously).
        var delayTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 2. Pre-build the exact exception instance the underlying task will fault with.
        var faultException = new InvalidOperationException("late fault");

        // 3. Start the actual internal core helper.
        Exception? observedException = null;
        var helperCts = new CancellationTokenSource();

        var coreTask = InjectedTaskAwaiter.AwaitWithAbandonedObservationCoreAsync(
            delayTcs.Task,
            helperCts.Token,
            ex => observedException = ex);

        // 4. Cancel the helper token.
        helperCts.Cancel();

        // 5. Await helper exit and obtain the non-null observation continuation Task.
        Task? observationContinuation = await coreTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(observationContinuation);

        // 6. Assert helper exited before the underlying task completed.
        Assert.False(delayTcs.Task.IsCompleted);

        // 7. Fault the underlying task afterward with the pre-built exception.
        delayTcs.SetException(faultException);

        // 8. Await the returned observation continuation (timeout is deadlock guard only).
        await observationContinuation.WaitAsync(TimeSpan.FromSeconds(5));

        // 9. Assert continuation completed successfully.
        Assert.True(observationContinuation.IsCompletedSuccessfully);

        // 10. Assert observer was invoked with the exact pre-built exception instance.
        Assert.NotNull(observedException);
        Assert.IsType<AggregateException>(observedException);
        var aggEx = (AggregateException)observedException;
        Assert.Same(faultException, aggEx.InnerException);
    }

    [Fact]
    public async Task InjectedTaskAwaiter_ObserverThrows_ObservationContinuationDoesNotFault()
    {
        // 1. Create an incomplete TCS-backed underlying task.
        var delayTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 2. Pre-build the observer's deliberate exception.
        var observerException = new InvalidOperationException("observer fault");

        // 3. Start the core helper with an observer that throws.
        bool observerInvoked = false;
        Exception? observedAggregate = null;
        var helperCts = new CancellationTokenSource();

        var coreTask = InjectedTaskAwaiter.AwaitWithAbandonedObservationCoreAsync(
            delayTcs.Task,
            helperCts.Token,
            ex =>
            {
                observerInvoked = true;
                observedAggregate = ex;
                throw observerException;
            });

        // 4. Cancel the helper token.
        helperCts.Cancel();

        // 5. Await helper exit and obtain the exact non-null continuation Task.
        Task? observationContinuation = await coreTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(observationContinuation);

        // 6. Fault the underlying task afterward.
        var faultException = new InvalidOperationException("late fault");
        delayTcs.SetException(faultException);

        // 7. Await the returned continuation Task directly (timeout is deadlock guard only).
        await observationContinuation.WaitAsync(TimeSpan.FromSeconds(5));

        // 8. Assert observer executed and received the underlying AggregateException.
        Assert.True(observerInvoked);
        Assert.NotNull(observedAggregate);
        Assert.IsType<AggregateException>(observedAggregate);
        var aggEx = (AggregateException)observedAggregate;
        Assert.Same(faultException, aggEx.InnerException);

        // 9. Assert continuation completed successfully — observer's exception did not escape.
        Assert.True(observationContinuation.IsCompletedSuccessfully);
    }

    // =========================================================================
    // H-1: Cleanup ownership selection — production-wired tests
    // =========================================================================

    [Fact]
    public void SelectCleanupOwnership_SupervisorExit_ReturnsCommittedGlobal()
    {
        var decision = CleanupDecision(SteamVrRecoveryClassification.SupervisorExit);
        var mode = AppSupervisor.SelectCleanupOwnership(decision);
        Assert.Equal(CleanupOwnershipMode.CommittedGlobal, mode);
    }

    [Fact]
    public void SelectCleanupOwnership_RecoveryTimedOut_ReturnsRuntimeAbsence()
    {
        var decision = CleanupDecision(SteamVrRecoveryClassification.RecoveryTimedOut);
        var mode = AppSupervisor.SelectCleanupOwnership(decision);
        Assert.Equal(CleanupOwnershipMode.RuntimeAbsence, mode);
    }

    [Fact]
    public void SelectCleanupOwnership_RecoveryLimitReached_ReturnsRuntimeAbsence()
    {
        var decision = CleanupDecision(SteamVrRecoveryClassification.RecoveryLimitReached);
        var mode = AppSupervisor.SelectCleanupOwnership(decision);
        Assert.Equal(CleanupOwnershipMode.RuntimeAbsence, mode);
    }

    [Fact]
    public void SelectCleanupOwnership_NormalExit_ReturnsRuntimeAbsence()
    {
        var decision = CleanupDecision(SteamVrRecoveryClassification.NormalExit);
        var mode = AppSupervisor.SelectCleanupOwnership(decision);
        Assert.Equal(CleanupOwnershipMode.RuntimeAbsence, mode);
    }

    // =========================================================================
    // H-2: Lifecycle outcome handler — production-wired tests
    // =========================================================================

    [Fact]
    public void LifecycleHandler_SupersededByRuntime_NoMarkCompleted_EstablishesBaseline()
    {
        int markCompletedCount = 0;
        int establishBaselineCount = 0;
        SteamVrRuntimeIdentity? adoptedIdentity = null;
        SupervisorLifecyclePhase? lifecyclePhaseAfter = null;
        List<(string name, string? reason, string? result)> eventsWritten = new();

        var runtimeB = Runtime(200);
        var handler = new CleanupLifecycleOutcomeHandler(
            markCompleted: () => markCompletedCount++,
            establishBaseline: () => establishBaselineCount++,
            adoptExplicitReplacement: (identity, timestamp) => adoptedIdentity = identity,
            setLifecyclePhase: (phase) => lifecyclePhaseAfter = phase,
            writeLifecycleEvent: (name, reason, result, fields) =>
                eventsWritten.Add((name, reason, result)));

        var shouldExit = handler.Handle(CleanupOutcome.Superseded(runtimeB));

        Assert.False(shouldExit);
        Assert.Equal(0, markCompletedCount);
        Assert.Equal(1, establishBaselineCount);
        Assert.Equal(runtimeB.Identity, adoptedIdentity);
        Assert.Equal(SupervisorLifecyclePhase.VrChatRunning, lifecyclePhaseAfter);
        Assert.Contains(eventsWritten, e => e.name == "cleanup.supersession" && e.reason == "adoption");
    }

    [Fact]
    public void LifecycleHandler_Completed_MarkCompletedCalledOnce()
    {
        int markCompletedCount = 0;
        int establishBaselineCount = 0;
        SteamVrRuntimeIdentity? adoptedIdentity = null;

        var handler = new CleanupLifecycleOutcomeHandler(
            markCompleted: () => markCompletedCount++,
            establishBaseline: () => establishBaselineCount++,
            adoptExplicitReplacement: (identity, timestamp) => adoptedIdentity = identity,
            setLifecyclePhase: (phase) => { },
            writeLifecycleEvent: (name, reason, result, fields) => { });

        var shouldExit = handler.Handle(CleanupOutcome.Completed());

        Assert.True(shouldExit);
        Assert.Equal(1, markCompletedCount);
        Assert.Equal(0, establishBaselineCount);
        Assert.Null(adoptedIdentity);
    }

    // =========================================================================
    // Lifecycle coordinator adoption tests
    // =========================================================================

    [Fact]
    public void AdoptExplicitReplacement_AcceptsCoordinatorAfterCleanupAdmission()
    {
        var coordinator = new SteamVrRecoveryCoordinator(true);

        _ = coordinator.Observe([Runtime(100)], SteamVrLifecycleEvidence.None, Now);
        Assert.Equal(SteamVrRecoveryState.Running, coordinator.State);

        var exit = coordinator.Observe(
            [],
            new SteamVrLifecycleEvidence(true, false, SteamVrLifecycleEvidenceReader.ShutdownRequestedMarker),
            Now.AddSeconds(1));
        Assert.Equal(SteamVrRecoveryClassification.NormalExit, exit.Classification);
        Assert.True(exit.RunNormalCleanup);
        Assert.Equal(SteamVrRecoveryState.SessionEnding, coordinator.State);

        coordinator.AdoptExplicitReplacement(Runtime(200).Identity, Now.AddSeconds(2));

        Assert.Equal(SteamVrRecoveryState.Running, coordinator.State);
        Assert.Equal(Runtime(200).Identity, coordinator.CurrentRuntime);
        Assert.False(coordinator.IsRecoveryPending);
    }

    [Fact]
    public void AdoptExplicitReplacement_ClearsRecoveryDeadlines()
    {
        var coordinator = new SteamVrRecoveryCoordinator(true);
        _ = coordinator.Observe([Runtime(100)], SteamVrLifecycleEvidence.None, Now);

        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Now.AddSeconds(1));
        Assert.True(coordinator.IsRecoveryPending);

        coordinator.AdoptExplicitReplacement(Runtime(200).Identity, Now.AddSeconds(2));

        Assert.Equal(SteamVrRecoveryState.Running, coordinator.State);
        Assert.False(coordinator.IsRecoveryPending);
    }

    [Fact]
    public void AdoptExplicitReplacement_DoesNotSuppressLaterGenuineLoss()
    {
        var coordinator = new SteamVrRecoveryCoordinator(true);
        _ = coordinator.Observe([Runtime(100)], SteamVrLifecycleEvidence.None, Now);

        coordinator.AdoptExplicitReplacement(Runtime(200).Identity, Now.AddSeconds(1));
        Assert.Equal(SteamVrRecoveryState.Running, coordinator.State);

        var loss = coordinator.Observe([], SteamVrLifecycleEvidence.None, Now.AddSeconds(2));

        Assert.True(loss.LossDetected);
        Assert.True(coordinator.IsRecoveryPending);
        Assert.Null(coordinator.CurrentRuntime);
    }

    [Fact]
    public void AdoptExplicitReplacement_AcceptsFromRecoveryPendingState()
    {
        var coordinator = new SteamVrRecoveryCoordinator(true);
        _ = coordinator.Observe([Runtime(100)], SteamVrLifecycleEvidence.None, Now);
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Now.AddSeconds(1));
        Assert.Equal(SteamVrRecoveryState.RecoveryPending, coordinator.State);

        coordinator.AdoptExplicitReplacement(Runtime(200).Identity, Now.AddSeconds(2));

        Assert.Equal(SteamVrRecoveryState.Running, coordinator.State);
        Assert.Equal(Runtime(200).Identity, coordinator.CurrentRuntime);
    }

    [Fact]
    public void AdoptExplicitReplacement_RefusedWhenSupervisorExitRequested()
    {
        var coordinator = new SteamVrRecoveryCoordinator(true);
        _ = coordinator.Observe([Runtime(100)], SteamVrLifecycleEvidence.None, Now);

        coordinator.MarkSupervisorExitRequested();
        Assert.Equal(SteamVrRecoveryState.SessionEnding, coordinator.State);

        coordinator.AdoptExplicitReplacement(Runtime(200).Identity, Now.AddSeconds(1));

        Assert.Equal(SteamVrRecoveryState.SessionEnding, coordinator.State);
    }

    // =========================================================================
    // Exact-B journal → outcome → adoption integration test
    // =========================================================================

    [Fact]
    public async Task Supersession_ExactRuntimeFlowsThroughJournalOutcomeAndAdoption()
    {
        // 1. Create exact replacement snapshot B with a known identity.
        var runtimeB = Runtime(200);

        // 2. Record actual supersession lifecycle journal events.
        List<(string name, string? reason, string? result, IReadOnlyDictionary<string, string?>? fields)> journalEvents = new();

        // 3. Configure context: initial capture is null; after monitor restore,
        //    the supersession probe returns exact B; B commits through the real arbiter.
        var counts = new SideEffectCounts();

        var context = CreateContext(
            captureRuntime: () =>
            {
                // After monitor restore, the next probe returns B.
                if (counts.RestoreMonitorsCount >= 1)
                {
                    return runtimeB;
                }
                return null;
            },
            counts: counts,
            writeLifecycleEvent: (name, reason, result, fields) =>
                journalEvents.Add((name, reason, result, fields)));

        // 4. Run the real SteamVrCleanupSupersession.RunAsync.
        using var supersession = new SteamVrCleanupSupersession(context, CancellationToken.None);
        var outcome = await supersession.RunAsync();

        // 5. Assert outcome.
        Assert.Equal(CleanupOutcomeKind.SupersededByRuntime, outcome.Kind);
        Assert.Same(runtimeB, outcome.SupersedingRuntime);

        // 6. Assert exactly one replacement-detected/supersession journal event exists.
        var supersessionEvents = journalEvents
            .Where(e => e.name == "cleanup.supersession" && e.reason == "superseded")
            .ToList();
        Assert.Single(supersessionEvents);

        // 7. That event identifies exact B.
        var supersessionEvent = supersessionEvents[0];
        Assert.NotNull(supersessionEvent.fields);
        Assert.Equal(runtimeB.Identity.ToString(), supersessionEvent.fields!["supersedingRuntime"]);

        // 8. Pass the actual returned outcome into the real CleanupLifecycleOutcomeHandler.
        int markCompletedCount = 0;
        int establishBaselineCount = 0;
        SteamVrRuntimeIdentity? adoptedIdentity = null;
        SupervisorLifecyclePhase? lifecyclePhaseAfter = null;
        List<(string name, string? reason, string? result)> handlerEvents = new();

        var handler = new CleanupLifecycleOutcomeHandler(
            markCompleted: () => markCompletedCount++,
            establishBaseline: () => establishBaselineCount++,
            adoptExplicitReplacement: (identity, timestamp) => adoptedIdentity = identity,
            setLifecyclePhase: (phase) => lifecyclePhaseAfter = phase,
            writeLifecycleEvent: (name, reason, result, fields) =>
                handlerEvents.Add((name, reason, result)));

        bool shouldExit = handler.Handle(outcome);

        // 9. Assert handler behavior.
        Assert.False(shouldExit);
        Assert.Equal(0, markCompletedCount);
        Assert.Equal(1, establishBaselineCount);
        Assert.Equal(runtimeB.Identity, adoptedIdentity);
        Assert.Equal(SupervisorLifecyclePhase.VrChatRunning, lifecyclePhaseAfter);

        // 10. Assert exactly one replacement-adopted journal event from handler.
        var adoptionEvents = handlerEvents
            .Where(e => e.name == "cleanup.supersession" && e.reason == "adoption")
            .ToList();
        Assert.Single(adoptionEvents);

        // 11. Confirm the same B identity flows through the entire chain:
        //     capture → arbiter winner → detection journal → CleanupOutcome →
        //     CleanupLifecycleOutcomeHandler → adoption journal → replacement adoption.
        Assert.Same(runtimeB, outcome.SupersedingRuntime);
        Assert.Equal(runtimeB.Identity, adoptedIdentity);
    }

    // =========================================================================
    // Arbiter disposal lifecycle tests
    // =========================================================================

    [Fact]
    public void CleanupSupersessionArbiter_Dispose_UnregistersCallerCancellation()
    {
        // 1. Create a real caller CancellationTokenSource.
        var callerCts = new CancellationTokenSource();

        // 2. Construct the arbiter with the caller token.
        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(callerCts.Token);

        // 3. Capture the signal token BEFORE disposal.
        var signalToken = arbiter.SignalToken;

        // 4. Assert signal token is not yet cancelled.
        Assert.False(signalToken.IsCancellationRequested);

        // 5. Dispose the arbiter.
        arbiter.Dispose();

        // 6. Cancel the caller token AFTER disposal.
        callerCts.Cancel();

        // 7. Assert: cancel did not throw; signal token remains uncancelled;
        //    no callback exception escaped; no ObjectDisposedException occurred.
        Assert.False(signalToken.IsCancellationRequested);
    }

    [Fact]
    public void CleanupSupersessionArbiter_Dispose_IsIdempotent()
    {
        // Multiple Dispose calls must not throw.
        using var arbiter = new CleanupSupersessionArbiter();
        arbiter.RegisterCallerCancellation(CancellationToken.None);

        arbiter.Dispose();
        // Second dispose must not throw.
        arbiter.Dispose();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static SteamVrCleanupSupersessionContext CreateContext(
        Func<SteamVrRuntimeSnapshot?> captureRuntime,
        SideEffectCounts counts,
        Action? restoreMonitors = null,
        Func<CancellationToken, Task>? powerDownBaseStations = null,
        Func<CancellationToken, Task>? stopLovenseApps = null,
        Func<CancellationToken, Task>? stopManagedApps = null,
        Func<CancellationToken, Task>? supersessionPollDelay = null,
        Action<string, string?, string?, IReadOnlyDictionary<string, string?>?>? writeLifecycleEvent = null)
    {
        return new SteamVrCleanupSupersessionContext
        {
            CaptureRuntime = captureRuntime,
            RestoreMonitors = restoreMonitors ?? (() => counts.RestoreMonitorsCount++),
            PowerDownBaseStations = powerDownBaseStations ?? (token =>
            {
                counts.PowerDownBaseStationsCount++;
                return Task.CompletedTask;
            }),
            StopLovenseApps = stopLovenseApps ?? (token =>
            {
                counts.StopLovenseAppsCount++;
                return Task.CompletedTask;
            }),
            StopManagedApps = stopManagedApps ?? (token =>
            {
                counts.StopManagedAppsCount++;
                return Task.CompletedTask;
            }),
            SupersessionPollDelay = supersessionPollDelay ?? (token => Task.Delay(1, token)),
            WriteLifecycleEvent = writeLifecycleEvent ?? ((name, reason, result, fields) => { /* no-op */ }),
        };
    }

    private static SteamVrRuntimeSnapshot Runtime(int pid)
        => new(pid, Now.AddSeconds(pid));

    private static SteamVrRecoveryDecision CleanupDecision(SteamVrRecoveryClassification classification)
        => new(
            SteamVrRecoveryState.RecoveryPending,
            SteamVrRecoveryState.SessionEnding,
            classification,
            "test reason",
            SteamVrMonitorDisposition.RestoreIfOwned,
            LossDetected: true,
            DeferBaseStationShutdown: false,
            RunNormalCleanup: true,
            ReplacementAdopted: false,
            PreviousRuntime: null,
            CurrentRuntime: null,
            RecoveryDeadline: null,
            ConsecutiveReplacementAdoptions: 0,
            EvidenceMarker: null);

    private sealed class SideEffectCounts
    {
        public int RestoreMonitorsCount;
        public int PowerDownBaseStationsCount;
        public int StopLovenseAppsCount;
        public int StopManagedAppsCount;
    }
}
