using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

internal static class PimaxUsbPhysicalPortMapSchema
{
    public const string Version = "pimax-usb-physical-port-map-v1";
}

internal static class PimaxUsbPhysicalPortMapJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal sealed record PimaxUsbPhysicalPortMapRequest(
    int DurationSeconds,
    int SampleIntervalMilliseconds,
    int AssessmentIntervalMilliseconds,
    string ScenarioId,
    string? OutputDirectory,
    string? MarkerFile)
{
    public bool ObservationMode => DurationSeconds > 0;

    public static PimaxUsbPhysicalPortMapRequest Parse(string[] args)
        => new(
            BoundedInt(Option(args, "--duration-seconds"), 0, 0, 1800),
            BoundedInt(Option(args, "--sample-interval-ms"), 500, 250, 5000),
            BoundedInt(Option(args, "--assessment-interval-ms"), 2000, 1000, 10000),
            Option(args, "--scenario") ?? "snapshot",
            Option(args, "--output-dir"),
            Option(args, "--marker-file"));

    private static int BoundedInt(string? value, int fallback, int minimum, int maximum)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static string? Option(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return args[index][prefix.Length..];
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
                && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) return args[index + 1];
        }
        return null;
    }
}

internal sealed record PimaxUsbHubRecord(
    string HubId,
    string InterfacePath,
    string SanitizedInterfaceIdentity,
    string? PnpInstanceId,
    string? ParentPnpInstanceId,
    string? ContainerId,
    string? DriverKey,
    string[] HardwareIds,
    string[] CompatibleIds,
    string? Vid,
    string? Pid,
    string? DeviceClass,
    string? ClassGuid,
    string? Manufacturer,
    string? Product,
    string? Serial,
    string? LocationInformation,
    string[] LocationPaths,
    string[] UsbProtocolSupport,
    string HubType,
    bool IsRootHub,
    bool? IsExternalHub,
    int DownstreamPortCount,
    DateTimeOffset ProcessTimestamp,
    string[] Warnings,
    string[] AccessFailures);

internal sealed record PimaxUsbCompanionReference(
    int CompanionIndex,
    int CompanionPortNumber,
    string CompanionHubSymbolicLink,
    bool Reciprocal);

internal sealed record PimaxUsbPortRecord(
    string PortId,
    string HubId,
    string HubInterfacePath,
    int ConnectionIndex,
    string ConnectionStatus,
    string? NegotiatedSpeed,
    string[] UsbProtocols,
    bool DeviceConnected,
    bool DeviceIsHub,
    ushort VendorId,
    ushort ProductId,
    ushort DeviceRevision,
    byte DeviceClass,
    byte DeviceSubClass,
    byte DeviceProtocol,
    string? DriverKey,
    string? ChildPnpInstanceId,
    string? ChildContainerId,
    string[] ChildLocationPaths,
    string[] ChildHardwareIds,
    string[] ChildCompatibleIds,
    string? DownstreamHubIdentity,
    string PortConnectorType,
    bool? DebugCapable,
    PimaxUsbCompanionReference[] Companions,
    string? PhysicalConnectorGroupId,
    string OccupantClassification,
    string[] DescendantPnpInstanceIds,
    DateTimeOffset FirstSeenTimestamp,
    DateTimeOffset LastSeenTimestamp,
    string[] Warnings,
    string[] AccessFailures);

internal sealed record PimaxUsbPhysicalConnectorGroup(
    string GroupId,
    string[] PortIds,
    string[] SupportingEvidence,
    string[] ContraryEvidence,
    string Confidence,
    bool ApiConfirmed,
    bool ObservationInferred,
    string OccupantClassification,
    string[] DescendantPnpInstanceIds,
    bool ContainsUnrelatedOccupant);

internal sealed record PimaxUsbHubCandidate(
    string HubId,
    int Score,
    string[] SupportingEvidence,
    string[] ContraryEvidence,
    string Confidence);

internal sealed record PimaxUsbPortTransition(
    double ElapsedMs,
    DateTimeOffset ObservedAt,
    string PortId,
    string PreviousStatus,
    string CurrentStatus,
    string? PreviousDriverKey,
    string? CurrentDriverKey,
    string TransitionType);

internal sealed record PimaxUsbPhysicalPortMarker(
    string SessionId,
    string ScenarioId,
    DateTimeOffset UtcTimestamp,
    DateTimeOffset LocalTimestamp,
    double ElapsedMs,
    string Label,
    string Phase,
    string Source,
    string? Note);

