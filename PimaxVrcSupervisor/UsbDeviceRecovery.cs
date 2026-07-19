using System.Text.RegularExpressions;

internal enum UsbPhysicalDeviceClassification
{
    Unknown,
    Unrelated,
    SteamVrRadioDongle,
    PimaxRuntime,
    PimaxEyeTracking,
    PimaxTrackingInterface,
    ViveFaceTracker
}

internal enum UsbPhysicalDeviceTransitionKind
{
    Removed,
    Added
}

internal enum DeviceRecoveryReason
{
    PimaxEyeRuntimeRecovered,
    ViveFaceTrackerRecovered
}

internal enum DeviceRecoveryTarget
{
    VrcFaceTracking,
    BrokenEye,
    PimaxDependentAutoLaunchApps
}

internal sealed record UsbVidPid(string Vid, string Pid)
{
    public static UsbVidPid Create(string vid, string pid)
        => new(vid.ToUpperInvariant(), pid.ToUpperInvariant());

    public override string ToString() => $"{Vid}:{Pid}";
}

internal sealed record UsbPhysicalDevice(
    string PhysicalKey,
    bool UsesContainerIdentity,
    UsbPhysicalDeviceClassification Classification,
    UsbVidPid[] VidPidPairs,
    string[] NodeStableIds,
    string[] ParentStableIds,
    string[] DeviceClasses,
    string[] Services)
{
    public string DebugIdentity
        => $"physicalKey={PhysicalKey}; identity={(UsesContainerIdentity ? "containerId" : "instanceId")}; "
            + $"vidPid={Format(VidPidPairs.Select(pair => pair.ToString()))}; nodes={NodeStableIds.Length}; "
            + $"nodeIds={Format(NodeStableIds)}; parentIds={Format(ParentStableIds)}; "
            + $"classes={Format(DeviceClasses)}; services={Format(Services)}";

    private static string Format(IEnumerable<string> values)
    {
        var materialized = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return materialized.Length == 0 ? "none" : string.Join(",", materialized);
    }
}

internal sealed record UsbPhysicalDeviceTransition(
    UsbPhysicalDeviceTransitionKind Kind,
    UsbPhysicalDevice Device,
    bool MatchedPendingDisconnect,
    bool RecoverySuppressed);

internal sealed record UsbDeviceInventoryObservation(
    bool BaselineEstablished,
    bool InventoryUnavailable,
    UsbPhysicalDeviceTransition[] Transitions,
    int ExpiredPendingReconnects);

internal sealed record DeviceRecoveryDependencies(
    bool VrcFaceTrackingDependsOnPimax,
    bool BrokenEyeDependsOnPimax,
    bool PimaxDependentAutoLaunchAppsConfigured,
    bool VrcFaceTrackingDependsOnViveFaceTracker);

internal sealed record DeviceRecoveryReadiness(
    bool PimaxRuntimeReady,
    bool ViveFaceTrackerReady);

internal sealed record DeviceRecoveryPlan(
    DeviceRecoveryReason Reason,
    DeviceRecoveryTarget[] Targets,
    int CoalescedPhysicalDeviceCount);

internal sealed class UsbDeviceAttributionRules
{
    private static readonly UsbVidPid SteamVrRadio = UsbVidPid.Create("28DE", "2101");
    private static readonly UsbVidPid PimaxEyeChip = UsbVidPid.Create("2104", "0220");
    private static readonly UsbVidPid PimaxTracking = UsbVidPid.Create("28DE", "2300");
    private readonly HashSet<UsbVidPid> _pimaxRuntime;
    private readonly HashSet<UsbVidPid> _viveFaceTracker;

    public UsbDeviceAttributionRules(
        IEnumerable<UsbVidPid> pimaxRuntime,
        IEnumerable<UsbVidPid> viveFaceTracker)
    {
        _pimaxRuntime = pimaxRuntime.ToHashSet();
        _viveFaceTracker = viveFaceTracker.ToHashSet();
    }

