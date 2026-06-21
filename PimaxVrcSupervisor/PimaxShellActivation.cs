using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

internal static class PimaxShellActivationCapabilitySchema
{
    public const string Version = "pimax-shell-activation-capability-v1";
}

internal static class PimaxShellActivationResultSchema
{
    public const string Version = "pimax-shell-activation-result-v1";
}

internal static class PimaxShellActivationCapabilityState
{
    public const string UnsupportedPlatform = "unsupportedPlatform";
    public const string ShellEntryNotFound = "shellEntryNotFound";
    public const string ShellEntryAmbiguous = "shellEntryAmbiguous";
    public const string ShellEntryUntrusted = "shellEntryUntrusted";
    public const string ShellEntryInvalid = "shellEntryInvalid";
    public const string SoftwareGroupAlreadyRunning = "softwareGroupAlreadyRunning";
    public const string SoftwareGroupPartial = "softwareGroupPartial";
    public const string SoftwareGroupStateUnknown = "softwareGroupStateUnknown";
    public const string ReadyForControlledValidation = "readyForControlledValidation";
    public const string Validated = "validated";
    public const string DisabledByPolicy = "disabledByPolicy";
}

internal static class PimaxShellActivationState
{
    public const string NotStarted = "notStarted";
    public const string ValidatingPreconditions = "validatingPreconditions";
    public const string RequestingShellActivation = "requestingShellActivation";
    public const string ActivationRequested = "activationRequested";
    public const string WaitingForPimaxClient = "waitingForPimaxClient";
    public const string WaitingForDeviceSetting = "waitingForDeviceSetting";
    public const string WaitingForRuntimeGroup = "waitingForRuntimeGroup";
    public const string Stabilizing = "stabilizing";
    public const string Healthy = "healthy";
    public const string Partial = "partial";
    public const string Conflicting = "conflicting";
    public const string TimedOut = "timedOut";
    public const string Refused = "refused";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

internal sealed record PimaxShellActivationCapabilitySnapshot(
    string Schema,
    DateTimeOffset CollectedAt,
    string CapabilityState,
    int CandidateCount,
    PimaxShellActivationCandidate[] Candidates,
    PimaxShellActivationCandidate? SelectedShellEntry,
    string SanitizedShortcutPath,
    string SanitizedTargetPath,
    string ShortcutSourceLocation,
    string TargetProduct,
    string TargetVersion,
    string SignerTrustSummary,
    string ShortcutArgumentsState,
    string ShortcutWorkingDirectoryState,
    string ActivationMethod,
    bool DirectExecutableFallbackAllowed,
    bool RuntimeComponentFallbackAllowed,
    bool ServiceMutationAllowed,
    bool RetryAllowed,
    bool ElevationRequired,
    string CurrentSoftwareGroupState,
    string CurrentComponentHealthState,
    string PreconditionResult,
    bool BackendExecutable,
    bool ReadinessForControlledValidation,
    string[] Warnings,
    string[] Errors,
    string HumanReadableSummary);

internal sealed record PimaxShellActivationCandidate(
    string CandidateId,
    string DisplayName,
    string SourceLocation,
    string SanitizedShortcutPath,
    string SanitizedTargetPath,
    bool IsShellShortcut,
    bool DisplayNameMatches,
    bool TargetPathMatches,
    bool TargetExists,
    bool ArgumentsAllowed,
    bool WorkingDirectoryMatches,
    bool TargetKindAllowed,
    bool TrustMatches,
    string TargetProduct,
    string TargetVersion,
    string SignerTrustSummary,
    string ShortcutArgumentsState,
    string ShortcutWorkingDirectoryState,
    string ValidationState,
    string[] Blockers);

internal sealed record PimaxShellActivationResultSnapshot(
    string Schema,
    string OperationId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string State,
    bool Accepted,
    bool ConfirmationAccepted,
    string PolicyResult,
    string PolicyRefusalReason,
    string SelectedShellEntry,
    string IntendedActivationStrategy,
    bool ExactlyOneShellRequest,
    bool NoRetryPolicy,
    bool NoDirectLaunchPolicy,
    bool NoServiceMutationPolicy,
    bool BackendExecutable,
    int BoundedTimeoutSeconds,
    string[] ExpectedReadinessStages,
    PimaxShellActivationCapabilitySnapshot Capability,
    PimaxShellReadinessObservation? Readiness,
    string[] Preconditions,
    string[] Warnings,
    string[] Errors,
    string HumanReadableSummary);

internal sealed record PimaxShellReadinessObservation(
    string State,
    double ElapsedSeconds,
    string SoftwareGroupState,
    string ComponentHealthState,
    int ConsecutiveHealthySamples,
    string[] RequiredMembersPresent,
    string[] RequiredMembersMissing,
    string[] Transitions,
    string HumanReadableSummary);

internal sealed record PimaxShellReadinessSample(
    TimeSpan Elapsed,
    PimaxSoftwareGroupSnapshot SoftwareGroup,
    string ComponentHealthState);

internal sealed record PimaxShellShortcutCandidate(
    string ShortcutPath,
    string SourceLocation,
    PimaxShellShortcutInfo? Shortcut);

internal sealed record PimaxShellShortcutInfo(
    string TargetPath,
    string Arguments,
    string WorkingDirectory);

internal sealed record PimaxShellTargetEvidence(
    bool Exists,
    string ProductName,
    string CompanyName,
    string FileVersion,
    string ProductVersion,
    string SignerSummary,
    bool SignerMatches,
    string RequestedExecutionLevel);

internal interface IPimaxShellShortcutReader
{
    PimaxShellShortcutInfo? Read(string shortcutPath);
}

internal interface IPimaxShellTargetInspector
{
    PimaxShellTargetEvidence Inspect(string targetPath);
}

internal interface IPimaxShellActivationRequestor
{
    Task<PimaxShellActivationRequestResult> RequestAsync(string shortcutPath, CancellationToken cancellationToken);
}

internal sealed record PimaxShellActivationRequestResult(bool Attempted, bool Accepted, string Message);

internal sealed class PimaxShellActivationCoordinator(
    IPimaxShellShortcutReader? shortcutReader = null,
    IPimaxShellTargetInspector? targetInspector = null,
    IPimaxShellActivationRequestor? activationRequestor = null,
    Func<DateTimeOffset>? now = null)
{
    public const string ConfirmationString = "CONFIRM ONE PIMAX SHELL ACTIVATION";
    private const string ExpectedDisplayName = "PimaxPlay";
    private const string ExpectedShortcutName = "PimaxPlay.lnk";
    private const string ExpectedTarget = @"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe";
    private const string ExpectedWorkingDirectory = @"C:\Program Files\Pimax\PimaxClient\pimaxui";
    private static readonly ConcurrentDictionary<string, byte> ActiveOperations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] ExpectedStages =
    [
        PimaxShellActivationState.ValidatingPreconditions,
        PimaxShellActivationState.RequestingShellActivation,
        PimaxShellActivationState.ActivationRequested,
        PimaxShellActivationState.WaitingForPimaxClient,
        PimaxShellActivationState.WaitingForDeviceSetting,
        PimaxShellActivationState.WaitingForRuntimeGroup,
        PimaxShellActivationState.Stabilizing,
        PimaxShellActivationState.Healthy
    ];

