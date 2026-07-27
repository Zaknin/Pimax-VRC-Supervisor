using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace PimaxVrcSupervisor.Updates;

internal interface IUpdateHttpTransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

internal sealed class GitHubUpdateHttpTransport : IUpdateHttpTransport, IDisposable
{
    private readonly HttpClient _client;

    public GitHubUpdateHttpTransport(TimeSpan connectTimeout)
    {
        if (connectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        }

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = connectTimeout,
            MaxResponseHeadersLength = 32,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    public void Dispose() => _client.Dispose();
}

internal sealed record UpdateDiscoveryOptions
{
    public required SemanticVersion InstalledVersion { get; init; }

    public required UpdatePackageVariant InstalledVariant { get; init; }

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public int MaximumReleaseMetadataBytes { get; init; } = 256 * 1024;

    public int MaximumRedirects { get; init; } = 3;

    public string UserAgent { get; init; } = "PimaxVrcSupervisor-UpdateDiscovery/1";

    public Uri LatestReleaseUri { get; init; } = new(
        $"https://api.github.com/repos/{UpdateManifestConstants.Repository}/releases/latest");

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(InstalledVersion);
        if (!InstalledVersion.IsStable
            || InstalledVersion.BuildMetadata is not null
            || ConnectTimeout <= TimeSpan.Zero
            || ConnectTimeout > TimeSpan.FromMinutes(1)
            || RequestTimeout <= TimeSpan.Zero
            || RequestTimeout > TimeSpan.FromMinutes(1)
            || MaximumReleaseMetadataBytes is <= 0 or > 1024 * 1024
            || MaximumRedirects is < 0 or > 5
            || UserAgent.Length is 0 or > 128
            || UserAgent.Any(char.IsControl)
            || !ProductInfoHeaderValue.TryParse(UserAgent, out _)
            || LatestReleaseUri != GitHubUpdateEndpointPolicy.LatestReleaseUri)
        {
            throw new ArgumentException("The update discovery options are invalid.");
        }
    }
}

internal enum UpdateDiscoveryStatus
{
    UpdateAvailable,
    Current,
    NotModified,
    Ignored,
    Failed,
    Cancelled
}

internal sealed record UpdateDiscoveryCheckResult
{
    public required UpdateDiscoveryStatus Status { get; init; }

    public required string? ETag { get; init; }

    public required string? Version { get; init; }

    public required string? Tag { get; init; }

    public required string? ReleaseUrl { get; init; }

    public required ValidatedUpdateManifest? VerifiedManifest { get; init; }

    // Opaque capability. Only GitHubUpdateDiscoveryClient can create its concrete runtime type.
    // A caller-supplied object never becomes download authority.
    internal object? VerifiedPackageAuthority { get; init; }

    public required UpdateErrorCategory? ErrorCategory { get; init; }

    public required string? ErrorCode { get; init; }

    public bool CompletedSuccessfully => Status is
        UpdateDiscoveryStatus.UpdateAvailable
        or UpdateDiscoveryStatus.Current
        or UpdateDiscoveryStatus.NotModified
        or UpdateDiscoveryStatus.Ignored;

    public static UpdateDiscoveryCheckResult Failure(UpdateErrorCategory category, string code) => new()
    {
        Status = UpdateDiscoveryStatus.Failed,
        ETag = null,
        Version = null,
        Tag = null,
        ReleaseUrl = null,
        VerifiedManifest = null,
        VerifiedPackageAuthority = null,
        ErrorCategory = category,
        ErrorCode = code
    };

    public static UpdateDiscoveryCheckResult Cancelled() => new()
    {
        Status = UpdateDiscoveryStatus.Cancelled,
        ETag = null,
        Version = null,
        Tag = null,
        ReleaseUrl = null,
        VerifiedManifest = null,
        VerifiedPackageAuthority = null,
        ErrorCategory = UpdateErrorCategory.Cancelled,
        ErrorCode = "cancelled"
    };
}

internal interface IUpdateDiscoveryClient
{
    Task<UpdateDiscoveryCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken);
}

