using Xunit;

public sealed class SteamVrRecoveryCoordinatorTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-19T19:00:00Z");

    [Fact]
    public void AmbiguousLoss_PreservesMonitorStateDuringFastReplacementWindow()
    {
        var coordinator = CreateRunningCoordinator();

        var first = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));
        var duplicate = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(2));

        Assert.Equal(SteamVrRecoveryClassification.AmbiguousLoss, first.Classification);
        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, first.MonitorDisposition);
        Assert.True(first.DeferBaseStationShutdown);
        Assert.False(first.RunNormalCleanup);
        Assert.Equal(SteamVrRecoveryClassification.None, duplicate.Classification);
        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, duplicate.MonitorDisposition);
    }

    [Fact]
    public void ShutdownRequestedWithoutRestart_ClassifiesNormalExitPromptly()
    {
        var coordinator = CreateRunningCoordinator();

        var decision = coordinator.Observe([], new SteamVrLifecycleEvidence(true, false, SteamVrLifecycleEvidenceReader.ShutdownRequestedMarker), Start.AddSeconds(1));

        Assert.Equal(SteamVrRecoveryClassification.NormalExit, decision.Classification);
        Assert.True(decision.RunNormalCleanup);
        Assert.False(decision.DeferBaseStationShutdown);
        Assert.Equal(SteamVrMonitorDisposition.RestoreIfOwned, decision.MonitorDisposition);
    }

    [Fact]
    public void ExplicitReplacementRearmsLaterNormalExitAfterGracefulShutdownMarker()
    {
        var coordinator = CreateRunningCoordinator();
        var firstExit = coordinator.Observe(
            [],
            new SteamVrLifecycleEvidence(true, false, SteamVrLifecycleEvidenceReader.ShutdownRequestedMarker),
            Start.AddSeconds(1));
        Assert.Equal(SteamVrRecoveryState.SessionEnding, firstExit.StateAfter);

        coordinator.AdoptExplicitReplacement(Runtime(200).Identity, Start.AddSeconds(2));
        var laterExit = coordinator.Observe(
            [],
            new SteamVrLifecycleEvidence(true, false, SteamVrLifecycleEvidenceReader.ShutdownRequestedMarker),
            Start.AddMinutes(1));

        Assert.Equal(SteamVrRecoveryClassification.NormalExit, laterExit.Classification);
        Assert.True(laterExit.RunNormalCleanup);
        Assert.Equal(SteamVrMonitorDisposition.RestoreIfOwned, laterExit.MonitorDisposition);
    }

    [Fact]
    public void ConflictingShutdownAndRestart_PreservesStations()
    {
        var coordinator = CreateRunningCoordinator();

        var decision = coordinator.Observe([], new SteamVrLifecycleEvidence(true, true, SteamVrLifecycleEvidenceReader.RestartStateMarker), Start.AddSeconds(1));

        Assert.Equal(SteamVrRecoveryClassification.RestartEvidence, decision.Classification);
        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, decision.MonitorDisposition);
        Assert.True(decision.DeferBaseStationShutdown);
        Assert.False(decision.RunNormalCleanup);
    }

    [Theory]
    [InlineData(SteamVrLifecycleEvidenceReader.RestartStateMarker)]
    [InlineData(SteamVrLifecycleEvidenceReader.RestartHmdMarker)]
    [InlineData(SteamVrLifecycleEvidenceReader.RestartSystemMarker)]
    [InlineData(SteamVrLifecycleEvidenceReader.RestartStartupReasonMarker)]
    public void RestartMarkers_StartFastRecovery(string marker)
    {
        var coordinator = CreateRunningCoordinator();

        var decision = coordinator.Observe([], new SteamVrLifecycleEvidence(false, true, marker), Start.AddSeconds(1));

        Assert.Equal(SteamVrRecoveryClassification.RestartEvidence, decision.Classification);
        Assert.Equal(Start.AddSeconds(4), decision.RecoveryDeadline);
        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, decision.MonitorDisposition);
    }

    [Fact]
    public void ReplacementDuringRecovery_IsAdoptedWithoutCleanup()
    {
        var coordinator = CreateRunningCoordinator();
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));

        var decision = coordinator.Observe([Runtime(200)], SteamVrLifecycleEvidence.None, Start.AddSeconds(2));

        Assert.Equal(SteamVrRecoveryClassification.ReplacementRuntime, decision.Classification);
        Assert.True(decision.ReplacementAdopted);
        Assert.True(decision.DeferBaseStationShutdown);
        Assert.False(decision.RunNormalCleanup);
        Assert.Equal(200, decision.CurrentRuntime!.Pid);
    }

    [Fact]
    public void SamePidWithNewStartTime_IsReplacementIdentity()
    {
        var coordinator = new SteamVrRecoveryCoordinator(true);
        _ = coordinator.Observe([Runtime(100, Start)], SteamVrLifecycleEvidence.None, Start);
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));

        var decision = coordinator.Observe([Runtime(100, Start.AddMinutes(1))], SteamVrLifecycleEvidence.None, Start.AddSeconds(2));

        Assert.True(decision.ReplacementAdopted);
        Assert.NotEqual(decision.PreviousRuntime, decision.CurrentRuntime);
    }

    [Fact]
    public void ReplacementLoss_UsesShortChainedGap_AndAdoptsSecondReplacement()
    {
        var coordinator = CreateRunningCoordinator();
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));
        _ = coordinator.Observe([Runtime(200)], SteamVrLifecycleEvidence.None, Start.AddSeconds(2));

        var loss = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(3));
        var replacement = coordinator.Observe([Runtime(300)], SteamVrLifecycleEvidence.None, Start.AddSeconds(4));

        Assert.Equal(SteamVrRecoveryClassification.AmbiguousLoss, loss.Classification);
        Assert.Equal(Start.AddSeconds(8), loss.RecoveryDeadline);
        Assert.Equal(SteamVrRecoveryClassification.ChainedReplacement, replacement.Classification);
        Assert.Equal(2, replacement.ConsecutiveReplacementAdoptions);
    }

    [Fact]
    public void StableReplacement_ClearsConsecutiveRecoveryHistory()
    {
        var coordinator = CreateRunningCoordinator();
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));
        _ = coordinator.Observe([Runtime(200)], SteamVrLifecycleEvidence.None, Start.AddSeconds(2));

        var stable = coordinator.Observe([Runtime(200)], SteamVrLifecycleEvidence.None, Start.AddSeconds(32));

        Assert.Equal(SteamVrRecoveryState.Running, stable.StateAfter);
        Assert.Equal(0, stable.ConsecutiveReplacementAdoptions);
    }

    [Fact]
    public void AmbiguousLoss_TimesOutAtConfiguredWindow()
    {
        var timing = SteamVrRecoveryTiming.Default with { AmbiguousRecoveryWindow = TimeSpan.FromSeconds(20) };
        var coordinator = new SteamVrRecoveryCoordinator(true, timing);
        _ = coordinator.Observe([Runtime(100)], SteamVrLifecycleEvidence.None, Start);
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));

        var restore = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(4));
        var duplicateRestorePoll = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(5));
        var waiting = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(20));
        var timeout = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(21));

        Assert.Equal(SteamVrMonitorDisposition.RestoreIfOwned, restore.MonitorDisposition);
        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, duplicateRestorePoll.MonitorDisposition);
        Assert.False(waiting.RunNormalCleanup);
        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, waiting.MonitorDisposition);
        Assert.Equal(SteamVrRecoveryClassification.RecoveryTimedOut, timeout.Classification);
        Assert.True(timeout.RunNormalCleanup);
        Assert.Equal(SteamVrMonitorDisposition.RestoreAlreadyAttempted, timeout.MonitorDisposition);
    }

    [Fact]
    public void RapidReplacement_AdoptsWithoutRestoringOrChangingMonitorState()
    {
        var coordinator = CreateRunningCoordinator();
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));

        var decision = coordinator.Observe([Runtime(200)], SteamVrLifecycleEvidence.None, Start.AddSeconds(3.9));

        Assert.True(decision.ReplacementAdopted);
        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, decision.MonitorDisposition);
    }

    [Fact]
    public void LateReplacement_AfterAmbiguousRestore_DoesNotRequestAnotherMonitorChange()
    {
        var coordinator = CreateRunningCoordinator();
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(4));

        var decision = coordinator.Observe([Runtime(200)], SteamVrLifecycleEvidence.None, Start.AddSeconds(5));

        Assert.True(decision.ReplacementAdopted);
        Assert.Equal(SteamVrMonitorDisposition.RestoreAlreadyAttempted, decision.MonitorDisposition);
    }

    [Fact]
    public void ChainedRestart_BeforeAmbiguousRestore_PreservesDisabledMonitorState()
    {
        var coordinator = CreateRunningCoordinator();
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));
        _ = coordinator.Observe([Runtime(200)], SteamVrLifecycleEvidence.None, Start.AddSeconds(2));

        var chainedLoss = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(3));

        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, chainedLoss.MonitorDisposition);
        Assert.Equal(Start.AddSeconds(8), chainedLoss.RecoveryDeadline);
    }

    [Fact]
    public void ExplicitSupervisorExit_BypassesRecoveryHold()
    {
        var coordinator = CreateRunningCoordinator();
        coordinator.MarkSupervisorExitRequested();

        var decision = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));

        Assert.False(decision.DeferBaseStationShutdown);
        Assert.False(decision.RunNormalCleanup); // Existing explicit exit path owns cleanup.
        Assert.Equal(SteamVrRecoveryState.SessionEnding, decision.StateAfter);
    }

    [Fact]
    public void WatcherDoesNotClaimReplacementWhileSupervisorIsActive()
    {
        var oldSession = new SteamVrSessionIdentity(100, Start);
        var replacement = new SteamVrSessionIdentity(200, Start.AddSeconds(2));

        var decision = AutoLaunchWatcher.GetLaunchDecision(replacement, supervisorRunning: true, launchedForSteamVrSession: oldSession);

        Assert.False(decision.ShouldLaunchSupervisor);
        Assert.Equal(oldSession, decision.LaunchedForSteamVrSession);
    }

    [Fact]
    public void EvidenceReader_IgnoresStaleMarkerAndRetainsCurrentMarkerUntilReplacementBaseline()
    {
        var root = Path.Combine(Path.GetTempPath(), "PimaxVrcSupervisorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var log = Path.Combine(root, "vrmonitor.txt");
        try
        {
            File.WriteAllText(log, SteamVrLifecycleEvidenceReader.ShutdownRequestedMarker);
            var reader = new SteamVrLifecycleEvidenceReader([log]);
            reader.EstablishBaseline();
            Assert.Equal(SteamVrLifecycleEvidence.None, reader.ReadCurrentSessionEvidence());

            File.AppendAllText(log, Environment.NewLine + SteamVrLifecycleEvidenceReader.RestartStateMarker);
            var current = reader.ReadCurrentSessionEvidence();
            Assert.True(current.RestartRequested);
            Assert.True(reader.ReadCurrentSessionEvidence().RestartRequested);

            reader.EstablishBaseline();
            Assert.Equal(SteamVrLifecycleEvidence.None, reader.ReadCurrentSessionEvidence());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EvidenceReader_HandlesTruncationAndMissingLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "PimaxVrcSupervisorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var log = Path.Combine(root, "vrmonitor.txt");
        try
        {
            var reader = new SteamVrLifecycleEvidenceReader([log]);
            reader.EstablishBaseline();
            Assert.Equal(SteamVrLifecycleEvidence.None, reader.ReadCurrentSessionEvidence());

            File.WriteAllText(log, SteamVrLifecycleEvidenceReader.ShutdownRequestedMarker);
            Assert.True(reader.ReadCurrentSessionEvidence().ShutdownRequested);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EvidenceReader_HandlesRotationByReadingNewShorterFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "PimaxVrcSupervisorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var log = Path.Combine(root, "vrmonitor.txt");
        try
        {
            File.WriteAllText(log, new string('x', 512));
            var reader = new SteamVrLifecycleEvidenceReader([log]);
            reader.EstablishBaseline();

            File.WriteAllText(log, SteamVrLifecycleEvidenceReader.RestartSystemMarker);

            var evidence = reader.ReadCurrentSessionEvidence();
            Assert.True(evidence.RestartRequested);
            Assert.Equal(SteamVrLifecycleEvidenceReader.RestartSystemMarker, evidence.Marker);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SteamVrRecoveryCoordinator CreateRunningCoordinator()
    {
        var coordinator = new SteamVrRecoveryCoordinator(true);
        _ = coordinator.Observe([Runtime(100)], SteamVrLifecycleEvidence.None, Start);
        return coordinator;
    }

    private static SteamVrRuntimeSnapshot Runtime(int pid, DateTimeOffset? startTime = null)
        => new(pid, startTime ?? Start.AddSeconds(pid));
}
