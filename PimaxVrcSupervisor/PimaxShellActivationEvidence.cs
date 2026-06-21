using System.Diagnostics;
using System.Management;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class PimaxShellActivationEvidenceCollectorSchema
{
    public const string Version = "pimax-shell-activation-evidence-collector-result-v1";
}

internal static class PimaxShellActivationEvidenceEnvelopeSchema
{
    public const string Version = "pimax-shell-activation-evidence-v1";
}

internal static class PimaxShellActivationEvidenceAssessmentSchema
{
    public const string Version = "pimax-shell-activation-evidence-assessment-v1";
}

internal static class PimaxShellActivationEvidencePurpose
{
    public const string ReadOnlyGateAssessment = "pimaxShellActivationReadOnlyGateAssessment";
}

internal sealed record PimaxShellActivationEvidenceCollectorResult(
    string Schema,
    DateTimeOffset CollectedAt,
    bool Accepted,
    string CorrelationId,
    int TtlSeconds,
    string EvidenceFile,
    string DirectorySecurityState,
    string FileSecurityState,
    int SampleCount,
    int StableSampleCount,
    string StableSetResult,
    string Purpose,
    bool ShellActivationRequested,
    bool ProcessMutationAttempted,
    bool ServiceMutationAttempted,
    bool UsbActionAttempted,
    bool DisplayPortActionAttempted,
    bool ConnectActionAttempted,
    PimaxShellActivationEvidencePublicProcess[] PublicProcessEvidence,
    PimaxShellActivationEvidenceServiceEvidence? ServiceEvidence,
    string[] Warnings,
    string[] Errors,
    string HumanReadableSummary);

internal sealed record PimaxShellActivationEvidenceAssessmentResult(
    string Schema,
    DateTimeOffset CollectedAt,
    bool Accepted,
    string CorrelationId,
    string EvidenceFile,
    bool EvidenceValid,
    bool EvidenceFresh,
    bool EvidenceConsumed,
    bool EvidenceFileDeleted,
    string OwnerValidationState,
    string AclValidationState,
    string ContentHashValidationState,
    string CurrentBindingState,
    string Purpose,
    PimaxShellActivationPreconditionSnapshot Precondition,
    PimaxShellActivationCapabilitySnapshot Capability,
    string VrssLivePath,
    string VrssSession,
    string VrssSha256,
    string VrssSignatureState,
    string ProvenanceSource,
    string ProvenanceConfidence,
    int ShellRequestCount,
    int RetryCount,
    bool FinalLiveActivationCorrelationIdGenerated,
    bool LiveObserverStarted,
    bool ActivationExecuted,
    bool BackendExecutable,
    bool AutomaticRecoveryAllowed,
    bool TuiExposureAllowed,
    bool ConfiguratorExposureAllowed,
    bool WatcherExecutionAllowed,
    string[] PrivacyRedactions,
    string[] Warnings,
    string[] Errors,
    string HumanReadableSummary);

internal sealed record PimaxShellActivationEvidenceEnvelope(
    string Schema,
    string EvidenceId,
    string CorrelationId,
    string Purpose,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int TtlSeconds,
    string CollectorBuildIdentity,
    string CollectorAssemblyVersion,
    string CollectorExecutableSha256,
    string RepositoryCommit,
    string PackageIdentity,
    string HostBootSessionToken,
    string ActiveInteractiveSessionClassification,
    string ExplorerSessionClassification,
    int SampleCount,
    double SampleIntervalSeconds,
    string StableSetResult,
    PimaxShellActivationEvidencePrivateBinding[] PrivateProcessBindings,
    PimaxShellActivationEvidencePublicProcess[] PublicSanitizedProcessEvidence,
    PimaxShellActivationEvidenceServiceEvidence ServiceEvidence,
    string ProvenanceClassification,
    string ProvenanceConfidence,
    string[] Warnings,
    string[] Errors,
    string[] PrivacyPolicy,
    string EnvelopeContentHash);

internal sealed record PimaxShellActivationEvidencePrivateBinding(
    string ProcessName,
    int RawProcessId,
    int RawParentProcessId,
    string CreationTimeUtc,
    int SessionId,
    string CanonicalExecutablePath,
    string VolumeFileIdentity,
    long FileSizeBytes,
    string LastWriteTimeUtc,
    string Sha256,
    string SignatureState,
    string ProcessInstanceStabilityToken);

internal sealed record PimaxShellActivationEvidencePublicProcess(
    string ProcessName,
    string SanitizedPath,
    string CanonicalPathClassification,
    string InstallationRootClassification,
    string SessionClassification,
    string SignatureState,
    string Sha256,
    long FileSizeBytes,
    string FileWrittenUtc,
    bool StableAcrossSamples,
    bool SingleInstance,
    bool ExpectedRuntimeRoot,
    bool ReparsePointRejected,
    bool UserWritablePath,
    string OwnershipClassification,
    string CreatorClassification,
    string ProvenanceSource,
    string ProvenanceConfidence,
    string ParentState,
    string ServiceIdentity,
    string ServiceBinaryPathClassification,
    string ServiceSignerClassification,
    string ClassificationReason);

internal sealed record PimaxShellActivationEvidenceServiceEvidence(
    string ServiceName,
    string DisplayName,
    string State,
    string StartMode,
    string ServiceAccount,
    string BinaryPath,
    string CanonicalBinaryPath,
    string ExpectedRootClassification,
    string Sha256,
    string SignatureState,
    string SignerTrustClassification,
    bool ServiceConfigurationAmbiguous,
    bool DuplicateLauncherServiceExists);

