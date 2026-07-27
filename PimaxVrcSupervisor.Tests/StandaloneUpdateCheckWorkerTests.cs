using System.Text;
using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class StandaloneUpdateCheckWorkerTests
{
    [Fact]
    public void OneShotCommandBranchesBeforeNormalSupervisorInitialization()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "Program.cs"));
        var command = source.IndexOf("StandaloneUpdateCheckCommand.IsRequested", StringComparison.Ordinal);

        Assert.True(command >= 0, "The one-shot update command is not implemented.");
        Assert.True(command < source.IndexOf("SupervisorConsoleLog.Install", StringComparison.Ordinal));
        Assert.True(command < source.IndexOf("StartupExecutionContext.Parse", StringComparison.Ordinal));
        Assert.True(command < source.IndexOf("SupervisorConfig.Load", StringComparison.Ordinal));
        Assert.True(command < source.IndexOf("new AppSupervisor", StringComparison.Ordinal));
    }

    [Fact]
    public void OneShotCommandAcceptsOnlyTheFixedConfiguratorArguments()
    {
        Assert.True(StandaloneUpdateCheckCommand.HasExactArguments(
            ["--update-check-once", "--source", "configurator"]));
        Assert.False(StandaloneUpdateCheckCommand.HasExactArguments(
            ["--update-check-once", "--source", "configurator", "--config", "other.json"]));
        Assert.False(StandaloneUpdateCheckCommand.HasExactArguments(
            ["--update-check-once", "--source", "other"]));
        Assert.False(StandaloneUpdateCheckCommand.HasExactArguments(
            ["--update-check-once", "--endpoint", "https://example.invalid"]));
    }

    [Fact]
    public void WorkerSourceHasNoSupervisorLifecycleOrProcessActionSurface()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor",
            "StandaloneUpdateCheckWorker.cs"));

        foreach (var forbidden in new[]
                 {
                     "AppSupervisor",
                     "SteamVrLifecycle",
                     "PimaxConnectivity",
                     "BaseStation",
                     "MonitorLayout",
                     "ManagedApplication",
                     "SupervisorCommandServer",
                     "StartupIntegration",
                     "AutoLaunchWatcher",
                     "TerminalUi",
                     "Overlay",
                     "Process.Start",
                     "ProcessStartInfo",
                     "ZipFile",
                     "ExtractToDirectory",
                     "packageDownload",
                     "packageStaging",
                     "packageInstallation"
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SuccessfulSignedCheckUpdatesCachedStateAndReturnsBoundedStatus()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, temp.Path);
        var signed = UpdateContractTestData.Sign(UpdateContractTestData.CreateManifest());
        var verified = UpdateManifestVerifier.VerifyAndParse(
            signed.ManifestBytes,
            signed.SignatureEnvelopeBytes,
            signed.TrustStore,
            UpdatePackageVariant.NoDotnet9);
        var discovery = new UpdateDiscoveryCheckResult
        {
            Status = UpdateDiscoveryStatus.UpdateAvailable,
            ETag = "\"worker\"",
            Version = verified.Version.ToString(),
            Tag = verified.Manifest.Release.Tag,
            ReleaseUrl = verified.Manifest.Release.ReleaseUrl,
            VerifiedManifest = verified,
            ErrorCategory = null,
            ErrorCode = null
        };
        var worker = CreateWorker(store, discovery);

        var result = await worker.RunAsync(CancellationToken.None);
        var state = store.Load().State;

        Assert.True(result.Success);
        Assert.Equal("update_available", result.ResultCode);
        Assert.Equal("1.4.0", result.LatestVerifiedVersion);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.4.0", state.LatestVerifiedVersion);
        Assert.Equal(verified.ManifestSha256, state.LastManifestSha256);
        Assert.NotNull(result.Status?.LastSuccessfulCheckAt);
    }

    [Theory]
    [InlineData("release_mutable", "ReleaseMismatch")]
    [InlineData("signature_invalid", "Signature")]
    public async Task VerificationFailuresRemainFailClosed(string code, string categoryName)
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, temp.Path);
        var category = Enum.Parse<UpdateErrorCategory>(categoryName);
        var worker = CreateWorker(store, UpdateDiscoveryCheckResult.Failure(category, code));

        var result = await worker.RunAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(code, result.ResultCode);
        Assert.Null(result.LatestVerifiedVersion);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(code, store.Load().State.LastError?.Code);
    }

    [Fact]
    public void WorkerJsonIsOneBoundedVersionedResultWithoutRemoteOrSignatureContent()
    {
        var json = StandaloneUpdateCheckJson.Serialize(StandaloneUpdateCheckCommand.Failed(
            "release_mutable",
            "The published release is mutable."));

        Assert.True(Encoding.UTF8.GetByteCount(json) <= StandaloneUpdateCheckJson.MaximumOutputBytes);
        Assert.DoesNotContain('\n', json);
        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"operation\":\"update-check-once\"", json, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "manifest", "signature", "releaseUrl", "private", "token", "credential", "stack" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task MissingVerificationTrustPersistsBoundedFailureWithoutNetworkRequest()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, temp.Path);
        var client = new FakeUpdateDiscoveryClient(UpdateDiscoveryCheckResult.Failure(
            UpdateErrorCategory.Http,
            "must_not_run"));
        var clock = new ManualUpdateScheduleClock(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, checkExclusion: new IsolatedUpdateCheckExclusion());
        var worker = new StandaloneUpdateCheckWorker(store, scheduler, clock, verificationConfigured: false);

        var result = await worker.RunAsync(CancellationToken.None);
        var state = store.Load().State;

        Assert.False(result.Success);
        Assert.Equal("verification_unavailable", result.ResultCode);
        Assert.Equal(0, client.CallCount);
        Assert.Equal("verification_unavailable", state.LastError?.Code);
        Assert.Equal(clock.UtcNow, state.LastAttemptUtc);
    }

    [Fact]
    public async Task ContendedWorkerDoesNotMutateStateEvenWhenVerificationIsUnavailable()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, temp.Path);
        var initialState = store.Load().State;
        var client = new FakeUpdateDiscoveryClient(UpdateDiscoveryCheckResult.Failure(
            UpdateErrorCategory.Http,
            "must_not_run"));
        var clock = new ManualUpdateScheduleClock(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));
        var scheduler = new UpdateDiscoveryScheduler(
            store,
            client,
            clock,
            checkExclusion: new AlwaysBusyUpdateCheckExclusion());
        var worker = new StandaloneUpdateCheckWorker(store, scheduler, clock, verificationConfigured: false);

        var result = await worker.RunAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("already_running", result.ResultCode);
        Assert.Null(result.Status);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(initialState, store.Load().State);
    }

    private static StandaloneUpdateCheckWorker CreateWorker(
        UpdateStateStore store,
        UpdateDiscoveryCheckResult result)
    {
        var clock = new ManualUpdateScheduleClock(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));
        var scheduler = new UpdateDiscoveryScheduler(store, new FakeUpdateDiscoveryClient(result), clock, checkExclusion: new IsolatedUpdateCheckExclusion());
        return new StandaloneUpdateCheckWorker(store, scheduler, clock, verificationConfigured: true);
    }

    private sealed class AlwaysBusyUpdateCheckExclusion : IUpdateCheckExclusion
    {
        public IUpdateCheckLease? TryAcquire() => null;
    }

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PimaxVrcSupervisor.Tests", "PimaxVrcSupervisor.Tests.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
