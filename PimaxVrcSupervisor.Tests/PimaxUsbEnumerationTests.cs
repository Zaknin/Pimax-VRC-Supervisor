using System.Text.Json;
using Xunit;

public sealed class PimaxUsbEnumerationTests
{
    [Fact]
    public void EmptyInventorySerializesRequiredSchemaAndFields()
    {
        var snapshot = new PimaxUsbEnumerationSnapshotCollector(new FakeInventorySource([])).Collect();
        var json = JsonSerializer.Serialize(snapshot, PimaxUsbEnumerationJson.Options);

        Assert.Equal(PimaxUsbEnumerationSchema.Version, snapshot.SchemaVersion);
        Assert.Equal(0, snapshot.InventorySummary.TotalDevices);
        Assert.Empty(snapshot.CandidateDevices);
        Assert.Contains("\"schemaVersion\":\"pimax-usb-enumeration-v1\"", json);
        Assert.Contains("\"inventorySummary\"", json);
        Assert.Contains("\"candidateDevices\"", json);
        Assert.Contains("\"fullInventory\"", json);
        Assert.Contains("\"warnings\"", json);
        Assert.Contains("\"errors\"", json);
    }

    [Fact]
    public void IdentitySanitizerIsDeterministicAndDoesNotExposeRawSerial()
    {
        const string first = @"USB\VID_34A4&PID_0012\P30100P201382100320";
        const string second = @"USB\VID_34A4&PID_0012\DIFFERENT";

        var firstHash = PnpIdentitySanitizer.StableHash(first);
        var repeatedHash = PnpIdentitySanitizer.StableHash(first.ToLowerInvariant());
        var secondHash = PnpIdentitySanitizer.StableHash(second);

        Assert.Equal(firstHash, repeatedHash);
        Assert.NotEqual(firstHash, secondHash);
        Assert.DoesNotContain("P30100P201382100320", firstHash, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("sha256:", firstHash);
        Assert.Null(PnpIdentitySanitizer.StableHashOrNull(null));
    }

    [Fact]
    public void KnownCrystalVidPidSelectsCandidateWithoutRawIdentifierLeak()
    {
        var raw = Raw(instanceId: @"USB\VID_34A4&PID_0012\P30100P201382100320", deviceClass: "USB");
        var snapshot = new PimaxUsbEnumerationSnapshotCollector(new FakeInventorySource([raw])).Collect();
        var candidate = Assert.Single(snapshot.CandidateDevices);
        var json = JsonSerializer.Serialize(snapshot, PimaxUsbEnumerationJson.Options);

        Assert.Contains("knownCrystalVidPid", candidate.CandidateReasons);
        Assert.Contains("usbDeviceWithRelevantInterfaceClass", candidate.CandidateReasons);
        Assert.Equal("34A4", candidate.Vid);
        Assert.Equal("0012", candidate.Pid);
        Assert.DoesNotContain("P30100P201382100320", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CandidateSelectionUsesNameInterfaceAndProblemEvidence()
    {
        var raw = Raw(
            instanceId: @"HID\VID_9999&PID_8888\FAKE123",
            deviceClass: "HIDClass",
            friendlyName: "Pimax Controller Interface",
            problemCode: 28,
            status: "Problem");

        var candidate = Assert.Single(new PimaxUsbEnumerationSnapshotCollector(new FakeInventorySource([raw])).Collect().CandidateDevices);

        Assert.Contains("knownPimaxName", candidate.CandidateReasons);
        Assert.Contains("relatedHidInterface", candidate.CandidateReasons);
        Assert.Contains("problemUsbOrPnpDevice", candidate.CandidateReasons);
        Assert.Equal(28, candidate.ProblemCode);
    }

    [Fact]
    public void SummaryCountsPresentNonPresentProblemDuplicateRows()
    {
        var devices = new[]
        {
            Raw(instanceId: @"USB\VID_34A4&PID_0012\ONE", deviceClass: "USB"),
            Raw(instanceId: @"USB\VID_34A4&PID_0012\ONE", deviceClass: "USB"),
            Raw(instanceId: @"USB\VID_34A4&PID_0012\TWO", deviceClass: "USB", present: false, phantom: true, connected: false, status: "NonPresent"),
            Raw(instanceId: @"USB\VID_34A4&PID_0012\THREE", deviceClass: "USB", problemCode: 10, status: "Problem")
        };

        var summary = new PimaxUsbEnumerationSnapshotCollector(new FakeInventorySource(devices)).Collect().InventorySummary;

        Assert.Equal(4, summary.TotalDevices);
        Assert.Equal(3, summary.PresentDevices);
        Assert.Equal(1, summary.NonPresentDevices);
        Assert.Equal(1, summary.ProblemDevices);
        Assert.Equal(4, summary.CountsByEnumerator["USB"]);
        Assert.Equal(4, summary.CountsByVidPid["VID_34A4&PID_0012"]);
    }

    [Fact]
    public void ParentContainerAndLocationPathsAreSanitizedButStable()
    {
        var raw = Raw(
            instanceId: @"USB\VID_34A4&PID_0012\CHILD",
            parentInstanceId: @"USB\ROOT_HUB30\ROOTSERIAL",
            containerId: "{11111111-2222-3333-4444-555555555555}",
            locationPaths: [@"PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)"]);

        var record = Assert.Single(new PimaxUsbEnumerationSnapshotCollector(new FakeInventorySource([raw])).Collect().FullInventory);

        Assert.NotNull(record.ParentStableId);
        Assert.NotNull(record.ContainerStableId);
        Assert.Single(record.LocationPathHashes);
        Assert.DoesNotContain("ROOTSERIAL", record.ParentStableId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PCIROOT", record.LocationPathHashes[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarningsAndErrorsFlowIntoSnapshot()
    {
        var snapshot = new PimaxUsbEnumerationSnapshotCollector(
            new FakeInventorySource([], ["warning"], ["error"]))
            .Collect();

        Assert.Equal(["warning"], snapshot.Warnings);
        Assert.Equal(["error"], snapshot.Errors);
    }

    [Fact]
    public void ExistingConnectivitySchemaConstantRemainsUnchanged()
    {
        Assert.Equal("pimax-connectivity-v1", PimaxConnectivitySchema.Version);
        Assert.Equal("pimax-usb-enumeration-v1", PimaxUsbEnumerationSchema.Version);
    }

    private static PimaxUsbRawDeviceRecord Raw(
        string instanceId,
        string? parentInstanceId = null,
        string? containerId = null,
        string enumeratorName = "USB",
        bool present = true,
        bool connected = true,
        bool phantom = false,
        string? deviceClass = "USB",
        string? friendlyName = "Fake Pimax Device",
        string? deviceDescription = "Fake Pimax Device",
        string? manufacturer = "Pimax",
        string? status = "Started",
        int? problemCode = null,
        string[]? hardwareIds = null,
        string[]? compatibleIds = null,
        string? vid = "34A4",
        string? pid = "0012",
        string[]? locationPaths = null)
        => new(
            instanceId,
            parentInstanceId,
            containerId,
            enumeratorName,
            present,
            connected,
            phantom,
            deviceClass,
            "{FAKE-CLASS-GUID}",
            friendlyName,
            deviceDescription,
            manufacturer,
            "fake-service",
            null,
            null,
            null,
            status,
            problemCode,
            problemCode is > 0 ? "Problem" : status,
            hardwareIds ?? [@"USB\VID_34A4&PID_0012"],
            compatibleIds ?? [],
            vid,
            pid,
            null,
            null,
            "Port_#0001.Hub_#0002",
            locationPaths ?? [],
            [],
            "FakeSource");

    private sealed class FakeInventorySource(
        PimaxUsbRawDeviceRecord[] devices,
        string[]? warnings = null,
        string[]? errors = null) : IPimaxUsbDeviceInventorySource
    {
        public PimaxUsbInventoryResult Collect()
            => new(devices, warnings ?? [], errors ?? []);
    }
}
