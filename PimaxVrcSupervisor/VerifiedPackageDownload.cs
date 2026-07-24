using Microsoft.Win32.SafeHandles;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PimaxVrcSupervisor.Updates;

internal sealed record VerifiedPackageDownloadOptions
{
    public long HardMaximumPackageBytes { get; init; } = UpdateManifestConstants.MaximumPackageBytes;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaximumRedirects { get; init; } = 3;
    public string UserAgent { get; init; } = "PimaxVrcSupervisor-VerifiedPackageDownload/1";

    public void Validate()
    {
        if (HardMaximumPackageBytes <= 0 || HardMaximumPackageBytes > UpdateManifestConstants.MaximumPackageBytes
            || RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(15)
            || IdleTimeout <= TimeSpan.Zero || IdleTimeout > TimeSpan.FromMinutes(5)
            || MaximumRedirects < 0 || MaximumRedirects > 5
            || UserAgent.Length is 0 or > 128 || UserAgent.Any(char.IsControl)
            || !ProductInfoHeaderValue.TryParse(UserAgent, out _))
        {
            throw new ArgumentException("The verified package download options are invalid.");
        }
    }
}

internal sealed record VerifiedPackageRecordV1
{
    [JsonPropertyName("schemaVersion"), JsonRequired] public required int SchemaVersion { get; init; }
    [JsonPropertyName("operationId"), JsonRequired] public required string OperationId { get; init; }
    [JsonPropertyName("repository"), JsonRequired] public required string Repository { get; init; }
    [JsonPropertyName("tag"), JsonRequired] public required string Tag { get; init; }
    [JsonPropertyName("version"), JsonRequired] public required string Version { get; init; }
    [JsonPropertyName("channel"), JsonRequired] public required UpdateChannel Channel { get; init; }
    [JsonPropertyName("variant"), JsonRequired] public required UpdatePackageVariant Variant { get; init; }
    [JsonPropertyName("filename"), JsonRequired] public required string Filename { get; init; }
    [JsonPropertyName("expectedSize"), JsonRequired] public required long ExpectedSize { get; init; }
    [JsonPropertyName("actualSize"), JsonRequired] public required long ActualSize { get; init; }
    [JsonPropertyName("expectedSha256"), JsonRequired] public required string ExpectedSha256 { get; init; }
    [JsonPropertyName("actualSha256"), JsonRequired] public required string ActualSha256 { get; init; }
    [JsonPropertyName("signedManifestSha256"), JsonRequired] public required string SignedManifestSha256 { get; init; }
    [JsonPropertyName("signatureKeyId"), JsonRequired] public required string SignatureKeyId { get; init; }
    [JsonPropertyName("releaseUrl"), JsonRequired] public required string ReleaseUrl { get; init; }
    [JsonPropertyName("verifiedAt"), JsonRequired] public required DateTimeOffset VerifiedAt { get; init; }
    [JsonPropertyName("localPackageRelativePath"), JsonRequired] public required string LocalPackageRelativePath { get; init; }
    [JsonPropertyName("state"), JsonRequired] public required string State { get; init; }
}

internal sealed record VerifiedReleaseEvidenceV1
{
    [JsonPropertyName("schemaVersion"), JsonRequired] public required int SchemaVersion { get; init; }
    [JsonPropertyName("repository"), JsonRequired] public required string Repository { get; init; }
    [JsonPropertyName("channel"), JsonRequired] public required string Channel { get; init; }
    [JsonPropertyName("tag"), JsonRequired] public required string Tag { get; init; }
    [JsonPropertyName("version"), JsonRequired] public required string Version { get; init; }
    [JsonPropertyName("releaseCommitSha"), JsonRequired] public required string ReleaseCommitSha { get; init; }
    [JsonPropertyName("releaseUrl"), JsonRequired] public required string ReleaseUrl { get; init; }
    [JsonPropertyName("releaseSequence"), JsonRequired] public required long ReleaseSequence { get; init; }
    [JsonPropertyName("variant"), JsonRequired] public required UpdatePackageVariant Variant { get; init; }
    [JsonPropertyName("packageFilename"), JsonRequired] public required string PackageFilename { get; init; }
    [JsonPropertyName("packageSize"), JsonRequired] public required long PackageSize { get; init; }
    [JsonPropertyName("packageSha256"), JsonRequired] public required string PackageSha256 { get; init; }
    [JsonPropertyName("signatureKeyId"), JsonRequired] public required string SignatureKeyId { get; init; }
    [JsonPropertyName("signatureAlgorithm"), JsonRequired] public required string SignatureAlgorithm { get; init; }
    [JsonPropertyName("manifestFilename"), JsonRequired] public required string ManifestFilename { get; init; }
    [JsonPropertyName("manifestSha256"), JsonRequired] public required string ManifestSha256 { get; init; }
    [JsonPropertyName("signatureFilename"), JsonRequired] public required string SignatureFilename { get; init; }
    [JsonPropertyName("signatureSha256"), JsonRequired] public required string SignatureSha256 { get; init; }
}

internal sealed record VerifiedPackageJournalV1
{
    [JsonPropertyName("schemaVersion"), JsonRequired] public required int SchemaVersion { get; init; }
    [JsonPropertyName("operationId"), JsonRequired] public required string OperationId { get; init; }
    [JsonPropertyName("repository"), JsonRequired] public required string Repository { get; init; }
    [JsonPropertyName("channel"), JsonRequired] public required UpdateChannel Channel { get; init; }
    [JsonPropertyName("tag"), JsonRequired] public required string Tag { get; init; }
    [JsonPropertyName("version"), JsonRequired] public required string Version { get; init; }
    [JsonPropertyName("variant"), JsonRequired] public required UpdatePackageVariant Variant { get; init; }
    [JsonPropertyName("filename"), JsonRequired] public required string Filename { get; init; }
    [JsonPropertyName("expectedSize"), JsonRequired] public required long ExpectedSize { get; init; }
    [JsonPropertyName("expectedSha256"), JsonRequired] public required string ExpectedSha256 { get; init; }
    [JsonPropertyName("signedManifestSha256"), JsonRequired] public required string SignedManifestSha256 { get; init; }
    [JsonPropertyName("signatureKeyId"), JsonRequired] public required string SignatureKeyId { get; init; }
    [JsonPropertyName("releaseUrl"), JsonRequired] public required string ReleaseUrl { get; init; }
    [JsonPropertyName("partialFilename"), JsonRequired] public required string? PartialFilename { get; init; }
    [JsonPropertyName("finalFilename"), JsonRequired] public required string? FinalFilename { get; init; }
    [JsonPropertyName("partialIdentity"), JsonRequired] public required NativeFileIdentity? PartialIdentity { get; init; }
    [JsonPropertyName("finalIdentity"), JsonRequired] public required NativeFileIdentity? FinalIdentity { get; init; }
    [JsonPropertyName("state"), JsonRequired] public required VerifiedPackageJournalState State { get; init; }
    [JsonPropertyName("updatedAt"), JsonRequired] public required DateTimeOffset UpdatedAt { get; init; }
}

internal enum VerifiedPackageJournalState
{
    PartialCreated,
    Downloading,
    PartialVerified,
    PromotedPendingRecord,
    DownloadedAndVerified,
    Failed,
    Cancelled
}

internal enum UpdateDownloadFaultPoint
{
    AfterInitialJournalDurability,
    DuringDownloading,
    AfterPartialVerificationJournalDurability,
    BeforePromotion,
    AfterNativePromotion,
    BeforeFinalRecordWrite,
    DuringFinalRecordWrite,
    AfterFinalRecordDurabilityBeforeJournalRetirement
}

internal interface IUpdateDownloadFaultInjector
{
    void ThrowIfRequested(UpdateDownloadFaultPoint point);
}

internal sealed class NullUpdateDownloadFaultInjector : IUpdateDownloadFaultInjector
{
    public static NullUpdateDownloadFaultInjector Instance { get; } = new();
    public void ThrowIfRequested(UpdateDownloadFaultPoint point) { }
}

internal sealed record VerifiedPackageLoadResult(VerifiedPackageRecordV1? Record, bool CorruptionDetected);

internal sealed record VerifiedPackageDownloadResult(bool CompletedSuccessfully, string ResultCode, VerifiedPackageRecordV1? Record)
{
    public static VerifiedPackageDownloadResult Failure(string code) => new(false, code, null);
}

internal sealed class VerifiedPackageStore
{
    public const int SchemaVersion = 1;
    public const int MaximumRecordBytes = 16 * 1024;
    private static readonly TimeSpan MaximumJournalAge = TimeSpan.FromDays(7);
    public const string RecordFileName = "verified-package-v1.json";
    internal const string PreviousRecordFileName = "verified-package-v1.previous.json";
    internal const string JournalFileName = "verified-package-v1.journal.json";
    internal const string PreviousJournalFileName = "verified-package-v1.journal.previous.json";
    internal const string ReleaseEvidenceFileName = "verified-release-evidence-v1.json";
    internal const string PreviousReleaseEvidenceFileName = "verified-release-evidence-v1.previous.json";
    internal const string ReleaseManifestFileName = "verified-release-manifest-v1.bin";
    internal const string PreviousReleaseManifestFileName = "verified-release-manifest-v1.previous.bin";
    internal const string ReleaseSignatureFileName = "verified-release-signature-v1.bin";
    internal const string PreviousReleaseSignatureFileName = "verified-release-signature-v1.previous.bin";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly UpdatePackageVariant _installedVariant;
    private readonly HardenedUpdateStagingStore _staging;
    private readonly IUpdateDownloadFaultInjector _faults;
    private readonly Action? _hashStartedForTests;
    private readonly Action<string>? _beforeRecoveryDeleteForTests;

