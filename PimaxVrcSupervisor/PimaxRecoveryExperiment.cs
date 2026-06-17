using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class PimaxRecoveryExperimentSchema
{
    public const string Version = "pimax-recovery-experiment-v1";
}

internal static class PimaxRecoveryExperimentKind
{
    public const string WaitControl = "wait-control";
    public const string RestartPlayClient = "restart-play-client";
}

internal static class PimaxRecoveryFailureCategory
{
    public const string None = "none";
    public const string SafetyGuardRejected = "safetyGuardRejected";
    public const string AssessmentInconclusive = "assessmentInconclusive";
    public const string TargetNotFound = "targetNotFound";
    public const string TargetAmbiguous = "targetAmbiguous";
    public const string ConfirmationRejected = "confirmationRejected";
    public const string GracefulCloseTimeout = "gracefulCloseTimeout";
    public const string ForcedStopFailed = "forcedStopFailed";
    public const string RelaunchFailed = "relaunchFailed";
    public const string ClientStartedButRegistrationUnchanged = "clientStartedButRegistrationUnchanged";
    public const string AssessmentFailed = "assessmentFailed";
    public const string Cancelled = "cancelled";
    public const string OverallTimeout = "overallTimeout";
    public const string UnexpectedProcessState = "unexpectedProcessState";
}

internal static class PimaxRecoveryExperimentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record PimaxRecoveryExperimentRequest(
    string Experiment,
    bool Confirm,
    string? ConfirmationToken,
    int DurationSeconds,
    string? EvidenceDirectory);

internal sealed record PimaxRecoveryExperimentResult(
    string SchemaVersion,
    string ExperimentId,
    string ExperimentKind,
    bool DryRun,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationMs,
    PimaxRegistrationAssessmentSnapshot? InitialAssessment,
    PimaxRecoverySafetyResult Safety,
    PimaxRecoveryConfirmationResult Confirmation,
    PimaxRecoveryExperimentStage[] Stages,
    PimaxClientTargetDescriptor? Target,
    PimaxClientProcessSnapshot[] ProcessSnapshots,
    PimaxRecoveryOperationResult? GracefulClose,
    PimaxRecoveryOperationResult? ForcedStop,
    PimaxRecoveryOperationResult? Relaunch,
    PimaxRecoveryAssessmentSample[] AssessmentTimeline,
    PimaxRegistrationAssessmentSnapshot? FinalAssessment,
    bool Success,
    string FailureCategory,
    string[] Warnings,
    string[] Errors,
    bool Cancelled,
    bool ClientRunningAfterExperiment,
    string? EvidencePackagePath);

internal sealed record PimaxRecoverySafetyResult(
    bool Permitted,
    string[] ChecksPassed,
    string[] ChecksFailed,
    string[] Warnings,
    string? ConfirmationToken,
    DateTimeOffset? ConfirmationTokenExpiresAt);

internal sealed record PimaxRecoveryConfirmationResult(
    bool Required,
    bool Provided,
    bool Accepted,
    string? RejectionReason);

internal sealed record PimaxRecoveryExperimentStage(
    string Name,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool Success,
    string? Message);

internal sealed record PimaxRecoveryOperationResult(
    bool Attempted,
    bool Success,
    string Message,
    int[] ProcessIds);

internal sealed record PimaxRecoveryAssessmentSample(
    DateTimeOffset CollectedAt,
    string State,
    string Confidence,
    string Explanation);

internal sealed record PimaxClientProcessSnapshot(
    int ProcessId,
    int? ParentProcessId,
    string ProcessName,
    string? ExecutablePath,
    string Role,
    DateTimeOffset? StartTime,
    bool HasMainWindow,
    string? MainWindowTitle,
    string? CompanyName,
    string? FileDescription,
    string? ProductName,
    string? ProductVersion);

internal sealed record PimaxClientTargetDescriptor(
    string ExecutablePath,
    string WorkingDirectory,
    string Arguments,
    string ProductName,
    string CompanyName,
    string ProductVersion,
    string Sha256,
    int[] TargetProcessIds,
    string RelaunchSource,
    string Role);

internal interface IPimaxRegistrationAssessmentCollector
{
    Task<PimaxRegistrationAssessmentSnapshot> CollectAsync(CancellationToken cancellationToken);
}

