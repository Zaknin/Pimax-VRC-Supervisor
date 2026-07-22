namespace PimaxVrcSupervisor.Updates;

internal interface IUpdateScheduleClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemUpdateScheduleClock : IUpdateScheduleClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}

internal enum UpdateCheckKind
{
    Automatic,
    Manual,
    Worker
}

internal sealed record UpdateDiscoveryDiagnostic(
    UpdateCheckKind Kind,
    string Event,
    UpdateDiscoveryStatus? Status,
    string? ErrorCode);

internal interface IUpdateDiscoveryDiagnostics
{
    void Record(UpdateDiscoveryDiagnostic diagnostic);
}

internal sealed class NullUpdateDiscoveryDiagnostics : IUpdateDiscoveryDiagnostics
{
    public static NullUpdateDiscoveryDiagnostics Instance { get; } = new();

    public void Record(UpdateDiscoveryDiagnostic diagnostic)
    {
    }
}

internal enum AutomaticUpdateCheckDecision
{
    Disabled,
    Due,
    Wait,
    ClockRollback
}

internal sealed record AutomaticUpdateCheckEligibility(
    AutomaticUpdateCheckDecision Decision,
    TimeSpan Delay);

internal sealed class UpdateDiscoveryScheduler
{
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(24);

    private readonly UpdateStateStore _stateStore;
    private readonly IUpdateDiscoveryClient _client;
    private readonly IUpdateScheduleClock _clock;
    private readonly IUpdateDiscoveryDiagnostics _diagnostics;
    private readonly UpdateCheckAdmission _admission;
    private int _automaticSessionStarted;
    private int _automaticAttempted;

    public bool IsCheckInProgress => _admission.IsActive;

    public UpdateDiscoveryScheduler(
        UpdateStateStore stateStore,
        IUpdateDiscoveryClient client,
        IUpdateScheduleClock clock,
        IUpdateDiscoveryDiagnostics? diagnostics = null,
        IUpdateCheckExclusion? checkExclusion = null,
        UpdateCheckAdmission? admission = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _diagnostics = diagnostics ?? NullUpdateDiscoveryDiagnostics.Instance;
        if (checkExclusion is not null && admission is not null)
        {
            throw new ArgumentException("Specify either an update-check exclusion or a shared admission component, not both.");
        }

        _admission = admission
            ?? new UpdateCheckAdmission(
                checkExclusion ?? UserScopedUpdateCheckExclusion.ForCurrentUser());
    }

    public void StartAutomaticSessionInBackground(CancellationToken cancellationToken)
        => _ = Task.Run(() => RunAutomaticSessionAsync(cancellationToken), CancellationToken.None);