    public static UsbDeviceAttributionRules FromConfig(SupervisorConfig config)
        => new(
            ExtractExactVidPid(config.PimaxDetectors),
            ExtractExactVidPid(config.MouthTrackerDetectors));

    public UsbPhysicalDeviceClassification Classify(IEnumerable<PimaxUsbDeviceRecord> nodes)
    {
        var materialized = nodes.ToArray();
        var pairs = materialized
            .Where(node => !string.IsNullOrWhiteSpace(node.Vid) && !string.IsNullOrWhiteSpace(node.Pid))
            .Select(node => UsbVidPid.Create(node.Vid!, node.Pid!))
            .Distinct()
            .ToArray();

        if (pairs.Contains(SteamVrRadio))
        {
            return UsbPhysicalDeviceClassification.SteamVrRadioDongle;
        }

        if (pairs.Contains(PimaxEyeChip))
        {
            return UsbPhysicalDeviceClassification.PimaxEyeTracking;
        }

        if (pairs.Any(_viveFaceTracker.Contains))
        {
            return UsbPhysicalDeviceClassification.ViveFaceTracker;
        }

        if (pairs.Any(_pimaxRuntime.Contains))
        {
            return UsbPhysicalDeviceClassification.PimaxRuntime;
        }

        if (pairs.Contains(PimaxTracking))
        {
            return UsbPhysicalDeviceClassification.PimaxTrackingInterface;
        }

        return pairs.Length > 0
            ? UsbPhysicalDeviceClassification.Unrelated
            : UsbPhysicalDeviceClassification.Unknown;
    }

    internal static UsbVidPid[] ExtractExactVidPid(IEnumerable<string[]> detectorGroups)
    {
        var pairs = new HashSet<UsbVidPid>();
        foreach (var value in detectorGroups.SelectMany(group => group))
        {
            var match = Regex.Match(
                value,
                @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                pairs.Add(UsbVidPid.Create(match.Groups[1].Value, match.Groups[2].Value));
            }
        }

        return pairs.ToArray();
    }
}

internal sealed class UsbPhysicalDeviceInventory
{
    private readonly IReadOnlyDictionary<string, UsbPhysicalDevice> _devices;

    private UsbPhysicalDeviceInventory(IReadOnlyDictionary<string, UsbPhysicalDevice> devices)
    {
        _devices = devices;
    }

    public IReadOnlyDictionary<string, UsbPhysicalDevice> Devices => _devices;

