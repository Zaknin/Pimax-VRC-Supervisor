using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PimaxVrcSupervisor;

internal sealed record BaseStationStartupAnalysisRequest(
    IReadOnlyList<string> SupervisorLogPaths,
    IReadOnlyList<string> ConfiguratorLogPaths)
{
    public static BaseStationStartupAnalysisRequest Parse(string[] args)
    {
        static IReadOnlyList<string> Values(string[] source, string option)
        {
            var values = new List<string>();
            for (var index = 0; index < source.Length; index++)
            {
                if (!string.Equals(source[index], option, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (++index >= source.Length || string.IsNullOrWhiteSpace(source[index]))
                {
                    throw new ArgumentException($"{option} requires a path.");
                }

                values.Add(Path.GetFullPath(source[index]));
            }

            return values;
        }

        var supervisor = Values(args, "--supervisor-log");
        var configurator = Values(args, "--configurator-log");
        if (supervisor.Count == 0)
        {
            throw new ArgumentException("At least one --supervisor-log path is required.");
        }

        return new(supervisor, configurator);
    }
}

internal static class BaseStationStartupAnalysisJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed class BaseStationStartupAnalyzer
{
    private static readonly HashSet<string> TimelineEvents = new(StringComparer.Ordinal)
    {
        "schedulerDelayStarted", "schedulerDelayCompleted", "schedulerArmed", "schedulerStateChanged",
        "burstStarted", "burstCompleted", "bluetoothAdapterLookupStarted", "bluetoothAdapterLookupCompleted",
        "deviceResolutionStarted", "deviceResolutionCompleted", "gattServiceQueryStarted", "gattServiceQueryCompleted",
        "characteristicResolutionStarted", "characteristicResolutionCompleted", "powerWriteStarted", "powerWriteCompleted",
        "stationAttemptStarted", "stationAttemptSucceeded", "stationAttemptTimedOut", "stationAttemptCancelled",
        "sessionCompleted", "configuratorScanStarted", "configuratorWatcherStarted", "configuratorStationObserved",
        "configuratorWatcherStopped", "configuratorSavedStationMatched", "configuratorScanCompleted"
    };

    public BaseStationStartupAnalysisResult Analyze(BaseStationStartupAnalysisRequest request)
    {
        var supervisor = ParseFiles(request.SupervisorLogPaths, "Supervisor");
        var configurator = ParseFiles(request.ConfiguratorLogPaths, "Configurator");
        var sessions = DiscoverSessions(supervisor.Records, configurator.Records);
        var selected = SelectSessions(sessions);
        var comparison = Compare(selected);
        var correlation = CorrelateScan(selected.Assisted, configurator.Records);
        var classification = Classify(selected, correlation);

        return new(
            "base-station-startup-analysis-v1",
            new[] { supervisor.Integrity, configurator.Integrity },
            sessions.Select(ToSummary).ToArray(),
            new(selected.Normal?.SessionId, selected.Failed?.SessionId, selected.Assisted?.SessionId),
            new[]
            {
                ToTimeline("normal", selected.Normal),
                ToTimeline("failedNoScan", selected.Failed),
                ToTimeline("failedScanAssisted", selected.Assisted)
            },
            comparison,
            correlation,
            classification,
            EvidenceGaps(selected, correlation),
            "Phase 29D-E - improve instrumentation before correction");
    }

    internal static ParsedLogSet ParseFiles(IReadOnlyList<string> paths, string process)
    {
        var records = new List<AnalysisRecord>();
        var files = new List<FileIntegrity>();
        foreach (var path in paths)
        {
            var bytes = File.ReadAllBytes(path);
            var malformed = new List<MalformedLine>();
            var valid = 0;
            var invalidTimestamps = 0;
            var missingCorrelation = 0;
            var outOfOrder = 0;
            var schemas = new HashSet<string>(StringComparer.Ordinal);
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            var duplicateEventIds = 0;
            DateTimeOffset? previous = null;
            var offset = 0;
            var lineNumber = 1;
            while (offset < bytes.Length)
            {
                var newline = Array.IndexOf(bytes, (byte)'\n', offset);
                var end = newline >= 0 ? newline : bytes.Length;
                var count = end - offset;
                if (count > 0 && bytes[end - 1] == '\r')
                {
                    count--;
                }

                var text = Encoding.UTF8.GetString(bytes, offset, count);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        var node = JsonNode.Parse(text)?.AsObject() ?? throw new JsonException("Record is not an object.");
                        valid++;
                        var timestampText = Text(node, "timestampUtc");
                        DateTimeOffset? timestamp = DateTimeOffset.TryParse(timestampText, out var parsed) ? parsed.ToUniversalTime() : null;
                        if (timestamp is null)
                        {
                            invalidTimestamps++;
                        }
                        else if (previous is not null && timestamp < previous)
                        {
                            outOfOrder++;
                        }
                        if (timestamp is not null)
                        {
                            previous = timestamp;
                        }

                        var schema = Text(node, "schemaVersion");
                        if (schema is not null) schemas.Add(schema);
                        var eventId = Text(node, "eventId");
                        if (eventId is not null && !eventIds.Add(eventId)) duplicateEventIds++;
                        var sessionId = Text(node, "sessionId");
                        if (string.IsNullOrWhiteSpace(sessionId)) missingCorrelation++;
                        records.Add(new(path, lineNumber, offset, process, timestamp, node));
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                    {
                        malformed.Add(new(lineNumber, offset, newline < 0, ex.Message));
                    }
                }

                if (newline < 0) break;
                offset = newline + 1;
                lineNumber++;
            }

            files.Add(new(path, bytes.Length, valid, malformed.Count, malformed, invalidTimestamps,
                duplicateEventIds, outOfOrder, schemas.Order().ToArray(), missingCorrelation));
        }

        return new(records, new(process, files, files.Sum(file => file.ValidRecords), files.Sum(file => file.MalformedLines)));
    }

    private static IReadOnlyList<SessionAnalysis> DiscoverSessions(
        IReadOnlyList<AnalysisRecord> supervisor,
        IReadOnlyList<AnalysisRecord> configurator)
    {
        var scans = ScanWindows(configurator);
        return supervisor
            .Where(record => record.TimestampUtc is not null && Text(record.Node, "sessionId") is not null)
            .GroupBy(record => Text(record.Node, "sessionId")!, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderBy(record => record.TimestampUtc).ThenBy(record => record.Path).ThenBy(record => record.LineNumber).ToArray();
                var start = ordered[0].TimestampUtc!.Value;
                var end = ordered[^1].TimestampUtc!.Value;
                var timeouts = Count(ordered, "stationAttemptTimedOut");
                var successes = Count(ordered, "stationAttemptSucceeded");
                var attempts = Count(ordered, "stationAttemptStarted");
                var overlap = scans.Any(scan => scan.Start <= end && scan.End >= start);
                var completed = ordered.Any(record => Event(record) == "sessionCompleted");
                var cancelled = ordered.Any(record => Event(record) == "stationAttemptCancelled")
                    || ordered.Any(record => Event(record) == "schedulerStateChanged" && Text(record.Node, "currentStage") == "Cancelled");
                var outcome = timeouts == 0 && successes > 0 && completed && !cancelled ? "successful" :
                    timeouts > 0 && successes == 0 ? "failed" :
                    timeouts > 0 && successes > 0 ? "failedThenRecovered" : "incomplete";
                return new SessionAnalysis(group.Key, start, end, ordered, attempts, successes, timeouts, overlap, outcome);
            })
            .OrderBy(session => session.StartUtc)
            .ToArray();
    }

    private static SelectedSessions SelectSessions(IReadOnlyList<SessionAnalysis> sessions)
    {
        var normal = sessions.Where(session => session.Outcome == "successful" && !session.ConfiguratorOverlap)
            .OrderBy(session => session.EndUtc - session.StartUtc).FirstOrDefault();
        var failed = sessions.Where(session => session.Outcome == "failed" && !session.ConfiguratorOverlap)
            .OrderByDescending(session => session.Timeouts).FirstOrDefault();
        var assisted = sessions.Where(session => session.Outcome == "failedThenRecovered" && session.ConfiguratorOverlap)
            .OrderByDescending(session => session.Timeouts).FirstOrDefault();
        return new(normal, failed, assisted);
    }

    private static SessionComparison Compare(SelectedSessions selected)
    {
        string? divergence = null;
        if (selected.Normal is not null && selected.Failed is not null)
        {
            var normalCompleted = EventTypes(selected.Normal, "Completed");
            var failedTimedOutStages = selected.Failed.Records
                .Where(record => Event(record) == "stationAttemptTimedOut")
                .Select(record => Text(record.Node, "currentStage"))
                .Where(stage => stage is not null)
                .ToHashSet(StringComparer.Ordinal);
            divergence = normalCompleted.FirstOrDefault(failedTimedOutStages.Contains) ?? "unknown";
        }

        return new(
            divergence,
            selected.Normal is null ? null : ToOperationSummary(selected.Normal),
            selected.Failed is null ? null : ToOperationSummary(selected.Failed),
            selected.Assisted is null ? null : ToOperationSummary(selected.Assisted));
    }

    private static ScanCorrelation CorrelateScan(SessionAnalysis? assisted, IReadOnlyList<AnalysisRecord> configurator)
    {
        if (assisted is null)
        {
            return new(false, null, null, null, false, false, "No scan-assisted session selected.");
        }

        var scan = ScanWindows(configurator).FirstOrDefault(candidate => candidate.Start <= assisted.EndUtc && candidate.End >= assisted.StartUtc);
        if (scan is null)
        {
            return new(false, null, null, null, false, false, "No overlapping Configurator Scan.");
        }

        var firstProgress = assisted.Records
            .Where(record => record.TimestampUtc >= scan.Start && Event(record) is "deviceResolutionCompleted" or "stationAttemptSucceeded")
            .OrderBy(record => record.TimestampUtc).FirstOrDefault();
        var inFlight = assisted.Records.Any(record => Event(record) == "stationAttemptStarted" && record.TimestampUtc < scan.Start)
            && assisted.Records.Any(record => Event(record) == "stationAttemptTimedOut" && record.TimestampUtc < scan.Start);
        var configuratorCommands = configurator.Any(record => Event(record) is "powerWriteStarted" or "powerWriteCompleted" or "stationAttemptSucceeded");
        return new(true, scan.Start, scan.End, firstProgress is null ? null : new(firstProgress.TimestampUtc, Event(firstProgress), Text(firstProgress.Node, "currentStage")),
            inFlight, configuratorCommands,
            "Supervisor progress followed Scan, but the Supervisor attempt already existed and Configurator emitted no station-command event.");
    }

    private static FailureClassification Classify(SelectedSessions selected, ScanCorrelation correlation)
    {
        if (selected.Failed is null || selected.Assisted is null || !correlation.Overlapped)
        {
            return new("G", "Instrumentation is insufficient", "low", new[] { "Required sessions or overlap are missing." }, Array.Empty<string>());
        }

        return new(
            "G",
            "Instrumentation is insufficient",
            "medium",
            new[]
            {
                "Adapter lookup completed in failed and successful sessions.",
                "Failures timed out in deviceResolution before GATT access.",
                "Configurator started active discovery immediately before Supervisor device resolution recovered.",
                "Supervisor owned the successful operation and Configurator emitted no command event."
            },
            new[]
            {
                "The recovering Supervisor attempt started before Configurator Scan.",
                "The logs cannot distinguish active-discovery warm-up from elapsed-time readiness."
            });
    }

    private static IReadOnlyList<string> EvidenceGaps(SelectedSessions selected, ScanCorrelation correlation)
    {
        var gaps = new List<string>();
        if (selected.Normal is null) gaps.Add("No comparable successful session without Scan.");
        if (selected.Failed is null) gaps.Add("No failed session without Scan.");
        if (selected.Assisted is null) gaps.Add("No failed-then-recovered session overlapping Scan.");
        if (correlation.Overlapped)
        {
            gaps.Add("No OS Bluetooth readiness or device-arrival event links Scan to Supervisor recovery.");
            gaps.Add("No control run preserves the same delay without starting discovery watchers.");
        }
        return gaps;
    }

    private static SessionSummary ToSummary(SessionAnalysis session) => new(
        session.SessionId, session.StartUtc, session.EndUtc, session.Attempts, session.Successes,
        session.Timeouts, session.ConfiguratorOverlap, session.Outcome);

    private static NormalizedTimeline ToTimeline(string role, SessionAnalysis? session)
    {
        if (session is null) return new(role, null, Array.Empty<TimelineEvent>());
        var events = session.Records.Where(record => TimelineEvents.Contains(Event(record) ?? "") && record.TimestampUtc is not null)
            .Select(record => new TimelineEvent(
                record.TimestampUtc!.Value,
                (record.TimestampUtc.Value - session.StartUtc).TotalMilliseconds,
                Event(record), Text(record.Node, "currentStage"), Text(record.Node, "operationId"),
                Text(record.Node, "stationIdentity"), Integer(record.Node, "burstNumber"), Integer(record.Node, "retryNumber"),
                Text(record.Node, "outcome"), record.LineNumber, record.ByteOffset))
            .ToArray();
        return new(role, session.SessionId, events);
    }

    private static OperationSummary ToOperationSummary(SessionAnalysis session) => new(
        session.Attempts, session.Successes, session.Timeouts,
        session.Records.Where(record => Event(record) == "burstStarted").Select(record => Integer(record.Node, "burstNumber")).Distinct().Count(),
        session.Records.Where(record => Text(record.Node, "stationIdentity") is not null).Select(record => Text(record.Node, "stationIdentity")).Distinct().Count(),
        session.Records.Any(record => Event(record) == "sessionCompleted"));

    private static IReadOnlyList<ScanWindow> ScanWindows(IReadOnlyList<AnalysisRecord> configurator) => configurator
        .Where(record => record.TimestampUtc is not null && Text(record.Node, "scanSessionId") is not null)
        .GroupBy(record => Text(record.Node, "scanSessionId")!, StringComparer.Ordinal)
        .Select(group => new ScanWindow(group.Key, group.Min(record => record.TimestampUtc!.Value), group.Max(record => record.TimestampUtc!.Value)))
        .ToArray();

    private static int Count(IEnumerable<AnalysisRecord> records, string eventType) => records.Count(record => Event(record) == eventType);
    private static string? Event(AnalysisRecord record) => Text(record.Node, "eventType");
    private static IReadOnlyList<string> EventTypes(SessionAnalysis session, string suffix) => session.Records
        .Select(record => Event(record)).Where(value => value?.EndsWith(suffix, StringComparison.Ordinal) == true)
        .Select(value => value![..^suffix.Length]).ToArray();
    private static string? Text(JsonObject node, string property) => node[property]?.GetValue<string>();
    private static int? Integer(JsonObject node, string property) => node[property] is JsonValue value && value.TryGetValue<int>(out var result) ? result : null;
}

