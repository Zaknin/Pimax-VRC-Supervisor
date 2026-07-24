using System.Text.Json;
using PimaxVrcSupervisor.BaseStations;
using Xunit;

public sealed class SteamVrBurstSuppressionTests
{
    [Fact]
    public async Task CheckAsync_PollsFromZeroToAll_AndSuppressesSecondBurst()
    {
        using var temp = new TempDirectory();
        var stations = Stations(4);
        var poll = 0;

        var result = await CreateChecker(temp, delayAsync: static (_, _) => Task.CompletedTask).CheckAsync(
            stations,
            enabled: true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>(
                ++poll == 1 ? [] : ReferencesFor(stations)),
            "wake-all",
            pass: 1,
            burstCycle: 1,
            CancellationToken.None);

        Assert.Equal(SteamVrBurstSuppressionOutcome.AllConfirmed, result.Outcome);
        Assert.True(result.SuppressBurst);
        Assert.Equal(2, result.PollCount);
        Assert.Equal(4, result.HighestConfirmedCount);
        Assert.True(result.SteamVrEverReachable);
        Assert.Equal("skipped", SteamVrBurstRetryPlanner.Plan(result, stations).Disposition);
    }

    [Fact]
    public async Task CheckAsync_ReportsProgressOnlyWhenExactCountChanges_AndOneTerminalEvent()
    {
        using var temp = new TempDirectory();
        var stations = Stations(4);
        var snapshots = new[]
        {
            Array.Empty<SteamVrTrackingReference>(),
            ReferencesFor(stations[..2]),
            ReferencesFor(stations)
        };
        var poll = 0;

        var result = await CreateChecker(temp).CheckAsync(
            stations,
            true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>(snapshots[Math.Min(poll++, snapshots.Length - 1)]),
            "wake-progress",
            1,
            1,
            CancellationToken.None);

        Assert.Equal(SteamVrBurstSuppressionOutcome.AllConfirmed, result.Outcome);
        Assert.Equal(3, result.PollCount);
        Assert.Equal(4, result.HighestConfirmedCount);
        var events = ReadEvents(temp);
        Assert.Equal(3, events.Count(element => EventType(element) == "steamVrConfirmationPollProgress"));
        Assert.Single(events, element => EventType(element) == "steamVrBurstSuppressionCheckCompleted");
        var completed = Assert.Single(events, element => EventType(element) == "steamVrBurstSuppressionCheckCompleted");
        Assert.Equal(3, completed.GetProperty("pollCount").GetInt32());
        Assert.Equal(4, completed.GetProperty("highestConfirmedCount").GetInt32());
        Assert.True(completed.GetProperty("steamVrEverReachable").GetBoolean());
    }

    [Fact]
    public async Task CheckAsync_UsesFullFallback_WhenExactIdentitiesRemainAbsentThroughDeadline()
    {
        using var temp = new TempDirectory();
        var stations = Stations(2);

        var result = await CreateChecker(temp, TimeSpan.FromMilliseconds(100)).CheckAsync(
            stations,
            true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>([]),
            "wake-deadline",
            1,
            1,
            CancellationToken.None);

        Assert.Equal(SteamVrBurstSuppressionOutcome.DeadlineExpired, result.Outcome);
        Assert.True(result.ConfirmationTimedOut);
        Assert.True(result.PollCount > 1);
        Assert.Equal("fullBurst", SteamVrBurstRetryPlanner.Plan(result, stations).Disposition);
    }

    [Fact]
    public async Task CheckAsync_DoesNotSuppress_WhenActiveCountMatchesButIdentitiesDoNot()
    {
        using var temp = new TempDirectory();
        var stations = Stations(2);
        SteamVrTrackingReference[] wrongReferences =
        [
            new(1, ["LHB-WRONG001"]),
            new(2, ["LHB-WRONG002"])
        ];

        var result = await CreateChecker(temp, TimeSpan.FromMilliseconds(15)).CheckAsync(
            stations,
            true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>(wrongReferences),
            "wake-mismatch",
            1,
            1,
            CancellationToken.None);

        Assert.Equal(SteamVrBurstSuppressionOutcome.DeadlineExpired, result.Outcome);
        Assert.Equal(0, result.ConfirmedActiveStationCount);
        Assert.False(result.SuppressBurst);
        Assert.Equal("fullBurst", SteamVrBurstRetryPlanner.Plan(result, stations).Disposition);
    }

