using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PimaxVrcSupervisor.LifecycleObservability;

internal static class LifecycleJournalCaptureSchema
{
    public const string Version = "lifecycle-journal-capture-v1";
    public const string ManifestFileName = "manifest.json";
}

internal static class LifecycleJournalCaptureJson
{
    public const int MaximumOutputBytes = 64 * 1024;

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(LifecycleJournalCaptureResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var json = JsonSerializer.Serialize(result, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumOutputBytes)
        {
            throw new InvalidOperationException("The lifecycle journal capture result exceeded its output bound.");
        }

        return json;
    }
}

internal sealed record LifecycleJournalCaptureRequest(
    string SourceJournalPath,
    string ExternalOutputRoot,
    string RepositoryRoot)
{
    public static LifecycleJournalCaptureRequest Parse(string[] args)
    {
        string? sourcePath = null;
        string? outputRoot = null;
        string? repoRoot = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--source=", StringComparison.OrdinalIgnoreCase))
            {
                sourcePath = arg["--source=".Length..];
            }
            else if (arg.StartsWith("--output=", StringComparison.OrdinalIgnoreCase))
            {
                outputRoot = arg["--output=".Length..];
            }
            else if (arg.StartsWith("--repo-root=", StringComparison.OrdinalIgnoreCase))
            {
                repoRoot = arg["--repo-root=".Length..];
            }
        }

        return new LifecycleJournalCaptureRequest(
            SourceJournalPath: sourcePath ?? "",
            ExternalOutputRoot: outputRoot ?? "",
            RepositoryRoot: repoRoot ?? "");
    }
}

internal sealed record LifecycleJournalCaptureResultV1(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("timestampUtc")] string TimestampUtc,
    [property: JsonPropertyName("timestampAzerbaijan")] string TimestampAzerbaijan,
    [property: JsonPropertyName("source")] SourceMetadata Source,
    [property: JsonPropertyName("snapshot")] SnapshotMetadata Snapshot,
    [property: JsonPropertyName("bundle")] BundleMetadata Bundle,
    [property: JsonPropertyName("sourceChangedDuringCapture")] bool SourceChangedDuringCapture,
    [property: JsonPropertyName("warnings")] string[] Warnings,
    [property: JsonPropertyName("errors")] string[] Errors);

internal sealed record SourceMetadata(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("lengthBeforeCapture")] long LengthBeforeCapture,
    [property: JsonPropertyName("lastWriteTimeUtcBeforeCapture")] string LastWriteTimeUtcBeforeCapture,
    [property: JsonPropertyName("sha256BeforeCapture")] string Sha256BeforeCapture,
    [property: JsonPropertyName("lengthAfterCapture")] long LengthAfterCapture,
    [property: JsonPropertyName("lastWriteTimeUtcAfterCapture")] string LastWriteTimeUtcAfterCapture,
    [property: JsonPropertyName("sha256AfterCapture")] string Sha256AfterCapture);

internal sealed record SnapshotMetadata(
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("fullPath")] string FullPath,
    [property: JsonPropertyName("length")] long Length,
    [property: JsonPropertyName("lineCount")] long LineCount,
    [property: JsonPropertyName("sha256")] string Sha256);

internal sealed record BundleMetadata(
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("fullPath")] string FullPath,
    [property: JsonPropertyName("manifestPath")] string ManifestPath);
