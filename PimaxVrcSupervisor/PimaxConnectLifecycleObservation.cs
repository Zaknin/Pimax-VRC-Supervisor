using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

internal static class PimaxConnectLifecycleObservationSchema
{
    public const string Version = "pimax-connect-lifecycle-observation-v1";
}

internal static class PimaxConnectLifecycleObservationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal sealed record PimaxConnectLifecycleObservationRequest(
    string Scenario,
    int DurationSeconds,
    int SampleIntervalMilliseconds,
    int AssessmentIntervalMilliseconds,
    string? OutputDirectory,
    string? MarkerFile)
{
    public static PimaxConnectLifecycleObservationRequest Parse(string[] args)
        => new(
            Option(args, "--scenario") ?? "unspecified",
            BoundedInt(Option(args, "--duration-seconds"), 30, 1, 600),
            BoundedInt(Option(args, "--sample-interval-ms"), 500, 250, 5000),
            BoundedInt(Option(args, "--assessment-interval-ms"), 2000, 1000, 10000),
            Option(args, "--output-dir"),
            Option(args, "--marker-file"));

    private static int BoundedInt(string? value, int fallback, int minimum, int maximum)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static string? Option(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[index][prefix.Length..];
            }

            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
                && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

internal sealed record PimaxConnectLifecycleObservationResult(
    string SchemaVersion,
    string SessionId,
    string Scenario,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationMs,
    int SampleIntervalMilliseconds,
    int AssessmentIntervalMilliseconds,
    string? SourceCommit,
    string BinaryPath,
    string BinarySha256,
    string WindowsVersion,
    string DotNetVersion,
    string? OutputDirectory,
    PimaxObservationMarker[] Markers,
    PimaxServiceTransition[] ServiceTimeline,
    PimaxProcessTransition[] ProcessTimeline,
    PimaxAssessmentSample[] AssessmentTimeline,
    PimaxCheckpointReference[] Checkpoints,
    PimaxWindowsEventReference[] WindowsEvents,
    PimaxLogTailReference[] PimaxLogTails,
    PimaxObservationClassification Classification,
    string[] Warnings,
    string[] Errors,
    bool Cancelled);

internal sealed record PimaxObservationMarker(
    DateTimeOffset UtcTimestamp,
    DateTimeOffset LocalTimestamp,
    double ElapsedMs,
    string Label,
    string? Note,
    string Source);

internal sealed record PimaxServiceSnapshot(string Name, string? DisplayName, string? State, int? ProcessId, string? StartMode, string? ServiceType, string? ExecutablePath, string? Sha256, string? Company, string? Product, string? FileDescription, string[] Dependencies, string[] Dependents);
internal sealed record PimaxServiceTransition(double ElapsedMs, DateTimeOffset ObservedAt, string TransitionType, PimaxServiceSnapshot Current, PimaxServiceSnapshot? Previous);
internal sealed record PimaxProcessSnapshot(int ProcessId, int? ParentProcessId, string Name, string? ExecutablePath, string? Sha256, string? Company, string? Product, string? FileDescription, string? Version, string? SignerSubject, string? SignatureStatus, DateTimeOffset? StartTime, string? CommandLine, int? SessionId, bool? HasVisibleWindow, string? AssociatedServiceName, int[] ChildProcessIds);
internal sealed record PimaxProcessTransition(double ElapsedMs, DateTimeOffset ObservedAt, string TransitionType, PimaxProcessSnapshot Current, PimaxProcessSnapshot? Previous);
internal sealed record PimaxAssessmentSample(double ElapsedMs, DateTimeOffset CollectedAt, string State, string Confidence, string Explanation, string FilteredConnectivity, int WarningCount, int ErrorCount);
internal sealed record PimaxCheckpointReference(double ElapsedMs, DateTimeOffset CollectedAt, string Reason, string UsbSnapshotFile, string ConnectivitySnapshotFile, string UsbSha256, string ConnectivitySha256);
internal sealed record PimaxWindowsEventReference(DateTimeOffset Timestamp, string LogName, string Provider, int EventId, string Level, string Message);
internal sealed record PimaxLogTailReference(string Source, string Path, string Sha256, DateTimeOffset LastWriteTime, long Length, string[] RelevantTailLines);
internal sealed record PimaxObservationClassification(string Status, string? PrimaryClassification, string[] SupportingEvidence, string[] ContraryEvidence, string Confidence, string[] Limitations);
internal sealed record PimaxScenarioEvidence(bool RegisteredReady, bool LauncherStarted, bool LauncherAbnormalExit, bool PersistentRuntimeTransition, bool FreshUsbArrivalObserved, bool RuntimeDevicesAppearedAfterUsb);

internal static class PimaxConnectLifecycleCorrelation
{
    public static PimaxObservationClassification Classify(PimaxScenarioEvidence connectOnly, PimaxScenarioEvidence connectWithUsb)
    {
        if (connectOnly.RegisteredReady)
        {
            return Classification("D", "Connect workflow alone can recover", "confirmed", ["Connect-only scenario reached registered-ready."], []);
        }

        if (!connectWithUsb.RegisteredReady)
        {
            return Classification("F", "Inconclusive", "insufficient", [], ["Neither controlled Connect scenario reached registered-ready."]);
        }

        if (connectOnly.LauncherAbnormalExit && !connectWithUsb.LauncherAbnormalExit)
        {
            return Classification("B", "PiServiceLauncher crash correlates with failed registration", "probable", ["Launcher termination mode differed with registration outcome."], []);
        }

        if (!connectOnly.PersistentRuntimeTransition && connectWithUsb.PersistentRuntimeTransition)
        {
            return Classification("C", "Persistent runtime process transition correlates with success", "probable", ["A persistent runtime transition occurred only in the successful scenario."], []);
        }

        if (connectWithUsb.FreshUsbArrivalObserved && connectWithUsb.RuntimeDevicesAppearedAfterUsb)
        {
            return Classification("A", "Fresh USB arrival is the decisive missing trigger", "probable", ["Connect-only remained unregistered.", "Fresh USB arrival preceded runtime-device appearance and registered-ready."], []);
        }

        return Classification("E", "Combined Connect plus USB arrival required, exact owner unresolved", "probable", ["Only the combined scenario reached registered-ready."], ["The synchronized evidence did not isolate one process or service owner."]);
    }

    private static PimaxObservationClassification Classification(string code, string label, string confidence, string[] supporting, string[] contrary)
        => new("classified", $"{code} - {label}", supporting, contrary, confidence, []);
}

internal interface IPimaxLifecycleObservationProbe
{
    Task<PimaxServiceSnapshot[]> CaptureServicesAsync(CancellationToken cancellationToken);
    PimaxProcessSnapshot[] CaptureProcesses();
    Task<PimaxConnectivitySnapshot> CaptureConnectivityAsync(CancellationToken cancellationToken);
    PimaxUsbEnumerationSnapshot CaptureUsb();
    Task<PimaxWindowsEventReference[]> CaptureEventsAsync(DateTimeOffset startedAt, DateTimeOffset endedAt, CancellationToken cancellationToken);
    PimaxLogTailReference[] CaptureLogTails();
}

internal sealed class PimaxConnectLifecycleObserver
{
    private const int MaxTransitions = 4000;
    private const int MaxAssessments = 600;
    private readonly IPimaxLifecycleObservationProbe _probe;
    private readonly PimaxRegistrationStateAssessor _assessor = new();
    private readonly Func<DateTimeOffset> _now;

    public PimaxConnectLifecycleObserver(SupervisorConfig config)
        : this(new WindowsPimaxLifecycleObservationProbe(config), () => DateTimeOffset.Now)
    {
    }

    internal PimaxConnectLifecycleObserver(IPimaxLifecycleObservationProbe probe, Func<DateTimeOffset>? now = null)
    {
        _probe = probe;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<PimaxConnectLifecycleObservationResult> ObserveAsync(PimaxConnectLifecycleObservationRequest request, CancellationToken cancellationToken)
    {
        var sessionId = $"pimax-connect-observation-{Guid.NewGuid():N}";
        var startedAt = _now();
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();
        var errors = new List<string>();
        var markers = new List<PimaxObservationMarker>();
        var serviceTransitions = new List<PimaxServiceTransition>();
        var processTransitions = new List<PimaxProcessTransition>();
        var assessments = new List<PimaxAssessmentSample>();
        var checkpoints = new List<PimaxCheckpointReference>();
        var previousServices = new Dictionary<string, PimaxServiceSnapshot>(StringComparer.OrdinalIgnoreCase);
        var previousProcesses = new Dictionary<(int Pid, long StartTicks), PimaxProcessSnapshot>();
        var markerReader = new PimaxObservationMarkerReader(request.MarkerFile);
        var outputDirectory = PrepareOutputDirectory(request.OutputDirectory, warnings);
        var cachedUsb = SafeCaptureUsb(errors);
        var nextAssessmentAt = TimeSpan.Zero;
        var cancelled = false;

        markers.Add(Marker(startedAt, stopwatch.Elapsed, "baseline-start", request.Scenario, "observer-generated"));
        await CaptureCheckpointAsync("scenario-start", cachedUsb, checkpoints, outputDirectory, stopwatch.Elapsed, errors, cancellationToken);

        using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        duration.CancelAfter(TimeSpan.FromSeconds(request.DurationSeconds));
        while (!duration.IsCancellationRequested)
        {
            var observedAt = _now();
            foreach (var marker in markerReader.ReadNew(stopwatch.Elapsed, observedAt, warnings))
            {
                markers.Add(marker);
                if (IsCheckpointMarker(marker.Label))
                {
                    cachedUsb = SafeCaptureUsb(errors);
                    await CaptureCheckpointAsync(marker.Label, cachedUsb, checkpoints, outputDirectory, stopwatch.Elapsed, errors, duration.Token);
                }
            }

            try
            {
                TrackServices(await _probe.CaptureServicesAsync(duration.Token), previousServices, serviceTransitions, observedAt, stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
            catch (Exception ex) { warnings.Add($"Service sample failed: {ex.Message}"); }

            try
            {
                TrackProcesses(_probe.CaptureProcesses(), previousProcesses, processTransitions, observedAt, stopwatch.Elapsed);
            }
            catch (Exception ex) { warnings.Add($"Process sample failed: {ex.Message}"); }

            if (stopwatch.Elapsed >= nextAssessmentAt && assessments.Count < MaxAssessments)
            {
                try
                {
                    var connectivity = await _probe.CaptureConnectivityAsync(duration.Token);
                    var assessment = _assessor.Evaluate(connectivity, cachedUsb, Math.Abs((cachedUsb.CollectedAt - connectivity.CollectedAt).TotalMilliseconds));
                    assessments.Add(new PimaxAssessmentSample(stopwatch.Elapsed.TotalMilliseconds, _now(), assessment.State, assessment.Confidence, assessment.Explanation, connectivity.Assessment.Value, assessment.Warnings.Length, connectivity.Errors.Length + cachedUsb.Errors.Length));
                }
                catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
                catch (Exception ex) { warnings.Add($"Registration sample failed: {ex.Message}"); }

                nextAssessmentAt = stopwatch.Elapsed + TimeSpan.FromMilliseconds(request.AssessmentIntervalMilliseconds);
            }

            try
            {
                await Task.Delay(request.SampleIntervalMilliseconds, duration.Token);
            }
            catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
        }

        cancelled = cancellationToken.IsCancellationRequested;
        foreach (var marker in markerReader.ReadNew(stopwatch.Elapsed, _now(), warnings))
        {
            markers.Add(marker);
        }

        cachedUsb = SafeCaptureUsb(errors);
        await CaptureCheckpointAsync("scenario-end", cachedUsb, checkpoints, outputDirectory, stopwatch.Elapsed, errors, CancellationToken.None);
        markers.Add(Marker(_now(), stopwatch.Elapsed, "scenario-ended", cancelled ? "cancelled" : "duration-complete", "observer-generated"));
        var endedAt = _now();
        PimaxWindowsEventReference[] events;
        PimaxLogTailReference[] logs;
        try { events = await _probe.CaptureEventsAsync(startedAt, endedAt, CancellationToken.None); }
        catch (Exception ex) { events = []; warnings.Add($"Windows event collection failed: {ex.Message}"); }
        try { logs = _probe.CaptureLogTails(); }
        catch (Exception ex) { logs = []; warnings.Add($"Pimax log-tail collection failed: {ex.Message}"); }

        return new PimaxConnectLifecycleObservationResult(
            PimaxConnectLifecycleObservationSchema.Version,
            sessionId,
            request.Scenario,
            startedAt,
            endedAt,
            stopwatch.Elapsed.TotalMilliseconds,
            request.SampleIntervalMilliseconds,
            request.AssessmentIntervalMilliseconds,
            ReadSourceCommit(),
            Assembly.GetExecutingAssembly().Location,
            HashFile(Assembly.GetExecutingAssembly().Location),
            Environment.OSVersion.VersionString,
            Environment.Version.ToString(),
            outputDirectory,
            markers.OrderBy(marker => marker.ElapsedMs).ToArray(),
            serviceTransitions.Take(MaxTransitions).ToArray(),
            processTransitions.Take(MaxTransitions).ToArray(),
            assessments.ToArray(),
            checkpoints.ToArray(),
            events,
            logs,
            new PimaxObservationClassification("pendingCrossScenarioAnalysis", null, [], [], "unclassified", ["A single observation session cannot establish cross-scenario causality."]),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            cancelled);
    }

    private PimaxUsbEnumerationSnapshot SafeCaptureUsb(List<string> errors)
    {
        try { return _probe.CaptureUsb(); }
        catch (Exception ex)
        {
            errors.Add($"USB/PnP checkpoint failed: {ex.Message}");
            var emptyCounts = new Dictionary<string, int>();
            return new PimaxUsbEnumerationSnapshot(
                PimaxUsbEnumerationSchema.Version,
                _now(),
                "unavailable",
                new PimaxUsbEnumerationHost(Environment.OSVersion.VersionString, RuntimeInformation.OSArchitecture.ToString(), false),
                new PimaxUsbInventorySummary(0, 0, 0, 0, emptyCounts, emptyCounts, emptyCounts, emptyCounts, emptyCounts),
                [],
                [],
                [],
                [ex.Message]);
        }
    }

    private async Task CaptureCheckpointAsync(string reason, PimaxUsbEnumerationSnapshot usb, List<PimaxCheckpointReference> checkpoints, string? outputDirectory, TimeSpan elapsed, List<string> errors, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;
        try
        {
            var connectivity = await _probe.CaptureConnectivityAsync(cancellationToken);
            var sequence = checkpoints.Count.ToString("D3", CultureInfo.InvariantCulture);
            var safeReason = string.Concat(reason.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-'));
            var usbFile = Path.Combine(outputDirectory, $"checkpoint-{sequence}-{safeReason}-usb.json");
            var connectivityFile = Path.Combine(outputDirectory, $"checkpoint-{sequence}-{safeReason}-connectivity.json");
            File.WriteAllText(usbFile, JsonSerializer.Serialize(usb, PimaxUsbEnumerationJson.Options));
            File.WriteAllText(connectivityFile, JsonSerializer.Serialize(connectivity, PimaxConnectivityJson.Options));
            checkpoints.Add(new PimaxCheckpointReference(elapsed.TotalMilliseconds, _now(), reason, Path.GetFileName(usbFile), Path.GetFileName(connectivityFile), HashFile(usbFile), HashFile(connectivityFile)));
        }
        catch (Exception ex) { errors.Add($"Checkpoint '{reason}' failed: {ex.Message}"); }
    }

    internal static void TrackServices(IEnumerable<PimaxServiceSnapshot> current, Dictionary<string, PimaxServiceSnapshot> previous, List<PimaxServiceTransition> timeline, DateTimeOffset observedAt, TimeSpan elapsed)
    {
        foreach (var item in current)
        {
            previous.TryGetValue(item.Name, out var prior);
            var type = prior is null ? "firstSeen" : prior.State != item.State || prior.ProcessId != item.ProcessId ? "stateOrPidChanged" : null;
            if (type is not null) timeline.Add(new PimaxServiceTransition(elapsed.TotalMilliseconds, observedAt, type, item, prior));
            previous[item.Name] = item;
        }
    }

    internal static void TrackProcesses(IEnumerable<PimaxProcessSnapshot> current, Dictionary<(int Pid, long StartTicks), PimaxProcessSnapshot> previous, List<PimaxProcessTransition> timeline, DateTimeOffset observedAt, TimeSpan elapsed)
    {
        var now = current.ToDictionary(item => (item.ProcessId, item.StartTime?.UtcTicks ?? 0));
        foreach (var entry in now.Where(entry => !previous.ContainsKey(entry.Key))) timeline.Add(new PimaxProcessTransition(elapsed.TotalMilliseconds, observedAt, "started", entry.Value, null));
        foreach (var entry in previous.Where(entry => !now.ContainsKey(entry.Key))) timeline.Add(new PimaxProcessTransition(elapsed.TotalMilliseconds, observedAt, "exited", entry.Value, entry.Value));
        previous.Clear();
        foreach (var entry in now) previous[entry.Key] = entry.Value;
    }

    private static bool IsCheckpointMarker(string label)
        => label is "connect-pressed" or "connect-scan-visible" or "usb-reseat-started" or "usb-reseat-completed" or "green-registered-confirmed";

    private static PimaxObservationMarker Marker(DateTimeOffset timestamp, TimeSpan elapsed, string label, string? note, string source)
        => new(timestamp.ToUniversalTime(), timestamp, elapsed.TotalMilliseconds, label, string.IsNullOrWhiteSpace(note) ? null : PimaxConnectivityRedactor.SanitizeMessage(note), source);

    private static string? PrepareOutputDirectory(string? path, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { Directory.CreateDirectory(path); return Path.GetFullPath(path); }
        catch (Exception ex) { warnings.Add($"Output directory unavailable: {ex.Message}"); return null; }
    }

    private static string HashFile(string path)
    {
        try { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        catch { return "unavailable"; }
    }

    private static string? ReadSourceCommit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (process is null || !process.WaitForExit(2000) || process.ExitCode != 0) return null;
            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch { return null; }
    }
}

internal sealed class PimaxObservationMarkerReader(string? path)
{
    private int _lineCount;

    public IEnumerable<PimaxObservationMarker> ReadNew(TimeSpan elapsed, DateTimeOffset observedAt, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) yield break;
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception ex) { warnings.Add($"Marker file read failed: {ex.Message}"); yield break; }
        foreach (var line in lines.Skip(_lineCount))
        {
            PimaxMarkerInput? marker = null;
            try { marker = JsonSerializer.Deserialize<PimaxMarkerInput>(line, PimaxConnectLifecycleObservationJson.Options); }
            catch (JsonException ex) { warnings.Add($"Invalid marker JSON was ignored: {ex.Message}"); }
            if (marker is not null && !string.IsNullOrWhiteSpace(marker.Label))
            {
                var timestamp = marker.Timestamp ?? observedAt;
                yield return new PimaxObservationMarker(timestamp.ToUniversalTime(), timestamp, elapsed.TotalMilliseconds, marker.Label.Trim(), string.IsNullOrWhiteSpace(marker.Note) ? null : PimaxConnectivityRedactor.SanitizeMessage(marker.Note), string.IsNullOrWhiteSpace(marker.Source) ? "user-confirmed" : marker.Source);
            }
        }
        _lineCount = lines.Length;
    }

    private sealed record PimaxMarkerInput(string Label, string? Note, string? Source, DateTimeOffset? Timestamp);
}

internal sealed class WindowsPimaxLifecycleObservationProbe(SupervisorConfig config) : IPimaxLifecycleObservationProbe
{
    private readonly IPimaxProcessRunner _runner = new PimaxProcessRunner();
    private readonly Dictionary<string, PimaxServiceSnapshot> _serviceMetadata = LoadServiceMetadata();
    private readonly Dictionary<string, PimaxFileMetadata> _fileMetadata = new(StringComparer.OrdinalIgnoreCase);

    public async Task<PimaxServiceSnapshot[]> CaptureServicesAsync(CancellationToken cancellationToken)
    {
        var results = new List<PimaxServiceSnapshot>();
        foreach (var name in _serviceMetadata.Keys.ToArray())
        {
            var metadata = _serviceMetadata[name];
            var query = await _runner.RunAsync("sc.exe", $"queryex \"{metadata.Name}\"", TimeSpan.FromSeconds(2), cancellationToken);
            var (state, pid) = ParseServiceQuery(query.StandardOutput);
            var current = metadata with { State = state, ProcessId = pid };
            _serviceMetadata[name] = current;
            results.Add(current);
        }
        return results.ToArray();
    }

    public PimaxProcessSnapshot[] CaptureProcesses()
    {
        var snapshots = new List<PimaxProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    var path = Safe(() => process.MainModule?.FileName);
                    if (!IsPimaxProcess(name, path)) continue;
                    var metadata = FileMetadata(path);
                    snapshots.Add(new PimaxProcessSnapshot(process.Id, ParentProcessId(process), name, PimaxConnectivityRedactor.SanitizePath(path), metadata.Sha256, metadata.Company, metadata.Product, metadata.Description, metadata.Version, metadata.SignerSubject, metadata.SignatureStatus, Safe(() => new DateTimeOffset(process.StartTime)), null, Safe(() => process.SessionId), Safe(() => process.MainWindowHandle != IntPtr.Zero), ServiceForPid(process.Id), []));
                }
                catch { }
            }
        }
        var children = snapshots
            .Where(snapshot => snapshot.ParentProcessId is not null)
            .GroupBy(snapshot => snapshot.ParentProcessId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(child => child.ProcessId).ToArray());
        return snapshots.Select(snapshot => snapshot with { ChildProcessIds = children.GetValueOrDefault(snapshot.ProcessId, []) }).OrderBy(snapshot => snapshot.ProcessId).ToArray();
    }

    public Task<PimaxConnectivitySnapshot> CaptureConnectivityAsync(CancellationToken cancellationToken)
        => new PimaxConnectivitySnapshotCollector().CollectAsync(config, cancellationToken);

    public PimaxUsbEnumerationSnapshot CaptureUsb() => new PimaxUsbEnumerationSnapshotCollector().Collect();

    public async Task<PimaxWindowsEventReference[]> CaptureEventsAsync(DateTimeOffset startedAt, DateTimeOffset endedAt, CancellationToken cancellationToken)
    {
        var start = startedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        var end = endedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        var script = "$s=[datetime]'" + start + "';$e=[datetime]'" + end + "';Get-WinEvent -FilterHashtable @{LogName=@('Application','System');StartTime=$s;EndTime=$e} -ErrorAction SilentlyContinue|Where-Object{$_.ProviderName -match 'Application Error|Windows Error Reporting|Service Control Manager|.NET Runtime|Pimax' -or $_.Message -match 'Pimax|PiService|DeviceSetting|pi_server'}|Select-Object TimeCreated,LogName,ProviderName,Id,LevelDisplayName,Message|ConvertTo-Json -Compress";
        var result = await _runner.RunAsync("powershell.exe", $"-NoProfile -Command \"{script.Replace("\"", "`\"")}\"", TimeSpan.FromSeconds(15), cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return [];
        using var document = JsonDocument.Parse(result.StandardOutput);
        var elements = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToArray() : [document.RootElement];
        return elements.Select(element => new PimaxWindowsEventReference(
            element.TryGetProperty("TimeCreated", out var time) && time.TryGetDateTimeOffset(out var parsed) ? parsed : startedAt,
            element.TryGetProperty("LogName", out var log) ? log.GetString() ?? "" : "",
            element.TryGetProperty("ProviderName", out var provider) ? provider.GetString() ?? "" : "",
            element.TryGetProperty("Id", out var id) ? id.GetInt32() : 0,
            element.TryGetProperty("LevelDisplayName", out var level) ? level.GetString() ?? "" : "",
            PimaxConnectivityRedactor.SanitizeMessage(element.TryGetProperty("Message", out var message) ? message.GetString() ?? "" : ""))).ToArray();
    }

    public PimaxLogTailReference[] CaptureLogTails()
    {
        var roots = new[]
        {
            ("PiService", Environment.ExpandEnvironmentVariables(config.PimaxServiceLogDirectory)),
            ("PimaxClient", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PimaxClient", "logs"))
        };
        var result = new List<PimaxLogTailReference>();
        foreach (var (source, root) in roots.Where(item => Directory.Exists(item.Item2)))
        {
            foreach (var file in Directory.EnumerateFiles(root).Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).Take(5))
            {
                try
                {
                    var lines = File.ReadLines(file.FullName).TakeLast(400).Where(line => line.Contains("connect", StringComparison.OrdinalIgnoreCase) || line.Contains("device", StringComparison.OrdinalIgnoreCase) || line.Contains("usb", StringComparison.OrdinalIgnoreCase) || line.Contains("service", StringComparison.OrdinalIgnoreCase)).TakeLast(80).Select(PimaxConnectivityRedactor.SanitizeMessage).Where(line => line is not null).Cast<string>().ToArray();
                    result.Add(new PimaxLogTailReference(source, PimaxConnectivityRedactor.SanitizePath(file.FullName) ?? "", Hash(file.FullName), file.LastWriteTime, file.Length, lines));
                }
                catch { }
            }
        }
        return result.ToArray();
    }

    private static Dictionary<string, PimaxServiceSnapshot> LoadServiceMetadata()
    {
        var result = new Dictionary<string, PimaxServiceSnapshot>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (root is null) return result;
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            var display = key?.GetValue("DisplayName") as string;
            var image = key?.GetValue("ImagePath") as string;
            var haystack = $"{name}|{display}|{image}";
            if (!haystack.Contains("Pimax", StringComparison.OrdinalIgnoreCase) && !haystack.Contains("PiService", StringComparison.OrdinalIgnoreCase) && !haystack.Contains("Tobii", StringComparison.OrdinalIgnoreCase)) continue;
            var executable = NormalizeServiceExecutable(image);
            var info = FileInfo(executable);
            result[name] = new PimaxServiceSnapshot(name, display, null, null, StartMode(key?.GetValue("Start")), ServiceType(key?.GetValue("Type")), PimaxConnectivityRedactor.SanitizePath(executable), info.Sha256, info.Company, info.Product, info.Description, ReadMultiString(key?.GetValue("DependOnService")), []);
        }
        var dependents = result.Values.SelectMany(service => service.Dependencies.Select(dependency => (dependency, service.Name))).GroupBy(item => item.dependency, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Select(item => item.Name).ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var name in result.Keys.ToArray()) result[name] = result[name] with { Dependents = dependents.GetValueOrDefault(name, []) };
        return result;
    }

    private string? ServiceForPid(int pid) => _serviceMetadata.Values.FirstOrDefault(service => service.ProcessId == pid)?.Name;
    private PimaxFileMetadata FileMetadata(string? path) { if (string.IsNullOrWhiteSpace(path)) return new(null, null, null, null, null, null, "unavailable"); if (!_fileMetadata.TryGetValue(path, out var value)) _fileMetadata[path] = value = FileInfo(path); return value; }
    private static bool IsPimaxProcess(string name, string? path) => name.Contains("Pimax", StringComparison.OrdinalIgnoreCase) || name is "PiServiceLauncher" or "PiService" or "PiPlayService" or "DeviceSetting" or "pi_server" or "PiPlatformService_64" || path?.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) == true;
    private static (string? State, int? Pid) ParseServiceQuery(string output) { string? state = null; int? pid = null; foreach (var line in output.SplitLines()) { var parts = line.Split(':', 2); if (parts.Length != 2) continue; if (parts[0].Trim().Equals("STATE", StringComparison.OrdinalIgnoreCase)) state = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault(); if (parts[0].Trim().Equals("PID", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1], out var parsed) && parsed > 0) pid = parsed; } return (state, pid); }
    private static string? NormalizeServiceExecutable(string? image) { if (string.IsNullOrWhiteSpace(image)) return null; var expanded = Environment.ExpandEnvironmentVariables(image.Trim()); if (expanded.StartsWith('"')) { var end = expanded.IndexOf('"', 1); return end > 1 ? expanded[1..end] : expanded.Trim('"'); } var exe = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); return exe >= 0 ? expanded[..(exe + 4)] : expanded.Split(' ', 2)[0]; }
    private static string? StartMode(object? value) => value is int number ? number switch { 0 => "boot", 1 => "system", 2 => "auto", 3 => "manual", 4 => "disabled", _ => number.ToString() } : null;
    private static string? ServiceType(object? value) => value is int number ? $"0x{number:X}" : null;
    private static string[] ReadMultiString(object? value) => value switch { string[] values => values, string text when !string.IsNullOrWhiteSpace(text) => [text], _ => [] };
    private static PimaxFileMetadata FileInfo(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new(null, null, null, null, null, null, "unavailable");
            var info = FileVersionInfo.GetVersionInfo(path);
            string? signer = null;
            var signature = "unavailable";
            try
            {
                using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(path);
                signer = certificate.Subject;
                signature = "signaturePresent";
            }
            catch (System.Security.Cryptography.CryptographicException) { signature = "notSignedOrUnreadable"; }
            return new(Hash(path), info.CompanyName, info.ProductName, info.FileDescription, info.FileVersion, signer, signature);
        }
        catch { return new(null, null, null, null, null, null, "unavailable"); }
    }
    private static string Hash(string path) { try { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); } catch { return "unavailable"; } }
    private static T? Safe<T>(Func<T> action) { try { return action(); } catch { return default; } }

    private static int? ParentProcessId(Process process)
    {
        try
        {
            var info = new ProcessBasicInformation();
            return NtQueryInformationProcess(process.Handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) == 0 ? (int)info.InheritedFromUniqueProcessId : null;
        }
        catch { return null; }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation { public IntPtr Reserved1; public IntPtr PebBaseAddress; public IntPtr Reserved2_0; public IntPtr Reserved2_1; public IntPtr UniqueProcessId; public IntPtr InheritedFromUniqueProcessId; }
    private sealed record PimaxFileMetadata(string? Sha256, string? Company, string? Product, string? Description, string? Version, string? SignerSubject, string? SignatureStatus);
}
