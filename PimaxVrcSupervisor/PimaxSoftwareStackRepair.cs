using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class PimaxRepairTargetsSchema
{
    public const string Version = "pimax-repair-targets-v1";
}

internal static class PimaxRepairStartSchema
{
    public const string Version = "pimax-repair-start-v1";
}

internal static class PimaxRepairStatusSchema
{
    public const string Version = "pimax-repair-status-v1";
}

internal static class PimaxRepairCancelSchema
{
    public const string Version = "pimax-repair-cancel-v1";
}

internal static class PimaxRepairResultSchema
{
    public const string Version = "pimax-repair-result-v1";
}

internal static class PimaxRepairTargetClassification
{
    public const string ApprovedRestartableService = "approvedRestartableService";
    public const string ApprovedRestartableProcess = "approvedRestartableProcess";
    public const string ObserveOnly = "observeOnly";
    public const string Prohibited = "prohibited";
}

internal static class PimaxSoftwareRepairOutcome
{
    public const string NoRepairNeeded = "noRepairNeeded";
    public const string Repaired = "repaired";
    public const string RepairedWithDegradedFeatures = "repairedWithDegradedFeatures";
    public const string SoftwareStackHealthyButNotRegistered = "softwareStackHealthyButNotRegistered";
    public const string CoreUsbMissing = "coreUsbMissing";
    public const string DisplayPathMissing = "displayPathMissing";
    public const string SoftwareRepairFailed = "softwareRepairFailed";
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timedOut";
    public const string UnsupportedAutomaticRecovery = "unsupportedAutomaticRecovery";
    public const string ConflictingEvidence = "conflictingEvidence";
    public const string Unknown = "unknown";
}

internal sealed record PimaxRepairTargetsSnapshot(
    string Schema,
    DateTimeOffset CollectedAt,
    PimaxRepairTarget[] Targets,
    string[] ApprovedRestartableProcesses,
    string[] ApprovedRestartableServices,
    string[] ObserveOnlyTargets,
    string[] ProhibitedTargets,
    string[] IntendedStopOrder,
    string[] IntendedStartOrder,
    string HumanReadableLimitations,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxRepairTarget(
    string TargetId,
    string DisplayName,
    string TargetType,
    string Classification,
    bool Approved,
    string? ExecutableName,
    string? SanitizedPath,
    string? ServiceName,
    string? ServiceType,
    string? CurrentState,
    bool SignerValid,
    string SignerSummary,
    bool PathValidated,
    bool LoadsOrHostsDriver,
    string[] Dependencies,
    string[] DependentServices,
    string[] CurrentProcessIds,
    string Reason,
    string IntendedOrder);

internal sealed record PimaxRepairStartRequest(
    string Mode,
    bool DryRun,
    bool Confirm,
    string? ConfirmationToken,
    int TimeoutSeconds);

internal sealed record PimaxRepairStartResponse(
    string Schema,
    bool Accepted,
    string OperationId,
    string Mode,
    string CurrentStage,
    string Classification,
    string[] ProposedActions,
    string[] MutatingActions,
    string[] Limitations,
    bool ConfirmationRequired,
    string? ConfirmationToken,
    DateTimeOffset? ConfirmationTokenExpiresAt,
    string ConflictResult,
    string HumanReadableSummary,
    PimaxRepairResultResponse? Result,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxRepairStatusResponse(
    string Schema,
    bool Active,
    string? OperationId,
    string Stage,
    string? CurrentAction,
    string[] CompletedActions,
    string[] Warnings,
    double ElapsedSeconds,
    bool CancellationAvailable,
    bool CancellationRequested,
    string HumanReadableSummary);

internal sealed record PimaxRepairCancelResponse(
    string Schema,
    bool Accepted,
    string? OperationId,
    string Stage,
    string Outcome,
    string HumanReadableSummary);

internal sealed record PimaxRepairResultResponse(
    string Schema,
    string OperationId,
    string CorrelationId,
    string Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Outcome,
    string Stage,
    PimaxRepairHealthSummary PreHealth,
    PimaxRepairHealthSummary? PostHealth,
    PimaxRepairComponentReport[] Components,
    PimaxRepairTarget[] Targets,
    PimaxRepairActionEvent[] Actions,
    string[] BlockingIssues,
    string[] DegradedFeatures,
    string HumanReadableSummary,
    string? RequiredOperatorAction,
    bool CancellationRequested,
    bool TimedOut,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxRepairHealthSummary(
    string OverallStatus,
    string RegistrationState,
    string RegistrationConfidence,
    string EvidenceConfidence,
    string HumanReadableSummary);

internal sealed record PimaxRepairComponentReport(
    string ComponentId,
    string DisplayName,
    string Status,
    string Criticality,
    string Explanation);

internal sealed record PimaxRepairActionEvent(
    string ActionId,
    string Stage,
    string TargetId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationMs,
    bool Attempted,
    bool Success,
    bool Mutating,
    bool TimedOut,
    bool CancellationRequested,
    string Message);

internal sealed record PimaxRepairOperationLogEntry(
    string Schema,
    string OperationId,
    string CorrelationId,
    string BuildIdentity,
    DateTimeOffset Timestamp,
    string Stage,
    string? Action,
    string? TargetId,
    string Result,
    double DurationMs,
    bool Timeout,
    bool Cancellation,
    string? ExceptionType,
    string? Error,
    PimaxRepairHealthSummary? PreHealth,
    PimaxRepairHealthSummary? PostHealth,
    string? FinalOutcome,
    string[] Warnings);

internal interface IPimaxRepairTargetCatalog
{
    Task<PimaxRepairTargetsSnapshot> DiscoverAsync(CancellationToken cancellationToken);
}

internal interface IPimaxRepairHealthCollector
{
    Task<PimaxComponentHealthSnapshot> CollectAsync(CancellationToken cancellationToken);
}

internal interface IPimaxRepairProcessController
{
    Task<PimaxRecoveryOperationResult> RequestGracefulCloseAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken);
    Task<PimaxRecoveryOperationResult> RelaunchAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken);
}

internal interface IPimaxRepairServiceController
{
    Task<PimaxRecoveryOperationResult> StopAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken);
    Task<PimaxRecoveryOperationResult> StartAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken);
}

