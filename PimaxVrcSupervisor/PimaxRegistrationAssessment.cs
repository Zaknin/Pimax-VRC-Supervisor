using System.Text.Json;

internal static class PimaxRegistrationAssessmentSchema
{
    public const string Version = "pimax-registration-assessment-v1";
}

internal static class PimaxRegistrationAssessmentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

internal static class PimaxRegistrationState
{
    public const string Unknown = "unknown";
    public const string LikelyHeadsetOff = "likelyHeadsetOff";
    public const string LikelyPoweredOnAwaitingRegistration = "likelyPoweredOnAwaitingRegistration";
    public const string RegisteredReady = "registeredReady";
    public const string ConflictingEvidence = "conflictingEvidence";
}

internal static class PimaxRegistrationConfidence
{
    public const string Confirmed = "confirmed";
    public const string Probable = "probable";
    public const string Possible = "possible";
    public const string Insufficient = "insufficient";
}

internal sealed record PimaxRegistrationAssessmentSnapshot(
    string SchemaVersion,
    DateTimeOffset CollectedAt,
    PimaxRegistrationAssessmentResult Assessment,
    PimaxRegistrationSourceSchemaVersions SourceSchemaVersions,
    PimaxRegistrationSnapshotMetadata FilteredSnapshot,
    PimaxRegistrationSnapshotMetadata ExpandedSnapshot,
    double CollectionGapMs,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxRegistrationSourceSchemaVersions(
    string FilteredConnectivity,
    string ExpandedUsbEnumeration);

internal sealed record PimaxRegistrationSnapshotMetadata(
    string SchemaVersion,
    DateTimeOffset CollectedAt,
    double? DurationMs,
    string Assessment,
    string Confidence,
    int Warnings,
    int Errors);

internal sealed record PimaxRegistrationAssessmentResult(
    string State,
    string Confidence,
    string Explanation,
    string[] SupportingEvidence,
    string[] ContraryEvidence,
    string[] MissingEvidence,
    string[] Conflicts,
    string[] Warnings,
    PimaxRegistrationEvidence Evidence);

internal sealed record PimaxRegistrationEvidence(
    bool HeadsetPowerOnGroupPresent,
    int HeadsetPowerOnGroupStartedRecords,
    int HeadsetPowerOnGroupTotalRecords,
    bool CrystalRuntimeGroupPresent,
    int CrystalRuntimeGroupStartedRecords,
    int CrystalRuntimeGroupTotalRecords,
    bool EyeChipPresent,
    bool FilteredConnectivityConnected,
    bool FilteredConnectivityWindowsDevicesAbsent,
    bool FilteredConnectivityPartialOrProblem,
    bool FilteredExpandedAgreement,
    string[] HeadsetPowerOnEvidence,
    string[] CrystalRuntimeEvidence);

internal sealed class PimaxRegistrationStateAssessor
{
    private static readonly HashSet<string> PowerOnVidPid = new(StringComparer.OrdinalIgnoreCase)
    {
        "VID_05E3&PID_0608",
        "VID_28DE&PID_2101",
        "VID_28DE&PID_2300"
    };

    private static readonly HashSet<string> CrystalRuntimeVidPid = new(StringComparer.OrdinalIgnoreCase)
    {
        "VID_34A4&PID_0012",
        "VID_2104&PID_0220"
    };

    public PimaxRegistrationAssessmentResult Evaluate(
        PimaxConnectivitySnapshot filtered,
        PimaxUsbEnumerationSnapshot expanded,
        double collectionGapMs)
    {
        var supporting = new List<string>();
        var contrary = new List<string>();
        var missing = new List<string>();
        var conflicts = new List<string>();
        var warnings = new List<string>();

        var powerOnRecords = expanded.FullInventory
            .Where(IsHeadsetPowerOnEvidence)
            .ToArray();
        var runtimeRecords = expanded.FullInventory
            .Where(IsCrystalRuntimeEvidence)
            .ToArray();
        var startedPowerOn = powerOnRecords.Where(IsPresentStarted).ToArray();
        var startedRuntime = runtimeRecords.Where(IsPresentStarted).ToArray();
        var eyeChipPresent = expanded.FullInventory.Any(record =>
            IsPresentStarted(record)
            && string.Equals(record.Vid, "2104", StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.Pid, "0220", StringComparison.OrdinalIgnoreCase));

        var filteredConnected = string.Equals(filtered.Assessment.Value, PimaxConnectivityAssessmentValue.Connected, StringComparison.OrdinalIgnoreCase);
        var filteredAbsent = string.Equals(filtered.Assessment.Value, PimaxConnectivityAssessmentValue.WindowsDevicesAbsent, StringComparison.OrdinalIgnoreCase);
        var filteredPartial = string.Equals(filtered.Assessment.Value, PimaxConnectivityAssessmentValue.WindowsDevicesPartialOrProblem, StringComparison.OrdinalIgnoreCase)
            || string.Equals(filtered.Assessment.Value, PimaxConnectivityAssessmentValue.WindowsDevicesPresentRuntimeNotConfirmed, StringComparison.OrdinalIgnoreCase);
        var runtimePresent = startedRuntime.Length >= 4 && eyeChipPresent;
        var powerOnPresent = startedPowerOn.Length >= 3;
        var filteredExpandedAgreement =
            filteredConnected && runtimePresent
            || filteredAbsent && !runtimePresent
            || filteredPartial && runtimeRecords.Length > 0;

        if (collectionGapMs > 3_000)
        {
            warnings.Add($"Filtered and expanded snapshots were collected {collectionGapMs:0} ms apart; evidence may have changed between probes.");
        }

        if (powerOnPresent)
        {
            supporting.Add("Headset power-on evidence group is present and started.");
        }
        else
        {
            missing.Add("Headset power-on evidence group is absent or non-present.");
        }

        if (runtimePresent)
        {
            supporting.Add("Crystal runtime evidence group is present and started.");
        }
        else
        {
            missing.Add("Crystal runtime evidence group is absent or incomplete.");
        }

        if (filteredConnected)
        {
            supporting.Add("Filtered connectivity assessment reports connected.");
        }
        else if (filteredAbsent)
        {
            missing.Add("Filtered connectivity assessment reports Windows Crystal devices absent.");
        }
        else if (filteredPartial)
        {
            warnings.Add("Filtered connectivity assessment reports partial or runtime-unconfirmed Crystal evidence.");
        }

        if (filteredConnected && !runtimePresent)
        {
            conflicts.Add("Filtered connectivity reports connected, but expanded Crystal runtime group is not present.");
        }

        if (!filteredConnected && runtimePresent)
        {
            conflicts.Add("Expanded Crystal runtime group is present, but filtered connectivity does not report connected.");
        }

        if (conflicts.Count > 0)
        {
            return Result(
                PimaxRegistrationState.ConflictingEvidence,
                PimaxRegistrationConfidence.Insufficient,
                "Filtered and expanded Pimax evidence disagree.",
                supporting,
                contrary,
                missing,
                conflicts,
                warnings,
                Evidence());
        }

        if (runtimePresent && filteredConnected)
        {
            return Result(
                PimaxRegistrationState.RegisteredReady,
                PimaxRegistrationConfidence.Confirmed,
                "Pimax runtime-ready evidence is present and filtered connectivity reports connected.",
                supporting,
                contrary,
                missing,
                conflicts,
                warnings,
                Evidence());
        }

        if (powerOnPresent && !runtimePresent)
        {
            return Result(
                PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration,
                PimaxRegistrationConfidence.Probable,
                "Headset power-on evidence is present, but Crystal runtime-ready evidence is absent.",
                supporting,
                contrary,
                missing,
                conflicts,
                warnings,
                Evidence());
        }

        if (!powerOnPresent && !runtimePresent && filteredAbsent)
        {
            return Result(
                PimaxRegistrationState.LikelyHeadsetOff,
                PimaxRegistrationConfidence.Probable,
                "Headset power-on and Crystal runtime-ready evidence are absent.",
                supporting,
                contrary,
                missing,
                conflicts,
                warnings,
                Evidence());
        }

        return Result(
            PimaxRegistrationState.Unknown,
            PimaxRegistrationConfidence.Insufficient,
            "The available evidence is incomplete for registration-state assessment.",
            supporting,
            contrary,
            missing,
            conflicts,
            warnings,
            Evidence());

        PimaxRegistrationEvidence Evidence()
            => new(
                powerOnPresent,
                startedPowerOn.Length,
                powerOnRecords.Length,
                runtimePresent,
                startedRuntime.Length,
                runtimeRecords.Length,
                eyeChipPresent,
                filteredConnected,
                filteredAbsent,
                filteredPartial,
                filteredExpandedAgreement,
                Describe(startedPowerOn),
                Describe(startedRuntime));
    }

    private static PimaxRegistrationAssessmentResult Result(
        string state,
        string confidence,
        string explanation,
        IEnumerable<string> supporting,
        IEnumerable<string> contrary,
        IEnumerable<string> missing,
        IEnumerable<string> conflicts,
        IEnumerable<string> warnings,
        PimaxRegistrationEvidence evidence)
        => new(
            state,
            confidence,
            explanation,
            supporting.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            contrary.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            conflicts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            evidence);

    private static bool IsHeadsetPowerOnEvidence(PimaxUsbDeviceRecord record)
        => MatchesVidPid(record, PowerOnVidPid);

    private static bool IsCrystalRuntimeEvidence(PimaxUsbDeviceRecord record)
        => MatchesVidPid(record, CrystalRuntimeVidPid)
            || (record.CandidateReasons.Contains("knownCrystalVidPid", StringComparer.OrdinalIgnoreCase)
                && string.Equals(record.Vid, "34A4", StringComparison.OrdinalIgnoreCase));

    private static bool MatchesVidPid(PimaxUsbDeviceRecord record, HashSet<string> values)
        => !string.IsNullOrWhiteSpace(record.Vid)
            && !string.IsNullOrWhiteSpace(record.Pid)
            && values.Contains($"VID_{record.Vid}&PID_{record.Pid}");

    private static bool IsPresentStarted(PimaxUsbDeviceRecord record)
        => record.Present
            && record.Connected
            && !record.Phantom
            && string.Equals(record.Status, "Started", StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.ConfigManagerStatus, "Started", StringComparison.OrdinalIgnoreCase);

    private static string[] Describe(IEnumerable<PimaxUsbDeviceRecord> records)
        => records
            .Select(record =>
            {
                var name = record.FriendlyName ?? record.DeviceDescription ?? record.DeviceClass ?? record.EnumeratorName;
                var vidPid = string.IsNullOrWhiteSpace(record.Vid) || string.IsNullOrWhiteSpace(record.Pid)
                    ? "no VID/PID"
                    : $"VID_{record.Vid}&PID_{record.Pid}";
                var mi = string.IsNullOrWhiteSpace(record.UsbInterfaceNumber)
                    ? ""
                    : $" MI_{record.UsbInterfaceNumber}";
                return $"{name} ({vidPid}{mi})";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
