using System.Diagnostics;
using System.Security.Principal;

namespace PimaxShellValidationHarness;

internal interface IProcessInventory
{
    ProcessRecord[] Collect();
}

internal sealed class SystemProcessInventory : IProcessInventory
{
    public ProcessRecord[] Collect()
        => Process.GetProcesses()
            .Select(ToRecord)
            .OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.ProcessId)
            .ToArray();

    private static ProcessRecord ToRecord(Process process)
    {
        try
        {
            return new ProcessRecord(
                process.ProcessName,
                process.Id,
                process.SessionId,
                TryRead(() => process.MainModule?.FileName),
                TryRead(() => new DateTimeOffset(process.StartTime)));
        }
        catch
        {
            return new ProcessRecord(process.ProcessName, SafeId(process), SafeSession(process), null, null);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static int SafeId(Process process)
    {
        try { return process.Id; } catch { return -1; }
    }

    private static int SafeSession(Process process)
    {
        try { return process.SessionId; } catch { return -1; }
    }

    private static T? TryRead<T>(Func<T> read)
    {
        try { return read(); } catch { return default; }
    }
}

internal static class ExecutionGuards
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static HarnessRefusal? ValidateMainContext(
        bool isElevated,
        bool userInteractive,
        int currentSessionId,
        ProcessRecord[] processes,
        string? confirmation)
    {
        if (!string.Equals(confirmation, HarnessConstants.ConfirmationPhrase, StringComparison.Ordinal))
        {
            return new HarnessRefusal("missing-or-wrong-confirmation", "The exact confirmation phrase is required.");
        }

        if (isElevated)
        {
            return new HarnessRefusal("main-mode-elevated", "Main orchestration mode must run from a normal non-elevated token.");
        }

        if (!userInteractive || currentSessionId <= 0)
        {
            return new HarnessRefusal("not-interactive", "Main orchestration mode requires an interactive desktop session.");
        }

        var explorerInSession = processes.Any(process =>
            string.Equals(process.Name, "explorer", StringComparison.OrdinalIgnoreCase)
            && process.SessionId == currentSessionId);
        if (!explorerInSession)
        {
            return new HarnessRefusal("missing-explorer", "No explorer.exe instance was found in the current interactive session.");
        }

        return null;
    }

    public static HarnessRefusal? ValidateObserverContext(bool isElevated)
        => isElevated
            ? null
            : new HarnessRefusal("observer-not-elevated", "Observer child mode requires elevation.");

    public static HarnessRefusal? ValidateStoppedLaunchGroup(ProcessRecord[] processes)
    {
        var blocked = processes
            .Where(process => HarnessConstants.LaunchOwnedProcessNames.Contains(process.Name, StringComparer.OrdinalIgnoreCase))
            .Select(process => process.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return blocked.Length == 0
            ? null
            : new HarnessRefusal("launch-owned-process-running", $"Launch-owned process still running: {blocked[0]}");
    }
}
