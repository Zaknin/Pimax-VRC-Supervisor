using System.Text.Json;
using Xunit;

public sealed class LifecycleObservabilityTests
{
    [Fact]
    public void CleanupAdmissionEventsCaptureBothAdmittedAndRejectedOutcomes()
    {
        using var temp = new TempDirectory();
        var sink = new LifecycleEventSink(temp.Path, "Supervisor", Guid.Parse("11111111-1111-1111-1111-111111111111"));

        sink.Write("cleanup.admission", "cleanup-lock-acquired", "admitted", CleanupFields());
        sink.Write("cleanup.admission", "cleanup-lock-busy", "rejected", CleanupFields());

        var events = ReadEvents(sink.ActivePath);
        Assert.Collection(
            events,
            admitted =>
            {
                Assert.Equal("cleanup.admission", admitted.GetProperty("eventName").GetString());
                Assert.Equal("admitted", admitted.GetProperty("result").GetString());
                Assert.Equal("cleanup-lock-acquired", admitted.GetProperty("reason").GetString());
            },
            rejected =>
            {
                Assert.Equal("rejected", rejected.GetProperty("result").GetString());
                Assert.Equal("cleanup-lock-busy", rejected.GetProperty("reason").GetString());
            });
    }

    [Fact]
    public void OriginalAdmissionForNewMutexSucceedsAndRecordsSuccess()
    {
        using var temp = new TempDirectory();
        var sink = new LifecycleEventSink(temp.Path, "Supervisor");
        var name = @"Local\PimaxVrcSupervisorTests-" + Guid.NewGuid().ToString("N");

        using var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        LifecycleOwnershipObservation.RecordAfterOriginalAcquisition(sink, name, createdNew);

        Assert.True(createdNew);
        var eventDocument = Assert.Single(ReadEvents(sink.ActivePath));
        Assert.Equal("lifecycle.ownerLockAcquisition", eventDocument.GetProperty("eventName").GetString());
        Assert.Equal("admitted", eventDocument.GetProperty("result").GetString());
        Assert.Equal("True", eventDocument.GetProperty("fields").GetProperty("createdNew").GetString());
    }

    [Fact]
    public void OriginalAdmissionForExistingOwnedMutexRemainsRejectedWithoutWaitOrRetry()
    {
        using var temp = new TempDirectory();
        var sink = new LifecycleEventSink(temp.Path, "Supervisor");
        var name = @"Local\PimaxVrcSupervisorTests-" + Guid.NewGuid().ToString("N");

        using var owner = new Mutex(initiallyOwned: true, name, out var ownerCreatedNew);
        using var candidate = new Mutex(initiallyOwned: true, name, out var candidateCreatedNew);
        LifecycleOwnershipObservation.RecordAfterOriginalAcquisition(sink, name, candidateCreatedNew);

        Assert.True(ownerCreatedNew);
        Assert.False(candidateCreatedNew);
        var eventDocument = Assert.Single(ReadEvents(sink.ActivePath));
        Assert.Equal("rejected-existing-mutex", eventDocument.GetProperty("result").GetString());
    }

    [Fact]
    public void OriginalAdmissionForExistingUnownedKernelObjectRemainsRejected()
    {
        using var temp = new TempDirectory();
        var sink = new LifecycleEventSink(temp.Path, "Supervisor");
        var name = @"Local\PimaxVrcSupervisorTests-" + Guid.NewGuid().ToString("N");

        using var seed = new Mutex(initiallyOwned: false, name, out var seedCreatedNew);
        using var candidate = new Mutex(initiallyOwned: true, name, out var candidateCreatedNew);
        LifecycleOwnershipObservation.RecordAfterOriginalAcquisition(sink, name, candidateCreatedNew);

        Assert.True(seedCreatedNew);
        Assert.False(candidateCreatedNew);
        Assert.Equal("rejected-existing-mutex", Assert.Single(ReadEvents(sink.ActivePath)).GetProperty("result").GetString());
    }