internal sealed record PimaxUsbPhysicalPortSnapshot(
    DateTimeOffset CollectedAt,
    PimaxUsbHubRecord[] Hubs,
    PimaxUsbPortRecord[] Ports,
    PimaxUsbPhysicalConnectorGroup[] ConnectorGroups,
    PimaxUsbHubCandidate[] HubCandidates,
    string? SelectedExternalHubId,
    string? PimaxConnectorGroupId,
    string? ViveConnectorGroupId,
    PimaxUsbEnumerationSnapshot PnpSnapshot,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxUsbPhysicalPortMapResult(
    string Schema,
    string SessionId,
    string ScenarioId,
    string Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationMs,
    int SampleIntervalMilliseconds,
    string? SourceCommit,
    string BinaryPath,
    string BinarySha256,
    string? OutputDirectory,
    PimaxUsbPhysicalPortSnapshot Baseline,
    PimaxUsbPhysicalPortSnapshot FinalSnapshot,
    PimaxUsbPortTransition[] PortTimeline,
    PimaxUsbPhysicalPortMarker[] Markers,
    PimaxAssessmentSample[] RegistrationTimeline,
    PimaxServiceTransition[] ServiceTimeline,
    PimaxProcessTransition[] ProcessTimeline,
    PimaxWindowsEventReference[] WindowsEvents,
    PimaxLogTailReference[] PimaxLogReferences,
    string[] Warnings,
    string[] Errors,
    bool Cancelled);

internal sealed record PimaxUsbHubInterface(string InterfacePath, string? PnpInstanceId);
internal sealed record PimaxUsbHubProbeResult(string HubType, int PortCount, string[] Protocols, PimaxUsbRawPortProbe[] Ports, string[] Warnings, string[] Errors);
internal sealed record PimaxUsbRawPortProbe(
    int ConnectionIndex,
    string ConnectionStatus,
    string? Speed,
    string[] Protocols,
    bool Connected,
    bool DeviceIsHub,
    ushort VendorId,
    ushort ProductId,
    ushort Revision,
    byte DeviceClass,
    byte DeviceSubClass,
    byte DeviceProtocol,
    string? DriverKey,
    string? DownstreamHubName,
    bool? UserConnectable,
    bool? DebugCapable,
    bool? TypeC,
    PimaxUsbCompanionReference[] Companions,
    string[] Warnings,
    string[] Errors);

internal interface IPimaxUsbHubQuerySource
{
    PimaxUsbHubInterface[] EnumerateHubInterfaces();
    PimaxUsbHubProbeResult QueryHub(PimaxUsbHubInterface hub);
}

internal sealed class PimaxUsbPhysicalPortSnapshotCollector
{
    private readonly IPimaxUsbHubQuerySource _hubSource;
    private readonly IPimaxUsbDeviceInventorySource _pnpSource;
    private readonly Func<DateTimeOffset> _now;

    public PimaxUsbPhysicalPortSnapshotCollector()
        : this(new WindowsUsbHubQuerySource(), new WindowsPnpDeviceInventorySource(), () => DateTimeOffset.Now) { }

    internal PimaxUsbPhysicalPortSnapshotCollector(IPimaxUsbHubQuerySource hubSource, IPimaxUsbDeviceInventorySource pnpSource, Func<DateTimeOffset>? now = null)
    {
        _hubSource = hubSource;
        _pnpSource = pnpSource;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public PimaxUsbPhysicalPortSnapshot Collect()
    {
        var collectedAt = _now();
        var warnings = new List<string>();
        var errors = new List<string>();
        PimaxUsbInventoryResult rawPnp;
        try { rawPnp = _pnpSource.Collect(); }
        catch (Exception ex) { rawPnp = new([], [], [ex.Message]); }
        warnings.AddRange(rawPnp.Warnings);
        errors.AddRange(rawPnp.Errors);

        var pnpSnapshot = new PimaxUsbEnumerationSnapshotCollector(new FixedPnpSource(rawPnp)).Collect();
        var byInstance = rawPnp.Devices.ToDictionary(device => device.InstanceId, StringComparer.OrdinalIgnoreCase);
        var byDriver = rawPnp.Devices.Where(device => !string.IsNullOrWhiteSpace(device.DriverKey)).GroupBy(device => device.DriverKey!, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var children = rawPnp.Devices.Where(device => !string.IsNullOrWhiteSpace(device.ParentInstanceId)).GroupBy(device => device.ParentInstanceId!, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        PimaxUsbHubInterface[] interfaces;
        try { interfaces = _hubSource.EnumerateHubInterfaces(); }
        catch (Exception ex) { interfaces = []; errors.Add($"USB hub interface enumeration failed: {ex.Message}"); }

        var hubs = new List<PimaxUsbHubRecord>();
        var ports = new List<PimaxUsbPortRecord>();
        foreach (var item in interfaces)
        {
            PimaxUsbHubProbeResult probe;
            try { probe = _hubSource.QueryHub(item); }
            catch (Exception ex) { probe = new("unknown", 0, [], [], [], [ex.Message]); }
            byInstance.TryGetValue(item.PnpInstanceId ?? "", out var hubPnp);
            var hubId = PnpIdentitySanitizer.StableHash(item.InterfacePath);
            var root = probe.HubType.Equals("root", StringComparison.OrdinalIgnoreCase);
            hubs.Add(new PimaxUsbHubRecord(
                hubId, item.InterfacePath, PnpIdentitySanitizer.StableHash(item.InterfacePath), item.PnpInstanceId,
                hubPnp?.ParentInstanceId, hubPnp?.ContainerId, hubPnp?.DriverKey, hubPnp?.HardwareIds ?? [], hubPnp?.CompatibleIds ?? [],
                hubPnp?.Vid, hubPnp?.Pid, hubPnp?.DeviceClass, hubPnp?.ClassGuid, hubPnp?.Manufacturer, hubPnp?.DeviceDescription,
                SerialFromInstance(item.PnpInstanceId), hubPnp?.LocationInformation, hubPnp?.LocationPaths ?? [], probe.Protocols, probe.HubType,
                root, root ? false : true, probe.PortCount, collectedAt, probe.Warnings, probe.Errors));

            foreach (var port in probe.Ports)
            {
                byDriver.TryGetValue(port.DriverKey ?? "", out var child);
                var descendants = child is null ? [] : Descendants(child, children).ToArray();
                var classification = PimaxUsbOccupantClassifier.Classify(descendants);
                ports.Add(new PimaxUsbPortRecord(
                    PortId(hubId, port.ConnectionIndex), hubId, item.InterfacePath, port.ConnectionIndex, port.ConnectionStatus, port.Speed,
                    port.Protocols, port.Connected, port.DeviceIsHub, port.VendorId, port.ProductId, port.Revision, port.DeviceClass,
                    port.DeviceSubClass, port.DeviceProtocol, port.DriverKey, child?.InstanceId, child?.ContainerId, child?.LocationPaths ?? [],
                    child?.HardwareIds ?? [], child?.CompatibleIds ?? [], port.DownstreamHubName,
                    port.TypeC == true ? "usbTypeC" : port.UserConnectable == true ? "userConnectable" : "unknown",
                    port.DebugCapable, port.Companions, null, classification,
                    descendants.Select(device => device.InstanceId).ToArray(), collectedAt, collectedAt, port.Warnings, port.Errors));
            }
        }

        var portArray = ports.ToArray();
        portArray = portArray.Select(port => port with
        {
            Companions = port.Companions.Select(companion => companion with
            {
                Reciprocal = portArray.Any(other => other.ConnectionIndex == companion.CompanionPortNumber
                    && PimaxUsbConnectorGrouper.SameHub(other.HubInterfacePath, companion.CompanionHubSymbolicLink)
                    && other.Companions.Any(back => back.CompanionPortNumber == port.ConnectionIndex
                        && PimaxUsbConnectorGrouper.SameHub(back.CompanionHubSymbolicLink, port.HubInterfacePath)))
            }).ToArray()
        }).ToArray();
        var grouped = PimaxUsbConnectorGrouper.Group(portArray);
        var groupByPort = grouped.SelectMany(group => group.PortIds.Select(portId => (portId, group.GroupId))).ToDictionary(item => item.portId, item => item.GroupId, StringComparer.OrdinalIgnoreCase);
        var mappedPorts = portArray.Select(port => port with { PhysicalConnectorGroupId = groupByPort.GetValueOrDefault(port.PortId) }).ToArray();
        var candidates = PimaxUsbHubCandidateSelector.Rank(hubs.ToArray(), mappedPorts, grouped);
        var selectedHub = SelectHubCandidate(candidates, hubs.ToArray(), mappedPorts, grouped);
        var selectedGroups = selectedHub is null ? [] : grouped.Where(group => group.PortIds.Any(id => mappedPorts.Any(port => port.PortId == id && port.HubId == selectedHub))).ToArray();
        var pimax = UniqueGroup(selectedGroups, "pimax-related", warnings, "Pimax");
        var vive = UniqueGroup(selectedGroups, "vive-face-tracker-related", warnings, "Vive face tracker");
        if (pimax is not null && string.Equals(pimax, vive, StringComparison.OrdinalIgnoreCase)) errors.Add("Pimax and Vive face tracker resolve to the same connector group.");
        if (selectedHub is null) warnings.Add("No unique external seven-port hub candidate met the confidence threshold.");

        return new PimaxUsbPhysicalPortSnapshot(collectedAt, hubs.ToArray(), mappedPorts, grouped, candidates, selectedHub, pimax, vive, pnpSnapshot,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static string PortId(string hubId, int index) => $"{hubId}:port:{index}";

    private static string? UniqueGroup(IEnumerable<PimaxUsbPhysicalConnectorGroup> groups, string classification, List<string> warnings, string label)
    {
        var matches = groups.Where(group => group.OccupantClassification == classification).ToArray();
        if (matches.Length == 1) return matches[0].GroupId;
        if (matches.Length > 1) warnings.Add($"{label} appears on multiple connector groups.");
        return null;
    }

    private static string? SelectHubCandidate(PimaxUsbHubCandidate[] candidates, PimaxUsbHubRecord[] hubs, PimaxUsbPortRecord[] ports, PimaxUsbPhysicalConnectorGroup[] groups)
    {
        if (candidates.Length == 0 || candidates[0].Score < 5) return null;
        var top = candidates.Where(candidate => candidate.Score == candidates[0].Score).ToArray();
        if (top.Length == 1) return top[0].HubId;
        var topIds = top.Select(candidate => candidate.HubId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pairedTop = groups.Any(group => group.ApiConfirmed && group.PortIds.Select(id => ports.First(port => port.PortId == id).HubId).Where(topIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (!pairedTop) return null;
        return hubs.Where(hub => topIds.Contains(hub.HubId)).OrderBy(hub => hub.HubType == "usb20" ? 0 : 1).ThenBy(hub => hub.HubId, StringComparer.OrdinalIgnoreCase).First().HubId;
    }

    private static IEnumerable<PimaxUsbRawDeviceRecord> Descendants(PimaxUsbRawDeviceRecord root, IReadOnlyDictionary<string, PimaxUsbRawDeviceRecord[]> children)
    {
        var queue = new Queue<PimaxUsbRawDeviceRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(root);
        while (queue.TryDequeue(out var current) && seen.Count < 2048)
        {
            if (!seen.Add(current.InstanceId)) continue;
            yield return current;
            foreach (var child in children.GetValueOrDefault(current.InstanceId, [])) queue.Enqueue(child);
        }
    }

    private static string? SerialFromInstance(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return null;
        var parts = instanceId.Split('\\');
        return parts.Length >= 3 && !parts[^1].Contains('&') ? parts[^1] : null;
    }

    private sealed class FixedPnpSource(PimaxUsbInventoryResult result) : IPimaxUsbDeviceInventorySource
    {
        public PimaxUsbInventoryResult Collect() => result;
    }
}

internal static class PimaxUsbOccupantClassifier
{
    public static string Classify(IReadOnlyCollection<PimaxUsbRawDeviceRecord> devices)
    {
        if (devices.Count == 0) return "empty-or-unresolved";
        var text = string.Join("\n", devices.SelectMany(device => new[] { device.InstanceId, device.FriendlyName, device.DeviceDescription, device.Manufacturer }.Concat(device.HardwareIds).Concat(device.CompatibleIds)).Where(value => value is not null));
        var pimax = text.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Crystal", StringComparison.OrdinalIgnoreCase)
            || text.Contains("VID_34A4&PID_0012", StringComparison.OrdinalIgnoreCase)
            || text.Contains("VID_05E3&PID_0608", StringComparison.OrdinalIgnoreCase)
            || text.Contains("VID_2104&PID_0220", StringComparison.OrdinalIgnoreCase);
        var vive = (text.Contains("Vive", StringComparison.OrdinalIgnoreCase) || text.Contains("HTC", StringComparison.OrdinalIgnoreCase))
            && (text.Contains("face", StringComparison.OrdinalIgnoreCase) || text.Contains("tracker", StringComparison.OrdinalIgnoreCase));
        if (pimax && vive) return "mixed-pimax-and-vive";
        if (pimax) return "pimax-related";
        if (vive) return "vive-face-tracker-related";
        return "unrelated-or-unresolved";
    }
}

internal static class PimaxUsbConnectorGrouper
{
    public static PimaxUsbPhysicalConnectorGroup[] Group(PimaxUsbPortRecord[] ports)
    {
        var byKey = ports.ToDictionary(port => Key(port.HubInterfacePath, port.ConnectionIndex), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<PimaxUsbPhysicalConnectorGroup>();
        foreach (var port in ports.OrderBy(item => item.HubId).ThenBy(item => item.ConnectionIndex))
        {
            if (!used.Add(port.PortId)) continue;
            var members = new List<PimaxUsbPortRecord> { port };
            var supporting = new List<string>();
            var contrary = new List<string>();
            var apiConfirmed = false;
            foreach (var companion in port.Companions)
            {
                if (!byKey.TryGetValue(Key(companion.CompanionHubSymbolicLink, companion.CompanionPortNumber), out var other))
                {
                    contrary.Add("Companion metadata did not resolve to an enumerated hub port.");
                    continue;
                }
                var reciprocal = other.Companions.Any(reference => reference.CompanionPortNumber == port.ConnectionIndex && SameHub(reference.CompanionHubSymbolicLink, port.HubInterfacePath));
                if (reciprocal)
                {
                    supporting.Add("Reciprocal companion-port and companion-hub metadata.");
                    apiConfirmed = true;
                }
                else
                {
                    supporting.Add("One-sided companion-port metadata.");
                    contrary.Add("Companion relationship is not reciprocal.");
                }
                if (used.Add(other.PortId)) members.Add(other);
            }
            var occupantKinds = members.Select(member => member.OccupantClassification).Where(value => value != "empty-or-unresolved").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var occupant = occupantKinds.Length switch { 0 => "empty-or-unresolved", 1 => occupantKinds[0], _ => "mixed-or-conflicting" };
            var idSeed = string.Join("|", members.Select(member => member.PortId).OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            groups.Add(new PimaxUsbPhysicalConnectorGroup(
                "connector:" + PnpIdentitySanitizer.StableHash(idSeed)[7..23], members.Select(member => member.PortId).ToArray(),
                supporting.ToArray(), contrary.ToArray(), apiConfirmed ? "high" : members.Count > 1 ? "medium" : "low", apiConfirmed, false,
                occupant, members.SelectMany(member => member.DescendantPnpInstanceIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                occupant is "mixed-or-conflicting" or "mixed-pimax-and-vive"));
        }
        return groups.ToArray();
    }

    private static string Key(string hub, int port) => NormalizeHub(hub) + "|" + port.ToString(CultureInfo.InvariantCulture);
    internal static bool SameHub(string left, string right) => string.Equals(NormalizeHub(left), NormalizeHub(right), StringComparison.OrdinalIgnoreCase);
    internal static string NormalizeHub(string value)
    {
        var normalized = value.Trim().TrimEnd('\\').Replace("\\??\\", "\\\\?\\", StringComparison.OrdinalIgnoreCase);
        return normalized.StartsWith("\\\\?\\", StringComparison.Ordinal) ? normalized : "\\\\?\\" + normalized;
    }

    internal static bool SupportsObservationInference(PimaxUsbPortRecord first, PimaxUsbPortRecord second, IReadOnlyCollection<PimaxUsbPortTransition> transitions, bool ancestryConsistent, double maximumDeltaMs = 1500)
    {
        if (!ancestryConsistent || first.PortId == second.PortId || first.HubId == second.HubId) return false;
        var firstEvents = transitions.Where(item => item.PortId == first.PortId).OrderBy(item => item.ElapsedMs).ToArray();
        var secondEvents = transitions.Where(item => item.PortId == second.PortId).OrderBy(item => item.ElapsedMs).ToArray();
        if (firstEvents.Length < 2 || secondEvents.Length < 2) return false;
        return firstEvents.Zip(secondEvents).All(pair => pair.First.TransitionType == pair.Second.TransitionType
            && Math.Abs(pair.First.ElapsedMs - pair.Second.ElapsedMs) <= maximumDeltaMs);
    }
}

internal static class PimaxUsbPhysicalPortProtocol
{
    public static string[] Validate(IReadOnlyCollection<PimaxUsbPhysicalPortMarker> markers)
    {
        var ordered = markers.OrderBy(marker => marker.ElapsedMs).ToArray();
        var issues = new List<string>();
        var observer = Array.FindIndex(ordered, marker => marker.Label == "observer-start");
        var ready = Array.FindIndex(ordered, marker => marker.Phase == "readiness");
        var action = Array.FindIndex(ordered, marker => marker.Phase == "action");
        var result = Array.FindIndex(ordered, marker => marker.Phase == "result");
        if (observer < 0) issues.Add("Observer-start marker is missing.");
        if (ready >= 0 && (observer < 0 || ready <= observer)) issues.Add("Readiness does not follow observer start.");
        if (action >= 0 && (ready < 0 || action <= ready)) issues.Add("Action does not follow readiness.");
        if (result >= 0 && (action < 0 || result <= action)) issues.Add("Result does not follow action.");
        return issues.ToArray();
    }
}

internal static class PimaxUsbHubCandidateSelector
{
    public static PimaxUsbHubCandidate[] Rank(PimaxUsbHubRecord[] hubs, PimaxUsbPortRecord[] ports, PimaxUsbPhysicalConnectorGroup[] groups)
        => hubs.Select(hub =>
        {
            var support = new List<string>();
            var contrary = new List<string>();
            var score = 0;
            var knownPimaxInternalHub = (string.Equals(hub.Vid, "0424", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(hub.Pid, "2137", StringComparison.OrdinalIgnoreCase) || string.Equals(hub.Pid, "5537", StringComparison.OrdinalIgnoreCase)))
                || (string.Equals(hub.Vid, "05E3", StringComparison.OrdinalIgnoreCase) && string.Equals(hub.Pid, "0608", StringComparison.OrdinalIgnoreCase));
            if (knownPimaxInternalHub)
            {
                score -= 4;
                contrary.Add("Known Pimax-internal hub identity cannot represent the external enclosure boundary.");
            }
            if (hub.IsExternalHub == true && !hub.IsRootHub) { score += 2; support.Add("Non-root USB hub interface."); }
            else contrary.Add("Hub is root or external status is unresolved.");
            var hubGroups = groups.Where(group => group.PortIds.Any(id => ports.Any(port => port.PortId == id && port.HubId == hub.HubId))).ToArray();
            if (hubGroups.Length == 7 || hub.DownstreamPortCount == 7) { score += 1; support.Add("Seven downstream connectors or logical ports."); }
            if (ports.Any(port => port.HubId == hub.HubId && port.DeviceIsHub
                    && hubs.Any(child => string.Equals(child.PnpInstanceId, port.ChildPnpInstanceId, StringComparison.OrdinalIgnoreCase) && child.DownstreamPortCount == 4)))
            {
                score += 1;
                support.Add("Cascaded four-port tier is consistent with a seven-connector enclosure.");
            }
            if (ports.Any(port => port.HubId == hub.HubId && port.Companions.Any()))
            {
                score += 1;
                support.Add("USB 2/SuperSpeed companion hub is API-linked.");
            }
            var containsPimax = hubGroups.Any(group => group.OccupantClassification == "pimax-related");
            var containsVive = hubGroups.Any(group => group.OccupantClassification == "vive-face-tracker-related");
            if (containsPimax) { score += 2; support.Add("Contains Pimax-related descendants."); }
            if (containsVive) { score += 2; support.Add("Contains Vive face-tracker descendants."); }
            if (!containsPimax || !containsVive) contrary.Add("Does not contain both independently identified Pimax and Vive connector groups.");
            if (hub.LocationPaths.Length > 0) { score += 1; support.Add("Has a non-root physical location path."); }
            if (hub.DownstreamPortCount == 7 && support.Count == 1) contrary.Add("Seven-port count alone is insufficient.");
            return new PimaxUsbHubCandidate(hub.HubId, score, support.ToArray(), contrary.ToArray(), score >= 7 ? "high" : score >= 5 ? "medium" : "low");
        }).OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.HubId, StringComparer.OrdinalIgnoreCase).ToArray();
}

internal sealed class PimaxUsbPhysicalPortMapper
{
    private const int MaxTransitions = 4000;
    private const int MaxMarkers = 1000;
    private const int MaxAssessments = 900;
    private readonly PimaxUsbPhysicalPortSnapshotCollector _collector;
    private readonly IPimaxLifecycleObservationProbe? _lifecycle;
    private readonly Func<DateTimeOffset> _now;

    public PimaxUsbPhysicalPortMapper(SupervisorConfig config)
        : this(new PimaxUsbPhysicalPortSnapshotCollector(), new WindowsPimaxLifecycleObservationProbe(config), () => DateTimeOffset.Now) { }

    internal PimaxUsbPhysicalPortMapper(PimaxUsbPhysicalPortSnapshotCollector collector, IPimaxLifecycleObservationProbe? lifecycle = null, Func<DateTimeOffset>? now = null)
    {
        _collector = collector;
        _lifecycle = lifecycle;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<PimaxUsbPhysicalPortMapResult> RunAsync(PimaxUsbPhysicalPortMapRequest request, CancellationToken cancellationToken)
    {
        var sessionId = $"pimax-usb-port-map-{Guid.NewGuid():N}";
        var startedAt = _now();
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();
        var errors = new List<string>();
        var timeline = new List<PimaxUsbPortTransition>();
        var markers = new List<PimaxUsbPhysicalPortMarker>();
        var assessments = new List<PimaxAssessmentSample>();
        var services = new List<PimaxServiceTransition>();
        var processes = new List<PimaxProcessTransition>();
        var priorServices = new Dictionary<string, PimaxServiceSnapshot>(StringComparer.OrdinalIgnoreCase);
        var priorProcesses = new Dictionary<(int Pid, long StartTicks), PimaxProcessSnapshot>();
        var markerReader = new PimaxObservationMarkerReader(request.MarkerFile);
        var outputDirectory = PrepareOutputDirectory(request.OutputDirectory, warnings);
        var baseline = SafeCollect(errors);
        var latest = baseline;
        var priorPorts = baseline.Ports.ToDictionary(port => port.PortId, StringComparer.OrdinalIgnoreCase);
        markers.Add(new(sessionId, request.ScenarioId, startedAt.ToUniversalTime(), startedAt, stopwatch.Elapsed.TotalMilliseconds, "observer-start", "observer", "observer-generated", null));
        WriteDetail(outputDirectory, "baseline.json", baseline, errors);
        WriteStatus(outputDirectory, sessionId, request.ScenarioId, startedAt, stopwatch.Elapsed, "running", errors);

        if (request.ObservationMode)
        {
            using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            duration.CancelAfter(TimeSpan.FromSeconds(request.DurationSeconds));
            var nextAssessment = TimeSpan.Zero;
            while (!duration.IsCancellationRequested)
            {
                latest = SafeCollect(errors);
                TrackPorts(priorPorts, latest.Ports, timeline, _now(), stopwatch.Elapsed);
                foreach (var marker in markerReader.ReadNew(stopwatch.Elapsed, _now(), warnings).Take(MaxMarkers - markers.Count))
                {
                    markers.Add(WrapMarker(marker, sessionId, request.ScenarioId));
                    WriteDetail(outputDirectory, $"marker-{markers.Count:D3}-{SafeName(marker.Label)}.json", latest, errors);
                }
                if (_lifecycle is not null)
                {
                    try { PimaxConnectLifecycleObserver.TrackServices(await _lifecycle.CaptureServicesAsync(duration.Token), priorServices, services, _now(), stopwatch.Elapsed); }
                    catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
                    catch (Exception ex) { warnings.Add($"Service observation failed: {ex.Message}"); }
                    try { PimaxConnectLifecycleObserver.TrackProcesses(_lifecycle.CaptureProcesses(), priorProcesses, processes, _now(), stopwatch.Elapsed); }
                    catch (Exception ex) { warnings.Add($"Process observation failed: {ex.Message}"); }
                    if (stopwatch.Elapsed >= nextAssessment && assessments.Count < MaxAssessments)
                    {
                        try
                        {
                            var connectivity = await _lifecycle.CaptureConnectivityAsync(duration.Token);
                            var assessment = new PimaxRegistrationStateAssessor().Evaluate(connectivity, latest.PnpSnapshot, Math.Abs((latest.CollectedAt - connectivity.CollectedAt).TotalMilliseconds));
                            assessments.Add(new(stopwatch.Elapsed.TotalMilliseconds, _now(), assessment.State, assessment.Confidence, assessment.Explanation, connectivity.Assessment.Value, assessment.Warnings.Length, connectivity.Errors.Length + latest.Errors.Length));
                        }
                        catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
                        catch (Exception ex) { warnings.Add($"Registration observation failed: {ex.Message}"); }
                        nextAssessment = stopwatch.Elapsed + TimeSpan.FromMilliseconds(request.AssessmentIntervalMilliseconds);
                    }
                }
                WriteStatus(outputDirectory, sessionId, request.ScenarioId, startedAt, stopwatch.Elapsed, "running", errors);
                try { await Task.Delay(request.SampleIntervalMilliseconds, duration.Token); }
                catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
            }
            foreach (var marker in markerReader.ReadNew(stopwatch.Elapsed, _now(), warnings).Take(MaxMarkers - markers.Count)) markers.Add(WrapMarker(marker, sessionId, request.ScenarioId));
        }

        latest = SafeCollect(errors);
        TrackPorts(priorPorts, latest.Ports, timeline, _now(), stopwatch.Elapsed);
        var endedAt = _now();
        markers.Add(new(sessionId, request.ScenarioId, endedAt.ToUniversalTime(), endedAt, stopwatch.Elapsed.TotalMilliseconds, "observer-ended", "observer", "observer-generated", cancellationToken.IsCancellationRequested ? "cancelled" : "duration-complete"));
        PimaxWindowsEventReference[] events = [];
        PimaxLogTailReference[] logs = [];
        if (_lifecycle is not null)
        {
            try { events = await _lifecycle.CaptureEventsAsync(startedAt, endedAt, CancellationToken.None); }
            catch (Exception ex) { warnings.Add($"Windows event collection failed: {ex.Message}"); }
            try { logs = _lifecycle.CaptureLogTails(); }
            catch (Exception ex) { warnings.Add($"Pimax log reference collection failed: {ex.Message}"); }
        }
        WriteDetail(outputDirectory, "final.json", latest, errors);
        WriteStatus(outputDirectory, sessionId, request.ScenarioId, startedAt, stopwatch.Elapsed, "complete", errors);
        var binary = Assembly.GetExecutingAssembly().Location;
        return new(PimaxUsbPhysicalPortMapSchema.Version, sessionId, request.ScenarioId, request.ObservationMode ? "observation" : "snapshot",
            startedAt, endedAt, stopwatch.Elapsed.TotalMilliseconds, request.SampleIntervalMilliseconds, ReadSourceCommit(), binary, HashFile(binary), outputDirectory,
            baseline, latest, timeline.Take(MaxTransitions).ToArray(), markers.ToArray(), assessments.ToArray(), services.Take(MaxTransitions).ToArray(),
            processes.Take(MaxTransitions).ToArray(), events, logs, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), cancellationToken.IsCancellationRequested);
    }

    internal static void TrackPorts(Dictionary<string, PimaxUsbPortRecord> previous, IEnumerable<PimaxUsbPortRecord> current, List<PimaxUsbPortTransition> timeline, DateTimeOffset observedAt, TimeSpan elapsed)
    {
        var now = current.ToDictionary(port => port.PortId, StringComparer.OrdinalIgnoreCase);
        foreach (var item in now)
        {
            if (!previous.TryGetValue(item.Key, out var prior))
            {
                timeline.Add(new(elapsed.TotalMilliseconds, observedAt, item.Key, "not-enumerated", item.Value.ConnectionStatus, null, item.Value.DriverKey, "portAppeared"));
            }
            else if (prior.ConnectionStatus != item.Value.ConnectionStatus || !string.Equals(prior.DriverKey, item.Value.DriverKey, StringComparison.OrdinalIgnoreCase))
            {
                timeline.Add(new(elapsed.TotalMilliseconds, observedAt, item.Key, prior.ConnectionStatus, item.Value.ConnectionStatus, prior.DriverKey, item.Value.DriverKey, "occupancyChanged"));
            }
        }
        foreach (var item in previous.Where(item => !now.ContainsKey(item.Key))) timeline.Add(new(elapsed.TotalMilliseconds, observedAt, item.Key, item.Value.ConnectionStatus, "not-enumerated", item.Value.DriverKey, null, "portDisappeared"));
        previous.Clear();
        foreach (var item in now) previous[item.Key] = item.Value;
    }

    private PimaxUsbPhysicalPortSnapshot SafeCollect(List<string> errors)
    {
        try { return _collector.Collect(); }
        catch (Exception ex)
        {
            errors.Add($"Physical USB topology collection failed: {ex.Message}");
            var emptyPnp = new PimaxUsbEnumerationSnapshotCollector(new EmptyPnpSource()).Collect();
            return new(_now(), [], [], [], [], null, null, null, emptyPnp, [], [ex.Message]);
        }
    }

    private static string? PrepareOutputDirectory(string? path, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { Directory.CreateDirectory(path); return Path.GetFullPath(path); }
        catch (Exception ex) { warnings.Add($"Output directory unavailable: {ex.Message}"); return null; }
    }

    private static void WriteDetail<T>(string? directory, string fileName, T value, List<string> errors)
    {
        if (directory is null) return;
        try { File.WriteAllText(Path.Combine(directory, fileName), JsonSerializer.Serialize(value, PimaxUsbPhysicalPortMapJson.Options)); }
        catch (Exception ex) { errors.Add($"Detailed evidence write failed for {fileName}: {ex.Message}"); }
    }

    private static void WriteStatus(string? directory, string sessionId, string scenario, DateTimeOffset startedAt, TimeSpan elapsed, string state, List<string> errors)
        => WriteDetail(directory, "observer-status.json", new { schema = "pimax-usb-physical-port-observer-status-v1", sessionId, scenario, startedAt, updatedAt = DateTimeOffset.Now, elapsedMs = elapsed.TotalMilliseconds, state }, errors);

    private static string SafeName(string value) => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character == '-' ? character : '-')).Trim('-');
    private static PimaxUsbPhysicalPortMarker WrapMarker(PimaxObservationMarker marker, string sessionId, string scenarioId)
        => new(sessionId, scenarioId, marker.UtcTimestamp, marker.LocalTimestamp, marker.ElapsedMs, marker.Label, MarkerPhase(marker.Label), marker.Source, marker.Note);
    private static string MarkerPhase(string label)
    {
        if (label.Contains("ready", StringComparison.OrdinalIgnoreCase)) return "readiness";
        if (label.Contains("observed", StringComparison.OrdinalIgnoreCase) || label.Contains("result", StringComparison.OrdinalIgnoreCase)) return "result";
        if (label.Contains("unplug", StringComparison.OrdinalIgnoreCase) || label.Contains("reconnect", StringComparison.OrdinalIgnoreCase) || label.Contains("action", StringComparison.OrdinalIgnoreCase)) return "action";
        return "annotation";
    }
    private static string HashFile(string path) { try { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); } catch { return "unavailable"; } }
    private static string? ReadSourceCommit() { try { using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }); return process is not null && process.WaitForExit(2000) && process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : null; } catch { return null; } }
    private sealed class EmptyPnpSource : IPimaxUsbDeviceInventorySource { public PimaxUsbInventoryResult Collect() => new([], [], []); }
}

internal static class PimaxUsbNativeBufferParser
{
    internal const int ConnectorPropertiesMinimumLength = 18;
    internal const int ConnectionInformationExMinimumLength = 35;
    internal const int ConnectionInformationV2Length = 16;
    internal const int VariableNameHeaderLength = 8;
    internal const int MaximumNativeBufferLength = 65536;

    public static (uint ActualLength, uint Properties, ushort CompanionIndex, ushort CompanionPort, string CompanionHub) ParseConnectorProperties(ReadOnlySpan<byte> buffer)
    {
        Require(buffer, ConnectorPropertiesMinimumLength, "USB_PORT_CONNECTOR_PROPERTIES");
        var actual = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        if (actual < ConnectorPropertiesMinimumLength || actual > MaximumNativeBufferLength || actual > buffer.Length) throw new InvalidDataException($"Invalid connector-property ActualLength {actual}.");
        return (actual, BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]), BinaryPrimitives.ReadUInt16LittleEndian(buffer[12..]), BinaryPrimitives.ReadUInt16LittleEndian(buffer[14..]), DecodeUnicode(buffer[16..(int)actual], "companion hub symbolic link"));
    }

    public static string ParseVariableName(ReadOnlySpan<byte> buffer, string field)
    {
        Require(buffer, VariableNameHeaderLength, field);
        var actual = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        if (actual < VariableNameHeaderLength || actual > MaximumNativeBufferLength || actual > buffer.Length) throw new InvalidDataException($"Invalid {field} ActualLength {actual}.");
        return DecodeUnicode(buffer[8..(int)actual], field);
    }

    public static PimaxUsbRawPortProbe ParseConnectionInformation(ReadOnlySpan<byte> buffer, int index, string? driverKey, string? hubName, PimaxUsbCompanionReference[] companions, bool? userConnectable, bool? debugCapable, bool? typeC, string[] protocols, string[] warnings, string[] errors)
    {
        Require(buffer, ConnectionInformationExMinimumLength, "USB_NODE_CONNECTION_INFORMATION_EX");
        var status = BinaryPrimitives.ReadInt32LittleEndian(buffer[31..]);
        var connected = status == 1;
        return new(index, ConnectionStatus(status), Speed(buffer[23]), protocols, connected, buffer[24] != 0,
            BinaryPrimitives.ReadUInt16LittleEndian(buffer[12..]), BinaryPrimitives.ReadUInt16LittleEndian(buffer[14..]), BinaryPrimitives.ReadUInt16LittleEndian(buffer[16..]),
            buffer[8], buffer[9], buffer[10], driverKey, hubName, userConnectable, debugCapable, typeC, companions, warnings, errors);
    }

    public static (string[] Protocols, uint Flags) ParseConnectionInformationV2(ReadOnlySpan<byte> buffer)
    {
        Require(buffer, ConnectionInformationV2Length, "USB_NODE_CONNECTION_INFORMATION_EX_V2");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        if (length != ConnectionInformationV2Length) throw new InvalidDataException($"Invalid connection-information-v2 Length {length}.");
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]);
        return (Protocols(bits), BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]));
    }

    public static string[] Protocols(uint bits)
    {
        var result = new List<string>();
        if ((bits & 1) != 0) result.Add("USB 1.1");
        if ((bits & 2) != 0) result.Add("USB 2.0");
        if ((bits & 4) != 0) result.Add("USB 3.x");
        return result.ToArray();
    }

    private static string DecodeUnicode(ReadOnlySpan<byte> bytes, string field)
    {
        if ((bytes.Length & 1) != 0) throw new InvalidDataException($"{field} has an odd byte length.");
        var text = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        if (text.Length > 32768) throw new InvalidDataException($"{field} is unreasonably long.");
        return text;
    }

    private static void Require(ReadOnlySpan<byte> buffer, int required, string structure)
    {
        if (buffer.Length < required) throw new InvalidDataException($"Truncated {structure}: {buffer.Length} bytes, expected at least {required}.");
    }

    private static string ConnectionStatus(int value) => value switch { 0 => "noDeviceConnected", 1 => "deviceConnected", 2 => "failedEnumeration", 3 => "generalFailure", 4 => "overcurrent", 5 => "notEnoughPower", 6 => "notEnoughBandwidth", 7 => "hubNestedTooDeeply", 8 => "legacyHub", 9 => "enumerating", 10 => "reset", _ => $"unknown:{value}" };
    private static string Speed(byte value) => value switch { 0 => "low", 1 => "full", 2 => "high", 3 => "superSpeed", _ => $"unknown:{value}" };
}

