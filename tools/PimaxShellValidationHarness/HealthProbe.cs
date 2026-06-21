using System.Management;

namespace PimaxShellValidationHarness;

internal sealed class HealthProbe
{
    private readonly IProcessInventory _processInventory;

    public HealthProbe(IProcessInventory processInventory)
    {
        _processInventory = processInventory;
    }

    public HealthSnapshot Collect(DateTimeOffset? shellRequestedAt)
    {
        var collectedAt = DateTimeOffset.Now;
        var processes = _processInventory.Collect();
        var processPresence = HarnessConstants.HealthProcessNames
            .Select(name =>
            {
                var matches = processes.Where(process => string.Equals(process.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
                return new ProcessPresence(name, matches.Length, matches.Select(process => process.ProcessId).Where(id => id >= 0).ToArray());
            })
            .ToArray();

        var pnpEvidence = CollectPnpEvidence();
        var processNames = processes.Select(process => process.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var softwareReady = processNames.Contains("PimaxClient")
            && (processNames.Contains("PiService") || processNames.Contains("pi_server") || processNames.Contains("PiPlayService"));
        var crystalDetected = pnpEvidence.Any(record =>
            ContainsAny(record.Name, "Pimax", "Crystal")
            || ContainsAny(record.DeviceId, "VID_34A4&PID_0012", "VID_2104&PID_0220"));
        var runtimeReady = pnpEvidence.Any(record => record.Present && ContainsAny(record.DeviceId, "VID_34A4&PID_0012"))
            && pnpEvidence.Any(record => record.Present && ContainsAny(record.DeviceId, "VID_2104&PID_0220"));
        var usb2 = pnpEvidence.Any(record => record.Present && ContainsAny(record.DeviceId, "VID_05E3&PID_0608", "VID_28DE&PID_2101", "VID_28DE&PID_2300"));
        var superSpeed = pnpEvidence.Any(record => record.Present && ContainsAny(record.DeviceId, "VID_34A4&PID_0012", "VID_2104&PID_0220"));
        var displayPort = pnpEvidence.Any(record => record.Present
            && ContainsAny(record.PnpClass, "Monitor", "Display")
            && ContainsAny(record.Name, "Pimax", "Crystal"));

        return new HealthSnapshot(
            collectedAt,
            shellRequestedAt is null ? 0 : Math.Max(0, (collectedAt - shellRequestedAt.Value).TotalSeconds),
            processPresence,
            softwareReady,
            crystalDetected,
            runtimeReady,
            usb2,
            superSpeed,
            displayPort,
            pnpEvidence);
    }

    private static PnpDeviceRecord[] CollectPnpEvidence()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, PNPClass, Status, Present FROM Win32_PnPEntity");
            return searcher.Get()
                .OfType<ManagementObject>()
                .Select(device => new PnpDeviceRecord(
                    Convert.ToString(device["Name"]),
                    Convert.ToString(device["DeviceID"]),
                    Convert.ToString(device["PNPClass"]),
                    Convert.ToString(device["Status"]),
                    device["Present"] is bool present && present))
                .Where(IsPimaxEvidence)
                .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.DeviceId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    internal static bool IsPimaxEvidence(PnpDeviceRecord record)
        => ContainsAny(record.Name, "Pimax", "Crystal", "EyeChip", "Tobii")
            || ContainsAny(record.DeviceId, "VID_34A4", "VID_2104", "VID_05E3", "VID_28DE", "PIMAX");

    private static bool ContainsAny(string? value, params string[] needles)
        => !string.IsNullOrWhiteSpace(value)
            && needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
