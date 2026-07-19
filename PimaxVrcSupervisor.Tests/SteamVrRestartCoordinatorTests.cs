using Xunit;

namespace PimaxVrcSupervisor.Tests;

public sealed class SteamVrRestartCoordinatorTests
{
    private static readonly SteamVrRuntimeSnapshot OldRuntime =
        new(100, DateTimeOffset.Parse("2026-07-19T18:00:00Z"));

    private static readonly SteamVrRuntimeSnapshot NewRuntime =
        new(200, DateTimeOffset.Parse("2026-07-19T18:01:00Z"));

    [Fact]
    public async Task RestartOrdersShutdownExitStartReplacementAndReadiness()
    {
        var runtime = new FakeRestartRuntime();

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.Succeeded, outcome.Kind);
        AssertOrdered(
            runtime.Events,
            "progress:Requesting SteamVR shutdown...",
            "shutdown",
            "progress:Waiting for the old SteamVR runtime to exit...",
            "wait-old",
            "progress:Starting SteamVR...",
            "start-steamvr",
            "progress:Waiting for the replacement SteamVR runtime...",
            "wait-replacement",
            "progress:Waiting for the replacement SteamVR runtime to become ready...",
            "wait-ready",
            "adopt-replacement");
    }

    [Fact]
    public async Task ShutdownFailureDoesNotStartSteamVr()
    {
        var runtime = new FakeRestartRuntime
        {
            ShutdownResult = SteamVrShutdownRequestResult.Failure("vrstartup.exe", "request failed")
        };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.ShutdownRequestFailed, outcome.Kind);
        Assert.DoesNotContain("start-steamvr", runtime.Events);
    }

    [Fact]
    public async Task ShutdownTimeoutDoesNotStartSteamVr()
    {
        var runtime = new FakeRestartRuntime
        {
            ShutdownResult = SteamVrShutdownRequestResult.Failure(
                "vrstartup.exe",
                "request timed out",
                timedOut: true)
        };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.ShutdownRequestFailed, outcome.Kind);
        Assert.DoesNotContain("start-steamvr", runtime.Events);
    }

    [Fact]
    public async Task OldRuntimeMustDisappearBeforeReplacementLaunch()
    {
        var runtime = new FakeRestartRuntime { OldRuntimeExited = false };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.OldRuntimeExitTimedOut, outcome.Kind);
        Assert.Equal("SteamVR did not shut down within the restart timeout.", outcome.Result);
        Assert.DoesNotContain("start-steamvr", runtime.Events);
    }

    [Fact]
    public async Task ReplacementMustHaveNewIdentity()
    {
        var runtime = new FakeRestartRuntime { Replacement = OldRuntime };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.ReplacementStartFailed, outcome.Kind);
        Assert.Null(outcome.ReplacementRuntime);
        Assert.DoesNotContain("wait-ready", runtime.Events);
    }

    [Fact]
    public async Task MissingReplacementPublishesStartFailure()
    {
        var runtime = new FakeRestartRuntime { Replacement = null };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.ReplacementStartFailed, outcome.Kind);
        Assert.True(outcome.OldRuntimeExited);
        Assert.Equal("SteamVR shut down, but the replacement runtime did not start.", outcome.Result);
    }

    [Fact]
    public async Task ReplacementReadinessIsRequired()
    {
        var runtime = new FakeRestartRuntime { ReplacementReady = false };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.ReplacementReadinessTimedOut, outcome.Kind);
        Assert.Equal(NewRuntime, outcome.ReplacementRuntime);
    }

    [Fact]
    public async Task RestartWithoutVrChatDoesNotLaunchOrRestoreVrChat()
    {
        var runtime = new FakeRestartRuntime();

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal("SteamVR restarted.", outcome.Result);
        Assert.DoesNotContain("start-vrchat", runtime.Events);
        Assert.DoesNotContain("restore-managed-apps", runtime.Events);
    }

    [Fact]
    public async Task RestartWithVrChatResumesAndRestoresManagedApps()
    {
        var runtime = new FakeRestartRuntime { VrChatRunning = false };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, true, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.SucceededWithVrChat, outcome.Kind);
        AssertOrdered(
            runtime.Events,
            "prepare-vrchat-resume",
            "is-vrchat-running",
            "start-vrchat",
            "wait-vrchat",
            "restore-managed-apps");
    }

    [Fact]
    public async Task VrChatAlreadyRunningIsNotLaunchedAgain()
    {
        var runtime = new FakeRestartRuntime { VrChatRunning = true };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, true, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.DoesNotContain("start-vrchat", runtime.Events);
        Assert.Contains("restore-managed-apps", runtime.Events);
    }

    [Fact]
    public async Task VrChatResumeFailureIsPartialAndLeavesReplacementAvailable()
    {
        var runtime = new FakeRestartRuntime
        {
            VrChatRunning = false,
            VrChatResumed = false
        };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, true, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.VrChatResumeFailed, outcome.Kind);
        Assert.True(outcome.IsWarning);
        Assert.Equal(NewRuntime, outcome.ReplacementRuntime);
        Assert.Equal("SteamVR restarted, but VRChat could not be resumed.", outcome.Result);
    }

    [Fact]
    public async Task CoordinatorCanRunASecondRestartAfterCompletion()
    {
        var runtime = new FakeRestartRuntime();
        var coordinator = CreateCoordinator(runtime);

        var first = await coordinator.RunAsync(OldRuntime, false, CancellationToken.None);
        var secondOld = NewRuntime;
        runtime.Replacement = new SteamVrRuntimeSnapshot(300, DateTimeOffset.Parse("2026-07-19T18:02:00Z"));
        var second = await coordinator.RunAsync(secondOld, false, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, runtime.Events.Count(entry => entry == "shutdown"));
        Assert.Equal(2, runtime.Events.Count(entry => entry == "start-steamvr"));
    }

    [Fact]
    public void AppSupervisorCapturesInputsBeforeStartingRestartTaskAndClearsIntentInFinally()
    {
        var source = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var acceptance = Slice(
            source,
            "private VrSessionRestartAcceptance TryAcceptVrSessionRestart",
            "private async Task RunVrSessionRestartOperationAsync");

        Assert.True(acceptance.IndexOf("oldRuntime = CaptureCurrentSteamVrRuntime()", StringComparison.Ordinal)
                    < acceptance.IndexOf("resumeVrChat = IsAnyProcessRunning", StringComparison.Ordinal));
        Assert.True(acceptance.IndexOf("resumeVrChat = IsAnyProcessRunning", StringComparison.Ordinal)
                    < acceptance.IndexOf("RunVrSessionRestartOperationAsync", StringComparison.Ordinal));
        Assert.Contains("Volatile.Write(ref _vrSessionRestartActive, 0)", acceptance, StringComparison.Ordinal);
        Assert.Contains("_vrSessionRestartLock.Release()", acceptance, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartWaitDoesNotAcceptAReplacementWhileOldIdentityStillRuns()
    {
        var source = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var wait = Slice(
            source,
            "private async Task<bool> WaitForSteamVrRuntimeToDisappearAsync",
            "private async Task<SteamVrRuntimeSnapshot?> WaitForReplacementSteamVrRuntimeAppearanceAsync");

        Assert.Contains("if (!IsSteamVrRuntimeRunning(oldRuntime))", wait, StringComparison.Ordinal);
        Assert.DoesNotContain("current.Identity != oldRuntime", wait, StringComparison.Ordinal);
        Assert.DoesNotContain("Waiting for old SteamVR runtime to close", wait, StringComparison.Ordinal);
    }

    private static SteamVrRestartCoordinator CreateCoordinator(FakeRestartRuntime runtime)
        => new(
            runtime,
            new SteamVrRestartTimeouts(
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(40)));

    private static void AssertOrdered(IReadOnlyList<string> values, params string[] expected)
    {
        var previous = -1;
        foreach (var item in expected)
        {
            var index = values.ToList().IndexOf(item);
            Assert.True(index > previous, $"Expected '{item}' after index {previous}. Events: {string.Join(", ", values)}");
            previous = index;
        }
    }

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

    private sealed class FakeRestartRuntime : ISteamVrRestartRuntime
    {
        public List<string> Events { get; } = [];
        public SteamVrShutdownRequestResult ShutdownResult { get; set; } =
            SteamVrShutdownRequestResult.Success("vrstartup.exe", 0);
        public bool OldRuntimeExited { get; set; } = true;
        public SteamVrRuntimeSnapshot? Replacement { get; set; } = NewRuntime;
        public bool ReplacementReady { get; set; } = true;
        public bool VrChatRunning { get; set; }
        public bool VrChatResumed { get; set; } = true;

        public Task<SteamVrShutdownRequestResult> RequestGracefulShutdownAsync(CancellationToken cancellationToken)
        {
            Events.Add("shutdown");
            return Task.FromResult(ShutdownResult);
        }

        public Task<bool> WaitForOldRuntimeExitAsync(
            SteamVrRuntimeIdentity oldRuntime,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Events.Add("wait-old");
            return Task.FromResult(OldRuntimeExited);
        }

        public void StartSteamVr() => Events.Add("start-steamvr");

        public Task<SteamVrRuntimeSnapshot?> WaitForReplacementRuntimeAsync(
            SteamVrRuntimeIdentity oldRuntime,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Events.Add("wait-replacement");
            return Task.FromResult(Replacement);
        }

        public Task<bool> WaitForReplacementReadinessAsync(
            SteamVrRuntimeIdentity replacementRuntime,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Events.Add("wait-ready");
            return Task.FromResult(ReplacementReady);
        }

        public void AdoptReplacementRuntime(SteamVrRuntimeSnapshot replacementRuntime)
            => Events.Add("adopt-replacement");

        public Task PrepareForVrChatResumeAsync(CancellationToken cancellationToken)
        {
            Events.Add("prepare-vrchat-resume");
            return Task.CompletedTask;
        }

        public bool IsVrChatRunning()
        {
            Events.Add("is-vrchat-running");
            return VrChatRunning;
        }

        public void StartVrChat() => Events.Add("start-vrchat");

        public Task<bool> WaitForVrChatAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Events.Add("wait-vrchat");
            return Task.FromResult(VrChatResumed);
        }

        public Task RestoreManagedAppsAsync(CancellationToken cancellationToken)
        {
            Events.Add("restore-managed-apps");
            return Task.CompletedTask;
        }

        public void ReportProgress(string progress) => Events.Add("progress:" + progress);

        public void WriteDiagnostic(string message) => Events.Add("log:" + message);
    }
}