internal sealed class GitHubUpdateDiscoveryClient : IUpdateDiscoveryClient, IDisposable
{
    private readonly IUpdateHttpTransport _transport;
    private readonly UpdateTrustStore _trustStore;
    private readonly UpdateDiscoveryOptions _options;
    private readonly IDisposable? _ownedTransport;

    private GitHubUpdateDiscoveryClient(
        IUpdateHttpTransport transport,
        UpdateDiscoveryOptions options)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _trustStore = ProductionUpdateTrustRoots.CreateTrustStore();
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    private GitHubUpdateDiscoveryClient(
        IUpdateHttpTransport transport,
        UpdateDiscoveryOptions options,
        IDisposable ownedTransport)
        : this(transport, options)
    {
        _ownedTransport = ownedTransport;
    }

#if PHASE33B_TEST_TRUST
    private GitHubUpdateDiscoveryClient(
        IUpdateHttpTransport transport,
        UpdateDiscoveryOptions options,
        UpdateTrustStore testTrustStore)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _trustStore = testTrustStore ?? throw new ArgumentNullException(nameof(testTrustStore));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    internal static GitHubUpdateDiscoveryClient CreateForTestTrust(
        IUpdateHttpTransport transport,
        UpdateTrustStore testTrustStore,
        UpdateDiscoveryOptions options)
        => new(transport, options, testTrustStore);
#endif

    public static GitHubUpdateDiscoveryClient CreateProduction(UpdateDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var transport = new GitHubUpdateHttpTransport(options.ConnectTimeout);
        return new GitHubUpdateDiscoveryClient(
            transport,
            options,
            transport);
    }

    internal static bool TryGetVerifiedPackageAuthority(object? authority, out VerifiedPackageAuthorityView view)
    {
        if (authority is PackageAuthority packageAuthority && IsExactAuthorityView(packageAuthority.View))
        {
            view = packageAuthority.View;
            return true;
        }

        view = null!;
        return false;
    }

    internal static VerifiedPackageAuthorityView RequireVerifiedPackageAuthority(object? authority)
        => TryGetVerifiedPackageAuthority(authority, out var view)
            ? view
            : throw new UpdateContractException("package_authority", "A verifier-created package authority capability is required.");

    private static bool IsExactAuthorityView(VerifiedPackageAuthorityView view)
    {
        if (view is null
            || !string.Equals(view.Repository, UpdateManifestConstants.Repository, StringComparison.Ordinal)
            || !string.Equals(view.Channel, UpdateManifestConstants.Channel, StringComparison.Ordinal)
            || !string.Equals(view.Tag, "v" + view.Version, StringComparison.Ordinal)
            || !Uri.TryCreate(
                $"https://github.com/{UpdateManifestConstants.Repository}/releases/download/{view.Tag}/{view.FileName}",
                UriKind.Absolute,
                out var expected))
        {
            return false;
        }

        if (view.ReleaseSequence <= 0
            || !UpdateContractValidation.IsSafeIdentifier(view.ReleaseCommitSha, 128)
            || !string.Equals(view.SignatureAlgorithm, UpdateManifestConstants.SignatureAlgorithm, StringComparison.Ordinal)
            || view.SignedManifestBytes.IsDefaultOrEmpty
            || view.SignatureEnvelopeBytes.IsDefaultOrEmpty
            || view.SignedManifestBytes.Length > UpdateManifestConstants.MaximumManifestBytes
            || view.SignatureEnvelopeBytes.Length > UpdateManifestConstants.MaximumSignatureEnvelopeBytes
            || !string.Equals(
                Convert.ToHexString(SHA256.HashData(view.SignedManifestBytes.AsSpan())).ToLowerInvariant(),
                view.SignedManifestSha256,
                StringComparison.Ordinal))
        {
            return false;
        }

        return NormalizedUriEquals(view.BrowserDownloadUri, expected);
    }

    public void Dispose() => _ownedTransport?.Dispose();

