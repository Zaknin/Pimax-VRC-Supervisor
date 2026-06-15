internal static class PimaxConnectivitySchema
{
    public const string Version = "pimax-connectivity-v1";
}

internal static class PimaxProbeStatus
{
    public const string Available = "available";
    public const string NotFound = "notFound";
    public const string Unavailable = "unavailable";
    public const string AccessDenied = "accessDenied";
    public const string Error = "error";
    public const string Inconclusive = "inconclusive";
}

internal static class PimaxConnectivityConfidence
{
    public const string Confirmed = "confirmed";
    public const string Probable = "probable";
    public const string Inconclusive = "inconclusive";
    public const string Unavailable = "unavailable";
}

internal static class PimaxConnectivityAssessmentValue
{
    public const string Connected = "connected";
    public const string WindowsDevicesPresentRuntimeNotConfirmed = "windowsDevicesPresentRuntimeNotConfirmed";
    public const string WindowsDevicesAbsent = "windowsDevicesAbsent";
    public const string WindowsDevicesPartialOrProblem = "windowsDevicesPartialOrProblem";
    public const string PimaxClientNotRunning = "pimaxClientNotRunning";
    public const string PimaxClientNotInstalled = "pimaxClientNotInstalled";
    public const string ConflictingEvidence = "conflictingEvidence";
    public const string InsufficientEvidence = "insufficientEvidence";
}

internal static class PimaxRuntimeEvidenceState
{
    public const string Connected = "connected";
    public const string DisconnectedOrError = "disconnectedOrError";
    public const string HidAdded = "hidAdded";
    public const string HidRemoved = "hidRemoved";
    public const string DisplayLost = "displayLost";
    public const string DisplayRestored = "displayRestored";
    public const string Inconclusive = "inconclusive";
}

internal sealed record PimaxConnectivitySnapshot(
    string SchemaVersion,
    DateTimeOffset CollectedAt,
    double DurationMs,
    PimaxInstallationObservation Installation,
    PimaxProcessObservation Processes,
    PimaxServiceObservation Services,
    PimaxDeviceObservation Devices,
    PimaxRuntimeEvidenceObservation RuntimeEvidence,
    PimaxSteamVrDriverObservation SteamVrDriver,
    PimaxConnectivityAssessmentResult Assessment,
    string Confidence,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxInstallationObservation(
    string Status,
    PimaxInstalledProduct[] Products,
    string[] InstallRoots,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxInstalledProduct(
    string DisplayName,
    string? DisplayVersion,
    string? Publisher,
    string? InstallLocation,
    string SourceKind);

internal sealed record PimaxProcessObservation(
    string Status,
    PimaxProcessInfo[] Processes,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxProcessInfo(
    string ProcessName,
    int ProcessId,
    int? ParentProcessId,
    string? ExecutablePath,
    string Role,
    DateTimeOffset? StartTime,
    string? CompanyName,
    string? FileDescription,
    string? ProductName,
    string? FileVersion,
    string? ProductVersion);

internal sealed record PimaxServiceObservation(
    string Status,
    PimaxServiceInfo[] Services,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxServiceInfo(
    string Name,
    string? DisplayName,
    string? State,
    string? StartMode,
    int? ProcessId,
    string? BinaryPath,
    string Role);

internal sealed record PimaxDeviceObservation(
    string Status,
    PimaxDeviceInfo[] RelevantDevices,
    PimaxDeviceInfo[] AuxiliaryDevices,
    bool WiredCrystalCompositePresent,
    bool WiredCrystalCompositeHealthy,
    bool HasRelevantProblem,
    string[] MissingObservedHealthyInterfaceRoles,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxDeviceInfo(
    string Role,
    string Class,
    string? FriendlyName,
    string SanitizedInstanceId,
    string[] HardwareIds,
    string? Status,
    int? ProblemCode,
    string? DriverOrService,
    string? ContainerId,
    string? ParentId);

internal sealed record PimaxRuntimeEvidenceObservation(
    string Status,
    DateTimeOffset FreshnessWindowStartedAt,
    int FreshnessWindowSeconds,
    PimaxRuntimeEvidenceEvent[] Events,
    PimaxRuntimeEvidenceEvent? FreshConnectedEvent,
    PimaxRuntimeEvidenceEvent? FreshDisconnectedOrErrorEvent,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxRuntimeEvidenceEvent(
    string Source,
    string State,
    DateTimeOffset? EventTimestamp,
    DateTimeOffset SourceLastWriteTime,
    double? EventAgeSeconds,
    bool IsFresh,
    string TimestampReliability,
    string SanitizedMessage);

internal sealed record PimaxSteamVrDriverObservation(
    string Status,
    string[] RegisteredDriverPaths,
    bool ManifestFound,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxConnectivityAssessmentResult(
    string Value,
    string Confidence,
    string Explanation,
    string[] SupportingEvidence,
    string[] MissingEvidence,
    string[] Warnings);
