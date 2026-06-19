using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PimaxVrcSupervisor.Diagnostics;

internal static class UvcIsolationSchemas
{
    public const string Session = "uvc-isolation-session-v1";
    public const string StartResult = "uvc-isolation-session-start-result-v1";
    public const string Annotation = "uvc-isolation-session-annotation-v1";
    public const string FinishResult = "uvc-isolation-session-finish-result-v1";
    public const string Analysis = "uvc-isolation-analysis-v1";
    public const string UvcInventory = "uvc-isolation-uvc-inventory-v1";
    public const string ProcessState = "uvc-isolation-process-state-v1";
    public const string SystemState = "uvc-isolation-system-state-v1";
    public const string ConfigSnapshot = "uvc-isolation-config-snapshot-v1";
}

internal static class UvcIsolationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal sealed record UvcIsolationStartRequest(
    string OutputPath,
    string Label,
    string[] ScenarioTags,
    UvcOperatorPhysicalState OperatorState,
    string? Notes,
    string? ConfigPath)
{
    public static UvcIsolationStartRequest Parse(string[] args, string? configPath)
    {
        var scenarios = UvcIsolationArguments.Values(args, "--scenario").SelectMany(SplitCsv).ToArray();
        return new(
            UvcIsolationArguments.Required(args, "--output"),
            UvcIsolationArguments.Required(args, "--label"),
            scenarios.Length == 0 ? ["custom"] : scenarios,
            UvcOperatorPhysicalState.FromArgs(args),
            UvcIsolationArguments.Value(args, "--notes"),
            configPath);
    }

    private static IEnumerable<string> SplitCsv(string value)
        => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

internal sealed record UvcIsolationAnnotationRequest(
    string SessionPath,
    string Observation,
    DateTimeOffset? ObservationTimeUtc,
    string Source,
    bool CaptureSnapshot,
    string? Notes)
{
    public static UvcIsolationAnnotationRequest Parse(string[] args)
    {
        DateTimeOffset? at = null;
        if (DateTimeOffset.TryParse(UvcIsolationArguments.Value(args, "--timestamp-utc"), out var parsed)) at = parsed.ToUniversalTime();
        return new(
            UvcIsolationArguments.Required(args, "--session"),
            UvcIsolationArguments.Required(args, "--observation"),
            at,
            UvcIsolationArguments.Value(args, "--source") ?? "operator",
            UvcIsolationArguments.Has(args, "--capture-snapshot"),
            UvcIsolationArguments.Value(args, "--notes"));
    }
}

internal sealed record UvcIsolationFinishRequest(
    string SessionPath,
    string Result,
    string? Notes,
    string? DumpPath,
    string? WindbgReportPath)
{
    public static UvcIsolationFinishRequest Parse(string[] args)
        => new(
            UvcIsolationArguments.Required(args, "--session"),
            ValidateResult(UvcIsolationArguments.Required(args, "--result")),
            UvcIsolationArguments.Value(args, "--notes"),
            UvcIsolationArguments.Value(args, "--dump"),
            UvcIsolationArguments.Value(args, "--windbg-report"));

    private static string ValidateResult(string value)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "stable", "applicationCrash", "systemFreeze", "unexpectedReboot", "bugcheck", "aborted", "unknown"
        };
        if (!allowed.Contains(value)) throw new ArgumentException("--result has an unsupported value.");
        return value;
    }
}

internal sealed record UvcIsolationAnalysisRequest(
    string[] SessionPaths,
    string[] FlightRecorderPaths,
    string[] WindowsEventCorrelationPaths,
    string? OutputPath,
    string? MarkdownOutputPath)
{
    public static UvcIsolationAnalysisRequest Parse(string[] args)
    {
        var sessions = UvcIsolationArguments.Values(args, "--session").ToArray();
        if (sessions.Length == 0) throw new ArgumentException("At least one --session path is required.");
        return new(
            sessions,
            UvcIsolationArguments.Values(args, "--flight-recorder").ToArray(),
            UvcIsolationArguments.Values(args, "--windows-events").ToArray(),
            UvcIsolationArguments.Value(args, "--output"),
            UvcIsolationArguments.Value(args, "--markdown-output"));
    }
}

internal sealed record UvcOperatorPhysicalState(
    string VivePhysicallyConnected,
    string ViveDisconnectedBeforeSleep,
    string ViveReconnectedAfterWake,
    string SamePhysicalUsbPortUsed,
    string PimaxConnected,
    string SleepOccurred,
    string RebootOccurred,
    string? Notes,
    string ObservationSource)
{
    public static UvcOperatorPhysicalState FromArgs(string[] args)
        => new(
            Tri(UvcIsolationArguments.Value(args, "--vive-connected")),
            Tri(UvcIsolationArguments.Value(args, "--vive-disconnected-before-sleep")),
            Tri(UvcIsolationArguments.Value(args, "--vive-reconnected-after-wake")),
            Tri(UvcIsolationArguments.Value(args, "--same-port")),
            Tri(UvcIsolationArguments.Value(args, "--pimax-connected")),
            Tri(UvcIsolationArguments.Value(args, "--sleep-wake")),
            Tri(UvcIsolationArguments.Value(args, "--reboot")),
            UvcIsolationArguments.Value(args, "--physical-notes"),
            "operator");

    internal static string Tri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase)) return "yes";
        if (value.Equals("no", StringComparison.OrdinalIgnoreCase)) return "no";
        if (value.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return "unknown";
        throw new ArgumentException("Physical-state values must be yes, no, or unknown.");
    }
}

internal sealed record UvcIsolationSessionManifest(
    string Schema,
    string SessionId,
    string Label,
    string ScenarioId,
    string[] ScenarioTags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CreatedAtLocal,
    string ToolBuildCommit,
    string MachineIdentityHash,
    UvcBootIdentityRecord BootIdentity,
    string ProcessInvocationId,
    UvcOperatorPhysicalState OperatorPhysicalState,
    UvcSupervisorConfigSnapshot SupervisorConfiguration,
    string? Notes);

