using System.Text.Json;
using Xunit;

namespace PimaxVrcSupervisor.Tests;

public sealed class BaseStationStartupAnalysisTests
{
    [Fact]
    public void ParsesValidRecordsAndPreservesOffsets()
    {
        var parsed = Parse("{\"schemaVersion\":\"v1\",\"timestampUtc\":\"2026-01-01T00:00:00Z\",\"sessionId\":\"s\",\"eventType\":\"a\"}\n" +
                           "{\"schemaVersion\":\"v1\",\"timestampUtc\":\"2026-01-01T00:00:01Z\",\"sessionId\":\"s\",\"eventType\":\"b\"}\n");

        Assert.Equal(2, parsed.Integrity.ValidRecords);
        Assert.Equal(0, parsed.Records[0].ByteOffset);
        Assert.True(parsed.Records[1].ByteOffset > 0);
    }

    [Fact]
    public void ReportsMalformedAndTruncatedFinalLines()
    {
        var parsed = Parse("{\"timestampUtc\":\"2026-01-01T00:00:00Z\",\"sessionId\":\"s\"}\n{\"broken\":");

        var file = Assert.Single(parsed.Integrity.Files);
        Assert.Equal(1, file.MalformedLines);
        Assert.True(Assert.Single(file.Malformed).IsFinalLine);
    }

    [Fact]
    public void ReportsMixedSchemasOutOfOrderAndMissingCorrelation()
    {
        var parsed = Parse("{\"schemaVersion\":\"v1\",\"timestampUtc\":\"2026-01-01T00:00:02Z\",\"sessionId\":\"s\"}\n" +
                           "{\"schemaVersion\":\"v2\",\"timestampUtc\":\"2026-01-01T00:00:01Z\"}\n");

        var file = Assert.Single(parsed.Integrity.Files);
        Assert.Equal(new[] { "v1", "v2" }, file.SchemaVersions);
        Assert.Equal(1, file.OutOfOrderTimestamps);
        Assert.Equal(1, file.RecordsWithoutSessionCorrelation);
    }

    [Fact]
    public void ReportsDuplicateEventIdsAndInvalidTimestamp()
    {
        var parsed = Parse("{\"eventId\":\"e\",\"timestampUtc\":\"invalid\",\"sessionId\":\"s\"}\n" +
                           "{\"eventId\":\"e\",\"timestampUtc\":\"2026-01-01T00:00:01Z\",\"sessionId\":\"s\"}\n");

        var file = Assert.Single(parsed.Integrity.Files);
        Assert.Equal(1, file.DuplicateEventIds);
        Assert.Equal(1, file.InvalidTimestamps);
    }

    [Fact]
    public void SegmentsNormalFailedAndScanAssistedSessions()
    {
        var result = AnalyzeFixtures();

        Assert.Equal("session-normal", result.SelectedSessionIds.Normal);
        Assert.Equal("session-failed", result.SelectedSessionIds.FailedNoScan);
        Assert.Equal("session-assisted", result.SelectedSessionIds.FailedScanAssisted);
    }

    [Fact]
    public void ExcludesUnrelatedConfiguratorSession()
    {
        var result = AnalyzeFixtures();

        Assert.False(result.DiscoveredSessions.Single(session => session.SessionId == "session-normal").ConfiguratorOverlap);
        Assert.True(result.DiscoveredSessions.Single(session => session.SessionId == "session-assisted").ConfiguratorOverlap);
    }

    [Fact]
    public void NormalizesUtcAndRelativeTimeDeterministically()
    {
        var result = AnalyzeFixtures();
        var timeline = result.NormalizedTimelines.Single(item => item.Role == "normal");

        Assert.Equal(0, timeline.Events[0].RelativeMilliseconds);
        Assert.True(timeline.Events.Zip(timeline.Events.Skip(1)).All(pair => pair.First.TimestampUtc <= pair.Second.TimestampUtc));
        Assert.All(timeline.Events, item => Assert.Equal(TimeSpan.Zero, item.TimestampUtc.Offset));
    }

    [Fact]
    public void DetectsEarliestDeviceResolutionDivergence()
    {
        Assert.Equal("deviceResolution", AnalyzeFixtures().Comparison.EarliestDivergenceStage);
    }

    [Fact]
    public void ComparesRetriesTimeoutsStationsAndCompletion()
    {
        var comparison = AnalyzeFixtures().Comparison;

        Assert.Equal(0, comparison.Normal!.Timeouts);
        Assert.Equal(1, comparison.FailedNoScan!.Timeouts);
        Assert.Equal(2, comparison.FailedScanAssisted!.Attempts);
        Assert.Equal(1, comparison.FailedScanAssisted.Stations);
        Assert.True(comparison.FailedScanAssisted.Completed);
    }

    [Fact]
    public void CorrelatesScanWithLaterSupervisorProgress()
    {
        var correlation = AnalyzeFixtures().ConfiguratorScanCorrelation;

        Assert.True(correlation.Overlapped);
        Assert.Equal("deviceResolutionCompleted", correlation.FirstSupervisorProgress!.EventType);
        Assert.True(correlation.SupervisorAttemptAlreadyInFlight);
    }

    [Fact]
    public void DoesNotAttributeStationCommandsToConfigurator()
    {
        Assert.False(AnalyzeFixtures().ConfiguratorScanCorrelation.ConfiguratorPerformedStationCommand);
    }

    [Fact]
    public void ClassifiesAmbiguousCausalityAsInsufficientEvidence()
    {
        var classification = AnalyzeFixtures().Classification;

        Assert.Equal("G", classification.Code);
        Assert.Equal("medium", classification.Confidence);
        Assert.NotEmpty(classification.ContradictoryEvidence);
    }

    [Fact]
    public void RecommendsInstrumentationOnly()
    {
        Assert.Equal("Phase 29D-E - improve instrumentation before correction", AnalyzeFixtures().RecommendedNextPhase);
    }

    [Fact]
    public void CommandRequiresExplicitSupervisorInput()
    {
        Assert.Throws<ArgumentException>(() => BaseStationStartupAnalysisRequest.Parse(["base-station-startup-analysis-json"]));
    }

    [Fact]
    public void CommandRequestContainsOnlyInputPaths()
    {
        var request = BaseStationStartupAnalysisRequest.Parse([
            "base-station-startup-analysis-json", "--supervisor-log", Fixture("normal-supervisor.jsonl"),
            "--configurator-log", Fixture("scan-assisted-configurator.jsonl")]);

        Assert.Single(request.SupervisorLogPaths);
        Assert.Single(request.ConfiguratorLogPaths);
        Assert.Equal(2, typeof(BaseStationStartupAnalysisRequest).GetProperties().Length);
    }

    [Fact]
    public void ResultSerializesAsSingleVersionedJsonDocument()
    {
        var json = JsonSerializer.Serialize(AnalyzeFixtures(), BaseStationStartupAnalysisJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("base-station-startup-analysis-v1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    private static BaseStationStartupAnalysisResult AnalyzeFixtures() => new BaseStationStartupAnalyzer().Analyze(new(
        [Fixture("normal-supervisor.jsonl"), Fixture("failed-supervisor.jsonl"), Fixture("scan-assisted-supervisor.jsonl")],
        [Fixture("scan-assisted-configurator.jsonl")]));

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "BaseStationStartup", name);

    private static ParsedLogSet Parse(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"base-station-analysis-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, content);
        try
        {
            return BaseStationStartupAnalyzer.ParseFiles([path], "Supervisor");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
