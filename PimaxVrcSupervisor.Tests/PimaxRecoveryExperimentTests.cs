using System.Text.Json;
using Xunit;

public sealed class PimaxRecoveryExperimentTests
{
    [Fact]
    public async Task WaitControlDoesNotMutate()
    {
        var controller = new FakePimaxClientController();
        var runner = Runner(
            [Assessment(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration), Assessment(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration)],
            controller);

        var result = await runner.RunAsync(new PimaxRecoveryExperimentRequest(PimaxRecoveryExperimentKind.WaitControl, false, null, 1, null), CancellationToken.None);

        Assert.False(result.DryRun);
        Assert.False(result.Success);
        Assert.Equal(0, controller.GracefulCloseCalls);
        Assert.Equal(0, controller.ForceStopCalls);
        Assert.Equal(0, controller.RelaunchCalls);
    }

    [Fact]
    public async Task RestartDryRunGeneratesTokenWithoutMutationWhenSafe()
    {
        var controller = new FakePimaxClientController();
        var runner = Runner([Assessment(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration)], controller);

        var result = await runner.RunAsync(new PimaxRecoveryExperimentRequest(PimaxRecoveryExperimentKind.RestartPlayClient, false, null, 30, null), CancellationToken.None);

        Assert.True(result.DryRun);
        Assert.True(result.Safety.Permitted);
        Assert.False(string.IsNullOrWhiteSpace(result.Safety.ConfirmationToken));
        Assert.Equal(0, controller.GracefulCloseCalls);
        Assert.Equal(0, controller.ForceStopCalls);
        Assert.Equal(0, controller.RelaunchCalls);
    }