    private readonly IPimaxShellShortcutReader _shortcutReader = shortcutReader ?? new WindowsPimaxShellShortcutReader();
    private readonly IPimaxShellTargetInspector _targetInspector = targetInspector ?? new WindowsPimaxShellTargetInspector();
    private readonly IPimaxShellActivationRequestor _activationRequestor = activationRequestor ?? new WindowsPimaxShellActivationRequestor();
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);

    public async Task<PimaxShellActivationCapabilitySnapshot> BuildCapabilityAsync(SupervisorConfig config, CancellationToken cancellationToken)
    {
        PimaxComponentHealthSnapshot health;
        try
        {
            health = await new PimaxComponentHealthCoordinator().CollectAsync(config, cancellationToken);
        }
        catch (Exception ex)
        {
            health = UnknownHealth("shell-activation", _now(), $"Health collection failed: {ex.GetType().Name}");
        }

        return BuildCapability(DiscoverCandidates(), health.SourceEvidence.SoftwareGroup, health.OverallStatus);
    }

    internal PimaxShellActivationCapabilitySnapshot BuildCapability(
        IEnumerable<PimaxShellShortcutCandidate> discovered,
        PimaxSoftwareGroupSnapshot softwareGroup,
        string componentHealthState)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            return Capability(
                PimaxShellActivationCapabilityState.UnsupportedPlatform,
                [],
                null,
                softwareGroup.State,
                componentHealthState,
                "unsupportedPlatform",
                false,
                warnings,
                ["Windows Shell activation is only supported on Windows."],
                "Windows Shell activation is unsupported on this platform.");
        }

        var candidates = discovered.Select(BuildCandidate).ToArray();
        var eligible = candidates
            .Where(candidate => candidate.ValidationState == PimaxShellActivationCapabilityState.ReadyForControlledValidation)
            .ToArray();
        if (candidates.Length == 0)
        {
            return Capability(PimaxShellActivationCapabilityState.ShellEntryNotFound, candidates, null, softwareGroup.State, componentHealthState, "shellEntryNotFound", false, warnings, errors, "No official PimaxPlay Start Menu Shell entry was found.");
        }

        if (eligible.Length > 1)
        {
            return Capability(PimaxShellActivationCapabilityState.ShellEntryAmbiguous, candidates, null, softwareGroup.State, componentHealthState, "shellEntryAmbiguous", false, warnings, errors, "More than one trusted PimaxPlay Start Menu Shell entry was found.");
        }

        if (eligible.Length == 0)
        {
            var untrusted = candidates.Any(candidate => candidate.Blockers.Length > 0
                && candidate.Blockers.All(blocker => blocker.Contains("trust", StringComparison.OrdinalIgnoreCase)
                    || blocker.Contains("publisher", StringComparison.OrdinalIgnoreCase)
                    || blocker.Contains("signature", StringComparison.OrdinalIgnoreCase)));
            var state = untrusted ? PimaxShellActivationCapabilityState.ShellEntryUntrusted : PimaxShellActivationCapabilityState.ShellEntryInvalid;
            return Capability(state, candidates, null, softwareGroup.State, componentHealthState, state, false, warnings, errors, "PimaxPlay Start Menu Shell entry discovery produced no eligible trusted candidate.");
        }

        var selected = eligible.Single();
        var precondition = StateForSoftwareGroup(softwareGroup.State);
        var ready = precondition == PimaxShellActivationCapabilityState.ReadyForControlledValidation;
        return Capability(
            precondition,
            candidates,
            selected,
            softwareGroup.State,
            componentHealthState,
            ready ? "readyForControlledValidation" : precondition,
            ready,
            warnings,
            errors,
            ready
                ? "A single trusted official PimaxPlay Start Menu Shell entry is ready for a later controlled validation. B2D does not execute it."
                : $"A trusted Shell entry exists, but activation is refused for current software-group state: {softwareGroup.State}.");
    }

    public async Task<PimaxShellActivationResultSnapshot> ActivateAsync(
        SupervisorConfig config,
        PimaxShellActivationCommandLine request,
        CancellationToken cancellationToken)
    {
        var started = _now();
        var operationId = $"pimax-shell-activation-{Guid.NewGuid():N}";
        var capability = await BuildCapabilityAsync(config, cancellationToken);
        var confirmationAccepted = string.Equals(request.Confirmation, ConfirmationString, StringComparison.Ordinal);
        var warnings = new List<string>();
        var errors = new List<string>();
        var policyReason = "implementationCompleteLiveValidationRequired";
        if (!confirmationAccepted)
        {
            policyReason = string.IsNullOrWhiteSpace(request.Confirmation) ? "missingExactConfirmation" : "incorrectExactConfirmation";
            errors.Add("Exact confirmation string is required: " + ConfirmationString);
        }

        if (!ActiveOperations.TryAdd(operationId, 0))
        {
            policyReason = "activationAlreadyInProgress";
            errors.Add("A Shell activation operation already owns the operation slot.");
        }

        try
        {
            var result = new PimaxShellActivationResultSnapshot(
                PimaxShellActivationResultSchema.Version,
                operationId,
                started,
                _now(),
                PimaxShellActivationState.Refused,
                Accepted: false,
                confirmationAccepted,
                "policyRefused",
                policyReason,
                capability.SelectedShellEntry?.SanitizedShortcutPath ?? "none",
                "Windows Shell open verb against the validated PimaxPlay.lnk Start Menu entry; B2D reports the plan only.",
                ExactlyOneShellRequest: true,
                NoRetryPolicy: true,
                NoDirectLaunchPolicy: true,
                NoServiceMutationPolicy: true,
                BackendExecutable: false,
                BoundedTimeoutSeconds: 90,
                ExpectedStages,
                capability,
                Readiness: null,
                Preconditions(capability, confirmationAccepted),
                warnings.ToArray(),
                errors.ToArray(),
                "B2D implementation is complete, but live Shell activation is policy-disabled until Phase 28D2-B2D-V performs exactly one controlled validation.");

            return result;
        }
        finally
        {
            ActiveOperations.TryRemove(operationId, out _);
        }
    }

    internal static PimaxShellReadinessObservation EvaluateReadiness(IReadOnlyCollection<PimaxShellReadinessSample> samples)
    {
        var transitions = new List<string>();
        var healthy = 0;
        PimaxShellReadinessSample? last = null;
        foreach (var sample in samples.OrderBy(sample => sample.Elapsed))
        {
            last = sample;
            var state = sample.SoftwareGroup.State switch
            {
                PimaxSoftwareGroupState.Conflicting => PimaxShellActivationState.Conflicting,
                PimaxSoftwareGroupState.Partial => PimaxShellActivationState.Partial,
                PimaxSoftwareGroupState.Complete => PimaxShellActivationState.Stabilizing,
                PimaxSoftwareGroupState.Unavailable => MissingStage(sample.SoftwareGroup),
                _ => PimaxShellActivationState.WaitingForPimaxClient
            };
            transitions.Add($"{sample.Elapsed.TotalSeconds:0}s:{state}");
            if (sample.SoftwareGroup.State == PimaxSoftwareGroupState.Complete && RequiredRuntimeMembersPresent(sample.SoftwareGroup))
            {
                healthy++;
                if (healthy >= 3)
                {
                    return Observation(PimaxShellActivationState.Healthy, sample, healthy, transitions, "Pimax software group reached three consecutive healthy samples. Headset registration is still reported separately.");
                }
            }
            else
            {
                healthy = 0;
            }

            if (sample.Elapsed.TotalSeconds >= 90)
            {
                return Observation(PimaxShellActivationState.TimedOut, sample, healthy, transitions, "Pimax Shell activation readiness timed out within the bounded 90 second window.");
            }
        }

        return last is null
            ? new PimaxShellReadinessObservation(PimaxShellActivationState.NotStarted, 0, PimaxSoftwareGroupState.Unknown, "unknown", 0, [], RequiredRuntimeMembers, [], "No readiness samples were observed.")
            : Observation(last.SoftwareGroup.State == PimaxSoftwareGroupState.Conflicting ? PimaxShellActivationState.Conflicting : PimaxShellActivationState.Partial, last, healthy, transitions, "Pimax software group did not reach the consecutive stable readiness threshold.");
    }

    private PimaxShellActivationCandidate BuildCandidate(PimaxShellShortcutCandidate candidate)
    {
        var shortcut = candidate.Shortcut;
        var display = Path.GetFileNameWithoutExtension(candidate.ShortcutPath);
        var extension = Path.GetExtension(candidate.ShortcutPath);
        var target = shortcut?.TargetPath ?? "";
        var evidence = string.IsNullOrWhiteSpace(target)
            ? new PimaxShellTargetEvidence(false, "", "", "", "", "target unavailable", false, "unknown")
            : _targetInspector.Inspect(target);
        var blockers = new List<string>();
        if (!string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)) blockers.Add("Shell entry is not a .lnk shortcut.");
        if (!string.Equals(display, ExpectedDisplayName, StringComparison.OrdinalIgnoreCase)) blockers.Add("Display name does not match PimaxPlay.");
        if (shortcut is null) blockers.Add("Shortcut could not be resolved.");
        if (!IsExpectedTarget(target)) blockers.Add("Shortcut target is not the expected PimaxClient executable under the Pimax installation root.");
        if (!evidence.Exists) blockers.Add("Shortcut target does not exist.");
        if (!string.IsNullOrWhiteSpace(shortcut?.Arguments)) blockers.Add("Shortcut has unexpected arguments.");
        if (!string.Equals(shortcut?.WorkingDirectory ?? "", ExpectedWorkingDirectory, StringComparison.OrdinalIgnoreCase)) blockers.Add("Shortcut working directory does not match the PimaxClient program directory.");
        if (!AllowedTargetKind(target)) blockers.Add("Shortcut target kind is not allowed for Shell activation.");
        if (!evidence.SignerMatches || !string.Equals(evidence.ProductName, "PimaxClient", StringComparison.OrdinalIgnoreCase) || !string.Equals(evidence.CompanyName, "Pimax", StringComparison.OrdinalIgnoreCase)) blockers.Add("Target trust, product, or publisher evidence does not match Pimax.");
        var valid = blockers.Count == 0;
        return new PimaxShellActivationCandidate(
            CandidateId(candidate.ShortcutPath, target),
            display,
            candidate.SourceLocation,
            SanitizePath(candidate.ShortcutPath, candidate.SourceLocation),
            SanitizePath(target, "target"),
            string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase),
            string.Equals(display, ExpectedDisplayName, StringComparison.OrdinalIgnoreCase),
            IsExpectedTarget(target),
            evidence.Exists,
            string.IsNullOrWhiteSpace(shortcut?.Arguments),
            string.Equals(shortcut?.WorkingDirectory ?? "", ExpectedWorkingDirectory, StringComparison.OrdinalIgnoreCase),
            AllowedTargetKind(target),
            evidence.SignerMatches,
            evidence.ProductName,
            string.IsNullOrWhiteSpace(evidence.ProductVersion) ? evidence.FileVersion : evidence.ProductVersion,
            evidence.SignerSummary,
            string.IsNullOrWhiteSpace(shortcut?.Arguments) ? "none" : "unexpectedArguments",
            string.Equals(shortcut?.WorkingDirectory ?? "", ExpectedWorkingDirectory, StringComparison.OrdinalIgnoreCase) ? "expectedPimaxClientProgramDirectory" : "unexpectedOrMissing",
            valid ? PimaxShellActivationCapabilityState.ReadyForControlledValidation : PimaxShellActivationCapabilityState.ShellEntryInvalid,
            blockers.ToArray());
    }

    private IEnumerable<PimaxShellShortcutCandidate> DiscoverCandidates()
    {
        foreach (var root in StartMenuRoots())
        {
            if (!Directory.Exists(root.Path)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root.Path, ExpectedShortcutName, SearchOption.AllDirectories).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return new PimaxShellShortcutCandidate(file, root.Source, _shortcutReader.Read(file));
            }
        }
    }

    private static PimaxShellActivationCapabilitySnapshot Capability(
        string state,
        PimaxShellActivationCandidate[] candidates,
        PimaxShellActivationCandidate? selected,
        string groupState,
        string componentHealthState,
        string precondition,
        bool ready,
        List<string> warnings,
        IReadOnlyCollection<string> errors,
        string summary)
        => new(
            PimaxShellActivationCapabilitySchema.Version,
            DateTimeOffset.Now,
            state == PimaxShellActivationCapabilityState.Validated ? PimaxShellActivationCapabilityState.ReadyForControlledValidation : state,
            candidates.Length,
            candidates,
            selected,
            selected?.SanitizedShortcutPath ?? "none",
            selected?.SanitizedTargetPath ?? "none",
            selected?.SourceLocation ?? "none",
            selected?.TargetProduct ?? "unknown",
            selected?.TargetVersion ?? "unknown",
            selected?.SignerTrustSummary ?? "unknown",
            selected?.ShortcutArgumentsState ?? "unknown",
            selected?.ShortcutWorkingDirectoryState ?? "unknown",
            "Windows Shell open verb against official Start Menu .lnk",
            DirectExecutableFallbackAllowed: false,
            RuntimeComponentFallbackAllowed: false,
            ServiceMutationAllowed: false,
            RetryAllowed: false,
            ElevationRequired: false,
            groupState,
            componentHealthState,
            precondition,
            BackendExecutable: false,
            ReadinessForControlledValidation: ready,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            summary);

    private static string[] Preconditions(PimaxShellActivationCapabilitySnapshot capability, bool confirmationAccepted)
    {
        var result = new List<string>
        {
            "exactConfirmation=" + (confirmationAccepted ? "accepted" : "missingOrIncorrect"),
            "shellEntry=" + capability.CapabilityState,
            "softwareGroupState=" + capability.CurrentSoftwareGroupState,
            "backendExecutable=false",
            "noDirectExecutableFallback=true",
            "noRuntimeComponentFallback=true",
            "noServiceMutation=true",
            "noRetry=true"
        };
        return result.ToArray();
    }

    private static string StateForSoftwareGroup(string state)
        => state switch
        {
            PimaxSoftwareGroupState.Complete => PimaxShellActivationCapabilityState.SoftwareGroupAlreadyRunning,
            PimaxSoftwareGroupState.Unavailable => PimaxShellActivationCapabilityState.ReadyForControlledValidation,
            PimaxSoftwareGroupState.Partial => PimaxShellActivationCapabilityState.SoftwareGroupPartial,
            PimaxSoftwareGroupState.Conflicting => PimaxShellActivationCapabilityState.SoftwareGroupPartial,
            _ => PimaxShellActivationCapabilityState.SoftwareGroupStateUnknown
        };

    private static string MissingStage(PimaxSoftwareGroupSnapshot group)
        => !group.Members.Any(member => string.Equals(member.ProcessName, "PimaxClient", StringComparison.OrdinalIgnoreCase))
            ? PimaxShellActivationState.WaitingForPimaxClient
            : !group.Members.Any(member => string.Equals(member.ProcessName, "DeviceSetting", StringComparison.OrdinalIgnoreCase))
                ? PimaxShellActivationState.WaitingForDeviceSetting
                : PimaxShellActivationState.WaitingForRuntimeGroup;

    private static PimaxShellReadinessObservation Observation(string state, PimaxShellReadinessSample sample, int healthy, List<string> transitions, string summary)
    {
        var missing = RequiredRuntimeMembers.Where(required => !sample.SoftwareGroup.Members.Any(member => string.Equals(member.ProcessName, required, StringComparison.OrdinalIgnoreCase))).ToArray();
        var present = RequiredRuntimeMembers.Except(missing, StringComparer.OrdinalIgnoreCase).ToArray();
        return new PimaxShellReadinessObservation(state, sample.Elapsed.TotalSeconds, sample.SoftwareGroup.State, sample.ComponentHealthState, healthy, present, missing, transitions.ToArray(), summary);
    }

    private static readonly string[] RequiredRuntimeMembers = ["PimaxClient", "DeviceSetting", "PiPlayService", "PiService", "pi_server"];

    private static bool RequiredRuntimeMembersPresent(PimaxSoftwareGroupSnapshot group)
        => RequiredRuntimeMembers.All(required => group.Members.Any(member => string.Equals(member.ProcessName, required, StringComparison.OrdinalIgnoreCase)));

    private static bool IsExpectedTarget(string path)
        => string.Equals(path, ExpectedTarget, StringComparison.OrdinalIgnoreCase);

    private static bool AllowedTargetKind(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file") return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var name = Path.GetFileName(path);
        if (name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var ext = Path.GetExtension(path);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    internal static string SanitizePath(string path, string source)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        var current = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (!string.IsNullOrWhiteSpace(common) && path.StartsWith(common, StringComparison.OrdinalIgnoreCase))
        {
            return "<common-start-menu>" + path[common.Length..];
        }

        if (!string.IsNullOrWhiteSpace(current) && path.StartsWith(current, StringComparison.OrdinalIgnoreCase))
        {
            return "<user-start-menu>" + path[current.Length..];
        }

        const string pimaxRoot = @"C:\Program Files\Pimax";
        if (path.StartsWith(pimaxRoot, StringComparison.OrdinalIgnoreCase))
        {
            return "<pimax>" + path[pimaxRoot.Length..];
        }

        return source is "currentUserStartMenu" or "commonStartMenu"
            ? "<start-menu>\\" + Path.GetFileName(path)
            : PimaxConnectivityRedactor.SanitizePath(path) ?? "";
    }

    private static string CandidateId(string shortcutPath, string target)
    {
        var basis = (shortcutPath + "|" + target).ToLowerInvariant();
        return "shell:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(basis)))[..16].ToLowerInvariant();
    }

    private static IEnumerable<(string Path, string Source)> StartMenuRoots()
    {
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.Programs), "currentUserStartMenu");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "commonStartMenu");
    }

    private static PimaxComponentHealthSnapshot UnknownHealth(string operationId, DateTimeOffset now, string warning)
        => new(
            PimaxComponentHealthSchema.Version,
            now,
            operationId,
            PimaxHealthOverallStatus.Unknown,
            new PimaxRegistrationAssessmentResult(PimaxRegistrationState.Unknown, PimaxRegistrationConfidence.Insufficient, PimaxEvidenceFreshness.Unknown, "Health unavailable.", [], [], [], [], [], new PimaxRegistrationEvidence(false, 0, 0, false, 0, 0, false, false, false, false, false, [], [])),
            "insufficient",
            [],
            ["Health unavailable."],
            [],
            [],
            "Health unavailable.",
            "insufficient",
            new PimaxHealthCapabilitySummary("unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown"),
            new PimaxHealthSanitizedEvidence(PimaxConnectivitySchema.Version, PimaxUsbEnumerationSchema.Version, PimaxRegistrationAssessmentSchema.Version, operationId, PimaxRegistrationState.Unknown, 0, 0, [], [], [], [], PimaxEvidenceFreshness.Unknown, PimaxSoftwareGroupModel.Unknown(now, operationId)),
            [warning],
            []);
}

