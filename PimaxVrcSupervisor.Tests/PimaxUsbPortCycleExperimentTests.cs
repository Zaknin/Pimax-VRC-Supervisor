using System.Text.Json;
using Xunit;

public sealed class PimaxUsbPortCycleExperimentTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CorrectExactTargetIsAccepted()
    {
        var (signature, state) = Fixture();
        var result = PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now);
        Assert.True(result.Safety.Permitted);
        Assert.NotNull(result.Plan);
        Assert.Equal(4, result.Plan.ConnectionIndex);
        Assert.Equal(1, result.Plan.ExactRequestCount);
        Assert.Contains("SuperSpeed companion cycle", result.Plan.ExcludedOperations);
    }

    [Fact]
    public void WrongHubContainerAndLocationAreRejected()
    {
        var (signature, state) = Fixture();
        signature = signature with { Usb2Hub = signature.Usb2Hub with { ContainerId = "wrong", LocationPaths = ["wrong"] } };
        var result = PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now);
        Assert.False(result.Safety.Permitted);
        Assert.Contains(result.Safety.RefusalReasons, value => value.Contains("missing or ambiguous"));
    }

    [Fact]
    public void AmbiguousExactHubIsRejected()
    {
        var (signature, state) = Fixture();
        state = state with { Topology = state.Topology with { Hubs = [.. state.Topology.Hubs, state.Topology.Hubs[0] with { HubId = "duplicate" }] } };
        Assert.Contains(PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now).Safety.RefusalReasons, value => value.Contains("ambiguous"));
    }

    [Fact]
    public void WrongCompanionHubIsRejected()
    {
        var (signature, state) = Fixture();
        var ports = state.Topology.Ports.Select(port => port.ConnectionIndex == 4 ? port with { Companions = [new(0, 4, "wrong-hub", true)] } : port).ToArray();
        state = state with { Topology = state.Topology with { Ports = ports } };
        Assert.Contains(PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now).Safety.RefusalReasons, value => value.Contains("not reciprocal"));
    }

    [Fact]
    public void WrongPimaxIndexIsRejected()
    {
        var (signature, state) = Fixture();
        signature = signature with { PimaxUsb2Port = signature.PimaxUsb2Port with { ConnectionIndex = 3 } };
        Assert.False(PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now).Safety.Permitted);
    }

    [Fact]
    public void NonreciprocalCompanionIsRejected()
    {
        var (signature, state) = Fixture();
        var ports = state.Topology.Ports.Select(port => port.ConnectionIndex == 4 && port.HubInterfacePath == signature.Usb2Hub.InterfacePath
            ? port with { Companions = port.Companions.Select(value => value with { Reciprocal = false }).ToArray() }
            : port).ToArray();
        state = state with { Topology = state.Topology with { Ports = ports } };
        Assert.Contains(PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now).Safety.RefusalReasons, value => value.Contains("not reciprocal"));
    }

    [Fact]
    public void ViveMissingOrOverlappingIsRejected()
    {
        var (signature, state) = Fixture();
        signature = signature with { ViveUsb2Port = signature.ViveUsb2Port with { ConnectorGroupId = signature.PimaxUsb2Port.ConnectorGroupId } };
        var result = PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now);
        Assert.False(result.Safety.Permitted);
        Assert.Contains(result.Safety.RefusalReasons, value => value.Contains("overlap") || value.Contains("Vive"));
    }

    [Fact]
    public void UnrelatedOccupantOnPimaxConnectorIsRejected()
    {
        var (signature, state) = Fixture();
        state = state with { Topology = state.Topology with { Ports = state.Topology.Ports.Select(port => port.PhysicalConnectorGroupId == signature.PimaxUsb2Port.ConnectorGroupId ? port with { OccupantClassification = "unrelated-or-unresolved" } : port).ToArray() } };
        Assert.Contains(PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now).Safety.RefusalReasons, value => value.Contains("unexpected occupant"));
    }

    [Fact]
    public void UnrelatedPortInventoryChangeIsRejected()
    {
        var (signature, state) = Fixture();
        state = state with { Topology = state.Topology with { Ports = state.Topology.Ports.Select(port => port.ConnectionIndex == 1 ? port with { ProductId = 99 } : port).ToArray() } };
        Assert.Contains(PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now).Safety.RefusalReasons, value => value.Contains("Unrelated-port"));
    }

    [Fact]
    public void RootHubAndXhciControllerAreRejected()
    {
        var (signature, state) = Fixture();
        var changed = state.Topology.Hubs.Select(hub => hub.InterfacePath == signature.Usb2Hub.InterfacePath ? hub with { IsRootHub = true, HubType = "root", Product = "xHCI controller" } : hub).ToArray();
        state = state with { Topology = state.Topology with { Hubs = changed } };
        signature = signature with { Usb2Hub = signature.Usb2Hub with { IsRootHub = true, HubType = "root" } };
        var result = PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now);
        Assert.Contains(result.Safety.RefusalReasons, value => value.Contains("root hub"));
        Assert.Contains(result.Safety.RefusalReasons, value => value.Contains("xHCI"));
    }

    [Theory]
    [InlineData("registeredReady", "confirmed", true, false, false)]
    [InlineData("likelyPoweredOnAwaitingRegistration", "probable", false, true, false)]
    [InlineData("likelyPoweredOnAwaitingRegistration", "probable", false, false, true)]
    public void RuntimeSafetyRequirementsAreEnforced(string stateName, string confidence, bool noPlay, bool steamVr, bool noObserver)
    {
        var (signature, state) = Fixture();
        state = state with
        {
            Registration = Registration(stateName, confidence),
            PimaxPlayRunning = !noPlay,
            SteamVrRunning = steamVr,
            Observer = noObserver ? null : state.Observer
        };
        Assert.False(PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now).Safety.Permitted);
    }

    [Fact]
    public void DryRunProducesBoundOneTimeTokenWithoutInvokingNativeAdapter()
    {
        var (signature, state) = Fixture();
        var validation = PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now);
        var plan = Assert.IsType<PimaxUsbPortCyclePlan>(validation.Plan);
        var created = PimaxUsbPortCycleConfirmationToken.Create("experiment", plan, Now);
        using var directory = new TempDirectory();
        var first = PimaxUsbPortCycleConfirmationToken.Validate(created.Token, "experiment", plan, Now, directory.Path, true);
        var second = PimaxUsbPortCycleConfirmationToken.Validate(created.Token, "experiment", plan, Now, directory.Path, true);
        Assert.True(first.Accepted);
        Assert.False(second.Accepted);
        Assert.Contains("already used", second.Reason);
    }

    [Fact]
    public void TokenRejectsChangedTopologyObserverMarkerAndExpiry()
    {
        var (signature, state) = Fixture();
        var plan = PimaxUsbPortCycleTargetValidator.Validate(signature, state, Now).Plan!;
        var token = PimaxUsbPortCycleConfirmationToken.Create("experiment", plan, Now).Token;
        var changed = plan with { BindingSha256 = "changed", Observer = plan.Observer! with { MarkerFileSha256 = "changed" } };
        Assert.False(PimaxUsbPortCycleConfirmationToken.Validate(token, "experiment", changed, Now, null, false).Accepted);
        Assert.False(PimaxUsbPortCycleConfirmationToken.Validate(token, "experiment", plan, Now.AddMinutes(6), null, false).Accepted);
    }

    [Fact]
    public void MarkerSequenceRequiresReadinessThenConnectThenScan()
    {
        using var directory = new TempDirectory();
        var status = Path.Combine(directory.Path, "status.json");
        var markers = Path.Combine(directory.Path, "markers.jsonl");
        File.WriteAllText(status, JsonSerializer.Serialize(new { sessionId = "observer", updatedAt = Now, state = "running" }));
        File.WriteAllLines(markers,
        [
            JsonSerializer.Serialize(new { label = "ready-to-start-connect-scan", timestamp = Now.AddSeconds(-3) }),
            JsonSerializer.Serialize(new { label = "connect-pressed", timestamp = Now.AddSeconds(-2) }),
            JsonSerializer.Serialize(new { label = "connect-scan-active", timestamp = Now.AddSeconds(-1) })
        ]);
        Assert.NotNull(PimaxUsbPortCycleObserverReader.Read(status, markers, Now));
        File.WriteAllLines(markers,
        [
            JsonSerializer.Serialize(new { label = "connect-pressed", timestamp = Now.AddSeconds(-2) }),
            JsonSerializer.Serialize(new { label = "connect-scan-active", timestamp = Now.AddSeconds(-1) })
        ]);
        Assert.Null(PimaxUsbPortCycleObserverReader.Read(status, markers, Now));
    }

    [Fact]
    public void NativeBoundaryContainsOneCycleCallAndNoFallbackOrRetry()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "PimaxUsbPortCycleExperiment.cs"));
        var adapterStart = source.IndexOf("internal sealed class WindowsPimaxUsbPortCycleNativeAdapter", StringComparison.Ordinal);
        var executorStart = source.IndexOf("internal static class PimaxUsbPortCycleElevatedExecutor", adapterStart, StringComparison.Ordinal);
        var adapter = source[adapterStart..executorStart];
        Assert.Equal(1, Count(adapter, "Native.DeviceIoControl("));
        Assert.Equal(1, Count(adapter, "IoctlUsbHubCyclePort" + ","));
        Assert.DoesNotContain("for (", adapter);
        Assert.DoesNotContain("while (", adapter);
        Assert.DoesNotContain("Thread.Sleep", adapter);
    }

    [Fact]
    public void SingleShotSubmitterCallsAdapterOnceForUsb2Index4Only()
    {
        var adapter = new FakeNativeAdapter();
        var result = PimaxUsbPortCycleSingleShotSubmitter.Submit(adapter, "exact-hub", 4);
        Assert.True(result.ReturnedSuccess);
        Assert.Equal(1, adapter.Calls);
        Assert.Equal("exact-hub", adapter.Path);
        Assert.Equal(4, adapter.Index);
        Assert.Throws<InvalidOperationException>(() => PimaxUsbPortCycleSingleShotSubmitter.Submit(adapter, "superspeed", 3));
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public void PrivilegedContextRequiresDedicatedElevatedHelper()
    {
        Assert.False(PimaxUsbPortCycleElevatedExecutor.IsPermittedExecutionContext(@"C:\bin\PimaxVrcSupervisor.PortCycleHelper.exe", false));
        Assert.False(PimaxUsbPortCycleElevatedExecutor.IsPermittedExecutionContext(@"C:\bin\PimaxVrcSupervisor.exe", true));
        Assert.True(PimaxUsbPortCycleElevatedExecutor.IsPermittedExecutionContext(@"C:\bin\PimaxVrcSupervisor.PortCycleHelper.exe", true));
        Assert.True(PimaxUsbPortCycleUacLauncher.IsCancellation(1223));
        Assert.False(PimaxUsbPortCycleUacLauncher.IsCancellation(5));
    }

    [Fact]
    public void PrivilegedResultIsWrittenAtomically()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "result.json");
        var result = new PimaxUsbPortCyclePrivilegedResult(PimaxUsbPortCycleExperimentSchema.PrivilegedResultVersion, "id", 1, true, "hash", true, true, true, true, true, "deviceConnected", Now, true, 0, 0, 1, Now, true, "none", [], []);
        PimaxUsbPortCycleElevatedExecutor.AtomicWriteResult(path, result);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp-*"));
        Assert.Equal("id", JsonSerializer.Deserialize<PimaxUsbPortCyclePrivilegedResult>(File.ReadAllText(path), PimaxUsbPortCycleJson.Options)!.ExperimentId);
    }

    [Theory]
    [InlineData(false, false, true, true, 0, false, "no-port-transition-observed")]
    [InlineData(true, false, true, true, 0, false, "usb2-only-transitioned-registration-unavailable")]
    [InlineData(true, true, true, true, 0, true, "both-sides-transitioned-registration-ready")]
    [InlineData(true, false, true, true, 2, false, "partial-pimax-descendant-return")]
    [InlineData(true, false, false, true, 0, true, "unexpected-nontarget-transition")]
    [InlineData(true, false, true, false, 0, true, "unexpected-nontarget-transition")]
    public void ObservationOutcomesAreDistinct(bool usb2, bool superSpeed, bool vive, bool unrelated, int missing, bool ready, string expected)
        => Assert.Equal(expected, PimaxUsbPortCycleObservationClassifier.Classify(usb2, superSpeed, vive, unrelated, missing, ready));

    [Fact]
    public void StaticSafetyHasNoForbiddenMutationOrPhase29DeploymentPath()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "PimaxUsbPortCycleExperiment.cs"));
        var forbidden = new[] { "CM_Reenumerate", "SetupDiCallClassInstaller", "Disable-PnpDevice", "Enable-PnpDevice", "pnputil", "devcon", "TerminateProcess", "Restart-Service", "Phase29B-d347151", "SendInput" };
        foreach (var token in forbidden) Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SchemaSerializesAsOneJsonDocument()
    {
        var result = new PimaxUsbPortCycleExperimentResult(PimaxUsbPortCycleExperimentSchema.Version, "id", "dry-run", Now, Now, null, new(false, [], ["rejected"], []), null, null, null, null, null, null, null, [], []);
        var json = JsonSerializer.Serialize(result, PimaxUsbPortCycleJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("pimax-usb-port-cycle-experiment-v1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(json.Length, document.RootElement.GetRawText().Length);
    }

    private static (PimaxUsbPortCycleTargetSignature Signature, PimaxUsbPortCycleRuntimeState State) Fixture()
    {
        var usb2 = Hub("hub2", @"\\?\usb#vid_05e3&pid_0610#exact#{guid}", "USB\\VID_05E3&PID_0610\\exact", "0610", "usb20");
        var usb3 = Hub("hub3", @"\\?\usb#vid_05e3&pid_0626#exact#{guid}", "USB\\VID_05E3&PID_0626\\exact", "0626", "usb30");
        var ports = new[]
        {
            Port(usb2, 1, "other", ["other2"]), Port(usb2, 2, "vive", ["vive2"], "connector:vive", usb3, 2),
            Port(usb2, 3, "other", []), Port(usb2, 4, "pimax-related", ["pimax2"], "connector:pimax", usb3, 4),
            Port(usb3, 1, "other", ["other3"]), Port(usb3, 2, "vive", ["vive3"], "connector:vive", usb2, 2),
            Port(usb3, 3, "other", []), Port(usb3, 4, "pimax-related", ["pimax3"], "connector:pimax", usb2, 4)
        };
        var snapshot = Snapshot([usb2, usb3], ports);
        var phase = Phase29();
        var signature = new PimaxUsbPortCycleTargetSignature("phase-28c3b-target-signature-v1", PimaxUsbPortCycleTargetValidator.Identity(usb2),
            new("connector:pimax", 4, "pimax-related", ["pimax2"]), PimaxUsbPortCycleTargetValidator.Identity(usb3),
            new("connector:pimax", 4, "pimax-related", ["pimax3"]), new("connector:vive", 2, "vive", ["vive2"]),
            new("connector:vive", 2, "vive", ["vive3"]), PimaxUsbPortCycleTargetValidator.Inventory(snapshot, usb2, usb3), phase);
        var observer = new PimaxUsbPortCycleObserverBinding("observer", Now.AddSeconds(-1), "running", "connect-scan-active", Now.AddSeconds(-2), 2, "marker-hash");
        return (signature, new(snapshot, Registration("likelyPoweredOnAwaitingRegistration", "probable"), Connectivity(), true, false, false, observer, phase));
    }

    private static PimaxUsbHubRecord Hub(string id, string path, string pnp, string pid, string type)
        => new(id, path, id, pnp, "parent", "container", "driver", [$"USB\\VID_05E3&PID_{pid}"], [], "05E3", pid, "USB", "guid", "vendor", "hub", null, null, ["location"], type == "usb20" ? ["USB 2.0"] : ["USB 3.x"], type, false, true, 4, Now, [], []);

    private static PimaxUsbPortRecord Port(PimaxUsbHubRecord hub, int index, string classification, string[] descendants, string? group = null, PimaxUsbHubRecord? companion = null, int companionIndex = 0)
        => new($"{hub.HubId}:{index}", hub.HubId, hub.InterfacePath, index, descendants.Length > 0 ? "deviceConnected" : "noDeviceConnected", "high", hub.UsbProtocolSupport,
            descendants.Length > 0, index is 2 or 4, index == 2 ? (ushort)0x2109 : (ushort)0x0424, (ushort)index, 1, 9, 0, 1,
            descendants.Length > 0 ? $"driver-{hub.HubId}-{index}" : null, descendants.FirstOrDefault(), "child-container", ["child-location"], [], [], null, "userConnectable", false,
            companion is null ? [] : [new(0, companionIndex, companion.InterfacePath, true)], group, classification, descendants, Now, Now, [], []);

    private static PimaxUsbPhysicalPortSnapshot Snapshot(PimaxUsbHubRecord[] hubs, PimaxUsbPortRecord[] ports)
    {
        var counts = new Dictionary<string, int>();
        var pnp = new PimaxUsbEnumerationSnapshot(PimaxUsbEnumerationSchema.Version, Now, "test", new("Windows", "X64", false), new(0, 0, 0, 0, counts, counts, counts, counts, counts), [], [], [], []);
        var groups = new[]
        {
            new PimaxUsbPhysicalConnectorGroup("connector:pimax", ports.Where(port => port.PhysicalConnectorGroupId == "connector:pimax").Select(port => port.PortId).ToArray(), [], [], "high", true, false, "pimax-related", ["pimax2", "pimax3"], false),
            new PimaxUsbPhysicalConnectorGroup("connector:vive", ports.Where(port => port.PhysicalConnectorGroupId == "connector:vive").Select(port => port.PortId).ToArray(), [], [], "high", true, false, "unrelated-or-unresolved", ["vive2", "vive3"], false)
        };
        return new(Now, hubs, ports, groups, [], hubs[0].HubId, "connector:pimax", "connector:vive", pnp, [], []);
    }

    private static PimaxRegistrationAssessmentSnapshot Registration(string state, string confidence)
        => JsonSerializer.Deserialize<PimaxRegistrationAssessmentSnapshot>(JsonSerializer.Serialize(new
        {
            schemaVersion = "pimax-registration-assessment-v1",
            collectedAt = Now,
            assessment = new { state, confidence, explanation = "test", supportingEvidence = Array.Empty<string>(), missingEvidence = Array.Empty<string>(), warnings = Array.Empty<string>(), conflicts = Array.Empty<string>(), evidence = new { crystalRuntimeGroupPresent = false } },
            sourceSchemaVersions = new { }, filteredSnapshot = new { }, expandedSnapshot = new { }, collectionGapMs = 0,
            warnings = Array.Empty<string>(), errors = Array.Empty<string>()
        }), PimaxRegistrationAssessmentJson.Options)!;

    private static PimaxConnectivitySnapshot Connectivity()
        => JsonSerializer.Deserialize<PimaxConnectivitySnapshot>(JsonSerializer.Serialize(new
        {
            schemaVersion = "pimax-connectivity-v1", collectedAt = Now,
            assessment = new { value = "windowsDevicesPresentRuntimeNotConfirmed", confidence = "probable", explanation = "test", supportingEvidence = Array.Empty<string>(), missingEvidence = Array.Empty<string>(), warnings = Array.Empty<string>() },
            warnings = Array.Empty<string>(), errors = Array.Empty<string>()
        }), PimaxConnectivityJson.Options)!;

    private static PimaxPhase29IntegritySignature Phase29()
        => new("task", "\\", @"C:\deployment\watcher.exe", "--args", @"C:\deployment", "HASH", new(@"C:\logs\supervisor.jsonl", 10, Now), new(@"C:\logs\configurator.jsonl", 10, Now));

    private static int Count(string text, string value) { var count = 0; var offset = 0; while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0) { count++; offset += value.Length; } return count; }
    private static string FindRepositoryRoot() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }

    private sealed class FakeNativeAdapter : IPimaxUsbPortCycleNativeAdapter
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public int Index { get; private set; }
        public PimaxUsbPortCycleNativeResponse CycleUsb2PortOnce(string hubInterfacePath, int connectionIndex)
        {
            Calls++; Path = hubInterfacePath; Index = connectionIndex;
            return new(true, 0, 0);
        }
    }
}
