namespace PimaxVrcSupervisor.Updates;

internal sealed class StandaloneUpdateCheckWorker
{
    private const string OperationName = "update-check-once";
    private readonly UpdateStateStore _store;
    private readonly UpdateDiscoveryScheduler _scheduler;
    private readonly IUpdateScheduleClock _clock;
    private readonly bool _verificationConfigured;

    public StandaloneUpdateCheckWorker(
        UpdateStateStore store,
        UpdateDiscoveryScheduler scheduler,
        IUpdateScheduleClock clock,
        bool verificationConfigured)
    {
        _store = store;
        _scheduler = scheduler;
        _clock = clock;
        _verificationConfigured = verificationConfigured;
    }

    public async Task<StandaloneUpdateCheckResultV1> RunAsync(CancellationToken cancellationToken)
    {
        var admission = _scheduler.TryAcquireAdmission(UpdateCheckKind.Worker);
        if (!admission.IsAdmitted)
        {
            var code = admission.ErrorCode ?? "gate_unavailable";
            return StandaloneUpdateCheckCommand.Failed(
                code,
                code == "already_running"
                    ? "An update check is already running."
                    : "The secure update-check gate is unavailable.");
        }

        UpdateDiscoveryCheckResult result;
        if (!_verificationConfigured)
        {
            using (admission.Lease!)
            {
                result = await PersistVerificationUnavailableAsync().ConfigureAwait(false);
            }
        }
        else
        {
            result = await _scheduler.CheckAdmittedAsync(
                UpdateCheckKind.Worker,
                admission.Lease!,
                cancellationToken).ConfigureAwait(false);
        }

        var completedAt = _clock.UtcNow;
        var summary = SupervisorUpdateCoordinator.DescribeResult(result);
        var resultCode = ResultCode(result);
        var status = CreateStatus(result.CompletedSuccessfully, resultCode, summary, completedAt);
        return new StandaloneUpdateCheckResultV1(
            SchemaVersion: 1,
            Operation: OperationName,
            ResultCode: resultCode,
            Success: result.CompletedSuccessfully,
            LatestVerifiedVersion: status.LatestVerifiedVersion,
            UpdateAvailable: status.UpdateAvailable,
            Summary: Bound(summary, 256),
            Status: status);
    }