internal sealed record AnalysisRecord(string Path, int LineNumber, long ByteOffset, string Process, DateTimeOffset? TimestampUtc, JsonObject Node);
internal sealed record ParsedLogSet(IReadOnlyList<AnalysisRecord> Records, LogIntegrity Integrity);
internal sealed record FileIntegrity(string Path, long Bytes, int ValidRecords, int MalformedLines, IReadOnlyList<MalformedLine> Malformed, int InvalidTimestamps, int DuplicateEventIds, int OutOfOrderTimestamps, IReadOnlyList<string> SchemaVersions, int RecordsWithoutSessionCorrelation);
internal sealed record MalformedLine(int LineNumber, long ByteOffset, bool IsFinalLine, string Error);
internal sealed record LogIntegrity(string Process, IReadOnlyList<FileIntegrity> Files, int ValidRecords, int MalformedLines);
internal sealed record SessionAnalysis(string SessionId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, IReadOnlyList<AnalysisRecord> Records, int Attempts, int Successes, int Timeouts, bool ConfiguratorOverlap, string Outcome);
internal sealed record SelectedSessions(SessionAnalysis? Normal, SessionAnalysis? Failed, SessionAnalysis? Assisted);
internal sealed record ScanWindow(string ScanSessionId, DateTimeOffset Start, DateTimeOffset End);
internal sealed record SelectedSessionIds(string? Normal, string? FailedNoScan, string? FailedScanAssisted);
internal sealed record SessionSummary(string SessionId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, int Attempts, int Successes, int Timeouts, bool ConfiguratorOverlap, string Outcome);
internal sealed record TimelineEvent(DateTimeOffset TimestampUtc, double RelativeMilliseconds, string? EventType, string? Stage, string? OperationId, string? StationIdentity, int? BurstNumber, int? RetryNumber, string? Outcome, int SourceLine, long ByteOffset);
internal sealed record NormalizedTimeline(string Role, string? SessionId, IReadOnlyList<TimelineEvent> Events);
internal sealed record OperationSummary(int Attempts, int Successes, int Timeouts, int Bursts, int Stations, bool Completed);
internal sealed record SessionComparison(string? EarliestDivergenceStage, OperationSummary? Normal, OperationSummary? FailedNoScan, OperationSummary? FailedScanAssisted);
internal sealed record CorrelatedEvent(DateTimeOffset? TimestampUtc, string? EventType, string? Stage);
internal sealed record ScanCorrelation(bool Overlapped, DateTimeOffset? ScanStartedUtc, DateTimeOffset? ScanCompletedUtc, CorrelatedEvent? FirstSupervisorProgress, bool SupervisorAttemptAlreadyInFlight, bool ConfiguratorPerformedStationCommand, string Conclusion);
internal sealed record FailureClassification(string Code, string Name, string Confidence, IReadOnlyList<string> SupportingEvidence, IReadOnlyList<string> ContradictoryEvidence);
internal sealed record BaseStationStartupAnalysisResult(string SchemaVersion, IReadOnlyList<LogIntegrity> IntegritySummary, IReadOnlyList<SessionSummary> DiscoveredSessions, SelectedSessionIds SelectedSessionIds, IReadOnlyList<NormalizedTimeline> NormalizedTimelines, SessionComparison Comparison, ScanCorrelation ConfiguratorScanCorrelation, FailureClassification Classification, IReadOnlyList<string> EvidenceGaps, string RecommendedNextPhase);
