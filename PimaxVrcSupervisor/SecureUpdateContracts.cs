using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PimaxVrcSupervisor.Updates;

internal static class UpdateManifestConstants
{
    public const int SchemaVersion = 1;
    public const string Repository = "Zaknin/Pimax-VRC-Supervisor";
    public const string Channel = "stable";
    public const string Rid = "win-x64";
    public const string SignatureAlgorithm = "ecdsa-p256-sha256-der";
    public const int MaximumManifestBytes = 256 * 1024;
    public const int MaximumSignatureEnvelopeBytes = 64 * 1024;
    public const long MaximumPackageBytes = 1024L * 1024L * 1024L;

    public static string ManifestFileName(SemanticVersion version)
        => $"PimaxVrcSupervisor-v{version}-update-manifest-v1.json";

    public static string SignatureFileName(SemanticVersion version)
        => $"PimaxVrcSupervisor-v{version}-update-manifest-v1.signatures.json";

    public static string PackageFileName(SemanticVersion version, UpdatePackageVariant variant)
        => variant switch
        {
            UpdatePackageVariant.WithDotnet9 => $"PimaxVrcSupervisor-v{version}-win-x64-with-dotnet9.zip",
            UpdatePackageVariant.NoDotnet9 => $"PimaxVrcSupervisor-v{version}-win-x64-no-dotnet9.zip",
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
}

internal enum UpdatePackageVariant
{
    WithDotnet9,
    NoDotnet9
}

internal sealed record UpdateManifestV1
{
    [JsonPropertyName("schemaVersion"), JsonRequired]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("repository"), JsonRequired]
    public required string Repository { get; init; }

    [JsonPropertyName("channel"), JsonRequired]
    public required string Channel { get; init; }

    [JsonPropertyName("releaseSequence"), JsonRequired]
    public required long ReleaseSequence { get; init; }

    [JsonPropertyName("generatedAtUtc"), JsonRequired]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("release"), JsonRequired]
    public required UpdateManifestReleaseV1 Release { get; init; }

    [JsonPropertyName("assets"), JsonRequired]
    public required ImmutableArray<UpdateManifestAssetV1> Assets { get; init; }
}

internal sealed record UpdateManifestReleaseV1
{
    [JsonPropertyName("version"), JsonRequired]
    public required string Version { get; init; }

    [JsonPropertyName("tag"), JsonRequired]
    public required string Tag { get; init; }

    [JsonPropertyName("commitSha"), JsonRequired]
    public required string CommitSha { get; init; }

    [JsonPropertyName("releaseUrl"), JsonRequired]
    public required string ReleaseUrl { get; init; }
}

internal sealed record UpdateManifestAssetV1
{
    [JsonPropertyName("variant"), JsonRequired]
    public required string Variant { get; init; }

    [JsonPropertyName("rid"), JsonRequired]
    public required string Rid { get; init; }

    [JsonPropertyName("runtimeMode"), JsonRequired]
    public required string RuntimeMode { get; init; }

    [JsonPropertyName("requiredWindowsDesktopRuntime"), JsonRequired]
    public required string? RequiredWindowsDesktopRuntime { get; init; }

    [JsonPropertyName("fileName"), JsonRequired]
    public required string FileName { get; init; }

    [JsonPropertyName("sizeBytes"), JsonRequired]
    public required long SizeBytes { get; init; }

    [JsonPropertyName("sha256"), JsonRequired]
    public required string Sha256 { get; init; }

    [JsonPropertyName("checksumFile"), JsonRequired]
    public required UpdateManifestCompanionFileV1 ChecksumFile { get; init; }

    [JsonPropertyName("sigstoreBundleFile"), JsonRequired]
    public required UpdateManifestCompanionFileV1 SigstoreBundleFile { get; init; }

    [JsonPropertyName("attestationBundleFile"), JsonRequired]
    public required UpdateManifestCompanionFileV1 AttestationBundleFile { get; init; }
}

internal sealed record UpdateManifestCompanionFileV1
{
    [JsonPropertyName("fileName"), JsonRequired]
    public required string FileName { get; init; }

    [JsonPropertyName("sha256"), JsonRequired]
    public required string Sha256 { get; init; }
}

