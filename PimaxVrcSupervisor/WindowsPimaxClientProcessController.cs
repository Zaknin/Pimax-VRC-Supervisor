using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class WindowsPimaxClientProcessController : IPimaxClientProcessController
{
    private const string ProcessName = "PimaxClient";
    private const string ExpectedProductName = "PimaxClient";
    private const string ExpectedCompanyName = "Pimax";
    private const string ShortcutName = "PimaxPlay.lnk";

    public Task<PimaxClientTargetDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var warnings = new List<string>();
        var errors = new List<string>();
        var snapshots = SnapshotPimaxClientProcesses();
        var rootCandidates = snapshots
            .Where(snapshot =>
                snapshot.HasMainWindow
                && string.Equals(snapshot.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(snapshot.ExecutablePath)
                && string.Equals(snapshot.ProductName, ExpectedProductName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(snapshot.FileDescription, ExpectedProductName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(snapshot.CompanyName, ExpectedCompanyName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (rootCandidates.Length == 0)
        {
            errors.Add("No verified top-level Pimax Play client process was found.");
            return Task.FromResult(new PimaxClientTargetDiscoveryResult(null, snapshots, warnings.ToArray(), errors.ToArray(), PimaxRecoveryFailureCategory.TargetNotFound));
        }

        if (rootCandidates.Length > 1)
        {
            errors.Add("More than one verified top-level Pimax Play client process was found.");
            return Task.FromResult(new PimaxClientTargetDiscoveryResult(null, snapshots, warnings.ToArray(), errors.ToArray(), PimaxRecoveryFailureCategory.TargetAmbiguous));
        }

        var root = rootCandidates[0];
        var shortcut = FindVerifiedShortcut(root.ExecutablePath!, warnings, errors);
        if (shortcut is null)
        {
            return Task.FromResult(new PimaxClientTargetDiscoveryResult(null, snapshots, warnings.ToArray(), errors.ToArray(), PimaxRecoveryFailureCategory.TargetNotFound));
        }

        var target = new PimaxClientTargetDescriptor(
            root.ExecutablePath!,
            shortcut.WorkingDirectory,
            shortcut.Arguments,
            root.ProductName ?? "",
            root.CompanyName ?? "",
            root.ProductVersion ?? "",
            HashFile(root.ExecutablePath!),
            [root.ProcessId],
            shortcut.Source,
            "PimaxPlayUiClient");

        return Task.FromResult(new PimaxClientTargetDiscoveryResult(target, snapshots, warnings.ToArray(), errors.ToArray(), null));
    }

    public Task<PimaxClientProcessSnapshot[]> SnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SnapshotPimaxClientProcesses());
    }

    public async Task<PimaxRecoveryOperationResult> RequestGracefulCloseAsync(
        PimaxClientTargetDescriptor target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var processes = GetVerifiedTargetProcesses(target).ToArray();
        if (processes.Length == 0)
        {
            return new PimaxRecoveryOperationResult(true, true, "Verified Pimax Play client process was already closed.", target.TargetProcessIds);
        }

        foreach (var process in processes)
        {
            using (process)
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    process.CloseMainWindow();
                }
            }
        }

        var exited = await WaitForExitAsync(target.TargetProcessIds, timeout, cancellationToken);
        return new PimaxRecoveryOperationResult(
            true,
            exited,
            exited ? "Verified Pimax Play client closed gracefully." : "Verified Pimax Play client did not close before the graceful timeout.",
            target.TargetProcessIds);
    }

    public async Task<PimaxRecoveryOperationResult> ForceStopAsync(
        PimaxClientTargetDescriptor target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var failed = new List<int>();
        foreach (var process in GetVerifiedTargetProcesses(target))
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: false);
                }
                catch
                {
                    failed.Add(process.Id);
                }
            }
        }

        var exited = failed.Count == 0 && await WaitForExitAsync(target.TargetProcessIds, timeout, cancellationToken);
        return new PimaxRecoveryOperationResult(
            true,
            exited,
            exited ? "Verified Pimax Play client PID stopped." : "Could not stop every verified Pimax Play client PID.",
            target.TargetProcessIds);
    }

    public async Task<PimaxRecoveryOperationResult> RelaunchAsync(
        PimaxClientTargetDescriptor target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(target.ExecutablePath))
        {
            return new PimaxRecoveryOperationResult(true, false, "Verified Pimax Play client executable no longer exists.", []);
        }

        var startInfo = BuildDetachedRelaunchStartInfo(target);
        Process.Start(startInfo)?.Dispose();

        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var discovery = await DiscoverAsync(cancellationToken);
            if (discovery.Target is not null
                && string.Equals(discovery.Target.ExecutablePath, target.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(discovery.Target.Sha256, target.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new PimaxRecoveryOperationResult(true, true, "Verified Pimax Play client relaunched.", discovery.Target.TargetProcessIds);
            }

            await Task.Delay(500, cancellationToken);
        }

        return new PimaxRecoveryOperationResult(true, false, "Pimax Play client relaunch was not detected before timeout.", []);
    }

    internal static ProcessStartInfo BuildDetachedRelaunchStartInfo(PimaxClientTargetDescriptor target)
        => new()
        {
            FileName = target.ExecutablePath,
            Arguments = target.Arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(target.WorkingDirectory)
                ? Path.GetDirectoryName(target.ExecutablePath) ?? Environment.CurrentDirectory
                : target.WorkingDirectory,
            UseShellExecute = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false
        };

    private static IEnumerable<Process> GetVerifiedTargetProcesses(PimaxClientTargetDescriptor target)
    {
        var result = new List<Process>();
        foreach (var processId in target.TargetProcessIds)
        {
            Process? process = null;
            try
            {
                process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    process.Dispose();
                    continue;
                }

                var path = SafeGetProcessPath(process);
                if (!string.Equals(path, target.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(HashFile(path!), target.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    process.Dispose();
                    continue;
                }

                result.Add(process);
                process = null;
            }
            catch
            {
                process?.Dispose();
            }
        }

        return result;
    }

    private static async Task<bool> WaitForExitAsync(int[] processIds, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!processIds.Any(IsProcessRunning))
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        return !processIds.Any(IsProcessRunning);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static PimaxClientProcessSnapshot[] SnapshotPimaxClientProcesses()
        => Process.GetProcessesByName(ProcessName)
            .Select(Snapshot)
            .OrderBy(snapshot => snapshot.ParentProcessId ?? -1)
            .ThenBy(snapshot => snapshot.ProcessId)
            .ToArray();

    private static PimaxClientProcessSnapshot Snapshot(Process process)
    {
        using (process)
        {
            var path = SafeGetProcessPath(process);
            var version = SafeVersion(path);
            return new PimaxClientProcessSnapshot(
                SafeProcessId(process),
                null,
                SafeProcessName(process),
                path,
                process.MainWindowHandle != IntPtr.Zero ? "TopLevelUiClientCandidate" : "ChildOrHelperProcess",
                SafeStartTime(process),
                process.MainWindowHandle != IntPtr.Zero,
                SafeMainWindowTitle(process),
                version?.CompanyName,
                version?.FileDescription,
                version?.ProductName,
                version?.ProductVersion);
        }
    }

    private static ShortcutTarget? FindVerifiedShortcut(string expectedPath, List<string> warnings, List<string> errors)
    {
        var shortcuts = ShortcutRoots()
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, ShortcutName, SearchOption.AllDirectories))
            .Select(TryReadShortcut)
            .Where(shortcut => shortcut is not null)
            .Cast<ShortcutTarget>()
            .Where(shortcut => string.Equals(shortcut.TargetPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (shortcuts.Length == 0)
        {
            errors.Add($"No verified {ShortcutName} shortcut target matched the running Pimax Play client executable.");
            return null;
        }

        var distinct = shortcuts
            .DistinctBy(shortcut => $"{shortcut.TargetPath}|{shortcut.Arguments}|{shortcut.WorkingDirectory}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinct.Length > 1)
        {
            errors.Add("Multiple distinct PimaxPlay shortcut relaunch targets were found.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(distinct[0].Arguments))
        {
            warnings.Add("PimaxPlay shortcut contains launch arguments; they will be preserved exactly.");
        }

        return distinct[0];
    }

    private static IEnumerable<string> ShortcutRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
    }

    private static ShortcutTarget? TryReadShortcut(string path)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(path);
            return new ShortcutTarget(
                path,
                (string)shortcut.TargetPath,
                (string)shortcut.Arguments,
                (string)shortcut.WorkingDirectory);
        }
        catch
        {
            return null;
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static FileVersionInfo? SafeVersion(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : FileVersionInfo.GetVersionInfo(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static string? SafeMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? SafeStartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime);
        }
        catch
        {
            return null;
        }
    }

    private sealed record ShortcutTarget(
        string Source,
        string TargetPath,
        string Arguments,
        string WorkingDirectory);
}