    public async Task<UpdateDiscoveryCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken)
    {
        try
        {
            ValidateOptionalEtag(etag);
            var releaseResponse = await SendBoundedGetAsync(
                _options.LatestReleaseUri,
                etag,
                _options.MaximumReleaseMetadataBytes,
                UpdateEndpointKind.ReleaseMetadata,
                cancellationToken).ConfigureAwait(false);
            if (releaseResponse.StatusCode == HttpStatusCode.NotModified)
            {
                return new UpdateDiscoveryCheckResult
                {
                    Status = UpdateDiscoveryStatus.NotModified,
                    ETag = releaseResponse.ETag ?? etag,
                    Version = null,
                    Tag = null,
                    ReleaseUrl = null,
                    VerifiedManifest = null,
                    VerifiedPackageAuthority = null,
                    ErrorCategory = null,
                    ErrorCode = null
                };
            }

            EnsureSuccess(releaseResponse, "release_http");
            var candidate = GitHubReleaseCandidate.Parse(releaseResponse.Body);
            if (candidate.Draft || candidate.Prerelease)
            {
                return CandidateResult(UpdateDiscoveryStatus.Ignored, candidate, releaseResponse.ETag, null);
            }

            if (!candidate.Immutable)
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "release_mutable");
            }

            var versionComparison = candidate.Version.CompareTo(_options.InstalledVersion);
            if (versionComparison < 0)
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.Rollback, "release_rollback");
            }

            if (versionComparison == 0)
            {
                return CandidateResult(UpdateDiscoveryStatus.Current, candidate, releaseResponse.ETag, null);
            }

            var manifestName = UpdateManifestConstants.ManifestFileName(candidate.Version);
            var signatureName = UpdateManifestConstants.SignatureFileName(candidate.Version);
            var manifestAsset = candidate.GetRequiredAsset(manifestName);
            var signatureAsset = candidate.GetRequiredAsset(signatureName);

            var manifestResponse = await SendBoundedGetAsync(
                manifestAsset.BrowserDownloadUri,
                etag: null,
                UpdateManifestConstants.MaximumManifestBytes,
                UpdateEndpointKind.ReleaseAsset,
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(manifestResponse, "manifest_http");

            var signatureResponse = await SendBoundedGetAsync(
                signatureAsset.BrowserDownloadUri,
                etag: null,
                UpdateManifestConstants.MaximumSignatureEnvelopeBytes,
                UpdateEndpointKind.ReleaseAsset,
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(signatureResponse, "signature_http");

            ValidateDownloadedAssetMetadata(manifestAsset, manifestResponse.Body, "manifest_asset");
            ValidateDownloadedAssetMetadata(signatureAsset, signatureResponse.Body, "signature_asset");
            var verified = UpdateManifestVerifier.VerifyAndParse(
                manifestResponse.Body,
                signatureResponse.Body,
                _trustStore,
                _options.InstalledVariant);
            if (verified.Version.CompareTo(candidate.Version) != 0
                || !string.Equals(verified.Manifest.Release.Tag, candidate.Tag, StringComparison.Ordinal)
                || !string.Equals(verified.Manifest.Release.ReleaseUrl, candidate.ReleaseUrl, StringComparison.Ordinal))
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "manifest_release_mismatch");
            }

            candidate.ValidateCompleteAssetSet(verified);
            var authority = MintAuthority(candidate, verified, manifestResponse.Body, signatureResponse.Body);
            return CandidateResult(UpdateDiscoveryStatus.UpdateAvailable, candidate, releaseResponse.ETag, verified, authority);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UpdateDiscoveryCheckResult.Cancelled();
        }
        catch (OperationCanceledException)
        {
            return UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.Timeout, "request_timeout");
        }
        catch (UpdateRequestTimeoutException)
        {
            return UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.Timeout, "request_timeout");
        }
        catch (UpdateDiscoveryException ex)
        {
            return UpdateDiscoveryCheckResult.Failure(ex.Category, ex.Code);
        }
        catch (UpdateContractException ex)
        {
            return UpdateDiscoveryCheckResult.Failure(MapContractError(ex.Code), ex.Code);
        }
        catch (HttpRequestException)
        {
            return UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.Offline, "network_unavailable");
        }
        catch (IOException)
        {
            return UpdateDiscoveryCheckResult.Failure(UpdateErrorCategory.Http, "response_io");
        }
    }

    private async Task<BoundedHttpResponse> SendBoundedGetAsync(
        Uri initialUri,
        string? etag,
        int maximumBytes,
        UpdateEndpointKind endpointKind,
        CancellationToken cancellationToken)
    {
        GitHubUpdateEndpointPolicy.ValidateInitial(initialUri, endpointKind);
        var currentUri = initialUri;
        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (redirectCount == 0 && etag is not null)
            {
                request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);
            HttpResponseMessage response;
            try
            {
                response = await _transport.SendAsync(request, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateRequestTimeoutException();
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= _options.MaximumRedirects || response.Headers.Location is null)
                    {
                        throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "redirect_limit");
                    }

                    var nextUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(currentUri, response.Headers.Location);
                    GitHubUpdateEndpointPolicy.ValidateRedirect(initialUri, currentUri, nextUri, endpointKind);
                    currentUri = nextUri;
                    continue;
                }

                var responseEtag = ValidateResponseEtag(response.Headers.ETag?.ToString());
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return new BoundedHttpResponse(response.StatusCode, [], responseEtag, currentUri);
                }

                var body = await ReadBoundedBodyAsync(response.Content, maximumBytes, timeout.Token).ConfigureAwait(false);
                return new BoundedHttpResponse(response.StatusCode, body, responseEtag, currentUri);
            }
        }
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is < 0 || content.Headers.ContentLength > maximumBytes)
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "response_too_large");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "response_too_large");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static void ValidateDownloadedAssetMetadata(GitHubReleaseAsset asset, byte[] body, string code)
    {
        if (asset.Size != body.LongLength)
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, code + "_size");
        }

        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        if (!string.Equals(asset.Digest, digest, StringComparison.Ordinal))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, code + "_digest");
        }
    }

    private static PackageAuthority MintAuthority(
        GitHubReleaseCandidate candidate,
        ValidatedUpdateManifest manifest,
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureEnvelopeBytes)
    {
        if (candidate.Draft
            || candidate.Prerelease
            || !candidate.Immutable
            || candidate.Version.CompareTo(manifest.Version) != 0
            || !string.Equals(manifest.Manifest.Repository, UpdateManifestConstants.Repository, StringComparison.Ordinal)
            || !string.Equals(manifest.Manifest.Channel, UpdateManifestConstants.Channel, StringComparison.Ordinal)
            || !string.Equals(manifest.Manifest.Release.Tag, candidate.Tag, StringComparison.Ordinal)
            || !string.Equals(manifest.Manifest.Release.ReleaseUrl, candidate.ReleaseUrl, StringComparison.Ordinal))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "package_authority_release");
        }

        ValidateExactReleaseAsset(candidate, UpdateManifestConstants.ManifestFileName(manifest.Version), manifestBytes);
        ValidateExactReleaseAsset(candidate, UpdateManifestConstants.SignatureFileName(manifest.Version), signatureEnvelopeBytes);
        candidate.ValidateCompleteAssetSet(manifest);
        var package = manifest.SelectedPackage;
        var expectedUri = new Uri($"https://github.com/{UpdateManifestConstants.Repository}/releases/download/{candidate.Tag}/{package.FileName}", UriKind.Absolute);
        var releaseAsset = candidate.GetRequiredAsset(package.FileName);
        if (releaseAsset.Size != package.SizeBytes
            || !string.Equals(releaseAsset.Digest, "sha256:" + package.Sha256, StringComparison.Ordinal)
            || !NormalizedUriEquals(releaseAsset.BrowserDownloadUri, expectedUri)
            || manifest.SignatureKeyId.Length == 0)
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "package_authority_mismatch");
        }

        return new PackageAuthority(new VerifiedPackageAuthorityView(
            UpdateManifestConstants.Repository,
            candidate.Tag,
            manifest.Version.ToString(),
            UpdateManifestConstants.Channel,
            candidate.ReleaseUrl,
            manifest.InstalledVariant,
            package.FileName,
            package.SizeBytes,
            package.Sha256,
            manifest.ManifestSha256,
            manifest.SignatureKeyId,
            expectedUri,
            manifest.Manifest.Release.CommitSha,
            manifest.Manifest.ReleaseSequence,
            UpdateManifestConstants.SignatureAlgorithm,
            ImmutableArray.Create(manifestBytes.ToArray()),
            ImmutableArray.Create(signatureEnvelopeBytes.ToArray())));
    }

    private static void ValidateExactReleaseAsset(GitHubReleaseCandidate candidate, string name, ReadOnlySpan<byte> content)
    {
        var asset = candidate.GetRequiredAsset(name);
        var expectedUri = new Uri($"https://github.com/{UpdateManifestConstants.Repository}/releases/download/{candidate.Tag}/{name}", UriKind.Absolute);
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!string.Equals(asset.Name, name, StringComparison.Ordinal)
            || asset.Size != content.Length
            || !string.Equals(asset.Digest, digest, StringComparison.Ordinal)
            || !NormalizedUriEquals(asset.BrowserDownloadUri, expectedUri))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "package_authority_asset");
        }
    }

    private static bool NormalizedUriEquals(Uri actual, Uri expected)
        => actual is not null
            && actual.IsAbsoluteUri
            && string.Equals(actual.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && actual.IsDefaultPort
            && string.IsNullOrEmpty(actual.UserInfo)
            && string.IsNullOrEmpty(actual.Fragment)
            && string.Equals(actual.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped), expected.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped), StringComparison.Ordinal);

    private sealed record PackageAuthority(VerifiedPackageAuthorityView View);

    private static UpdateDiscoveryCheckResult CandidateResult(
        UpdateDiscoveryStatus status,
        GitHubReleaseCandidate candidate,
        string? etag,
        ValidatedUpdateManifest? verifiedManifest,
        object? verifiedPackageAuthority = null)
        => new()
        {
            Status = status,
            ETag = etag,
            Version = candidate.Version.ToString(),
            Tag = candidate.Tag,
            ReleaseUrl = candidate.ReleaseUrl,
            VerifiedManifest = verifiedManifest,
            VerifiedPackageAuthority = status == UpdateDiscoveryStatus.UpdateAvailable ? verifiedPackageAuthority : null,
            ErrorCategory = null,
            ErrorCode = null
        };

    private static void EnsureSuccess(BoundedHttpResponse response, string code)
    {
        if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Http, code);
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static void ValidateOptionalEtag(string? etag)
    {
        if (etag is null)
        {
            return;
        }

        if (etag.Length > 1024
            || etag.Any(char.IsControl)
            || !EntityTagHeaderValue.TryParse(etag, out _))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.State, "etag_invalid");
        }
    }

    private static string? ValidateResponseEtag(string? etag)
    {
        ValidateOptionalEtag(etag);
        return etag;
    }

    private static UpdateErrorCategory MapContractError(string code)
        => code.Contains("signature", StringComparison.Ordinal)
            || code is "unknown_key" or "manifest_hash"
                ? UpdateErrorCategory.Signature
                : UpdateErrorCategory.Schema;
}

