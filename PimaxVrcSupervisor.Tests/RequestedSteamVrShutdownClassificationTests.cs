using Xunit;

namespace PimaxVrcSupervisor.Tests;

public sealed class RequestedSteamVrShutdownClassificationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-19T18:00:00Z");
    private static readonly SteamVrRuntimeSnapshot OldRuntime = new(100, Start);
    private static readonly SteamVrRuntimeSnapshot ReplacementRuntime = new(200, Start.AddSeconds(5));

    [Fact]
    public void AcceptedRestartClassifiesCapturedOldRuntimeLossAsExpected()
    {
        var decision = ObserveInitialLoss(SteamVrLifecycleEvidence.None);

        var expected = SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: true,
            OldRuntime.Identity,
            decision);

        Assert.True(expected);
        Assert.Equal(SteamVrRecoveryClassification.AmbiguousLoss, decision.Classification);
        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, decision.MonitorDisposition);
        Assert.True(decision.DeferBaseStationShutdown);
        Assert.False(decision.RunNormalCleanup);
    }

    [Fact]
    public void FastLossBeforeDisplayedRestartStageStillUsesAcceptedIdentity()
    {
        var decision = ObserveInitialLoss(SteamVrLifecycleEvidence.None);

        Assert.True(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: true,
            OldRuntime.Identity,
            decision));
    }

    [Fact]
    public void ShutdownEvidenceDuringAcceptedRestartDoesNotForceFinalCleanupPresentation()
    {
        var decision = ObserveInitialLoss(new SteamVrLifecycleEvidence(
            ShutdownRequested: true,
            RestartRequested: false,
            Marker: "ShutdownRequested"));

        Assert.Equal(SteamVrRecoveryClassification.NormalExit, decision.Classification);
        Assert.True(decision.RunNormalCleanup);
        Assert.True(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: true,
            OldRuntime.Identity,
            decision));
    }

    [Fact]
    public void NoAcceptedRestartLeavesGenuineLossUnexpected()
    {
        var decision = ObserveInitialLoss(SteamVrLifecycleEvidence.None);

        Assert.False(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: false,
            OldRuntime.Identity,
            decision));
        Assert.Equal(SteamVrRecoveryClassification.AmbiguousLoss, decision.Classification);
    }

    [Fact]
    public void ClearedOrExpiredIntentDoesNotSuppressLaterUnexpectedExit()
    {
        var decision = ObserveInitialLoss(SteamVrLifecycleEvidence.None);

        Assert.False(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: false,
            expectedOldRuntime: null,
            decision));
    }

    [Fact]
    public void DifferentRuntimeLossIsNotAttributedToCompletedRestart()
    {
        var coordinator = CreateCoordinator();
        _ = coordinator.Observe([ReplacementRuntime], SteamVrLifecycleEvidence.None, Start);
        var decision = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));

        Assert.Equal(ReplacementRuntime.Identity, decision.PreviousRuntime);
        Assert.False(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: true,
            OldRuntime.Identity,
            decision));
    }

    [Fact]
    public void ReplacementDetectionIsNotClassifiedAsOldRuntimeExit()
    {
        var coordinator = CreateCoordinator();
        _ = coordinator.Observe([OldRuntime], SteamVrLifecycleEvidence.None, Start);
        _ = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));
        var replacement = coordinator.Observe([ReplacementRuntime], SteamVrLifecycleEvidence.None, Start.AddSeconds(2));

        Assert.True(replacement.ReplacementAdopted);
        Assert.False(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: true,
            OldRuntime.Identity,
            replacement));
    }

    [Fact]
    public void NormalFinalShutdownRemainsDistinctAndRunsCleanup()
    {
        var decision = ObserveInitialLoss(new SteamVrLifecycleEvidence(
            ShutdownRequested: true,
            RestartRequested: false,
            Marker: "ShutdownRequested"));

        Assert.False(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: false,
            OldRuntime.Identity,
            decision));
        Assert.Equal(SteamVrRecoveryClassification.NormalExit, decision.Classification);
        Assert.True(decision.RunNormalCleanup);
        Assert.Equal(SteamVrMonitorDisposition.RestoreIfOwned, decision.MonitorDisposition);
        Assert.False(decision.DeferBaseStationShutdown);
    }

    [Fact]
    public void RequestedReplacementIsPreservedButLaterNormalExitRunsFinalCleanup()
    {
        var coordinator = CreateCoordinator();
        _ = coordinator.Observe([OldRuntime], SteamVrLifecycleEvidence.None, Start);
        var requestedLoss = coordinator.Observe([], SteamVrLifecycleEvidence.None, Start.AddSeconds(1));

        Assert.True(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: true,
            OldRuntime.Identity,
            requestedLoss));
        Assert.False(requestedLoss.RunNormalCleanup);
        Assert.True(requestedLoss.DeferBaseStationShutdown);
        Assert.Equal(SteamVrMonitorDisposition.PreserveCurrentState, requestedLoss.MonitorDisposition);

        coordinator.AdoptExplicitReplacement(ReplacementRuntime.Identity, Start.AddSeconds(5));
        var finalExit = coordinator.Observe(
            [],
            new SteamVrLifecycleEvidence(
                ShutdownRequested: true,
                RestartRequested: false,
                SteamVrLifecycleEvidenceReader.ShutdownRequestedMarker),
            Start.AddMinutes(1));

        Assert.False(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: false,
            OldRuntime.Identity,
            finalExit));
        Assert.Equal(SteamVrRecoveryClassification.NormalExit, finalExit.Classification);
        Assert.True(finalExit.RunNormalCleanup);
        Assert.False(finalExit.DeferBaseStationShutdown);
        Assert.Equal(SteamVrMonitorDisposition.RestoreIfOwned, finalExit.MonitorDisposition);
    }

    [Fact]
    public void ExplicitSupervisorShutdownTakesPrecedenceOverRequestedRestartClassification()
    {
        var decision = new SteamVrRecoveryDecision(
            SteamVrRecoveryState.Running,
            SteamVrRecoveryState.SessionEnding,
            SteamVrRecoveryClassification.SupervisorExit,
            "SteamVR disappeared after explicit Supervisor exit.",
            SteamVrMonitorDisposition.RestoreIfOwned,
            LossDetected: true,
            DeferBaseStationShutdown: false,
            RunNormalCleanup: true,
            ReplacementAdopted: false,
            PreviousRuntime: OldRuntime.Identity,
            CurrentRuntime: null,
            RecoveryDeadline: null,
            ConsecutiveReplacementAdoptions: 0,
            EvidenceMarker: null);

        Assert.False(SteamVrRequestedRestartExitClassifier.IsExpectedOldRuntimeExit(
            restartActive: true,
            OldRuntime.Identity,
            decision));
    }

    [Fact]
    public void ProgramChecksExpectedIdentityBeforeGenericUnexpectedPresentation()
    {
        var source = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var apply = Slice(
            source,
            "private async Task<bool> ApplySteamVrLifecycleDecisionAsync",
            "private void WriteSteamVrLifecycleDecision");

        var expectedCheck = apply.IndexOf(
            "IsExpectedSteamVrShutdownForRequestedRestart(decision)",
            StringComparison.Ordinal);
        var expectedMessage = apply.IndexOf(
            "Expected SteamVR shutdown detected for requested restart.",
            StringComparison.Ordinal);
        var genericUnexpected = apply.IndexOf(
            "SteamVR stopped unexpectedly.",
            StringComparison.Ordinal);

        Assert.True(expectedCheck >= 0);
        Assert.True(expectedMessage > expectedCheck);
        Assert.True(genericUnexpected > expectedMessage);
        Assert.Contains("return false;", apply[expectedCheck..genericUnexpected], StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedRestartCapturesIdentityBeforePublishingActiveIntentAndClearsBothInFinally()
    {
        var source = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var acceptance = Slice(
            source,
            "private SteamVrRestartRequestDecision TryAcceptVrSessionRestart",
            "private async Task RunVrSessionRestartOperationAsync");

        var captureIdentity = acceptance.IndexOf(
            "Volatile.Write(ref _vrSessionRestartExpectedOldRuntime, capturedRuntime.Identity)",
            StringComparison.Ordinal);
        var publishActive = acceptance.IndexOf(
            "Volatile.Write(ref _vrSessionRestartActive, 1)",
            StringComparison.Ordinal);
        var clearActive = acceptance.IndexOf(
            "Volatile.Write(ref _vrSessionRestartActive, 0)",
            publishActive,
            StringComparison.Ordinal);
        var clearIdentity = acceptance.IndexOf(
            "Volatile.Write(ref _vrSessionRestartExpectedOldRuntime, null)",
            clearActive,
            StringComparison.Ordinal);

        Assert.True(captureIdentity >= 0 && captureIdentity < publishActive);
        Assert.True(clearActive > publishActive && clearIdentity > clearActive);
    }

    [Fact]
    public void ExpectedLifecycleMessageIsInformationalConciseAndEmittedAtMostOnce()
    {
        var source = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var reporter = Slice(
            source,
            "private void ReportVrSessionRestartLifecycleOnce",
            "private async Task<SteamVrShutdownRequestResult> RequestSteamVrShutdownOnceAsync");

        Assert.Contains("Interlocked.CompareExchange", reporter, StringComparison.Ordinal);
        Assert.DoesNotContain("_operatorWarning", reporter, StringComparison.Ordinal);
        Assert.DoesNotContain("operationId", reporter, StringComparison.Ordinal);
        Assert.DoesNotContain("oldSteamVr", reporter, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedConsoleRetainsGenuineUnexpectedWarningAndNoClientSpecificOverride()
    {
        var program = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var tui = File.ReadAllText(SourcePath("PimaxVrcSupervisor.Tui", "src", "app.rs"));
        var overlay = File.ReadAllText(SourcePath("PimaxVrcSupervisor.SteamVrHost", "Program.cs"));

        Assert.Contains("SteamVR stopped unexpectedly.", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamVR stopped unexpectedly.", tui, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamVR stopped unexpectedly.", overlay, StringComparison.Ordinal);
    }

    private static SteamVrRecoveryDecision ObserveInitialLoss(SteamVrLifecycleEvidence evidence)
    {
        var coordinator = CreateCoordinator();
        _ = coordinator.Observe([OldRuntime], SteamVrLifecycleEvidence.None, Start);
        return coordinator.Observe([], evidence, Start.AddSeconds(1));
    }

    private static SteamVrRecoveryCoordinator CreateCoordinator()
        => new(
            managedSession: true,
            new SteamVrRecoveryTiming(
                FastReplacementWindow: TimeSpan.FromSeconds(3),
                AmbiguousRecoveryWindow: TimeSpan.FromSeconds(20),
                ReplacementGapWindow: TimeSpan.FromSeconds(5),
                StableReplacementThreshold: TimeSpan.FromSeconds(30),
                MaximumUnstableRecoveryPeriod: TimeSpan.FromSeconds(60),
                MaximumConsecutiveReplacementAdoptions: 3));

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string SourcePath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            Path.Combine(segments)));
}