    public VerifiedPackageStore(
        UpdatePackageVariant installedVariant,
        string? updateDirectory = null,
        IUpdateDownloadFaultInjector? faults = null,
        Action? hashStartedForTests = null,
        Action<string>? beforeRecoveryDeleteForTests = null)
    {
        _installedVariant = installedVariant;
        UpdateDirectory = Path.GetFullPath(updateDirectory ?? UpdateStateStore.GetDefaultUpdateDirectory());
        PackagesDirectory = Path.Combine(UpdateDirectory, "Packages");
        _staging = new HardenedUpdateStagingStore(installedVariant, UpdateDirectory);
        _faults = faults ?? NullUpdateDownloadFaultInjector.Instance;
        _hashStartedForTests = hashStartedForTests;
        _beforeRecoveryDeleteForTests = beforeRecoveryDeleteForTests;
    }

    public string UpdateDirectory { get; }
    public string PackagesDirectory { get; }
    internal UpdatePackageVariant InstalledVariant => _installedVariant;

    // These are UI/test projections only. All authoritative I/O beneath Packages is handle-relative.
    public string GetPackagePath(VerifiedPackageRecordV1 record)
    {
        ValidateRecord(record, _installedVariant);
        var expected = GetRelativePackagePath(record.Version, record.Variant, record.Filename);
        if (!string.Equals(record.LocalPackageRelativePath, expected, StringComparison.Ordinal))
        {
            throw new UpdateContractException("package_record_path", "The verified package record path is inconsistent.");
        }

        return ResolveProjection(expected);
    }

    public string GetRecordPath(VerifiedPackageRecordV1 record) => Path.Combine(Path.GetDirectoryName(GetPackagePath(record))!, RecordFileName);

    public string GetFinalPath(object authorityCapability)
    {
        var authority = RequireAuthority(authorityCapability);
        ValidateAuthority(authority);
        return ResolveProjection(GetRelativePackagePath(authority.Version, authority.Variant, authority.FileName));
    }

    internal HardenedPackageDirectory OpenDirectory(object authorityCapability)
    {
        var authority = RequireAuthority(authorityCapability);
        return OpenDirectoryCore(authority);
    }

    private HardenedPackageDirectory OpenDirectoryCore(VerifiedPackageAuthorityView authority)
    {
        ValidateAuthority(authority);
        return _staging.OpenPackageDirectory(authority.Version, authority.Variant);
    }

    internal async Task<VerifiedPackageRecordV1?> RecoverAsync(
        string operationId,
        object authorityCapability,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken)
    {
        var authority = RequireAuthority(authorityCapability);
        ValidateAuthority(authority);
        ValidateOperationId(operationId);
        using var directory = OpenDirectoryCore(authority);
        var partialName = authority.FileName + ".partial";

        var journalRead = ReadJournal(directory, authority);
        var journal = journalRead.Journal;
        if (journal is not null)
        {
            if (journalRead.UsedPrevious)
            {
                await RepairCurrentJournalAsync(directory, journal, cancellationToken).ConfigureAwait(false);
            }

            if (journal.UpdatedAt > verifiedAt + TimeSpan.FromMinutes(5)
                || journal.UpdatedAt < verifiedAt - MaximumJournalAge)
            {
                throw new UpdateContractException("package_journal", "The package journal timestamp is outside the bounded verifier-clock window.");
            }

            return await RecoverJournalAsync(directory, journal, authority, verifiedAt, cancellationToken).ConfigureAwait(false);
        }

        // A partial is never authoritative. It is always confined to this exact pinned package directory.
        directory.TryDeleteExactFile(partialName);

        var (record, recordHandle, recordName) = TryOpenRecordForRecovery(directory, authority);
        using (recordHandle)
        {
            if (record is not null && TryVerifyRecordPackage(directory, record))
            {
                return record;
            }

            if (recordHandle is not null)
            {
                DeleteRecoveryFile(directory, recordHandle, directory.CaptureFileIdentity(recordHandle), recordName!);
            }
        }

        using var package = directory.TryOpenExistingFileForRecovery(authority.FileName);
        if (package is null)
        {
            return null;
        }

        // A final package without a valid final record is reusable only after this invocation has
        // received a fresh verifier-created authority and has rehashed the still-pinned file.
        if (directory.GetPinnedFileLength(package) != authority.ExpectedSize
            || !string.Equals(HashPinnedFile(directory, package, authority.ExpectedSize), authority.ExpectedSha256, StringComparison.Ordinal))
        {
            DeleteRecoveryFile(directory, package, directory.CaptureFileIdentity(package), authority.FileName);
            return null;
        }

        await PersistCoreAsync(operationId, authority, directory, package, authority.ExpectedSize, authority.ExpectedSha256, verifiedAt, cancellationToken).ConfigureAwait(false);
        return BuildRecord(operationId, authority, authority.ExpectedSize, authority.ExpectedSha256, verifiedAt);
    }

