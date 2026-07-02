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
        var fallbackCalls = 0;

        var result = await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Success(Station("01"))],
            "wake-1",
            StreamingSuccess,
            (_, _) =>
            {
                fallbackCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([]);
            },
            CancellationToken.None);

        Assert.True(Assert.Single(result).Succeeded);
        Assert.Equal(0, scanner.Calls);
        Assert.Equal(0, fallbackCalls);
    }

    [Fact]
    public async Task FirstDeviceResolutionFailureTriggersRefreshImmediately()
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([]);

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(Station("01"), BaseStationCommandFailureStage.DeviceResolution, elapsedMilliseconds: 42)],
            "wake-1",
            StreamingSuccess,
            FallbackSuccess,
            CancellationToken.None);

        Assert.Equal(1, scanner.Calls);
        var first = Assert.Single(ReadEvents(temp), element => EventType(element) == "firstResolutionFailure");
        Assert.Equal(42, first.GetProperty("firstFailureElapsedMilliseconds").GetDouble());
    }

    [Fact]
    public async Task RefreshDoesNotWaitForAllStationsToFail()
    {
        using var temp = new TempDirectory();
        var stations = new[] { Station("01"), Station("02"), Station("03"), Station("04") };

        await Create(temp, FakeScanner.Success([])).RetryUnresolvedOnceAsync(
            [
                Failure(stations[0], BaseStationCommandFailureStage.DeviceResolution),
                Unattempted(stations[1]),
                Unattempted(stations[2]),
                Unattempted(stations[3])
            ],
            "wake-1",
            StreamingSuccess,
            FallbackSuccess,
            CancellationToken.None);

        var start = Assert.Single(ReadEvents(temp), element => EventType(element) == "start");
        Assert.Equal(4, start.GetProperty("candidateStationCount").GetInt32());
    }

    [Fact]
    public async Task SuccessfulStationsBeforeFirstFailureRemainSuccessful()
    {
        using var temp = new TempDirectory();
        var succeeded = Station("01");
        var failed = Station("02");
        var notAttempted = Station("03");
        var streamingAttempts = new List<string>();

        var result = await Create(temp, FakeScanner.Success([failed, notAttempted])).RetryUnresolvedOnceAsync(
            [Success(succeeded), Failure(failed, BaseStationCommandFailureStage.DeviceResolution), Unattempted(notAttempted)],
            "wake-1",
            (station, _) =>
            {
                streamingAttempts.Add(station.BluetoothAddress);
                return Task.FromResult(Success(station));
            },
            FallbackSuccess,
            CancellationToken.None);

        Assert.True(result.Single(item => ReferenceEquals(item.Station, succeeded)).Succeeded);
        Assert.DoesNotContain(succeeded.BluetoothAddress, streamingAttempts);
    }

    [Fact]
    public async Task SuccessfulStationsAreNeverRequeued()
    {
        using var temp = new TempDirectory();
        var succeeded = Station("01");
        var failed = Station("02");
        var streamingAttempts = new List<string>();
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(succeeded);
            ctx.Observe(failed);
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([succeeded, failed]);
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Success(succeeded), Failure(failed, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (station, _) =>
            {
                streamingAttempts.Add(station.BluetoothAddress);
                return Task.FromResult(Success(station));
            },
            FallbackSuccess,
            CancellationToken.None);

        Assert.Equal([failed.BluetoothAddress], streamingAttempts);
    }

    [Fact]
    public async Task NotYetAttemptedStationsBecomeRecoveryCandidates()
    {
        using var temp = new TempDirectory();
        var failed = Station("01");
        var notAttempted = Station("02");
        IReadOnlyList<BaseStationDevice>? fallbackStations = null;

        await Create(temp, FakeScanner.Success([])).RetryUnresolvedOnceAsync(
            [Failure(failed, BaseStationCommandFailureStage.DeviceResolution), Unattempted(notAttempted)],
            "wake-1",
            StreamingFailure,
            (stations, _) =>
            {
                fallbackStations = stations;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(stations.Select(Success).ToArray());
            },
            CancellationToken.None);

        Assert.Contains(notAttempted, fallbackStations!);
    }

    [Theory]
    [InlineData((int)BaseStationCommandFailureStage.GattService)]
    [InlineData((int)BaseStationCommandFailureStage.Write)]
    [InlineData((int)BaseStationCommandFailureStage.BluetoothAdapter)]
    [InlineData((int)BaseStationCommandFailureStage.Characteristic)]
    [InlineData((int)BaseStationCommandFailureStage.Cancelled)]
    [InlineData((int)BaseStationCommandFailureStage.Unknown)]
    public async Task NonResolutionFailuresDoNotTriggerRefresh(int stageValue)
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([]);

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(Station("01"), (BaseStationCommandFailureStage)stageValue)],
            "wake-1",
            StreamingSuccess,
            (_, _) => throw new Xunit.Sdk.XunitException("Fallback must not run."),
            CancellationToken.None);

        Assert.Equal(0, scanner.Calls);
    }

    [Fact]
    public async Task ConfiguredStationObservationQueuesImmediately()
    {
        using var temp = new TempDirectory();
        var station = Station("01");
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(station);
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([station]);
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            StreamingSuccess,
            FallbackSuccess,
            CancellationToken.None);

        Assert.Contains(ReadEvents(temp), element => EventType(element) == "stationQueued");
    }

    [Fact]
    public async Task UnconfiguredBleObservationsAreIgnored()
    {
        using var temp = new TempDirectory();
        var configured = Station("01");
        var unconfigured = Station("02");
        var streamingAttempts = 0;
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(unconfigured);
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([unconfigured]);
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(configured, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (_, _) =>
            {
                streamingAttempts++;
                return Task.FromResult(Success(configured));
            },
            FallbackSuccess,
            CancellationToken.None);

        Assert.Equal(0, streamingAttempts);
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "stationObserved" && element.GetProperty("outcome").GetString() == "ignored");
    }

    [Fact]
    public async Task DuplicateAdvertisementsDoNotDuplicateQueueEntries()
    {
        using var temp = new TempDirectory();
        var station = Station("01");
        var attempts = 0;
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(station);
            ctx.Observe(station);
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([station]);
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(Success(station));
            },
            FallbackSuccess,
            CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "stationObserved" && element.GetProperty("outcome").GetString() == "duplicateIgnored");
    }

    [Fact]
    public async Task BluetoothEventCallbackReturnsWithoutAwaitingWakeOperation()
    {
        using var temp = new TempDirectory();
        var station = Station("01");
        var callbackReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner(async ctx =>
        {
            ctx.Observe(station);
            callbackReturned.SetResult();
            await releaseWake.Task;
            return [station];
        });

        var refreshTask = Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            async (_, _) =>
            {
                await releaseWake.Task;
                return Success(station);
            },
            FallbackSuccess,
            CancellationToken.None);

        await callbackReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(refreshTask.IsCompleted);
        releaseWake.SetResult();
        await refreshTask;
    }

    [Fact]
    public async Task DiscoveryRemainsActiveWhileWakeProcessingRuns()
    {
        using var temp = new TempDirectory();
        var first = Station("01");
        var second = Station("02");
        var wakeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner(async ctx =>
        {
            ctx.Observe(first);
            await wakeStarted.Task;
            Assert.False(ctx.Token.IsCancellationRequested);
            ctx.Observe(second);
            releaseWake.SetResult();
            return [first, second];
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(first, BaseStationCommandFailureStage.DeviceResolution), Unattempted(second)],
            "wake-1",
            async (station, _) =>
            {
                wakeStarted.TrySetResult();
                await releaseWake.Task;
                return Success(station);
            },
            FallbackSuccess,
            CancellationToken.None);

        Assert.Contains(ReadEvents(temp), element => EventType(element) == "stationWakeStarted");
    }

    [Fact]
    public async Task OnlyOneStreamingWakeAttemptRunsPerStationAndWorkerIsSerialized()
    {
        using var temp = new TempDirectory();
        var first = Station("01");
        var second = Station("02");
        var active = 0;
        var maxActive = 0;
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(first);
            ctx.Observe(second);
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([first, second]);
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(first, BaseStationCommandFailureStage.DeviceResolution), Unattempted(second)],
            "wake-1",
            (station, _) =>
            {
                var current = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, current);
                Interlocked.Decrement(ref active);
                return Task.FromResult(Success(station));
            },
            FallbackSuccess,
            CancellationToken.None);

        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task StreamingSuccessRemovesStationFromFallback()
    {
        using var temp = new TempDirectory();
        var first = Station("01");
        var second = Station("02");
        IReadOnlyList<BaseStationDevice>? fallbackStations = null;
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(first);
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([first]);
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(first, BaseStationCommandFailureStage.DeviceResolution), Unattempted(second)],
            "wake-1",
            StreamingSuccess,
            (stations, _) =>
            {
                fallbackStations = stations;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(stations.Select(Success).ToArray());
            },
            CancellationToken.None);

        Assert.DoesNotContain(first, fallbackStations!);
        Assert.Contains(second, fallbackStations!);
    }

    [Fact]
    public async Task StreamingFailureRemainsEligibleForOneFinalFallbackRetry()
    {
        using var temp = new TempDirectory();
        var station = Station("01");
        var fallbackCalls = 0;
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(station);
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([station]);
        });

        var result = await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            StreamingFailure,
            (stations, _) =>
            {
                fallbackCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(stations.Select(Success).ToArray());
            },
            CancellationToken.None);

        Assert.Equal(1, fallbackCalls);
        Assert.True(Assert.Single(result).Succeeded);
    }

    [Fact]
    public async Task AllCandidatesSucceedingStopsScanEarlyAndCleansUpWatchers()
    {
        using var temp = new TempDirectory();
        var station = Station("01");
        var scanner = new FakeScanner(async ctx =>
        {
            ctx.Observe(station);
            await Task.Delay(Timeout.InfiniteTimeSpan, ctx.Token);
            return [];
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            StreamingSuccess,
            FallbackSuccess,
            CancellationToken.None);

        Assert.True(scanner.TokenCancelled);
        Assert.Equal(1, scanner.CleanupCalls);
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "scanStoppedEarly");
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "watchersStopped");
    }

    [Fact]
    public async Task PartialDiscoveryRunsFinalRetryForRemainingOnly()
    {
        using var temp = new TempDirectory();
        var first = Station("01");
        var second = Station("02");
        IReadOnlyList<BaseStationDevice>? fallbackStations = null;
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(first);
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([first]);
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(first, BaseStationCommandFailureStage.DeviceResolution), Unattempted(second)],
            "wake-1",
            StreamingSuccess,
            (stations, _) =>
            {
                fallbackStations = stations;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(stations.Select(Success).ToArray());
            },
            CancellationToken.None);

        Assert.Equal([second], fallbackStations);
    }

    [Fact]
    public async Task FullStreamingSuccessSkipsFinalFallbackRetry()
    {
        using var temp = new TempDirectory();
        var station = Station("01");
        var fallbackCalls = 0;
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(station);
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([station]);
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            StreamingSuccess,
            (_, _) =>
            {
                fallbackCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>([]);
            },
            CancellationToken.None);

        Assert.Equal(0, fallbackCalls);
    }

    [Fact]
    public async Task EmptyDiscoveryResultStillRunsOneFallbackRetry()
    {
        using var temp = new TempDirectory();
        var station = Station("01");
        var fallbackCalls = 0;

        await Create(temp, FakeScanner.Success([])).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-1",
            StreamingSuccess,
            (stations, _) =>
            {
                fallbackCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(stations.Select(Success).ToArray());
            },
            CancellationToken.None);

        Assert.Equal(1, fallbackCalls);
    }

    [Fact]
    public async Task ScanTimeoutAndScannerExceptionRemainNonfatal()
    {
        using var temp = new TempDirectory();
        var timeoutScanner = new FakeScanner(async ctx =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ctx.Token);
            return [];
        });
        var exceptionScanner = new FakeScanner(_ =>
            Task.FromException<IReadOnlyList<BaseStationDevice>>(new UnauthorizedAccessException("denied")));
        var fallbackCalls = 0;

        await Create(temp, timeoutScanner, timeout: TimeSpan.FromMilliseconds(20)).RetryUnresolvedOnceAsync(
            [Failure(Station("01"), BaseStationCommandFailureStage.DeviceResolution)],
            "wake-timeout",
            StreamingSuccess,
            (stations, _) =>
            {
                fallbackCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(stations.Select(Success).ToArray());
            },
            CancellationToken.None);
        await Create(temp, exceptionScanner).RetryUnresolvedOnceAsync(
            [Failure(Station("02"), BaseStationCommandFailureStage.DeviceResolution)],
            "wake-exception",
            StreamingSuccess,
            (stations, _) =>
            {
                fallbackCalls++;
                return Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(stations.Select(Success).ToArray());
            },
            CancellationToken.None);

        Assert.Equal(2, fallbackCalls);
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "timedOut");
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "failed");
    }

    [Fact]
    public async Task CancellationPreservesCompletedSuccessesAndStopsQueueWorker()
    {
        using var temp = new TempDirectory();
        var first = Station("01");
        var second = Station("02");
        using var cancellation = new CancellationTokenSource();
        var scanner = new FakeScanner(ctx =>
        {
            ctx.Observe(first);
            ctx.Observe(second);
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<BaseStationDevice>>([first, second]);
        });

        var result = await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(first, BaseStationCommandFailureStage.DeviceResolution), Unattempted(second)],
            "wake-1",
            (station, _) => Task.FromResult(Success(station)),
            FallbackSuccess,
            cancellation.Token);

        Assert.Contains(result, item => ReferenceEquals(item.Station, first) && item.Succeeded);
        Assert.Contains(ReadEvents(temp), element => EventType(element) == "complete");
    }

    [Fact]
    public async Task RefreshGuardPreventsRepeatedPollingLoopScan()
    {
        using var temp = new TempDirectory();
        var scanner = FakeScanner.Success([]);
        var refresh = Create(temp, scanner);
        var failure = Failure(Station("01"), BaseStationCommandFailureStage.DeviceResolution);

        await refresh.RetryUnresolvedOnceAsync([failure], "wake-1", StreamingSuccess, FallbackSuccess, CancellationToken.None);
        await refresh.RetryUnresolvedOnceAsync([failure], "wake-1", StreamingSuccess, FallbackSuccess, CancellationToken.None);

        Assert.Equal(1, scanner.Calls);
    }

    [Fact]
    public async Task DiagnosticsHaveOneTerminalOutcomeAndShowWakeBeforeScanCompletion()
    {
        using var temp = new TempDirectory();
        var station = Station("01");
        var wakeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner(async ctx =>
        {
            ctx.Observe(station);
            await wakeStarted.Task;
            return [station];
        });

        await Create(temp, scanner).RetryUnresolvedOnceAsync(
            [Failure(station, BaseStationCommandFailureStage.DeviceResolution)],
            "wake-correlation",
            (wakeStation, _) =>
            {
                wakeStarted.TrySetResult();
                return Task.FromResult(Success(wakeStation));
            },
            FallbackSuccess,
            CancellationToken.None);

        var path = Assert.Single(Directory.GetFiles(temp.Path, "base-station-startup-*.jsonl"));
        Assert.EndsWith("base-station-startup-supervisor.jsonl", path, StringComparison.OrdinalIgnoreCase);
        var events = ReadEvents(temp);
        Assert.Single(events, element => element.TryGetProperty("terminal", out var value) && value.GetBoolean());
        Assert.True(IndexOf(events, "stationWakeStarted") < IndexOf(events, "scanCompleted"));
        Assert.All(events, element => Assert.Equal("wake-correlation", element.GetProperty("wakeSequenceId").GetString()));
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
    public void ConfiguratorScanBehaviorRemainsUnchangedWithoutStreamingObserver()
    {
        var root = RepositoryRoot();
        var configurator = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor.ConfigEditor", "Program.cs"));
        var support = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "BaseStationSupport.cs"));

        Assert.Contains("BaseStationDiscovery.ScanAsync(", configurator, StringComparison.Ordinal);
        Assert.Contains("IBaseStationDiscoveryObserver? observer = null", support, StringComparison.Ordinal);
        Assert.DoesNotContain("IBaseStationDiscoveryObserver", configurator, StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceInstrumentationDocumentsAvoidedPreRefreshAttempts()
    {
        using var temp = new TempDirectory();
        var stations = new[] { Station("01"), Station("02"), Station("03"), Station("04") };
        var initial = new[]
        {
            Failure(stations[0], BaseStationCommandFailureStage.DeviceResolution),
            Unattempted(stations[1]),
            Unattempted(stations[2]),
            Unattempted(stations[3])
        };

        var avoided = initial.Count(result => !result.Attempted);

        Assert.Equal(3, avoided);
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

    private static Task<BaseStationWakeAttemptResult> StreamingSuccess(BaseStationDevice station, CancellationToken _)
        => Task.FromResult(Success(station));

    private static Task<BaseStationWakeAttemptResult> StreamingFailure(BaseStationDevice station, CancellationToken _)
        => Task.FromResult(Failure(station, BaseStationCommandFailureStage.DeviceResolution));

    private static Task<IReadOnlyList<BaseStationWakeAttemptResult>> FallbackSuccess(
        IReadOnlyList<BaseStationDevice> stations,
        CancellationToken _)
        => Task.FromResult<IReadOnlyList<BaseStationWakeAttemptResult>>(stations.Select(Success).ToArray());

    private static BaseStationWakeAttemptResult Success(BaseStationDevice station)
        => BaseStationWakeAttemptResult.Success(station);

    private static BaseStationWakeAttemptResult Failure(
        BaseStationDevice station,
        BaseStationCommandFailureStage stage,
        bool attempted = true,
        double? elapsedMilliseconds = null)
        => BaseStationWakeAttemptResult.Failure(
            station,
            stage,
            new BaseStationCommandException(stage, "test failure"),
            attempted,
            elapsedMilliseconds);

    private static BaseStationWakeAttemptResult Unattempted(BaseStationDevice station)
        => BaseStationWakeAttemptResult.UnattemptedResolutionCandidate(station);

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

    private static int IndexOf(JsonElement[] events, string eventType)
    {
        for (var index = 0; index < events.Length; index++)
        {
            if (EventType(events[index]) == eventType)
            {
                return index;
            }
        }

        return int.MaxValue;
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
        Func<FakeScanContext, Task<IReadOnlyList<BaseStationDevice>>> behavior) : IBaseStationDiscoveryScanner
    {
        public int Calls { get; private set; }
        public int CleanupCalls { get; private set; }
        public bool TokenCancelled { get; private set; }
        private IBaseStationDiscoveryObserver? Observer { get; set; }

        public static FakeScanner Success(IReadOnlyList<BaseStationDevice> devices)
            => new(_ => Task.FromResult(devices));

        public async Task<IReadOnlyList<BaseStationDevice>> ScanAsync(
            TimeSpan duration,
            CancellationToken cancellationToken,
            BaseStationDiagnosticSink diagnostics,
            string scanSessionId,
            string trigger,
            Action<BaseStationDiscoveryCleanupResult> cleanupObserver,
            IBaseStationDiscoveryObserver? observer = null)
        {
            Calls++;
            Observer = observer;
            using var registration = cancellationToken.Register(() => TokenCancelled = true);
            try
            {
                return await behavior(new FakeScanContext(this, cancellationToken));
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

        public void Observe(BaseStationDevice station)
            => Observer?.OnStationObserved(new BaseStationDiscoveryObservation(station, "fake"));
    }

    private sealed class FakeScanContext(FakeScanner scanner, CancellationToken token)
    {
        public CancellationToken Token { get; } = token;

        public void Observe(BaseStationDevice station)
            => scanner.Observe(station);
    }
}
