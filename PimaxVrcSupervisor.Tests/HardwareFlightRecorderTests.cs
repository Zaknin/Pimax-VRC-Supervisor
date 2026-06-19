using System.Collections.Concurrent;
using System.Text.Json;
using PimaxVrcSupervisor.Diagnostics;
using Xunit;

namespace PimaxVrcSupervisor.Tests;

[CollectionDefinition("HardwareFlightRecorder", DisableParallelization = true)]
public sealed class HardwareFlightRecorderCollection;

[Collection("HardwareFlightRecorder")]
public sealed class HardwareFlightRecorderTests
{
    [Fact]
    public void WriterProducesDeterministicIncreasingSequencesAndValidJsonl()
    {
        using var writer = new CaptureWriter();
        using (var engine = new HardwareFlightRecorderEngine(writer))
        {
            engine.Enqueue(Record("heartbeat"), false);
            engine.Enqueue(Record("heartbeat"), false);
        }
        var records = writer.Lines.Select(line => JsonSerializer.Deserialize<HardwareFlightRecord>(line, HardwareFlightRecorder.JsonOptions)!).ToArray();
        Assert.Equal(new long[] { 1, 2 }, records.Select(record => record.ProcessSequence));
        Assert.All(records, record => Assert.False(string.IsNullOrWhiteSpace(record.EventId)));
    }

