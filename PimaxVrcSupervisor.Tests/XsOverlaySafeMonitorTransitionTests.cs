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
        Assert.Equal(0, fixture.Platform.StartCalls);
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
        Assert.Equal(1, fixture.Platform.StartCalls);
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
        Assert.Equal(1, fixture.Platform.StartCalls);
    }

    [Fact]
    public async Task OriginalExitsBeforeForce_DoesNotTargetReplacementOrRestartUnownedProcess()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.ExitOnGracefulRequestWithoutOwnership = true;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.False(result.XsOverlayStoppedBySupervisor);
        Assert.Equal(0, fixture.Platform.ForceCalls);
        Assert.Equal(0, fixture.Platform.StartCalls);
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
        Assert.Equal(0, fixture.Platform.StartCalls);
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
        Assert.Equal(0, fixture.Platform.StartCalls);
    }

    [Fact]
    public async Task MissingRestartInformation_DoesNotStopOrMutateMonitors()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.RestartInformationAvailable = false;

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
        Assert.Equal(1, fixture.Platform.StartCalls);
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
    public async Task RestartFailure_AttemptsOnlyOnceAndDoesNotLoop()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;
        fixture.Platform.StartProducesProcess = false;

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.Equal("restartFailed", result.Outcome);
        Assert.Equal(1, fixture.Platform.StartCalls);
        Assert.Contains(fixture.Events, item => item.EventType == "xsOverlayRestartFailed");
    }

    [Fact]
    public async Task IndependentlyReappearingInstance_AvoidsDuplicateLaunch()
    {
        var fixture = new Fixture(running: true);
        fixture.Platform.GracefulExit = true;
        fixture.OnMonitor = () => fixture.Platform.Running.Add(Fixture.Reappeared());

        var result = await fixture.Coordinator.RunAsync(true, CancellationToken.None);

        Assert.True(result.RestartSucceeded);
        Assert.Equal(0, fixture.Platform.StartCalls);
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
        Assert.Equal(0, fixture.Platform.StartCalls);
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
        Assert.Equal(1, fixture.Platform.StartCalls);
    }

    private sealed class Fixture
    {
        public const int OriginalPid = 100;
        public const int RestartedPid = 200;
        public const int ReappearedPid = 300;

        public Fixture(bool running = false)
        {
            Platform = new FakePlatform();
            if (running)
            {
                Platform.Running.Add(Original());
            }

            RebuildCoordinator();
        }

        public FakePlatform Platform { get; }
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
        public int StartCalls { get; private set; }
        public bool RestartInformationAvailable { get; set; } = true;
        public bool GracefulRequestAccepted { get; set; } = true;
        public bool GracefulExit { get; set; }
        public bool ExitOnGracefulRequestWithoutOwnership { get; set; }
        public bool ForceSucceeds { get; set; } = true;
        public bool StartProducesProcess { get; set; } = true;
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

        public XsOverlayRestartInformation? CaptureRestartInformation(XsOverlayProcessSnapshot process)
            => RestartInformationAvailable
                ? new(process.ExecutablePath!, @"C:\Steam\steamapps\common\XSOverlay", "capturedExecutablePath", null)
                : null;

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

        public Task<int?> StartAsync(XsOverlayRestartInformation restartInformation, CancellationToken cancellationToken)
        {
            StartCalls++;
            Order.Add("start");
            if (StartProducesProcess)
            {
                Running.Add(new(
                    Fixture.RestartedPid,
                    1,
                    "XSOverlay",
                    DateTime.UnixEpoch.AddSeconds(1),
                    restartInformation.ExecutablePath));
            }

            return Task.FromResult<int?>(Fixture.RestartedPid);
        }
    }
}
