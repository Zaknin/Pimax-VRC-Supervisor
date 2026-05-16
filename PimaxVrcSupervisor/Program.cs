using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

var config = SupervisorConfig.Load();
var commandLineArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (commandLineArgs.Any(arg => string.Equals(arg, "--install-auto-launch-task", StringComparison.OrdinalIgnoreCase)))
{
    await InstallAutoLaunchScheduledTaskFromCommandLineAsync(config, shutdown.Token);
    return;
}
if (commandLineArgs.Any(arg => string.Equals(arg, "--watch-vrchat-auto-launch", StringComparison.OrdinalIgnoreCase)))
{
    await AutoLaunchWatcher.RunAsync(shutdown.Token);
    return;
}

var supervisor = new AppSupervisor(config);
await supervisor.RunAsync(shutdown.Token);

static async Task InstallAutoLaunchScheduledTaskFromCommandLineAsync(SupervisorConfig config, CancellationToken cancellationToken)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine("This supervisor is Windows-only.");
        return;
    }

    Console.WriteLine("Installing elevated auto-launch scheduled task...");
    var taskDetails = await ScheduledTaskInstaller.CreateOrUpdateAsync(cancellationToken);
    config.SetAutoLaunchScheduledTask(true);
    config.SaveAutoLaunchScheduledTaskPreference();
    Console.WriteLine($"Installed scheduled task: {taskDetails.TaskName}");
    Console.WriteLine($"Trigger: {taskDetails.TriggerDescription}");
}

internal sealed record ResolvedExecutablePath(string Path, bool WasSelected);

internal enum WatchedProcessState
{
    NotSeenYet,
    Running,
    NormalExit,
    WaitingAfterCrash,
    CrashGraceExpired
}

internal sealed class AppSupervisor
{
    private const int BrokenEyeStartupMaxAttempts = 10;
    private static readonly TimeSpan BrokenEyeStartupCheckDelay = TimeSpan.FromSeconds(5);

    private readonly SupervisorConfig _config;
    private readonly TimeSpan _pollInterval;
    private readonly Dictionary<int, Process> _watchedProcessHandles = new();
    private bool? _lastPimaxConnected;
    private bool? _lastMouthTrackerConnected;
    private bool _mouthTrackerUser;
    private bool _watchedProcessHasBeenSeen;
    private bool _waitingForWatchedProcessRelaunch;
    private bool _managedAppsStarted;
    private DateTimeOffset? _watchedProcessMissingSince;

