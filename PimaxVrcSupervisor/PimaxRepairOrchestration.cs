using System.Text.Json;
using System.Text.Json.Serialization;

internal static class PimaxRepairCapabilitiesSchema
{
    public const string Version = "pimax-repair-capabilities-v1";
}

internal static class PimaxRepairPlanSchema
{
    public const string Version = "pimax-repair-plan-v1";
}

internal static class PimaxRepairJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal static class PimaxRepairCapabilityAvailability
{
    public const string Available = "available";
    public const string AvailableWithLimitations = "availableWithLimitations";
    public const string Unavailable = "unavailable";
    public const string NotApproved = "notApproved";
    public const string Unknown = "unknown";
}

internal static class PimaxRepairClassification
{
    public const string AlreadyHealthy = "alreadyHealthy";
    public const string SoftwareStackUnhealthy = "softwareStackUnhealthy";
    public const string SoftwareStackUnavailable = "softwareStackUnavailable";
    public const string SoftwareStackPartial = "softwareStackPartial";
    public const string StaleRegistrationEvidence = "staleRegistrationEvidence";
    public const string PoweredOnAwaitingRegistration = "poweredOnAwaitingRegistration";
    public const string CoreUsbMissing = "coreUsbMissing";
    public const string SuperSpeedMissing = "superSpeedMissing";
    public const string DisplayPathMissing = "displayPathMissing";
    public const string AudioOutputMissing = "audioOutputMissing";
    public const string MicrophoneMissing = "microphoneMissing";
    public const string EyeChipMissing = "eyeChipMissing";
    public const string TrackingInterfacesMissing = "trackingInterfacesMissing";
    public const string ViveFaceTrackerMissing = "viveFaceTrackerMissing";
    public const string MultipleFailures = "multipleFailures";
    public const string ConflictingEvidence = "conflictingEvidence";
    public const string Unknown = "unknown";
}

internal static class PimaxRepairOutcome
{
    public const string NoRepairNeeded = "noRepairNeeded";
    public const string RepairPlanned = "repairPlanned";
    public const string SoftwareRepairCandidate = "softwareRepairCandidate";
    public const string PhysicalUsbConnectionRequired = "physicalUsbConnectionRequired";
    public const string DisplayPortConnectionRequired = "displayPortConnectionRequired";
    public const string UnsupportedAutomaticRecovery = "unsupportedAutomaticRecovery";
    public const string ConflictingEvidence = "conflictingEvidence";
    public const string Unknown = "unknown";
}

internal static class PimaxRepairStage
{
    public const string Created = "created";
    public const string Preflight = "preflight";
    public const string CapturingPreHealth = "capturingPreHealth";
    public const string ClassifyingFailure = "classifyingFailure";
    public const string BuildingPlan = "buildingPlan";
    public const string AwaitingConfirmation = "awaitingConfirmation";
    public const string PreparingSoftwareActions = "preparingSoftwareActions";
    public const string ExecutingSoftwareActions = "executingSoftwareActions";
    public const string Settling = "settling";
    public const string CapturingPostHealth = "capturingPostHealth";
    public const string VerifyingOutcome = "verifyingOutcome";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

internal sealed record PimaxRepairCapabilitiesSnapshot(
    string Schema,
    string PolicyId,
    string SafetyLevel,
    PimaxRepairCapability[] Capabilities,
    PimaxRepairActionDescriptor[] ActionDescriptors,
    PimaxRepairStateMachineDefinition StateMachine,
    PimaxRepairVerificationContract VerificationContract,
    PimaxRepairObservationPolicy ObservationPolicy,
    PimaxRepairConcurrencyPolicy ConcurrencyPolicy,
    PimaxRepairBackendTuiProtocol FutureProtocol,
    string[] AutomationLimitations,
    string HumanReadableSummary);

internal sealed record PimaxRepairCapability(
    string CapabilityId,
    string DisplayName,
    string Availability,
    string Confidence,
    string[] SourceEvidence,
    string Explanation,
    string ProductImplication);

internal sealed record PimaxRepairActionDescriptor(
    string ActionId,
    string DisplayName,
    string Category,
    bool Mutating,
    bool Supported,
    bool Approved,
    bool RequiresConfirmation,
    bool CancellableBeforeStart,
    bool CancellableWhileRunning,
    int TimeoutSeconds,
    string[] Preconditions,
    string[] SuccessCriteria,
    string[] FailureCriteria,
    string Explanation);

internal sealed record PimaxRepairStateMachineDefinition(
    string[] Stages,
    PimaxRepairTransition[] LegalTransitions,
    string[] TerminalStages,
    string[] CancellationStages,
    string[] TimeoutStages);

internal sealed record PimaxRepairTransition(string From, string To);

internal sealed record PimaxRepairOperationState(
    string OperationId,
    string CorrelationId,
    DateTimeOffset StartedAt,
    string CurrentStage,
    int ProgressOrdinal,
    int ProgressTotal,
    bool CancellationRequested,
    bool TimedOut,
    string? ActiveAction,
    string[] CompletedActions,
    string[] Warnings,
    string? FinalOutcome);

internal sealed record PimaxRepairPlanSnapshot(
    string Schema,
    string OperationId,
    string CorrelationId,
    DateTimeOffset CreatedAt,
    string Mode,
    PimaxComponentHealthSnapshot PreHealth,
    PimaxRepairCapabilitiesSnapshot Capabilities,
    string Classification,
    string Outcome,
    PimaxRepairOperationState OperationState,
    PimaxRepairActionDescriptor[] PlannedActions,
    string[] ExecutableSoftwareActionIds,
    string[] RequiredUnavailableActionIds,
    string[] BlockingIssues,
    string[] DegradedFeatures,
    string[] DependencyOrderingRules,
    string HumanReadableSummary,
    string[] ExpectedOutcomes,
    string[] AutomationLimitations,
    string[] Warnings);

internal sealed record PimaxRepairVerificationContract(
    string[] SuccessMustDependOn,
    string[] FullRepairSuccessRequires,
    string[] PartialRepairRequires,
    string[] FailedRepairExamples,
    string[] NeverCallSuccessfulBecause);

internal sealed record PimaxRepairObservationPolicy(
    string[] Allowed,
    string[] Avoid,
    string[] DefaultFutureTiming);

internal sealed record PimaxRepairConcurrencyPolicy(
    int MaxConcurrentRepairs,
    string[] ConflictsWith,
    string[] AllowedOutsideCriticalWindow,
    string LockScope,
    string CancellationOwnershipRule);

internal sealed record PimaxRepairBackendTuiProtocol(
    string[] FutureCommands,
    object StartResponseExample,
    object StatusExample,
    object FinalResultExample,
    string TuiPresentation);

internal sealed class PimaxRepairPlanner
{
    private readonly PimaxComponentHealthCoordinator _healthCoordinator;
    private readonly Func<DateTimeOffset> _now;