internal enum UpdateEndpointKind
{
    ReleaseMetadata,
    ReleaseAsset
}

internal static class GitHubUpdateEndpointPolicy
{
    public static Uri LatestReleaseUri { get; } = new(
        $"https://api.github.com/repos/{UpdateManifestConstants.Repository}/releases/latest");

    public static void ValidateInitial(Uri uri, UpdateEndpointKind kind)
    {
        if (!IsCleanHttpsUri(uri))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "endpoint_rejected");
        }

        if (kind == UpdateEndpointKind.ReleaseMetadata)
        {
            if (uri != LatestReleaseUri)
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "metadata_endpoint_rejected");
            }

            return;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(
                $"/{UpdateManifestConstants.Repository}/releases/download/",
                StringComparison.Ordinal))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "asset_endpoint_rejected");
        }
    }

    public static void ValidateRedirect(
        Uri initialUri,
        Uri currentUri,
        Uri nextUri,
        UpdateEndpointKind kind)
    {
        if (!IsCleanHttpsUri(nextUri))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "redirect_rejected");
        }

        if (kind == UpdateEndpointKind.ReleaseMetadata)
        {
            if (nextUri != LatestReleaseUri)
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "redirect_rejected");
            }

            return;
        }

        var nextHost = nextUri.Host;
        var currentHost = currentUri.Host;
        var allowed = string.Equals(nextHost, "github.com", StringComparison.OrdinalIgnoreCase)
            ? nextUri == initialUri
            : string.Equals(nextHost, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(currentHost, "github.com", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(currentHost, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase));
        if (!allowed)
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "redirect_rejected");
        }
    }

    private static bool IsCleanHttpsUri(Uri uri)
        => uri.IsAbsoluteUri
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment);
}