    public AppSupervisor(SupervisorConfig config)
    {
        _config = config;
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, config.PollIntervalSeconds));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Pimax VRC Supervisor");
        Console.WriteLine("---------------------");

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("This supervisor is Windows-only.");
            return;
        }

        if (!IsAdministrator())
        {
            Console.WriteLine("Warning: this process is not elevated. Build/run the exe directly so the manifest can request administrator permission.");
        }

        if (!await EnsureExecutablePathsAsync(cancellationToken))
        {
            return;
        }

        _mouthTrackerUser = await EnsureMouthTrackerPreferenceAsync(cancellationToken);
        await EnsureAutoLaunchScheduledTaskPreferenceAsync(cancellationToken);

        try
        {
            _lastPimaxConnected = await WaitForPimaxOnStartupAsync(cancellationToken);

            if (_mouthTrackerUser)
            {
                _lastMouthTrackerConnected = await IsMouthTrackerConnectedAsync(cancellationToken);
                if (_lastMouthTrackerConnected.Value)
                {
                    Console.WriteLine("Mouth tracker detected.");
                }
                else
                {
                    Console.WriteLine("you forgot to connect mouth tracker!");
                }
            }
            else
            {
                Console.WriteLine("Mouth tracker monitoring is disabled by config.");
            }

            await StartManagedAppsAsync(cancellationToken);

            Console.WriteLine($"Pimax Crystal initial state: {DescribeConnection(_lastPimaxConnected.Value)}");
            Console.WriteLine("Waiting for Pimax reconnects or VRChat shutdown. Press Ctrl+C to stop.");

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_pollInterval, cancellationToken);

                var watchedProcessState = ObserveWatchedShutdownProcesses();
                if (watchedProcessState == WatchedProcessState.NormalExit)
                {
                    Console.WriteLine("VRChat has shut down. Closing managed apps and exiting.");
                    await StopManagedAppsAsync(cancellationToken);
                    return;
                }
                if (watchedProcessState == WatchedProcessState.CrashGraceExpired)
                {
                    Console.WriteLine("VRChat did not relaunch after a likely crash. Closing managed apps and exiting.");
                    await StopManagedAppsAsync(cancellationToken);
                    return;
                }

                var pimaxConnected = await IsPimaxConnectedAsync(cancellationToken);
                var mouthTrackerConnected = _mouthTrackerUser
                    ? await IsMouthTrackerConnectedAsync(cancellationToken)
                    : (bool?)null;
                var pimaxReconnected = _lastPimaxConnected == false && pimaxConnected;
                var mouthTrackerReconnected = _mouthTrackerUser
                    && _lastMouthTrackerConnected == false
                    && mouthTrackerConnected == true;

                if (pimaxReconnected)
                {
                    Console.WriteLine($"Pimax Crystal reconnected. Restarting managed apps in {_config.RestartDelayAfterReconnectSeconds} seconds.");
                    await DelayWithCancellationAsync(TimeSpan.FromSeconds(_config.RestartDelayAfterReconnectSeconds), cancellationToken);

                    if (_watchedProcessHasBeenSeen && !IsAnyProcessRunning(_config.WatchedShutdownProcessNames))
                    {
                        Console.WriteLine("VRChat shut down during reconnect delay. Closing managed apps and exiting.");
                        await StopManagedAppsAsync(cancellationToken);
                        return;
                    }

                    await StopManagedAppsAsync(cancellationToken);
                    await StartManagedAppsAsync(cancellationToken);
                    pimaxConnected = await IsPimaxConnectedAsync(cancellationToken);
                    mouthTrackerConnected = _mouthTrackerUser
                        ? await IsMouthTrackerConnectedAsync(cancellationToken)
                        : (bool?)null;
                    Console.WriteLine($"Pimax Crystal state after restart: {DescribeConnection(pimaxConnected)}");
                }
                else if (_lastPimaxConnected != pimaxConnected)
                {
                    Console.WriteLine($"Pimax Crystal state changed: {DescribeConnection(pimaxConnected)}");
                }

                if (_mouthTrackerUser && mouthTrackerReconnected && !pimaxReconnected && pimaxConnected)
                {
                    Console.WriteLine("Mouth tracker reconnected while Pimax Crystal stayed connected. Restarting VRCFaceTracking.");
                    await RestartVrcFaceTrackingAsync(cancellationToken);
                }
                else if (_mouthTrackerUser && _lastMouthTrackerConnected == true && mouthTrackerConnected == false)
                {
                    Console.WriteLine("you forgot to connect mouth tracker!");
                }

                _lastPimaxConnected = pimaxConnected;
                if (_mouthTrackerUser)
                {
                    _lastMouthTrackerConnected = mouthTrackerConnected;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Shutdown requested. Closing managed apps.");
            await TryStopManagedAppsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Unexpected supervisor error:");
            Console.WriteLine(ex);

            if (_managedAppsStarted)
            {
                Console.WriteLine("Attempting to close managed apps after unexpected error.");
                await TryStopManagedAppsAsync(CancellationToken.None);
            }
        }
        finally
        {
            ClearWatchedProcessHandles();
        }
    }

    private async Task<bool> EnsureExecutablePathsAsync(CancellationToken cancellationToken)
    {
        var brokenEyePath = await ResolveExecutablePathAsync(
            _config.BrokenEyePath,
            "Broken Eye",
            "Broken Eye.exe",
            suggestedPath: null,
            cancellationToken);
        if (brokenEyePath is null)
        {
            return false;
        }
        UpdateProcessNamesFromSelectedExecutable(
            "Broken Eye",
            brokenEyePath.Path,
            brokenEyePath.WasSelected,
            processNames => _config.BrokenEyeProcessNames = processNames,
            _config.BrokenEyeProcessNames);

        var vrcFaceTrackingPath = await ResolveExecutablePathAsync(
            _config.VrcFaceTrackingPath,
            "VRCFaceTracking",
            "VRCFaceTracking.exe",
            SupervisorConfig.DefaultVrcFaceTrackingPath,
            cancellationToken);
        if (vrcFaceTrackingPath is null)
        {
            return false;
        }
        UpdateProcessNamesFromSelectedExecutable(
            "VRCFaceTracking",
            vrcFaceTrackingPath.Path,
            vrcFaceTrackingPath.WasSelected,
            processNames => _config.VrcFaceTrackingProcessNames = processNames,
            _config.VrcFaceTrackingProcessNames);

        var pathsChanged = !StringComparer.OrdinalIgnoreCase.Equals(_config.BrokenEyePath, brokenEyePath.Path)
            || !StringComparer.OrdinalIgnoreCase.Equals(_config.VrcFaceTrackingPath, vrcFaceTrackingPath.Path)
            || brokenEyePath.WasSelected
            || vrcFaceTrackingPath.WasSelected;

        _config.BrokenEyePath = brokenEyePath.Path;
        _config.VrcFaceTrackingPath = vrcFaceTrackingPath.Path;

        ValidateExecutable(_config.BrokenEyePath, "Broken Eye");
        ValidateExecutable(_config.VrcFaceTrackingPath, "VRCFaceTracking");

        if (pathsChanged)
        {
            _config.SaveExecutableSettings();
        }

        return true;
    }

    private async Task<bool> EnsureMouthTrackerPreferenceAsync(CancellationToken cancellationToken)
    {
        if (_config.TryGetMouthTrackerUser(out var mouthTrackerUser))
        {
            return mouthTrackerUser;
        }

        Console.WriteLine("MouthTrackerUser is not configured.");
        var answer = await AskMouthTrackerPreferenceAsync(cancellationToken);
        _config.SetMouthTrackerUser(answer);
        _config.SaveMouthTrackerPreference();

        Console.WriteLine(answer
            ? "Mouth tracker workflow enabled."
            : "Mouth tracker workflow disabled.");

        return answer;
    }

    private async Task EnsureAutoLaunchScheduledTaskPreferenceAsync(CancellationToken cancellationToken)
    {
        if (_config.TryGetAutoLaunchScheduledTask(out var autoLaunchScheduledTask))
        {
            if (autoLaunchScheduledTask)
            {
                await EnsureAutoLaunchScheduledTaskInstalledAsync(cancellationToken);
            }

            return;
        }

        Console.WriteLine("AutoLaunchScheduledTask is not configured.");
        var answer = await AskAutoLaunchScheduledTaskPreferenceAsync(cancellationToken);
        if (!answer)
        {
            _config.SetAutoLaunchScheduledTask(false);
            _config.SaveAutoLaunchScheduledTaskPreference();
            Console.WriteLine("Elevated auto-launch scheduled task was not created.");
            return;
        }

        try
        {
            var taskDetails = await ScheduledTaskInstaller.CreateOrUpdateAsync(cancellationToken);
            _config.SetAutoLaunchScheduledTask(true);
            _config.SaveAutoLaunchScheduledTaskPreference();
            Console.WriteLine($"Created elevated auto-launch scheduled task: {taskDetails.TaskName}");
            Console.WriteLine($"Trigger: {taskDetails.TriggerDescription}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not create elevated auto-launch scheduled task:");
            Console.WriteLine(ex.Message);
            Console.WriteLine("The preference was not saved, so the app can ask again next run.");
        }
    }

    private async Task EnsureAutoLaunchScheduledTaskInstalledAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Ensuring elevated auto-launch scheduled task is installed.");
        try
        {
            var taskDetails = await ScheduledTaskInstaller.CreateOrUpdateAsync(cancellationToken);
            Console.WriteLine($"Installed elevated auto-launch scheduled task: {taskDetails.TaskName}");
            Console.WriteLine($"Trigger: {taskDetails.TriggerDescription}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not install elevated auto-launch scheduled task:");
            Console.WriteLine(ex.Message);
        }
    }

    private static Task<bool> AskAutoLaunchScheduledTaskPreferenceAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                var result = MessageBox.Show(
                    "Create an elevated Windows Scheduled Task that watches for VRChat.exe and starts Pimax VRC Supervisor when vrserver.exe is already running?\n\nThis uses a hidden elevated watcher at Windows sign-in, so it does not depend on Windows Security audit events.",
                    "Pimax VRC Supervisor",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);

                completion.TrySetResult(result == DialogResult.Yes);
            }
            catch (Exception ex)
            {
                completion.TrySetException(new InvalidOperationException("Could not open scheduled task question dialog.", ex));
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    private static Task<bool> AskMouthTrackerPreferenceAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                var result = MessageBox.Show(
                    "Do you use Vive mouth tracker?",
                    "Pimax VRC Supervisor",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);

                completion.TrySetResult(result == DialogResult.Yes);
            }
            catch (Exception ex)
            {
                completion.TrySetException(new InvalidOperationException("Could not open mouth tracker question dialog.", ex));
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    private static async Task<ResolvedExecutablePath?> ResolveExecutablePathAsync(
        string configuredPath,
        string displayName,
        string expectedFileName,
        string? suggestedPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return new ResolvedExecutablePath(configuredPath, WasSelected: false);
        }

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            Console.WriteLine($"{displayName} path is not configured.");
        }
        else
        {
            Console.WriteLine($"{displayName} was not found at: {configuredPath}");
        }

        Console.WriteLine($"Please browse for {expectedFileName}.");

        var selectedPath = await BrowseForExecutableAsync(displayName, expectedFileName, suggestedPath, cancellationToken);
        if (selectedPath is null)
        {
            Console.WriteLine($"{displayName} was not selected. Exiting.");
            return null;
        }

        Console.WriteLine($"{displayName} selected: {selectedPath}");
        return new ResolvedExecutablePath(selectedPath, WasSelected: true);
    }

    private static void UpdateProcessNamesFromSelectedExecutable(
        string displayName,
        string executablePath,
        bool wasSelected,
        Action<string[]> updateProcessNames,
        string[] configuredProcessNames)
    {
        var selectedProcessName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(selectedProcessName))
        {
            return;
        }

        var configuredContainsSelectedName = configuredProcessNames
            .Any(name => string.Equals(Path.GetFileNameWithoutExtension(name), selectedProcessName, StringComparison.OrdinalIgnoreCase));

        if (wasSelected)
        {
            if (!configuredContainsSelectedName)
            {
                Console.WriteLine($"{displayName} selected exe name does not match configured process names. Auto-updating process name to: {selectedProcessName}");
            }

            updateProcessNames([selectedProcessName]);
        }
        else if (!configuredContainsSelectedName)
        {
            Console.WriteLine($"Warning: {displayName} exe name '{selectedProcessName}' does not match configured process names: {string.Join(", ", configuredProcessNames)}");
        }
    }

    private static Task<string?> BrowseForExecutableAsync(
        string displayName,
        string expectedFileName,
        string? suggestedPath,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var dialog = new OpenFileDialog
                {
                    Title = $"Select {expectedFileName}",
                    FileName = expectedFileName,
                    Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false
                };

                if (!string.IsNullOrWhiteSpace(suggestedPath))
                {
                    var suggestedDirectory = Path.GetDirectoryName(suggestedPath);
                    if (!string.IsNullOrWhiteSpace(suggestedDirectory) && Directory.Exists(suggestedDirectory))
                    {
                        dialog.InitialDirectory = suggestedDirectory;
                    }

                    dialog.FileName = Path.GetFileName(suggestedPath);
                }

                var result = dialog.ShowDialog();
                completion.TrySetResult(result == DialogResult.OK ? dialog.FileName : null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(new InvalidOperationException($"Could not open browse dialog for {displayName}.", ex));
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    private async Task<bool> WaitForPimaxOnStartupAsync(CancellationToken cancellationToken)
    {
        if (await IsPimaxConnectedAsync(cancellationToken))
        {
            return true;
        }

        Console.WriteLine("Waiting for the headset to connect...");

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, cancellationToken);
            if (await IsPimaxConnectedAsync(cancellationToken))
            {
                Console.WriteLine("Pimax Crystal connected.");
                return true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private WatchedProcessState ObserveWatchedShutdownProcesses()
    {
        var currentProcesses = GetProcesses(_config.WatchedShutdownProcessNames);
        if (currentProcesses.Count > 0)
        {
            _watchedProcessHasBeenSeen = true;
            _watchedProcessMissingSince = null;

            if (_waitingForWatchedProcessRelaunch)
            {
                Console.WriteLine("VRChat relaunched after a likely crash. Continuing supervision.");
                _waitingForWatchedProcessRelaunch = false;
            }

            var currentIds = currentProcesses.Select(process => process.Id).ToHashSet();
            foreach (var staleProcessId in _watchedProcessHandles.Keys.Where(id => !currentIds.Contains(id)).ToArray())
            {
                _watchedProcessHandles[staleProcessId].Dispose();
                _watchedProcessHandles.Remove(staleProcessId);
            }

            foreach (var process in currentProcesses)
            {
                if (_watchedProcessHandles.ContainsKey(process.Id))
                {
                    process.Dispose();
                    continue;
                }

                _watchedProcessHandles[process.Id] = process;
            }

            return WatchedProcessState.Running;
        }

        if (!_watchedProcessHasBeenSeen)
        {
            return WatchedProcessState.NotSeenYet;
        }

        currentProcesses.ForEach(process => process.Dispose());

        if (_waitingForWatchedProcessRelaunch)
        {
            var elapsed = DateTimeOffset.UtcNow - (_watchedProcessMissingSince ?? DateTimeOffset.UtcNow);
            return elapsed.TotalSeconds >= _config.WatchedProcessCrashRelaunchGraceSeconds
                ? WatchedProcessState.CrashGraceExpired
                : WatchedProcessState.WaitingAfterCrash;
        }

        var crashed = DidAnyWatchedProcessExitAbnormally();
        ClearWatchedProcessHandles();

        if (!crashed)
        {
            return WatchedProcessState.NormalExit;
        }

        _waitingForWatchedProcessRelaunch = true;
        _watchedProcessMissingSince = DateTimeOffset.UtcNow;
        Console.WriteLine($"VRChat appears to have crashed. Waiting {_config.WatchedProcessCrashRelaunchGraceSeconds} seconds for it to relaunch.");
        return WatchedProcessState.WaitingAfterCrash;
    }

    private bool DidAnyWatchedProcessExitAbnormally()
    {
        var sawExitCode = false;
        var sawAbnormalExit = false;

        foreach (var process in _watchedProcessHandles.Values)
        {
            try
            {
                process.Refresh();
                if (!process.HasExited)
                {
                    continue;
                }

                sawExitCode = true;
                if (process.ExitCode != 0)
                {
                    sawAbnormalExit = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read watched process exit code: {ex.Message}");
            }
        }

        return sawExitCode && sawAbnormalExit;
    }

    private void ClearWatchedProcessHandles()
    {
        foreach (var process in _watchedProcessHandles.Values)
        {
            process.Dispose();
        }

        _watchedProcessHandles.Clear();
    }

    private async Task StartManagedAppsAsync(CancellationToken cancellationToken)
    {
        _managedAppsStarted = true;
        await StartBrokenEyeWithRetriesAsync(cancellationToken);
        Console.WriteLine($"Waiting {_config.DelayBeforeVrcFaceTrackingSeconds} seconds before starting VRCFaceTracking...");
        await DelayWithCancellationAsync(TimeSpan.FromSeconds(_config.DelayBeforeVrcFaceTrackingSeconds), cancellationToken);
        await VerifyRunningAsync("Broken Eye", _config.BrokenEyeProcessNames, cancellationToken, requiredStableSeconds: 0);

        Console.WriteLine("Starting VRCFaceTracking...");
        StartOrAttach(_config.VrcFaceTrackingPath, _config.VrcFaceTrackingProcessNames);
        await VerifyRunningAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
    }

    private async Task StartBrokenEyeWithRetriesAsync(CancellationToken cancellationToken)
    {
        if (IsAnyProcessRunning(_config.BrokenEyeProcessNames))
        {
            Console.WriteLine($"Broken Eye is already running: {string.Join(", ", _config.BrokenEyeProcessNames)}");
            return;
        }

        for (var attempt = 1; attempt <= BrokenEyeStartupMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"Starting Broken Eye (attempt {attempt}/{BrokenEyeStartupMaxAttempts})...");

            try
            {
                StartProcess(_config.BrokenEyePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not start Broken Eye on attempt {attempt}: {ex.Message}");
            }

            Console.WriteLine($"Checking whether Broken Eye is running in {BrokenEyeStartupCheckDelay.TotalSeconds:0} seconds...");
            await DelayWithCancellationAsync(BrokenEyeStartupCheckDelay, cancellationToken);

            if (IsAnyProcessRunning(_config.BrokenEyeProcessNames))
            {
                Console.WriteLine($"Broken Eye is running after attempt {attempt}.");
                return;
            }

            if (attempt < BrokenEyeStartupMaxAttempts)
            {
                Console.WriteLine("Broken Eye is not running. Trying again...");
            }
        }

        throw new TimeoutException($"Broken Eye did not start after {BrokenEyeStartupMaxAttempts} attempts.");
    }

    private async Task StopManagedAppsAsync(CancellationToken cancellationToken)
    {
        await StopProcessesAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
        await StopProcessesAsync("Broken Eye", _config.BrokenEyeProcessNames, cancellationToken);
    }

    private async Task TryStopManagedAppsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StopManagedAppsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not complete managed app cleanup: {ex.Message}");
        }
    }

    private async Task RestartVrcFaceTrackingAsync(CancellationToken cancellationToken)
    {
        await StopProcessesAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
        Console.WriteLine("Starting VRCFaceTracking...");
        StartOrAttach(_config.VrcFaceTrackingPath, _config.VrcFaceTrackingProcessNames);
        await VerifyRunningAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
    }

    private void StartOrAttach(string path, string[] processNames)
    {
        if (IsAnyProcessRunning(processNames))
        {
            Console.WriteLine($"Already running: {string.Join(", ", processNames)}");
            return;
        }

        StartProcess(path);
    }

    private static void StartProcess(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    private async Task VerifyRunningAsync(
        string displayName,
        string[] processNames,
        CancellationToken cancellationToken,
        int? requiredStableSeconds = null)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_config.StartupTimeoutSeconds);
        var stableSeconds = requiredStableSeconds ?? _config.StartupStableSeconds;
        var stableUntil = DateTimeOffset.UtcNow.AddSeconds(stableSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsAnyProcessRunning(processNames))
            {
                if (stableSeconds == 0 || DateTimeOffset.UtcNow >= stableUntil)
                {
                    Console.WriteLine($"{displayName} is running.");
                    return;
                }
            }
            else
            {
                stableUntil = DateTimeOffset.UtcNow.AddSeconds(stableSeconds);
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"{displayName} did not stay running within {_config.StartupTimeoutSeconds} seconds.");
    }

    private async Task StopProcessesAsync(string displayName, string[] processNames, CancellationToken cancellationToken)
    {
        var processes = GetProcesses(processNames);
        if (processes.Count == 0)
        {
            Console.WriteLine($"{displayName} is already closed.");
            return;
        }

        Console.WriteLine($"Closing {displayName}...");

        foreach (var process in processes)
        {
            using (process)
            {
                TryCloseMainWindow(process);
            }
        }

        var gracefulDeadline = DateTimeOffset.UtcNow.AddSeconds(_config.ShutdownGraceSeconds);
        while (DateTimeOffset.UtcNow < gracefulDeadline && IsAnyProcessRunning(processNames))
        {
            await Task.Delay(500, cancellationToken);
        }

        processes = GetProcesses(processNames);
        foreach (var process in processes)
        {
            using (process)
            {
                TryKill(process);
            }
        }

        var forceDeadline = DateTimeOffset.UtcNow.AddSeconds(_config.ShutdownGraceSeconds);
        while (DateTimeOffset.UtcNow < forceDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAnyProcessRunning(processNames))
            {
                Console.WriteLine($"{displayName} is closed.");
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"{displayName} did not close cleanly.");
    }

    private async Task<bool> IsPimaxConnectedAsync(CancellationToken cancellationToken)
        => await IsDeviceConnectedAsync(_config.PimaxDetectors, cancellationToken);

    private async Task<bool> IsMouthTrackerConnectedAsync(CancellationToken cancellationToken)
        => await IsDeviceConnectedAsync(_config.MouthTrackerDetectors, cancellationToken);

    private async Task<bool> IsDeviceConnectedAsync(string[][] detectorGroups, CancellationToken cancellationToken)
    {
        var output = await RunProcessForOutputAsync(
            "pnputil.exe",
            "/enum-devices /connected",
            TimeSpan.FromSeconds(_config.DeviceProbeTimeoutSeconds),
            cancellationToken);

        var deviceBlocks = SplitDeviceBlocks(output);
        return detectorGroups
            .Where(group => group.Length > 0)
            .Any(group => deviceBlocks.Any(block => DetectorGroupMatchesBlock(group, block)));
    }

    private static string[] SplitDeviceBlocks(string pnputilOutput)
    {
        return Regex
            .Split(pnputilOutput.Trim(), @"(?:\r?\n){2,}")
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Select(block => block.ToLowerInvariant())
            .ToArray();
    }

    private static bool DetectorGroupMatchesBlock(string[] detectorGroup, string normalizedDeviceBlock)
    {
        var keywords = detectorGroup.Where(keyword => !string.IsNullOrWhiteSpace(keyword)).ToArray();
        return keywords.Length > 0
            && keywords.All(keyword => normalizedDeviceBlock.Contains(keyword.ToLowerInvariant()));
    }

    private static async Task<string> RunProcessForOutputAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        await process.WaitForExitAsync(timeoutSource.Token);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}: {error}");
        }

        return output;
    }

    private static bool IsAnyProcessRunning(string[] processNames) => GetProcesses(processNames).Count > 0;

    private static List<Process> GetProcesses(string[] processNames)
    {
        var result = new List<Process>();
        foreach (var processName in processNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            result.AddRange(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)));
        }

        return result;
    }

    private static void TryCloseMainWindow(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not close process {process.ProcessName} gracefully: {ex.Message}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                Console.WriteLine($"Force closing {process.ProcessName} ({process.Id}).");
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not force close process {process.ProcessName}: {ex.Message}");
        }
    }

    private static void ValidateExecutable(string path, string displayName)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{displayName} executable was not found.", path);
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static Task DelayWithCancellationAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);

    private static string DescribeConnection(bool connected) => connected ? "connected" : "not connected";
}

