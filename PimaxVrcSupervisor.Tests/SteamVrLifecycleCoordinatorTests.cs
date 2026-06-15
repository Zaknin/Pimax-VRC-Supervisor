using Xunit;

public sealed class SteamVrLifecycleCoordinatorTests
{
    [Fact]
    public void NonManagedSupervisorDoesNotClassifySteamVrDisappearance()
    {
        using var coordinator = new SteamVrLifecycleCoordinator(runtimeSessionOwned: false, supervisorPid: 10);
        coordinator.ObserveSnapshots([Process(100)], "test");

        var decision = coordinator.ObserveSnapshots([], "test");

        Assert.Equal(SteamVrTerminationClassification.None, decision.Classification);
        Assert.Equal(SteamVrCleanupPolicy.None, decision.CleanupPolicy);
        Assert.False(decision.ShowPersistentWarning);
        Assert.Equal(SteamVrSessionState.NotOwned, coordinator.State);
    }

    [Fact]
    public void OpenVrProbeDoesNotEstablishManagedSessionOwnership()
    {
        using var coordinator = new SteamVrLifecycleCoordinator(runtimeSessionOwned: true, supervisorPid: 10);
        using (coordinator.BeginOpenVrProbe())
        {
            coordinator.ObserveSnapshots([Process(100)], "probe");
        }

        var decision = coordinator.ObserveSnapshots([], "after-probe");

        Assert.Equal(SteamVrTerminationClassification.None, decision.Classification);
        Assert.Equal(SteamVrCleanupPolicy.None, decision.CleanupPolicy);
        Assert.Equal(SteamVrSessionState.WaitingForSession, coordinator.State);
    }

    [Fact]
    public void SupervisorRequestedShutdownUsesNormalCleanupDecision()
    {
        using var coordinator = new SteamVrLifecycleCoordinator(runtimeSessionOwned: true, supervisorPid: 10);
        coordinator.ObserveSnapshots([Process(100)], "running");
        coordinator.MarkSupervisorShutdownRequested("test");

        var decision = coordinator.ObserveSnapshots([], "shutdown");

        Assert.Equal(SteamVrTerminationClassification.SupervisorRequested, decision.Classification);
        Assert.Equal(SteamVrCleanupPolicy.NormalCleanup, decision.CleanupPolicy);
        Assert.False(decision.ShowPersistentWarning);
        Assert.Equal("test", decision.ShutdownIntentSource);
    }

    [Fact]
    public void ExternalDisappearanceUsesAmbiguousNormalCleanupWithoutPersistentWarning()
    {
        using var coordinator = new SteamVrLifecycleCoordinator(runtimeSessionOwned: true, supervisorPid: 10);
        coordinator.ObserveSnapshots([Process(100)], "running");

        var decision = coordinator.ObserveSnapshots([], "missing");

        Assert.Equal(SteamVrTerminationClassification.AmbiguousExternalExit, decision.Classification);
        Assert.Equal(SteamVrCleanupPolicy.NormalCleanup, decision.CleanupPolicy);
        Assert.False(decision.ShowPersistentWarning);
        Assert.Contains("without reliable abnormal termination evidence", decision.Reason);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, true)]
    [InlineData(55, true)]
    public void ExitCodeIsDiagnosticAndNotAuthoritative(int? exitCode, bool exitCodeAvailable)
    {
        using var coordinator = new SteamVrLifecycleCoordinator(runtimeSessionOwned: true, supervisorPid: 10);
        coordinator.ObserveSnapshots([Process(100, exitCodeAvailable: exitCodeAvailable, exitCode: exitCode)], "running");

        var decision = coordinator.ObserveSnapshots([], "missing");
        var diagnostic = Assert.Single(decision.ObservedProcesses);

        Assert.Equal(SteamVrTerminationClassification.AmbiguousExternalExit, decision.Classification);
        Assert.Equal(exitCodeAvailable, diagnostic.ExitCodeAvailable);
        Assert.Equal(exitCode, diagnostic.ExitCode);
    }

    [Fact]
    public void NewProcessAfterRunningSessionIsTrackedAsExternalAfterManagedStart()
    {
        using var coordinator = new SteamVrLifecycleCoordinator(runtimeSessionOwned: true, supervisorPid: 10);
        coordinator.ObserveSnapshots([Process(100)], "first");
        coordinator.ObserveSnapshots([Process(101)], "second");

        var origins = coordinator.SnapshotProcesses().Select(process => process.Origin).ToArray();

        Assert.Equal(
            [SteamVrProcessOrigin.ExistingAtManagedSessionStart, SteamVrProcessOrigin.ExternalAfterManagedSessionStart],
            origins);
    }

    [Fact]
    public void TerminationDecisionIsLatchedAndNotRepeatedForSameSession()
    {
        using var coordinator = new SteamVrLifecycleCoordinator(runtimeSessionOwned: true, supervisorPid: 10);
        coordinator.ObserveSnapshots([Process(100)], "running");

        var first = coordinator.ObserveSnapshots([], "missing");
        var second = coordinator.ObserveSnapshots([], "missing-again");

        Assert.Equal(SteamVrTerminationClassification.AmbiguousExternalExit, first.Classification);
        Assert.Equal(SteamVrTerminationClassification.None, second.Classification);
    }

    private static SteamVrProcessSnapshot Process(
        int pid,
        bool exitCodeAvailable = false,
        int? exitCode = null)
        => new(
            pid,
            "vrserver",
            DateTimeOffset.UtcNow.AddSeconds(-10),
            HasExited: false,
            ExitCodeAvailable: exitCodeAvailable,
            ExitCode: exitCode);
}