    internal Task WriteJournalAsync(
        string operationId,
        object authorityCapability,
        HardenedPackageDirectory directory,
        VerifiedPackageJournalState state,
        string? partialName,
        NativeFileIdentity? partialIdentity,
        NativeFileIdentity? finalIdentity,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
        => WriteJournalCoreAsync(
            operationId,
            RequireAuthority(authorityCapability),
            directory,
            state,
            partialName,
            partialIdentity,
            finalIdentity,
            updatedAt,
            cancellationToken);

    private async Task WriteJournalCoreAsync(
        string operationId,
        VerifiedPackageAuthorityView authority,
        HardenedPackageDirectory directory,
        VerifiedPackageJournalState state,
        string? partialName,
        NativeFileIdentity? partialIdentity,
        NativeFileIdentity? finalIdentity,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        ValidateAuthority(authority);
        ValidateOperationId(operationId);
        var finalName = state is VerifiedPackageJournalState.PromotedPendingRecord or VerifiedPackageJournalState.DownloadedAndVerified
            || (state is VerifiedPackageJournalState.Failed or VerifiedPackageJournalState.Cancelled && finalIdentity is not null)
                ? authority.FileName
                : null;
        ValidateJournalTopology(state, authority, partialName, finalName, partialIdentity, finalIdentity);
        var priorJournal = ReadJournal(directory, authority);
        var previous = priorJournal.Journal;
        if (previous is not null && priorJournal.UsedPrevious)
        {
            await RepairCurrentJournalAsync(directory, previous, cancellationToken).ConfigureAwait(false);
        }
        if ((previous is null && state != VerifiedPackageJournalState.PartialCreated)
            || (previous is not null
                && (!string.Equals(previous.OperationId, operationId, StringComparison.Ordinal)
                    || !IsLegalJournalTransition(previous.State, state))))
        {
            throw new UpdateContractException("package_journal_transition", "The package journal transition is not legal for this authority transaction.");
        }
        if (previous is not null)
        {
            ValidateJournalTransition(previous, state, partialIdentity, finalIdentity, updatedAt);
        }
        var journal = new VerifiedPackageJournalV1
        {
            SchemaVersion = SchemaVersion,
            OperationId = operationId,
            Repository = authority.Repository,
            Channel = UpdateChannel.Stable,
            Tag = authority.Tag,
            Version = authority.Version,
            Variant = authority.Variant,
            Filename = authority.FileName,
            ExpectedSize = authority.ExpectedSize,
            ExpectedSha256 = authority.ExpectedSha256,
            SignedManifestSha256 = authority.SignedManifestSha256,
            SignatureKeyId = authority.SignatureKeyId,
            ReleaseUrl = authority.ReleaseUrl,
            PartialFilename = partialName,
            FinalFilename = finalName,
            PartialIdentity = partialIdentity,
            FinalIdentity = finalIdentity,
            State = state,
            UpdatedAt = updatedAt
        };
        await WritePinnedJsonAsync(directory, JournalFileName, journal, replaceExisting: true, cancellationToken).ConfigureAwait(false);
    }

    internal Task<VerifiedPackageRecordV1> PersistAsync(
        string operationId,
        object authorityCapability,
        HardenedPackageDirectory directory,
        SafeFileHandle promotedPackage,
        long actualSize,
        string actualSha256,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken)
        => PersistCoreAsync(
            operationId,
            RequireAuthority(authorityCapability),
            directory,
            promotedPackage,
            actualSize,
            actualSha256,
            verifiedAt,
            cancellationToken);

    private async Task<VerifiedPackageRecordV1> PersistCoreAsync(
        string operationId,
        VerifiedPackageAuthorityView authority,
        HardenedPackageDirectory directory,
        SafeFileHandle promotedPackage,
        long actualSize,
        string actualSha256,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken)
    {
        ValidateAuthority(authority);
        ValidateOperationId(operationId);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(promotedPackage);
        if (actualSize != authority.ExpectedSize || !string.Equals(actualSha256, authority.ExpectedSha256, StringComparison.Ordinal)
            || directory.GetPinnedFileLength(promotedPackage) != actualSize)
        {
            throw new UpdateContractException("package_record_invalid", "Verified package evidence does not match its pinned promoted file.");
        }

        directory.VerifyFinalFilePath(promotedPackage, authority.FileName, "package_record");
        await PersistExactSignedEvidenceAsync(authority, directory, cancellationToken).ConfigureAwait(false);
        var record = BuildRecord(operationId, authority, actualSize, actualSha256, verifiedAt);
        _faults.ThrowIfRequested(UpdateDownloadFaultPoint.BeforeFinalRecordWrite);
        await WritePinnedJsonAsync(
            directory,
            RecordFileName,
            record,
            replaceExisting: true,
            cancellationToken,
            () => _faults.ThrowIfRequested(UpdateDownloadFaultPoint.DuringFinalRecordWrite)).ConfigureAwait(false);
        return record;
    }

    private async Task PersistExactSignedEvidenceAsync(
        VerifiedPackageAuthorityView authority,
        HardenedPackageDirectory directory,
        CancellationToken cancellationToken)
    {
        if (authority.SignedManifestBytes.IsDefaultOrEmpty || authority.SignatureEnvelopeBytes.IsDefaultOrEmpty)
        {
            throw new UpdateContractException("package_evidence", "The verifier-created authority does not retain exact signed release evidence.");
        }

        await WritePinnedBytesAsync(directory, ReleaseManifestFileName, authority.SignedManifestBytes.AsMemory(), UpdateManifestConstants.MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
        await WritePinnedBytesAsync(directory, ReleaseSignatureFileName, authority.SignatureEnvelopeBytes.AsMemory(), UpdateManifestConstants.MaximumSignatureEnvelopeBytes, cancellationToken).ConfigureAwait(false);
        var evidence = new VerifiedReleaseEvidenceV1
        {
            SchemaVersion = SchemaVersion,
            Repository = authority.Repository,
            Channel = authority.Channel,
            Tag = authority.Tag,
            Version = authority.Version,
            ReleaseCommitSha = authority.ReleaseCommitSha,
            ReleaseUrl = authority.ReleaseUrl,
            ReleaseSequence = authority.ReleaseSequence,
            Variant = authority.Variant,
            PackageFilename = authority.FileName,
            PackageSize = authority.ExpectedSize,
            PackageSha256 = authority.ExpectedSha256,
            SignatureKeyId = authority.SignatureKeyId,
            SignatureAlgorithm = authority.SignatureAlgorithm,
            ManifestFilename = ReleaseManifestFileName,
            ManifestSha256 = authority.SignedManifestSha256,
            SignatureFilename = ReleaseSignatureFileName,
            SignatureSha256 = Convert.ToHexString(SHA256.HashData(authority.SignatureEnvelopeBytes.AsSpan())).ToLowerInvariant()
        };
        await WritePinnedJsonAsync(directory, ReleaseEvidenceFileName, evidence, replaceExisting: true, cancellationToken).ConfigureAwait(false);
        VerifyPersistedSignedEvidence(directory, authority);
    }

    private void VerifyPersistedSignedEvidence(HardenedPackageDirectory directory, VerifiedPackageAuthorityView authority)
    {
        try
        {
            VerifyPersistedSignedEvidenceCandidate(
                directory,
                authority,
                ReleaseEvidenceFileName,
                ReleaseManifestFileName,
                ReleaseSignatureFileName);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UpdateContractException or ArgumentException)
        {
            VerifyPersistedSignedEvidenceCandidate(
                directory,
                authority,
                PreviousReleaseEvidenceFileName,
                PreviousReleaseManifestFileName,
                PreviousReleaseSignatureFileName);
        }
    }

    private void VerifyPersistedSignedEvidenceCandidate(
        HardenedPackageDirectory directory,
        VerifiedPackageAuthorityView authority,
        string evidenceFileName,
        string manifestFileName,
        string signatureFileName)
    {
        using var evidenceHandle = directory.TryOpenExistingFile(evidenceFileName, write: false)
            ?? throw new UpdateContractException("package_evidence", "The persisted signed evidence record is missing.");
        var evidence = DeserializeBounded<VerifiedReleaseEvidenceV1>(directory, evidenceHandle, "package_evidence_json");
        var manifest = ReadPinnedBytes(directory, manifestFileName, UpdateManifestConstants.MaximumManifestBytes, "package_manifest_evidence");
        var signature = ReadPinnedBytes(directory, signatureFileName, UpdateManifestConstants.MaximumSignatureEnvelopeBytes, "package_signature_evidence");
        ValidateEvidence(evidence, authority, manifest, signature);
#if !PHASE33B_TEST_TRUST
        var verified = UpdateManifestVerifier.VerifyAndParse(manifest, signature, ProductionUpdateTrustRoots.CreateTrustStore(), _installedVariant);
        if (verified.Manifest.ReleaseSequence != authority.ReleaseSequence
            || !string.Equals(verified.Manifest.Release.CommitSha, authority.ReleaseCommitSha, StringComparison.Ordinal)
            || !string.Equals(verified.SelectedPackage.FileName, authority.FileName, StringComparison.Ordinal)
            || verified.SelectedPackage.SizeBytes != authority.ExpectedSize
            || !string.Equals(verified.SelectedPackage.Sha256, authority.ExpectedSha256, StringComparison.Ordinal)
            || !string.Equals(verified.SignatureKeyId, authority.SignatureKeyId, StringComparison.Ordinal))
        {
            throw new UpdateContractException("package_evidence", "Offline production-root evidence re-verification did not bind to the verifier-created package authority.");
        }
#endif
    }

    public VerifiedPackageLoadResult Load(string version, UpdatePackageVariant variant, string filename)
    {
        try
        {
            ValidateRequestedLocation(version, variant, filename);
            using var directory = _staging.OpenPackageDirectory(version, variant);
            using var recordHandle = directory.TryOpenExistingFile(RecordFileName, write: false);
            if (recordHandle is null)
            {
                using var package = directory.TryOpenExistingFile(filename, write: false);
                return new VerifiedPackageLoadResult(null, package is not null);
            }

            var record = DeserializeBounded<VerifiedPackageRecordV1>(directory, recordHandle, "package_record_json");
            ValidateRecord(record, _installedVariant);
            if (!string.Equals(record.Version, version, StringComparison.Ordinal)
                || record.Variant != variant
                || !string.Equals(record.Filename, filename, StringComparison.Ordinal)
                || !string.Equals(record.LocalPackageRelativePath, GetRelativePackagePath(version, variant, filename), StringComparison.Ordinal))
            {
                return new VerifiedPackageLoadResult(null, true);
            }

            // This pathname-only API is a descriptive projection, never install authority.
            // The record and package are user-writable evidence until a caller supplies a
            // freshly verifier-created authority through the recovery/install boundary.
            return TryVerifyRecordPackage(directory, record)
                ? new VerifiedPackageLoadResult(null, false)
                : new VerifiedPackageLoadResult(null, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or UpdateContractException or ArgumentException)
        {
            return new VerifiedPackageLoadResult(null, true);
        }
    }

    private static async Task WritePinnedJsonAsync<T>(
        HardenedPackageDirectory directory,
        string finalName,
        T value,
        bool replaceExisting,
        CancellationToken cancellationToken,
        Action? afterTemporaryWrite = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (bytes.Length is <= 0 or > MaximumRecordBytes)
        {
            throw new UpdateContractException("package_record_size", "The pinned package metadata exceeds its size bound.");
        }

        if (replaceExisting)
        {
            await PinnedDurableMetadata.WriteAsync(
                directory,
                finalName,
                PreviousMetadataName(finalName),
                bytes,
                MaximumRecordBytes,
                cancellationToken,
                afterTemporaryWrite).ConfigureAwait(false);
            return;
        }

        var temporaryName = "." + finalName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var temporary = directory.CreateNewFile(temporaryName, write: true);
        try
        {
            await RandomAccess.WriteAsync(temporary, bytes, 0, cancellationToken).ConfigureAwait(false);
            afterTemporaryWrite?.Invoke();
            directory.FlushPinnedFile(temporary);
            if (replaceExisting)
            {
                directory.ReplacePinnedFile(temporary, finalName);
            }
            else
            {
                directory.RenamePinnedFile(temporary, finalName);
            }
            directory.FlushPinnedFile(temporary);
            directory.FlushPinnedDirectory();
        }
        catch (Exception primary)
        {
            try
            {
                directory.DeletePinnedFile(temporary, directory.CaptureFileIdentity(temporary), temporaryName);
                directory.FlushPinnedDirectory();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Pinned package metadata write and exact temporary cleanup both failed.", primary, cleanup);
            }
            throw;
        }
    }

    private static async Task WritePinnedBytesAsync(HardenedPackageDirectory directory, string finalName, ReadOnlyMemory<byte> bytes, int maximumBytes, CancellationToken cancellationToken)
    {
        if (bytes.Length <= 0 || bytes.Length > maximumBytes)
        {
            throw new UpdateContractException("package_evidence_size", "The exact signed evidence exceeds its bounded size.");
        }

        await PinnedDurableMetadata.WriteAsync(
            directory,
            finalName,
            PreviousMetadataName(finalName),
            bytes,
            maximumBytes,
            cancellationToken).ConfigureAwait(false);
    }

    private static string PreviousMetadataName(string currentName)
        => currentName switch
        {
            RecordFileName => PreviousRecordFileName,
            JournalFileName => PreviousJournalFileName,
            ReleaseEvidenceFileName => PreviousReleaseEvidenceFileName,
            ReleaseManifestFileName => PreviousReleaseManifestFileName,
            ReleaseSignatureFileName => PreviousReleaseSignatureFileName,
            _ => throw new UpdateContractException("package_metadata_name", "The durable metadata filename is not part of the fixed Phase 33B contract.")
        };

    private static byte[] ReadPinnedBytes(HardenedPackageDirectory directory, string fileName, int maximumBytes, string code)
    {
        using var handle = directory.TryOpenExistingFile(fileName, write: false)
            ?? throw new UpdateContractException("package_evidence", "A required exact signed evidence file is missing.");
        var length = directory.GetPinnedFileLength(handle);
        if (length <= 0 || length > maximumBytes)
        {
            throw new UpdateContractException("package_evidence_size", "A persisted exact signed evidence file exceeds its bounded size.");
        }
        var bytes = new byte[checked((int)length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);
            if (read == 0)
            {
                throw new IOException("A pinned exact signed evidence file ended before its advertised length.");
            }
            offset += read;
        }
        return bytes;
    }

    private (VerifiedPackageRecordV1? Record, SafeFileHandle? Handle, string? Name) TryOpenRecordForRecovery(
        HardenedPackageDirectory directory,
        VerifiedPackageAuthorityView authority)
    {
        try
        {
            var current = TryOpenRecordCandidateForRecovery(directory, authority, RecordFileName);
            if (current.Record is not null)
            {
                return current;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UpdateContractException or ArgumentException)
        {
            try
            {
                var previous = TryOpenRecordCandidateForRecovery(directory, authority, PreviousRecordFileName);
                if (previous.Record is not null)
                {
                    return previous;
                }
            }
            catch (Exception previousException) when (previousException is IOException or JsonException or UpdateContractException or ArgumentException)
            {
                throw new AggregateException("Both current and previous verified package records are invalid.", exception, previousException);
            }

            throw;
        }

        return TryOpenRecordCandidateForRecovery(directory, authority, PreviousRecordFileName);
    }

    private (VerifiedPackageRecordV1? Record, SafeFileHandle? Handle, string? Name) TryOpenRecordCandidateForRecovery(
        HardenedPackageDirectory directory,
        VerifiedPackageAuthorityView authority,
        string fileName)
    {
        var recordHandle = directory.TryOpenExistingFileForRecovery(fileName);
        if (recordHandle is null)
        {
            return (null, null, null);
        }

        try
        {
            var record = DeserializeBounded<VerifiedPackageRecordV1>(directory, recordHandle, "package_record_json");
            ValidateRecord(record, _installedVariant);
            if (!RecordMatchesAuthority(record, authority))
            {
                throw new UpdateContractException("package_record", "The verified package record does not bind to the current authority.");
            }

            VerifyPersistedSignedEvidence(directory, authority);
            return (record, recordHandle, fileName);
        }
        catch
        {
            recordHandle.Dispose();
            throw;
        }
    }

    private VerifiedPackageRecordV1? TryReadRecord(HardenedPackageDirectory directory, VerifiedPackageAuthorityView authority)
    {
        var current = TryReadRecordCandidate(directory, authority, RecordFileName);
        return current ?? TryReadRecordCandidate(directory, authority, PreviousRecordFileName);
    }

    private VerifiedPackageRecordV1? TryReadRecordCandidate(
        HardenedPackageDirectory directory,
        VerifiedPackageAuthorityView authority,
        string fileName)
    {
        using var recordHandle = directory.TryOpenExistingFile(fileName, write: false);
        if (recordHandle is null)
        {
            return null;
        }

        try
        {
            var record = DeserializeBounded<VerifiedPackageRecordV1>(directory, recordHandle, "package_record_json");
            ValidateRecord(record, _installedVariant);
            return RecordMatchesAuthority(record, authority) ? record : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UpdateContractException or ArgumentException)
        {
            return null;
        }
    }

    private bool TryVerifyRecordPackage(HardenedPackageDirectory directory, VerifiedPackageRecordV1 record)
    {
        try
        {
            using var package = directory.TryOpenExistingFile(record.Filename, write: false);
            return package is not null && TryVerifyRecordPinnedPackage(directory, package, record);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UpdateContractException)
        {
            return false;
        }
    }

    private bool TryVerifyRecordPinnedPackage(
        HardenedPackageDirectory directory,
        SafeFileHandle package,
        VerifiedPackageRecordV1 record)
    {
        try
        {
            directory.VerifyFinalFilePath(package, record.Filename, "package_record");
            return directory.GetPinnedFileLength(package) == record.ActualSize
                && string.Equals(HashPinnedFile(directory, package, record.ActualSize), record.ActualSha256, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UpdateContractException)
        {
            return false;
        }
    }

    private static T DeserializeBounded<T>(HardenedPackageDirectory directory, SafeFileHandle handle, string code)
    {
        var length = directory.GetPinnedFileLength(handle);
        if (length is <= 0 or > MaximumRecordBytes)
        {
            throw new UpdateContractException("package_record_size", "The pinned package metadata exceeds its size bound.");
        }

        var bytes = new byte[checked((int)length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);
            if (read == 0)
            {
                throw new IOException("The pinned package metadata ended before its advertised length.");
            }

            offset += read;
        }

        StrictUpdateJson.ValidateSyntax(bytes, code);
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new JsonException("The pinned package metadata is null.");
    }

    private string HashPinnedFile(HardenedPackageDirectory directory, SafeFileHandle handle, long maximumBytes)
    {
        var length = directory.GetPinnedFileLength(handle);
        if (maximumBytes is <= 0 or > UpdateManifestConstants.MaximumPackageBytes
            || length is <= 0 or > UpdateManifestConstants.MaximumPackageBytes
            || length != maximumBytes)
        {
            throw new UpdateContractException("package_size", "The pinned package size is not the exact verified positive bounded size before hashing.");
        }

        _hashStartedForTests?.Invoke();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long offset = 0;
        while (offset < length)
        {
            var read = RandomAccess.Read(handle, buffer.AsSpan(0, checked((int)Math.Min(buffer.Length, length - offset))), offset);
            if (read == 0)
            {
                throw new IOException("The pinned package ended before its advertised length.");
            }

            hash.AppendData(buffer, 0, read);
            offset += read;
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private VerifiedPackageRecordV1 BuildRecord(string operationId, VerifiedPackageAuthorityView authority, long actualSize, string actualSha256, DateTimeOffset verifiedAt)
    {
        var record = new VerifiedPackageRecordV1
        {
            SchemaVersion = SchemaVersion,
            OperationId = operationId,
            Repository = authority.Repository,
            Tag = authority.Tag,
            Version = authority.Version,
            Channel = UpdateChannel.Stable,
            Variant = authority.Variant,
            Filename = authority.FileName,
            ExpectedSize = authority.ExpectedSize,
            ActualSize = actualSize,
            ExpectedSha256 = authority.ExpectedSha256,
            ActualSha256 = actualSha256,
            SignedManifestSha256 = authority.SignedManifestSha256,
            SignatureKeyId = authority.SignatureKeyId,
            ReleaseUrl = authority.ReleaseUrl,
            VerifiedAt = verifiedAt,
            LocalPackageRelativePath = GetRelativePackagePath(authority.Version, authority.Variant, authority.FileName),
            State = "downloadedAndVerified"
        };
        ValidateRecord(record, _installedVariant);
        return record;
    }

    private static bool RecordMatchesAuthority(VerifiedPackageRecordV1 record, VerifiedPackageAuthorityView authority)
        => string.Equals(record.Repository, authority.Repository, StringComparison.Ordinal)
            && string.Equals(record.Tag, authority.Tag, StringComparison.Ordinal)
            && string.Equals(record.Version, authority.Version, StringComparison.Ordinal)
            && record.Channel == UpdateChannel.Stable
            && record.Variant == authority.Variant
            && string.Equals(record.Filename, authority.FileName, StringComparison.Ordinal)
            && record.ExpectedSize == authority.ExpectedSize
            && string.Equals(record.ExpectedSha256, authority.ExpectedSha256, StringComparison.Ordinal)
            && string.Equals(record.ActualSha256, authority.ExpectedSha256, StringComparison.Ordinal)
            && string.Equals(record.LocalPackageRelativePath, GetRelativePackagePath(authority.Version, authority.Variant, authority.FileName), StringComparison.Ordinal)
            && string.Equals(record.SignedManifestSha256, authority.SignedManifestSha256, StringComparison.Ordinal)
            && string.Equals(record.SignatureKeyId, authority.SignatureKeyId, StringComparison.Ordinal)
            && string.Equals(record.ReleaseUrl, authority.ReleaseUrl, StringComparison.Ordinal);

    private static void ValidateEvidence(VerifiedReleaseEvidenceV1 evidence, VerifiedPackageAuthorityView authority, ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.SchemaVersion != SchemaVersion
            || !string.Equals(evidence.Repository, authority.Repository, StringComparison.Ordinal)
            || !string.Equals(evidence.Channel, authority.Channel, StringComparison.Ordinal)
            || !string.Equals(evidence.Tag, authority.Tag, StringComparison.Ordinal)
            || !string.Equals(evidence.Version, authority.Version, StringComparison.Ordinal)
            || !string.Equals(evidence.ReleaseCommitSha, authority.ReleaseCommitSha, StringComparison.Ordinal)
            || !string.Equals(evidence.ReleaseUrl, authority.ReleaseUrl, StringComparison.Ordinal)
            || evidence.ReleaseSequence != authority.ReleaseSequence
            || evidence.Variant != authority.Variant
            || !string.Equals(evidence.PackageFilename, authority.FileName, StringComparison.Ordinal)
            || evidence.PackageSize != authority.ExpectedSize
            || !string.Equals(evidence.PackageSha256, authority.ExpectedSha256, StringComparison.Ordinal)
            || !string.Equals(evidence.SignatureKeyId, authority.SignatureKeyId, StringComparison.Ordinal)
            || !string.Equals(evidence.SignatureAlgorithm, UpdateManifestConstants.SignatureAlgorithm, StringComparison.Ordinal)
            || !string.Equals(evidence.ManifestFilename, ReleaseManifestFileName, StringComparison.Ordinal)
            || !string.Equals(evidence.SignatureFilename, ReleaseSignatureFileName, StringComparison.Ordinal)
            || !string.Equals(Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant(), evidence.ManifestSha256, StringComparison.Ordinal)
            || !string.Equals(Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant(), evidence.SignatureSha256, StringComparison.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(manifest, authority.SignedManifestBytes.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(signature, authority.SignatureEnvelopeBytes.AsSpan()))
        {
            throw new UpdateContractException("package_evidence", "The persisted exact signed evidence does not bind to the verifier-created authority.");
        }
    }

    private static VerifiedPackageAuthorityView RequireAuthority(object authorityCapability)
        => GitHubUpdateDiscoveryClient.RequireVerifiedPackageAuthority(authorityCapability);

    private static void ValidateAuthority(VerifiedPackageAuthorityView authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!string.Equals(authority.Repository, UpdateManifestConstants.Repository, StringComparison.Ordinal)
            || !string.Equals(authority.Channel, UpdateManifestConstants.Channel, StringComparison.Ordinal)
            || authority.Variant is not (UpdatePackageVariant.NoDotnet9 or UpdatePackageVariant.WithDotnet9)
            || authority.ExpectedSize is <= 0 or > UpdateManifestConstants.MaximumPackageBytes
            || !string.Equals(authority.Tag, "v" + authority.Version, StringComparison.Ordinal)
            || !string.Equals(authority.ReleaseUrl, $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/{authority.Tag}", StringComparison.Ordinal)
            || authority.BrowserDownloadUri is null)
        {
            throw new UpdateContractException("package_authority", "The verifier-created package authority is invalid.");
        }

        var version = SemanticVersion.Parse(authority.Version);
        if (!version.IsStable || !string.Equals(version.ToString(), authority.Version, StringComparison.Ordinal))
        {
            throw new UpdateContractException("package_authority", "The verifier-created package authority version is invalid.");
        }

        UpdateContractValidation.ValidateSafeFileName(authority.FileName, "package_filename");
        UpdateContractValidation.ValidateSha256(authority.ExpectedSha256, "package_expected_hash");
        UpdateContractValidation.ValidateSha256(authority.SignedManifestSha256, "package_manifest_hash");
        if (!UpdateContractValidation.IsSafeIdentifier(authority.SignatureKeyId, 128))
        {
            throw new UpdateContractException("package_key", "The verifier-created signature key ID is invalid.");
        }
    }

    internal void ThrowIfRequested(UpdateDownloadFaultPoint point)
        => _faults.ThrowIfRequested(point);

    private static void ValidateJournalTopology(
        VerifiedPackageJournalState state,
        VerifiedPackageAuthorityView authority,
        string? partialName,
        string? finalName,
        NativeFileIdentity? partialIdentity,
        NativeFileIdentity? finalIdentity)
    {
        var partialState = state is VerifiedPackageJournalState.PartialCreated
            or VerifiedPackageJournalState.Downloading
            or VerifiedPackageJournalState.PartialVerified;
        var finalState = state is VerifiedPackageJournalState.PromotedPendingRecord
            or VerifiedPackageJournalState.DownloadedAndVerified;
        var terminalState = state is VerifiedPackageJournalState.Failed or VerifiedPackageJournalState.Cancelled;
        var terminalTopologyIsValid = !terminalState
            || (partialIdentity is not null
                ? string.Equals(partialName, authority.FileName + ".partial", StringComparison.Ordinal) && finalName is null && finalIdentity is null
                : finalIdentity is not null
                    ? partialName is null && string.Equals(finalName, authority.FileName, StringComparison.Ordinal)
                    : partialName is null && finalName is null);
        if (!Enum.IsDefined(state)
            || (partialState && (!string.Equals(partialName, authority.FileName + ".partial", StringComparison.Ordinal) || finalName is not null || partialIdentity is null || finalIdentity is not null))
            || (finalState && (partialName is not null || !string.Equals(finalName, authority.FileName, StringComparison.Ordinal) || partialIdentity is not null || finalIdentity is null))
            || (!partialState && !finalState && !terminalState)
            || !terminalTopologyIsValid)
        {
            throw new UpdateContractException("package_journal", "The package journal state topology is invalid.");
        }
    }

    private static bool IsLegalJournalTransition(VerifiedPackageJournalState from, VerifiedPackageJournalState to)
        => (from, to) switch
        {
            (VerifiedPackageJournalState.PartialCreated, VerifiedPackageJournalState.Downloading) => true,
            (VerifiedPackageJournalState.PartialCreated, VerifiedPackageJournalState.Failed) => true,
            (VerifiedPackageJournalState.PartialCreated, VerifiedPackageJournalState.Cancelled) => true,
            (VerifiedPackageJournalState.Downloading, VerifiedPackageJournalState.PartialVerified) => true,
            (VerifiedPackageJournalState.Downloading, VerifiedPackageJournalState.Failed) => true,
            (VerifiedPackageJournalState.Downloading, VerifiedPackageJournalState.Cancelled) => true,
            (VerifiedPackageJournalState.PartialVerified, VerifiedPackageJournalState.PromotedPendingRecord) => true,
            (VerifiedPackageJournalState.PartialVerified, VerifiedPackageJournalState.Failed) => true,
            (VerifiedPackageJournalState.PartialVerified, VerifiedPackageJournalState.Cancelled) => true,
            (VerifiedPackageJournalState.PromotedPendingRecord, VerifiedPackageJournalState.DownloadedAndVerified) => true,
            (VerifiedPackageJournalState.PromotedPendingRecord, VerifiedPackageJournalState.Failed) => true,
            (VerifiedPackageJournalState.PromotedPendingRecord, VerifiedPackageJournalState.Cancelled) => true,
            (VerifiedPackageJournalState.Failed, VerifiedPackageJournalState.DownloadedAndVerified) => true,
            (VerifiedPackageJournalState.Cancelled, VerifiedPackageJournalState.DownloadedAndVerified) => true,
            _ => false
        };

    private static void ValidateJournalTransition(
        VerifiedPackageJournalV1 previous,
        VerifiedPackageJournalState nextState,
        NativeFileIdentity? nextPartialIdentity,
        NativeFileIdentity? nextFinalIdentity,
        DateTimeOffset nextUpdatedAt)
    {
        var identityIsContinuous = (previous.State, nextState) switch
        {
            (VerifiedPackageJournalState.PartialCreated, VerifiedPackageJournalState.Downloading)
                or (VerifiedPackageJournalState.Downloading, VerifiedPackageJournalState.PartialVerified)
                => previous.PartialIdentity == nextPartialIdentity,
            (VerifiedPackageJournalState.PartialVerified, VerifiedPackageJournalState.PromotedPendingRecord)
                => previous.PartialIdentity == nextFinalIdentity,
            (VerifiedPackageJournalState.PromotedPendingRecord, VerifiedPackageJournalState.DownloadedAndVerified)
                => previous.FinalIdentity == nextFinalIdentity,
            (_, VerifiedPackageJournalState.Failed or VerifiedPackageJournalState.Cancelled)
                => (nextPartialIdentity is null || nextPartialIdentity == previous.PartialIdentity)
                    && (nextFinalIdentity is null || nextFinalIdentity == previous.FinalIdentity),
            _ => true
        };
        if (nextUpdatedAt.Offset != TimeSpan.Zero
            || nextUpdatedAt < previous.UpdatedAt
            || !identityIsContinuous)
        {
            throw new UpdateContractException("package_journal_transition", "The package journal transition does not preserve timestamp or object identity continuity.");
        }
    }

    private readonly record struct JournalReadResult(VerifiedPackageJournalV1? Journal, bool UsedPrevious);

    private JournalReadResult ReadJournal(HardenedPackageDirectory directory, VerifiedPackageAuthorityView authority)
    {
        try
        {
            var current = ReadJournalCandidate(directory, authority, JournalFileName);
            if (current is not null)
            {
                return new JournalReadResult(current, UsedPrevious: false);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UpdateContractException or ArgumentException)
        {
            var previous = ReadJournalCandidate(directory, authority, PreviousJournalFileName);
            if (previous is not null)
            {
                return new JournalReadResult(previous, UsedPrevious: true);
            }

            throw;
        }

        return new JournalReadResult(
            ReadJournalCandidate(directory, authority, PreviousJournalFileName),
            UsedPrevious: true);
    }

    private static Task RepairCurrentJournalAsync(
        HardenedPackageDirectory directory,
        VerifiedPackageJournalV1 journal,
        CancellationToken cancellationToken)
        => PinnedDurableMetadata.RepairCurrentAsync(
            directory,
            JournalFileName,
            JsonSerializer.SerializeToUtf8Bytes(journal, JsonOptions),
            MaximumRecordBytes,
            cancellationToken);

    private static VerifiedPackageJournalV1? ReadJournalCandidate(
        HardenedPackageDirectory directory,
        VerifiedPackageAuthorityView authority,
        string fileName)
    {
        using var handle = directory.TryOpenExistingFile(fileName, write: false);
        if (handle is null)
        {
            return null;
        }

        var journal = DeserializeBounded<VerifiedPackageJournalV1>(directory, handle, "package_journal_json");
        ValidateJournal(journal, authority);
        return journal;
    }

    private static void ValidateJournal(VerifiedPackageJournalV1 journal, VerifiedPackageAuthorityView authority)
    {
        if (journal.SchemaVersion != SchemaVersion
            || !UpdateContractValidation.IsSafeIdentifier(journal.OperationId, 64)
            || !string.Equals(journal.Repository, authority.Repository, StringComparison.Ordinal)
            || journal.Channel != UpdateChannel.Stable
            || !string.Equals(journal.Tag, authority.Tag, StringComparison.Ordinal)
            || !string.Equals(journal.Version, authority.Version, StringComparison.Ordinal)
            || journal.Variant != authority.Variant
            || !string.Equals(journal.Filename, authority.FileName, StringComparison.Ordinal)
            || journal.ExpectedSize != authority.ExpectedSize
            || !string.Equals(journal.ExpectedSha256, authority.ExpectedSha256, StringComparison.Ordinal)
            || !string.Equals(journal.SignedManifestSha256, authority.SignedManifestSha256, StringComparison.Ordinal)
            || !string.Equals(journal.SignatureKeyId, authority.SignatureKeyId, StringComparison.Ordinal)
            || !string.Equals(journal.ReleaseUrl, authority.ReleaseUrl, StringComparison.Ordinal)
            || journal.UpdatedAt.Offset != TimeSpan.Zero
            || !Enum.IsDefined(journal.State))
        {
            throw new UpdateContractException("package_journal", "The package journal is stale, malformed, or does not bind to the verified authority.");
        }

        ValidateJournalTopology(journal.State, authority, journal.PartialFilename, journal.FinalFilename, journal.PartialIdentity, journal.FinalIdentity);
    }

    private async Task<VerifiedPackageRecordV1?> RecoverJournalAsync(
        HardenedPackageDirectory directory,
        VerifiedPackageJournalV1 journal,
        VerifiedPackageAuthorityView authority,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken)
    {
        switch (journal.State)
        {
            case VerifiedPackageJournalState.PartialCreated:
            case VerifiedPackageJournalState.Downloading:
            case VerifiedPackageJournalState.PartialVerified:
                using (var partial = directory.TryOpenExistingFileForRecovery(journal.PartialFilename!))
                {
                    if (partial is not null && directory.CaptureFileIdentity(partial) != journal.PartialIdentity)
                    {
                        throw new UpdateContractException("package_journal", "The journal partial file identity no longer matches its pinned file.");
                    }

                    // Rename succeeds before the promoted-state journal replacement. A crash in that
                    // narrow interval leaves a durable PartialVerified journal and the same pinned
                    // file under its final name. Re-establish the promoted state from identity, then
                    // use the ordinary promoted recovery path; journal text alone is never trusted.
                    if (journal.State == VerifiedPackageJournalState.PartialVerified)
                    {
                        NativeFileIdentity? recoveredFinalIdentity = null;
                        using (var promoted = directory.TryOpenExistingFile(authority.FileName, write: false))
                        {
                            if (promoted is not null && directory.CaptureFileIdentity(promoted) != journal.PartialIdentity)
                            {
                                throw new UpdateContractException("package_journal", "The promoted package identity no longer matches the partial journal; the replacement was preserved.");
                            }

                            if (promoted is not null)
                            {
                                recoveredFinalIdentity = journal.PartialIdentity;
                            }
                        }

                        if (recoveredFinalIdentity is not null)
                        {
                            await WriteJournalCoreAsync(journal.OperationId, authority, directory, VerifiedPackageJournalState.PromotedPendingRecord, null, null, recoveredFinalIdentity, verifiedAt, cancellationToken).ConfigureAwait(false);
                            var promotedJournal = ReadJournal(directory, authority).Journal
                                ?? throw new UpdateContractException("package_journal", "The promoted journal was not persisted.");
                            return await RecoverJournalAsync(directory, promotedJournal, authority, verifiedAt, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (partial is not null)
                    {
                        DeleteRecoveryFile(directory, partial, journal.PartialIdentity!.Value, journal.PartialFilename!);
                    }
                }
                await WriteJournalCoreAsync(journal.OperationId, authority, directory, VerifiedPackageJournalState.Failed, null, null, null, verifiedAt, cancellationToken).ConfigureAwait(false);
                DeleteRecoveryJournal(directory, authority, journal.OperationId, VerifiedPackageJournalState.Failed);
                directory.FlushPinnedDirectory();
                return null;

            case VerifiedPackageJournalState.PromotedPendingRecord:
                using (var package = directory.TryOpenExistingFileForRecovery(authority.FileName))
                {
                    if (package is null)
                    {
                        await WriteJournalCoreAsync(journal.OperationId, authority, directory, VerifiedPackageJournalState.Failed, null, null, null, verifiedAt, cancellationToken).ConfigureAwait(false);
                        DeleteRecoveryJournal(directory, authority, journal.OperationId, VerifiedPackageJournalState.Failed);
                        directory.FlushPinnedDirectory();
                        return null;
                    }

                    if (directory.CaptureFileIdentity(package) != journal.FinalIdentity)
                    {
                        throw new UpdateContractException("package_journal", "The promoted package identity no longer matches the journal; the replacement was preserved.");
                    }

                    if (directory.GetPinnedFileLength(package) != authority.ExpectedSize
                        || !string.Equals(HashPinnedFile(directory, package, authority.ExpectedSize), authority.ExpectedSha256, StringComparison.Ordinal))
                    {
                        DeleteRecoveryFile(directory, package, journal.FinalIdentity!.Value, authority.FileName);
                        await WriteJournalCoreAsync(journal.OperationId, authority, directory, VerifiedPackageJournalState.Failed, null, null, null, verifiedAt, cancellationToken).ConfigureAwait(false);
                        DeleteRecoveryJournal(directory, authority, journal.OperationId, VerifiedPackageJournalState.Failed);
                        directory.FlushPinnedDirectory();
                        return null;
                    }

                    var record = await PersistCoreAsync(journal.OperationId, authority, directory, package, authority.ExpectedSize, authority.ExpectedSha256, verifiedAt, cancellationToken).ConfigureAwait(false);
                    await WriteJournalCoreAsync(journal.OperationId, authority, directory, VerifiedPackageJournalState.DownloadedAndVerified, null, null, journal.FinalIdentity, verifiedAt, cancellationToken).ConfigureAwait(false);
                    DeleteRecoveryJournal(directory, authority, journal.OperationId, VerifiedPackageJournalState.DownloadedAndVerified);
                    directory.FlushPinnedDirectory();
                    return record;
                }

            case VerifiedPackageJournalState.DownloadedAndVerified:
                using (var terminalPackage = directory.TryOpenExistingFile(authority.FileName, write: false))
                {
                    if (terminalPackage is null || directory.CaptureFileIdentity(terminalPackage) != journal.FinalIdentity)
                    {
                        throw new UpdateContractException("package_journal", "The terminal package identity does not reconcile with the journal.");
                    }

                    var terminalRecord = TryReadRecord(directory, authority);
                    if (terminalRecord is not null && TryVerifyRecordPinnedPackage(directory, terminalPackage, terminalRecord))
                    {
                        DeleteRecoveryJournal(directory, authority, journal.OperationId, journal.State);
                        directory.FlushPinnedDirectory();
                        return terminalRecord;
                    }
                }
                throw new UpdateContractException("package_journal", "The terminal package journal does not reconcile with a verified final record.");

            case VerifiedPackageJournalState.Failed:
            case VerifiedPackageJournalState.Cancelled:
                if (journal.PartialIdentity is not null)
                {
                    using var terminalPartial = directory.TryOpenExistingFileForRecovery(journal.PartialFilename!);
                    if (terminalPartial is not null && directory.CaptureFileIdentity(terminalPartial) != journal.PartialIdentity)
                    {
                        throw new UpdateContractException("package_journal", "The terminal partial identity no longer matches; the replacement was preserved.");
                    }

                    if (terminalPartial is not null)
                    {
                        DeleteRecoveryFile(directory, terminalPartial, journal.PartialIdentity.Value, journal.PartialFilename!);
                    }
                }

                if (journal.FinalIdentity is not null)
                {
                    using var terminalFinal = directory.TryOpenExistingFileForRecovery(authority.FileName);
                    if (terminalFinal is not null && directory.CaptureFileIdentity(terminalFinal) != journal.FinalIdentity)
                    {
                        throw new UpdateContractException("package_journal", "The terminal final identity no longer matches; the replacement was preserved.");
                    }

                    if (terminalFinal is not null
                        && directory.GetPinnedFileLength(terminalFinal) == authority.ExpectedSize
                        && string.Equals(HashPinnedFile(directory, terminalFinal, authority.ExpectedSize), authority.ExpectedSha256, StringComparison.Ordinal))
                    {
                        var recoveredRecord = await PersistCoreAsync(journal.OperationId, authority, directory, terminalFinal, authority.ExpectedSize, authority.ExpectedSha256, verifiedAt, cancellationToken).ConfigureAwait(false);
                        await WriteJournalCoreAsync(journal.OperationId, authority, directory, VerifiedPackageJournalState.DownloadedAndVerified, null, null, journal.FinalIdentity, verifiedAt, cancellationToken).ConfigureAwait(false);
                        DeleteRecoveryJournal(directory, authority, journal.OperationId, VerifiedPackageJournalState.DownloadedAndVerified);
                        directory.FlushPinnedDirectory();
                        return recoveredRecord;
                    }

                    if (terminalFinal is not null)
                    {
                        DeleteRecoveryFile(directory, terminalFinal, journal.FinalIdentity.Value, authority.FileName);
                    }
                }

                DeleteRecoveryJournal(directory, authority, journal.OperationId, journal.State);
                directory.FlushPinnedDirectory();
                return null;

            default:
                throw new UpdateContractException("package_journal", "The package journal contains an unsupported state.");
        }
    }

    private void DeleteRecoveryFile(
        HardenedPackageDirectory directory,
        SafeFileHandle file,
        NativeFileIdentity expectedIdentity,
        string expectedName)
    {
        _beforeRecoveryDeleteForTests?.Invoke(expectedName);
        directory.DeletePinnedFile(file, expectedIdentity, expectedName);
        directory.FlushPinnedDirectory();
    }

    internal void DeleteRecoveryJournal(
        HardenedPackageDirectory directory,
        VerifiedPackageAuthorityView authority,
        string expectedOperationId,
        VerifiedPackageJournalState expectedState)
    {
        DeleteRecoveryJournalCandidate(
            directory,
            authority,
            PreviousJournalFileName,
            expectedOperationId,
            expectedState: null,
            required: false);
        DeleteRecoveryJournalCandidate(
            directory,
            authority,
            JournalFileName,
            expectedOperationId,
            expectedState,
            required: true);
    }

    private void DeleteRecoveryJournalCandidate(
        HardenedPackageDirectory directory,
        VerifiedPackageAuthorityView authority,
        string fileName,
        string expectedOperationId,
        VerifiedPackageJournalState? expectedState,
        bool required)
    {
        using var journalHandle = directory.TryOpenExistingFileForRecovery(fileName);
        if (journalHandle is null)
        {
            if (required)
            {
                throw new UpdateContractException("package_journal", "The package journal disappeared before handle-bound retirement.");
            }

            return;
        }

        var journal = DeserializeBounded<VerifiedPackageJournalV1>(directory, journalHandle, "package_journal_json");
        ValidateJournal(journal, authority);
        if (!string.Equals(journal.OperationId, expectedOperationId, StringComparison.Ordinal)
            || (expectedState is not null && journal.State != expectedState.Value))
        {
            throw new UpdateContractException("package_journal", "The package journal changed before handle-bound retirement.");
        }

        DeleteRecoveryFile(
            directory,
            journalHandle,
            directory.CaptureFileIdentity(journalHandle),
            fileName);
    }

    internal static void ValidateRecord(VerifiedPackageRecordV1 record, UpdatePackageVariant installedVariant)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.SchemaVersion != SchemaVersion || !UpdateContractValidation.IsSafeIdentifier(record.OperationId, 64)
            || !string.Equals(record.Repository, UpdateManifestConstants.Repository, StringComparison.Ordinal)
            || record.Channel != UpdateChannel.Stable || record.Variant != installedVariant
            || record.ExpectedSize is <= 0 or > UpdateManifestConstants.MaximumPackageBytes
            || record.ActualSize is <= 0 or > UpdateManifestConstants.MaximumPackageBytes
            || record.ActualSize != record.ExpectedSize || !string.Equals(record.ExpectedSha256, record.ActualSha256, StringComparison.Ordinal)
            || !string.Equals(record.State, "downloadedAndVerified", StringComparison.Ordinal)
            || !string.Equals(record.Tag, "v" + record.Version, StringComparison.Ordinal)
            || !string.Equals(record.ReleaseUrl, $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/{record.Tag}", StringComparison.Ordinal)
            || !UpdateContractValidation.IsSafeIdentifier(record.SignatureKeyId, 128))
        {
            throw new UpdateContractException("package_record", "The verified package record is invalid.");
        }

        _ = GetRelativePackagePath(record.Version, record.Variant, record.Filename);
        UpdateContractValidation.ValidateSha256(record.ExpectedSha256, "package_expected_hash");
        UpdateContractValidation.ValidateSha256(record.SignedManifestSha256, "package_manifest_hash");
    }

    private static void ValidateRequestedLocation(string version, UpdatePackageVariant variant, string filename)
    {
        _ = GetRelativePackagePath(version, variant, filename);
    }

    private static void ValidateOperationId(string operationId)
    {
        if (!UpdateContractValidation.IsSafeIdentifier(operationId, 64))
        {
            throw new UpdateContractException("package_operation", "The package operation ID is invalid.");
        }
    }

    private static string GetRelativePackagePath(string version, UpdatePackageVariant variant, string filename)
    {
        var parsedVersion = SemanticVersion.Parse(version);
        if (!parsedVersion.IsStable || !string.Equals(parsedVersion.ToString(), version, StringComparison.Ordinal))
        {
            throw new UpdateContractException("package_version", "The package version must be normalized and stable.");
        }

        UpdateContractValidation.ValidateSafeFileName(filename, "package_filename");
        return Path.Combine("Packages", version, HardenedUpdateStagingStore.VariantDirectoryName(variant), filename);
    }

    private string ResolveProjection(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(UpdateDirectory, relativePath));
        var root = UpdateDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateContractException("package_path", "The package path escapes the fixed update root.");
        }

        return path;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            WriteIndented = true
        };
        options.Converters.Add(new UpdateChannelJsonConverter());
        options.Converters.Add(new UpdatePackageVariantJsonConverter());
        options.Converters.Add(new StrictUtcDateTimeOffsetJsonConverter());
        return options;
    }
}

internal sealed class VerifiedPackageDownloader
{
    private readonly IUpdateDiscoveryClient _discovery;
    private readonly IUpdateHttpTransport _transport;
    private readonly VerifiedPackageStore _store;
    private readonly VerifiedPackageDownloadOptions _options;
    private readonly IUpdateScheduleClock _clock;
    private readonly UpdateCheckAdmission _admission;
    private readonly UpdateStateStore _stateStore;
    private readonly Func<string> _operationIdFactory;

    public VerifiedPackageDownloader(
        IUpdateDiscoveryClient discovery,
        IUpdateHttpTransport transport,
        VerifiedPackageStore store,
        VerifiedPackageDownloadOptions options,
        IUpdateScheduleClock clock,
        UpdateCheckAdmission admission,
        Func<string>? operationIdFactoryForTests = null,
        UpdateStateStore? stateStore = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _stateStore = stateStore ?? new UpdateStateStore(store.InstalledVariant, store.UpdateDirectory);
        _operationIdFactory = operationIdFactoryForTests ?? CreateOperationId;
        _options.Validate();
    }

    private void ValidateAntiRollbackAdmission(VerifiedPackageAuthorityView authority)
    {
        var loaded = _stateStore.Load();
        if (loaded.CorruptionDetected && loaded.Source == UpdateStateSource.Default)
        {
            throw new UpdateContractException("package_rollback_state", "The authoritative anti-rollback state is corrupt and no validated previous copy is available.");
        }

        var state = loaded.State;
        if (authority.ReleaseSequence < state.HighestAcceptedReleaseSequence)
        {
            throw new UpdateContractException("package_rollback", "The verified release sequence is below the durable accepted boundary.");
        }

        if (authority.ReleaseSequence == state.HighestAcceptedReleaseSequence && authority.ReleaseSequence != 0
            && (!string.Equals(state.HighestAcceptedVersion, authority.Version, StringComparison.Ordinal)
                || !string.Equals(state.LastManifestSha256, authority.SignedManifestSha256, StringComparison.Ordinal)))
        {
            throw new UpdateContractException("package_rollback", "The verified release conflicts with the durable same-sequence accepted boundary.");
        }
    }

    private async Task CommitAntiRollbackAdmissionAsync(VerifiedPackageAuthorityView authority, CancellationToken cancellationToken)
    {
        var loaded = _stateStore.Load();
        ValidateAntiRollbackAdmission(authority);
        var state = loaded.State;
        if (authority.ReleaseSequence == state.HighestAcceptedReleaseSequence)
        {
            return;
        }

        var committed = state with
        {
            HighestAcceptedReleaseSequence = authority.ReleaseSequence,
            HighestAcceptedVersion = authority.Version,
            LastManifestSha256 = authority.SignedManifestSha256
        };
        await _stateStore.SaveAsync(committed, cancellationToken).ConfigureAwait(false);
        await _stateStore.SaveAsync(committed, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VerifiedPackageDownloadResult> DownloadAsync(CancellationToken cancellationToken)
    {
        HardenedPackageDirectory? directory = null;
        SafeFileHandle? partial = null;
        string? operationId = null;
        object? authorityCapability = null;
        var partialName = string.Empty;
        NativeFileIdentity? partialIdentity = null;
        NativeFileIdentity? finalIdentity = null;
        var promoted = false;
        try
        {
            var admission = _admission.TryAcquire();
            if (!admission.IsAdmitted)
            {
                return VerifiedPackageDownloadResult.Failure(admission.ErrorCode ?? "gate_unavailable");
            }

            using var admissionLease = admission.Lease!;
            operationId = _operationIdFactory();
            var discovery = await _discovery.CheckAsync(null, cancellationToken).ConfigureAwait(false);
            authorityCapability = discovery.VerifiedPackageAuthority;
            if (discovery.Status != UpdateDiscoveryStatus.UpdateAvailable
                || !GitHubUpdateDiscoveryClient.TryGetVerifiedPackageAuthority(authorityCapability, out var authority))
            {
                return VerifiedPackageDownloadResult.Failure(discovery.ErrorCode ?? "package_not_verified");
            }
            if (authority.ExpectedSize > _options.HardMaximumPackageBytes)
            {
                return VerifiedPackageDownloadResult.Failure("package_hard_maximum");
            }
            ValidateAntiRollbackAdmission(authority);

            var recovered = await _store.RecoverAsync(operationId, authorityCapability!, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
            if (recovered is not null)
            {
                await CommitAntiRollbackAdmissionAsync(authority, cancellationToken).ConfigureAwait(false);
                return new VerifiedPackageDownloadResult(true, "recovered_verified_package", recovered);
            }

            directory = _store.OpenDirectory(authorityCapability!);
            partialName = authority.FileName + ".partial";
            partial = directory.CreateNewFile(partialName, write: true);
            partialIdentity = directory.CaptureFileIdentity(partial);
            await _store.WriteJournalAsync(
                operationId,
                authorityCapability!,
                directory,
                VerifiedPackageJournalState.PartialCreated,
                partialName,
                partialIdentity,
                finalIdentity: null,
                _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
            _store.ThrowIfRequested(UpdateDownloadFaultPoint.AfterInitialJournalDurability);
            await _store.WriteJournalAsync(
                operationId,
                authorityCapability!,
                directory,
                VerifiedPackageJournalState.Downloading,
                partialName,
                partialIdentity,
                finalIdentity: null,
                _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
            _store.ThrowIfRequested(UpdateDownloadFaultPoint.DuringDownloading);
            var actual = await DownloadToPartialAsync(authority, directory, partial, cancellationToken).ConfigureAwait(false);
            if (actual.Size != authority.ExpectedSize || !string.Equals(actual.Sha256, authority.ExpectedSha256, StringComparison.Ordinal))
            {
                await PersistTerminalStateAsync(
                    operationId,
                    authorityCapability!,
                    directory,
                    partialName,
                    partialIdentity,
                    finalIdentity: null,
                    VerifiedPackageJournalState.Failed).ConfigureAwait(false);
                return VerifiedPackageDownloadResult.Failure("package_digest_mismatch");
            }

            partialIdentity = directory.CaptureFileIdentity(partial);
            await _store.WriteJournalAsync(
                operationId,
                authorityCapability!,
                directory,
                VerifiedPackageJournalState.PartialVerified,
                partialName,
                partialIdentity,
                finalIdentity: null,
                _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
            _store.ThrowIfRequested(UpdateDownloadFaultPoint.AfterPartialVerificationJournalDurability);
            _store.ThrowIfRequested(UpdateDownloadFaultPoint.BeforePromotion);
            directory.RenamePinnedFile(partial, authority.FileName);
            promoted = true;
            finalIdentity = directory.CaptureFileIdentity(partial);
            _store.ThrowIfRequested(UpdateDownloadFaultPoint.AfterNativePromotion);
            await _store.WriteJournalAsync(
                operationId,
                authorityCapability!,
                directory,
                VerifiedPackageJournalState.PromotedPendingRecord,
                partialName: null,
                partialIdentity: null,
                finalIdentity,
                _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
            var record = await _store.PersistAsync(operationId, authorityCapability!, directory, partial, actual.Size, actual.Sha256, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
            await CommitAntiRollbackAdmissionAsync(authority, cancellationToken).ConfigureAwait(false);
            await _store.WriteJournalAsync(
                operationId,
                authorityCapability!,
                directory,
                VerifiedPackageJournalState.DownloadedAndVerified,
                partialName: null,
                partialIdentity: null,
                finalIdentity,
                _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
            _store.ThrowIfRequested(UpdateDownloadFaultPoint.AfterFinalRecordDurabilityBeforeJournalRetirement);
            _store.DeleteRecoveryJournal(
                directory,
                authority,
                operationId,
                VerifiedPackageJournalState.DownloadedAndVerified);
            directory.FlushPinnedDirectory();
            return new VerifiedPackageDownloadResult(true, "downloaded_and_verified", record);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PersistTerminalStateAsync(operationId, authorityCapability, directory, partialName, partialIdentity, finalIdentity, VerifiedPackageJournalState.Cancelled).ConfigureAwait(false);
            return VerifiedPackageDownloadResult.Failure("cancelled");
        }
        catch (OperationCanceledException)
        {
            await PersistTerminalStateAsync(operationId, authorityCapability, directory, partialName, partialIdentity, finalIdentity, VerifiedPackageJournalState.Failed).ConfigureAwait(false);
            return VerifiedPackageDownloadResult.Failure("download_timeout");
        }
        catch (UpdateRequestTimeoutException)
        {
            await PersistTerminalStateAsync(operationId, authorityCapability, directory, partialName, partialIdentity, finalIdentity, VerifiedPackageJournalState.Failed).ConfigureAwait(false);
            return VerifiedPackageDownloadResult.Failure("download_timeout");
        }
        catch (UpdateDiscoveryException exception)
        {
            await PersistTerminalStateAsync(operationId, authorityCapability, directory, partialName, partialIdentity, finalIdentity, VerifiedPackageJournalState.Failed).ConfigureAwait(false);
            return VerifiedPackageDownloadResult.Failure(exception.Code);
        }
        catch (UpdateContractException exception)
        {
            await PersistTerminalStateAsync(operationId, authorityCapability, directory, partialName, partialIdentity, finalIdentity, VerifiedPackageJournalState.Failed).ConfigureAwait(false);
            return VerifiedPackageDownloadResult.Failure(exception.Code);
        }
        catch (HttpRequestException)
        {
            await PersistTerminalStateAsync(operationId, authorityCapability, directory, partialName, partialIdentity, finalIdentity, VerifiedPackageJournalState.Failed).ConfigureAwait(false);
            return VerifiedPackageDownloadResult.Failure("network_unavailable");
        }
        catch (IOException)
        {
            await PersistTerminalStateAsync(operationId, authorityCapability, directory, partialName, partialIdentity, finalIdentity, VerifiedPackageJournalState.Failed).ConfigureAwait(false);
            return VerifiedPackageDownloadResult.Failure("package_io");
        }
        catch (UnauthorizedAccessException)
        {
            await PersistTerminalStateAsync(operationId, authorityCapability, directory, partialName, partialIdentity, finalIdentity, VerifiedPackageJournalState.Failed).ConfigureAwait(false);
            return VerifiedPackageDownloadResult.Failure("package_io");
        }
        finally
        {
            if (!promoted
                && directory is not null
                && partial is not null
                && partialIdentity is { } expectedPartialIdentity
                && partialName.Length > 0)
            {
                try
                {
                    directory.DeletePinnedFile(partial, expectedPartialIdentity, partialName);
                    directory.FlushPinnedDirectory();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UpdateContractException)
                {
                    // Preserve the primary download result. A retained journal and the pinned
                    // partial identity remain available to the next recovery transaction.
                }
            }
            partial?.Dispose();
            directory?.Dispose();
        }
    }

    private static string CreateOperationId()
        => "download-" + Guid.NewGuid().ToString("N");

    private async Task PersistTerminalStateAsync(
        string? operationId,
        object? authorityCapability,
        HardenedPackageDirectory? directory,
        string partialName,
        NativeFileIdentity? partialIdentity,
        NativeFileIdentity? finalIdentity,
        VerifiedPackageJournalState terminalState)
    {
        if (operationId is null
            || authorityCapability is null
            || directory is null
            || (partialIdentity is null && finalIdentity is null))
        {
            return;
        }

        try
        {
            await _store.WriteJournalAsync(
                operationId,
                authorityCapability,
                directory,
                terminalState,
                finalIdentity is null ? partialName : null,
                finalIdentity is null ? partialIdentity : null,
                finalIdentity,
                _clock.UtcNow,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UpdateContractException)
        {
            // The primary operation result remains authoritative; an earlier durable journal
            // is retained for recovery when the terminal replacement cannot be committed.
        }
    }

    private async Task<(long Size, string Sha256)> DownloadToPartialAsync(VerifiedPackageAuthorityView authority, HardenedPackageDirectory directory, SafeFileHandle output, CancellationToken cancellationToken)
    {
        GitHubUpdateEndpointPolicy.ValidateInitial(authority.BrowserDownloadUri, UpdateEndpointKind.ReleaseAsset);
        var currentUri = authority.BrowserDownloadUri;
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(_options.RequestTimeout);
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            HttpResponseMessage response;
            try { response = await _transport.SendAsync(request, requestTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new UpdateRequestTimeoutException(); }

            using (response)
            {
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (redirects >= _options.MaximumRedirects || response.Headers.Location is null)
                    {
                        throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "redirect_limit");
                    }
                    var nextUri = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(currentUri, response.Headers.Location);
                    GitHubUpdateEndpointPolicy.ValidateRedirect(authority.BrowserDownloadUri, currentUri, nextUri, UpdateEndpointKind.ReleaseAsset);
                    currentUri = nextUri;
                    continue;
                }

                if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
                {
                    throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "package_http");
                }
                if (response.Content.Headers.ContentLength is { } contentLength && (contentLength != authority.ExpectedSize || contentLength > _options.HardMaximumPackageBytes))
                {
                    throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "package_content_length");
                }

                await using var input = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024];
                long total = 0;
                while (true)
                {
                    int read;
                    using (var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(requestTimeout.Token))
                    {
                        idleTimeout.CancelAfter(_options.IdleTimeout);
                        try { read = await input.ReadAsync(buffer.AsMemory(), idleTimeout.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !requestTimeout.IsCancellationRequested) { throw new UpdateRequestTimeoutException(); }
                    }
                    if (read == 0) break;
                    if (read > authority.ExpectedSize - total || read > _options.HardMaximumPackageBytes - total)
                    {
                        throw new UpdateDiscoveryException(UpdateErrorCategory.Http, "package_too_large");
                    }
                    await RandomAccess.WriteAsync(output, buffer.AsMemory(0, read), total, requestTimeout.Token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    total += read;
                }

                directory.FlushPinnedFile(output);
                if (directory.GetPinnedFileLength(output) != total)
                {
                    throw new UpdateContractException("package_size", "The pinned partial size changed during streaming.");
                }
                return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
            }
        }
    }
}