internal interface IPimaxRecoveryEnvironment
{
    bool IsSteamVrRunning();
    bool IsSupervisorCleanupInProgress();
}

internal interface IPimaxClientProcessController
{
    Task<PimaxClientTargetDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
    Task<PimaxRecoveryOperationResult> RequestGracefulCloseAsync(PimaxClientTargetDescriptor target, TimeSpan timeout, CancellationToken cancellationToken);
    Task<PimaxRecoveryOperationResult> ForceStopAsync(PimaxClientTargetDescriptor target, TimeSpan timeout, CancellationToken cancellationToken);
    Task<PimaxRecoveryOperationResult> RelaunchAsync(PimaxClientTargetDescriptor target, TimeSpan timeout, CancellationToken cancellationToken);
    Task<PimaxClientProcessSnapshot[]> SnapshotAsync(CancellationToken cancellationToken);
}

internal sealed record PimaxClientTargetDiscoveryResult(
    PimaxClientTargetDescriptor? Target,
    PimaxClientProcessSnapshot[] Processes,
    string[] Warnings,
    string[] Errors,
    string? FailureCategory);

internal sealed class DefaultPimaxRegistrationAssessmentCollector(SupervisorConfig config) : IPimaxRegistrationAssessmentCollector
{
    public Task<PimaxRegistrationAssessmentSnapshot> CollectAsync(CancellationToken cancellationToken)
        => new PimaxRegistrationAssessmentCoordinator().CollectAsync(config, cancellationToken);
}

internal sealed class DefaultPimaxRecoveryEnvironment : IPimaxRecoveryEnvironment
{
    private static readonly string[] SteamVrProcessNames = ["vrserver", "vrmonitor", "vrcompositor"];

    public bool IsSteamVrRunning()
        => SteamVrProcessNames.Any(name => Process.GetProcessesByName(name).Any(process =>
        {
            using (process)
            {
                return !process.HasExited;
            }
        }));

    public bool IsSupervisorCleanupInProgress()
        => false;
}

internal sealed class PimaxRecoveryExperimentRunner
{
    internal static readonly TimeSpan ConfirmationTokenLifetime = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan ForcedExitTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan RelaunchDetectionTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan RestartObservationTimeout = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan AssessmentInterval = TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<string, byte> ActiveExperiments = new(StringComparer.Ordinal);
    private readonly IPimaxRegistrationAssessmentCollector _assessmentCollector;
    private readonly IPimaxClientProcessController _clientController;
    private readonly IPimaxRecoveryEnvironment _environment;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<string> _newExperimentId;

    public PimaxRecoveryExperimentRunner(
        IPimaxRegistrationAssessmentCollector assessmentCollector,
        IPimaxClientProcessController clientController,
        IPimaxRecoveryEnvironment environment,
        Func<DateTimeOffset>? now = null,
        Func<string>? newExperimentId = null)
    {
        _assessmentCollector = assessmentCollector;
        _clientController = clientController;
        _environment = environment;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _newExperimentId = newExperimentId ?? (() => $"pimax-recovery-{Guid.NewGuid():N}");
    }

