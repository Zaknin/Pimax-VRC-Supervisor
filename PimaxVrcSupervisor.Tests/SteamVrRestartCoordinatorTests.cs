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
            "progress:Waiting for SteamVR to close...",
            "wait-old",
            "capture-replacement",
            "progress:Starting SteamVR...",
            "start-steamvr",
            "progress:Waiting for SteamVR...",
            "wait-replacement",
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
    public async Task PendingHelperCompletionDoesNotBlockRuntimeDrivenRestart()
    {
        var helperCompletion = new TaskCompletionSource<SteamVrShutdownHelperDiagnostics>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRestartRuntime
        {
            ShutdownResult = SteamVrShutdownRequestResult.Issued("vrstartup.exe", helperCompletion.Task)
        };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.Succeeded, outcome.Kind);
        Assert.False(helperCompletion.Task.IsCompleted);
        Assert.Contains("wait-old", runtime.Events);
        Assert.Contains("start-steamvr", runtime.Events);
    }

    [Fact]
    public async Task UndocumentedHelperExitDoesNotOverrideOldRuntimeObservation()
    {
        var helperCompletion = new TaskCompletionSource<SteamVrShutdownHelperDiagnostics>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRestartRuntime
        {
            ShutdownResult = SteamVrShutdownRequestResult.Issued("vrstartup.exe", helperCompletion.Task)
        };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);
        helperCompletion.SetResult(new SteamVrShutdownHelperDiagnostics(-1, null));
        await WaitForEventAsync(runtime, "exitCode=-1");

        Assert.Equal(SteamVrRestartOutcomeKind.Succeeded, outcome.Kind);
        Assert.Contains(runtime.Events, entry => entry.Contains("authoritativeCompletion=old-runtime-observation", StringComparison.Ordinal));
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
    public async Task ExistingReplacementIsAdoptedWithoutDuplicateLaunch()
    {
        var runtime = new FakeRestartRuntime { ExistingReplacement = NewRuntime };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.Succeeded, outcome.Kind);
        Assert.DoesNotContain("start-steamvr", runtime.Events);
        Assert.DoesNotContain("wait-replacement", runtime.Events);
        Assert.Contains("adopt-replacement", runtime.Events);
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
        Assert.Equal("SteamVR shut down, but could not be started again.", outcome.Result);
    }

    [Fact]
    public async Task ReplacementLaunchFailureLeavesOldRuntimeExitedAndPublishesRecoveryResult()
    {
        var runtime = new FakeRestartRuntime
        {
            StartException = new InvalidOperationException("Steam URI launch failed")
        };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.ReplacementStartFailed, outcome.Kind);
        Assert.True(outcome.OldRuntimeExited);
        Assert.Null(outcome.ReplacementRuntime);
        Assert.Equal("SteamVR shut down, but could not be started again.", outcome.Result);
        Assert.DoesNotContain("wait-replacement", runtime.Events);
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
    public async Task SamePidWithDifferentStartTimeQualifiesAsReplacement()
    {
        var samePidNewStart = new SteamVrRuntimeSnapshot(
            OldRuntime.Pid,
            OldRuntime.StartTime!.Value.AddMinutes(1));
        var runtime = new FakeRestartRuntime { Replacement = samePidNewStart };

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, false, CancellationToken.None);

        Assert.Equal(SteamVrRestartOutcomeKind.Succeeded, outcome.Kind);
        Assert.Equal(samePidNewStart, outcome.ReplacementRuntime);
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
    public async Task CoordinatorEmitsEachWaitingDiagnosticOnlyOnce()
    {
        var runtime = new FakeRestartRuntime();

        var outcome = await CreateCoordinator(runtime).RunAsync(OldRuntime, true, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(
            1,
            runtime.Events.Count(entry => entry.Contains(
                "waiting for old SteamVR runtime to exit",
                StringComparison.Ordinal)));
        Assert.Equal(1, runtime.Events.Count(entry => entry == "progress:Waiting for SteamVR..."));
    }

    [Fact]
    public void AppSupervisorCapturesInputsBeforeStartingRestartTaskAndClearsIntentInFinally()
    {
        var source = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var acceptance = Slice(
            source,
            "private SteamVrRestartRequestDecision TryAcceptVrSessionRestart",
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

        var immediateCheck = wait.IndexOf("if (!IsSteamVrRuntimeRunning(oldRuntime))", StringComparison.Ordinal);
        var lifecycleObservation = wait.IndexOf("ObserveSteamVrLifecycle", StringComparison.Ordinal);
        Assert.True(immediateCheck >= 0 && immediateCheck < lifecycleObservation);
        Assert.DoesNotContain("current.Identity != oldRuntime", wait, StringComparison.Ordinal);
        Assert.DoesNotContain("Waiting for old SteamVR runtime to close", wait, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartUsesSupervisorLifetimeRatherThanClientConnectionLifetime()
    {
        var source = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var acceptance = Slice(
            source,
            "private SteamVrRestartRequestDecision TryAcceptVrSessionRestart",
            "private async Task RunVrSessionRestartOperationAsync");

        Assert.Contains(
            "RunVrSessionRestartOperationAsync(operationId, capturedRuntime, resumeVrChat, _shutdown.Token)",
            acceptance,
            StringComparison.Ordinal);
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

    private static async Task WaitForEventAsync(FakeRestartRuntime runtime, string fragment)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (runtime.Events.Any(entry => entry.Contains(fragment, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Yield();
        }

        Assert.Fail($"Expected an event containing '{fragment}'. Events: {string.Join(", ", runtime.Events)}");
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
        private readonly object _eventsLock = new();
        private readonly List<string> _events = [];

        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_eventsLock)
                {
                    return [.. _events];
                }
            }
        }

        public SteamVrShutdownRequestResult ShutdownResult { get; set; } =
            SteamVrShutdownRequestResult.Issued(
                "vrstartup.exe",
                Task.FromResult(new SteamVrShutdownHelperDiagnostics(0, null)));
        public bool OldRuntimeExited { get; set; } = true;
        public SteamVrRuntimeSnapshot? ExistingReplacement { get; set; }
        public SteamVrRuntimeSnapshot? Replacement { get; set; } = NewRuntime;
        public bool ReplacementReady { get; set; } = true;
        public bool VrChatRunning { get; set; }
        public bool VrChatResumed { get; set; } = true;
        public Exception? StartException { get; set; }

        public Task<SteamVrShutdownRequestResult> RequestGracefulShutdownAsync(CancellationToken cancellationToken)
        {
            AddEvent("shutdown");
            return Task.FromResult(ShutdownResult);
        }

        public Task<bool> WaitForOldRuntimeExitAsync(
            SteamVrRuntimeIdentity oldRuntime,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            AddEvent("wait-old");
            return Task.FromResult(OldRuntimeExited);
        }

        public SteamVrRuntimeSnapshot? CaptureReplacementRuntime(SteamVrRuntimeIdentity oldRuntime)
        {
            AddEvent("capture-replacement");
            return ExistingReplacement;
        }

        public void StartSteamVr()
        {
            AddEvent("start-steamvr");
            if (StartException is not null)
            {
                throw StartException;
            }
        }

        public Task<SteamVrRuntimeSnapshot?> WaitForReplacementRuntimeAsync(
            SteamVrRuntimeIdentity oldRuntime,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            AddEvent("wait-replacement");
            return Task.FromResult(Replacement);
        }

        public Task<bool> WaitForReplacementReadinessAsync(
            SteamVrRuntimeIdentity replacementRuntime,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            AddEvent("wait-ready");
            return Task.FromResult(ReplacementReady);
        }

        public void AdoptReplacementRuntime(SteamVrRuntimeSnapshot replacementRuntime)
            => AddEvent("adopt-replacement");

        public Task PrepareForVrChatResumeAsync(CancellationToken cancellationToken)
        {
            AddEvent("prepare-vrchat-resume");
            return Task.CompletedTask;
        }

        public bool IsVrChatRunning()
        {
            AddEvent("is-vrchat-running");
            return VrChatRunning;
        }

        public void StartVrChat() => AddEvent("start-vrchat");

        public Task<bool> WaitForVrChatAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            AddEvent("wait-vrchat");
            return Task.FromResult(VrChatResumed);
        }

        public Task RestoreManagedAppsAsync(CancellationToken cancellationToken)
        {
            AddEvent("restore-managed-apps");
            return Task.CompletedTask;
        }

        public void ReportProgress(string progress) => AddEvent("progress:" + progress);

        public void WriteDiagnostic(string message) => AddEvent("log:" + message);

        private void AddEvent(string value)
        {
            lock (_eventsLock)
            {
                _events.Add(value);
            }
        }
    }
}
