using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PimaxVrcSupervisor.LifecycleObservability;

internal sealed class LifecycleJournalCaptureWorker
{
    private readonly Func<DateTimeOffset> _nowUtc;
    private readonly Func<string, byte[]> _readAllBytes;
    private readonly Func<string, string, bool> _pathEndsWith;
    private readonly Func<string, string[], bool> _pathStartsWith;
    private readonly Func<string, string> _getFullPath;
    private readonly Func<string, DateTime> _getFileLastWriteTimeUtc;
    private readonly Func<string, long> _getFileLength;
    private readonly Func<string, string> _computeSha256;
    private readonly Func<string, string, long, long, bool> _copyWithReadShareReadWriteDelete;

    public LifecycleJournalCaptureWorker() : this(
            () => DateTimeOffset.UtcNow,
            File.ReadAllBytes,
            (path, suffix) => path.EndsWith(suffix),
            (path, prefixes) => prefixes.Any(prefix => path.StartsWith(prefix)),
            Path.GetFullPath,
            f => File.GetLastWriteTimeUtc(f),
            f => new FileInfo(f).Length,
            ComputeSha256,
            CopyFileWithReadShareReadWriteDelete)
    {
    }

    internal LifecycleJournalCaptureWorker(
        Func<DateTimeOffset> nowUtc,
        Func<string, byte[]> readAllBytes,
        Func<string, string, bool> pathEndsWith,
        Func<string, string[], bool> pathStartsWith,
        Func<string, string> getFullPath,
        Func<string, DateTime> getFileLastWriteTimeUtc,
        Func<string, long> getFileLength,
        Func<string, string> computeSha256,
        Func<string, string, long, long, bool> copyWithReadShareReadWriteDelete)
    {
        _nowUtc = nowUtc;
        _readAllBytes = readAllBytes;
        _pathEndsWith = pathEndsWith;
        _pathStartsWith = pathStartsWith;
        _getFullPath = getFullPath;
        _getFileLastWriteTimeUtc = getFileLastWriteTimeUtc;
        _getFileLength = getFileLength;
        _computeSha256 = computeSha256;
        _copyWithReadShareReadWriteDelete = copyWithReadShareReadWriteDelete;
    }

    public LifecycleJournalCaptureResultV1 Capture(LifecycleJournalCaptureRequest request)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Validate required arguments
            if (string.IsNullOrWhiteSpace(request.SourceJournalPath))
            {
                errors.Add("Missing required argument: --source=<path>");
                return CreateFailureResult("missing_source_argument", errors, warnings);
            }

            if (string.IsNullOrWhiteSpace(request.ExternalOutputRoot))
            {
                errors.Add("Missing required argument: --output=<path>");
                return CreateFailureResult("missing_output_argument", errors, warnings);
            }

            if (string.IsNullOrWhiteSpace(request.RepositoryRoot))
            {
                errors.Add("Missing required argument: --repo-root=<path>");
                return CreateFailureResult("missing_repo_argument", errors, warnings);
            }

            // Normalize paths
            var sourcePath = _getFullPath(request.SourceJournalPath);
            var outputRoot = _getFullPath(request.ExternalOutputRoot);
            var repoRoot = _getFullPath(request.RepositoryRoot);

            // Require source to exist (check before output validation to avoid path-prefix confusion)
            if (!File.Exists(sourcePath))
            {
                errors.Add($"Source journal '{sourcePath}' does not exist.");
                return CreateFailureResult("source_missing", errors, warnings);
            }

            // Reject output equal to or beneath repository root
            if (outputRoot == repoRoot || IsPathBeneath(outputRoot, repoRoot))
            {
                errors.Add($"Output root '{outputRoot}' is equal to or beneath repository root '{repoRoot}'.");
                return CreateFailureResult("output_root_rejected", errors, warnings);
            }

            // Capture source metadata before copy
            var beforeLength = _getFileLength(sourcePath);
            var beforeLastWrite = _getFileLastWriteTimeUtc(sourcePath);
            var beforeSha256 = _computeSha256(sourcePath);

            // Create timestamped bundle directory
            var timestamp = _nowUtc();
            var bundleName = $"lifecycle-journal-capture-{timestamp:yyyyMMdd-HHmmss}";
            var bundlePath = Path.Combine(outputRoot, bundleName);
            Directory.CreateDirectory(bundlePath);

            // Create snapshot path
            var snapshotRelative = Path.Combine(bundleName, "lifecycle-events.jsonl");
            var snapshotPath = Path.Combine(outputRoot, snapshotRelative);
            snapshotPath = _getFullPath(snapshotPath);

            // Copy with read sharing (does not modify source)
            var copied = _copyWithReadShareReadWriteDelete(sourcePath, snapshotPath, beforeLength, beforeLength);
            if (!copied)
            {
                errors.Add($"Failed to copy source to '{snapshotPath}'.");
                return CreateFailureResult("copy_failed", errors, warnings);
            }

            // Capture source metadata after copy
            var afterLength = _getFileLength(sourcePath);
            var afterLastWrite = _getFileLastWriteTimeUtc(sourcePath);
            var afterSha256 = _computeSha256(sourcePath);

