using System.Text.Json;
using PimaxVrcSupervisor.BaseStations;
using Xunit;

public sealed class BaseStationDiagnosticsTests
{
    [Fact]
    public void DiagnosticEventSerializesSchemaAndRequiredFields()
    {
        using var temp = new TempDirectory();
        var sink = new BaseStationDiagnosticSink(temp.Path, "Supervisor", "test");

        sink.WriteEvent("burstStarted", "SteamVR autostart", configuredStationCount: 4, currentStage: "burst", outcome: "started");

        var document = ReadSingleEvent(sink.ActivePath);
        Assert.Equal(BaseStationStartupDiagnosticsSchema.Version, document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("Supervisor", document.RootElement.GetProperty("process").GetString());
        Assert.Equal("burstStarted", document.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("SteamVR autostart", document.RootElement.GetProperty("trigger").GetString());
        Assert.True(document.RootElement.TryGetProperty("timestampUtc", out _));
        Assert.True(document.RootElement.TryGetProperty("elapsedMilliseconds", out _));
    }

    [Fact]
    public void StationIdentityDoesNotLeakBluetoothAddress()
    {
        var station = Station("AA:BB:CC:DD:EE:FF");

        var identity = BaseStationDiagnosticSink.StationIdentity(station);
        var label = BaseStationDiagnosticSink.StationLabel(station);

        Assert.DoesNotContain("AA", identity, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AA:BB:CC:DD:EE:FF", label, StringComparison.OrdinalIgnoreCase);
        Assert.False(BaseStationDiagnosticSink.ContainsBluetoothAddress(identity));
        Assert.False(BaseStationDiagnosticSink.ContainsBluetoothAddress(label));
    }

    [Fact]
    public void SanitizedErrorMessageRemovesBluetoothAddressAndControls()
    {
        var message = BaseStationDiagnosticSink.SanitizeMessage("failed for AA:BB:CC:DD:EE:FF\r\nnext");

        Assert.Equal("failed for [redacted-address] next", message);
        Assert.False(BaseStationDiagnosticSink.ContainsBluetoothAddress(message));
    }

    [Fact]
    public void ObservationTrackerReportsObservationAge()
    {
        BaseStationObservationTracker.ClearForTests();
        var station = Station("AA:BB:CC:DD:EE:FF");
        var observedAt = DateTimeOffset.UtcNow.AddSeconds(-2);

        BaseStationObservationTracker.Record(station, observedAt);

        var age = BaseStationObservationTracker.TryGetObservationAge(station, DateTimeOffset.UtcNow);
        Assert.NotNull(age);
        Assert.True(age.Value >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void OperationTimeoutRecordsLastActiveStage()
    {
        using var temp = new TempDirectory();
        var sink = new BaseStationDiagnosticSink(temp.Path, "Supervisor", "test");
        var operation = sink.CreateOperation(
            "SteamVR autostart",
            Station("AA:BB:CC:DD:EE:FF"),
            burstNumber: 1,
            retryNumber: 0,
            configuredStationCount: 1,
            TimeSpan.FromSeconds(8));

        operation.BeginStage("gattServiceQuery");
        operation.TimedOut(new TimeoutException("Bluetooth power-on command did not finish within 8 seconds. Stage: gattServiceQuery."));

        var events = File.ReadAllLines(sink.ActivePath)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        var timeout = events.Select(doc => doc.RootElement).Single(element => element.GetProperty("eventType").GetString() == "stationAttemptTimedOut");
        Assert.Equal("gattServiceQuery", timeout.GetProperty("currentStage").GetString());
        Assert.Equal("timeout", timeout.GetProperty("outcome").GetString());
        Assert.False(BaseStationDiagnosticSink.ContainsBluetoothAddress(timeout.GetRawText()));
    }

    [Fact]
    public void DiagnosticSinkRotatesAndRetainsBoundedFiles()
    {
        using var temp = new TempDirectory();
        var sink = new BaseStationDiagnosticSink(temp.Path, "Supervisor", "test");
        File.WriteAllText(sink.ActivePath, new string('x', (int)BaseStationDiagnosticSink.MaxActiveBytes + 1));

        sink.WriteEvent("burstStarted");

        Assert.True(File.Exists(sink.ActivePath));
        Assert.True(File.Exists(sink.ActivePath + ".1"));
        Assert.False(File.Exists(sink.ActivePath + ".4"));
    }

    [Fact]
    public void DiagnosticSinkSupportsConcurrentSameProcessWrites()
    {
        using var temp = new TempDirectory();
        var sink = new BaseStationDiagnosticSink(temp.Path, "Supervisor", "test");

        Parallel.For(0, 20, index => sink.WriteEvent("stationAttemptQueued", operationId: "op-" + index));

        var lines = File.ReadAllLines(sink.ActivePath);
        Assert.Equal(20, lines.Length);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal(BaseStationStartupDiagnosticsSchema.Version, document.RootElement.GetProperty("schemaVersion").GetString());
        }
    }

    private static JsonDocument ReadSingleEvent(string path)
        => JsonDocument.Parse(Assert.Single(File.ReadAllLines(path)));

    private static BaseStationDevice Station(string address)
        => new()
        {
            Name = "LHB-TEST0001",
            FriendlyName = "Test Station",
            BluetoothAddress = address,
            Version = BaseStationVersion.V2,
            Enabled = true
        };
}