internal sealed record UpdateSignatureEnvelopeV1
{
    [JsonPropertyName("schemaVersion"), JsonRequired]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("manifestFile"), JsonRequired]
    public required string ManifestFile { get; init; }

    [JsonPropertyName("manifestSha256"), JsonRequired]
    public required string ManifestSha256 { get; init; }

    [JsonPropertyName("signatures"), JsonRequired]
    public required ImmutableArray<UpdateDetachedSignatureV1> Signatures { get; init; }
}

internal sealed record UpdateDetachedSignatureV1
{
    [JsonPropertyName("keyId"), JsonRequired]
    public required string KeyId { get; init; }

    [JsonPropertyName("algorithm"), JsonRequired]
    public required string Algorithm { get; init; }

    [JsonPropertyName("signatureBase64"), JsonRequired]
    public required string SignatureBase64 { get; init; }
}

internal sealed record ValidatedUpdateManifest(
    UpdateManifestV1 Manifest,
    SemanticVersion Version,
    DateTimeOffset GeneratedAtUtc,
    ImmutableDictionary<UpdatePackageVariant, UpdateManifestAssetV1> Packages,
    UpdatePackageVariant InstalledVariant,
    UpdateManifestAssetV1 SelectedPackage,
    string ManifestSha256);

internal sealed class UpdateContractException : Exception
{
    public UpdateContractException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public UpdateContractException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

internal sealed record UpdateTrustRoot
{
    public UpdateTrustRoot(string keyId, ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (!UpdateContractValidation.IsSafeIdentifier(keyId, maximumLength: 128))
        {
            throw new ArgumentException("The update key ID is invalid.", nameof(keyId));
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
        if (bytesRead != subjectPublicKeyInfo.Length || ecdsa.KeySize != 256)
        {
            throw new ArgumentException("The update trust root must be a DER SubjectPublicKeyInfo for ECDSA P-256.", nameof(subjectPublicKeyInfo));
        }

        KeyId = keyId;
        SubjectPublicKeyInfo = ImmutableArray.Create(subjectPublicKeyInfo.ToArray());
    }

    public string KeyId { get; }

    public ImmutableArray<byte> SubjectPublicKeyInfo { get; }
}

internal sealed class UpdateTrustStore
{
    private readonly ImmutableDictionary<string, UpdateTrustRoot> _roots;

    public UpdateTrustStore(IEnumerable<UpdateTrustRoot> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var builder = ImmutableDictionary.CreateBuilder<string, UpdateTrustRoot>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (!builder.TryAdd(root.KeyId, root))
            {
                throw new ArgumentException($"Duplicate update key ID '{root.KeyId}'.", nameof(roots));
            }
        }

        _roots = builder.ToImmutable();
    }

    public static UpdateTrustStore Empty { get; } = new([]);

    public int Count => _roots.Count;

    public bool TryGet(string keyId, out UpdateTrustRoot root) => _roots.TryGetValue(keyId, out root!);
}

internal static class UpdateManifestVerifier
{
    public static ValidatedUpdateManifest VerifyAndParse(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureEnvelopeBytes,
        UpdateTrustStore trustStore,
        UpdatePackageVariant installedVariant)
    {
        ArgumentNullException.ThrowIfNull(trustStore);
        ValidateResponseSize(manifestBytes, UpdateManifestConstants.MaximumManifestBytes, "manifest_size");
        ValidateResponseSize(signatureEnvelopeBytes, UpdateManifestConstants.MaximumSignatureEnvelopeBytes, "signature_size");

        var envelope = StrictUpdateJson.Deserialize<UpdateSignatureEnvelopeV1>(signatureEnvelopeBytes, "signature_json");
        ValidateEnvelope(envelope);

        var computedHash = SHA256.HashData(manifestBytes);
        var declaredHash = Convert.FromHexString(envelope.ManifestSha256);
        if (!CryptographicOperations.FixedTimeEquals(computedHash, declaredHash))
        {
            throw new UpdateContractException("manifest_hash", "The detached signature envelope does not match the exact manifest bytes.");
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signature in envelope.Signatures)
        {
            if (!seenKeys.Add(signature.KeyId))
            {
                throw new UpdateContractException("duplicate_signature", "The detached signature envelope contains a duplicate key ID.");
            }

            if (!trustStore.TryGet(signature.KeyId, out var trustRoot))
            {
                throw new UpdateContractException("unknown_key", "The detached signature references a key ID that is not embedded in this application.");
            }

            if (!string.Equals(signature.Algorithm, UpdateManifestConstants.SignatureAlgorithm, StringComparison.Ordinal))
            {
                throw new UpdateContractException("signature_algorithm", "The detached signature algorithm is not supported.");
            }

            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signature.SignatureBase64);
            }
            catch (FormatException ex)
            {
                throw new UpdateContractException("signature_base64", "The detached signature is not valid Base64.", ex);
            }