internal sealed record PimaxShellActivationEvidenceCommandLine(string? CorrelationId, int TtlSeconds, string? EvidenceFile, bool Fake)
{
    public static PimaxShellActivationEvidenceCommandLine Parse(string[] args)
    {
        var ttl = int.TryParse(Option(args, "--ttl-seconds"), out var parsed) ? parsed : 60;
        return new(Option(args, "--correlation-id"), ttl, Option(args, "--evidence-file"), HasFlag(args, "--fake"));
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

internal interface IPimaxShellActivationEvidenceContext
{
    bool IsWindows { get; }
    bool IsElevated { get; }
    bool IsInteractive { get; }
    bool IsLocalSystem { get; }
    bool IsSessionZero { get; }
    int CurrentSessionId { get; }
    bool ExplorerSessionMatched { get; }
    string ActiveUserSid { get; }
    string Summary { get; }
}

internal interface IPimaxShellActivationEvidenceStore
{
    string ProtectedDirectory { get; }
    string EvidencePath(Guid correlationId);
    PimaxShellActivationEvidenceStoreValidation ValidateDirectoryForCollect(string activeUserSid);
    PimaxShellActivationEvidenceStoreValidation ValidateEvidenceFileForAssess(string evidenceFile, string activeUserSid);
    string WriteEnvelopeAtomically(PimaxShellActivationEvidenceEnvelope envelope, string activeUserSid);
    PimaxShellActivationEvidenceEnvelope ReadEnvelope(string evidenceFile);
    bool DeleteConsumedEvidence(string evidenceFile);
}

internal sealed record PimaxShellActivationEvidenceStoreValidation(bool Accepted, string OwnerState, string AclState, string CanonicalPath, string[] Warnings, string[] Errors);

internal interface IPimaxShellActivationEvidenceCollectorProbe
{
    Task<PimaxShellActivationEvidenceCollectorProbeResult> CollectAsync(int sampleCount, TimeSpan sampleInterval, CancellationToken cancellationToken);
}

internal sealed record PimaxShellActivationEvidenceCollectorProbeResult(
    int SampleCount,
    int StableSampleCount,
    string StableSetResult,
    PimaxShellActivationEvidencePrivateBinding[] PrivateBindings,
    PimaxShellActivationEvidencePublicProcess[] PublicProcesses,
    PimaxShellActivationEvidenceServiceEvidence ServiceEvidence,
    string ProvenanceClassification,
    string ProvenanceConfidence,
    string[] Warnings,
    string[] Errors);

internal sealed class PimaxShellActivationEvidenceCoordinator(
    IPimaxShellActivationEvidenceContext? context = null,
    IPimaxShellActivationEvidenceStore? store = null,
    IPimaxShellActivationEvidenceCollectorProbe? collectorProbe = null,
    Func<SupervisorConfig, IPimaxShellActivationEvidenceStore, PimaxShellActivationEvidenceEnvelope, IPimaxShellQuiescenceProbe>? evidenceProbeFactory = null,
    Func<SupervisorConfig, IPimaxShellQuiescenceProbe, CancellationToken, Task<PimaxShellActivationCapabilitySnapshot>>? capabilityBuilder = null,
    Func<DateTimeOffset>? now = null)
{
    private static readonly string[] PrivacyPolicy = ["raw PIDs are private envelope fields only", "raw parent PIDs are private envelope fields only", "raw command lines are never collected", "user names and machine names are redacted", "certificate serial numbers are never emitted"];
    private readonly IPimaxShellActivationEvidenceContext _context = context ?? new WindowsPimaxShellActivationEvidenceContext();
    private readonly IPimaxShellActivationEvidenceStore _store = store ?? new WindowsPimaxShellActivationEvidenceStore();
    private readonly IPimaxShellActivationEvidenceCollectorProbe _collectorProbe = collectorProbe ?? new WindowsPimaxShellActivationEvidenceCollectorProbe();
    private readonly Func<SupervisorConfig, IPimaxShellActivationEvidenceStore, PimaxShellActivationEvidenceEnvelope, IPimaxShellQuiescenceProbe> _evidenceProbeFactory = evidenceProbeFactory ?? ((config, evidenceStore, envelope) => new PimaxShellActivationEvidenceQuiescenceProbe(config, evidenceStore, envelope));
    private readonly Func<SupervisorConfig, IPimaxShellQuiescenceProbe, CancellationToken, Task<PimaxShellActivationCapabilitySnapshot>> _capabilityBuilder = capabilityBuilder ?? ((supervisorConfig, probe, token) => new PimaxShellActivationCoordinator(quiescenceProbe: probe).BuildCapabilityAsync(supervisorConfig, token));
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);

    public async Task<PimaxShellActivationEvidenceCollectorResult> CollectElevatedAsync(PimaxShellActivationEvidenceCommandLine request, CancellationToken cancellationToken)
    {
        var collectedAt = _now();
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!Guid.TryParse(request.CorrelationId, out var correlationId))
        {
            errors.Add("A valid GUID correlation ID is required.");
        }

        var ttl = Math.Clamp(request.TtlSeconds, 30, 120);
        if (request.TtlSeconds is < 30 or > 120)
        {
            warnings.Add("TTL was bounded to the allowed 30-120 second range.");
        }

        if (!_context.IsWindows)
        {
            errors.Add("Elevated evidence collection is supported only on Windows.");
        }

        if (!_context.IsElevated)
        {
            errors.Add("Elevated evidence collection requires an existing administrator token and will not self-elevate.");
        }

        if (errors.Count > 0)
        {
            return CollectorRefused(collectedAt, request.CorrelationId ?? "", ttl, warnings, errors);
        }

        var directory = _store.ValidateDirectoryForCollect(_context.ActiveUserSid);
        warnings.AddRange(directory.Warnings);
        errors.AddRange(directory.Errors);
        if (!directory.Accepted)
        {
            return CollectorRefused(collectedAt, correlationId.ToString(), ttl, warnings, errors);
        }

        var probe = await _collectorProbe.CollectAsync(3, TimeSpan.FromSeconds(1), cancellationToken);
        warnings.AddRange(probe.Warnings);
        errors.AddRange(probe.Errors);
        var envelope = BuildEnvelope(correlationId, ttl, collectedAt, probe, warnings, errors);
        var writtenPath = errors.Count == 0 ? _store.WriteEnvelopeAtomically(envelope, _context.ActiveUserSid) : "";
        return new PimaxShellActivationEvidenceCollectorResult(
            PimaxShellActivationEvidenceCollectorSchema.Version,
            collectedAt,
            Accepted: errors.Count == 0,
            correlationId.ToString(),
            ttl,
            writtenPath,
            directory.OwnerState,
            directory.AclState,
            probe.SampleCount,
            probe.StableSampleCount,
            probe.StableSetResult,
            PimaxShellActivationEvidencePurpose.ReadOnlyGateAssessment,
            ShellActivationRequested: false,
            ProcessMutationAttempted: false,
            ServiceMutationAttempted: false,
            UsbActionAttempted: false,
            DisplayPortActionAttempted: false,
            ConnectActionAttempted: false,
            probe.PublicProcesses,
            probe.ServiceEvidence,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Count == 0
                ? "Protected read-only Shell activation evidence was collected for one normal assessment. No activation was requested."
                : "Protected read-only Shell activation evidence collection was refused.");
    }