    public async Task<PimaxRecoveryExperimentResult> RunAsync(
        PimaxRecoveryExperimentRequest request,
        CancellationToken cancellationToken)
    {
        var experimentId = _newExperimentId();
        if (!ActiveExperiments.TryAdd("global", 0))
        {
            var startedAt = _now();
            return EmptyResult(
                experimentId,
                request,
                startedAt,
                startedAt,
                new PimaxRecoverySafetyResult(false, [], ["Another recovery experiment is already active."], [], null, null),
                PimaxRecoveryFailureCategory.SafetyGuardRejected);
        }

        var started = _now();
        var stages = new List<PimaxRecoveryExperimentStage>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var samples = new List<PimaxRecoveryAssessmentSample>();
        var processSnapshots = Array.Empty<PimaxClientProcessSnapshot>();
        PimaxRegistrationAssessmentSnapshot? initial = null;
        PimaxRegistrationAssessmentSnapshot? final = null;
        PimaxClientTargetDiscoveryResult? discovery = null;
        PimaxRecoveryOperationResult? graceful = null;
        PimaxRecoveryOperationResult? forced = null;
        PimaxRecoveryOperationResult? relaunch = null;

        try
        {
            if (!IsSupportedExperiment(request.Experiment))
            {
                var ended = _now();
                return EmptyResult(
                    experimentId,
                    request,
                    started,
                    ended,
                    new PimaxRecoverySafetyResult(false, [], [$"Unsupported experiment: {request.Experiment}"], [], null, null),
                    PimaxRecoveryFailureCategory.SafetyGuardRejected);
            }

            initial = await StageAsync(
                "initialAssessment",
                stages,
                async () => await _assessmentCollector.CollectAsync(cancellationToken));
            samples.Add(Sample(initial));

            if (string.Equals(request.Experiment, PimaxRecoveryExperimentKind.WaitControl, StringComparison.OrdinalIgnoreCase))
            {
                final = await RunWaitControlAsync(request, stages, samples, cancellationToken);
                var waitEnded = _now();
                return BuildResult(
                    experimentId,
                    request,
                    dryRun: false,
                    started,
                    waitEnded,
                    initial,
                    new PimaxRecoverySafetyResult(true, ["Wait-control performs no mutation."], [], [], null, null),
                    new PimaxRecoveryConfirmationResult(false, false, true, null),
                    stages,
                    null,
                    [],
                    null,
                    null,
                    null,
                    samples,
                    final,
                    string.Equals(final.Assessment.State, PimaxRegistrationState.RegisteredReady, StringComparison.OrdinalIgnoreCase),
                    string.Equals(final.Assessment.State, PimaxRegistrationState.RegisteredReady, StringComparison.OrdinalIgnoreCase)
                        ? PimaxRecoveryFailureCategory.None
                        : PimaxRecoveryFailureCategory.ClientStartedButRegistrationUnchanged,
                    warnings,
                    errors,
                    cancelled: false,
                    clientRunningAfterExperiment: false,
                    request.EvidenceDirectory);
            }

            discovery = await StageAsync(
                "discoverPimaxClientTarget",
                stages,
                async () => await _clientController.DiscoverAsync(cancellationToken));
            processSnapshots = discovery.Processes;
            warnings.AddRange(discovery.Warnings);
            errors.AddRange(discovery.Errors);

            var safety = EvaluateSafety(initial, discovery, request.Experiment);
            if (safety.Permitted && !request.Confirm)
            {
                safety = safety with
                {
                    ConfirmationToken = PimaxRecoveryConfirmationToken.Create(
                        request.Experiment,
                        discovery.Target!,
                        initial.Assessment.State,
                        _now().Add(ConfirmationTokenLifetime),
                        _now),
                    ConfirmationTokenExpiresAt = _now().Add(ConfirmationTokenLifetime)
                };
            }

            if (!safety.Permitted)
            {
                var rejectedAt = _now();
                return BuildResult(
                    experimentId,
                    request,
                    dryRun: true,
                    started,
                    rejectedAt,
                    initial,
                    safety,
                    new PimaxRecoveryConfirmationResult(true, request.Confirm, false, "Safety guard rejected execution."),
                    stages,
                    discovery.Target,
                    processSnapshots,
                    null,
                    null,
                    null,
                    samples,
                    initial,
                    false,
                    discovery.Target is null && !string.IsNullOrWhiteSpace(discovery.FailureCategory)
                        ? discovery.FailureCategory
                        : PimaxRecoveryFailureCategory.SafetyGuardRejected,
                    warnings,
                    errors,
                    cancelled: false,
                    clientRunningAfterExperiment: discovery.Target is not null,
                    request.EvidenceDirectory);
            }

            if (!request.Confirm)
            {
                var dryRunEnded = _now();
                return BuildResult(
                    experimentId,
                    request,
                    dryRun: true,
                    started,
                    dryRunEnded,
                    initial,
                    safety,
                    new PimaxRecoveryConfirmationResult(true, false, false, "Dry run only. Re-run with --confirm and --confirmation-token."),
                    stages,
                    discovery.Target,
                    processSnapshots,
                    null,
                    null,
                    null,
                    samples,
                    initial,
                    false,
                    PimaxRecoveryFailureCategory.None,
                    warnings,
                    errors,
                    cancelled: false,
                    clientRunningAfterExperiment: true,
                    request.EvidenceDirectory);
            }

            var confirmation = PimaxRecoveryConfirmationToken.Validate(
                request.ConfirmationToken,
                request.Experiment,
                discovery.Target!,
                initial.Assessment.State,
                _now);
            if (!confirmation.Accepted)
            {
                var rejectedAt = _now();
                return BuildResult(
                    experimentId,
                    request,
                    dryRun: false,
                    started,
                    rejectedAt,
                    initial,
                    safety,
                    confirmation,
                    stages,
                    discovery.Target,
                    processSnapshots,
                    null,
                    null,
                    null,
                    samples,
                    initial,
                    false,
                    PimaxRecoveryFailureCategory.ConfirmationRejected,
                    warnings,
                    errors,
                    cancelled: false,
                    clientRunningAfterExperiment: true,
                    request.EvidenceDirectory);
            }

            graceful = await StageAsync(
                "requestGracefulClientClose",
                stages,
                async () => await _clientController.RequestGracefulCloseAsync(discovery.Target!, GracefulCloseTimeout, cancellationToken));

            if (!graceful.Success)
            {
                forced = await StageAsync(
                    "forceStopVerifiedClientPid",
                    stages,
                    async () => await _clientController.ForceStopAsync(discovery.Target!, ForcedExitTimeout, cancellationToken));
                if (!forced.Success)
                {
                    final = await SafeCollectFinalAsync(samples, warnings, errors, cancellationToken);
                    var failedAt = _now();
                    return BuildResult(
                        experimentId,
                        request,
                        dryRun: false,
                        started,
                        failedAt,
                        initial,
                        safety,
                        confirmation,
                        stages,
                        discovery.Target,
                        processSnapshots,
                        graceful,
                        forced,
                        null,
                        samples,
                        final,
                        false,
                        PimaxRecoveryFailureCategory.ForcedStopFailed,
                        warnings,
                        errors,
                        cancelled: false,
                        clientRunningAfterExperiment: false,
                        request.EvidenceDirectory);
                }
            }

            relaunch = await StageAsync(
                "relaunchVerifiedClientTarget",
                stages,
                async () => await _clientController.RelaunchAsync(discovery.Target!, RelaunchDetectionTimeout, cancellationToken));
            if (!relaunch.Success)
            {
                final = await SafeCollectFinalAsync(samples, warnings, errors, cancellationToken);
                var failedAt = _now();
                return BuildResult(
                    experimentId,
                    request,
                    dryRun: false,
                    started,
                    failedAt,
                    initial,
                    safety,
                    confirmation,
                    stages,
                    discovery.Target,
                    processSnapshots,
                    graceful,
                    forced,
                    relaunch,
                    samples,
                    final,
                    false,
                    PimaxRecoveryFailureCategory.RelaunchFailed,
                    warnings,
                    errors,
                    cancelled: false,
                    clientRunningAfterExperiment: false,
                    request.EvidenceDirectory);
            }

            final = await ObserveRegistrationAsync(stages, samples, cancellationToken);
            var endedAt = _now();
            var success = string.Equals(final.Assessment.State, PimaxRegistrationState.RegisteredReady, StringComparison.OrdinalIgnoreCase);
            return BuildResult(
                experimentId,
                request,
                dryRun: false,
                started,
                endedAt,
                initial,
                safety,
                confirmation,
                stages,
                discovery.Target,
                processSnapshots,
                graceful,
                forced,
                relaunch,
                samples,
                final,
                success,
                success ? PimaxRecoveryFailureCategory.None : PimaxRecoveryFailureCategory.ClientStartedButRegistrationUnchanged,
                warnings,
                errors,
                cancelled: false,
                clientRunningAfterExperiment: true,
                request.EvidenceDirectory);
        }
        catch (OperationCanceledException)
        {
            var cancelledAt = _now();
            return BuildResult(
                experimentId,
                request,
                dryRun: !request.Confirm,
                started,
                cancelledAt,
                initial,
                new PimaxRecoverySafetyResult(false, [], ["Cancellation was requested."], [], null, null),
                new PimaxRecoveryConfirmationResult(string.Equals(request.Experiment, PimaxRecoveryExperimentKind.RestartPlayClient, StringComparison.OrdinalIgnoreCase), request.Confirm, false, "Canceled."),
                stages,
                discovery?.Target,
                processSnapshots,
                graceful,
                forced,
                relaunch,
                samples,
                final,
                false,
                PimaxRecoveryFailureCategory.Cancelled,
                warnings,
                errors,
                cancelled: true,
                clientRunningAfterExperiment: false,
                request.EvidenceDirectory);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            var failedAt = _now();
            return BuildResult(
                experimentId,
                request,
                dryRun: !request.Confirm,
                started,
                failedAt,
                initial,
                new PimaxRecoverySafetyResult(false, [], ["Experiment failed unexpectedly."], [], null, null),
                new PimaxRecoveryConfirmationResult(string.Equals(request.Experiment, PimaxRecoveryExperimentKind.RestartPlayClient, StringComparison.OrdinalIgnoreCase), request.Confirm, false, "Unexpected failure."),
                stages,
                discovery?.Target,
                processSnapshots,
                graceful,
                forced,
                relaunch,
                samples,
                final,
                false,
                PimaxRecoveryFailureCategory.UnexpectedProcessState,
                warnings,
                errors,
                cancelled: false,
                clientRunningAfterExperiment: false,
                request.EvidenceDirectory);
        }
        finally
        {
            ActiveExperiments.TryRemove("global", out _);
        }
    }

