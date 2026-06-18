using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;

internal static class PimaxUsbPortCycleExperimentSchema
{
    public const string Version = "pimax-usb-port-cycle-experiment-v1";
    public const string PrivilegedRequestVersion = "pimax-usb-port-cycle-privileged-request-v1";
    public const string PrivilegedResultVersion = "pimax-usb-port-cycle-privileged-result-v1";
}

internal static class PimaxUsbPortCycleExperimentKind
{
    public const string CycleExactExternalHubUsb2Port = "cycle-exact-external-hub-usb2-port";
}

internal static class PimaxUsbPortCycleMode
{
    public const string DryRun = "dry-run";
    public const string Prepare = "prepare";
    public const string ExecuteElevatedHelper = "execute-elevated-helper";
    public const string ObserveResult = "observe-result";
}

internal static class PimaxUsbPortCycleJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal sealed record PimaxUsbPortCycleRequest(
    string Mode,
    string? TargetSignaturePath,
    string? ObserverStatusPath,
    string? MarkerFilePath,
    string? ConfirmationToken,
    string? ConfirmationPhrase,
    string? EvidenceDirectory,
    string? PrivilegedRequestPath,
    string? PrivilegedResultPath,
    string? HelperPath,
    string? ExpectedRequestSha256,
    bool LaunchHelper,
    int ObservationSeconds)
{
    public static PimaxUsbPortCycleRequest Parse(string[] args)
        => new(
            Option(args, "--mode") ?? PimaxUsbPortCycleMode.DryRun,
            Option(args, "--target-signature"),
            Option(args, "--observer-status"),
            Option(args, "--marker-file"),
            Option(args, "--confirmation-token"),
            Option(args, "--confirmation-phrase"),
            Option(args, "--evidence-dir"),
            Option(args, "--request-file"),
            Option(args, "--result-file"),
            Option(args, "--helper-path"),
            Option(args, "--request-sha256"),
            args.Any(arg => arg.Equals("--launch-helper", StringComparison.OrdinalIgnoreCase)),
            Math.Clamp(ParseInt(Option(args, "--duration-seconds"), 60), 5, 120));

    private static int ParseInt(string? text, int fallback)
        => int.TryParse(text, out var value) ? value : fallback;

    private static string? Option(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return args[index][prefix.Length..];
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length) return args[index + 1];
        }
        return null;
    }
}

internal sealed record PimaxUsbPortCycleHubIdentity(
    string InterfacePath,
    string PnpInstanceId,
    string ContainerId,
    string[] HardwareIds,
    string[] LocationPaths,
    string Vid,
    string Pid,
    string HubType,
    bool IsRootHub);

internal sealed record PimaxUsbPortCyclePortIdentity(
    string ConnectorGroupId,
    int ConnectionIndex,
    string OccupantClassification,
    string[] DescendantPnpInstanceIds);

internal sealed record PimaxUsbPortCycleInventoryItem(
    string HubPnpInstanceId,
    int ConnectionIndex,
    string ConnectionStatus,
    ushort VendorId,
    ushort ProductId,
    string? ChildPnpInstanceId,
    string[] DescendantPnpInstanceIds);

internal sealed record PimaxPhase29LogIdentity(string Path, long MinimumLength, DateTimeOffset MinimumLastWriteTime);

internal sealed record PimaxPhase29IntegritySignature(
    string TaskName,
    string TaskPath,
    string Execute,
    string Arguments,
    string WorkingDirectory,
    string WatcherSha256,
    PimaxPhase29LogIdentity SupervisorLog,
    PimaxPhase29LogIdentity ConfiguratorLog);

internal sealed record PimaxUsbPortCycleTargetSignature(
    string Schema,
    PimaxUsbPortCycleHubIdentity Usb2Hub,
    PimaxUsbPortCyclePortIdentity PimaxUsb2Port,
    PimaxUsbPortCycleHubIdentity SuperSpeedHub,
    PimaxUsbPortCyclePortIdentity PimaxSuperSpeedPort,
    PimaxUsbPortCyclePortIdentity ViveUsb2Port,
    PimaxUsbPortCyclePortIdentity ViveSuperSpeedPort,
    PimaxUsbPortCycleInventoryItem[] UnrelatedPortInventory,
    PimaxPhase29IntegritySignature Phase29);

internal sealed record PimaxUsbPortCycleObserverBinding(
    string SessionId,
    string ScenarioId,
    DateTimeOffset UpdatedAt,
    string State,
    string ConnectMarkerId,
    int ConnectMarkerSequence,
    string ConnectMarkerType,
    string ConnectMarkerSource,
    string ConnectMarkerLabel,
    DateTimeOffset ConnectMarkerTimestamp,
    double ConnectMarkerAgeSeconds,
    double MaximumConnectMarkerAgeSeconds,
    string ConnectAction);

internal sealed record PimaxUsbPortCycleStableObserverIdentity(
    string SessionId,
    string ScenarioId,
    string ConnectMarkerId,
    int ConnectMarkerSequence,
    string ConnectMarkerType,
    string ConnectMarkerSource,
    string ConnectMarkerLabel,
    DateTimeOffset ConnectMarkerTimestamp,
    double MaximumConnectMarkerAgeSeconds,
    string ConnectAction);

internal sealed record PimaxUsbPortCycleRuntimeState(
    PimaxUsbPhysicalPortSnapshot Topology,
    PimaxRegistrationAssessmentSnapshot Registration,
    PimaxConnectivitySnapshot Connectivity,
    bool PimaxPlayRunning,
    bool SteamVrRunning,
    bool RecoveryExperimentActive,
    PimaxUsbPortCycleObserverBinding? Observer,
    PimaxPhase29IntegritySignature? Phase29);

internal sealed record PimaxUsbPortCyclePlan(
    string ExperimentKind,
    PimaxUsbPortCycleHubIdentity Usb2Hub,
    int ConnectionIndex,
    string PimaxConnectorGroupId,
    PimaxUsbPortCycleHubIdentity SuperSpeedCompanionHub,
    int SuperSpeedCompanionIndex,
    string ViveConnectorGroupId,
    int ViveConnectionIndex,
    string[] PimaxDescendantInventory,
    int UnrelatedOccupantCount,
    PimaxUsbPortCycleInventoryItem[] OtherPortOccupants,
    string RegistrationState,
    string RegistrationConfidence,
    string FilteredConnectivity,
    bool PimaxPlayRunning,
    bool SteamVrRunning,
    PimaxUsbPortCycleObserverBinding? Observer,
    string PlannedIoctl,
    int ExactRequestCount,
    string[] ExcludedOperations,
    string BindingSha256);

internal sealed record PimaxUsbPortCycleSafety(bool Permitted, string[] ChecksPassed, string[] RefusalReasons, string[] Warnings);
internal sealed record PimaxUsbPortCycleUacLaunch(bool Attempted, bool Started, bool Cancelled, int? ProcessId, string? Failure);

