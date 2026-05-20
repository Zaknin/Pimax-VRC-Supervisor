using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Win32;
using PimaxVrcSupervisor.BaseStations;
using Windows.Devices.Bluetooth.Advertisement;

using var shutdown = new CancellationTokenSource();

var commandLineArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (TryGetCommandOption(commandLineArgs, "--emergency-base-station-cleanup", out var emergencyConfigPath))
{
    var initialDelaySeconds = TryGetCommandOption(commandLineArgs, "--delay-seconds", out var delayText)
        && int.TryParse(delayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDelay)
        ? Math.Max(0, parsedDelay)
        : 0;
    var emergencyConfig = SupervisorConfig.Load(emergencyConfigPath);
    await BaseStationEmergencyCleanup.RunAsync(emergencyConfig, TimeSpan.FromSeconds(initialDelaySeconds), CancellationToken.None);
    return;
}

var config = SupervisorConfig.Load();
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

using var supervisorMutex = new Mutex(initiallyOwned: true, @"Local\PimaxVrcSupervisor", out var ownsSupervisorMutex);
if (!ownsSupervisorMutex)
{
    Console.WriteLine("Pimax VRC Supervisor is already running. Exiting this duplicate instance.");
    return;
}

var supervisor = new AppSupervisor(config);
var supervisorStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    RunBlockingEmergencyShutdown("Ctrl+C requested. Restoring monitors and closing managed apps.", shutdown, supervisorStopped.Task, supervisor.RunEmergencyCloseCleanupAsync);
};
using var consoleCloseHandler = ConsoleCloseHandler.Register(
    shutdown,
    supervisorStopped.Task,
    supervisor.RunEmergencyCloseCleanupAsync,
    () => BaseStationEmergencyCleanup.TryLaunchDetached(config, TimeSpan.FromSeconds(6)));
try
{
    await supervisor.RunAsync(shutdown.Token);
}
finally
{
    supervisorStopped.TrySetResult();
}

static void RunBlockingEmergencyShutdown(
    string message,
    CancellationTokenSource shutdown,
    Task supervisorStopped,
    Func<Task> emergencyCleanupAsync)
{
    try
    {
        Console.WriteLine(message);
        emergencyCleanupAsync().GetAwaiter().GetResult();
        shutdown.Cancel();
        supervisorStopped.Wait(TimeSpan.FromSeconds(60));
    }
    catch
    {
        // Console shutdown has a short OS-managed timeout; cleanup is best-effort.
    }
}

static bool TryGetCommandOption(string[] args, string name, out string? value)
{
    value = null;
    for (var index = 0; index < args.Length; index++)
    {
        if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[index + 1];
        }

        return true;
    }

    return false;
}

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

internal static class AppVersion
{
    public static string Current =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}

internal sealed record ResolvedExecutablePath(string Path, bool WasSelected);

internal sealed record ManagedAutoLaunchApp(string DisplayName, string Path, string[] ProcessNames, bool RestartOnPimaxReconnect, bool RunAsAdmin, bool StartMinimized);

internal sealed record PimaxServiceReconnect(DateTimeOffset RemoveAt, DateTimeOffset AddAt);

internal enum ManagedAppStopReason
{
    SessionEnding,
    PimaxReconnect
}

internal enum WatchedProcessState
{
    NotSeenYet,
    Running,
    NormalExit,
    WaitingAfterCrash,
    CrashGraceExpired
}

internal struct ConsoleHotkeys
{
    public bool LaunchOscGoesBrrr { get; set; }
    public bool RestartCoreApps { get; set; }
    public bool RetryOscRouter { get; set; }
}

internal sealed class AppSupervisor
{
    private const int BrokenEyeStartupMaxAttempts = 10;
    private static readonly TimeSpan BrokenEyeStartupCheckDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LovenseBluetoothRegistryRecentWindow = TimeSpan.FromHours(1);

    private readonly SupervisorConfig _config;
    private readonly BaseStationGattClient _baseStationGattClient = new();
    private readonly SteamVrTrackingReferenceReader _steamVrTrackingReferenceReader = new();
    private readonly MonitorLayoutController _monitorLayout = new();
    private readonly TimeSpan _pollInterval;
    private readonly Dictionary<int, Process> _watchedProcessHandles = new();
    private readonly SemaphoreSlim _oscGoesBrrrLaunchLock = new(1, 1);
    private readonly SemaphoreSlim _oscRouterLaunchLock = new(1, 1);
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private readonly SemaphoreSlim _coreAppRestartLock = new(1, 1);
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private bool? _lastPimaxConnected;
    private bool? _lastMouthTrackerConnected;
    private bool _mouthTrackerUser;
    private bool _turnOffSecondaryMonitors;
    private bool _watchedProcessHasBeenSeen;
    private bool _waitingForWatchedProcessRelaunch;
    private bool _managedAppsStarted;
    private bool _baseStationPowerOnAttempted;
    private bool _baseStationsPoweredOn;
    private bool _baseStationPowerOnComplete;
    private int _baseStationPowerOnPassesCompleted;
    private readonly HashSet<string> _baseStationPowerOnCommandSucceeded = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _lastBaseStationPowerOnSkippedLogAt;
    private DateTimeOffset? _nextBaseStationPowerOnAttemptAt;
    private DateTimeOffset? _baseStationSecondPowerOnPassCompletedAt;
    private bool _baseStationSettingsNeedSave;
    private bool? _steamVrTrackingReferenceStartupAvailable;
    private bool _steamVrTrackingReferenceStartupUnavailableLogged;
    private bool _cleanupStarted;
    private bool? _lastLovenseConnected;
    private bool _lovenseIntifaceStarted;
    private bool _lovenseWorkflowTriggered;
    private OscRouter? _oscRouter;
    private bool _oscRouterWaitingForRetry;
    private Task? _oscGoesBrrrBleScannerTask;
    private bool _oscGoesBrrrBleScannerWarningShown;
    private DateTimeOffset? _watchedProcessMissingSince;
    private DateTimeOffset? _lastPimaxServiceLogEventSeenAt;
    private DateTimeOffset? _pendingPimaxServiceHidRemoveAt;
    private DateTimeOffset? _lastPimaxServiceReconnectAt;
    private DateTimeOffset? _lastHandledPimaxReconnectSignalAt;
    private DateTimeOffset? _lastMouthTrackerPnPEventSeenAt;
    private bool _mouthTrackerPnPEventWarningShown;