internal sealed class WindowsUsbHubQuerySource : IPimaxUsbHubQuerySource
{
    public PimaxUsbHubInterface[] EnumerateHubInterfaces()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var guid = NativeMethods.GuidDevInterfaceUsbHub;
        var set = NativeMethods.SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);
        if (set == NativeMethods.InvalidHandleValue) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs for USB hubs failed.");
        var result = new List<PimaxUsbHubInterface>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = NativeMethods.SpDeviceInterfaceData.Create();
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ErrorNoMoreItems) break;
                    throw new Win32Exception(error, $"SetupDiEnumDeviceInterfaces({index}) failed.");
                }
                NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref interfaceData, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                if (required < 8 || required > PimaxUsbNativeBufferParser.MaximumNativeBufferLength) throw new InvalidDataException($"Invalid USB hub interface detail length {required}.");
                var buffer = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    var info = NativeMethods.SpDevinfoData.Create();
                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref interfaceData, buffer, required, out _, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetDeviceInterfaceDetail failed.");
                    var path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4)) ?? throw new InvalidDataException("USB hub interface path was empty.");
                    result.Add(new(path, DeviceId(info.DevInst)));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { NativeMethods.SetupDiDestroyDeviceInfoList(set); }
        return result.ToArray();
    }

    public PimaxUsbHubProbeResult QueryHub(PimaxUsbHubInterface hub)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        using var handle = NativeMethods.CreateFileW(hub.InterfacePath, 0, NativeMethods.FileShareRead | NativeMethods.FileShareWrite, IntPtr.Zero, NativeMethods.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return new("unknown", 0, [], [], [], [$"Hub open failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}"]);
        var hubBuffer = new byte[128];
        var type = "unknown";
        var portCount = 0;
        if (Ioctl(handle, NativeMethods.IoctlUsbGetHubInformationEx, hubBuffer, out var returned, out var error) && returned >= 6)
        {
            type = BinaryPrimitives.ReadInt32LittleEndian(hubBuffer) switch { 1 => "root", 2 => "usb20", 3 => "usb30", _ => "unknown" };
            portCount = BinaryPrimitives.ReadUInt16LittleEndian(hubBuffer.AsSpan(4));
        }
        else
        {
            warnings.Add($"Extended hub information unavailable: {error}");
            var node = new byte[128];
            if (Ioctl(handle, NativeMethods.IoctlUsbGetNodeInformation, node, out returned, out error) && returned >= 7)
            {
                type = "legacyHub";
                portCount = node[6];
            }
            else errors.Add($"Hub information query failed: {error}");
        }
        if (portCount is < 0 or > 255) { errors.Add($"Unsafe downstream port count {portCount}."); portCount = 0; }
        var ports = Enumerable.Range(1, portCount).Select(index => QueryPort(handle, index)).ToArray();
        var protocols = ports.SelectMany(port => port.Protocols).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(type, portCount, protocols, ports, warnings.ToArray(), errors.ToArray());
    }

    private static PimaxUsbRawPortProbe QueryPort(SafeFileHandle handle, int index)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var properties = 0u;
        bool? user = null, debug = null, typeC = null;
        var companions = new List<PimaxUsbCompanionReference>();
        var connector = new byte[4096];
        BinaryPrimitives.WriteUInt32LittleEndian(connector, (uint)index);
        if (Ioctl(handle, NativeMethods.IoctlUsbGetPortConnectorProperties, connector, out var returned, out var connectorError))
        {
            try
            {
                var parsed = PimaxUsbNativeBufferParser.ParseConnectorProperties(connector.AsSpan(0, returned));
                properties = parsed.Properties;
                user = (properties & 1) != 0;
                debug = (properties & 2) != 0;
                typeC = (properties & 8) != 0;
                companions.Add(new(parsed.CompanionIndex, parsed.CompanionPort, parsed.CompanionHub, false));
                if ((properties & 4) != 0)
                {
                    for (ushort companionIndex = 1; companionIndex < 32; companionIndex++)
                    {
                        Array.Clear(connector);
                        BinaryPrimitives.WriteUInt32LittleEndian(connector, (uint)index);
                        BinaryPrimitives.WriteUInt16LittleEndian(connector.AsSpan(12), companionIndex);
                        if (!Ioctl(handle, NativeMethods.IoctlUsbGetPortConnectorProperties, connector, out returned, out _)) break;
                        var extra = PimaxUsbNativeBufferParser.ParseConnectorProperties(connector.AsSpan(0, returned));
                        if (extra.CompanionPort == 0 || string.IsNullOrWhiteSpace(extra.CompanionHub)) break;
                        companions.Add(new(extra.CompanionIndex, extra.CompanionPort, extra.CompanionHub, false));
                    }
                }
            }
            catch (InvalidDataException ex) { errors.Add(ex.Message); }
        }
        else warnings.Add($"Connector properties unsupported or unavailable: {connectorError}");
        companions.RemoveAll(item => item.CompanionPortNumber == 0 || string.IsNullOrWhiteSpace(item.CompanionHubSymbolicLink));

        var v2 = new byte[PimaxUsbNativeBufferParser.ConnectionInformationV2Length];
        BinaryPrimitives.WriteUInt32LittleEndian(v2, (uint)index);
        BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(4), (uint)v2.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(8), 7);
        string[] protocols = [];
        if (Ioctl(handle, NativeMethods.IoctlUsbGetNodeConnectionInformationExV2, v2, out returned, out var v2Error))
        {
            try { protocols = PimaxUsbNativeBufferParser.ParseConnectionInformationV2(v2.AsSpan(0, returned)).Protocols; }
            catch (InvalidDataException ex) { errors.Add(ex.Message); }
        }
        else warnings.Add($"USB protocol query unavailable: {v2Error}");

        var driverKey = QueryVariableName(handle, NativeMethods.IoctlUsbGetNodeConnectionDriverKeyName, index, "driver key", warnings);
        var hubName = QueryVariableName(handle, NativeMethods.IoctlUsbGetNodeConnectionName, index, "downstream hub name", warnings);
        var connection = new byte[4096];
        BinaryPrimitives.WriteUInt32LittleEndian(connection, (uint)index);
        if (!Ioctl(handle, NativeMethods.IoctlUsbGetNodeConnectionInformationEx, connection, out returned, out var connectionError))
            return new(index, "queryFailed", null, protocols, false, false, 0, 0, 0, 0, 0, 0, driverKey, hubName, user, debug, typeC, companions.ToArray(), warnings.ToArray(), [$"Connection query failed: {connectionError}"]);
        try { return PimaxUsbNativeBufferParser.ParseConnectionInformation(connection.AsSpan(0, returned), index, driverKey, hubName, companions.ToArray(), user, debug, typeC, protocols, warnings.ToArray(), errors.ToArray()); }
        catch (InvalidDataException ex) { errors.Add(ex.Message); return new(index, "parseFailed", null, protocols, false, false, 0, 0, 0, 0, 0, 0, driverKey, hubName, user, debug, typeC, companions.ToArray(), warnings.ToArray(), errors.ToArray()); }
    }

    private static string? QueryVariableName(SafeFileHandle handle, uint code, int index, string field, List<string> warnings)
    {
        var buffer = new byte[4096];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)index);
        if (!Ioctl(handle, code, buffer, out var returned, out var error)) { if (error != "The device is not connected.") warnings.Add($"{field} query unavailable: {error}"); return null; }
        try { return PimaxUsbNativeBufferParser.ParseVariableName(buffer.AsSpan(0, returned), field); }
        catch (InvalidDataException ex) { warnings.Add(ex.Message); return null; }
    }

    private static bool Ioctl(SafeFileHandle handle, uint code, byte[] buffer, out int returned, out string error)
    {
        var success = NativeMethods.DeviceIoControl(handle, code, buffer, buffer.Length, buffer, buffer.Length, out returned, IntPtr.Zero);
        error = success ? "" : new Win32Exception(Marshal.GetLastWin32Error()).Message;
        return success;
    }

    private static string? DeviceId(uint devInst)
    {
        var buffer = new StringBuilder(1024);
        return NativeMethods.CM_Get_Device_IDW(devInst, buffer, buffer.Capacity, 0) == 0 ? buffer.ToString() : null;
    }

    private static class NativeMethods
    {
        public static readonly IntPtr InvalidHandleValue = new(-1);
        public static readonly Guid GuidDevInterfaceUsbHub = new(0xf18a0e88, 0xc30c, 0x11d0, 0x88, 0x15, 0x00, 0xa0, 0xc9, 0x06, 0xbe, 0xd8);
        public const int DigcfPresent = 0x2, DigcfDeviceInterface = 0x10, ErrorNoMoreItems = 259;
        public const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;
        public const uint IoctlUsbGetNodeInformation = 0x00220408;
        public const uint IoctlUsbGetNodeConnectionName = 0x00220414;
        public const uint IoctlUsbGetNodeConnectionDriverKeyName = 0x00220420;
        public const uint IoctlUsbGetNodeConnectionInformationEx = 0x00220448;
        public const uint IoctlUsbGetHubInformationEx = 0x00220454;
        public const uint IoctlUsbGetPortConnectorProperties = 0x00220458;
        public const uint IoctlUsbGetNodeConnectionInformationExV2 = 0x0022045C;

        [StructLayout(LayoutKind.Sequential)] public struct SpDeviceInterfaceData { public int CbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; public static SpDeviceInterfaceData Create() => new() { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() }; }
        [StructLayout(LayoutKind.Sequential)] public struct SpDevinfoData { public int CbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; public static SpDevinfoData Create() => new() { CbSize = Marshal.SizeOf<SpDevinfoData>() }; }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfoData, ref Guid guid, uint index, ref SpDeviceInterfaceData data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SpDeviceInterfaceData interfaceData, IntPtr detail, uint detailSize, out uint required, IntPtr info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SpDeviceInterfaceData interfaceData, IntPtr detail, uint detailSize, out uint required, ref SpDevinfoData info);
        [DllImport("setupapi.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputLength, byte[] output, int outputLength, out int returned, IntPtr overlapped);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern uint CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int bufferLength, uint flags);
    }
}
