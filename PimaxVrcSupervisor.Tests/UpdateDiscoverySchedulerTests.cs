using System.Security.Principal;
using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class UpdateDiscoverySchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DisabledPolicyProducesZeroAutomaticRequests()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var client = new FakeUpdateDiscoveryClient(CurrentResult());
        var scheduler = new UpdateDiscoveryScheduler(store, client, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());

        await scheduler.RunAutomaticSessionAsync(CancellationToken.None);

        Assert.Equal(0, client.CallCount);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("notify")]
    public async Task ManualChecksRunUnderEveryPolicy(string policyName)
    {
        var policy = policyName == "disabled" ? UpdateCheckPolicy.Disabled : UpdateCheckPolicy.NotifyStable;
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await store.SaveAsync(UpdateContractTestData.CreateState(policy: policy), CancellationToken.None);
        var client = new FakeUpdateDiscoveryClient(CurrentResult());
        var scheduler = new UpdateDiscoveryScheduler(store, client, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());

        var result = await scheduler.CheckManuallyAsync(CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Current, result.Status);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void Exact24HourBoundaryIsDue()
    {
        var state = UpdateContractTestData.CreateState(
            lastSuccessfulCheckUtc: Now - TimeSpan.FromHours(24));

        var atBoundary = UpdateDiscoveryScheduler.EvaluateAutomaticEligibility(state, Now);
        var beforeBoundary = UpdateDiscoveryScheduler.EvaluateAutomaticEligibility(
            state with { LastSuccessfulCheckUtc = Now - TimeSpan.FromHours(24) + TimeSpan.FromTicks(1) },
            Now);

        Assert.Equal(AutomaticUpdateCheckDecision.Due, atBoundary.Decision);
        Assert.Equal(AutomaticUpdateCheckDecision.Wait, beforeBoundary.Decision);
        Assert.Equal(TimeSpan.FromTicks(1), beforeBoundary.Delay);
    }

    [Fact]
    public async Task RecentAttemptDoesNotDelayCheckWhenLastSuccessIsDue()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var state = UpdateContractTestData.CreateState(
            lastSuccessfulCheckUtc: Now - TimeSpan.FromHours(24)) with
        {
            LastAttemptUtc = Now - TimeSpan.FromMinutes(1)
        };
        await store.SaveAsync(state, CancellationToken.None);
        var client = new FakeUpdateDiscoveryClient(CurrentResult());
        var scheduler = new UpdateDiscoveryScheduler(store, client, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());

        await scheduler.RunAutomaticSessionAsync(CancellationToken.None);

        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task AutomaticFailureAttemptsOnlyOncePerSessionAndDoesNotAdvanceSuccess()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var previousSuccess = Now - TimeSpan.FromHours(25);
        await store.SaveAsync(
            UpdateContractTestData.CreateState(lastSuccessfulCheckUtc: previousSuccess),
            CancellationToken.None);
        var client = new FakeUpdateDiscoveryClient(
            UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.Timeout, "request_timeout"));
        var diagnostics = new RecordingUpdateDiscoveryDiagnostics();
        var scheduler = new UpdateDiscoveryScheduler(
            store,
            client,
            new ManualUpdateScheduleClock(Now),
            diagnostics,
            checkExclusion: new IsolatedUpdateCheckExclusion());

        await scheduler.RunAutomaticSessionAsync(CancellationToken.None);
        await scheduler.RunAutomaticSessionAsync(CancellationToken.None);
        var state = store.Load().State;

        Assert.Equal(1, client.CallCount);
        Assert.Equal(previousSuccess, state.LastSuccessfulCheckUtc);
        Assert.Equal("request_timeout", state.LastError?.Code);
        Assert.Contains(diagnostics.Events, item => item.Kind == UpdateCheckKind.Automatic && item.Event == "failed");
    }

    [Fact]
    public async Task LongRunningSessionChecksOnceWhenItBecomesDue()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await store.SaveAsync(
            UpdateContractTestData.CreateState(lastSuccessfulCheckUtc: Now),
            CancellationToken.None);
        var client = new FakeUpdateDiscoveryClient(CurrentResult());
        var clock = new ManualUpdateScheduleClock(Now);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, checkExclusion: new IsolatedUpdateCheckExclusion());

        var run = scheduler.RunAutomaticSessionAsync(CancellationToken.None);
        await clock.WaitForDelayAsync();
        Assert.Equal(TimeSpan.FromHours(24), Assert.Single(clock.Delays));
        Assert.Equal(0, client.CallCount);

        clock.Advance(TimeSpan.FromHours(24));
        await run;

        Assert.Equal(1, client.CallCount);
        Assert.Equal(Now + TimeSpan.FromHours(24), store.Load().State.LastSuccessfulCheckUtc);
    }

    [Fact]
    public async Task ClockRollbackSuppressesAutomaticRequestConservatively()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await store.SaveAsync(
            UpdateContractTestData.CreateState(lastSuccessfulCheckUtc: Now + TimeSpan.FromHours(1)),
            CancellationToken.None);
        var client = new FakeUpdateDiscoveryClient(CurrentResult());
        var diagnostics = new RecordingUpdateDiscoveryDiagnostics();
        var scheduler = new UpdateDiscoveryScheduler(store, client, new ManualUpdateScheduleClock(Now), diagnostics, new IsolatedUpdateCheckExclusion());

        await scheduler.RunAutomaticSessionAsync(CancellationToken.None);

        Assert.Equal(0, client.CallCount);
        Assert.Contains(diagnostics.Events, item => item.Event == "clockRollback");
    }

    [Fact]
    public async Task CorruptTimestampRecoversToDisabledStateWithoutRequest()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        Directory.CreateDirectory(temp.Path);
        await File.WriteAllTextAsync(
            store.StatePath,
            "{\"schemaVersion\":1,\"policy\":\"notifyStable\",\"lastSuccessfulCheckUtc\":\"broken\"}");
        var client = new FakeUpdateDiscoveryClient(CurrentResult());
        var scheduler = new UpdateDiscoveryScheduler(store, client, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());

        await scheduler.RunAutomaticSessionAsync(CancellationToken.None);

        Assert.Equal(0, client.CallCount);
        Assert.True(store.Load().CorruptionDetected);
    }

    [Fact]
    public async Task ManualFailureReturnsStructuredResultAndPersistsBoundedError()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var failure = UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.Http, "release_http");
        var client = new FakeUpdateDiscoveryClient(failure);
        var scheduler = new UpdateDiscoveryScheduler(store, client, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());

        var result = await scheduler.CheckManuallyAsync(CancellationToken.None);
        var state = store.Load().State;

        Assert.Equal(UpdateDiscoveryStatus.Failed, result.Status);
        Assert.Equal(UpdateErrorCategory.Http, result.ErrorCategory);
        Assert.Equal("release_http", result.ErrorCode);
        Assert.Equal("release_http", state.LastError?.Code);
        Assert.Equal(Now, state.LastAttemptUtc);
        Assert.Null(state.LastSuccessfulCheckUtc);
    }

    [Fact]
    public async Task ManualCancellationReturnsStructuredCancelledResult()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var client = new FakeUpdateDiscoveryClient(CurrentResult());
        var scheduler = new UpdateDiscoveryScheduler(store, client, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await scheduler.CheckManuallyAsync(cancellation.Token);

        Assert.Equal(UpdateDiscoveryStatus.Cancelled, result.Status);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task VerifiedUpdatePersistsManifestSequenceVersionAndCanonicalRelease()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var signed = UpdateContractTestData.Sign(UpdateContractTestData.CreateManifest());
        var verified = UpdateManifestVerifier.VerifyAndParse(
            signed.ManifestBytes,
            signed.SignatureEnvelopeBytes,
            signed.TrustStore,
            UpdatePackageVariant.WithDotnet9);
        var update = new UpdateDiscoveryCheckResult
        {
            Status = UpdateDiscoveryStatus.UpdateAvailable,
            ETag = "\"verified\"",
            Version = "1.4.0",
            Tag = "v1.4.0",
            ReleaseUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v1.4.0",
            VerifiedManifest = verified,
            ErrorCategory = null,
            ErrorCode = null
        };
        var scheduler = new UpdateDiscoveryScheduler(
            store,
            new FakeUpdateDiscoveryClient(update),
            new ManualUpdateScheduleClock(Now),
            checkExclusion: new IsolatedUpdateCheckExclusion());

        var result = await scheduler.CheckManuallyAsync(CancellationToken.None);
        var state = store.Load().State;

        Assert.Equal(UpdateDiscoveryStatus.UpdateAvailable, result.Status);
        Assert.Equal(4, state.HighestAcceptedReleaseSequence);
        Assert.Equal("1.4.0", state.HighestAcceptedVersion);
        Assert.Equal("1.4.0", state.LatestVerifiedVersion);
        Assert.Equal(verified.ManifestSha256, state.LastManifestSha256);
        Assert.Equal(Now, state.LastSuccessfulCheckUtc);
    }

    [Fact]
    public async Task PersistedSequenceRejectsOtherwiseValidRollbackAsStructuredFailure()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await store.SaveAsync(
            UpdateContractTestData.CreateState() with
            {
                HighestAcceptedReleaseSequence = 10,
                HighestAcceptedVersion = "1.4.0"
            },
            CancellationToken.None);
        var signed = UpdateContractTestData.Sign(UpdateContractTestData.CreateManifest());
        var verified = UpdateManifestVerifier.VerifyAndParse(
            signed.ManifestBytes,
            signed.SignatureEnvelopeBytes,
            signed.TrustStore,
            UpdatePackageVariant.WithDotnet9);
        var update = new UpdateDiscoveryCheckResult
        {
            Status = UpdateDiscoveryStatus.UpdateAvailable,
            ETag = "\"rollback\"",
            Version = "1.4.0",
            Tag = "v1.4.0",
            ReleaseUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v1.4.0",
            VerifiedManifest = verified,
            ErrorCategory = null,
            ErrorCode = null
        };
        var scheduler = new UpdateDiscoveryScheduler(
            store,
            new FakeUpdateDiscoveryClient(update),
            new ManualUpdateScheduleClock(Now),
            checkExclusion: new IsolatedUpdateCheckExclusion());

        var result = await scheduler.CheckManuallyAsync(CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, result.Status);
        Assert.Equal(UpdateErrorCategory.Rollback, result.ErrorCategory);
        Assert.Equal("state_rollback", result.ErrorCode);
        Assert.Equal(10, store.Load().State.HighestAcceptedReleaseSequence);
    }

    [Fact]
    public void BackgroundStartSurfaceCannotBeAwaitedByStartup()
    {
        var method = typeof(UpdateDiscoveryScheduler).GetMethod(nameof(UpdateDiscoveryScheduler.StartAutomaticSessionInBackground));

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);
    }

    [Fact]
    public async Task NotModifiedAdvancesSuccessfulCompletionAndPersistsEtag()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await store.SaveAsync(
            UpdateContractTestData.CreateState(policy: UpdateCheckPolicy.Disabled) with { ETag = "\"old\"" },
            CancellationToken.None);
        var client = new FakeUpdateDiscoveryClient(new UpdateDiscoveryCheckResult
        {
            Status = UpdateDiscoveryStatus.NotModified,
            ETag = "\"new\"",
            Version = null,
            Tag = null,
            ReleaseUrl = null,
            VerifiedManifest = null,
            ErrorCategory = null,
            ErrorCode = null
        });
        var scheduler = new UpdateDiscoveryScheduler(store, client, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());

        var result = await scheduler.CheckManuallyAsync(CancellationToken.None);
        var state = store.Load().State;

        Assert.Equal(UpdateDiscoveryStatus.NotModified, result.Status);
        Assert.Equal("\"old\"", Assert.Single(client.ETags));
        Assert.Equal("\"new\"", state.ETag);
        Assert.Equal(Now, state.LastSuccessfulCheckUtc);
        Assert.Null(state.LastError);
    }

    [Fact]
    public async Task ConcurrentManualChecksReturnAlreadyRunningWithoutQueue()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var client = new BlockingUpdateDiscoveryClient();
        var scheduler = new UpdateDiscoveryScheduler(store, client, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());

        var first = scheduler.CheckManuallyAsync(CancellationToken.None);
        await client.WaitForFirstCallAsync();
        var second = await scheduler.CheckManuallyAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(250));

        Assert.Equal(1, client.CallCount);
        Assert.Equal(UpdateDiscoveryStatus.Failed, second.Status);
        Assert.Equal("already_running", second.ErrorCode);

        client.Release();
        await first;

        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task SeparateWorkerSchedulersShareCrossProcessExclusionWithoutQueueing()
    {
        using var temp = new TempDirectory();
        var firstStore = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var secondStore = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var firstClient = new BlockingUpdateDiscoveryClient();
        var secondClient = new FakeUpdateDiscoveryClient(CurrentResult());
        var sharedExclusion = new SharedTestUpdateCheckExclusion();
        var firstScheduler = new UpdateDiscoveryScheduler(firstStore, firstClient, new ManualUpdateScheduleClock(Now), checkExclusion: sharedExclusion);
        var secondScheduler = new UpdateDiscoveryScheduler(secondStore, secondClient, new ManualUpdateScheduleClock(Now), checkExclusion: sharedExclusion);

        var first = firstScheduler.CheckManuallyAsync(CancellationToken.None);
        await firstClient.WaitForFirstCallAsync();
        var second = await secondScheduler.CheckManuallyAsync(CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, second.Status);
        Assert.Equal("already_running", second.ErrorCode);
        Assert.Equal(0, secondClient.CallCount);

        firstClient.Release();
        await first;
        var afterRelease = await secondScheduler.CheckManuallyAsync(CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Current, afterRelease.Status);
        Assert.Equal(1, secondClient.CallCount);
    }

    [Fact]
    public async Task AutomaticSupervisorCheckCannotOverlapStandaloneWorkerCheck()
    {
        using var temp = new TempDirectory();
        var workerStore = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var supervisorStore = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await workerStore.SaveAsync(
            UpdateContractTestData.CreateState(policy: UpdateCheckPolicy.NotifyStable),
            CancellationToken.None);
        var workerClient = new BlockingUpdateDiscoveryClient();
        var automaticClient = new FakeUpdateDiscoveryClient(CurrentResult());
        var diagnostics = new RecordingUpdateDiscoveryDiagnostics();
        var sharedExclusion = new SharedTestUpdateCheckExclusion();
        var workerScheduler = new UpdateDiscoveryScheduler(workerStore, workerClient, new ManualUpdateScheduleClock(Now), checkExclusion: sharedExclusion);
        var automaticScheduler = new UpdateDiscoveryScheduler(
            supervisorStore,
            automaticClient,
            new ManualUpdateScheduleClock(Now),
            diagnostics,
            checkExclusion: sharedExclusion);

        var worker = workerScheduler.CheckManuallyAsync(CancellationToken.None);
        await workerClient.WaitForFirstCallAsync();
        var stateBeforeRejectedAutomatic = supervisorStore.Load().State;
        await automaticScheduler.RunAutomaticSessionAsync(CancellationToken.None);

        Assert.Equal(0, automaticClient.CallCount);
        Assert.Equal(stateBeforeRejectedAutomatic, supervisorStore.Load().State);
        Assert.Contains(
            diagnostics.Events,
            item => item.Kind == UpdateCheckKind.Automatic
                && item.Event == "rejectedAlreadyRunning"
                && item.ErrorCode == "already_running");
        Assert.DoesNotContain(
            diagnostics.Events,
            item => item.Kind == UpdateCheckKind.Automatic
                && item.Event is "attemptStatePersisted"
                    or "started"
                    or "resultStatePersisted"
                    or "completed"
                    or "failed"
                    or "releaseCandidateSelected"
                    or "signatureVerified"
                    or "manifestValidated"
                    or "semanticVersionUpdateAvailable");

        workerClient.Release();
        await worker;
    }

    [Fact]
    public void NamedUpdateCheckMutexRecoversAfterLeaseRelease()
    {
        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("The current Windows user SID is unavailable.");
        var name = @"Global\PimaxVrcSupervisor.UpdateCheck.Test." + Guid.NewGuid().ToString("N");
        var first = UserScopedUpdateCheckExclusion.ForMutexName(name, sid);
        var second = UserScopedUpdateCheckExclusion.ForMutexName(name, sid);

        using (var lease = first.TryAcquire())
        {
            Assert.NotNull(lease);
            Assert.Null(second.TryAcquire());
        }

        using var recovered = second.TryAcquire();
        Assert.NotNull(recovered);
    }

    private static UpdateDiscoveryCheckResult CurrentResult() => new()
    {
        Status = UpdateDiscoveryStatus.Current,
        ETag = "\"current\"",
        Version = "1.3.1",
        Tag = "v1.3.1",
        ReleaseUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v1.3.1",
        VerifiedManifest = null,
        ErrorCategory = null,
        ErrorCode = null
    };
}