            // Compute snapshot metadata
            var snapshotLength = _getFileLength(snapshotPath);
            var snapshotLineCount = CountLines(snapshotPath);
            var snapshotSha256 = _computeSha256(snapshotPath);

            // Determine if source changed during capture
            var sourceChanged = beforeLength != afterLength
                || beforeLastWrite != afterLastWrite
                || !string.Equals(beforeSha256, afterSha256, StringComparison.Ordinal);

            if (sourceChanged)
            {
                warnings.Add("Source file changed during capture. The snapshot is an immutable copy taken at a point in time.");
            }

            // Format timestamps
            var utcString = timestamp.ToString("o", CultureInfo.InvariantCulture);
            var azerbaijanTime = timestamp.ToOffset(TimeSpan.FromHours(4));
            var azerbaijanString = azerbaijanTime.ToString("o", CultureInfo.InvariantCulture);

            // Write manifest to bundle directory (after snapshot metadata is complete)
            var manifestPath = Path.Combine(bundlePath, LifecycleJournalCaptureSchema.ManifestFileName);
            var manifestResult = new LifecycleJournalCaptureResultV1(
                SchemaVersion: LifecycleJournalCaptureSchema.Version,
                TimestampUtc: utcString,
                TimestampAzerbaijan: azerbaijanString,
                Source: new SourceMetadata(
                    Path: sourcePath,
                    LengthBeforeCapture: beforeLength,
                    LastWriteTimeUtcBeforeCapture: beforeLastWrite.ToString("o", CultureInfo.InvariantCulture),
                    Sha256BeforeCapture: beforeSha256,
                    LengthAfterCapture: afterLength,
                    LastWriteTimeUtcAfterCapture: afterLastWrite.ToString("o", CultureInfo.InvariantCulture),
                    Sha256AfterCapture: afterSha256),
                Snapshot: new SnapshotMetadata(
                    RelativePath: snapshotRelative,
                    FullPath: snapshotPath,
                    Length: snapshotLength,
                    LineCount: snapshotLineCount,
                    Sha256: snapshotSha256),
                Bundle: new BundleMetadata(
                    RelativePath: snapshotRelative,
                    FullPath: bundlePath,
                    ManifestPath: manifestPath),
                SourceChangedDuringCapture: sourceChanged,
                Warnings: warnings.ToArray(),
                Errors: errors.ToArray());
            File.WriteAllText(manifestPath, LifecycleJournalCaptureJson.Serialize(manifestResult), Encoding.UTF8);

            // Return result
            return manifestResult;
        }
        catch (Exception ex)
        {
            errors.Add($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
            return CreateFailureResult("unexpected_error", errors, warnings);
        }
    }

    private bool IsPathBeneath(string child, string parent)
    {
        var childNormalized = _getFullPath(child).TrimEnd(Path.DirectorySeparatorChar);
        var parentNormalized = _getFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);

        // Use case-insensitive comparison for Windows paths
        if (string.Equals(childNormalized, parentNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var childComponents = childNormalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var parentComponents = parentNormalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        if (childComponents.Length <= parentComponents.Length)
        {
            return false;
        }

        for (var i = 0; i < parentComponents.Length; i++)
        {
            if (!string.Equals(childComponents[i], parentComponents[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static long CountLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var count = 0L;
        while (reader.ReadLine() is { } _)
        {
            count++;
        }
        return count;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToUpperInvariant();
    }

    private static bool CopyFileWithReadShareReadWriteDelete(string source, string destination, long expectedSourceLength, long expectedDestinationLength)
    {
        try
        {
            using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var destStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            sourceStream.CopyTo(destStream);
            destStream.Flush(true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private LifecycleJournalCaptureResultV1 CreateFailureResult(string resultCode, List<string> errors, List<string> warnings)
    {
        var timestamp = _nowUtc();
        var azerbaijanTime = timestamp.ToOffset(TimeSpan.FromHours(4));

        return new LifecycleJournalCaptureResultV1(
            SchemaVersion: LifecycleJournalCaptureSchema.Version,
            TimestampUtc: timestamp.ToString("o", CultureInfo.InvariantCulture),
            TimestampAzerbaijan: azerbaijanTime.ToString("o", CultureInfo.InvariantCulture),
            Source: new SourceMetadata(
                Path: "N/A",
                LengthBeforeCapture: 0L,
                LastWriteTimeUtcBeforeCapture: "1970-01-01T00:00:00.0000000Z",
                Sha256BeforeCapture: "N/A",
                LengthAfterCapture: 0L,
                LastWriteTimeUtcAfterCapture: "1970-01-01T00:00:00.0000000Z",
                Sha256AfterCapture: "N/A"),
            Snapshot: new SnapshotMetadata(
                RelativePath: "N/A",
                FullPath: "N/A",
                Length: 0L,
                LineCount: 0L,
                Sha256: "N/A"),
            Bundle: new BundleMetadata(
                RelativePath: "N/A",
                FullPath: "N/A",
                ManifestPath: "N/A"),
            SourceChangedDuringCapture: false,
            Warnings: warnings.ToArray(),
            Errors: errors.ToArray());
    }
}