    public async Task RunAutomaticSessionAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _automaticSessionStarted, 1, 0) != 0)
        {
            return;
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var load = _stateStore.Load();
                var eligibility = EvaluateAutomaticEligibility(load.State, _clock.UtcNow);
                switch (eligibility.Decision)
                {
                    case AutomaticUpdateCheckDecision.Disabled:
                        _diagnostics.Record(new UpdateDiscoveryDiagnostic(
                            UpdateCheckKind.Automatic,
                            "disabled",
                            Status: null,
                            ErrorCode: load.CorruptionDetected ? "state_corrupt" : null));
                        return;
                    case AutomaticUpdateCheckDecision.ClockRollback:
                        _diagnostics.Record(new UpdateDiscoveryDiagnostic(
                            UpdateCheckKind.Automatic,
                            "clockRollback",
                            Status: null,
                            ErrorCode: "clock_rollback"));
                        return;
                    case AutomaticUpdateCheckDecision.Wait:
                        _diagnostics.Record(new UpdateDiscoveryDiagnostic(
                            UpdateCheckKind.Automatic,
                            "notDue",
                            Status: null,
                            ErrorCode: null));
                        await _clock.DelayAsync(eligibility.Delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    case AutomaticUpdateCheckDecision.Due:
                        if (Interlocked.CompareExchange(ref _automaticAttempted, 1, 0) != 0)
                        {
                            return;
                        }

                        _diagnostics.Record(new UpdateDiscoveryDiagnostic(
                            UpdateCheckKind.Automatic,
                            "due",
                            Status: null,
                            ErrorCode: null));
                        var admission = TryAcquireAdmission(UpdateCheckKind.Automatic);
                        if (!admission.IsAdmitted)
                        {
                            return;
                        }

                        await CheckAdmittedAsync(
                            UpdateCheckKind.Automatic,
                            admission.Lease!,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    default:
                        throw new InvalidOperationException("Unknown automatic update eligibility decision.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(
                UpdateCheckKind.Automatic,
                "cancelled",
                UpdateDiscoveryStatus.Cancelled,
                "cancelled"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UpdateContractException)
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(
                UpdateCheckKind.Automatic,
                "stateFailure",
                UpdateDiscoveryStatus.Failed,
                "state_io"));
        }
    }

    public async Task<UpdateDiscoveryCheckResult> CheckManuallyAsync(CancellationToken cancellationToken)
    {
        var admission = TryAcquireAdmission(UpdateCheckKind.Manual);
        if (!admission.IsAdmitted)
        {
            return AdmissionFailure(admission);
        }

        return await CheckAdmittedAsync(
            UpdateCheckKind.Manual,
            admission.Lease!,
            cancellationToken).ConfigureAwait(false);
    }

    public UpdateCheckAdmissionResult TryAcquireAdmission(UpdateCheckKind kind)
    {
        var admission = _admission.TryAcquire();
        if (admission.Status == UpdateCheckAdmissionStatus.AlreadyRunning)
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(
                kind,
                "rejectedAlreadyRunning",
                UpdateDiscoveryStatus.Failed,
                "already_running"));
        }
        else if (admission.Status == UpdateCheckAdmissionStatus.Unavailable)
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(
                kind,
                "rejectedGateUnavailable",
                UpdateDiscoveryStatus.Failed,
                "gate_unavailable"));
        }

        return admission;
    }

    public async Task<UpdateDiscoveryCheckResult> CheckAdmittedAsync(
        UpdateCheckKind kind,
        UpdateCheckAdmissionLease admissionLease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admissionLease);
        using (admissionLease)
        {
            try
            {
                return await ExecuteCheckAsync(kind, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return UpdateDiscoveryCheckResult.Cancelled();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UpdateContractException)
            {
                return UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.State, "state_io");
            }
        }
    }

    private static UpdateDiscoveryCheckResult AdmissionFailure(UpdateCheckAdmissionResult admission)
        => UpdateDiscoveryCheckResult.Failure(
            UpdateErrorCategory.State,
            admission.ErrorCode ?? "gate_unavailable");

    public static AutomaticUpdateCheckEligibility EvaluateAutomaticEligibility(
        UpdateStateV1 state,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Policy != UpdateCheckPolicy.NotifyStable || state.Channel != UpdateChannel.Stable)
        {
            return new AutomaticUpdateCheckEligibility(AutomaticUpdateCheckDecision.Disabled, TimeSpan.Zero);
        }

        if (state.LastSuccessfulCheckUtc is null)
        {
            return new AutomaticUpdateCheckEligibility(AutomaticUpdateCheckDecision.Due, TimeSpan.Zero);
        }

        if (state.LastSuccessfulCheckUtc > nowUtc)
        {
            return new AutomaticUpdateCheckEligibility(AutomaticUpdateCheckDecision.ClockRollback, TimeSpan.Zero);
        }

        var elapsed = nowUtc - state.LastSuccessfulCheckUtc.Value;
        return elapsed >= AutomaticInterval
            ? new AutomaticUpdateCheckEligibility(AutomaticUpdateCheckDecision.Due, TimeSpan.Zero)
            : new AutomaticUpdateCheckEligibility(AutomaticUpdateCheckDecision.Wait, AutomaticInterval - elapsed);
    }

    private async Task<UpdateDiscoveryCheckResult> ExecuteCheckAsync(
        UpdateCheckKind kind,
        CancellationToken cancellationToken)
    {
        var load = _stateStore.Load();
        var state = load.State;
        if (kind == UpdateCheckKind.Automatic)
        {
            var eligibility = EvaluateAutomaticEligibility(state, _clock.UtcNow);
            if (eligibility.Decision != AutomaticUpdateCheckDecision.Due)
            {
                return new UpdateDiscoveryCheckResult
                {
                    Status = UpdateDiscoveryStatus.Ignored,
                    ETag = state.ETag,
                    Version = state.LatestVerifiedVersion,
                    Tag = state.LatestVerifiedTag,
                    ReleaseUrl = state.LatestVerifiedReleaseUrl,
                    VerifiedManifest = null,
                    ErrorCategory = null,
                    ErrorCode = null
                };
            }
        }

        var attemptAtUtc = _clock.UtcNow;
        try
        {
            await _stateStore.SaveAsync(
                state with { LastAttemptUtc = attemptAtUtc },
                cancellationToken).ConfigureAwait(false);
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, "attemptStatePersisted", Status: null, ErrorCode: null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UpdateContractException)
        {
            var stateFailure = UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.State, "state_write");
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, "failed", stateFailure.Status, stateFailure.ErrorCode));
            return stateFailure;
        }

        _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, "started", Status: null, ErrorCode: null));
        var result = await _client.CheckAsync(state.ETag, cancellationToken).ConfigureAwait(false);
        var completedAtUtc = _clock.UtcNow;
        result = EnforcePersistedRollbackBoundary(state, result);
        RecordVerificationDiagnostics(kind, result);
        var nextState = BuildNextState(state, result, attemptAtUtc, completedAtUtc);
        try
        {
            await _stateStore.SaveAsync(nextState, cancellationToken).ConfigureAwait(false);
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, "resultStatePersisted", result.Status, result.ErrorCode));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UpdateContractException)
        {
            var stateFailure = UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.State, "state_write");
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, "failed", stateFailure.Status, stateFailure.ErrorCode));
            return stateFailure;
        }

        _diagnostics.Record(new UpdateDiscoveryDiagnostic(
            kind,
            result.CompletedSuccessfully ? "completed" : "failed",
            result.Status,
            result.ErrorCode));
        return result;
    }

    private void RecordVerificationDiagnostics(UpdateCheckKind kind, UpdateDiscoveryCheckResult result)
    {
        if (result.Status is UpdateDiscoveryStatus.UpdateAvailable or UpdateDiscoveryStatus.Current or UpdateDiscoveryStatus.Ignored)
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, "releaseCandidateSelected", result.Status, null));
        }

        if (result.Status == UpdateDiscoveryStatus.UpdateAvailable && result.VerifiedManifest is not null)
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, "signatureVerified", result.Status, null));
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, "manifestValidated", result.Status, null));
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, "semanticVersionUpdateAvailable", result.Status, null));
            return;
        }

        var verificationEvent = result.ErrorCategory switch
        {
            UpdateErrorCategory.Signature => "signatureRejected",
            UpdateErrorCategory.Schema or UpdateErrorCategory.ReleaseMismatch => "manifestRejected",
            _ => result.Status switch
            {
                UpdateDiscoveryStatus.Current => "semanticVersionCurrent",
                UpdateDiscoveryStatus.Ignored => "semanticVersionIneligible",
                _ => null
            }
        };
        if (verificationEvent is not null)
        {
            _diagnostics.Record(new UpdateDiscoveryDiagnostic(kind, verificationEvent, result.Status, result.ErrorCode));
        }
    }

    private static UpdateStateV1 BuildNextState(
        UpdateStateV1 state,
        UpdateDiscoveryCheckResult result,
        DateTimeOffset attemptAtUtc,
        DateTimeOffset completedAtUtc)
    {
        if (!result.CompletedSuccessfully)
        {
            return state with
            {
                LastAttemptUtc = attemptAtUtc,
                LastError = new UpdateBoundedErrorV1
                {
                    Category = result.ErrorCategory ?? UpdateErrorCategory.Http,
                    Code = BoundErrorCode(result.ErrorCode),
                    AtUtc = completedAtUtc
                }
            };
        }

        var updated = state with
        {
            LastAttemptUtc = attemptAtUtc,
            LastSuccessfulCheckUtc = completedAtUtc,
            ETag = result.ETag ?? state.ETag,
            LastError = null
        };
        if (result.Status != UpdateDiscoveryStatus.UpdateAvailable || result.VerifiedManifest is null)
        {
            return updated;
        }

        var manifest = result.VerifiedManifest;
        return updated with
        {
            LastManifestSha256 = manifest.ManifestSha256,
            HighestAcceptedReleaseSequence = Math.Max(
                state.HighestAcceptedReleaseSequence,
                manifest.Manifest.ReleaseSequence),
            HighestAcceptedVersion = manifest.Version.ToString(),
            LatestVerifiedVersion = manifest.Version.ToString(),
            LatestVerifiedTag = manifest.Manifest.Release.Tag,
            LatestVerifiedReleaseUrl = manifest.Manifest.Release.ReleaseUrl
        };
    }

    private static string BoundErrorCode(string? code)
        => UpdateContractValidation.IsSafeIdentifier(code, maximumLength: 64)
            ? code!
            : "update_failed";

    private static UpdateDiscoveryCheckResult EnforcePersistedRollbackBoundary(
        UpdateStateV1 state,
        UpdateDiscoveryCheckResult result)
    {
        if (result.Status != UpdateDiscoveryStatus.UpdateAvailable || result.VerifiedManifest is null)
        {
            return result;
        }

        var manifest = result.VerifiedManifest;
        return manifest.Manifest.ReleaseSequence < state.HighestAcceptedReleaseSequence
            || (state.HighestAcceptedVersion is not null
                && manifest.Version.CompareTo(SemanticVersion.Parse(state.HighestAcceptedVersion)) < 0)
                    ? UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.Rollback, "state_rollback")
                    : result;
    }
}