internal sealed record UvcBootIdentityRecord(
    DateTimeOffset ApproximateBootTimeUtc,
    long UptimeMilliseconds,
    int WindowsSessionId,
    string Fingerprint,
    string Confidence)
{
    public static UvcBootIdentityRecord Capture()
    {
        var boot = BootIdentity.Capture();
        return new(boot.ApproximateBootTimeUtc, boot.UptimeMilliseconds, boot.WindowsSessionId, boot.Fingerprint, boot.Confidence);
    }
}

internal sealed record UvcSupervisorConfigSnapshot(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string Status,
    string? ConfigPathHash,
    string? ConfigFileName,
    bool? DetectViveFaceTrackerUsage,
    bool? RestartOnFaceTrackerReconnect,
    bool? RestartOnHeadsetReconnect,
    bool? PiServiceReconnectLogWatching,
    bool? WindowsPnpFastTrackerReconnectWatching,
    bool? VrcFaceTrackingManagedAppEnabled,
    bool? VrcFaceTrackingStartMinimized,
    string? VrcFaceTrackingExecutableBasename,
    string? VrcFaceTrackingExecutablePathHash,
    string SupervisorBuildIdentity,
    string[] Warnings);

internal sealed record UvcProcessSnapshot(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    UvcProcessGroupState[] Processes,
    string[] Warnings);

internal sealed record UvcProcessGroupState(
    string Group,
    bool Present,
    UvcProcessRecord[] Instances);

internal sealed record UvcProcessRecord(
    int ProcessId,
    DateTimeOffset? StartTimeUtc,
    string ProcessName,
    string? ExecutableBasename,
    string? ExecutableHash,
    string? Version,
    string Status,
    string[] Warnings);

internal sealed record UvcSystemStateSnapshot(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    UvcBootIdentityRecord BootIdentity,
    string MachineIdentityHash,
    UvcRecorderHealth RecorderHealth,
    string[] Warnings);

internal sealed record UvcRecorderHealth(
    string Status,
    string? LastHeartbeatUtc,
    string? ProcessName,
    int? ProcessId,
    long? DroppedEventCount,
    int? QueueDepth,
    long? LastDurableSequence,
    string? SourcePath,
    string[] Warnings);

internal sealed record UvcInventorySnapshot(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    UvcDeviceRecord[] Devices,
    string[] Warnings,
    string[] Errors);

internal sealed record UvcDeviceRecord(
    string FriendlyName,
    string SanitizedInstanceHash,
    string? Vid,
    string? Pid,
    string? InterfaceNumber,
    string? ContainerHash,
    string? Service,
    string? DriverInf,
    string? Provider,
    string? Version,
    string? Status,
    string? ParentDeviceHash,
    string? ParentVid,
    string? ParentPid,
    string[] SanitizedLocationPathHashes,
    string? UsbHubControllerGroup,
    string? ConnectorGroup,
    string[] UpperFilters,
    string[] LowerFilters,
    string Classification,
    string ClassificationConfidence,
    string[] Evidence);

internal sealed record UvcIsolationAnnotation(
    string Schema,
    string AnnotationId,
    string SessionId,
    DateTimeOffset RecordedAtUtc,
    DateTimeOffset ObservationTimeUtc,
    string Source,
    string Observation,
    string? Notes,
    bool CapturedSnapshot,
    string[] MachineObservationFiles);

internal sealed record UvcCrashMetadata(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    UvcDumpMetadata? Dump,
    UvcWindbgParseResult? Windbg,
    bool MatchesRepeatedUsbVideoPayloadBucket,
    string[] Warnings);

internal sealed record UvcDumpMetadata(
    string Path,
    string SanitizedPath,
    string Status,
    long? SizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    string? Sha256,
    string? Error);

internal sealed record UvcWindbgParseResult(
    string Schema,
    string? BugcheckCode,
    string? BugcheckName,
    string[] Arguments,
    string? ProcessName,
    string? ModuleName,
    string? ImageName,
    string? SymbolName,
    string? FailureBucketId,
    string? FailureHash,
    string[] StackSummary,
    string? CrashTimestamp,
    string? DumpName,
    string[] Warnings);

internal sealed record UvcSessionFinalRecord(
    string Schema,
    string SessionId,
    string Label,
    string[] ScenarioTags,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    double DurationSeconds,
    bool BootIdentityChanged,
    string Result,
    string? Notes,
    UvcCrashMetadata CrashMetadata,
    string[] Warnings);

internal sealed record UvcIsolationStartResult(
    string Schema,
    string SessionId,
    string SessionPath,
    string[] FilesWritten,
    UvcRecorderHealth RecorderHealth,
    string[] Warnings);

internal sealed record UvcIsolationFinishResult(
    string Schema,
    string SessionId,
    string SessionPath,
    string Result,
    bool BootIdentityChanged,
    double DurationSeconds,
    string[] FilesWritten,
    UvcCrashMetadata CrashMetadata,
    string[] Warnings);

internal sealed record UvcIsolationAnalysisResult(
    string Schema,
    DateTimeOffset AnalyzedAtUtc,
    UvcAnalysisSessionRow[] Sessions,
    UvcComparisonGroup[] ComparisonGroups,
    UvcFinding[] Findings,
    string RecommendedNextTest,
    string[] Warnings);