    public async Task<PimaxShellActivationEvidenceAssessmentResult> AssessAsync(SupervisorConfig config, PimaxShellActivationEvidenceCommandLine request, CancellationToken cancellationToken)
    {
        var collectedAt = _now();
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!Guid.TryParse(request.CorrelationId, out var correlationId))
        {
            errors.Add("A valid GUID correlation ID is required.");
        }

        if (!_context.IsWindows || !_context.IsInteractive || _context.IsElevated || _context.IsLocalSystem || _context.IsSessionZero || !_context.ExplorerSessionMatched)
        {
            errors.Add("Evidence assessment requires a normal non-elevated interactive Explorer desktop session.");
        }

        var evidenceFile = request.EvidenceFile ?? (Guid.TryParse(request.CorrelationId, out var parsed) ? _store.EvidencePath(parsed) : "");
        var validation = _store.ValidateEvidenceFileForAssess(evidenceFile, _context.ActiveUserSid);
        warnings.AddRange(validation.Warnings);
        errors.AddRange(validation.Errors);
        PimaxShellActivationEvidenceEnvelope? envelope = null;
        if (validation.Accepted && errors.Count == 0)
        {
            envelope = _store.ReadEnvelope(evidenceFile);
            errors.AddRange(ValidateEnvelope(envelope, correlationId, collectedAt));
        }

        PimaxShellActivationPreconditionSnapshot precondition;
        PimaxShellActivationCapabilitySnapshot capability;
        if (envelope is not null && errors.Count == 0)
        {
            var probe = _evidenceProbeFactory(config, _store, envelope);
            capability = await _capabilityBuilder(config, probe, cancellationToken);
            precondition = capability.ActivationPrecondition ?? UnknownPrecondition(collectedAt, ["Capability did not return an embedded precondition result."]);
            if (!precondition.ReadinessForControlledValidation)
            {
                errors.Add("Verified evidence did not satisfy the Shell activation precondition.");
            }
        }
        else
        {
            precondition = UnknownPrecondition(collectedAt, errors);
            capability = new PimaxShellActivationCoordinator().BuildCapability([], precondition);
        }

        var deleted = false;
        if (envelope is not null && errors.Count == 0)
        {
            deleted = _store.DeleteConsumedEvidence(evidenceFile);
            if (!deleted)
            {
                errors.Add("Evidence file could not be deleted after assessment; one-time consumption refused.");
            }
        }