    private async Task<UpdateDiscoveryCheckResult> PersistVerificationUnavailableAsync()
    {
        try
        {
            var state = _store.Load().State;
            await _store.SaveAsync(
                state with
                {
                    LastAttemptUtc = _clock.UtcNow,
                    LastError = new UpdateBoundedErrorV1
                    {
                        Category = UpdateErrorCategory.Signature,
                        Code = "verification_unavailable",
                        AtUtc = _clock.UtcNow
                    }
                },
                CancellationToken.None).ConfigureAwait(false);
            return UpdateDiscoveryCheckResult.Failure(
                UpdateErrorCategory.Signature,
                "verification_unavailable");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UpdateContractException)
        {
            return UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.State, "state_write");
        }
    }

    public static async Task<StandaloneUpdateCheckResultV1> RunProductionAsync(
        CancellationToken cancellationToken,
        string runtimeConfigFileName = "PimaxVrcSupervisor.runtimeconfig.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeConfigFileName);
        if (!string.Equals(runtimeConfigFileName, Path.GetFileName(runtimeConfigFileName), StringComparison.Ordinal))
        {
            throw new ArgumentException("The runtime configuration file name must not include a path.", nameof(runtimeConfigFileName));
        }

        var runtimeConfigPath = Path.Combine(AppContext.BaseDirectory, runtimeConfigFileName);
        var variant = InstalledPackageVariantDetector.Detect(File.ReadAllBytes(runtimeConfigPath));
        var store = new UpdateStateStore(variant);
        var clock = new SystemUpdateScheduleClock();
        var options = new UpdateDiscoveryOptions
        {
            InstalledVersion = SemanticVersion.Parse(AppVersion.Current),
            InstalledVariant = variant
        };
        using var client = GitHubUpdateDiscoveryClient.CreateProduction(options);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock);
        var worker = new StandaloneUpdateCheckWorker(store, scheduler, clock, verificationConfigured: true);
        return await worker.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private UpdateStatusSnapshotV1 CreateStatus(
        bool operationSuccess,
        string resultCode,
        string summary,
        DateTimeOffset completedAt)
    {
        var load = _store.Load();
        var state = load.State;
        var current = SemanticVersion.Parse(AppVersion.Current);
        var latest = TryParseStable(state.LatestVerifiedVersion);
        var available = latest is not null && latest.CompareTo(current) > 0;
        var dismissed = available && string.Equals(state.DismissedVersion, state.LatestVerifiedVersion, StringComparison.Ordinal);
        var eligibility = UpdateDiscoveryScheduler.EvaluateAutomaticEligibility(state, _clock.UtcNow);
        return new UpdateStatusSnapshotV1(
            SchemaVersion: 1,
            Policy: state.Policy == UpdateCheckPolicy.NotifyStable ? "Notify" : "Disabled",
            Channel: "Stable",
            CurrentVersion: current.ToString(),
            LatestVerifiedVersion: state.LatestVerifiedVersion,
            UpdateAvailable: available,
            Dismissed: dismissed,
            DismissedVersion: state.DismissedVersion,
            LastAttemptAt: state.LastAttemptUtc,
            LastSuccessfulCheckAt: state.LastSuccessfulCheckUtc,
            LastErrorCode: load.CorruptionDetected ? "state_corrupt" : state.LastError?.Code,
            LastErrorSummary: load.CorruptionDetected
                ? "Saved update state was corrupt and was ignored."
                : SupervisorUpdateCoordinator.DescribeError(state.LastError?.Code),
            VerificationConfigured: _verificationConfigured,
            AutomaticCheckDue: _verificationConfigured && eligibility.Decision == AutomaticUpdateCheckDecision.Due,
            CheckInProgress: false,
            Operation: new UpdateCheckOperationSnapshot(
                OperationId: OperationName,
                Status: operationSuccess ? "completed" : "failed",
                Success: operationSuccess,
                ResultCode: Bound(resultCode, 64),
                ResultSummary: Bound(summary, 256),
                StartedAt: state.LastAttemptUtc ?? completedAt,
                CompletedAt: completedAt));
    }

    private static string ResultCode(UpdateDiscoveryCheckResult result)
        => result.ErrorCode ?? result.Status switch
        {
            UpdateDiscoveryStatus.UpdateAvailable => "update_available",
            UpdateDiscoveryStatus.Current => "current",
            UpdateDiscoveryStatus.NotModified => "not_modified",
            UpdateDiscoveryStatus.Ignored => "ignored",
            UpdateDiscoveryStatus.Cancelled => "cancelled",
            _ => "update_failed"
        };

    private static SemanticVersion? TryParseStable(string? value)
        => SemanticVersion.TryParse(value, out var version) && version.IsStable ? version : null;

    private static string Bound(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum];
}

internal static class StandaloneUpdateCheckCommand
{
    private static readonly string[] AllowedArguments = ["--update-check-once", "--source", "configurator"];

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(argument => string.Equals(argument, "--update-check-once", StringComparison.OrdinalIgnoreCase));

    public static bool HasExactArguments(IReadOnlyList<string> args)
        => args.Count == AllowedArguments.Length
            && args.Select((argument, index) => string.Equals(argument, AllowedArguments[index], StringComparison.Ordinal))
                .All(matches => matches);

    public static StandaloneUpdateCheckResultV1 InvalidArguments() => new(
        SchemaVersion: 1,
        Operation: "update-check-once",
        ResultCode: "invalid_arguments",
        Success: false,
        LatestVerifiedVersion: null,
        UpdateAvailable: false,
        Summary: "The standalone update check accepts only the fixed Configurator command arguments.",
        Status: null);

    public static StandaloneUpdateCheckResultV1 Failed(string code, string summary) => new(
        SchemaVersion: 1,
        Operation: "update-check-once",
        ResultCode: code,
        Success: false,
        LatestVerifiedVersion: null,
        UpdateAvailable: false,
        Summary: summary,
        Status: null);
}
