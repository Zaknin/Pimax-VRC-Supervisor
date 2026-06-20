using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class PimaxComponentHealthSchema
{
    public const string Version = "pimax-component-health-v1";
}

internal static class PimaxComponentHealthJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal static class PimaxHealthOverallStatus
{
    public const string Healthy = "healthy";
    public const string UsableWithDegradedFeatures = "usableWithDegradedFeatures";
    public const string NotRegistered = "notRegistered";
    public const string CoreConnectionMissing = "coreConnectionMissing";
    public const string Initializing = "initializing";
    public const string SoftwareStackUnavailable = "softwareStackUnavailable";
    public const string SoftwareStackPartial = "softwareStackPartial";
    public const string StaleRegistrationEvidence = "staleRegistrationEvidence";
    public const string SoftwareStackStarting = "softwareStackStarting";
    public const string SoftwareStackConflicting = "softwareStackConflicting";
    public const string ConflictingEvidence = "conflictingEvidence";
    public const string Unknown = "unknown";
}

internal static class PimaxHealthComponentStatus
{
    public const string Present = "present";
    public const string Missing = "missing";
    public const string Degraded = "degraded";
    public const string Initializing = "initializing";
    public const string NotApplicable = "notApplicable";
    public const string Unknown = "unknown";
    public const string Conflicting = "conflicting";
}

internal static class PimaxHealthCriticality
{
    public const string RequiredForRegistration = "requiredForRegistration";
    public const string RequiredForCoreVr = "requiredForCoreVr";
    public const string RequiredForFeature = "requiredForFeature";
    public const string OptionalAccessory = "optionalAccessory";
    public const string Informational = "informational";
}

