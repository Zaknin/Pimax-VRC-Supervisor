using System.Text.Json;
using Xunit;

public sealed class PimaxConnectivityProbeTests
{
    [Fact]
    public void ParsePnPDevicesClassifiesHealthyCrystalProfileAndAuxiliaryAirLink()
    {
        var observation = PimaxDeviceProbe.ParsePnPDevices(PnPHealthySample);

        Assert.True(observation.WiredCrystalCompositePresent);
        Assert.True(observation.WiredCrystalCompositeHealthy);
        Assert.Contains(observation.RelevantDevices, device => device.Role == "CrystalCompositeRoot");
        Assert.Contains(observation.RelevantDevices, device => device.Role == "CrystalCameraInterface");
        Assert.Contains(observation.RelevantDevices, device => device.Role == "CrystalHidInterface");
        Assert.Contains(observation.RelevantDevices, device => device.Role == "CrystalAudioInterface");
        Assert.Contains(observation.RelevantDevices, device => device.Role == "CrystalAudioEndpoint");
        Assert.Contains(observation.AuxiliaryDevices, device => device.Role == "AuxiliaryAirLink");
    }

    [Fact]
    public void ParsePnPDevicesRedactsSerialSuffixes()
    {
        var observation = PimaxDeviceProbe.ParsePnPDevices(PnPHealthySample);

        Assert.All(observation.RelevantDevices, device =>
        {
            Assert.DoesNotContain("5b4923c8", device.SanitizedInstanceId, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("1d8ca709", device.SanitizedInstanceId, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ParsePnPDevicesReportsMissingObservedHealthyInterfaces()
    {
        var observation = PimaxDeviceProbe.ParsePnPDevices("""
            Instance ID:                USB\VID_34A4&PID_0012\5b4923c8
            Device Description:         USB Composite Device
            Class Name:                 USB
            Manufacturer Name:          (Standard USB Host Controller)
            Status:                     Started
            """);

        Assert.True(observation.WiredCrystalCompositePresent);
        Assert.False(observation.WiredCrystalCompositeHealthy);
        Assert.Contains("CrystalCameraInterface", observation.MissingObservedHealthyInterfaceRoles);
    }

    [Fact]
    public void RuntimeLogParserUsesFreshnessWindow()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PimaxConnectivityProbeTests", Guid.NewGuid().ToString("N"));
        var piServiceRoot = Path.Combine(tempRoot, "PiService");
        Directory.CreateDirectory(piServiceRoot);
        try
        {
            File.WriteAllText(
                Path.Combine(piServiceRoot, "PiService__2026-06-15-17.log"),
                """
                2026-06-15 17:57:26.684 I 26704 [System::SetHidStatus] connected hmd name:Pimax Crystal
                """);
            var config = new SupervisorConfig
            {
                PimaxServiceLogDirectory = piServiceRoot
            };
            var offset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 6, 15, 17, 58, 0));

            var fresh = PimaxRuntimeEvidenceProbe.Collect(config, new DateTimeOffset(2026, 6, 15, 17, 58, 0, offset));
            var stale = PimaxRuntimeEvidenceProbe.Collect(config, new DateTimeOffset(2026, 6, 15, 18, 10, 0, offset));

            Assert.NotNull(fresh.FreshConnectedEvent);
            Assert.Null(stale.FreshConnectedEvent);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void MessageRedactionRemovesUserEmailSerialAndAccountIdentifiers()
    {
        var redacted = PimaxConnectivityRedactor.SanitizeMessage(
            @"C:\Users\Somebody\AppData\Roaming\PimaxClient {""email"":""person@example.com"",""userId"":114205,""SNCode"":""P30100P201382100320""} serial='LHR-9A72154F'");

        Assert.Contains("%USERPROFILE%", redacted);
        Assert.DoesNotContain("Somebody", redacted);
        Assert.DoesNotContain("person@example.com", redacted);
        Assert.DoesNotContain("114205", redacted);
        Assert.DoesNotContain("P30100P201382100320", redacted);
        Assert.DoesNotContain("LHR-9A72154F", redacted);
    }

    [Fact]
    public void SnapshotSerializesCamelCaseAndNoUnredactedInstanceId()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new PimaxConnectivitySnapshot(
            PimaxConnectivitySchema.Version,
            now,
            12.3,
            new PimaxInstallationObservation(PimaxProbeStatus.Available, [], [], [], []),
            new PimaxProcessObservation(PimaxProbeStatus.Available, [], [], []),
            new PimaxServiceObservation(PimaxProbeStatus.Available, [], [], []),
            PimaxDeviceProbe.ParsePnPDevices(PnPHealthySample),
            new PimaxRuntimeEvidenceObservation(PimaxProbeStatus.Inconclusive, now.AddMinutes(-5), 300, [], null, null, [], []),
            new PimaxSteamVrDriverObservation(PimaxProbeStatus.NotFound, [], false, [], []),
            new PimaxConnectivityAssessmentResult(PimaxConnectivityAssessmentValue.InsufficientEvidence, PimaxConnectivityConfidence.Inconclusive, "test", [], [], []),
            PimaxConnectivityConfidence.Inconclusive,
            [],
            []);

        var json = JsonSerializer.Serialize(snapshot, PimaxConnectivityJson.Options);

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"sanitizedInstanceId\"", json);
        Assert.DoesNotContain("5b4923c8", json, StringComparison.OrdinalIgnoreCase);
    }

    private const string PnPHealthySample = """
        Instance ID:                USB\VID_34A4&PID_0012\5b4923c8
        Device Description:         USB Composite Device
        Class Name:                 USB
        Manufacturer Name:          (Standard USB Host Controller)
        Status:                     Started

        Instance ID:                USB\VID_34A4&PID_0012&MI_00\8&1d8ca709&3&0000
        Device Description:         UVC Camera
        Class Name:                 Camera
        Manufacturer Name:          Microsoft
        Status:                     Started

        Instance ID:                USB\VID_34A4&PID_0012&MI_02\8&1d8ca709&3&0002
        Device Description:         USB Input Device
        Class Name:                 HIDClass
        Manufacturer Name:          (Standard system devices)
        Status:                     Started

        Instance ID:                USB\VID_34A4&PID_0012&MI_03\8&1d8ca709&3&0003
        Device Description:         AC Interface
        Class Name:                 MEDIA
        Manufacturer Name:          (Generic USB Audio)
        Status:                     Started

        Instance ID:                SWD\MMDEVAPI\{0.0.1.00000000}.{3e35e922-4be4-4656-a63a-a6782bbf93b5}
        Device Description:         Pimax Streaming Microphone (AC Interface)
        Class Name:                 AudioEndpoint
        Manufacturer Name:          Microsoft
        Status:                     Started

        Instance ID:                ROOT\PimaxAirLink\0000
        Device Description:         Pimax AirLink
        Class Name:                 MEDIA
        Manufacturer Name:          Pimax Technologies
        Status:                     Started
        """;
}