internal interface IPimaxRepairDiagnosticsWriter
{
    void Append(PimaxRepairOperationLogEntry entry);
}

internal sealed class DefaultPimaxRepairHealthCollector(SupervisorConfig config) : IPimaxRepairHealthCollector
{
    public Task<PimaxComponentHealthSnapshot> CollectAsync(CancellationToken cancellationToken)
        => new PimaxComponentHealthCoordinator().CollectAsync(config, cancellationToken);
}

internal sealed class PimaxRepairTargetCatalog(IPimaxClientProcessController clientController) : IPimaxRepairTargetCatalog
{
    public PimaxRepairTargetCatalog()
        : this(new WindowsPimaxClientProcessController())
    {
    }

    public async Task<PimaxRepairTargetsSnapshot> DiscoverAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var warnings = new List<string>();
        var errors = new List<string>();
        var targets = new List<PimaxRepairTarget>();

        var discovery = await clientController.DiscoverAsync(cancellationToken);
        warnings.AddRange(discovery.Warnings);
        errors.AddRange(discovery.Errors);

        foreach (var group in discovery.Processes.GroupBy(process => process.ExecutablePath ?? process.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var exactTarget = discovery.Target is not null
                && !string.IsNullOrWhiteSpace(first.ExecutablePath)
                && string.Equals(discovery.Target.ExecutablePath, first.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            var approved = exactTarget
                && group.Any(process => process.HasMainWindow)
                && string.Equals(discovery.Target!.ProductName, "PimaxClient", StringComparison.OrdinalIgnoreCase)
                && string.Equals(discovery.Target.CompanyName, "Pimax", StringComparison.OrdinalIgnoreCase);
            var classification = approved
                ? PimaxRepairTargetClassification.ApprovedRestartableProcess
                : IsProhibitedProcess(first.ProcessName, first.ExecutablePath)
                    ? PimaxRepairTargetClassification.Prohibited
                    : PimaxRepairTargetClassification.ObserveOnly;
            targets.Add(new PimaxRepairTarget(
                TargetId("process", first.ProcessName, first.ExecutablePath),
                first.ProcessName,
                "process",
                classification,
                approved,
                first.ProcessName,
                SanitizePath(first.ExecutablePath),
                null,
                null,
                group.Any(process => process.ProcessId > 0) ? "running" : "notRunning",
                approved,
                approved ? "Pimax client file identity and shortcut target validated." : "Signer or launch semantics not approved by this phase.",
                !string.IsNullOrWhiteSpace(first.ExecutablePath),
                false,
                [],
                [],
                group.Select(process => process.ProcessId.ToString()).Where(value => value != "0").ToArray(),
                approved
                    ? "Exact top-level Pimax Play client target validated for graceful close and exact relaunch."
                    : ClassificationReason(first.ProcessName, first.ExecutablePath),
                approved ? "stop before services; start after services if it was running before repair" : "observe only"));
        }

        targets.AddRange(DiscoverAdditionalPimaxProcesses(targets));
        targets.AddRange(await DiscoverServicesAsync(cancellationToken));

        return new PimaxRepairTargetsSnapshot(
            PimaxRepairTargetsSchema.Version,
            now,
            targets.OrderBy(target => target.TargetType, StringComparer.OrdinalIgnoreCase).ThenBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray(),
            targets.Where(target => target.Classification == PimaxRepairTargetClassification.ApprovedRestartableProcess).Select(target => target.TargetId).ToArray(),
            targets.Where(target => target.Classification == PimaxRepairTargetClassification.ApprovedRestartableService).Select(target => target.TargetId).ToArray(),
            targets.Where(target => target.Classification == PimaxRepairTargetClassification.ObserveOnly).Select(target => target.TargetId).ToArray(),
            targets.Where(target => target.Classification == PimaxRepairTargetClassification.Prohibited).Select(target => target.TargetId).ToArray(),
            targets.Where(target => target.Approved && target.TargetType == "process").Select(target => target.TargetId).ToArray(),
            targets.Where(target => target.Approved && target.TargetType == "service").Select(target => target.TargetId).Concat(targets.Where(target => target.Approved && target.TargetType == "process").Select(target => target.TargetId)).ToArray(),
            "Only exact validated Pimax Play user-mode client processes may be restarted. Pimax services are observe-only unless a future phase proves exact safe dependency and restart semantics.",
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static PimaxRepairTarget ApprovedProcessForTests(string id = "process:pimaxclient")
        => new(id, "PimaxClient.exe", "process", PimaxRepairTargetClassification.ApprovedRestartableProcess, true, "PimaxClient.exe", @"<pimax>\PimaxClient\pimaxui\PimaxClient.exe", null, null, "running", true, "valid", true, false, [], [], ["101"], "test approved", "stop before services; start after services");

    internal static PimaxRepairTarget ApprovedServiceForTests(string id = "service:test")
        => new(id, "Pimax test service", "service", PimaxRepairTargetClassification.ApprovedRestartableService, true, null, @"<pimax>\Runtime\TestService.exe", "PimaxTestService", "Own Process", "Running", true, "valid", true, false, [], [], [], "test approved", "dependency order");

    private static async Task<IEnumerable<PimaxRepairTarget>> DiscoverServicesAsync(CancellationToken cancellationToken)
    {
        PimaxServiceObservation services;
        try
        {
            services = await PimaxServiceProbe.CollectAsync(new PimaxProcessRunner(), TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch
        {
            return [];
        }

        var result = new List<PimaxRepairTarget>();
        foreach (var service in services.Services)
        {
            string name = service.Name;
            string displayName = service.DisplayName ?? service.Name;
            string path = service.BinaryPath ?? "";
            string serviceType = "unknown";
            var driver = false;
            var prohibited = driver || name.Contains("USB", StringComparison.OrdinalIgnoreCase) || name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
            result.Add(new PimaxRepairTarget(
                TargetId("service", name, path),
                string.IsNullOrWhiteSpace(displayName) ? name : displayName,
                "service",
                prohibited ? PimaxRepairTargetClassification.Prohibited : PimaxRepairTargetClassification.ObserveOnly,
                false,
                null,
                SanitizePath(ExecutableFromServicePath(path)),
                name,
                serviceType,
                service.State,
                false,
                "Service restart safety is not approved in Phase 28D2-B.",
                !string.IsNullOrWhiteSpace(ExecutableFromServicePath(path)),
                driver,
                [],
                [],
                [],
                prohibited ? "Service is prohibited because it is a driver or protected system class." : "Service is observe-only because safe dependency and restart semantics are not proven.",
                "observe only"));
        }

        return result;
    }

    private static IEnumerable<PimaxRepairTarget> DiscoverAdditionalPimaxProcesses(IReadOnlyCollection<PimaxRepairTarget> existing)
    {
        var result = new List<PimaxRepairTarget>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string processName;
                string? path;
                try
                {
                    processName = process.ProcessName;
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    continue;
                }

                var sanitized = SanitizePath(path);
                if (string.IsNullOrWhiteSpace(path)
                    || !path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase)
                    || existing.Any(target => string.Equals(target.ExecutableName, processName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(target.SanitizedPath, sanitized, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var prohibited = IsProhibitedProcess(processName, path);
                result.Add(new PimaxRepairTarget(
                    TargetId("process", processName, path),
                    processName + ".exe",
                    "process",
                    prohibited ? PimaxRepairTargetClassification.Prohibited : PimaxRepairTargetClassification.ObserveOnly,
                    false,
                    processName + ".exe",
                    sanitized,
                    null,
                    null,
                    process.HasExited ? "notRunning" : "running",
                    false,
                    "Signer and restart semantics are not approved for this target.",
                    true,
                    false,
                    [],
                    [],
                    [process.Id.ToString()],
                    prohibited ? ClassificationReason(processName, path) : "Additional Pimax-root process is useful diagnostically but not approved for restart.",
                    "observe only"));
            }
        }

        return result;
    }

    private static bool IsProhibitedProcess(string processName, string? path)
    {
        var value = $"{processName}|{path}";
        return value.Contains("Supervisor", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SteamVR", StringComparison.OrdinalIgnoreCase)
            || value.Contains("VRChat", StringComparison.OrdinalIgnoreCase)
            || value.Contains("VRCFaceTracking", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PVRHome", StringComparison.OrdinalIgnoreCase)
            || value.Contains("UnityCrashHandler", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassificationReason(string processName, string? path)
        => IsProhibitedProcess(processName, path)
            ? "Target is prohibited because it is Supervisor, SteamVR, VRChat, VRCFT, crash handler, home app, or unrelated runtime."
            : "Target is observe-only because exact safe restart semantics are not proven.";

    private static string TargetId(string kind, string name, string? path)
    {
        var basis = $"{kind}|{name}|{path ?? ""}".ToLowerInvariant();
        return $"{kind}:{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(basis)))[..16].ToLowerInvariant()}";
    }

    private static string? SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var pimaxRoot = @"C:\Program Files\Pimax";
        return path.StartsWith(pimaxRoot, StringComparison.OrdinalIgnoreCase)
            ? "<pimax>" + path[pimaxRoot.Length..]
            : PimaxConnectivityRedactor.SanitizePath(path);
    }

    private static string? ExecutableFromServicePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.Trim();
        if (trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.IndexOf('"', 1) is var quote && quote > 1)
        {
            return trimmed[1..quote];
        }

        var exe = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? trimmed[..(exe + 4)] : trimmed;
    }

    private static string SafeString(object? value) => value?.ToString() ?? "";
}

internal sealed class PimaxClientRepairProcessController(IPimaxClientProcessController clientController) : IPimaxRepairProcessController
{
    public PimaxClientRepairProcessController()
        : this(new WindowsPimaxClientProcessController())
    {
    }

    public async Task<PimaxRecoveryOperationResult> RequestGracefulCloseAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var discovery = await clientController.DiscoverAsync(cancellationToken);
        return discovery.Target is null
            ? new PimaxRecoveryOperationResult(true, false, "Approved Pimax Play client target was not discoverable at execution time.", [])
            : await clientController.RequestGracefulCloseAsync(discovery.Target, timeout, cancellationToken);
    }

    public async Task<PimaxRecoveryOperationResult> RelaunchAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var discovery = await clientController.DiscoverAsync(cancellationToken);
        return discovery.Target is null
            ? new PimaxRecoveryOperationResult(true, false, "Approved Pimax Play client relaunch target was not discoverable at execution time.", [])
            : await clientController.RelaunchAsync(discovery.Target, timeout, cancellationToken);
    }
}

internal sealed class NoApprovedWindowsServiceRepairController : IPimaxRepairServiceController
{
    public Task<PimaxRecoveryOperationResult> StopAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult(new PimaxRecoveryOperationResult(true, false, "No Windows service is approved for restart in this phase.", []));

    public Task<PimaxRecoveryOperationResult> StartAsync(PimaxRepairTarget target, TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult(new PimaxRecoveryOperationResult(true, false, "No Windows service is approved for restart in this phase.", []));
}

internal sealed class PimaxRepairDiagnosticsWriter : IPimaxRepairDiagnosticsWriter
{
    public const string Schema = "pimax-repair-operation-v1";
    private const long MaxBytes = 1024 * 1024;
    private const int MaxArchives = 4;
    private readonly string _path;

    public PimaxRepairDiagnosticsWriter(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PimaxVrcSupervisor", "Diagnostics", "PimaxRepair");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "pimax-repair-operations.jsonl");
    }

    public void Append(PimaxRepairOperationLogEntry entry)
    {
        try
        {
            RotateIfNeeded();
            File.AppendAllText(_path, JsonSerializer.Serialize(entry, PimaxRepairJson.Options) + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void RotateIfNeeded()
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length < MaxBytes)
        {
            return;
        }

        for (var index = MaxArchives - 1; index >= 1; index--)
        {
            var from = $"{_path}.{index}";
            var to = $"{_path}.{index + 1}";
            if (File.Exists(to)) File.Delete(to);
            if (File.Exists(from)) File.Move(from, to);
        }

        File.Move(_path, $"{_path}.1", overwrite: true);
    }
}

internal sealed class PimaxSoftwareStackRepairBackend
{
    public const string ModeSoftwareStackOnly = "software-stack-only";
    private static readonly ConcurrentDictionary<string, PimaxSoftwareStackRepairBackend> BackendByScope = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> ActiveScopes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> ActiveCancellation = new(StringComparer.OrdinalIgnoreCase);

    internal static readonly TimeSpan ConfirmationTokenLifetime = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan ProcessStopTimeout = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan ProcessStartTimeout = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan PassiveSettle = TimeSpan.FromSeconds(10);

    private readonly string _scope;
    private readonly IPimaxRepairHealthCollector _healthCollector;
    private readonly IPimaxRepairTargetCatalog _targetCatalog;
    private readonly IPimaxRepairProcessController _processController;
    private readonly IPimaxRepairServiceController _serviceController;
    private readonly IPimaxRepairDiagnosticsWriter _diagnostics;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _passiveSettle;
    private PimaxRepairResultResponse? _lastResult;
    private PimaxRepairStatusResponse? _lastStatus;

    public PimaxSoftwareStackRepairBackend(
        string scope,
        IPimaxRepairHealthCollector healthCollector,
        IPimaxRepairTargetCatalog targetCatalog,
        IPimaxRepairProcessController processController,
        IPimaxRepairServiceController serviceController,
        IPimaxRepairDiagnosticsWriter diagnostics,
        Func<DateTimeOffset>? now = null,
        TimeSpan? passiveSettle = null)
    {
        _scope = scope;
        _healthCollector = healthCollector;
        _targetCatalog = targetCatalog;
        _processController = processController;
        _serviceController = serviceController;
        _diagnostics = diagnostics;
        _now = now ?? (() => DateTimeOffset.Now);
        _passiveSettle = passiveSettle ?? PassiveSettle;
    }

    public static PimaxSoftwareStackRepairBackend ForConfig(SupervisorConfig config)
        => BackendByScope.GetOrAdd(
            config.LoadedFromPath ?? AppContext.BaseDirectory,
            scope => new PimaxSoftwareStackRepairBackend(
                scope,
                new DefaultPimaxRepairHealthCollector(config),
                new PimaxRepairTargetCatalog(),
                new PimaxClientRepairProcessController(),
                new NoApprovedWindowsServiceRepairController(),
                new PimaxRepairDiagnosticsWriter()));

    public Task<PimaxRepairTargetsSnapshot> DiscoverTargetsAsync(CancellationToken cancellationToken)
        => _targetCatalog.DiscoverAsync(cancellationToken);

    public async Task<PimaxRepairStartResponse> StartAsync(PimaxRepairStartRequest request, CancellationToken externalCancellationToken)
    {
        var started = _now();
        var operationId = $"pimax-repair-{Guid.NewGuid():N}";
        var correlationId = $"pimax-repair-correlation-{Guid.NewGuid():N}";
        var warnings = new List<string>();
        var errors = new List<string>();
        var actions = new List<PimaxRepairActionEvent>();
        var completed = new List<string>();
        var stage = PimaxRepairStage.Created;

        if (!string.Equals(request.Mode, ModeSoftwareStackOnly, StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(operationId, request, "unknown", "Unsupported mode. Supported mode: software-stack-only.", ["Unsupported repair mode."]);
        }

        if (!ActiveScopes.TryAdd(_scope, 0))
        {
            return Rejected(operationId, request, "unknown", "Another Pimax repair operation is already active.", ["Duplicate repair rejected by backend operation lock."]);
        }

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        ActiveCancellation[_scope] = operationCancellation;
        var token = operationCancellation.Token;

        try
        {
            UpdateStatus(operationId, stage, null, completed, started, cancellationAvailable: true, cancellationRequested: false, "Repair operation created.");
            stage = PimaxRepairStage.CapturingPreHealth;
            var preHealth = await _healthCollector.CollectAsync(token);
            var classification = PimaxRepairPlanner.Classify(preHealth);
            stage = PimaxRepairStage.BuildingPlan;
            var targets = await _targetCatalog.DiscoverAsync(token);
            warnings.AddRange(targets.Warnings);
            errors.AddRange(targets.Errors);

            if (classification == PimaxRepairClassification.AlreadyHealthy)
            {
                var result = BuildResult(operationId, correlationId, request.Mode, started, _now(), PimaxSoftwareRepairOutcome.NoRepairNeeded, PimaxRepairStage.Completed, preHealth, preHealth, targets, actions, false, false, warnings, errors);
                _lastResult = result;
                Log(operationId, correlationId, PimaxRepairStage.Completed, null, null, "noRepairNeeded", 0, false, false, null, null, HealthSummary(preHealth), HealthSummary(preHealth), result.Outcome, warnings);
                return Accepted(operationId, request, classification, [], [], false, null, null, "none", result.HumanReadableSummary, result, warnings, errors);
            }

            if (classification is PimaxRepairClassification.ConflictingEvidence or PimaxRepairClassification.Unknown)
            {
                var outcome = classification == PimaxRepairClassification.ConflictingEvidence ? PimaxSoftwareRepairOutcome.ConflictingEvidence : PimaxSoftwareRepairOutcome.Unknown;
                var result = BuildResult(operationId, correlationId, request.Mode, started, _now(), outcome, PimaxRepairStage.Completed, preHealth, preHealth, targets, actions, false, false, warnings, errors);
                _lastResult = result;
                return Accepted(operationId, request, classification, [], [], false, null, null, "none", result.HumanReadableSummary, result, warnings, errors);
            }

            var approvedProcesses = targets.Targets.Where(target => target.Classification == PimaxRepairTargetClassification.ApprovedRestartableProcess).ToArray();
            var approvedServices = targets.Targets.Where(target => target.Classification == PimaxRepairTargetClassification.ApprovedRestartableService).ToArray();
            var mutatingActions = approvedProcesses.Select(target => $"restart-process:{target.TargetId}").Concat(approvedServices.Select(target => $"restart-service:{target.TargetId}")).ToArray();
            if (mutatingActions.Length == 0)
            {
                var result = BuildResult(operationId, correlationId, request.Mode, started, _now(), PimaxSoftwareRepairOutcome.UnsupportedAutomaticRecovery, PimaxRepairStage.Completed, preHealth, preHealth, targets, actions, false, false, warnings, errors);
                _lastResult = result;
                return Accepted(operationId, request, classification, [], [], false, null, null, "none", result.HumanReadableSummary, result, warnings, errors);
            }

            var targetForToken = new PimaxClientTargetDescriptor(
                approvedProcesses.FirstOrDefault()?.SanitizedPath ?? "approved-service-only",
                "",
                "",
                "PimaxSoftwareStackRepair",
                "Pimax",
                "Phase28D2B",
                string.Join("|", mutatingActions),
                [],
                "pimax-repair-start-json",
                "SoftwareStackRepair");
            var confirmationExpires = _now().Add(ConfirmationTokenLifetime);
            var generatedToken = PimaxRecoveryConfirmationToken.Create(request.Mode, targetForToken, preHealth.RegistrationAssessment.State, confirmationExpires, _now);

            if (request.DryRun || !request.Confirm)
            {
                var summary = "Dry run only. No Pimax process or service was restarted.";
                return Accepted(operationId, request, classification, mutatingActions, mutatingActions, true, generatedToken, confirmationExpires, "none", summary, null, warnings, errors);
            }

            var confirmation = PimaxRecoveryConfirmationToken.Validate(request.ConfirmationToken, request.Mode, targetForToken, preHealth.RegistrationAssessment.State, _now);
            if (!confirmation.Accepted)
            {
                return Rejected(operationId, request, classification, confirmation.RejectionReason ?? "Confirmation rejected.", ["Confirmation token was rejected."]);
            }

            stage = PimaxRepairStage.ExecutingSoftwareActions;
            foreach (var process in approvedProcesses)
            {
                token.ThrowIfCancellationRequested();
                var close = await ExecuteActionAsync("stopValidatedPimaxProcesses", stage, process, ProcessStopTimeout, actions, async ct => await _processController.RequestGracefulCloseAsync(process, ProcessStopTimeout, ct), token);
                completed.Add("stopValidatedPimaxProcesses");
                if (!close.Success)
                {
                    var postFailure = await SafePostHealthAsync(warnings, errors, token);
                    var failed = BuildResult(operationId, correlationId, request.Mode, started, _now(), PimaxSoftwareRepairOutcome.SoftwareRepairFailed, PimaxRepairStage.Failed, preHealth, postFailure, targets, actions, false, false, warnings, errors);
                    _lastResult = failed;
                    return Accepted(operationId, request, classification, mutatingActions, mutatingActions, false, null, null, "none", failed.HumanReadableSummary, failed, warnings, errors);
                }
            }

            foreach (var service in approvedServices)
            {
                token.ThrowIfCancellationRequested();
                var stop = await ExecuteActionAsync("restartValidatedPimaxServices.stop", stage, service, ServiceStopTimeout, actions, async ct => await _serviceController.StopAsync(service, ServiceStopTimeout, ct), token);
                completed.Add("restartValidatedPimaxServices.stop");
                if (!stop.Success)
                {
                    var postFailure = await SafePostHealthAsync(warnings, errors, token);
                    var failed = BuildResult(operationId, correlationId, request.Mode, started, _now(), PimaxSoftwareRepairOutcome.SoftwareRepairFailed, PimaxRepairStage.Failed, preHealth, postFailure, targets, actions, false, false, warnings, errors);
                    _lastResult = failed;
                    return Accepted(operationId, request, classification, mutatingActions, mutatingActions, false, null, null, "none", failed.HumanReadableSummary, failed, warnings, errors);
                }
            }

            foreach (var service in approvedServices.Reverse())
            {
                token.ThrowIfCancellationRequested();
                var start = await ExecuteActionAsync("restartValidatedPimaxServices.start", stage, service, ServiceStartTimeout, actions, async ct => await _serviceController.StartAsync(service, ServiceStartTimeout, ct), token);
                completed.Add("restartValidatedPimaxServices.start");
                if (!start.Success)
                {
                    var postFailure = await SafePostHealthAsync(warnings, errors, token);
                    var failed = BuildResult(operationId, correlationId, request.Mode, started, _now(), PimaxSoftwareRepairOutcome.SoftwareRepairFailed, PimaxRepairStage.Failed, preHealth, postFailure, targets, actions, false, false, warnings, errors);
                    _lastResult = failed;
                    return Accepted(operationId, request, classification, mutatingActions, mutatingActions, false, null, null, "none", failed.HumanReadableSummary, failed, warnings, errors);
                }
            }

            foreach (var process in approvedProcesses)
            {
                token.ThrowIfCancellationRequested();
                var start = await ExecuteActionAsync("startValidatedPimaxProcesses", stage, process, ProcessStartTimeout, actions, async ct => await _processController.RelaunchAsync(process, ProcessStartTimeout, ct), token);
                completed.Add("startValidatedPimaxProcesses");
                if (!start.Success)
                {
                    var postFailure = await SafePostHealthAsync(warnings, errors, token);
                    var failed = BuildResult(operationId, correlationId, request.Mode, started, _now(), PimaxSoftwareRepairOutcome.SoftwareRepairFailed, PimaxRepairStage.Failed, preHealth, postFailure, targets, actions, false, false, warnings, errors);
                    _lastResult = failed;
                    return Accepted(operationId, request, classification, mutatingActions, mutatingActions, false, null, null, "none", failed.HumanReadableSummary, failed, warnings, errors);
                }
            }

            stage = PimaxRepairStage.Settling;
            UpdateStatus(operationId, stage, "waitForSoftwareStack", completed, started, cancellationAvailable: true, cancellationRequested: token.IsCancellationRequested, "Passive settle interval.");
            await Task.Delay(_passiveSettle, token);
            stage = PimaxRepairStage.CapturingPostHealth;
            var postHealth = await _healthCollector.CollectAsync(token);
            if (postHealth.OverallStatus == PimaxHealthOverallStatus.Initializing)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                postHealth = await _healthCollector.CollectAsync(token);
            }

            var finalOutcome = DetermineOutcome(preHealth, postHealth, actions);
            var finalStage = finalOutcome == PimaxSoftwareRepairOutcome.SoftwareRepairFailed ? PimaxRepairStage.Failed : PimaxRepairStage.Completed;
            var resultFinal = BuildResult(operationId, correlationId, request.Mode, started, _now(), finalOutcome, finalStage, preHealth, postHealth, targets, actions, false, false, warnings, errors);
            _lastResult = resultFinal;
            Log(operationId, correlationId, finalStage, "reportResult", null, finalOutcome, 0, false, false, null, null, HealthSummary(preHealth), HealthSummary(postHealth), finalOutcome, warnings);
            return Accepted(operationId, request, classification, mutatingActions, mutatingActions, false, null, null, "none", resultFinal.HumanReadableSummary, resultFinal, warnings, errors);
        }
        catch (OperationCanceledException)
        {
            warnings.Add("Cancellation requested at a safe boundary.");
            var cancelledAt = _now();
            var emptyHealth = UnknownHealth();
            var result = new PimaxRepairResultResponse(PimaxRepairResultSchema.Version, operationId, correlationId, request.Mode, started, cancelledAt, PimaxSoftwareRepairOutcome.Cancelled, PimaxRepairStage.Cancelled, HealthSummary(emptyHealth), null, [], [], actions.ToArray(), [], [], "Pimax repair was cancelled. No rollback is claimed.", null, true, false, warnings.ToArray(), errors.ToArray());
            _lastResult = result;
            return Accepted(operationId, request, "unknown", [], [], false, null, null, "none", result.HumanReadableSummary, result, warnings, errors);
        }
        finally
        {
            ActiveCancellation.TryRemove(_scope, out _);
            ActiveScopes.TryRemove(_scope, out _);
        }
    }

    public PimaxRepairStatusResponse Status()
        => _lastStatus ?? new PimaxRepairStatusResponse(PimaxRepairStatusSchema.Version, false, null, "idle", null, [], [], 0, false, false, "No Pimax repair operation is active.");

    public PimaxRepairCancelResponse Cancel()
    {
        if (ActiveCancellation.TryGetValue(_scope, out var cancellation))
        {
            cancellation.Cancel();
            return new PimaxRepairCancelResponse(PimaxRepairCancelSchema.Version, true, _lastStatus?.OperationId, _lastStatus?.Stage ?? "unknown", PimaxSoftwareRepairOutcome.Cancelled, "Cancellation requested. The active operation will stop at the next safe boundary.");
        }

        return new PimaxRepairCancelResponse(PimaxRepairCancelSchema.Version, false, null, "idle", "none", "No Pimax repair operation is active.");
    }

    public PimaxRepairResultResponse? Result() => _lastResult;

    private async Task<PimaxRecoveryOperationResult> ExecuteActionAsync(
        string actionId,
        string stage,
        PimaxRepairTarget target,
        TimeSpan timeout,
        List<PimaxRepairActionEvent> actions,
        Func<CancellationToken, Task<PimaxRecoveryOperationResult>> action,
        CancellationToken cancellationToken)
    {
        var started = _now();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            UpdateStatus(null, stage, actionId, actions.Select(item => item.ActionId), started, cancellationAvailable: true, cancellationRequested: cancellationToken.IsCancellationRequested, actionId);
            var result = await action(timeoutSource.Token);
            var ended = _now();
            actions.Add(new PimaxRepairActionEvent(actionId, stage, target.TargetId, started, ended, (ended - started).TotalMilliseconds, result.Attempted, result.Success, true, false, cancellationToken.IsCancellationRequested, result.Message));
            return result;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var ended = _now();
            actions.Add(new PimaxRepairActionEvent(actionId, stage, target.TargetId, started, ended, (ended - started).TotalMilliseconds, true, false, true, true, false, $"Timed out after {timeout.TotalSeconds:0}s."));
            return new PimaxRecoveryOperationResult(true, false, $"Timed out after {timeout.TotalSeconds:0}s.", []);
        }
    }

    private async Task<PimaxComponentHealthSnapshot> SafePostHealthAsync(List<string> warnings, List<string> errors, CancellationToken cancellationToken)
    {
        try { return await _healthCollector.CollectAsync(cancellationToken); }
        catch (Exception ex) { errors.Add($"Post-health capture failed: {ex.GetType().Name}: {ex.Message}"); return UnknownHealth(); }
    }

    private static string DetermineOutcome(PimaxComponentHealthSnapshot pre, PimaxComponentHealthSnapshot post, IReadOnlyCollection<PimaxRepairActionEvent> actions)
    {
        if (!actions.All(action => action.Success))
        {
            return PimaxSoftwareRepairOutcome.SoftwareRepairFailed;
        }

        if (post.OverallStatus == PimaxHealthOverallStatus.CoreConnectionMissing)
        {
            return PimaxSoftwareRepairOutcome.CoreUsbMissing;
        }

        if (post.Components.Any(component => component.ComponentId == "displayPortVideo" && component.Status == PimaxHealthComponentStatus.Missing))
        {
            return PimaxSoftwareRepairOutcome.DisplayPathMissing;
        }

        if (post.OverallStatus == PimaxHealthOverallStatus.Healthy && post.RegistrationAssessment.State == PimaxRegistrationState.RegisteredReady && pre.OverallStatus != PimaxHealthOverallStatus.Healthy)
        {
            return PimaxSoftwareRepairOutcome.Repaired;
        }

        if (post.RegistrationAssessment.State == PimaxRegistrationState.RegisteredReady && post.OverallStatus == PimaxHealthOverallStatus.UsableWithDegradedFeatures)
        {
            return PimaxSoftwareRepairOutcome.RepairedWithDegradedFeatures;
        }

        if (post.RegistrationAssessment.State == PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration)
        {
            return PimaxSoftwareRepairOutcome.SoftwareStackHealthyButNotRegistered;
        }

        return PimaxSoftwareRepairOutcome.SoftwareRepairFailed;
    }

    private PimaxRepairResultResponse BuildResult(
        string operationId,
        string correlationId,
        string mode,
        DateTimeOffset started,
        DateTimeOffset ended,
        string outcome,
        string stage,
        PimaxComponentHealthSnapshot preHealth,
        PimaxComponentHealthSnapshot? postHealth,
        PimaxRepairTargetsSnapshot targets,
        IReadOnlyCollection<PimaxRepairActionEvent> actions,
        bool cancellation,
        bool timedOut,
        IReadOnlyCollection<string> warnings,
        IReadOnlyCollection<string> errors)
        => new(
            PimaxRepairResultSchema.Version,
            operationId,
            correlationId,
            mode,
            started,
            ended,
            outcome,
            stage,
            HealthSummary(preHealth),
            postHealth is null ? null : HealthSummary(postHealth),
            ComponentReport(postHealth ?? preHealth),
            targets.Targets,
            actions.ToArray(),
            (postHealth ?? preHealth).BlockingIssues,
            (postHealth ?? preHealth).DegradedFeatures,
            HumanResult(outcome),
            RequiredOperatorAction(outcome),
            cancellation,
            timedOut,
            warnings.ToArray(),
            errors.ToArray());

    private static PimaxRepairHealthSummary HealthSummary(PimaxComponentHealthSnapshot health)
        => new(health.OverallStatus, health.RegistrationAssessment.State, health.RegistrationAssessment.Confidence, health.EvidenceConfidence, health.HumanReadableSummary);

    private static PimaxRepairComponentReport[] ComponentReport(PimaxComponentHealthSnapshot health)
        => health.Components
            .Where(component => component.ComponentId is "pimaxRegistration" or "coreUsb" or "usb2Companion" or "superSpeedCompanion" or "displayPortVideo" or "headsetAudioOutput" or "headsetMicrophone" or "eyeChip" or "eyeTracking" or "trackingCameras" or "headsetHid" or "viveFaceTracker" or "pimaxRuntime" or "pimaxPlay" or "pimaxServices")
            .Select(component => new PimaxRepairComponentReport(component.ComponentId, component.DisplayName, component.Status, component.Criticality, component.Explanation))
            .ToArray();

    private static string HumanResult(string outcome) => outcome switch
    {
        PimaxSoftwareRepairOutcome.NoRepairNeeded => "No Pimax repair was needed. Registration and required core components were already healthy.",
        PimaxSoftwareRepairOutcome.Repaired => "Pimax connection repair: REPAIRED. Post-health is healthy and registration is ready.",
        PimaxSoftwareRepairOutcome.RepairedWithDegradedFeatures => "Pimax connection repair: PARTIAL. Core VR is usable, but one or more feature components remain degraded.",
        PimaxSoftwareRepairOutcome.SoftwareStackHealthyButNotRegistered => "The Pimax software stack restarted successfully, but Pimax Play still has not registered the headset.\n\nPimax Play Connect and a physical USB reconnection may still be required.",
        PimaxSoftwareRepairOutcome.CoreUsbMissing => "Core Pimax USB is missing. No software restart can be reported as a complete repair.",
        PimaxSoftwareRepairOutcome.DisplayPathMissing => "Pimax registration may be ready, but the DisplayPort image path remains missing.",
        PimaxSoftwareRepairOutcome.UnsupportedAutomaticRecovery => "No approved executable software action exists for the diagnosed Pimax problem.",
        PimaxSoftwareRepairOutcome.ConflictingEvidence => "Pimax evidence is conflicting. Automatic repair was not attempted.",
        PimaxSoftwareRepairOutcome.Unknown => "Pimax repair state is unknown. Automatic repair was not attempted.",
        PimaxSoftwareRepairOutcome.Cancelled => "Pimax repair was cancelled at a safe boundary.",
        PimaxSoftwareRepairOutcome.TimedOut => "Pimax repair timed out. No automatic retry was attempted.",
        _ => "Pimax software repair failed before post-health could prove recovery."
    };

    private static string? RequiredOperatorAction(string outcome) => outcome switch
    {
        PimaxSoftwareRepairOutcome.SoftwareStackHealthyButNotRegistered => "Use Pimax Play Connect and physically reconnect the Pimax USB cable if needed.",
        PimaxSoftwareRepairOutcome.CoreUsbMissing => "Check the physical Pimax USB connection. Do not use software USB reset as a repair.",
        PimaxSoftwareRepairOutcome.DisplayPathMissing => "Check the physical DisplayPort connection.",
        PimaxSoftwareRepairOutcome.UnsupportedAutomaticRecovery => "Operator review or physical recovery may be required.",
        _ => null
    };

    private PimaxRepairStartResponse Accepted(string operationId, PimaxRepairStartRequest request, string classification, string[] proposed, string[] mutating, bool confirmationRequired, string? confirmationToken, DateTimeOffset? confirmationExpires, string conflict, string summary, PimaxRepairResultResponse? result, IReadOnlyCollection<string> warnings, IReadOnlyCollection<string> errors)
        => new(PimaxRepairStartSchema.Version, true, operationId, request.Mode, result?.Stage ?? PimaxRepairStage.AwaitingConfirmation, classification, proposed, mutating, PimaxRepairPolicy.BuildCapabilities().AutomationLimitations, confirmationRequired, confirmationToken, confirmationExpires, conflict, summary, result, warnings.ToArray(), errors.ToArray());

    private static PimaxRepairStartResponse Rejected(string operationId, PimaxRepairStartRequest request, string classification, string summary, string[] errors)
        => new(PimaxRepairStartSchema.Version, false, operationId, request.Mode, PimaxRepairStage.Failed, classification, [], [], PimaxRepairPolicy.BuildCapabilities().AutomationLimitations, false, null, null, summary, summary, null, [], errors);

    private void UpdateStatus(string? operationId, string stage, string? action, IEnumerable<string> completed, DateTimeOffset started, bool cancellationAvailable, bool cancellationRequested, string summary)
        => _lastStatus = new PimaxRepairStatusResponse(PimaxRepairStatusSchema.Version, true, operationId ?? _lastStatus?.OperationId, stage, action, completed.ToArray(), [], (_now() - started).TotalSeconds, cancellationAvailable, cancellationRequested, summary);

    private void Log(string operationId, string correlationId, string stage, string? action, string? targetId, string result, double durationMs, bool timeout, bool cancellation, string? exceptionType, string? error, PimaxRepairHealthSummary? pre, PimaxRepairHealthSummary? post, string? finalOutcome, IReadOnlyCollection<string> warnings)
        => _diagnostics.Append(new PimaxRepairOperationLogEntry(PimaxRepairDiagnosticsWriter.Schema, operationId, correlationId, typeof(PimaxSoftwareStackRepairBackend).Assembly.GetName().Version?.ToString() ?? "unknown", _now(), stage, action, targetId, result, durationMs, timeout, cancellation, exceptionType, error, pre, post, finalOutcome, warnings.ToArray()));

    private static PimaxComponentHealthSnapshot UnknownHealth()
        => new(PimaxComponentHealthSchema.Version, DateTimeOffset.Now, "unknown", PimaxHealthOverallStatus.Unknown, new PimaxRegistrationAssessmentResult(PimaxRegistrationState.Unknown, PimaxRegistrationConfidence.Insufficient, "Health unavailable.", [], [], [], [], [], new PimaxRegistrationEvidence(false, 0, 0, false, 0, 0, false, false, false, false, false, [], [])), "insufficient", [], ["Health unavailable."], [], [], "Health unavailable.", "insufficient", new PimaxHealthCapabilitySummary("unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown"), new PimaxHealthSanitizedEvidence(PimaxConnectivitySchema.Version, PimaxUsbEnumerationSchema.Version, PimaxRegistrationAssessmentSchema.Version, "unknown", PimaxRegistrationState.Unknown, 0, 0, [], [], [], []), [], []);
}

internal static class PimaxRepairCommandLine
{
    public static PimaxRepairStartRequest ParseStart(string[] args)
    {
        var mode = Option(args, "--mode") ?? PimaxSoftwareStackRepairBackend.ModeSoftwareStackOnly;
        var timeout = int.TryParse(Option(args, "--timeout-seconds"), out var parsed) ? Math.Clamp(parsed, 30, 300) : 120;
        return new PimaxRepairStartRequest(
            mode,
            HasFlag(args, "--dry-run") || !HasFlag(args, "--confirm"),
            HasFlag(args, "--confirm"),
            Option(args, "--confirmation-token"),
            timeout);
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static string? Option(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return args[index][prefix.Length..];
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) return args[index + 1];
        }

        return null;
    }
}