    private static bool IsSupportedExperiment(string experiment)
        => string.Equals(experiment, PimaxRecoveryExperimentKind.WaitControl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(experiment, PimaxRecoveryExperimentKind.RestartPlayClient, StringComparison.OrdinalIgnoreCase);

    private async Task<PimaxRegistrationAssessmentSnapshot> RunWaitControlAsync(
        PimaxRecoveryExperimentRequest request,
        List<PimaxRecoveryExperimentStage> stages,
        List<PimaxRecoveryAssessmentSample> samples,
        CancellationToken cancellationToken)
    {
        var duration = TimeSpan.FromSeconds(Math.Clamp(request.DurationSeconds, 1, 300));
        var deadline = _now().Add(duration);
        PimaxRegistrationAssessmentSnapshot final = samples.Count > 0
            ? await _assessmentCollector.CollectAsync(cancellationToken)
            : await _assessmentCollector.CollectAsync(cancellationToken);

        while (_now() < deadline)
        {
            await Task.Delay(AssessmentInterval, cancellationToken);
            final = await StageAsync(
                "waitControlAssessment",
                stages,
                async () => await _assessmentCollector.CollectAsync(cancellationToken));
            samples.Add(Sample(final));
            if (string.Equals(final.Assessment.State, PimaxRegistrationState.RegisteredReady, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return final;
    }

    private async Task<PimaxRegistrationAssessmentSnapshot> ObserveRegistrationAsync(
        List<PimaxRecoveryExperimentStage> stages,
        List<PimaxRecoveryAssessmentSample> samples,
        CancellationToken cancellationToken)
    {
        var deadline = _now().Add(RestartObservationTimeout);
        PimaxRegistrationAssessmentSnapshot? final = null;
        while (_now() < deadline)
        {
            await Task.Delay(AssessmentInterval, cancellationToken);
            final = await StageAsync(
                "postRestartAssessment",
                stages,
                async () => await _assessmentCollector.CollectAsync(cancellationToken));
            samples.Add(Sample(final));
            if (string.Equals(final.Assessment.State, PimaxRegistrationState.RegisteredReady, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return final ?? await _assessmentCollector.CollectAsync(cancellationToken);
    }

    private async Task<PimaxRegistrationAssessmentSnapshot> SafeCollectFinalAsync(
        List<PimaxRecoveryAssessmentSample> samples,
        List<string> warnings,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var final = await _assessmentCollector.CollectAsync(cancellationToken);
            samples.Add(Sample(final));
            return final;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"Final assessment failed: {ex.Message}");
            throw;
        }
    }

    private PimaxRecoverySafetyResult EvaluateSafety(
        PimaxRegistrationAssessmentSnapshot initial,
        PimaxClientTargetDiscoveryResult discovery,
        string experiment)
    {
        var passed = new List<string>();
        var failed = new List<string>();
        var warnings = new List<string>();

        if (!string.Equals(experiment, PimaxRecoveryExperimentKind.RestartPlayClient, StringComparison.OrdinalIgnoreCase))
        {
            failed.Add("Only restart-play-client uses mutation safety checks.");
        }

        if (string.Equals(initial.Assessment.State, PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration, StringComparison.OrdinalIgnoreCase)
            && string.Equals(initial.Assessment.Confidence, PimaxRegistrationConfidence.Probable, StringComparison.OrdinalIgnoreCase))
        {
            passed.Add("Assessment is likelyPoweredOnAwaitingRegistration with probable confidence.");
        }
        else
        {
            failed.Add($"Current assessment is {initial.Assessment.State}/{initial.Assessment.Confidence}, not likelyPoweredOnAwaitingRegistration/probable.");
        }

        if (initial.Assessment.Conflicts.Length == 0 && !string.Equals(initial.Assessment.State, PimaxRegistrationState.ConflictingEvidence, StringComparison.OrdinalIgnoreCase))
        {
            passed.Add("Assessment evidence is not conflicting.");
        }
        else
        {
            failed.Add("Assessment evidence is conflicting.");
        }

        if (!_environment.IsSteamVrRunning())
        {
            passed.Add("SteamVR is not running.");
        }
        else
        {
            failed.Add("SteamVR is running.");
        }

        if (!_environment.IsSupervisorCleanupInProgress())
        {
            passed.Add("No Supervisor cleanup/shutdown state is visible to this CLI experiment.");
        }
        else
        {
            failed.Add("Supervisor cleanup/shutdown is in progress.");
        }

        if (discovery.Target is not null)
        {
            passed.Add("Exact Pimax Play client target was verified.");
        }
        else
        {
            failed.AddRange(discovery.Errors.Length == 0 ? ["Pimax Play client target was not found."] : discovery.Errors);
        }

        warnings.AddRange(discovery.Warnings);
        return new PimaxRecoverySafetyResult(failed.Count == 0, passed.ToArray(), failed.ToArray(), warnings.ToArray(), null, null);
    }

    private async Task<T> StageAsync<T>(
        string name,
        List<PimaxRecoveryExperimentStage> stages,
        Func<Task<T>> action)
    {
        var started = _now();
        try
        {
            var result = await action();
            stages.Add(new PimaxRecoveryExperimentStage(name, started, _now(), true, null));
            return result;
        }
        catch (Exception ex)
        {
            stages.Add(new PimaxRecoveryExperimentStage(name, started, _now(), false, ex.Message));
            throw;
        }
    }

    private static PimaxRecoveryAssessmentSample Sample(PimaxRegistrationAssessmentSnapshot snapshot)
        => new(snapshot.CollectedAt, snapshot.Assessment.State, snapshot.Assessment.Confidence, snapshot.Assessment.Explanation);

    private static PimaxRecoveryExperimentResult EmptyResult(
        string experimentId,
        PimaxRecoveryExperimentRequest request,
        DateTimeOffset started,
        DateTimeOffset ended,
        PimaxRecoverySafetyResult safety,
        string failureCategory)
        => BuildResult(
            experimentId,
            request,
            dryRun: !request.Confirm,
            started,
            ended,
            null,
            safety,
            new PimaxRecoveryConfirmationResult(false, request.Confirm, false, safety.ChecksFailed.FirstOrDefault()),
            [],
            null,
            [],
            null,
            null,
            null,
            [],
            null,
            false,
            failureCategory,
            [],
            safety.ChecksFailed,
            false,
            false,
            request.EvidenceDirectory);

    private static PimaxRecoveryExperimentResult BuildResult(
        string experimentId,
        PimaxRecoveryExperimentRequest request,
        bool dryRun,
        DateTimeOffset started,
        DateTimeOffset ended,
        PimaxRegistrationAssessmentSnapshot? initial,
        PimaxRecoverySafetyResult safety,
        PimaxRecoveryConfirmationResult confirmation,
        IEnumerable<PimaxRecoveryExperimentStage> stages,
        PimaxClientTargetDescriptor? target,
        IEnumerable<PimaxClientProcessSnapshot> processSnapshots,
        PimaxRecoveryOperationResult? graceful,
        PimaxRecoveryOperationResult? forced,
        PimaxRecoveryOperationResult? relaunch,
        IEnumerable<PimaxRecoveryAssessmentSample> timeline,
        PimaxRegistrationAssessmentSnapshot? final,
        bool success,
        string failureCategory,
        IEnumerable<string> warnings,
        IEnumerable<string> errors,
        bool cancelled,
        bool clientRunningAfterExperiment,
        string? evidencePackagePath)
        => new(
            PimaxRecoveryExperimentSchema.Version,
            experimentId,
            request.Experiment,
            dryRun,
            started,
            ended,
            (ended - started).TotalMilliseconds,
            initial,
            safety,
            confirmation,
            stages.ToArray(),
            target,
            processSnapshots.ToArray(),
            graceful,
            forced,
            relaunch,
            timeline.ToArray(),
            final,
            success,
            failureCategory,
            warnings.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            cancelled,
            clientRunningAfterExperiment,
            evidencePackagePath);
}

internal static class PimaxRecoveryConfirmationToken
{
    private static readonly ConcurrentDictionary<string, byte> UsedTokens = new(StringComparer.Ordinal);

    public static string Create(
        string experiment,
        PimaxClientTargetDescriptor target,
        string state,
        DateTimeOffset expiresAt,
        Func<DateTimeOffset> now)
    {
        var payload = new PimaxRecoveryConfirmationTokenPayload(
            experiment,
            target.Sha256,
            target.ExecutablePath,
            state,
            expiresAt,
            Guid.NewGuid().ToString("N"));
        var json = JsonSerializer.Serialize(payload, PimaxRecoveryExperimentJson.Options);
        var payloadText = Base64Url(Encoding.UTF8.GetBytes(json));
        var signature = Base64Url(Sign(payloadText));
        return $"{payloadText}.{signature}";
    }

    public static PimaxRecoveryConfirmationResult Validate(
        string? token,
        string experiment,
        PimaxClientTargetDescriptor target,
        string state,
        Func<DateTimeOffset> now)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PimaxRecoveryConfirmationResult(true, false, false, "Missing confirmation token.");
        }

        if (!UsedTokens.TryAdd(token, 0))
        {
            return new PimaxRecoveryConfirmationResult(true, true, false, "Confirmation token was already used in this process.");
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return new PimaxRecoveryConfirmationResult(true, true, false, "Confirmation token format is invalid.");
        }

        var expected = Base64Url(Sign(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[1])))
        {
            return new PimaxRecoveryConfirmationResult(true, true, false, "Confirmation token signature is invalid.");
        }

        PimaxRecoveryConfirmationTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PimaxRecoveryConfirmationTokenPayload>(
                Encoding.UTF8.GetString(Base64UrlDecode(parts[0])),
                PimaxRecoveryExperimentJson.Options);
        }
        catch (Exception ex)
        {
            return new PimaxRecoveryConfirmationResult(true, true, false, $"Confirmation token payload is invalid: {ex.Message}");
        }

        if (payload is null)
        {
            return new PimaxRecoveryConfirmationResult(true, true, false, "Confirmation token payload is empty.");
        }

        if (payload.ExpiresAt <= now())
        {
            return new PimaxRecoveryConfirmationResult(true, true, false, "Confirmation token expired.");
        }

        if (!string.Equals(payload.Experiment, experiment, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payload.TargetSha256, target.Sha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payload.TargetPath, target.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payload.AssessmentState, state, StringComparison.OrdinalIgnoreCase))
        {
            return new PimaxRecoveryConfirmationResult(true, true, false, "Confirmation token does not match the current target and assessment.");
        }

        return new PimaxRecoveryConfirmationResult(true, true, true, null);
    }

    internal static void ResetForTests()
        => UsedTokens.Clear();

    private static byte[] Sign(string payload)
    {
        var keyMaterial = $"PimaxVrcSupervisor.Phase28C.Token.v1|{Environment.MachineName}|{Environment.UserName}";
        using var hmac = new HMACSHA256(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial)));
        return hmac.ComputeHash(Encoding.ASCII.GetBytes(payload));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record PimaxRecoveryConfirmationTokenPayload(
        string Experiment,
        string TargetSha256,
        string TargetPath,
        string AssessmentState,
        DateTimeOffset ExpiresAt,
        string Nonce);
}
