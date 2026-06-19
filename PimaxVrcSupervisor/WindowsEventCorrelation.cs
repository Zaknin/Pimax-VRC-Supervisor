using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using Microsoft.Win32;

namespace PimaxVrcSupervisor.Diagnostics;

public static class WindowsEventCorrelationSchema
{
    public const string Version = "windows-event-correlation-v1";
}

internal sealed record WindowsEventCorrelationRequest(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? FlightRecorderPath,
    string? ProcessSessionId,
    string? OperationId,
    string? OutputPath)
{
    public static WindowsEventCorrelationRequest Parse(string[] args)
    {
        static string? Value(string[] source, string name)
        {
            for (var index = 0; index < source.Length - 1; index++)
                if (string.Equals(source[index], name, StringComparison.OrdinalIgnoreCase)) return source[index + 1];
            return null;
        }
        if (!DateTimeOffset.TryParse(Value(args, "--start-utc"), out var start)) throw new ArgumentException("--start-utc is required and must be an ISO-8601 timestamp.");
        if (!DateTimeOffset.TryParse(Value(args, "--end-utc"), out var end)) throw new ArgumentException("--end-utc is required and must be an ISO-8601 timestamp.");
        start = start.ToUniversalTime();
        end = end.ToUniversalTime();
        if (end <= start) throw new ArgumentException("--end-utc must be later than --start-utc.");
        if (end - start > TimeSpan.FromHours(24)) throw new ArgumentException("The event-correlation window is limited to 24 hours.");
        return new(start, end, Value(args, "--flight-recorder"), Value(args, "--process-session-id"),
            Value(args, "--operation-id"), Value(args, "--output"));
    }
}

internal sealed record CorrelatedWindowsEvent(
    string LogName,
    string Provider,
    int EventId,
    byte? Level,
    DateTimeOffset? TimestampUtc,
    string Category,
    string? Message);

internal sealed record WindowsEventChannelResult(string Channel, string Status, int RecordCount, string? Error);
internal sealed record CrashDumpFileMetadata(string Path, long Size, DateTimeOffset LastWriteTimeUtc);
internal sealed record CrashDumpStatus(string ConfigurationStatus, int? CrashDumpEnabled, string? DumpFile, IReadOnlyList<CrashDumpFileMetadata> RecentDumpFiles, string[] Warnings);
internal sealed record WindowsEventCorrelationResult(
    string SchemaVersion,
    DateTimeOffset CollectedAtUtc,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? FlightRecorderPath,
    string? ProcessSessionId,
    string? OperationId,
    IReadOnlyList<WindowsEventChannelResult> Channels,
    IReadOnlyList<CorrelatedWindowsEvent> Events,
    CrashDumpStatus CrashDumpStatus,
    string[] Warnings);

internal interface IWindowsEventSource
{
    (IReadOnlyList<CorrelatedWindowsEvent> Events, WindowsEventChannelResult Result) Read(string channel, DateTimeOffset startUtc, DateTimeOffset endUtc, int maximumRecords);
}

internal sealed class WindowsEventLogSource : IWindowsEventSource
{
    public (IReadOnlyList<CorrelatedWindowsEvent> Events, WindowsEventChannelResult Result) Read(
        string channel, DateTimeOffset startUtc, DateTimeOffset endUtc, int maximumRecords)
    {
        if (!OperatingSystem.IsWindows()) return ([], new(channel, "unavailable", 0, "Windows event logs are Windows-only."));
        try
        {
            var start = startUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var end = endUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var xpath = $"*[System[TimeCreated[@SystemTime >= '{start}' and @SystemTime <= '{end}']]]";
            var query = new EventLogQuery(channel, PathType.LogName, xpath) { ReverseDirection = true, TolerateQueryErrors = true };
            using var reader = new EventLogReader(query);
            var events = new List<CorrelatedWindowsEvent>();
            while (events.Count < maximumRecords)
            {
                using var record = reader.ReadEvent();
                if (record is null) break;
                var provider = record.ProviderName ?? "unknown";
                var category = Classify(provider, record.Id);
                if (category == "other") continue;
                string? message = null;
                try { message = SanitizeMessage(record.FormatDescription()); } catch { }
                events.Add(new(channel, provider, record.Id, record.Level, record.TimeCreated is null ? null : new DateTimeOffset(record.TimeCreated.Value).ToUniversalTime(), category, message));
            }
            events.Reverse();
            return (events, new(channel, "available", events.Count, null));
        }
        catch (UnauthorizedAccessException ex) { return ([], new(channel, "accessDenied", 0, ex.GetType().Name)); }
        catch (EventLogNotFoundException ex) { return ([], new(channel, "unavailable", 0, ex.GetType().Name)); }
        catch (Exception ex) { return ([], new(channel, "error", 0, ex.GetType().Name)); }
    }