internal static class AutoLaunchWatcher
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const string WatcherMutexName = @"Local\PimaxVrcSupervisorAutoLaunchWatcher";
    private const string VrServerProcessName = "vrserver";
    private const string VrChatProcessName = "VRChat";

    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var mutex = new Mutex(initiallyOwned: true, WatcherMutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            return;
        }

        var supervisorPath = ScheduledTaskInstaller.GetSupervisorExecutablePath();
        var supervisorProcessName = Path.GetFileNameWithoutExtension(supervisorPath);
        var launchedForCurrentVrChatSession = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var vrChatRunning = IsProcessRunning(VrChatProcessName);
            var vrServerRunning = IsProcessRunning(VrServerProcessName);

            if (!vrChatRunning)
            {
                launchedForCurrentVrChatSession = false;
            }
            else if (vrServerRunning && !launchedForCurrentVrChatSession && !IsAnotherSupervisorRunning(supervisorProcessName))
            {
                StartSupervisor(supervisorPath);
                launchedForCurrentVrChatSession = true;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static bool IsAnotherSupervisorRunning(string supervisorProcessName)
    {
        var currentProcessId = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(supervisorProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.Id != currentProcessId && !process.HasExited)
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    private static void StartSupervisor(string supervisorPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = supervisorPath,
            WorkingDirectory = Path.GetDirectoryName(supervisorPath) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }
}

internal sealed record ScheduledTaskDetails(string TaskName, string TriggerDescription);

internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal static class ScheduledTaskInstaller
{
    private const string TaskName = "Pimax VRC Supervisor Auto Launch";
    private const string WatcherArgument = "--watch-vrchat-auto-launch";

    public static async Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(
            "schtasks.exe",
            ["/Query", "/TN", TaskName],
            cancellationToken);

        return result.ExitCode == 0;
    }

    public static async Task<ScheduledTaskDetails> CreateOrUpdateAsync(CancellationToken cancellationToken)
    {
        var supervisorPath = GetSupervisorExecutablePath();
        var supervisorWorkingDirectory = Path.GetDirectoryName(supervisorPath) ?? AppContext.BaseDirectory;
        var taskXml = BuildTaskXml(supervisorPath, supervisorWorkingDirectory);
        var taskXmlPath = Path.Combine(Path.GetTempPath(), $"PimaxVrcSupervisorAutoLaunch-{Guid.NewGuid():N}.xml");

        try
        {
            await File.WriteAllTextAsync(taskXmlPath, taskXml, Encoding.Unicode, cancellationToken);
            await RunProcessAsync(
                "schtasks.exe",
                ["/Create", "/TN", TaskName, "/XML", taskXmlPath, "/F"],
                cancellationToken);
        }
        finally
        {
            TryDeleteFile(taskXmlPath);
        }

        if (!await ExistsAsync(cancellationToken))
        {
            throw new InvalidOperationException("schtasks.exe reported success, but the task could not be queried afterward.");
        }

        await RunProcessAsync(
            "schtasks.exe",
            ["/Run", "/TN", TaskName],
            cancellationToken);

        return new ScheduledTaskDetails(TaskName, "Hidden elevated watcher at Windows sign-in; launches supervisor when VRChat.exe and vrserver.exe are running.");
    }

    private static string BuildTaskXml(
        string supervisorPath,
        string supervisorWorkingDirectory)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var identity = WindowsIdentity.GetCurrent();
        var command = BuildPowerShellCommand(supervisorPath, supervisorWorkingDirectory);

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Runs an elevated hidden watcher that starts Pimax VRC Supervisor when VRChat starts while vrserver.exe is running.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", identity.User?.Value ?? throw new InvalidOperationException("Could not resolve the current Windows user SID.")),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "IdleSettings",
                        new XElement(ns + "StopOnIdleEnd", "false"),
                        new XElement(ns + "RestartOnIdle", "false")),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "true"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "WakeToRun", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", "7")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", "powershell.exe"),
                        new XElement(ns + "Arguments", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command {QuotePowerShellArgument(command)}")))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildPowerShellCommand(string supervisorPath, string workingDirectory)
    {
        var supervisorPathLiteral = ToPowerShellSingleQuotedString(supervisorPath);
        var workingDirectoryLiteral = ToPowerShellSingleQuotedString(workingDirectory);
        var watcherArgumentLiteral = ToPowerShellSingleQuotedString(WatcherArgument);

        return $"Start-Process -WindowStyle Hidden -FilePath {supervisorPathLiteral} -ArgumentList {watcherArgumentLiteral} -WorkingDirectory {workingDirectoryLiteral}";
    }

    public static string GetSupervisorExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            var appHostPath = Path.ChangeExtension(assemblyPath, ".exe");
            if (File.Exists(appHostPath))
            {
                return appHostPath;
            }
        }

        var fallbackPath = Path.Combine(AppContext.BaseDirectory, "PimaxVrcSupervisor.exe");
        if (File.Exists(fallbackPath))
        {
            return fallbackPath;
        }

        throw new InvalidOperationException("Could not resolve PimaxVrcSupervisor.exe for the scheduled task action.");
    }

    private static async Task RunProcessAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(fileName, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {result.ExitCode}: {result.Error}{result.Output}");
        }
    }

    private static async Task<ProcessResult> RunProcessCaptureAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        return new ProcessResult(process.ExitCode, output, error);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary XML cleanup failure is harmless.
        }
    }

    private static string QuotePowerShellArgument(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string ToPowerShellSingleQuotedString(string value) => "'" + value.Replace("'", "''") + "'";
}

