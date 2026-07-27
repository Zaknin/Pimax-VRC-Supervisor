using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

internal static partial class LifecycleEventSchema
{
    public const string Version = "lifecycle-observability-v1";
}

internal sealed class LifecycleEvent
{
    public string SchemaVersion { get; init; } = LifecycleEventSchema.Version;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public long Sequence { get; init; }
    public string Component { get; init; } = "";
    public int ProcessId { get; init; }
    public string ProcessStartIdentity { get; init; } = "";
    public string LifecycleCorrelationId { get; init; } = "";
    public string EventName { get; init; } = "";
    public string? Reason { get; init; }
    public string? Result { get; init; }
    public IReadOnlyDictionary<string, string>? Fields { get; init; }
}

internal sealed record LifecycleProcessIdentity(int ProcessId, DateTimeOffset? StartTime)
{
    public string Value => StartTime is { } startTime
        ? $"{ProcessId}@{startTime:O}"
        : ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "@unknown";

    public static LifecycleProcessIdentity Current()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return new LifecycleProcessIdentity(process.Id, new DateTimeOffset(process.StartTime));
        }
        catch
        {
            return new LifecycleProcessIdentity(Environment.ProcessId, null);
        }
    }

    public static LifecycleProcessIdentity From(Process process)
    {
        try
        {
            return new LifecycleProcessIdentity(process.Id, new DateTimeOffset(process.StartTime));
        }
        catch
        {
            return new LifecycleProcessIdentity(process.Id, null);
        }
    }
}

internal sealed class LifecycleEventSink
{
    internal const long MaxActiveBytes = 2 * 1024 * 1024;
    internal const int RetainedRotatedFiles = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directory;
    private readonly string _component;
    private readonly LifecycleProcessIdentity _processIdentity;
    private readonly Guid _correlationId;
    private readonly Action<string, string>? _append;
    private readonly string _writerMutexName;
    private long _sequence;

    public LifecycleEventSink(
        string directory,
        string component,
        Guid? correlationId = null,
        Action<string, string>? append = null)
    {
        _directory = directory;
        _component = NormalizeComponent(component);
        _processIdentity = LifecycleProcessIdentity.Current();
        _correlationId = correlationId ?? Guid.NewGuid();
        _append = append;
        ActivePath = Path.Combine(directory, "lifecycle-events.jsonl");
        _writerMutexName = @"Local\PimaxVrcSupervisorLifecycleEvents-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(ActivePath))))[..24];
    }

    public string ActivePath { get; }

    public Guid CorrelationId => _correlationId;

    public string CorrelationIdText => _correlationId.ToString("N");

    public LifecycleProcessIdentity ProcessIdentity => _processIdentity;

    public static LifecycleEventSink ForCurrentProcess(string component, Guid? correlationId = null)
        => new(DefaultDirectory(), component, correlationId);

    public static string DefaultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PimaxVrcSupervisor",
            "Diagnostics",
            "Lifecycle");

    public void Write(
        string eventName,
        string? reason = null,
        string? result = null,
        IReadOnlyDictionary<string, string?>? fields = null)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(new LifecycleEvent
            {
                Sequence = Interlocked.Increment(ref _sequence),
                Component = _component,
                ProcessId = _processIdentity.ProcessId,
                ProcessStartIdentity = _processIdentity.Value,
                LifecycleCorrelationId = CorrelationIdText,
                EventName = NormalizeEventName(eventName),
                Reason = LifecycleEventSanitizer.Value(reason),
                Result = LifecycleEventSanitizer.Value(result),
                Fields = LifecycleEventSanitizer.Fields(fields)
            }, JsonOptions);
            WriteLine(serialized);
        }
        catch
        {
            // Lifecycle observability is strictly passive.
        }
    }

    private void WriteLine(string line)
    {
        using var writerMutex = new Mutex(initiallyOwned: false, _writerMutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = writerMutex.WaitOne(TimeSpan.FromMilliseconds(250));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                return;
            }

            Directory.CreateDirectory(_directory);
            RotateIfNeeded(line);
            if (_append is not null)
            {
                _append(ActivePath, line + Environment.NewLine);
                return;
            }

            using var stream = new FileStream(ActivePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            if (ownsMutex)
            {
                writerMutex.ReleaseMutex();
            }
        }
    }

    private void RotateIfNeeded(string nextLine)
    {
        var nextBytes = Encoding.UTF8.GetByteCount(nextLine) + Environment.NewLine.Length;
        if (!File.Exists(ActivePath) || new FileInfo(ActivePath).Length + nextBytes <= MaxActiveBytes)
        {
            return;
        }

        for (var index = RetainedRotatedFiles; index >= 1; index--)
        {
            var path = $"{ActivePath}.{index}";
            if (!File.Exists(path))
            {
                continue;
            }

            if (index == RetainedRotatedFiles)
            {
                File.Delete(path);
            }
            else
            {
                File.Move(path, $"{ActivePath}.{index + 1}", overwrite: true);
            }
        }

        File.Move(ActivePath, $"{ActivePath}.1", overwrite: true);
    }

    private static string NormalizeComponent(string component)
        => string.IsNullOrWhiteSpace(component) ? "unknown" : component.Trim()[..Math.Min(component.Trim().Length, 64)];

    private static string NormalizeEventName(string eventName)
        => string.IsNullOrWhiteSpace(eventName) ? "unknown" : eventName.Trim()[..Math.Min(eventName.Trim().Length, 96)];
}

internal static class LifecycleOwnershipObservation
{
    public const string AbandonedStateUnavailable = "unavailable-under-preserved-admission-semantics";

    public static void RecordAfterOriginalAcquisition(
        LifecycleEventSink events,
        string lockName,
        bool createdNew)
    {
        events.Write(
            "lifecycle.ownerLockAcquisition",
            result: createdNew ? "admitted" : "rejected-existing-mutex",
            fields: new Dictionary<string, string?>
            {
                ["lockName"] = lockName,
                ["createdNew"] = createdNew.ToString(),
                ["ownershipAdmitted"] = createdNew.ToString(),
                ["abandonedState"] = AbandonedStateUnavailable
            });
    }
}

internal static partial class LifecycleEventSanitizer
{
    public static string? Value(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = ControlCharacters().Replace(value, " ").Trim();
        sanitized = SecretAssignment().Replace(sanitized, "$1=[redacted]");
        sanitized = OpenAiKey().Replace(sanitized, "[redacted-key]");
        return sanitized.Length <= 256 ? sanitized : sanitized[..256];
    }

    public static IReadOnlyDictionary<string, string>? Fields(IReadOnlyDictionary<string, string?>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return null;
        }

        var sanitized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in fields)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || SecretFieldName().IsMatch(pair.Key))
            {
                continue;
            }

            var value = Value(pair.Value);
            if (value is not null)
            {
                sanitized[Value(pair.Key) ?? "field"] = value;
            }
        }

        return sanitized.Count == 0 ? null : sanitized;
    }

    [GeneratedRegex("[\\r\\n\\t\\p{Cc}]+")]
    private static partial Regex ControlCharacters();

    [GeneratedRegex("(?i)\\b(password|secret|token|api[_-]?key|authorization)\\s*=\\s*[^\\s;,&]+")]
    private static partial Regex SecretAssignment();

    [GeneratedRegex("(?i)\\bsk-[a-z0-9_-]{12,}\\b")]
    private static partial Regex OpenAiKey();

    [GeneratedRegex("(?i)(password|secret|token|api[_-]?key|authorization)")]
    private static partial Regex SecretFieldName();
}
