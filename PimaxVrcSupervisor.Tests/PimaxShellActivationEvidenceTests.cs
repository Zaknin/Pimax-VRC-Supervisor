using Xunit;

public sealed class PimaxShellActivationEvidenceTests
{
    [Fact]
    public async Task ElevatedCollectorRefusesNonElevatedExecutionWithoutSelfElevation()
    {
        var coordinator = new PimaxShellActivationEvidenceCoordinator(
            context: new FakeEvidenceContext(elevated: false),
            store: new FakeEvidenceStore(),
            collectorProbe: ValidCollectorProbe());

        var result = await coordinator.CollectElevatedAsync(
            new PimaxShellActivationEvidenceCommandLine(Guid.NewGuid().ToString(), 60, null, Fake: false),
            CancellationToken.None);

        Assert.Equal(PimaxShellActivationEvidenceCollectorSchema.Version, result.Schema);
        Assert.False(result.Accepted);
        Assert.False(result.ShellActivationRequested);
        Assert.False(result.ProcessMutationAttempted);
        Assert.False(result.ServiceMutationAttempted);
        Assert.Contains("administrator token", string.Join("\n", result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ElevatedCollectorBoundsTtlAndWritesHashedEnvelope()
    {
        var store = new FakeEvidenceStore();
        var coordinator = new PimaxShellActivationEvidenceCoordinator(
            context: new FakeEvidenceContext(elevated: true),
            store: store,
            collectorProbe: ValidCollectorProbe(),
            now: () => DateTimeOffset.Parse("2026-06-21T00:00:00Z"));
        var correlationId = Guid.NewGuid();

        var result = await coordinator.CollectElevatedAsync(
            new PimaxShellActivationEvidenceCommandLine(correlationId.ToString(), 500, null, Fake: false),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(120, result.TtlSeconds);
        Assert.Equal(store.EvidencePath(correlationId), result.EvidenceFile);
        Assert.NotNull(store.LastEnvelope);
        Assert.Equal(PimaxShellActivationEvidenceEnvelopeSchema.Version, store.LastEnvelope.Schema);
        Assert.Equal(PimaxShellActivationEvidencePurpose.ReadOnlyGateAssessment, store.LastEnvelope.Purpose);
        Assert.False(string.IsNullOrWhiteSpace(store.LastEnvelope.EnvelopeContentHash));
        Assert.DoesNotContain("RawProcessId", System.Text.Json.JsonSerializer.Serialize(result, PimaxRepairJson.Options), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssessmentRejectsModifiedEnvelopeHash()
    {
        var envelope = ValidEnvelope();
        var store = new FakeEvidenceStore { EnvelopeToRead = envelope with { EnvelopeContentHash = "bad" } };
        var coordinator = new PimaxShellActivationEvidenceCoordinator(
            context: new FakeEvidenceContext(elevated: false),
            store: store,
            capabilityBuilder: (_, _, _) => Task.FromResult(ReadyCapability()));

        var result = await coordinator.AssessAsync(
            new SupervisorConfig(),
            new PimaxShellActivationEvidenceCommandLine(envelope.CorrelationId, 60, store.EvidencePath(Guid.Parse(envelope.CorrelationId)), Fake: false),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.False(result.EvidenceConsumed);
        Assert.Equal("mismatch", result.ContentHashValidationState);
        Assert.Contains("hash", string.Join("\n", result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssessmentConsumesValidEvidenceOnceAndKeepsActivationDisabled()
    {
        var envelope = ValidEnvelope();
        var store = new FakeEvidenceStore { EnvelopeToRead = envelope };
        var coordinator = new PimaxShellActivationEvidenceCoordinator(
            context: new FakeEvidenceContext(elevated: false),
            store: store,
            capabilityBuilder: (_, _, _) => Task.FromResult(ReadyCapability()),
            now: () => DateTimeOffset.Parse("2026-06-21T00:00:10Z"));

        var result = await coordinator.AssessAsync(
            new SupervisorConfig(),
            new PimaxShellActivationEvidenceCommandLine(envelope.CorrelationId, 60, store.EvidencePath(Guid.Parse(envelope.CorrelationId)), Fake: false),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.True(result.EvidenceValid);
        Assert.True(result.EvidenceFresh);
        Assert.True(result.EvidenceConsumed);
        Assert.True(result.EvidenceFileDeleted);
        Assert.Equal(1, store.DeleteCount);
        Assert.Equal("quiescentForShellActivation", result.Precondition.ActivationPreconditionState);
        Assert.True(result.Capability.ReadinessForControlledValidation);
        Assert.False(result.BackendExecutable);
        Assert.False(result.AutomaticRecoveryAllowed);
        Assert.False(result.ActivationExecuted);
        Assert.Equal(0, result.ShellRequestCount);
        Assert.Equal(0, result.RetryCount);
    }

    [Fact]
    public async Task AssessmentRequiresNormalNonElevatedExplorerSession()
    {
        var envelope = ValidEnvelope();
        var store = new FakeEvidenceStore { EnvelopeToRead = envelope };
        var coordinator = new PimaxShellActivationEvidenceCoordinator(
            context: new FakeEvidenceContext(elevated: true, explorerMatched: true),
            store: store,
            capabilityBuilder: (_, _, _) => Task.FromResult(ReadyCapability()));

        var result = await coordinator.AssessAsync(
            new SupervisorConfig(),
            new PimaxShellActivationEvidenceCommandLine(envelope.CorrelationId, 60, store.EvidencePath(Guid.Parse(envelope.CorrelationId)), Fake: false),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("non-elevated", string.Join("\n", result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    private static PimaxShellActivationEvidenceCollectorProbeResult ValidProbeResult()
        => new(
            SampleCount: 3,
            StableSampleCount: 3,
            StableSetResult: "stable",
            PrivateBindings:
            [
                new PimaxShellActivationEvidencePrivateBinding(
                    "vrss_gaze_provider",
                    100,
                    50,
                    "2026-06-21T00:00:00.0000000Z",
                    0,
                    @"C:\Program Files\Pimax\Runtime\vrss_gaze_provider.exe",
                    "123:456",
                    123456,
                    "2026-06-21T00:00:00.0000000Z",
                    "829327485C0B4B09CBF75F5FAE5E3AB5FC0D13FCFB7E273C682495094E6186CF",
                    "unsigned",
                    "stable-vrss")
            ],
            PublicProcesses:
            [
                new PimaxShellActivationEvidencePublicProcess(
                    "vrss_gaze_provider",
                    @"<pimax>\Runtime\vrss_gaze_provider.exe",
                    "exactExpectedRuntimePath",
                    "expectedPimaxRuntimeRoot",
                    "session0",
                    "unsigned",
                    "829327485C0B4B09CBF75F5FAE5E3AB5FC0D13FCFB7E273C682495094E6186CF",
                    123456,
                    "2026-06-21T00:00:00.0000000Z",
                    StableAcrossSamples: true,
                    SingleInstance: true,
                    ExpectedRuntimeRoot: true,
                    ReparsePointRejected: false,
                    UserWritablePath: false,
                    "persistentServiceDescendant",
                    "serviceControlManagerViaExitedLauncher",
                    "elevatedReadOnlyEvidenceAndPreservedObservation",
                    "probable",
                    "parentExitedOrUnavailable",
                    "PiServiceLauncher",
                    "expectedPiServiceLauncherPath",
                    "trustedSignedExpectedLauncher",
                    "synthetic evidence")
            ],
            ServiceEvidence: ValidService(),
            ProvenanceClassification: "preservedElevatedObservation",
            ProvenanceConfidence: "probable",
            Warnings: [],
            Errors: []);

    private static IPimaxShellActivationEvidenceCollectorProbe ValidCollectorProbe()
        => new FakeCollectorProbe(ValidProbeResult());

    private static PimaxShellActivationEvidenceServiceEvidence ValidService()
        => new(
            "PiServiceLauncher",
            "PiServiceLauncher",
            "Running",
            "Auto",
            "LocalSystem",
            @"<pimax>\Runtime\PiServiceLauncher.exe",
            @"C:\Program Files\Pimax\Runtime\PiServiceLauncher.exe",
            "expectedPiServiceLauncherPath",
            "7CDC5EDB615A2499C93A3DBF181E78825EAC7FB5B01E6F88E522FB81E68605A7",
            "signaturePresent",
            "trustedSignedExpectedLauncher",
            ServiceConfigurationAmbiguous: false,
            DuplicateLauncherServiceExists: false);

    private static PimaxShellActivationEvidenceEnvelope ValidEnvelope()
    {
        var coordinator = new PimaxShellActivationEvidenceCoordinator(
            context: new FakeEvidenceContext(elevated: true),
            store: new FakeEvidenceStore(),
            collectorProbe: ValidCollectorProbe(),
            now: () => DateTimeOffset.Parse("2026-06-21T00:00:00Z"));
        return coordinator.BuildEnvelope(
            Guid.NewGuid(),
            60,
            DateTimeOffset.Parse("2026-06-21T00:00:00Z"),
            ValidProbeResult(),
            [],
            []);
    }

    private static PimaxShellActivationCapabilitySnapshot ReadyCapability()
    {
        var precondition = new PimaxShellActivationPreconditionSnapshot(
            PimaxShellActivationPreconditionSchema.Version,
            DateTimeOffset.Parse("2026-06-21T00:00:10Z"),
            "ready",
            PimaxHealthOverallStatus.SoftwareStackPartial,
            PimaxSoftwareGroupState.Partial,
            PimaxShellActivationPreconditionState.QuiescentForShellActivation,
            Quiescent: true,
            Stable: true,
            StableSampleCount: 3,
            RequiredStableSampleCount: 3,
            SampleIntervalSeconds: 1,
            CoreMembersPresent: [],
            LaunchOwnedMembersPresent: [],
            PermittedPersistentMembersPresent: ["PiPlatformService_64", "platform_runtime_VR4PIMAXP3B_service", "vrss_gaze_provider"],
            UnclassifiedMembersPresent: [],
            OwnershipEvidence: [],
            PiServiceLauncherClassification: "notObserved",
            RegistrationEvidenceState: PimaxRegistrationState.RegistrationEvidenceStale,
            StaleRegistrationBlocking: false,
            DuplicateInstallationEvidence: "none",
            RecoveryLeaseState: "noneObserved",
            ShellEntryTrustState: PimaxShellActivationCapabilityState.ReadyForControlledValidation,
            ReadinessForControlledValidation: true,
            BackendExecutable: false,
            AutomaticRecoveryAllowed: false,
            Warnings: [],
            Errors: [],
            PrivacyRedactions: ["raw PIDs", "raw parent PIDs"],
            HumanReadableSummary: "ready");
        return new PimaxShellActivationCapabilitySnapshot(
            PimaxShellActivationCapabilitySchema.Version,
            DateTimeOffset.Parse("2026-06-21T00:00:10Z"),
            PimaxShellActivationCapabilityState.ReadyForControlledValidation,
            CandidateCount: 1,
            Candidates: [],
            SelectedShellEntry: null,
            SanitizedShortcutPath: @"<common-start-menu>\PimaxPlay.lnk",
            SanitizedTargetPath: @"<pimax>\PimaxClient\pimaxui\PimaxClient.exe",
            ShortcutSourceLocation: "commonStartMenu",
            TargetProduct: "PimaxClient",
            TargetVersion: "1.43.9.272",
            SignerTrustSummary: "Pimax certificate subject present",
            ShortcutArgumentsState: "none",
            ShortcutWorkingDirectoryState: "expectedPimaxClientProgramDirectory",
            ActivationMethod: "Windows Shell open verb against official Start Menu .lnk",
            DirectExecutableFallbackAllowed: false,
            RuntimeComponentFallbackAllowed: false,
            ServiceMutationAllowed: false,
            RetryAllowed: false,
            ElevationRequired: false,
            CurrentSoftwareGroupState: PimaxSoftwareGroupState.Partial,
            CurrentComponentHealthState: PimaxHealthOverallStatus.SoftwareStackPartial,
            PreconditionResult: "readyForControlledValidation",
            BackendExecutable: false,
            ReadinessForControlledValidation: true,
            Warnings: [],
            Errors: [],
            HumanReadableSummary: "ready",
            ActivationPrecondition: precondition);
    }

    private sealed class FakeCollectorProbe(PimaxShellActivationEvidenceCollectorProbeResult result) : IPimaxShellActivationEvidenceCollectorProbe
    {
        public Task<PimaxShellActivationEvidenceCollectorProbeResult> CollectAsync(int sampleCount, TimeSpan sampleInterval, CancellationToken cancellationToken)
            => Task.FromResult(result);
    }

    private sealed class FakeEvidenceContext(
        bool elevated,
        bool explorerMatched = true,
        bool interactive = true,
        bool localSystem = false,
        bool sessionZero = false) : IPimaxShellActivationEvidenceContext
    {
        public bool IsWindows => true;
        public bool IsElevated => elevated;
        public bool IsInteractive => interactive;
        public bool IsLocalSystem => localSystem;
        public bool IsSessionZero => sessionZero;
        public int CurrentSessionId => sessionZero ? 0 : 1;
        public bool ExplorerSessionMatched => explorerMatched;
        public string ActiveUserSid => "S-1-5-21-1000";
        public string Summary => "synthetic";
    }

    private sealed class FakeEvidenceStore : IPimaxShellActivationEvidenceStore
    {
        public string ProtectedDirectory => @"C:\ProgramData\PimaxVrcSupervisor\ValidationEvidence";
        public PimaxShellActivationEvidenceEnvelope? LastEnvelope { get; private set; }
        public PimaxShellActivationEvidenceEnvelope? EnvelopeToRead { get; init; }
        public int DeleteCount { get; private set; }

        public string EvidencePath(Guid correlationId)
            => Path.Combine(ProtectedDirectory, $"pimax-shell-evidence-{correlationId}.json");

        public PimaxShellActivationEvidenceStoreValidation ValidateDirectoryForCollect(string activeUserSid)
            => new(true, "administrators", "protectedNoStandardUserCreate", ProtectedDirectory, [], []);

        public PimaxShellActivationEvidenceStoreValidation ValidateEvidenceFileForAssess(string evidenceFile, string activeUserSid)
            => new(true, "administrators", "readDeleteOnly", evidenceFile, [], []);

        public string WriteEnvelopeAtomically(PimaxShellActivationEvidenceEnvelope envelope, string activeUserSid)
        {
            LastEnvelope = envelope;
            return EvidencePath(Guid.Parse(envelope.CorrelationId));
        }

        public PimaxShellActivationEvidenceEnvelope ReadEnvelope(string evidenceFile)
            => EnvelopeToRead ?? LastEnvelope ?? throw new InvalidOperationException("No envelope");

        public bool DeleteConsumedEvidence(string evidenceFile)
        {
            DeleteCount++;
            return DeleteCount == 1;
        }
    }
}