            if (signatureBytes.Length is < 8 or > 256)
            {
                throw new UpdateContractException("signature_der", "The detached signature has an invalid DER length.");
            }

            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(trustRoot.SubjectPublicKeyInfo.AsSpan(), out var bytesRead);
                if (bytesRead != trustRoot.SubjectPublicKeyInfo.Length
                    || !ecdsa.VerifyData(
                        manifestBytes,
                        signatureBytes,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.Rfc3279DerSequence))
                {
                    throw new UpdateContractException("signature_invalid", "The detached signature is invalid for the exact manifest bytes.");
                }
            }
            catch (CryptographicException ex)
            {
                throw new UpdateContractException("signature_der", "The detached signature is not a valid ECDSA DER sequence.", ex);
            }
        }

        var manifest = StrictUpdateJson.Deserialize<UpdateManifestV1>(manifestBytes, "manifest_json");
        var validated = UpdateContractValidation.ValidateManifest(manifest, installedVariant, Convert.ToHexString(computedHash).ToLowerInvariant());
        var expectedManifestFile = UpdateManifestConstants.ManifestFileName(validated.Version);
        if (!string.Equals(envelope.ManifestFile, expectedManifestFile, StringComparison.Ordinal))
        {
            throw new UpdateContractException("manifest_file", "The signature envelope manifest file does not match the verified release version.");
        }

        return validated;
    }

    private static void ValidateEnvelope(UpdateSignatureEnvelopeV1 envelope)
    {
        if (envelope.SchemaVersion != UpdateManifestConstants.SchemaVersion)
        {
            throw new UpdateContractException("signature_schema", "The detached signature schema version is not supported.");
        }

        UpdateContractValidation.ValidateSafeFileName(envelope.ManifestFile, "manifest_file");
        UpdateContractValidation.ValidateSha256(envelope.ManifestSha256, "manifest_hash");
        if (envelope.Signatures.IsDefaultOrEmpty || envelope.Signatures.Length > 2)
        {
            throw new UpdateContractException("signature_count", "The detached signature envelope must contain one or two embedded-key signatures.");
        }

        foreach (var signature in envelope.Signatures)
        {
            if (!UpdateContractValidation.IsSafeIdentifier(signature.KeyId, maximumLength: 128))
            {
                throw new UpdateContractException("signature_key", "The detached signature key ID is invalid.");
            }
        }
    }

    private static void ValidateResponseSize(ReadOnlySpan<byte> bytes, int maximum, string code)
    {
        if (bytes.IsEmpty || bytes.Length > maximum)
        {
            throw new UpdateContractException(code, $"The response must contain between 1 and {maximum} bytes.");
        }
    }
}