    public static UsbPhysicalDeviceInventory Create(
        PimaxUsbEnumerationSnapshot snapshot,
        UsbDeviceAttributionRules rules)
    {
        var groups = snapshot.FullInventory
            .Where(IsPresentUsbOrPnpNode)
            .GroupBy(
                node => !string.IsNullOrWhiteSpace(node.ContainerStableId)
                    ? $"container:{node.ContainerStableId}"
                    : $"instance:{node.StableId}",
                StringComparer.OrdinalIgnoreCase);

        var devices = new Dictionary<string, UsbPhysicalDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var nodes = group.ToArray();
            var pairs = nodes
                .Where(node => !string.IsNullOrWhiteSpace(node.Vid) && !string.IsNullOrWhiteSpace(node.Pid))
                .Select(node => UsbVidPid.Create(node.Vid!, node.Pid!))
                .Distinct()
                .OrderBy(pair => pair.Vid, StringComparer.Ordinal)
                .ThenBy(pair => pair.Pid, StringComparer.Ordinal)
                .ToArray();
            devices[group.Key] = new UsbPhysicalDevice(
                group.Key,
                group.Key.StartsWith("container:", StringComparison.Ordinal),
                rules.Classify(nodes),
                pairs,
                nodes.Select(node => node.StableId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                nodes.Select(node => node.ParentStableId).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                nodes.Select(node => node.DeviceClass).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                nodes.Select(node => node.Service).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        return new UsbPhysicalDeviceInventory(devices);
    }

    private static bool IsPresentUsbOrPnpNode(PimaxUsbDeviceRecord node)
    {
        if (!node.Present || !node.Connected)
        {
            return false;
        }

        return string.Equals(node.EnumeratorName, "USB", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.EnumeratorName, "HID", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(node.Vid)
            || node.HardwareIds.Any(id => id.Contains("USB", StringComparison.OrdinalIgnoreCase)
                || id.Contains("HID", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class UsbDeviceRecoveryCoordinator
{
    private readonly UsbDeviceAttributionRules _rules;
    private readonly TimeSpan _pendingReconnectLifetime;
    private readonly Dictionary<string, PendingReconnect> _pending = new(StringComparer.OrdinalIgnoreCase);
    private UsbPhysicalDeviceInventory? _previous;

    public UsbDeviceRecoveryCoordinator(
        UsbDeviceAttributionRules rules,
        TimeSpan pendingReconnectLifetime)
    {
        _rules = rules;
        _pendingReconnectLifetime = pendingReconnectLifetime;
    }

    public bool HasPimaxRecoveryAwaitingReadiness
        => _pending.Values.Any(pending => pending.Stage == PendingReconnectStage.AwaitingReadiness
            && RecoveryReason(pending.Classification) == DeviceRecoveryReason.PimaxEyeRuntimeRecovered);

    public bool HasViveFaceTrackerRecoveryAwaitingReadiness
        => _pending.Values.Any(pending => pending.Stage == PendingReconnectStage.AwaitingReadiness
            && RecoveryReason(pending.Classification) == DeviceRecoveryReason.ViveFaceTrackerRecovered);

    public UsbDeviceInventoryObservation Observe(
        PimaxUsbEnumerationSnapshot snapshot,
        DateTimeOffset now,
        bool suppressRecovery)
    {
        var expired = ExpirePending(now);
        if (snapshot.Errors.Length > 0)
        {
            return new UsbDeviceInventoryObservation(
                BaselineEstablished: _previous is not null,
                InventoryUnavailable: true,
                [],
                expired);
        }

        var current = UsbPhysicalDeviceInventory.Create(snapshot, _rules);
        if (_previous is null)
        {
            _previous = current;
            if (suppressRecovery)
            {
                _pending.Clear();
            }

            return new UsbDeviceInventoryObservation(
                BaselineEstablished: true,
                InventoryUnavailable: false,
                [],
                expired);
        }

        var transitions = new List<UsbPhysicalDeviceTransition>();
        foreach (var removed in _previous.Devices.Values
            .Where(device => !current.Devices.ContainsKey(device.PhysicalKey))
            .OrderBy(device => device.PhysicalKey, StringComparer.OrdinalIgnoreCase))
        {
            transitions.Add(new UsbPhysicalDeviceTransition(
                UsbPhysicalDeviceTransitionKind.Removed,
                removed,
                MatchedPendingDisconnect: false,
                RecoverySuppressed: suppressRecovery));

            if (!suppressRecovery && RecoveryReason(removed.Classification) is not null)
            {
                _pending[removed.PhysicalKey] = new PendingReconnect(
                    removed.Classification,
                    PendingReconnectStage.AwaitingReturn,
                    now.Add(_pendingReconnectLifetime));
            }
        }

        foreach (var added in current.Devices.Values
            .Where(device => !_previous.Devices.ContainsKey(device.PhysicalKey))
            .OrderBy(device => device.PhysicalKey, StringComparer.OrdinalIgnoreCase))
        {
            PendingReconnect? matchedPending = null;
            if (!suppressRecovery
                && _pending.TryGetValue(added.PhysicalKey, out var pending)
                && pending.Stage == PendingReconnectStage.AwaitingReturn
                && pending.Classification == added.Classification)
            {
                matchedPending = pending;
            }

            if (matchedPending is not null)
            {
                _pending[added.PhysicalKey] = matchedPending with
                {
                    Stage = PendingReconnectStage.AwaitingReadiness,
                    ExpiresAt = now.Add(_pendingReconnectLifetime)
                };
            }

            transitions.Add(new UsbPhysicalDeviceTransition(
                UsbPhysicalDeviceTransitionKind.Added,
                added,
                MatchedPendingDisconnect: matchedPending is not null,
                RecoverySuppressed: suppressRecovery));
        }

        _previous = current;
        if (suppressRecovery)
        {
            _pending.Clear();
        }

        return new UsbDeviceInventoryObservation(
            BaselineEstablished: true,
            InventoryUnavailable: false,
            transitions.ToArray(),
            expired);
    }

    public DeviceRecoveryPlan[] CreateReadyPlans(
        DeviceRecoveryReadiness readiness,
        DeviceRecoveryDependencies dependencies,
        DateTimeOffset now,
        bool suppressRecovery)
    {
        ExpirePending(now);
        if (suppressRecovery)
        {
            _pending.Clear();
            return [];
        }

        var plans = new List<DeviceRecoveryPlan>();
        foreach (var reason in Enum.GetValues<DeviceRecoveryReason>())
        {
            var matchingKeys = _pending
                .Where(pair => pair.Value.Stage == PendingReconnectStage.AwaitingReadiness
                    && RecoveryReason(pair.Value.Classification) == reason)
                .Select(pair => pair.Key)
                .ToArray();
            if (matchingKeys.Length == 0 || !IsReady(reason, readiness))
            {
                continue;
            }

            var targets = Targets(reason, dependencies);
            plans.Add(new DeviceRecoveryPlan(reason, targets, matchingKeys.Length));
            foreach (var key in matchingKeys)
            {
                _pending.Remove(key);
            }
        }

        return plans.ToArray();
    }

    private int ExpirePending(DateTimeOffset now)
    {
        var expired = _pending
            .Where(pair => pair.Value.ExpiresAt <= now)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expired)
        {
            _pending.Remove(key);
        }

        return expired.Length;
    }

    private static bool IsReady(DeviceRecoveryReason reason, DeviceRecoveryReadiness readiness)
        => reason switch
        {
            DeviceRecoveryReason.PimaxEyeRuntimeRecovered => readiness.PimaxRuntimeReady,
            DeviceRecoveryReason.ViveFaceTrackerRecovered => readiness.ViveFaceTrackerReady,
            _ => false
        };

    private static DeviceRecoveryTarget[] Targets(
        DeviceRecoveryReason reason,
        DeviceRecoveryDependencies dependencies)
    {
        var targets = new List<DeviceRecoveryTarget>();
        if (reason == DeviceRecoveryReason.PimaxEyeRuntimeRecovered)
        {
            if (dependencies.VrcFaceTrackingDependsOnPimax)
            {
                targets.Add(DeviceRecoveryTarget.VrcFaceTracking);
            }

            if (dependencies.BrokenEyeDependsOnPimax)
            {
                targets.Add(DeviceRecoveryTarget.BrokenEye);
            }

            if (dependencies.PimaxDependentAutoLaunchAppsConfigured)
            {
                targets.Add(DeviceRecoveryTarget.PimaxDependentAutoLaunchApps);
            }
        }
        else if (reason == DeviceRecoveryReason.ViveFaceTrackerRecovered
            && dependencies.VrcFaceTrackingDependsOnViveFaceTracker)
        {
            targets.Add(DeviceRecoveryTarget.VrcFaceTracking);
        }

        return targets.ToArray();
    }

    private static DeviceRecoveryReason? RecoveryReason(UsbPhysicalDeviceClassification classification)
        => classification switch
        {
            UsbPhysicalDeviceClassification.PimaxRuntime
                or UsbPhysicalDeviceClassification.PimaxEyeTracking
                => DeviceRecoveryReason.PimaxEyeRuntimeRecovered,
            UsbPhysicalDeviceClassification.ViveFaceTracker
                => DeviceRecoveryReason.ViveFaceTrackerRecovered,
            _ => null
        };

    private enum PendingReconnectStage
    {
        AwaitingReturn,
        AwaitingReadiness
    }

    private sealed record PendingReconnect(
        UsbPhysicalDeviceClassification Classification,
        PendingReconnectStage Stage,
        DateTimeOffset ExpiresAt);
}

internal static class UsbDeviceRecoveryLog
{
    public static string Decision(UsbPhysicalDeviceTransition transition)
    {
        var action = transition.Kind == UsbPhysicalDeviceTransitionKind.Removed
            ? "removed"
            : "reconnected";
        if (transition.RecoverySuppressed)
        {
            return $"USB topology changed: {Label(transition.Device.Classification)} {action}; "
                + "device recovery is suppressed by the active session lifecycle.";
        }

        return transition.Device.Classification switch
        {
            UsbPhysicalDeviceClassification.SteamVrRadioDongle
                => $"USB topology changed: unrelated SteamVR radio dongle {action}; no managed application recovery required.",
            UsbPhysicalDeviceClassification.Unrelated
                => $"USB topology changed: unrelated physical device {action}; no managed application recovery required.",
            UsbPhysicalDeviceClassification.Unknown
                => $"USB topology changed: unknown physical device {action}; no managed application recovery scheduled.",
            UsbPhysicalDeviceClassification.PimaxTrackingInterface
                => $"USB topology changed: Pimax tracking interface {action}; no face-application recovery required.",
            UsbPhysicalDeviceClassification.PimaxRuntime
                or UsbPhysicalDeviceClassification.PimaxEyeTracking
                => RelevantDecision("Pimax runtime device", action, transition),
            UsbPhysicalDeviceClassification.ViveFaceTracker
                => RelevantDecision("Vive Face Tracker", action, transition),
            _ => $"USB topology changed: device {action}; no managed application recovery scheduled."
        };
    }

    public static string Plan(DeviceRecoveryPlan plan)
    {
        var reason = plan.Reason == DeviceRecoveryReason.PimaxEyeRuntimeRecovered
            ? "Pimax eye/runtime recovery"
            : "Vive Face Tracker recovery";
        var included = plan.Targets.Length == 0
            ? "none"
            : string.Join(", ", plan.Targets.Select(TargetLabel));
        return $"{reason} is ready; scoped recovery plan includes: {included}. "
            + "XSOverlay, SteamVR, monitors, base stations, OSC Router, and unrelated managed apps are excluded.";
    }

    private static string RelevantDecision(
        string label,
        string action,
        UsbPhysicalDeviceTransition transition)
    {
        if (transition.Kind == UsbPhysicalDeviceTransitionKind.Removed)
        {
            return $"USB topology changed: {label} disconnected; removal alone does not restart applications.";
        }

        return transition.MatchedPendingDisconnect
            ? $"USB topology changed: the same {label} reconnected; waiting for authoritative readiness before scoped recovery."
            : $"USB topology changed: {label} appeared without a matching disconnect; no recovery scheduled.";
    }

    private static string Label(UsbPhysicalDeviceClassification classification)
        => classification switch
        {
            UsbPhysicalDeviceClassification.SteamVrRadioDongle => "unrelated SteamVR radio dongle",
            UsbPhysicalDeviceClassification.PimaxRuntime => "Pimax runtime device",
            UsbPhysicalDeviceClassification.PimaxEyeTracking => "Pimax eye-tracking device",
            UsbPhysicalDeviceClassification.PimaxTrackingInterface => "Pimax tracking interface",
            UsbPhysicalDeviceClassification.ViveFaceTracker => "Vive Face Tracker",
            UsbPhysicalDeviceClassification.Unrelated => "unrelated physical device",
            _ => "unknown physical device"
        };

    private static string TargetLabel(DeviceRecoveryTarget target)
        => target switch
        {
            DeviceRecoveryTarget.VrcFaceTracking => "VRCFaceTracking",
            DeviceRecoveryTarget.BrokenEye => "Broken Eye",
            DeviceRecoveryTarget.PimaxDependentAutoLaunchApps => "explicit Pimax-dependent Autostart apps",
            _ => target.ToString()
        };
}
