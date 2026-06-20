using System.Text.Json;
using Xunit;

public sealed class PimaxComponentHealthTests
{
    [Fact]
    public async Task AllRequiredComponentsPresentProducesHealthyCapabilitySummary()
    {
        var snapshot = await Health(Connectivity(PimaxConnectivityAssessmentValue.Connected), Usb(AllDevices()));

        Assert.Equal(PimaxHealthOverallStatus.Healthy, snapshot.OverallStatus);
        Assert.Equal("available", snapshot.CapabilitySummary.CoreVr);
        Assert.Equal("ready", snapshot.CapabilitySummary.PimaxRegistration);
        Assert.Contains(snapshot.Components, component => component.ComponentId == "eyeChip" && component.Status == PimaxHealthComponentStatus.Present);
    }

    [Fact]
    public async Task UsbPresentButRegistrationAbsentProducesNotRegisteredMessage()
    {
        var snapshot = await Health(Connectivity(PimaxConnectivityAssessmentValue.WindowsDevicesPresentRuntimeNotConfirmed), Usb(PowerOnGroup()));

        Assert.Equal(PimaxHealthOverallStatus.NotRegistered, snapshot.OverallStatus);
        Assert.Contains("Windows detects the Pimax USB stack", snapshot.HumanReadableSummary);
        Assert.Contains(snapshot.Components, component => component.ComponentId == "pimaxRegistration" && component.ReasonCode == "windows_usb_present_pimax_unregistered");
    }

    [Fact]
    public async Task CoreUsbMissingBlocksRegistration()
    {
        var snapshot = await Health(Connectivity(PimaxConnectivityAssessmentValue.WindowsDevicesAbsent), Usb([]));

        Assert.Equal(PimaxHealthOverallStatus.CoreConnectionMissing, snapshot.OverallStatus);
        Assert.Contains("core Pimax USB interface is not detected", snapshot.Components.Single(component => component.ComponentId == "coreUsb").Explanation);
    }

    [Theory]
    [InlineData("34A4", "0012", "superSpeedCompanion", "The Pimax SuperSpeed connection is missing")]
    [InlineData("2104", "0220", "eyeChip", "EyeChip is not detected")]
    public async Task MissingFeatureComponentsProduceDeterministicMessages(string vidToRemove, string pidToRemove, string componentId, string expected)
    {
        var devices = AllDevices().Where(record => !(record.Vid == vidToRemove && record.Pid == pidToRemove)).ToArray();
        var snapshot = await Health(Connectivity(PimaxConnectivityAssessmentValue.Connected), Usb(devices));

        var component = snapshot.Components.Single(item => item.ComponentId == componentId);
        Assert.Equal(PimaxHealthComponentStatus.Missing, component.Status);
        Assert.Contains(expected, component.Explanation);
    }

    [Fact]
    public async Task DisplayAudioAndMicrophoneMissingAreReportedSeparately()
    {
        var devices = PowerOnGroup().Concat(RuntimeGroup()).ToArray();
        var snapshot = await Health(Connectivity(PimaxConnectivityAssessmentValue.Connected), Usb(devices));

        Assert.Contains("no image", snapshot.Components.Single(component => component.ComponentId == "displayPortVideo").Explanation);
        Assert.Contains("no sound", snapshot.Components.Single(component => component.ComponentId == "headsetAudioOutput").Explanation);
        Assert.Contains("microphone is not available", snapshot.Components.Single(component => component.ComponentId == "headsetMicrophone").Explanation);
    }

    [Fact]
    public async Task MissingViveFaceTrackerDoesNotMakeHeadsetUnusable()
    {
        var snapshot = await Health(Connectivity(PimaxConnectivityAssessmentValue.Connected), Usb(AllDevices().Where(record => record.Vid != "0BB4").ToArray()));

        Assert.Equal(PimaxHealthOverallStatus.Healthy, snapshot.OverallStatus);
        Assert.Equal(PimaxHealthComponentStatus.Missing, snapshot.Components.Single(component => component.ComponentId == "viveFaceTracker").Status);
        Assert.Contains("VRCFT", snapshot.Components.Single(component => component.ComponentId == "viveFaceTracker").Explanation);
    }

    [Fact]
    public async Task ConflictingRegistrationEvidenceProducesConflictingOverall()
    {
        var snapshot = await Health(Connectivity(PimaxConnectivityAssessmentValue.Connected), Usb(PowerOnGroup()));

        Assert.Equal(PimaxHealthOverallStatus.ConflictingEvidence, snapshot.OverallStatus);
    }