    [Fact]
    public void OriginalAdmissionCanCreateAgainAfterEveryHandleCloses()
    {
        var name = @"Local\PimaxVrcSupervisorTests-" + Guid.NewGuid().ToString("N");
        using (var original = new Mutex(initiallyOwned: true, name, out var originalCreatedNew))
        {
            Assert.True(originalCreatedNew);
        }

        using var replacement = new Mutex(initiallyOwned: true, name, out var replacementCreatedNew);
        Assert.True(replacementCreatedNew);
    }

    [Fact]
    public void OwnershipInstrumentationFailureCannotChangeOriginalAdmissionResult()
    {
        using var temp = new TempDirectory();
        var sink = new LifecycleEventSink(temp.Path, "Supervisor", append: (_, _) => throw new IOException("test sink failure"));
        var name = @"Local\PimaxVrcSupervisorTests-" + Guid.NewGuid().ToString("N");

        using var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        LifecycleOwnershipObservation.RecordAfterOriginalAcquisition(sink, name, createdNew);

        Assert.True(createdNew);
    }

    [Fact]
    public void OwnershipInstrumentationHonestlyMarksAbandonedStateUnavailable()
    {
        using var temp = new TempDirectory();
        var sink = new LifecycleEventSink(temp.Path, "Supervisor");

        LifecycleOwnershipObservation.RecordAfterOriginalAcquisition(sink, "test-lock", createdNew: true);

        var fields = Assert.Single(ReadEvents(sink.ActivePath)).GetProperty("fields");
        Assert.Equal(LifecycleOwnershipObservation.AbandonedStateUnavailable, fields.GetProperty("abandonedState").GetString());
    }

