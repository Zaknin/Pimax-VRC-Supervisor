using Xunit;

public sealed class PimaxServiceReconnectRecoveryTests
{
    private static readonly DateTimeOffset Now = LocalTime(2026, 7, 20, 16, 47, 0);
    private const string Serial = "P30100P201382100320";

    [Fact]
    public void ExactLiveP3bSequenceCreatesOneFullyScopedRecovery()
    {
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(Pimax()), Now, suppressRecovery: false);

        var observation = coordinator.ObservePimaxServiceLog(
            Parse(
                "2026-07-20 16:48:00.141 HMD disconnected: Pimax P3B",
                $"2026-07-20 16:48:19.310 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: {Serial}",
                "2026-07-20 16:48:19.311 HID device is ready, continuing with HMD initialization",
                "2026-07-20 16:48:28.538 hmd display restore",
                "2026-07-20 16:48:30.737 P3B VersionChecker: Register success"),
            Now.AddMinutes(2),
            suppressRecovery: false);

        var plan = Assert.Single(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(PimaxRuntimeReady: false, ViveFaceTrackerReady: false),
            new DeviceRecoveryDependencies(true, true, true, false),
            Now.AddMinutes(2),
            suppressRecovery: false));
        Assert.Equal(DeviceRecoveryReason.PimaxEyeRuntimeRecovered, plan.Reason);
        Assert.Equal(
            [
                DeviceRecoveryTarget.VrcFaceTracking,
                DeviceRecoveryTarget.BrokenEye,
                DeviceRecoveryTarget.PimaxDependentAutoLaunchApps
            ],
            plan.Targets);
        Assert.DoesNotContain(plan.Targets, target => target.ToString().Contains("Steam", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.Targets, target => target.ToString().Contains("VRChat", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.DisconnectObserved);
        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.PendingArmed);
        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.CandidateReturnObserved);
        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.ReadinessMarkerObserved);
        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.Ready);

        Assert.Empty(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, true),
            new DeviceRecoveryDependencies(true, true, true, true),
            Now.AddMinutes(3),
            suppressRecovery: false));
    }

    [Fact]
    public void RepeatedConnectedAndReadinessLinesDoNotDuplicateRecovery()
    {
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(Pimax()), Now, false);
        var sequence = Parse(
            "2026-07-20 16:48:00.141 HMD disconnected: Pimax P3B",
            "2026-07-20 16:48:00.241 HMD disconnected: Pimax P3B",
            $"2026-07-20 16:48:19.310 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: {Serial}",
            $"2026-07-20 16:48:19.410 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: {Serial}",
            "2026-07-20 16:48:19.311 HID device is ready, continuing with HMD initialization",
            "2026-07-20 16:48:19.411 HID device is ready, continuing with HMD initialization",
            "2026-07-20 16:48:28.538 hmd display restore",
            "2026-07-20 16:48:28.638 hmd display restore",
            "2026-07-20 16:48:30.737 P3B VersionChecker: Register success",
            "2026-07-20 16:48:30.837 P3B VersionChecker: Register success");

        var firstObservation = coordinator.ObservePimaxServiceLog(sequence, Now.AddMinutes(2), false);
        Assert.Contains(firstObservation.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.DuplicateSuppressed);
        Assert.Single(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(false, false),
            AllDependencies(),
            Now.AddMinutes(2),
            false));

        var repeated = coordinator.ObservePimaxServiceLog(sequence, Now.AddMinutes(3), false);
        Assert.Empty(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, true),
            AllDependencies(),
            Now.AddMinutes(3),
            false));
        Assert.Contains(repeated.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.DuplicateSuppressed);
    }

    [Fact]
    public void ReturnWithoutDisconnectDoesNotRecover()
    {
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(Pimax()), Now, false);

        var observation = coordinator.ObservePimaxServiceLog(
            Parse(
                $"2026-07-20 16:48:19.310 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: {Serial}",
                "2026-07-20 16:48:30.737 P3B VersionChecker: Register success"),
            Now.AddMinutes(2),
            false);

        Assert.Empty(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            AllDependencies(),
            Now.AddMinutes(2),
            false));
        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.StandaloneReturnIgnored);
    }

    [Fact]
    public void DisconnectWithoutReadinessExpiresWithoutRecovery()
    {
        var coordinator = Coordinator(TimeSpan.FromSeconds(5));
        coordinator.Observe(Snapshot(Pimax()), Now, false);
        coordinator.ObservePimaxServiceLog(
            Parse(
                "2026-07-20 16:48:00.141 HMD disconnected: Pimax P3B",
                $"2026-07-20 16:48:01.310 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: {Serial}"),
            Now.AddMinutes(1),
            false);

        var expired = coordinator.ObservePimaxServiceLog([], Now.AddMinutes(1).AddSeconds(7), false);

        Assert.Contains(expired.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.Expired);
        Assert.Empty(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            AllDependencies(),
            Now.AddMinutes(2),
            false));
    }

    [Fact]
    public void WrongKnownSerialIsRejected()
    {
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(Pimax()), Now, false);
        coordinator.SeedPimaxServiceHeadsetIdentity(new PimaxHeadsetIdentity("Pimax P3B", "Pimax Crystal", "EXPECTED-SERIAL"));

        var observation = coordinator.ObservePimaxServiceLog(
            Parse(
                "2026-07-20 16:48:00.141 HMD disconnected: Pimax P3B",
                "2026-07-20 16:48:19.310 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: DIFFERENT-SERIAL",
                "2026-07-20 16:48:30.737 P3B VersionChecker: Register success"),
            Now.AddMinutes(2),
            false);

        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.IdentityMismatch);
        Assert.Empty(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            AllDependencies(),
            Now.AddMinutes(2),
            false));
    }

    [Fact]
    public void PimaxLogDisconnectRequiresPositivePhysicalPimaxAttribution()
    {
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(Radio()), Now, false);

        var observation = coordinator.ObservePimaxServiceLog(
            Parse(
                "2026-07-20 16:48:00.141 HMD disconnected: Pimax P3B",
                $"2026-07-20 16:48:19.310 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: {Serial}",
                "2026-07-20 16:48:30.737 P3B VersionChecker: Register success"),
            Now.AddMinutes(2),
            false);

        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.Code == PimaxServiceReconnectDiagnosticCode.PhysicalIdentityUnavailable);
        Assert.Empty(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            AllDependencies(),
            Now.AddMinutes(2),
            false));
    }

    [Fact]
    public void ParserRecognizesEveryLiveP3bLifecycleMarker()
    {
        var events = Parse(
            "2026-07-20 16:48:00.141 HMD disconnected: Pimax P3B",
            $"2026-07-20 16:48:19.310 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: {Serial}",
            "2026-07-20 16:48:19.311 HID device is ready, continuing with HMD initialization",
            "2026-07-20 16:48:28.538 hmd display restore",
            "2026-07-20 16:48:30.737 P3B VersionChecker: Register success");

        Assert.Equal(
            [
                PimaxServiceLogEventKind.HmdDisconnected,
                PimaxServiceLogEventKind.HmdConnected,
                PimaxServiceLogEventKind.HidReady,
                PimaxServiceLogEventKind.DisplayRestore,
                PimaxServiceLogEventKind.RegistrationSucceeded
            ],
            events.Select(entry => entry.Kind));
        Assert.Equal(Serial, events[1].Identity?.SerialNumber);
    }

    [Fact]
    public void ScopedSelectionNamesEveryConfiguredPimaxDependentApplication()
    {
        var plan = new DeviceRecoveryPlan(
            DeviceRecoveryReason.PimaxEyeRuntimeRecovered,
            [
                DeviceRecoveryTarget.VrcFaceTracking,
                DeviceRecoveryTarget.BrokenEye,
                DeviceRecoveryTarget.PimaxDependentAutoLaunchApps
            ],
            1);
        var boopCounter = new ManagedAutoLaunchApp(
            "Boop Counter",
            @"C:\Apps\BoopCounter.exe",
            ["BoopCounter"],
            RestartOnPimaxReconnect: true,
            RunAsAdmin: false,
            StartMinimized: false);

        var selection = ScopedDeviceRecoverySelection.Create(plan, [boopCounter]);

        Assert.Equal(["VRCFaceTracking", "Broken Eye", "Boop Counter"], selection.ApplicationNames);
        Assert.DoesNotContain(selection.ApplicationNames, name => name.Contains("SteamVR", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(selection.ApplicationNames, name => name.Contains("VRChat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConfiguredStabilityDelayDoesNotDuplicateOrBlockReadyP3bSequence()
    {
        var events = Parse(
            "2026-07-20 16:48:00.141 HMD disconnected: Pimax P3B",
            $"2026-07-20 16:48:19.310 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: {Serial}",
            "2026-07-20 16:48:30.737 P3B VersionChecker: Register success");
        var coordinator = new UsbDeviceRecoveryCoordinator(
            new UsbDeviceAttributionRules([UsbVidPid.Create("34A4", "0012")], []),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(10));
        coordinator.Observe(Snapshot(Pimax()), events[0].Timestamp.AddSeconds(-1), false);
        coordinator.ObservePimaxServiceLog(events, events[^1].Timestamp, false);

        Assert.Empty(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(false, false),
            AllDependencies(),
            events[1].Timestamp.AddSeconds(9),
            false));
        Assert.Single(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(false, false),
            AllDependencies(),
            events[^1].Timestamp,
            false));
    }

    [Fact]
    public void SharedSupervisorLoopOwnsReconnectRecoveryInEveryUiLaunchMode()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "Program.cs"));
        var start = source.IndexOf("var pimaxServiceTopologySignal", StringComparison.Ordinal);
        var end = source.IndexOf("if (_config.OscGoesBrrrEnabled", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var loop = source[start..end];

        Assert.Contains("ObservePimaxServiceLog", loop, StringComparison.Ordinal);
        Assert.Contains("RunReadyDeviceRecoveryPlansAsync", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("_steamVrStart", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("_launchDesktopTuiAfterReady", loop, StringComparison.Ordinal);
    }

    private static PimaxServiceLogEvent[] Parse(params string[] lines)
        => lines.Select(line =>
        {
            Assert.True(PimaxServiceLogParser.TryParse(line, out var entry));
            return entry!;
        }).ToArray();

    private static UsbDeviceRecoveryCoordinator Coordinator(TimeSpan? lifetime = null)
        => new(
            new UsbDeviceAttributionRules(
                [UsbVidPid.Create("34A4", "0012")],
                [UsbVidPid.Create("0BB4", "0321")]),
            lifetime ?? TimeSpan.FromMinutes(5));

    private static DeviceRecoveryDependencies AllDependencies() => new(true, true, true, true);

    private static PimaxUsbDeviceRecord Pimax() => Device("pimax", "pimax-container", "34A4", "0012");

    private static PimaxUsbDeviceRecord Radio() => Device("radio", "radio-container", "28DE", "2101");

    private static PimaxUsbEnumerationSnapshot Snapshot(params PimaxUsbDeviceRecord[] devices)
        => new(
            PimaxUsbEnumerationSchema.Version,
            Now,
            "test",
            new PimaxUsbEnumerationHost("test", "X64", false),
            new PimaxUsbInventorySummary(
                devices.Length,
                devices.Length,
                0,
                0,
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>()),
            [],
            devices,
            [],
            []);

    private static PimaxUsbDeviceRecord Device(string stableId, string containerId, string vid, string pid)
        => new(
            $"sha256:{stableId}",
            "sha256:parent",
            $"sha256:{containerId}",
            "USB",
            true,
            true,
            false,
            "USB",
            "{test-class}",
            null,
            null,
            "test",
            "usbccgp",
            null,
            null,
            null,
            "Started",
            null,
            "Started",
            [$"USB\\VID_{vid}&PID_{pid}"],
            [],
            vid,
            pid,
            null,
            null,
            null,
            [],
            [],
            [],
            "test");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !HasGitMetadata(directory.FullName))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static DateTimeOffset LocalTime(int year, int month, int day, int hour, int minute, int second)
    {
        var value = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
    }

    private static bool HasGitMetadata(string directory)
    {
        var path = Path.Combine(directory, ".git");
        return Directory.Exists(path) || File.Exists(path);
    }
}