internal sealed record PimaxComponentHealthSnapshot(
    string Schema,
    DateTimeOffset Timestamp,
    string OperationId,
    string OverallStatus,
    PimaxRegistrationAssessmentResult RegistrationAssessment,
    string HeadsetModelConfidence,
    PimaxHealthComponent[] Components,
    string[] BlockingIssues,
    string[] DegradedFeatures,
    string[] InformationalWarnings,
    string HumanReadableSummary,
    string EvidenceConfidence,
    PimaxHealthCapabilitySummary CapabilitySummary,
    PimaxHealthSanitizedEvidence SourceEvidence,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxHealthComponent(
    string ComponentId,
    string DisplayName,
    string Status,
    string Criticality,
    string Confidence,
    string ExpectedState,
    string[] ObservedEvidence,
    string ReasonCode,
    string Explanation,
    string SuggestedNextActionCategory);

internal sealed record PimaxHealthCapabilitySummary(
    string CoreVr,
    string Display,
    string Audio,
    string Microphone,
    string EyeTracking,
    string FaceTracking,
    string PimaxRegistration,
    string HumanReadable);

internal sealed record PimaxHealthSanitizedEvidence(
    string ConnectivitySchema,
    string UsbEnumerationSchema,
    string RegistrationSchema,
    string ConnectivityAssessment,
    string RegistrationState,
    int RelevantDeviceCount,
    int CandidateUsbDeviceCount,
    string[] RelevantRoles,
    string[] CandidateVidPids,
    string[] PimaxProcesses,
    string[] PimaxServices,
    string RegistrationFreshness,
    PimaxSoftwareGroupSnapshot SoftwareGroup);

internal sealed class PimaxComponentHealthCoordinator
{
    private readonly Func<SupervisorConfig, CancellationToken, Task<PimaxConnectivitySnapshot>> _collectConnectivity;
    private readonly Func<PimaxUsbEnumerationSnapshot> _collectUsb;
    private readonly PimaxRegistrationStateAssessor _assessor;

    public PimaxComponentHealthCoordinator()
        : this(
            (config, cancellationToken) => new PimaxConnectivitySnapshotCollector().CollectAsync(config, cancellationToken),
            () => new PimaxUsbEnumerationSnapshotCollector().Collect(),
            new PimaxRegistrationStateAssessor())
    {
    }

    internal PimaxComponentHealthCoordinator(
        Func<SupervisorConfig, CancellationToken, Task<PimaxConnectivitySnapshot>> collectConnectivity,
        Func<PimaxUsbEnumerationSnapshot> collectUsb,
        PimaxRegistrationStateAssessor assessor)
    {
        _collectConnectivity = collectConnectivity;
        _collectUsb = collectUsb;
        _assessor = assessor;
    }

    public async Task<PimaxComponentHealthSnapshot> CollectAsync(SupervisorConfig config, CancellationToken cancellationToken)
    {
        var operationId = $"pimax-health-{Guid.NewGuid():N}";
        var warnings = new List<string>();
        var errors = new List<string>();
        var started = Stopwatch.GetTimestamp();
        PimaxConnectivitySnapshot? connectivity = null;
        PimaxUsbEnumerationSnapshot? usb = null;

        try { connectivity = await _collectConnectivity(config, cancellationToken); }
        catch (OperationCanceledException) { errors.Add("Filtered Pimax connectivity collection was canceled."); }
        catch (Exception ex) { errors.Add($"Filtered Pimax connectivity collection failed: {ex.Message}"); }

        try { usb = _collectUsb(); }
        catch (Exception ex) { errors.Add($"Expanded USB/PnP inventory collection failed: {ex.Message}"); }

        var now = DateTimeOffset.Now;
        if (connectivity is null || usb is null)
        {
            var unavailable = UnavailableRegistration();
            var group = PimaxSoftwareGroupModel.FromConnectivity(connectivity, now, operationId);
            var evidence = new PimaxHealthSanitizedEvidence(
                connectivity?.SchemaVersion ?? PimaxConnectivitySchema.Version,
                usb?.SchemaVersion ?? PimaxUsbEnumerationSchema.Version,
                PimaxRegistrationAssessmentSchema.Version,
                connectivity?.Assessment.Value ?? "unavailable",
                unavailable.State,
                connectivity?.Devices.RelevantDevices.Length ?? 0,
                usb?.CandidateDevices.Length ?? 0,
                connectivity?.Devices.RelevantDevices.Select(device => device.Role).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
                usb?.CandidateDevices.Select(VidPid).Where(value => value != "unknown").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
                connectivity?.Processes.Processes.Select(process => process.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
                connectivity?.Services.Services.Select(service => service.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
                unavailable.EvidenceFreshness,
                group);

            return new PimaxComponentHealthSnapshot(
                PimaxComponentHealthSchema.Version,
                now,
                operationId,
                PimaxHealthOverallStatus.Unknown,
                unavailable,
                "insufficient",
                [],
                ["Component health could not be assessed because one or more read-only probes failed."],
                [],
                warnings.ToArray(),
                "Pimax component health is unknown because required read-only evidence is unavailable.",
                "insufficient",
                new PimaxHealthCapabilitySummary("unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "Core VR: unknown; Display: unknown; Audio: unknown; Microphone: unknown; Eye tracking: unknown; Face tracking: unknown; Pimax registration: unknown"),
                evidence,
                warnings.ToArray(),
                errors.ToArray());
        }

        warnings.AddRange(connectivity.Warnings);
        warnings.AddRange(usb.Warnings);
        errors.AddRange(connectivity.Errors);
        errors.AddRange(usb.Errors);
        var gap = Math.Abs((usb.CollectedAt - connectivity.CollectedAt).TotalMilliseconds);
        var groupSnapshot = PimaxSoftwareGroupModel.FromConnectivity(connectivity, now, operationId);
        var registration = ApplyOwnershipFreshness(_assessor.Evaluate(connectivity, usb, gap), groupSnapshot);
        warnings.AddRange(registration.Warnings);
        warnings.AddRange(groupSnapshot.Warnings);
        var components = PimaxComponentHealthBuilder.Build(connectivity, usb, registration, groupSnapshot).ToArray();
        var overall = PimaxComponentHealthBuilder.ClassifyOverall(components, registration, groupSnapshot);
        var capabilities = PimaxComponentHealthBuilder.BuildCapabilitySummary(components, registration);
        var blocking = components
            .Where(component => component.Status is PimaxHealthComponentStatus.Missing or PimaxHealthComponentStatus.Conflicting
                && component.Criticality is PimaxHealthCriticality.RequiredForRegistration or PimaxHealthCriticality.RequiredForCoreVr)
            .Select(component => component.Explanation)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var degraded = components
            .Where(component => component.Status is PimaxHealthComponentStatus.Missing or PimaxHealthComponentStatus.Degraded or PimaxHealthComponentStatus.Unknown
                && component.Criticality is PimaxHealthCriticality.RequiredForFeature or PimaxHealthCriticality.OptionalAccessory)
            .Select(component => component.Explanation)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PimaxComponentHealthSnapshot(
            PimaxComponentHealthSchema.Version,
            now,
            operationId,
            overall,
            registration,
            ModelConfidence(usb),
            components,
            blocking,
            degraded,
            warnings.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            PimaxComponentHealthMessages.Overall(overall),
            EvidenceConfidence(overall, errors),
            capabilities,
            new PimaxHealthSanitizedEvidence(
                connectivity.SchemaVersion,
                usb.SchemaVersion,
                PimaxRegistrationAssessmentSchema.Version,
                connectivity.Assessment.Value,
                registration.State,
                connectivity.Devices.RelevantDevices.Length,
                usb.CandidateDevices.Length,
                connectivity.Devices.RelevantDevices.Select(device => device.Role).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                usb.CandidateDevices.Select(VidPid).Where(value => value != "unknown").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                connectivity.Processes.Processes.Select(process => process.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                connectivity.Services.Services.Select(service => service.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                registration.EvidenceFreshness,
                groupSnapshot),
            warnings.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static PimaxRegistrationAssessmentResult ApplyOwnershipFreshness(PimaxRegistrationAssessmentResult registration, PimaxSoftwareGroupSnapshot group)
    {
        if (registration.State != PimaxRegistrationState.RegisteredReady)
        {
            return registration;
        }

        if (group.State == PimaxSoftwareGroupState.Complete && group.Freshness == PimaxEvidenceFreshness.Current)
        {
            return registration;
        }

        var state = group.State switch
        {
            PimaxSoftwareGroupState.Unavailable => PimaxRegistrationState.SoftwareStackUnavailable,
            PimaxSoftwareGroupState.Partial => PimaxRegistrationState.RegistrationEvidenceStale,
            PimaxSoftwareGroupState.Conflicting => PimaxRegistrationState.RegistrationEvidenceStale,
            _ => PimaxRegistrationState.RegistrationEvidenceStale
        };
        var explanation = group.State == PimaxSoftwareGroupState.Unavailable
            ? "The Pimax software stack is not running, so previously available registration evidence is unowned."
            : "The Pimax software stack changed or is incomplete, so previously ready registration evidence is stale.";
        return registration with
        {
            State = state,
            Confidence = PimaxRegistrationConfidence.Insufficient,
            EvidenceFreshness = group.State == PimaxSoftwareGroupState.Unavailable ? PimaxEvidenceFreshness.Unowned : PimaxEvidenceFreshness.Contradicted,
            Explanation = explanation,
            ContraryEvidence = registration.ContraryEvidence.Concat([group.HumanReadableSummary]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MissingEvidence = registration.MissingEvidence.Concat(group.RequiredMissingRoles.Select(role => $"Missing current Pimax software owner role: {role}.")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = registration.Warnings.Concat(["Registration ready evidence was downgraded because current Pimax software ownership was not proven."]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static PimaxRegistrationAssessmentResult UnavailableRegistration()
        => new(
            PimaxRegistrationState.Unknown,
            PimaxRegistrationConfidence.Insufficient,
            PimaxEvidenceFreshness.Unknown,
            "One or more required read-only probes failed.",
            [],
            [],
            ["Filtered connectivity or expanded USB/PnP evidence is unavailable."],
            [],
            [],
            new PimaxRegistrationEvidence(false, 0, 0, false, 0, 0, false, false, false, false, false, [], []));

    private static string VidPid(PimaxUsbDeviceRecord record)
        => string.IsNullOrWhiteSpace(record.Vid) || string.IsNullOrWhiteSpace(record.Pid)
            ? "unknown"
            : $"VID_{record.Vid}&PID_{record.Pid}";

    private static string ModelConfidence(PimaxUsbEnumerationSnapshot usb)
        => usb.FullInventory.Any(record => record.Present && string.Equals(record.Vid, "34A4", StringComparison.OrdinalIgnoreCase) && string.Equals(record.Pid, "0012", StringComparison.OrdinalIgnoreCase))
            ? "probablePimaxCrystal"
            : "insufficient";

    private static string EvidenceConfidence(string overall, IReadOnlyCollection<string> errors)
        => errors.Count > 0 ? "degraded" : overall == PimaxHealthOverallStatus.Unknown ? "inconclusive" : "probable";
}

internal static class PimaxComponentHealthBuilder
{
    public static IEnumerable<PimaxHealthComponent> Build(
        PimaxConnectivitySnapshot connectivity,
        PimaxUsbEnumerationSnapshot usb,
        PimaxRegistrationAssessmentResult registration,
        PimaxSoftwareGroupSnapshot group)
    {
        yield return Component("pimaxSoftwareGroup", "Pimax software group", SoftwareGroupStatus(group), PimaxHealthCriticality.RequiredForRegistration, "probable", "Pimax Play/runtime process group is complete and current.", Evidence(new[] { group.State, group.Freshness }.Concat(group.Members.Select(member => $"{member.ProcessName}:{member.Role}"))), "pimax_software_group_" + group.State, group.HumanReadableSummary, "inspectPimaxSoftwareGroup");
        yield return Component("pimaxPlay", "Pimax Play", group.HasRole(PimaxSoftwareGroupRole.PimaxPlayUiProcess) ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForRegistration, "probable", "Pimax Play client process is running.", Evidence(ProcessNames(connectivity)), group.HasRole(PimaxSoftwareGroupRole.PimaxPlayUiProcess) ? "pimax_play_running" : "pimax_play_not_running", group.HasRole(PimaxSoftwareGroupRole.PimaxPlayUiProcess) ? "Pimax Play is running." : "Pimax Play is not running.", "startOrInspectPimaxPlay");
        yield return Component("pimaxRuntime", group.State == PimaxSoftwareGroupState.Unavailable ? "Pimax runtime" : "Pimax runtime", group.HasRole(PimaxSoftwareGroupRole.RuntimeProcess) || group.HasRole(PimaxSoftwareGroupRole.ServiceOwnedProcess) ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForRegistration, "probable", "Pimax runtime process or service is running.", Evidence(ProcessNames(connectivity).Concat(ServiceNames(connectivity))), group.HasRole(PimaxSoftwareGroupRole.RuntimeProcess) || group.HasRole(PimaxSoftwareGroupRole.ServiceOwnedProcess) ? "pimax_runtime_present" : "pimax_runtime_missing", group.HasRole(PimaxSoftwareGroupRole.RuntimeProcess) || group.HasRole(PimaxSoftwareGroupRole.ServiceOwnedProcess) ? "Pimax runtime evidence is present." : "Pimax runtime evidence is missing.", "inspectPimaxRuntime");
        yield return Component("pimaxServices", "Pimax services", connectivity.Services.Services.Length > 0 ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Unknown, PimaxHealthCriticality.RequiredForRegistration, "probable", "Relevant Pimax services are discoverable.", Evidence(ServiceNames(connectivity)), connectivity.Services.Services.Length > 0 ? "pimax_services_found" : "pimax_services_unknown", connectivity.Services.Services.Length > 0 ? "Pimax service evidence is present." : "Pimax service state is unknown.", "inspectPimaxServices");
        yield return Component("pimaxBackgroundProcesses", "Pimax background processes", connectivity.Processes.Processes.Length > 0 ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.Informational, "probable", "Relevant Pimax background processes are discoverable.", Evidence(ProcessNames(connectivity)), connectivity.Processes.Processes.Length > 0 ? "pimax_processes_found" : "pimax_processes_missing", connectivity.Processes.Processes.Length > 0 ? "Pimax background process evidence is present." : "Pimax background process evidence is missing.", "inspectPimaxProcesses");

        yield return Component("coreUsb", "Core headset USB", registration.Evidence.HeadsetPowerOnGroupPresent ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForRegistration, registration.Confidence, "Core USB power-on group is present.", registration.Evidence.HeadsetPowerOnEvidence, registration.Evidence.HeadsetPowerOnGroupPresent ? "core_usb_present" : "core_usb_missing", registration.Evidence.HeadsetPowerOnGroupPresent ? "Headset USB is present." : PimaxComponentHealthMessages.CoreUsbMissing, "checkUsbCable");
        yield return Component("usb2Companion", "USB 2 companion path", HasUsbVidPid(usb, "28DE", "2101") || HasUsbVidPid(usb, "28DE", "2300") ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForRegistration, "probable", "USB 2 companion interfaces are present.", Evidence(VidPidNames(usb, "28DE")), HasUsbVidPid(usb, "28DE", "2101") || HasUsbVidPid(usb, "28DE", "2300") ? "usb2_present" : "usb2_missing", HasUsbVidPid(usb, "28DE", "2101") || HasUsbVidPid(usb, "28DE", "2300") ? "The Pimax USB 2 companion connection is present." : "The Pimax USB 2 companion connection is missing.", "checkUsbCable");
        yield return Component("superSpeedCompanion", "SuperSpeed companion path", HasUsbVidPid(usb, "34A4", "0012") ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForCoreVr, "probable", "Pimax SuperSpeed runtime interface is present.", Evidence(VidPidNames(usb, "34A4")), HasUsbVidPid(usb, "34A4", "0012") ? "superspeed_present" : "superspeed_missing", HasUsbVidPid(usb, "34A4", "0012") ? "The Pimax SuperSpeed connection is present." : PimaxComponentHealthMessages.SuperSpeedMissing, "checkUsbCable");
        yield return Component("pimaxRegistration", "Pimax registration", RegistrationStatus(registration), PimaxHealthCriticality.RequiredForRegistration, registration.Confidence, "Pimax registration is ready.", Evidence([registration.Explanation]), RegistrationReason(registration), RegistrationExplanation(registration), "pressConnectThenReseatUsbIfManualRecovery");
        yield return Component("headsetHid", "Headset HID", HasClassOrRole(usb, "HIDClass", "CrystalHidInterface") ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForRegistration, "probable", "Headset HID interface is present.", Evidence(DeviceNames(usb, "HIDClass", "HID")), HasClassOrRole(usb, "HIDClass", "CrystalHidInterface") ? "hid_present" : "hid_missing", HasClassOrRole(usb, "HIDClass", "CrystalHidInterface") ? "Pimax headset HID is present." : "Pimax headset HID is missing.", "checkUsbCable");

        yield return Component("displayPortVideo", "DisplayPort video path", HasDisplayEvidence(usb) ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForCoreVr, "possible", "Display or NVIDIA audio interface associated with the headset is present.", Evidence(DeviceNames(usb, "DISPLAY", "MEDIA")), HasDisplayEvidence(usb) ? "display_present" : "display_missing", HasDisplayEvidence(usb) ? "The DisplayPort video path is detected." : PimaxComponentHealthMessages.DisplayMissing, "checkDisplayPortCable");
        yield return Component("headsetAudioOutput", "Pimax audio output", HasAudio(usb, output: true) ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForFeature, "probable", "Pimax headset playback endpoint is present.", Evidence(AudioNames(usb)), HasAudio(usb, output: true) ? "audio_output_present" : "audio_output_missing", HasAudio(usb, output: true) ? "The Pimax headset audio output is available." : PimaxComponentHealthMessages.AudioOutputMissing, "checkWindowsAudioDevices");
        yield return Component("headsetMicrophone", "Pimax microphone", HasMicrophone(usb) ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForFeature, "probable", "Pimax headset recording endpoint is present.", Evidence(AudioNames(usb)), HasMicrophone(usb) ? "microphone_present" : "microphone_missing", HasMicrophone(usb) ? "The Pimax headset microphone is available." : PimaxComponentHealthMessages.MicrophoneMissing, "checkWindowsAudioDevices");

        yield return Component("eyeChip", "EyeChip", HasUsbVidPid(usb, "2104", "0220") ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForFeature, "probable", "EyeChip USB device is present.", Evidence(VidPidNames(usb, "2104")), HasUsbVidPid(usb, "2104", "0220") ? "eyechip_present" : "eyechip_missing", HasUsbVidPid(usb, "2104", "0220") ? "EyeChip is detected." : PimaxComponentHealthMessages.EyeChipMissing, "checkEyeTrackingRuntime");
        yield return Component("eyeTracking", "Eye tracking", HasEyeTracking(connectivity, usb) ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForFeature, "probable", "Eye tracking service and EyeChip evidence are present.", Evidence(ServiceNames(connectivity).Concat(VidPidNames(usb, "2104"))), HasEyeTracking(connectivity, usb) ? "eye_tracking_present" : "eye_tracking_missing", HasEyeTracking(connectivity, usb) ? "Eye tracking evidence is present." : "Eye tracking is not recognized and eye-tracking features are unavailable.", "checkEyeTrackingRuntime");
        yield return Component("trackingCameras", "Tracking cameras", HasTrackingCamera(usb) ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.RequiredForCoreVr, "probable", "Pimax camera interfaces are present.", Evidence(DeviceNames(usb, "Camera", "UVC")), HasTrackingCamera(usb) ? "tracking_cameras_present" : "tracking_cameras_missing", HasTrackingCamera(usb) ? "Headset tracking-camera interfaces are present." : PimaxComponentHealthMessages.TrackingCamerasMissing, "checkUsbCable");

        yield return Component("viveFaceTracker", "Vive face tracker", HasViveFaceTracker(usb) ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Missing, PimaxHealthCriticality.OptionalAccessory, "probable", "Vive face tracker is present when configured.", Evidence(DeviceNamesContaining(usb, "vive", "htc")), HasViveFaceTracker(usb) ? "vive_face_tracker_present" : "vive_face_tracker_missing", HasViveFaceTracker(usb) ? "The Vive face tracker is detected." : PimaxComponentHealthMessages.ViveMissing, "checkViveFaceTrackerCable");
        yield return Component("mouthTrackerVrcftIntegration", "Mouth tracker / VRCFT integration", HasViveFaceTracker(usb) ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Unknown, PimaxHealthCriticality.OptionalAccessory, "possible", "Accessory evidence is available for VRCFT integration.", Evidence(DeviceNamesContaining(usb, "vive", "htc")), HasViveFaceTracker(usb) ? "vrcft_accessory_present" : "vrcft_accessory_unknown", HasViveFaceTracker(usb) ? "Face tracking accessory evidence is present." : "Face tracking integration is unavailable or unconfirmed.", "checkVrcft");

        yield return Component("steamVrIntegration", "SteamVR integration", connectivity.SteamVrDriver.ManifestFound ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Unknown, PimaxHealthCriticality.Informational, "possible", "Pimax SteamVR driver registration is present.", Evidence(connectivity.SteamVrDriver.RegisteredDriverPaths.Select(path => PimaxConnectivityRedactor.SanitizePath(path) ?? "driver path")), connectivity.SteamVrDriver.ManifestFound ? "steamvr_driver_present" : "steamvr_driver_unknown", connectivity.SteamVrDriver.ManifestFound ? "Pimax SteamVR integration evidence is present." : "Pimax SteamVR integration is not confirmed.", "inspectSteamVrIntegration");
        yield return Component("pimaxOpenVrOpenXrIntegration", "Pimax OpenVR/OpenXR integration", connectivity.SteamVrDriver.ManifestFound ? PimaxHealthComponentStatus.Present : PimaxHealthComponentStatus.Unknown, PimaxHealthCriticality.Informational, "possible", "OpenVR/OpenXR integration is safely observable only through registration metadata in this phase.", Evidence(connectivity.SteamVrDriver.RegisteredDriverPaths.Select(path => PimaxConnectivityRedactor.SanitizePath(path) ?? "driver path")), connectivity.SteamVrDriver.ManifestFound ? "openvr_openxr_present" : "openvr_openxr_unknown", connectivity.SteamVrDriver.ManifestFound ? "Pimax runtime integration metadata is present." : "Pimax OpenVR/OpenXR integration is not confirmed.", "inspectRuntimeIntegration");
    }

    public static string ClassifyOverall(IReadOnlyCollection<PimaxHealthComponent> components, PimaxRegistrationAssessmentResult registration, PimaxSoftwareGroupSnapshot group)
    {
        if (group.State == PimaxSoftwareGroupState.Unavailable || registration.State == PimaxRegistrationState.SoftwareStackUnavailable)
        {
            return PimaxHealthOverallStatus.SoftwareStackUnavailable;
        }

        if (group.State == PimaxSoftwareGroupState.Partial)
        {
            return PimaxHealthOverallStatus.SoftwareStackPartial;
        }

        if (group.State == PimaxSoftwareGroupState.Conflicting)
        {
            return PimaxHealthOverallStatus.SoftwareStackConflicting;
        }

        if (registration.State == PimaxRegistrationState.RegistrationEvidenceStale)
        {
            return PimaxHealthOverallStatus.StaleRegistrationEvidence;
        }

        if (registration.State == PimaxRegistrationState.ConflictingEvidence || components.Any(component => component.Status == PimaxHealthComponentStatus.Conflicting))
        {
            return PimaxHealthOverallStatus.ConflictingEvidence;
        }

        if (Missing(components, "coreUsb") || Missing(components, "usb2Companion"))
        {
            return PimaxHealthOverallStatus.CoreConnectionMissing;
        }

        if (registration.State == PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration)
        {
            return PimaxHealthOverallStatus.NotRegistered;
        }

        if (registration.State == PimaxRegistrationState.Unknown)
        {
            return PimaxHealthOverallStatus.Unknown;
        }

        var degraded = components.Any(component =>
            component.Status is PimaxHealthComponentStatus.Missing or PimaxHealthComponentStatus.Degraded or PimaxHealthComponentStatus.Unknown
            && component.Criticality is PimaxHealthCriticality.RequiredForCoreVr or PimaxHealthCriticality.RequiredForFeature);
        return degraded ? PimaxHealthOverallStatus.UsableWithDegradedFeatures : PimaxHealthOverallStatus.Healthy;
    }

    public static PimaxHealthCapabilitySummary BuildCapabilitySummary(IReadOnlyCollection<PimaxHealthComponent> components, PimaxRegistrationAssessmentResult registration)
    {
        var core = Available(Present(components, "coreUsb") && Present(components, "superSpeedCompanion") && registration.State == PimaxRegistrationState.RegisteredReady);
        var display = Available(Present(components, "displayPortVideo"));
        var audio = Available(Present(components, "headsetAudioOutput"));
        var microphone = Available(Present(components, "headsetMicrophone"));
        var eye = Available(Present(components, "eyeChip") && Present(components, "eyeTracking"));
        var face = Available(Present(components, "viveFaceTracker"));
        var registrationText = registration.State == PimaxRegistrationState.RegisteredReady ? "ready" : registration.State == PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration ? "not registered" : "unknown";
        return new PimaxHealthCapabilitySummary(
            core,
            display,
            audio,
            microphone,
            eye,
            face,
            registrationText,
            $"Core VR: {core}; Display: {display}; Audio: {audio}; Microphone: {microphone}; Eye tracking: {eye}; Face tracking: {face}; Pimax registration: {registrationText}");
    }

    private static PimaxHealthComponent Component(string id, string name, string status, string criticality, string confidence, string expected, IEnumerable<string> evidence, string reason, string explanation, string action)
        => new(id, name, status, criticality, confidence, expected, evidence.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(), reason, explanation, action);

    private static string[] Evidence(IEnumerable<string> values) => values.ToArray();
    private static bool Missing(IEnumerable<PimaxHealthComponent> components, string id) => components.Any(component => component.ComponentId == id && component.Status == PimaxHealthComponentStatus.Missing);
    private static bool Present(IEnumerable<PimaxHealthComponent> components, string id) => components.Any(component => component.ComponentId == id && component.Status == PimaxHealthComponentStatus.Present);
    private static string Available(bool value) => value ? "available" : "unavailable";
    private static bool HasProcess(PimaxConnectivitySnapshot snapshot, string name) => snapshot.Processes.Processes.Any(process => process.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase));
    private static bool HasRuntimeProcessOrService(PimaxConnectivitySnapshot snapshot) => snapshot.Processes.Processes.Any(process => process.Role.Contains("Runtime", StringComparison.OrdinalIgnoreCase) || process.ProcessName.Contains("PiService", StringComparison.OrdinalIgnoreCase)) || snapshot.Services.Services.Any(service => service.Role.Contains("Core", StringComparison.OrdinalIgnoreCase) || service.Name.Contains("PiService", StringComparison.OrdinalIgnoreCase) || service.Name.Contains("Pimax", StringComparison.OrdinalIgnoreCase));
    private static bool HasUsbVidPid(PimaxUsbEnumerationSnapshot snapshot, string vid, string pid) => snapshot.FullInventory.Any(record => IsPresent(record) && string.Equals(record.Vid, vid, StringComparison.OrdinalIgnoreCase) && string.Equals(record.Pid, pid, StringComparison.OrdinalIgnoreCase));
    private static bool IsPresent(PimaxUsbDeviceRecord record) => record.Present && record.Connected && !record.Phantom && string.Equals(record.Status, "Started", StringComparison.OrdinalIgnoreCase);
    private static bool HasClassOrRole(PimaxUsbEnumerationSnapshot snapshot, string deviceClass, string role) => snapshot.FullInventory.Any(record => IsPresent(record) && string.Equals(record.DeviceClass, deviceClass, StringComparison.OrdinalIgnoreCase)) || snapshot.CandidateDevices.Any(record => IsPresent(record) && record.CandidateReasons.Contains(role, StringComparer.OrdinalIgnoreCase));
    private static bool HasDisplayEvidence(PimaxUsbEnumerationSnapshot snapshot) => snapshot.FullInventory.Any(record => IsPresent(record) && ((record.DeviceClass?.Contains("Display", StringComparison.OrdinalIgnoreCase) == true) || ((record.FriendlyName ?? record.DeviceDescription ?? "").Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && (record.FriendlyName ?? record.DeviceDescription ?? "").Contains("Audio", StringComparison.OrdinalIgnoreCase))));
    private static bool HasAudio(PimaxUsbEnumerationSnapshot snapshot, bool output) => snapshot.FullInventory.Any(record => IsPresent(record) && string.Equals(record.DeviceClass, "AudioEndpoint", StringComparison.OrdinalIgnoreCase) && (record.FriendlyName ?? record.DeviceDescription ?? "").Contains("Pimax", StringComparison.OrdinalIgnoreCase) && (output ? !(record.FriendlyName ?? record.DeviceDescription ?? "").Contains("Microphone", StringComparison.OrdinalIgnoreCase) : true));
    private static bool HasMicrophone(PimaxUsbEnumerationSnapshot snapshot) => snapshot.FullInventory.Any(record => IsPresent(record) && string.Equals(record.DeviceClass, "AudioEndpoint", StringComparison.OrdinalIgnoreCase) && (record.FriendlyName ?? record.DeviceDescription ?? "").Contains("Microphone", StringComparison.OrdinalIgnoreCase));
    private static bool HasEyeTracking(PimaxConnectivitySnapshot connectivity, PimaxUsbEnumerationSnapshot usb) => HasUsbVidPid(usb, "2104", "0220") && connectivity.Services.Services.Any(service => (service.Name + "|" + service.DisplayName + "|" + service.Role).Contains("Tobii", StringComparison.OrdinalIgnoreCase) || (service.Name + "|" + service.DisplayName).Contains("Eye", StringComparison.OrdinalIgnoreCase));
    private static bool HasTrackingCamera(PimaxUsbEnumerationSnapshot snapshot) => snapshot.FullInventory.Any(record => IsPresent(record) && (string.Equals(record.DeviceClass, "Camera", StringComparison.OrdinalIgnoreCase) || (record.FriendlyName ?? record.DeviceDescription ?? "").Contains("UVC", StringComparison.OrdinalIgnoreCase)) && (string.Equals(record.Vid, "34A4", StringComparison.OrdinalIgnoreCase) || (record.FriendlyName ?? record.DeviceDescription ?? "").Contains("Pimax", StringComparison.OrdinalIgnoreCase)));
    private static bool HasViveFaceTracker(PimaxUsbEnumerationSnapshot snapshot) => snapshot.FullInventory.Any(record => IsPresent(record) && ((record.FriendlyName ?? record.DeviceDescription ?? "").Contains("Vive", StringComparison.OrdinalIgnoreCase) || (record.FriendlyName ?? record.DeviceDescription ?? "").Contains("HTC", StringComparison.OrdinalIgnoreCase) || string.Equals(record.Vid, "0BB4", StringComparison.OrdinalIgnoreCase)));
    private static IEnumerable<string> ProcessNames(PimaxConnectivitySnapshot snapshot) => snapshot.Processes.Processes.Select(process => process.ProcessName);
    private static IEnumerable<string> ServiceNames(PimaxConnectivitySnapshot snapshot) => snapshot.Services.Services.Select(service => service.Name);
    private static IEnumerable<string> VidPidNames(PimaxUsbEnumerationSnapshot snapshot, string vid) => snapshot.FullInventory.Where(record => string.Equals(record.Vid, vid, StringComparison.OrdinalIgnoreCase)).Select(record => $"{record.FriendlyName ?? record.DeviceDescription ?? record.DeviceClass ?? record.EnumeratorName} (VID_{record.Vid}&PID_{record.Pid})");
    private static IEnumerable<string> DeviceNames(PimaxUsbEnumerationSnapshot snapshot, params string[] classOrText) => snapshot.FullInventory.Where(IsPresent).Where(record => classOrText.Any(value => string.Equals(record.DeviceClass, value, StringComparison.OrdinalIgnoreCase) || (record.FriendlyName ?? record.DeviceDescription ?? "").Contains(value, StringComparison.OrdinalIgnoreCase))).Select(record => record.FriendlyName ?? record.DeviceDescription ?? record.DeviceClass ?? record.EnumeratorName);
    private static IEnumerable<string> DeviceNamesContaining(PimaxUsbEnumerationSnapshot snapshot, params string[] values) => snapshot.FullInventory.Where(IsPresent).Where(record => values.Any(value => (record.FriendlyName ?? record.DeviceDescription ?? "").Contains(value, StringComparison.OrdinalIgnoreCase))).Select(record => record.FriendlyName ?? record.DeviceDescription ?? record.DeviceClass ?? record.EnumeratorName);
    private static IEnumerable<string> AudioNames(PimaxUsbEnumerationSnapshot snapshot) => DeviceNames(snapshot, "AudioEndpoint", "MEDIA", "Microphone", "Pimax");
    private static string SoftwareGroupStatus(PimaxSoftwareGroupSnapshot group) => group.State switch { PimaxSoftwareGroupState.Complete => PimaxHealthComponentStatus.Present, PimaxSoftwareGroupState.Partial => PimaxHealthComponentStatus.Degraded, PimaxSoftwareGroupState.Unavailable => PimaxHealthComponentStatus.Missing, PimaxSoftwareGroupState.Conflicting => PimaxHealthComponentStatus.Conflicting, PimaxSoftwareGroupState.Starting => PimaxHealthComponentStatus.Initializing, _ => PimaxHealthComponentStatus.Unknown };
    private static string RegistrationStatus(PimaxRegistrationAssessmentResult registration) => registration.State switch { PimaxRegistrationState.RegisteredReady => PimaxHealthComponentStatus.Present, PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration => PimaxHealthComponentStatus.Missing, PimaxRegistrationState.SoftwareStackUnavailable => PimaxHealthComponentStatus.Missing, PimaxRegistrationState.RegistrationEvidenceStale => PimaxHealthComponentStatus.Degraded, PimaxRegistrationState.ConflictingEvidence => PimaxHealthComponentStatus.Conflicting, PimaxRegistrationState.Unknown => PimaxHealthComponentStatus.Unknown, _ => PimaxHealthComponentStatus.Missing };
    private static string RegistrationReason(PimaxRegistrationAssessmentResult registration) => registration.State switch { PimaxRegistrationState.RegisteredReady => "pimax_registration_ready", PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration => "windows_usb_present_pimax_unregistered", PimaxRegistrationState.SoftwareStackUnavailable => "pimax_software_stack_unavailable", PimaxRegistrationState.RegistrationEvidenceStale => "pimax_registration_evidence_stale", PimaxRegistrationState.ConflictingEvidence => "pimax_registration_conflicting", _ => "pimax_registration_unknown" };
    private static string RegistrationExplanation(PimaxRegistrationAssessmentResult registration) => registration.State switch { PimaxRegistrationState.RegisteredReady => "All required headset components are present and current Pimax software ownership is proven.", PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration => PimaxComponentHealthMessages.UsbPresentRegistrationMissing, PimaxRegistrationState.SoftwareStackUnavailable => PimaxComponentHealthMessages.SoftwareStackUnavailable, PimaxRegistrationState.RegistrationEvidenceStale => PimaxComponentHealthMessages.StaleRegistrationEvidence, PimaxRegistrationState.ConflictingEvidence => "Pimax registration evidence is conflicting.", _ => "Pimax registration state is unknown." };
}

internal static class PimaxComponentHealthMessages
{
    public const string UsbPresentRegistrationMissing = "Windows detects the Pimax USB stack, but Pimax Play has not registered the headset.\n\nThe headset may remain blue and error 10500 may be visible.";
    public const string CoreUsbMissing = "The core Pimax USB interface is not detected.\n\nPimax Play cannot register the headset until the USB connection returns.";
    public const string SuperSpeedMissing = "The Pimax SuperSpeed connection is missing.\n\nHigh-bandwidth camera or sensor features may be unavailable.";
    public const string DisplayMissing = "The DisplayPort video path is not detected.\n\nThe headset may be registered but have no image.";
    public const string AudioOutputMissing = "The Pimax headset audio output is not available.\n\nThe headset may have video but no sound.";
    public const string MicrophoneMissing = "The Pimax headset microphone is not available.";
    public const string EyeChipMissing = "EyeChip is not detected.\n\nEye tracking is not recognized and eye-tracking features are unavailable.";
    public const string TrackingCamerasMissing = "One or more headset tracking-camera interfaces are missing.\n\nHeadset tracking may be unavailable or unstable.";
    public const string ViveMissing = "The Vive face tracker is not detected.\n\nFace or mouth tracking through VRCFT will be unavailable.";
    public const string Healthy = "Pimax headset connection is healthy.\n\nAll required core components are present and Pimax registration is ready.";
    public const string SoftwareStackUnavailable = "The Pimax software stack is not running.\n\nWindows still detects some previously enumerated headset components, but\ncurrent Pimax registration cannot be confirmed.\n\nThe headset may appear blue or unavailable until the Pimax software\nstack is restored.";
    public const string StaleRegistrationEvidence = "Previously recorded registration evidence is no longer current.\n\nPimax Play or its runtime processes have changed, so the headset's\ncurrent registration state must be reassessed.";

    public static string Overall(string status) => status switch
    {
        PimaxHealthOverallStatus.Healthy => Healthy,
        PimaxHealthOverallStatus.UsableWithDegradedFeatures => "Pimax headset registration is available, but one or more headset features are degraded.",
        PimaxHealthOverallStatus.NotRegistered => UsbPresentRegistrationMissing,
        PimaxHealthOverallStatus.CoreConnectionMissing => CoreUsbMissing,
        PimaxHealthOverallStatus.Initializing => "Pimax headset connection appears to be initializing.",
        PimaxHealthOverallStatus.SoftwareStackUnavailable => SoftwareStackUnavailable,
        PimaxHealthOverallStatus.SoftwareStackPartial => "The Pimax software stack is partially running.\n\nStandalone member restart is not approved; group-level recovery semantics are required.",
        PimaxHealthOverallStatus.StaleRegistrationEvidence => StaleRegistrationEvidence,
        PimaxHealthOverallStatus.SoftwareStackStarting => "The Pimax software stack appears to be starting. Reassess after it settles.",
        PimaxHealthOverallStatus.SoftwareStackConflicting => "The Pimax software stack has conflicting or unknown members. Automatic repair is not allowed.",
        PimaxHealthOverallStatus.ConflictingEvidence => "Pimax component evidence is conflicting. Recheck the headset state before repair.",
        _ => "Pimax component health is unknown."
    };
}