    [Fact]
    public void ProductionOwnershipAdmissionUsesOriginalConstructorBeforePassiveObservation()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "Program.cs"));
        var supervisorStart = source.IndexOf("using var supervisorMutex = new Mutex", StringComparison.Ordinal);
        var supervisorEnd = source.IndexOf("DirectLaunchMigrationResult? migrationResultForStartup", supervisorStart, StringComparison.Ordinal);
        var watcherStart = source.IndexOf("using var mutex = new Mutex", StringComparison.Ordinal);
        var watcherEnd = source.IndexOf("var supervisorPath = ScheduledTaskInstaller.GetSupervisorExecutablePath", watcherStart, StringComparison.Ordinal);
        var supervisorAdmission = source[supervisorStart..supervisorEnd];
        var watcherAdmission = source[watcherStart..watcherEnd];

        Assert.Contains("new Mutex(initiallyOwned: true", supervisorAdmission, StringComparison.Ordinal);
        Assert.Contains("new Mutex(initiallyOwned: true", watcherAdmission, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(supervisorAdmission, "new Mutex(initiallyOwned: true"));
        Assert.Equal(1, CountOccurrences(watcherAdmission, "new Mutex(initiallyOwned: true"));
        Assert.DoesNotContain("WaitOne", supervisorAdmission, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitOne", watcherAdmission, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseMutex", supervisorAdmission, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseMutex", watcherAdmission, StringComparison.Ordinal);
        Assert.DoesNotContain("AbandonedMutexException", supervisorAdmission, StringComparison.Ordinal);
        Assert.DoesNotContain("AbandonedMutexException", watcherAdmission, StringComparison.Ordinal);
        Assert.True(supervisorAdmission.IndexOf("LifecycleOwnershipObservation.RecordAfterOriginalAcquisition", StringComparison.Ordinal)
            > supervisorAdmission.IndexOf("new Mutex(initiallyOwned: true", StringComparison.Ordinal));
        Assert.True(watcherAdmission.IndexOf("LifecycleOwnershipObservation.RecordAfterOriginalAcquisition", StringComparison.Ordinal)
            > watcherAdmission.IndexOf("new Mutex(initiallyOwned: true", StringComparison.Ordinal));
    }

    [Fact]
    public void WatcherWaitToLaunchAndDuplicateSuppressionRemainDecisionOnly()
    {
        var oldSession = new SteamVrSessionIdentity(100, DateTimeOffset.Parse("2026-07-24T01:00:00Z"));
        var replacement = new SteamVrSessionIdentity(200, DateTimeOffset.Parse("2026-07-24T01:00:01Z"));

        var wait = AutoLaunchWatcher.GetLaunchDecision(replacement, supervisorRunning: true, oldSession);
        var launch = AutoLaunchWatcher.GetLaunchDecision(replacement, supervisorRunning: false, oldSession);
        var suppress = AutoLaunchWatcher.GetLaunchDecision(replacement, supervisorRunning: false, replacement);

        Assert.False(wait.ShouldLaunchSupervisor);
        Assert.True(launch.ShouldLaunchSupervisor);
        Assert.True(suppress.SuppressedForCurrentSession);
    }

    [Fact]
    public void ChildLaunchCorrelationUsesAStableExplicitId()
    {
        var expected = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var context = StartupExecutionContext.Parse(["--lifecycle-correlation", expected.ToString("D")]);

        Assert.Equal(expected, context.LifecycleCorrelationId);
    }

    [Fact]
    public void BaseStationOffAndLaterWakeCanShareOneCorrelationAndIdentifyDelayedWake()
    {
        using var temp = new TempDirectory();
        var correlation = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var sink = new LifecycleEventSink(temp.Path, "Supervisor", correlation);

        sink.Write("baseStation.powerDown", "cleanup", "completed", new Dictionary<string, string?> { ["targetStationCount"] = "2" });
        sink.Write("baseStation.powerOn", "session-startup", "Ran", new Dictionary<string, string?> { ["delayedStartupWakePass"] = "true", ["targetStationCount"] = "2" });

        var events = ReadEvents(sink.ActivePath);
        Assert.All(events, item => Assert.Equal(correlation.ToString("N"), item.GetProperty("lifecycleCorrelationId").GetString()));
        Assert.Equal("true", events[1].GetProperty("fields").GetProperty("delayedStartupWakePass").GetString());
    }

    [Fact]
    public void EvidenceReaderReportsBaselineStalenessAndRotationWithoutRawLogContent()
    {
        using var temp = new TempDirectory();
        var log = temp.WriteFile("vrmonitor.txt", SteamVrLifecycleEvidenceReader.ShutdownRequestedMarker + " raw secret token=should-not-appear");
        var observations = new List<SteamVrEvidenceReaderObservation>();
        var reader = new SteamVrLifecycleEvidenceReader([log], observations.Add);

        reader.EstablishBaseline();
        File.AppendAllText(log, Environment.NewLine + SteamVrLifecycleEvidenceReader.RestartStateMarker);
        _ = reader.ReadCurrentSessionEvidence();
        File.WriteAllText(log, SteamVrLifecycleEvidenceReader.RestartSystemMarker);
        _ = reader.ReadCurrentSessionEvidence();
        File.Move(log, log + ".rotated");
        File.WriteAllText(log, new string('x', 512) + SteamVrLifecycleEvidenceReader.RestartHmdMarker);
        _ = reader.ReadCurrentSessionEvidence();

        Assert.True(observations[0].StaleMarkersCleared);
        Assert.Contains(observations, observation => observation.Marker == SteamVrLifecycleEvidenceReader.RestartStateMarker);
        Assert.Contains(observations, observation => observation.TruncationDetected);
        Assert.Contains(observations, observation => observation.RotationDetected);
        Assert.DoesNotContain(observations.SelectMany(observation => observation.SourceIdentities), value => value.Contains("raw", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvidenceReaderReportsRotationWithoutChangingPreExistingOffsetSemantics()
    {
        using var temp = new TempDirectory();
        const int sourceLength = 512;
        var log = temp.WriteFile("vrmonitor.txt", new string('a', sourceLength));
        var observations = new List<SteamVrEvidenceReaderObservation>();
        var reader = new SteamVrLifecycleEvidenceReader([log], observations.Add);

        reader.EstablishBaseline();
        File.Move(log, log + ".rotated");
        File.WriteAllText(
            log,
            SteamVrLifecycleEvidenceReader.RestartStateMarker
            + new string('b', sourceLength - SteamVrLifecycleEvidenceReader.RestartStateMarker.Length));

        var evidence = reader.ReadCurrentSessionEvidence();

        Assert.False(evidence.RestartRequested);
        Assert.Contains(observations, observation => observation.RotationDetected);
    }

    [Fact]
    public void ConcurrentSupervisorAndWatcherWritersProduceCompleteJsonLines()
    {
        using var temp = new TempDirectory();
        var supervisor = new LifecycleEventSink(temp.Path, "Supervisor");
        var watcher = new LifecycleEventSink(temp.Path, "Watcher");

        Parallel.For(0, 80, index =>
        {
            (index % 2 == 0 ? supervisor : watcher).Write("watcher.decision", result: "observed", fields: new Dictionary<string, string?> { ["index"] = index.ToString() });
        });

        var lines = File.ReadAllLines(supervisor.ActivePath);
        Assert.Equal(80, lines.Length);
        Assert.All(lines, line => Assert.Equal(LifecycleEventSchema.Version, JsonDocument.Parse(line).RootElement.GetProperty("schemaVersion").GetString()));
    }

    [Fact]
    public void RetentionIsBoundedAndSinkFailureDoesNotChangeDecisionResult()
    {
        using var temp = new TempDirectory();
        var sink = new LifecycleEventSink(temp.Path, "Supervisor");
        File.WriteAllText(sink.ActivePath, new string('x', (int)LifecycleEventSink.MaxActiveBytes + 1));
        sink.Write("lifecycle.health", result: "ok");

        Assert.True(File.Exists(sink.ActivePath + ".1"));
        Assert.False(File.Exists(sink.ActivePath + ".4"));

        var failingSink = new LifecycleEventSink(temp.Path, "Supervisor", append: (_, _) => throw new IOException("test sink failure"));
        var session = new SteamVrSessionIdentity(8, DateTimeOffset.UtcNow);
        var before = AutoLaunchWatcher.GetLaunchDecision(session, supervisorRunning: false, launchedForSteamVrSession: null);
        failingSink.Write("watcher.decision", result: "launch");
        var after = AutoLaunchWatcher.GetLaunchDecision(session, supervisorRunning: false, launchedForSteamVrSession: null);

        Assert.Equal(before, after);
    }

    [Fact]
    public void EventSinkDropsSecretFieldsAndSanitizesSecretAssignments()
    {
        using var temp = new TempDirectory();
        var sink = new LifecycleEventSink(temp.Path, "Supervisor");

        sink.Write(
            "steamVr.evidenceHealth",
            reason: "raw=never",
            result: "apiKey=abc123",
            fields: new Dictionary<string, string?>
            {
                ["token"] = "never-write-this",
                ["markerVisible"] = SteamVrLifecycleEvidenceReader.RestartStateMarker,
                ["message"] = "password=also-never"
            });

        var raw = File.ReadAllText(sink.ActivePath);
        Assert.DoesNotContain("never-write-this", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("also-never", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"token\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", raw, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> CleanupFields()
        => new()
        {
            ["currentSupervisorIdentity"] = "123@2026-07-24T00:00:00.0000000+00:00",
            ["observedVrserverIdentity"] = "456@2026-07-24T00:00:00.0000000+00:00",
            ["ownerLockState"] = "held-by-current-supervisor",
            ["managedApplicationsWillClose"] = "true"
        };

    private static JsonElement[] ReadEvents(string path)
        => File.ReadAllLines(path)
            .Select(line => JsonDocument.Parse(line))
            .Select(document => document.RootElement.Clone())
            .ToArray();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitMetadata = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitMetadata) || File.Exists(gitMetadata))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }
}
