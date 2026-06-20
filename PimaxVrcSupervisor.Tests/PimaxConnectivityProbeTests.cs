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
        using var freshTemp = TempPiServiceLog(DateTimeOffset.Now.AddSeconds(-30), "connected hmd name:Pimax Crystal");
        using var staleTemp = TempPiServiceLog(DateTimeOffset.Now.AddMinutes(-10), "connected hmd name:Pimax Crystal");

        var fresh = PimaxRuntimeEvidenceProbe.Collect(freshTemp.Config, DateTimeOffset.Now, includePimaxClientLogs: false);
        var stale = PimaxRuntimeEvidenceProbe.Collect(staleTemp.Config, DateTimeOffset.Now, includePimaxClientLogs: false);

        var freshPiServiceEvent = Assert.Single(fresh.Events, IsTempPiServiceConnectedEvent);
        var stalePiServiceEvent = Assert.Single(stale.Events, IsTempPiServiceConnectedEvent);
        Assert.True(freshPiServiceEvent.IsFresh);
        Assert.False(stalePiServiceEvent.IsFresh);
    }

    [Fact]
    public void RuntimeLogParserClampsSlightFutureEventAgeAndKeepsItFresh()
    {
        var eventTime = DateTimeOffset.Now.AddSeconds(2);
        using var temp = TempPiServiceLog(eventTime, "connected hmd name:Pimax Crystal");

        var evidence = PimaxRuntimeEvidenceProbe.Collect(temp.Config, DateTimeOffset.Now, includePimaxClientLogs: false);
        var ev = Assert.Single(evidence.Events, IsTempPiServiceConnectedEvent);

        Assert.True(ev.IsFresh);
        Assert.True(ev.EventAgeSeconds >= 0);
    }

    [Fact]
    public void RuntimeLogParserRejectsImplausiblyFutureEventAsFresh()
    {
        var eventTime = DateTimeOffset.Now.AddSeconds(45);
        using var temp = TempPiServiceLog(eventTime, "connected hmd name:Pimax Crystal");

        var evidence = PimaxRuntimeEvidenceProbe.Collect(temp.Config, DateTimeOffset.Now, includePimaxClientLogs: false);
        var ev = Assert.Single(evidence.Events, IsTempPiServiceConnectedEvent);

        Assert.False(ev.IsFresh);
        Assert.True(ev.EventAgeSeconds >= 0);
    }

    [Fact]
    public void RuntimeLogParserKeepsNormalPastEventAgeAndFreshnessWindow()
    {
        var eventTime = DateTimeOffset.Now.AddSeconds(-120);
        using var temp = TempPiServiceLog(eventTime, "connected hmd name:Pimax Crystal");

        var evidence = PimaxRuntimeEvidenceProbe.Collect(temp.Config, DateTimeOffset.Now, includePimaxClientLogs: false);
        var ev = Assert.Single(evidence.Events, IsTempPiServiceConnectedEvent);

        Assert.True(ev.IsFresh);
        Assert.InRange(ev.EventAgeSeconds ?? -1, 110, 130);
        Assert.Equal(300, evidence.FreshnessWindowSeconds);
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

    private static TempRuntimeLog TempPiServiceLog(DateTimeOffset eventTime, string marker)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PimaxConnectivityProbeTests", Guid.NewGuid().ToString("N"));
        var piServiceRoot = Path.Combine(tempRoot, "PiService");
        Directory.CreateDirectory(piServiceRoot);
        File.WriteAllText(
            Path.Combine(piServiceRoot, "PiService__2026-06-15-17.log"),
            $"{eventTime.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff} I 26704 [System::SetHidStatus] {marker}{Environment.NewLine}");
        return new TempRuntimeLog(
            tempRoot,
            new SupervisorConfig
            {
                PimaxServiceLogDirectory = piServiceRoot
            });
    }

    private static bool IsTempPiServiceConnectedEvent(PimaxRuntimeEvidenceEvent ev)
        => ev.Source == "PiService"
            && ev.SanitizedMessage.Contains("connected hmd name", StringComparison.OrdinalIgnoreCase);

    private sealed class TempRuntimeLog(string root, SupervisorConfig config) : IDisposable
    {
        public SupervisorConfig Config { get; } = config;

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