    [Fact]
    public async Task CheckAsync_ContinuesAfterTransientEmptySnapshots()
    {
        using var temp = new TempDirectory();
        var stations = Stations(2);
        var poll = 0;

        var result = await CreateChecker(temp).CheckAsync(
            stations,
            true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>(
                ++poll < 3 ? [] : ReferencesFor(stations)),
            "wake-empty",
            1,
            1,
            CancellationToken.None);

        Assert.Equal(SteamVrBurstSuppressionOutcome.AllConfirmed, result.Outcome);
        Assert.Equal(3, result.PollCount);
    }

    [Fact]
    public async Task CheckAsync_TerminatesWithoutReading_WhenSteamVrIsUnavailable()
    {
        using var temp = new TempDirectory();
        var readerCalled = false;

        var result = await CreateChecker(temp).CheckAsync(
            Stations(2),
            true,
            () => (false, "OpenVR API unavailable"),
            _ =>
            {
                readerCalled = true;
                return Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>([]);
            },
            "wake-unavailable",
            1,
            1,
            CancellationToken.None);

        Assert.False(readerCalled);
        Assert.Equal(SteamVrBurstSuppressionOutcome.Unavailable, result.Outcome);
        Assert.False(result.SteamVrEverReachable);
    }

    [Fact]
    public async Task CheckAsync_ReturnsErrored_AndRetainsFallback_WhenQueryFails()
    {
        using var temp = new TempDirectory();
        var stations = Stations(2);

        var result = await CreateChecker(temp).CheckAsync(
            stations,
            true,
            Available,
            _ => Task.FromException<IReadOnlyList<SteamVrTrackingReference>>(new InvalidOperationException("query failed")),
            "wake-error",
            1,
            1,
            CancellationToken.None);

        Assert.Equal(SteamVrBurstSuppressionOutcome.Errored, result.Outcome);
        Assert.Equal("fullBurst", SteamVrBurstRetryPlanner.Plan(result, stations).Disposition);
    }