    public AppSupervisor(SupervisorConfig config)
    {
        _config = config;
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, config.PollIntervalSeconds));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Pimax VRC Supervisor {AppVersion.Current}");
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
        _turnOffSecondaryMonitors = await EnsureTurnOffSecondaryMonitorsPreferenceAsync(cancellationToken);
        await EnsureAutoLaunchScheduledTaskPreferenceAsync(cancellationToken);

        try
        {
            _lastPimaxConnected = await WaitForPimaxOnStartupAsync(cancellationToken);

            await TryPowerOnBaseStationsForSessionAsync(1, cancellationToken);

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

            await TryStartOscRouterAsync(cancellationToken);
            await StartManagedAppsAsync(cancellationToken);
            await InitializeOscGoesBrrrWorkflowAsync(cancellationToken);
            await TryPowerOnBaseStationsForSessionAsync(BaseStationCommandTiming.PowerOnPasses, cancellationToken);
            ShowOscRouterRetryPromptIfNeeded();

            Console.WriteLine($"Pimax Crystal initial state: {DescribeConnection(_lastPimaxConnected.Value)}");
            Console.WriteLine("Waiting for Pimax reconnects or VRChat shutdown. Press Ctrl+C to stop.");

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_pollInterval, cancellationToken);
                RefreshOscGoesBrrrWorkflowState();
                await HandleConsoleHotkeysAsync(cancellationToken);
                if (_lastPimaxConnected == true)
                {
                    await TryPowerOnBaseStationsForSessionAsync(BaseStationCommandTiming.PowerOnPasses, cancellationToken);
                }

                var watchedProcessState = ObserveWatchedShutdownProcesses();
                if (watchedProcessState == WatchedProcessState.NormalExit)
                {
                    var waitForSteamVrServerExit = ShouldWaitForSteamVrServerExitBeforeCleanup();
                    Console.WriteLine(waitForSteamVrServerExit
                        ? "VRChat has shut down. Waiting for SteamVR if needed, restoring monitors, then closing managed apps."
                        : "VRChat has shut down. Closing managed apps and exiting.");
                    await RestoreMonitorsAndStopManagedAppsAsync(waitForSteamVrServerExit, cancellationToken);
                    return;
                }
                if (watchedProcessState == WatchedProcessState.CrashGraceExpired)
                {
                    var waitForSteamVrServerExit = ShouldWaitForSteamVrServerExitBeforeCleanup();
                    Console.WriteLine(waitForSteamVrServerExit
                        ? "VRChat did not relaunch after a likely crash. Waiting for SteamVR if needed, restoring monitors, then closing managed apps."
                        : "VRChat did not relaunch after a likely crash. Closing managed apps and exiting.");
                    await RestoreMonitorsAndStopManagedAppsAsync(waitForSteamVrServerExit, cancellationToken);
                    return;
                }

                var pimaxConnected = await ReadDeviceConnectedOrPreviousAsync(
                    "Pimax Crystal",
                    IsPimaxConnectedAsync,
                    _lastPimaxConnected,
                    cancellationToken);
                var mouthTrackerConnected = _mouthTrackerUser
                    ? await ReadDeviceConnectedOrPreviousAsync(
                        "mouth tracker",
                        IsMouthTrackerConnectedAsync,
                        _lastMouthTrackerConnected,
                        cancellationToken)
                    : (bool?)null;
                var lovenseConnected = _config.OscGoesBrrrEnabled
                    && !_config.OscGoesBrrrHotkeyEnabled
                    && !_config.OscGoesBrrrBleScannerEnabled
                    && !IsOscGoesBrrrWorkflowRunning()
                    ? await ReadDeviceConnectedOrPreviousAsync(
                        "Lovense",
                        IsLovenseConnectedAsync,
                        _lastLovenseConnected,
                        cancellationToken)
                    : _lastLovenseConnected;
                var pimaxServiceReconnect = pimaxConnected
                    ? DetectPimaxServiceLogReconnect()
                    : null;
                var pimaxRuntimeReconnected = pimaxServiceReconnect is not null;
                var pimaxReconnected = _lastPimaxConnected == false && pimaxConnected;
                var mouthTrackerReconnected = _mouthTrackerUser
                    && _lastMouthTrackerConnected == false
                    && mouthTrackerConnected == true;
                var mouthTrackerPnPReconnected = _mouthTrackerUser
                    && mouthTrackerConnected == true
                    && await DetectMouthTrackerPnPReconnectAsync(cancellationToken);

                if (pimaxReconnected || pimaxRuntimeReconnected)
                {
                    var reconnectSignalAt = pimaxServiceReconnect?.AddAt ?? DateTimeOffset.Now;
                    if (IsDuplicatePimaxReconnectSignal(reconnectSignalAt))
                    {
                        if (pimaxServiceReconnect is not null)
                        {
                            Console.WriteLine($"Ignoring PiService HID reconnect at {pimaxServiceReconnect.AddAt:HH:mm:ss.fff}; it matches a Pimax reconnect already handled.");
                        }

                        pimaxReconnected = false;
                        pimaxRuntimeReconnected = false;
                    }
                    else
                    {
                        _lastHandledPimaxReconnectSignalAt = reconnectSignalAt;

                        if (pimaxServiceReconnect is not null)
                        {
                            Console.WriteLine($"Pimax PiService HID remove/add sequence: {pimaxServiceReconnect.RemoveAt:HH:mm:ss.fff} -> {pimaxServiceReconnect.AddAt:HH:mm:ss.fff}");
                        }

                        if (pimaxRuntimeReconnected && !pimaxReconnected)
                        {
                            Console.WriteLine("Pimax runtime HID reconnect detected from PiService logs.");
                        }

                        var reconnectDelay = TimeSpan.FromSeconds(_config.RestartDelayAfterReconnectSeconds);
                        Console.WriteLine($"Pimax Crystal reconnected. Waiting {reconnectDelay.TotalSeconds:0} seconds for a stable connection before restarting managed apps.");
                        var stableReconnect = await WaitForPimaxStableConnectedAsync(reconnectDelay, cancellationToken);
                        if (!stableReconnect)
                        {
                            Console.WriteLine("Pimax Crystal did not stay connected during the reconnect wait. Waiting for the next reconnect.");
                            _lastPimaxConnected = false;
                            continue;
                        }

                        if (_watchedProcessHasBeenSeen && !IsAnyProcessRunning(_config.WatchedShutdownProcessNames))
                        {
                            var waitForSteamVrServerExit = ShouldWaitForSteamVrServerExitBeforeCleanup();
                            Console.WriteLine(waitForSteamVrServerExit
                                ? "VRChat shut down during reconnect delay. Waiting for SteamVR if needed, restoring monitors, then closing managed apps."
                                : "VRChat shut down during reconnect delay. Closing managed apps and exiting.");
                            await RestoreMonitorsAndStopManagedAppsAsync(waitForSteamVrServerExit, cancellationToken);
                            return;
                        }

                        await StopManagedAppsAsync(ManagedAppStopReason.PimaxReconnect, cancellationToken);
                        await StartManagedAppsAsync(cancellationToken);
                        pimaxConnected = await ReadDeviceConnectedOrPreviousAsync(
                            "Pimax Crystal",
                            IsPimaxConnectedAsync,
                            true,
                            cancellationToken);
                        mouthTrackerConnected = _mouthTrackerUser
                            ? await ReadDeviceConnectedOrPreviousAsync(
                                "mouth tracker",
                                IsMouthTrackerConnectedAsync,
                                _lastMouthTrackerConnected,
                                cancellationToken)
                            : (bool?)null;
                        Console.WriteLine($"Pimax Crystal state after restart: {DescribeConnection(pimaxConnected)}");
                    }
                }
                else if (_lastPimaxConnected != pimaxConnected)
                {
                    Console.WriteLine($"Pimax Crystal state changed: {DescribeConnection(pimaxConnected)}");
                }

                if (_mouthTrackerUser && (mouthTrackerReconnected || mouthTrackerPnPReconnected) && !pimaxReconnected && !pimaxRuntimeReconnected && pimaxConnected)
                {
                    Console.WriteLine(mouthTrackerPnPReconnected && !mouthTrackerReconnected
                        ? "Mouth tracker device event detected while Pimax Crystal stayed connected. Restarting VRCFaceTracking."
                        : "Mouth tracker reconnected while Pimax Crystal stayed connected. Restarting VRCFaceTracking.");
                    await RestartVrcFaceTrackingAsync(cancellationToken);
                }
                else if (_mouthTrackerUser && _lastMouthTrackerConnected == true && mouthTrackerConnected == false)
                {
                    Console.WriteLine("you forgot to connect mouth tracker!");
                }

                if (_config.OscGoesBrrrEnabled
                    && !_config.OscGoesBrrrHotkeyEnabled
                    && !_config.OscGoesBrrrBleScannerEnabled
                    && !IsOscGoesBrrrWorkflowRunning()
                    && _lastLovenseConnected == false
                    && lovenseConnected == true)
                {
                    Console.WriteLine("Lovense device detected. Starting OscGoesBrrr.");
                    await StartLovenseOscAsync(cancellationToken);
                }

                _lastPimaxConnected = pimaxConnected;
                if (_mouthTrackerUser)
                {
                    _lastMouthTrackerConnected = mouthTrackerConnected;
                }

                if (_config.OscGoesBrrrEnabled && !_config.OscGoesBrrrHotkeyEnabled && !_config.OscGoesBrrrBleScannerEnabled)
                {
                    _lastLovenseConnected = lovenseConnected;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Shutdown requested. Restoring monitors and closing managed apps.");
            await TryEmergencyCloseCleanupAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Unexpected supervisor error:");
            Console.WriteLine(ex);

            if (_managedAppsStarted)
            {
                Console.WriteLine("Attempting to restore monitors and close managed apps after unexpected error.");
                await TryRestoreMonitorsAndStopManagedAppsAsync(waitForSteamVrServerExit: false, CancellationToken.None);
            }
        }
        finally
        {
            StopOscRouter();
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

        if (_config.OscGoesBrrrEnabled)
        {
            var intifacePath = await ResolveExecutablePathAsync(
                _config.IntifacePath,
                "Intiface",
                "intiface_central.exe",
                SupervisorConfig.DefaultIntifacePath,
                cancellationToken);
            if (intifacePath is null)
            {
                return false;
            }

            UpdateProcessNamesFromSelectedExecutable(
                "Intiface",
                intifacePath.Path,
                intifacePath.WasSelected,
                processNames => _config.IntifaceProcessNames = processNames,
                _config.IntifaceProcessNames);

            var oscGoesBrrrrPath = await ResolveExecutablePathAsync(
                _config.OscGoesBrrrPath,
                "OscGoesBrrr",
                "OscGoesBrrr.exe",
                SupervisorConfig.DefaultOscGoesBrrrPath,
                cancellationToken);
            if (oscGoesBrrrrPath is null)
            {
                return false;
            }

            UpdateProcessNamesFromSelectedExecutable(
                "OscGoesBrrr",
                oscGoesBrrrrPath.Path,
                oscGoesBrrrrPath.WasSelected,
                processNames => _config.OscGoesBrrrProcessNames = processNames,
                _config.OscGoesBrrrProcessNames);

            pathsChanged = pathsChanged
                || !StringComparer.OrdinalIgnoreCase.Equals(_config.IntifacePath, intifacePath.Path)
                || !StringComparer.OrdinalIgnoreCase.Equals(_config.OscGoesBrrrPath, oscGoesBrrrrPath.Path)
                || intifacePath.WasSelected
                || oscGoesBrrrrPath.WasSelected;

            _config.IntifacePath = intifacePath.Path;
            _config.OscGoesBrrrPath = oscGoesBrrrrPath.Path;

            ValidateExecutable(_config.IntifacePath, "Intiface");
            ValidateExecutable(_config.OscGoesBrrrPath, "OscGoesBrrr");
        }

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

    private async Task<bool> EnsureTurnOffSecondaryMonitorsPreferenceAsync(CancellationToken cancellationToken)
    {
        if (_config.TryGetTurnOffSecondaryMonitors(out var turnOffSecondaryMonitors))
        {
            return turnOffSecondaryMonitors;
        }

        Console.WriteLine("TurnOffSecondaryMonitors is not configured.");
        var answer = await AskTurnOffSecondaryMonitorsPreferenceAsync(cancellationToken);
        _config.SetTurnOffSecondaryMonitors(answer);
        _config.SaveTurnOffSecondaryMonitorsPreference();

        Console.WriteLine(answer
            ? "Secondary monitors will be turned off during headset sessions."
            : "Secondary monitors will stay enabled during headset sessions.");

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

    private static Task<bool> AskTurnOffSecondaryMonitorsPreferenceAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                var result = MessageBox.Show(
                    "Do you want to turn off secondary monitors when using the headset?",
                    "Pimax VRC Supervisor",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);

                completion.TrySetResult(result == DialogResult.Yes);
            }
            catch (Exception ex)
            {
                completion.TrySetException(new InvalidOperationException("Could not open secondary monitor question dialog.", ex));
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
        var expandedConfiguredPath = Environment.ExpandEnvironmentVariables(configuredPath);
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(expandedConfiguredPath))
        {
            return new ResolvedExecutablePath(expandedConfiguredPath, WasSelected: false);
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

        if (wasSelected || configuredProcessNames.All(string.IsNullOrWhiteSpace))
        {
            if (!configuredContainsSelectedName)
            {
                Console.WriteLine($"{displayName} process name will be inferred from the configured exe: {selectedProcessName}");
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
        if (await ReadDeviceConnectedOrPreviousAsync("Pimax Crystal", IsPimaxConnectedAsync, previousConnected: false, cancellationToken))
        {
            return true;
        }

        Console.WriteLine("Waiting for the headset to connect...");

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, cancellationToken);
            if (await ReadDeviceConnectedOrPreviousAsync("Pimax Crystal", IsPimaxConnectedAsync, previousConnected: false, cancellationToken))
            {
                Console.WriteLine("Pimax Crystal connected.");
                return true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private async Task<bool> WaitForPimaxStableConnectedAsync(TimeSpan stableDuration, CancellationToken cancellationToken)
    {
        if (stableDuration <= TimeSpan.Zero)
        {
            return await ReadDeviceConnectedOrPreviousAsync("Pimax Crystal", IsPimaxConnectedAsync, previousConnected: true, cancellationToken);
        }

        var stableUntil = DateTimeOffset.UtcNow.Add(stableDuration);
        while (DateTimeOffset.UtcNow < stableUntil)
        {
            await Task.Delay(_pollInterval, cancellationToken);
            if (!await ReadDeviceConnectedOrPreviousAsync("Pimax Crystal", IsPimaxConnectedAsync, previousConnected: true, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> ReadDeviceConnectedOrPreviousAsync(
        string displayName,
        Func<CancellationToken, Task<bool>> readConnectedAsync,
        bool? previousConnected,
        CancellationToken cancellationToken)
    {
        try
        {
            return await readConnectedAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var fallback = previousConnected ?? false;
            Console.WriteLine($"Could not read {displayName} device state: {ex.Message} Keeping previous state: {DescribeConnection(fallback)}.");
            return fallback;
        }
    }

    private bool IsDuplicatePimaxReconnectSignal(DateTimeOffset signalAt)
    {
        if (_lastHandledPimaxReconnectSignalAt is not { } lastHandledSignalAt)
        {
            return false;
        }

        var coalesceWindow = TimeSpan.FromSeconds(Math.Max(
            30,
            _config.RestartDelayAfterReconnectSeconds + _config.PollIntervalSeconds + 5));
        return (signalAt - lastHandledSignalAt).Duration() <= coalesceWindow;
    }

    private PimaxServiceReconnect? DetectPimaxServiceLogReconnect()
    {
        if (!_config.UsePimaxServiceLogReconnectDetector)
        {
            return null;
        }

        var logFile = GetNewestPimaxServiceLogFile();
        if (logFile is null)
        {
            return null;
        }

        try
        {
            foreach (var entry in ReadRecentPimaxServiceLogEvents(logFile))
            {
                if (entry.Timestamp <= _startedAt || entry.Timestamp <= (_lastPimaxServiceLogEventSeenAt ?? DateTimeOffset.MinValue))
                {
                    continue;
                }

                _lastPimaxServiceLogEventSeenAt = entry.Timestamp;
                if (entry.IsRemove)
                {
                    _pendingPimaxServiceHidRemoveAt = entry.Timestamp;
                    continue;
                }

                if (entry.IsAdd && _pendingPimaxServiceHidRemoveAt is { } removeAt && entry.Timestamp >= removeAt)
                {
                    _pendingPimaxServiceHidRemoveAt = null;
                    if (_lastPimaxServiceReconnectAt == entry.Timestamp)
                    {
                        continue;
                    }

                    _lastPimaxServiceReconnectAt = entry.Timestamp;
                    return new PimaxServiceReconnect(removeAt, entry.Timestamp);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not scan Pimax PiService log for reconnects: {ex.Message}");
        }

        return null;
    }

    private async Task<bool> DetectMouthTrackerPnPReconnectAsync(CancellationToken cancellationToken)
    {
        if (!_config.UseMouthTrackerPnPReconnectDetector)
        {
            return false;
        }

        try
        {
            var output = await RunProcessForOutputAsync(
                "wevtutil.exe",
                "qe System /rd:true /c:120 /f:text /q:\"*[System[Provider[@Name='Microsoft-Windows-Kernel-PnP']]]\"",
                TimeSpan.FromSeconds(_config.DeviceProbeTimeoutSeconds),
                cancellationToken);

            var detectedReconnect = false;
            foreach (var eventBlock in SplitWindowsEventLogBlocks(output).Reverse())
            {
                if (!TryParseWindowsEventLogTimestamp(eventBlock, out var timestamp)
                    || timestamp <= _startedAt
                    || timestamp <= (_lastMouthTrackerPnPEventSeenAt ?? DateTimeOffset.MinValue)
                    || !DetectorGroupMatchesBlockAny(_config.MouthTrackerDetectors, eventBlock.ToLowerInvariant()))
                {
                    continue;
                }

                _lastMouthTrackerPnPEventSeenAt = timestamp;
                detectedReconnect = true;
            }

            return detectedReconnect;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (!_mouthTrackerPnPEventWarningShown)
            {
                Console.WriteLine($"Could not scan Windows PnP events for mouth tracker reconnects: {ex.Message}");
                _mouthTrackerPnPEventWarningShown = true;
            }

            return false;
        }
    }

    private static string[] SplitWindowsEventLogBlocks(string output)
    {
        return Regex
            .Split(output.Trim(), @"(?m)(?=^Event\[\d+\]\s*:?\s*$)")
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToArray();
    }

    private static bool TryParseWindowsEventLogTimestamp(string eventBlock, out DateTimeOffset timestamp)
    {
        timestamp = default;
        var match = Regex.Match(eventBlock, @"(?m)^\s*Date:\s*(.+?)\s*$");
        return match.Success
            && DateTimeOffset.TryParse(
                match.Groups[1].Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out timestamp);
    }

    private string? GetNewestPimaxServiceLogFile()
    {
        var directory = Environment.ExpandEnvironmentVariables(_config.PimaxServiceLogDirectory);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(directory, "PiService__*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private IEnumerable<PimaxServiceLogEvent> ReadRecentPimaxServiceLogEvents(string logFile)
    {
        using var stream = new FileStream(
            logFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = ReadAllLines(reader)
            .TakeLast(Math.Max(50, _config.PimaxServiceLogReconnectLookbackLines));

        foreach (var line in lines)
        {
            var isRemove = line.Contains("removed hid device", StringComparison.OrdinalIgnoreCase);
            var isAdd = line.Contains("added hid device", StringComparison.OrdinalIgnoreCase);
            if ((!isRemove && !isAdd) || !TryParsePimaxServiceTimestamp(line, out var timestamp))
            {
                continue;
            }

            yield return new PimaxServiceLogEvent(timestamp, isRemove, isAdd);
        }
    }

    private static IEnumerable<string> ReadAllLines(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static bool TryParsePimaxServiceTimestamp(string line, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (line.Length < 23)
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
            line[..23],
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out timestamp);
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
        PrepareMonitorLayoutForVrSession();
        await StartCoreAppsAsync(cancellationToken);
        await StartAutoLaunchAppsAsync(cancellationToken);
    }

    private async Task StartCoreAppsAsync(CancellationToken cancellationToken)
    {
        await StartBrokenEyeWithRetriesAsync(cancellationToken);
        Console.WriteLine($"Waiting {_config.DelayBeforeVrcFaceTrackingSeconds} seconds before starting VRCFaceTracking...");
        await DelayWithCancellationAsync(TimeSpan.FromSeconds(_config.DelayBeforeVrcFaceTrackingSeconds), cancellationToken);
        await VerifyRunningAsync("Broken Eye", _config.BrokenEyeProcessNames, cancellationToken, requiredStableSeconds: 0);

        Console.WriteLine("Starting VRCFaceTracking...");
        var vrcFaceTrackingStarted = StartOrAttach(
            _config.VrcFaceTrackingPath,
            _config.VrcFaceTrackingProcessNames,
            startMinimized: _config.VrcFaceTrackingStartMinimized);
        await VerifyRunningAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
        if (vrcFaceTrackingStarted && _config.VrcFaceTrackingStartMinimized)
        {
            await MinimizeProcessWindowsAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
        }
    }

    private void PrepareMonitorLayoutForVrSession()
    {
        if (!_turnOffSecondaryMonitors)
        {
            return;
        }

        try
        {
            _monitorLayout.KeepPrimaryMonitorOnly();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not switch to primary monitor only before launching Broken Eye: {ex.Message}");
        }
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
            var startedOnThisAttempt = false;

            try
            {
                StartProcess(_config.BrokenEyePath, _config.BrokenEyeStartMinimized);
                startedOnThisAttempt = true;
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
                if (startedOnThisAttempt && _config.BrokenEyeStartMinimized)
                {
                    await MinimizeProcessWindowsAsync("Broken Eye", _config.BrokenEyeProcessNames, cancellationToken);
                }

                return;
            }

            if (attempt < BrokenEyeStartupMaxAttempts)
            {
                Console.WriteLine("Broken Eye is not running. Trying again...");
            }
        }

        throw new TimeoutException($"Broken Eye did not start after {BrokenEyeStartupMaxAttempts} attempts.");
    }

    private async Task StopManagedAppsAsync(ManagedAppStopReason reason, CancellationToken cancellationToken)
    {
        foreach (var app in GetEnabledAutoLaunchApps().Reverse())
        {
            if (reason == ManagedAppStopReason.PimaxReconnect && !app.RestartOnPimaxReconnect)
            {
                Console.WriteLine($"{app.DisplayName} is configured to stay running during Pimax reconnect cleanup.");
                continue;
            }

            await StopProcessesAsync(app.DisplayName, app.ProcessNames, cancellationToken);
        }

        await StopProcessesAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
        await StopProcessesAsync("Broken Eye", _config.BrokenEyeProcessNames, cancellationToken);
    }

    private async Task RestartCoreAppsAsync(CancellationToken cancellationToken)
    {
        if (!await _coreAppRestartLock.WaitAsync(0, cancellationToken))
        {
            Console.WriteLine("Core app restart is already in progress.");
            return;
        }

        try
        {
            Console.WriteLine("Restarting VRCFaceTracking and Broken Eye...");
            await StopProcessesAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
            await StopProcessesAsync("Broken Eye", _config.BrokenEyeProcessNames, cancellationToken);
            await StartCoreAppsAsync(cancellationToken);
            Console.WriteLine("Core app restart complete.");
        }
        finally
        {
            _coreAppRestartLock.Release();
        }
    }

    private async Task TryPowerOnBaseStationsForSessionAsync(int targetPowerOnPasses, CancellationToken cancellationToken)
    {
        if (_baseStationPowerOnComplete)
        {
            return;
        }

        var baseStations = GetEnabledBaseStations();
        if (!_config.BaseStationsEnabled || baseStations.Length == 0)
        {
            return;
        }

        if (!IsAnyProcessRunning(_config.SteamVrServerProcessNames))
        {
            if (_lastBaseStationPowerOnSkippedLogAt is null || DateTimeOffset.UtcNow - _lastBaseStationPowerOnSkippedLogAt.Value > TimeSpan.FromSeconds(30))
            {
                Console.WriteLine("Base station power-on waiting for SteamVR server.");
                _lastBaseStationPowerOnSkippedLogAt = DateTimeOffset.UtcNow;
            }

            return;
        }

        if (_nextBaseStationPowerOnAttemptAt is { } nextAttempt && DateTimeOffset.UtcNow < nextAttempt)
        {
            return;
        }

        var useSteamVrTrackingConfirmation = CanUseSteamVrTrackingConfirmationForStartup();
        if (useSteamVrTrackingConfirmation && targetPowerOnPasses >= BaseStationCommandTiming.PowerOnPasses)
        {
            targetPowerOnPasses = BaseStationCommandTiming.OpenVrPowerOnCycles;
        }

        var maximumPowerOnPasses = useSteamVrTrackingConfirmation
            ? BaseStationCommandTiming.OpenVrPowerOnCycles
            : BaseStationCommandTiming.PowerOnPasses;
        targetPowerOnPasses = Math.Clamp(targetPowerOnPasses, 1, maximumPowerOnPasses);
        var initialStates = await ReadBaseStationPowerStatesAsync(baseStations, cancellationToken);
        var alreadyAwake = initialStates.Select(IsAwakeBaseStationState).ToArray();
        if (alreadyAwake.Any(value => value))
        {
            _baseStationPowerOnAttempted = true;
            _baseStationsPoweredOn = true;
        }

        var baseStationsToPowerOn = baseStations
            .Where((_, index) => !alreadyAwake[index])
            .ToArray();
        if (baseStationsToPowerOn.Length == 0)
        {
            Console.WriteLine("All enabled base stations already report awake.");
            _baseStationPowerOnComplete = true;
            _nextBaseStationPowerOnAttemptAt = null;
            return;
        }

        _baseStationPowerOnAttempted = true;
        var passesToRun = Math.Max(0, targetPowerOnPasses - _baseStationPowerOnPassesCompleted);
        for (var passOffset = 0; passOffset < passesToRun; passOffset++)
        {
            var pass = _baseStationPowerOnPassesCompleted + 1;
            var passBaseStations = !useSteamVrTrackingConfirmation && pass >= 3
                ? baseStationsToPowerOn.Where(baseStation => baseStation.RequiresExtendedPowerOnPasses).ToArray()
                : baseStationsToPowerOn;
            if (!useSteamVrTrackingConfirmation && pass == 3 && passBaseStations.Length > 0)
            {
                var thirdPassAt = (_baseStationSecondPowerOnPassCompletedAt ?? DateTimeOffset.UtcNow).Add(BaseStationCommandTiming.PowerOnRetryPassDelay);
                if (DateTimeOffset.UtcNow < thirdPassAt)
                {
                    _nextBaseStationPowerOnAttemptAt = thirdPassAt;
                    return;
                }
            }

            if (passBaseStations.Length == 0)
            {
                _baseStationPowerOnPassesCompleted = pass;
                continue;
            }

            var passSucceeded = await SendBaseStationPowerOnPassAsync(passBaseStations, pass, maximumPowerOnPasses, cancellationToken);
            for (var index = 0; index < passBaseStations.Length; index++)
            {
                if (passSucceeded[index])
                {
                    _baseStationPowerOnCommandSucceeded.Add(passBaseStations[index].BluetoothAddress);
                }
            }

            _baseStationPowerOnPassesCompleted = pass;
            if (pass == 2)
            {
                _baseStationSecondPowerOnPassCompletedAt = DateTimeOffset.UtcNow;
            }

            if (useSteamVrTrackingConfirmation)
            {
                var confirmation = await TryConfirmBaseStationStartupWithSteamVrAsync(baseStations, cancellationToken);
                if (confirmation == true)
                {
                    _baseStationsPoweredOn = true;
                    _baseStationPowerOnComplete = true;
                    _nextBaseStationPowerOnAttemptAt = null;
                    return;
                }

                if (confirmation is null)
                {
                    useSteamVrTrackingConfirmation = false;
                    maximumPowerOnPasses = BaseStationCommandTiming.PowerOnPasses;
                    targetPowerOnPasses = Math.Min(targetPowerOnPasses, maximumPowerOnPasses);
                    if (_baseStationPowerOnPassesCompleted >= maximumPowerOnPasses)
                    {
                        break;
                    }
                }
            }
        }

        _baseStationsPoweredOn = _baseStationsPoweredOn || _baseStationPowerOnCommandSucceeded.Count > 0;
        if (targetPowerOnPasses < maximumPowerOnPasses)
        {
            _nextBaseStationPowerOnAttemptAt = null;
            return;
        }

        var finalStates = await ReadBaseStationPowerStatesAsync(baseStations, cancellationToken);
        if (useSteamVrTrackingConfirmation)
        {
            Console.WriteLine($"SteamVR did not confirm all enabled base stations after {BaseStationCommandTiming.OpenVrPowerOnCycles} startup cycle(s). Stopping startup retries.");
        }

        _baseStationPowerOnComplete = IsBaseStationPowerOnComplete(baseStations, finalStates, _baseStationPowerOnCommandSucceeded);
        if (_baseStationPowerOnComplete)
        {
            _nextBaseStationPowerOnAttemptAt = null;
            return;
        }

        _baseStationPowerOnPassesCompleted = 0;
        _baseStationSecondPowerOnPassCompletedAt = null;
        _baseStationPowerOnCommandSucceeded.Clear();
        _nextBaseStationPowerOnAttemptAt = DateTimeOffset.UtcNow.AddSeconds(10);
    }

    private bool CanUseSteamVrTrackingConfirmationForStartup()
    {
        if (_steamVrTrackingReferenceStartupAvailable is { } available)
        {
            return available;
        }

        available = _steamVrTrackingReferenceReader.IsAvailable(out var reason);
        _steamVrTrackingReferenceStartupAvailable = available;
        if (!available && !_steamVrTrackingReferenceStartupUnavailableLogged)
        {
            Console.WriteLine($"SteamVR base-station tracking confirmation unavailable: {reason}. Using BLE startup fallback.");
            _steamVrTrackingReferenceStartupUnavailableLogged = true;
        }

        return available;
    }

    private async Task<bool?> TryConfirmBaseStationStartupWithSteamVrAsync(BaseStationDevice[] baseStations, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Waiting {BaseStationCommandTiming.OpenVrTrackingCheckDelay.TotalSeconds:0} seconds before checking SteamVR base-station tracking...");
        await Task.Delay(BaseStationCommandTiming.OpenVrTrackingCheckDelay, cancellationToken);

        try
        {
            var trackingReferences = _steamVrTrackingReferenceReader.ReadActiveTrackingReferences();
            var match = SteamVrBaseStationMatcher.Match(baseStations, trackingReferences);
            if (match.AllMatchedExactly)
            {
                Console.WriteLine($"SteamVR reports all {baseStations.Length} enabled base station(s) active by exact identity match. Startup complete.");
                return true;
            }

            if (match.CountFallbackMatched)
            {
                Console.WriteLine($"SteamVR reports {match.ActiveTrackingReferenceCount} active tracking reference(s) for {baseStations.Length} configured base station(s). Startup complete by count fallback.");
                return true;
            }

            Console.WriteLine($"SteamVR reports {match.ExactMatchCount}/{baseStations.Length} exact base station match(es) and {match.ActiveTrackingReferenceCount} active tracking reference(s). Continuing startup.");
            return false;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _steamVrTrackingReferenceStartupAvailable = false;
            if (!_steamVrTrackingReferenceStartupUnavailableLogged)
            {
                Console.WriteLine($"SteamVR base-station tracking confirmation failed: {ex.Message}. Using BLE startup fallback.");
                _steamVrTrackingReferenceStartupUnavailableLogged = true;
            }

            return null;
        }
    }

    private async Task TryPowerDownBaseStationsForSessionAsync(CancellationToken cancellationToken)
    {
        if (!_baseStationsPoweredOn && !_baseStationPowerOnAttempted)
        {
            return;
        }

        var baseStations = GetEnabledBaseStations();
        if (!_config.BaseStationsEnabled || baseStations.Length == 0)
        {
            _baseStationPowerOnAttempted = false;
            _baseStationsPoweredOn = false;
            _baseStationPowerOnComplete = false;
            _baseStationPowerOnPassesCompleted = 0;
            _baseStationSecondPowerOnPassCompletedAt = null;
            _baseStationPowerOnCommandSucceeded.Clear();
            return;
        }

        var mode = _config.BaseStationPowerDownMode;
        Console.WriteLine($"Sending {mode.ToString().ToLowerInvariant()} to {baseStations.Length} base station(s)...");
        var result = await BaseStationPowerDownRoutine.RunAsync(
            baseStations,
            mode,
            _baseStationGattClient,
            Console.WriteLine,
            _config.SaveBaseStationSettings,
            cancellationToken);

        if (result.AllStationsHandled)
        {
            _baseStationPowerOnAttempted = false;
            _baseStationsPoweredOn = false;
            _baseStationPowerOnComplete = false;
            _baseStationPowerOnPassesCompleted = 0;
            _baseStationSecondPowerOnPassCompletedAt = null;
            _baseStationPowerOnCommandSucceeded.Clear();
        }
    }

    public async Task RunEmergencyCloseCleanupAsync()
    {
        await TryEmergencyCloseCleanupAsync();
    }

    private async Task TryEmergencyCloseCleanupAsync()
    {
        try
        {
            await RunCleanupOnceAsync(waitForSteamVrServerExit: false, emergencyClose: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not complete emergency cleanup: {ex.Message}");
        }
    }

    private async Task RunCleanupOnceAsync(bool waitForSteamVrServerExit, bool emergencyClose, CancellationToken cancellationToken)
    {
        if (!await _cleanupLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (_cleanupStarted)
            {
                return;
            }

            if (emergencyClose)
            {
                Console.WriteLine("Emergency cleanup: restoring monitors before closing apps and base stations.");
                RestoreMonitorLayout();
                await TryStopManagedAppsForEmergencyCloseAsync();
                await TryPowerDownBaseStationsWithTimeoutAsync(TimeSpan.FromSeconds(20));
                _cleanupStarted = true;
                return;
            }

            await RestoreMonitorsAndStopManagedAppsCoreAsync(waitForSteamVrServerExit, cancellationToken);
            _cleanupStarted = true;
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    private async Task TryPowerDownBaseStationsWithTimeoutAsync(TimeSpan timeout)
    {
        if (!_baseStationsPoweredOn && !_baseStationPowerOnAttempted)
        {
            return;
        }

        try
        {
            using var timeoutSource = new CancellationTokenSource(timeout);
            await TryPowerDownBaseStationsForSessionAsync(timeoutSource.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Emergency cleanup could not power down base stations: {ex.Message}");
        }
    }

    private async Task TryStopManagedAppsForEmergencyCloseAsync()
    {
        try
        {
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, _config.ShutdownGraceSeconds + 5)));
            await StopLovenseAppsAsync(timeoutSource.Token);
            await StopManagedAppsAsync(ManagedAppStopReason.SessionEnding, timeoutSource.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Emergency cleanup could not close managed apps: {ex.Message}");
        }
    }

    private async Task<int> SendBaseStationCommandsAsync(
        BaseStationDevice[] baseStations,
        string action,
        Func<BaseStationDevice, CancellationToken, Task> commandAsync,
        CancellationToken cancellationToken,
        int attemptsPerStation = 1,
        Action<int>? onSuccess = null)
    {
        var successes = 0;
        for (var index = 0; index < baseStations.Length; index++)
        {
            var baseStation = baseStations[index];
            cancellationToken.ThrowIfCancellationRequested();
            Exception? lastException = null;
            try
            {
                for (var attempt = 1; attempt <= Math.Max(1, attemptsPerStation); attempt++)
                {
                    try
                    {
                        await commandAsync(baseStation, cancellationToken);
                        successes++;
                        onSuccess?.Invoke(index);
                        lastException = null;
                        Console.WriteLine($"Base station {baseStation.DisplayName}: {action} succeeded.");
                        break;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        lastException = ex;
                        if (attempt < attemptsPerStation)
                        {
                            Console.WriteLine($"Base station {baseStation.DisplayName}: {action} attempt {attempt} failed: {ex.Message}. Trying again.");
                            await Task.Delay(BaseStationCommandTiming.InterStationDelay, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
            }

            if (lastException is not null)
            {
                Console.WriteLine($"Base station {baseStation.DisplayName}: could not {action}: {lastException.Message}");
            }

            if (index < baseStations.Length - 1)
            {
                await Task.Delay(BaseStationCommandTiming.InterStationDelay, cancellationToken);
            }
        }

        return successes;
    }

    private async Task<bool[]> SendBaseStationPowerOnPassAsync(BaseStationDevice[] baseStations, int pass, int totalPasses, CancellationToken cancellationToken)
    {
        var stationSucceeded = new bool[baseStations.Length];
        if (pass > 1)
        {
            Console.WriteLine($"Repeating base station power-on pass {pass}/{totalPasses}...");
        }
        else
        {
            Console.WriteLine($"Powering on {baseStations.Length} base station(s)...");
        }

        await SendBaseStationCommandsAsync(
            baseStations,
            pass == 1 ? "power on" : $"power on pass {pass}",
            (baseStation, token) => _baseStationGattClient.PowerOnAsync(baseStation, token),
            cancellationToken,
            BaseStationCommandTiming.PowerOnAttempts,
            index => stationSucceeded[index] = true);

        return stationSucceeded;
    }

    private async Task<BaseStationPowerState[]> ReadBaseStationPowerStatesAsync(BaseStationDevice[] baseStations, CancellationToken cancellationToken)
    {
        var states = new BaseStationPowerState[baseStations.Length];
        for (var index = 0; index < baseStations.Length; index++)
        {
            var baseStation = baseStations[index];
            try
            {
                if (baseStation.PowerStateReadUnsupported)
                {
                    states[index] = BaseStationPowerState.Unsupported;
                    continue;
                }

                states[index] = await _baseStationGattClient.ReadPowerStateAsync(baseStation, cancellationToken);
                if (states[index] == BaseStationPowerState.Unsupported)
                {
                    baseStation.PowerStateReadUnsupported = true;
                    _baseStationSettingsNeedSave = true;
                }

                Console.WriteLine($"Base station {baseStation.DisplayName}: reported state {states[index]}.");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                states[index] = BaseStationPowerState.Unknown;
                Console.WriteLine($"Base station {baseStation.DisplayName}: could not read power state: {ex.Message}");
            }

            if (index < baseStations.Length - 1)
            {
                await Task.Delay(BaseStationCommandTiming.InterStationDelay, cancellationToken);
            }
        }

        if (_baseStationSettingsNeedSave)
        {
            _config.SaveBaseStationSettings();
            _baseStationSettingsNeedSave = false;
        }

        return states;
    }

    private static bool IsAwakeBaseStationState(BaseStationPowerState state)
        => state is BaseStationPowerState.Awake or BaseStationPowerState.Waking;

    private static bool IsBaseStationPowerOnComplete(
        BaseStationDevice[] allBaseStations,
        BaseStationPowerState[] finalStates,
        HashSet<string> commandSucceeded)
    {
        for (var index = 0; index < allBaseStations.Length; index++)
        {
            if (IsAwakeBaseStationState(finalStates[index]))
            {
                continue;
            }

            if (commandSucceeded.Contains(allBaseStations[index].BluetoothAddress)
                && finalStates[index] is BaseStationPowerState.Unknown or BaseStationPowerState.Unsupported)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private BaseStationDevice[] GetEnabledBaseStations()
        => _config.BaseStations
            .Where(baseStation => baseStation.Enabled && !string.IsNullOrWhiteSpace(baseStation.BluetoothAddress))
            .Select(baseStation => baseStation.WithDefaults())
            .ToArray();

    private async Task RestoreMonitorsAndStopManagedAppsAsync(bool waitForSteamVrServerExit, CancellationToken cancellationToken)
        => await RunCleanupOnceAsync(waitForSteamVrServerExit, emergencyClose: false, cancellationToken);

    private async Task RestoreMonitorsAndStopManagedAppsCoreAsync(bool waitForSteamVrServerExit, CancellationToken cancellationToken)
    {
        if (waitForSteamVrServerExit)
        {
            await WaitForSteamVrServerExitAsync(cancellationToken);
        }

        await TryPowerDownBaseStationsForSessionAsync(cancellationToken);
        RestoreMonitorLayout();
        await StopLovenseAppsAsync(cancellationToken);
        await StopManagedAppsAsync(ManagedAppStopReason.SessionEnding, cancellationToken);
    }

    private bool ShouldWaitForSteamVrServerExitBeforeCleanup()
        => (_turnOffSecondaryMonitors && _monitorLayout.HasSavedLayout) || _baseStationsPoweredOn || _baseStationPowerOnAttempted;

    private async Task TryRestoreMonitorsAndStopManagedAppsAsync(bool waitForSteamVrServerExit, CancellationToken cancellationToken)
    {
        try
        {
            await RunCleanupOnceAsync(waitForSteamVrServerExit, emergencyClose: false, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not complete monitor restore and managed app cleanup: {ex.Message}");
        }
    }

    private async Task WaitForSteamVrServerExitAsync(CancellationToken cancellationToken)
    {
        if (!IsAnyProcessRunning(_config.SteamVrServerProcessNames))
        {
            return;
        }

        Console.WriteLine($"Waiting for SteamVR server to exit: {string.Join(", ", _config.SteamVrServerProcessNames)}");
        while (IsAnyProcessRunning(_config.SteamVrServerProcessNames))
        {
            await Task.Delay(_pollInterval, cancellationToken);
        }

        Console.WriteLine("SteamVR server has exited.");
    }

    private void RestoreMonitorLayout()
    {
        try
        {
            _monitorLayout.Restore();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not restore previous monitor layout: {ex.Message}");
        }
    }

    private async Task RestartVrcFaceTrackingAsync(CancellationToken cancellationToken)
    {
        await StopProcessesAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
        Console.WriteLine("Starting VRCFaceTracking...");
        var vrcFaceTrackingStarted = StartOrAttach(
            _config.VrcFaceTrackingPath,
            _config.VrcFaceTrackingProcessNames,
            startMinimized: _config.VrcFaceTrackingStartMinimized);
        await VerifyRunningAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
        if (vrcFaceTrackingStarted && _config.VrcFaceTrackingStartMinimized)
        {
            await MinimizeProcessWindowsAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
        }
    }

    private async Task TryStartOscRouterAsync(CancellationToken cancellationToken)
    {
        if (!_config.OscRouterEnabled)
        {
            Console.WriteLine("OSC router is disabled by config.");
            return;
        }

        if (_oscRouter is not null)
        {
            Console.WriteLine("OSC router is already running.");
            _oscRouterWaitingForRetry = false;
            return;
        }

        await _oscRouterLaunchLock.WaitAsync(cancellationToken);
        try
        {
            if (_oscRouter is not null)
            {
                Console.WriteLine("OSC router is already running.");
                _oscRouterWaitingForRetry = false;
                return;
            }

            try
            {
                var router = OscRouter.Start(_config);
                _oscRouter = router;
                _oscRouterWaitingForRetry = false;
                Console.WriteLine(router.DescribeStarted());
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                _oscRouterWaitingForRetry = true;
                Console.WriteLine($"Warning: OSC router could not bind to 127.0.0.1:{_config.OscRouterReceivePort} because the endpoint is already in use. OSC routing is disabled temporarily.");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _oscRouterWaitingForRetry = true;
                Console.WriteLine($"Warning: OSC router could not start on 127.0.0.1:{_config.OscRouterReceivePort}: {ex.Message}. OSC routing is disabled temporarily.");
            }
        }
        finally
        {
            _oscRouterLaunchLock.Release();
        }
    }

    private async Task RetryOscRouterAsync(CancellationToken cancellationToken)
    {
        if (!_config.OscRouterEnabled)
        {
            Console.WriteLine("OSC router is disabled by config.");
            return;
        }

        if (_oscRouter is not null)
        {
            Console.WriteLine("OSC router is already running; no retry is needed.");
            return;
        }

        Console.WriteLine("Retrying OSC routing startup...");
        await TryStartOscRouterAsync(cancellationToken);
        ShowOscRouterRetryPromptIfNeeded();
    }

    private void ShowOscRouterRetryPromptIfNeeded()
    {
        if (_oscRouterWaitingForRetry && _config.OscRouterEnabled && _oscRouter is null)
        {
            Console.WriteLine("Press Space to retry to restart OSC routing.");
        }
    }

    private void StopOscRouter()
    {
        _oscRouter?.Dispose();
        _oscRouter = null;
    }

    private async Task InitializeOscGoesBrrrWorkflowAsync(CancellationToken cancellationToken)
    {
        if (!_config.OscGoesBrrrEnabled)
        {
            Console.WriteLine("OscGoesBrrr workflow is disabled by config.");
            return;
        }

        if (_config.OscGoesBrrrBleScannerEnabled)
        {
            StartOscGoesBrrrBleScanner(cancellationToken);
        }

        if (_config.OscGoesBrrrHotkeyEnabled)
        {
            Console.WriteLine("Press L to launch OSCGoesBrrr.");
            return;
        }

        if (_config.OscGoesBrrrBleScannerEnabled)
        {
            Console.WriteLine("Waiting for BLE scanner to detect Lovense before launching OSCGoesBrrr.");
            return;
        }

        try
        {
            await StartLovenseIntifaceAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Could not start Intiface for OscGoesBrrr workflow: {ex.Message}");
            return;
        }

        _lastLovenseConnected = await ReadDeviceConnectedOrPreviousAsync(
            "Lovense",
            IsLovenseConnectedAsync,
            previousConnected: false,
            cancellationToken);

        if (_lastLovenseConnected.Value)
        {
            Console.WriteLine("Lovense device detected after Intiface startup. Starting OscGoesBrrr.");
            await StartLovenseOscAsync(cancellationToken);
        }
        else
        {
            Console.WriteLine("No Lovense device detected yet. Intiface is running; OscGoesBrrr will start if a Lovense device appears.");
        }
    }

    private void StartOscGoesBrrrBleScanner(CancellationToken cancellationToken)
    {
        if (_oscGoesBrrrBleScannerTask is not null)
        {
            return;
        }

        var scanSeconds = Math.Max(1, _config.OscGoesBrrrBleScanSeconds);
        var intervalSeconds = Math.Max(1, _config.OscGoesBrrrBleScanIntervalSeconds);
        Console.WriteLine($"BLE scanner enabled for OSCGoesBrrr. Scanning for {scanSeconds} seconds every {intervalSeconds} seconds.");
        _oscGoesBrrrBleScannerTask = Task.Run(() => RunOscGoesBrrrBleScannerAsync(cancellationToken), CancellationToken.None);
    }

    private async Task RunOscGoesBrrrBleScannerAsync(CancellationToken cancellationToken)
    {
        var scanDuration = TimeSpan.FromSeconds(Math.Max(1, _config.OscGoesBrrrBleScanSeconds));
        var scanInterval = TimeSpan.FromSeconds(Math.Max(1, _config.OscGoesBrrrBleScanIntervalSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsOscGoesBrrrWorkflowRunning())
            {
                await Task.Delay(scanInterval, cancellationToken);
                continue;
            }

            if (IsIntifaceRunning() && !IsOscGoesBrrrRunning())
            {
                Console.WriteLine("Intiface is running but OscGoesBrrr is missing. Repairing OSCGoesBrrr workflow.");
                await StartLovenseOscAsync(cancellationToken);
                await Task.Delay(scanInterval, cancellationToken);
                continue;
            }

            try
            {
                var detected = await ScanForLovenseBleAdvertisementAsync(scanDuration, cancellationToken);
                if (detected && !IsOscGoesBrrrWorkflowRunning())
                {
                    Console.WriteLine("Lovense BLE advertisement detected. Launching OSCGoesBrrr workflow.");
                    await StartLovenseOscAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!_oscGoesBrrrBleScannerWarningShown)
                {
                    Console.WriteLine($"Could not scan BLE advertisements for Lovense: {ex.Message}. Press L to launch OSCGoesBrrr manually.");
                    _oscGoesBrrrBleScannerWarningShown = true;
                }

                return;
            }

            await Task.Delay(scanInterval, cancellationToken);
        }
    }

    private async Task<bool> ScanForLovenseBleAdvertisementAsync(TimeSpan scanDuration, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
        {
            throw new PlatformNotSupportedException("BLE advertisement scanning requires Windows 10 or newer.");
        }

        var matchCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            var advertisementBlock = BuildBleAdvertisementMatchBlock(eventArgs);
            if (DetectorGroupMatchesBlockAny(_config.LovenseDetectors, advertisementBlock.ToLowerInvariant()))
            {
                matchCompletion.TrySetResult(DescribeBleAdvertisement(eventArgs));
            }
        }

        watcher.Received += OnReceived;
        try
        {
            watcher.Start();

            var completedTask = await Task.WhenAny(matchCompletion.Task, Task.Delay(scanDuration, cancellationToken));
            if (completedTask != matchCompletion.Task)
            {
                return false;
            }

            Console.WriteLine($"Lovense BLE advertisement matched: {await matchCompletion.Task}");
            return true;
        }
        finally
        {
            watcher.Received -= OnReceived;
            if (watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Created)
            {
                watcher.Stop();
            }
        }
    }

    private static string BuildBleAdvertisementMatchBlock(BluetoothLEAdvertisementReceivedEventArgs eventArgs)
    {
        var builder = new StringBuilder();
        var advertisement = eventArgs.Advertisement;

        builder.AppendLine(advertisement.LocalName);
        builder.AppendLine(eventArgs.BluetoothAddress.ToString("X12", CultureInfo.InvariantCulture));
        builder.AppendLine(eventArgs.RawSignalStrengthInDBm.ToString(CultureInfo.InvariantCulture));

        foreach (var serviceUuid in advertisement.ServiceUuids)
        {
            builder.AppendLine(serviceUuid.ToString());
        }

        foreach (var manufacturerData in advertisement.ManufacturerData)
        {
            builder.Append("manufacturer:");
            builder.AppendLine(manufacturerData.CompanyId.ToString("X4", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string DescribeBleAdvertisement(BluetoothLEAdvertisementReceivedEventArgs eventArgs)
    {
        var name = eventArgs.Advertisement.LocalName;
        return string.IsNullOrWhiteSpace(name)
            ? eventArgs.BluetoothAddress.ToString("X12", CultureInfo.InvariantCulture)
            : name;
    }

    private async Task HandleConsoleHotkeysAsync(CancellationToken cancellationToken)
    {
        var hotkeys = ConsumeConsoleHotkeys();
        if (hotkeys.RestartCoreApps)
        {
            await RestartCoreAppsAsync(cancellationToken);
        }

        if (hotkeys.RetryOscRouter)
        {
            await RetryOscRouterAsync(cancellationToken);
        }

        if (hotkeys.LaunchOscGoesBrrr)
        {
            await HandleOscGoesBrrrHotkeyAsync(cancellationToken, hotkeyAlreadyConsumed: true);
        }
    }

    private async Task HandleOscGoesBrrrHotkeyAsync(CancellationToken cancellationToken, bool hotkeyAlreadyConsumed)
    {
        if (!_config.OscGoesBrrrEnabled || !_config.OscGoesBrrrHotkeyEnabled)
        {
            return;
        }

        if (!hotkeyAlreadyConsumed)
        {
            return;
        }

        if (IsOscGoesBrrrWorkflowRunning())
        {
            Console.WriteLine("OSCGoesBrrr workflow is already launched.");
            return;
        }

        Console.WriteLine("Launching OSCGoesBrrr workflow...");
        await StartLovenseOscAsync(cancellationToken);
    }

    private void RefreshOscGoesBrrrWorkflowState()
    {
        if (!_config.OscGoesBrrrEnabled)
        {
            return;
        }

        var wasWorkflowActive = _lovenseWorkflowTriggered || _lovenseIntifaceStarted;
        if (wasWorkflowActive && !IsOscGoesBrrrWorkflowRunning())
        {
            _lovenseWorkflowTriggered = false;
            _lovenseIntifaceStarted = false;
            Console.WriteLine("OSCGoesBrrr workflow is incomplete. Launch can be requested again.");
        }
    }

    private bool IsIntifaceRunning()
        => IsAnyProcessRunning(_config.IntifaceProcessNames);

    private bool IsOscGoesBrrrRunning()
        => IsAnyProcessRunning(_config.OscGoesBrrrProcessNames);

    private bool IsOscGoesBrrrWorkflowRunning()
        => IsIntifaceRunning() && IsOscGoesBrrrRunning();

    private static ConsoleHotkeys ConsumeConsoleHotkeys()
    {
        if (Console.IsInputRedirected)
        {
            return default;
        }

        try
        {
            var hotkeys = new ConsoleHotkeys();
            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.L)
                {
                    hotkeys.LaunchOscGoesBrrr = true;
                }
                else if (key.Key == ConsoleKey.Spacebar)
                {
                    hotkeys.RetryOscRouter = true;
                }
                else if (key.Key == ConsoleKey.R)
                {
                    hotkeys.RestartCoreApps = true;
                }
            }

            return hotkeys;
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }

    private async Task StartLovenseIntifaceAsync(CancellationToken cancellationToken)
    {
        if (IsIntifaceRunning())
        {
            Console.WriteLine($"Already running: {string.Join(", ", _config.IntifaceProcessNames)}");
            _lovenseIntifaceStarted = true;
            return;
        }

        Console.WriteLine("Starting Intiface...");
        var intifaceStarted = StartOrAttach(
            _config.IntifacePath,
            _config.IntifaceProcessNames,
            suppressOutput: true,
            startMinimized: _config.IntifaceStartMinimized);
        await VerifyRunningAsync("Intiface", _config.IntifaceProcessNames, cancellationToken);
        if (intifaceStarted && _config.IntifaceStartMinimized)
        {
            await MinimizeProcessWindowsAsync("Intiface", _config.IntifaceProcessNames, cancellationToken);
        }

        _lovenseIntifaceStarted = true;
    }

    private async Task StartLovenseOscAsync(CancellationToken cancellationToken)
    {
        await _oscGoesBrrrLaunchLock.WaitAsync(cancellationToken);
        try
        {
            if (IsOscGoesBrrrWorkflowRunning())
            {
                _lovenseIntifaceStarted = true;
                _lovenseWorkflowTriggered = true;
                return;
            }

            try
            {
                await StartLovenseIntifaceAsync(cancellationToken);

                Console.WriteLine($"Waiting {_config.DelayBeforeOscGoesBrrrSeconds} seconds before starting OscGoesBrrr...");
                await DelayWithCancellationAsync(TimeSpan.FromSeconds(_config.DelayBeforeOscGoesBrrrSeconds), cancellationToken);

                Console.WriteLine("Starting OscGoesBrrr...");
                var oscGoesBrrrStarted = StartOrAttach(
                    _config.OscGoesBrrrPath,
                    _config.OscGoesBrrrProcessNames,
                    suppressOutput: true,
                    startMinimized: _config.OscGoesBrrrStartMinimized);
                await VerifyRunningAsync("OscGoesBrrr", _config.OscGoesBrrrProcessNames, cancellationToken);
                if (oscGoesBrrrStarted && _config.OscGoesBrrrStartMinimized)
                {
                    await MinimizeProcessWindowsAsync("OscGoesBrrr", _config.OscGoesBrrrProcessNames, cancellationToken);
                }

                _lovenseWorkflowTriggered = true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Could not complete OscGoesBrrr startup: {ex.Message}");
            }
        }
        finally
        {
            _oscGoesBrrrLaunchLock.Release();
        }
    }

    private async Task StopLovenseAppsAsync(CancellationToken cancellationToken)
    {
        if (!_config.OscGoesBrrrEnabled && !_lovenseWorkflowTriggered && !_lovenseIntifaceStarted)
        {
            return;
        }

        var oscGoesBrrrRunning = _config.OscGoesBrrrEnabled && IsOscGoesBrrrRunning();
        var intifaceRunning = _config.OscGoesBrrrEnabled && IsIntifaceRunning();

        if (_lovenseWorkflowTriggered || oscGoesBrrrRunning)
        {
            await StopProcessesAsync("OscGoesBrrr", _config.OscGoesBrrrProcessNames, cancellationToken, forceFirst: true);
            _lovenseWorkflowTriggered = false;
        }

        if (_lovenseIntifaceStarted || intifaceRunning)
        {
            await StopProcessesAsync("Intiface", _config.IntifaceProcessNames, cancellationToken);
            _lovenseIntifaceStarted = false;
        }
    }

    private async Task StartAutoLaunchAppsAsync(CancellationToken cancellationToken)
    {
        var apps = GetEnabledAutoLaunchApps();
        if (apps.Length == 0)
        {
            return;
        }

        Console.WriteLine("Starting configured auto-launch apps...");
        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(app.Path))
            {
                Console.WriteLine($"Skipping {app.DisplayName}: executable was not found at {app.Path}");
                continue;
            }

            try
            {
                Console.WriteLine($"Starting {app.DisplayName}...");
                var appStarted = StartOrAttach(
                    app.Path,
                    app.ProcessNames,
                    suppressOutput: true,
                    runAsAdmin: app.RunAsAdmin,
                    startMinimized: app.StartMinimized);
                await VerifyRunningAsync(app.DisplayName, app.ProcessNames, cancellationToken);
                if (appStarted && app.StartMinimized)
                {
                    await MinimizeProcessWindowsAsync(app.DisplayName, app.ProcessNames, cancellationToken);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Could not start {app.DisplayName}: {ex.Message}");
            }
        }
    }

    private ManagedAutoLaunchApp[] GetEnabledAutoLaunchApps()
    {
        return _config.AutoLaunchApps
            .Where(app => app.Enabled && !string.IsNullOrWhiteSpace(app.Path))
            .Select(CreateManagedAutoLaunchApp)
            .Where(app => app is not null)
            .Cast<ManagedAutoLaunchApp>()
            .ToArray();
    }

    private static ManagedAutoLaunchApp? CreateManagedAutoLaunchApp(AutoLaunchAppConfig app)
    {
        var path = app.Path.Trim();
        var processNames = app.ProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Path.GetFileNameWithoutExtension(name.Trim()))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (processNames.Length == 0)
        {
            var inferredProcessName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(inferredProcessName))
            {
                processNames = [inferredProcessName];
            }
        }

        if (processNames.Length == 0)
        {
            Console.WriteLine($"Skipping auto-launch app with no process name: {path}");
            return null;
        }

        var displayName = !string.IsNullOrWhiteSpace(app.Name)
            ? app.Name.Trim()
            : Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = path;
        }

        var restartOnPimaxReconnect = app.RestartOnPimaxReconnect
            ?? app.CloseOnPimaxDisconnect
            ?? true;
        return new ManagedAutoLaunchApp(displayName, path, processNames, restartOnPimaxReconnect, app.RunAsAdmin, app.StartMinimized);
    }

    private bool StartOrAttach(
        string path,
        string[] processNames,
        bool suppressOutput = false,
        bool runAsAdmin = true,
        bool startMinimized = false)
    {
        if (IsAnyProcessRunning(processNames))
        {
            Console.WriteLine($"Already running: {string.Join(", ", processNames)}");
            return false;
        }

        if (!runAsAdmin)
        {
            StartProcessUnelevated(path, startMinimized);
            return true;
        }

        if (suppressOutput)
        {
            StartProcessSilently(path, startMinimized);
        }
        else
        {
            StartProcess(path, startMinimized);
        }

        return true;
    }

    private static void StartProcess(string path, bool startMinimized = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
            UseShellExecute = true,
            WindowStyle = startMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
        };

        Process.Start(startInfo);
    }

    private static void StartProcessUnelevated(string path, bool startMinimized = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = QuoteCommandLineArgument(path),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = startMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
        };

        Process.Start(startInfo);
    }

    private static void StartProcessSilently(string path, bool startMinimized = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = startMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {path}.");
        _ = Task.Run(async () =>
        {
            using (process)
            {
                try
                {
                    await Task.WhenAll(
                        process.StandardOutput.ReadToEndAsync(),
                        process.StandardError.ReadToEndAsync(),
                        process.WaitForExitAsync());
                }
                catch
                {
                    // Auto-launch app output is intentionally discarded.
                }
            }
        });
    }

    private static string QuoteCommandLineArgument(string value)
        => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

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

    private static async Task MinimizeProcessWindowsAsync(string displayName, string[] processNames, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryMinimizeProcessWindows(processNames))
            {
                Console.WriteLine($"{displayName} window minimized.");
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        Console.WriteLine($"Could not find a main window to minimize for {displayName}.");
    }

    private static bool TryMinimizeProcessWindows(string[] processNames)
    {
        var minimizedAny = false;
        var processes = GetProcesses(processNames);
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    process.Refresh();
                    var windowHandle = process.MainWindowHandle;
                    if (windowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    ShowWindow(windowHandle, ShowWindowMinimize);
                    minimizedAny = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not minimize process {process.ProcessName}: {ex.Message}");
                }
            }
        }

        return minimizedAny;
    }

    private async Task StopProcessesAsync(string displayName, string[] processNames, CancellationToken cancellationToken, bool forceFirst = false)
    {
        var processes = GetProcesses(processNames);
        if (processes.Count == 0)
        {
            Console.WriteLine($"{displayName} is already closed.");
            return;
        }

        Console.WriteLine($"Closing {displayName}...");

        if (forceFirst)
        {
            foreach (var process in processes)
            {
                using (process)
                {
                    TryKill(process);
                }
            }

            var forceOnlyDeadline = DateTimeOffset.UtcNow.AddSeconds(_config.ShutdownGraceSeconds);
            while (DateTimeOffset.UtcNow < forceOnlyDeadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsAnyProcessRunning(processNames))
                {
                    Console.WriteLine($"{displayName} is closed.");
                    return;
                }

                await Task.Delay(250, cancellationToken);
            }

            throw new TimeoutException($"{displayName} did not close cleanly.");
        }

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

    private async Task<bool> IsLovenseConnectedAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await IsDeviceConnectedAsync(_config.LovenseDetectors, cancellationToken))
            {
                return true;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Could not scan connected PnP devices for Lovense: {ex.Message} Trying Bluetooth device names.");
        }

        return IsRecentlySeenLovenseBluetoothDevice();
    }

    private bool IsRecentlySeenLovenseBluetoothDevice()
    {
        const string bluetoothDevicesKeyPath = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices";

        try
        {
            using var devicesKey = Registry.LocalMachine.OpenSubKey(bluetoothDevicesKeyPath);
            if (devicesKey is null)
            {
                return false;
            }

            foreach (var deviceKeyName in devicesKey.GetSubKeyNames())
            {
                using var deviceKey = devicesKey.OpenSubKey(deviceKeyName);
                if (deviceKey is null)
                {
                    continue;
                }

                var deviceName = DecodeBluetoothDeviceName(deviceKey.GetValue("Name"));
                if (string.IsNullOrWhiteSpace(deviceName))
                {
                    continue;
                }

                var lastSeen = ReadBluetoothFileTime(deviceKey.GetValue("LastSeen"))
                    ?? ReadBluetoothFileTime(deviceKey.GetValue("LastConnected"));
                if (lastSeen is null || DateTime.UtcNow - lastSeen.Value > LovenseBluetoothRegistryRecentWindow)
                {
                    continue;
                }

                var normalizedBlock = $"{deviceName}\n{deviceKeyName}".ToLowerInvariant();
                if (DetectorGroupMatchesBlockAny(_config.LovenseDetectors, normalizedBlock))
                {
                    Console.WriteLine($"Lovense Bluetooth device name matched: {deviceName}");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not scan Bluetooth device names for Lovense: {ex.Message}");
        }

        return false;
    }

    private static string DecodeBluetoothDeviceName(object? value)
    {
        return value is byte[] bytes
            ? Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim()
            : "";
    }

    private static DateTime? ReadBluetoothFileTime(object? value)
    {
        try
        {
            return value switch
            {
                long longValue => DateTime.FromFileTimeUtc(longValue),
                int intValue => DateTime.FromFileTimeUtc(intValue),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

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

    private static bool DetectorGroupMatchesBlockAny(string[][] detectorGroups, string normalizedDeviceBlock)
        => detectorGroups
            .Where(group => group.Length > 0)
            .Any(group => DetectorGroupMatchesBlock(group, normalizedDeviceBlock));

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
        try
        {
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"{fileName} timed out after {timeout.TotalSeconds:0} seconds.");
        }
    }

    private static bool IsAnyProcessRunning(string[] processNames)
    {
        var processes = GetProcesses(processNames);
        try
        {
            return processes.Count > 0;
        }
        finally
        {
            processes.ForEach(process => process.Dispose());
        }
    }

    private static List<Process> GetProcesses(string[] processNames)
    {
        var result = new List<Process>();
        foreach (var processName in processNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            result.AddRange(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)));
        }

        return result;
    }

    private const int ShowWindowMinimize = 6;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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

internal sealed record SteamVrTrackingReference(uint DeviceIndex, string[] IdentityValues);

internal sealed record SteamVrBaseStationMatchResult(
    int ExactMatchCount,
    int ActiveTrackingReferenceCount,
    bool AllMatchedExactly,
    bool CountFallbackMatched);

internal static class SteamVrBaseStationMatcher
{
    private static readonly Regex LighthouseNamePattern = new(@"LHB-?[A-Z0-9]{8}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SteamVrBaseStationMatchResult Match(BaseStationDevice[] baseStations, IReadOnlyList<SteamVrTrackingReference> trackingReferences)
    {
        var activeIdentitySets = trackingReferences
            .Select(reference => reference.IdentityValues
                .Select(NormalizeIdentity)
                .Where(value => value.Length >= 6)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray())
            .ToArray();
        var matchedReferenceIndexes = new HashSet<int>();
        var exactMatches = 0;

        foreach (var baseStation in baseStations)
        {
            var candidates = BuildBaseStationIdentityCandidates(baseStation)
                .Select(NormalizeIdentity)
                .Where(value => value.Length >= 6)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }

            for (var index = 0; index < activeIdentitySets.Length; index++)
            {
                if (matchedReferenceIndexes.Contains(index))
                {
                    continue;
                }

                if (!candidates.Any(candidate => activeIdentitySets[index].Any(identity => IdentityMatches(candidate, identity))))
                {
                    continue;
                }

                matchedReferenceIndexes.Add(index);
                exactMatches++;
                break;
            }
        }

        var allMatchedExactly = baseStations.Length > 0 && exactMatches == baseStations.Length;
        var countFallbackMatched = !allMatchedExactly && trackingReferences.Count >= baseStations.Length;
        return new SteamVrBaseStationMatchResult(exactMatches, trackingReferences.Count, allMatchedExactly, countFallbackMatched);
    }

    private static IEnumerable<string> BuildBaseStationIdentityCandidates(BaseStationDevice baseStation)
    {
        if (!string.IsNullOrWhiteSpace(baseStation.Name))
        {
            yield return baseStation.Name;
            foreach (Match match in LighthouseNamePattern.Matches(baseStation.Name))
            {
                yield return match.Value;
                yield return match.Value.Replace("-", "", StringComparison.Ordinal);
            }
        }

        if (!string.IsNullOrWhiteSpace(baseStation.FriendlyName))
        {
            yield return baseStation.FriendlyName;
        }

        if (!string.IsNullOrWhiteSpace(baseStation.Id))
        {
            yield return baseStation.Id;
        }
    }

    private static bool IdentityMatches(string baseStationIdentity, string steamVrIdentity)
        => string.Equals(baseStationIdentity, steamVrIdentity, StringComparison.OrdinalIgnoreCase)
            || steamVrIdentity.Contains(baseStationIdentity, StringComparison.OrdinalIgnoreCase)
            || baseStationIdentity.Contains(steamVrIdentity, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeIdentity(string value)
        => new(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
}

internal sealed class SteamVrTrackingReferenceReader
{
    private const string OpenVrSystemFnTableVersion = "FnTable:IVRSystem_026";
    private const int VrApplicationBackground = 3;
    private const int VrInitErrorNone = 0;
    private const int TrackingUniverseStanding = 1;
    private const int TrackedDeviceClassTrackingReference = 4;
    private const int TrackingResultRunningOk = 200;
    private const int TrackingResultRunningOutOfRange = 201;
    private const int MaxTrackedDeviceCount = 64;
    private const int PropTrackingSystemNameString = 1000;
    private const int PropModelNumberString = 1001;
    private const int PropSerialNumberString = 1002;
    private const int PropRenderModelNameString = 1003;
    private const int PropRegisteredDeviceTypeString = 1036;
    private const int PropManufacturerSerialNumberString = 1049;
    private const int PropComputedSerialNumberString = 1050;
    private const int PropActualTrackingSystemNameString = 1054;

    public bool IsAvailable(out string reason)
        => TryFindOpenVrApiDll(out _, out reason);

    public IReadOnlyList<SteamVrTrackingReference> ReadActiveTrackingReferences()
    {
        if (!TryFindOpenVrApiDll(out var openVrApiDllPath, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var library = NativeLibrary.Load(openVrApiDllPath);
        var initialized = false;
        try
        {
            var initInternal = GetExportDelegate<VrInitInternalDelegate>(library, "VR_InitInternal");
            var shutdownInternal = GetExportDelegate<VrShutdownInternalDelegate>(library, "VR_ShutdownInternal");
            var getGenericInterface = GetExportDelegate<VrGetGenericInterfaceDelegate>(library, "VR_GetGenericInterface");
            var getInitErrorDescription = GetOptionalExportDelegate<VrGetVrInitErrorAsEnglishDescriptionDelegate>(library, "VR_GetVRInitErrorAsEnglishDescription");

            var initError = 0;
            _ = initInternal(ref initError, VrApplicationBackground);
            if (initError != VrInitErrorNone)
            {
                throw new InvalidOperationException($"OpenVR init failed: {DescribeOpenVrInitError(initError, getInitErrorDescription)}");
            }

            initialized = true;
            var interfaceError = 0;
            var systemTablePointer = getGenericInterface(OpenVrSystemFnTableVersion, ref interfaceError);
            if (systemTablePointer == IntPtr.Zero || interfaceError != VrInitErrorNone)
            {
                throw new InvalidOperationException($"OpenVR system interface unavailable: {DescribeOpenVrInitError(interfaceError, getInitErrorDescription)}");
            }

            var systemTable = Marshal.PtrToStructure<OpenVrSystemFnTable>(systemTablePointer);
            var getDeviceToAbsoluteTrackingPose = CreateDelegate<GetDeviceToAbsoluteTrackingPoseDelegate>(systemTable.GetDeviceToAbsoluteTrackingPose);
            var getTrackedDeviceClass = CreateDelegate<GetTrackedDeviceClassDelegate>(systemTable.GetTrackedDeviceClass);
            var isTrackedDeviceConnected = CreateDelegate<IsTrackedDeviceConnectedDelegate>(systemTable.IsTrackedDeviceConnected);
            var getStringTrackedDeviceProperty = CreateDelegate<GetStringTrackedDevicePropertyDelegate>(systemTable.GetStringTrackedDeviceProperty);

            var poses = ReadTrackedDevicePoses(getDeviceToAbsoluteTrackingPose);
            var activeReferences = new List<SteamVrTrackingReference>();
            for (uint deviceIndex = 0; deviceIndex < MaxTrackedDeviceCount; deviceIndex++)
            {
                var pose = poses[deviceIndex];
                if (getTrackedDeviceClass(deviceIndex) != TrackedDeviceClassTrackingReference
                    || !isTrackedDeviceConnected(deviceIndex)
                    || !IsActiveTrackingReferencePose(pose))
                {
                    continue;
                }

                activeReferences.Add(new SteamVrTrackingReference(
                    deviceIndex,
                    ReadTrackingReferenceIdentityValues(deviceIndex, getStringTrackedDeviceProperty)));
            }

            shutdownInternal();
            initialized = false;
            return activeReferences;
        }
        finally
        {
            try
            {
                if (initialized)
                {
                    GetExportDelegate<VrShutdownInternalDelegate>(library, "VR_ShutdownInternal")();
                }
            }
            finally
            {
                NativeLibrary.Free(library);
            }
        }
    }

    private static bool TryFindOpenVrApiDll(out string openVrApiDllPath, out string reason)
    {
        foreach (var runtimePath in GetOpenVrRuntimePaths())
        {
            var candidate = Path.Combine(runtimePath, "bin", "win64", "openvr_api.dll");
            if (File.Exists(candidate))
            {
                openVrApiDllPath = candidate;
                reason = "";
                return true;
            }
        }

        openVrApiDllPath = "";
        reason = "openvr_api.dll was not found in the configured SteamVR runtime";
        return false;
    }

    private static IEnumerable<string> GetOpenVrRuntimePaths()
    {
        var openVrPathsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openvr",
            "openvrpaths.vrpath");
        if (File.Exists(openVrPathsFile))
        {
            foreach (var runtimePath in ReadRuntimePathsFromOpenVrPathsFile(openVrPathsFile))
            {
                yield return runtimePath;
            }
        }

        foreach (var runtimePath in ReadSteamVrInstallLocationsFromRegistry())
        {
            yield return runtimePath;
        }
    }

    private static IEnumerable<string> ReadRuntimePathsFromOpenVrPathsFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("runtime", out var runtimeElement) || runtimeElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in runtimeElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } runtimePath)
            {
                yield return runtimePath.TrimEnd('\\', '/');
            }
        }
    }

    private static IEnumerable<string> ReadSteamVrInstallLocationsFromRegistry()
    {
        const string steamVrUninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 250820";
        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var key = baseKey.OpenSubKey(steamVrUninstallKeyPath);
            if (key?.GetValue("InstallLocation") is string installLocation && !string.IsNullOrWhiteSpace(installLocation))
            {
                yield return installLocation.TrimEnd('\\', '/');
            }
        }
    }

    private static Dictionary<uint, OpenVrTrackedDevicePose> ReadTrackedDevicePoses(GetDeviceToAbsoluteTrackingPoseDelegate getDeviceToAbsoluteTrackingPose)
    {
        var poseSize = Marshal.SizeOf<OpenVrTrackedDevicePose>();
        var poseBuffer = Marshal.AllocHGlobal(poseSize * MaxTrackedDeviceCount);
        try
        {
            getDeviceToAbsoluteTrackingPose(TrackingUniverseStanding, 0, poseBuffer, MaxTrackedDeviceCount);
            return Enumerable
                .Range(0, MaxTrackedDeviceCount)
                .ToDictionary(
                    index => (uint)index,
                    index => Marshal.PtrToStructure<OpenVrTrackedDevicePose>(IntPtr.Add(poseBuffer, poseSize * index)));
        }
        finally
        {
            Marshal.FreeHGlobal(poseBuffer);
        }
    }

    private static bool IsActiveTrackingReferencePose(OpenVrTrackedDevicePose pose)
        => pose.DeviceIsConnected
            && pose.PoseIsValid
            && pose.TrackingResult is TrackingResultRunningOk or TrackingResultRunningOutOfRange;

    private static string[] ReadTrackingReferenceIdentityValues(uint deviceIndex, GetStringTrackedDevicePropertyDelegate getStringTrackedDeviceProperty)
    {
        return new[]
            {
                ReadStringTrackedDeviceProperty(deviceIndex, PropSerialNumberString, getStringTrackedDeviceProperty),
                ReadStringTrackedDeviceProperty(deviceIndex, PropManufacturerSerialNumberString, getStringTrackedDeviceProperty),
                ReadStringTrackedDeviceProperty(deviceIndex, PropComputedSerialNumberString, getStringTrackedDeviceProperty),
                ReadStringTrackedDeviceProperty(deviceIndex, PropRegisteredDeviceTypeString, getStringTrackedDeviceProperty),
                ReadStringTrackedDeviceProperty(deviceIndex, PropRenderModelNameString, getStringTrackedDeviceProperty),
                ReadStringTrackedDeviceProperty(deviceIndex, PropModelNumberString, getStringTrackedDeviceProperty),
                ReadStringTrackedDeviceProperty(deviceIndex, PropTrackingSystemNameString, getStringTrackedDeviceProperty),
                ReadStringTrackedDeviceProperty(deviceIndex, PropActualTrackingSystemNameString, getStringTrackedDeviceProperty)
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ReadStringTrackedDeviceProperty(
        uint deviceIndex,
        int property,
        GetStringTrackedDevicePropertyDelegate getStringTrackedDeviceProperty)
    {
        var error = 0;
        var bufferSize = 256u;
        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            var requiredSize = getStringTrackedDeviceProperty(deviceIndex, property, buffer, bufferSize, ref error);
            if (requiredSize > bufferSize)
            {
                Marshal.FreeHGlobal(buffer);
                bufferSize = requiredSize;
                buffer = Marshal.AllocHGlobal((int)bufferSize);
                error = 0;
                requiredSize = getStringTrackedDeviceProperty(deviceIndex, property, buffer, bufferSize, ref error);
            }

            return requiredSize == 0 ? "" : Marshal.PtrToStringAnsi(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string DescribeOpenVrInitError(int error, VrGetVrInitErrorAsEnglishDescriptionDelegate? getDescription)
    {
        if (error == VrInitErrorNone)
        {
            return "none";
        }

        if (getDescription is null)
        {
            return error.ToString(CultureInfo.InvariantCulture);
        }

        var descriptionPointer = getDescription(error);
        var description = descriptionPointer == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(descriptionPointer);
        return string.IsNullOrWhiteSpace(description)
            ? error.ToString(CultureInfo.InvariantCulture)
            : $"{description} ({error.ToString(CultureInfo.InvariantCulture)})";
    }

    private static T GetExportDelegate<T>(IntPtr library, string exportName) where T : Delegate
        => CreateDelegate<T>(NativeLibrary.GetExport(library, exportName));

    private static T? GetOptionalExportDelegate<T>(IntPtr library, string exportName) where T : Delegate
        => NativeLibrary.TryGetExport(library, exportName, out var exportPointer)
            ? CreateDelegate<T>(exportPointer)
            : null;

    private static T CreateDelegate<T>(IntPtr functionPointer) where T : Delegate
    {
        if (functionPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("OpenVR returned a null function pointer.");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(functionPointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenVrSystemFnTable
    {
        public IntPtr GetRecommendedRenderTargetSize;
        public IntPtr GetProjectionMatrix;
        public IntPtr GetProjectionRaw;
        public IntPtr ComputeDistortion;
        public IntPtr ComputeDistortionSet;
        public IntPtr GetEyeToHeadTransform;
        public IntPtr GetTimeSinceLastVsync;
        public IntPtr GetD3D9AdapterIndex;
        public IntPtr GetDXGIOutputInfo;
        public IntPtr GetOutputDevice;
        public IntPtr IsDisplayOnDesktop;
        public IntPtr SetDisplayVisibility;
        public IntPtr GetDeviceToAbsoluteTrackingPose;
        public IntPtr GetSeatedZeroPoseToStandingAbsoluteTrackingPose;
        public IntPtr GetRawZeroPoseToStandingAbsoluteTrackingPose;
        public IntPtr GetSortedTrackedDeviceIndicesOfClass;
        public IntPtr GetTrackedDeviceActivityLevel;
        public IntPtr ApplyTransform;
        public IntPtr GetTrackedDeviceIndexForControllerRole;
        public IntPtr GetControllerRoleForTrackedDeviceIndex;
        public IntPtr GetTrackedDeviceClass;
        public IntPtr IsTrackedDeviceConnected;
        public IntPtr GetBoolTrackedDeviceProperty;
        public IntPtr GetFloatTrackedDeviceProperty;
        public IntPtr GetInt32TrackedDeviceProperty;
        public IntPtr GetUint64TrackedDeviceProperty;
        public IntPtr GetMatrix34TrackedDeviceProperty;
        public IntPtr GetArrayTrackedDeviceProperty;
        public IntPtr GetStringTrackedDeviceProperty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HmdMatrix34
    {
        public float M0;
        public float M1;
        public float M2;
        public float M3;
        public float M4;
        public float M5;
        public float M6;
        public float M7;
        public float M8;
        public float M9;
        public float M10;
        public float M11;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HmdVector3
    {
        public float X;
        public float Y;
        public float Z;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct OpenVrTrackedDevicePose
    {
        public HmdMatrix34 DeviceToAbsoluteTracking;
        public HmdVector3 Velocity;
        public HmdVector3 AngularVelocity;
        public int TrackingResult;
        [MarshalAs(UnmanagedType.I1)]
        public bool PoseIsValid;
        [MarshalAs(UnmanagedType.I1)]
        public bool DeviceIsConnected;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr VrInitInternalDelegate(ref int error, int applicationType);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VrShutdownInternalDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr VrGetGenericInterfaceDelegate(string interfaceVersion, ref int error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr VrGetVrInitErrorAsEnglishDescriptionDelegate(int error);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetDeviceToAbsoluteTrackingPoseDelegate(int origin, float predictedSecondsToPhotonsFromNow, IntPtr trackedDevicePoseArray, uint trackedDevicePoseArrayCount);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetTrackedDeviceClassDelegate(uint deviceIndex);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool IsTrackedDeviceConnectedDelegate(uint deviceIndex);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint GetStringTrackedDevicePropertyDelegate(uint deviceIndex, int property, IntPtr value, uint bufferSize, ref int error);
}

internal static class BaseStationEmergencyCleanup
{
    public static bool TryLaunchDetached(SupervisorConfig config, TimeSpan initialDelay)
    {
        if (!config.BaseStationsEnabled || config.BaseStations.Length == 0)
        {
            return false;
        }

        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                executablePath = Path.Combine(AppContext.BaseDirectory, "PimaxVrcSupervisor.exe");
            }

            if (!File.Exists(executablePath))
            {
                return false;
            }

            var configPath = config.LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
            if (!File.Exists(configPath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--emergency-base-station-cleanup");
            startInfo.ArgumentList.Add(configPath);
            startInfo.ArgumentList.Add("--delay-seconds");
            startInfo.ArgumentList.Add(Math.Max(0, (int)Math.Round(initialDelay.TotalSeconds)).ToString(CultureInfo.InvariantCulture));

            Process.Start(startInfo)?.Dispose();
            Console.WriteLine("Started detached base-station emergency cleanup helper.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not start detached base-station cleanup helper: {ex.Message}");
            return false;
        }
    }

    public static async Task RunAsync(SupervisorConfig config, TimeSpan initialDelay, CancellationToken cancellationToken)
    {
        if (initialDelay > TimeSpan.Zero)
        {
            await Task.Delay(initialDelay, cancellationToken);
        }

        if (!config.BaseStationsEnabled)
        {
            return;
        }

        var baseStations = config.BaseStations
            .Where(baseStation => baseStation.Enabled && !string.IsNullOrWhiteSpace(baseStation.BluetoothAddress))
            .Select(baseStation => baseStation.WithDefaults())
            .ToArray();
        if (baseStations.Length == 0)
        {
            return;
        }

        await BaseStationPowerDownRoutine.RunAsync(
            baseStations,
            config.BaseStationPowerDownMode,
            new BaseStationGattClient(),
            Console.WriteLine,
            config.SaveBaseStationSettings,
            cancellationToken);
    }
}

internal sealed class ConsoleCloseHandler : IDisposable
{
    private const uint CtrlCloseEvent = 2;
    private const uint CtrlLogoffEvent = 5;
    private const uint CtrlShutdownEvent = 6;
    private static readonly IDisposable NoopRegistration = new NoopDisposable();

    private readonly HandlerRoutine _handler;

    private ConsoleCloseHandler(
        CancellationTokenSource shutdown,
        Task supervisorStopped,
        Func<Task> emergencyCleanupAsync,
        Action launchDetachedBaseStationCleanup)
    {
        _handler = ctrlType =>
        {
            if (ctrlType is not (CtrlCloseEvent or CtrlLogoffEvent or CtrlShutdownEvent))
            {
                return false;
            }

            try
            {
                Console.WriteLine("Console close requested. Restoring monitors and closing managed apps.");
                launchDetachedBaseStationCleanup();
                emergencyCleanupAsync().GetAwaiter().GetResult();
                shutdown.Cancel();
                supervisorStopped.Wait(TimeSpan.FromSeconds(60));
            }
            catch
            {
                // Windows gives console close handlers limited time; keep this best-effort.
            }

            return true;
        };

        SetConsoleCtrlHandler(_handler, add: true);
    }

    public static IDisposable Register(
        CancellationTokenSource shutdown,
        Task supervisorStopped,
        Func<Task> emergencyCleanupAsync,
        Action launchDetachedBaseStationCleanup)
        => OperatingSystem.IsWindows()
            ? new ConsoleCloseHandler(shutdown, supervisorStopped, emergencyCleanupAsync, launchDetachedBaseStationCleanup)
            : NoopRegistration;

    public void Dispose()
    {
        SetConsoleCtrlHandler(_handler, add: false);
    }

    private delegate bool HandlerRoutine(uint ctrlType);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
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

internal sealed record PimaxServiceLogEvent(DateTimeOffset Timestamp, bool IsRemove, bool IsAdd);

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

internal sealed class MonitorLayoutController
{
    private DisplayLayoutSnapshot? _savedLayout;

    public bool HasSavedLayout => _savedLayout is not null;

    public void KeepPrimaryMonitorOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_savedLayout is not null)
        {
            return;
        }

        var currentLayout = DisplayLayoutSnapshot.Capture();
        if (currentLayout.ActiveMonitorCount <= 1)
        {
            Console.WriteLine("Only one active monitor detected. Keeping current monitor layout.");
            return;
        }

        var primaryPathIndex = currentLayout.FindPrimaryPathIndex();
        _savedLayout = currentLayout;

        Console.WriteLine($"Multiple active monitors detected ({currentLayout.ActiveMonitorCount}). Saving layout and switching to monitor 1 only.");
        currentLayout.ApplyOnlyPath(primaryPathIndex);
        Console.WriteLine("Extra monitors are disabled for the VR session.");
    }

    public void Restore()
    {
        if (_savedLayout is null)
        {
            return;
        }

        Console.WriteLine("Restoring previous monitor layout.");
        _savedLayout.Apply();
        _savedLayout = null;
        Console.WriteLine("Previous monitor layout restored.");
    }
}

internal sealed class DisplayLayoutSnapshot
{
    private const uint ErrorSuccess = 0;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcApply = 0x00000080;
    private const uint SdcAllowChanges = 0x00000400;
    private const uint InvalidModeInfoIndex = 0xffffffff;

    private readonly DisplayConfigPathInfo[] _paths;
    private readonly DisplayConfigModeInfo[] _modes;

    private DisplayLayoutSnapshot(DisplayConfigPathInfo[] paths, DisplayConfigModeInfo[] modes)
    {
        _paths = paths;
        _modes = modes;
    }

    public int ActiveMonitorCount => _paths.Length;

    public static DisplayLayoutSnapshot Capture()
    {
        var error = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
        ThrowIfWin32Error(error, "get display configuration buffer sizes");

        while (true)
        {
            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            var queryPathCount = pathCount;
            var queryModeCount = modeCount;

            error = QueryDisplayConfig(
                QdcOnlyActivePaths,
                ref queryPathCount,
                paths,
                ref queryModeCount,
                modes,
                IntPtr.Zero);

            if (error == ErrorSuccess)
            {
                Array.Resize(ref paths, checked((int)queryPathCount));
                Array.Resize(ref modes, checked((int)queryModeCount));
                return new DisplayLayoutSnapshot(paths, modes);
            }

            const uint errorInsufficientBuffer = 122;
            if (error != errorInsufficientBuffer)
            {
                ThrowIfWin32Error(error, "query active display configuration");
            }

            error = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount);
            ThrowIfWin32Error(error, "get display configuration buffer sizes");
        }
    }

    public int FindPrimaryPathIndex()
    {
        for (var index = 0; index < _paths.Length; index++)
        {
            if (TryGetSourcePosition(_paths[index], out var position) && position.X == 0 && position.Y == 0)
            {
                return index;
            }
        }

        return 0;
    }

    public void ApplyOnlyPath(int pathIndex)
    {
        if (pathIndex < 0 || pathIndex >= _paths.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(pathIndex));
        }

        var path = _paths[pathIndex];
        path.Flags |= DisplayConfigNative.PathActive;
        var modes = new List<DisplayConfigModeInfo>();
        path.SourceInfo.ModeInfoIdx = AddReferencedMode(path.SourceInfo.ModeInfoIdx, modes);
        path.TargetInfo.ModeInfoIdx = AddReferencedMode(path.TargetInfo.ModeInfoIdx, modes);
        Apply([path], modes.ToArray());
    }

    public void Apply() => Apply(_paths, _modes);

    private bool TryGetSourcePosition(DisplayConfigPathInfo path, out PointL position)
    {
        position = default;
        if (path.SourceInfo.ModeInfoIdx == InvalidModeInfoIndex || path.SourceInfo.ModeInfoIdx >= _modes.Length)
        {
            return false;
        }

        var mode = _modes[path.SourceInfo.ModeInfoIdx];
        if (mode.InfoType != DisplayConfigModeInfoType.Source)
        {
            return false;
        }

        position = mode.ModeInfo.SourceMode.Position;
        return true;
    }

    private uint AddReferencedMode(uint modeIndex, List<DisplayConfigModeInfo> modes)
    {
        if (modeIndex == InvalidModeInfoIndex || modeIndex >= _modes.Length)
        {
            return InvalidModeInfoIndex;
        }

        var updatedModeIndex = checked((uint)modes.Count);
        modes.Add(_modes[modeIndex]);
        return updatedModeIndex;
    }

    private static void Apply(DisplayConfigPathInfo[] paths, DisplayConfigModeInfo[] modes)
    {
        var error = SetDisplayConfig(
            (uint)paths.Length,
            paths,
            (uint)modes.Length,
            modes,
            SdcUseSuppliedDisplayConfig | SdcApply | SdcAllowChanges);

        ThrowIfWin32Error(error, "apply display configuration");
    }

    private static void ThrowIfWin32Error(uint error, string action)
    {
        if (error == ErrorSuccess)
        {
            return;
        }

        throw new InvalidOperationException($"Could not {action}. Win32 error: {error}.");
    }

    [DllImport("user32.dll")]
    private static extern uint GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern uint QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern uint SetDisplayConfig(
        uint numPathArrayElements,
        [In] DisplayConfigPathInfo[] pathArray,
        uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[] modeInfoArray,
        uint flags);
}

internal static class DisplayConfigNative
{
    public const uint PathActive = 0x00000001;
}

internal sealed class OscRouter : IDisposable
{
    private const int MaxUdpDatagramBytes = 65507;
    private static readonly IOControlCode SioUdpConnReset = unchecked((IOControlCode)0x9800000C);

    private readonly Socket _receiveSocket;
    private readonly Socket _sendSocket;
    private readonly OscRoute[] _routes;
    private readonly IPEndPoint[] _routeEndpoints;
    private readonly IPEndPoint _listenEndpoint;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _runTask;
    private DateTimeOffset? _lastReceiveResetLoggedAt;

    private OscRouter(Socket receiveSocket, Socket sendSocket, OscRoute[] routes, IPEndPoint listenEndpoint)
    {
        _receiveSocket = receiveSocket;
        _sendSocket = sendSocket;
        _routes = routes;
        _routeEndpoints = routes
            .Select(route => new IPEndPoint(IPAddress.Loopback, route.AppReceivePort))
            .ToArray();
        _listenEndpoint = listenEndpoint;
        _runTask = Task.Run(Run);
    }

    public static OscRouter Start(SupervisorConfig config)
    {
        if (config.OscRouterReceivePort is < 1 or > 65535)
        {
            throw new InvalidOperationException($"OSC router receive port must be between 1 and 65535: {config.OscRouterReceivePort}");
        }

        var routes = config.OscRoutes
            .Where(route => route.Enabled)
            .Where(route => route.AppReceivePort is >= 1 and <= 65535)
            .ToArray();
        var endpoint = new IPEndPoint(IPAddress.Loopback, config.OscRouterReceivePort);
        var receiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            TryDisableUdpConnectionReset(receiveSocket);
            TryDisableUdpConnectionReset(sendSocket);
            receiveSocket.Bind(endpoint);
            return new OscRouter(receiveSocket, sendSocket, routes, endpoint);
        }
        catch
        {
            receiveSocket.Dispose();
            sendSocket.Dispose();
            throw;
        }
    }

    public string DescribeStarted()
    {
        var routeText = _routes.Length == 1 ? "1 route" : $"{_routes.Length} routes";
        return $"OSC router listening on {_listenEndpoint.Address}:{_listenEndpoint.Port}; forwarding to {routeText}.";
    }

    private void Run()
    {
        var buffer = new byte[MaxUdpDatagramBytes];
        EndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
        while (!_shutdown.IsCancellationRequested)
        {
            int receivedBytes;
            try
            {
                receivedBytes = _receiveSocket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteEndpoint);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex) when (_shutdown.IsCancellationRequested || ex.SocketErrorCode == SocketError.Interrupted)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                LogReceiveResetThrottled();
                continue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OSC router receive error: {ex.Message}");
                continue;
            }

            for (var routeIndex = 0; routeIndex < _routes.Length; routeIndex++)
            {
                var route = _routes[routeIndex];
                var routeEndpoint = _routeEndpoints[routeIndex];
                try
                {
                    _sendSocket.SendTo(buffer, 0, receivedBytes, SocketFlags.None, routeEndpoint);
                }
                catch (SocketException ex) when (_shutdown.IsCancellationRequested || ex.SocketErrorCode == SocketError.Interrupted)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"OSC router could not forward to {route.DisplayName}: {ex.Message}");
                }
            }
        }
    }

    private void LogReceiveResetThrottled()
    {
        var now = DateTimeOffset.Now;
        if (_lastReceiveResetLoggedAt is not null && now - _lastReceiveResetLoggedAt < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastReceiveResetLoggedAt = now;
        Console.WriteLine("OSC router ignored a UDP connection reset from Windows. Routing is still running.");
    }

    private static void TryDisableUdpConnectionReset(Socket socket)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }
        catch
        {
            // Older Windows socket stacks may not support this option; separate sockets still keep routing resilient.
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _receiveSocket.Dispose();
        _sendSocket.Dispose();
        try
        {
            _runTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown is best-effort; the UDP socket has already been closed.
        }

        _shutdown.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    public DisplayConfigPathSourceInfo SourceInfo;
    public DisplayConfigPathTargetInfo TargetInfo;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public DisplayConfigVideoOutputTechnology OutputTechnology;
    public DisplayConfigRotation Rotation;
    public DisplayConfigScaling Scaling;
    public DisplayConfigRational RefreshRate;
    public DisplayConfigScanLineOrdering ScanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)]
    public bool TargetAvailable;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigModeInfo
{
    public DisplayConfigModeInfoType InfoType;
    public uint Id;
    public Luid AdapterId;
    public DisplayConfigModeInfoUnion ModeInfo;
}

[StructLayout(LayoutKind.Explicit)]
internal struct DisplayConfigModeInfoUnion
{
    [FieldOffset(0)]
    public DisplayConfigTargetMode TargetMode;

    [FieldOffset(0)]
    public DisplayConfigSourceMode SourceMode;

    [FieldOffset(0)]
    public DisplayConfigDesktopImageInfo DesktopImageInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigTargetMode
{
    public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigVideoSignalInfo
{
    public ulong PixelRate;
    public DisplayConfigRational HSyncFreq;
    public DisplayConfigRational VSyncFreq;
    public DisplayConfig2DRegion ActiveSize;
    public DisplayConfig2DRegion TotalSize;
    public uint VideoStandard;
    public DisplayConfigScanLineOrdering ScanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigSourceMode
{
    public uint Width;
    public uint Height;
    public DisplayConfigPixelFormat PixelFormat;
    public PointL Position;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDesktopImageInfo
{
    public PointL PathSourceSize;
    public RectL DesktopImageRegion;
    public RectL DesktopImageClip;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigRational
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfig2DRegion
{
    public uint Cx;
    public uint Cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointL
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RectL
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal enum DisplayConfigModeInfoType : uint
{
    Source = 1,
    Target = 2,
    DesktopImage = 3
}

internal enum DisplayConfigVideoOutputTechnology : uint
{
    Other = 0xffffffff
}

internal enum DisplayConfigRotation : uint
{
    Identity = 1
}

internal enum DisplayConfigScaling : uint
{
    Identity = 1
}

internal enum DisplayConfigScanLineOrdering : uint
{
    Unspecified = 0
}

internal enum DisplayConfigPixelFormat : uint
{
    Bpp32 = 0,
    Bpp16 = 1,
    Bpp8 = 2,
    NonGdi = 3
}

internal sealed class SupervisorConfig
{
    public string BrokenEyePath { get; set; } = "";
    public string VrcFaceTrackingPath { get; set; } = "";
    public string IntifacePath { get; set; } = "";
    public string OscGoesBrrrPath { get; set; } = "";
    public bool BrokenEyeStartMinimized { get; set; }
    public bool VrcFaceTrackingStartMinimized { get; set; }
    public bool IntifaceStartMinimized { get; set; }
    public bool OscGoesBrrrStartMinimized { get; set; }
    public const string DefaultVrcFaceTrackingPath = @"C:\Program Files (x86)\Steam\steamapps\common\VRCFaceTracking\VRCFaceTracking.exe";
    public static string DefaultIntifacePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IntifaceCentral",
        "intiface_central.exe");
    public static string DefaultOscGoesBrrrPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "OscGoesBrrr",
        "OscGoesBrrr.exe");
    public string[] BrokenEyeProcessNames { get; set; } = ["Broken Eye"];
    public string[] VrcFaceTrackingProcessNames { get; set; } = ["VRCFaceTracking"];
    public string[] IntifaceProcessNames { get; set; } = ["intiface_central.exe"];
    public string[] OscGoesBrrrProcessNames { get; set; } = ["OscGoesBrrr.exe"];
    public string OscGoesBrrrrPath
    {
        set
        {
            if (string.IsNullOrWhiteSpace(OscGoesBrrrPath))
            {
                OscGoesBrrrPath = value;
            }
        }
    }
    public string[] OscGoesBrrrrProcessNames
    {
        set
        {
            if (OscGoesBrrrProcessNames.Length == 0 || OscGoesBrrrProcessNames.SequenceEqual(["OscGoesBrrr.exe"], StringComparer.OrdinalIgnoreCase))
            {
                OscGoesBrrrProcessNames = value;
            }
        }
    }
    public AutoLaunchAppConfig[] AutoLaunchApps { get; init; } = [];
    public string[] WatchedShutdownProcessNames { get; init; } = ["VRChat"];
    public string[] SteamVrServerProcessNames { get; init; } = ["vrserver"];
    public bool BaseStationsEnabled { get; init; }
    public BaseStationPowerDownMode BaseStationPowerDownMode { get; init; } = BaseStationPowerDownMode.Sleep;
    public BaseStationDevice[] BaseStations { get; init; } = [];
    public bool OscGoesBrrrEnabled { get; set; }
    public bool LovenseAutoLaunchEnabled
    {
        set => OscGoesBrrrEnabled = value;
    }
    public bool OscGoesBrrrHotkeyEnabled { get; init; } = true;
    public bool OscGoesBrrrBleScannerEnabled { get; init; }
    public int OscGoesBrrrBleScanSeconds { get; init; } = 30;
    public int OscGoesBrrrBleScanIntervalSeconds { get; init; } = 60;
    public bool OscRouterEnabled { get; init; }
    public int OscRouterReceivePort { get; init; } = 9001;
    public OscRoute[] OscRoutes { get; init; } = [];
    public JsonElement MouthTrackerUser { get; set; }
    public JsonElement TurnOffSecondaryMonitors { get; set; }
    public JsonElement AutoLaunchScheduledTask { get; set; }
    public string[][] PimaxDetectors { get; init; } =
    [
        ["USB\\VID_34A4&PID_0012"],
        ["USB\\VID_34A4&PID_0018"],
        ["USB\\VID_34A4&PID_0020"],
        ["USB\\VID_34A4&PID_0040"],
        ["USB\\VID_34A4&PID_0042"],
        ["USB\\VID_34A4&PID_0044"],
        ["USB\\VID_34A4&PID_0046"],
        ["Pimax", "Crystal"],
        ["Pimax", "P3C"],
        ["Pimax", "WiGig"]
    ];
    public string[][] MouthTrackerDetectors { get; init; } =
    [
        ["USB\\VID_0BB4&PID_0321&MI_00"],
        ["HTC Multimedia Camera"],
        ["VIVE", "Camera"]
    ];
    public string[][] LovenseDetectors { get; init; } =
    [
        ["Lovense"],
        ["LVS-"]
    ];
    public bool UsePimaxServiceLogReconnectDetector { get; init; } = true;
    public bool UseMouthTrackerPnPReconnectDetector { get; init; } = true;
    public string PimaxServiceLogDirectory { get; init; } = @"%LOCALAPPDATA%\Pimax\PiService\Log";
    public int PimaxServiceLogReconnectLookbackLines { get; init; } = 400;
    public int PollIntervalSeconds { get; init; } = 2;
    public int StartupTimeoutSeconds { get; init; } = 30;
    public int StartupStableSeconds { get; init; } = 5;
    public int DelayBeforeVrcFaceTrackingSeconds { get; init; } = 5;
    public int DelayBeforeOscGoesBrrrSeconds { get; set; } = 5;
    public int DelayBeforeOscGoesBrrrrSeconds
    {
        set => DelayBeforeOscGoesBrrrSeconds = value;
    }
    public int RestartDelayAfterReconnectSeconds { get; init; } = 10;
    public int WatchedProcessCrashRelaunchGraceSeconds { get; init; } = 300;
    public int ShutdownGraceSeconds { get; init; } = 8;
    public int DeviceProbeTimeoutSeconds { get; init; } = 10;

    [JsonIgnore]
    public string? LoadedFromPath { get; private set; }

    public static SupervisorConfig Load(string? explicitPath = null)
    {
        var configPath = FindConfigPath(explicitPath);
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
        json = ReplaceJsonStringProperty(json, nameof(IntifacePath), IntifacePath);
        json = ReplaceJsonStringProperty(json, nameof(OscGoesBrrrPath), OscGoesBrrrPath);
        json = ReplaceJsonStringArrayProperty(json, nameof(BrokenEyeProcessNames), BrokenEyeProcessNames);
        json = ReplaceJsonStringArrayProperty(json, nameof(VrcFaceTrackingProcessNames), VrcFaceTrackingProcessNames);
        json = ReplaceJsonStringArrayProperty(json, nameof(IntifaceProcessNames), IntifaceProcessNames);
        json = ReplaceJsonStringArrayProperty(json, nameof(OscGoesBrrrProcessNames), OscGoesBrrrProcessNames);

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

    public bool TryGetTurnOffSecondaryMonitors(out bool turnOffSecondaryMonitors)
    {
        if (TurnOffSecondaryMonitors.ValueKind == JsonValueKind.True)
        {
            turnOffSecondaryMonitors = true;
            return true;
        }

        if (TurnOffSecondaryMonitors.ValueKind == JsonValueKind.False)
        {
            turnOffSecondaryMonitors = false;
            return true;
        }

        turnOffSecondaryMonitors = false;
        return false;
    }

    public void SetTurnOffSecondaryMonitors(bool turnOffSecondaryMonitors)
    {
        TurnOffSecondaryMonitors = JsonSerializer.SerializeToElement(turnOffSecondaryMonitors);
    }

    public void SaveTurnOffSecondaryMonitorsPreference()
    {
        var configPath = LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{\n}";

        json = ReplaceJsonBooleanOrStringProperty(
            json,
            nameof(TurnOffSecondaryMonitors),
            TryGetTurnOffSecondaryMonitors(out var value) && value);

        File.WriteAllText(configPath, json);
        Console.WriteLine($"Saved secondary monitor preference to: {configPath}");
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

    public void SaveBaseStationSettings()
    {
        var configPath = LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{\n}";

        json = ReplaceJsonValueProperty(json, nameof(BaseStations), JsonSerializer.Serialize(BaseStations, JsonOptions()));

        File.WriteAllText(configPath, json);
        Console.WriteLine($"Saved base station settings to: {configPath}");
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

    private static string ReplaceJsonValueProperty(string json, string propertyName, string valueJson)
    {
        var pattern = $"(\"{Regex.Escape(propertyName)}\"\\s*:\\s*)\\[(?:.|\\r|\\n)*?\\]";
        if (Regex.IsMatch(json, pattern))
        {
            return Regex.Replace(json, pattern, match => match.Groups[1].Value + valueJson, RegexOptions.Multiline);
        }

        var insertion = $"  \"{propertyName}\": {valueJson},\n";
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

    private static string? FindConfigPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var fullPath = Path.GetFullPath(explicitPath);
            return File.Exists(fullPath) ? fullPath : null;
        }

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

internal sealed class AutoLaunchAppConfig
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string[] ProcessNames { get; init; } = [];
    public bool Enabled { get; init; } = true;
    public bool? RestartOnPimaxReconnect { get; init; }
    public bool? CloseOnPimaxDisconnect { get; init; }
    public bool RunAsAdmin { get; init; }
    public bool StartMinimized { get; init; }
}

internal sealed class OscRoute
{
    public string Name { get; init; } = "";
    public int AppReceivePort { get; init; }
    public int OutputPort
    {
        init
        {
            if (AppReceivePort == 0)
            {
                AppReceivePort = value;
            }
        }
    }
    public bool Enabled { get; init; } = true;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Name) ? "OSC route" : Name;
            return $"{name} (127.0.0.1:{AppReceivePort})";
        }
    }
}