internal static class UpdateContractValidation
{
    private static readonly Regex LowerHex64 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex LowerHex40 = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly string[] UtcTimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"
    ];

    public static ValidatedUpdateManifest ValidateManifest(
        UpdateManifestV1 manifest,
        UpdatePackageVariant installedVariant,
        string manifestSha256)
    {
        if (manifest.SchemaVersion != UpdateManifestConstants.SchemaVersion)
        {
            throw new UpdateContractException("manifest_schema", "The update manifest schema version is not supported.");
        }

        if (!string.Equals(manifest.Repository, UpdateManifestConstants.Repository, StringComparison.Ordinal))
        {
            throw new UpdateContractException("repository", "The update manifest repository identity is invalid.");
        }

        if (!string.Equals(manifest.Channel, UpdateManifestConstants.Channel, StringComparison.Ordinal))
        {
            throw new UpdateContractException("channel", "The update manifest channel is not supported.");
        }

        if (manifest.ReleaseSequence <= 0)
        {
            throw new UpdateContractException("release_sequence", "The release sequence must be positive.");
        }

        if (!DateTimeOffset.TryParseExact(
                manifest.GeneratedAtUtc,
                UtcTimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var generatedAtUtc))
        {
            throw new UpdateContractException("generated_at", "The manifest generation time must be a valid UTC timestamp ending in Z.");
        }

        if (!SemanticVersion.TryParse(manifest.Release.Version, out var version)
            || !version.IsStable
            || version.BuildMetadata is not null
            || !string.Equals(version.ToString(), manifest.Release.Version, StringComparison.Ordinal))
        {
            throw new UpdateContractException("version", "A stable manifest version must be a normalized semantic version without prerelease or build metadata.");
        }

        if (!string.Equals(manifest.Release.Tag, "v" + version, StringComparison.Ordinal))
        {
            throw new UpdateContractException("tag", "The release tag does not match the manifest version.");
        }

        if (!LowerHex40.IsMatch(manifest.Release.CommitSha))
        {
            throw new UpdateContractException("commit", "The release commit must be exactly 40 lowercase hexadecimal characters.");
        }

        var expectedReleaseUrl = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v{version}";
        if (!string.Equals(manifest.Release.ReleaseUrl, expectedReleaseUrl, StringComparison.Ordinal))
        {
            throw new UpdateContractException("release_url", "The release URL is not the fixed canonical repository URL.");
        }

        if (manifest.Assets.IsDefault || manifest.Assets.Length != 2)
        {
            throw new UpdateContractException("package_count", "The update manifest must contain exactly two package variants.");
        }

        var packages = ImmutableDictionary.CreateBuilder<UpdatePackageVariant, UpdateManifestAssetV1>();
        foreach (var asset in manifest.Assets)
        {
            var variant = ParseVariant(asset.Variant);
            if (!packages.TryAdd(variant, asset))
            {
                throw new UpdateContractException("duplicate_variant", "The update manifest contains a duplicate package variant.");
            }

            ValidateAsset(asset, version, variant);
        }

        if (!packages.ContainsKey(UpdatePackageVariant.WithDotnet9)
            || !packages.ContainsKey(UpdatePackageVariant.NoDotnet9))
        {
            throw new UpdateContractException("package_variants", "The update manifest does not contain both supported package variants.");
        }

        var immutablePackages = packages.ToImmutable();
        if (!immutablePackages.TryGetValue(installedVariant, out var selectedPackage))
        {
            throw new UpdateContractException("installed_variant", "The currently installed package variant is unavailable.");
        }

        return new ValidatedUpdateManifest(
            manifest,
            version,
            generatedAtUtc,
            immutablePackages,
            installedVariant,
            selectedPackage,
            manifestSha256);
    }

    public static void ValidateSha256(string value, string code)
    {
        if (value is null || !LowerHex64.IsMatch(value))
        {
            throw new UpdateContractException(code, "A SHA-256 value must be exactly 64 lowercase hexadecimal characters.");
        }
    }

    public static void ValidateSafeFileName(string value, string code)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 255
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains("..", StringComparison.Ordinal)
            || value.Any(char.IsControl)
            || !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
        {
            throw new UpdateContractException(code, "An update asset name must be a safe file name without paths, traversal, or control characters.");
        }
    }

    public static bool IsSafeIdentifier(string? value, int maximumLength)
        => !string.IsNullOrEmpty(value)
            && value.Length <= maximumLength
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static UpdatePackageVariant ParseVariant(string value)
        => value switch
        {
            "with-dotnet9" => UpdatePackageVariant.WithDotnet9,
            "no-dotnet9" => UpdatePackageVariant.NoDotnet9,
            _ => throw new UpdateContractException("package_variant", "The update manifest contains an unsupported package variant.")
        };

    private static void ValidateAsset(UpdateManifestAssetV1 asset, SemanticVersion version, UpdatePackageVariant variant)
    {
        if (!string.Equals(asset.Rid, UpdateManifestConstants.Rid, StringComparison.Ordinal))
        {
            throw new UpdateContractException("package_rid", "The update package RID is unsupported.");
        }

        var expectedRuntimeMode = variant == UpdatePackageVariant.WithDotnet9 ? "self-contained" : "framework-dependent";
        var expectedRuntime = variant == UpdatePackageVariant.WithDotnet9 ? null : "9.0.x-windowsdesktop-x64";
        if (!string.Equals(asset.RuntimeMode, expectedRuntimeMode, StringComparison.Ordinal)
            || !string.Equals(asset.RequiredWindowsDesktopRuntime, expectedRuntime, StringComparison.Ordinal))
        {
            throw new UpdateContractException("package_runtime", "The package runtime contract does not match its variant.");
        }

        ValidateSafeFileName(asset.FileName, "package_file");
        var expectedFileName = UpdateManifestConstants.PackageFileName(version, variant);
        if (!string.Equals(asset.FileName, expectedFileName, StringComparison.Ordinal))
        {
            throw new UpdateContractException("package_file", "The package file name does not match its release version and variant.");
        }

        if (asset.SizeBytes <= 0 || asset.SizeBytes > UpdateManifestConstants.MaximumPackageBytes)
        {
            throw new UpdateContractException("package_size", "The package size is outside the supported positive bound.");
        }

        ValidateSha256(asset.Sha256, "package_hash");
        ValidateCompanion(asset.ChecksumFile, expectedFileName + ".sha256", "checksum");
        ValidateCompanion(asset.SigstoreBundleFile, expectedFileName + ".sigstore.json", "sigstore");
        ValidateCompanion(asset.AttestationBundleFile, expectedFileName + ".attestation.json", "attestation");
    }

    private static void ValidateCompanion(UpdateManifestCompanionFileV1 companion, string expectedFileName, string code)
    {
        ValidateSafeFileName(companion.FileName, code + "_file");
        if (!string.Equals(companion.FileName, expectedFileName, StringComparison.Ordinal))
        {
            throw new UpdateContractException(code + "_file", "A package companion file name does not match its package.");
        }

        ValidateSha256(companion.Sha256, code + "_hash");
    }
}