internal sealed class SupervisorConfig
{
    public string BrokenEyePath { get; set; } = "";
    public string VrcFaceTrackingPath { get; set; } = "";
    public const string DefaultVrcFaceTrackingPath = @"C:\Program Files (x86)\Steam\steamapps\common\VRCFaceTracking\VRCFaceTracking.exe";
    public string[] BrokenEyeProcessNames { get; set; } = ["Broken Eye"];
    public string[] VrcFaceTrackingProcessNames { get; set; } = ["VRCFaceTracking"];
    public string[] WatchedShutdownProcessNames { get; init; } = ["VRChat"];
    public JsonElement MouthTrackerUser { get; set; }
    public JsonElement AutoLaunchScheduledTask { get; set; }
    public string[][] PimaxDetectors { get; init; } =
    [
        ["USB\\VID_2104&PID_0220"],
        ["Pimax", "Crystal"],
        ["EyeChip"]
    ];
    public string[][] MouthTrackerDetectors { get; init; } =
    [
        ["USB\\VID_0BB4&PID_0321&MI_00"],
        ["HTC Multimedia Camera"],
        ["VIVE", "Camera"]
    ];
    public int PollIntervalSeconds { get; init; } = 5;
    public int StartupTimeoutSeconds { get; init; } = 30;
    public int StartupStableSeconds { get; init; } = 5;
    public int DelayBeforeVrcFaceTrackingSeconds { get; init; } = 5;
    public int RestartDelayAfterReconnectSeconds { get; init; } = 10;
    public int WatchedProcessCrashRelaunchGraceSeconds { get; init; } = 300;
    public int ShutdownGraceSeconds { get; init; } = 8;
    public int DeviceProbeTimeoutSeconds { get; init; } = 10;

