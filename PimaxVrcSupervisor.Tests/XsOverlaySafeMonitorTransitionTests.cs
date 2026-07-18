using PimaxVrcSupervisor;
using Xunit;

public sealed class XsOverlaySafeMonitorTransitionTests
{
    [Fact]
    public async Task Disabled_DoesNotDetectOrMutateMonitors()
    {
        var fixture = new Fixture();

        var result = await fixture.Coordinator.RunAsync(false, CancellationToken.None);

        Assert.Equal("disabled", result.Outcome);
        Assert.Equal(0, fixture.Platform.FindCount);
        Assert.Equal(0, fixture.MonitorCalls);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task NotRunning_DisablesMonitorsWithoutStopOrRestart()
    {
        var fixture = new Fixture();

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.True(result.MonitorShutdownSucceeded);
        Assert.Equal(1, fixture.MonitorCalls);
        Assert.Equal(0, fixture.Platform.GracefulCalls);
        Assert.Equal(0, fixture.Platform.ForceCalls);
        Assert.Equal(0, fixture.Launcher.RequestCalls);
    }

    [Fact]
    public async Task RunningWithRestartInformation_StopsTransitionsSettlesAndRestartsOnce()
    {
        var fixture = new Fixture(running: true) { TopologyDelayObserved = false };
        fixture.Platform.GracefulExit = true;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.True(result.XsOverlayStoppedBySupervisor);
        Assert.True(result.MonitorShutdownSucceeded);
        Assert.True(result.RestartSucceeded);
        Assert.Equal(1, fixture.Platform.GracefulCalls);
        Assert.Equal(0, fixture.Platform.ForceCalls);
        Assert.Equal(1, fixture.Launcher.RequestCalls);
        Assert.True(fixture.TopologyDelayObserved);
        Assert.Equal(
            ["graceful", "monitor", "settle", "start"],
            fixture.Order);
    }

    [Fact]
    public async Task GracefulStopSuccess_DoesNotForceTerminate()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;

        await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.Equal(1, fixture.Platform.GracefulCalls);
        Assert.Equal(0, fixture.Platform.ForceCalls);
    }

