using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Xunit;

public sealed class PimaxUsbPhysicalPortMappingTests
{
    [Fact]
    public void RequestParsesSnapshotAndBoundedObservationOptions()
    {
        var snapshot = PimaxUsbPhysicalPortMapRequest.Parse(["pimax-usb-physical-port-map-json"]);
        var observation = PimaxUsbPhysicalPortMapRequest.Parse(["--duration-seconds", "9999", "--sample-interval-ms=1", "--scenario", "unicode ✓", "--output-dir", @"C:\evidence path", "--marker-file", @"C:\markers path\m.jsonl"]);

        Assert.False(snapshot.ObservationMode);
        Assert.True(observation.ObservationMode);
        Assert.Equal(1800, observation.DurationSeconds);
        Assert.Equal(250, observation.SampleIntervalMilliseconds);
        Assert.Equal("unicode ✓", observation.ScenarioId);
        Assert.Equal(@"C:\evidence path", observation.OutputDirectory);
    }

    [Fact]
    public void ConnectorParserReadsVariableLengthCompanionLink()
    {
        var link = @"USB#VID_0424&PID_5537#sample#{hub-guid}";
        var bytes = new byte[16 + Encoding.Unicode.GetByteCount(link + "\0")];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 2);
        Encoding.Unicode.GetBytes(link + "\0").CopyTo(bytes, 16);

        var parsed = PimaxUsbNativeBufferParser.ParseConnectorProperties(bytes);

        Assert.Equal((ushort)2, parsed.CompanionPort);
        Assert.Equal(link, parsed.CompanionHub);
        Assert.Equal((uint)3, parsed.Properties);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void ConnectorParserRejectsTruncatedBuffers(int length)
        => Assert.Throws<InvalidDataException>(() => PimaxUsbNativeBufferParser.ParseConnectorProperties(new byte[length]));

