using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PimaxVrcSupervisor.Diagnostics;

public static class HardwareFlightRecorderAnalysisSchema
{
    public const string Version = "hardware-flight-recorder-analysis-v1";
}

internal sealed record HardwareFlightRecorderAnalysisRequest(
    IReadOnlyList<string> FlightRecorderPaths,
    string? WindowsEventPath,
    string? ProcessSessionId)
{
    public static HardwareFlightRecorderAnalysisRequest Parse(string[] args)
    {
        var paths = new List<string>();
        string? windows = null;
        string? session = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--flight-recorder", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length) throw new ArgumentException("--flight-recorder requires a path.");
                paths.Add(Path.GetFullPath(args[index]));
            }
            else if (string.Equals(args[index], "--windows-events", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length) throw new ArgumentException("--windows-events requires a path.");
                windows = Path.GetFullPath(args[index]);
            }
            else if (string.Equals(args[index], "--process-session-id", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length) throw new ArgumentException("--process-session-id requires a value.");
                session = args[index];
            }
        }
        if (paths.Count == 0) throw new ArgumentException("At least one --flight-recorder path is required.");
        return new(paths, windows, session);
    }
}

internal sealed record FlightRecorderFileIntegrity(string Path, long Bytes, int ValidRecords, int MalformedLines, int TruncatedFinalLines, int OutOfOrderSequences, int DuplicateEventIds, string[] Schemas);
internal sealed record AnalyzedFlightEvent(string ProcessSessionId, string? OperationId, DateTimeOffset TimestampUtc, long Sequence, string Category, string Routine, string Stage, string? Result, string[] Resources, int ActiveOperationCount, string[] Overlaps, string[] OverlapCategories);
internal sealed record FlightSessionAnalysis(string ProcessSessionId, string BootIdentity, string Classification, bool CleanShutdown, AnalyzedFlightEvent? LastDurableEvent, IReadOnlyList<AnalyzedFlightEvent> ActiveOperationsAtInterruption, IReadOnlyList<AnalyzedFlightEvent> UnmatchedNativeCalls);
internal sealed record OperationOverlap(DateTimeOffset TimestampUtc, string OperationId, string Category, IReadOnlyList<string> OverlappingOperationIds, IReadOnlyList<string> OverlappingCategories);
internal sealed record CorrelationCandidate(string Label, string Strength, DateTimeOffset? ApplicationEventUtc, DateTimeOffset? WindowsEventUtc, string Evidence, string Limitation);
internal sealed record HardwareFlightRecorderAnalysisResult(
    string SchemaVersion,
    IReadOnlyList<FlightRecorderFileIntegrity> Integrity,
    IReadOnlyList<FlightSessionAnalysis> Sessions,
    IReadOnlyList<OperationOverlap> OperationOverlapTimeline,
    IReadOnlyList<AnalyzedFlightEvent> BaseStationResolutionTimeline,
    IReadOnlyList<AnalyzedFlightEvent> HeadsetConnectivityTimeline,
    IReadOnlyList<AnalyzedFlightEvent> MouthFaceTrackerTimeline,
    IReadOnlyList<AnalyzedFlightEvent> ConfiguratorScanTimeline,
    IReadOnlyList<CorrelatedWindowsEvent> WindowsRebootCrashEvents,
    IReadOnlyList<CorrelatedWindowsEvent> BluetoothPnpEvents,
    string CorrelationConfidence,
    IReadOnlyList<string> Contradictions,
    IReadOnlyList<CorrelationCandidate> CandidateRelationships,
    IReadOnlyList<string> Warnings);