    [JsonIgnore]
    public string? LoadedFromPath { get; private set; }

    public static SupervisorConfig Load()
    {
        var configPath = FindConfigPath();
        if (configPath is null)
        {
            return new SupervisorConfig();
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<SupervisorConfig>(json, JsonOptions()) ?? new SupervisorConfig();
        config.LoadedFromPath = configPath;
        return config;
    }

    public void SaveExecutableSettings()
    {
        var configPath = LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{\n}";

        json = ReplaceJsonStringProperty(json, nameof(BrokenEyePath), BrokenEyePath);
        json = ReplaceJsonStringProperty(json, nameof(VrcFaceTrackingPath), VrcFaceTrackingPath);
        json = ReplaceJsonStringArrayProperty(json, nameof(BrokenEyeProcessNames), BrokenEyeProcessNames);
        json = ReplaceJsonStringArrayProperty(json, nameof(VrcFaceTrackingProcessNames), VrcFaceTrackingProcessNames);

        File.WriteAllText(configPath, json);
        Console.WriteLine($"Saved selected executable settings to: {configPath}");
    }

    public bool TryGetMouthTrackerUser(out bool mouthTrackerUser)
    {
        if (MouthTrackerUser.ValueKind == JsonValueKind.True)
        {
            mouthTrackerUser = true;
            return true;
        }

        if (MouthTrackerUser.ValueKind == JsonValueKind.False)
        {
            mouthTrackerUser = false;
            return true;
        }

        mouthTrackerUser = false;
        return false;
    }

    public void SetMouthTrackerUser(bool mouthTrackerUser)
    {
        MouthTrackerUser = JsonSerializer.SerializeToElement(mouthTrackerUser);
    }

    public void SaveMouthTrackerPreference()
    {
        var configPath = LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{\n}";

        json = ReplaceJsonBooleanOrStringProperty(json, nameof(MouthTrackerUser), TryGetMouthTrackerUser(out var value) && value);

        File.WriteAllText(configPath, json);
        Console.WriteLine($"Saved mouth tracker preference to: {configPath}");
    }

    public bool TryGetAutoLaunchScheduledTask(out bool autoLaunchScheduledTask)
    {
        if (AutoLaunchScheduledTask.ValueKind == JsonValueKind.True)
        {
            autoLaunchScheduledTask = true;
            return true;
        }

        if (AutoLaunchScheduledTask.ValueKind == JsonValueKind.False)
        {
            autoLaunchScheduledTask = false;
            return true;
        }

        autoLaunchScheduledTask = false;
        return false;
    }

    public void SetAutoLaunchScheduledTask(bool autoLaunchScheduledTask)
    {
        AutoLaunchScheduledTask = JsonSerializer.SerializeToElement(autoLaunchScheduledTask);
    }

    public void SaveAutoLaunchScheduledTaskPreference()
    {
        var configPath = LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{\n}";

        json = ReplaceJsonBooleanOrStringProperty(json, nameof(AutoLaunchScheduledTask), TryGetAutoLaunchScheduledTask(out var value) && value);

        File.WriteAllText(configPath, json);
        Console.WriteLine($"Saved scheduled task preference to: {configPath}");
    }

    private static string ReplaceJsonStringProperty(string json, string propertyName, string value)
    {
        var escapedValue = JsonSerializer.Serialize(value);
        var pattern = $"(\"{Regex.Escape(propertyName)}\"\\s*:\\s*)\"(?:\\\\.|[^\"])*\"";

        if (Regex.IsMatch(json, pattern))
        {
            return Regex.Replace(json, pattern, match => match.Groups[1].Value + escapedValue, RegexOptions.Multiline);
        }

        var insertion = $"  \"{propertyName}\": {escapedValue},\n";
        var openBrace = json.IndexOf('{');
        if (openBrace >= 0)
        {
            return json.Insert(openBrace + 1, Environment.NewLine + insertion);
        }

        return $"{{\n{insertion}}}\n";
    }

    private static string ReplaceJsonStringArrayProperty(string json, string propertyName, string[] values)
    {
        var escapedValue = JsonSerializer.Serialize(values, JsonOptions());
        var pattern = $"(\"{Regex.Escape(propertyName)}\"\\s*:\\s*)\\[(?:.|\\r|\\n)*?\\]";

        if (Regex.IsMatch(json, pattern))
        {
            return Regex.Replace(json, pattern, match => match.Groups[1].Value + escapedValue, RegexOptions.Multiline);
        }

        var insertion = $"  \"{propertyName}\": {escapedValue},\n";
        var openBrace = json.IndexOf('{');
        if (openBrace >= 0)
        {
            return json.Insert(openBrace + 1, Environment.NewLine + insertion);
        }

        return $"{{\n{insertion}}}\n";
    }

    private static string ReplaceJsonBooleanOrStringProperty(string json, string propertyName, bool value)
    {
        var escapedValue = value ? "true" : "false";
        var pattern = $"(\"{Regex.Escape(propertyName)}\"\\s*:\\s*)(?:true|false|null|\"(?:\\\\.|[^\"])*\"|[^,\\r\\n}}]+)";

        if (Regex.IsMatch(json, pattern, RegexOptions.IgnoreCase))
        {
            return Regex.Replace(
                json,
                pattern,
                match => match.Groups[1].Value + escapedValue,
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }

        var insertion = $"  \"{propertyName}\": {escapedValue},\n";
        var openBrace = json.IndexOf('{');
        if (openBrace >= 0)
        {
            return json.Insert(openBrace + 1, Environment.NewLine + insertion);
        }

        return $"{{\n{insertion}}}\n";
    }

    private static string? FindConfigPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "supervisor.config.json"),
            Path.Combine(Environment.CurrentDirectory, "supervisor.config.json")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
