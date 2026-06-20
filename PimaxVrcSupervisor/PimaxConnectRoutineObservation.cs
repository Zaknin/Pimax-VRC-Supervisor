using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class PimaxConnectRoutineObservationSchema
{
    public const string Version = "pimax-connect-routine-observation-v1";
}

internal static class PimaxConnectRoutineObservationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal sealed record PimaxConnectRoutineObservationRequest(
    int? DurationSeconds,
    string? OutputDirectory,
    int SampleIntervalMilliseconds,
    string Scenario,
    int MaxOutputFiles,
    int MaxFileBytes)
{
    public static PimaxConnectRoutineObservationRequest Parse(string[] args)
        => new(
            OptionalBoundedInt(Option(args, "--duration-seconds"), 20, 45),
            Option(args, "--output-dir"),
            BoundedInt(Option(args, "--sample-interval-ms"), 500, 250, 5000),
            Option(args, "--scenario") ?? "connect-routine-observation",
            BoundedInt(Option(args, "--max-output-files"), 64, 16, 256),
            BoundedInt(Option(args, "--max-file-bytes"), 2 * 1024 * 1024, 256 * 1024, 16 * 1024 * 1024));

    private static int? OptionalBoundedInt(string? value, int minimum, int maximum)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : null;

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

internal sealed record PimaxConnectRoutineObservationResult(
    string Schema,
    string OperationId,
    string Scenario,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationMs,
    int DurationSeconds,
    int SampleIntervalMilliseconds,
    string? OutputDirectory,
    bool Cancelled,
    PimaxRoutineSnapshot Baseline,
    PimaxRoutineSnapshot Final,
    PimaxRoutineTransition[] Transitions,
    PimaxRoutineFileChange[] FileChanges,
    PimaxComponentHealthSnapshot[] HealthTimeline,
    string[] Bounds,
    string[] PrivacyControls,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxRoutineSnapshot(
    DateTimeOffset CollectedAt,
    PimaxRoutineProcess[] Processes,
    PimaxServiceSnapshot[] Services,
    PimaxRoutineEndpoint[] LocalhostEndpoints,
    string[] NamedPipeNames,
    PimaxRoutineFileFingerprint[] PimaxLogs,
    PimaxComponentHealthSnapshot? ComponentHealth);

internal sealed record PimaxRoutineProcess(
    int ProcessId,
    int? ParentProcessId,
    string Name,
    string? ExecutablePath,
    DateTimeOffset? StartTime,
    string? CommandLine,
    bool? HasVisibleWindow);

internal sealed record PimaxRoutineEndpoint(string Protocol, string LocalAddress, int LocalPort, string State, int? ProcessId);
internal sealed record PimaxRoutineFileFingerprint(string Source, string Path, DateTimeOffset LastWriteTime, long Length, string Sha256);
internal sealed record PimaxRoutineTransition(double ElapsedMs, DateTimeOffset ObservedAt, string Kind, string Identity, string Change);
internal sealed record PimaxRoutineFileChange(string Source, string Path, DateTimeOffset BeforeLastWriteTime, DateTimeOffset AfterLastWriteTime, long BeforeLength, long AfterLength, string BeforeSha256, string AfterSha256, string[] SanitizedChangedLines);

internal sealed class PimaxConnectRoutineObserver
{
    private const int MaxTransitions = 4000;
    private readonly SupervisorConfig _config;
    private readonly PimaxComponentHealthCoordinator _health;
    private readonly Func<DateTimeOffset> _now;

    public PimaxConnectRoutineObserver(SupervisorConfig config)
        : this(config, new PimaxComponentHealthCoordinator(), () => DateTimeOffset.Now)
    {
    }

    internal PimaxConnectRoutineObserver(SupervisorConfig config, PimaxComponentHealthCoordinator health, Func<DateTimeOffset>? now = null)
    {
        _config = config;
        _health = health;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<PimaxConnectRoutineObservationResult> ObserveAsync(PimaxConnectRoutineObservationRequest request, CancellationToken cancellationToken)
    {
        var operationId = $"pimax-connect-routine-{Guid.NewGuid():N}";
        var warnings = new List<string>();
        var errors = new List<string>();
        if (request.DurationSeconds is null) errors.Add("--duration-seconds is required and must be between 20 and 45 seconds.");
        if (string.IsNullOrWhiteSpace(request.OutputDirectory)) errors.Add("--output-dir is required.");

        var startedAt = _now();
        var outputDirectory = PrepareOutputDirectory(request.OutputDirectory, errors);
        if (errors.Count > 0)
        {
            var empty = await CaptureSnapshotAsync(null, includeComponentHealth: false, errors, CancellationToken.None);
            return Result(operationId, request, startedAt, _now(), 0, outputDirectory, false, empty, empty, [], [], [], warnings, errors);
        }

        var stopwatch = Stopwatch.StartNew();
        var baseline = await CaptureSnapshotAsync("baseline", includeComponentHealth: true, errors, cancellationToken);
        WriteJson(outputDirectory!, "baseline-snapshot.json", baseline, request.MaxFileBytes, warnings);
        var previous = baseline;
        var transitions = new List<PimaxRoutineTransition>();
        var healthTimeline = new List<PimaxComponentHealthSnapshot>();
        if (baseline.ComponentHealth is not null) healthTimeline.Add(baseline.ComponentHealth);
        var cancelled = false;

        using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        duration.CancelAfter(TimeSpan.FromSeconds(request.DurationSeconds!.Value));
        while (!duration.IsCancellationRequested)
        {
            try { await Task.Delay(request.SampleIntervalMilliseconds, duration.Token); }
            catch (OperationCanceledException) when (duration.IsCancellationRequested) { }

            var current = await CaptureSnapshotAsync(null, includeComponentHealth: false, errors, CancellationToken.None);
            Track(previous, current, transitions, stopwatch.Elapsed);
            previous = current;
            if (current.ComponentHealth is not null && healthTimeline.Count < 64) healthTimeline.Add(current.ComponentHealth);
        }

        cancelled = cancellationToken.IsCancellationRequested;
        var final = await CaptureSnapshotAsync("final", includeComponentHealth: true, errors, CancellationToken.None);
        WriteJson(outputDirectory!, "final-snapshot.json", final, request.MaxFileBytes, warnings);
        var fileChanges = CompareFiles(baseline.PimaxLogs, final.PimaxLogs).Take(128).ToArray();
        WriteJson(outputDirectory!, "file-changes.json", fileChanges, request.MaxFileBytes, warnings);
        var endedAt = _now();
        var result = Result(
            operationId,
            request,
            startedAt,
            endedAt,
            stopwatch.Elapsed.TotalMilliseconds,
            outputDirectory,
            cancelled,
            baseline,
            final,
            transitions.Take(MaxTransitions).ToArray(),
            fileChanges,
            healthTimeline.Take(64).ToArray(),
            warnings,
            errors);
        WriteJson(outputDirectory!, "observation-result.json", result, request.MaxFileBytes, warnings);
        return result with { Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    }

    private PimaxConnectRoutineObservationResult Result(
        string operationId,
        PimaxConnectRoutineObservationRequest request,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        double durationMs,
        string? outputDirectory,
        bool cancelled,
        PimaxRoutineSnapshot baseline,
        PimaxRoutineSnapshot final,
        PimaxRoutineTransition[] transitions,
        PimaxRoutineFileChange[] fileChanges,
        PimaxComponentHealthSnapshot[] healthTimeline,
        List<string> warnings,
        List<string> errors)
        => new(
            PimaxConnectRoutineObservationSchema.Version,
            operationId,
            request.Scenario,
            startedAt,
            endedAt,
            durationMs,
            request.DurationSeconds ?? 0,
            request.SampleIntervalMilliseconds,
            outputDirectory,
            cancelled,
            baseline,
            final,
            transitions,
            fileChanges,
            healthTimeline,
            [
                "Explicit duration required; clamped to 20-45 seconds.",
                "Explicit output directory required.",
                "No persistence after completion.",
                "No scheduled task or background child is created.",
                "Output files are bounded and written only under the requested output directory."
            ],
            [
                "Raw PnP instance IDs, serials, usernames, machine names, and private full paths are not emitted.",
                "Named pipes are reduced to bounded sanitized names.",
                "Log output is limited to sanitized relevant changed lines.",
                "Local endpoints are inventoried without capturing payloads."
            ],
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    private async Task<PimaxRoutineSnapshot> CaptureSnapshotAsync(string? reason, bool includeComponentHealth, List<string> errors, CancellationToken cancellationToken)
    {
        PimaxComponentHealthSnapshot? health = null;
        if (includeComponentHealth)
        {
            try { health = await _health.CollectAsync(_config, cancellationToken); }
            catch (Exception ex) { errors.Add($"Component health sample failed: {ex.Message}"); }
        }

        return new PimaxRoutineSnapshot(
            _now(),
            CaptureProcesses(),
            await CaptureServicesAsync(cancellationToken),
            CaptureEndpoints(),
            CapturePipes(),
            DiscoverPimaxLogs().Take(32).ToArray(),
            health);
    }

    private static PimaxRoutineProcess[] CaptureProcesses()
        => Process.GetProcesses()
            .Select(process =>
            {
                using (process)
                {
                    try
                    {
                        var name = process.ProcessName;
                        var path = Safe(() => process.MainModule?.FileName);
                        if (!IsRelevantProcess(name, path)) return null;
                        return new PimaxRoutineProcess(
                            process.Id,
                            null,
                            name,
                            PimaxConnectivityRedactor.SanitizePath(path),
                            Safe(() => new DateTimeOffset(process.StartTime)),
                            null,
                            Safe(() => process.MainWindowHandle != IntPtr.Zero));
                    }
                    catch { return null; }
                }
            })
            .Where(process => process is not null)
            .Cast<PimaxRoutineProcess>()
            .OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.ProcessId)
            .ToArray();

    private static bool IsRelevantProcess(string name, string? path)
    {
        var haystack = $"{name}|{path}";
        return haystack.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("PiService", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("DeviceSetting", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("pi_server", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("SteamVR", StringComparison.OrdinalIgnoreCase)
            || name is "vrserver" or "vrmonitor" or "vrdashboard";
    }

    private static async Task<PimaxServiceSnapshot[]> CaptureServicesAsync(CancellationToken cancellationToken)
    {
        try { return await new WindowsPimaxLifecycleObservationProbe(new SupervisorConfig()).CaptureServicesAsync(cancellationToken); }
        catch { return []; }
    }

    private static PimaxRoutineEndpoint[] CaptureEndpoints()
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var tcp = properties.GetActiveTcpConnections()
                .Where(endpoint => IPAddressIsLocal(endpoint.LocalEndPoint.Address))
                .Select(endpoint => new PimaxRoutineEndpoint("tcp", endpoint.LocalEndPoint.Address.ToString(), endpoint.LocalEndPoint.Port, endpoint.State.ToString(), null));
            var udp = properties.GetActiveUdpListeners()
                .Where(endpoint => IPAddressIsLocal(endpoint.Address))
                .Select(endpoint => new PimaxRoutineEndpoint("udp", endpoint.Address.ToString(), endpoint.Port, "listen", null));
            return tcp.Concat(udp)
                .OrderBy(endpoint => endpoint.Protocol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(endpoint => endpoint.LocalPort)
                .Take(512)
                .ToArray();
        }
        catch { return []; }
    }

    private static bool IPAddressIsLocal(System.Net.IPAddress address)
        => System.Net.IPAddress.IsLoopback(address)
            || address.ToString() == "0.0.0.0"
            || address.ToString() == "::";

    private static string[] CapturePipes()
    {
        try
        {
            return Directory.GetFiles(@"\\.\pipe\")
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => name!.Contains("pimax", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("pi", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("vr", StringComparison.OrdinalIgnoreCase))
                .Select(name => PimaxConnectivityRedactor.SanitizeMessage(name!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(128)
                .ToArray();
        }
        catch { return []; }
    }

    private IEnumerable<PimaxRoutineFileFingerprint> DiscoverPimaxLogs()
    {
        var roots = new[]
        {
            ("PiService", Environment.ExpandEnvironmentVariables(_config.PimaxServiceLogDirectory)),
            ("PimaxClientAppData", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PimaxClient", "logs")),
            ("PimaxLocalAppData", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pimax"))
        };

        foreach (var (source, root) in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(path => path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(12)
                    .ToArray();
            }
            catch { continue; }

            foreach (var path in files)
            {
                PimaxRoutineFileFingerprint? fingerprint = null;
                try
                {
                    var info = new FileInfo(path);
                    fingerprint = new PimaxRoutineFileFingerprint(source, PimaxConnectivityRedactor.SanitizePath(path) ?? Path.GetFileName(path), info.LastWriteTime, info.Length, HashFile(path));
                }
                catch { }

                if (fingerprint is not null) yield return fingerprint;
            }
        }
    }

    private static void Track(PimaxRoutineSnapshot previous, PimaxRoutineSnapshot current, List<PimaxRoutineTransition> transitions, TimeSpan elapsed)
    {
        TrackSet(previous.Processes.Select(process => $"{process.ProcessId}:{process.Name}"), current.Processes.Select(process => $"{process.ProcessId}:{process.Name}"), "process", transitions, elapsed, current.CollectedAt);
        TrackSet(previous.Services.Select(service => $"{service.Name}:{service.State}:{service.ProcessId}"), current.Services.Select(service => $"{service.Name}:{service.State}:{service.ProcessId}"), "service", transitions, elapsed, current.CollectedAt);
        TrackSet(previous.LocalhostEndpoints.Select(endpoint => $"{endpoint.Protocol}:{endpoint.LocalAddress}:{endpoint.LocalPort}:{endpoint.State}"), current.LocalhostEndpoints.Select(endpoint => $"{endpoint.Protocol}:{endpoint.LocalAddress}:{endpoint.LocalPort}:{endpoint.State}"), "localhostEndpoint", transitions, elapsed, current.CollectedAt);
        TrackSet(previous.NamedPipeNames, current.NamedPipeNames, "namedPipe", transitions, elapsed, current.CollectedAt);
        if (previous.ComponentHealth?.OverallStatus != current.ComponentHealth?.OverallStatus)
        {
            transitions.Add(new PimaxRoutineTransition(elapsed.TotalMilliseconds, current.CollectedAt, "componentHealth", "overallStatus", $"{previous.ComponentHealth?.OverallStatus ?? "none"} -> {current.ComponentHealth?.OverallStatus ?? "none"}"));
        }
    }

    private static void TrackSet(IEnumerable<string> previous, IEnumerable<string> current, string kind, List<PimaxRoutineTransition> transitions, TimeSpan elapsed, DateTimeOffset observedAt)
    {
        var before = previous.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = current.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var value in after.Except(before, StringComparer.OrdinalIgnoreCase))
        {
            transitions.Add(new PimaxRoutineTransition(elapsed.TotalMilliseconds, observedAt, kind, value, "appeared"));
        }

        foreach (var value in before.Except(after, StringComparer.OrdinalIgnoreCase))
        {
            transitions.Add(new PimaxRoutineTransition(elapsed.TotalMilliseconds, observedAt, kind, value, "disappeared"));
        }
    }

    private static IEnumerable<PimaxRoutineFileChange> CompareFiles(IEnumerable<PimaxRoutineFileFingerprint> before, IEnumerable<PimaxRoutineFileFingerprint> after)
    {
        var beforeByPath = before.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var current in after)
        {
            if (!beforeByPath.TryGetValue(current.Path, out var previous) || previous.Sha256 == current.Sha256) continue;
            yield return new PimaxRoutineFileChange(
                current.Source,
                current.Path,
                previous.LastWriteTime,
                current.LastWriteTime,
                previous.Length,
                current.Length,
                previous.Sha256,
                current.Sha256,
                []);
        }
    }

    private static string? PrepareOutputDirectory(string? path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            Directory.CreateDirectory(path);
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            errors.Add($"Output directory unavailable: {ex.Message}");
            return null;
        }
    }

    private static void WriteJson<T>(string directory, string fileName, T value, int maxBytes, List<string> warnings)
    {
        try
        {
            var path = Path.Combine(directory, fileName);
            var json = JsonSerializer.Serialize(value, PimaxConnectRoutineObservationJson.Options);
            if (json.Length > maxBytes)
            {
                warnings.Add($"{fileName} exceeded the configured output byte cap and was not written.");
                return;
            }

            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not write {fileName}: {ex.Message}");
        }
    }

    private static string HashFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch { return "unavailable"; }
    }

    private static T? Safe<T>(Func<T> action)
    {
        try { return action(); }
        catch { return default; }
    }
}
