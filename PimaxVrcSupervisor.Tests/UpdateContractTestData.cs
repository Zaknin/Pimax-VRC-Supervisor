using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PimaxVrcSupervisor.Updates;

internal static class UpdateContractTestData
{
    public const string KeyId = "test-current-2026";

    public static JsonObject CreateManifest(string version = "1.4.0")
    {
        var tag = "v" + version;
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["repository"] = UpdateManifestConstants.Repository,
            ["channel"] = UpdateManifestConstants.Channel,
            ["releaseSequence"] = 4,
            ["generatedAtUtc"] = "2026-07-21T00:00:00Z",
            ["release"] = new JsonObject
            {
                ["version"] = version,
                ["tag"] = tag,
                ["commitSha"] = new string('a', 40),
                ["releaseUrl"] = $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/{tag}"
            },
            ["assets"] = new JsonArray
            {
                CreateAsset(version, "with-dotnet9", "self-contained", requiredRuntime: null, 'b'),
                CreateAsset(version, "no-dotnet9", "framework-dependent", "9.0.x-windowsdesktop-x64", '7')
            }
        };
    }

    public static SignedUpdateTestData Sign(JsonObject manifest, string keyId = KeyId)
        => Sign(JsonSerializer.SerializeToUtf8Bytes(manifest), keyId);

    public static SignedUpdateTestData Sign(byte[] manifestBytes, string keyId = KeyId)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = key.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var manifestVersion = FindManifestVersion(manifestBytes);
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            manifestFile = UpdateManifestConstants.ManifestFileName(SemanticVersion.Parse(manifestVersion)),
            manifestSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
            signatures = new[]
            {
                new
                {
                    keyId,
                    algorithm = UpdateManifestConstants.SignatureAlgorithm,
                    signatureBase64 = Convert.ToBase64String(signature)
                }
            }
        });

        return new SignedUpdateTestData(
            manifestBytes,
            envelope,
            new UpdateTrustStore([new UpdateTrustRoot(keyId, publicKey)]));
    }

    public static UpdateStateV1 CreateState(
        UpdatePackageVariant variant = UpdatePackageVariant.WithDotnet9,
        UpdateCheckPolicy policy = UpdateCheckPolicy.NotifyStable,
        DateTimeOffset? lastSuccessfulCheckUtc = null)
        => UpdateStateV1.CreateDefault(variant) with
        {
            Policy = policy,
            LastAttemptUtc = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero),
            LastSuccessfulCheckUtc = lastSuccessfulCheckUtc,
            ETag = "\"etag-1\""
        };

    private static JsonObject CreateAsset(
        string version,
        string variant,
        string runtimeMode,
        string? requiredRuntime,
        char hashCharacter)
    {
        var fileName = $"PimaxVrcSupervisor-v{version}-win-x64-{variant}.zip";
        return new JsonObject
        {
            ["variant"] = variant,
            ["rid"] = "win-x64",
            ["runtimeMode"] = runtimeMode,
            ["requiredWindowsDesktopRuntime"] = requiredRuntime,
            ["fileName"] = fileName,
            ["sizeBytes"] = 50_000_000,
            ["sha256"] = new string(hashCharacter, 64),
            ["checksumFile"] = Companion(fileName + ".sha256", 'c'),
            ["sigstoreBundleFile"] = Companion(fileName + ".sigstore.json", 'd'),
            ["attestationBundleFile"] = Companion(fileName + ".attestation.json", 'e')
        };
    }

    private static JsonObject Companion(string fileName, char hashCharacter) => new()
    {
        ["fileName"] = fileName,
        ["sha256"] = new string(hashCharacter, 64)
    };

    private static string FindManifestVersion(byte[] manifestBytes)
    {
        using var document = JsonDocument.Parse(manifestBytes);
        return document.RootElement.GetProperty("release").GetProperty("version").GetString()!;
    }
}

internal sealed record SignedUpdateTestData(
    byte[] ManifestBytes,
    byte[] SignatureEnvelopeBytes,
    UpdateTrustStore TrustStore);