    public PimaxRepairPlanner()
        : this(new PimaxComponentHealthCoordinator(), () => DateTimeOffset.Now)
    {
    }

    internal PimaxRepairPlanner(PimaxComponentHealthCoordinator healthCoordinator, Func<DateTimeOffset>? now = null)
    {
        _healthCoordinator = healthCoordinator;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public static PimaxRepairCapabilitiesSnapshot BuildCapabilities()
        => PimaxRepairPolicy.BuildCapabilities();

    public async Task<PimaxRepairPlanSnapshot> BuildPlanAsync(SupervisorConfig config, CancellationToken cancellationToken)
    {
        var health = await _healthCoordinator.CollectAsync(config, cancellationToken);
        return BuildPlan(health, _now());
    }

    internal static PimaxRepairPlanSnapshot BuildPlan(PimaxComponentHealthSnapshot health, DateTimeOffset createdAt)
    {
        var classification = Classify(health);
        var outcome = OutcomeFor(classification);
        var capabilities = BuildCapabilities();
        var actions = BuildActions(classification, health).ToArray();
        var operationId = $"pimax-repair-plan-{Guid.NewGuid():N}";
        var correlationId = $"pimax-repair-{Guid.NewGuid():N}";
        var unavailable = actions.Where(action => !action.Supported || !action.Approved).Select(action => action.ActionId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var executable = actions.Where(action => action.Supported && action.Approved && !action.Mutating).Select(action => action.ActionId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var state = new PimaxRepairOperationState(
            operationId,
            correlationId,
            createdAt,
            PimaxRepairStage.Completed,
            actions.Length,
            actions.Length,
            false,
            false,
            null,
            actions.Select(action => action.ActionId).ToArray(),
            [],
            outcome);

        return new PimaxRepairPlanSnapshot(
            PimaxRepairPlanSchema.Version,
            operationId,
            correlationId,
            createdAt,
            "nonMutatingPlanOnly",
            health,
            capabilities,
            classification,
            outcome,
            state,
            actions,
            executable,
            unavailable,
            health.BlockingIssues,
            health.DegradedFeatures,
            DependencyRules(classification),
            SummaryFor(classification),
            ExpectedOutcomes(classification),
            capabilities.AutomationLimitations,
            WarningsFor(classification, unavailable));
    }

    internal static string Classify(PimaxComponentHealthSnapshot health)
    {
        if (health.OverallStatus == PimaxHealthOverallStatus.ConflictingEvidence || HasStatus(health, PimaxHealthComponentStatus.Conflicting))
        {
            return PimaxRepairClassification.ConflictingEvidence;
        }

        if (health.OverallStatus == PimaxHealthOverallStatus.SoftwareStackUnavailable || health.RegistrationAssessment.State == PimaxRegistrationState.SoftwareStackUnavailable)
        {
            return PimaxRepairClassification.SoftwareStackUnavailable;
        }

        if (health.OverallStatus == PimaxHealthOverallStatus.SoftwareStackPartial)
        {
            return PimaxRepairClassification.SoftwareStackPartial;
        }

        if (health.OverallStatus == PimaxHealthOverallStatus.StaleRegistrationEvidence || health.RegistrationAssessment.State == PimaxRegistrationState.RegistrationEvidenceStale)
        {
            return PimaxRepairClassification.StaleRegistrationEvidence;
        }

        if (health.OverallStatus == PimaxHealthOverallStatus.Unknown || health.Components.Length == 0)
        {
            return PimaxRepairClassification.Unknown;
        }

        if (Missing(health, "coreUsb") || Missing(health, "usb2Companion") || Missing(health, "headsetHid"))
        {
            return PimaxRepairClassification.CoreUsbMissing;
        }

        if (health.RegistrationAssessment.State == PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration)
        {
            return PimaxRepairClassification.PoweredOnAwaitingRegistration;
        }

        if (Missing(health, "pimaxPlay") || Missing(health, "pimaxRuntime") || Unknown(health, "pimaxServices"))
        {
            return PimaxRepairClassification.SoftwareStackUnhealthy;
        }

        var primaryFailures = MissingComponentIds(health)
            .Where(id => id is not "viveFaceTracker" and not "mouthTrackerVrcftIntegration")
            .ToArray();
        if (primaryFailures.Length > 1)
        {
            return PimaxRepairClassification.MultipleFailures;
        }

        if (Missing(health, "superSpeedCompanion")) return PimaxRepairClassification.SuperSpeedMissing;
        if (Missing(health, "displayPortVideo")) return PimaxRepairClassification.DisplayPathMissing;
        if (Missing(health, "headsetAudioOutput")) return PimaxRepairClassification.AudioOutputMissing;
        if (Missing(health, "headsetMicrophone")) return PimaxRepairClassification.MicrophoneMissing;
        if (Missing(health, "eyeChip") || Missing(health, "eyeTracking")) return PimaxRepairClassification.EyeChipMissing;
        if (Missing(health, "trackingCameras")) return PimaxRepairClassification.TrackingInterfacesMissing;
        if (Missing(health, "viveFaceTracker")) return PimaxRepairClassification.ViveFaceTrackerMissing;

        return PimaxRepairClassification.AlreadyHealthy;
    }

    internal static bool IsLegalTransition(string from, string to)
        => PimaxRepairPolicy.LegalTransitions.Any(transition => transition.From == from && transition.To == to);

    internal static bool ConflictsWithActiveOperation(string activeOperationKind)
        => PimaxRepairPolicy.ConcurrencyPolicy.ConflictsWith.Contains(activeOperationKind, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<PimaxRepairActionDescriptor> BuildActions(string classification, PimaxComponentHealthSnapshot health)
    {
        var all = PimaxRepairPolicy.ActionDescriptors;
        yield return all["captureHealth"];

        switch (classification)
        {
            case PimaxRepairClassification.AlreadyHealthy:
                yield return all["noAction"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.PoweredOnAwaitingRegistration:
                yield return all["verifyServiceState"];
                yield return all["verifyProcessState"];
                yield return all["requireApprovedGroupRestartRecipe"];
                yield return all["waitForSoftwareStack"];
                yield return all["waitForRegistration"];
                yield return all["requirePimaxConnect"];
                yield return all["requirePhysicalUsbReconnect"];
                yield return all["capturePostHealth"];
                yield return all["compareHealth"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.SoftwareStackUnhealthy:
                yield return all["verifyProcessState"];
                yield return all["verifyServiceState"];
                yield return all["requireApprovedGroupRestartRecipe"];
                yield return all["waitForSoftwareStack"];
                yield return all["capturePostHealth"];
                yield return all["compareHealth"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.SoftwareStackUnavailable:
            case PimaxRepairClassification.SoftwareStackPartial:
            case PimaxRepairClassification.StaleRegistrationEvidence:
                yield return all["verifyProcessState"];
                yield return all["verifyServiceState"];
                yield return all["requireApprovedGroupRestartRecipe"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.CoreUsbMissing:
                yield return all["requirePhysicalUsbReconnect"];
                yield return all["capturePostHealth"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.DisplayPathMissing:
                yield return all["verifyProcessState"];
                yield return all["requireDisplayPortReconnect"];
                yield return all["capturePostHealth"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.EyeChipMissing:
                yield return all["waitForRegistration"];
                yield return all["verifyServiceState"];
                yield return all["requireApprovedGroupRestartRecipe"];
                yield return all["waitForSoftwareStack"];
                yield return all["capturePostHealth"];
                yield return all["compareHealth"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.AudioOutputMissing:
            case PimaxRepairClassification.MicrophoneMissing:
                yield return all["waitForRegistration"];
                yield return all["verifyProcessState"];
                yield return all["requireApprovedGroupRestartRecipe"];
                yield return all["waitForSoftwareStack"];
                yield return all["capturePostHealth"];
                yield return all["compareHealth"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.SuperSpeedMissing:
            case PimaxRepairClassification.TrackingInterfacesMissing:
                yield return all["waitForRegistration"];
                yield return all["verifyProcessState"];
                yield return all["requireApprovedGroupRestartRecipe"];
                yield return all["capturePostHealth"];
                yield return all["compareHealth"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.ViveFaceTrackerMissing:
                yield return all["noAction"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.MultipleFailures:
                foreach (var component in health.Components.Where(component => component.Status == PimaxHealthComponentStatus.Missing).OrderBy(ComponentPriority).ThenBy(component => component.ComponentId, StringComparer.OrdinalIgnoreCase))
                {
                    if (component.ComponentId == "displayPortVideo") yield return all["requireDisplayPortReconnect"];
                    else if (component.ComponentId is "coreUsb" or "usb2Companion" or "headsetHid") yield return all["requirePhysicalUsbReconnect"];
                    else if (component.ComponentId is "pimaxPlay" or "pimaxRuntime") yield return all["verifyProcessState"];
                    else yield return all["verifyServiceState"];
                }
                yield return all["capturePostHealth"];
                yield return all["compareHealth"];
                yield return all["reportResult"];
                break;
            case PimaxRepairClassification.ConflictingEvidence:
            case PimaxRepairClassification.Unknown:
            default:
                yield return all["reportResult"];
                break;
        }
    }

    private static string OutcomeFor(string classification) => classification switch
    {
        PimaxRepairClassification.AlreadyHealthy => PimaxRepairOutcome.NoRepairNeeded,
        PimaxRepairClassification.SoftwareStackUnhealthy => PimaxRepairOutcome.UnsupportedAutomaticRecovery,
        PimaxRepairClassification.SoftwareStackUnavailable => PimaxRepairOutcome.UnsupportedAutomaticRecovery,
        PimaxRepairClassification.SoftwareStackPartial => PimaxRepairOutcome.UnsupportedAutomaticRecovery,
        PimaxRepairClassification.StaleRegistrationEvidence => PimaxRepairOutcome.UnsupportedAutomaticRecovery,
        PimaxRepairClassification.PoweredOnAwaitingRegistration => PimaxRepairOutcome.PhysicalUsbConnectionRequired,
        PimaxRepairClassification.CoreUsbMissing => PimaxRepairOutcome.PhysicalUsbConnectionRequired,
        PimaxRepairClassification.DisplayPathMissing => PimaxRepairOutcome.DisplayPortConnectionRequired,
        PimaxRepairClassification.ConflictingEvidence => PimaxRepairOutcome.ConflictingEvidence,
        PimaxRepairClassification.Unknown => PimaxRepairOutcome.Unknown,
        PimaxRepairClassification.ViveFaceTrackerMissing => PimaxRepairOutcome.RepairPlanned,
        _ => PimaxRepairOutcome.RepairPlanned
    };

    private static string SummaryFor(string classification) => classification switch
    {
        PimaxRepairClassification.AlreadyHealthy => "No repair is needed.\n\nPimax registration is ready and all required core headset components are present.",
        PimaxRepairClassification.PoweredOnAwaitingRegistration => "Windows detects the Pimax headset USB stack, but Pimax Play has not registered the headset.\n\nNo Pimax Play/runtime restart is approved until a complete group launch and readiness recipe is proven.\n\nPimax Play Connect and a real physical USB reconnection may still be required.",
        PimaxRepairClassification.EyeChipMissing => "EyeChip is not detected.\n\nEye tracking is unavailable.\n\nThe planned repair will verify the Pimax software stack and reassess EyeChip after the stack settles.",
        PimaxRepairClassification.DisplayPathMissing => "Pimax registration may be ready, but the DisplayPort video path is not detected.\n\nThe headset may have no image.\n\nAutomatic DisplayPort reconnection is not available.",
        PimaxRepairClassification.ViveFaceTrackerMissing => "The Vive face tracker is not detected.\n\nCore Pimax VR may still be available, but VRCFT face or mouth tracking will be unavailable.",
        PimaxRepairClassification.CoreUsbMissing => "The core Pimax USB connection is not detected.\n\nA software restart is unlikely to restore a physically absent USB link, and logical USB reset is not an approved repair action.",
        PimaxRepairClassification.SoftwareStackUnavailable => "The full Pimax Play/runtime group is unavailable.\n\nA verified Pimax Play launcher candidate has been identified, but the complete process-group launch and readiness recipe has not yet been validated from a stopped state.\n\nAutomatic restart remains disabled.",
        PimaxRepairClassification.SoftwareStackPartial => "The Pimax Play/runtime group is partial or inconsistent.\n\nStandalone member restart is prohibited; group-level recovery semantics and a complete recipe are required.",
        PimaxRepairClassification.StaleRegistrationEvidence => "Previously ready registration evidence is stale because the owning Pimax software group changed or is absent.\n\nNo mutating action is allowed solely from stale readiness.",
        PimaxRepairClassification.SoftwareStackUnhealthy => "Pimax core devices are present, but the Pimax software stack appears unhealthy.\n\nA complete Pimax Play/runtime group restart recipe is required before any software mutation can run.",
        PimaxRepairClassification.SuperSpeedMissing => "The Pimax SuperSpeed connection is missing.\n\nHigh-bandwidth camera or sensor features may be unavailable, and full repair cannot be guaranteed by software.",
        PimaxRepairClassification.AudioOutputMissing => "The Pimax audio output is not detected.\n\nA future software-stack restart candidate would need to verify the audio endpoint after settling.",
        PimaxRepairClassification.MicrophoneMissing => "The Pimax microphone is not detected.\n\nA future software-stack restart candidate would need to verify the recording endpoint after settling.",
        PimaxRepairClassification.TrackingInterfacesMissing => "Pimax tracking interfaces are missing.\n\nHeadset tracking may be unavailable or unstable until the missing interfaces return.",
        PimaxRepairClassification.MultipleFailures => "Multiple Pimax component failures are present.\n\nThe plan preserves component-specific explanations, orders blockers by dependency, and avoids automatic repair claims.",
        PimaxRepairClassification.ConflictingEvidence => "Pimax component evidence is conflicting.\n\nAutomatic repair should not run until another low-intrusion assessment or operator review resolves the conflict.",
        _ => "Pimax repair planning cannot classify the current state with enough confidence.\n\nNo automatic repair action should run from this evidence."
    };

    private static string[] DependencyRules(string classification) => classification switch
    {
        PimaxRepairClassification.PoweredOnAwaitingRegistration => ["capture health", "verify core USB", "verify Pimax processes and services", "require complete group restart recipe before software mutation", "settle", "verify registration", "report Connect and physical USB limitation", "final health assessment"],
        PimaxRepairClassification.SoftwareStackUnavailable => ["capture health", "detect unavailable Pimax Play/runtime group", "report launcher candidate as unvalidated", "return unsupported automatic recovery"],
        PimaxRepairClassification.SoftwareStackPartial => ["capture health", "detect partial Pimax Play/runtime group", "refuse standalone member restart", "require group-level recovery semantics"],
        PimaxRepairClassification.StaleRegistrationEvidence => ["capture health", "reject stale registration readiness", "reassess only with current software-group evidence", "return unsupported automatic recovery if owner is missing"],
        PimaxRepairClassification.EyeChipMissing => ["verify registration", "verify USB 2 and SuperSpeed", "verify Pimax software stack", "propose software restart candidate", "verify EyeChip", "report eye tracking availability"],
        PimaxRepairClassification.DisplayPathMissing => ["verify registration", "verify DisplayPort path", "do not treat USB registration repair as guaranteed display repair", "report DisplayPort reconnect limitation"],
        PimaxRepairClassification.AudioOutputMissing => ["verify registration", "verify MMDEVAPI/audio endpoint", "propose safe software-stack restart candidate", "recheck endpoint", "report sound availability"],
        PimaxRepairClassification.MultipleFailures => ["registration and USB blockers before feature checks", "DisplayPort handled separately from registration", "optional accessories never block core Pimax usability"],
        _ => ["capture health", "classify component evidence", "build non-mutating plan", "report result"]
    };

    private static string[] ExpectedOutcomes(string classification) => classification switch
    {
        PimaxRepairClassification.AlreadyHealthy => ["No repair needed."],
        PimaxRepairClassification.PoweredOnAwaitingRegistration => ["No Pimax Play/runtime restart is approved without a complete group recipe.", "Automatic registration is not guaranteed.", "Pimax Play Connect and physical USB reconnection may still be required."],
        PimaxRepairClassification.SoftwareStackUnavailable => ["A verified Pimax Play launcher candidate has been identified, but the complete process-group launch and readiness recipe has not yet been validated from a stopped state.", "Automatic restart remains disabled.", "PimaxClient must not be restarted by itself."],
        PimaxRepairClassification.SoftwareStackPartial => ["No standalone member restart is allowed.", "Group-level launch and readiness semantics must be proven first."],
        PimaxRepairClassification.StaleRegistrationEvidence => ["Stale registration evidence cannot be used as proof of current health.", "The current software owner must be reassessed before any repair decision."],
        PimaxRepairClassification.DisplayPathMissing => ["Registration and video path are distinct.", "Automatic DisplayPort reconnection is unavailable."],
        PimaxRepairClassification.CoreUsbMissing => ["Operator physical USB action is required if the core link is absent.", "Logical USB reset is not proposed."],
        _ => ["Post-health verification is required before any future repair can be called successful."]
    };

    private static string[] WarningsFor(string classification, string[] unavailable)
    {
        var warnings = new List<string>();
        if (unavailable.Length > 0)
        {
            warnings.Add("One or more planned actions are descriptors only and are not executable in this phase.");
        }

        if (classification == PimaxRepairClassification.PoweredOnAwaitingRegistration)
        {
            warnings.Add("Automatic Pimax registration is unavailable; do not claim Connect or physical USB reconnection capability.");
        }

        if (classification is PimaxRepairClassification.SoftwareStackUnavailable or PimaxRepairClassification.SoftwareStackPartial or PimaxRepairClassification.StaleRegistrationEvidence)
        {
            warnings.Add("A launcher candidate does not make the Pimax Play/runtime group executable; stopped-state validation is still required.");
        }

        return warnings.ToArray();
    }

    private static bool Missing(PimaxComponentHealthSnapshot health, string id)
        => health.Components.Any(component => component.ComponentId == id && component.Status == PimaxHealthComponentStatus.Missing);

    private static bool Unknown(PimaxComponentHealthSnapshot health, string id)
        => health.Components.Any(component => component.ComponentId == id && component.Status == PimaxHealthComponentStatus.Unknown);

    private static bool HasStatus(PimaxComponentHealthSnapshot health, string status)
        => health.Components.Any(component => component.Status == status);

    private static IEnumerable<string> MissingComponentIds(PimaxComponentHealthSnapshot health)
        => health.Components.Where(component => component.Status == PimaxHealthComponentStatus.Missing).Select(component => component.ComponentId);

    private static int ComponentPriority(PimaxHealthComponent component) => component.ComponentId switch
    {
        "coreUsb" or "usb2Companion" or "headsetHid" => 0,
        "pimaxRegistration" => 1,
        "pimaxPlay" or "pimaxRuntime" or "pimaxServices" => 2,
        "displayPortVideo" => 3,
        "superSpeedCompanion" or "trackingCameras" => 4,
        "headsetAudioOutput" or "headsetMicrophone" or "eyeChip" or "eyeTracking" => 5,
        "viveFaceTracker" or "mouthTrackerVrcftIntegration" => 8,
        _ => 6
    };
}

internal static class PimaxRepairPolicy
{
    public static readonly PimaxRepairTransition[] LegalTransitions =
    [
        new(PimaxRepairStage.Created, PimaxRepairStage.Preflight),
        new(PimaxRepairStage.Preflight, PimaxRepairStage.CapturingPreHealth),
        new(PimaxRepairStage.CapturingPreHealth, PimaxRepairStage.ClassifyingFailure),
        new(PimaxRepairStage.ClassifyingFailure, PimaxRepairStage.BuildingPlan),
        new(PimaxRepairStage.BuildingPlan, PimaxRepairStage.AwaitingConfirmation),
        new(PimaxRepairStage.BuildingPlan, PimaxRepairStage.Completed),
        new(PimaxRepairStage.AwaitingConfirmation, PimaxRepairStage.PreparingSoftwareActions),
        new(PimaxRepairStage.AwaitingConfirmation, PimaxRepairStage.Cancelled),
        new(PimaxRepairStage.PreparingSoftwareActions, PimaxRepairStage.ExecutingSoftwareActions),
        new(PimaxRepairStage.PreparingSoftwareActions, PimaxRepairStage.Cancelled),
        new(PimaxRepairStage.ExecutingSoftwareActions, PimaxRepairStage.Settling),
        new(PimaxRepairStage.Settling, PimaxRepairStage.CapturingPostHealth),
        new(PimaxRepairStage.CapturingPostHealth, PimaxRepairStage.VerifyingOutcome),
        new(PimaxRepairStage.VerifyingOutcome, PimaxRepairStage.Completed),
        new(PimaxRepairStage.Preflight, PimaxRepairStage.Failed),
        new(PimaxRepairStage.CapturingPreHealth, PimaxRepairStage.Failed),
        new(PimaxRepairStage.ClassifyingFailure, PimaxRepairStage.Failed),
        new(PimaxRepairStage.BuildingPlan, PimaxRepairStage.Failed),
        new(PimaxRepairStage.ExecutingSoftwareActions, PimaxRepairStage.Failed),
        new(PimaxRepairStage.Settling, PimaxRepairStage.Failed),
        new(PimaxRepairStage.CapturingPostHealth, PimaxRepairStage.Failed),
        new(PimaxRepairStage.VerifyingOutcome, PimaxRepairStage.Failed)
    ];

    public static readonly PimaxRepairConcurrencyPolicy ConcurrencyPolicy = new(
        1,
        ["pimaxRepair", "pimaxUsbExperiment", "configuratorScanSharedPimaxStack", "connectRoutineObserver", "activeDeploymentUpdate"],
        ["componentHealthAssessmentOutsideCriticalRepairWindow", "repairCapabilitiesQuery", "repairPlanQuery"],
        "sharedBackendOperationLock",
        "Cancellation must return action ownership before any later operation can start.");

    public static readonly Dictionary<string, PimaxRepairActionDescriptor> ActionDescriptors = BuildActionDescriptors()
        .ToDictionary(action => action.ActionId, StringComparer.OrdinalIgnoreCase);

    public static PimaxRepairCapabilitiesSnapshot BuildCapabilities()
        => new(
            PimaxRepairCapabilitiesSchema.Version,
            "phase-28d2l-low-intrusion-repair-planning",
            "nonMutatingPlanningOnly",
            BuildCapabilitiesList(),
            ActionDescriptors.Values.OrderBy(action => action.ActionId, StringComparer.OrdinalIgnoreCase).ToArray(),
            new PimaxRepairStateMachineDefinition(
                [
                    PimaxRepairStage.Created,
                    PimaxRepairStage.Preflight,
                    PimaxRepairStage.CapturingPreHealth,
                    PimaxRepairStage.ClassifyingFailure,
                    PimaxRepairStage.BuildingPlan,
                    PimaxRepairStage.AwaitingConfirmation,
                    PimaxRepairStage.PreparingSoftwareActions,
                    PimaxRepairStage.ExecutingSoftwareActions,
                    PimaxRepairStage.Settling,
                    PimaxRepairStage.CapturingPostHealth,
                    PimaxRepairStage.VerifyingOutcome,
                    PimaxRepairStage.Completed,
                    PimaxRepairStage.Cancelled,
                    PimaxRepairStage.Failed
                ],
                LegalTransitions,
                [PimaxRepairStage.Completed, PimaxRepairStage.Cancelled, PimaxRepairStage.Failed],
                [PimaxRepairStage.AwaitingConfirmation, PimaxRepairStage.PreparingSoftwareActions],
                [PimaxRepairStage.Settling, PimaxRepairStage.CapturingPostHealth, PimaxRepairStage.VerifyingOutcome, PimaxRepairStage.Failed]),
            VerificationContract,
            ObservationPolicy,
            ConcurrencyPolicy,
            FutureProtocol,
            [
                "Pimax Play Connect cannot currently be invoked through a safe supported API.",
                "A reliable GUI-free Connect equivalent is not available.",
                "Physical USB reconnection cannot be automated.",
                "Software USB cycling is not approved as a product recovery action.",
                "DisplayPort electrical reconnection cannot be automated.",
                "This phase does not restart processes or services."
            ],
            "Repair planning is available, but automatic Pimax registration repair is not currently available.");

    private static PimaxRepairCapability[] BuildCapabilitiesList()
        =>
        [
            Capability("componentHealthAssessment", "Component-health assessment", PimaxRepairCapabilityAvailability.Available, "probable", ["Phase 28D1 pimax-component-health-v1"], "The Supervisor can capture a one-shot component-health snapshot.", "Repair plans can be grounded in the existing health model."),
            Capability("pimaxProcessStateAssessment", "Pimax process-state assessment", PimaxRepairCapabilityAvailability.Available, "probable", ["Phase 28D1 filtered connectivity probe"], "Relevant Pimax process state is observable.", "Future software repair can validate candidate targets before confirmation."),
            Capability("pimaxServiceStateAssessment", "Pimax service-state assessment", PimaxRepairCapabilityAvailability.Available, "probable", ["Phase 28D1 filtered connectivity probe"], "Relevant Pimax service state is observable.", "Future software repair can validate candidate targets before confirmation."),
            Capability("beforeAfterComponentComparison", "Before/after component comparison", PimaxRepairCapabilityAvailability.Available, "probable", ["Phase 28D1 component IDs"], "Pre-health and post-health snapshots can be compared by stable component IDs.", "Repair success can depend on observed component recovery."),
            Capability("boundedWaitDesign", "Bounded wait design", PimaxRepairCapabilityAvailability.Available, "probable", ["Phase 28D2-L state machine"], "Future waits are represented with explicit settle and verification stages.", "Long-running repair can avoid unbounded polling."),
            Capability("humanReadableDiagnosis", "Human-readable diagnosis", PimaxRepairCapabilityAvailability.Available, "probable", ["Phase 28D1 component explanations"], "Component-specific summaries are available.", "TUI and CLI can explain limitations without raw device identities."),
            Capability("softwareStackRestartCandidate", "Software-stack restart candidate identification", PimaxRepairCapabilityAvailability.NotApproved, "confirmed", ["Phase 28D2-BV live validation", "Phase 28D2-BV2 direct-launch rejection", "Phase 28D2-B2 startup-source discovery", "Phase 28D2-B2B creator-chain tooling", "Phase 28D2-B2C elevated creator-chain observer contract"], "PimaxClient is a coupled group member and direct process creation is rejected. Normal Start Menu activation is the current candidate, but the safe programmatic equivalent is not validated. The elevated observer can improve creator evidence only; it does not approve backend execution.", "The planner must refuse automatic group restart until a separate Shell activation adapter is implemented and validated."),
            Capability("pimaxPlayLauncherCandidate", "Pimax Play launcher candidate", PimaxRepairCapabilityAvailability.AvailableWithLimitations, "probable", ["Phase 28D2-B2 Start Menu and registry discovery"], "The installed Pimax Play shortcut can identify a launcher candidate and working directory without launching it.", "Discovery alone does not approve restart or repair execution."),
            Capability("processGroupLaunchRecipeModel", "Process-group launch recipe model", PimaxRepairCapabilityAvailability.AvailableWithLimitations, "probable", ["pimax-launch-recipe-v1", "pimax-startup-sources-v1", "pimax-startup-observation-v1", "pimax-startup-observation-elevated-v1", "pimax-startup-creator-chain-v1"], "The backend can report launcher metadata, startup sources, expected members, elevated creator-chain evidence, readiness criteria, failure criteria, and prohibited side effects.", "The recipe state may reach shellActivationObserved or activationRootIdentified, but it remains non-executable until a safe programmatic Shell activation adapter is validated."),
            Capability("operationProgressReporting", "Operation progress reporting", PimaxRepairCapabilityAvailability.AvailableWithLimitations, "probable", ["Phase 28D2-L operation state"], "The state machine defines stages, progress ordinal, active action, and completed actions.", "Future TUI status can share a backend contract."),
            Capability("cancellationBeforeMutation", "Cancellation before mutating software actions", PimaxRepairCapabilityAvailability.AvailableWithLimitations, "probable", ["Phase 28D2-L state machine"], "Cancellation points are defined before mutating actions start.", "Future repair can avoid leaving partial action ownership."),
            Capability("finalVerification", "Final verification", PimaxRepairCapabilityAvailability.Available, "probable", ["Phase 28D1 health model"], "A future repair result must depend on post-health verification.", "Process or service restart alone cannot be reported as success."),
            Capability("directPimaxPlayConnectInvocation", "Direct Pimax Play Connect invocation", PimaxRepairCapabilityAvailability.Unavailable, "probable", ["Phase 28D1 Connect routine mapping"], "No safe supported API or command-line path was identified for Connect.", "Automatic Pimax registration cannot be guaranteed."),
            Capability("supportedPimaxDiscoveryApi", "Supported Pimax discovery API", PimaxRepairCapabilityAvailability.Unavailable, "probable", ["Phase 28D1 Connect routine mapping"], "No supported discovery API was identified.", "Repair must rely on read-only Windows evidence and operator-visible state."),
            Capability("guiFreeConnectEquivalent", "Reliable GUI-free Connect equivalent", PimaxRepairCapabilityAvailability.Unavailable, "probable", ["Phase 28D1 classification D"], "Connect behavior remains opaque internal Pimax Play GUI behavior.", "The repair command must not claim full automatic registration."),
            Capability("electricalUsbReconnect", "Electrical USB disconnect/reconnect", PimaxRepairCapabilityAvailability.Unavailable, "confirmed", ["Hardware boundary"], "The Supervisor cannot physically reseat a USB cable.", "Operator action may still be required."),
            Capability("approvedSoftwareUsbCycle", "Approved software USB cycle for registration recovery", PimaxRepairCapabilityAvailability.NotApproved, "confirmed", ["Phase 28C3D paired-cycle result"], "Software logical port cycling succeeded technically but did not restore registration and is not approved for product repair.", "The Repair command must not expose USB cycling."),
            Capability("displayPortReconnect", "DisplayPort electrical reconnect", PimaxRepairCapabilityAvailability.Unavailable, "confirmed", ["Hardware boundary"], "The Supervisor cannot electrically reconnect DisplayPort.", "Display path failures may require operator cable action."),
            Capability("automaticPhysicalCableRecovery", "Automatic physical-cable recovery", PimaxRepairCapabilityAvailability.Unavailable, "confirmed", ["Phase 28D1-C minimal transition"], "The proven blue-to-green recovery still used a real physical USB reseat during Connect.", "The product must present operator-required steps honestly.")
        ];

    private static PimaxRepairCapability Capability(string id, string name, string availability, string confidence, string[] evidence, string explanation, string implication)
        => new(id, name, availability, confidence, evidence, explanation, implication);

    private static PimaxRepairActionDescriptor[] BuildActionDescriptors()
        =>
        [
            Action("captureHealth", "Capture component health", "observation", false, true, true, false, true, false, 30, ["Supervisor config can be loaded."], ["One sanitized component-health snapshot is produced."], ["Read-only health collection fails."], "Captures one current component-health assessment."),
            Action("verifyProcessState", "Verify Pimax process state", "observation", false, true, true, false, true, false, 15, ["Read-only process enumeration is available."], ["Relevant Pimax process state is summarized."], ["Process state cannot be assessed."], "Observes process state without stopping or starting anything."),
            Action("verifyServiceState", "Verify Pimax service state", "observation", false, true, true, false, true, false, 15, ["Read-only service enumeration is available."], ["Relevant Pimax service state is summarized."], ["Service state cannot be assessed."], "Observes service state without restarting services."),
            Action("requestOperatorConfirmation", "Request operator confirmation", "operator", false, true, true, true, true, false, 300, ["A mutating software plan exists."], ["Operator confirms or cancels."], ["Confirmation times out."], "Future execution must pause before any mutating software action."),
            Action("requireApprovedGroupRestartRecipe", "Require approved Pimax group restart recipe", "softwareStack", false, false, false, false, true, false, 0, ["Pimax Play/runtime group recovery is needed."], ["A complete safe launch, readiness, side-effect, and verification recipe is validated from a stopped state."], ["A launcher candidate exists but the recipe is not yet live validated."], "A verified Pimax Play launcher candidate has been identified, but the complete process-group launch and readiness recipe has not yet been validated from a stopped state. Automatic restart remains disabled."),
            Action("stopValidatedPimaxProcesses", "Stop validated Pimax processes", "softwareStack", true, false, false, true, true, false, 30, ["Targets are validated and operator confirmed."], ["Validated targets exit."], ["A target cannot be stopped or validation changes."], "Descriptor only in this phase; no process stop implementation is present."),
            Action("restartValidatedPimaxServices", "Restart validated Pimax services", "softwareStack", true, false, false, true, true, false, 60, ["Targets are validated and operator confirmed."], ["Validated services return to running state."], ["A service fails to restart or validation changes."], "Descriptor only in this phase; no service restart implementation is present."),
            Action("startValidatedPimaxProcesses", "Start validated Pimax processes", "softwareStack", true, false, false, true, true, false, 30, ["Executable target is validated and operator confirmed."], ["Validated process is observed."], ["Process launch fails or target is unsafe."], "Descriptor only in this phase; no process start implementation is present."),
            Action("waitForSoftwareStack", "Wait for software stack", "settle", false, true, true, false, true, true, 90, ["A software action was completed or skipped."], ["Bounded settle window completes."], ["Cancellation or timeout occurs."], "Future execution should wait passively rather than poll heavily."),
            Action("waitForRegistration", "Wait for registration", "settle", false, true, true, false, true, true, 120, ["Core USB evidence is present."], ["Registration becomes ready or timeout is reported."], ["Registration remains unavailable."], "A bounded wait descriptor; this phase does not poll live registration loops."),
            Action("capturePostHealth", "Capture post-health", "verification", false, true, true, false, true, false, 30, ["Planned action window has settled."], ["One post-health snapshot is produced."], ["Read-only health collection fails."], "Captures one post-health snapshot in a later execution phase."),
            Action("compareHealth", "Compare health snapshots", "verification", false, true, true, false, true, false, 10, ["Pre-health and post-health snapshots exist."], ["Component changes are classified."], ["Snapshots are missing or contradictory."], "Compares stable component IDs and registration state."),
            Action("reportResult", "Report repair result", "reporting", false, true, true, false, true, false, 10, ["Planning or verification result exists."], ["Human-readable outcome is produced."], ["Result model cannot be built."], "Reports the final outcome without overstating automatic capability."),
            Action("requirePimaxConnect", "Require Pimax Play Connect", "operator", false, false, false, false, true, false, 0, ["Registration remains unavailable."], ["Operator uses Pimax Play Connect."], ["Connect cannot be invoked or observed safely by Supervisor."], "Not executable by the Supervisor; Connect has no safe supported automation path."),
            Action("requirePhysicalUsbReconnect", "Require physical USB reconnect", "operator", false, false, false, false, true, false, 0, ["USB or registration state requires physical intervention."], ["Operator reseats the physical USB cable."], ["Supervisor cannot perform physical cable movement."], "Not executable by software; real physical reconnection may be required."),
            Action("requireDisplayPortReconnect", "Require DisplayPort reconnect", "operator", false, false, false, false, true, false, 0, ["Display path is absent."], ["Operator restores DisplayPort path."], ["Supervisor cannot perform DisplayPort electrical reconnection."], "Not executable by software; DisplayPort handling is outside current capability."),
            Action("noAction", "No repair action", "reporting", false, true, true, false, true, false, 0, ["Health does not require repair."], ["No mutation is attempted."], ["New blocking issue appears."], "Used when the current state is already healthy or only optional accessory state is missing.")
        ];

    private static PimaxRepairActionDescriptor Action(
        string id,
        string name,
        string category,
        bool mutating,
        bool supported,
        bool approved,
        bool confirmation,
        bool cancellableBeforeStart,
        bool cancellableWhileRunning,
        int timeoutSeconds,
        string[] preconditions,
        string[] successCriteria,
        string[] failureCriteria,
        string explanation)
        => new(id, name, category, mutating, supported, approved, confirmation, cancellableBeforeStart, cancellableWhileRunning, timeoutSeconds, preconditions, successCriteria, failureCriteria, explanation);

    private static readonly PimaxRepairVerificationContract VerificationContract = new(
        ["post-health component results", "registration assessment", "component changes", "absence of new blocking issues"],
        ["registration is registeredReady / confirmed", "all required core components are present", "no new blocking issue appears", "no unrelated-device regression is detected"],
        ["registration is ready", "core VR is usable", "one or more optional or feature-specific components may still be missing"],
        ["registration remains unavailable", "core USB is still absent", "software stack failed to restart", "a new blocking component disappeared", "evidence is contradictory"],
        ["a process restarted", "a service entered Running", "USB devices reappeared", "the command completed without exception"]);

    private static readonly PimaxRepairObservationPolicy ObservationPolicy = new(
        ["one pre-health snapshot", "operation timestamps", "lightweight existing Phase 29D-E recording", "one post-health snapshot", "bounded passive wait"],
        ["repeated SetupAPI enumeration", "continuous MMDEVAPI polling", "continuous process/service snapshots", "named-pipe inventory loops", "localhost endpoint loops", "continuous Pimax log tailing", "repeated component-health commands"],
        ["take one pre-snapshot", "perform the confirmed planned action in a later phase", "passive settle window", "take one post-snapshot", "take at most one delayed confirmation snapshot if still initializing"]);

    private static readonly PimaxRepairBackendTuiProtocol FutureProtocol = new(
        ["pimax-repair-start-json", "pimax-repair-status-json", "pimax-repair-cancel-json", "pimax-repair-result-json"],
        new { accepted = true, operationId = "pimax-repair-...", initialStage = PimaxRepairStage.Created, requiresConfirmation = true, planSummary = "Software-stack repair candidate; physical USB recovery may still be required." },
        new { operationId = "pimax-repair-...", stage = PimaxRepairStage.Settling, currentAction = "waitForSoftwareStack", completedActions = new[] { "captureHealth", "verifyProcessState" }, warnings = new[] { "Connect cannot be automated." }, elapsedSeconds = 20, cancellationAvailable = true },
        new { outcome = PimaxRepairOutcome.PhysicalUsbConnectionRequired, preHealth = "pimax-component-health-v1", postHealth = "pimax-component-health-v1", componentChanges = Array.Empty<string>(), blockingIssues = new[] { "registration unavailable" }, degradedFeatures = Array.Empty<string>(), humanReadableSummary = "Physical USB reconnection may still be required.", requiredOperatorAction = "Use Pimax Play Connect and reseat the Pimax USB cable if needed." },
        "Repair Pimax Connection\n\nCurrent status: Pimax not registered\n\nPlanned actions:\n[x] Assess headset components\n[ ] Verify Pimax software stack\n[ ] Restart validated Pimax components\n[ ] Wait for stack stabilization\n[ ] Verify registration and features\n\nAutomatic limitations:\n- Pimax Play Connect cannot currently be invoked\n- Physical USB reconnection cannot be performed");
}