    [Fact]
    public async Task UnknownProbeFailureProducesUnknownWithoutThrowing()
    {
        var coordinator = new PimaxComponentHealthCoordinator(
            (_, _) => Task.FromException<PimaxConnectivitySnapshot>(new InvalidOperationException("probe failed")),
            () => Usb([]),
            new PimaxRegistrationStateAssessor());

        var snapshot = await coordinator.CollectAsync(null!, CancellationToken.None);

        Assert.Equal(PimaxHealthOverallStatus.Unknown, snapshot.OverallStatus);
        Assert.Contains(snapshot.Errors, error => error.Contains("probe failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrivacyOutputExcludesRawPrivateIdentifiers()
    {
        var snapshot = await Health(Connectivity(PimaxConnectivityAssessmentValue.Connected), Usb(AllDevices()));
        var json = JsonSerializer.Serialize(snapshot, PimaxComponentHealthJson.Options);

        Assert.DoesNotContain("SYNTHETIC-", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"USB\\VID_", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredHumanReadableMessagesRemainStable()
    {
        Assert.Contains("eye-tracking features are unavailable", PimaxComponentHealthMessages.EyeChipMissing);
        Assert.Contains("no image", PimaxComponentHealthMessages.DisplayMissing);
        Assert.Contains("no sound", PimaxComponentHealthMessages.AudioOutputMissing);
        Assert.Contains("microphone", PimaxComponentHealthMessages.MicrophoneMissing);
        Assert.Contains("has not registered", PimaxComponentHealthMessages.UsbPresentRegistrationMissing);
        Assert.Contains("VRCFT", PimaxComponentHealthMessages.ViveMissing);
        Assert.Contains("Pimax headset connection is healthy", PimaxComponentHealthMessages.Healthy);
    }

    [Fact]
    public void ObserverRequestRequiresExplicitDurationAndOutput()
    {
        var missing = PimaxConnectRoutineObservationRequest.Parse(["pimax-connect-routine-observe-json"]);
        var bounded = PimaxConnectRoutineObservationRequest.Parse(["pimax-connect-routine-observe-json", "--duration-seconds", "999", "--output-dir", @"C:\evidence"]);

        Assert.Null(missing.DurationSeconds);
        Assert.Null(missing.OutputDirectory);
        Assert.Equal(45, bounded.DurationSeconds);
        Assert.Equal(@"C:\evidence", bounded.OutputDirectory);
    }

    [Fact]
    public void ObserverResultSerializesAsOneJsonDocument()
    {
        var snapshot = new PimaxRoutineSnapshot(DateTimeOffset.UtcNow, [], [], [], [], [], null);
        var result = new PimaxConnectRoutineObservationResult(
            PimaxConnectRoutineObservationSchema.Version,
            "op",
            "test",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            20,
            500,
            null,
            false,
            snapshot,
            snapshot,
            [],
            [],
            [],
            [],
            [],
            [],
            ["missing"]);

        var json = JsonSerializer.Serialize(result, PimaxConnectRoutineObservationJson.Options);
        using var parsed = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        Assert.Equal(PimaxConnectRoutineObservationSchema.Version, parsed.RootElement.GetProperty("schema").GetString());
    }

    [Fact]
    public void NewDiagnosticsContainNoMutationUiAutomationPublicNetworkOrScheduledTaskCalls()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxComponentHealth.cs"))
            + File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxConnectRoutineObservation.cs"));
        string[] forbidden =
        [
            "IOCTL_USB_HUB_CYCLE_PORT",
            "CM_Reenumerate",
            "SetupDiCallClassInstaller",
            "pnputil",
            "devcon",
            ".Kill(",
            "Restart-Service",
            "Stop-Service",
            "Start-Service",
            "SendInput",
            "mouse_event",
            "keybd_event",
            "ServiceController",
            "SetValue(",
            "GetScheduledTask",
            "ScheduledTask"
        ];

        foreach (var token in forbidden) Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpClient", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WebRequest", source, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<PimaxComponentHealthSnapshot> Health(PimaxConnectivitySnapshot connectivity, PimaxUsbEnumerationSnapshot usb)
    {
        var coordinator = new PimaxComponentHealthCoordinator(
            (_, _) => Task.FromResult(connectivity),
            () => usb,
            new PimaxRegistrationStateAssessor());
        return await coordinator.CollectAsync(null!, CancellationToken.None);
    }

    private static PimaxConnectivitySnapshot Connectivity(string assessmentValue)
    {
        var assessment = new PimaxConnectivityAssessmentResult(
            assessmentValue,
            assessmentValue == PimaxConnectivityAssessmentValue.Connected ? PimaxConnectivityConfidence.Confirmed : PimaxConnectivityConfidence.Probable,
            "synthetic",
            [],
            [],
            []);
        return new PimaxConnectivitySnapshot(
            PimaxConnectivitySchema.Version,
            DateTimeOffset.Now,
            1,
            new PimaxInstallationObservation(PimaxProbeStatus.Available, [], [], [], []),
            new PimaxProcessObservation(PimaxProbeStatus.Available, [Process("PimaxClient"), Process("PiService")], [], []),
            new PimaxServiceObservation(PimaxProbeStatus.Available, [Service("PiServiceLauncher", "CoreServiceCandidate"), Service("Tobii VR4PIMAXP3B Platform Runtime", "OptionalEyeTrackingService")], [], []),
            new PimaxDeviceObservation(PimaxProbeStatus.Available, [], [], true, true, false, [], [], []),
            new PimaxRuntimeEvidenceObservation(PimaxProbeStatus.Inconclusive, DateTimeOffset.Now, 0, [], null, null, [], []),
            new PimaxSteamVrDriverObservation(PimaxProbeStatus.Available, [@"<drive>:\Program Files\Pimax\runtime"], true, [], []),
            assessment,
            assessment.Confidence,
            [],
            []);
    }

    private static PimaxProcessInfo Process(string name)
        => new(name, 10, null, @"<drive>:\Program Files\Pimax\" + name + ".exe", "RuntimeProcess", DateTimeOffset.UtcNow, "Pimax", "Pimax", "Pimax", "1", "1");

    private static PimaxServiceInfo Service(string name, string role)
        => new(name, name, "RUNNING", "auto", 10, @"<drive>:\Program Files\Pimax\" + name + ".exe", role);

    private static PimaxUsbEnumerationSnapshot Usb(PimaxUsbDeviceRecord[] records)
        => new(
            PimaxUsbEnumerationSchema.Version,
            DateTimeOffset.Now.AddMilliseconds(20),
            "test",
            new PimaxUsbEnumerationHost("test", "x64", false),
            PimaxUsbInventorySummaryBuilder.Build(records),
            records.Where(record => record.CandidateReasons.Length > 0).ToArray(),
            records,
            [],
            []);

    private static PimaxUsbDeviceRecord[] AllDevices()
        => PowerOnGroup()
            .Concat(RuntimeGroup())
            .Concat([
                Device("DISPLAY", null, null, "Pimax Display", "DISPLAY"),
                Device("SWD", null, null, "Pimax Headphones", "AudioEndpoint"),
                Device("SWD", null, null, "Pimax Streaming Microphone", "AudioEndpoint"),
                Device("USB", "0BB4", "0321", "VIVE Camera", "Camera")
            ])
            .ToArray();

    private static PimaxUsbDeviceRecord[] PowerOnGroup()
        =>
        [
            Device("USB", "05E3", "0608", "Generic USB Hub", "USB"),
            Device("USB", "28DE", "2101", "USB Input Device", "HIDClass"),
            Device("USB", "28DE", "2300", "USB Composite Device", "USB"),
            Device("HID", "28DE", "2300", "HID-compliant vendor-defined device", "HIDClass", mi: "00")
        ];

    private static PimaxUsbDeviceRecord[] RuntimeGroup()
        =>
        [
            Device("USB", "34A4", "0012", "USB Composite Device", "USB"),
            Device("USB", "34A4", "0012", "UVC Camera", "Camera", mi: "00"),
            Device("USB", "34A4", "0012", "USB Input Device", "HIDClass", mi: "02"),
            Device("USB", "34A4", "0012", "AC Interface", "MEDIA", mi: "03"),
            Device("USB", "2104", "0220", "EyeChip", "USBDevice")
        ];

    private static PimaxUsbDeviceRecord Device(
        string enumerator,
        string? vid,
        string? pid,
        string name,
        string deviceClass,
        string? mi = null)
    {
        var hardwareIds = string.IsNullOrWhiteSpace(vid) || string.IsNullOrWhiteSpace(pid)
            ? Array.Empty<string>()
            : mi is null
                ? [$"USB\\VID_{vid}&PID_{pid}"]
                : [$"USB\\VID_{vid}&PID_{pid}&MI_{mi}"];
        var raw = new PimaxUsbRawDeviceRecord(
            $"{enumerator}\\{(vid is null || pid is null ? name : $"VID_{vid}&PID_{pid}")}\\SYNTHETIC-{mi ?? "ROOT"}",
            null,
            "synthetic-container",
            enumerator,
            true,
            true,
            false,
            deviceClass,
            "{SYNTHETIC-CLASS}",
            name,
            name,
            "Synthetic",
            null,
            null,
            null,
            null,
            "Started",
            null,
            "Started",
            hardwareIds,
            [],
            vid,
            pid,
            null,
            mi,
            "synthetic location",
            [],
            [],
            "Synthetic");
        return PimaxUsbDeviceNormalizer.ToSanitizedRecord(raw);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
