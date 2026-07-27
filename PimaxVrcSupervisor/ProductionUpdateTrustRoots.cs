using System.Collections.Immutable;
using System.Security.Cryptography;

namespace PimaxVrcSupervisor.Updates;

internal sealed record ProductionUpdateTrustDescriptor(
    string KeyId,
    string Algorithm,
    int SubjectPublicKeyInfoSize,
    string SubjectPublicKeyInfoSha256,
    ImmutableArray<byte> SubjectPublicKeyInfo);

// Production update trust roots live only in this file so additions and rotations have a
// single auditable diff. Never copy private or test key material into this registry. A future
// next key is added as a second independently audited descriptor during the overlap window.
internal static class ProductionUpdateTrustRoots
{
    internal const string CurrentKeyId = "pimax-update-primary-2026";
    internal const string CurrentAlgorithm = "ecdsa-p256-sha256-der";
    internal const int CurrentPublicKeySize = 91;
    internal const string CurrentPublicKeySha256 = "929fa8e2a3a8d46064202a415f6c62e3e731f334be3de4c1d6d7045267371933";
    internal const string CurrentPublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUiwnAdDYl34eGSDdmddIuXxkVgCNDfFu96luUsrhRELqDG/fPWXupuMQqT5lYwhlbd48vcxPaChVdqaZpyIwuw==";

    private static readonly ImmutableArray<ProductionUpdateTrustDescriptor> AuditedDescriptors =
    [
        ValidateDescriptor(
            CurrentKeyId,
            CurrentAlgorithm,
            CurrentPublicKeySize,
            CurrentPublicKeySha256,
            CurrentPublicKeyBase64)
    ];

    public static UpdateTrustStore CreateTrustStore()
        => new(AuditedDescriptors.Select(descriptor =>
            new UpdateTrustRoot(descriptor.KeyId, descriptor.SubjectPublicKeyInfo.AsSpan())));

    internal static ImmutableArray<ProductionUpdateTrustDescriptor> GetAuditedDescriptors()
        => AuditedDescriptors;

    private static ProductionUpdateTrustDescriptor ValidateDescriptor(
        string keyId,
        string algorithm,
        int expectedSize,
        string expectedSha256,
        string publicKeyBase64)
    {
        if (!string.Equals(algorithm, UpdateManifestConstants.SignatureAlgorithm, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The production update trust-root algorithm descriptor is invalid.");
        }

        if (expectedSize <= 0 || expectedSize > 4096)
        {
            throw new InvalidOperationException("The production update trust-root size descriptor is invalid.");
        }

        if (expectedSha256.Length != 64 || expectedSha256.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException("The production update trust-root fingerprint descriptor is invalid.");
        }

        byte[] subjectPublicKeyInfo;
        try
        {
            subjectPublicKeyInfo = Convert.FromBase64String(publicKeyBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("The embedded production update public key is not valid Base64.", ex);
        }

        if (!string.Equals(Convert.ToBase64String(subjectPublicKeyInfo), publicKeyBase64, StringComparison.Ordinal)
            || subjectPublicKeyInfo.Length != expectedSize)
        {
            throw new InvalidOperationException("The embedded production update public-key size or encoding differs from its descriptor.");
        }

        var actualSha256 = SHA256.HashData(subjectPublicKeyInfo);
        var expectedSha256Bytes = Convert.FromHexString(expectedSha256);
        if (!CryptographicOperations.FixedTimeEquals(actualSha256, expectedSha256Bytes))
        {
            throw new InvalidOperationException("The embedded production update public-key fingerprint differs from its descriptor.");
        }

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
            if (bytesRead != subjectPublicKeyInfo.Length
                || ecdsa.KeySize != 256
                || !string.Equals(parameters.Curve.Oid.Value, "1.2.840.10045.3.1.7", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The embedded production update public key is not DER SubjectPublicKeyInfo for ECDSA P-256.");
            }
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("The embedded production update public key cannot be imported as ECDSA P-256.", ex);
        }

        return new ProductionUpdateTrustDescriptor(
            keyId,
            algorithm,
            expectedSize,
            expectedSha256,
            ImmutableArray.Create(subjectPublicKeyInfo));
    }
}