internal sealed class HardwareFlightRecorderAnalyzer
{
    public HardwareFlightRecorderAnalysisResult Analyze(HardwareFlightRecorderAnalysisRequest request)
    {
        var all = new List<HardwareFlightRecord>();
        var integrity = new List<FlightRecorderFileIntegrity>();
        foreach (var path in request.FlightRecorderPaths)
        {
            var parsed = Parse(path);
            all.AddRange(parsed.Records);
            integrity.Add(parsed.Integrity);
        }
        if (!string.IsNullOrWhiteSpace(request.ProcessSessionId))
            all = all.Where(record => string.Equals(record.ProcessSessionId, request.ProcessSessionId, StringComparison.Ordinal)).ToList();
        var ordered = all.OrderBy(record => record.TimestampUtc).ThenBy(record => record.ProcessSequence).ToArray();
        var windows = ReadWindows(request.WindowsEventPath);
        var sessions = ordered.GroupBy(record => record.ProcessSessionId, StringComparer.Ordinal).Select(AnalyzeSession).ToArray();
        var events = ordered.Select(ToAnalyzed).ToArray();
        var candidates = Correlate(events, windows);
        var hasDirectProcessFailure = candidates.Any(candidate => candidate.Label == "possibleProcessFailure" && candidate.Strength == "direct evidence");
        var hasSystemFailure = candidates.Any(candidate => candidate.Label == "possibleSystemFailure");
        var confidence = hasDirectProcessFailure ? "high" : hasSystemFailure ? "medium" : candidates.Count > 0 ? "low" : "insufficient";
        return new(
            HardwareFlightRecorderAnalysisSchema.Version,
            integrity,
            sessions,
            events.Where(item => item.Overlaps.Length > 0 || item.OverlapCategories.Length > 0).Select(item => new OperationOverlap(item.TimestampUtc, item.OperationId ?? "none", item.Category, item.Overlaps, item.OverlapCategories)).ToArray(),
            events.Where(item => item.Category.StartsWith("baseStation", StringComparison.Ordinal)).ToArray(),
            events.Where(item => item.Category.Contains("headset", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("pimax", StringComparison.OrdinalIgnoreCase)).ToArray(),
            events.Where(item => item.Category.Contains("mouth", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("faceTracker", StringComparison.OrdinalIgnoreCase)).ToArray(),
            events.Where(item => item.Category == "configuratorScan").ToArray(),
            windows.Where(item => item.Category is "bugCheck" or "systemLifecycle" or "hardwareError" or "applicationFailure").ToArray(),
            windows.Where(item => item.Category is "bluetooth" or "usbPnp" or "serviceControl").ToArray(),
            confidence,
            ["An event preceding another event does not establish causality.", "Missing application returns can result from process or system interruption, forced termination, or recorder loss."],
            candidates,
            ["Use direct evidence, strong correlation, weak correlation, contradictory evidence, or insufficient evidence labels; never infer causality from timing alone."]);
    }

    internal static (IReadOnlyList<HardwareFlightRecord> Records, FlightRecorderFileIntegrity Integrity) Parse(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var records = new List<HardwareFlightRecord>();
        var malformed = 0;
        var truncated = 0;
        var outOfOrder = 0;
        var duplicate = 0;
        long previous = -1;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var schemas = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        while (offset < bytes.Length)
        {
            var newline = Array.IndexOf(bytes, (byte)'\n', offset);
            var end = newline < 0 ? bytes.Length : newline;
            var count = end - offset;
            if (count > 0 && bytes[end - 1] == '\r') count--;
            var text = Encoding.UTF8.GetString(bytes, offset, count);
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<HardwareFlightRecord>(text, HardwareFlightRecorder.JsonOptions) ?? throw new JsonException();
                    records.Add(record);
                    schemas.Add(record.Schema);
                    if (previous >= 0 && record.ProcessSequence < previous) outOfOrder++;
                    previous = record.ProcessSequence;
                    if (!string.IsNullOrWhiteSpace(record.EventId) && !ids.Add(record.EventId)) duplicate++;
                }
                catch
                {
                    malformed++;
                    if (newline < 0 || newline == bytes.Length - 1) truncated++;
                }
            }
            if (newline < 0) break;
            offset = newline + 1;
        }
        return (records, new(path, bytes.Length, records.Count, malformed, truncated, outOfOrder, duplicate, schemas.Order().ToArray()));
    }

