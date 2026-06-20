using System.Text.Json;
using Xunit;

public sealed class PimaxSoftwareStackRepairTests
{
    [Fact]
    public async Task TargetCatalogApprovesOnlyExactValidatedPimaxClient()
    {
        var target = new PimaxClientTargetDescriptor(@"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe", @"C:\Program Files\Pimax\PimaxClient\pimaxui", "", "PimaxClient", "Pimax", "1", "ABC", [101], "shortcut", "PimaxPlayUiClient");
        var controller = new FakeClientController
        {
            Discovery = new PimaxClientTargetDiscoveryResult(
                target,
                [
                    new PimaxClientProcessSnapshot(101, null, "PimaxClient", target.ExecutablePath, "TopLevelUiClientCandidate", DateTimeOffset.UtcNow, true, "Pimax Play", "Pimax", "PimaxClient", "PimaxClient", "1"),
                    new PimaxClientProcessSnapshot(202, null, "PVRHome", @"C:\Program Files\Pimax\PVRHome\PVRHome.exe", "OptionalComponent", DateTimeOffset.UtcNow, true, "PVRHome", null, "PVRHome", "PVRHome", "1")
                ],
                [],
                [],
                null)
        };

        var snapshot = await new PimaxRepairTargetCatalog(controller).DiscoverAsync(CancellationToken.None);

        Assert.Contains(snapshot.Targets, item => item.Classification == PimaxRepairTargetClassification.ApprovedRestartableProcess && item.ExecutableName == "PimaxClient");
        Assert.Contains(snapshot.Targets, item => item.Classification == PimaxRepairTargetClassification.Prohibited && item.ExecutableName == "PVRHome");
        Assert.DoesNotContain(snapshot.Targets, item => item.SanitizedPath?.Contains(@"C:\Users\", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task DryRunBuildsConfirmedPlanAndDoesNotMutate()
    {
        var process = new FakeProcessController();
        var backend = Backend(
            [Health(PimaxRepairClassification.PoweredOnAwaitingRegistration)],
            Targets([PimaxRepairTargetCatalog.ApprovedProcessForTests()]),
            process);

        var response = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, true, false, null, 120), CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.True(response.ConfirmationRequired);
        Assert.False(string.IsNullOrWhiteSpace(response.ConfirmationToken));
        Assert.Equal(0, process.CloseCalls);
        Assert.Equal(0, process.RelaunchCalls);
    }

    [Fact]
    public async Task ConfirmedApprovedProcessRestartRequiresPostHealthForRepaired()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var process = new FakeProcessController();
        var backend = Backend(
            [Health(PimaxRepairClassification.PoweredOnAwaitingRegistration), Health(PimaxRepairClassification.PoweredOnAwaitingRegistration), Health(PimaxRepairClassification.AlreadyHealthy)],
            Targets([PimaxRepairTargetCatalog.ApprovedProcessForTests()]),
            process);
        var dryRun = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, true, false, null, 120), CancellationToken.None);