internal sealed record PimaxShellActivationCommandLine(string? Confirmation)
{
    public static PimaxShellActivationCommandLine Parse(string[] args)
        => new(Option(args, "--confirm"));

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

internal sealed class WindowsPimaxShellShortcutReader : IPimaxShellShortcutReader
{
    public PimaxShellShortcutInfo? Read(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            var shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
            if (shortcut is null) return null;
            var type = shortcut.GetType();
            return new PimaxShellShortcutInfo(
                SafeString(type.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)),
                SafeString(type.InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)),
                SafeString(type.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)));
        }
        catch
        {
            return null;
        }
    }

    private static string SafeString(object? value) => value?.ToString() ?? "";
}

internal sealed class WindowsPimaxShellTargetInspector : IPimaxShellTargetInspector
{
    public PimaxShellTargetEvidence Inspect(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return new PimaxShellTargetEvidence(false, "", "", "", "", "target missing", false, "unknown");
        }

        var info = FileVersionInfo.GetVersionInfo(targetPath);
        var signer = SignerSummary(targetPath, info);
        return new PimaxShellTargetEvidence(
            true,
            info.ProductName ?? "",
            info.CompanyName ?? "",
            info.FileVersion ?? "",
            info.ProductVersion ?? "",
            signer.Summary,
            signer.Matches,
            RequestedExecutionLevel(targetPath));
    }

    private static (bool Matches, string Summary) SignerSummary(string path, FileVersionInfo info)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var certificateMatches = certificate.Subject.Contains("Pimax", StringComparison.OrdinalIgnoreCase);
            var metadataMatches = string.Equals(info.CompanyName, "Pimax", StringComparison.OrdinalIgnoreCase);
            return (certificateMatches || metadataMatches, certificateMatches ? "Pimax certificate subject present; certificate serial redacted" : "product metadata publisher=Pimax");
        }
        catch
        {
            var metadataMatches = string.Equals(info.CompanyName, "Pimax", StringComparison.OrdinalIgnoreCase);
            return (metadataMatches, metadataMatches ? "signature inspection unavailable; product metadata publisher=Pimax required" : "signature and product publisher unavailable");
        }
    }

    private static string RequestedExecutionLevel(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            if (text.Contains("level=\"asInvoker\"", StringComparison.OrdinalIgnoreCase)) return "asInvoker";
            if (text.Contains("level=\"requireAdministrator\"", StringComparison.OrdinalIgnoreCase)) return "requireAdministrator";
            if (text.Contains("level=\"highestAvailable\"", StringComparison.OrdinalIgnoreCase)) return "highestAvailable";
        }
        catch
        {
        }

        return "unknown";
    }
}

internal sealed class WindowsPimaxShellActivationRequestor : IPimaxShellActivationRequestor
{
    public Task<PimaxShellActivationRequestResult> RequestAsync(string shortcutPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = shortcutPath,
                UseShellExecute = true,
                Verb = "open",
                Arguments = "",
                WorkingDirectory = ""
            });
            return Task.FromResult(new PimaxShellActivationRequestResult(true, process is not null, process is null ? "Shell returned no process handle." : "Shell activation request accepted."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new PimaxShellActivationRequestResult(true, false, ex.GetType().Name + ": " + ex.Message));
        }
    }
}
