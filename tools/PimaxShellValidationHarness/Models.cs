using System.Text.Json;

namespace PimaxShellValidationHarness;

internal static class HarnessConstants
{
    public const string ConfirmationPhrase = "RUN ONE PIMAX SHELL VALIDATION";
    public const string ResultRoot = @"C:\Users\FucktoryVR\Documents\PimaxVrcSupervisorDiagnosticsArchive\PimaxRecovery";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static readonly string[] LaunchOwnedProcessNames =
    [
        "PimaxClient",
        "DeviceSetting",
        "PiPlayService",
        "PiService",
        "pi_server",
        "PVRHome",
        "pi_overlay",
        "lighthouse_console"
    ];

    public static readonly string[] PersistentAllowedProcessNames =
    [
        "PiPlatformService_64",
        "platform_runtime_VR4PIMAXP3B_service",
        "PiServiceLauncher",
        "vrss_gaze_provider"
    ];

    public static readonly string[] HealthProcessNames =
    [
        "PimaxClient",
        "launcher",
        "DeviceSetting",
        "PiPlayService",
        "PiService",
        "pi_server",
        "PVRHome",
        "pi_overlay",
        "lighthouse_console",
        "PiServiceLauncher",
        "PiPlatformService_64",
        "platform_runtime_VR4PIMAXP3B_service",
        "vrss_gaze_provider"
    ];
}

internal sealed record HarnessArguments(
    bool ObserverChild,
    bool DryRun,
    string? Confirm,
    Guid? CorrelationId,
    string? Output,
    int TimeoutSeconds);

internal sealed record HarnessRefusal(string Code, string Message);

internal sealed record ProcessRecord(
    string Name,
    int ProcessId,
    int SessionId,
    string? Path,
    DateTimeOffset? StartTime);

internal sealed record ShortcutCandidate(
    string Path,
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string SourceRoot);

internal sealed record ShortcutDiscoveryResult(
    bool Accepted,
    ShortcutCandidate? Shortcut,
    string[] Errors,
    ShortcutCandidate[] Candidates);

internal sealed record ShellRequestResult(
    DateTimeOffset RequestedAt,
    string ShortcutPath,
    bool Accepted,
    string? ExceptionType,
    string? ExceptionMessage,
    int ShellRequestCount,
    int RetryCount);

internal sealed record PnpDeviceRecord(
    string? Name,
    string? DeviceId,
    string? PnpClass,
    string? Status,
    bool Present);

internal sealed record ProcessPresence(string Name, int Count, int[] ProcessIds);

internal sealed record HealthSnapshot(
    DateTimeOffset CollectedAt,
    double SecondsAfterShellRequest,
    ProcessPresence[] Processes,
    bool SoftwareStackReady,
    bool CrystalDetected,
    bool RegistrationHealthy,
    bool Usb2EvidencePresent,
    bool SuperSpeedEvidencePresent,
    bool DisplayPortEvidencePresent,
    PnpDeviceRecord[] PnpEvidence);

internal sealed record ObserverEvent(
    DateTimeOffset Timestamp,
    string EventType,
    string ProcessName,
    int ProcessId,
    int? ParentProcessId);

internal sealed record ObserverResult(
    Guid CorrelationId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool ReadyMarkerWritten,
    string? Error,
    ObserverEvent[] Events);

internal sealed record FinalResult(
    string Classification,
    string Meaning,
    bool LiveActivationPerformed,
    int ShellRequestCount,
    int RetryCount,
    string[] Reasons,
    DateTimeOffset CompletedAt);
