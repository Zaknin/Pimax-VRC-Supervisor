using Xunit;

public sealed class PimaxConnectivityAssessmentTests
{
    [Fact]
    public void HealthyDevicesAndFreshRuntimeConnectedProducesConnected()
    {
        var result = PimaxConnectivityAssessment.Evaluate(
            Installed(),
            Processes("PimaxClient"),
            Services(),
            HealthyCrystalDevices(),
            Runtime(freshConnected: true),
            SteamVrDriver(PimaxProbeStatus.NotFound));

        Assert.Equal(PimaxConnectivityAssessmentValue.Connected, result.Value);
        Assert.Equal(PimaxConnectivityConfidence.Confirmed, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Contains("SteamVR driver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HealthyDevicesWithoutFreshRuntimeDoesNotProduceConnected()
    {
        var result = PimaxConnectivityAssessment.Evaluate(
            Installed(),
            Processes("PimaxClient"),
            Services(),
            HealthyCrystalDevices(),
            Runtime(freshConnected: false),
            SteamVrDriver(PimaxProbeStatus.Available));

        Assert.Equal(PimaxConnectivityAssessmentValue.WindowsDevicesPresentRuntimeNotConfirmed, result.Value);
        Assert.Equal(PimaxConnectivityConfidence.Probable, result.Confidence);
    }

    [Fact]
    public void StoppedTobiiServiceDoesNotOverrideConnected()
    {
        var result = PimaxConnectivityAssessment.Evaluate(
            Installed(),
            Processes("PimaxClient"),
            new PimaxServiceObservation(
                PimaxProbeStatus.Available,
                [new PimaxServiceInfo("Tobii VR4PIMAXP3B Platform Runtime", "Tobii VR4PIMAXP3B Platform Runtime", "STOPPED", "auto", null, @"C:\Program Files\Pimax\Runtime\EyeTrackingServer\platform_runtime\platform_runtime_VR4PIMAXP3B_service.exe", "OptionalEyeTrackingService")],
                [],
                []),
            HealthyCrystalDevices(),
            Runtime(freshConnected: true),
            SteamVrDriver(PimaxProbeStatus.Available));

        Assert.Equal(PimaxConnectivityAssessmentValue.Connected, result.Value);
    }

    [Fact]
    public void AirLinkOnlyDoesNotCountAsWiredCrystal()
    {
        var result = PimaxConnectivityAssessment.Evaluate(
            Installed(),
            Processes("PimaxClient"),
            Services(),
            new PimaxDeviceObservation(
                PimaxProbeStatus.Available,
                [],
                [Device("AuxiliaryAirLink", @"ROOT\PIMAXAIRLINK\<id>", "MEDIA", "Pimax AirLink")],
                WiredCrystalCompositePresent: false,
                WiredCrystalCompositeHealthy: false,
                HasRelevantProblem: false,
                MissingObservedHealthyInterfaceRoles: [],
                Warnings: [],
                Errors: []),
            Runtime(freshConnected: false),
            SteamVrDriver(PimaxProbeStatus.Available));

        Assert.Equal(PimaxConnectivityAssessmentValue.WindowsDevicesAbsent, result.Value);
    }

    [Fact]
    public void PnpProbeFailureProducesInsufficientEvidence()
    {
        var result = PimaxConnectivityAssessment.Evaluate(
            Installed(),
            Processes("PimaxClient"),
            Services(),
            new PimaxDeviceObservation(PimaxProbeStatus.Error, [], [], false, false, false, [], [], ["pnputil failed"]),
            Runtime(freshConnected: false),
            SteamVrDriver(PimaxProbeStatus.Available));

        Assert.Equal(PimaxConnectivityAssessmentValue.InsufficientEvidence, result.Value);
        Assert.Equal(PimaxConnectivityConfidence.Inconclusive, result.Confidence);
    }

    [Fact]
    public void InstalledButNoProcessesProducesClientNotRunning()
    {
        var result = PimaxConnectivityAssessment.Evaluate(
            Installed(),
            Processes(),
            Services(),
            HealthyCrystalDevices(),
            Runtime(freshConnected: false),
            SteamVrDriver(PimaxProbeStatus.Available));

        Assert.Equal(PimaxConnectivityAssessmentValue.PimaxClientNotRunning, result.Value);
    }

    [Fact]
    public void FreshRuntimeConnectedWithoutDevicesIsConflictingEvidence()
    {
        var result = PimaxConnectivityAssessment.Evaluate(
            Installed(),
            Processes("PimaxClient"),
            Services(),
            new PimaxDeviceObservation(PimaxProbeStatus.Available, [], [], false, false, false, [], [], []),
            Runtime(freshConnected: true),
            SteamVrDriver(PimaxProbeStatus.Available));

        Assert.Equal(PimaxConnectivityAssessmentValue.ConflictingEvidence, result.Value);
    }

    private static PimaxInstallationObservation Installed()
        => new(
            PimaxProbeStatus.Available,
            [new PimaxInstalledProduct("PimaxPlay", "1.43.9.272", "Pimax", @"C:\Program Files\Pimax\PimaxPlay", "hklm64Uninstall")],
            [@"C:\Program Files\Pimax"],
            [],
            []);

    private static PimaxProcessObservation Processes(params string[] names)
        => new(
            PimaxProbeStatus.Available,
            names.Select((name, index) => new PimaxProcessInfo(name, index + 10, null, $@"C:\Program Files\Pimax\Runtime\{name}.exe", "RuntimeProcess", null, null, null, null, null, null)).ToArray(),
            [],
            []);

    private static PimaxServiceObservation Services()
        => new(
            PimaxProbeStatus.Available,
            [new PimaxServiceInfo("PiServiceLauncher", "PiServiceLauncher", "RUNNING", "auto", 1234, @"C:\Program Files\Pimax\Runtime\PiServiceLauncher.exe", "CoreServiceCandidate")],
            [],
            []);

    private static PimaxDeviceObservation HealthyCrystalDevices()
        => new(
            PimaxProbeStatus.Available,
            [
                Device("CrystalCompositeRoot", @"USB\VID_34A4&PID_0012\<id>", "USB", "USB Composite Device"),
                Device("CrystalCameraInterface", @"USB\VID_34A4&PID_0012&MI_00\<id>", "Camera", "UVC Camera"),
                Device("CrystalHidInterface", @"USB\VID_34A4&PID_0012&MI_02\<id>", "HIDClass", "USB Input Device"),
                Device("CrystalAudioInterface", @"USB\VID_34A4&PID_0012&MI_03\<id>", "MEDIA", "AC Interface"),
                Device("CrystalAudioEndpoint", @"SWD\MMDEVAPI\<id>", "AudioEndpoint", "Pimax Streaming Microphone (AC Interface)")
            ],
            [],
            WiredCrystalCompositePresent: true,
            WiredCrystalCompositeHealthy: true,
            HasRelevantProblem: false,
            MissingObservedHealthyInterfaceRoles: [],
            Warnings: [],
            Errors: []);

    private static PimaxDeviceInfo Device(string role, string instanceId, string className, string friendlyName)
        => new(role, className, friendlyName, instanceId, [], "Started", null, null, null, null);

    private static PimaxRuntimeEvidenceObservation Runtime(bool freshConnected)
    {
        var now = DateTimeOffset.Now;
        var connected = freshConnected
            ? new PimaxRuntimeEvidenceEvent("PimaxClient", PimaxRuntimeEvidenceState.Connected, now.AddSeconds(-10), now, 10, true, "parsed", "HMD_hmdName: 'Pimax Crystal'")
            : null;
        return new PimaxRuntimeEvidenceObservation(
            connected is null ? PimaxProbeStatus.Inconclusive : PimaxProbeStatus.Available,
            now.AddMinutes(-5),
            300,
            connected is null ? [] : [connected],
            connected,
            null,
            [],
            []);
    }

    private static PimaxSteamVrDriverObservation SteamVrDriver(string status)
        => new(status, [], false, [], []);
}