internal sealed record UvcAnalysisSessionRow(
    string SessionId,
    string Label,
    DateTimeOffset DateUtc,
    string[] ScenarioTags,
    string BootIdentity,
    string SleepWake,
    string VivePhysicallyConnected,
    string ViveDisconnectedBeforeSleep,
    string ViveReconnectedAfterWake,
    string SamePortObservation,
    string VrcFaceTrackingState,
    string SteamVrState,
    string VrChatState,
    bool? SupervisorViveDetection,
    bool? FaceTrackerReconnectRestart,
    bool? HeadsetReconnectRestart,
    string[] UvcDeviceSet,
    double? VrDurationSeconds,
    string Result,
    string? Bugcheck,
    string? FailureBucket,
    string? DumpHash);

internal sealed record UvcComparisonGroup(string Dimension, string Value, int StableSessions, int CrashedSessions, string[] SessionIds);
internal sealed record UvcFinding(string Topic, string Label, string Evidence, string Limitation);

internal interface IUvcIsolationEnvironment
{
    UvcSupervisorConfigSnapshot CaptureConfig(string? configPath);
    UvcProcessSnapshot CaptureProcesses();
    UvcInventorySnapshot CaptureUvcInventory();
    UvcSystemStateSnapshot CaptureSystemState();
    WindowsEventCorrelationResult CaptureWindowsEvents(DateTimeOffset startUtc, DateTimeOffset endUtc);
}

internal sealed class WindowsUvcIsolationEnvironment : IUvcIsolationEnvironment
{
    public UvcSupervisorConfigSnapshot CaptureConfig(string? configPath)
    {
        try
        {
            var config = SupervisorConfig.Load(configPath);
            config.TryGetMouthTrackerUser(out var mouthTrackerUser);
            return new(
                UvcIsolationSchemas.ConfigSnapshot,
                DateTimeOffset.UtcNow,
                "available",
                UvcPrivacy.HashOrNull(config.LoadedFromPath),
                string.IsNullOrWhiteSpace(config.LoadedFromPath) ? null : Path.GetFileName(config.LoadedFromPath),
                mouthTrackerUser,
                config.MouthTrackerRestartOnReconnectEnabled,
                config.FaceTrackerRestartOnReconnectEnabled,
                config.UsePimaxServiceLogReconnectDetector,
                config.UseMouthTrackerPnPReconnectDetector,
                config.FaceTrackerAutomationEnabled,
                config.VrcFaceTrackingStartMinimized,
                string.IsNullOrWhiteSpace(config.VrcFaceTrackingPath) ? null : Path.GetFileName(config.VrcFaceTrackingPath),
                UvcPrivacy.HashOrNull(config.VrcFaceTrackingPath),
                BuildIdentity(),
                []);
        }
        catch (Exception ex)
        {
            return new(UvcIsolationSchemas.ConfigSnapshot, DateTimeOffset.UtcNow, "unavailable", null, null, null, null, null, null, null, null, null, null, null, BuildIdentity(), [$"Config snapshot unavailable: {ex.GetType().Name}"]);
        }
    }