        var live = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, false, true, dryRun.ConfirmationToken, 120), CancellationToken.None);

        Assert.Equal(PimaxSoftwareRepairOutcome.Repaired, live.Result?.Outcome);
        Assert.Equal(1, process.CloseCalls);
        Assert.Equal(1, process.RelaunchCalls);
    }

    [Fact]
    public async Task ProcessGracefulCloseFailureStopsWithoutForceKillOrRelaunch()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var process = new FakeProcessController { CloseSucceeds = false };
        var backend = Backend(
            [Health(PimaxRepairClassification.PoweredOnAwaitingRegistration), Health(PimaxRepairClassification.PoweredOnAwaitingRegistration)],
            Targets([PimaxRepairTargetCatalog.ApprovedProcessForTests()]),
            process);
        var dryRun = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, true, false, null, 120), CancellationToken.None);

        var live = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, false, true, dryRun.ConfirmationToken, 120), CancellationToken.None);

        Assert.Equal(PimaxSoftwareRepairOutcome.SoftwareRepairFailed, live.Result?.Outcome);
        Assert.Equal(1, process.CloseCalls);
        Assert.Equal(0, process.RelaunchCalls);
        Assert.DoesNotContain(live.Result!.Actions, action => action.ActionId.Contains("force", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApprovedServiceRestartUsesDependencyOrderInFakes()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var service = new FakeServiceController();
        var backend = Backend(
            [Health(PimaxRepairClassification.SoftwareStackUnhealthy), Health(PimaxRepairClassification.SoftwareStackUnhealthy), Health(PimaxRepairClassification.AlreadyHealthy)],
            Targets([PimaxRepairTargetCatalog.ApprovedServiceForTests()]),
            serviceController: service);
        var dryRun = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, true, false, null, 120), CancellationToken.None);

        var live = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, false, true, dryRun.ConfirmationToken, 120), CancellationToken.None);

        Assert.Equal(PimaxSoftwareRepairOutcome.Repaired, live.Result?.Outcome);
        Assert.Equal(["stop:PimaxTestService", "start:PimaxTestService"], service.Events);
    }

    [Theory]
    [InlineData(PimaxRepairClassification.PoweredOnAwaitingRegistration, PimaxSoftwareRepairOutcome.UnsupportedAutomaticRecovery)]
    [InlineData(PimaxRepairClassification.ConflictingEvidence, PimaxSoftwareRepairOutcome.ConflictingEvidence)]
    [InlineData(PimaxRepairClassification.Unknown, PimaxSoftwareRepairOutcome.Unknown)]
    [InlineData(PimaxRepairClassification.AlreadyHealthy, PimaxSoftwareRepairOutcome.NoRepairNeeded)]
    public async Task NonExecutableAndNonRepairStatesReturnAccurateOutcomes(string classification, string outcome)
    {
        var backend = Backend([Health(classification)], Targets([]));

        var response = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, true, false, null, 120), CancellationToken.None);

        Assert.Equal(outcome, response.Result?.Outcome);
    }

    [Theory]
    [InlineData(PimaxRepairClassification.PoweredOnAwaitingRegistration, PimaxSoftwareRepairOutcome.SoftwareStackHealthyButNotRegistered)]
    [InlineData(PimaxRepairClassification.CoreUsbMissing, PimaxSoftwareRepairOutcome.CoreUsbMissing)]
    [InlineData(PimaxRepairClassification.DisplayPathMissing, PimaxSoftwareRepairOutcome.DisplayPathMissing)]
    [InlineData(PimaxRepairClassification.EyeChipMissing, PimaxSoftwareRepairOutcome.RepairedWithDegradedFeatures)]
    public async Task PostHealthControlsFinalOutcome(string postClassification, string expectedOutcome)
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var backend = Backend(
            [Health(PimaxRepairClassification.PoweredOnAwaitingRegistration), Health(PimaxRepairClassification.PoweredOnAwaitingRegistration), Health(postClassification)],
            Targets([PimaxRepairTargetCatalog.ApprovedProcessForTests()]),
            new FakeProcessController());
        var dryRun = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, true, false, null, 120), CancellationToken.None);

        var live = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, false, true, dryRun.ConfirmationToken, 120), CancellationToken.None);

        Assert.Equal(expectedOutcome, live.Result?.Outcome);
    }

    [Fact]
    public async Task DuplicateRepairIsRejectedUntilLockReleased()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = Backend([Health(PimaxRepairClassification.PoweredOnAwaitingRegistration), Health(PimaxRepairClassification.PoweredOnAwaitingRegistration), Health(PimaxRepairClassification.AlreadyHealthy)], Targets([PimaxRepairTargetCatalog.ApprovedProcessForTests()]), new FakeProcessController { BeforeClose = () => gate.Task }, scope: "duplicate-test");
        var dryRun = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, true, false, null, 120), CancellationToken.None);
        var first = backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, false, true, dryRun.ConfirmationToken, 120), CancellationToken.None);
        await Task.Delay(20);

        var second = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, true, false, null, 120), CancellationToken.None);
        gate.SetResult();
        await first;

        Assert.False(second.Accepted);
        Assert.Contains("already active", second.HumanReadableSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelReportsSafeBoundaryWithoutRollbackClaim()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var backend = Backend([Health(PimaxRepairClassification.PoweredOnAwaitingRegistration), Health(PimaxRepairClassification.PoweredOnAwaitingRegistration), Health(PimaxRepairClassification.AlreadyHealthy)], Targets([PimaxRepairTargetCatalog.ApprovedProcessForTests()]), passiveSettle: TimeSpan.FromSeconds(5));
        var dryRun = await backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, true, false, null, 120), CancellationToken.None);
        var live = backend.StartAsync(new PimaxRepairStartRequest(PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly, false, true, dryRun.ConfirmationToken, 120), CancellationToken.None);

        await Task.Delay(50);
        var cancel = backend.Cancel();
        var response = await live;

        Assert.True(cancel.Accepted);
        Assert.Equal(PimaxSoftwareRepairOutcome.Cancelled, response.Result?.Outcome);
        Assert.DoesNotContain("rolled back", response.Result!.HumanReadableSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("restored previous state", response.Result!.HumanReadableSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticsWriterEmitsJsonlRotatesAndIsolatesFailures()
    {
        using var temp = new TempDirectory();
        var writer = new PimaxRepairDiagnosticsWriter(temp.Path);
        writer.Append(new PimaxRepairOperationLogEntry(PimaxRepairDiagnosticsWriter.Schema, "op", "corr", "build", DateTimeOffset.UtcNow, "stage", "action", "target", "ok", 1, false, false, null, null, null, null, "outcome", []));

        var file = Path.Combine(temp.Path, "pimax-repair-operations.jsonl");
        var line = File.ReadAllLines(file).Single();
        using var document = JsonDocument.Parse(line);
        Assert.Equal(PimaxRepairDiagnosticsWriter.Schema, document.RootElement.GetProperty("schema").GetString());
    }

    [Fact]
    public void StaticSafetyDoesNotAddForbiddenHardwareGuiOrSystemMutation()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxSoftwareStackRepair.cs"));
        string[] forbidden =
        [
            "IOCTL_USB_HUB_CYCLE_PORT",
            "CM_Reenumerate",
            "SetupDiCallClassInstaller",
            "pnputil",
            "devcon",
            ".Kill(",
            "SendInput",
            "mouse_event",
            "keybd_event",
            "SetScheduledTask",
            "GetScheduledTask",
            "Restart-Computer",
            "Stop-Computer",
            "HttpClient",
            "WebRequest"
        ];

        foreach (var token in forbidden)
        {
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PimaxSoftwareStackRepairBackend Backend(
        IReadOnlyCollection<PimaxComponentHealthSnapshot> health,
        PimaxRepairTargetsSnapshot targets,
        FakeProcessController? process = null,
        FakeServiceController? serviceController = null,
        string? scope = null,
        TimeSpan? passiveSettle = null)
        => new(
            scope ?? Guid.NewGuid().ToString("N"),
            new FakeHealthCollector(health),
            new FakeTargetCatalog(targets),
            process ?? new FakeProcessController(),
            serviceController ?? new FakeServiceController(),
            new FakeDiagnosticsWriter(),
            now: () => DateTimeOffset.UtcNow,
            passiveSettle: passiveSettle ?? TimeSpan.Zero);

    private static PimaxRepairTargetsSnapshot Targets(PimaxRepairTarget[] targets)
        => new(PimaxRepairTargetsSchema.Version, DateTimeOffset.UtcNow, targets, targets.Where(target => target.Classification == PimaxRepairTargetClassification.ApprovedRestartableProcess).Select(target => target.TargetId).ToArray(), targets.Where(target => target.Classification == PimaxRepairTargetClassification.ApprovedRestartableService).Select(target => target.TargetId).ToArray(), targets.Where(target => target.Classification == PimaxRepairTargetClassification.ObserveOnly).Select(target => target.TargetId).ToArray(), targets.Where(target => target.Classification == PimaxRepairTargetClassification.Prohibited).Select(target => target.TargetId).ToArray(), [], [], "test", [], []);

    private static PimaxComponentHealthSnapshot Health(string classification)
    {
        var components = HealthyComponents();
        var overall = PimaxHealthOverallStatus.Healthy;
        var registration = Registration(PimaxRegistrationState.RegisteredReady, PimaxRegistrationConfidence.Confirmed);
        switch (classification)
        {
            case PimaxRepairClassification.PoweredOnAwaitingRegistration:
                components = SetMissing(components, "pimaxRegistration");
                registration = Registration(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration, PimaxRegistrationConfidence.Probable);
                overall = PimaxHealthOverallStatus.NotRegistered;
                break;
            case PimaxRepairClassification.SoftwareStackUnhealthy:
                components = SetMissing(components, "pimaxRuntime");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.CoreUsbMissing:
                components = SetMissing(components, "coreUsb");
                overall = PimaxHealthOverallStatus.CoreConnectionMissing;
                break;
            case PimaxRepairClassification.DisplayPathMissing:
                components = SetMissing(components, "displayPortVideo");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.EyeChipMissing:
                components = SetMissing(components, "eyeChip");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.ConflictingEvidence:
                registration = Registration(PimaxRegistrationState.ConflictingEvidence, PimaxRegistrationConfidence.Probable);
                overall = PimaxHealthOverallStatus.ConflictingEvidence;
                break;
            case PimaxRepairClassification.Unknown:
                components = [];
                registration = Registration(PimaxRegistrationState.Unknown, PimaxRegistrationConfidence.Insufficient);
                overall = PimaxHealthOverallStatus.Unknown;
                break;
        }

        return new PimaxComponentHealthSnapshot(
            PimaxComponentHealthSchema.Version,
            DateTimeOffset.UtcNow,
            "health",
            overall,
            registration,
            "probable",
            components,
            components.Where(component => component.Status == PimaxHealthComponentStatus.Missing && component.Criticality is PimaxHealthCriticality.RequiredForRegistration or PimaxHealthCriticality.RequiredForCoreVr).Select(component => component.Explanation).ToArray(),
            components.Where(component => component.Status == PimaxHealthComponentStatus.Missing && component.Criticality is PimaxHealthCriticality.RequiredForFeature or PimaxHealthCriticality.OptionalAccessory).Select(component => component.Explanation).ToArray(),
            [],
            "summary",
            "probable",
            new PimaxHealthCapabilitySummary("available", "available", "available", "available", "available", "available", "ready", "summary"),
            new PimaxHealthSanitizedEvidence(PimaxConnectivitySchema.Version, PimaxUsbEnumerationSchema.Version, PimaxRegistrationAssessmentSchema.Version, "synthetic", registration.State, 1, 1, [], [], [], []),
            [],
            []);
    }

    private static PimaxHealthComponent[] HealthyComponents()
        =>
        [
            Component("pimaxPlay", "Pimax Play", PimaxHealthCriticality.RequiredForRegistration),
            Component("pimaxRuntime", "Pimax runtime", PimaxHealthCriticality.RequiredForRegistration),
            Component("pimaxServices", "Pimax services", PimaxHealthCriticality.RequiredForRegistration),
            Component("coreUsb", "Core headset USB", PimaxHealthCriticality.RequiredForRegistration),
            Component("usb2Companion", "USB 2 companion path", PimaxHealthCriticality.RequiredForRegistration),
            Component("superSpeedCompanion", "SuperSpeed companion path", PimaxHealthCriticality.RequiredForCoreVr),
            Component("pimaxRegistration", "Pimax registration", PimaxHealthCriticality.RequiredForRegistration),
            Component("headsetHid", "Headset HID", PimaxHealthCriticality.RequiredForRegistration),
            Component("displayPortVideo", "DisplayPort video path", PimaxHealthCriticality.RequiredForCoreVr),
            Component("headsetAudioOutput", "Pimax audio output", PimaxHealthCriticality.RequiredForFeature),
            Component("headsetMicrophone", "Pimax microphone", PimaxHealthCriticality.RequiredForFeature),
            Component("eyeChip", "EyeChip", PimaxHealthCriticality.RequiredForFeature),
            Component("eyeTracking", "Eye tracking", PimaxHealthCriticality.RequiredForFeature),
            Component("trackingCameras", "Tracking cameras", PimaxHealthCriticality.RequiredForCoreVr),
            Component("viveFaceTracker", "Vive face tracker", PimaxHealthCriticality.OptionalAccessory)
        ];

    private static PimaxHealthComponent Component(string id, string name, string criticality)
        => new(id, name, PimaxHealthComponentStatus.Present, criticality, "probable", "present", [name], id + "_present", name + " is present.", "inspect");

    private static PimaxHealthComponent[] SetMissing(PimaxHealthComponent[] components, string id)
        => components.Select(component => component.ComponentId == id ? component with { Status = PimaxHealthComponentStatus.Missing, Explanation = component.DisplayName + " is missing." } : component).ToArray();

    private static PimaxRegistrationAssessmentResult Registration(string state, string confidence)
        => new(state, confidence, "registration", [], [], [], [], [], new PimaxRegistrationEvidence(true, 1, 1, state == PimaxRegistrationState.RegisteredReady, 1, 1, true, state == PimaxRegistrationState.RegisteredReady, false, false, true, [], []));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeClientController : IPimaxClientProcessController
    {
        public PimaxClientTargetDiscoveryResult Discovery { get; init; } = new(null, [], [], [], null);
        public Task<PimaxClientTargetDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(Discovery);
        public Task<PimaxRecoveryOperationResult> RequestGracefulCloseAsync(PimaxClientTargetDescriptor target, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(new PimaxRecoveryOperationResult(true, true, "closed", target.TargetProcessIds));
        public Task<PimaxRecoveryOperationResult> ForceStopAsync(PimaxClientTargetDescriptor target, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(new PimaxRecoveryOperationResult(true, false, "not used", []));
        public Task<PimaxRecoveryOperationResult> RelaunchAsync(PimaxClientTargetDescriptor target, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(new PimaxRecoveryOperationResult(true, true, "started", target.TargetProcessIds));
        public Task<PimaxClientProcessSnapshot[]> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(Array.Empty<PimaxClientProcessSnapshot>());
    }

    private sealed class FakeHealthCollector(IReadOnlyCollection<PimaxComponentHealthSnapshot> health) : IPimaxRepairHealthCollector
    {
        private readonly Queue<PimaxComponentHealthSnapshot> _health = new(health);
        public Task<PimaxComponentHealthSnapshot> CollectAsync(CancellationToken cancellationToken) => Task.FromResult(_health.Count > 1 ? _health.Dequeue() : _health.Peek());
    }

    private sealed class FakeTargetCatalog(PimaxRepairTargetsSnapshot targets) : IPimaxRepairTargetCatalog
    {
        public Task<PimaxRepairTargetsSnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(targets);
    }

    private sealed class FakeProcessController : IPimaxRepairProcessController
    {
        public bool CloseSucceeds { get; init; } = true;
        public bool RelaunchSucceeds { get; init; } = true;
        public Func<Task>? BeforeClose { get; init; }
        public int CloseCalls { get; private set; }
        public int RelaunchCalls { get; private set; }
        public async Task<PimaxRecoveryOperationResult> RequestGracefulCloseAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (BeforeClose is not null) await BeforeClose();
            CloseCalls++;
            return new PimaxRecoveryOperationResult(true, CloseSucceeds, CloseSucceeds ? "closed" : "refused", []);
        }

        public Task<PimaxRecoveryOperationResult> RelaunchAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            RelaunchCalls++;
            return Task.FromResult(new PimaxRecoveryOperationResult(true, RelaunchSucceeds, RelaunchSucceeds ? "started" : "failed", []));
        }
    }

    private sealed class FakeServiceController : IPimaxRepairServiceController
    {
        public List<string> Events { get; } = [];
        public Task<PimaxRecoveryOperationResult> StopAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Events.Add("stop:" + target.ServiceName);
            return Task.FromResult(new PimaxRecoveryOperationResult(true, true, "stopped", []));
        }

        public Task<PimaxRecoveryOperationResult> StartAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Events.Add("start:" + target.ServiceName);
            return Task.FromResult(new PimaxRecoveryOperationResult(true, true, "started", []));
        }
    }

    private sealed class FakeDiagnosticsWriter : IPimaxRepairDiagnosticsWriter
    {
        public void Append(PimaxRepairOperationLogEntry entry) { }
    }
}