        var vrss = envelope?.PublicSanitizedProcessEvidence.FirstOrDefault(process => string.Equals(process.ProcessName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase));
        var accepted = envelope is not null && errors.Count == 0 && deleted && precondition.ReadinessForControlledValidation;
        var contentHashState = envelope is null
            ? "notValidated"
            : errors.Any(error => error.Contains("content hash", StringComparison.OrdinalIgnoreCase))
                ? "mismatch"
                : "valid";
        return new PimaxShellActivationEvidenceAssessmentResult(
            PimaxShellActivationEvidenceAssessmentSchema.Version,
            collectedAt,
            accepted,
            correlationId == Guid.Empty ? request.CorrelationId ?? "" : correlationId.ToString(),
            evidenceFile,
            EvidenceValid: envelope is not null && errors.Count == 0,
            EvidenceFresh: envelope is not null && collectedAt <= envelope.ExpiresAt,
            EvidenceConsumed: deleted,
            EvidenceFileDeleted: deleted,
            validation.OwnerState,
            validation.AclState,
            contentHashState,
            accepted ? "boundToCurrentSession0Process" : "notAccepted",
            PimaxShellActivationEvidencePurpose.ReadOnlyGateAssessment,
            precondition,
            capability,
            vrss?.SanitizedPath ?? "unavailable",
            vrss?.SessionClassification ?? "unavailable",
            vrss?.Sha256 ?? "unavailable",
            vrss?.SignatureState ?? "unavailable",
            vrss?.ProvenanceSource ?? "unavailable",
            vrss?.ProvenanceConfidence ?? "unavailable",
            ShellRequestCount: 0,
            RetryCount: 0,
            FinalLiveActivationCorrelationIdGenerated: false,
            LiveObserverStarted: false,
            ActivationExecuted: false,
            BackendExecutable: false,
            AutomaticRecoveryAllowed: false,
            TuiExposureAllowed: false,
            ConfiguratorExposureAllowed: false,
            WatcherExecutionAllowed: false,
            PrivacyPolicy,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            accepted
                ? "Evidence was verified, consumed once, and the read-only Shell activation gate is ready. No activation was performed."
                : "Evidence assessment was refused before Shell activation.");
    }

    internal PimaxShellActivationEvidenceEnvelope BuildEnvelope(Guid correlationId, int ttlSeconds, DateTimeOffset issuedAt, PimaxShellActivationEvidenceCollectorProbeResult probe, IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
    {
        var envelope = new PimaxShellActivationEvidenceEnvelope(
            PimaxShellActivationEvidenceEnvelopeSchema.Version,
            $"pimax-shell-evidence-{correlationId:N}",
            correlationId.ToString(),
            PimaxShellActivationEvidencePurpose.ReadOnlyGateAssessment,
            issuedAt,
            issuedAt.AddSeconds(ttlSeconds),
            ttlSeconds,
            BuildIdentity(),
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            SafeHash(Environment.ProcessPath),
            RepositoryCommit(),
            PackageIdentity(),
            BootSessionToken(),
            _context.Summary,
            _context.ExplorerSessionMatched ? $"explorerSession:{_context.CurrentSessionId}" : "explorerSessionMismatch",
            probe.SampleCount,
            1,
            probe.StableSetResult,
            probe.PrivateBindings,
            probe.PublicProcesses,
            probe.ServiceEvidence,
            probe.ProvenanceClassification,
            probe.ProvenanceConfidence,
            warnings.ToArray(),
            errors.ToArray(),
            PrivacyPolicy,
            "");
        return envelope with { EnvelopeContentHash = ComputeEnvelopeHash(envelope) };
    }

    private static string[] ValidateEnvelope(PimaxShellActivationEvidenceEnvelope envelope, Guid correlationId, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (envelope.Schema != PimaxShellActivationEvidenceEnvelopeSchema.Version) errors.Add("Evidence envelope schema mismatch.");
        if (!string.Equals(envelope.CorrelationId, correlationId.ToString(), StringComparison.OrdinalIgnoreCase)) errors.Add("Evidence correlation ID mismatch.");
        if (envelope.Purpose != PimaxShellActivationEvidencePurpose.ReadOnlyGateAssessment) errors.Add("Evidence purpose mismatch.");
        if (now > envelope.ExpiresAt) errors.Add("Evidence envelope expired.");
        if (envelope.IssuedAt > now.AddSeconds(5)) errors.Add("Evidence issued-at timestamp is in the future.");
        if (ComputeEnvelopeHash(envelope with { EnvelopeContentHash = "" }) != envelope.EnvelopeContentHash) errors.Add("Evidence content hash mismatch.");
        if (envelope.PrivateProcessBindings.Count(binding => string.Equals(binding.ProcessName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase)) != 1) errors.Add("Evidence must contain exactly one private VRSS binding.");
        return errors.ToArray();
    }

    private static string ComputeEnvelopeHash(PimaxShellActivationEvidenceEnvelope envelope)
    {
        var canonical = envelope with { EnvelopeContentHash = "" };
        var json = JsonSerializer.Serialize(canonical, PimaxRepairJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private PimaxShellActivationEvidenceCollectorResult CollectorRefused(DateTimeOffset collectedAt, string correlationId, int ttl, IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
        => new(
            PimaxShellActivationEvidenceCollectorSchema.Version,
            collectedAt,
            Accepted: false,
            correlationId,
            ttl,
            EvidenceFile: "",
            DirectorySecurityState: "notValidated",
            FileSecurityState: "notCreated",
            SampleCount: 0,
            StableSampleCount: 0,
            StableSetResult: "notCollected",
            PimaxShellActivationEvidencePurpose.ReadOnlyGateAssessment,
            ShellActivationRequested: false,
            ProcessMutationAttempted: false,
            ServiceMutationAttempted: false,
            UsbActionAttempted: false,
            DisplayPortActionAttempted: false,
            ConnectActionAttempted: false,
            [],
            null,
            warnings.ToArray(),
            errors.ToArray(),
            "Protected read-only Shell activation evidence collection was refused.");

    private static PimaxShellActivationPreconditionSnapshot UnknownPrecondition(DateTimeOffset collectedAt, IReadOnlyList<string> errors)
        => new(
            PimaxShellActivationPreconditionSchema.Version,
            collectedAt,
            $"pimax-shell-precondition-evidence-refused-{Guid.NewGuid():N}",
            PimaxHealthOverallStatus.Unknown,
            PimaxSoftwareGroupState.Unknown,
            PimaxShellActivationPreconditionState.Unknown,
            Quiescent: false,
            Stable: false,
            StableSampleCount: 0,
            RequiredStableSampleCount: 3,
            SampleIntervalSeconds: 1,
            CoreMembersPresent: [],
            LaunchOwnedMembersPresent: [],
            PermittedPersistentMembersPresent: [],
            UnclassifiedMembersPresent: [],
            OwnershipEvidence: [],
            PiServiceLauncherClassification: "notObserved",
            RegistrationEvidenceState: PimaxRegistrationState.Unknown,
            StaleRegistrationBlocking: true,
            DuplicateInstallationEvidence: "unknown",
            RecoveryLeaseState: "unknown",
            ShellEntryTrustState: PimaxShellActivationCapabilityState.ShellEntryNotFound,
            ReadinessForControlledValidation: false,
            BackendExecutable: false,
            AutomaticRecoveryAllowed: false,
            Warnings: [],
            Errors: errors.ToArray(),
            PrivacyRedactions: PrivacyPolicy,
            HumanReadableSummary: "Evidence-backed precondition could not be assessed.");

    private static string BuildIdentity()
        => Assembly.GetExecutingAssembly().GetName().Name ?? "PimaxVrcSupervisor";

    private static string PackageIdentity()
        => Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);

    private static string RepositoryCommit()
    {
        try
        {
            var start = new DirectoryInfo(AppContext.BaseDirectory);
            for (var dir = start; dir is not null; dir = dir.Parent)
            {
                var git = Path.Combine(dir.FullName, ".git");
                if (!Directory.Exists(git) && !File.Exists(git)) continue;
                var head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
                if (!head.StartsWith("ref:", StringComparison.OrdinalIgnoreCase)) return head;
                var refPath = head[4..].Trim().Replace('/', Path.DirectorySeparatorChar);
                var fullRef = Path.Combine(git, refPath);
                return File.Exists(fullRef) ? File.ReadAllText(fullRef).Trim() : "unknown";
            }
        }
        catch
        {
        }

        return "unknown";
    }

    private static string BootSessionToken()
        => Environment.TickCount64 <= 0 ? "unknown" : $"tick64:{Environment.TickCount64 / 1000 / 60}";

    private static string SafeHash(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "unavailable";
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return "unavailable";
        }
    }
}

internal sealed class PimaxShellActivationEvidenceQuiescenceProbe(
    SupervisorConfig config,
    IPimaxShellActivationEvidenceStore evidenceStore,
    PimaxShellActivationEvidenceEnvelope envelope) : IPimaxShellQuiescenceProbe
{
    private readonly WindowsPimaxShellQuiescenceProbe _windowsProbe = new();

    public async Task<PimaxShellQuiescenceSample> CollectAsync(SupervisorConfig ignored, CancellationToken cancellationToken)
    {
        GC.KeepAlive(evidenceStore);
        var baseSample = await _windowsProbe.CollectAsync(config, cancellationToken);
        var binding = envelope.PrivateProcessBindings.Single(binding => string.Equals(binding.ProcessName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase));
        if (!CurrentBindingStillMatches(binding))
        {
            return baseSample;
        }

        var vrss = envelope.PublicSanitizedProcessEvidence.Single(process => string.Equals(process.ProcessName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase));
        var replacement = new PimaxShellQuiescenceProcessSnapshot(
            vrss.ProcessName,
            vrss.SanitizedPath,
            vrss.OwnershipClassification,
            vrss.CreatorClassification,
            vrss.ServiceIdentity,
            vrss.InstallationRootClassification,
            binding.ProcessInstanceStabilityToken,
            vrss.SessionClassification,
            vrss.SignatureState,
            vrss.Sha256,
            vrss.FileSizeBytes,
            FileCreatedUtc: binding.CreationTimeUtc,
            FileWrittenUtc: vrss.FileWrittenUtc,
            vrss.CanonicalPathClassification,
            "elevatedReadOnlyEvidenceAndPreservedObservation",
            vrss.ProvenanceConfidence,
            vrss.ParentState,
            vrss.ServiceIdentity,
            vrss.ServiceBinaryPathClassification,
            vrss.ServiceSignerClassification,
            vrss.ClassificationReason);
        var processes = baseSample.Processes
            .Where(process => !string.Equals(process.ProcessName, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase))
            .Append(replacement)
            .ToArray();
        return baseSample with { Processes = processes };
    }

    private static bool CurrentBindingStillMatches(PimaxShellActivationEvidencePrivateBinding binding)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT ProcessId, CreationDate, SessionId, ExecutablePath FROM Win32_Process WHERE ProcessId = {binding.RawProcessId}");
            var process = searcher.Get().Cast<ManagementObject>().SingleOrDefault();
            if (process is null) return false;
            var creation = WmiDate(process["CreationDate"]?.ToString())?.ToUniversalTime().ToString("O") ?? "";
            var session = process["SessionId"] is null ? -1 : Convert.ToInt32(process["SessionId"]);
            var path = process["ExecutablePath"]?.ToString() ?? "";
            return session == binding.SessionId
                && string.Equals(creation, binding.CreationTimeUtc, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFullPath(path), binding.CanonicalExecutablePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(SafeHash(path), binding.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static DateTime? WmiDate(string? value)
    {
        try { return string.IsNullOrWhiteSpace(value) ? null : ManagementDateTimeConverter.ToDateTime(value); }
        catch { return null; }
    }

    private static string SafeHash(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "unavailable";
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return "unavailable";
        }
    }
}

internal sealed class WindowsPimaxShellActivationEvidenceContext : IPimaxShellActivationEvidenceContext
{
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsElevated { get; }
    public bool IsInteractive { get; }
    public bool IsLocalSystem { get; }
    public bool IsSessionZero { get; }
    public int CurrentSessionId { get; }
    public bool ExplorerSessionMatched { get; }
    public string ActiveUserSid { get; }
    public string Summary { get; }

    public WindowsPimaxShellActivationEvidenceContext()
    {
        if (!OperatingSystem.IsWindows())
        {
            IsInteractive = Environment.UserInteractive;
            CurrentSessionId = -1;
            ActiveUserSid = "unknown";
            Summary = "nonWindows";
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        IsElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        IsLocalSystem = identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;
        IsInteractive = Environment.UserInteractive;
        CurrentSessionId = SafeCurrentSessionId();
        IsSessionZero = CurrentSessionId == 0;
        ExplorerSessionMatched = ExplorerSessions().Contains(CurrentSessionId);
        ActiveUserSid = identity.User?.Value ?? "unknown";
        Summary = ExplorerSessionMatched ? $"interactiveSession:{CurrentSessionId}" : $"session:{CurrentSessionId};explorerMismatch";
    }

    private static int SafeCurrentSessionId()
    {
        try { return Process.GetCurrentProcess().SessionId; }
        catch { return -1; }
    }

    private static int[] ExplorerSessions()
    {
        try
        {
            return Process.GetProcessesByName("explorer")
                .Select(process =>
                {
                    try { return process.SessionId; }
                    catch { return -1; }
                    finally { process.Dispose(); }
                })
                .Where(session => session >= 0)
                .Distinct()
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}

internal sealed class WindowsPimaxShellActivationEvidenceStore : IPimaxShellActivationEvidenceStore
{
    public string ProtectedDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PimaxVrcSupervisor", "ValidationEvidence");

    public string EvidencePath(Guid correlationId)
        => Path.Combine(ProtectedDirectory, $"pimax-shell-evidence-{correlationId}.json");

    public PimaxShellActivationEvidenceStoreValidation ValidateDirectoryForCollect(string activeUserSid)
    {
        try
        {
            var full = Path.GetFullPath(ProtectedDirectory);
            if (!full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase))
            {
                return Refused(full, "wrongOwner", "outsideProgramData", "Protected evidence directory must stay under ProgramData.");
            }

            Directory.CreateDirectory(full);
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            {
                return Refused(full, "notValidated", "reparsePointRejected", "Protected evidence directory is a reparse point.");
            }

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(activeUserSid), FileSystemRights.ReadAndExecute, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(full).SetAccessControl(security);
            return new(true, "administrators", "protectedNoStandardUserCreate", full, [], []);
        }
        catch (Exception ex)
        {
            return Refused(ProtectedDirectory, "notValidated", "aclValidationFailed", ex.GetType().Name + ": " + ex.Message);
        }
    }

    public PimaxShellActivationEvidenceStoreValidation ValidateEvidenceFileForAssess(string evidenceFile, string activeUserSid)
    {
        try
        {
            var full = Path.GetFullPath(evidenceFile);
            var expectedRoot = Path.GetFullPath(ProtectedDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return Refused(full, "notValidated", "outsideProtectedDirectory", "Evidence file must be in the fixed protected ProgramData directory.");
            }

            if (Path.GetFileName(full).Contains(':', StringComparison.Ordinal))
            {
                return Refused(full, "notValidated", "alternateDataStreamRejected", "Alternate data stream evidence paths are refused.");
            }

            if (!File.Exists(full))
            {
                return Refused(full, "notValidated", "missingEvidenceFile", "Evidence file was not found.");
            }

            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            {
                return Refused(full, "notValidated", "reparsePointRejected", "Evidence file is a reparse point.");
            }

            var security = new FileInfo(full).GetAccessControl();
            var owner = security.GetOwner(typeof(SecurityIdentifier))?.Value ?? "unknown";
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            var userCanModify = rules.Any(rule => string.Equals(rule.IdentityReference.Value, activeUserSid, StringComparison.OrdinalIgnoreCase)
                && rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl | FileSystemRights.CreateFiles | FileSystemRights.AppendData)) != 0);
            if (userCanModify)
            {
                return Refused(full, owner, "standardUserModifyAllowed", "Evidence ACL allows the caller to modify the envelope.");
            }

            return new(true, owner, "readDeleteOnly", full, [], []);
        }
        catch (Exception ex)
        {
            return Refused(evidenceFile, "notValidated", "aclValidationFailed", ex.GetType().Name + ": " + ex.Message);
        }
    }

    public string WriteEnvelopeAtomically(PimaxShellActivationEvidenceEnvelope envelope, string activeUserSid)
    {
        var finalPath = EvidencePath(Guid.Parse(envelope.CorrelationId));
        var full = Path.GetFullPath(finalPath);
        if (File.Exists(full)) throw new IOException("Final evidence file already exists.");
        var temp = Path.Combine(ProtectedDirectory, $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(envelope, PimaxRepairJson.Options);
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        var fileSecurity = new FileSecurity();
        fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        fileSecurity.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        fileSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        fileSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        fileSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(activeUserSid), FileSystemRights.ReadData | FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadPermissions | FileSystemRights.Delete, AccessControlType.Allow));
        new FileInfo(temp).SetAccessControl(fileSecurity);
        File.Move(temp, full);
        return full;
    }

    public PimaxShellActivationEvidenceEnvelope ReadEnvelope(string evidenceFile)
        => JsonSerializer.Deserialize<PimaxShellActivationEvidenceEnvelope>(File.ReadAllText(evidenceFile), PimaxRepairJson.Options)
            ?? throw new InvalidDataException("Evidence envelope could not be parsed.");

    public bool DeleteConsumedEvidence(string evidenceFile)
    {
        try
        {
            File.Delete(evidenceFile);
            return !File.Exists(evidenceFile);
        }
        catch
        {
            return false;
        }
    }

    private static PimaxShellActivationEvidenceStoreValidation Refused(string path, string owner, string acl, string error)
        => new(false, owner, acl, path, [], [error]);
}

internal sealed class WindowsPimaxShellActivationEvidenceCollectorProbe : IPimaxShellActivationEvidenceCollectorProbe
{
    private static readonly string[] RelevantNames =
    [
        "PimaxClient", "DeviceSetting", "PiPlayService", "PiService", "pi_server", "PVRHome", "pi_overlay",
        "lighthouse_console", "launcher", "fastlist-0.3.0-x64", "PiServiceLauncher", "PiPlatformService_64",
        "platform_runtime_VR4PIMAXP3B_service", "vrss_gaze_provider"
    ];

    public async Task<PimaxShellActivationEvidenceCollectorProbeResult> CollectAsync(int sampleCount, TimeSpan sampleInterval, CancellationToken cancellationToken)
    {
        var samples = new List<RawProcess[]>();
        for (var index = 0; index < sampleCount; index++)
        {
            samples.Add(CollectProcesses());
            if (index + 1 < sampleCount)
            {
                await Task.Delay(sampleInterval, cancellationToken);
            }
        }

        var last = samples[^1];
        var service = CollectLauncherService();
        var vrss = last.Where(process => string.Equals(process.NameWithoutExtension, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase)).ToArray();
        var privateBindings = vrss.Select(process => PrivateBinding(process)).ToArray();
        var stableCount = StableVrssCount(samples);
        var publicProcesses = last
            .Where(process => RelevantNames.Contains(process.NameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                || process.ExecutablePath?.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) == true)
            .Select(process => PublicProcess(process, service, stableCount >= sampleCount, vrss.Length == 1))
            .ToArray();
        var errors = new List<string>();
        if (vrss.Length != 1) errors.Add("Elevated collection requires exactly one live VRSS process.");
        if (stableCount < sampleCount) errors.Add("VRSS process identity did not remain stable across all elevated samples.");
        if (service.SignerTrustClassification != "trustedSignedExpectedLauncher") errors.Add("PiServiceLauncher service configuration or signer is not trusted.");
        return new PimaxShellActivationEvidenceCollectorProbeResult(
            sampleCount,
            stableCount,
            stableCount >= sampleCount ? "stable" : "unstable",
            privateBindings,
            publicProcesses,
            service,
            "preservedElevatedObservation",
            "probable",
            [],
            errors.ToArray());
    }

    private static int StableVrssCount(IReadOnlyList<RawProcess[]> samples)
    {
        var keys = samples
            .Select(sample => sample.Where(process => string.Equals(process.NameWithoutExtension, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase)).Select(StabilityToken).Order(StringComparer.Ordinal).ToArray())
            .ToArray();
        if (keys.Length == 0) return 0;
        var last = string.Join("|", keys[^1]);
        var count = 0;
        for (var index = keys.Length - 1; index >= 0; index--)
        {
            if (string.Join("|", keys[index]) != last) break;
            count++;
        }

        return count;
    }

    private static RawProcess[] CollectProcesses()
    {
        var result = new List<RawProcess>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CreationDate, SessionId FROM Win32_Process");
            foreach (ManagementObject process in searcher.Get().Cast<ManagementObject>())
            {
                var name = process["Name"]?.ToString() ?? "";
                var path = process["ExecutablePath"]?.ToString();
                var bareName = Path.GetFileNameWithoutExtension(name);
                if (!RelevantNames.Contains(bareName, StringComparer.OrdinalIgnoreCase)
                    && path?.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                result.Add(new RawProcess(
                    Convert.ToInt32(process["ProcessId"]),
                    Convert.ToInt32(process["ParentProcessId"]),
                    name,
                    bareName,
                    path,
                    WmiDate(process["CreationDate"]?.ToString()),
                    process["SessionId"] is null ? -1 : Convert.ToInt32(process["SessionId"])));
            }
        }
        catch
        {
        }

        return result.ToArray();
    }

    private static PimaxShellActivationEvidencePrivateBinding PrivateBinding(RawProcess process)
    {
        var file = FileIdentity(process.ExecutablePath);
        return new(
            process.NameWithoutExtension,
            process.ProcessId,
            process.ParentProcessId,
            process.CreationTime?.ToUniversalTime().ToString("O") ?? "",
            process.SessionId,
            file.CanonicalPath,
            file.VolumeFileIdentity,
            file.FileSizeBytes,
            file.FileWrittenUtc,
            file.Sha256,
            file.SignatureState,
            StabilityToken(process));
    }

    private static PimaxShellActivationEvidencePublicProcess PublicProcess(RawProcess process, PimaxShellActivationEvidenceServiceEvidence service, bool stable, bool singleVrss)
    {
        var file = FileIdentity(process.ExecutablePath);
        var isVrss = string.Equals(process.NameWithoutExtension, "vrss_gaze_provider", StringComparison.OrdinalIgnoreCase);
        var exactVrss = isVrss && string.Equals(file.CanonicalPath, ExpectedVrssPath(), StringComparison.OrdinalIgnoreCase);
        return new(
            process.NameWithoutExtension,
            SanitizePath(file.CanonicalPath),
            exactVrss ? "exactExpectedRuntimePath" : "notExactExpectedRuntimePath",
            exactVrss ? "expectedPimaxRuntimeRoot" : InstallationRoot(file.CanonicalPath),
            process.SessionId == 0 ? "session0" : process.SessionId > 0 ? "interactiveSession" : "unknown",
            file.SignatureState,
            file.Sha256,
            file.FileSizeBytes,
            file.FileWrittenUtc,
            StableAcrossSamples: !isVrss || stable,
            SingleInstance: !isVrss || singleVrss,
            ExpectedRuntimeRoot: exactVrss,
            ReparsePointRejected: file.ReparsePointRejected,
            UserWritablePath: UserWritable(file.CanonicalPath),
            isVrss && process.SessionId == 0 && exactVrss ? "persistentServiceDescendant" : "unknown",
            isVrss && process.SessionId == 0 && exactVrss ? "serviceControlManagerViaExitedLauncher" : "unknown",
            isVrss ? "elevatedReadOnlyEvidenceAndPreservedObservation" : "liveElevatedSample",
            isVrss ? "probable" : "none",
            isVrss ? "parentExitedOrUnavailable" : "unknown",
            isVrss ? "PiServiceLauncher" : "none",
            service.ExpectedRootClassification == "expectedPiServiceLauncherPath" ? "expectedPiServiceLauncherPath" : "unexpectedPiServiceLauncherPath",
            service.SignerTrustClassification,
            isVrss ? "Session-0 VRSS identity was proven by elevated read-only evidence and preserved service-descendant observation." : "Elevated current process-set evidence.");
    }

    private static PimaxShellActivationEvidenceServiceEvidence CollectLauncherService()
    {
        var services = new List<PimaxShellActivationEvidenceServiceEvidence>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, PathName, StartMode, StartName, State FROM Win32_Service WHERE Name = 'PiServiceLauncher' OR DisplayName LIKE '%PiServiceLauncher%'");
            foreach (ManagementObject service in searcher.Get().Cast<ManagementObject>())
            {
                var rawPath = service["PathName"]?.ToString() ?? "";
                var canonical = NormalizeServiceExecutable(rawPath);
                var file = FileIdentity(canonical);
                var pathClass = string.Equals(file.CanonicalPath, ExpectedLauncherPath(), StringComparison.OrdinalIgnoreCase) ? "expectedPiServiceLauncherPath" : "unexpectedPiServiceLauncherPath";
                var signer = pathClass == "expectedPiServiceLauncherPath" && file.SignatureState is "valid" or "signaturePresent" ? "trustedSignedExpectedLauncher" : "untrustedOrUnsignedLauncher";
                services.Add(new PimaxShellActivationEvidenceServiceEvidence(
                    service["Name"]?.ToString() ?? "",
                    service["DisplayName"]?.ToString() ?? "",
                    service["State"]?.ToString() ?? "",
                    service["StartMode"]?.ToString() ?? "",
                    service["StartName"]?.ToString() ?? "",
                    SanitizePath(rawPath),
                    file.CanonicalPath,
                    pathClass,
                    file.Sha256,
                    file.SignatureState,
                    signer,
                    ServiceConfigurationAmbiguous: false,
                    DuplicateLauncherServiceExists: false));
            }
        }
        catch
        {
        }

        if (services.Count == 1)
        {
            return services[0];
        }

        return new PimaxShellActivationEvidenceServiceEvidence(
            "PiServiceLauncher",
            "",
            "unknown",
            "unknown",
            "unknown",
            "",
            "",
            "unavailable",
            "unavailable",
            "unavailable",
            "untrustedOrUnsignedLauncher",
            ServiceConfigurationAmbiguous: services.Count > 1,
            DuplicateLauncherServiceExists: services.Count > 1);
    }

    private static FileIdentityResult FileIdentity(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return FileIdentityResult.Unavailable;
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) return FileIdentityResult.Unavailable with { CanonicalPath = full };
            var attributes = File.GetAttributes(full);
            var info = new FileInfo(full);
            return new FileIdentityResult(
                full,
                VolumeFileIdentity: $"{info.Length}:{info.LastWriteTimeUtc.Ticks}",
                SignatureState(full),
                SafeHash(full),
                info.Length,
                info.LastWriteTimeUtc.ToString("O"),
                (attributes & FileAttributes.ReparsePoint) != 0);
        }
        catch
        {
            return FileIdentityResult.Unavailable;
        }
    }

    private static string StabilityToken(RawProcess process)
    {
        var basis = $"{process.ProcessId}|{process.CreationTime?.ToUniversalTime():O}|{process.SessionId}|{process.ExecutablePath}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(basis)))[..16].ToLowerInvariant();
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

    private static string SafeHash(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "unavailable";
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return "unavailable";
        }
    }

    private static DateTime? WmiDate(string? value)
    {
        try { return string.IsNullOrWhiteSpace(value) ? null : ManagementDateTimeConverter.ToDateTime(value); }
        catch { return null; }
    }

    private static string NormalizeServiceExecutable(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return "";
        var expanded = Environment.ExpandEnvironmentVariables(image.Trim());
        if (expanded.StartsWith('"'))
        {
            var end = expanded.IndexOf('"', 1);
            return end > 1 ? expanded[1..end] : expanded.Trim('"');
        }

        var exe = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? expanded[..(exe + 4)] : expanded.Split(' ', 2)[0];
    }

    private static string SanitizePath(string path)
    {
        const string pimaxRoot = @"C:\Program Files\Pimax";
        return path.StartsWith(pimaxRoot, StringComparison.OrdinalIgnoreCase)
            ? "<pimax>" + path[pimaxRoot.Length..]
            : PimaxConnectivityRedactor.SanitizePath(path) ?? "";
    }

    private static string InstallationRoot(string path)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax");
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "expectedPimaxRoot";
        return path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) ? "duplicateOrUnexpectedPimaxRoot" : "unknown";
    }

    private static bool UserWritable(string path)
        => !path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);

    private static string ExpectedVrssPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax", "Runtime", "vrss_gaze_provider.exe");

    private static string ExpectedLauncherPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax", "Runtime", "PiServiceLauncher.exe");

    private sealed record RawProcess(int ProcessId, int ParentProcessId, string Name, string NameWithoutExtension, string? ExecutablePath, DateTime? CreationTime, int SessionId);
    private sealed record FileIdentityResult(string CanonicalPath, string VolumeFileIdentity, string SignatureState, string Sha256, long FileSizeBytes, string FileWrittenUtc, bool ReparsePointRejected)
    {
        public static FileIdentityResult Unavailable { get; } = new("", "unavailable", "unavailable", "unavailable", 0, "", false);
    }
}
