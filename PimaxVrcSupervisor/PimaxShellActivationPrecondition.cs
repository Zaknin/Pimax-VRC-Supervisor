using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

internal static class PimaxShellActivationPreconditionSchema
{
    public const string Version = "pimax-shell-activation-precondition-v1";
}

internal static class PimaxShellActivationPreconditionState
{
    public const string QuiescentForShellActivation = "quiescentForShellActivation";
    public const string LaunchOwnedMembersPresent = "launchOwnedMembersPresent";
    public const string PersistentOwnershipUnresolved = "persistentOwnershipUnresolved";
    public const string UnclassifiedMembersPresent = "unclassifiedMembersPresent";
    public const string DuplicateInstallation = "duplicateInstallation";
    public const string RegistrationContradictory = "registrationContradictory";
    public const string RecoveryLeaseActive = "recoveryLeaseActive";
    public const string Unstable = "unstable";
    public const string Incomplete = "incomplete";
    public const string Unknown = "unknown";
}

internal sealed record PimaxShellActivationPreconditionSnapshot(
    string Schema,
    DateTimeOffset CollectedAt,
    string AssessmentId,
    string SoftwareGroupHealthState,
    string GeneralSoftwareGroupState,
    string ActivationPreconditionState,
    bool Quiescent,
    bool Stable,
    int StableSampleCount,
    int RequiredStableSampleCount,
    double SampleIntervalSeconds,
    string[] CoreMembersPresent,
    string[] LaunchOwnedMembersPresent,
    string[] PermittedPersistentMembersPresent,
    string[] UnclassifiedMembersPresent,
    PimaxShellProcessOwnershipEvidence[] OwnershipEvidence,
    string PiServiceLauncherClassification,
    string RegistrationEvidenceState,
    bool StaleRegistrationBlocking,
    string DuplicateInstallationEvidence,
    string RecoveryLeaseState,
    string ShellEntryTrustState,
    bool ReadinessForControlledValidation,
    bool BackendExecutable,
    bool AutomaticRecoveryAllowed,
    string[] Warnings,
    string[] Errors,
    string[] PrivacyRedactions,
    string HumanReadableSummary);

internal sealed record PimaxShellProcessOwnershipEvidence(
    string ProcessName,
    string SanitizedPath,
    string OwnershipClassification,
    string CreatorClassification,
    string AssociatedService,
    string InstallationRootClassification,
    string StabilityKey);

internal sealed record PimaxShellQuiescenceProcessSnapshot(
    string ProcessName,
    string SanitizedPath,
    string OwnershipClassification,
    string CreatorClassification,
    string AssociatedService,
    string InstallationRootClassification,
    string StabilityKey);

internal sealed record PimaxShellQuiescenceSample(
    DateTimeOffset CapturedAt,
    PimaxComponentHealthSnapshot Health,
    PimaxShellQuiescenceProcessSnapshot[] Processes,
    bool RecoveryLeaseActive);

internal interface IPimaxShellQuiescenceProbe
{
    Task<PimaxShellQuiescenceSample> CollectAsync(SupervisorConfig config, CancellationToken cancellationToken);
}

