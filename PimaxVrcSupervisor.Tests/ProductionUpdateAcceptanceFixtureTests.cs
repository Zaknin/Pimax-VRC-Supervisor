using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class ProductionUpdateAcceptanceFixtureTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<UpdateAcceptanceState> NetworkFixtureStates => new()
    {
        UpdateAcceptanceState.CurrentVersionLatest,
        UpdateAcceptanceState.VerifiedStableAvailable,
        UpdateAcceptanceState.AlteredManifestBytes,
        UpdateAcceptanceState.InvalidSignature,
        UpdateAcceptanceState.UnknownKeyId,
        UpdateAcceptanceState.WrongRepository,
        UpdateAcceptanceState.DraftOrPrerelease,
        UpdateAcceptanceState.TimeoutOrUnavailable,
        UpdateAcceptanceState.Http304Cached,
        UpdateAcceptanceState.UnsupportedManifestSchema
    };

    [Fact]
    public void ExactOperatorSignedManifestVerifiesWithApprovedProductionKey()
    {
        var fixture = LoadProductionFixture();

        var withRuntime = UpdateManifestVerifier.VerifyAndParse(
            fixture.ManifestBytes,
            fixture.SignatureEnvelopeBytes,
            fixture.TrustStore,
            UpdatePackageVariant.WithDotnet9);
        var withoutRuntime = UpdateManifestVerifier.VerifyAndParse(
            fixture.ManifestBytes,
            fixture.SignatureEnvelopeBytes,
            fixture.TrustStore,
            UpdatePackageVariant.NoDotnet9);

        Assert.Equal("1.4.0", withRuntime.Version.ToString());
        Assert.EndsWith("-with-dotnet9.zip", withRuntime.SelectedPackage.FileName, StringComparison.Ordinal);
        Assert.EndsWith("-no-dotnet9.zip", withoutRuntime.SelectedPackage.FileName, StringComparison.Ordinal);
        Assert.Equal("8bedf3676d17427bb1cf1bcc9b4b2097b3a161ed08208ee2d92cddded4b10472", withRuntime.ManifestSha256);
    }

    [Fact]
    public async Task OnlyTheEmbeddedProductionSignaturePathMintsDownloadAuthority()
    {
        var productionFixture = LoadProductionFixture();
        var productionScenario = new DiscoveryScenario(productionFixture);
        using var productionClient = CreateClient(productionScenario.CreateSuccessfulTransport(), productionFixture.TrustStore);

        var productionResult = await productionClient.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.UpdateAvailable, productionResult.Status);
        Assert.True(GitHubUpdateDiscoveryClient.TryGetVerifiedPackageAuthority(productionResult.VerifiedPackageAuthority, out var productionPackage));
        Assert.Equal(
            $"https://github.com/{UpdateManifestConstants.Repository}/releases/download/v1.4.0/PimaxVrcSupervisor-v1.4.0-win-x64-no-dotnet9.zip",
            productionPackage.BrowserDownloadUri.AbsoluteUri);

        var testSigned = UpdateContractTestData.Sign(UpdateContractTestData.CreateManifest());
        var testSignedFailure = Assert.Throws<UpdateContractException>(() => UpdateManifestVerifier.VerifyAndParse(
            testSigned.ManifestBytes,
            testSigned.SignatureEnvelopeBytes,
            ProductionUpdateTrustRoots.CreateTrustStore(),
            UpdatePackageVariant.NoDotnet9));

        Assert.Equal("unknown_key", testSignedFailure.Code);
    }

    [Fact]
    public void OneByteMutationOfOperatorSignedManifestFailsClosed()
    {
        var fixture = LoadProductionFixture();
        var altered = fixture.ManifestBytes.ToArray();
        altered[^1] ^= 1;

        var error = Assert.Throws<UpdateContractException>(() => UpdateManifestVerifier.VerifyAndParse(
            altered,
            fixture.SignatureEnvelopeBytes,
            fixture.TrustStore,
            UpdatePackageVariant.NoDotnet9));

        Assert.Equal("manifest_hash", error.Code);
    }

    [Theory]
    [MemberData(nameof(NetworkFixtureStates))]
    public async Task BoundedAcceptanceNetworkStateProducesExpectedResult(UpdateAcceptanceState state)
    {
        var fixture = LoadProductionFixture();
        var productionScenario = new DiscoveryScenario(fixture);
        var (client, transport, expectedStatus, expectedCode) = CreateNetworkState(state, productionScenario);
        using (client)
        {
            var result = await client.CheckAsync(state == UpdateAcceptanceState.Http304Cached ? "\"fixture-etag\"" : null, CancellationToken.None);

            Assert.Equal(expectedStatus, result.Status);
            Assert.Equal(expectedCode, result.ErrorCode);
            if (result.Status != UpdateDiscoveryStatus.UpdateAvailable)
            {
                Assert.Null(result.VerifiedManifest);
            }
            if (transport is not null)
            {
                Assert.DoesNotContain(transport.Requests, request => request.Uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public async Task VerifiedProductionFixtureCreatesCachedAvailabilityAndInvalidDataDoesNot()
    {
        var fixture = LoadProductionFixture();
        var scenario = new DiscoveryScenario(fixture);
        using var validDirectory = new TempDirectory();
        var validStore = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, validDirectory.Path);
        var validTransport = scenario.CreateSuccessfulTransport();
        using var validClient = CreateClient(validTransport, fixture.TrustStore);
        var validScheduler = new UpdateDiscoveryScheduler(validStore, validClient, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());

        var validResult = await validScheduler.CheckManuallyAsync(CancellationToken.None);
        var validState = validStore.Load().State;

        Assert.Equal(UpdateDiscoveryStatus.UpdateAvailable, validResult.Status);
        Assert.Equal("1.4.0", validState.LatestVerifiedVersion);
        Assert.Equal("8bedf3676d17427bb1cf1bcc9b4b2097b3a161ed08208ee2d92cddded4b10472", validState.LastManifestSha256);

        using var invalidDirectory = new TempDirectory();
        var invalidStore = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, invalidDirectory.Path);
        var alteredManifest = fixture.ManifestBytes.Concat([(byte)' ']).ToArray();
        var invalidTransport = new FakeUpdateHttpTransport(
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, scenario.CreateReleaseMetadata(manifestBytes: alteredManifest)),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, alteredManifest),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, fixture.SignatureEnvelopeBytes));
        using var invalidClient = CreateClient(invalidTransport, fixture.TrustStore);
        var invalidScheduler = new UpdateDiscoveryScheduler(invalidStore, invalidClient, new ManualUpdateScheduleClock(Now), checkExclusion: new IsolatedUpdateCheckExclusion());

        var invalidResult = await invalidScheduler.CheckManuallyAsync(CancellationToken.None);
        var invalidState = invalidStore.Load().State;

        Assert.Equal(UpdateDiscoveryStatus.Failed, invalidResult.Status);
        Assert.Null(invalidState.LatestVerifiedVersion);
        Assert.Null(invalidState.LatestVerifiedTag);
        Assert.Null(invalidState.LatestVerifiedReleaseUrl);
    }

    [Fact]
    public async Task ConfiguratorManualCheckUnderDisabledReturnsStructuredVerifiedResultWithoutBlockingCaller()
    {
        var fixture = LoadProductionFixture();
        var scenario = new DiscoveryScenario(fixture);
        using var directory = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, directory.Path);
        var transport = scenario.CreateSuccessfulTransport();
        using var client = CreateClient(transport, fixture.TrustStore);
        var clock = new ManualUpdateScheduleClock(Now);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, checkExclusion: new IsolatedUpdateCheckExclusion());
        using var coordinator = new SupervisorUpdateCoordinator(
            SupervisorUpdatePolicy.Disabled,
            store,
            scheduler,
            clock,
            verificationConfigured: true);
        var stopwatch = Stopwatch.StartNew();

        var acceptance = coordinator.TryStartManualCheck(CancellationToken.None);
        stopwatch.Stop();
        await WaitForTerminalAsync(coordinator);
        var status = coordinator.GetStatus();

        Assert.True(acceptance.Accepted);
        Assert.False(acceptance.AlreadyInProgress);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal("Disabled", status.Policy);
        Assert.Equal("Stable", status.Channel);
        Assert.Equal(AppVersion.Current, status.CurrentVersion);
        Assert.Equal("1.4.0", status.LatestVerifiedVersion);
        Assert.True(status.UpdateAvailable);
        Assert.True(status.Operation?.Success);
        Assert.Equal("update_available", status.Operation?.ResultCode);
        Assert.Contains("Verified stable update 1.4.0", status.Operation?.ResultSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(transport.Requests, request => request.Uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DismissedCandidateIsSuppressedAndNewerCandidateResurfaces()
    {
        Assert.Equal(12, Enum.GetValues<UpdateAcceptanceState>().Length);
        using var directory = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, directory.Path);
        var initial = store.Load().State with
        {
            LatestVerifiedVersion = "1.4.0",
            LatestVerifiedTag = "v1.4.0",
            LatestVerifiedReleaseUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v1.4.0"
        };
        await store.SaveAsync(initial, CancellationToken.None);
        var clock = new ManualUpdateScheduleClock(Now);
        var client = new FakeUpdateDiscoveryClient(UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.Http, "not_used"));
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, checkExclusion: new IsolatedUpdateCheckExclusion());
        using var coordinator = new SupervisorUpdateCoordinator(SupervisorUpdatePolicy.Disabled, store, scheduler, clock, true);

        var dismissal = await coordinator.DismissAsync(CancellationToken.None);
        var dismissed = coordinator.GetStatus();
        Assert.True(dismissal.Accepted);
        Assert.True(dismissed.UpdateAvailable);
        Assert.True(dismissed.Dismissed);

        var newer = store.Load().State with
        {
            LatestVerifiedVersion = "1.5.0",
            LatestVerifiedTag = "v1.5.0",
            LatestVerifiedReleaseUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v1.5.0"
        };
        await store.SaveAsync(newer, CancellationToken.None);

        Assert.True(coordinator.GetStatus().UpdateAvailable);
        Assert.False(coordinator.GetStatus().Dismissed);
        Assert.Equal(0, client.CallCount);
    }

    private static (GitHubUpdateDiscoveryClient Client, FakeUpdateHttpTransport? Transport, UpdateDiscoveryStatus Status, string? ErrorCode)
        CreateNetworkState(UpdateAcceptanceState state, DiscoveryScenario productionScenario)
    {
        var trustStore = productionScenario.Signed.TrustStore;
        switch (state)
        {
            case UpdateAcceptanceState.CurrentVersionLatest:
                {
                    var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(
                        HttpStatusCode.OK,
                        productionScenario.CreateReleaseMetadata(version: AppVersion.Current)));
                    return (CreateClient(transport, trustStore), transport, UpdateDiscoveryStatus.Current, null);
                }
            case UpdateAcceptanceState.VerifiedStableAvailable:
                {
                    var transport = productionScenario.CreateSuccessfulTransport();
                    return (CreateClient(transport, trustStore), transport, UpdateDiscoveryStatus.UpdateAvailable, null);
                }
            case UpdateAcceptanceState.AlteredManifestBytes:
                {
                    var altered = productionScenario.Signed.ManifestBytes.Concat([(byte)' ']).ToArray();
                    var transport = new FakeUpdateHttpTransport(
                        _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, productionScenario.CreateReleaseMetadata(manifestBytes: altered)),
                        _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, altered),
                        _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, productionScenario.Signed.SignatureEnvelopeBytes));
                    return (CreateClient(transport, trustStore), transport, UpdateDiscoveryStatus.Failed, "manifest_hash");
                }
            case UpdateAcceptanceState.InvalidSignature:
                {
                    var signature = MutateEnvelope(productionScenario.Signed.SignatureEnvelopeBytes, envelope =>
                    {
                        var signatureBytes = Convert.FromBase64String(envelope["signatures"]![0]!["signatureBase64"]!.GetValue<string>());
                        signatureBytes[^1] ^= 1;
                        envelope["signatures"]![0]!["signatureBase64"] = Convert.ToBase64String(signatureBytes);
                    });
                    var transport = new FakeUpdateHttpTransport(
                        _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, productionScenario.CreateReleaseMetadata(signatureBytes: signature)),
                        _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, productionScenario.Signed.ManifestBytes),
                        _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, signature));
                    return (CreateClient(transport, trustStore), transport, UpdateDiscoveryStatus.Failed, "signature_invalid");
                }
            case UpdateAcceptanceState.UnknownKeyId:
                {
                    var signature = MutateEnvelope(productionScenario.Signed.SignatureEnvelopeBytes, envelope =>
                        envelope["signatures"]![0]!["keyId"] = "unknown-production-key");
                    var transport = new FakeUpdateHttpTransport(
                        _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, productionScenario.CreateReleaseMetadata(signatureBytes: signature)),
                        _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, productionScenario.Signed.ManifestBytes),
                        _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, signature));
                    return (CreateClient(transport, trustStore), transport, UpdateDiscoveryStatus.Failed, "unknown_key");
                }
            case UpdateAcceptanceState.WrongRepository:
                {
                    var manifest = UpdateContractTestData.CreateManifest();
                    manifest["repository"] = "Other/Repository";
                    var scenario = new DiscoveryScenario(UpdateContractTestData.Sign(manifest));
                    var transport = scenario.CreateSuccessfulTransport();
                    return (CreateClient(transport, scenario.Signed.TrustStore), transport, UpdateDiscoveryStatus.Failed, "repository");
                }
            case UpdateAcceptanceState.DraftOrPrerelease:
                {
                    var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(
                        HttpStatusCode.OK,
                        productionScenario.CreateReleaseMetadata(draft: true)));
                    return (CreateClient(transport, trustStore), transport, UpdateDiscoveryStatus.Ignored, null);
                }
            case UpdateAcceptanceState.TimeoutOrUnavailable:
                {
                    var transport = new FakeUpdateHttpTransport(async (_, cancellationToken) =>
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        throw new InvalidOperationException();
                    });
                    return (CreateClient(transport, trustStore, TimeSpan.FromMilliseconds(20)), transport, UpdateDiscoveryStatus.Failed, "request_timeout");
                }
            case UpdateAcceptanceState.Http304Cached:
                {
                    var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.NotModified, [], etag: "\"fixture-etag\""));
                    return (CreateClient(transport, trustStore), transport, UpdateDiscoveryStatus.NotModified, null);
                }
            case UpdateAcceptanceState.UnsupportedManifestSchema:
                {
                    var manifest = UpdateContractTestData.CreateManifest();
                    manifest["schemaVersion"] = 2;
                    var scenario = new DiscoveryScenario(UpdateContractTestData.Sign(manifest));
                    var transport = scenario.CreateSuccessfulTransport();
                    return (CreateClient(transport, scenario.Signed.TrustStore), transport, UpdateDiscoveryStatus.Failed, "manifest_schema");
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    private static byte[] MutateEnvelope(byte[] bytes, Action<JsonObject> mutation)
    {
        var envelope = JsonNode.Parse(bytes)!.AsObject();
        mutation(envelope);
        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }

    private static SignedUpdateTestData LoadProductionFixture()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "UpdateAcceptance");
        return new SignedUpdateTestData(
            File.ReadAllBytes(Path.Combine(root, "PimaxVrcSupervisor-v1.4.0-update-manifest-v1.json")),
            File.ReadAllBytes(Path.Combine(root, "PimaxVrcSupervisor-v1.4.0-update-manifest-v1.signatures.json")),
            ProductionUpdateTrustRoots.CreateTrustStore());
    }

    private static GitHubUpdateDiscoveryClient CreateClient(
        IUpdateHttpTransport transport,
        UpdateTrustStore trustStore,
        TimeSpan? timeout = null)
        => TestOnlyDiscoveryHarness.Create(
            transport,
            trustStore,
            new UpdateDiscoveryOptions
            {
                InstalledVersion = SemanticVersion.Parse(AppVersion.Current),
                InstalledVariant = UpdatePackageVariant.NoDotnet9,
                RequestTimeout = timeout ?? TimeSpan.FromSeconds(2)
            });

    private static async Task WaitForTerminalAsync(SupervisorUpdateCoordinator coordinator)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (coordinator.GetStatus().Operation?.CompletedAt is not null)
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("The acceptance update check did not publish a terminal result.");
    }
}

public enum UpdateAcceptanceState
{
    CurrentVersionLatest,
    VerifiedStableAvailable,
    CandidateDismissed,
    NewerVersionSupersedesOlderDismissal,
    AlteredManifestBytes,
    InvalidSignature,
    UnknownKeyId,
    WrongRepository,
    DraftOrPrerelease,
    TimeoutOrUnavailable,
    Http304Cached,
    UnsupportedManifestSchema
}
