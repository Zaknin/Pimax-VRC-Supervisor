using System.Text.Json;
using Xunit;

public sealed class PimaxRepairOrchestrationTests
{
    [Fact]
    public void CapabilitiesRepresentCurrentBoundariesDeterministically()
    {
        var first = PimaxRepairPlanner.BuildCapabilities();
        var second = PimaxRepairPlanner.BuildCapabilities();

        Assert.Equal("pimax-repair-capabilities-v1", first.Schema);
        Assert.Equal(JsonSerializer.Serialize(first, PimaxRepairJson.Options), JsonSerializer.Serialize(second, PimaxRepairJson.Options));
        AssertCapability(first, "directPimaxPlayConnectInvocation", PimaxRepairCapabilityAvailability.Unavailable);
        AssertCapability(first, "electricalUsbReconnect", PimaxRepairCapabilityAvailability.Unavailable);
        AssertCapability(first, "displayPortReconnect", PimaxRepairCapabilityAvailability.Unavailable);
        AssertCapability(first, "componentHealthAssessment", PimaxRepairCapabilityAvailability.Available);
        AssertCapability(first, "finalVerification", PimaxRepairCapabilityAvailability.Available);
        AssertCapability(first, "softwareStackRestartCandidate", PimaxRepairCapabilityAvailability.AvailableWithLimitations);
        AssertCapability(first, "approvedSoftwareUsbCycle", PimaxRepairCapabilityAvailability.NotApproved);
    }