    public UvcProcessSnapshot CaptureProcesses()
    {
        var groups = new (string Group, string[] Names)[]
        {
            ("Supervisor", ["PimaxVrcSupervisor"]),
            ("Watcher", ["PimaxVrcSupervisorWatcher"]),
            ("Configurator", ["PimaxVrcSupervisor.ConfigEditor", "PimaxVrcSupervisorConfigurator"]),
            ("VRCFaceTracking", ["VRCFaceTracking"]),
            ("PimaxPlay", ["PimaxClient", "PimaxPlay", "PlayLauncher", "DeviceSetting"]),
            ("PiService", ["PiService", "PiServiceLauncher", "PiPlayService", "PiPlatformService_64"]),
            ("Steam", ["steam"]),
            ("SteamVR", ["vrserver", "vrmonitor", "vrcompositor"]),
            ("VRChat", ["VRChat"])
        };
        var warnings = new List<string>();
        var result = new List<UvcProcessGroupState>();
        foreach (var group in groups)
        {
            var instances = new List<UvcProcessRecord>();
            foreach (var name in group.Names)
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); }
                catch (Exception ex)
                {
                    warnings.Add($"{group.Group}/{name} query failed: {ex.GetType().Name}");
                    continue;
                }
                foreach (var process in processes)
                {
                    using (process)
                    {
                        instances.Add(ReadProcess(process));
                    }
                }
            }
            result.Add(new(group.Group, instances.Count > 0, instances.OrderBy(item => item.ProcessName).ThenBy(item => item.ProcessId).ToArray()));
        }
        return new(UvcIsolationSchemas.ProcessState, DateTimeOffset.UtcNow, result.ToArray(), warnings.ToArray());
    }

    public UvcInventorySnapshot CaptureUvcInventory()
    {
        try
        {
            var rawResult = new WindowsPnpDeviceInventorySource().Collect();
            var byId = rawResult.Devices.ToDictionary(item => item.InstanceId, StringComparer.OrdinalIgnoreCase);
            var devices = rawResult.Devices
                .Where(IsUvcLike)
                .Select(raw => ToUvcRecord(raw, byId))
                .OrderBy(item => item.Vid, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Pid, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SanitizedInstanceHash, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new(UvcIsolationSchemas.UvcInventory, DateTimeOffset.UtcNow, devices, rawResult.Warnings, rawResult.Errors);
        }
        catch (Exception ex)
        {
            return new(UvcIsolationSchemas.UvcInventory, DateTimeOffset.UtcNow, [], [], [$"UVC inventory unavailable: {ex.GetType().Name}"]);
        }
    }

    public UvcSystemStateSnapshot CaptureSystemState()
    {
        var boot = UvcBootIdentityRecord.Capture();
        return new(UvcIsolationSchemas.SystemState, DateTimeOffset.UtcNow, boot, UvcPrivacy.Hash(Environment.MachineName), ReadRecorderHealth(), []);
    }

    public WindowsEventCorrelationResult CaptureWindowsEvents(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var boundedStart = startUtc;
        var warnings = new List<string>();
        if (endUtc - boundedStart > TimeSpan.FromHours(24))
        {
            boundedStart = endUtc.AddHours(-24);
            warnings.Add("Windows event collection was bounded to the final 24 hours of the session.");
        }
        var result = new WindowsEventCorrelationCollector().Collect(new(boundedStart, endUtc, null, null, null, null));
        return result with { Warnings = result.Warnings.Concat(warnings).ToArray() };
    }

    private static UvcProcessRecord ReadProcess(Process process)
    {
        var warnings = new List<string>();
        DateTimeOffset? start = null;
        string? path = null;
        string? hash = null;
        string? version = null;
        try { start = process.StartTime.ToUniversalTime(); } catch (Exception ex) { warnings.Add($"startTime:{ex.GetType().Name}"); }
        try { path = process.MainModule?.FileName; } catch (Exception ex) { warnings.Add($"path:{ex.GetType().Name}"); }
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { hash = UvcPrivacy.FileSha256(path); } catch (Exception ex) { warnings.Add($"hash:{ex.GetType().Name}"); }
            try { version = FileVersionInfo.GetVersionInfo(path).FileVersion; } catch (Exception ex) { warnings.Add($"version:{ex.GetType().Name}"); }
        }
        return new(process.Id, start, process.ProcessName, string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path), hash, version, "present", warnings.ToArray());
    }

    private static bool IsUvcLike(PimaxUsbRawDeviceRecord raw)
    {
        var text = string.Join("\n", raw.InstanceId, raw.Service, raw.DeviceClass, raw.FriendlyName, raw.DeviceDescription, string.Join("\n", raw.HardwareIds), string.Join("\n", raw.CompatibleIds));
        return text.Contains("usbvideo", StringComparison.OrdinalIgnoreCase)
            || text.Contains("USB\\Class_0E", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw.DeviceClass, "Camera", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw.DeviceClass, "Image", StringComparison.OrdinalIgnoreCase);
    }

    internal static UvcDeviceRecord ToUvcRecord(PimaxUsbRawDeviceRecord raw, IReadOnlyDictionary<string, PimaxUsbRawDeviceRecord> byId)
    {
        byId.TryGetValue(raw.ParentInstanceId ?? "", out var parent);
        var evidence = new List<string>();
        var classification = "unknownUvc";
        var confidence = "low";
        var haystack = string.Join("\n", raw.InstanceId, raw.FriendlyName, raw.DeviceDescription, raw.Manufacturer, string.Join("\n", raw.HardwareIds));
        if (string.Equals(raw.Vid, "0BB4", StringComparison.OrdinalIgnoreCase) && string.Equals(raw.Pid, "0321", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("HTC Multimedia Camera", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("Vive", StringComparison.OrdinalIgnoreCase))
        {
            classification = "viveOrHtcUvc";
            confidence = "high";
            evidence.Add("HTC/Vive VID/PID or naming evidence");
        }
        else if (string.Equals(raw.Vid, "34A4", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("Pimax", StringComparison.OrdinalIgnoreCase))
        {
            classification = "pimaxLikeUvc";
            confidence = string.Equals(raw.Vid, "34A4", StringComparison.OrdinalIgnoreCase) ? "medium" : "low";
            evidence.Add("Pimax VID or naming evidence");
        }
        else
        {
            evidence.Add("UVC-compatible service/class evidence without known vendor match");
        }
        return new(
            raw.FriendlyName ?? raw.DeviceDescription ?? "unknown UVC device",
            PnpIdentitySanitizer.StableHash(raw.InstanceId),
            raw.Vid,
            raw.Pid,
            raw.UsbInterfaceNumber,
            PnpIdentitySanitizer.StableHashOrNull(raw.ContainerId),
            raw.Service,
            raw.Service?.Equals("usbvideo", StringComparison.OrdinalIgnoreCase) == true ? "usbvideo.inf" : null,
            raw.DriverProvider,
            raw.DriverVersion,
            raw.Status,
            PnpIdentitySanitizer.StableHashOrNull(raw.ParentInstanceId),
            parent?.Vid,
            parent?.Pid,
            raw.LocationPaths.Select(PnpIdentitySanitizer.StableHash).ToArray(),
            raw.LocationPaths.FirstOrDefault() is { } location ? UvcPrivacy.Hash(location.Split('#')[0]) : null,
            null,
            [],
            [],
            classification,
            confidence,
            evidence.ToArray());
    }

    private static UvcRecorderHealth ReadRecorderHealth()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PimaxVrcSupervisor", "Diagnostics", "FlightRecorder", "supervisor-hardware-flight-recorder.jsonl");
        var warnings = new List<string>();
        try
        {
            if (!File.Exists(path)) return new("missing", null, null, null, null, null, null, UvcPrivacy.SanitizePath(path), []);
            foreach (var line in UvcTail.ReadTailLines(path, 64 * 1024).Reverse())
            {
                HardwareFlightRecord? record = null;
                try { record = JsonSerializer.Deserialize<HardwareFlightRecord>(line, HardwareFlightRecorder.JsonOptions); } catch { }
                if (record is null || record.Stage != "heartbeat") continue;
                return new("available", record.TimestampUtc.ToString("O"), record.ProcessName, record.ProcessId, record.DroppedEventCount, record.QueueDepth, record.LastDurableSequence, UvcPrivacy.SanitizePath(path), warnings.ToArray());
            }
            return new("availableNoHeartbeatInTail", null, null, null, null, null, null, UvcPrivacy.SanitizePath(path), warnings.ToArray());
        }
        catch (Exception ex)
        {
            return new("unavailable", null, null, null, null, null, null, UvcPrivacy.SanitizePath(path), [$"Recorder health unavailable: {ex.GetType().Name}"]);
        }
    }

    private static string BuildIdentity()
        => typeof(WindowsUvcIsolationEnvironment).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? "unknown";
}

internal sealed class UvcIsolationSessionService(IUvcIsolationEnvironment? environment = null)
{
    private readonly IUvcIsolationEnvironment _environment = environment ?? new WindowsUvcIsolationEnvironment();

    public UvcIsolationStartResult Start(UvcIsolationStartRequest request)
    {
        var path = Path.GetFullPath(request.OutputPath);
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException($"Session output already exists: {path}");
        Directory.CreateDirectory(path);
        var warnings = new List<string>();
        var system = _environment.CaptureSystemState();
        var config = _environment.CaptureConfig(request.ConfigPath);
        var process = _environment.CaptureProcesses();
        var uvc = _environment.CaptureUvcInventory();
        var session = new UvcIsolationSessionManifest(
            UvcIsolationSchemas.Session,
            $"uvc-session-{Guid.NewGuid():N}",
            request.Label,
            request.ScenarioTags.FirstOrDefault() ?? "custom",
            request.ScenarioTags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.Now,
            UvcGitIdentity.HeadCommit(),
            system.MachineIdentityHash,
            system.BootIdentity,
            $"process-invocation-{Guid.NewGuid():N}",
            request.OperatorState,
            config,
            request.Notes);

        var files = new List<string>
        {
            UvcSessionFiles.WriteJson(path, "session.json", session),
            UvcSessionFiles.WriteJson(path, "start-system-state.json", system),
            UvcSessionFiles.WriteJson(path, "start-process-state.json", process),
            UvcSessionFiles.WriteJson(path, "start-uvc-inventory.json", uvc),
            UvcSessionFiles.WriteJson(path, "start-config-snapshot.json", config)
        };
        files.Add(UvcSessionFiles.WriteSha256Sums(path));
        warnings.AddRange(system.Warnings);
        warnings.AddRange(config.Warnings);
        warnings.AddRange(process.Warnings);
        warnings.AddRange(uvc.Warnings);
        warnings.AddRange(uvc.Errors);
        return new(UvcIsolationSchemas.StartResult, session.SessionId, path, files.ToArray(), system.RecorderHealth, warnings.Distinct().ToArray());
    }

    public UvcIsolationAnnotation Annotate(UvcIsolationAnnotationRequest request)
    {
        var path = Path.GetFullPath(request.SessionPath);
        var session = ReadSession(path);
        var files = new List<string>();
        if (request.CaptureSnapshot)
        {
            var id = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
            files.Add(UvcSessionFiles.WriteJson(path, $"annotation-{id}-process-state.json", _environment.CaptureProcesses()));
            files.Add(UvcSessionFiles.WriteJson(path, $"annotation-{id}-uvc-inventory.json", _environment.CaptureUvcInventory()));
        }
        var annotation = new UvcIsolationAnnotation(
            UvcIsolationSchemas.Annotation,
            $"annotation-{Guid.NewGuid():N}",
            session.SessionId,
            DateTimeOffset.UtcNow,
            request.ObservationTimeUtc ?? DateTimeOffset.UtcNow,
            request.Source,
            request.Observation,
            request.Notes,
            request.CaptureSnapshot,
            files.ToArray());
        File.AppendAllText(Path.Combine(path, "annotations.jsonl"), JsonSerializer.Serialize(annotation, UvcIsolationJson.Options) + Environment.NewLine, new UTF8Encoding(false));
        UvcSessionFiles.WriteSha256Sums(path);
        return annotation;
    }

    public UvcIsolationFinishResult Finish(UvcIsolationFinishRequest request)
    {
        var path = Path.GetFullPath(request.SessionPath);
        var session = ReadSession(path);
        var endSystem = _environment.CaptureSystemState();
        var endProcesses = _environment.CaptureProcesses();
        var endUvc = _environment.CaptureUvcInventory();
        var finishedAt = DateTimeOffset.UtcNow;
        var windows = _environment.CaptureWindowsEvents(session.CreatedAtUtc, finishedAt);
        var crash = CaptureCrashMetadata(request.DumpPath, request.WindbgReportPath);
        var bootChanged = !string.Equals(session.BootIdentity.Fingerprint, endSystem.BootIdentity.Fingerprint, StringComparison.Ordinal);
        var duration = Math.Max(0, (finishedAt - session.CreatedAtUtc).TotalSeconds);
        var warnings = new List<string>();
        warnings.AddRange(endSystem.Warnings);
        warnings.AddRange(endProcesses.Warnings);
        warnings.AddRange(endUvc.Warnings);
        warnings.AddRange(endUvc.Errors);
        warnings.AddRange(windows.Warnings);
        warnings.AddRange(crash.Warnings);
        var final = new UvcSessionFinalRecord(
            UvcIsolationSchemas.Session,
            session.SessionId,
            session.Label,
            session.ScenarioTags,
            session.CreatedAtUtc,
            finishedAt,
            duration,
            bootChanged,
            request.Result,
            request.Notes,
            crash,
            warnings.Distinct().ToArray());
        var files = new List<string>
        {
            UvcSessionFiles.WriteJson(path, "end-system-state.json", endSystem),
            UvcSessionFiles.WriteJson(path, "end-process-state.json", endProcesses),
            UvcSessionFiles.WriteJson(path, "end-uvc-inventory.json", endUvc),
            UvcSessionFiles.WriteJson(path, "windows-event-summary.json", windows),
            UvcSessionFiles.WriteJson(path, "crash-metadata.json", crash),
            UvcSessionFiles.WriteJson(path, "session-final.json", final)
        };
        files.Add(UvcSessionFiles.WriteSha256Sums(path));
        return new(UvcIsolationSchemas.FinishResult, session.SessionId, path, request.Result, bootChanged, duration, files.ToArray(), crash, warnings.Distinct().ToArray());
    }

    private static UvcIsolationSessionManifest ReadSession(string path)
        => JsonSerializer.Deserialize<UvcIsolationSessionManifest>(
               File.ReadAllText(Path.Combine(path, "session.json")),
               UvcIsolationJson.Options)
           ?? throw new InvalidDataException("session.json is not a valid UVC isolation session.");

    private static UvcCrashMetadata CaptureCrashMetadata(string? dumpPath, string? windbgReportPath)
    {
        var warnings = new List<string>();
        UvcDumpMetadata? dump = null;
        UvcWindbgParseResult? windbg = null;
        if (!string.IsNullOrWhiteSpace(dumpPath)) dump = UvcCrashEvidence.ReadDumpMetadata(dumpPath);
        if (!string.IsNullOrWhiteSpace(windbgReportPath))
        {
            try { windbg = UvcWindbgTextParser.Parse(File.ReadAllText(windbgReportPath)); }
            catch (Exception ex) { warnings.Add($"WinDbg report unavailable: {ex.GetType().Name}"); }
        }
        if (dump?.Error is not null) warnings.Add(dump.Error);
        if (windbg is not null) warnings.AddRange(windbg.Warnings);
        var matches = string.Equals(windbg?.FailureBucketId, "AV_usbvideo!CaptureProcessDataPayload", StringComparison.OrdinalIgnoreCase)
            || (windbg?.SymbolName?.StartsWith("usbvideo!CaptureProcessDataPayload", StringComparison.OrdinalIgnoreCase) ?? false);
        return new("uvc-isolation-crash-metadata-v1", DateTimeOffset.UtcNow, dump, windbg, matches, warnings.Distinct().ToArray());
    }
}

internal sealed class UvcIsolationAnalyzer
{
    public UvcIsolationAnalysisResult Analyze(UvcIsolationAnalysisRequest request)
    {
        var warnings = new List<string>();
        var rows = request.SessionPaths.Select(path => ReadRow(Path.GetFullPath(path), warnings)).ToArray();
        var groups = BuildGroups(rows);
        var findings = BuildFindings(rows);
        var result = new UvcIsolationAnalysisResult(UvcIsolationSchemas.Analysis, DateTimeOffset.UtcNow, rows, groups, findings, RecommendNext(rows), warnings.Distinct().ToArray());
        if (!string.IsNullOrWhiteSpace(request.OutputPath))
        {
            var output = Path.GetFullPath(request.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(result, UvcIsolationJson.Options), new UTF8Encoding(false));
        }
        if (!string.IsNullOrWhiteSpace(request.MarkdownOutputPath))
        {
            var output = Path.GetFullPath(request.MarkdownOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, UvcIsolationMarkdownReport.Render(result), new UTF8Encoding(false));
        }
        return result;
    }

    private static UvcAnalysisSessionRow ReadRow(string path, List<string> warnings)
    {
        var session = ReadJson<UvcIsolationSessionManifest>(Path.Combine(path, "session.json"));
        var final = TryReadJson<UvcSessionFinalRecord>(Path.Combine(path, "session-final.json"));
        var startProcess = TryReadJson<UvcProcessSnapshot>(Path.Combine(path, "start-process-state.json"));
        var startUvc = TryReadJson<UvcInventorySnapshot>(Path.Combine(path, "start-uvc-inventory.json"));
        var crash = final?.CrashMetadata ?? TryReadJson<UvcCrashMetadata>(Path.Combine(path, "crash-metadata.json"));
        var result = final?.Result ?? "unknown";
        var bucket = crash?.Windbg?.FailureBucketId;
        return new(
            session.SessionId,
            session.Label,
            session.CreatedAtUtc,
            session.ScenarioTags,
            session.BootIdentity.Fingerprint,
            session.OperatorPhysicalState.SleepOccurred,
            session.OperatorPhysicalState.VivePhysicallyConnected,
            session.OperatorPhysicalState.ViveDisconnectedBeforeSleep,
            session.OperatorPhysicalState.ViveReconnectedAfterWake,
            session.OperatorPhysicalState.SamePhysicalUsbPortUsed,
            ProcessState(startProcess, "VRCFaceTracking"),
            ProcessState(startProcess, "SteamVR"),
            ProcessState(startProcess, "VRChat"),
            session.SupervisorConfiguration.DetectViveFaceTrackerUsage,
            session.SupervisorConfiguration.RestartOnFaceTrackerReconnect,
            session.SupervisorConfiguration.RestartOnHeadsetReconnect,
            startUvc?.Devices.Select(device => $"{device.Classification}:{device.Vid ?? "????"}:{device.Pid ?? "????"}").Distinct().Order().ToArray() ?? [],
            final?.DurationSeconds,
            result,
            crash?.Windbg?.BugcheckCode,
            bucket,
            crash?.Dump?.Sha256);
    }

    private static UvcComparisonGroup[] BuildGroups(UvcAnalysisSessionRow[] rows)
    {
        var items = new List<(string Dimension, string Value, UvcAnalysisSessionRow Row)>
        ();
        foreach (var row in rows)
        {
            items.Add(("supervisorTrackerAutomation", Bool(row.FaceTrackerReconnectRestart), row));
            items.Add(("headsetReconnectRestart", Bool(row.HeadsetReconnectRestart), row));
            items.Add(("viveConnected", row.VivePhysicallyConnected, row));
            items.Add(("vrcFaceTracking", row.VrcFaceTrackingState, row));
            items.Add(("sleepWake", row.SleepWake, row));
            items.Add(("pimaxOnlyVsPimaxVive", row.UvcDeviceSet.Any(device => device.StartsWith("viveOrHtc", StringComparison.OrdinalIgnoreCase)) ? "pimaxPlusVive" : "pimaxOnlyOrUnknown", row));
            items.Add(("result", IsCrash(row) ? "crash" : "stableOrNonCrash", row));
            if (!string.IsNullOrWhiteSpace(row.FailureBucket)) items.Add(("failureBucket", row.FailureBucket!, row));
        }
        return items.GroupBy(item => (item.Dimension, item.Value))
            .OrderBy(group => group.Key.Dimension).ThenBy(group => group.Key.Value)
            .Select(group => new UvcComparisonGroup(
                group.Key.Dimension,
                group.Key.Value,
                group.Count(item => !IsCrash(item.Row)),
                group.Count(item => IsCrash(item.Row)),
                group.Select(item => item.Row.SessionId).Distinct().Order().ToArray()))
            .ToArray();
    }

    private static UvcFinding[] BuildFindings(UvcAnalysisSessionRow[] rows)
    {
        var findings = new List<UvcFinding>();
        var crashRows = rows.Where(IsCrash).ToArray();
        foreach (var bucket in crashRows.Where(row => !string.IsNullOrWhiteSpace(row.FailureBucket)).GroupBy(row => row.FailureBucket))
        {
            findings.Add(new("repeatedFailureBucket", bucket.Count() > 1 ? "repeated association" : "observed association", $"{bucket.Count()} crashed session(s) reported {bucket.Key}.", "Repeated buckets identify recurrence, not root cause."));
        }
        findings.Add(ConditionFinding("vivePresence", crashRows, row => row.VivePhysicallyConnected == "yes", "crashes require Vive presence"));
        findings.Add(ConditionFinding("vrcFaceTrackingPresence", crashRows, row => row.VrcFaceTrackingState == "present", "crashes require VRCFaceTracking"));
        findings.Add(ConditionFinding("supervisorReconnectAutomation", crashRows, row => row.FaceTrackerReconnectRestart == true, "crashes require Supervisor reconnect automation"));
        findings.Add(ConditionFinding("sleepWake", crashRows, row => row.SleepWake == "yes", "crashes follow sleep/wake"));
        findings.Add(ConditionFinding("viveReconnect", crashRows, row => row.ViveReconnectedAfterWake == "yes", "crashes follow physical Vive reconnect"));
        findings.Add(ConditionFinding("cleanReboot", crashRows, row => row.ScenarioTags.Contains("rebootControl", StringComparer.OrdinalIgnoreCase), "crashes occur after clean reboot"));
        if (rows.Length < 3) findings.Add(new("sampleSize", "insufficient evidence", "Fewer than three sessions are available.", "No causal proof can be drawn from a small isolation matrix."));
        if (rows.GroupBy(row => string.Join("|", row.ScenarioTags.Order()) + "|" + row.VivePhysicallyConnected + "|" + row.VrcFaceTrackingState).Any(group => group.Any(IsCrash) && group.Any(row => !IsCrash(row))))
            findings.Add(new("conflicts", "contradictory evidence", "At least one comparable condition has both stable and crash outcomes.", "Compare exact timing, sleep/wake, and UVC inventory before interpreting."));
        return findings.ToArray();
    }

    private static UvcFinding ConditionFinding(string topic, UvcAnalysisSessionRow[] crashes, Func<UvcAnalysisSessionRow, bool> predicate, string evidence)
    {
        if (crashes.Length == 0) return new(topic, "insufficient evidence", "No crashed sessions are available.", "Stable sessions alone cannot prove the condition is safe.");
        if (crashes.All(predicate)) return new(topic, "possibly required", $"All crashed sessions currently satisfy: {evidence}.", "This is an isolation-matrix association, not causal proof.");
        return new(topic, "condition not required", $"At least one crashed session does not satisfy: {evidence}.", "Missing or unknown operator observations can weaken this label.");
    }

    private static string RecommendNext(UvcAnalysisSessionRow[] rows)
    {
        var scenarios = rows.SelectMany(row => row.ScenarioTags).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!scenarios.Contains("sleepWakeReconnect")) return "sleepWakeReconnect: Vive disconnected before sleep, VRCFaceTracking running normally, reconnect Vive to the same port after wake, observe for 30-60 minutes.";
        if (!rows.Any(row => row.VivePhysicallyConnected == "no")) return "viveDisconnected: Vive disconnected, VRCFaceTracking not running.";
        if (!rows.Any(row => row.VivePhysicallyConnected == "yes" && row.VrcFaceTrackingState != "present")) return "viveConnectedVrcftStopped: Vive connected, VRCFaceTracking not running.";
        if (!scenarios.Contains("rebootControl")) return "rebootControl: clean reboot control without sleep/wake.";
        return "viveConnectedVrcftRunning: Vive connected, VRCFaceTracking running.";
    }

    private static T ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), UvcIsolationJson.Options) ?? throw new InvalidDataException(path);
    private static T? TryReadJson<T>(string path) where T : class => File.Exists(path) ? ReadJson<T>(path) : null;
    private static string ProcessState(UvcProcessSnapshot? snapshot, string group) => snapshot?.Processes.FirstOrDefault(item => item.Group == group)?.Present == true ? "present" : "notPresent";
    private static string Bool(bool? value) => value is null ? "unknown" : value.Value ? "enabled" : "disabled";
    private static bool IsCrash(UvcAnalysisSessionRow row) => row.Result is "bugcheck" or "unexpectedReboot" or "systemFreeze" or "applicationCrash";
}

