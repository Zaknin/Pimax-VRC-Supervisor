using System.Text.Json;
using PimaxVrcSupervisor.BaseStations;
using Xunit;

public sealed class SteamVrBurstSuppressionTests
{
    [Fact]
    public async Task CheckAsync_SuppressesSecondBurst_WhenAllStationsMatchExactly()
    {
        using var temp = new TempDirectory();
        var stations = Stations(2);
        var checker = CreateChecker(temp);

        var result = await checker.CheckAsync(
            stations,
            enabled: true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>(ReferencesFor(stations)),
            "wake-all",
            pass: 1,
            burstCycle: 1,
            CancellationToken.None);
        var plan = SteamVrBurstRetryPlanner.Plan(result, stations);
        checker.WriteDisposition("burstSuppressed", result, "wake-all", 1, 2, plan.Disposition);

        Assert.Equal(SteamVrBurstSuppressionOutcome.AllConfirmed, result.Outcome);
        Assert.True(result.SuppressBurst);
        Assert.Equal("skipped", plan.Disposition);
        Assert.Empty(plan.Stations);
        var events = ReadEvents(temp);
        Assert.Equal(
            ["steamVrBurstSuppressionCheckStarted", "steamVrBurstSuppressionCheckCompleted", "burstSuppressed"],
            events.Select(EventType));
        var completed = events[1];
        Assert.Equal(2, completed.GetProperty("configuredStationCount").GetInt32());
        Assert.Equal(2, completed.GetProperty("confirmedActiveStationCount").GetInt32());
        Assert.Equal(0, completed.GetProperty("missingStationCount").GetInt32());
        Assert.True(completed.GetProperty("steamVrAvailable").GetBoolean());
        Assert.False(completed.GetProperty("confirmationTimedOut").GetBoolean());
        Assert.Equal("allConfirmed", completed.GetProperty("outcome").GetString());
        Assert.Equal("skipped", events[2].GetProperty("burstDisposition").GetString());
    }

