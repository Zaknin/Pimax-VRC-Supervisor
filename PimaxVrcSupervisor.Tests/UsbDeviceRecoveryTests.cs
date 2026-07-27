using Xunit;

public sealed class UsbDeviceAttributionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IdenticalInventoryProducesNoDomainTransition()
    {
        var device = Device("radio-node", "radio-container", "28DE", "2101");
        var coordinator = Coordinator();

        coordinator.Observe(Snapshot(device), Now, suppressRecovery: false);
        var observation = coordinator.Observe(Snapshot(device), Now.AddSeconds(1), suppressRecovery: false);

        Assert.Empty(observation.Transitions);
    }

    [Theory]
    [InlineData("0781", "5591", (int)UsbPhysicalDeviceClassification.Unrelated)]
    [InlineData("28DE", "2101", (int)UsbPhysicalDeviceClassification.SteamVrRadioDongle)]
    [InlineData("34A4", "0012", (int)UsbPhysicalDeviceClassification.PimaxRuntime)]
    [InlineData("2104", "0220", (int)UsbPhysicalDeviceClassification.PimaxEyeTracking)]
    [InlineData("28DE", "2300", (int)UsbPhysicalDeviceClassification.PimaxTrackingInterface)]
    [InlineData("0BB4", "0321", (int)UsbPhysicalDeviceClassification.ViveFaceTracker)]
    public void StructuredVidPidDeterminesPhysicalDeviceClassification(
        string vid,
        string pid,
        int expected)
    {
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(Device("node", "container", vid, pid)), Now, false);

        var observation = coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);

        Assert.Equal((UsbPhysicalDeviceClassification)expected, Assert.Single(observation.Transitions).Device.Classification);
    }

    [Fact]
    public void FriendlyNameContainingVrDoesNotImplyPimax()
    {
        var coordinator = Coordinator();
        coordinator.Observe(
            Snapshot(Device("node", "container", "9999", "0001", friendlyName: "VR Pimax-looking Tracker")),
            Now,
            false);

        var transition = Assert.Single(coordinator.Observe(Snapshot(), Now.AddSeconds(1), false).Transitions);

        Assert.Equal(UsbPhysicalDeviceClassification.Unrelated, transition.Device.Classification);
    }

    [Theory]
    [InlineData("USB", "USB")]
    [InlineData("HID", "HIDClass")]
    public void GenericUsbOrHidClassWithoutIdentityIsUnknown(string enumerator, string deviceClass)
    {
        var coordinator = Coordinator();
        coordinator.Observe(
            Snapshot(Device("node", null, null, null, enumerator, deviceClass, "VR Headset")),
            Now,
            false);

        var transition = Assert.Single(coordinator.Observe(Snapshot(), Now.AddSeconds(1), false).Transitions);

        Assert.Equal(UsbPhysicalDeviceClassification.Unknown, transition.Device.Classification);
    }

    [Fact]
    public void PimaxRemainingPresentWhileAnotherDeviceChangesIsNotPimaxReconnect()
    {
        var pimax = Device("pimax-root", "pimax-container", "34A4", "0012");
        var storage = Device("storage-root", "storage-container", "0781", "5591");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(pimax, storage), Now, false);

        var observation = coordinator.Observe(Snapshot(pimax), Now.AddSeconds(1), false);

        var transition = Assert.Single(observation.Transitions);
        Assert.Equal(UsbPhysicalDeviceClassification.Unrelated, transition.Device.Classification);
        Assert.False(coordinator.HasPimaxRecoveryAwaitingReadiness);
    }

    [Fact]
    public void ExactPimaxContainerRemovalAndReturnAreSeparateAttributedTransitions()
    {
        var pimax = Device("pimax-root", "pimax-container", "34A4", "0012");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(pimax), Now, false);

        var removed = Assert.Single(coordinator.Observe(Snapshot(), Now.AddSeconds(1), false).Transitions);
        var added = Assert.Single(coordinator.Observe(Snapshot(pimax), Now.AddSeconds(2), false).Transitions);

        Assert.Equal(UsbPhysicalDeviceTransitionKind.Removed, removed.Kind);
        Assert.Equal(UsbPhysicalDeviceClassification.PimaxRuntime, removed.Device.Classification);
        Assert.Equal(UsbPhysicalDeviceTransitionKind.Added, added.Kind);
        Assert.True(added.MatchedPendingDisconnect);
    }

    [Fact]
    public void FaceTrackerContainerIsClassifiedSeparatelyFromPimax()
    {
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(Device("face-root", "face-container", "0BB4", "0321")), Now, false);

        var transition = Assert.Single(coordinator.Observe(Snapshot(), Now.AddSeconds(1), false).Transitions);

        Assert.Equal(UsbPhysicalDeviceClassification.ViveFaceTracker, transition.Device.Classification);
        Assert.False(coordinator.HasPimaxRecoveryAwaitingReadiness);
    }

    [Fact]
    public void ContainerIdentityGroupsCompositeInterfacesIntoOneTransition()
    {
        var composite = new[]
        {
            Device("root", "physical", "34A4", "0012", deviceClass: "USB"),
            Device("audio", "physical", "34A4", "0012", deviceClass: "MEDIA"),
            Device("hid", "physical", "34A4", "0012", enumerator: "HID", deviceClass: "HIDClass")
        };
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(composite), Now, false);

        var transition = Assert.Single(coordinator.Observe(Snapshot(), Now.AddSeconds(1), false).Transitions);

        Assert.Equal(3, transition.Device.NodeStableIds.Length);
        Assert.True(transition.Device.UsesContainerIdentity);
    }

    [Fact]
    public void OneInterfaceDisappearingWhileContainerRemainsProducesNoPhysicalTransition()
    {
        var root = Device("root", "physical", "34A4", "0012", deviceClass: "USB");
        var hid = Device("hid", "physical", "34A4", "0012", enumerator: "HID", deviceClass: "HIDClass");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(root, hid), Now, false);

        var observation = coordinator.Observe(Snapshot(root), Now.AddSeconds(1), false);

        Assert.Empty(observation.Transitions);
    }

    [Fact]
    public void NonPresentHistoricalNodeDoesNotCountAsConnectedPhysicalDevice()
    {
        var historical = Device("old", "old-container", "28DE", "2101", present: false, connected: false);
        var coordinator = Coordinator();

        coordinator.Observe(Snapshot(historical), Now, false);
        var observation = coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);

        Assert.Empty(observation.Transitions);
    }

    [Fact]
    public void InventoryFailureFailsClosedAndPreservesPreviousBaseline()
    {
        var radio = Device("radio", "radio-container", "28DE", "2101");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(radio), Now, false);

        var failed = coordinator.Observe(Snapshot(errors: ["SetupAPI unavailable"]), Now.AddSeconds(1), false);
        var recovered = coordinator.Observe(Snapshot(radio), Now.AddSeconds(2), false);

        Assert.True(failed.InventoryUnavailable);
        Assert.Empty(failed.Transitions);
        Assert.Empty(recovered.Transitions);
    }

    [Theory]
    [InlineData("28DE", "2101")]
    [InlineData("0781", "5591")]
    [InlineData("9999", "0001")]
    public void UnrelatedRemovalAndReturnCreateZeroRecoveryPlans(string vid, string pid)
    {
        var device = Device("node", "container", vid, pid);
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(device), Now, false);
        coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);
        coordinator.Observe(Snapshot(device), Now.AddSeconds(2), false);

        var plans = coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, true),
            AllDependencies(),
            Now.AddSeconds(3),
            false);

        Assert.Empty(plans);
    }

    [Fact]
    public void UnknownDeviceTransitionCreatesZeroRecoveryPlans()
    {
        var device = Device("node", null, null, null, "USB", "USB", "VR radio");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(device), Now, false);
        coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);
        coordinator.Observe(Snapshot(device), Now.AddSeconds(2), false);

        Assert.Empty(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, true),
            AllDependencies(),
            Now.AddSeconds(3),
            false));
    }

    [Fact]
    public void RelevantRemovalAloneCreatesNoRecoveryPlan()
    {
        var pimax = Device("pimax", "pimax-container", "34A4", "0012");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(pimax), Now, false);
        coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);

        var plans = coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, true),
            AllDependencies(),
            Now.AddSeconds(2),
            false);

        Assert.Empty(plans);
    }

    [Fact]
    public void RelevantReturnBeforeReadinessCreatesNoRecoveryPlan()
    {
        var pimax = Device("pimax", "pimax-container", "34A4", "0012");
        var coordinator = Reconnect(coordinator: Coordinator(), pimax);

        var plans = coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(false, true),
            AllDependencies(),
            Now.AddSeconds(3),
            false);

        Assert.Empty(plans);
        Assert.True(coordinator.HasPimaxRecoveryAwaitingReadiness);
    }

    [Fact]
    public void RelevantReturnAfterReadinessCreatesExactlyOneRecoveryPlan()
    {
        var pimax = Device("pimax", "pimax-container", "34A4", "0012");
        var coordinator = Reconnect(coordinator: Coordinator(), pimax);

        var first = coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, true),
            AllDependencies(),
            Now.AddSeconds(3),
            false);
        var repeated = coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, true),
            AllDependencies(),
            Now.AddSeconds(4),
            false);

        Assert.Single(first);
        Assert.Empty(repeated);
    }

    [Fact]
    public void RapidRemoveAndReturnProducesAtMostOneRecovery()
    {
        var pimax = Device("pimax", "pimax-container", "34A4", "0012");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(pimax), Now, false);
        coordinator.Observe(Snapshot(), Now.AddMilliseconds(10), false);
        coordinator.Observe(Snapshot(pimax), Now.AddMilliseconds(20), false);

        var plans = coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            AllDependencies(),
            Now.AddMilliseconds(30),
            false);

        Assert.Single(plans);
    }

    [Fact]
    public void TwoUnrelatedDonglesChangingTogetherProduceZeroRecovery()
    {
        var one = Device("one", "one-container", "28DE", "2101");
        var two = Device("two", "two-container", "28DE", "2101");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(one, two), Now, false);
        var removed = coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);
        coordinator.Observe(Snapshot(one, two), Now.AddSeconds(2), false);

        Assert.Equal(2, removed.Transitions.Length);
        Assert.Empty(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, true),
            AllDependencies(),
            Now.AddSeconds(3),
            false));
    }

    [Fact]
    public void RelevantAndUnrelatedChangesInOneSnapshotPreserveAttribution()
    {
        var pimax = Device("pimax", "pimax-container", "34A4", "0012");
        var radio = Device("radio", "radio-container", "28DE", "2101");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(pimax, radio), Now, false);

        var removed = coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);

        Assert.Contains(removed.Transitions, transition => transition.Device.Classification == UsbPhysicalDeviceClassification.PimaxRuntime);
        Assert.Contains(removed.Transitions, transition => transition.Device.Classification == UsbPhysicalDeviceClassification.SteamVrRadioDongle);
    }

    [Fact]
    public void DifferentPhysicalDeviceReturnDoesNotSatisfyPendingReconnect()
    {
        var first = Device("first", "first-container", "34A4", "0012");
        var second = Device("second", "second-container", "34A4", "0012");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(first), Now, false);
        coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);
        var added = Assert.Single(coordinator.Observe(Snapshot(second), Now.AddSeconds(2), false).Transitions);

        var plans = coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            AllDependencies(),
            Now.AddSeconds(3),
            false);

        Assert.False(added.MatchedPendingDisconnect);
        Assert.Empty(plans);
    }

    [Fact]
    public void StalePendingReconnectExpiresSafely()
    {
        var pimax = Device("pimax", "pimax-container", "34A4", "0012");
        var coordinator = new UsbDeviceRecoveryCoordinator(Rules(), TimeSpan.FromSeconds(5));
        coordinator.Observe(Snapshot(pimax), Now, false);
        coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);

        var expired = coordinator.Observe(Snapshot(), Now.AddSeconds(7), false);
        var added = Assert.Single(coordinator.Observe(Snapshot(pimax), Now.AddSeconds(8), false).Transitions);

        Assert.Equal(1, expired.ExpiredPendingReconnects);
        Assert.False(added.MatchedPendingDisconnect);
    }

    [Fact]
    public void SteamVrLifecycleSuppressionDiscardsPendingRecovery()
    {
        var pimax = Device("pimax", "pimax-container", "34A4", "0012");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(pimax), Now, false);
        coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);
        coordinator.Observe(Snapshot(pimax), Now.AddSeconds(2), suppressRecovery: true);

        var plans = coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, true),
            AllDependencies(),
            Now.AddSeconds(3),
            false);

        Assert.Empty(plans);
    }

    [Fact]
    public void PimaxRecoveryPlanContainsOnlyConfiguredDependencies()
    {
        var coordinator = Reconnect(Coordinator(), Device("pimax", "pimax-container", "34A4", "0012"));
        var dependencies = new DeviceRecoveryDependencies(
            VrcFaceTrackingDependsOnPimax: true,
            BrokenEyeDependsOnPimax: true,
            PimaxDependentAutoLaunchAppsConfigured: true,
            VrcFaceTrackingDependsOnViveFaceTracker: false);

        var plan = Assert.Single(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            dependencies,
            Now.AddSeconds(3),
            false));

        Assert.Equal(
            [
                DeviceRecoveryTarget.VrcFaceTracking,
                DeviceRecoveryTarget.BrokenEye,
                DeviceRecoveryTarget.PimaxDependentAutoLaunchApps
            ],
            plan.Targets);
    }

    [Fact]
    public void PimaxRecoveryNeverIncludesXsOverlayTarget()
    {
        var coordinator = Reconnect(Coordinator(), Device("pimax", "pimax-container", "34A4", "0012"));
        var plan = Assert.Single(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            AllDependencies(),
            Now.AddSeconds(3),
            false));

        Assert.All(plan.Targets, target => Assert.DoesNotContain("XSOverlay", target.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void FaceTrackerRecoveryOnlyTargetsConfiguredVrcFaceTrackingDependency(
        bool configured,
        bool expectTarget)
    {
        var coordinator = Reconnect(Coordinator(), Device("face", "face-container", "0BB4", "0321"));
        var dependencies = new DeviceRecoveryDependencies(false, false, false, configured);

        var plan = Assert.Single(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(false, true),
            dependencies,
            Now.AddSeconds(3),
            false));

        Assert.Equal(expectTarget, plan.Targets.Contains(DeviceRecoveryTarget.VrcFaceTracking));
        Assert.DoesNotContain(DeviceRecoveryTarget.BrokenEye, plan.Targets);
    }

    [Fact]
    public void UnconfiguredDependencyProducesNoAutomaticRestartTargets()
    {
        var coordinator = Reconnect(Coordinator(), Device("pimax", "pimax-container", "34A4", "0012"));

        var plan = Assert.Single(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            new DeviceRecoveryDependencies(false, false, false, false),
            Now.AddSeconds(3),
            false));

        Assert.Empty(plan.Targets);
    }

    [Fact]
    public void MultiplePimaxComponentsReturnAsOneCoalescedPlan()
    {
        var runtime = Device("runtime", "runtime-container", "34A4", "0012");
        var eye = Device("eye", "eye-container", "2104", "0220");
        var coordinator = Coordinator();
        coordinator.Observe(Snapshot(runtime, eye), Now, false);
        coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);
        coordinator.Observe(Snapshot(runtime, eye), Now.AddSeconds(2), false);

        var plan = Assert.Single(coordinator.CreateReadyPlans(
            new DeviceRecoveryReadiness(true, false),
            AllDependencies(),
            Now.AddSeconds(3),
            false));

        Assert.Equal(DeviceRecoveryReason.PimaxEyeRuntimeRecovered, plan.Reason);
        Assert.Equal(2, plan.CoalescedPhysicalDeviceCount);
    }

    [Theory]
    [InlineData(true, "removed")]
    [InlineData(false, "reconnected")]
    public void SteamVrRadioLogIsConciseAndNoRecoveryOriented(
        bool removed,
        string action)
    {
        var transition = new UsbPhysicalDeviceTransition(
            removed ? UsbPhysicalDeviceTransitionKind.Removed : UsbPhysicalDeviceTransitionKind.Added,
            Physical(UsbPhysicalDeviceClassification.SteamVrRadioDongle),
            MatchedPendingDisconnect: false,
            RecoverySuppressed: false);

        var message = UsbDeviceRecoveryLog.Decision(transition);

        Assert.Contains($"unrelated SteamVR radio dongle {action}", message, StringComparison.Ordinal);
        Assert.Contains("no managed application recovery required", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownDeviceLogRecordsSafeClassificationWithoutIdentifier()
    {
        var transition = new UsbPhysicalDeviceTransition(
            UsbPhysicalDeviceTransitionKind.Removed,
            Physical(UsbPhysicalDeviceClassification.Unknown),
            false,
            false);

        var message = UsbDeviceRecoveryLog.Decision(transition);

        Assert.Contains("unknown physical device", message, StringComparison.Ordinal);
        Assert.Contains("no managed application recovery", message, StringComparison.Ordinal);
        Assert.DoesNotContain("container:", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RelevantPlanLogNamesIncludedAndExcludedApplications()
    {
        var message = UsbDeviceRecoveryLog.Plan(new DeviceRecoveryPlan(
            DeviceRecoveryReason.PimaxEyeRuntimeRecovered,
            [DeviceRecoveryTarget.VrcFaceTracking, DeviceRecoveryTarget.BrokenEye],
            1));

        Assert.Contains("VRCFaceTracking", message, StringComparison.Ordinal);
        Assert.Contains("Broken Eye", message, StringComparison.Ordinal);
        Assert.Contains("XSOverlay", message, StringComparison.Ordinal);
        Assert.Contains("excluded", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugIdentityRetainsStructuredEvidence()
    {
        var physical = Physical(UsbPhysicalDeviceClassification.SteamVrRadioDongle);

        Assert.Contains("physicalKey=container:sha256:physical", physical.DebugIdentity, StringComparison.Ordinal);
        Assert.Contains("vidPid=28DE:2101", physical.DebugIdentity, StringComparison.Ordinal);
        Assert.Contains("parentIds=sha256:parent", physical.DebugIdentity, StringComparison.Ordinal);
        Assert.Contains("services=HidUsb", physical.DebugIdentity, StringComparison.Ordinal);
    }

    private static UsbDeviceRecoveryCoordinator Reconnect(
        UsbDeviceRecoveryCoordinator coordinator,
        PimaxUsbDeviceRecord device)
    {
        coordinator.Observe(Snapshot(device), Now, false);
        coordinator.Observe(Snapshot(), Now.AddSeconds(1), false);
        coordinator.Observe(Snapshot(device), Now.AddSeconds(2), false);
        return coordinator;
    }

    private static UsbDeviceRecoveryCoordinator Coordinator()
        => new(Rules(), TimeSpan.FromMinutes(5));

    private static UsbDeviceAttributionRules Rules()
        => new(
            [UsbVidPid.Create("34A4", "0012")],
            [UsbVidPid.Create("0BB4", "0321")]);

    private static DeviceRecoveryDependencies AllDependencies()
        => new(true, true, true, true);

    private static UsbPhysicalDevice Physical(UsbPhysicalDeviceClassification classification)
        => new(
            "container:sha256:physical",
            true,
            classification,
            [UsbVidPid.Create("28DE", "2101")],
            ["sha256:node"],
            ["sha256:parent"],
            ["HIDClass"],
            ["HidUsb"]);

    private static PimaxUsbEnumerationSnapshot Snapshot(
        PimaxUsbDeviceRecord[]? devices = null,
        string[]? errors = null)
        => new(
            PimaxUsbEnumerationSchema.Version,
            Now,
            "test",
            new PimaxUsbEnumerationHost("test", "X64", false),
            new PimaxUsbInventorySummary(
                devices?.Length ?? 0,
                devices?.Count(device => device.Present) ?? 0,
                devices?.Count(device => !device.Present) ?? 0,
                0,
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>()),
            [],
            devices ?? [],
            [],
            errors ?? []);

    private static PimaxUsbEnumerationSnapshot Snapshot(params PimaxUsbDeviceRecord[] devices)
        => Snapshot(devices, null);

    private static PimaxUsbDeviceRecord Device(
        string stableId,
        string? containerId,
        string? vid,
        string? pid,
        string enumerator = "USB",
        string deviceClass = "USB",
        string? friendlyName = null,
        string? service = "usbccgp",
        bool present = true,
        bool connected = true)
        => new(
            $"sha256:{stableId}",
            "sha256:parent",
            containerId is null ? null : $"sha256:{containerId}",
            enumerator,
            present,
            connected,
            !present,
            deviceClass,
            "{test-class}",
            friendlyName,
            friendlyName,
            "test",
            service,
            null,
            null,
            null,
            present ? "Started" : "NonPresent",
            null,
            present ? "Started" : "NonPresent",
            vid is null || pid is null ? [] : [$"{enumerator}\\VID_{vid}&PID_{pid}"],
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
}

public sealed class UsbDeviceRecoveryIntegrationGuardTests
{
    [Fact]
    public void PiServiceAndKernelPnpSignalsAreRescanHintsNotRecoveryAuthority()
    {
        var source = ProgramSource();
        var loop = Slice(
            source,
            "var pimaxServiceTopologySignal",
            "if (_config.OscGoesBrrrEnabled");

        Assert.Contains("scheduling structured inventory attribution rescan", loop, StringComparison.Ordinal);
        Assert.Contains("ObserveUsbDeviceInventory", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("StopManagedAppsAsync", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("StartManagedAppsAsync", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("RestartVrcFaceTrackingAsync", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopedDeviceRecoveryDoesNotUseBroadOrUnrelatedLifecycleOperations()
    {
        var source = ProgramSource();
        var scoped = Slice(
            source,
            "private async Task ExecuteScopedDeviceRecoveryPlanAsync",
            "private PimaxServiceReconnect? DetectPimaxServiceLogReconnect");

        Assert.DoesNotContain("RestartCoreAppsAsync", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("StartManagedAppsAsync", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareMonitorLayoutForVrSessionAsync", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("XSOverlay", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamVr", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("BaseStation", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("OscRouter", scoped, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualRestartCoreAppsRetainsBroadManualSemantics()
    {
        var source = ProgramSource();
        var manual = Slice(
            source,
            "private async Task RestartCoreAppsAsync",
            "internal async Task<string> ExecuteSupervisorCommandAsync");

        Assert.Contains("StopProcessesAsync(\"VRCFaceTracking\"", manual, StringComparison.Ordinal);
        Assert.Contains("StopProcessesAsync(\"Broken Eye\"", manual, StringComparison.Ordinal);
        Assert.Contains("StartCoreAppsAsync", manual, StringComparison.Ordinal);
        Assert.Contains("\"restart-core-apps\" => await RestartCoreAppsCommandAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceRecoveryIsSuppressedBySteamVrAndManagedAppLifecycleOwners()
    {
        var source = ProgramSource();
        var guard = Slice(
            source,
            "private bool ShouldSuppressDeviceRecovery()",
            "private void ObserveUsbDeviceInventory");

        Assert.Contains("IsVrSessionRestartActive()", guard, StringComparison.Ordinal);
        Assert.Contains("_steamVrRecovery.IsRecoveryPending", guard, StringComparison.Ordinal);
        Assert.Contains("_lifecyclePhase != SupervisorLifecyclePhase.VrChatRunning", guard, StringComparison.Ordinal);
        Assert.Contains("_coreAppRestartLock.CurrentCount == 0", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void XsOverlayIsCentrallyExcludedFromPimaxDependentAutostartApps()
    {
        var source = ProgramSource();
        var mapping = Slice(
            source,
            "private ManagedAutoLaunchApp[] GetPimaxDependentAutoLaunchApps()",
            "private bool ShouldSkipDuplicateCoreAutoLaunchApp");

        Assert.Contains("!IsXsOverlayApplication(app)", mapping, StringComparison.Ordinal);
        Assert.Contains("Path.GetFileNameWithoutExtension", mapping, StringComparison.Ordinal);
        Assert.Contains("\"XSOverlay\"", mapping, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAutostartDependencyFailsClosedInSupervisorAndConfigurator()
    {
        var supervisor = ProgramSource();
        var configurator = SourceFile("PimaxVrcSupervisor.ConfigEditor", "Program.cs");

        Assert.Contains(
            "app.CloseOnPimaxDisconnect\n            ?? false",
            supervisor.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.Contains(
            "GetOptionalBool(obj, \"CloseOnPimaxDisconnect\")\n                            ?? false",
            configurator.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.Contains("e.Row.Cells[\"RestartOnPimaxReconnect\"].Value = false", configurator, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionsAndAutomaticResultPopupPolicyRemainUnchanged()
    {
        var source = ProgramSource();
        var commands = Slice(
            source,
            "private SupervisorCommandCapabilitiesSnapshot BuildSupervisorCommandCapabilitiesSnapshot()",
            "private static SupervisorCommandDefinition CommandDefinition");

        Assert.DoesNotContain("\"8\"", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("\"9\"", commands, StringComparison.Ordinal);
        Assert.Contains("RestartVrSessionCommandName", commands, StringComparison.Ordinal);
        Assert.Contains("requiresConfirmation: true", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("automatic Action Result", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductVersionRemains131()
    {
        var project = SourceFile("PimaxVrcSupervisor", "PimaxVrcSupervisor.csproj");

        Assert.Contains("<Version>1.3.1</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>1.3.1.0</FileVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>1.3.1</InformationalVersion>", project, StringComparison.Ordinal);
    }

    private static string ProgramSource()
        => SourceFile("PimaxVrcSupervisor", "Program.cs");

    private static string SourceFile(string directory, string fileName)
        => File.ReadAllText(Path.Combine(RepositoryRoot(), directory, fileName));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        Assert.True(end > start, $"Missing source marker: {endMarker}");
        return source[start..end];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !HasGitMetadata(directory.FullName))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static bool HasGitMetadata(string directory)
    {
        var path = Path.Combine(directory, ".git");
        return Directory.Exists(path) || File.Exists(path);
    }
}