internal static partial class UvcWindbgTextParser
{
    public static UvcWindbgParseResult Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var bucket = Field(lines, "FAILURE_BUCKET_ID");
        var symbol = Field(lines, "SYMBOL_NAME");
        var module = Field(lines, "MODULE_NAME");
        var image = Field(lines, "IMAGE_NAME");
        var bug = Bugcheck(lines);
        return new(
            "uvc-windbg-text-parse-v1",
            bug.Code,
            bug.Name,
            Arguments(lines),
            Field(lines, "PROCESS_NAME"),
            module,
            image,
            symbol,
            bucket,
            Field(lines, "FAILURE_ID_HASH") ?? Field(lines, "FAILURE_HASH"),
            Stack(lines),
            Field(lines, "DEBUG_FLR_IMAGE_TIMESTAMP") ?? Field(lines, "DUMP_TIME"),
            Field(lines, "DUMP_FILE") ?? Field(lines, "Dump.Name"),
            bucket is null && symbol is null && bug.Code is null ? ["No recognized WinDbg crash fields were found."] : []);
    }

    private static string? Field(string[] lines, string name)
    {
        var prefix = name + ":";
        return lines.Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is { } line
            ? line[prefix.Length..].Trim()
            : null;
    }

    private static (string? Code, string? Name) Bugcheck(string[] lines)
    {
        foreach (var line in lines.Select(item => item.Trim()))
        {
            var match = Regex.Match(line, @"^([A-Z0-9_]+)\s+\(([0-9a-fA-Fx]+)\)");
            if (match.Success) return ("0x" + match.Groups[2].Value.TrimStart('0', 'x', 'X').ToUpperInvariant(), match.Groups[1].Value);
            match = Regex.Match(line, @"BugCheck\s+([0-9a-fA-Fx]+)", RegexOptions.IgnoreCase);
            if (match.Success) return ("0x" + match.Groups[1].Value.TrimStart('0', 'x', 'X').ToUpperInvariant(), null);
        }
        return (null, null);
    }

    private static string[] Arguments(string[] lines)
        => lines.Select(line => Regex.Match(line.Trim(), @"^Arg\d+:\s*(.+)$", RegexOptions.IgnoreCase))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value.Trim())
            .Take(8)
            .ToArray();

    private static string[] Stack(string[] lines)
        => lines.Where(line => line.Contains('!') && !line.Contains("SYMBOL_NAME:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(32)
            .ToArray();
}

internal static class UvcCrashEvidence
{
    public static UvcDumpMetadata ReadDumpMetadata(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists) return new(full, UvcPrivacy.SanitizePath(full), "missing", null, null, null, "Dump path does not exist.");
            return new(full, UvcPrivacy.SanitizePath(full), "available", info.Length, info.LastWriteTimeUtc, UvcPrivacy.FileSha256(full), null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(full, UvcPrivacy.SanitizePath(full), "accessDenied", null, null, null, ex.GetType().Name);
        }
        catch (Exception ex)
        {
            return new(full, UvcPrivacy.SanitizePath(full), "unavailable", null, null, null, ex.GetType().Name);
        }
    }
}