    internal static string Classify(string provider, int eventId)
    {
        var text = provider.ToLowerInvariant();
        if (text.Contains("bugcheck") || (text.Contains("wer-systemerrorreporting") && eventId == 1001)) return "bugCheck";
        if (text.Contains("kernel-power") || text.Contains("eventlog") && eventId is 6005 or 6006 or 6008
            || text.Contains("kernel-general") && eventId is 12 or 13) return "systemLifecycle";
        if (text.Contains("whea")) return "hardwareError";
        if (text.Contains("application error") || text.Contains(".net runtime") || text.Contains("windows error reporting")) return "applicationFailure";
        if (text.Contains("bluetooth") || text.Contains("bthusb")) return "bluetooth";
        if (text.Contains("kernel-pnp") || text.Contains("userpnp") || text.Contains("driverframeworks") || text.Contains("usb")) return "usbPnp";
        if (text.Contains("service control manager")) return "serviceControl";
        return "other";
    }

    private static string? SanitizeMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var sanitized = value.Replace(Environment.MachineName, "<machine>", StringComparison.OrdinalIgnoreCase);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) sanitized = sanitized.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length <= 2048 ? sanitized : sanitized[..2048];
    }
}

internal sealed class WindowsEventCorrelationCollector
{
    internal static readonly string[] Channels =
    [
        "System", "Application", "Microsoft-Windows-Bluetooth-BthLEPrepairing/Operational",
        "Microsoft-Windows-DriverFrameworks-UserMode/Operational", "Microsoft-Windows-Kernel-PnP/Configuration"
    ];
    private readonly IWindowsEventSource _source;
    internal WindowsEventCorrelationCollector(IWindowsEventSource? source = null) => _source = source ?? new WindowsEventLogSource();

    public WindowsEventCorrelationResult Collect(WindowsEventCorrelationRequest request)
    {
        var events = new List<CorrelatedWindowsEvent>();
        var channels = new List<WindowsEventChannelResult>();
        foreach (var channel in Channels)
        {
            var result = _source.Read(channel, request.StartUtc, request.EndUtc, 500);
            events.AddRange(result.Events);
            channels.Add(result.Result);
        }
        var output = new WindowsEventCorrelationResult(
            WindowsEventCorrelationSchema.Version, DateTimeOffset.UtcNow, request.StartUtc, request.EndUtc,
            request.FlightRecorderPath, request.ProcessSessionId, request.OperationId, channels,
            events.OrderBy(item => item.TimestampUtc).ThenBy(item => item.Provider).ThenBy(item => item.EventId).ToArray(),
            ReadDumpStatus(request.StartUtc),
            ["Temporal proximity is correlation, not proof of causality."]);
        if (!string.IsNullOrWhiteSpace(request.OutputPath))
        {
            var path = Path.GetFullPath(request.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(output, HardwareFlightRecorder.JsonOptions));
        }
        return output;
    }

    private static CrashDumpStatus ReadDumpStatus(DateTimeOffset sinceUtc)
    {
        var warnings = new List<string>();
        int? enabled = null;
        string? dumpFile = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl", writable: false);
            enabled = key?.GetValue("CrashDumpEnabled") as int?;
            dumpFile = key?.GetValue("DumpFile") as string;
        }
        catch (Exception ex) { warnings.Add($"Crash-dump configuration unavailable: {ex.GetType().Name}"); }
        var files = new List<CrashDumpFileMetadata>();
        foreach (var candidate in new[] { @"%SystemRoot%\MEMORY.DMP", @"%SystemRoot%\Minidump" }.Select(Environment.ExpandEnvironmentVariables))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    var info = new FileInfo(candidate);
                    if (info.LastWriteTimeUtc >= sinceUtc.UtcDateTime) files.Add(new(candidate.Replace(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "%SystemRoot%", StringComparison.OrdinalIgnoreCase), info.Length, info.LastWriteTimeUtc));
                }
                else if (Directory.Exists(candidate))
                {
                    files.AddRange(Directory.EnumerateFiles(candidate, "*.dmp").Select(path => new FileInfo(path))
                        .Where(info => info.LastWriteTimeUtc >= sinceUtc.UtcDateTime).Take(20)
                        .Select(info => new CrashDumpFileMetadata(info.FullName.Replace(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "%SystemRoot%", StringComparison.OrdinalIgnoreCase), info.Length, info.LastWriteTimeUtc)));
                }
            }
            catch (Exception ex) { warnings.Add($"Dump metadata unavailable: {ex.GetType().Name}"); }
        }
        return new(enabled is null ? "unknown" : "available", enabled, dumpFile, files.OrderBy(file => file.LastWriteTimeUtc).ToArray(), warnings.ToArray());
    }
}

internal static class DeferredWindowsEventSnapshot
{
    public static void ScheduleIfNeeded(PreviousSessionAssessment assessment, DateTimeOffset currentSessionStartUtc)
    {
        if (assessment.PreviousCleanShutdown || assessment.LastDurableEventUtc is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PimaxVrcSupervisor", "Diagnostics", "SystemEvents");
                var output = Path.Combine(root, $"incomplete-session-{assessment.PreviousProcessSessionId ?? "unknown"}.json");
                if (File.Exists(output)) return;
                var request = new WindowsEventCorrelationRequest(
                    assessment.LastDurableEventUtc.Value.AddMinutes(-2), currentSessionStartUtc.AddMinutes(5), null,
                    assessment.PreviousProcessSessionId, null, output);
                await Task.Run(() => new WindowsEventCorrelationCollector().Collect(request), timeout.Token);
            }
            catch { }
        });
    }
}