    [Theory]
    [InlineData(PimaxRegistrationState.RegisteredReady, PimaxRegistrationConfidence.Confirmed)]
    [InlineData(PimaxRegistrationState.LikelyHeadsetOff, PimaxRegistrationConfidence.Probable)]
    [InlineData(PimaxRegistrationState.Unknown, PimaxRegistrationConfidence.Insufficient)]
    [InlineData(PimaxRegistrationState.ConflictingEvidence, PimaxRegistrationConfidence.Insufficient)]
    public async Task RestartRejectsUnsafeRegistrationStates(string state, string confidence)
    {
        var controller = new FakePimaxClientController();
        var runner = Runner([Assessment(state, confidence, state == PimaxRegistrationState.ConflictingEvidence ? ["conflict"] : [])], controller);

        var result = await runner.RunAsync(new PimaxRecoveryExperimentRequest(PimaxRecoveryExperimentKind.RestartPlayClient, false, null, 30, null), CancellationToken.None);

        Assert.True(result.DryRun);
        Assert.False(result.Safety.Permitted);
        Assert.Equal(PimaxRecoveryFailureCategory.SafetyGuardRejected, result.FailureCategory);
        Assert.Contains(result.Safety.ChecksFailed, check => check.Contains("assessment", StringComparison.OrdinalIgnoreCase) || check.Contains("conflicting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestartRejectsWhenSteamVrIsRunning()
    {
        var controller = new FakePimaxClientController();
        var runner = Runner(
            [Assessment(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration)],
            controller,
            environment: new FakeEnvironment(steamVrRunning: true));

        var result = await runner.RunAsync(new PimaxRecoveryExperimentRequest(PimaxRecoveryExperimentKind.RestartPlayClient, false, null, 30, null), CancellationToken.None);

        Assert.False(result.Safety.Permitted);
        Assert.Contains(result.Safety.ChecksFailed, check => check.Contains("SteamVR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestartRejectsAmbiguousClientTarget()
    {
        var controller = new FakePimaxClientController { Discovery = new PimaxClientTargetDiscoveryResult(null, [], [], ["ambiguous"], PimaxRecoveryFailureCategory.TargetAmbiguous) };
        var runner = Runner([Assessment(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration)], controller);

        var result = await runner.RunAsync(new PimaxRecoveryExperimentRequest(PimaxRecoveryExperimentKind.RestartPlayClient, false, null, 30, null), CancellationToken.None);

        Assert.False(result.Safety.Permitted);
        Assert.Equal(PimaxRecoveryFailureCategory.TargetAmbiguous, result.FailureCategory);
    }

    [Fact]
    public void ConfirmationTokenRejectsWrongTokenAndExpiredToken()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var target = Target();
        var token = PimaxRecoveryConfirmationToken.Create(
            PimaxRecoveryExperimentKind.RestartPlayClient,
            target,
            PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
            DateTimeOffset.UtcNow.AddSeconds(-1),
            () => DateTimeOffset.UtcNow);

        var expired = PimaxRecoveryConfirmationToken.Validate(
            token,
            PimaxRecoveryExperimentKind.RestartPlayClient,
            target,
            PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
            () => DateTimeOffset.UtcNow);
        var wrong = PimaxRecoveryConfirmationToken.Validate(
            "not-a-token",
            PimaxRecoveryExperimentKind.RestartPlayClient,
            target,
            PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
            () => DateTimeOffset.UtcNow);

        Assert.False(expired.Accepted);
        Assert.Contains("expired", expired.RejectionReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(wrong.Accepted);
    }

    [Fact]
    public void ConfirmationTokenRejectsReuse()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var target = Target();
        var now = DateTimeOffset.UtcNow;
        var token = PimaxRecoveryConfirmationToken.Create(
            PimaxRecoveryExperimentKind.RestartPlayClient,
            target,
            PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
            now.AddMinutes(5),
            () => now);

        var first = PimaxRecoveryConfirmationToken.Validate(
            token,
            PimaxRecoveryExperimentKind.RestartPlayClient,
            target,
            PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
            () => now);
        var second = PimaxRecoveryConfirmationToken.Validate(
            token,
            PimaxRecoveryExperimentKind.RestartPlayClient,
            target,
            PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
            () => now);

        Assert.True(first.Accepted);
        Assert.False(second.Accepted);
        Assert.Contains("already used", second.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmedRestartCanRecoverToRegisteredReady()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var target = Target();
        var now = DateTimeOffset.UtcNow;
        var token = PimaxRecoveryConfirmationToken.Create(
            PimaxRecoveryExperimentKind.RestartPlayClient,
            target,
            PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
            now.AddMinutes(5),
            () => now);
        var controller = new FakePimaxClientController { Target = target };
        var runner = Runner(
            [Assessment(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration), Assessment(PimaxRegistrationState.RegisteredReady, PimaxRegistrationConfidence.Confirmed)],
            controller,
            now: () => now);

        var result = await runner.RunAsync(new PimaxRecoveryExperimentRequest(PimaxRecoveryExperimentKind.RestartPlayClient, true, token, 30, null), CancellationToken.None);

        Assert.False(result.DryRun);
        Assert.True(result.Confirmation.Accepted);
        Assert.True(result.Success);
        Assert.Equal(1, controller.GracefulCloseCalls);
        Assert.Equal(0, controller.ForceStopCalls);
        Assert.Equal(1, controller.RelaunchCalls);
        Assert.Equal(PimaxRecoveryFailureCategory.None, result.FailureCategory);
    }

    [Fact]
    public async Task ConfirmedRestartUsesForcedStopOnlyWhenGracefulCloseFails()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var target = Target();
        var now = DateTimeOffset.UtcNow;
        var token = PimaxRecoveryConfirmationToken.Create(
            PimaxRecoveryExperimentKind.RestartPlayClient,
            target,
            PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
            now.AddMinutes(5),
            () => now);
        var controller = new FakePimaxClientController { Target = target, GracefulCloseSucceeds = false };
        var runner = Runner(
            [Assessment(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration), Assessment(PimaxRegistrationState.RegisteredReady, PimaxRegistrationConfidence.Confirmed)],
            controller,
            now: () => now);

        var result = await runner.RunAsync(new PimaxRecoveryExperimentRequest(PimaxRecoveryExperimentKind.RestartPlayClient, true, token, 30, null), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, controller.ForceStopCalls);
        Assert.NotNull(result.ForcedStop);
        Assert.Equal([101], result.ForcedStop.ProcessIds);
    }

    [Fact]
    public async Task RelaunchFailureStopsWithoutRetry()
    {
        PimaxRecoveryConfirmationToken.ResetForTests();
        var target = Target();
        var now = DateTimeOffset.UtcNow;
        var token = PimaxRecoveryConfirmationToken.Create(
            PimaxRecoveryExperimentKind.RestartPlayClient,
            target,
            PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
            now.AddMinutes(5),
            () => now);
        var controller = new FakePimaxClientController { Target = target, RelaunchSucceeds = false };
        var runner = Runner([Assessment(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration)], controller, now: () => now);

        var result = await runner.RunAsync(new PimaxRecoveryExperimentRequest(PimaxRecoveryExperimentKind.RestartPlayClient, true, token, 30, null), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PimaxRecoveryFailureCategory.RelaunchFailed, result.FailureCategory);
        Assert.Equal(1, controller.RelaunchCalls);
    }

    [Fact]
    public void RecoverySchemaSerializesExpectedFields()
    {
        var result = new PimaxRecoveryExperimentResult(
            PimaxRecoveryExperimentSchema.Version,
            "id",
            PimaxRecoveryExperimentKind.WaitControl,
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0,
            null,
            new PimaxRecoverySafetyResult(false, [], [], [], null, null),
            new PimaxRecoveryConfirmationResult(false, false, false, null),
            [],
            null,
            [],
            null,
            null,
            null,
            [],
            null,
            false,
            PimaxRecoveryFailureCategory.None,
            [],
            [],
            false,
            false,
            null);

        var json = JsonSerializer.Serialize(result, PimaxRecoveryExperimentJson.Options);

        Assert.Contains("\"schemaVersion\":\"pimax-recovery-experiment-v1\"", json);
        Assert.Contains("\"experimentKind\"", json);
        Assert.Contains("\"safety\"", json);
    }

    [Fact]
    public void ExistingDiagnosticSchemasRemainStable()
    {
        Assert.Equal("pimax-connectivity-v1", PimaxConnectivitySchema.Version);
        Assert.Equal("pimax-usb-enumeration-v1", PimaxUsbEnumerationSchema.Version);
        Assert.Equal("pimax-registration-assessment-v1", PimaxRegistrationAssessmentSchema.Version);
    }

    [Fact]
    public void RecoveryExperimentSourceDoesNotReferenceServiceOrDeviceMutationApis()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceFiles = new[]
        {
            Path.Combine(repositoryRoot, "PimaxVrcSupervisor", "PimaxRecoveryExperiment.cs"),
            Path.Combine(repositoryRoot, "PimaxVrcSupervisor", "WindowsPimaxClientProcessController.cs"),
        };
        var forbiddenTokens = new[]
        {
            "ServiceController",
            "SetupDi",
            "CM_",
            "pnputil",
            "devcon",
            "Disable-PnpDevice",
            "Enable-PnpDevice",
            "Restart-Service",
            "Stop-Service",
            "Start-Service",
        };

        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            foreach (var token in forbiddenTokens)
            {
                Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static PimaxRecoveryExperimentRunner Runner(
        IEnumerable<PimaxRegistrationAssessmentSnapshot> assessments,
        FakePimaxClientController controller,
        FakeEnvironment? environment = null,
        Func<DateTimeOffset>? now = null)
        => new(new FakeAssessmentCollector(assessments), controller, environment ?? new FakeEnvironment(), now);

    private static PimaxRegistrationAssessmentSnapshot Assessment(
        string state,
        string confidence = PimaxRegistrationConfidence.Probable,
        string[]? conflicts = null)
    {
        var result = new PimaxRegistrationAssessmentResult(
            state,
            confidence,
            "synthetic",
            [],
            [],
            [],
            conflicts ?? [],
            [],
            new PimaxRegistrationEvidence(
                state is PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration or PimaxRegistrationState.RegisteredReady,
                state is PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration or PimaxRegistrationState.RegisteredReady ? 3 : 0,
                state is PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration or PimaxRegistrationState.RegisteredReady ? 3 : 0,
                state is PimaxRegistrationState.RegisteredReady,
                state is PimaxRegistrationState.RegisteredReady ? 4 : 0,
                state is PimaxRegistrationState.RegisteredReady ? 4 : 0,
                state is PimaxRegistrationState.RegisteredReady,
                state is PimaxRegistrationState.RegisteredReady,
                state is PimaxRegistrationState.LikelyHeadsetOff,
                false,
                true,
                [],
                []));
        return new PimaxRegistrationAssessmentSnapshot(
            PimaxRegistrationAssessmentSchema.Version,
            DateTimeOffset.UtcNow,
            result,
            new PimaxRegistrationSourceSchemaVersions(PimaxConnectivitySchema.Version, PimaxUsbEnumerationSchema.Version),
            new PimaxRegistrationSnapshotMetadata(PimaxConnectivitySchema.Version, DateTimeOffset.UtcNow, 1, "synthetic", confidence, 0, 0),
            new PimaxRegistrationSnapshotMetadata(PimaxUsbEnumerationSchema.Version, DateTimeOffset.UtcNow, null, "synthetic", confidence, 0, 0),
            10,
            [],
            []);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PimaxVrcSupervisor", "PimaxVrcSupervisor.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root from test output directory.");
    }

    private static PimaxClientTargetDescriptor Target(string path = @"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe")
        => new(path, Path.GetDirectoryName(path)!, "", "PimaxClient", "Pimax", "1.43.9.272", "ABC123", [101], "shortcut", "PimaxPlayUiClient");

    private sealed class FakeAssessmentCollector : IPimaxRegistrationAssessmentCollector
    {
        private readonly Queue<PimaxRegistrationAssessmentSnapshot> _assessments;

        public FakeAssessmentCollector(IEnumerable<PimaxRegistrationAssessmentSnapshot> assessments)
            => _assessments = new Queue<PimaxRegistrationAssessmentSnapshot>(assessments);

        public Task<PimaxRegistrationAssessmentSnapshot> CollectAsync(CancellationToken cancellationToken)
            => Task.FromResult(_assessments.Count > 1 ? _assessments.Dequeue() : _assessments.Peek());
    }

    private sealed class FakeEnvironment(bool steamVrRunning = false, bool cleanupInProgress = false) : IPimaxRecoveryEnvironment
    {
        public bool IsSteamVrRunning() => steamVrRunning;
        public bool IsSupervisorCleanupInProgress() => cleanupInProgress;
    }

    private sealed class FakePimaxClientController : IPimaxClientProcessController
    {
        public PimaxClientTargetDescriptor Target { get; init; } = PimaxRecoveryExperimentTests.Target();
        public PimaxClientTargetDiscoveryResult? Discovery { get; init; }
        public bool GracefulCloseSucceeds { get; init; } = true;
        public bool ForceStopSucceeds { get; init; } = true;
        public bool RelaunchSucceeds { get; init; } = true;
        public int GracefulCloseCalls { get; private set; }
        public int ForceStopCalls { get; private set; }
        public int RelaunchCalls { get; private set; }

        public Task<PimaxClientTargetDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
            => Task.FromResult(Discovery ?? new PimaxClientTargetDiscoveryResult(Target, [], [], [], null));

        public Task<PimaxRecoveryOperationResult> RequestGracefulCloseAsync(PimaxClientTargetDescriptor target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            GracefulCloseCalls++;
            return Task.FromResult(new PimaxRecoveryOperationResult(true, GracefulCloseSucceeds, GracefulCloseSucceeds ? "closed" : "timeout", target.TargetProcessIds));
        }

        public Task<PimaxRecoveryOperationResult> ForceStopAsync(PimaxClientTargetDescriptor target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ForceStopCalls++;
            return Task.FromResult(new PimaxRecoveryOperationResult(true, ForceStopSucceeds, ForceStopSucceeds ? "stopped" : "failed", target.TargetProcessIds));
        }

        public Task<PimaxRecoveryOperationResult> RelaunchAsync(PimaxClientTargetDescriptor target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            RelaunchCalls++;
            return Task.FromResult(new PimaxRecoveryOperationResult(true, RelaunchSucceeds, RelaunchSucceeds ? "started" : "failed", RelaunchSucceeds ? target.TargetProcessIds : []));
        }

        public Task<PimaxClientProcessSnapshot[]> SnapshotAsync(CancellationToken cancellationToken)
            => Task.FromResult(Array.Empty<PimaxClientProcessSnapshot>());
    }
}