internal static class InstalledPackageVariantDetector
{
    public static UpdatePackageVariant Detect(ReadOnlySpan<byte> runtimeConfigBytes)
    {
        using var document = JsonDocument.Parse(runtimeConfigBytes.ToArray());
        if (!document.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions)
            || runtimeOptions.ValueKind != JsonValueKind.Object)
        {
            throw new UpdateContractException("installed_variant", "The runtime configuration does not identify the installed package variant.");
        }

        var hasFramework = runtimeOptions.TryGetProperty("framework", out _)
            || runtimeOptions.TryGetProperty("frameworks", out _);
        var hasIncludedFrameworks = runtimeOptions.TryGetProperty("includedFrameworks", out _);
        if (hasFramework == hasIncludedFrameworks)
        {
            throw new UpdateContractException("installed_variant", "The runtime configuration has an ambiguous package variant.");
        }

        return hasIncludedFrameworks
            ? UpdatePackageVariant.WithDotnet9
            : UpdatePackageVariant.NoDotnet9;
    }
}

internal static class StrictUpdateJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static T Deserialize<T>(ReadOnlySpan<byte> bytes, string code)
    {
        var ownedBytes = ValidateSyntax(bytes, code);
        try
        {
            return JsonSerializer.Deserialize<T>(ownedBytes, Options)
                ?? throw new UpdateContractException(code, "The JSON document is null.");
        }
        catch (UpdateContractException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new UpdateContractException(code, "The JSON document does not satisfy the strict schema.", ex);
        }
    }

    public static byte[] ValidateSyntax(ReadOnlySpan<byte> bytes, string code)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
        {
            throw new UpdateContractException(code, "UTF-8 input must not contain a byte-order mark.");
        }

        try
        {
            var ownedBytes = bytes.ToArray();
            using var document = JsonDocument.Parse(
                ownedBytes,
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                    MaxDepth = 32
                });
            EnsureNoDuplicateProperties(document.RootElement);
            return ownedBytes;
        }
        catch (UpdateContractException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new UpdateContractException(code, "The JSON document does not satisfy the strict schema.", ex);
        }
    }

    public static byte[] Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new UpdateContractException("duplicate_json_property", $"The JSON document contains duplicate property '{property.Name}'.");
                }

                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item);
            }
        }
    }
}
