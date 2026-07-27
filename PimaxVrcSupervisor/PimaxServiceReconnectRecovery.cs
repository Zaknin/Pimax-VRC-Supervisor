using System.Globalization;
using System.Text.RegularExpressions;

internal enum PimaxServiceLogEventKind
{
    HmdDisconnected,
    HmdConnected,
    HidReady,
    DisplayRestore,
    RegistrationSucceeded,
    LegacyHidRemoved,
    LegacyHidAdded
}

internal sealed record PimaxHeadsetIdentity(
    string? Model,
    string? ProductName,
    string? SerialNumber)
{
    public bool IsPositivePimaxCrystalP3b
        => Model?.Contains("Pimax P3B", StringComparison.OrdinalIgnoreCase) == true
            && (string.IsNullOrWhiteSpace(ProductName)
                || ProductName.Contains("Pimax Crystal", StringComparison.OrdinalIgnoreCase));

    public bool Matches(PimaxHeadsetIdentity candidate)
    {
        if (!IsPositivePimaxCrystalP3b || !candidate.IsPositivePimaxCrystalP3b)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SerialNumber)
            && !string.IsNullOrWhiteSpace(candidate.SerialNumber)
            && !string.Equals(SerialNumber, candidate.SerialNumber, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public string DiagnosticIdentity
        => $"model={(string.IsNullOrWhiteSpace(Model) ? "unknown" : Model)}"
            + $"; product={(string.IsNullOrWhiteSpace(ProductName) ? "unknown" : ProductName)}"
            + $"; serial={(string.IsNullOrWhiteSpace(SerialNumber) ? "unavailable" : "present")}";
}

internal sealed record PimaxServiceLogEvent(
    DateTimeOffset Timestamp,
    PimaxServiceLogEventKind Kind,
    PimaxHeadsetIdentity? Identity,
    string EventKey);

internal static class PimaxServiceLogParser
{
    private static readonly Regex ConnectedPattern = new(
        @"HMD connected:\s*(?<model>[^,]+),\s*product name:\s*(?<product>[^,]+),\s*serial number:\s*(?<serial>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DisconnectedPattern = new(
        @"HMD disconnected:\s*(?<model>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string line, out PimaxServiceLogEvent? entry)
    {
        entry = null;
        if (!TryParseTimestamp(line, out var timestamp))
        {
            return false;
        }

        var connected = ConnectedPattern.Match(line);
        if (connected.Success)
        {
            entry = Create(
                timestamp,
                PimaxServiceLogEventKind.HmdConnected,
                new PimaxHeadsetIdentity(
                    connected.Groups["model"].Value.Trim(),
                    connected.Groups["product"].Value.Trim(),
                    connected.Groups["serial"].Value.Trim()),
                line);
            return true;
        }

        var disconnected = DisconnectedPattern.Match(line);
        if (disconnected.Success)
        {
            entry = Create(
                timestamp,
                PimaxServiceLogEventKind.HmdDisconnected,
                new PimaxHeadsetIdentity(disconnected.Groups["model"].Value.Trim(), null, null),
                line);
            return true;
        }

        var kind = line switch
        {
            _ when line.Contains("HID device is ready", StringComparison.OrdinalIgnoreCase)
                => PimaxServiceLogEventKind.HidReady,
            _ when line.Contains("hmd display restore", StringComparison.OrdinalIgnoreCase)
                => PimaxServiceLogEventKind.DisplayRestore,
            _ when line.Contains("P3B VersionChecker: Register success", StringComparison.OrdinalIgnoreCase)
                => PimaxServiceLogEventKind.RegistrationSucceeded,
            _ when line.Contains("removed hid device", StringComparison.OrdinalIgnoreCase)
                => PimaxServiceLogEventKind.LegacyHidRemoved,
            _ when line.Contains("added hid device", StringComparison.OrdinalIgnoreCase)
                => PimaxServiceLogEventKind.LegacyHidAdded,
            _ => (PimaxServiceLogEventKind?)null
        };
        if (kind is null)
        {
            return false;
        }

        entry = Create(timestamp, kind.Value, null, line);
        return true;
    }

    private static PimaxServiceLogEvent Create(
        DateTimeOffset timestamp,
        PimaxServiceLogEventKind kind,
        PimaxHeadsetIdentity? identity,
        string line)
        => new(timestamp, kind, identity, $"{timestamp:O}|{kind}|{line.Trim()}");

    private static bool TryParseTimestamp(string line, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return line.Length >= 23
            && DateTimeOffset.TryParseExact(
                line[..23],
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out timestamp);
    }
}

internal enum PimaxServiceReconnectDiagnosticCode
{
    DisconnectObserved,
    PendingArmed,
    CandidateReturnObserved,
    IdentityMismatch,
    PhysicalIdentityUnavailable,
    ReadinessMarkerObserved,
    ReadinessIncomplete,
    Ready,
    Expired,
    DuplicateSuppressed,
    StandaloneReturnIgnored
}

internal sealed record PimaxServiceReconnectDiagnostic(
    PimaxServiceReconnectDiagnosticCode Code,
    string Message);

internal sealed record PimaxServiceReconnectObservation(
    PimaxServiceReconnectDiagnostic[] Diagnostics);

internal sealed record ScopedDeviceRecoverySelection(
    bool RestartVrcFaceTracking,
    bool RestartBrokenEye,
    ManagedAutoLaunchApp[] AutoLaunchApps)
{
    public string[] ApplicationNames
    {
        get
        {
            var names = new List<string>();
            if (RestartVrcFaceTracking)
            {
                names.Add("VRCFaceTracking");
            }

            if (RestartBrokenEye)
            {
                names.Add("Broken Eye");
            }

            names.AddRange(AutoLaunchApps.Select(app => app.DisplayName));
            return names.ToArray();
        }
    }

    public static ScopedDeviceRecoverySelection Create(
        DeviceRecoveryPlan plan,
        ManagedAutoLaunchApp[] pimaxDependentAutoLaunchApps)
        => new(
            plan.Targets.Contains(DeviceRecoveryTarget.VrcFaceTracking),
            plan.Targets.Contains(DeviceRecoveryTarget.BrokenEye),
            plan.Targets.Contains(DeviceRecoveryTarget.PimaxDependentAutoLaunchApps)
                ? pimaxDependentAutoLaunchApps
                : []);
}