internal sealed class FakeUpdateDiscoveryClient : IUpdateDiscoveryClient
{
    private readonly Queue<UpdateDiscoveryCheckResult> _results;

    public FakeUpdateDiscoveryClient(params UpdateDiscoveryCheckResult[] results)
    {
        _results = new Queue<UpdateDiscoveryCheckResult>(results);
    }

    public int CallCount { get; private set; }

    public List<string?> ETags { get; } = [];

    public Task<UpdateDiscoveryCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        ETags.Add(etag);
        if (_results.Count == 0)
        {
            throw new InvalidOperationException("No fake update discovery result remains.");
        }

        return Task.FromResult(_results.Count == 1 ? _results.Peek() : _results.Dequeue());
    }
}

internal sealed class BlockingUpdateDiscoveryClient : IUpdateDiscoveryClient
{
    private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public async Task<UpdateDiscoveryCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken)
    {
        CallCount++;
        _firstCall.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        return new UpdateDiscoveryCheckResult
        {
            Status = UpdateDiscoveryStatus.Current,
            ETag = etag,
            Version = "1.3.1",
            Tag = "v1.3.1",
            ReleaseUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v1.3.1",
            VerifiedManifest = null,
            ErrorCategory = null,
            ErrorCode = null
        };
    }

    public Task WaitForFirstCallAsync() => _firstCall.Task;

    public void Release() => _release.TrySetResult();
}

internal sealed class ManualUpdateScheduleClock : IUpdateScheduleClock
{
    private readonly List<PendingDelay> _pending = [];
    private readonly TaskCompletionSource _delayObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ManualUpdateScheduleClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Delays.Add(delay);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        _pending.Add(new PendingDelay(UtcNow + delay, completion, registration));
        _delayObserved.TrySetResult();
        return completion.Task;
    }

    public Task WaitForDelayAsync() => _delayObserved.Task;

    public void Advance(TimeSpan duration)
    {
        UtcNow += duration;
        foreach (var pending in _pending.Where(item => item.DueAtUtc <= UtcNow).ToArray())
        {
            pending.Registration.Dispose();
            pending.Completion.TrySetResult();
            _pending.Remove(pending);
        }
    }

    private sealed record PendingDelay(
        DateTimeOffset DueAtUtc,
        TaskCompletionSource Completion,
        CancellationTokenRegistration Registration);
}

internal sealed class RecordingUpdateDiscoveryDiagnostics : IUpdateDiscoveryDiagnostics
{
    public List<UpdateDiscoveryDiagnostic> Events { get; } = [];

    public void Record(UpdateDiscoveryDiagnostic diagnostic) => Events.Add(diagnostic);
}
