using System.Text.Json;
using Xunit;

public sealed class PimaxConnectLifecycleObservationTests
{
    [Fact]
    public void RequestParsingBoundsIntervalsAndSupportsPathsWithSpaces()
    {
        var request = PimaxConnectLifecycleObservationRequest.Parse([
            "pimax-connect-lifecycle-observe-json", "--scenario", "connect no reseat",
            "--duration-seconds=9999", "--sample-interval-ms", "1",
            "--assessment-interval-ms", "250", "--output-dir", @"C:\evidence path", "--marker-file", @"C:\marker path\markers.jsonl"]);

        Assert.Equal("connect no reseat", request.Scenario);
        Assert.Equal(600, request.DurationSeconds);
        Assert.Equal(250, request.SampleIntervalMilliseconds);
        Assert.Equal(1000, request.AssessmentIntervalMilliseconds);
        Assert.Equal(@"C:\evidence path", request.OutputDirectory);
        Assert.Equal(@"C:\marker path\markers.jsonl", request.MarkerFile);
    }

    [Fact]
    public void ServiceTrackerRecordsTransientAndRepeatedLauncherInvocations()
    {
        var previous = new Dictionary<string, PimaxServiceSnapshot>(StringComparer.OrdinalIgnoreCase);
        var timeline = new List<PimaxServiceTransition>();
        var stopped = Service("STOPPED", null);
        var first = Service("RUNNING", 17116);
        var second = Service("RUNNING", 48984);

        Track(stopped); Track(first); Track(stopped); Track(second); Track(stopped);

        Assert.Equal(5, timeline.Count);
        Assert.Equal([null, 17116, null, 48984, null], timeline.Select(item => item.Current.ProcessId));
        Assert.All(timeline.Skip(1), item => Assert.Equal("stateOrPidChanged", item.TransitionType));
        return;

        void Track(PimaxServiceSnapshot snapshot)
            => PimaxConnectLifecycleObserver.TrackServices([snapshot], previous, timeline, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(timeline.Count * 500));
    }

    [Fact]
    public void ProcessTrackerRecordsParentChildStartsAndPersistentChildAfterParentExit()
    {
        var previous = new Dictionary<(int, long), PimaxProcessSnapshot>();
        var timeline = new List<PimaxProcessTransition>();
        var parent = ProcessSnapshot(10, null, "PiServiceLauncher", 100);
        var child = ProcessSnapshot(11, 10, "PiService", 101);

        PimaxConnectLifecycleObserver.TrackProcesses([parent], previous, timeline, DateTimeOffset.UtcNow, TimeSpan.Zero);
        PimaxConnectLifecycleObserver.TrackProcesses([parent, child], previous, timeline, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(500));
        PimaxConnectLifecycleObserver.TrackProcesses([child], previous, timeline, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));