    private static FlightSessionAnalysis AnalyzeSession(IGrouping<string, HardwareFlightRecord> group)
    {
        var records = group.OrderBy(record => record.TimestampUtc).ThenBy(record => record.ProcessSequence).ToArray();
        var clean = records.Any(record => record.Stage == "cleanShutdownCompleted");
        var starts = records.Where(record => record.Stage == "operationStarted" && record.OperationId is not null).GroupBy(record => record.OperationId!).ToDictionary(g => g.Key, g => g.Last());
        var terminals = records.Where(record => record.Stage is "operationCompleted" or "operationFailed" or "operationTimedOut" or "operationCancelled" or "operationAbandoned" && record.OperationId is not null).Select(record => record.OperationId!).ToHashSet();
        var active = starts.Where(pair => !terminals.Contains(pair.Key)).Select(pair =>
            ToAnalyzed(records.Last(record => record.OperationId == pair.Key))).ToArray();
        var nativeStarts = records.Where(record => record.Stage == "nativeOrLibraryCallStarted" && record.OperationId is not null).ToArray();
        var nativeReturns = records.Where(record => record.Stage == "nativeOrLibraryCallReturned" && record.OperationId is not null)
            .GroupBy(record => (record.OperationId!, record.Result)).ToDictionary(grouping => grouping.Key, grouping => grouping.Count());
        var unmatched = new List<AnalyzedFlightEvent>();
        foreach (var start in nativeStarts)
        {
            var key = (start.OperationId!, start.Result);
            if (nativeReturns.TryGetValue(key, out var count) && count > 0) nativeReturns[key] = count - 1;
            else unmatched.Add(ToAnalyzed(start));
        }
        var classification = clean ? "cleanExit" : records.Length == 0 ? "unknownInterruption" : "incompleteSession";
        var lastDurable = records.LastOrDefault(record => record.FlushClass == "critical");
        return new(group.Key, records.FirstOrDefault()?.BootIdentity ?? "unknown", classification, clean,
            lastDurable is null ? null : ToAnalyzed(lastDurable), active, unmatched);
    }

    private static AnalyzedFlightEvent ToAnalyzed(HardwareFlightRecord record) => new(
        record.ProcessSessionId, record.OperationId, record.TimestampUtc, record.ProcessSequence, record.OperationCategory,
        record.Routine, record.Stage, record.Result, record.SharedResourceTags, record.ActiveOperationCount,
        record.OverlappingOperationIds, record.OverlappingOperationCategories);

    private static IReadOnlyList<CorrelatedWindowsEvent> ReadWindows(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return [];
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            return root?["events"]?.Deserialize<CorrelatedWindowsEvent[]>(HardwareFlightRecorder.JsonOptions) ?? [];
        }
        catch { return []; }
    }

    private static List<CorrelationCandidate> Correlate(IReadOnlyList<AnalyzedFlightEvent> application, IReadOnlyList<CorrelatedWindowsEvent> windows)
    {
        var result = new List<CorrelationCandidate>();
        foreach (var window in windows)
        {
            if (window.TimestampUtc is null) continue;
            var nearby = application.OrderBy(item => Math.Abs((item.TimestampUtc - window.TimestampUtc.Value).TotalSeconds)).FirstOrDefault();
            if (nearby is null || Math.Abs((nearby.TimestampUtc - window.TimestampUtc.Value).TotalMinutes) > 5) continue;
            var label = window.Category == "applicationFailure" ? "possibleProcessFailure" :
                window.Category is "bugCheck" or "systemLifecycle" or "hardwareError" ? "possibleSystemFailure" : "possibleBluetoothPnpTransition";
            var direct = window.Category == "applicationFailure" && (window.Message?.Contains("PimaxVrcSupervisor", StringComparison.OrdinalIgnoreCase) ?? false);
            result.Add(new(label, direct ? "direct evidence" : Math.Abs((nearby.TimestampUtc - window.TimestampUtc.Value).TotalSeconds) <= 30 ? "strong correlation" : "weak correlation",
                nearby.TimestampUtc, window.TimestampUtc, $"{window.Provider}/{window.EventId} near {nearby.Category}/{nearby.Stage}",
                "Temporal proximity alone does not prove that the application operation caused the Windows event."));
        }
        return result;
    }
}
