namespace PimaxVrcSupervisor.Updates;

internal sealed class SupervisorUpdateCoordinator : IDisposable
{
    private readonly UpdateStateStore _store;
    private readonly UpdateDiscoveryScheduler _scheduler;
    private readonly IDisposable? _ownedClient;
    private readonly IUpdateScheduleClock _clock;
    private readonly bool _verificationConfigured;
    private readonly IUpdateDiscoveryDiagnostics _diagnostics;
    private readonly object _operationLock = new();
    private UpdateCheckOperationSnapshot? _operation;

    public SupervisorUpdateCoordinator(
        SupervisorUpdatePolicy policy,
        UpdateStateStore store,
        UpdateDiscoveryScheduler scheduler,
        IUpdateScheduleClock clock,
        bool verificationConfigured,
        IDisposable? ownedClient = null,
        IUpdateDiscoveryDiagnostics? diagnostics = null)
    {
        Policy = policy;
        _store = store;
        _scheduler = scheduler;
        _clock = clock;
        _verificationConfigured = verificationConfigured;
        _ownedClient = ownedClient;
        _diagnostics = diagnostics ?? NullUpdateDiscoveryDiagnostics.Instance;
        SynchronizePolicy();
    }

    public SupervisorUpdatePolicy Policy { get; }