    [Fact]
    public void ConcurrentProducersRemainValidAndUnique()
    {
        using var writer = new CaptureWriter();
        using (var engine = new HardwareFlightRecorderEngine(writer))
            Parallel.For(0, 500, _ => engine.Enqueue(Record("heartbeat"), false));
        var records = writer.Lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("eventId").GetString()).ToArray();
        Assert.Equal(500, records.Length);
        Assert.Equal(500, records.Distinct().Count());
    }

    [Fact]
    public void CriticalWritesInvokeDurableWriterAndFlush()
    {
        using var writer = new CaptureWriter();
        using (var engine = new HardwareFlightRecorderEngine(writer)) engine.Enqueue(Record("operationStarted"), true);
        Assert.Contains(true, writer.DurableWrites);
        Assert.True(writer.FlushCalls > 0);
    }

    [Fact]
    public void NormalWritesDoNotRequestDurableFlush()
    {
        using var writer = new CaptureWriter();
        using (var engine = new HardwareFlightRecorderEngine(writer)) engine.Enqueue(Record("heartbeat"), false);
        Assert.Contains(false, writer.DurableWrites);
    }

    [Fact]
    public void BoundedQueueAccountsForDroppedEvents()
    {
        using var writer = new BlockingWriter();
        using var engine = new HardwareFlightRecorderEngine(writer);
        engine.Enqueue(Record("first"), false);
        Assert.True(writer.Started.Wait(TimeSpan.FromSeconds(2)));
        for (var index = 0; index < HardwareFlightRecorderEngine.QueueCapacity + 100; index++) engine.Enqueue(Record("queued"), false);
        Assert.True(engine.Dropped > 0);
        writer.Release.Set();
    }

    [Fact]
    public void WriterFailureIsCountedWithoutRecursiveFailure()
    {
        using var writer = new FailingWriter();
        using (var engine = new HardwareFlightRecorderEngine(writer))
        {
            for (var index = 0; index < 10; index++) engine.Enqueue(Record("heartbeat"), false);
        }
        Assert.True(writer.Attempts > 0);
    }

    [Fact]
    public void RotationMovesFullActiveFileAndPreservesNewRecord()
    {
        var directory = TempDirectory();
        var path = Path.Combine(directory, "flight.jsonl");
        File.WriteAllBytes(path, new byte[RotatingHardwareFlightWriter.MaxFileBytes]);
        using (var writer = new RotatingHardwareFlightWriter(path)) writer.Write("{}", true);
        Assert.Equal(RotatingHardwareFlightWriter.MaxFileBytes, new FileInfo(path + ".1").Length);
        Assert.Equal("{}", File.ReadAllText(path).Trim());
    }

    [Fact]
    public void TotalRotationPolicyIsExactly128MiB()
        => Assert.Equal(128L * 1024 * 1024, RotatingHardwareFlightWriter.MaxFileBytes * RotatingHardwareFlightWriter.MaxFiles);

    [Fact]
    public void ParserRecoversBeforeTruncatedFinalLine()
    {
        var parsed = HardwareFlightRecorderAnalyzer.Parse(Fixture("truncated-final-line.jsonl"));
        Assert.Equal(1, parsed.Integrity.ValidRecords);
        Assert.Equal(1, parsed.Integrity.MalformedLines);
        Assert.Equal(1, parsed.Integrity.TruncatedFinalLines);
    }

    [Fact]
    public void CleanShutdownIsClassified()
    {
        var assessment = HardwareFlightRecorder.AssessPreviousSession(Fixture("clean-shutdown.jsonl"), Boot("boot-a"), "Supervisor");
        Assert.Equal("cleanExit", assessment.Classification);
        Assert.True(assessment.PreviousCleanShutdown);
    }

    [Fact]
    public void SameBootInterruptionIsClassifiedWithoutCallingItCrash()
    {
        var assessment = HardwareFlightRecorder.AssessPreviousSession(Fixture("same-boot-interruption.jsonl"), Boot("boot-a"), "Supervisor");
        Assert.Equal("sameBootProcessInterruption", assessment.Classification);
        Assert.Contains("not proof", assessment.Warnings.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BootChangeWithMissingShutdownIsClassified()
    {
        var assessment = HardwareFlightRecorder.AssessPreviousSession(Fixture("same-boot-interruption.jsonl"), Boot("boot-new"), "Supervisor");
        Assert.Equal("bootChangedWithIncompleteSession", assessment.Classification);
    }

    [Fact]
    public void UnknownBootMetadataIsReportedLowConfidence()
    {
        var path = Path.Combine(TempDirectory(), "unknown.jsonl");
        File.WriteAllText(path, "{\"schema\":\"supervisor-session-summary-v1\",\"timestampUtc\":\"2026-01-01T00:00:00Z\",\"processName\":\"Other\",\"stage\":\"heartbeat\"}\n");
        var assessment = HardwareFlightRecorder.AssessPreviousSession(path, Boot("boot"), "Supervisor");
        Assert.Equal("unknownInterruption", assessment.Classification);
        Assert.Equal("low", assessment.Confidence);
    }

    [Fact]
    public void RegistrySupportsRegistrationStageAndTerminalRemoval()
    {
        var registry = new ActiveHardwareOperationRegistry();
        var operation = Active("a", null);
        Assert.True(registry.TryAdd(operation));
        Assert.True(registry.TryUpdateStage("a", "nativeOrLibraryCallStarted"));
        Assert.Equal("nativeOrLibraryCallStarted", registry.Snapshot().Single().LastStage);
        Assert.True(registry.TryRemove("a", out _));
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void RegistryRejectsDuplicateIdsAndSupportsNestedOverlap()
    {
        var registry = new ActiveHardwareOperationRegistry();
        Assert.True(registry.TryAdd(Active("parent", null)));
        Assert.True(registry.TryAdd(Active("child", "parent")));
        Assert.False(registry.TryAdd(Active("child", "parent")));
        Assert.Equal(2, registry.Count);
        Assert.Equal("parent", registry.Snapshot().Single(item => item.OperationId == "child").ParentOperationId);
    }

    [Fact]
    public async Task ScopeRecordsPairingAndDoesNotInvokeWrappedCallTwice()
    {
        using var writer = new CaptureWriter();
        var summary = Path.Combine(TempDirectory(), "summary.jsonl");
        var calls = 0;
        using (var session = HardwareFlightRecorder.StartForTest("Supervisor", writer, summary, Boot("boot")))
        {
            using var scope = HardwareFlightRecorder.Begin(new("headsetConnectivityDetection", "probe", ["usbPnp"]));
            var result = await HardwareFlightRecorder.NativeCallAsync(scope, "fake", () => Task.FromResult(++calls));
            scope.Completed(result.ToString());
            session.MarkCleanShutdown();
        }
        Assert.Equal(1, calls);
        var stages = writer.Lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("stage").GetString()).ToArray();
        Assert.Contains("nativeOrLibraryCallStarted", stages);
        Assert.Contains("nativeOrLibraryCallReturned", stages);
        Assert.Contains("operationCompleted", stages);
    }

    [Fact]
    public void ExceptionIsRethrownAfterInstrumentation()
    {
        using var writer = new CaptureWriter();
        var summary = Path.Combine(TempDirectory(), "summary.jsonl");
        using var session = HardwareFlightRecorder.StartForTest("Supervisor", writer, summary, Boot("boot"));
        using var scope = HardwareFlightRecorder.Begin(new("pimaxPnpQuery", "probe", ["usbPnp"]));
        Assert.Throws<InvalidOperationException>(() => HardwareFlightRecorder.NativeCallAsync<int>(scope, "fake", () => Task.FromException<int>(new InvalidOperationException("synthetic"))).GetAwaiter().GetResult());
    }

    [Theory]
    [InlineData("Microsoft-Windows-Kernel-Power", 41, "systemLifecycle")]
    [InlineData("Microsoft-Windows-WHEA-Logger", 18, "hardwareError")]
    [InlineData("Application Error", 1000, "applicationFailure")]
    [InlineData(".NET Runtime", 1026, "applicationFailure")]
    [InlineData("Windows Error Reporting", 1001, "applicationFailure")]
    [InlineData("BTHUSB", 17, "bluetooth")]
    [InlineData("Microsoft-Windows-Kernel-PnP", 219, "usbPnp")]
    public void WindowsProvidersAreClassified(string provider, int eventId, string expected)
        => Assert.Equal(expected, WindowsEventLogSource.Classify(provider, eventId));

    [Fact]
    public void WindowsCollectorReportsUnavailableAndAccessDeniedChannels()
    {
        var source = new FakeWindowsEventSource();
        var result = new WindowsEventCorrelationCollector(source).Collect(new(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, null, null, null, null));
        Assert.Contains(result.Channels, channel => channel.Status == "accessDenied");
        Assert.Contains(result.Channels, channel => channel.Status == "unavailable");
    }

    [Fact]
    public void WindowsRequestEnforcesBoundedUtcWindow()
    {
        Assert.Throws<ArgumentException>(() => WindowsEventCorrelationRequest.Parse(["--start-utc", "2026-01-01T00:00:00Z", "--end-utc", "2026-01-03T00:00:00Z"]));
    }

    [Fact]
    public void WindowsOutputIsDeterministicForFakeSource()
    {
        var collector = new WindowsEventCorrelationCollector(new FakeWindowsEventSource(alwaysAvailable: true));
        var request = new WindowsEventCorrelationRequest(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-01T00:01:00Z"), null, null, null, null);
        var first = JsonSerializer.Serialize(collector.Collect(request) with { CollectedAtUtc = default }, HardwareFlightRecorder.JsonOptions);
        var second = JsonSerializer.Serialize(collector.Collect(request) with { CollectedAtUtc = default }, HardwareFlightRecorder.JsonOptions);
        Assert.Equal(first, second);
    }

    [Fact]
    public void AnalyzerFindsLastDurableActiveAndUnmatchedCall()
    {
        var result = new HardwareFlightRecorderAnalyzer().Analyze(new([Fixture("reboot-active-operation.jsonl")], Fixture("bugcheck-base-station.json"), null));
        var session = Assert.Single(result.Sessions);
        Assert.Equal("incompleteSession", session.Classification);
        Assert.Single(session.ActiveOperationsAtInterruption);
        Assert.Single(session.UnmatchedNativeCalls);
        Assert.Equal("nativeOrLibraryCallStarted", session.LastDurableEvent!.Stage);
    }

    [Fact]
    public void AnalyzerReportsSystemCorrelationWithoutClaimingCausality()
    {
        var result = new HardwareFlightRecorderAnalyzer().Analyze(new([Fixture("reboot-active-operation.jsonl")], Fixture("bugcheck-base-station.json"), null));
        Assert.Contains(result.CandidateRelationships, candidate => candidate.Label == "possibleSystemFailure");
        Assert.All(result.CandidateRelationships, candidate => Assert.Contains("does not prove", candidate.Limitation));
    }

    [Fact]
    public void AnalyzerCapturesThreeRoutineOverlap()
    {
        var result = new HardwareFlightRecorderAnalyzer().Analyze(new([Fixture("three-routine-overlap.jsonl")], null, null));
        Assert.Equal(2, result.OperationOverlapTimeline.Count);
        Assert.NotEmpty(result.BaseStationResolutionTimeline);
        Assert.NotEmpty(result.HeadsetConnectivityTimeline);
        Assert.NotEmpty(result.MouthFaceTrackerTimeline);
    }

    [Fact]
    public void AnalyzerMergesRotationBoundaryInChronologicalOrder()
    {
        var result = new HardwareFlightRecorderAnalyzer().Analyze(new([Fixture("rotation-active.jsonl.1"), Fixture("rotation-active.jsonl")], null, null));
        Assert.Equal(1, result.Sessions.Single().LastDurableEvent!.Sequence);
    }

    [Fact]
    public void EventCreationAndQueuedWritesRemainBoundedInSyntheticBenchmark()
    {
        using var writer = new CaptureWriter();
        var started = System.Diagnostics.Stopwatch.StartNew();
        using (var engine = new HardwareFlightRecorderEngine(writer))
            for (var index = 0; index < 2000; index++) engine.Enqueue(Record("heartbeat"), false);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(writer.Lines.Count <= 2000);
    }

    private static HardwareFlightRecord Record(string stage) => new()
    {
        TimestampUtc = DateTimeOffset.UtcNow, ProcessSessionId = "test", ProcessName = "Supervisor",
        BootIdentity = "boot", Stage = stage, OperationCategory = "test", Routine = "test"
    };
    private static ActiveHardwareOperation Active(string id, string? parent) => new(id, "test", "routine", DateTimeOffset.UtcNow, 1, 1, ["usbPnp"], parent, "operationStarted");
    private static BootIdentity Boot(string fingerprint) => new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 1, 1, fingerprint, "test");
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "FlightRecorder", name);
    private static string TempDirectory() { var path = Path.Combine(Path.GetTempPath(), "flight-recorder-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }

    private sealed class CaptureWriter : IHardwareFlightWriter
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ConcurrentQueue<bool> DurableWrites { get; } = new();
        public int FlushCalls;
        public void Write(string jsonLine, bool durable) { Lines.Enqueue(jsonLine); DurableWrites.Enqueue(durable); }
        public void Flush(bool durable) => Interlocked.Increment(ref FlushCalls);
        public void Dispose() { }
    }
    private sealed class BlockingWriter : IHardwareFlightWriter
    {
        public ManualResetEventSlim Started { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public void Write(string jsonLine, bool durable) { Started.Set(); Release.Wait(TimeSpan.FromSeconds(5)); }
        public void Flush(bool durable) { }
        public void Dispose() { Release.Set(); }
    }
    private sealed class FailingWriter : IHardwareFlightWriter
    {
        public int Attempts;
        public void Write(string jsonLine, bool durable) { Interlocked.Increment(ref Attempts); throw new IOException("synthetic"); }
        public void Flush(bool durable) { }
        public void Dispose() { }
    }
    private sealed class FakeWindowsEventSource(bool alwaysAvailable = false) : IWindowsEventSource
    {
        private int _calls;
        public (IReadOnlyList<CorrelatedWindowsEvent> Events, WindowsEventChannelResult Result) Read(string channel, DateTimeOffset startUtc, DateTimeOffset endUtc, int maximumRecords)
        {
            var call = Interlocked.Increment(ref _calls);
            if (!alwaysAvailable && call == 1) return ([], new(channel, "accessDenied", 0, "synthetic"));
            if (!alwaysAvailable && call == 2) return ([], new(channel, "unavailable", 0, "synthetic"));
            var item = new CorrelatedWindowsEvent(channel, "BTHUSB", 17, 4, startUtc.AddSeconds(1), "bluetooth", "synthetic");
            return ([item], new(channel, "available", 1, null));
        }
    }
}