    [Fact]
    public async Task CheckAsync_UsesFullBurstFallback_WhenSteamVrIsUnavailable()
    {
        using var temp = new TempDirectory();
        var stations = Stations(2);
        var checker = CreateChecker(temp);
        var readerCalled = false;

        var result = await checker.CheckAsync(
            stations,
            enabled: true,
            () => (false, "OpenVR API unavailable"),
            _ =>
            {
                readerCalled = true;
                return Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>([]);
            },
            "wake-unavailable",
            pass: 1,
            burstCycle: 1,
            CancellationToken.None);
        var plan = SteamVrBurstRetryPlanner.Plan(result, stations);
        checker.WriteDisposition("burstSuppressionBypassed", result, "wake-unavailable", 1, 2, plan.Disposition);

        Assert.False(readerCalled);
        Assert.Equal(SteamVrBurstSuppressionOutcome.Unavailable, result.Outcome);
        Assert.Equal("fullBurst", plan.Disposition);
        Assert.Equal(stations, plan.Stations);
        var bypassed = Assert.Single(ReadEvents(temp), element => EventType(element) == "burstSuppressionBypassed");
        Assert.False(bypassed.GetProperty("steamVrAvailable").GetBoolean());
        Assert.Equal("fullBurst", bypassed.GetProperty("burstDisposition").GetString());
        Assert.Equal("unavailable", bypassed.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task CheckAsync_TargetsOnlyMissingStations_WhenSteamVrConfirmsPartialSet()
    {
        using var temp = new TempDirectory();
        var stations = Stations(3);
        var checker = CreateChecker(temp);

        var result = await checker.CheckAsync(
            stations,
            enabled: true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>(ReferencesFor([stations[0]])),
            "wake-partial",
            pass: 1,
            burstCycle: 1,
            CancellationToken.None);
        var plan = SteamVrBurstRetryPlanner.Plan(result, stations);
        checker.WriteDisposition(
            "partialBurstRetryStarted",
            result,
            "wake-partial",
            1,
            2,
            plan.Disposition,
            retryStationCount: plan.Stations.Length);
        checker.WriteDisposition(
            "partialBurstRetryCompleted",
            result,
            "wake-partial",
            1,
            2,
            plan.Disposition,
            retryStationCount: plan.Stations.Length,
            retrySuccessCount: plan.Stations.Length);

        Assert.Equal(SteamVrBurstSuppressionOutcome.PartialConfirmed, result.Outcome);
        Assert.Equal(1, result.ConfirmedActiveStationCount);
        Assert.Equal(2, result.MissingBluetoothAddresses.Length);
        Assert.Equal("subsetTargeted", plan.Disposition);
        Assert.Equal(stations.Skip(1).Select(station => station.BluetoothAddress), plan.Stations.Select(station => station.BluetoothAddress));
        var events = ReadEvents(temp);
        Assert.Contains(events, element => EventType(element) == "partialBurstRetryStarted");
        var completed = Assert.Single(events, element => EventType(element) == "partialBurstRetryCompleted");
        Assert.Equal(2, completed.GetProperty("retryStationCount").GetInt32());
        Assert.Equal(2, completed.GetProperty("retrySuccessCount").GetInt32());
        Assert.Equal("partialConfirmed", completed.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task CheckAsync_DoesNotSuppress_WhenActiveCountMatchesButIdentitiesDoNot()
    {
        using var temp = new TempDirectory();
        var stations = Stations(2);
        var wrongReferences = new[]
        {
            new SteamVrTrackingReference(1, ["LHB-WRONG001"]),
            new SteamVrTrackingReference(2, ["LHB-WRONG002"])
        };

        var result = await CreateChecker(temp).CheckAsync(
            stations,
            enabled: true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>(wrongReferences),
            "wake-mismatch",
            pass: 1,
            burstCycle: 1,
            CancellationToken.None);
        var plan = SteamVrBurstRetryPlanner.Plan(result, stations);

        Assert.Equal(SteamVrBurstSuppressionOutcome.PartialConfirmed, result.Outcome);
        Assert.Equal(0, result.ConfirmedActiveStationCount);
        Assert.False(result.SuppressBurst);
        Assert.Equal("fullBurst", plan.Disposition);
        Assert.Contains("count fallback is not eligible", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_UsesFullBurstFallback_WhenConfirmationTimesOut()
    {
        using var temp = new TempDirectory();
        var stations = Stations(2);
        var checker = CreateChecker(temp, TimeSpan.FromMilliseconds(10));
        var never = new TaskCompletionSource<IReadOnlyList<SteamVrTrackingReference>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var result = await checker.CheckAsync(
            stations,
            enabled: true,
            Available,
            _ => never.Task,
            "wake-timeout",
            pass: 1,
            burstCycle: 1,
            CancellationToken.None);
        var plan = SteamVrBurstRetryPlanner.Plan(result, stations);
        checker.WriteDisposition("burstSuppressionBypassed", result, "wake-timeout", 1, 2, plan.Disposition);

        Assert.Equal(SteamVrBurstSuppressionOutcome.TimedOut, result.Outcome);
        Assert.True(result.ConfirmationTimedOut);
        Assert.Equal("fullBurst", plan.Disposition);
        var completed = Assert.Single(ReadEvents(temp), element => EventType(element) == "steamVrBurstSuppressionCheckCompleted");
        Assert.True(completed.GetProperty("confirmationTimedOut").GetBoolean());
        Assert.Equal("timedOut", completed.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task CheckAsync_IsDisabledWithoutConfiguredStations()
    {
        using var temp = new TempDirectory();

        var result = await CreateChecker(temp).CheckAsync(
            [],
            enabled: true,
            Available,
            _ => Task.FromResult<IReadOnlyList<SteamVrTrackingReference>>([]),
            "wake-disabled",
            pass: 1,
            burstCycle: 1,
            CancellationToken.None);

        Assert.Equal(SteamVrBurstSuppressionOutcome.Disabled, result.Outcome);
        Assert.Empty(result.MissingBluetoothAddresses);
        Assert.Equal("fullBurst", SteamVrBurstRetryPlanner.Plan(result, []).Disposition);
    }

    private static SteamVrBurstSuppressionChecker CreateChecker(TempDirectory temp, TimeSpan? timeout = null)
        => new(
            new BaseStationDiagnosticSink(temp.Path, "Supervisor", "test"),
            timeout ?? TimeSpan.FromSeconds(1));

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