internal static class UvcIsolationMarkdownReport
{
    public static string Render(UvcIsolationAnalysisResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# UVC isolation analysis");
        builder.AppendLine();
        builder.AppendLine("This report summarizes operator-invoked, offline UVC isolation sessions. Associations are not causal proof.");
        builder.AppendLine();
        builder.AppendLine("## Session matrix");
        builder.AppendLine("| Session | Scenario | Vive | VRCFT | Sleep/wake | Result | Bucket |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in result.Sessions)
            builder.AppendLine($"| {row.Label} | {string.Join(", ", row.ScenarioTags)} | {row.VivePhysicallyConnected} | {row.VrcFaceTrackingState} | {row.SleepWake} | {row.Result} | {row.FailureBucket ?? ""} |");
        builder.AppendLine();
        builder.AppendLine("## Findings");
        foreach (var finding in result.Findings) builder.AppendLine($"- **{finding.Label}**: {finding.Topic}. {finding.Evidence} {finding.Limitation}");
        builder.AppendLine();
        builder.AppendLine("## Next controlled test");
        builder.AppendLine(result.RecommendedNextTest);
        return builder.ToString();
    }
}

internal static class UvcSessionFiles
{
    public static string WriteJson<T>(string directory, string fileName, T value)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(value, UvcIsolationJson.Options), new UTF8Encoding(false));
        return fileName;
    }

    public static string WriteSha256Sums(string directory)
    {
        var lines = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => $"{UvcPrivacy.FileSha256(path)}  {Path.GetFileName(path)}")
            .ToArray();
        File.WriteAllLines(Path.Combine(directory, "SHA256SUMS.txt"), lines, new UTF8Encoding(false));
        return "SHA256SUMS.txt";
    }
}

