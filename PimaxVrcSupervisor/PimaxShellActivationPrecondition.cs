using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
    string StabilityKey,
    string SessionClassification,
    string SignatureState,
    string Sha256,
    long FileSizeBytes,
    string FileCreatedUtc,
    string FileWrittenUtc,
    string CanonicalPathClassification,
    string ProvenanceEvidenceSource,
    string ProvenanceConfidence,
    string ParentState,
    string ServiceIdentity,
    string ServiceBinaryPathClassification,
    string ServiceSignerClassification,
    string ClassificationReason);

internal sealed record PimaxShellQuiescenceProcessSnapshot(
    string ProcessName,
    string SanitizedPath,
    string OwnershipClassification,
    string CreatorClassification,
    string AssociatedService,
    string InstallationRootClassification,
    string StabilityKey,
    string SessionClassification = "unknown",
    string SignatureState = "unavailable",
    string Sha256 = "unavailable",
    long FileSizeBytes = 0,
    string FileCreatedUtc = "",
    string FileWrittenUtc = "",
    string CanonicalPathClassification = "unknown",
    string ProvenanceEvidenceSource = "notApplicable",
    string ProvenanceConfidence = "none",
    string ParentState = "unknown",
    string ServiceIdentity = "none",
    string ServiceBinaryPathClassification = "unknown",
    string ServiceSignerClassification = "unknown",
    string ClassificationReason = "notClassified");

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
        var processes = sample.Processes
            .Where(process => !IsTrustedSupervisorProcess(process))
            .ToArray();
        var core = Present(processes, CoreRequiredAbsent);
        var launchOwned = Present(processes, LaunchOwnedRequiredAbsent);
        var permitted = new List<string>();
        var unclassified = new List<string>();
        var errors = new List<string>();
        var vrssProcesses = processes
            .Where(process => string.Equals(process.ProcessName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var process in processes)
        {
            if (CoreRequiredAbsent.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)
                || LaunchOwnedRequiredAbsent.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(process.ProcessName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase))
            {
                if (IsPermittedPersistentVrss(process, vrssProcesses, out var vrssError))
                {
                    permitted.Add(process.ProcessName);
                }
                else
                {
                    unclassified.Add(process.ProcessName);
                    errors.Add(vrssError);
                }

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
                process.StabilityKey,
                process.SessionClassification,
                process.SignatureState,
                process.Sha256,
                process.FileSizeBytes,
                process.FileCreatedUtc,
                process.FileWrittenUtc,
                process.CanonicalPathClassification,
                process.ProvenanceEvidenceSource,
                process.ProvenanceConfidence,
                process.ParentState,
                process.ServiceIdentity,
                process.ServiceBinaryPathClassification,
                process.ServiceSignerClassification,
                process.ClassificationReason))
            .ToArray();
        var fingerprint = string.Join("|", processes
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.StabilityKey, StringComparer.Ordinal)
            .Select(process => $"{process.ProcessName}:{process.OwnershipClassification}:{process.CreatorClassification}:{process.AssociatedService}:{process.InstallationRootClassification}:{process.SessionClassification}:{process.Sha256}:{process.CanonicalPathClassification}:{process.ProvenanceEvidenceSource}:{process.ProvenanceConfidence}:{process.ParentState}:{process.ServiceBinaryPathClassification}:{process.ServiceSignerClassification}"));
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

    private static bool IsPermittedPersistentVrss(
        PimaxShellQuiescenceProcessSnapshot process,
        PimaxShellQuiescenceProcessSnapshot[] vrssProcesses,
        out string error)
    {
        if (vrssProcesses.Length != 1)
        {
            error = "vrss_gaze_provider must have exactly one stable instance.";
            return false;
        }

        if (process.CanonicalPathClassification != "exactExpectedRuntimePath"
            || process.InstallationRootClassification != "expectedPimaxRuntimeRoot")
        {
            error = "vrss_gaze_provider path is not the exact expected Pimax Runtime executable.";
            return false;
        }

        if (process.SessionClassification != "session0")
        {
            error = "vrss_gaze_provider is not confined to session 0.";
            return false;
        }

        if (process.SignatureState is not "unsigned" and not "notSignedOrUnreadable")
        {
            error = "vrss_gaze_provider signature state is unexpected for the locally observed unsigned binary.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(process.Sha256) || process.Sha256 == "unavailable")
        {
            error = "vrss_gaze_provider hash could not be recorded.";
            return false;
        }

        if (process.OwnershipClassification != "persistentServiceDescendant")
        {
            error = "vrss_gaze_provider provenance is not the approved persistent service-descendant lifecycle.";
            return false;
        }

        if (process.CreatorClassification is not "serviceControlManagerViaLiveLauncher" and not "serviceControlManagerViaExitedLauncher" and not "persistentServiceDescendantFromPreservedEvidence")
        {
            error = "vrss_gaze_provider creator classification is not approved.";
            return false;
        }

        if (process.ProvenanceConfidence is not "confirmed" and not "probable")
        {
            error = "vrss_gaze_provider provenance confidence is insufficient.";
            return false;
        }

        if (!string.Equals(process.ServiceIdentity, "PiServiceLauncher", StringComparison.OrdinalIgnoreCase)
            || process.ServiceBinaryPathClassification != "expectedPiServiceLauncherPath"
            || process.ServiceSignerClassification != "trustedSignedExpectedLauncher")
        {
            error = "PiServiceLauncher service identity or signer evidence is not approved for vrss_gaze_provider.";
            return false;
        }

        error = "";
        return true;
    }

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

    private static bool IsTrustedSupervisorProcess(PimaxShellQuiescenceProcessSnapshot process)
    {
        if (!TrustedSupervisorProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(process.SanitizedPath))
        {
            return string.Equals(process.ProcessName, "PimaxVrcSupervisorWatcher", StringComparison.OrdinalIgnoreCase);
        }

        if (process.SanitizedPath.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return process.SanitizedPath.Contains(@"\PimaxVrcSupervisor-TestDeployments\", StringComparison.OrdinalIgnoreCase)
            || process.SanitizedPath.Contains(@"\PimaxVrcSupervisor\", StringComparison.OrdinalIgnoreCase)
            || process.SanitizedPath.Contains(@"\New project\", StringComparison.OrdinalIgnoreCase)
            || process.SanitizedPath.StartsWith("<app>", StringComparison.OrdinalIgnoreCase);
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
        "lighthouse_console",
        "launcher",
        "fastlist-0.3.0-x64"
    ];

    private static readonly string[] TrustedSupervisorProcessNames =
    [
        "PimaxVrcSupervisor",
        "PimaxVrcSupervisorWatcher",
        "PimaxVrcSupervisorConfigurator",
        "PimaxVrcSupervisorSteamVrHost",
        "PimaxVrcSupervisorTui"
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
        var servicesByName = ServiceMetadata();
        var wmiProcesses = WmiProcesses();
        var raw = new List<RawProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    var wmi = wmiProcesses.GetValueOrDefault(process.Id);
                    var path = Safe(() => process.MainModule?.FileName) ?? wmi?.ExecutablePath;
                    if (!IsRelevant(name, path)) continue;
                    var parentId = ParentProcessId(process) ?? wmi?.ParentProcessId;
                    raw.Add(new RawProcess(process.Id, parentId, name, path, SafeStartTime(process) ?? wmi?.CreationTime, SafeSessionId(process) ?? wmi?.SessionId));
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
            var file = FileIdentity(process.ExecutablePath);
            var serviceIdentity = string.IsNullOrWhiteSpace(service) ? "none" : service;
            var servicePathClassification = "unknown";
            var serviceSignerClassification = "unknown";
            var provenanceSource = "notApplicable";
            var provenanceConfidence = "none";
            var parentState = parentName is null ? "parentUnknown" : "parentPresent";
            var classificationReason = "notClassified";
            if (string.Equals(process.ProcessName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase))
            {
                var vrss = ClassifyVrssProcess(process, parentName, byId, servicesByPid, servicesByName);
                ownership = vrss.OwnershipClassification;
                creator = vrss.CreatorClassification;
                serviceIdentity = vrss.ServiceIdentity;
                servicePathClassification = vrss.ServiceBinaryPathClassification;
                serviceSignerClassification = vrss.ServiceSignerClassification;
                provenanceSource = vrss.ProvenanceEvidenceSource;
                provenanceConfidence = vrss.ProvenanceConfidence;
                parentState = vrss.ParentState;
                classificationReason = vrss.ClassificationReason;
            }

            var sanitized = PimaxConnectivityRedactor.SanitizePath(file.CanonicalPath ?? process.ExecutablePath) ?? "";
            return new PimaxShellQuiescenceProcessSnapshot(
                process.ProcessName,
                sanitized,
                ownership,
                creator,
                serviceIdentity,
                InstallationRootClassification(file.CanonicalPath ?? process.ExecutablePath, process.ProcessName),
                StabilityKey(process.ProcessName, sanitized, ownership, creator, serviceIdentity, file.Sha256, SessionClassification(process.SessionId), CanonicalPathClassification(file.CanonicalPath, process.ProcessName), provenanceSource, provenanceConfidence),
                SessionClassification(process.SessionId),
                file.SignatureState,
                file.Sha256,
                file.FileSizeBytes,
                file.CreatedUtc,
                file.WrittenUtc,
                CanonicalPathClassification(file.CanonicalPath, process.ProcessName),
                provenanceSource,
                provenanceConfidence,
                parentState,
                serviceIdentity,
                servicePathClassification,
                serviceSignerClassification,
                classificationReason);
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

    private static Dictionary<int, WmiProcessSnapshot> WmiProcesses()
    {
        var result = new Dictionary<int, WmiProcessSnapshot>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CreationDate, SessionId FROM Win32_Process");
            foreach (ManagementObject process in searcher.Get().Cast<ManagementObject>())
            {
                var pid = Convert.ToInt32(process["ProcessId"]);
                if (pid <= 0) continue;
                var creation = WmiDate(process["CreationDate"]?.ToString());
                result[pid] = new WmiProcessSnapshot(
                    pid,
                    Convert.ToInt32(process["ParentProcessId"]),
                    process["Name"]?.ToString() ?? "",
                    process["ExecutablePath"]?.ToString(),
                    creation,
                    process["SessionId"] is null ? null : Convert.ToInt32(process["SessionId"]));
            }
        }
        catch
        {
        }

        return result;
    }

    private static Dictionary<string, ServiceMetadataSnapshot> ServiceMetadata()
    {
        var result = new Dictionary<string, ServiceMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, PathName, StartMode, StartName, State FROM Win32_Service");
            foreach (ManagementObject service in searcher.Get().Cast<ManagementObject>())
            {
                var name = service["Name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                var display = service["DisplayName"]?.ToString() ?? "";
                var path = NormalizeServiceExecutable(service["PathName"]?.ToString());
                if (!name.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("PiService", StringComparison.OrdinalIgnoreCase)
                    && !display.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
                    && !display.Contains("PiService", StringComparison.OrdinalIgnoreCase)
                    && path?.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                var file = FileIdentity(path);
                result[name] = new ServiceMetadataSnapshot(
                    name,
                    display,
                    file.CanonicalPath,
                    service["StartMode"]?.ToString() ?? "",
                    service["StartName"]?.ToString() ?? "",
                    service["State"]?.ToString() ?? "",
                    file.SignatureState);
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

    private static DateTime? WmiDate(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? null : ManagementDateTimeConverter.ToDateTime(value);
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

    private static VrssClassification ClassifyVrssProcess(
        RawProcess process,
        string? parentName,
        IReadOnlyDictionary<int, RawProcess> byId,
        IReadOnlyDictionary<int, string> servicesByPid,
        IReadOnlyDictionary<string, ServiceMetadataSnapshot> servicesByName)
    {
        var service = servicesByName.GetValueOrDefault("PiServiceLauncher");
        var servicePath = ServiceBinaryPathClassification(service?.CanonicalPath);
        var serviceSigner = ServiceSignerClassification(service);
        if (process.SessionId != 0)
        {
            return VrssClassification.Blocked("launchOrUserOwned", "interactiveOrUnknownSession", "PiServiceLauncher", servicePath, serviceSigner, "liveProcessSample", "none", parentName is null ? "parentUnknown" : "parentPresent", "VRSS is not running in session 0.");
        }

        var pathClassification = CanonicalPathClassification(FileIdentity(process.ExecutablePath).CanonicalPath, process.ProcessName);
        if (pathClassification != "exactExpectedRuntimePath")
        {
            return VrssClassification.Blocked("unknown", "unexpectedPath", "PiServiceLauncher", servicePath, serviceSigner, "liveProcessSample", "none", parentName is null ? "parentUnknown" : "parentPresent", "VRSS path is not the exact expected Runtime executable.");
        }

        if (servicePath != "expectedPiServiceLauncherPath" || serviceSigner != "trustedSignedExpectedLauncher")
        {
            return VrssClassification.Blocked("unknown", "serviceIdentityUntrusted", "PiServiceLauncher", servicePath, serviceSigner, "liveServiceConfiguration", "none", parentName is null ? "parentUnknown" : "parentPresent", "PiServiceLauncher service configuration or signer evidence is not trusted.");
        }

        if (string.Equals(parentName, "PiServiceLauncher", StringComparison.OrdinalIgnoreCase)
            && process.ParentProcessId is int parentId
            && byId.TryGetValue(parentId, out var parent)
            && string.Equals(servicesByPid.GetValueOrDefault(parent.ProcessId), "PiServiceLauncher", StringComparison.OrdinalIgnoreCase)
            && ServiceBinaryPathClassification(FileIdentity(parent.ExecutablePath).CanonicalPath) == "expectedPiServiceLauncherPath")
        {
            return new VrssClassification(
                "persistentServiceDescendant",
                "serviceControlManagerViaLiveLauncher",
                "PiServiceLauncher",
                servicePath,
                serviceSigner,
                "liveParentProcessAndServiceTable",
                "confirmed",
                "parentPresent",
                "VRSS is a live child of the trusted service-owned PiServiceLauncher.");
        }

        if (parentName is null)
        {
            return new VrssClassification(
                "persistentServiceDescendant",
                "persistentServiceDescendantFromPreservedEvidence",
                "PiServiceLauncher",
                servicePath,
                serviceSigner,
                "machineLocalServiceConfigurationAndOperatorConfirmedPhaseEvidence",
                "probable",
                "parentExitedOrUnavailable",
                "VRSS parent is no longer live; exact session/path/service evidence matches the operator-confirmed service-descendant lifecycle for this controlled phase.");
        }

        return string.Equals(parentName, "DeviceSetting", StringComparison.OrdinalIgnoreCase)
            ? VrssClassification.Blocked("deviceSettingOwned", "deviceSetting", "PiServiceLauncher", servicePath, serviceSigner, "liveParentProcess", "none", "parentPresent", "VRSS is owned by DeviceSetting, not the persistent service launcher lifecycle.")
            : VrssClassification.Blocked("launchOrUserOwned", "contradictoryLiveParent", "PiServiceLauncher", servicePath, serviceSigner, "liveParentProcess", "none", "parentPresent", "VRSS has a live parent that is not the trusted PiServiceLauncher service lifecycle.");
    }

    private static string InstallationRootClassification(string? path, string processName)
    {
        if (string.IsNullOrWhiteSpace(path)) return "unknown";
        var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax", "Runtime");
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax");
        if (string.Equals(processName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith(runtime + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return "expectedPimaxRuntimeRoot";
        }

        if (path.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) return "expectedPimaxRoot";
        if (path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase)) return "duplicateOrUnexpectedPimaxRoot";
        return "unknown";
    }

    private static string CanonicalPathClassification(string? path, string processName)
    {
        if (string.IsNullOrWhiteSpace(path)) return "unavailable";
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return "uncPath";
        if (path.Contains("..", StringComparison.Ordinal)) return "pathTraversalRejected";
        var expectedVrss = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax", "Runtime", "vrss_gaze_provider.exe");
        if (string.Equals(processName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase)
            && string.Equals(path, expectedVrss, StringComparison.OrdinalIgnoreCase))
        {
            return "exactExpectedRuntimePath";
        }

        return "notExactExpectedRuntimePath";
    }

    private static string ServiceBinaryPathClassification(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "unavailable";
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax", "Runtime", "PiServiceLauncher.exe");
        return string.Equals(path, expected, StringComparison.OrdinalIgnoreCase)
            ? "expectedPiServiceLauncherPath"
            : "unexpectedPiServiceLauncherPath";
    }

    private static string ServiceSignerClassification(ServiceMetadataSnapshot? service)
        => service is not null
            && ServiceBinaryPathClassification(service.CanonicalPath) == "expectedPiServiceLauncherPath"
            && service.SignatureState is "valid" or "signaturePresent"
                ? "trustedSignedExpectedLauncher"
                : "untrustedOrUnsignedLauncher";

    private static string SessionClassification(int? sessionId)
        => sessionId switch
        {
            0 => "session0",
            > 0 => "interactiveSession",
            _ => "unknown"
        };

    private static string StabilityKey(string processName, string sanitizedPath, string ownership, string creator, string serviceName, string sha256, string session, string pathClassification, string provenanceSource, string provenanceConfidence)
    {
        var basis = $"{processName}|{sanitizedPath}|{ownership}|{creator}|{serviceName}|{sha256}|{session}|{pathClassification}|{provenanceSource}|{provenanceConfidence}".ToLowerInvariant();
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

    private static DateTime? SafeStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return null; }
    }

    private static int? SafeSessionId(Process process)
    {
        try { return process.SessionId; }
        catch { return null; }
    }

    private static FileIdentitySnapshot FileIdentity(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return FileIdentitySnapshot.Unavailable;
            }

            var full = Path.GetFullPath(path);
            if (!File.Exists(full))
            {
                return FileIdentitySnapshot.Unavailable with { CanonicalPath = full };
            }

            var attributes = File.GetAttributes(full);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return FileIdentitySnapshot.Unavailable with { CanonicalPath = full, SignatureState = "reparsePointRejected" };
            }

            var info = new FileInfo(full);
            return new FileIdentitySnapshot(
                full,
                SignatureState(full),
                Hash(full),
                info.Length,
                info.CreationTimeUtc.ToString("O"),
                info.LastWriteTimeUtc.ToString("O"));
        }
        catch
        {
            return FileIdentitySnapshot.Unavailable;
        }
    }

    private static string SignatureState(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return string.IsNullOrWhiteSpace(certificate.Subject) ? "signaturePresent" : "signaturePresent";
        }
        catch (CryptographicException)
        {
            return "unsigned";
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string Hash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string? NormalizeServiceExecutable(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(image.Trim());
        if (expanded.StartsWith('"'))
        {
            var end = expanded.IndexOf('"', 1);
            return end > 1 ? expanded[1..end] : expanded.Trim('"');
        }

        var exe = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? expanded[..(exe + 4)] : expanded.Split(' ', 2)[0];
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

    private sealed record RawProcess(int ProcessId, int? ParentProcessId, string ProcessName, string? ExecutablePath, DateTime? StartTime, int? SessionId);
    private sealed record WmiProcessSnapshot(int ProcessId, int? ParentProcessId, string ProcessName, string? ExecutablePath, DateTime? CreationTime, int? SessionId);
    private sealed record ServiceMetadataSnapshot(string Name, string DisplayName, string? CanonicalPath, string StartMode, string StartName, string State, string SignatureState);
    private sealed record FileIdentitySnapshot(string? CanonicalPath, string SignatureState, string Sha256, long FileSizeBytes, string CreatedUtc, string WrittenUtc)
    {
        public static FileIdentitySnapshot Unavailable { get; } = new(null, "unavailable", "unavailable", 0, "", "");
    }

    private sealed record VrssClassification(
        string OwnershipClassification,
        string CreatorClassification,
        string ServiceIdentity,
        string ServiceBinaryPathClassification,
        string ServiceSignerClassification,
        string ProvenanceEvidenceSource,
        string ProvenanceConfidence,
        string ParentState,
        string ClassificationReason)
    {
        public static VrssClassification Blocked(
            string ownership,
            string creator,
            string serviceIdentity,
            string serviceBinaryPathClassification,
            string serviceSignerClassification,
            string provenanceEvidenceSource,
            string provenanceConfidence,
            string parentState,
            string reason)
            => new(ownership, creator, serviceIdentity, serviceBinaryPathClassification, serviceSignerClassification, provenanceEvidenceSource, provenanceConfidence, parentState, reason);
    }
}
