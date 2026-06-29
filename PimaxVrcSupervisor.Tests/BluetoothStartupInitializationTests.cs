using System.Text.Json;
using PimaxVrcSupervisor.BaseStations;
using Xunit;

public sealed class BluetoothStartupInitializationTests
{
    [Fact]
    public async Task WatcherStartupRunsInitializationOnceBeforeTheWatchLoop()
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([Station()]);
        var initializer = CreateInitializer(temp, scanner);
        var loopIterations = 0;

        await AutoLaunchWatcher.RunStartupAndWatchLoopAsync(
            RelevantConfig(),
            initializer,
            _ =>
            {
                Assert.Equal(1, scanner.Calls);
                for (var index = 0; index < 3; index++)
                {
                    loopIterations++;
                }

                return Task.CompletedTask;
            },
            CancellationToken.None);
        await initializer.RunOnceAsync(RelevantConfig(), CancellationToken.None);

        Assert.Equal(3, loopIterations);
        Assert.Equal(1, scanner.Calls);
        var events = ReadEvents(temp);
        Assert.Single(events, element => EventType(element) == "start");
        Assert.Single(events.Select(element => element.GetProperty("operationId").GetString()).Distinct());
    }

    [Theory]
    [InlineData(false, true, "baseStationsDisabled")]
    [InlineData(true, false, "noEnabledBaseStations")]
    public async Task IrrelevantConfigurationSkipsInitialization(
        bool baseStationsEnabled,
        bool stationEnabled,
        string expectedReason)
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([Station()]);
        var config = new SupervisorConfig
        {
            BaseStationsEnabled = baseStationsEnabled,
            BaseStations = [Station(stationEnabled)]
        };

        var result = await CreateInitializer(temp, scanner).RunOnceAsync(config, CancellationToken.None);

        Assert.Equal("skipped", result.Outcome);
        Assert.Equal(expectedReason, result.SkipReason);
        Assert.Equal(0, scanner.Calls);
        AssertTerminalOutcome(temp, "skipped");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SuccessfulOrEmptyScanAllowsWatcherStartupToContinue(int foundDeviceCount)
    {
        using var temp = new TempDirectory();
        var devices = foundDeviceCount == 0 ? [] : new[] { Station() };
        var scanner = FakeScanner.Success(devices);
        var loopEntered = false;

        await AutoLaunchWatcher.RunStartupAndWatchLoopAsync(
            RelevantConfig(),
            CreateInitializer(temp, scanner),
            _ =>
            {
                loopEntered = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(loopEntered);
        var completed = Assert.Single(ReadEvents(temp), element => EventType(element) == "scanCompleted");
        Assert.Equal(foundDeviceCount, completed.GetProperty("foundDeviceCount").GetInt32());
        AssertTerminalOutcome(temp, "scanCompleted");
    }

    [Fact]
    public async Task TimeoutAllowsWatcherStartupToContinue()
    {
        using var temp = new TempDirectory();
        var scanner = new FakeScanner(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        });
        var loopEntered = false;

        await AutoLaunchWatcher.RunStartupAndWatchLoopAsync(
            RelevantConfig(),
            CreateInitializer(temp, scanner, timeout: TimeSpan.FromMilliseconds(20)),
            _ =>
            {
                loopEntered = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(loopEntered);
        AssertTerminalOutcome(temp, "timedOut");
    }

    [Fact]
    public async Task CancellationAllowsWatcherStartupToContinue()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var scanner = new FakeScanner(token => Task.FromCanceled<IReadOnlyList<BaseStationDevice>>(token));
        var loopEntered = false;

        await AutoLaunchWatcher.RunStartupAndWatchLoopAsync(
            RelevantConfig(),
            CreateInitializer(temp, scanner),
            _ =>
            {
                loopEntered = true;
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.True(loopEntered);
        AssertTerminalOutcome(temp, "cancelled");
    }

    [Theory]
    [InlineData(false, nameof(InvalidOperationException))]
    [InlineData(true, nameof(UnauthorizedAccessException))]
    public async Task MissingAdapterOrAccessExceptionAllowsWatcherStartupToContinue(
        bool accessDenied,
        string expectedExceptionType)
    {
        using var temp = new TempDirectory();
        Exception exception = accessDenied
            ? new UnauthorizedAccessException("access denied")
            : new InvalidOperationException("Bluetooth LE adapter not found.");
        var scanner = new FakeScanner(_ => Task.FromException<IReadOnlyList<BaseStationDevice>>(exception));
        var loopEntered = false;

        await AutoLaunchWatcher.RunStartupAndWatchLoopAsync(
            RelevantConfig(),
            CreateInitializer(temp, scanner),
            _ =>
            {
                loopEntered = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(loopEntered);
        var failed = Assert.Single(ReadEvents(temp), element => EventType(element) == "failed");
        Assert.Equal(expectedExceptionType, failed.GetProperty("exceptionType").GetString());
        AssertTerminalOutcome(temp, "failed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedDiscoveryCleanupIsObservedOnSuccessAndFailure(bool failScan)
    {
        using var temp = new TempDirectory();
        var scanner = failScan
            ? new FakeScanner(_ => Task.FromException<IReadOnlyList<BaseStationDevice>>(new InvalidOperationException("scan failed")))
            : FakeScanner.Success([Station()]);

        await CreateInitializer(temp, scanner).RunOnceAsync(RelevantConfig(), CancellationToken.None);

        Assert.Equal(1, scanner.CleanupCalls);
        var stopped = Assert.Single(ReadEvents(temp), element => EventType(element) == "watchersStopped");
        Assert.Equal("succeeded", stopped.GetProperty("outcome").GetString());
        Assert.Contains("stopRequests=3", stopped.GetProperty("cleanupResult").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionInitializerUsesSharedDiscoveryAndHasNoPowerCommandSurface()
    {
        var root = RepositoryRoot();
        var initializer = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "BluetoothStartupInitialization.cs"));
        var configurator = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor.ConfigEditor", "Program.cs"));

        Assert.Contains("BaseStationDiscovery.ScanAsync(", initializer, StringComparison.Ordinal);
        Assert.Contains("BaseStationDiscovery.ScanAsync(", configurator, StringComparison.Ordinal);
        Assert.Contains("BaseStationDiscovery.ConfiguratorScanDuration", configurator, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(10), BaseStationDiscovery.ConfiguratorScanDuration);
        foreach (var forbidden in new[] { "PowerOnAsync", "PowerDownAsync", "SendBaseStationPower", "WakeBaseStation", "SleepBaseStation" })
        {
            Assert.DoesNotContain(forbidden, initializer, StringComparison.Ordinal);
        }
    }

    private static BluetoothStartupInitializer CreateInitializer(
        TempDirectory temp,
        FakeScanner scanner,
        TimeSpan? timeout = null)
        => new(
            scanner,
            new BaseStationDiagnosticSink(temp.Path, "Watcher", "test"),
            scanDuration: TimeSpan.FromMilliseconds(5),
            timeout: timeout ?? TimeSpan.FromSeconds(1),
            cleanupWait: TimeSpan.FromSeconds(1));

    private static SupervisorConfig RelevantConfig()
        => new()
        {
            BaseStationsEnabled = true,
            BaseStations = [Station()]
        };

    private static BaseStationDevice Station(bool enabled = true)
        => new()
        {
            Name = "LHB-TEST0001",
            FriendlyName = "Test Station",
            BluetoothAddress = "AA:BB:CC:DD:EE:FF",
            Version = BaseStationVersion.V2,
            Enabled = enabled
        };

    private static JsonElement[] ReadEvents(TempDirectory temp)
    {
        var path = Directory.GetFiles(temp.Path, "base-station-startup-*.jsonl").Single();
        return File.ReadAllLines(path)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .Where(element => element.GetProperty("operationName").GetString() == BluetoothStartupInitializer.OperationName)
            .ToArray();
    }

    private static string? EventType(JsonElement element)
        => element.GetProperty("eventType").GetString();

    private static void AssertTerminalOutcome(TempDirectory temp, string expected)
    {
        var events = ReadEvents(temp);
        var terminalNames = new[] { "scanCompleted", "skipped", "timedOut", "cancelled", "failed" };
        var terminal = Assert.Single(events, element => terminalNames.Contains(EventType(element), StringComparer.Ordinal));
        Assert.Equal(expected, EventType(terminal));
        var complete = Assert.Single(events, element => EventType(element) == "complete");
        Assert.Equal(expected, complete.GetProperty("outcome").GetString());
    }

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Directory.Exists(Path.Combine(directory, "PimaxVrcSupervisor")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private sealed class FakeScanner(
        Func<CancellationToken, Task<IReadOnlyList<BaseStationDevice>>> behavior) : IBaseStationDiscoveryScanner
    {
        public int Calls { get; private set; }
        public int CleanupCalls { get; private set; }

        public static FakeScanner Success(IReadOnlyList<BaseStationDevice> devices)
            => new(_ => Task.FromResult(devices));

        public async Task<IReadOnlyList<BaseStationDevice>> ScanAsync(
            TimeSpan duration,
            CancellationToken cancellationToken,
            BaseStationDiagnosticSink diagnostics,
            string scanSessionId,
            string trigger,
            Action<BaseStationDiscoveryCleanupResult> cleanupObserver)
        {
            Calls++;
            try
            {
                return await behavior(cancellationToken);
            }
            finally
            {
                CleanupCalls++;
                cleanupObserver(new BaseStationDiscoveryCleanupResult(
                    3,
                    3,
                    3,
                    true,
                    true,
                    "stopRequests=3; handlersDetached=true"));
            }
        }
    }
}