internal sealed record PimaxUsbPortCycleExperimentResult(
    string SchemaVersion,
    string ExperimentId,
    string Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    PimaxUsbPortCyclePlan? Plan,
    PimaxUsbPortCycleSafety Safety,
    string? ConfirmationToken,
    DateTimeOffset? ConfirmationTokenExpiresAt,
    string? PrivilegedRequestPath,
    string? PrivilegedRequestSha256,
    PimaxUsbPortCyclePrivilegedResult? PrivilegedResult,
    PimaxUsbPortCycleObservation? Observation,
    PimaxUsbPortCycleUacLaunch? UacLaunch,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxUsbPortCyclePrivilegedPayload(
    string Schema,
    string ExperimentId,
    string ExperimentKind,
    PimaxUsbPortCycleTargetSignature TargetSignature,
    PimaxUsbPortCyclePlan Plan,
    string ObserverStatusPath,
    string MarkerFilePath,
    string ObserverSessionId,
    string ObserverScenarioId,
    string ConnectMarkerId,
    int ConnectMarkerSequence,
    string ConnectMarkerType,
    string ConnectMarkerSource,
    string ConnectMarkerLabel,
    DateTimeOffset ConnectMarkerTimestamp,
    double MaximumConnectMarkerAgeSeconds,
    string ConnectAction,
    string ConfirmationToken,
    string Nonce,
    DateTimeOffset CreatedAt,
    DateTimeOffset TokenExpiresAt,
    DateTimeOffset ExpiresAt,
    string OutputResultPath);

internal sealed record PimaxUsbPortCyclePrivilegedRequest(PimaxUsbPortCyclePrivilegedPayload Payload, string RequestSha256);

internal sealed record PimaxUsbPortCyclePrivilegedResult(
    string Schema,
    string ExperimentId,
    int HelperPid,
    bool Elevated,
    string? RequestSha256,
    bool RequestValid,
    bool ExactHubValid,
    bool ExactConnectionIndexValid,
    bool CompanionValid,
    bool ViveExclusionValid,
    string? PreRequestPortState,
    DateTimeOffset? RequestSubmittedAt,
    bool? IoctlReturnedSuccess,
    int? Win32Error,
    uint? StatusReturned,
    int RequestCount,
    DateTimeOffset CompletedAt,
    bool Success,
    string FailureCategory,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxUsbPortCycleObservation(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Outcome,
    bool Usb2Transitioned,
    bool SuperSpeedTransitioned,
    bool ViveStable,
    bool UnrelatedPortsStable,
    string[] MissingPimaxDescendants,
    string FinalRegistrationState,
    string FinalRegistrationConfidence);

internal static class PimaxUsbPortCycleObservationClassifier
{
    public static string Classify(bool usb2Transitioned, bool superSpeedTransitioned, bool viveStable, bool unrelatedPortsStable, int missingDescendantCount, bool registeredReady)
    {
        if (!viveStable || !unrelatedPortsStable) return "unexpected-nontarget-transition";
        if (!usb2Transitioned && !superSpeedTransitioned) return "no-port-transition-observed";
        if (!usb2Transitioned && superSpeedTransitioned) return "unexpected-superspeed-only-transition";
        if (missingDescendantCount > 0) return "partial-pimax-descendant-return";
        if (registeredReady) return superSpeedTransitioned ? "both-sides-transitioned-registration-ready" : "usb2-only-transitioned-registration-ready";
        return superSpeedTransitioned ? "both-sides-transitioned-registration-unavailable" : "usb2-only-transitioned-registration-unavailable";
    }
}

internal interface IPimaxUsbPortCycleStateCollector
{
    Task<PimaxUsbPortCycleRuntimeState> CollectAsync(PimaxUsbPortCycleTargetSignature signature, string? observerStatusPath, string? markerFilePath, CancellationToken cancellationToken);
}

internal sealed class WindowsPimaxUsbPortCycleStateCollector(SupervisorConfig config) : IPimaxUsbPortCycleStateCollector
{
    public async Task<PimaxUsbPortCycleRuntimeState> CollectAsync(PimaxUsbPortCycleTargetSignature signature, string? observerStatusPath, string? markerFilePath, CancellationToken cancellationToken)
    {
        var topology = new PimaxUsbPhysicalPortSnapshotCollector().Collect();
        var registration = await new PimaxRegistrationAssessmentCoordinator().CollectAsync(config, cancellationToken);
        var connectivity = await new PimaxConnectivitySnapshotCollector().CollectAsync(config, cancellationToken);
        var discovery = await new WindowsPimaxClientProcessController().DiscoverAsync(cancellationToken);
        return new(topology, registration, connectivity, discovery.Target is not null, new DefaultPimaxRecoveryEnvironment().IsSteamVrRunning(), PimaxRecoveryExperimentRunner.HasActiveExperiment,
            PimaxUsbPortCycleObserverReader.Read(observerStatusPath, markerFilePath, DateTimeOffset.UtcNow),
            PimaxPhase29IntegrityCollector.Collect(signature.Phase29));
    }
}

internal static class PimaxUsbPortCycleObserverReader
{
    internal const double MaximumMarkerAgeSeconds = 120;
    internal const string ConnectAction = "pimax-play-pimax-crystal-connect-and-scan-start";
    private static readonly string[] ObserverLabels = ["observer-started", "observer-start"];
    private static readonly string[] InfoLabels = ["pimax-info-opened"];
    private static readonly string[] ModelLabels = ["pimax-crystal-model-selected"];
    private static readonly string[] ReadinessLabels = ["connect-ready-before-action", "ready-to-press-connect"];
    private static readonly string[] ConnectLabels = ["connect-action-completed", "connect-pressed-scan-started"];

    private sealed record Marker(string Label, DateTimeOffset Timestamp, string Source, string Type, string Action, string Id, int Sequence);

    public static PimaxUsbPortCycleObserverBinding? Read(string? statusPath, string? markerPath, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(statusPath) || string.IsNullOrWhiteSpace(markerPath) || !File.Exists(statusPath) || !File.Exists(markerPath)) return null;
        using var status = JsonDocument.Parse(File.ReadAllText(statusPath));
        var root = status.RootElement;
        var state = root.TryGetProperty("state", out var stateValue) ? stateValue.GetString() ?? "" : "";
        var session = root.TryGetProperty("sessionId", out var sessionValue) ? sessionValue.GetString() ?? "" : "";
        var scenario = root.TryGetProperty("scenario", out var scenarioValue) ? scenarioValue.GetString() ?? "" : "";
        var updated = root.TryGetProperty("updatedAt", out var updatedValue) && updatedValue.TryGetDateTimeOffset(out var parsedUpdated) ? parsedUpdated : DateTimeOffset.MinValue;
        var markers = File.ReadLines(markerPath).Select((line, index) => TryReadMarker(line, index + 1, session, scenario)).Where(value => value is not null).Cast<Marker>().ToArray();
        var connectIndex = Array.FindLastIndex(markers, value => ConnectLabels.Contains(value.Label, StringComparer.OrdinalIgnoreCase));
        if (connectIndex < 0) return null;
        var observerIndex = FindLastBefore(markers, connectIndex, ObserverLabels);
        var infoIndex = FindLastBefore(markers, connectIndex, InfoLabels);
        var modelIndex = FindLastBefore(markers, connectIndex, ModelLabels);
        var readyIndex = FindLastBefore(markers, connectIndex, ReadinessLabels);
        if (string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(scenario)
            || observerIndex < 0 || infoIndex <= observerIndex || modelIndex <= infoIndex || readyIndex <= modelIndex || connectIndex <= readyIndex) return null;
        var marker = markers[connectIndex];
        return new(session, scenario, updated, state, marker.Id, marker.Sequence, marker.Type, marker.Source, marker.Label, marker.Timestamp,
            Math.Max(0, (now - marker.Timestamp.ToUniversalTime()).TotalSeconds), MaximumMarkerAgeSeconds, marker.Action);
    }

    private static int FindLastBefore(Marker[] markers, int before, string[] labels)
    {
        for (var index = before - 1; index >= 0; index--)
            if (labels.Contains(markers[index].Label, StringComparer.OrdinalIgnoreCase)) return index;
        return -1;
    }

    private static Marker? TryReadMarker(string line, int fallbackSequence, string session, string scenario)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("label", out var label)) return null;
            var timestamp = root.TryGetProperty("timestamp", out var value) && value.TryGetDateTimeOffset(out var parsed) ? parsed : DateTimeOffset.MinValue;
            var text = label.GetString()?.Trim() ?? "";
            var source = root.TryGetProperty("source", out var sourceValue) ? sourceValue.GetString() ?? "user-confirmed" : "user-confirmed";
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() ?? "operator-marker" : "operator-marker";
            var action = root.TryGetProperty("action", out var actionValue) ? actionValue.GetString() ?? "" : "";
            if (ConnectLabels.Contains(text, StringComparer.OrdinalIgnoreCase)) action = string.IsNullOrWhiteSpace(action) ? ConnectAction : action;
            var sequence = root.TryGetProperty("sequence", out var sequenceValue) && sequenceValue.TryGetInt32(out var parsedSequence) ? parsedSequence : fallbackSequence;
            var id = root.TryGetProperty("markerId", out var idValue) ? idValue.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(id)) id = Fingerprint(new { session, scenario, sequence, label = text, source, type, timestamp, action });
            return new(text, timestamp, source, type, action, id, sequence);
        }
        catch (JsonException) { return null; }
    }

    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, PimaxUsbPortCycleJson.Options)));
}