    [Fact]
    public void ConnectorParserRejectsUnreasonableActualLength()
    {
        var bytes = new byte[18];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 70000);
        Assert.Throws<InvalidDataException>(() => PimaxUsbNativeBufferParser.ParseConnectorProperties(bytes));
    }

    [Fact]
    public void VariableNameParserHandlesUnicodeAndRejectsOddLength()
    {
        var text = "路径 with spaces";
        var bytes = new byte[8 + Encoding.Unicode.GetByteCount(text + "\0")];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        Encoding.Unicode.GetBytes(text + "\0").CopyTo(bytes, 8);
        Assert.Equal(text, PimaxUsbNativeBufferParser.ParseVariableName(bytes, "name"));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 1));
        Assert.Throws<InvalidDataException>(() => PimaxUsbNativeBufferParser.ParseVariableName(bytes, "name"));
    }

    [Fact]
    public void ConnectionV2ParserValidatesLayoutAndProtocols()
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 3);

        var parsed = PimaxUsbNativeBufferParser.ParseConnectionInformationV2(bytes);

        Assert.Equal(["USB 1.1", "USB 2.0", "USB 3.x"], parsed.Protocols);
        Assert.Equal((uint)3, parsed.Flags);
        Assert.Equal(16, PimaxUsbNativeBufferParser.ConnectionInformationV2Length);
        Assert.Equal(35, PimaxUsbNativeBufferParser.ConnectionInformationExMinimumLength);
    }

    [Fact]
    public void ReciprocalCompanionsFormOneApiConfirmedGroup()
    {
        var usb2 = Port("hub2", @"\\?\USB#hub2", 2, "pimax-related", [new(0, 2, @"USB#hub3", true)]);
        var usb3 = Port("hub3", @"\\?\USB#hub3", 2, "empty-or-unresolved", [new(0, 2, @"USB#hub2", true)]);

        var group = Assert.Single(PimaxUsbConnectorGrouper.Group([usb2, usb3]));

        Assert.True(group.ApiConfirmed);
        Assert.Equal("high", group.Confidence);
        Assert.Equal("pimax-related", group.OccupantClassification);
        Assert.Equal(2, group.PortIds.Length);
    }

    [Fact]
    public void OneSidedCompanionMetadataIsReportedButNotApiConfirmed()
    {
        var first = Port("hub2", @"\\?\USB#hub2", 1, "empty-or-unresolved", [new(0, 1, @"USB#hub3", false)]);
        var second = Port("hub3", @"\\?\USB#hub3", 1);

        var group = Assert.Single(PimaxUsbConnectorGrouper.Group([first, second]));

        Assert.False(group.ApiConfirmed);
        Assert.Contains(group.ContraryEvidence, value => value.Contains("not reciprocal"));
    }

    [Fact]
    public void MissingOrContradictoryCompanionDoesNotGuessAGroup()
    {
        var missing = Port("hub2", @"\\?\USB#hub2", 1, companions: [new(0, 7, @"USB#missing", false)]);
        var unrelated = Port("hub3", @"\\?\USB#hub3", 1);

        var groups = PimaxUsbConnectorGrouper.Group([missing, unrelated]);

        Assert.Equal(2, groups.Length);
        Assert.Contains(groups, group => group.ContraryEvidence.Any(value => value.Contains("did not resolve")));
    }

    [Fact]
    public void ObservationInferenceRequiresAncestryAndRepeatedSynchronizedChanges()
    {
        var first = Port("hub2", @"\\?\USB#hub2", 2);
        var second = Port("hub3", @"\\?\USB#hub3", 2);
        var transitions = new[]
        {
            Transition(first.PortId, 1000), Transition(second.PortId, 1100),
            Transition(first.PortId, 5000), Transition(second.PortId, 5100)
        };

        Assert.True(PimaxUsbConnectorGrouper.SupportsObservationInference(first, second, transitions, true));
        Assert.False(PimaxUsbConnectorGrouper.SupportsObservationInference(first, second, transitions, false));
    }

    [Theory]
    [InlineData("USB\\VID_34A4&PID_0012\\serial", "pimax-related")]
    [InlineData("USB\\VID_05E3&PID_0608\\branch", "pimax-related")]
    [InlineData("USB\\VID_2104&PID_0220\\eyechip", "pimax-related")]
    [InlineData("USB\\VID_1234&PID_5678\\HTC-Vive-Face-Tracker", "vive-face-tracker-related")]
    [InlineData("USB\\VID_1234&PID_5678\\other", "unrelated-or-unresolved")]
    public void OccupantClassificationUsesObservedIdentityAndAncestry(string id, string expected)
        => Assert.Equal(expected, PimaxUsbOccupantClassifier.Classify([RawDevice(id)]));

    [Fact]
    public void SevenPortCountAloneCannotSelectExternalHub()
    {
        var hub = Hub("seven", 7);
        var candidate = Assert.Single(PimaxUsbHubCandidateSelector.Rank([hub], [], []));
        Assert.True(candidate.Score < 5);
        Assert.Contains(candidate.ContraryEvidence, value => value.Contains("both independently"));
    }

    [Fact]
    public void HubWithPimaxViveLocationAndSevenPortsRanksHigh()
    {
        var hub = Hub("external", 7, ["PCIROOT#USBROOT#USB"]);
        var p = Port("external", "external", 2, "pimax-related") with { PhysicalConnectorGroupId = "p" };
        var v = Port("external", "external", 4, "vive-face-tracker-related") with { PhysicalConnectorGroupId = "v" };
        var groups = new[] { Group("p", p), Group("v", v) };

        var candidate = Assert.Single(PimaxUsbHubCandidateSelector.Rank([hub], [p, v], groups));

        Assert.Equal("high", candidate.Confidence);
        Assert.True(candidate.Score >= 7);
    }

    [Fact]
    public void SameVidPidOnMultiplePortsDoesNotMergeWithoutCompanionMetadata()
    {
        var first = Port("hub", "hub", 1, "pimax-related");
        var second = Port("hub", "hub", 2, "pimax-related");
        Assert.Equal(2, PimaxUsbConnectorGrouper.Group([first, second]).Length);
    }

    [Fact]
    public void TimelineCapturesPartialReconnectAndPortIndexChange()
    {
        var oldPort = Port("hub", "hub", 2, "pimax-related") with { DriverKey = "old" };
        var prior = new Dictionary<string, PimaxUsbPortRecord>(StringComparer.OrdinalIgnoreCase) { [oldPort.PortId] = oldPort };
        var timeline = new List<PimaxUsbPortTransition>();
        var newPort = Port("hub", "hub", 3, "pimax-related") with { DriverKey = "new" };

        PimaxUsbPhysicalPortMapper.TrackPorts(prior, [newPort], timeline, DateTimeOffset.Now, TimeSpan.FromSeconds(1));

        Assert.Contains(timeline, item => item.TransitionType == "portDisappeared");
        Assert.Contains(timeline, item => item.TransitionType == "portAppeared");
    }

    [Fact]
    public void ObserverProtocolRequiresObserverThenReadinessThenActionThenResult()
    {
        var valid = new[]
        {
            Marker("observer-start", "observer", 0), Marker("ready-before-action", "readiness", 1),
            Marker("action-completed", "action", 2), Marker("result-observed", "result", 3)
        };
        var invalid = new[] { Marker("action-completed", "action", 1), Marker("observer-start", "observer", 2) };

        Assert.Empty(PimaxUsbPhysicalPortProtocol.Validate(valid));
        Assert.Contains(PimaxUsbPhysicalPortProtocol.Validate(invalid), value => value.Contains("readiness"));
    }

    [Fact]
    public void OutputSerializesAsExactlyOneJsonDocument()
    {
        var snapshot = EmptySnapshot();
        var result = new PimaxUsbPhysicalPortMapResult(PimaxUsbPhysicalPortMapSchema.Version, "session", "路径 with spaces", "snapshot", DateTimeOffset.Now, DateTimeOffset.Now, 0, 500, null, @"C:\path with spaces\app.dll", "hash", null, snapshot, snapshot, [], [], [], [], [], [], [], [], [], false);
        var json = JsonSerializer.Serialize(result, PimaxUsbPhysicalPortMapJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(PimaxUsbPhysicalPortMapSchema.Version, document.RootElement.GetProperty("schema").GetString());
        Assert.Equal("路径 with spaces", document.RootElement.GetProperty("scenarioId").GetString());
    }

    [Fact]
    public void PhaseImplementationContainsNoMutationApiOrDeploymentPath()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "PimaxUsbPhysicalPortMapping.cs"));
        var forbidden = new[] { "IOCTL_USB_HUB_CYCLE_PORT", "CM_Reenumerate", "SetupDiCallClassInstaller", "pnputil", "devcon", "Phase29B-d347151", "TerminateProcess", "SendInput" };
        foreach (var value in forbidden) Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase);
    }

    private static PimaxUsbPortTransition Transition(string id, double elapsed)
        => new(elapsed, DateTimeOffset.Now, id, "deviceConnected", "noDeviceConnected", "key", null, "occupancyChanged");

    private static PimaxUsbPhysicalPortMarker Marker(string label, string phase, double elapsed)
        => new("session", "scenario", DateTimeOffset.UtcNow, DateTimeOffset.Now, elapsed, label, phase, "test", null);

    private static PimaxUsbPortRecord Port(string hubId, string hubPath, int index, string classification = "empty-or-unresolved", PimaxUsbCompanionReference[]? companions = null)
        => new(PimaxUsbPhysicalPortSnapshotCollector.PortId(hubId, index), hubId, hubPath, index, "deviceConnected", "high", ["USB 2.0"], true, false, 0, 0, 0, 0, 0, 0, null, null, null, [], [], [], null, "userConnectable", false, companions ?? [], null, classification, [], DateTimeOffset.Now, DateTimeOffset.Now, [], []);

    private static PimaxUsbHubRecord Hub(string id, int ports, string[]? locations = null)
        => new(id, id, id, "USB\\hub", "USB\\parent", "container", "driver", [], [], "9999", "0001", "USB", "guid", "vendor", "hub", null, null, locations ?? [], ["USB 2.0"], "usb20", false, true, ports, DateTimeOffset.Now, [], []);

    private static PimaxUsbPhysicalConnectorGroup Group(string id, PimaxUsbPortRecord port)
        => new(id, [port.PortId], [], [], "high", true, false, port.OccupantClassification, [], false);

    private static PimaxUsbRawDeviceRecord RawDevice(string id)
        => new(id, null, null, "USB", true, true, false, "USB", null, null, null, null, null, null, null, null, "Started", null, "Started", [id], [], null, null, null, null, null, [], [], "test");

    private static PimaxUsbPhysicalPortSnapshot EmptySnapshot()
    {
        var counts = new Dictionary<string, int>();
        var pnp = new PimaxUsbEnumerationSnapshot(PimaxUsbEnumerationSchema.Version, DateTimeOffset.Now, "test", new("Windows", "X64", false), new(0, 0, 0, 0, counts, counts, counts, counts, counts), [], [], [], []);
        return new(DateTimeOffset.Now, [], [], [], [], null, null, null, pnp, [], []);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
