internal static class PimaxConnectivityAssessment
{
    internal enum PimaxRuntimeResolvedState
    {
        Connected,
        DisconnectedOrError,
        Conflict,
        Unavailable
    }

    public static PimaxConnectivityAssessmentResult Evaluate(
        PimaxInstallationObservation installation,
        PimaxProcessObservation processes,
        PimaxServiceObservation services,
        PimaxDeviceObservation devices,
        PimaxRuntimeEvidenceObservation runtimeEvidence,
        PimaxSteamVrDriverObservation steamVrDriver)
    {
        var supportingEvidence = new List<string>();
        var missingEvidence = new List<string>();
        var warnings = new List<string>();

        if (installation.Status == PimaxProbeStatus.Available)
        {
            supportingEvidence.Add("Pimax Client installation metadata was found.");
        }
        else if (installation.Status == PimaxProbeStatus.NotFound)
        {
            missingEvidence.Add("Pimax Client installation metadata was not found.");
            return Result(
                PimaxConnectivityAssessmentValue.PimaxClientNotInstalled,
                PimaxConnectivityConfidence.Probable,
                "Pimax Client does not appear to be installed from registry or bounded fallback roots.",
                supportingEvidence,
                missingEvidence,
                warnings);
        }

        if (processes.Status == PimaxProbeStatus.Available && processes.Processes.Length == 0)
        {
            missingEvidence.Add("No confirmed Pimax Client or runtime process was found under a known Pimax install path.");
            return Result(
                PimaxConnectivityAssessmentValue.PimaxClientNotRunning,
                PimaxConnectivityConfidence.Probable,
                "Pimax Client installation exists, but no confirmed Pimax Client/runtime process is running.",
                supportingEvidence,
                missingEvidence,
                warnings);
        }

        if (processes.Processes.Length > 0)
        {
            supportingEvidence.Add("Confirmed Pimax Client/runtime process evidence was found.");
        }

        if (services.Services.Length > 0)
        {
            supportingEvidence.Add("Pimax service evidence was found.");
        }

        if (devices.Status is PimaxProbeStatus.Error or PimaxProbeStatus.AccessDenied or PimaxProbeStatus.Unavailable)
        {
            missingEvidence.Add("Windows device inventory could not be collected.");
            return Result(
                PimaxConnectivityAssessmentValue.InsufficientEvidence,
                PimaxConnectivityConfidence.Inconclusive,
                "The Windows device probe did not complete, so the headset USB state cannot be assessed.",
                supportingEvidence,
                missingEvidence,
                warnings);
        }

        var runtimeState = ResolveRuntimeState(runtimeEvidence);

        if (devices.WiredCrystalCompositePresent)
        {
            supportingEvidence.Add("Wired Pimax Crystal USB composite evidence is present.");
        }

        if (devices.HasRelevantProblem || devices.MissingObservedHealthyInterfaceRoles.Length > 0)
        {
            if (runtimeState is PimaxRuntimeResolvedState.Connected or PimaxRuntimeResolvedState.Conflict)
            {
                warnings.Add("Runtime reports a fresh connected marker, but the observed Windows device profile is incomplete or has a problem.");
                return Result(
                    PimaxConnectivityAssessmentValue.ConflictingEvidence,
                    PimaxConnectivityConfidence.Inconclusive,
                    "Fresh runtime evidence and Windows device evidence disagree.",
                    supportingEvidence,
                    missingEvidence,
                    warnings);
            }

            supportingEvidence.Add("Windows found at least part of the wired Crystal profile, but the observed healthy interface set is incomplete or problematic.");
            return Result(
                PimaxConnectivityAssessmentValue.WindowsDevicesPartialOrProblem,
                PimaxConnectivityConfidence.Probable,
                "Windows device inventory is partial or reports a problem on a relevant Pimax device.",
                supportingEvidence,
                missingEvidence,
                warnings);
        }

        if (devices.WiredCrystalCompositeHealthy && runtimeState == PimaxRuntimeResolvedState.Connected)
        {
            supportingEvidence.Add("Recent runtime logs explicitly report a connected Pimax headset.");
            AddSecondarySteamVrWarning(steamVrDriver, warnings);
            return Result(
                PimaxConnectivityAssessmentValue.Connected,
                PimaxConnectivityConfidence.Confirmed,
                "Windows reports the wired Crystal USB profile and recent runtime evidence reports the headset connected.",
                supportingEvidence,
                missingEvidence,
                warnings);
        }

        if (devices.WiredCrystalCompositeHealthy)
        {
            if (runtimeState == PimaxRuntimeResolvedState.Conflict)
            {
                warnings.Add("Runtime connected and disconnected/error evidence could not be ordered safely.");
                return Result(
                    PimaxConnectivityAssessmentValue.ConflictingEvidence,
                    PimaxConnectivityConfidence.Inconclusive,
                    "Fresh runtime evidence is conflicting or cannot be ordered safely.",
                    supportingEvidence,
                    missingEvidence,
                    warnings);
            }

            if (runtimeState == PimaxRuntimeResolvedState.DisconnectedOrError)
            {
                supportingEvidence.Add("Windows reports the wired Crystal USB profile, but recent runtime evidence reports a disconnected or error state.");
            }
            else
            {
                missingEvidence.Add("No fresh runtime-connected marker was found.");
            }

            AddSecondarySteamVrWarning(steamVrDriver, warnings);
            return Result(
                PimaxConnectivityAssessmentValue.WindowsDevicesPresentRuntimeNotConfirmed,
                PimaxConnectivityConfidence.Probable,
                "Windows reports the wired Crystal USB profile, but current Pimax runtime registration is not confirmed.",
                supportingEvidence,
                missingEvidence,
                warnings);
        }

        if (devices.Status == PimaxProbeStatus.Available && devices.RelevantDevices.Length == 0)
        {
            if (runtimeState is PimaxRuntimeResolvedState.Connected or PimaxRuntimeResolvedState.Conflict)
            {
                warnings.Add("Runtime reports a fresh connected marker, but no wired Crystal PnP device was found.");
                return Result(
                    PimaxConnectivityAssessmentValue.ConflictingEvidence,
                    PimaxConnectivityConfidence.Inconclusive,
                    "Fresh runtime evidence and Windows device evidence disagree.",
                    supportingEvidence,
                    missingEvidence,
                    warnings);
            }

            missingEvidence.Add("No wired Crystal PnP device was found.");
            AddSecondarySteamVrWarning(steamVrDriver, warnings);
            return Result(
                PimaxConnectivityAssessmentValue.WindowsDevicesAbsent,
                PimaxConnectivityConfidence.Probable,
                "Windows device inventory completed and did not find the wired Crystal USB profile.",
                supportingEvidence,
                missingEvidence,
                warnings);
        }

        missingEvidence.Add("Required Pimax Client runtime or Windows device evidence is incomplete.");
        AddSecondarySteamVrWarning(steamVrDriver, warnings);
        return Result(
            PimaxConnectivityAssessmentValue.InsufficientEvidence,
            PimaxConnectivityConfidence.Inconclusive,
            "The snapshot does not contain enough current evidence to identify the failed layer.",
            supportingEvidence,
            missingEvidence,
            warnings);
    }

