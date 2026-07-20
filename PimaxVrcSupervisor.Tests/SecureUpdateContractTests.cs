using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class SecureUpdateContractTests
{
    [Fact]
    public void ManifestSignatureAndPackageNamesAreExact()
    {
        var version = SemanticVersion.Parse("1.4.0");

        Assert.Equal("PimaxVrcSupervisor-v1.4.0-update-manifest-v1.json", UpdateManifestConstants.ManifestFileName(version));
        Assert.Equal("PimaxVrcSupervisor-v1.4.0-update-manifest-v1.signatures.json", UpdateManifestConstants.SignatureFileName(version));
        Assert.Equal(
            "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip",
            UpdateManifestConstants.PackageFileName(version, UpdatePackageVariant.WithDotnet9));
        Assert.Equal(
            "PimaxVrcSupervisor-v1.4.0-win-x64-no-dotnet9.zip",
            UpdateManifestConstants.PackageFileName(version, UpdatePackageVariant.NoDotnet9));
    }

    [Fact]
    public void ExactByteDerSignatureSelectsInstalledVariant()
    {
        var signed = UpdateContractTestData.Sign(UpdateContractTestData.CreateManifest());

        var result = UpdateManifestVerifier.VerifyAndParse(
            signed.ManifestBytes,
            signed.SignatureEnvelopeBytes,
            signed.TrustStore,
            UpdatePackageVariant.NoDotnet9);

        Assert.Equal("1.4.0", result.Version.ToString());
        Assert.Equal(UpdatePackageVariant.NoDotnet9, result.InstalledVariant);
        Assert.EndsWith("-no-dotnet9.zip", result.SelectedPackage.FileName, StringComparison.Ordinal);
        Assert.Equal(2, result.Packages.Count);
    }

    [Fact]
    public void UnknownKeyIsRejectedBeforeManifestParsing()
    {
        var malformedManifest = Encoding.UTF8.GetBytes("not json");
        var signed = SignArbitraryBytes(malformedManifest);

        var exception = Assert.Throws<UpdateContractException>(() => UpdateManifestVerifier.VerifyAndParse(
            signed.ManifestBytes,
            signed.SignatureEnvelopeBytes,
            UpdateTrustStore.Empty,
            UpdatePackageVariant.WithDotnet9));

        Assert.Equal("unknown_key", exception.Code);
    }

    [Fact]
    public void AlteredBytesFailSignatureBeforeJsonParsing()
    {
        var signed = UpdateContractTestData.Sign(UpdateContractTestData.CreateManifest());
        var altered = signed.ManifestBytes.Concat([(byte)' ']).ToArray();
        var envelope = JsonNode.Parse(signed.SignatureEnvelopeBytes)!.AsObject();
        envelope["manifestSha256"] = Convert.ToHexString(SHA256.HashData(altered)).ToLowerInvariant();

        var exception = Assert.Throws<UpdateContractException>(() => UpdateManifestVerifier.VerifyAndParse(
            altered,
            JsonSerializer.SerializeToUtf8Bytes(envelope),
            signed.TrustStore,
            UpdatePackageVariant.WithDotnet9));

        Assert.Equal("signature_invalid", exception.Code);
    }

    [Fact]
    public void SignatureEnvelopeCannotSupplyPublicKey()
    {
        var signed = UpdateContractTestData.Sign(UpdateContractTestData.CreateManifest());
        var envelope = JsonNode.Parse(signed.SignatureEnvelopeBytes)!.AsObject();
        envelope["publicKey"] = "remote-key";

        var exception = Assert.Throws<UpdateContractException>(() => UpdateManifestVerifier.VerifyAndParse(
            signed.ManifestBytes,
            JsonSerializer.SerializeToUtf8Bytes(envelope),
            signed.TrustStore,
            UpdatePackageVariant.WithDotnet9));

        Assert.Equal("signature_json", exception.Code);
    }

    [Fact]
    public void DuplicateManifestPropertyIsRejectedAfterValidSignature()
    {
        var manifest = JsonSerializer.Serialize(UpdateContractTestData.CreateManifest());
        var duplicated = manifest.Replace(
            "\"repository\":\"Zaknin/Pimax-VRC-Supervisor\"",
            "\"repository\":\"Zaknin/Pimax-VRC-Supervisor\",\"repository\":\"Zaknin/Pimax-VRC-Supervisor\"",
            StringComparison.Ordinal);
        var signed = UpdateContractTestData.Sign(Encoding.UTF8.GetBytes(duplicated));

        var exception = Assert.Throws<UpdateContractException>(() => Verify(signed));

        Assert.Equal("duplicate_json_property", exception.Code);
    }

    [Theory]
    [InlineData("bom")]
    [InlineData("comment")]
    [InlineData("trailing-comma")]
    public void StrictManifestJsonRejectsNonCanonicalSyntaxAfterSignatureVerification(string mutation)
    {
        var valid = JsonSerializer.SerializeToUtf8Bytes(UpdateContractTestData.CreateManifest());
        var validText = Encoding.UTF8.GetString(valid);
        byte[] invalid = mutation switch
        {
            "bom" => new byte[] { 0xef, 0xbb, 0xbf }.Concat(valid).ToArray(),
            "comment" => Encoding.UTF8.GetBytes(validText.Insert(1, "/*comment*/")),
            "trailing-comma" => Encoding.UTF8.GetBytes(validText[..^1] + ",}"),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var signed = SignArbitraryBytes(invalid);

        var exception = Assert.Throws<UpdateContractException>(() => Verify(signed));

        Assert.Equal("manifest_json", exception.Code);
    }

    [Fact]
    public void UnknownManifestPropertyIsRejected()
    {
        var manifest = UpdateContractTestData.CreateManifest();
        manifest["publicKey"] = "never-trusted";

        var exception = Assert.Throws<UpdateContractException>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.Equal("manifest_json", exception.Code);
    }

    [Theory]
    [InlineData("repository", "Other/Repository", "repository")]
    [InlineData("channel", "beta", "channel")]
    [InlineData("generatedAtUtc", "2026-07-21T04:00:00+04:00", "generated_at")]
    public void RootIdentityAndDateFieldsAreStrict(string property, string value, string expectedCode)
    {
        var manifest = UpdateContractTestData.CreateManifest();
        manifest[property] = value;

        var exception = Assert.Throws<UpdateContractException>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void UnknownSchemaVersionIsRejected()
    {
        var manifest = UpdateContractTestData.CreateManifest();
        manifest["schemaVersion"] = 2;

        var exception = Assert.Throws<UpdateContractException>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.Equal("manifest_schema", exception.Code);
    }

    [Theory]
    [InlineData("1.4.0-alpha")]
    [InlineData("1.4.0+build.1")]
    [InlineData("01.4.0")]
    public void StableManifestRejectsNonCanonicalVersions(string version)
    {
        var manifest = UpdateContractTestData.CreateManifest(version);

        var exception = Assert.ThrowsAny<Exception>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.True(exception is UpdateContractException or FormatException);
    }

    [Fact]
    public void TagMustMatchVersion()
    {
        var manifest = UpdateContractTestData.CreateManifest();
        manifest["release"]!["tag"] = "v1.4.1";

        var exception = Assert.Throws<UpdateContractException>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.Equal("tag", exception.Code);
    }

    [Theory]
    [InlineData("../package.zip")]
    [InlineData("folder/package.zip")]
    [InlineData("folder\\package.zip")]
    [InlineData("package\u0001.zip")]
    public void PackageNamesRejectPathsTraversalAndControls(string fileName)
    {
        var manifest = UpdateContractTestData.CreateManifest();
        manifest["assets"]![0]!["fileName"] = fileName;

        var exception = Assert.Throws<UpdateContractException>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.Equal("package_file", exception.Code);
    }

    [Fact]
    public void PackageNameMustExactlyMatchVersionAndVariant()
    {
        var manifest = UpdateContractTestData.CreateManifest();
        manifest["assets"]![0]!["fileName"] = "PimaxVrcSupervisor-v1.4.0-win-x64-other.zip";

        var exception = Assert.Throws<UpdateContractException>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.Equal("package_file", exception.Code);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1073741825L)]
    public void PackageSizeMustBePositiveAndBounded(long size)
    {
        var manifest = UpdateContractTestData.CreateManifest();
        manifest["assets"]![0]!["sizeBytes"] = size;

        var exception = Assert.Throws<UpdateContractException>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.Equal("package_size", exception.Code);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Sha256MustBeExactly64LowercaseHexCharacters(string hash)
    {
        var manifest = UpdateContractTestData.CreateManifest();
        manifest["assets"]![0]!["sha256"] = hash;

        var exception = Assert.Throws<UpdateContractException>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.Equal("package_hash", exception.Code);
    }

    [Fact]
    public void DuplicatePackageVariantIsRejected()
    {
        var manifest = UpdateContractTestData.CreateManifest();
        manifest["assets"]![1]!["variant"] = "with-dotnet9";

        var exception = Assert.Throws<UpdateContractException>(() => Verify(UpdateContractTestData.Sign(manifest)));

        Assert.Equal("duplicate_variant", exception.Code);
    }

    [Theory]
    [InlineData("{\"runtimeOptions\":{\"includedFrameworks\":[]}}", "WithDotnet9")]
    [InlineData("{\"runtimeOptions\":{\"framework\":{}}}", "NoDotnet9")]
    public void InstalledVariantIsDetectedFromRuntimeConfiguration(string json, string expected)
        => Assert.Equal(expected, InstalledPackageVariantDetector.Detect(Encoding.UTF8.GetBytes(json)).ToString());

    [Fact]
    public void AmbiguousRuntimeConfigurationIsRejected()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"runtimeOptions\":{\"framework\":{},\"includedFrameworks\":[]}}");

        var exception = Assert.Throws<UpdateContractException>(() => InstalledPackageVariantDetector.Detect(bytes));

        Assert.Equal("installed_variant", exception.Code);
    }

    [Fact]
    public void ProductionTrustRegistryContainsNoTestKeys()
    {
        var production = ProductionUpdateTrustRoots.CreateTrustStore();

        Assert.Equal(0, production.Count);
        Assert.False(production.TryGet(UpdateContractTestData.KeyId, out _));
    }

    private static ValidatedUpdateManifest Verify(SignedUpdateTestData signed)
        => UpdateManifestVerifier.VerifyAndParse(
            signed.ManifestBytes,
            signed.SignatureEnvelopeBytes,
            signed.TrustStore,
            UpdatePackageVariant.WithDotnet9);

    private static SignedUpdateTestData SignArbitraryBytes(byte[] bytes)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            manifestFile = "PimaxVrcSupervisor-v1.4.0-update-manifest-v1.json",
            manifestSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            signatures = new[]
            {
                new
                {
                    keyId = UpdateContractTestData.KeyId,
                    algorithm = UpdateManifestConstants.SignatureAlgorithm,
                    signatureBase64 = Convert.ToBase64String(signature)
                }
            }
        });
        return new SignedUpdateTestData(
            bytes,
            envelope,
            new UpdateTrustStore([new UpdateTrustRoot(UpdateContractTestData.KeyId, key.ExportSubjectPublicKeyInfo())]));
    }
}