        Assert.Contains(timeline, item => item.TransitionType == "started" && item.Current.ProcessId == 11 && item.Current.ParentProcessId == 10);
        Assert.Contains(timeline, item => item.TransitionType == "exited" && item.Current.ProcessId == 10);
        Assert.DoesNotContain(timeline.Skip(2), item => item.TransitionType == "exited" && item.Current.ProcessId == 11);
    }

    [Fact]
    public void ProcessTrackerProtectsAgainstPidReuse()
    {
        var previous = new Dictionary<(int, long), PimaxProcessSnapshot>();
        var timeline = new List<PimaxProcessTransition>();
        PimaxConnectLifecycleObserver.TrackProcesses([ProcessSnapshot(22, null, "PiService", 100)], previous, timeline, DateTimeOffset.UtcNow, TimeSpan.Zero);
        PimaxConnectLifecycleObserver.TrackProcesses([ProcessSnapshot(22, null, "unrelated", 200)], previous, timeline, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));

        Assert.Contains(timeline, item => item.TransitionType == "exited" && item.Current.Name == "PiService");
        Assert.Contains(timeline, item => item.TransitionType == "started" && item.Current.Name == "unrelated");
    }

    [Fact]
    public void MarkerReaderPreservesOrderingAndUnicode()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "markers.jsonl");
        File.WriteAllLines(path,
        [
            "{\"label\":\"connect-pressed\",\"note\":\"Connect ✓\",\"source\":\"user-confirmed\"}",
            "{\"label\":\"usb-reseat-completed\",\"source\":\"user-confirmed\"}"
        ]);
        var warnings = new List<string>();
        var markers = new PimaxObservationMarkerReader(path).ReadNew(TimeSpan.FromSeconds(2), DateTimeOffset.Now, warnings).ToArray();

        Assert.Equal(["connect-pressed", "usb-reseat-completed"], markers.Select(marker => marker.Label));
        Assert.Contains("✓", markers[0].Note);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ResultSerializesAsExactlyOneDocumentWithSchemaAndPartialFailures()
    {
        var result = EmptyResult(warnings: ["service metadata unavailable"], errors: ["one probe failed"]);
        var json = JsonSerializer.Serialize(result, PimaxConnectLifecycleObservationJson.Options);
        using var parsed = JsonDocument.Parse(json);

        Assert.Equal(PimaxConnectLifecycleObservationSchema.Version, parsed.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        Assert.Equal(1, parsed.RootElement.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public void ExistingDiagnosticSchemasRemainUnchanged()
    {
        Assert.Equal("pimax-connectivity-v1", PimaxConnectivitySchema.Version);
        Assert.Equal("pimax-usb-enumeration-v1", PimaxUsbEnumerationSchema.Version);
        Assert.Equal("pimax-registration-assessment-v1", PimaxRegistrationAssessmentSchema.Version);
        Assert.Equal("pimax-recovery-experiment-v1", PimaxRecoveryExperimentSchema.Version);
        Assert.Equal("pimax-connect-lifecycle-observation-v1", PimaxConnectLifecycleObservationSchema.Version);
    }

    [Fact]
    public void ObserverSourceContainsNoMutationOrUiAutomationCalls()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxConnectLifecycleObservation.cs"));
        string[] forbidden = [".Kill(", "CloseMainWindow(", "StartService(", "StopService(", "ControlService(", "pnputil", "devcon", "SendKeys", "mouse_event", "keybd_event"];
        foreach (var token in forbidden) Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PimaxVrcSupervisor.Tui", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BaseStation", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutputModelIsBoundedByTransitionAndAssessmentArrays()
    {
        var result = EmptyResult();
        Assert.Empty(result.ServiceTimeline);
        Assert.Empty(result.ProcessTimeline);
        Assert.Empty(result.AssessmentTimeline);
        Assert.Equal("pendingCrossScenarioAnalysis", result.Classification.Status);
    }

    [Theory]
    [InlineData(true, false, false, false, false, false, "D -")]
    [InlineData(false, true, true, false, false, false, "B -")]
    [InlineData(false, true, false, true, false, false, "C -")]
    [InlineData(false, true, false, false, true, true, "A -")]
    [InlineData(false, true, false, false, false, false, "E -")]
    [InlineData(false, false, false, false, false, false, "F -")]
    public void CorrelationClassificationUsesSynchronizedOutcomeEvidence(
        bool connectOnlyReady,
        bool combinedReady,
        bool failedLauncherCrash,
        bool combinedPersistentTransition,
        bool freshUsbArrival,
        bool runtimeAfterUsb,
        string expectedPrefix)
    {
        var connectOnly = new PimaxScenarioEvidence(connectOnlyReady, true, failedLauncherCrash, false, false, false);
        var combined = new PimaxScenarioEvidence(combinedReady, true, false, combinedPersistentTransition, freshUsbArrival, runtimeAfterUsb);

        var result = PimaxConnectLifecycleCorrelation.Classify(connectOnly, combined);

        Assert.StartsWith(expectedPrefix, result.PrimaryClassification);
    }

    private static PimaxServiceSnapshot Service(string state, int? pid)
        => new("PiServiceLauncher", "PiServiceLauncher", state, pid, "auto", "0x10", @"C:\Program Files\Pimax\Runtime\PiServiceLauncher.exe", "hash", "Pimax", "Pimax", "launcher", [], []);

    private static PimaxProcessSnapshot ProcessSnapshot(int pid, int? parent, string name, long startTicks)
        => new(pid, parent, name, @"C:\Program Files\Pimax\Runtime\" + name + ".exe", "hash", "Pimax", "Pimax", name, "1", "CN=Pimax", "signaturePresent", new DateTimeOffset(startTicks, TimeSpan.Zero), null, 1, false, null, []);

    private static PimaxConnectLifecycleObservationResult EmptyResult(string[]? warnings = null, string[]? errors = null)
        => new(PimaxConnectLifecycleObservationSchema.Version, "id", "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 500, 2000, "commit", "binary", "hash", "windows", "dotnet", null, [], [], [], [], [], [], [], new PimaxObservationClassification("pendingCrossScenarioAnalysis", null, [], [], "unclassified", []), warnings ?? [], errors ?? [], false);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !HasGitMetadata(directory.FullName)) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static bool HasGitMetadata(string directory)
    {
        var path = Path.Combine(directory, ".git");
        return Directory.Exists(path) || File.Exists(path);
    }
}