    [Theory]
    [InlineData(PimaxRepairClassification.AlreadyHealthy, PimaxRepairOutcome.NoRepairNeeded)]
    [InlineData(PimaxRepairClassification.PoweredOnAwaitingRegistration, PimaxRepairOutcome.PhysicalUsbConnectionRequired)]
    [InlineData(PimaxRepairClassification.SoftwareStackUnhealthy, PimaxRepairOutcome.SoftwareRepairCandidate)]
    [InlineData(PimaxRepairClassification.CoreUsbMissing, PimaxRepairOutcome.PhysicalUsbConnectionRequired)]
    [InlineData(PimaxRepairClassification.SuperSpeedMissing, PimaxRepairOutcome.RepairPlanned)]
    [InlineData(PimaxRepairClassification.DisplayPathMissing, PimaxRepairOutcome.DisplayPortConnectionRequired)]
    [InlineData(PimaxRepairClassification.AudioOutputMissing, PimaxRepairOutcome.RepairPlanned)]
    [InlineData(PimaxRepairClassification.MicrophoneMissing, PimaxRepairOutcome.RepairPlanned)]
    [InlineData(PimaxRepairClassification.EyeChipMissing, PimaxRepairOutcome.RepairPlanned)]
    [InlineData(PimaxRepairClassification.TrackingInterfacesMissing, PimaxRepairOutcome.RepairPlanned)]
    [InlineData(PimaxRepairClassification.ViveFaceTrackerMissing, PimaxRepairOutcome.RepairPlanned)]
    [InlineData(PimaxRepairClassification.MultipleFailures, PimaxRepairOutcome.RepairPlanned)]
    [InlineData(PimaxRepairClassification.ConflictingEvidence, PimaxRepairOutcome.ConflictingEvidence)]
    [InlineData(PimaxRepairClassification.Unknown, PimaxRepairOutcome.Unknown)]
    public void PlannerClassifiesRequiredStatesAndOutcomes(string expectedClassification, string expectedOutcome)
    {
        var plan = PimaxRepairPlanner.BuildPlan(HealthFor(expectedClassification), DateTimeOffset.Parse("2026-06-20T00:00:00Z"));

        Assert.Equal("pimax-repair-plan-v1", plan.Schema);
        Assert.Equal(expectedClassification, plan.Classification);
        Assert.Equal(expectedOutcome, plan.Outcome);
        Assert.Equal("nonMutatingPlanOnly", plan.Mode);
        Assert.DoesNotContain(plan.PlannedActions, action => action.Mutating && action.Supported);
        Assert.DoesNotContain("repaired", plan.Outcome, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistrationPlanOrdersDependenciesAndDoesNotClaimAutomaticRegistration()
    {
        var plan = PimaxRepairPlanner.BuildPlan(HealthFor(PimaxRepairClassification.PoweredOnAwaitingRegistration), DateTimeOffset.UtcNow);
        var ids = plan.PlannedActions.Select(action => action.ActionId).ToArray();

        Assert.True(Array.IndexOf(ids, "captureHealth") < Array.IndexOf(ids, "verifyServiceState"));
        Assert.True(Array.IndexOf(ids, "waitForRegistration") < Array.IndexOf(ids, "requirePimaxConnect"));
        Assert.True(Array.IndexOf(ids, "requirePimaxConnect") < Array.IndexOf(ids, "requirePhysicalUsbReconnect"));
        Assert.Contains("automatic registration cannot currently be guaranteed", plan.HumanReadableSummary);
        Assert.Contains("physical USB reconnection may still be required", plan.HumanReadableSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requirePimaxConnect", plan.RequiredUnavailableActionIds);
        Assert.Contains("requirePhysicalUsbReconnect", plan.RequiredUnavailableActionIds);
    }

    [Theory]
    [InlineData(PimaxRepairClassification.EyeChipMissing, "waitForRegistration", "verifyServiceState", "capturePostHealth")]
    [InlineData(PimaxRepairClassification.DisplayPathMissing, "verifyProcessState", "requireDisplayPortReconnect", "capturePostHealth")]
    [InlineData(PimaxRepairClassification.AudioOutputMissing, "waitForRegistration", "verifyProcessState", "compareHealth")]
    public void ComponentSpecificPlansPreserveDependencyOrdering(string classification, string first, string second, string later)
    {
        var plan = PimaxRepairPlanner.BuildPlan(HealthFor(classification), DateTimeOffset.UtcNow);
        var ids = plan.PlannedActions.Select(action => action.ActionId).ToArray();

        Assert.True(Array.IndexOf(ids, first) < Array.IndexOf(ids, second));
        Assert.True(Array.IndexOf(ids, second) < Array.IndexOf(ids, later));
    }

    [Fact]
    public void OptionalViveAccessoryDoesNotBlockCorePimaxUsability()
    {
        var plan = PimaxRepairPlanner.BuildPlan(HealthFor(PimaxRepairClassification.ViveFaceTrackerMissing), DateTimeOffset.UtcNow);

        Assert.Equal(PimaxRepairClassification.ViveFaceTrackerMissing, plan.Classification);
        Assert.Contains("Core Pimax VR may still be available", plan.HumanReadableSummary);
        Assert.Contains(plan.PlannedActions, action => action.ActionId == "noAction");
    }

    [Fact]
    public void HumanSummariesAvoidFalseSuccessAndUsbCycleClaims()
    {
        var plan = PimaxRepairPlanner.BuildPlan(HealthFor(PimaxRepairClassification.PoweredOnAwaitingRegistration), DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(plan, PimaxRepairJson.Options);

        Assert.DoesNotContain("USB cycling is available", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("restart equals success", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("automatic registration is available", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-health", string.Join("\n", plan.ExpectedOutcomes.Concat(plan.Capabilities.VerificationContract.SuccessMustDependOn)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateMachineRejectsInvalidTransitionsAndSupportsTerminalPolicy()
    {
        Assert.True(PimaxRepairPlanner.IsLegalTransition(PimaxRepairStage.Created, PimaxRepairStage.Preflight));
        Assert.True(PimaxRepairPlanner.IsLegalTransition(PimaxRepairStage.AwaitingConfirmation, PimaxRepairStage.Cancelled));
        Assert.True(PimaxRepairPlanner.IsLegalTransition(PimaxRepairStage.VerifyingOutcome, PimaxRepairStage.Completed));
        Assert.False(PimaxRepairPlanner.IsLegalTransition(PimaxRepairStage.Created, PimaxRepairStage.ExecutingSoftwareActions));
        Assert.False(PimaxRepairPlanner.IsLegalTransition(PimaxRepairStage.Completed, PimaxRepairStage.CapturingPostHealth));

        var stages = PimaxRepairPlanner.BuildCapabilities().StateMachine;
        Assert.Contains(PimaxRepairStage.Completed, stages.TerminalStages);
        Assert.Contains(PimaxRepairStage.Cancelled, stages.TerminalStages);
        Assert.Contains(PimaxRepairStage.Failed, stages.TerminalStages);
    }

    [Fact]
    public void ConcurrencyPolicyRejectsConflictingOperationsButAllowsReadOnlyPlanner()
    {
        var policy = PimaxRepairPlanner.BuildCapabilities().ConcurrencyPolicy;

        Assert.Equal(1, policy.MaxConcurrentRepairs);
        Assert.True(PimaxRepairPlanner.ConflictsWithActiveOperation("pimaxUsbExperiment"));
        Assert.True(PimaxRepairPlanner.ConflictsWithActiveOperation("connectRoutineObserver"));
        Assert.True(PimaxRepairPlanner.ConflictsWithActiveOperation("activeDeploymentUpdate"));
        Assert.Contains("repairPlanQuery", policy.AllowedOutsideCriticalWindow);
    }

    [Fact]
    public void PrivacyOutputExcludesRawDeviceAndMachineIdentifiers()
    {
        var plan = PimaxRepairPlanner.BuildPlan(HealthFor(PimaxRepairClassification.PoweredOnAwaitingRegistration), DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(plan, PimaxRepairJson.Options);

        Assert.DoesNotContain("SYNTHETIC-", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"USB\\VID_", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaticSafetyContainsNoExecutionImplementation()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxRepairOrchestration.cs"));
        string[] forbidden =
        [
            "IOCTL_USB_HUB_CYCLE_PORT",
            "CM_Reenumerate",
            "SetupDiCallClassInstaller",
            "pnputil",
            "devcon",
            ".Kill(",
            "Process.Start",
            "ServiceController",
            "Restart-Service",
            "Stop-Service",
            "Start-Service",
            "SendInput",
            "mouse_event",
            "keybd_event",
            "SetScheduledTask",
            "GetScheduledTask",
            "HttpClient",
            "WebRequest"
        ];

        foreach (var token in forbidden)
        {
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertCapability(PimaxRepairCapabilitiesSnapshot snapshot, string id, string availability)
        => Assert.Contains(snapshot.Capabilities, capability => capability.CapabilityId == id && capability.Availability == availability);

    private static PimaxComponentHealthSnapshot HealthFor(string classification)
    {
        var components = HealthyComponents();
        var overall = PimaxHealthOverallStatus.Healthy;
        var registration = Registration(PimaxRegistrationState.RegisteredReady, PimaxRegistrationConfidence.Confirmed);

        switch (classification)
        {
            case PimaxRepairClassification.PoweredOnAwaitingRegistration:
                components = SetMissing(components, "pimaxRegistration");
                registration = Registration(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration, PimaxRegistrationConfidence.Probable);
                overall = PimaxHealthOverallStatus.NotRegistered;
                break;
            case PimaxRepairClassification.SoftwareStackUnhealthy:
                components = SetMissing(components, "pimaxRuntime");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.CoreUsbMissing:
                components = SetMissing(components, "coreUsb");
                overall = PimaxHealthOverallStatus.CoreConnectionMissing;
                break;
            case PimaxRepairClassification.SuperSpeedMissing:
                components = SetMissing(components, "superSpeedCompanion");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.DisplayPathMissing:
                components = SetMissing(components, "displayPortVideo");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.AudioOutputMissing:
                components = SetMissing(components, "headsetAudioOutput");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.MicrophoneMissing:
                components = SetMissing(components, "headsetMicrophone");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.EyeChipMissing:
                components = SetMissing(components, "eyeChip");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.TrackingInterfacesMissing:
                components = SetMissing(components, "trackingCameras");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.ViveFaceTrackerMissing:
                components = SetMissing(components, "viveFaceTracker");
                overall = PimaxHealthOverallStatus.Healthy;
                break;
            case PimaxRepairClassification.MultipleFailures:
                components = SetMissing(SetMissing(components, "displayPortVideo"), "headsetAudioOutput");
                overall = PimaxHealthOverallStatus.UsableWithDegradedFeatures;
                break;
            case PimaxRepairClassification.ConflictingEvidence:
                components = SetStatus(components, "pimaxRegistration", PimaxHealthComponentStatus.Conflicting);
                registration = Registration(PimaxRegistrationState.ConflictingEvidence, PimaxRegistrationConfidence.Probable);
                overall = PimaxHealthOverallStatus.ConflictingEvidence;
                break;
            case PimaxRepairClassification.Unknown:
                components = [];
                registration = Registration(PimaxRegistrationState.Unknown, PimaxRegistrationConfidence.Insufficient);
                overall = PimaxHealthOverallStatus.Unknown;
                break;
        }

        return new PimaxComponentHealthSnapshot(
            PimaxComponentHealthSchema.Version,
            DateTimeOffset.Parse("2026-06-20T00:00:00Z"),
            "pimax-health-test",
            overall,
            registration,
            "probablePimaxCrystal",
            components,
            components.Where(component => component.Status == PimaxHealthComponentStatus.Missing && component.Criticality is PimaxHealthCriticality.RequiredForRegistration or PimaxHealthCriticality.RequiredForCoreVr).Select(component => component.Explanation).ToArray(),
            components.Where(component => component.Status == PimaxHealthComponentStatus.Missing && component.Criticality is PimaxHealthCriticality.RequiredForFeature or PimaxHealthCriticality.OptionalAccessory).Select(component => component.Explanation).ToArray(),
            [],
            "synthetic summary",
            "probable",
            new PimaxHealthCapabilitySummary("available", "available", "available", "available", "available", "available", "ready", "synthetic"),
            new PimaxHealthSanitizedEvidence(PimaxConnectivitySchema.Version, PimaxUsbEnumerationSchema.Version, PimaxRegistrationAssessmentSchema.Version, "synthetic", registration.State, 8, 8, ["CrystalHidInterface"], ["VID_34A4&PID_0012"], ["PimaxClient"], ["PiServiceLauncher"]),
            [],
            []);
    }

    private static PimaxHealthComponent[] HealthyComponents()
        =>
        [
            Component("pimaxPlay", "Pimax Play", PimaxHealthCriticality.RequiredForRegistration),
            Component("pimaxRuntime", "Pimax runtime", PimaxHealthCriticality.RequiredForRegistration),
            Component("pimaxServices", "Pimax services", PimaxHealthCriticality.RequiredForRegistration),
            Component("pimaxBackgroundProcesses", "Pimax background processes", PimaxHealthCriticality.Informational),
            Component("coreUsb", "Core headset USB", PimaxHealthCriticality.RequiredForRegistration),
            Component("usb2Companion", "USB 2 companion path", PimaxHealthCriticality.RequiredForRegistration),
            Component("superSpeedCompanion", "SuperSpeed companion path", PimaxHealthCriticality.RequiredForCoreVr),
            Component("pimaxRegistration", "Pimax registration", PimaxHealthCriticality.RequiredForRegistration),
            Component("headsetHid", "Headset HID", PimaxHealthCriticality.RequiredForRegistration),
            Component("displayPortVideo", "DisplayPort video path", PimaxHealthCriticality.RequiredForCoreVr),
            Component("headsetAudioOutput", "Pimax audio output", PimaxHealthCriticality.RequiredForFeature),
            Component("headsetMicrophone", "Pimax microphone", PimaxHealthCriticality.RequiredForFeature),
            Component("eyeChip", "EyeChip", PimaxHealthCriticality.RequiredForFeature),
            Component("eyeTracking", "Eye tracking", PimaxHealthCriticality.RequiredForFeature),
            Component("trackingCameras", "Tracking cameras", PimaxHealthCriticality.RequiredForCoreVr),
            Component("viveFaceTracker", "Vive face tracker", PimaxHealthCriticality.OptionalAccessory),
            Component("mouthTrackerVrcftIntegration", "Mouth tracker / VRCFT integration", PimaxHealthCriticality.OptionalAccessory),
            Component("steamVrIntegration", "SteamVR integration", PimaxHealthCriticality.Informational),
            Component("pimaxOpenVrOpenXrIntegration", "Pimax OpenVR/OpenXR integration", PimaxHealthCriticality.Informational)
        ];

    private static PimaxHealthComponent Component(string id, string name, string criticality)
        => new(id, name, PimaxHealthComponentStatus.Present, criticality, "probable", "present", [name], id + "_present", name + " is present.", "inspect");

    private static PimaxHealthComponent[] SetMissing(PimaxHealthComponent[] components, string id)
        => SetStatus(components, id, PimaxHealthComponentStatus.Missing);

    private static PimaxHealthComponent[] SetStatus(PimaxHealthComponent[] components, string id, string status)
        => components.Select(component => component.ComponentId == id
            ? component with { Status = status, ReasonCode = id + "_" + status, Explanation = component.DisplayName + " is " + status + "." }
            : component).ToArray();

    private static PimaxRegistrationAssessmentResult Registration(string state, string confidence)
        => new(
            state,
            confidence,
            "synthetic registration",
            ["sanitized evidence"],
            [],
            [],
            [],
            [],
            new PimaxRegistrationEvidence(
                true,
                3,
                3,
                state == PimaxRegistrationState.RegisteredReady,
                state == PimaxRegistrationState.RegisteredReady ? 2 : 0,
                2,
                true,
                state == PimaxRegistrationState.RegisteredReady,
                false,
                false,
                true,
                ["power-on group"],
                ["runtime group"]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