internal sealed record GitHubReleaseAsset(
    string Name,
    long Size,
    string? Digest,
    Uri BrowserDownloadUri);

internal sealed record VerifiedPackageAuthorityView(
    string Repository,
    string Tag,
    string Version,
    string Channel,
    string ReleaseUrl,
    UpdatePackageVariant Variant,
    string FileName,
    long ExpectedSize,
    string ExpectedSha256,
    string SignedManifestSha256,
    string SignatureKeyId,
    Uri BrowserDownloadUri,
    string ReleaseCommitSha = "",
    long ReleaseSequence = 0,
    string SignatureAlgorithm = "",
    ImmutableArray<byte> SignedManifestBytes = default,
    ImmutableArray<byte> SignatureEnvelopeBytes = default);

internal sealed record GitHubReleaseCandidate(
    SemanticVersion Version,
    string Tag,
    string ReleaseUrl,
    bool Draft,
    bool Prerelease,
    bool Immutable,
    ImmutableDictionary<string, GitHubReleaseAsset> Assets)
{
    public static GitHubReleaseCandidate Parse(ReadOnlySpan<byte> bytes)
    {
        var ownedBytes = StrictUpdateJson.ValidateSyntax(bytes, "release_json");
        try
        {
            using var document = JsonDocument.Parse(ownedBytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_json");
            }

            var draft = RequiredBoolean(root, "draft");
            var prerelease = RequiredBoolean(root, "prerelease");
            var tag = RequiredString(root, "tag_name", 128);
            if (!tag.StartsWith('v')
                || !SemanticVersion.TryParse(tag[1..], out var version)
                || version.BuildMetadata is not null
                || (!prerelease && !version.IsStable))
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_version");
            }

            var releaseUrl = RequiredString(root, "html_url", 512);
            var expectedUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/{tag}";
            if (!string.Equals(releaseUrl, expectedUrl, StringComparison.Ordinal))
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "release_url");
            }

            var assetsElement = root.GetProperty("assets");
            if (assetsElement.ValueKind != JsonValueKind.Array || assetsElement.GetArrayLength() > 32)
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_assets");
            }

            var assets = ImmutableDictionary.CreateBuilder<string, GitHubReleaseAsset>(StringComparer.Ordinal);
            foreach (var item in assetsElement.EnumerateArray())
            {
                var name = RequiredString(item, "name", 255);
                UpdateContractValidation.ValidateSafeFileName(name, "release_asset_name");
                var size = item.GetProperty("size").GetInt64();
                if (size <= 0 || size > UpdateManifestConstants.MaximumPackageBytes)
                {
                    throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_asset_size");
                }

                var digest = item.TryGetProperty("digest", out var digestElement)
                    && digestElement.ValueKind == JsonValueKind.String
                        ? digestElement.GetString()
                        : null;
                if (digest is not null
                    && (!digest.StartsWith("sha256:", StringComparison.Ordinal)
                        || digest.Length != 71
                        || !digest[7..].All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
                {
                    throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_asset_digest");
                }

                var browserDownloadUrl = RequiredString(item, "browser_download_url", 2048);
                if (!Uri.TryCreate(browserDownloadUrl, UriKind.Absolute, out var browserDownloadUri))
                {
                    throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_asset_url");
                }

                var expectedDownloadUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/download/{tag}/{name}";
                if (!string.Equals(browserDownloadUri.AbsoluteUri, expectedDownloadUrl, StringComparison.Ordinal))
                {
                    throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "release_asset_url");
                }

                if (!assets.TryAdd(name, new GitHubReleaseAsset(name, size, digest, browserDownloadUri)))
                {
                    throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_asset_duplicate");
                }
            }

            return new GitHubReleaseCandidate(
                version,
                tag,
                releaseUrl,
                draft,
                prerelease,
                RequiredBoolean(root, "immutable"),
                assets.ToImmutable());
        }
        catch (UpdateDiscoveryException)
        {
            throw;
        }
        catch (UpdateContractException ex)
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, ex.Code, ex);
        }
        catch (Exception ex) when (ex is JsonException
            or InvalidOperationException
            or KeyNotFoundException
            or FormatException
            or OverflowException
            or ArgumentException)
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_json", ex);
        }
    }

    public GitHubReleaseAsset GetRequiredAsset(string name)
        => Assets.TryGetValue(name, out var asset)
            ? asset
            : throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "release_asset_missing");

    public void ValidateCompleteAssetSet(ValidatedUpdateManifest manifest)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            UpdateManifestConstants.ManifestFileName(Version),
            UpdateManifestConstants.SignatureFileName(Version),
            UpdateManifestConstants.ManifestFileName(Version) + ".sha256",
            UpdateManifestConstants.ManifestFileName(Version) + ".sigstore.json",
            UpdateManifestConstants.ManifestFileName(Version) + ".attestation.json"
        };
        foreach (var package in manifest.Packages.Values)
        {
            expected.Add(package.FileName);
            expected.Add(package.ChecksumFile.FileName);
            expected.Add(package.SigstoreBundleFile.FileName);
            expected.Add(package.AttestationBundleFile.FileName);
            var releaseAsset = GetRequiredAsset(package.FileName);
            if (releaseAsset.Size != package.SizeBytes
                || !string.Equals(releaseAsset.Digest, "sha256:" + package.Sha256, StringComparison.Ordinal))
            {
                throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "package_metadata_mismatch");
            }

            ValidateCompanionDigest(package.ChecksumFile);
            ValidateCompanionDigest(package.SigstoreBundleFile);
            ValidateCompanionDigest(package.AttestationBundleFile);
        }

        if (expected.Count != 13 || !expected.SetEquals(Assets.Keys))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "release_asset_set");
        }
    }

    private void ValidateCompanionDigest(UpdateManifestCompanionFileV1 companion)
    {
        var releaseAsset = GetRequiredAsset(companion.FileName);
        if (!string.Equals(releaseAsset.Digest, "sha256:" + companion.Sha256, StringComparison.Ordinal))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.ReleaseMismatch, "companion_metadata_mismatch");
        }
    }

    private static string RequiredString(JsonElement element, string propertyName, int maximumLength)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || property.GetString() is not { } value
            || value.Length == 0
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_" + propertyName);
        }

        return value;
    }

    private static bool RequiredBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new UpdateDiscoveryException(UpdateErrorCategory.Schema, "release_" + propertyName);
        }

        return property.GetBoolean();
    }
}

internal sealed record BoundedHttpResponse(
    HttpStatusCode StatusCode,
    byte[] Body,
    string? ETag,
    Uri FinalUri);

internal sealed class UpdateDiscoveryException : Exception
{
    public UpdateDiscoveryException(UpdateErrorCategory category, string code)
        : base(code)
    {
        Category = category;
        Code = code;
    }

    public UpdateDiscoveryException(UpdateErrorCategory category, string code, Exception innerException)
        : base(code, innerException)
    {
        Category = category;
        Code = code;
    }

    public UpdateErrorCategory Category { get; }

    public string Code { get; }
}

internal sealed class UpdateRequestTimeoutException : Exception;