    [Fact]
    public async Task CheckAsync_ReturnsCancelledPromptly_DuringPollingDelay()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        var firstPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var check = CreateChecker(temp, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100)).CheckAsync(
            Stations(2),
            true,
            Available,
            _ =>
            {
                firstPoll.TrySetResult();
                return Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>([]);
            },
            "wake-cancelled",
            1,
            1,
            cancellation.Token);
        await firstPoll.Task;
        cancellation.Cancel();
        var result = await check.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(SteamVrBurstSuppressionOutcome.Cancelled, result.Outcome);
        Assert.False(result.SuppressBurst);
    }

    [Fact]
    public async Task LaterWakePass_AllConfirmed_DoesNotExecuteBluetoothAndEndsSequence()
    {
        using var temp = new TempDirectory();
        var stations = Stations(4);
        var confirmation = await ConfirmAsync(temp, stations, ReferencesFor(stations));
        var executed = false;

        var result = await SteamVrLaterWakePassCoordinator.ExecuteAsync(
            confirmation,
            stations,
            subsetTargetingSafe: true,
            _ =>
            {
                executed = true;
                return Task.FromResult(Array.Empty<bool>());
            });

        Assert.False(executed);
        Assert.True(result.EndSequence);
        Assert.Equal("skipped", result.Disposition);
    }

    [Fact]
    public async Task LaterWakePass_PartialConfirmation_RetriesOnlyMissingStations_WhenSafe()
    {
        using var temp = new TempDirectory();
        var stations = Stations(4);
        var confirmation = await ConfirmAsync(
            temp,
            stations,
            ReferencesFor(stations[..3]),
            TimeSpan.FromMilliseconds(15));
        BaseStationDevice[]? executedStations = null;

        var result = await SteamVrLaterWakePassCoordinator.ExecuteAsync(
            confirmation,
            stations,
            subsetTargetingSafe: true,
            selected =>
            {
                executedStations = selected;
                return Task.FromResult(selected.Select(_ => true).ToArray());
            });

        Assert.Equal(SteamVrBurstSuppressionOutcome.PartialConfirmed, confirmation.Outcome);
        Assert.Equal("subsetTargeted", result.Disposition);
        Assert.NotNull(executedStations);
        Assert.Single(executedStations!);
        Assert.Equal(stations[3].BluetoothAddress, executedStations![0].BluetoothAddress);
        Assert.DoesNotContain(executedStations, station => stations[..3].Contains(station));
    }

    [Fact]
    public async Task LaterWakePass_PartialConfirmation_UsesFullPass_WhenSubsetIsUnsafe()
    {
        using var temp = new TempDirectory();
        var stations = Stations(3);
        var confirmation = await ConfirmAsync(
            temp,
            stations,
            ReferencesFor(stations[..1]),
            TimeSpan.FromMilliseconds(15));
        BaseStationDevice[]? executedStations = null;

        var result = await SteamVrLaterWakePassCoordinator.ExecuteAsync(
            confirmation,
            stations,
            subsetTargetingSafe: false,
            selected =>
            {
                executedStations = selected;
                return Task.FromResult(selected.Select(_ => true).ToArray());
            });

        Assert.Equal("fullBurst", result.Disposition);
        Assert.Equal(stations, executedStations);
    }

    [Fact]
    public async Task CheckAsync_IsDisabledWithoutConfiguredStations()
    {
        using var temp = new TempDirectory();

        var result = await CreateChecker(temp).CheckAsync(
            [],
            true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>([]),
            "wake-disabled",
            1,
            1,
            CancellationToken.None);

        Assert.Equal(SteamVrBurstSuppressionOutcome.Disabled, result.Outcome);
        Assert.Equal(0, result.PollCount);
        Assert.Empty(result.MissingBluetoothAddresses);
    }

    private static Task<SteamVrBurstSuppressionResult> ConfirmAsync(
        TempDirectory temp,
        BaseStationDevice[] stations,
        SteamVrTrackingReference[] references,
        TimeSpan? maximumDuration = null)
        => CreateChecker(temp, maximumDuration).CheckAsync(
            stations,
            true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>(references),
            "wake-later",
            2,
            0,
            CancellationToken.None,
            SteamVrBurstSuppressionChecker.LaterWakePassDecisionPoint);

    private static SteamVrBurstSuppressionChecker CreateChecker(
        TempDirectory temp,
        TimeSpan? maximumDuration = null,
        TimeSpan? pollingInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        => new(
            new BaseStationDiagnosticSink(temp.Path, "Supervisor", "test"),
            maximumDuration ?? TimeSpan.FromMilliseconds(100),
            pollingInterval ?? TimeSpan.FromMilliseconds(1),
            delayAsync);

    private static (bool Available, string Reason) Available()
        => (true, "available");

    private static BaseStationDevice[] Stations(int count)
        => Enumerable.Range(1, count)
            .Select(index => new BaseStationDevice
            {
                Name = $"LHB-TEST{index:0000}",
                FriendlyName = $"Station {index}",
                BluetoothAddress = $"AA:BB:CC:DD:EE:{index:00}",
                Version = BaseStationVersion.V2,
                Enabled = true
            })
            .ToArray();

    private static SteamVrTrackingReference[] ReferencesFor(BaseStationDevice[] stations)
        => stations
            .Select((station, index) => new SteamVrTrackingReference((uint)index, [station.Name]))
            .ToArray();

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
                && operationName.GetString() == SteamVrBurstSuppressionChecker.OperationName)
            .ToArray();
    }

    private static string? EventType(JsonElement element)
        => element.GetProperty("eventType").GetString();
}
