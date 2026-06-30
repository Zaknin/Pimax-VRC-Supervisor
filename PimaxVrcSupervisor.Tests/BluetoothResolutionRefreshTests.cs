using System.Text.Json;
using PimaxVrcSupervisor.BaseStations;
using Xunit;

public sealed class BluetoothResolutionRefreshTests
{
    [Fact]
    public async Task SuccessfulInitialWakeDoesNotRefreshOrRetry()
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([]);
        var retryCalls = 0;

        var result = await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Success(Station("01"))],
            "wake-1",
            (_, _) =>
            {
                retryCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([]);
            },
            CancellationToken.None);

        Assert.True(Assert.Single(result).Succeeded);
        Assert.Equal(0, scanner.Calls);
        Assert.Equal(0, retryCalls);
    }

    [Fact]
    public async Task DeviceResolutionFailureTriggersExactlyOneRefresh()
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([]);
        var station = Station("01");

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (stations, _) => Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(
                stations.Select(Success).ToArray()),
            CancellationToken.None);

        Assert.Equal(1, scanner.Calls);
    }

    [Fact]
    public async Task MultipleUnresolvedStationsUseOneSharedRefresh()
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([]);
        var stations = new[] { Station("01"), Station("02"), Station("03") };
        var retryCalls = 0;

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            stations.Select(station => Failure(station, BaseStationCommandFailureStage.DeviceResolution)).ToArray(),
            "wake-1",
            (retryStations, _) =>
            {
                retryCalls++;
                Assert.Equal(3, retryStations.Count);
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(
                    retryStations.Select(Success).ToArray());
            },
            CancellationToken.None);

        Assert.Equal(1, scanner.Calls);
        Assert.Equal(1, retryCalls);
    }

    [Fact]
    public async Task SuccessfulStationsAreNotRetried()
    {
        using var temp = new TempDirectory();
        var succeeded = Station("01");
        var unresolved = Station("02");

        await Create(temp, FakeScanner.Success([])).RetryUnresolvedOnceAsync(
            [Success(succeeded), Failure(unresolved, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (stations, _) =>
            {
                Assert.Single(stations);
                Assert.Same(unresolved, stations[0]);
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([Success(unresolved)]);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task UnresolvedStationsAreRetriedOnceAfterRefresh()
    {
        using var temp = new TempDirectory();
        var retryCalls = 0;
        var unresolved = Station("01");

        await Create(temp, FakeScanner.Success([])).RetryUnresolvedOnceAsync(
            [Failure(unresolved, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (stations, _) =>
            {
                retryCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([Success(stations[0])]);
            },
            CancellationToken.None);

        Assert.Equal(1, retryCalls);
    }

    [Fact]
    public async Task PostRefreshRetrySuccessIsReturned()
    {
        using var temp = new TempDirectory();
        var station = Station("01");

        var result = await Create(temp, FakeScanner.Success([])).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (stations, _) => Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([Success(stations[0])]),
            CancellationToken.None);

        Assert.True(Assert.Single(result).Succeeded);
    }

    [Fact]
    public async Task PostRefreshRetryFailureIsReturned()
    {
        using var temp = new TempDirectory();
        var station = Station("01");

        var result = await Create(temp, FakeScanner.Success([])).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (stations, _) => Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(
                [Failure(stations[0], BaseStationCommandFailureStage.DeviceResolution)]),
            CancellationToken.None);

        var failed = Assert.Single(result);
        Assert.False(failed.Succeeded);
        Assert.Equal(BaseStationCommandFailureStage.DeviceResolution, failed.FailureStage);
    }

    [Fact]
    public async Task RefreshTimeoutIsNonfatalAndStillRetries()
    {
        using var temp = new TempDirectory();
        var scanner = new FakeScanner(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        });
        var retryCalls = 0;

        await Create(temp, scanner, timeout: TimeSpan.FromMilliseconds(20)).RetryUnresolvedOnceAsync(
            [Failure(Station("01"), BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (stations, _) =>
            {
                retryCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([Success(stations[0])]);
            },
            CancellationToken.None);

        Assert.Equal(1, retryCalls);
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "timedOut");
    }

    [Fact]
    public async Task RefreshCancellationIsNonfatalAndStillRetries()
    {
        using var temp = new TempDirectory();
        var scanner = new FakeScanner(_ =>
            Task.FromException<IReadOnlyList<BaseStationDevice>>(new OperationCanceledException("scan cancelled")));
        var retryCalls = 0;

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(Station("01"), BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (stations, _) =>
            {
                retryCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([Success(stations[0])]);
            },
            CancellationToken.None);

        Assert.Equal(1, retryCalls);
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "cancelled");
    }

    [Fact]
    public async Task RefreshExceptionIsNonfatalAndStillRetries()
    {
        using var temp = new TempDirectory();
        var scanner = new FakeScanner(_ =>
            Task.FromException<IReadOnlyList<BaseStationDevice>>(new UnauthorizedAccessException("denied")));
        var retryCalls = 0;

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(Station("01"), BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (stations, _) =>
            {
                retryCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([Success(stations[0])]);
            },
            CancellationToken.None);

        Assert.Equal(1, retryCalls);
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "failed");
    }

    [Fact]
    public async Task EmptyDiscoveryResultStillRetriesOnce()
    {
        using var temp = new TempDirectory();
        var retryCalls = 0;

        await Create(temp, FakeScanner.Success([])).RetryUnresolvedOnceAsync(
            [Failure(Station("01"), BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (stations, _) =>
            {
                retryCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([Success(stations[0])]);
            },
            CancellationToken.None);

        Assert.Equal(1, retryCalls);
        var scanCompleted = Assert.Single(ReadEvents(temp), element => EventType(element) == "scanCompleted");
        Assert.Equal(0, scanCompleted.GetProperty("foundDeviceCount").GetInt32());
    }

    [Theory]
    [InlineData((int)BaseStationCommandFailureStage.GattService)]
    [InlineData((int)BaseStationCommandFailureStage.Characteristic)]
    [InlineData((int)BaseStationCommandFailureStage.Write)]
    [InlineData((int)BaseStationCommandFailureStage.BluetoothAdapter)]
    [InlineData((int)BaseStationCommandFailureStage.Cancelled)]
    [InlineData((int)BaseStationCommandFailureStage.Unknown)]
    public async Task NonResolutionFailuresDoNotTriggerRefresh(int stageValue)
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([]);
        var stage = (BaseStationCommandFailureStage)stageValue;

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(Station("01"), stage)],
            "wake-1",
            (_, _) => throw new Xunit.Sdk.XunitException("Retry must not run."),
            CancellationToken.None);

        Assert.Equal(0, scanner.Calls);
    }

    [Fact]
    public async Task MissingAdapterDoesNotCauseUnboundedRefresh()
    {
        using var temp = new TempDirectory();
        var scanner = new FakeScanner(_ =>
            Task.FromException<IReadOnlyList<BaseStationDevice>>(
                new InvalidOperationException("Bluetooth LE adapter not found.")));
        var refresh = Create(temp, scanner);
        var failure = Failure(Station("01"), BaseStationCommandFailureStage.DeviceResolution);

        await refresh.RetryUnresolvedOnceAsync(
            [failure],
            "wake-1",
            (_, _) => Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([failure]),
            CancellationToken.None);
        await refresh.RetryUnresolvedOnceAsync(
            [failure],
            "wake-1",
            (_, _) => throw new Xunit.Sdk.XunitException("Second retry must not run."),
            CancellationToken.None);

        Assert.Equal(1, scanner.Calls);
    }

    [Fact]
    public void DiscoveryRefreshHasNoWakeOrSleepCommandSurface()
    {
        var root = RepositoryRoot();
        var refresh = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "BluetoothResolutionRefresh.cs"));
        var startup = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "BluetoothStartupInitialization.cs"));

        Assert.Contains("IBaseStationDiscoveryScanner", refresh, StringComparison.Ordinal);
        Assert.Contains("BaseStationDiscovery.ScanAsync(", startup, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "PowerOnAsync", "PowerDownAsync", "WakeBaseStation", "SleepBaseStation" })
        {
            Assert.DoesNotContain(forbidden, refresh, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RefreshGuardPreventsWatcherLoopRepetition()
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([]);
        var refresh = Create(temp, scanner);
        var station = Station("01");
        var failure = Failure(station, BaseStationCommandFailureStage.DeviceResolution);
        var retryCalls = 0;

        for (var iteration = 0; iteration < 3; iteration++)
        {
            await refresh.RetryUnresolvedOnceAsync(
                [failure],
                "wake-1",
                (stations, _) =>
                {
                    retryCalls++;
                    return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([Success(stations[0])]);
                },
                CancellationToken.None);
        }

        Assert.Equal(1, scanner.Calls);
        Assert.Equal(1, retryCalls);
    }

    [Fact]
    public async Task DiagnosticsHaveOneTerminalOutcomeAndUseSupervisorSink()
    {
        using var temp = new TempDirectory();
        var station = Station("01");

        await Create(temp, FakeScanner.Success([station])).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-correlation",
            (stations, _) => Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([Success(stations[0])]),
            CancellationToken.None);

        var path = Assert.Single(Directory.GetFiles(temp.Path, "base-station-startup-*.jsonl"));
        Assert.EndsWith("base-station-startup-supervisor.jsonl", path, StringComparison.OrdinalIgnoreCase);
        var events = ReadEvents(temp);
        var terminal = Assert.Single(events, element =>
            element.TryGetProperty("terminal", out var value) && value.GetBoolean());
        Assert.Equal("complete", EventType(terminal));
        Assert.All(events, element =>
        {
            Assert.Equal(BluetoothResolutionRefresh.OperationName, element.GetProperty("operationName").GetString());
            Assert.Equal("wake-correlation", element.GetProperty("wakeSequenceId").GetString());
            Assert.Equal(nameof(BaseStationCommandFailureStage.DeviceResolution), element.GetProperty("triggerFailureStage").GetString());
        });
    }

    private static BluetoothResolutionRefresh Create(
        TempDirectory temp,
        FakeScanner scanner,
        TimeSpan? timeout = null)
        => new(
            scanner,
            new BaseStationDiagnosticSink(temp.Path, "Supervisor", "test"),
            scanDuration: TimeSpan.FromMilliseconds(5),
            timeout: timeout ?? TimeSpan.FromSeconds(1),
            cleanupWait: TimeSpan.FromSeconds(1));

    private static BaseStationWakeAttemptResult Success(BaseStationDevice station)
        => BaseStationWakeAttemptResult.Success(station);

    private static BaseStationWakeAttemptResult Failure(
        BaseStationDevice station,
        BaseStationCommandFailureStage stage)
        => BaseStationWakeAttemptResult.Failure(
            station,
            stage,
            new BaseStationCommandException(stage, "test failure"));

    private static BaseStationDevice Station(string suffix)
        => new()
        {
            Name = $"LHB-TEST00{suffix}",
            FriendlyName = $"Test Station {suffix}",
            BluetoothAddress = $"AA:BB:CC:DD:EE:{suffix}",
            Version = BaseStationVersion.V2,
            Enabled = true
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
            .Where(element =>
                element.TryGetProperty("operationName", out var operationName)
                && operationName.GetString() == BluetoothResolutionRefresh.OperationName)
            .ToArray();
    }

    private static string? EventType(JsonElement element)
        => element.GetProperty("eventType").GetString();

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
