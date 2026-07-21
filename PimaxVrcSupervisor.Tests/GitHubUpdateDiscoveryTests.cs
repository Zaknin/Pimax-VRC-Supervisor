using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class GitHubUpdateDiscoveryTests
{
    [Fact]
    public async Task VerifiedNewerReleaseFetchesOnlyManifestAndSignature()
    {
        var scenario = DiscoveryScenario.Create();
        var transport = scenario.CreateSuccessfulTransport();
        var client = CreateClient(transport, scenario.Signed.TrustStore);

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.4.0", result.Version);
        Assert.NotNull(result.VerifiedManifest);
        Assert.EndsWith("-with-dotnet9.zip", result.VerifiedManifest!.SelectedPackage.FileName, StringComparison.Ordinal);
        Assert.Equal(3, transport.Requests.Count);
        Assert.Equal(GitHubUpdateEndpointPolicy.LatestReleaseUri, transport.Requests[0].Uri);
        Assert.EndsWith("update-manifest-v1.json", transport.Requests[1].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("update-manifest-v1.signatures.json", transport.Requests[2].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain(transport.Requests, request => request.Uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        Assert.All(transport.Requests, request =>
        {
            Assert.Equal("PimaxVrcSupervisor-UpdateDiscovery/1", request.UserAgent);
            Assert.Null(request.Authorization);
        });
    }

    [Fact]
    public async Task UnknownManifestKeyFailsClosed()
    {
        var scenario = DiscoveryScenario.Create();
        var client = CreateClient(scenario.CreateSuccessfulTransport(), UpdateTrustStore.Empty);

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, result.Status);
        Assert.Equal(UpdateErrorCategory.Signature, result.ErrorCategory);
        Assert.Equal("unknown_key", result.ErrorCode);
    }

    [Fact]
    public async Task AlteredManifestBytesFailExactByteVerification()
    {
        var scenario = DiscoveryScenario.Create();
        var alteredManifest = scenario.Signed.ManifestBytes.Concat([(byte)' ']).ToArray();
        var release = scenario.CreateReleaseMetadata(manifestBytes: alteredManifest);
        var transport = new FakeUpdateHttpTransport(
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, release, etag: "\"release-1\""),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, alteredManifest),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, scenario.Signed.SignatureEnvelopeBytes));
        var client = CreateClient(transport, scenario.Signed.TrustStore);

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, result.Status);
        Assert.Equal(UpdateErrorCategory.Signature, result.ErrorCategory);
        Assert.Equal("manifest_hash", result.ErrorCode);
    }

    [Fact]
    public async Task MalformedDerSignatureFailsClosed()
    {
        var scenario = DiscoveryScenario.Create();
        var envelope = JsonNode.Parse(scenario.Signed.SignatureEnvelopeBytes)!.AsObject();
        envelope["signatures"]![0]!["signatureBase64"] = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]);
        var signatureBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var release = scenario.CreateReleaseMetadata(signatureBytes: signatureBytes);
        var transport = new FakeUpdateHttpTransport(
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, release),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, scenario.Signed.ManifestBytes),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, signatureBytes));
        var client = CreateClient(transport, scenario.Signed.TrustStore);

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, result.Status);
        Assert.Equal(UpdateErrorCategory.Signature, result.ErrorCategory);
        Assert.Contains(result.ErrorCode, new[] { "signature_invalid", "signature_der" });
    }

    [Theory]
    [InlineData(true, false, "1.4.0")]
    [InlineData(false, true, "1.4.0-beta.1")]
    public async Task DraftsAndPrereleasesAreIgnoredWithoutAssetRequests(bool draft, bool prerelease, string version)
    {
        var scenario = DiscoveryScenario.Create();
        var release = scenario.CreateReleaseMetadata(draft: draft, prerelease: prerelease, version: version);
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, release));
        var client = CreateClient(transport, scenario.Signed.TrustStore);

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Ignored, result.Status);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task CurrentReleaseDoesNotFetchAssets()
    {
        var scenario = DiscoveryScenario.Create();
        var release = scenario.CreateReleaseMetadata(version: "1.4.0");
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, release));
        var client = CreateClient(transport, scenario.Signed.TrustStore, installedVersion: "1.4.0");

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Current, result.Status);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task MutableReleaseFailsBeforeAssetRequests()
    {
        var scenario = DiscoveryScenario.Create();
        var release = scenario.CreateReleaseMetadata(immutable: false);
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, release));
        var client = CreateClient(transport, scenario.Signed.TrustStore);

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, result.Status);
        Assert.Equal("release_mutable", result.ErrorCode);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task ContentLengthOverLimitIsRejectedBeforeBodyRead()
    {
        var content = new TrackingHttpContent(new byte[] { 1 }, declaredLength: 300_000);
        var transport = new FakeUpdateHttpTransport(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var client = CreateClient(transport, UpdateTrustStore.Empty);

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, result.Status);
        Assert.Equal("response_too_large", result.ErrorCode);
        Assert.False(content.ReadStarted);
    }

    [Fact]
    public async Task StreamingBodyOverLimitIsRejected()
    {
        var oversized = new byte[256 * 1024 + 1];
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, oversized, setContentLength: false));
        var client = CreateClient(transport, UpdateTrustStore.Empty);

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, result.Status);
        Assert.Equal("response_too_large", result.ErrorCode);
    }

    [Fact]
    public async Task GitHubAssetRedirectIsAllowedButArbitraryHostIsRejected()
    {
        var scenario = DiscoveryScenario.Create();
        var allowedLocation = new Uri("https://release-assets.githubusercontent.com/example/manifest?token=bounded");
        var allowed = new FakeUpdateHttpTransport(
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, scenario.ReleaseMetadata),
            _ => FakeUpdateHttpTransport.Redirect(allowedLocation),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, scenario.Signed.ManifestBytes),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, scenario.Signed.SignatureEnvelopeBytes));
        var allowedResult = await CreateClient(allowed, scenario.Signed.TrustStore).CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.UpdateAvailable, allowedResult.Status);
        Assert.Equal(allowedLocation, allowed.Requests[2].Uri);

        var rejected = new FakeUpdateHttpTransport(
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, scenario.ReleaseMetadata),
            _ => FakeUpdateHttpTransport.Redirect(new Uri("https://example.com/manifest")));
        var rejectedResult = await CreateClient(rejected, scenario.Signed.TrustStore).CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, rejectedResult.Status);
        Assert.Equal("redirect_rejected", rejectedResult.ErrorCode);
        Assert.Equal(2, rejected.Requests.Count);
    }

    [Fact]
    public async Task RequestTimeoutReturnsStructuredFailure()
    {
        var transport = new FakeUpdateHttpTransport(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        });
        var client = CreateClient(
            transport,
            UpdateTrustStore.Empty,
            requestTimeout: TimeSpan.FromMilliseconds(20));

        var result = await client.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.Failed, result.Status);
        Assert.Equal(UpdateErrorCategory.Timeout, result.ErrorCategory);
        Assert.Equal("request_timeout", result.ErrorCode);
    }

    [Fact]
    public async Task CallerCancellationReturnsCancelledResult()
    {
        var transport = new FakeUpdateHttpTransport(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        });
        var client = CreateClient(transport, UpdateTrustStore.Empty);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await client.CheckAsync(null, cancellation.Token);

        Assert.Equal(UpdateDiscoveryStatus.Cancelled, result.Status);
        Assert.Equal("cancelled", result.ErrorCode);
    }

    [Fact]
    public async Task ConditionalEtagAndNotModifiedUseOneMetadataRequest()
    {
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(
            HttpStatusCode.NotModified,
            [],
            etag: "\"etag-2\""));
        var client = CreateClient(transport, UpdateTrustStore.Empty);

        var result = await client.CheckAsync("\"etag-1\"", CancellationToken.None);

        Assert.Equal(UpdateDiscoveryStatus.NotModified, result.Status);
        Assert.Equal("\"etag-2\"", result.ETag);
        Assert.Equal("\"etag-1\"", Assert.Single(transport.Requests).IfNoneMatch);
    }

    [Fact]
    public void UpdateDiscoverySurfaceContainsNoPackageInstallationOrExecutionApi()
    {
        var forbiddenNames = new[] { "DownloadPackage", "Install", "Stage", "ExecutePackage", "Extract" };
        var methods = new[]
            {
                typeof(GitHubUpdateDiscoveryClient),
                typeof(UpdateDiscoveryScheduler),
                typeof(UpdateStateStore)
            }
            .SelectMany(type => type.GetMethods(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic));

        Assert.DoesNotContain(methods, method => forbiddenNames.Any(name => method.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    private static GitHubUpdateDiscoveryClient CreateClient(
        IUpdateHttpTransport transport,
        UpdateTrustStore trustStore,
        string installedVersion = "1.3.1",
        TimeSpan? requestTimeout = null)
        => new(
            transport,
            trustStore,
            new UpdateDiscoveryOptions
            {
                InstalledVersion = SemanticVersion.Parse(installedVersion),
                InstalledVariant = UpdatePackageVariant.WithDotnet9,
                RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2)
            });
}

internal sealed class DiscoveryScenario
{
    internal DiscoveryScenario(SignedUpdateTestData signed)
    {
        Signed = signed;
        ReleaseMetadata = CreateReleaseMetadata();
    }

    public SignedUpdateTestData Signed { get; }

    public byte[] ReleaseMetadata { get; }

    public static DiscoveryScenario Create()
        => new(UpdateContractTestData.Sign(UpdateContractTestData.CreateManifest()));

    public FakeUpdateHttpTransport CreateSuccessfulTransport()
        => new(
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, ReleaseMetadata, etag: "\"release-1\""),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, Signed.ManifestBytes),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, Signed.SignatureEnvelopeBytes));

    public byte[] CreateReleaseMetadata(
        bool draft = false,
        bool prerelease = false,
        bool immutable = true,
        string version = "1.4.0",
        byte[]? manifestBytes = null,
        byte[]? signatureBytes = null)
    {
        manifestBytes ??= Signed.ManifestBytes;
        signatureBytes ??= Signed.SignatureEnvelopeBytes;
        var tag = "v" + version;
        var manifest = JsonNode.Parse(Signed.ManifestBytes)!.AsObject();
        var assets = new JsonArray();
        foreach (var package in manifest["assets"]!.AsArray())
        {
            var packageObject = package!.AsObject();
            AddAsset(
                assets,
                tag,
                packageObject["fileName"]!.GetValue<string>(),
                packageObject["sizeBytes"]!.GetValue<long>(),
                packageObject["sha256"]!.GetValue<string>());
            foreach (var companionName in new[] { "checksumFile", "sigstoreBundleFile", "attestationBundleFile" })
            {
                var companion = packageObject[companionName]!.AsObject();
                AddAsset(
                    assets,
                    tag,
                    companion["fileName"]!.GetValue<string>(),
                    100,
                    companion["sha256"]!.GetValue<string>());
            }
        }

        var manifestName = UpdateManifestConstants.ManifestFileName(SemanticVersion.Parse("1.4.0"));
        AddAsset(assets, tag, manifestName, manifestBytes.LongLength, Sha256(manifestBytes));
        AddAsset(
            assets,
            tag,
            UpdateManifestConstants.SignatureFileName(SemanticVersion.Parse("1.4.0")),
            signatureBytes.LongLength,
            Sha256(signatureBytes));
        AddAsset(assets, tag, manifestName + ".sha256", 100, new string('1', 64));
        AddAsset(assets, tag, manifestName + ".sigstore.json", 100, new string('2', 64));
        AddAsset(assets, tag, manifestName + ".attestation.json", 100, new string('3', 64));

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = tag,
            html_url = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/{tag}",
            draft,
            prerelease,
            immutable,
            assets
        });
    }

    private static void AddAsset(JsonArray assets, string tag, string name, long size, string digest)
        => assets.Add(new JsonObject
        {
            ["name"] = name,
            ["size"] = size,
            ["digest"] = "sha256:" + digest,
            ["browser_download_url"] = $"https://github.com/{UpdateManifestConstants.Repository}/releases/download/{tag}/{name}"
        });

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed class FakeUpdateHttpTransport : IUpdateHttpTransport
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses;

    public FakeUpdateHttpTransport(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : this(responses.Select<Func<HttpRequestMessage, HttpResponseMessage>, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(
            response => (request, _) => Task.FromResult(response(request))).ToArray())
    {
    }

    public FakeUpdateHttpTransport(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses)
    {
        _responses = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(responses);
    }

    public List<CapturedUpdateRequest> Requests { get; } = [];

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedUpdateRequest(
            request.RequestUri!,
            request.Headers.UserAgent.ToString(),
            request.Headers.Authorization?.ToString(),
            request.Headers.IfNoneMatch.SingleOrDefault()?.ToString()));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("The fake update transport received an unexpected request.");
        }

        return _responses.Dequeue()(request, cancellationToken);
    }

    public static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        byte[] body,
        string? etag = null,
        bool setContentLength = true)
    {
        HttpContent content = setContentLength
            ? new ByteArrayContent(body)
            : new StreamContent(new MemoryStream(body));
        if (!setContentLength)
        {
            content.Headers.ContentLength = null;
        }

        var response = new HttpResponseMessage(statusCode) { Content = content };
        if (etag is not null)
        {
            response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        }

        return response;
    }

    public static HttpResponseMessage Redirect(Uri location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = location;
        return response;
    }
}

internal sealed record CapturedUpdateRequest(
    Uri Uri,
    string UserAgent,
    string? Authorization,
    string? IfNoneMatch);

internal sealed class TrackingHttpContent : HttpContent
{
    private readonly byte[] _bytes;

    public TrackingHttpContent(byte[] bytes, long declaredLength)
    {
        _bytes = bytes;
        Headers.ContentLength = declaredLength;
    }

    public bool ReadStarted { get; private set; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        ReadStarted = true;
        return stream.WriteAsync(_bytes).AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = Headers.ContentLength ?? _bytes.Length;
        return true;
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        ReadStarted = true;
        return Task.FromResult<Stream>(new MemoryStream(_bytes));
    }
}
