using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class SupervisorUpdateCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, "Disabled")]
    [InlineData("", "Disabled")]
    [InlineData("NotifyStable", "Disabled")]
    [InlineData("notify", "Disabled")]
    [InlineData("Custom", "Disabled")]
    [InlineData("Disabled", "Disabled")]
    [InlineData("Notify", "Notify")]
    public void ConfigPolicyAllowsOnlyExactDisabledOrNotify(string? value, string expected)
        => Assert.Equal(expected, SupervisorUpdatePolicyContract.ParseConfigValue(value).ToString());

    [Fact]
    public void SupervisorConfigMissingOrUnknownPolicyFailsClosedToDisabled()
    {
        using var temp = new TempDirectory();
        var missingPath = Path.Combine(temp.Path, "missing.config.json");
        var unknownPath = Path.Combine(temp.Path, "unknown.config.json");
        File.WriteAllText(missingPath, "{}");
        File.WriteAllText(unknownPath, "{\"UpdatePolicy\":\"AnythingElse\"}");

        Assert.Equal(SupervisorUpdatePolicy.Disabled, SupervisorConfig.Load(missingPath).EffectiveUpdatePolicy);
        Assert.Equal(SupervisorUpdatePolicy.Disabled, SupervisorConfig.Load(unknownPath).EffectiveUpdatePolicy);
    }

    [Fact]
    public void SupervisorConfigLoadsExactNotifyAndIgnoresCustomChannelInput()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "notify.config.json");
        File.WriteAllText(path, "{\"UpdatePolicy\":\"Notify\",\"UpdateChannel\":\"beta\"}");

        var config = SupervisorConfig.Load(path);

        Assert.Equal(SupervisorUpdatePolicy.Notify, config.EffectiveUpdatePolicy);
        Assert.Equal("Notify", config.UpdatePolicy);
    }

    [Fact]
    public async Task CachedStatusNeverTreatsUnverifiedTagAsLatestVersion()
    {
        using var fixture = CreateCoordinator(SupervisorUpdatePolicy.Notify, verificationConfigured: true);
        var state = fixture.Store.Load().State with
        {
            LatestVerifiedVersion = null,
            LatestVerifiedTag = null,
            LatestVerifiedReleaseUrl = null
        };
        await fixture.Store.SaveAsync(state, CancellationToken.None);

        var status = fixture.Coordinator.GetStatus();

        Assert.Null(status.LatestVerifiedVersion);
        Assert.False(status.UpdateAvailable);
        Assert.Equal("Stable", status.Channel);
    }

    [Fact]
    public void CachedStatusQueryPerformsZeroDiscoveryRequests()
    {
        using var fixture = CreateCoordinator(SupervisorUpdatePolicy.Notify, verificationConfigured: true);

        _ = fixture.Coordinator.GetStatus();
        _ = fixture.Coordinator.GetStatus();

        Assert.Equal(0, fixture.Client.CallCount);
    }

    [Fact]
    public void UpdateStatusWireShapeIsVersionedAndContainsNoRemoteOrPackageLocation()
    {
        using var fixture = CreateCoordinator(SupervisorUpdatePolicy.Disabled, verificationConfigured: false);
        var json = System.Text.Json.JsonSerializer.Serialize(
            fixture.Coordinator.GetStatus(),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"channel\":\"Stable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"verificationConfigured\":false", json, StringComparison.Ordinal);
        Assert.DoesNotContain("url", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("package", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tag", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DismissalAppliesOnlyToCurrentVerifiedCandidateAndNewerVersionResurfaces()
    {
        using var fixture = CreateCoordinator(SupervisorUpdatePolicy.Notify, verificationConfigured: true);
        await fixture.Store.SaveAsync(VerifiedState(fixture.Store.Load().State, "1.4.0"), CancellationToken.None);

        var dismissed = await fixture.Coordinator.DismissAsync(CancellationToken.None);
        Assert.True(dismissed.Accepted);
        Assert.True(fixture.Coordinator.GetStatus().Dismissed);

        await fixture.Store.SaveAsync(VerifiedState(fixture.Store.Load().State, "1.5.0"), CancellationToken.None);
        var newer = fixture.Coordinator.GetStatus();
        Assert.True(newer.UpdateAvailable);
        Assert.False(newer.Dismissed);
        Assert.Equal("1.4.0", newer.DismissedVersion);
    }

    [Fact]
    public async Task ConcurrentManualRequestsAreRejectedInsteadOfQueued()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var client = new BlockingUpdateDiscoveryClient();
        var clock = new ManualUpdateScheduleClock(Now);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, checkExclusion: new IsolatedUpdateCheckExclusion());
        using var coordinator = new SupervisorUpdateCoordinator(
            SupervisorUpdatePolicy.Disabled,
            store,
            scheduler,
            clock,
            verificationConfigured: true);

        var first = coordinator.TryStartManualCheck(CancellationToken.None);
        await client.WaitForFirstCallAsync();
        var second = coordinator.TryStartManualCheck(CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.False(second.Accepted);
        Assert.True(second.AlreadyInProgress);
        Assert.Null(second.OperationId);
        Assert.Equal("already_running", second.ResultCode);
        Assert.Equal(1, client.CallCount);
        client.Release();
    }

    [Fact]
    public async Task CrossProcessContentionIsRejectedBeforeOperationStateOrNetworkWork()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var initialState = store.Load().State;
        var client = new FakeUpdateDiscoveryClient(UpdateDiscoveryCheckResult.Failure(
            UpdateErrorCategory.Http,
            "must_not_run"));
        var clock = new ManualUpdateScheduleClock(Now);
        var scheduler = new UpdateDiscoveryScheduler(
            store,
            client,
            clock,
            checkExclusion: new AlwaysBusyUpdateCheckExclusion());
        using var coordinator = new SupervisorUpdateCoordinator(
            SupervisorUpdatePolicy.Disabled,
            store,
            scheduler,
            clock,
            verificationConfigured: true);

        var acceptance = coordinator.TryStartManualCheck(CancellationToken.None);
        await Task.Delay(50);

        Assert.False(acceptance.Accepted);
        Assert.True(acceptance.AlreadyInProgress);
        Assert.Null(acceptance.OperationId);
        Assert.Equal("already_running", acceptance.ResultCode);
        Assert.Null(coordinator.GetStatus().Operation);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(initialState, store.Load().State);
    }

    [Fact]
    public void AcceptanceSetupFailureReleasesAdmissionLease()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var sharedExclusion = new SharedTestUpdateCheckExclusion();
        var admission = new UpdateCheckAdmission(sharedExclusion);
        var client = new FakeUpdateDiscoveryClient(UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.Http, "must_not_run"));
        var clock = new ManualUpdateScheduleClock(Now);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, admission: admission);
        using var coordinator = new SupervisorUpdateCoordinator(
            SupervisorUpdatePolicy.Disabled,
            store,
            scheduler,
            clock,
            verificationConfigured: true,
            diagnostics: new ThrowingAcceptedDiagnostics());

        Assert.Throws<InvalidOperationException>(() => coordinator.TryStartManualCheck(CancellationToken.None));
        using var recovered = admission.TryAcquire().Lease;

        Assert.NotNull(recovered);
    }

    [Fact]
    public async Task SuccessfulManualCheckUsesCanonicalResultCode()
    {
        using var fixture = CreateCoordinator(SupervisorUpdatePolicy.Disabled, verificationConfigured: true);

        var acceptance = fixture.Coordinator.TryStartManualCheck(CancellationToken.None);
        await WaitForTerminalAsync(fixture.Coordinator);
        var operation = fixture.Coordinator.GetStatus().Operation;

        Assert.True(acceptance.Accepted);
        Assert.NotNull(operation);
        Assert.Equal("current", operation!.ResultCode);
    }

    [Fact]
    public async Task MissingProductionTrustReportsStructuredUnavailableWithoutRequest()
    {
        using var fixture = CreateCoordinator(SupervisorUpdatePolicy.Disabled, verificationConfigured: false);

        var acceptance = fixture.Coordinator.TryStartManualCheck(CancellationToken.None);
        Assert.True(acceptance.Accepted);
        await WaitForTerminalAsync(fixture.Coordinator);
        var status = fixture.Coordinator.GetStatus();

        Assert.False(status.VerificationConfigured);
        Assert.False(status.Operation!.Success);
        Assert.Equal("verification_unavailable", status.Operation.ResultCode);
        Assert.Equal(0, fixture.Client.CallCount);
    }

    [Fact]
    public async Task ShutdownCancellationPublishesOneBoundedTerminalResult()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var client = new BlockingUpdateDiscoveryClient();
        var clock = new ManualUpdateScheduleClock(Now);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, checkExclusion: new IsolatedUpdateCheckExclusion());
        using var coordinator = new SupervisorUpdateCoordinator(SupervisorUpdatePolicy.Disabled, store, scheduler, clock, true);
        using var shutdown = new CancellationTokenSource();

        var acceptance = coordinator.TryStartManualCheck(shutdown.Token);
        await client.WaitForFirstCallAsync();
        shutdown.Cancel();
        await WaitForTerminalAsync(coordinator);

        var operation = coordinator.GetStatus().Operation;
        Assert.Equal(acceptance.OperationId, operation?.OperationId);
        Assert.Equal("cancelled", operation?.ResultCode);
        Assert.False(operation?.Success);
    }

    [Fact]
    public async Task ConfiguratorRunnerReturnsImmediatelyAndDeduplicatesDelayedManualClick()
    {
        var bridge = new DelayedConfiguratorBridge();
        var runner = new ConfiguratorUpdateCheckRunner(bridge, (_, _) => Task.CompletedTask);

        var first = runner.TryRunAsync(null, CancellationToken.None);
        await bridge.WaitForStartAsync();
        Assert.False(first.IsCompleted);

        var duplicate = await runner.TryRunAsync(null, CancellationToken.None);
        Assert.False(duplicate.Acceptance.Accepted);
        Assert.True(duplicate.Acceptance.AlreadyInProgress);
        Assert.Equal(1, bridge.StartCount);

        bridge.CompleteStart();
        var completed = await first;
        Assert.Equal("current version is latest", completed.Summary);
        Assert.Equal(1, bridge.StartCount);
    }

    [Fact]
    public void DisabledPolicyIsSynchronizedAndAutomaticDueRemainsFalse()
    {
        using var fixture = CreateCoordinator(SupervisorUpdatePolicy.Disabled, verificationConfigured: true);

        var status = fixture.Coordinator.GetStatus();

        Assert.Equal("Disabled", status.Policy);
        Assert.False(status.AutomaticCheckDue);
        Assert.Equal(UpdateCheckPolicy.Disabled, fixture.Store.Load().State.Policy);
    }

    private static async Task WaitForTerminalAsync(SupervisorUpdateCoordinator coordinator)
    {
        for (var i = 0; i < 100; i++)
        {
            if (coordinator.GetStatus().Operation?.CompletedAt is not null) return;
            await Task.Delay(10);
        }

        throw new TimeoutException("Update operation did not publish a terminal result.");
    }

    private static UpdateStateV1 VerifiedState(UpdateStateV1 state, string version) => state with
    {
        LatestVerifiedVersion = version,
        LatestVerifiedTag = "v" + version,
        LatestVerifiedReleaseUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v{version}"
    };

    private static CoordinatorFixture CreateCoordinator(SupervisorUpdatePolicy policy, bool verificationConfigured)
    {
        var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var client = new FakeUpdateDiscoveryClient(new UpdateDiscoveryCheckResult
        {
            Status = UpdateDiscoveryStatus.Current,
            ETag = "\"current\"",
            Version = AppVersion.Current,
            Tag = "v" + AppVersion.Current,
            ReleaseUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v{AppVersion.Current}",
            VerifiedManifest = null,
            ErrorCategory = null,
            ErrorCode = null
        });
        var clock = new ManualUpdateScheduleClock(Now);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, checkExclusion: new IsolatedUpdateCheckExclusion());
        var coordinator = new SupervisorUpdateCoordinator(policy, store, scheduler, clock, verificationConfigured);
        return new CoordinatorFixture(temp, store, client, coordinator);
    }

    private sealed class CoordinatorFixture(
        TempDirectory temp,
        UpdateStateStore store,
        FakeUpdateDiscoveryClient client,
        SupervisorUpdateCoordinator coordinator) : IDisposable
    {
        public UpdateStateStore Store { get; } = store;
        public FakeUpdateDiscoveryClient Client { get; } = client;
        public SupervisorUpdateCoordinator Coordinator { get; } = coordinator;

        public void Dispose()
        {
            Coordinator.Dispose();
            temp.Dispose();
        }
    }

    private sealed class DelayedConfiguratorBridge : IConfiguratorUpdateBridge
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCount { get; private set; }

        public async Task<UpdateActionAcceptance> StartCheckAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new UpdateActionAcceptance(true, false, "op-1", "accepted");
        }

        public Task<UpdateStatusSnapshotV1> QueryStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new UpdateStatusSnapshotV1(
                1, "Disabled", "Stable", AppVersion.Current, AppVersion.Current, false, false, null,
                Now, Now, null, null, true, false, false,
                new UpdateCheckOperationSnapshot("op-1", "completed", true, "current", "current version is latest", Now, Now)));

        public Task<UpdateActionAcceptance> DismissAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<UpdateActionAcceptance> ClearDismissalAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task WaitForStartAsync() => _started.Task;

        public void CompleteStart() => _release.TrySetResult();
    }

    private sealed class ThrowingAcceptedDiagnostics : IUpdateDiscoveryDiagnostics
    {
        public void Record(UpdateDiscoveryDiagnostic diagnostic)
        {
            if (diagnostic.Event == "accepted")
            {
                throw new InvalidOperationException("test acceptance setup failure");
            }
        }
    }

    private sealed class AlwaysBusyUpdateCheckExclusion : IUpdateCheckExclusion
    {
        public IUpdateCheckLease? TryAcquire() => null;
    }
}