internal sealed class PimaxShellActivationPreconditionCoordinator(
    IPimaxShellQuiescenceProbe? probe = null,
    TimeSpan? sampleInterval = null,
    TimeSpan? timeout = null,
    int requiredStableSamples = 3,
    Func<DateTimeOffset>? now = null)
{
    private readonly IPimaxShellQuiescenceProbe _probe = probe ?? new WindowsPimaxShellQuiescenceProbe();
    private readonly TimeSpan _sampleInterval = sampleInterval ?? TimeSpan.FromSeconds(1);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(10);
    private readonly int _requiredStableSamples = requiredStableSamples;
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);

    public async Task<PimaxShellActivationPreconditionSnapshot> AssessAsync(SupervisorConfig config, string shellEntryTrustState, CancellationToken cancellationToken)
    {
        var samples = new List<PimaxShellQuiescenceSample>();
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        while (!linked.IsCancellationRequested)
        {
            try
            {
                samples.Add(await _probe.CollectAsync(config, linked.Token));
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var current = Evaluate(samples, shellEntryTrustState, _requiredStableSamples, _sampleInterval, _now());
            if (current.ReadinessForControlledValidation || current.ActivationPreconditionState is not PimaxShellActivationPreconditionState.Incomplete)
            {
                return current;
            }

            await Task.Delay(_sampleInterval, linked.Token).ContinueWith(_ => { }, CancellationToken.None);
        }

        return Evaluate(samples, shellEntryTrustState, _requiredStableSamples, _sampleInterval, _now()) with
        {
            ActivationPreconditionState = samples.Count >= _requiredStableSamples ? Evaluate(samples, shellEntryTrustState, _requiredStableSamples, _sampleInterval, _now()).ActivationPreconditionState : PimaxShellActivationPreconditionState.Incomplete,
            Errors = samples.Count >= _requiredStableSamples ? Evaluate(samples, shellEntryTrustState, _requiredStableSamples, _sampleInterval, _now()).Errors : ["Quiescence precondition did not collect the required stable sample count within the bounded assessment window."],
            HumanReadableSummary = samples.Count >= _requiredStableSamples ? Evaluate(samples, shellEntryTrustState, _requiredStableSamples, _sampleInterval, _now()).HumanReadableSummary : "Pimax Shell activation precondition did not collect enough stable samples within the bounded assessment window."
        };
    }

    internal static PimaxShellActivationPreconditionSnapshot Evaluate(
        IReadOnlyList<PimaxShellQuiescenceSample> samples,
        string shellEntryTrustState,
        int requiredStableSamples,
        TimeSpan sampleInterval,
        DateTimeOffset collectedAt)
    {
        var assessmentId = $"pimax-shell-precondition-{Guid.NewGuid():N}";
        if (samples.Count == 0)
        {
            return Snapshot(assessmentId, collectedAt, "unknown", PimaxSoftwareGroupState.Unknown, PimaxShellActivationPreconditionState.Incomplete, false, false, 0, requiredStableSamples, sampleInterval, [], [], [], [], [], "notObserved", PimaxRegistrationState.Unknown, true, "unknown", "unknown", shellEntryTrustState, ["No quiescence samples were collected."], "No quiescence samples were collected.");
        }

        var last = samples[^1];
        var evaluations = samples.Select(EvaluateSample).ToArray();
        var lastEval = evaluations[^1];
        var stableCount = StableSuffixCount(evaluations);
        var stable = stableCount >= requiredStableSamples;
        var warnings = new List<string>();
        var errors = new List<string>();
        if (!stable)
        {
            errors.Add("Permitted persistent process set or ownership evidence was not stable for the required sample count.");
        }

        errors.AddRange(lastEval.Errors);
        warnings.AddRange(last.Health.Warnings);
        var state =
            lastEval.CoreMembersPresent.Length > 0 || lastEval.LaunchOwnedMembersPresent.Length > 0 ? PimaxShellActivationPreconditionState.LaunchOwnedMembersPresent :
            lastEval.PiServiceLauncherClassification is "unknownParent" or "deviceSettingOwned" or "ambiguous" or "staleOwnership" or "untrustedPath" ? PimaxShellActivationPreconditionState.PersistentOwnershipUnresolved :
            lastEval.UnclassifiedMembersPresent.Length > 0 ? PimaxShellActivationPreconditionState.UnclassifiedMembersPresent :
            lastEval.DuplicateInstallationEvidence != "none" ? PimaxShellActivationPreconditionState.DuplicateInstallation :
            lastEval.RegistrationContradictory ? PimaxShellActivationPreconditionState.RegistrationContradictory :
            last.RecoveryLeaseActive ? PimaxShellActivationPreconditionState.RecoveryLeaseActive :
            !stable ? PimaxShellActivationPreconditionState.Incomplete :
            PimaxShellActivationPreconditionState.QuiescentForShellActivation;
        var ready = state == PimaxShellActivationPreconditionState.QuiescentForShellActivation
            && string.Equals(shellEntryTrustState, PimaxShellActivationCapabilityState.ReadyForControlledValidation, StringComparison.OrdinalIgnoreCase);
        return Snapshot(
            assessmentId,
            collectedAt,
            last.Health.OverallStatus,
            last.Health.SourceEvidence.SoftwareGroup.State,
            state,
            state == PimaxShellActivationPreconditionState.QuiescentForShellActivation,
            stable,
            stableCount,
            requiredStableSamples,
            sampleInterval,
            lastEval.CoreMembersPresent,
            lastEval.LaunchOwnedMembersPresent,
            lastEval.PermittedPersistentMembersPresent,
            lastEval.UnclassifiedMembersPresent,
            lastEval.OwnershipEvidence,
            lastEval.PiServiceLauncherClassification,
            last.Health.RegistrationAssessment.State,
            lastEval.StaleRegistrationBlocking,
            lastEval.DuplicateInstallationEvidence,
            last.RecoveryLeaseActive ? "active" : "noneObserved",
            shellEntryTrustState,
            errors,
            ready
                ? "Pimax launch-owned group is quiescent and stable for controlled Shell activation. General health may remain partial because persistent platform processes are still present."
                : $"Pimax Shell activation precondition is not ready: {state}.",
            warnings);
    }

    private static PimaxShellActivationPreconditionSnapshot Snapshot(
        string assessmentId,
        DateTimeOffset collectedAt,
        string softwareGroupHealthState,
        string generalSoftwareGroupState,
        string state,
        bool quiescent,
        bool stable,
        int stableSampleCount,
        int requiredStableSamples,
        TimeSpan sampleInterval,
        string[] coreMembersPresent,
        string[] launchOwnedMembersPresent,
        string[] permittedPersistentMembersPresent,
        string[] unclassifiedMembersPresent,
        PimaxShellProcessOwnershipEvidence[] ownershipEvidence,
        string piServiceLauncherClassification,
        string registrationEvidenceState,
        bool staleRegistrationBlocking,
        string duplicateInstallationEvidence,
        string recoveryLeaseState,
        string shellEntryTrustState,
        IReadOnlyCollection<string> errors,
        string summary,
        IReadOnlyCollection<string>? warnings = null)
        => new(
            PimaxShellActivationPreconditionSchema.Version,
            collectedAt,
            assessmentId,
            softwareGroupHealthState,
            generalSoftwareGroupState,
            state,
            quiescent,
            stable,
            stableSampleCount,
            requiredStableSamples,
            Math.Round(sampleInterval.TotalSeconds, 3),
            coreMembersPresent,
            launchOwnedMembersPresent,
            permittedPersistentMembersPresent,
            unclassifiedMembersPresent,
            ownershipEvidence,
            piServiceLauncherClassification,
            registrationEvidenceState,
            staleRegistrationBlocking,
            duplicateInstallationEvidence,
            recoveryLeaseState,
            shellEntryTrustState,
            state == PimaxShellActivationPreconditionState.QuiescentForShellActivation && string.Equals(shellEntryTrustState, PimaxShellActivationCapabilityState.ReadyForControlledValidation, StringComparison.OrdinalIgnoreCase),
            BackendExecutable: false,
            AutomaticRecoveryAllowed: false,
            warnings?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ["raw PIDs", "raw parent PIDs", "raw command lines", "user names", "machine names", "handles", "environment blocks", "certificate serial numbers"],
            summary);

    private static int StableSuffixCount(PimaxShellSampleEvaluation[] evaluations)
    {
        if (evaluations.Length == 0) return 0;
        var key = evaluations[^1].StabilityFingerprint;
        var count = 0;
        for (var index = evaluations.Length - 1; index >= 0; index--)
        {
            if (!string.Equals(evaluations[index].StabilityFingerprint, key, StringComparison.Ordinal)) break;
            count++;
        }

        return count;
    }

    private static PimaxShellSampleEvaluation EvaluateSample(PimaxShellQuiescenceSample sample)
    {
        var processes = sample.Processes;
        var core = Present(processes, CoreRequiredAbsent);
        var launchOwned = Present(processes, LaunchOwnedRequiredAbsent);
        var permitted = new List<string>();
        var unclassified = new List<string>();
        var errors = new List<string>();
        foreach (var process in processes)
        {
            if (CoreRequiredAbsent.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)
                || LaunchOwnedRequiredAbsent.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsPermittedPlatform(process))
            {
                permitted.Add(process.ProcessName);
                continue;
            }

            if (string.Equals(process.ProcessName, "PiServiceLauncher", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            unclassified.Add(process.ProcessName);
        }

        var launcherClassification = ClassifyPiServiceLauncher(processes.Where(process => string.Equals(process.ProcessName, "PiServiceLauncher", StringComparison.OrdinalIgnoreCase)).ToArray());
        if (launcherClassification == "serviceOwned")
        {
            permitted.Add("PiServiceLauncher");
        }

        if (launcherClassification is "unknownParent" or "deviceSettingOwned" or "ambiguous" or "staleOwnership" or "untrustedPath")
        {
            errors.Add("PiServiceLauncher ownership is not the approved stable service-owned persistent instance.");
        }

        var duplicate = processes.Any(process => process.InstallationRootClassification == "duplicateOrUnexpectedPimaxRoot") ? "unexpectedPimaxRootObserved" : "none";
        var contradictory = sample.Health.RegistrationAssessment.State == PimaxRegistrationState.ConflictingEvidence;
        var staleBlocking = sample.Health.RegistrationAssessment.State == PimaxRegistrationState.RegistrationEvidenceStale
            && (core.Length > 0 || launchOwned.Length > 0 || unclassified.Count > 0 || launcherClassification is not "none" and not "serviceOwned");
        var evidence = processes
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.StabilityKey, StringComparer.Ordinal)
            .Select(process => new PimaxShellProcessOwnershipEvidence(
                process.ProcessName,
                process.SanitizedPath,
                process.OwnershipClassification,
                process.CreatorClassification,
                process.AssociatedService,
                process.InstallationRootClassification,
                process.StabilityKey))
            .ToArray();
        var fingerprint = string.Join("|", processes
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.StabilityKey, StringComparer.Ordinal)
            .Select(process => $"{process.ProcessName}:{process.OwnershipClassification}:{process.CreatorClassification}:{process.AssociatedService}:{process.InstallationRootClassification}"));
        return new PimaxShellSampleEvaluation(
            core,
            launchOwned,
            permitted.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            unclassified.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            evidence,
            launcherClassification,
            staleBlocking,
            duplicate,
            contradictory,
            errors.ToArray(),
            fingerprint);
    }

    private static string[] Present(PimaxShellQuiescenceProcessSnapshot[] processes, string[] names)
        => processes
            .Where(process => names.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            .Select(process => process.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsPermittedPlatform(PimaxShellQuiescenceProcessSnapshot process)
        => process.ProcessName.Equals("PiPlatformService_64", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("Tobii VR4PIMAXP3B Platform Runtime", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("platform_runtime_VR4PIMAXP3B_service", StringComparison.OrdinalIgnoreCase);

    private static string ClassifyPiServiceLauncher(PimaxShellQuiescenceProcessSnapshot[] launchers)
    {
        if (launchers.Length == 0) return "none";
        if (launchers.Length > 1) return "ambiguous";
        var launcher = launchers[0];
        if (launcher.InstallationRootClassification == "duplicateOrUnexpectedPimaxRoot") return "untrustedPath";
        if (launcher.OwnershipClassification == "deviceSettingOwned" || launcher.CreatorClassification == "deviceSetting") return "deviceSettingOwned";
        if (launcher.OwnershipClassification == "serviceOwned"
            && launcher.CreatorClassification is "services" or "serviceControlManager" or "approvedServiceHost")
        {
            return "serviceOwned";
        }

        if (launcher.OwnershipClassification == "stale") return "staleOwnership";
        return "unknownParent";
    }

    private static readonly string[] CoreRequiredAbsent =
    [
        "PimaxClient",
        "DeviceSetting",
        "PiPlayService",
        "PiService",
        "pi_server"
    ];

    private static readonly string[] LaunchOwnedRequiredAbsent =
    [
        "PVRHome",
        "pi_overlay",
        "vrss_gaze_provider",
        "lighthouse_console",
        "launcher",
        "fastlist-0.3.0-x64"
    ];

    private sealed record PimaxShellSampleEvaluation(
        string[] CoreMembersPresent,
        string[] LaunchOwnedMembersPresent,
        string[] PermittedPersistentMembersPresent,
        string[] UnclassifiedMembersPresent,
        PimaxShellProcessOwnershipEvidence[] OwnershipEvidence,
        string PiServiceLauncherClassification,
        bool StaleRegistrationBlocking,
        string DuplicateInstallationEvidence,
        bool RegistrationContradictory,
        string[] Errors,
        string StabilityFingerprint);
}

internal sealed class WindowsPimaxShellQuiescenceProbe : IPimaxShellQuiescenceProbe
{
    public async Task<PimaxShellQuiescenceSample> CollectAsync(SupervisorConfig config, CancellationToken cancellationToken)
    {
        var health = await new PimaxComponentHealthCoordinator().CollectAsync(config, cancellationToken);
        return new PimaxShellQuiescenceSample(DateTimeOffset.Now, health, CaptureProcesses(), RecoveryLeaseActive: false);
    }

    private static PimaxShellQuiescenceProcessSnapshot[] CaptureProcesses()
    {
        var servicesByPid = ServiceProcesses();
        var raw = new List<RawProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    var path = Safe(() => process.MainModule?.FileName);
                    if (!IsRelevant(name, path)) continue;
                    var parentId = ParentProcessId(process);
                    raw.Add(new RawProcess(process.Id, parentId, name, path, Safe(() => process.StartTime)));
                }
                catch
                {
                }
            }
        }

        var byId = raw.ToDictionary(process => process.ProcessId);
        return raw.Select(process =>
        {
            var parentName = process.ParentProcessId is int parent && byId.TryGetValue(parent, out var parentProcess)
                ? parentProcess.ProcessName
                : ParentProcessName(process.ParentProcessId);
            var service = servicesByPid.GetValueOrDefault(process.ProcessId) ?? "";
            var creator = CreatorClassification(parentName, service);
            var ownership = OwnershipClassification(process.ProcessName, creator, service);
            var sanitized = PimaxConnectivityRedactor.SanitizePath(process.ExecutablePath) ?? "";
            return new PimaxShellQuiescenceProcessSnapshot(
                process.ProcessName,
                sanitized,
                ownership,
                creator,
                string.IsNullOrWhiteSpace(service) ? "none" : service,
                InstallationRootClassification(process.ExecutablePath),
                StabilityKey(process.ProcessName, sanitized, ownership, creator, service));
        })
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.StabilityKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<int, string> ServiceProcesses()
    {
        var result = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, ProcessId FROM Win32_Service WHERE ProcessId > 0");
            foreach (ManagementObject service in searcher.Get().Cast<ManagementObject>())
            {
                var pid = Convert.ToInt32(service["ProcessId"]);
                var name = service["Name"]?.ToString() ?? "";
                if (pid > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    result[pid] = name;
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static string? ParentProcessName(int? parentProcessId)
    {
        if (parentProcessId is not int pid || pid <= 0) return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string CreatorClassification(string? parentName, string serviceName)
    {
        if (!string.IsNullOrWhiteSpace(serviceName)) return "serviceControlManager";
        if (string.Equals(parentName, "services", StringComparison.OrdinalIgnoreCase)) return "services";
        if (string.Equals(parentName, "svchost", StringComparison.OrdinalIgnoreCase)) return "approvedServiceHost";
        if (string.Equals(parentName, "DeviceSetting", StringComparison.OrdinalIgnoreCase)) return "deviceSetting";
        if (string.IsNullOrWhiteSpace(parentName)) return "unknown";
        return "other";
    }

    private static string OwnershipClassification(string processName, string creator, string serviceName)
    {
        if (!string.IsNullOrWhiteSpace(serviceName)) return "serviceOwned";
        if (creator == "deviceSetting") return "deviceSettingOwned";
        if (processName.Equals("PiPlatformService_64", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("Tobii VR4PIMAXP3B Platform Runtime", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("platform_runtime_VR4PIMAXP3B_service", StringComparison.OrdinalIgnoreCase))
        {
            return creator is "services" or "serviceControlManager" or "approvedServiceHost" ? "serviceOwned" : "persistentPlatform";
        }

        return creator == "unknown" ? "unknown" : "launchOrUserOwned";
    }

    private static string InstallationRootClassification(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "unknown";
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax");
        if (path.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) return "expectedPimaxRoot";
        if (path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase)) return "duplicateOrUnexpectedPimaxRoot";
        return "unknown";
    }

    private static string StabilityKey(string processName, string sanitizedPath, string ownership, string creator, string serviceName)
    {
        var basis = $"{processName}|{sanitizedPath}|{ownership}|{creator}|{serviceName}".ToLowerInvariant();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(basis)))[..16].ToLowerInvariant();
    }

    private static bool IsRelevant(string name, string? path)
        => name.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
            || name is "PimaxClient" or "DeviceSetting" or "PiPlayService" or "PiService" or "pi_server" or "PiServiceLauncher" or "PiPlatformService_64" or "PVRHome" or "pi_overlay" or "vrss_gaze_provider" or "lighthouse_console" or "launcher" or "fastlist-0.3.0-x64" or "Tobii VR4PIMAXP3B Platform Runtime" or "platform_runtime_VR4PIMAXP3B_service"
            || path?.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) == true;

    private static T? Safe<T>(Func<T> action)
    {
        try { return action(); }
        catch { return default; }
    }

    private static int? ParentProcessId(Process process)
    {
        try
        {
            var info = new ProcessBasicInformation();
            return NtQueryInformationProcess(process.Handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) == 0
                ? (int)info.InheritedFromUniqueProcessId
                : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    private sealed record RawProcess(int ProcessId, int? ParentProcessId, string ProcessName, string? ExecutablePath, DateTime? StartTime);
}