    private static PimaxConnectivityAssessmentResult Result(
        string value,
        string confidence,
        string explanation,
        IEnumerable<string> supportingEvidence,
        IEnumerable<string> missingEvidence,
        IEnumerable<string> warnings)
        => new(
            value,
            confidence,
            explanation,
            supportingEvidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            missingEvidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    private static void AddSecondarySteamVrWarning(
        PimaxSteamVrDriverObservation steamVrDriver,
        List<string> warnings)
    {
        if (steamVrDriver.Status is PimaxProbeStatus.NotFound or PimaxProbeStatus.Error or PimaxProbeStatus.AccessDenied)
        {
            warnings.Add("Pimax SteamVR driver registration was not confirmed. This is secondary integration evidence and does not override Pimax Client connectivity.");
        }
    }

    internal static PimaxRuntimeResolvedState ResolveRuntimeState(PimaxRuntimeEvidenceObservation runtimeEvidence)
    {
        var connected = GetReliableDecisiveEvent(runtimeEvidence.FreshConnectedEvent);
        var disconnected = GetReliableDecisiveEvent(runtimeEvidence.FreshDisconnectedOrErrorEvent);
        var hasConnectedReference = IsFreshDecisiveReference(runtimeEvidence.FreshConnectedEvent);
        var hasDisconnectedReference = IsFreshDecisiveReference(runtimeEvidence.FreshDisconnectedOrErrorEvent);

        if (connected is null && disconnected is null)
        {
            return hasConnectedReference || hasDisconnectedReference
                ? PimaxRuntimeResolvedState.Conflict
                : PimaxRuntimeResolvedState.Unavailable;
        }

        if (connected is not null && disconnected is null)
        {
            return hasDisconnectedReference
                ? PimaxRuntimeResolvedState.Conflict
                : PimaxRuntimeResolvedState.Connected;
        }

        if (connected is null && disconnected is not null)
        {
            return hasConnectedReference
                ? PimaxRuntimeResolvedState.Conflict
                : PimaxRuntimeResolvedState.DisconnectedOrError;
        }

        var comparison = DateTimeOffset.Compare(connected!.EventTimestamp!.Value, disconnected!.EventTimestamp!.Value);
        return comparison switch
        {
            > 0 => PimaxRuntimeResolvedState.Connected,
            < 0 => PimaxRuntimeResolvedState.DisconnectedOrError,
            _ => PimaxRuntimeResolvedState.Conflict
        };
    }

    private static PimaxRuntimeEvidenceEvent? GetReliableDecisiveEvent(PimaxRuntimeEvidenceEvent? candidate)
    {
        if (candidate is null
            || !IsFreshDecisiveReference(candidate)
            || candidate.EventTimestamp is null
            || !string.Equals(candidate.TimestampReliability, "parsed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return candidate;
    }

    private static bool IsFreshDecisiveReference(PimaxRuntimeEvidenceEvent? candidate)
        => candidate is not null && candidate.IsFresh;
}