internal static class PimaxPhase29IntegrityCollector
{
    public static PimaxPhase29IntegritySignature? Collect(PimaxPhase29IntegritySignature expected)
    {
        try
        {
            var start = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add("/Query"); start.ArgumentList.Add("/TN"); start.ArgumentList.Add(expected.TaskPath + expected.TaskName); start.ArgumentList.Add("/XML");
            using var process = Process.Start(start);
            if (process is null || !process.WaitForExit(5000) || process.ExitCode != 0) return null;
            var xml = XDocument.Parse(process.StandardOutput.ReadToEnd());
            XNamespace ns = xml.Root?.Name.Namespace ?? XNamespace.None;
            var exec = xml.Descendants(ns + "Exec").Single();
            var command = exec.Element(ns + "Command")?.Value ?? "";
            var arguments = exec.Element(ns + "Arguments")?.Value ?? "";
            var working = exec.Element(ns + "WorkingDirectory")?.Value ?? "";
            return new(expected.TaskName, expected.TaskPath, command, arguments, working, HashFile(command), CurrentLog(expected.SupervisorLog), CurrentLog(expected.ConfiguratorLog));
        }
        catch { return null; }
    }

    private static PimaxPhase29LogIdentity CurrentLog(PimaxPhase29LogIdentity expected)
    {
        var info = new FileInfo(expected.Path);
        return new(expected.Path, info.Exists ? info.Length : -1, info.Exists ? info.LastWriteTimeUtc : DateTimeOffset.MinValue);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

internal static class PimaxUsbPortCycleTargetValidator
{
    public static (PimaxUsbPortCyclePlan? Plan, PimaxUsbPortCycleSafety Safety) Validate(PimaxUsbPortCycleTargetSignature expected, PimaxUsbPortCycleRuntimeState current, DateTimeOffset now)
    {
        var passed = new List<string>();
        var failed = new List<string>();
        var warnings = new List<string>();
        var hubs = current.Topology.Hubs;
        var usb2Matches = hubs.Where(hub => HubMatches(expected.Usb2Hub, hub)).ToArray();
        var ssMatches = hubs.Where(hub => HubMatches(expected.SuperSpeedHub, hub)).ToArray();
        Check(usb2Matches.Length == 1, "Exact external USB 2 hub exists once.", "Exact external USB 2 hub is missing or ambiguous.", passed, failed);
        Check(ssMatches.Length == 1, "Exact SuperSpeed companion hub exists once.", "Exact SuperSpeed companion hub is missing or ambiguous.", passed, failed);
        if (usb2Matches.Length != 1 || ssMatches.Length != 1) return (null, new(false, passed.ToArray(), failed.ToArray(), warnings.ToArray()));
        var usb2Hub = usb2Matches[0]; var ssHub = ssMatches[0];
        Check(!usb2Hub.IsRootHub && !usb2Hub.HubType.Contains("root", StringComparison.OrdinalIgnoreCase), "Target is not a root hub.", "Target resolves to a root hub.", passed, failed);
        Check(!Text(usb2Hub).Contains("xhci", StringComparison.OrdinalIgnoreCase), "Target is not an xHCI controller.", "Target resembles an xHCI controller.", passed, failed);

        var pimax2 = FindPort(current.Topology, usb2Hub, expected.PimaxUsb2Port.ConnectionIndex);
        var pimax3 = FindPort(current.Topology, ssHub, expected.PimaxSuperSpeedPort.ConnectionIndex);
        var vive2 = FindPort(current.Topology, usb2Hub, expected.ViveUsb2Port.ConnectionIndex);
        var vive3 = FindPort(current.Topology, ssHub, expected.ViveSuperSpeedPort.ConnectionIndex);
        Check(PortMatches(expected.PimaxUsb2Port, pimax2), "Pimax USB 2 connector identity matches.", "Pimax USB 2 connector identity changed.", passed, failed);
        Check(PortMatches(expected.PimaxSuperSpeedPort, pimax3), "Pimax SuperSpeed connector identity matches.", "Pimax SuperSpeed connector identity changed.", passed, failed);
        Check(PortMatches(expected.ViveUsb2Port, vive2) && PortMatches(expected.ViveSuperSpeedPort, vive3), "Vive connector remains present on index 2.", "Vive connector is missing or changed.", passed, failed);
        Check(pimax2?.Companions.Any(value => value.Reciprocal && value.CompanionPortNumber == 4 && PimaxUsbConnectorGrouper.SameHub(value.CompanionHubSymbolicLink, ssHub.InterfacePath)) == true
            && pimax3?.Companions.Any(value => value.Reciprocal && value.CompanionPortNumber == 4 && PimaxUsbConnectorGrouper.SameHub(value.CompanionHubSymbolicLink, usb2Hub.InterfacePath)) == true,
            "Pimax companion mapping is reciprocal.", "Pimax companion mapping is not reciprocal.", passed, failed);
        Check(expected.PimaxUsb2Port.ConnectorGroupId != expected.ViveUsb2Port.ConnectorGroupId && pimax2?.PhysicalConnectorGroupId != vive2?.PhysicalConnectorGroupId,
            "Pimax and Vive connector groups are distinct.", "Pimax and Vive connector groups overlap.", passed, failed);
        Check(pimax2?.OccupantClassification == "pimax-related" && pimax3?.OccupantClassification == "pimax-related", "Pimax occupants match expected signature.", "Pimax connector contains an unexpected occupant.", passed, failed);
        var pimaxGroup = current.Topology.ConnectorGroups.SingleOrDefault(group => group.GroupId == expected.PimaxUsb2Port.ConnectorGroupId);
        Check(pimaxGroup is not null && !pimaxGroup.ContainsUnrelatedOccupant, "Pimax connector has no unrelated occupant.", "Pimax connector group is missing or reports an unrelated occupant.", passed, failed);
        Check(InventoryMatches(expected.UnrelatedPortInventory, Inventory(current.Topology, usb2Hub, ssHub)), "Unrelated-port inventory is unchanged.", "Unrelated-port inventory changed.", passed, failed);
        Check(current.Registration.Assessment.State == PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration && current.Registration.Assessment.Confidence == PimaxRegistrationConfidence.Probable,
            "Headset is probably powered on and awaiting registration.", "Headset is not stably awaiting registration.", passed, failed);
        Check(!current.Registration.Assessment.Evidence.CrystalRuntimeGroupPresent, "Crystal runtime group is absent as expected.", "Crystal runtime group is unexpectedly present.", passed, failed);
        Check(current.PimaxPlayRunning, "Pimax Play is running.", "Pimax Play is not running or its target is ambiguous.", passed, failed);
        Check(!current.SteamVrRunning, "SteamVR is closed.", "SteamVR is running.", passed, failed);
        Check(!current.RecoveryExperimentActive, "No concurrent recovery experiment is active.", "Another recovery experiment is active.", passed, failed);
        Check(current.Observer is not null && current.Observer.State.Equals("running", StringComparison.OrdinalIgnoreCase) && (now - current.Observer.UpdatedAt.ToUniversalTime()) <= TimeSpan.FromSeconds(5),
            "Observer is active.", "Observer is missing, stopped, or stale.", passed, failed);
        Check(IsMarkerFresh(current.Observer, now),
            "Connect scan marker is recent.", "Connect scan marker is missing or stale.", passed, failed);
        Check(Phase29Matches(expected.Phase29, current.Phase29), "Phase 29B deployment and logs remain intact.", "Phase 29B integrity changed or could not be verified.", passed, failed);

        var plan = new PimaxUsbPortCyclePlan(PimaxUsbPortCycleExperimentKind.CycleExactExternalHubUsb2Port, Identity(usb2Hub), 4,
            expected.PimaxUsb2Port.ConnectorGroupId, Identity(ssHub), 4, expected.ViveUsb2Port.ConnectorGroupId, 2,
            NormalizeStrings(pimax2?.DescendantPnpInstanceIds ?? []), 0,
            Inventory(current.Topology, usb2Hub, ssHub), current.Registration.Assessment.State, current.Registration.Assessment.Confidence,
            current.Connectivity.Assessment.Value, current.PimaxPlayRunning, current.SteamVrRunning, current.Observer,
            "IOCTL_USB_HUB_CYCLE_PORT", 1, PimaxUsbPortCycleSafetyBoundary.ExcludedOperations, "");
        plan = plan with { BindingSha256 = StablePlanFingerprint(plan) };
        return (plan, new(failed.Count == 0, passed.ToArray(), failed.ToArray(), warnings.ToArray()));
    }

    internal static PimaxUsbPortCycleInventoryItem[] Inventory(PimaxUsbPhysicalPortSnapshot snapshot, params PimaxUsbHubRecord[] hubs)
        => snapshot.Ports.Where(port => hubs.Any(hub => hub.HubId == port.HubId) && port.ConnectionIndex is not 2 and not 4)
            .Select(port => new PimaxUsbPortCycleInventoryItem(hubs.Single(hub => hub.HubId == port.HubId).PnpInstanceId ?? "", port.ConnectionIndex,
                port.ConnectionStatus, port.VendorId, port.ProductId, port.ChildPnpInstanceId, NormalizeStrings(port.DescendantPnpInstanceIds)))
            .GroupBy(InventoryKey, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .OrderBy(item => item.HubPnpInstanceId, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ConnectionIndex)
            .ThenBy(item => item.ChildPnpInstanceId, StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool InventoryMatches(PimaxUsbPortCycleInventoryItem[] expected, PimaxUsbPortCycleInventoryItem[] current)
        => Fingerprint(NormalizeInventory(expected)) == Fingerprint(NormalizeInventory(current));
    private static PimaxUsbPortRecord? FindPort(PimaxUsbPhysicalPortSnapshot snapshot, PimaxUsbHubRecord hub, int index)
        => snapshot.Ports.SingleOrDefault(port => port.HubId == hub.HubId && port.ConnectionIndex == index);
    private static bool PortMatches(PimaxUsbPortCyclePortIdentity expected, PimaxUsbPortRecord? current)
        => current is not null && current.ConnectionIndex == expected.ConnectionIndex
            && current.PhysicalConnectorGroupId == expected.ConnectorGroupId && current.DeviceConnected
            && Fingerprint(NormalizeStrings(current.DescendantPnpInstanceIds)) == Fingerprint(NormalizeStrings(expected.DescendantPnpInstanceIds));
    private static bool HubMatches(PimaxUsbPortCycleHubIdentity expected, PimaxUsbHubRecord current)
        => Same(expected.InterfacePath, current.InterfacePath) && Same(expected.PnpInstanceId, current.PnpInstanceId)
            && Same(expected.ContainerId, current.ContainerId) && Same(expected.Vid, current.Vid) && Same(expected.Pid, current.Pid)
            && Same(expected.HubType, current.HubType) && expected.IsRootHub == current.IsRootHub
            && Fingerprint(NormalizeStrings(expected.HardwareIds)) == Fingerprint(NormalizeStrings(current.HardwareIds))
            && Fingerprint(NormalizeStrings(expected.LocationPaths)) == Fingerprint(NormalizeStrings(current.LocationPaths));
    private static bool Phase29Matches(PimaxPhase29IntegritySignature expected, PimaxPhase29IntegritySignature? current)
        => current is not null && Same(expected.TaskName, current.TaskName) && Same(expected.TaskPath, current.TaskPath)
            && Same(expected.Execute, current.Execute) && Same(expected.Arguments, current.Arguments) && Same(expected.WorkingDirectory, current.WorkingDirectory)
            && Same(expected.WatcherSha256, current.WatcherSha256)
            && current.SupervisorLog.MinimumLength >= expected.SupervisorLog.MinimumLength && current.ConfiguratorLog.MinimumLength >= expected.ConfiguratorLog.MinimumLength
            && current.SupervisorLog.MinimumLastWriteTime >= expected.SupervisorLog.MinimumLastWriteTime && current.ConfiguratorLog.MinimumLastWriteTime >= expected.ConfiguratorLog.MinimumLastWriteTime
            && Same(expected.SupervisorLog.Path, current.SupervisorLog.Path) && Same(expected.ConfiguratorLog.Path, current.ConfiguratorLog.Path);
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Text(PimaxUsbHubRecord hub) => string.Join('|', hub.PnpInstanceId, hub.Product, hub.DeviceClass, hub.ClassGuid, string.Join('|', hub.HardwareIds));
    private static void Check(bool condition, string yes, string no, List<string> passed, List<string> failed) { if (condition) passed.Add(yes); else failed.Add(no); }
    internal static PimaxUsbPortCycleHubIdentity Identity(PimaxUsbHubRecord hub) => new(hub.InterfacePath, hub.PnpInstanceId ?? "", hub.ContainerId ?? "", NormalizeStrings(hub.HardwareIds), NormalizeStrings(hub.LocationPaths), hub.Vid ?? "", hub.Pid ?? "", hub.HubType, hub.IsRootHub);
    internal static bool IsMarkerFresh(PimaxUsbPortCycleObserverBinding? observer, DateTimeOffset now)
    {
        if (observer is null || observer.ConnectMarkerTimestamp == DateTimeOffset.MinValue || observer.MaximumConnectMarkerAgeSeconds != PimaxUsbPortCycleObserverReader.MaximumMarkerAgeSeconds) return false;
        var age = (now.ToUniversalTime() - observer.ConnectMarkerTimestamp.ToUniversalTime()).TotalSeconds;
        return age >= 0 && age <= observer.MaximumConnectMarkerAgeSeconds;
    }
    internal static string StablePlanFingerprint(PimaxUsbPortCyclePlan plan) => Fingerprint(new
    {
        plan.ExperimentKind,
        Usb2Hub = NormalizeHub(plan.Usb2Hub),
        plan.ConnectionIndex,
        plan.PimaxConnectorGroupId,
        SuperSpeedCompanionHub = NormalizeHub(plan.SuperSpeedCompanionHub),
        plan.SuperSpeedCompanionIndex,
        plan.ViveConnectorGroupId,
        plan.ViveConnectionIndex,
        PimaxDescendantInventory = NormalizeStrings(plan.PimaxDescendantInventory),
        plan.UnrelatedOccupantCount,
        OtherPortOccupants = NormalizeInventory(plan.OtherPortOccupants),
        plan.RegistrationState,
        plan.RegistrationConfidence,
        plan.FilteredConnectivity,
        plan.PimaxPlayRunning,
        plan.SteamVrRunning,
        Observer = StableObserver(plan.Observer),
        plan.PlannedIoctl,
        plan.ExactRequestCount,
        ExcludedOperations = NormalizeStrings(plan.ExcludedOperations)
    });
    internal static string StableTargetSignatureFingerprint(PimaxUsbPortCycleTargetSignature signature) => Fingerprint(new
    {
        signature.Schema,
        Usb2Hub = NormalizeHub(signature.Usb2Hub),
        PimaxUsb2Port = NormalizePort(signature.PimaxUsb2Port),
        SuperSpeedHub = NormalizeHub(signature.SuperSpeedHub),
        PimaxSuperSpeedPort = NormalizePort(signature.PimaxSuperSpeedPort),
        ViveUsb2Port = NormalizePort(signature.ViveUsb2Port),
        ViveSuperSpeedPort = NormalizePort(signature.ViveSuperSpeedPort),
        UnrelatedPortInventory = NormalizeInventory(signature.UnrelatedPortInventory),
        signature.Phase29
    });
    internal static string PrivilegedRequestFingerprint(PimaxUsbPortCyclePrivilegedPayload payload) => Fingerprint(new
    {
        payload.Schema,
        payload.ExperimentId,
        payload.ExperimentKind,
        TargetSignatureSha256 = StableTargetSignatureFingerprint(payload.TargetSignature),
        PlanBindingSha256 = payload.Plan.BindingSha256,
        payload.ObserverStatusPath,
        payload.MarkerFilePath,
        payload.ObserverSessionId,
        payload.ObserverScenarioId,
        payload.ConnectMarkerId,
        payload.ConnectMarkerSequence,
        payload.ConnectMarkerType,
        payload.ConnectMarkerSource,
        payload.ConnectMarkerLabel,
        payload.ConnectMarkerTimestamp,
        payload.MaximumConnectMarkerAgeSeconds,
        payload.ConnectAction,
        payload.ConfirmationToken,
        payload.Nonce,
        payload.CreatedAt,
        payload.TokenExpiresAt,
        payload.ExpiresAt,
        payload.OutputResultPath
    });
    internal static PimaxUsbPortCycleStableObserverIdentity? StableObserver(PimaxUsbPortCycleObserverBinding? observer)
        => observer is null ? null : new(observer.SessionId, observer.ScenarioId, observer.ConnectMarkerId, observer.ConnectMarkerSequence,
            observer.ConnectMarkerType, observer.ConnectMarkerSource, observer.ConnectMarkerLabel, observer.ConnectMarkerTimestamp,
            observer.MaximumConnectMarkerAgeSeconds, observer.ConnectAction);
    private static PimaxUsbPortCycleHubIdentity NormalizeHub(PimaxUsbPortCycleHubIdentity hub)
        => hub with { HardwareIds = NormalizeStrings(hub.HardwareIds), LocationPaths = NormalizeStrings(hub.LocationPaths) };
    private static PimaxUsbPortCyclePortIdentity NormalizePort(PimaxUsbPortCyclePortIdentity port)
        => port with { DescendantPnpInstanceIds = NormalizeStrings(port.DescendantPnpInstanceIds) };
    private static PimaxUsbPortCycleInventoryItem[] NormalizeInventory(PimaxUsbPortCycleInventoryItem[] items)
        => items.Select(item => item with { DescendantPnpInstanceIds = NormalizeStrings(item.DescendantPnpInstanceIds) })
            .GroupBy(InventoryKey, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .OrderBy(item => item.HubPnpInstanceId, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ConnectionIndex)
            .ThenBy(item => item.ChildPnpInstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
    private static string InventoryKey(PimaxUsbPortCycleInventoryItem item)
        => string.Join('|', item.HubPnpInstanceId, item.ConnectionIndex, item.ConnectionStatus, item.VendorId, item.ProductId,
            item.ChildPnpInstanceId, string.Join('\u001f', NormalizeStrings(item.DescendantPnpInstanceIds)));
    internal static string[] NormalizeStrings(IEnumerable<string> values)
        => values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    internal static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, PimaxUsbPortCycleJson.Options)));
}

internal static class PimaxUsbPortCycleSafetyBoundary
{
    public static readonly string[] ExcludedOperations =
    [
        "SuperSpeed companion cycle", "second cycle request", "retry", "fallback target", "machine-wide rescan", "hub reset", "xHCI controller reset",
        "device disable", "device enable", "device restart", "device remove", "device eject", "device uninstall", "devnode re-enumeration",
        "service restart", "process restart or kill", "Pimax Play restart", "SteamVR start or restart", "Connect-button automation", "keyboard or mouse automation"
    ];
}

internal static class PimaxUsbPortCycleConfirmationToken
{
    private sealed record Payload(string ExperimentId, string Kind, string BindingSha256, PimaxUsbPortCycleStableObserverIdentity? MarkerIdentity, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string Nonce);

    public static (string Token, DateTimeOffset ExpiresAt) Create(string experimentId, PimaxUsbPortCyclePlan plan, DateTimeOffset now)
    {
        var expires = now.AddMinutes(5);
        var payload = new Payload(experimentId, plan.ExperimentKind, plan.BindingSha256, PimaxUsbPortCycleTargetValidator.StableObserver(plan.Observer), now, expires, Guid.NewGuid().ToString("N"));
        var body = Base64(JsonSerializer.SerializeToUtf8Bytes(payload, PimaxUsbPortCycleJson.Options));
        return ($"{body}.{Base64(Sign(body))}", expires);
    }

    public static (bool Accepted, string? Reason, string? Nonce, DateTimeOffset? ExpiresAt) Validate(string? token, string experimentId, PimaxUsbPortCyclePlan plan, DateTimeOffset now, string? consumptionDirectory, bool consume)
    {
        if (string.IsNullOrWhiteSpace(token)) return (false, "Missing confirmation token.", null, null);
        var parts = token.Split('.', 2); if (parts.Length != 2) return (false, "Token format is invalid.", null, null);
        var signature = Base64(Sign(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(signature), Encoding.ASCII.GetBytes(parts[1]))) return (false, "Token signature is invalid.", null, null);
        Payload? payload;
        try { payload = JsonSerializer.Deserialize<Payload>(Base64Decode(parts[0]), PimaxUsbPortCycleJson.Options); }
        catch (Exception ex) { return (false, $"Token payload is invalid: {ex.Message}", null, null); }
        if (payload is null || payload.ExpiresAt <= now) return (false, "Token expired.", payload?.Nonce, payload?.ExpiresAt);
        if (payload.ExperimentId != experimentId || payload.Kind != plan.ExperimentKind || payload.BindingSha256 != plan.BindingSha256
            || PimaxUsbPortCycleTargetValidator.Fingerprint(payload.MarkerIdentity) != PimaxUsbPortCycleTargetValidator.Fingerprint(PimaxUsbPortCycleTargetValidator.StableObserver(plan.Observer)))
            return (false, "Token does not match the current topology, state, observer, or Connect marker.", payload.Nonce, payload.ExpiresAt);
        if (consume)
        {
            if (string.IsNullOrWhiteSpace(consumptionDirectory)) return (false, "A token-consumption directory is required.", payload.Nonce, payload.ExpiresAt);
            Directory.CreateDirectory(consumptionDirectory);
            var used = Path.Combine(consumptionDirectory, $"port-cycle-token-{payload.Nonce}.used");
            try { using var stream = new FileStream(used, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.WriteByte(1); }
            catch (IOException) { return (false, "Token was already used.", payload.Nonce, payload.ExpiresAt); }
        }
        return (true, null, payload.Nonce, payload.ExpiresAt);
    }

    private static byte[] Sign(string body)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes($"PimaxVrcSupervisor.Phase28C3B.v1|{Environment.MachineName}|{Environment.UserName}"));
        using var hmac = new HMACSHA256(key); return hmac.ComputeHash(Encoding.ASCII.GetBytes(body));
    }
    private static string Base64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64Decode(string value) { var text = value.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=')); }
}

internal sealed class PimaxUsbPortCycleExperimentRunner
{
    public const string ExactConfirmationPhrase = "CONFIRM EXACT PIMAX USB2 PORT CYCLE EXPERIMENT";
    private readonly IPimaxUsbPortCycleStateCollector _collector;
    private readonly Func<DateTimeOffset> _now;
    public PimaxUsbPortCycleExperimentRunner(IPimaxUsbPortCycleStateCollector collector, Func<DateTimeOffset>? now = null) { _collector = collector; _now = now ?? (() => DateTimeOffset.UtcNow); }

    public async Task<PimaxUsbPortCycleExperimentResult> RunAsync(PimaxUsbPortCycleRequest request, CancellationToken cancellationToken)
    {
        var started = _now(); var id = "phase28c3b-" + Guid.NewGuid().ToString("N"); var errors = new List<string>(); var warnings = new List<string>();
        if (request.Mode == PimaxUsbPortCycleMode.ExecuteElevatedHelper) return await PimaxUsbPortCycleElevatedExecutor.RunAsExperimentResultAsync(request, started, cancellationToken);
        if (request.Mode == PimaxUsbPortCycleMode.ObserveResult) return await ObserveAsync(request, id, started, cancellationToken);
        PimaxUsbPortCycleTargetSignature? signature = null;
        try { signature = Read<PimaxUsbPortCycleTargetSignature>(request.TargetSignaturePath); }
        catch (Exception ex) { errors.Add(ex.Message); }
        if (signature is null) return Result(id, request.Mode, started, null, new(false, [], ["A valid target signature is required."], []), errors: errors.ToArray());
        var current = await _collector.CollectAsync(signature, request.ObserverStatusPath, request.MarkerFilePath, cancellationToken);
        var (plan, safety) = PimaxUsbPortCycleTargetValidator.Validate(signature, current, _now());
        if (request.Mode == PimaxUsbPortCycleMode.DryRun)
        {
            string? token = null; DateTimeOffset? expires = null;
            if (safety.Permitted && plan is not null) (token, expires) = PimaxUsbPortCycleConfirmationToken.Create(id, plan, _now());
            return Result(id, request.Mode, started, plan, safety, token, expires, warnings: warnings.ToArray(), errors: errors.ToArray());
        }
        if (request.Mode != PimaxUsbPortCycleMode.Prepare) return Result(id, request.Mode, started, plan, new(false, safety.ChecksPassed, [.. safety.RefusalReasons, "Unsupported mode."], safety.Warnings));
        if (!safety.Permitted || plan is null) return Result(id, request.Mode, started, plan, safety);
        if (!string.Equals(request.ConfirmationPhrase, ExactConfirmationPhrase, StringComparison.Ordinal))
            return Result(id, request.Mode, started, plan, new(false, safety.ChecksPassed, ["Exact confirmation phrase was not supplied."], safety.Warnings));

        var tokenId = TokenExperimentId(request.ConfirmationToken);
        if (tokenId is null) return Result(id, request.Mode, started, plan, new(false, safety.ChecksPassed, ["Confirmation token payload could not be read."], safety.Warnings));
        var validation = PimaxUsbPortCycleConfirmationToken.Validate(request.ConfirmationToken, tokenId, plan, _now(), request.EvidenceDirectory, consume: true);
        if (!validation.Accepted) return Result(id, request.Mode, started, plan, new(false, safety.ChecksPassed, [validation.Reason!], safety.Warnings));
        if (!PimaxUsbPortCycleTargetValidator.IsMarkerFresh(plan.Observer, _now()))
            return Result(id, request.Mode, started, plan, new(false, safety.ChecksPassed, ["Connect scan marker became stale before privileged request preparation."], safety.Warnings));
        if (string.IsNullOrWhiteSpace(request.PrivilegedRequestPath) || string.IsNullOrWhiteSpace(request.PrivilegedResultPath) || plan.Observer is null)
            return Result(id, request.Mode, started, plan, new(false, safety.ChecksPassed, ["Request path, result path, and observer binding are required."], safety.Warnings));
        var payload = new PimaxUsbPortCyclePrivilegedPayload(PimaxUsbPortCycleExperimentSchema.PrivilegedRequestVersion, tokenId, plan.ExperimentKind, signature, plan,
            Path.GetFullPath(request.ObserverStatusPath!), Path.GetFullPath(request.MarkerFilePath!), plan.Observer.SessionId, plan.Observer.ScenarioId,
            plan.Observer.ConnectMarkerId, plan.Observer.ConnectMarkerSequence, plan.Observer.ConnectMarkerType, plan.Observer.ConnectMarkerSource,
            plan.Observer.ConnectMarkerLabel, plan.Observer.ConnectMarkerTimestamp, plan.Observer.MaximumConnectMarkerAgeSeconds, plan.Observer.ConnectAction,
            request.ConfirmationToken!, validation.Nonce!, _now(), validation.ExpiresAt!.Value, _now().AddSeconds(60), Path.GetFullPath(request.PrivilegedResultPath));
        var hash = PimaxUsbPortCycleTargetValidator.PrivilegedRequestFingerprint(payload);
        AtomicWrite(request.PrivilegedRequestPath, new PimaxUsbPortCyclePrivilegedRequest(payload, hash));
        PimaxUsbPortCycleUacLaunch? launch = null;
        if (request.LaunchHelper) launch = PimaxUsbPortCycleUacLauncher.Launch(request.HelperPath, request.PrivilegedRequestPath, hash);
        return Result(tokenId, request.Mode, started, plan, safety, request.ConfirmationToken, validation.ExpiresAt, request.PrivilegedRequestPath, hash, launch: launch);
    }

    private async Task<PimaxUsbPortCycleExperimentResult> ObserveAsync(PimaxUsbPortCycleRequest request, string id, DateTimeOffset started, CancellationToken cancellationToken)
    {
        try
        {
            var signature = Read<PimaxUsbPortCycleTargetSignature>(request.TargetSignaturePath) ?? throw new InvalidDataException("Target signature is missing.");
            var privileged = Read<PimaxUsbPortCyclePrivilegedResult>(request.PrivilegedResultPath);
            var baseline = await _collector.CollectAsync(signature, request.ObserverStatusPath, request.MarkerFilePath, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(request.ObservationSeconds), cancellationToken);
            var final = await _collector.CollectAsync(signature, request.ObserverStatusPath, request.MarkerFilePath, cancellationToken);
            var usb2 = Changed(signature.Usb2Hub, 4, baseline.Topology, final.Topology);
            var ss = Changed(signature.SuperSpeedHub, 4, baseline.Topology, final.Topology);
            var vive = !Changed(signature.Usb2Hub, 2, baseline.Topology, final.Topology) && !Changed(signature.SuperSpeedHub, 2, baseline.Topology, final.Topology);
            var unrelated = PimaxUsbPortCycleTargetValidator.Fingerprint(PimaxUsbPortCycleTargetValidator.Inventory(baseline.Topology, FindHub(baseline.Topology, signature.Usb2Hub), FindHub(baseline.Topology, signature.SuperSpeedHub)))
                == PimaxUsbPortCycleTargetValidator.Fingerprint(PimaxUsbPortCycleTargetValidator.Inventory(final.Topology, FindHub(final.Topology, signature.Usb2Hub), FindHub(final.Topology, signature.SuperSpeedHub)));
            var expected = signature.PimaxUsb2Port.DescendantPnpInstanceIds.Concat(signature.PimaxSuperSpeedPort.DescendantPnpInstanceIds).Distinct(StringComparer.OrdinalIgnoreCase);
            var actual = final.Topology.Ports.Where(port => port.PhysicalConnectorGroupId == signature.PimaxUsb2Port.ConnectorGroupId).SelectMany(port => port.DescendantPnpInstanceIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = expected.Where(value => !actual.Contains(value)).ToArray();
            var outcome = PimaxUsbPortCycleObservationClassifier.Classify(usb2, ss, vive, unrelated, missing.Length, final.Registration.Assessment.State == PimaxRegistrationState.RegisteredReady);
            var observation = new PimaxUsbPortCycleObservation(started, _now(), outcome, usb2, ss, vive, unrelated, missing, final.Registration.Assessment.State, final.Registration.Assessment.Confidence);
            return Result(privileged?.ExperimentId ?? id, request.Mode, started, null, new(true, [], [], []), privilegedResult: privileged, observation: observation);
        }
        catch (Exception ex) { return Result(id, request.Mode, started, null, new(false, [], [ex.Message], []), errors: [ex.ToString()]); }
    }

    private static bool Changed(PimaxUsbPortCycleHubIdentity hub, int index, PimaxUsbPhysicalPortSnapshot first, PimaxUsbPhysicalPortSnapshot second)
    {
        var before = Port(first, hub, index); var after = Port(second, hub, index);
        return before?.ConnectionStatus != after?.ConnectionStatus || before?.DriverKey != after?.DriverKey || PimaxUsbPortCycleTargetValidator.Fingerprint(before?.DescendantPnpInstanceIds ?? []) != PimaxUsbPortCycleTargetValidator.Fingerprint(after?.DescendantPnpInstanceIds ?? []);
    }
    private static PimaxUsbPortRecord? Port(PimaxUsbPhysicalPortSnapshot snapshot, PimaxUsbPortCycleHubIdentity hub, int index) { var found = snapshot.Hubs.SingleOrDefault(value => value.InterfacePath.Equals(hub.InterfacePath, StringComparison.OrdinalIgnoreCase)); return found is null ? null : snapshot.Ports.SingleOrDefault(value => value.HubId == found.HubId && value.ConnectionIndex == index); }
    private static PimaxUsbHubRecord FindHub(PimaxUsbPhysicalPortSnapshot snapshot, PimaxUsbPortCycleHubIdentity identity) => snapshot.Hubs.Single(value => value.InterfacePath.Equals(identity.InterfacePath, StringComparison.OrdinalIgnoreCase));
    private static T? Read<T>(string? path) { if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException($"{typeof(T).Name} path is required."); return JsonSerializer.Deserialize<T>(File.ReadAllText(path), PimaxUsbPortCycleJson.Options); }
    private static void AtomicWrite<T>(string path, T value) { var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temp = full + ".tmp-" + Guid.NewGuid().ToString("N"); File.WriteAllText(temp, JsonSerializer.Serialize(value, PimaxUsbPortCycleJson.Options), new UTF8Encoding(false)); File.Move(temp, full, false); }
    private static string? TokenExperimentId(string? token) { try { var body = token!.Split('.', 2)[0].Replace('-', '+').Replace('_', '/'); var bytes = Convert.FromBase64String(body.PadRight(body.Length + (4 - body.Length % 4) % 4, '=')); using var doc = JsonDocument.Parse(bytes); return doc.RootElement.GetProperty("experimentId").GetString(); } catch { return null; } }
    private PimaxUsbPortCycleExperimentResult Result(string id, string mode, DateTimeOffset started, PimaxUsbPortCyclePlan? plan, PimaxUsbPortCycleSafety safety, string? token = null, DateTimeOffset? expires = null, string? requestPath = null, string? requestHash = null, PimaxUsbPortCyclePrivilegedResult? privilegedResult = null, PimaxUsbPortCycleObservation? observation = null, PimaxUsbPortCycleUacLaunch? launch = null, string[]? warnings = null, string[]? errors = null)
        => new(PimaxUsbPortCycleExperimentSchema.Version, id, mode, started, _now(), plan, safety, token, expires, requestPath, requestHash, privilegedResult, observation, launch, warnings ?? [], errors ?? []);
}

internal static class PimaxUsbPortCycleUacLauncher
{
    internal static bool IsCancellation(int nativeErrorCode) => nativeErrorCode == 1223;
    public static PimaxUsbPortCycleUacLaunch Launch(string? helperPath, string requestPath, string requestHash)
    {
        if (string.IsNullOrWhiteSpace(helperPath) || !File.Exists(helperPath)) return new(true, false, false, null, "Helper executable was not found.");
        try
        {
            var info = new ProcessStartInfo(helperPath) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(helperPath))! };
            info.ArgumentList.Add("pimax-usb-port-cycle-experiment-json"); info.ArgumentList.Add("--mode"); info.ArgumentList.Add(PimaxUsbPortCycleMode.ExecuteElevatedHelper);
            info.ArgumentList.Add("--request-file"); info.ArgumentList.Add(Path.GetFullPath(requestPath)); info.ArgumentList.Add("--request-sha256"); info.ArgumentList.Add(requestHash);
            var process = Process.Start(info); return new(true, process is not null, false, process?.Id, process is null ? "Helper did not start." : null);
        }
        catch (Win32Exception ex) when (IsCancellation(ex.NativeErrorCode)) { return new(true, false, true, null, "UAC was cancelled."); }
        catch (Exception ex) { return new(true, false, false, null, ex.Message); }
    }
}

internal interface IPimaxUsbPortCycleNativeAdapter { PimaxUsbPortCycleNativeResponse CycleUsb2PortOnce(string hubInterfacePath, int connectionIndex); }
internal sealed record PimaxUsbPortCycleNativeResponse(bool ReturnedSuccess, int Win32Error, uint StatusReturned);

internal static class PimaxUsbPortCycleSingleShotSubmitter
{
    public static PimaxUsbPortCycleNativeResponse Submit(IPimaxUsbPortCycleNativeAdapter adapter, string hubInterfacePath, int connectionIndex)
    {
        if (connectionIndex != 4) throw new InvalidOperationException("Only USB 2 connection index 4 is permitted.");
        return adapter.CycleUsb2PortOnce(hubInterfacePath, connectionIndex);
    }
}

internal sealed class WindowsPimaxUsbPortCycleNativeAdapter : IPimaxUsbPortCycleNativeAdapter
{
    private const uint IoctlUsbHubCyclePort = 0x00220444;
    public PimaxUsbPortCycleNativeResponse CycleUsb2PortOnce(string hubInterfacePath, int connectionIndex)
    {
        if (connectionIndex != 4) throw new InvalidOperationException("Only USB 2 connection index 4 is permitted.");
        using var handle = Native.CreateFileW(hubInterfacePath, 0, Native.FileShareRead | Native.FileShareWrite, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the exact USB 2 hub.");
        var buffer = new byte[8]; BitConverter.GetBytes((uint)connectionIndex).CopyTo(buffer, 0);
        var success = Native.DeviceIoControl(handle, IoctlUsbHubCyclePort, buffer, buffer.Length, buffer, buffer.Length, out _, IntPtr.Zero);
        var error = success ? 0 : Marshal.GetLastWin32Error(); var status = BitConverter.ToUInt32(buffer, 4);
        return new(success, error, status);
    }

    private static class Native
    {
        public const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputLength, byte[] output, int outputLength, out int returned, IntPtr overlapped);
    }
}

internal static class PimaxUsbPortCycleElevatedExecutor
{
    internal static IPimaxUsbPortCycleNativeAdapter NativeAdapter { get; set; } = new WindowsPimaxUsbPortCycleNativeAdapter();

    public static async Task<PimaxUsbPortCycleExperimentResult> RunAsExperimentResultAsync(PimaxUsbPortCycleRequest request, DateTimeOffset started, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(request, cancellationToken);
        return new(PimaxUsbPortCycleExperimentSchema.Version, result.ExperimentId, request.Mode, started, DateTimeOffset.UtcNow, null,
            new(result.Success, [], result.Success ? [] : result.Errors, result.Warnings), null, null, request.PrivilegedRequestPath, result.RequestSha256, result, null, null, result.Warnings, result.Errors);
    }

    public static async Task<PimaxUsbPortCyclePrivilegedResult> ExecuteAsync(PimaxUsbPortCycleRequest request, CancellationToken cancellationToken)
    {
        PimaxUsbPortCyclePrivilegedRequest? envelope = null; var errors = new List<string>(); var warnings = new List<string>(); var elevated = IsElevated();
        var experimentId = "unknown"; var exactHub = false; var exactIndex = false; var companion = false; var vive = false; string? preState = null; DateTimeOffset? submitted = null; bool? api = null; int? win32 = null; uint? status = null; var count = 0; var valid = false;
        try
        {
            if (!IsPermittedExecutionContext(Environment.ProcessPath, elevated)) throw new InvalidOperationException("Privileged execution requires the dedicated elevated helper binary.");
            if (string.IsNullOrWhiteSpace(request.PrivilegedRequestPath) || string.IsNullOrWhiteSpace(request.ExpectedRequestSha256)) throw new InvalidDataException("Request file and expected hash are required.");
            envelope = JsonSerializer.Deserialize<PimaxUsbPortCyclePrivilegedRequest>(File.ReadAllText(request.PrivilegedRequestPath), PimaxUsbPortCycleJson.Options) ?? throw new InvalidDataException("Request is empty.");
            experimentId = envelope.Payload.ExperimentId;
            var calculated = PimaxUsbPortCycleTargetValidator.PrivilegedRequestFingerprint(envelope.Payload);
            if (!calculated.Equals(envelope.RequestSha256, StringComparison.OrdinalIgnoreCase) || !calculated.Equals(request.ExpectedRequestSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Privileged request SHA-256 does not match.");
            var helperNow = DateTimeOffset.UtcNow;
            var temporalFailure = ValidateTemporalBoundary(envelope.Payload.TokenExpiresAt, envelope.Payload.ExpiresAt,
                envelope.Payload.ConnectMarkerTimestamp, envelope.Payload.MaximumConnectMarkerAgeSeconds, envelope.Payload.Nonce, helperNow);
            if (temporalFailure is not null) throw new InvalidDataException(temporalFailure);
            var immutableMarker = new PimaxUsbPortCycleStableObserverIdentity(envelope.Payload.ObserverSessionId, envelope.Payload.ObserverScenarioId,
                envelope.Payload.ConnectMarkerId, envelope.Payload.ConnectMarkerSequence, envelope.Payload.ConnectMarkerType, envelope.Payload.ConnectMarkerSource,
                envelope.Payload.ConnectMarkerLabel, envelope.Payload.ConnectMarkerTimestamp, envelope.Payload.MaximumConnectMarkerAgeSeconds, envelope.Payload.ConnectAction);
            var collector = new WindowsPimaxUsbPortCycleStateCollector(SupervisorConfig.Load(null));
            var current = await collector.CollectAsync(envelope.Payload.TargetSignature, envelope.Payload.ObserverStatusPath, envelope.Payload.MarkerFilePath, cancellationToken);
            var validation = PimaxUsbPortCycleTargetValidator.Validate(envelope.Payload.TargetSignature, current, DateTimeOffset.UtcNow);
            if (!validation.Safety.Permitted || validation.Plan?.BindingSha256 != envelope.Payload.Plan.BindingSha256) throw new InvalidOperationException("Immediate target revalidation failed: " + string.Join("; ", validation.Safety.RefusalReasons));
            if (PimaxUsbPortCycleTargetValidator.Fingerprint(PimaxUsbPortCycleTargetValidator.StableObserver(validation.Plan.Observer))
                != PimaxUsbPortCycleTargetValidator.Fingerprint(immutableMarker)) throw new InvalidOperationException("Immutable Connect marker identity changed.");
            var token = PimaxUsbPortCycleConfirmationToken.Validate(envelope.Payload.ConfirmationToken, envelope.Payload.ExperimentId, validation.Plan, DateTimeOffset.UtcNow, null, consume: false);
            if (!token.Accepted || token.Nonce != envelope.Payload.Nonce) throw new InvalidOperationException("Signed confirmation token revalidation failed: " + token.Reason);
            exactHub = true; exactIndex = envelope.Payload.Plan.ConnectionIndex == 4; companion = true; vive = true; valid = true;
            var targetHub = current.Topology.Hubs.Single(hub => hub.InterfacePath.Equals(envelope.Payload.TargetSignature.Usb2Hub.InterfacePath, StringComparison.OrdinalIgnoreCase));
            var port = current.Topology.Ports.Single(value => value.HubId == targetHub.HubId && value.ConnectionIndex == 4); preState = port.ConnectionStatus;
            submitted = DateTimeOffset.UtcNow; count = 1;
            var response = PimaxUsbPortCycleSingleShotSubmitter.Submit(NativeAdapter, targetHub.InterfacePath, 4); api = response.ReturnedSuccess; win32 = response.Win32Error; status = response.StatusReturned;
            if (!response.ReturnedSuccess) errors.Add($"Cycle request failed with Win32 error {response.Win32Error}.");
        }
        catch (Exception ex) { errors.Add(ex.Message); }
        var output = new PimaxUsbPortCyclePrivilegedResult(PimaxUsbPortCycleExperimentSchema.PrivilegedResultVersion, experimentId, Environment.ProcessId, elevated,
            envelope?.RequestSha256, valid, exactHub, exactIndex, companion, vive, preState, submitted, api, win32, status, count, DateTimeOffset.UtcNow,
            valid && count == 1 && api == true, errors.Count == 0 ? "none" : valid ? "ioctlRejected" : "requestValidationFailed", warnings.ToArray(), errors.ToArray());
        if (envelope is not null) AtomicWriteResult(envelope.Payload.OutputResultPath, output);
        return output;
    }

    internal static bool IsPermittedExecutionContext(string? processPath, bool elevated)
        => elevated && string.Equals(Path.GetFileNameWithoutExtension(processPath), "PimaxVrcSupervisor.PortCycleHelper", StringComparison.OrdinalIgnoreCase);
    internal static string? ValidateTemporalBoundary(DateTimeOffset tokenExpiresAt, DateTimeOffset requestExpiresAt,
        DateTimeOffset markerTimestamp, double maximumMarkerAgeSeconds, string? nonce, DateTimeOffset now)
    {
        if (tokenExpiresAt <= now) return "Confirmation token expired.";
        if (requestExpiresAt <= now) return "Privileged request expired.";
        if (string.IsNullOrWhiteSpace(nonce)) return "Privileged request has no nonce.";
        var markerAge = (now.ToUniversalTime() - markerTimestamp.ToUniversalTime()).TotalSeconds;
        if (markerAge < 0 || markerAge > maximumMarkerAgeSeconds || maximumMarkerAgeSeconds != PimaxUsbPortCycleObserverReader.MaximumMarkerAgeSeconds)
            return "Connect scan marker is stale or has an invalid freshness policy.";
        return null;
    }
    private static bool IsElevated() { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    internal static void AtomicWriteResult(string path, PimaxUsbPortCyclePrivilegedResult value) { var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temp = full + ".tmp-" + Guid.NewGuid().ToString("N"); File.WriteAllText(temp, JsonSerializer.Serialize(value, PimaxUsbPortCycleJson.Options), new UTF8Encoding(false)); File.Move(temp, full, false); }
}