internal static class UvcPrivacy
{
    public static string Hash(string value)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()))).ToLowerInvariant();

    public static string? HashOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Hash(value);

    public static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string SanitizePath(string path)
    {
        var value = path;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) value = value.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows)) value = value.Replace(windows, "%SystemRoot%", StringComparison.OrdinalIgnoreCase);
        return value;
    }
}

internal static class UvcTail
{
    public static string[] ReadTailLines(string path, int maxBytes)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var start = Math.Max(0, length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var lines = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return start == 0 ? lines : lines.Skip(1).ToArray();
    }
}

internal static class UvcGitIdentity
{
    public static string HeadCommit()
    {
        try
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
            if (directory is null) return "unknown";
            var head = File.ReadAllText(Path.Combine(directory.FullName, ".git", "HEAD")).Trim();
            if (!head.StartsWith("ref:", StringComparison.Ordinal)) return head;
            var refPath = Path.Combine(directory.FullName, ".git", head[5..].Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : "unknown";
        }
        catch { return "unknown"; }
    }
}

internal static class UvcIsolationArguments
{
    public static bool Has(string[] args, string name) => args.Any(arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string Required(string[] args, string name)
        => Value(args, name) ?? throw new ArgumentException($"{name} is required.");

    public static string? Value(string[] args, string name)
        => Values(args, name).LastOrDefault();

    public static IEnumerable<string> Values(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) yield return args[index][prefix.Length..];
            else if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) yield return args[++index];
        }
    }
}