    [Fact]
    public async Task GracefulStopUnavailable_ForcesOnlyOriginalPid()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = false;
        fixture.Platform.GracefulRequestAccepted = false;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.True(result.XsOverlayStoppedBySupervisor);
        Assert.Equal([Fixture.OriginalPid], fixture.Platform.ForcedPids);
        Assert.Equal(1, fixture.Launcher.RequestCalls);
    }

    [Fact]
    public async Task OriginalExitsBeforeForce_DoesNotTargetReplacementOrRestartUnownedProcess()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.ExitOnGracefulRequestWithoutOwnership = true;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.False(result.XsOverlayStoppedBySupervisor);
        Assert.Equal(0, fixture.Platform.ForceCalls);
        Assert.Equal(0, fixture.Launcher.RequestCalls);
        Assert.Equal(1, fixture.MonitorCalls);
    }

    [Fact]
    public async Task ReusedPidWithDifferentStartIdentity_IsNeverStoppedOrClaimed()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.ReplaceOriginalAfterDetection = true;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.False(result.XsOverlayStoppedBySupervisor);
        Assert.Equal(0, fixture.Platform.GracefulCalls);
        Assert.Equal(0, fixture.Platform.ForceCalls);
        Assert.Equal(0, fixture.Launcher.RequestCalls);
        Assert.Contains(fixture.Platform.Running, item =>
            item.ProcessId == Fixture.OriginalPid && item.StartTimeUtc != Fixture.Original().StartTimeUtc);
    }

    [Fact]
    public async Task StopFailure_SkipsMonitorShutdown()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.ForceSucceeds = false;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.Equal("stopFailed", result.Outcome);
        Assert.Equal(0, fixture.MonitorCalls);
        Assert.Equal(0, fixture.Launcher.RequestCalls);
    }

    [Fact]
    public async Task MissingRestartInformation_DoesNotStopOrMutateMonitors()
    {
        var fixture = new Fixture(running: true);
        fixture.Launcher.Target = new(
            XsOverlayLaunchRoute.Unavailable,
            null,
            null,
            null,
            false,
            "no registered launch route",
            false);

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.Equal("restartInformationUnavailable", result.Outcome);
        Assert.Equal(0, fixture.Platform.GracefulCalls);
        Assert.Equal(0, fixture.MonitorCalls);
    }

    [Fact]
    public async Task MonitorFailureAfterStop_StillRestartsOverlay()
    {
        var fixture = new Fixture(running: true) { MonitorException = new InvalidOperationException("display failure") };
        fixture.Platform.GracefulExit = true;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.False(result.MonitorShutdownSucceeded);
        Assert.True(result.RestartAttempted);
        Assert.True(result.RestartSucceeded);
        Assert.Equal(1, fixture.Launcher.RequestCalls);
    }

    [Fact]
    public async Task SettleCancellationAfterStop_PerformsBestEffortRestart()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;
        fixture.DelayOverride = (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(token);
        };
        fixture.RebuildCoordinator();

        var result = await fixture.Coordinator.RunAsync(true, cancellation.Token);

        Assert.Equal("cancelled", result.Outcome);
        Assert.True(result.RestartAttempted);
        Assert.True(result.RestartSucceeded);
    }

    [Fact]
    public async Task RestartSuccess_VerifiesExactlyOneProcess()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.True(result.RestartSucceeded);
        Assert.Equal(Fixture.RestartedPid, result.RestartedPid);
        Assert.Single(fixture.Platform.Running);
    }

    [Fact]
    public async Task VisibleUnwantedDesktopWindow_IsRecordedAsRegression()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;
        fixture.WindowObserver.Result = XsOverlayWindowVerificationResult.UnwantedDesktopWindowDetected;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.Equal("completedWithDesktopWindowRegression", result.Outcome);
        Assert.Equal(XsOverlayWindowVerificationResult.UnwantedDesktopWindowDetected, result.WindowVerificationResult);
        Assert.Contains(fixture.Events, item => item.EventType == "xsOverlayUnwantedDesktopWindowDetected");
    }

    [Fact]
    public async Task UnavailableWindowState_DoesNotGuessFailure()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;
        fixture.WindowObserver.Result = XsOverlayWindowVerificationResult.Unavailable;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.True(result.RestartSucceeded);
        Assert.Equal(XsOverlayWindowVerificationResult.Unavailable, result.WindowVerificationResult);
    }

    [Fact]
    public async Task RestartFailure_AttemptsOnlyOnceAndDoesNotLoop()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;
        fixture.Launcher.ProducesProcess = false;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.Equal("restartFailed", result.Outcome);
        Assert.Equal(1, fixture.Launcher.RequestCalls);
        Assert.Contains(fixture.Events, item => item.EventType == "xsOverlayRestartFailed");
    }

    [Fact]
    public async Task BrokeredRequestFailure_DoesNotRetryOrUseBareExecutable()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;
        fixture.Launcher.RequestSucceeds = false;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.Equal("restartFailed", result.Outcome);
        Assert.Equal(1, fixture.Launcher.RequestCalls);
        Assert.Contains(fixture.Events, item => item.EventType == "xsOverlayBrokeredRestartFailed");
    }

    [Fact]
    public async Task IndependentlyReappearingInstance_AvoidsDuplicateLaunch()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;
        fixture.OnMonitor = () => fixture.Platform.Running.Add(Fixture.Reappeared());

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.True(result.RestartSucceeded);
        Assert.Equal(0, fixture.Launcher.RequestCalls);
        Assert.Equal(Fixture.ReappearedPid, result.RestartedPid);
    }

    [Fact]
    public async Task ProcessAppearsWhenNotPreviouslyRunning_IsNotOwnedOrRestarted()
    {
        var fixture = new Fixture();
        fixture.OnMonitor = () => fixture.Platform.Running.Add(Fixture.Reappeared());

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.False(result.XsOverlayWasRunning);
        Assert.Equal(0, fixture.Platform.GracefulCalls);
        Assert.Equal(0, fixture.Platform.ForceCalls);
        Assert.Equal(0, fixture.Launcher.RequestCalls);
    }

    [Theory]
    [InlineData("XSOverlay", true)]
    [InlineData("xsoverlay", true)]
    [InlineData("XSOverlayHelper", false)]
    [InlineData("MyXSOverlay", false)]
    [InlineData("vrmonitor", false)]
    public void ExactProcessMatching_IgnoresSimilarAndSteamVrProcesses(string processName, bool expected)
        => Assert.Equal(expected, WindowsXsOverlayProcessPlatform.IsExactProcessName(processName));

    [Fact]
    public async Task CancellationAfterSupervisorStopBeforeMonitor_StillRestarts()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;
        fixture.Platform.AfterGracefulRequest = cancellation.Cancel;

        var result = await fixture.Coordinator.RunAsync(true, cancellation.Token);

        Assert.Equal("cancelled", result.Outcome);
        Assert.Equal(0, fixture.MonitorCalls);
        Assert.True(result.RestartSucceeded);
        Assert.Equal(1, fixture.Launcher.RequestCalls);
    }

    private sealed class Fixture
    {
        public const int OriginalPid = 100;
        public const int RestartedPid = 200;
        public const int ReappearedPid = 300;

        public Fixture(bool running = false)
        {
            Platform = new FakePlatform();
            Launcher = new FakeLauncher(Platform);
            if (running)
            {
                Platform.Running.Add(Original());
            }

            RebuildCoordinator();
        }

        public FakePlatform Platform { get; }
        public FakeLauncher Launcher { get; }
        public FakeWindowObserver WindowObserver { get; } = new();
        public List<XsOverlayMonitorTransitionEvent> Events { get; } = [];
        public List<string> Order { get; } = [];
        public XsOverlaySafeMonitorTransitionCoordinator Coordinator { get; private set; } = null!;
        public int MonitorCalls { get; private set; }
        public Exception? MonitorException { get; set; }
        public Action? OnMonitor { get; set; }
        public bool TopologyDelayObserved { get; set; }
        public Func<TimeSpan, CancellationToken, Task>? DelayOverride { get; set; }

        public void RebuildCoordinator()
        {
            Coordinator = new(
                Platform,
                Launcher,
                WindowObserver,
                _ =>
                {
                    MonitorCalls++;
                    Order.Add("monitor");
                    OnMonitor?.Invoke();
                    return MonitorException is null ? Task.CompletedTask : Task.FromException(MonitorException);
                },
                Events.Add,
                _ => { },
                DelayOverride ?? ((_, _) =>
                {
                    TopologyDelayObserved = true;
                    Order.Add("settle");
                    return Task.CompletedTask;
                }),
                gracefulStopTimeout: TimeSpan.FromMilliseconds(5),
                processPollInterval: TimeSpan.FromMilliseconds(1),
                topologySettleDuration: TimeSpan.FromMilliseconds(1),
                restartVerificationTimeout: TimeSpan.FromMilliseconds(5));
            Platform.Order = Order;
        }

        public static XsOverlayProcessSnapshot Original()
            => new(OriginalPid, 1, "XSOverlay", DateTime.UnixEpoch, @"C:\Steam\steamapps\common\XSOverlay\XSOverlay.exe");

        public static XsOverlayProcessSnapshot Reappeared()
            => new(ReappearedPid, 1, "XSOverlay", DateTime.UnixEpoch.AddSeconds(2), @"C:\Steam\steamapps\common\XSOverlay\XSOverlay.exe");
    }

    private sealed class FakePlatform : IXsOverlayProcessPlatform
    {
        public List<XsOverlayProcessSnapshot> Running { get; } = [];
        public List<int> ForcedPids { get; } = [];
        public List<string> Order { get; set; } = [];
        public int FindCount { get; private set; }
        public int GracefulCalls { get; private set; }
        public int ForceCalls { get; private set; }
        public bool GracefulRequestAccepted { get; set; } = true;
        public bool GracefulExit { get; set; }
        public bool ExitOnGracefulRequestWithoutOwnership { get; set; }
        public bool ForceSucceeds { get; set; } = true;
        public bool ReplaceOriginalAfterDetection { get; set; }
        public Action? AfterGracefulRequest { get; set; }

        public Task<IReadOnlyList<XsOverlayProcessSnapshot>> FindRunningAsync(CancellationToken cancellationToken)
        {
            FindCount++;
            var snapshot = Running.ToArray();
            if (FindCount == 1 && ReplaceOriginalAfterDetection && Running.Count == 1)
            {
                var original = Running[0];
                Running[0] = original with { StartTimeUtc = original.StartTimeUtc.AddMinutes(1) };
            }

            return Task.FromResult<IReadOnlyList<XsOverlayProcessSnapshot>>(snapshot);
        }

        public Task<bool> RequestGracefulCloseAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
        {
            GracefulCalls++;
            Order.Add("graceful");
            if (GracefulExit || ExitOnGracefulRequestWithoutOwnership)
            {
                Running.RemoveAll(item => item.ProcessId == process.ProcessId);
            }

            AfterGracefulRequest?.Invoke();
            return Task.FromResult(GracefulExit ? true : GracefulRequestAccepted && !ExitOnGracefulRequestWithoutOwnership);
        }

        public Task<bool> IsSameProcessRunningAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
            => Task.FromResult(Running.Any(item => item == process));

        public Task<bool> ForceTerminateAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
        {
            ForceCalls++;
            ForcedPids.Add(process.ProcessId);
            if (ForceSucceeds)
            {
                Running.RemoveAll(item => item == process);
            }

            return Task.FromResult(ForceSucceeds);
        }

    }

    private sealed class FakeLauncher(FakePlatform platform) : IXsOverlayLauncher
    {
        public int RequestCalls { get; private set; }
        public bool ProducesProcess { get; set; } = true;
        public bool RequestSucceeds { get; set; } = true;
        public XsOverlayLaunchTarget Target { get; set; } = new(
            XsOverlayLaunchRoute.SteamApp,
            "1173510",
            "C:\\Steam\\steamapps\\appmanifest_1173510.acf",
            @"C:\Steam\steamapps\common\XSOverlay\XSOverlay.exe",
            true,
            null,
            true);

        public XsOverlayLaunchTarget Discover(XsOverlayProcessSnapshot process) => Target;

        public Task<XsOverlayLaunchRequestResult> RequestLaunchAsync(XsOverlayLaunchTarget target, CancellationToken cancellationToken)
        {
            RequestCalls++;
            platform.Order.Add("start");
            if (!RequestSucceeds)
            {
                return Task.FromResult(new XsOverlayLaunchRequestResult(false, "fakeSteamUri", "request failed"));
            }

            if (ProducesProcess)
            {
                platform.Running.Add(new(
                    Fixture.RestartedPid,
                    1,
                    "XSOverlay",
                    DateTime.UnixEpoch.AddSeconds(1),
                    target.ExecutableIdentity));
            }

            return Task.FromResult(new XsOverlayLaunchRequestResult(true, "fakeSteamUri"));
        }
    }

    private sealed class FakeWindowObserver : IXsOverlayWindowObserver
    {
        public XsOverlayWindowVerificationResult Result { get; set; } = XsOverlayWindowVerificationResult.NoUnwantedDesktopWindow;
        public Task<XsOverlayWindowVerificationResult> ObserveAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
            => Task.FromResult(Result);
    }
}