    public static SupervisorUpdateCoordinator CreateProduction(
        SupervisorUpdatePolicy policy,
        Action<UpdateDiscoveryDiagnostic> diagnostics)
    {
        var runtimeConfigPath = Path.Combine(AppContext.BaseDirectory, "PimaxVrcSupervisor.runtimeconfig.json");
        var variant = InstalledPackageVariantDetector.Detect(File.ReadAllBytes(runtimeConfigPath));
        var store = new UpdateStateStore(variant);
        var clock = new SystemUpdateScheduleClock();
        var options = new UpdateDiscoveryOptions
        {
            InstalledVersion = SemanticVersion.Parse(AppVersion.Current),
            InstalledVariant = variant
        };
        var trustStore = ProductionUpdateTrustRoots.CreateTrustStore();
        var client = GitHubUpdateDiscoveryClient.CreateProduction(options);
        var diagnosticSink = new DelegateUpdateDiscoveryDiagnostics(diagnostics);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, diagnosticSink);
        return new SupervisorUpdateCoordinator(policy, store, scheduler, clock, trustStore.Count > 0, client, diagnosticSink);
    }

    public void StartAutomaticSession(CancellationToken cancellationToken)
    {
        if (Policy == SupervisorUpdatePolicy.Notify && _verificationConfigured)
        {
            _scheduler.StartAutomaticSessionInBackground(cancellationToken);
        }
        else if (Policy == SupervisorUpdatePolicy.Notify)
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(
                UpdateCheckKind.Automatic,
                "verificationUnavailable",
                UpdateDiscoveryStatus.Failed,
                "verification_unavailable"));
        }
    }

    public UpdateStatusSnapshotV1 GetStatus()
    {
        var load = _store.Load();
        var state = load.State;
        var current = SemanticVersion.Parse(AppVersion.Current);
        var latest = TryParseStable(state.LatestVerifiedVersion);
        var available = latest is not null && latest.CompareTo(current) > 0;
        var dismissed = available && string.Equals(state.DismissedVersion, state.LatestVerifiedVersion, StringComparison.Ordinal);
        UpdateCheckOperationSnapshot? operation;
        lock (_operationLock)
        {
            operation = _operation;
        }

        var eligibility = UpdateDiscoveryScheduler.EvaluateAutomaticEligibility(state, _clock.UtcNow);
        return new UpdateStatusSnapshotV1(
            1,
            Policy.ToString(),
            "Stable",
            current.ToString(),
            state.LatestVerifiedVersion,
            available,
            dismissed,
            state.DismissedVersion,
            state.LastAttemptUtc,
            state.LastSuccessfulCheckUtc,
            load.CorruptionDetected ? "state_corrupt" : state.LastError?.Code,
            load.CorruptionDetected ? "Saved update state was corrupt and was ignored." : DescribeError(state.LastError?.Code),
            _verificationConfigured,
            _verificationConfigured && eligibility.Decision == AutomaticUpdateCheckDecision.Due,
            _scheduler.IsCheckInProgress,
            operation);
    }

    public UpdateActionAcceptance TryStartManualCheck(CancellationToken supervisorShutdown)
    {
        var admission = _scheduler.TryAcquireAdmission(UpdateCheckKind.Manual);
        if (admission.Status == UpdateCheckAdmissionStatus.AlreadyRunning)
        {
            return new UpdateActionAcceptance(
                Accepted: false,
                AlreadyInProgress: true,
                OperationId: null,
                Message: "An update check is already running.",
                ResultCode: "already_running");
        }

        if (!admission.IsAdmitted)
        {
            return new UpdateActionAcceptance(
                Accepted: false,
                AlreadyInProgress: false,
                OperationId: null,
                Message: "The secure update-check gate is unavailable.",
                ResultCode: "gate_unavailable");
        }

        var admissionLease = admission.Lease!;
        try
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(UpdateCheckKind.Manual, "accepted", null, null));
            var operationId = "updatecheck-" + Guid.NewGuid().ToString("N");
            SetOperation(new UpdateCheckOperationSnapshot(operationId, "running", null, "started", "Checking for a signed stable update.", _clock.UtcNow, null));
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!_verificationConfigured)
                    {
                        using (admissionLease)
                        {
                            _diagnostics.Record(new UpdateDiscoveryDiagnostic(UpdateCheckKind.Manual, "verificationUnavailable", UpdateDiscoveryStatus.Failed, "verification_unavailable"));
                            await PersistVerificationUnavailableAsync().ConfigureAwait(false);
                        }

                        Complete(operationId, false, "verification_unavailable", "Update verification is unavailable because no production trust root is configured.");
                        return;
                    }

                    var result = await _scheduler.CheckAdmittedAsync(
                        UpdateCheckKind.Manual,
                        admissionLease,
                        supervisorShutdown).ConfigureAwait(false);
                    Complete(operationId, result.CompletedSuccessfully, ResultCode(result), DescribeResult(result));
                }
                catch (OperationCanceledException)
                {
                    admissionLease.Dispose();
                    Complete(operationId, false, "cancelled", "The update check was cancelled during Supervisor shutdown.");
                }
            }, CancellationToken.None);
            return new UpdateActionAcceptance(true, false, operationId, "Update check accepted.", "accepted");
        }
        catch
        {
            admissionLease.Dispose();
            throw;
        }
    }

    public async Task<UpdateActionAcceptance> DismissAsync(CancellationToken cancellationToken)
    {
        var state = _store.Load().State;
        var latest = TryParseStable(state.LatestVerifiedVersion);
        if (latest is null || latest.CompareTo(SemanticVersion.Parse(AppVersion.Current)) <= 0)
        {
            return new UpdateActionAcceptance(false, false, null, "There is no newer verified update to dismiss.");
        }

        await _store.SaveAsync(state with { DismissedVersion = latest.ToString(), DismissedAtUtc = _clock.UtcNow }, cancellationToken).ConfigureAwait(false);
        return new UpdateActionAcceptance(true, false, null, $"Dismissed verified update {latest}.");
    }

    public async Task<UpdateActionAcceptance> ClearDismissalAsync(CancellationToken cancellationToken)
    {
        var state = _store.Load().State;
        await _store.SaveAsync(state with { DismissedVersion = null, DismissedAtUtc = null }, cancellationToken).ConfigureAwait(false);
        return new UpdateActionAcceptance(true, false, null, "Update dismissal cleared.");
    }

    public void Dispose() => _ownedClient?.Dispose();

    private void SynchronizePolicy()
    {
        var state = _store.Load().State;
        var statePolicy = Policy == SupervisorUpdatePolicy.Notify ? UpdateCheckPolicy.NotifyStable : UpdateCheckPolicy.Disabled;
        if (state.Policy != statePolicy)
        {
            _store.SaveAsync(state with { Policy = statePolicy }, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private async Task PersistVerificationUnavailableAsync()
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
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(UpdateCheckKind.Manual, "resultStatePersisted", UpdateDiscoveryStatus.Failed, "verification_unavailable"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UpdateContractException)
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(UpdateCheckKind.Manual, "statePersistenceFailed", UpdateDiscoveryStatus.Failed, "state_write"));
        }
    }

    private UpdateCheckOperationSnapshot? GetOperation()
    {
        lock (_operationLock) return _operation;
    }

    private void SetOperation(UpdateCheckOperationSnapshot operation)
    {
        lock (_operationLock) _operation = operation;
    }

    private void Complete(string operationId, bool success, string code, string summary)
    {
        lock (_operationLock)
        {
            if (_operation?.OperationId == operationId)
            {
                _operation = _operation with
                {
                    Status = success ? "completed" : "failed",
                    Success = success,
                    ResultCode = Bound(code, 64),
                    ResultSummary = Bound(summary, 256),
                    CompletedAt = _clock.UtcNow
                };
            }
        }
    }

    private static SemanticVersion? TryParseStable(string? value)
        => SemanticVersion.TryParse(value, out var version) && version.IsStable ? version : null;

    internal static string ResultCode(UpdateDiscoveryCheckResult result)
        => result.ErrorCode ?? result.Status switch
        {
            UpdateDiscoveryStatus.UpdateAvailable => "update_available",
            UpdateDiscoveryStatus.Current => "current",
            UpdateDiscoveryStatus.NotModified => "not_modified",
            UpdateDiscoveryStatus.Ignored => "ignored",
            UpdateDiscoveryStatus.Cancelled => "cancelled",
            _ => "update_failed"
        };

    internal static string DescribeResult(UpdateDiscoveryCheckResult result) => result.Status switch
    {
        UpdateDiscoveryStatus.UpdateAvailable => $"Verified stable update {result.Version} is available.",
        UpdateDiscoveryStatus.Current => "The installed version is current.",
        UpdateDiscoveryStatus.NotModified => "The verified release metadata has not changed.",
        UpdateDiscoveryStatus.Ignored => "No eligible stable release was found.",
        UpdateDiscoveryStatus.Cancelled => "The update check was cancelled.",
        _ => "The update check failed: " + (result.ErrorCode ?? "update_failed") + "."
    };

    internal static string? DescribeError(string? code)
        => code is null ? null : "The last update check failed: " + Bound(code, 64) + ".";

    private static string Bound(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum];

    private sealed class DelegateUpdateDiscoveryDiagnostics(Action<UpdateDiscoveryDiagnostic> write) : IUpdateDiscoveryDiagnostics
    {
        public void Record(UpdateDiscoveryDiagnostic diagnostic) => write(diagnostic);
    }
}
