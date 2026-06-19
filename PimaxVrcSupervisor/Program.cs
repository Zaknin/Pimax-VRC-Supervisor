using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using PimaxVrcSupervisor.BaseStations;
using Windows.Devices.Bluetooth.Advertisement;

using var shutdown = new CancellationTokenSource();
using var consoleLog = SupervisorConsoleLog.Install();

var commandLineArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var startupContext = StartupExecutionContext.Parse(commandLineArgs);
var desktopTuiStart = startupContext.DesktopTuiStart;
var launchDesktopTuiAfterReady = startupContext.LaunchDesktopTuiAfterReady;
var steamVrStart = startupContext.SteamVrStart;
var managedSteamVrSession = startupContext.ManagedSteamVrSession;
var watchVrchatAutoLaunch = startupContext.WatchVrchatAutoLaunch;
var applyStartupIntegration = startupContext.ApplyStartupIntegration;
var showStartupIntegrationResult = startupContext.ShowStartupIntegrationResult;
var desktopTuiDefaultInterface = startupContext.DesktopTuiDefaultInterface;
var explicitConfigSupplied = startupContext.ExplicitConfigSupplied;
var configPath = startupContext.ExplicitConfigPath;
if (commandLineArgs.Any(arg => string.Equals(arg, "base-station-startup-analysis-json", StringComparison.OrdinalIgnoreCase)))
{
    var request = PimaxVrcSupervisor.BaseStationStartupAnalysisRequest.Parse(commandLineArgs);
    var result = new PimaxVrcSupervisor.BaseStationStartupAnalyzer().Analyze(request);
    Console.WriteLine(JsonSerializer.Serialize(result, PimaxVrcSupervisor.BaseStationStartupAnalysisJson.Options));
    return;
}

if (commandLineArgs.Any(arg => string.Equals(arg, "pimax-connectivity-json", StringComparison.OrdinalIgnoreCase)))
{
    var diagnosticConfig = SupervisorConfig.Load(configPath);
    var snapshot = await new PimaxConnectivitySnapshotCollector().CollectAsync(diagnosticConfig, shutdown.Token);
    Console.WriteLine(JsonSerializer.Serialize(snapshot, PimaxConnectivityJson.Options));
    return;
}

if (commandLineArgs.Any(arg => string.Equals(arg, "pimax-usb-enumeration-json", StringComparison.OrdinalIgnoreCase)))
{
    var snapshot = new PimaxUsbEnumerationSnapshotCollector().Collect();
    Console.WriteLine(JsonSerializer.Serialize(snapshot, PimaxUsbEnumerationJson.Options));
    return;
}

if (commandLineArgs.Any(arg => string.Equals(arg, "pimax-registration-assessment-json", StringComparison.OrdinalIgnoreCase)))
{
    var diagnosticConfig = SupervisorConfig.Load(configPath);
    var snapshot = await new PimaxRegistrationAssessmentCoordinator().CollectAsync(diagnosticConfig, shutdown.Token);
    Console.WriteLine(JsonSerializer.Serialize(snapshot, PimaxRegistrationAssessmentJson.Options));
    return;
}

if (commandLineArgs.Any(arg => string.Equals(arg, "pimax-connect-lifecycle-observe-json", StringComparison.OrdinalIgnoreCase)))
{
    var diagnosticConfig = SupervisorConfig.Load(configPath);
    var request = PimaxConnectLifecycleObservationRequest.Parse(commandLineArgs);
    var result = await new PimaxConnectLifecycleObserver(diagnosticConfig).ObserveAsync(request, shutdown.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, PimaxConnectLifecycleObservationJson.Options));
    return;
}

if (commandLineArgs.Any(arg => string.Equals(arg, "pimax-usb-physical-port-map-json", StringComparison.OrdinalIgnoreCase)))
{
    var diagnosticConfig = SupervisorConfig.Load(configPath);
    var request = PimaxUsbPhysicalPortMapRequest.Parse(commandLineArgs);
    var result = await new PimaxUsbPhysicalPortMapper(diagnosticConfig).RunAsync(request, shutdown.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, PimaxUsbPhysicalPortMapJson.Options));
    return;
}

if (commandLineArgs.Any(arg => string.Equals(arg, "pimax-recovery-experiment-json", StringComparison.OrdinalIgnoreCase)))
{
    var diagnosticConfig = SupervisorConfig.Load(configPath);
    var request = BuildPimaxRecoveryExperimentRequest(commandLineArgs);
    var runner = new PimaxRecoveryExperimentRunner(
        new DefaultPimaxRegistrationAssessmentCollector(diagnosticConfig),
        new WindowsPimaxClientProcessController(),
        new DefaultPimaxRecoveryEnvironment());
    var result = await runner.RunAsync(request, shutdown.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, PimaxRecoveryExperimentJson.Options));
    return;
}

if (startupContext.ShouldHideConsole)
{
    ConsoleWindow.HideIfPresent();
}

if (startupContext.EmergencyBaseStationCleanup)
{
    var emergencyConfig = SupervisorConfig.Load(startupContext.EmergencyBaseStationCleanupConfigPath);
    await BaseStationEmergencyCleanup.RunAsync(emergencyConfig, TimeSpan.FromSeconds(startupContext.EmergencyBaseStationCleanupDelaySeconds), CancellationToken.None);
    return;
}

var config = SupervisorConfig.Load(configPath);
if (startupContext.InstallAutoLaunchTask)
{
    await InstallAutoLaunchScheduledTaskFromCommandLineAsync(config, shutdown.Token);
    return;
}
if (applyStartupIntegration)
{
    try
    {
        Console.WriteLine($"Pimax VRC Supervisor {AppVersion.Current}");
        Console.WriteLine("Applying startup integration.");
        if (!string.IsNullOrWhiteSpace(config.LoadedFromPath))
        {
            Console.WriteLine($"Config path: {config.LoadedFromPath}");
        }

        Console.WriteLine($"Startup mode: {config.GetEffectiveStartupLaunchMode()}");
        Console.WriteLine("This helper window closes automatically when the startup update finishes.");
        Console.WriteLine();
        Console.Out.Flush();
        await StartupIntegration.ApplyAsync(config, desktopTuiDefaultInterface, shutdown.Token);
        Console.WriteLine();
        Console.WriteLine("Startup integration update finished.");
        Console.Out.Flush();
        if (showStartupIntegrationResult)
        {
            ThemedPrompt.Show(
                "Startup integration was applied successfully.",
                "Pimax VRC Supervisor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
    catch (Exception ex)
    {
        if (showStartupIntegrationResult)
        {
            ThemedPrompt.Show(
                ex.Message,
                "Could not apply startup integration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        else
        {
            Console.Error.WriteLine(ex.Message);
        }

        Environment.ExitCode = 1;
    }

    return;
}
if (watchVrchatAutoLaunch)
{
    var skipCurrentSteamVrSession = commandLineArgs.Any(arg => string.Equals(arg, "--skip-current-vrserver-session", StringComparison.OrdinalIgnoreCase));
    await AutoLaunchWatcher.RunAsync(skipCurrentSteamVrSession, desktopTuiDefaultInterface, configPath, shutdown.Token);
    return;
}

using var supervisorMutex = new Mutex(initiallyOwned: true, @"Local\PimaxVrcSupervisor", out var ownsSupervisorMutex);
if (!ownsSupervisorMutex)
{
    Console.WriteLine("Pimax VRC Supervisor is already running. Exiting this duplicate instance.");
    return;
}

DirectLaunchMigrationResult? migrationResultForStartup = null;
if (startupContext.IsInteractiveSupervisorLaunch)
{
    var migrationResult = await DirectLaunchMigration.RunAsync(
        config,
        explicitConfigSupplied,
        configPath,
        shutdown.Token);
    if (!migrationResult.ContinueLaunch)
    {
        return;
    }

    migrationResultForStartup = migrationResult;
    config = migrationResult.Config;
}

var diagnosticsOptions = DiagnosticsOptions.ForSupervisor(config, commandLineArgs);
using var diagnostics = SupervisorDiagnosticsSession.Start(diagnosticsOptions);
var supervisor = new AppSupervisor(
    config,
    steamVrStart,
    managedSteamVrSession,
    launchDesktopTuiAfterReady,
    migrationResultForStartup?.AutoLaunchTaskBindingDeferredByUser ?? false,
    diagnostics,
    shutdown);
var supervisorStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    RunBlockingGracefulShutdown(supervisor, supervisorStopped.Task);
};
using var consoleCloseHandler = ConsoleCloseHandler.Register(
    shutdown,
    supervisorStopped.Task,
    supervisor.IsForcedManualReloadRequested,
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

static void RunBlockingGracefulShutdown(AppSupervisor supervisor, Task supervisorStopped)
{
    try
    {
        supervisor.RequestGracefulShutdownAsync("Ctrl+C", startInBackground: false).GetAwaiter().GetResult();
        supervisorStopped.Wait(TimeSpan.FromSeconds(60));
    }
    catch
    {
        // Console shutdown has a short OS-managed timeout; cleanup is best-effort.
    }
}

static async Task InstallAutoLaunchScheduledTaskFromCommandLineAsync(SupervisorConfig config, CancellationToken cancellationToken)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine("This supervisor is Windows-only.");
        return;
    }

    Console.WriteLine("Installing elevated auto-launch scheduled task...");
    var taskResult = await ScheduledTaskInstaller.CreateOrUpdateAsync(
        startWatcherImmediately: true,
        skipCurrentSteamVrSession: true,
        useDesktopTuiDefaultInterface: null,
        config.LoadedFromPath,
        cancellationToken);
    config.SetAutoLaunchScheduledTask(true);
    config.SaveAutoLaunchScheduledTaskPreference();
    Console.WriteLine(taskResult.OperatorMessage);
    Console.WriteLine($"Task: {taskResult.TaskName}");
    Console.WriteLine($"Trigger: {taskResult.TriggerDescription}");
}

static PimaxRecoveryExperimentRequest BuildPimaxRecoveryExperimentRequest(string[] args)
{
    var experiment = TryGetTopLevelCommandOption(args, "--experiment", out var experimentValue)
        && !string.IsNullOrWhiteSpace(experimentValue)
        ? experimentValue.Trim()
        : PimaxRecoveryExperimentKind.WaitControl;
    var duration = TryGetTopLevelCommandOption(args, "--duration-seconds", out var durationText)
        && int.TryParse(durationText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDuration)
        ? parsedDuration
        : 30;
    var token = TryGetTopLevelCommandOption(args, "--confirmation-token", out var tokenValue)
        ? tokenValue
        : null;
    var evidenceDirectory = TryGetTopLevelCommandOption(args, "--evidence-dir", out var evidenceDirectoryValue)
        ? evidenceDirectoryValue
        : null;
    return new PimaxRecoveryExperimentRequest(
        experiment,
        HasTopLevelFlag(args, "--confirm"),
        token,
        duration,
        evidenceDirectory);
}

static bool HasTopLevelFlag(string[] args, string name)
    => args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

static bool TryGetTopLevelCommandOption(string[] args, string name, out string? value)
{
    value = null;
    var prefix = name + "=";
    for (var index = 0; index < args.Length; index++)
    {
        if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = args[index][prefix.Length..];
            return true;
        }

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

internal static class AppVersion
{
    public static string Current =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}

internal sealed record DirectLaunchMigrationResult(
    SupervisorConfig Config,
    bool ContinueLaunch,
    bool AutoLaunchTaskBindingDeferredByUser);

internal enum TaskMigrationDecision
{
    None,
    Deferred,
    Rebound,
    RemovedOrDisabled
}

internal sealed record DirectLaunchTaskMigrationResult(
    TaskMigrationDecision Decision,
    bool AutoLaunchTaskBindingDeferredByUser)
{
    public static DirectLaunchTaskMigrationResult None { get; } = new(
        TaskMigrationDecision.None,
        AutoLaunchTaskBindingDeferredByUser: false);

    public static DirectLaunchTaskMigrationResult Rebound { get; } = new(
        TaskMigrationDecision.Rebound,
        AutoLaunchTaskBindingDeferredByUser: false);

    public static DirectLaunchTaskMigrationResult RemovedOrDisabled { get; } = new(
        TaskMigrationDecision.RemovedOrDisabled,
        AutoLaunchTaskBindingDeferredByUser: false);
}

internal static class DirectLaunchMigration
{
    public static async Task<DirectLaunchMigrationResult> RunAsync(
        SupervisorConfig config,
        bool explicitConfigSupplied,
        string? explicitConfigPath,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            "direct_launch_migration; interactive=True"
            + $"; explicitConfigSupplied={explicitConfigSupplied}"
            + $"; loadedConfig={FormatPath(config.LoadedFromPath)}");

        var resolvedConfig = await ResolveConfigAsync(config, explicitConfigSupplied, explicitConfigPath, cancellationToken);
        if (resolvedConfig is null)
        {
            return new DirectLaunchMigrationResult(
                new SupervisorConfig(),
                ContinueLaunch: false,
                AutoLaunchTaskBindingDeferredByUser: false);
        }

        var taskResult = await ResolveScheduledTasksAsync(resolvedConfig, cancellationToken);
        return new DirectLaunchMigrationResult(
            resolvedConfig,
            ContinueLaunch: true,
            AutoLaunchTaskBindingDeferredByUser: taskResult.AutoLaunchTaskBindingDeferredByUser);
    }

    private static async Task<SupervisorConfig?> ResolveConfigAsync(
        SupervisorConfig config,
        bool explicitConfigSupplied,
        string? explicitConfigPath,
        CancellationToken cancellationToken)
    {
        var releaseDirectory = AppContext.BaseDirectory;
        var loadedExternal = !string.IsNullOrWhiteSpace(config.LoadedFromPath)
            && !ConfigMigrationSupport.IsPathInDirectory(config.LoadedFromPath, releaseDirectory);

        if (explicitConfigSupplied)
        {
            Console.WriteLine(
                "config_migration; origin=explicit"
                + $"; supplied={FormatPath(explicitConfigPath)}"
                + $"; loaded={FormatPath(config.LoadedFromPath)}"
                + $"; external={loadedExternal}");
            if (!loadedExternal || string.IsNullOrWhiteSpace(config.LoadedFromPath))
            {
                return config;
            }

            var choice = await ShowConfigMigrationPromptAsync(
                "Use the configuration from another folder?",
                "The Supervisor was started with an explicit configuration file outside this release folder.\r\n\r\nYou can import a copy into this release, or keep using the supplied file for this launch.",
                "Import Copy",
                "Use Supplied File",
                cancellationToken);
            if (choice == ConfigMigrationChoice.Cancel)
            {
                Console.WriteLine("config_migration; result=cancelled");
                return null;
            }

            if (choice == ConfigMigrationChoice.KeepExternal)
            {
                Console.WriteLine("config_migration; result=kept-explicit; path=" + FormatPath(config.LoadedFromPath));
                return config;
            }

            return ImportAndReload(config.LoadedFromPath, releaseDirectory);
        }

        var currentConfigIncomplete = string.IsNullOrWhiteSpace(config.LoadedFromPath)
            || (config.RunInitialSetupQuestions && !config.AreInitialSetupQuestionsComplete());
        if (!currentConfigIncomplete)
        {
            Console.WriteLine("config_migration; result=current-config-complete; path=" + FormatPath(config.LoadedFromPath));
            return config;
        }

        var candidates = ConfigMigrationSupport.FindCandidates(
            releaseDirectory,
            explicitConfigPath: null,
            additionalCandidates: [config.LoadedFromPath]);
        var candidate = candidates.FirstOrDefault();
        Console.WriteLine(
            "config_migration; result=candidate-scan"
            + $"; candidateFound={candidate is not null}"
            + $"; candidate={FormatPath(candidate?.Path)}"
            + $"; source={candidate?.Source ?? "none"}");
        if (candidate is null)
        {
            return config;
        }

        var importChoice = await ShowConfigMigrationPromptAsync(
            "Import an existing configuration?",
            $"This release does not have a completed setup yet.\r\n\r\nA previous configuration was found:\r\n{candidate.Path}\r\n\r\nImport a copy into this release folder?",
            "Import Copy",
            "Start New",
            cancellationToken);
        if (importChoice == ConfigMigrationChoice.Cancel)
        {
            Console.WriteLine("config_migration; result=cancelled");
            return null;
        }

        if (importChoice == ConfigMigrationChoice.KeepExternal)
        {
            Console.WriteLine("config_migration; result=start-new");
            return config;
        }

        return ImportAndReload(candidate.Path, releaseDirectory);
    }

    private static SupervisorConfig? ImportAndReload(string sourcePath, string releaseDirectory)
    {
        var import = ConfigMigrationSupport.ImportConfig(sourcePath, releaseDirectory);
        Console.WriteLine(
            "config_migration; result=" + import.Outcome
            + $"; source={FormatPath(import.SourcePath)}"
            + $"; destination={FormatPath(import.DestinationPath)}"
            + $"; message={import.Message}");
        if (import.Outcome != ConfigMigrationOutcome.Imported
            || string.IsNullOrWhiteSpace(import.DestinationPath))
        {
            ThemedPrompt.Show(
                import.Message,
                "Could not import configuration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }

        return SupervisorConfig.Load(import.DestinationPath);
    }

    private static async Task<DirectLaunchTaskMigrationResult> ResolveScheduledTasksAsync(SupervisorConfig config, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return DirectLaunchTaskMigrationResult.None;
        }

        var currentDirectory = ScheduledTaskPathValidator.GetCurrentExecutableDirectory();
        var issues = await ScheduledTaskPathValidator.ValidateExistingManagedTasksAsync(currentDirectory, cancellationToken);
        var recognizedIssues = issues
            .Where(issue => ScheduledTaskPathValidator.ManagedTaskNames.Contains(issue.TaskName, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (recognizedIssues.Length == 0)
        {
            Console.WriteLine("task_migration; result=no-managed-task-issues");
            return DirectLaunchTaskMigrationResult.None;
        }

        Console.WriteLine("task_migration; recognizedIssues=" + string.Join(" | ", recognizedIssues.Select(FormatTaskIssue)));
        var message = "These Pimax VRC Supervisor startup tasks point to another release folder:\r\n\r\n"
            + string.Join("\r\n", recognizedIssues.Select(issue => $"- {issue.TaskName}: {issue.TaskExecutablePath}"))
            + "\r\n\r\nRebind updates recognized Pimax tasks to this release. Turn Off disables only the listed Pimax startup integrations.";
        var choice = await ShowTaskMigrationPromptAsync(message, cancellationToken);
        switch (choice)
        {
            case TaskMigrationChoice.Rebind:
                await RebindTasksAsync(config, recognizedIssues, cancellationToken);
                return DirectLaunchTaskMigrationResult.Rebound;
            case TaskMigrationChoice.TurnOff:
                await TurnOffTasksAsync(config, recognizedIssues, cancellationToken);
                return DirectLaunchTaskMigrationResult.RemovedOrDisabled;
            default:
                Console.WriteLine("task_migration; result=deferred");
                return new DirectLaunchTaskMigrationResult(
                    TaskMigrationDecision.Deferred,
                    AutoLaunchTaskBindingDeferredByUser: recognizedIssues.Any(issue =>
                        string.Equals(issue.TaskName, ScheduledTaskPathValidator.AutoLaunchTaskName, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static async Task RebindTasksAsync(
        SupervisorConfig config,
        IReadOnlyCollection<ScheduledTaskPathValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        if (issues.Any(issue => string.Equals(issue.TaskName, ScheduledTaskPathValidator.SteamVrStartTaskName, StringComparison.OrdinalIgnoreCase))
            && !issues.Any(issue => string.Equals(issue.TaskName, ScheduledTaskPathValidator.AutoLaunchTaskName, StringComparison.OrdinalIgnoreCase)))
        {
            config.AutoLaunchScheduledTask = JsonSerializer.SerializeToElement(false);
            config.StartupLaunchMode = StartupLaunchMode.SteamVrManifest;
            config.StopWithSteamVr = true;
            config.SaveAutoLaunchScheduledTaskPreference();
            await ScheduledTaskInstaller.DeleteAutoLaunchTaskAsync(cancellationToken);
            var details = await SteamVrStartupInstaller.CreateOrUpdateAsync(cancellationToken);
            Console.WriteLine($"task_migration; outcome=Rebound; task={details.AppKey}; manifest={details.ManifestPath}");
            return;
        }

        config.SetAutoLaunchScheduledTask(true);
        config.SaveAutoLaunchScheduledTaskPreference();
        await SteamVrStartupInstaller.DisableAsync(cancellationToken);
        await ScheduledTaskInstaller.DeleteSteamVrStartHelperAsync(cancellationToken);
        var result = await ScheduledTaskInstaller.CreateOrUpdateAsync(
            startWatcherImmediately: false,
            skipCurrentSteamVrSession: true,
            useDesktopTuiDefaultInterface: null,
            config.LoadedFromPath,
            cancellationToken);
        Console.WriteLine("task_migration; outcome=" + result.Outcome + $"; task={result.TaskName}; message={result.OperatorMessage}");
    }

    private static async Task TurnOffTasksAsync(
        SupervisorConfig config,
        IReadOnlyCollection<ScheduledTaskPathValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        if (issues.Any(issue => string.Equals(issue.TaskName, ScheduledTaskPathValidator.AutoLaunchTaskName, StringComparison.OrdinalIgnoreCase)))
        {
            await ScheduledTaskInstaller.DeleteAutoLaunchTaskAsync(cancellationToken);
            Console.WriteLine("task_migration; outcome=RemovedOrDisabled; task=" + ScheduledTaskPathValidator.AutoLaunchTaskName);
        }

        if (issues.Any(issue => string.Equals(issue.TaskName, ScheduledTaskPathValidator.SteamVrStartTaskName, StringComparison.OrdinalIgnoreCase)))
        {
            await SteamVrStartupInstaller.DisableAsync(cancellationToken);
            await ScheduledTaskInstaller.DeleteSteamVrStartHelperAsync(cancellationToken);
            Console.WriteLine("task_migration; outcome=RemovedOrDisabled; task=" + ScheduledTaskPathValidator.SteamVrStartTaskName);
        }

        config.StartupLaunchMode = StartupLaunchMode.None;
        config.SetAutoLaunchScheduledTask(false);
        config.SaveAutoLaunchScheduledTaskPreference();
    }

    private static Task<ConfigMigrationChoice> ShowConfigMigrationPromptAsync(
        string title,
        string message,
        string acceptText,
        string declineText,
        CancellationToken cancellationToken)
        => ShowPromptAsync(
            () => ThemedPrompt.Show(
                message,
                title,
                [
                    new(acceptText, DialogResult.Yes),
                    new(declineText, DialogResult.No),
                    new("Cancel", DialogResult.Cancel)
                ],
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1),
            result => result switch
            {
                DialogResult.Yes => ConfigMigrationChoice.Import,
                DialogResult.No => ConfigMigrationChoice.KeepExternal,
                _ => ConfigMigrationChoice.Cancel
            },
            cancellationToken);

    private static Task<TaskMigrationChoice> ShowTaskMigrationPromptAsync(
        string message,
        CancellationToken cancellationToken)
        => ShowPromptAsync(
            () => ThemedPrompt.Show(
                message,
                "Startup Integration",
                [
                    new("Rebind", DialogResult.Yes),
                    new("Turn Off", DialogResult.No),
                    new("Later", DialogResult.Cancel)
                ],
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1),
            result => result switch
            {
                DialogResult.Yes => TaskMigrationChoice.Rebind,
                DialogResult.No => TaskMigrationChoice.TurnOff,
                _ => TaskMigrationChoice.Defer
            },
            cancellationToken);

    private static Task<T> ShowPromptAsync<T>(
        Func<DialogResult> showPrompt,
        Func<DialogResult, T> mapResult,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                completion.TrySetResult(mapResult(showPrompt()));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    private static string FormatTaskIssue(ScheduledTaskPathValidationIssue issue)
        => $"{issue.TaskName}: {issue.TaskExecutablePath} -> {issue.Message}";

    private static string FormatPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? "(none)" : path;

    private enum ConfigMigrationChoice
    {
        Import,
        KeepExternal,
        Cancel
    }

    private enum TaskMigrationChoice
    {
        Rebind,
        TurnOff,
        Defer
    }
}

internal sealed record DiagnosticsOptions(
    string Role,
    bool Enabled,
    bool Verbose,
    bool DebugEnabled,
    TimeSpan SummaryInterval,
    string LogDirectory)
{
    public static DiagnosticsOptions ForSupervisor(SupervisorConfig config, string[] args)
    {
        var enabled = config.DiagnosticsLogSupervisor || HasFlag(args, "--diagnostics");
        var verbose = config.DiagnosticsVerbose || HasFlag(args, "--diagnostics-verbose");
        var debugEnabled = config.DiagnosticsDebugSupervisor;
        var logDirectory = TryGetCommandOption(args, "--diagnostics-log-dir", out var requestedDirectory)
            && !string.IsNullOrWhiteSpace(requestedDirectory)
            ? requestedDirectory
            : config.DiagnosticsLogDirectory;
        return new DiagnosticsOptions(
            "supervisor",
            enabled,
            verbose,
            debugEnabled,
            TimeSpan.FromSeconds(Math.Max(1, config.DiagnosticsSummaryIntervalSeconds)),
            logDirectory);
    }

    public string ResolveLogDirectory()
    {
        var directory = string.IsNullOrWhiteSpace(LogDirectory)
            ? Path.Combine(Path.GetTempPath(), "PimaxVrcSupervisorDiagnostics")
            : Environment.ExpandEnvironmentVariables(LogDirectory.Trim());
        return Path.GetFullPath(directory);
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetCommandOption(string[] args, string name, out string? value)
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
}

internal sealed class DiagnosticTextLog : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter? _writer;
    private bool _disposed;

    private DiagnosticTextLog(DiagnosticsOptions options)
    {
        Enabled = options.Enabled;
        Verbose = options.Verbose;
        SummaryInterval = options.SummaryInterval;
        if (!Enabled)
        {
            return;
        }

        Directory.CreateDirectory(options.ResolveLogDirectory());
        Path = System.IO.Path.Combine(
            options.ResolveLogDirectory(),
            $"{options.Role}-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}-pid{Environment.ProcessId}.log");
        _writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        Write("diagnostics started; role=" + options.Role + "; verbose=" + Verbose);
    }

    public bool Enabled { get; }
    public bool Verbose { get; }
    public TimeSpan SummaryInterval { get; }
    public string? Path { get; }

    public static DiagnosticTextLog Create(DiagnosticsOptions options) => new(options);

    public void Write(string message)
    {
        if (!Enabled || _writer is null)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}");
            _writer.Flush();
        }
    }

    public void WriteVerbose(string message)
    {
        if (Verbose)
        {
            Write(message);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (_writer is not null)
            {
                _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} diagnostics stopped");
                _writer.Dispose();
            }

            _disposed = true;
        }
    }
}

internal sealed class DebugLogSession : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter? _writer;
    private bool _disposed;

    private DebugLogSession(DiagnosticsOptions options)
    {
        Enabled = options.DebugEnabled;
        if (!Enabled)
        {
            return;
        }

        Directory.CreateDirectory(options.ResolveLogDirectory());
        Path = System.IO.Path.Combine(
            options.ResolveLogDirectory(),
            $"{options.Role}-debug-{DateTime.Now:yyyyMMdd-HHmmss}-pid{Environment.ProcessId}.log");
        _writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        Write("debug started; role=" + options.Role + "; diagnosticsVerbose=" + options.Verbose);
    }

    public bool Enabled { get; }
    public string? Path { get; }

    public static DebugLogSession Create(DiagnosticsOptions options) => new(options);

    public void Write(string message)
    {
        if (!Enabled || _writer is null)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}");
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (_writer is not null)
            {
                _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} debug stopped");
                _writer.Dispose();
            }

            _disposed = true;
        }
    }
}

internal sealed class OperationStats
{
    private long _count;
    private long _failures;
    private long _totalTicks;
    private long _maxTicks;

    public void Record(TimeSpan elapsed, bool success = true)
    {
        Interlocked.Increment(ref _count);
        if (!success)
        {
            Interlocked.Increment(ref _failures);
        }

        Interlocked.Add(ref _totalTicks, elapsed.Ticks);
        var ticks = elapsed.Ticks;
        while (true)
        {
            var current = Volatile.Read(ref _maxTicks);
            if (ticks <= current || Interlocked.CompareExchange(ref _maxTicks, ticks, current) == current)
            {
                break;
            }
        }
    }

    public OperationStatsSnapshot SnapshotAndReset()
    {
        var count = Interlocked.Exchange(ref _count, 0);
        var failures = Interlocked.Exchange(ref _failures, 0);
        var totalTicks = Interlocked.Exchange(ref _totalTicks, 0);
        var maxTicks = Interlocked.Exchange(ref _maxTicks, 0);
        return new OperationStatsSnapshot(count, failures, TimeSpan.FromTicks(totalTicks), TimeSpan.FromTicks(maxTicks));
    }
}

internal readonly record struct OperationStatsSnapshot(long Count, long Failures, TimeSpan Total, TimeSpan Max)
{
    public double AverageMilliseconds => Count == 0 ? 0 : Total.TotalMilliseconds / Count;
}

internal sealed class SupervisorDiagnosticsSession : IDisposable
{
    private static SupervisorDiagnosticsSession? s_current;
    private readonly DiagnosticTextLog _log;
    private readonly DebugLogSession _debugLog;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly OperationStats _mainLoop = new();
    private readonly OperationStats _processDetection = new();
    private readonly OperationStats _pimaxLogScan = new();
    private readonly OperationStats _baseStationWakeRoutine = new();
    private readonly OperationStats _baseStationWakeNoop = new();
    private readonly OperationStats _baseStationPowerDownRoutine = new();
    private readonly OperationStats _coreAppStart = new();
    private readonly OperationStats _coreAppRestart = new();
    private readonly ConcurrentDictionary<string, OperationStats> _commands = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastSummaryAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastCpuSampleAt = DateTimeOffset.UtcNow;
    private TimeSpan _lastCpuTime;
    private long _reconnectEvents;
    private bool _disposed;

    private SupervisorDiagnosticsSession(DiagnosticTextLog log, DebugLogSession debugLog)
    {
        _log = log;
        _debugLog = debugLog;
        _lastCpuTime = _process.TotalProcessorTime;
        if (_log.Enabled)
        {
            s_current = this;
            _log.Write("supervisor diagnostics file=" + _log.Path);
            if (_debugLog.Enabled)
            {
                _log.Write("supervisor debug file=" + _debugLog.Path);
            }
        }

        if (_debugLog.Enabled)
        {
            _debugLog.Write("supervisor diagnostics file=" + (_log.Path ?? "none"));
            _debugLog.Write("supervisor debug file=" + _debugLog.Path);
        }
    }

    public bool Enabled => _log.Enabled;
    public bool Verbose => _log.Verbose;
    public bool DebugEnabled => _debugLog.Enabled;
    public string? DiagnosticsPath => _log.Path;
    public string? DebugPath => _debugLog.Path;

    public static SupervisorDiagnosticsSession Start(DiagnosticsOptions options)
        => new(DiagnosticTextLog.Create(options), DebugLogSession.Create(options));

    public static void RecordProcessDetectionStatic(TimeSpan elapsed, int returnedProcessCount, string[] processNames)
    {
        var current = Volatile.Read(ref s_current);
        if (current is null || !current.Enabled)
        {
            return;
        }

        current._processDetection.Record(elapsed);
        if (current.Verbose && elapsed > TimeSpan.FromMilliseconds(25))
        {
            current._log.WriteVerbose($"slow process detection; elapsedMs={elapsed.TotalMilliseconds:0.0}; returned={returnedProcessCount}; names={string.Join(",", processNames)}");
        }
    }

    public void RecordMainLoop(TimeSpan elapsed)
        => _mainLoop.Record(elapsed);

    public void RecordPimaxLogScan(TimeSpan elapsed, bool foundReconnect)
    {
        _pimaxLogScan.Record(elapsed);
        if (foundReconnect)
        {
            Interlocked.Increment(ref _reconnectEvents);
        }

        if (Verbose && (foundReconnect || elapsed > TimeSpan.FromMilliseconds(25)))
        {
            _log.WriteVerbose($"pimax log scan; elapsedMs={elapsed.TotalMilliseconds:0.0}; foundReconnect={foundReconnect}");
        }
    }

    public void RecordCommand(string command, TimeSpan elapsed, bool success)
    {
        _commands.GetOrAdd(command, _ => new OperationStats()).Record(elapsed, success);
        if (Verbose || elapsed > TimeSpan.FromMilliseconds(250))
        {
            _log.Write($"command; name={command}; elapsedMs={elapsed.TotalMilliseconds:0.0}; success={success}");
        }
    }

    public void RecordBaseStationWakeRoutine(TimeSpan elapsed)
        => _baseStationWakeRoutine.Record(elapsed);

    public void RecordBaseStationWakeNoop(TimeSpan elapsed)
        => _baseStationWakeNoop.Record(elapsed);

    public void RecordBaseStationPowerDownRoutine(TimeSpan elapsed)
        => _baseStationPowerDownRoutine.Record(elapsed);

    public void RecordCoreAppStart(TimeSpan elapsed)
        => _coreAppStart.Record(elapsed);

    public void RecordCoreAppRestart(TimeSpan elapsed)
        => _coreAppRestart.Record(elapsed);

    public void WriteVerbose(string message)
        => _log.WriteVerbose(message);

    public void WriteEvent(string message)
        => _log.Write(message);

    public bool ShouldWriteCommandDebug(string command)
        => DebugEnabled && (Verbose || !IsRoutineDashboardPollCommand(command));

    public void WriteDebug(string message)
        => _debugLog.Write(message);

    public void WriteSummaryIfDue(string context)
    {
        if (!Enabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastSummaryAt < _log.SummaryInterval)
        {
            return;
        }

        _lastSummaryAt = now;
        _process.Refresh();
        var cpuPercent = CalculateCpuPercent(now);
        var mainLoop = _mainLoop.SnapshotAndReset();
        var processDetection = _processDetection.SnapshotAndReset();
        var pimaxLogScan = _pimaxLogScan.SnapshotAndReset();
        var baseStationWakeRoutine = _baseStationWakeRoutine.SnapshotAndReset();
        var baseStationWakeNoop = _baseStationWakeNoop.SnapshotAndReset();
        var baseStationPowerDownRoutine = _baseStationPowerDownRoutine.SnapshotAndReset();
        var coreAppStart = _coreAppStart.SnapshotAndReset();
        var coreAppRestart = _coreAppRestart.SnapshotAndReset();
        var commandSummary = string.Join(
            "; ",
            _commands.OrderBy(pair => pair.Key).Select(pair =>
            {
                var snapshot = pair.Value.SnapshotAndReset();
                return $"{pair.Key}:count={snapshot.Count},fail={snapshot.Failures},avgMs={snapshot.AverageMilliseconds:0.0},maxMs={snapshot.Max.TotalMilliseconds:0.0}";
            }));

        _log.Write(
            "summary"
            + $"; context={context}"
            + $"; cpuPct={cpuPercent:0.0}"
            + $"; workingSetMb={BytesToMegabytes(_process.WorkingSet64):0.0}"
            + $"; privateMb={BytesToMegabytes(_process.PrivateMemorySize64):0.0}"
            + $"; gcHeapMb={BytesToMegabytes(GC.GetTotalMemory(false)):0.0}"
            + $"; gc0={GC.CollectionCount(0)}; gc1={GC.CollectionCount(1)}; gc2={GC.CollectionCount(2)}"
            + $"; threads={SafeThreadCount()}; handles={SafeHandleCount()}"
            + $"; mainLoop=count={mainLoop.Count},avgMs={mainLoop.AverageMilliseconds:0.0},maxMs={mainLoop.Max.TotalMilliseconds:0.0}"
            + $"; processDetection=count={processDetection.Count},avgMs={processDetection.AverageMilliseconds:0.0},maxMs={processDetection.Max.TotalMilliseconds:0.0}"
            + $"; pimaxLogScan=count={pimaxLogScan.Count},avgMs={pimaxLogScan.AverageMilliseconds:0.0},maxMs={pimaxLogScan.Max.TotalMilliseconds:0.0},reconnects={Interlocked.Exchange(ref _reconnectEvents, 0)}"
            + $"; baseStationWakeRoutine=count={baseStationWakeRoutine.Count},avgMs={baseStationWakeRoutine.AverageMilliseconds:0.0},maxMs={baseStationWakeRoutine.Max.TotalMilliseconds:0.0}"
            + $"; baseStationWakeNoop=count={baseStationWakeNoop.Count},avgMs={baseStationWakeNoop.AverageMilliseconds:0.0},maxMs={baseStationWakeNoop.Max.TotalMilliseconds:0.0}"
            + $"; baseStationPowerDownRoutine=count={baseStationPowerDownRoutine.Count},avgMs={baseStationPowerDownRoutine.AverageMilliseconds:0.0},maxMs={baseStationPowerDownRoutine.Max.TotalMilliseconds:0.0}"
            + $"; coreAppStart=count={coreAppStart.Count},avgMs={coreAppStart.AverageMilliseconds:0.0},maxMs={coreAppStart.Max.TotalMilliseconds:0.0}"
            + $"; coreAppRestart=count={coreAppRestart.Count},avgMs={coreAppRestart.AverageMilliseconds:0.0},maxMs={coreAppRestart.Max.TotalMilliseconds:0.0}"
            + $"; commands=[{commandSummary}]");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }

        if (Enabled)
        {
            WriteSummaryIfDue("dispose");
        }

        _log.Dispose();
        _debugLog.Dispose();
        _process.Dispose();
    }

    private double CalculateCpuPercent(DateTimeOffset now)
    {
        var totalCpu = _process.TotalProcessorTime;
        var cpuDelta = totalCpu - _lastCpuTime;
        var wallDelta = now - _lastCpuSampleAt;
        _lastCpuTime = totalCpu;
        _lastCpuSampleAt = now;
        if (wallDelta.TotalMilliseconds <= 0)
        {
            return 0;
        }

        return cpuDelta.TotalMilliseconds / wallDelta.TotalMilliseconds / Environment.ProcessorCount * 100.0;
    }

    private int SafeThreadCount()
    {
        try
        {
            return _process.Threads.Count;
        }
        catch
        {
            return -1;
        }
    }

    private int SafeHandleCount()
    {
        try
        {
            return _process.HandleCount;
        }
        catch
        {
            return -1;
        }
    }

    private static double BytesToMegabytes(long bytes)
        => bytes / 1024.0 / 1024.0;

    private static bool IsRoutineDashboardPollCommand(string command)
        => string.Equals(command, "status", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "log", StringComparison.OrdinalIgnoreCase);
}

internal static class ConsoleWindow
{
    private const int ShowWindowHide = 0;
    private const int ShowWindowShow = 5;

    public static void HideIfPresent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
        {
            ShowWindow(consoleWindow, ShowWindowHide);
        }
    }

    public static void ShowIfPresent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
        {
            ShowWindow(consoleWindow, ShowWindowShow);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}

internal sealed record ThemedPromptButton(string Text, DialogResult Result);

internal sealed class ThemedPromptButtonControl : Button
{
    private const int Radius = 4;
    private bool _hovered;
    private bool _pressed;

    public ThemedPromptButtonControl()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        UseVisualStyleBackColor = false;
        Padding = new Padding(8, 2, 8, 2);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var backColor = BackColor;
        if (!Enabled)
        {
            backColor = SystemColors.Control;
        }
        else if (_pressed)
        {
            backColor = ColorOrFallback(FlatAppearance.MouseDownBackColor, BackColor);
        }
        else if (_hovered)
        {
            backColor = ColorOrFallback(FlatAppearance.MouseOverBackColor, BackColor);
        }

        using var path = CreateRoundedRectanglePath(bounds, Radius);
        using var background = new SolidBrush(backColor);
        using var border = new Pen(Enabled ? ColorOrFallback(FlatAppearance.BorderColor, SystemColors.ControlDark) : SystemColors.ControlDark);
        pevent.Graphics.FillPath(background, path);
        pevent.Graphics.DrawPath(border, path);

        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            bounds,
            Enabled ? ForeColor : SystemColors.GrayText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(pevent.Graphics, Rectangle.Inflate(bounds, -4, -4), ForeColor, backColor);
        }
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ColorOrFallback(Color color, Color fallback)
        => color.IsEmpty ? fallback : color;
}

internal static class ThemedPrompt
{
    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    private const int DwmWindowAttributeUseImmersiveDarkModeBefore20H1 = 19;

    public static DialogResult Show(
        string message,
        string title,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        var promptButtons = buttons switch
        {
            MessageBoxButtons.OK => new[] { new ThemedPromptButton("OK", DialogResult.OK) },
            MessageBoxButtons.YesNo => new[]
            {
                new ThemedPromptButton("Yes", DialogResult.Yes),
                new ThemedPromptButton("No", DialogResult.No)
            },
            _ => throw new NotSupportedException($"Unsupported prompt button set: {buttons}")
        };

        return Show(message, title, promptButtons, icon, defaultButton);
    }

    public static DialogResult Show(
        string message,
        string title,
        IReadOnlyList<ThemedPromptButton> buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return ShowOnCurrentThread(message, title, buttons, icon, defaultButton);
        }

        var completion = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                completion.SetResult(ShowOnCurrentThread(message, title, buttons, icon, defaultButton));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.GetAwaiter().GetResult();
    }

    private static DialogResult ShowOnCurrentThread(
        string message,
        string title,
        IReadOnlyList<ThemedPromptButton> buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        var dark = IsDarkThemeEnabled();
        var backColor = dark ? Color.FromArgb(31, 31, 31) : SystemColors.Control;
        var textColor = dark ? Color.FromArgb(245, 245, 245) : SystemColors.ControlText;
        var mutedTextColor = dark ? Color.FromArgb(218, 218, 218) : SystemColors.ControlText;
        var buttonBackColor = dark ? Color.FromArgb(55, 55, 55) : SystemColors.Control;
        var buttonBorderColor = dark ? Color.FromArgb(85, 85, 85) : SystemColors.ControlDark;

        using var form = new Form
        {
            Text = string.IsNullOrWhiteSpace(title) ? "Pimax VRC Supervisor" : title,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = true,
            TopMost = true,
            BackColor = backColor,
            ForeColor = textColor,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(22),
            MinimumSize = new Size(440, 0),
            Font = SystemFonts.MessageBoxFont
        };

        form.HandleCreated += (_, _) => ApplyDarkTitleBar(form.Handle, dark);
        form.Shown += (_, _) =>
        {
            form.Activate();
            form.BringToFront();
        };

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Dock = DockStyle.Fill,
            BackColor = backColor,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var iconPicture = new PictureBox
        {
            Image = GetIconBitmap(icon),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Width = 44,
            Height = 44,
            Margin = new Padding(0, 4, 18, 0),
            BackColor = backColor
        };
        root.Controls.Add(iconPicture, 0, 0);

        var messageLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Text = message,
            ForeColor = mutedTextColor,
            BackColor = backColor,
            Margin = new Padding(0, 0, 0, 24)
        };
        root.Controls.Add(messageLabel, 1, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            BackColor = backColor,
            Padding = new Padding(0, 10, 0, 0),
            Margin = new Padding(0)
        };
        root.SetColumnSpan(buttonPanel, 2);
        root.Controls.Add(buttonPanel, 0, 1);

        Button? defaultControl = null;
        for (var index = buttons.Count - 1; index >= 0; index--)
        {
            var promptButton = buttons[index];
            var button = new ThemedPromptButtonControl
            {
                Text = promptButton.Text,
                DialogResult = promptButton.Result,
                AutoSize = false,
                Width = Math.Max(96, TextRenderer.MeasureText(promptButton.Text, SystemFonts.MessageBoxFont).Width + 28),
                Height = 32,
                Margin = new Padding(8, 0, 0, 0),
                BackColor = buttonBackColor,
                ForeColor = textColor,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = buttonBorderColor;
            button.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(70, 70, 70) : SystemColors.ButtonHighlight;
            button.FlatAppearance.MouseDownBackColor = dark ? Color.FromArgb(45, 45, 45) : SystemColors.ControlDark;
            buttonPanel.Controls.Add(button);

            var buttonOrdinal = index + 1;
            if (MatchesDefaultButton(defaultButton, buttonOrdinal))
            {
                defaultControl = button;
            }
        }

        form.Controls.Add(root);
        form.AcceptButton = defaultControl;
        form.CancelButton = buttons.Any(button => button.Result == DialogResult.No)
            ? buttonPanel.Controls.OfType<Button>().FirstOrDefault(button => button.DialogResult == DialogResult.No)
            : buttonPanel.Controls.OfType<Button>().FirstOrDefault(button => button.DialogResult == DialogResult.OK);

        return form.ShowDialog();
    }

    private static bool MatchesDefaultButton(MessageBoxDefaultButton defaultButton, int ordinal)
        => defaultButton switch
        {
            MessageBoxDefaultButton.Button1 => ordinal == 1,
            MessageBoxDefaultButton.Button2 => ordinal == 2,
            MessageBoxDefaultButton.Button3 => ordinal == 3,
            _ => ordinal == 1
        };

    private static Bitmap GetIconBitmap(MessageBoxIcon icon)
        => icon switch
        {
            MessageBoxIcon.Error => SystemIcons.Error.ToBitmap(),
            MessageBoxIcon.Warning => SystemIcons.Warning.ToBitmap(),
            MessageBoxIcon.Information => SystemIcons.Information.ToBitmap(),
            MessageBoxIcon.Question => SystemIcons.Question.ToBitmap(),
            _ => SystemIcons.Information.ToBitmap()
        };

    private static bool IsDarkThemeEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyDarkTitleBar(IntPtr handle, bool dark)
    {
        if (!dark || !OperatingSystem.IsWindows())
        {
            return;
        }

        var enabled = 1;
        if (DwmSetWindowAttribute(handle, DwmWindowAttributeUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, DwmWindowAttributeUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}

internal static class SupervisorConsoleLog
{
    private const int Capacity = 80;
    private static readonly object Sync = new();
    private static readonly Queue<string> Lines = new();

    public static IDisposable Install()
    {
        var originalOut = Console.Out;
        var writer = new TeeConsoleWriter(originalOut, AppendLine);
        Console.SetOut(writer);
        return new RestoreConsoleWriter(originalOut, writer);
    }

    public static string[] GetRecentLines(int count)
    {
        lock (Sync)
        {
            return Lines
                .TakeLast(Math.Clamp(count, 0, Capacity))
                .ToArray();
        }
    }

    private static void AppendLine(string? value)
    {
        var line = value ?? "";
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var stamped = $"{DateTimeOffset.Now:HH:mm:ss} {line.Trim()}";
        lock (Sync)
        {
            Lines.Enqueue(stamped);
            while (Lines.Count > Capacity)
            {
                Lines.Dequeue();
            }
        }
    }

    private sealed class TeeConsoleWriter(TextWriter inner, Action<string?> appendLine) : TextWriter
    {
        public override Encoding Encoding => inner.Encoding;

        public override IFormatProvider FormatProvider => inner.FormatProvider;

        public override void Flush()
            => inner.Flush();

        public override Task FlushAsync()
            => inner.FlushAsync();

        public override void Write(char value)
            => inner.Write(value);

        public override void Write(string? value)
            => inner.Write(value);

        public override void WriteLine()
        {
            appendLine("");
            inner.WriteLine();
        }

        public override void WriteLine(string? value)
        {
            appendLine(value);
            inner.WriteLine(value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Flush();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class RestoreConsoleWriter(TextWriter originalOut, TextWriter installedWriter) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(Console.Out, installedWriter))
            {
                Console.SetOut(originalOut);
            }

            installedWriter.Dispose();
        }
    }
}

internal sealed record ResolvedExecutablePath(string Path, bool WasSelected);

internal sealed record ManagedAutoLaunchApp(string DisplayName, string Path, string[] ProcessNames, bool RestartOnPimaxReconnect, bool RunAsAdmin, bool StartMinimized);
internal sealed record AutoLaunchExecutableIdentity(string Label, string Original, string? FullPath, string FileName);

internal sealed record PimaxServiceReconnect(DateTimeOffset RemoveAt, DateTimeOffset AddAt);

internal enum ManagedAppStopReason
{
    SessionEnding,
    PimaxReconnect
}

internal enum StartupLaunchMode
{
    Unspecified,
    None,
    ScheduledTask,
    SteamVrManifest
}

internal enum WatchedProcessState
{
    NotSeenYet,
    Running,
    NormalExit,
    WaitingAfterCrash,
    CrashGraceExpired
}

internal enum SupervisorLifecyclePhase
{
    WaitingForVrChat,
    VrChatRunning,
    WaitingForVrChatRestartOrSteamVrExit,
    StartupRoutineRunning,
    ShutdownRoutineRunning
}

internal enum BaseStationWakeRoutineResult
{
    Ran,
    RanExhausted,
    NoopAlreadyComplete,
    NoopNoStations,
    NoopSteamVrNotRunning,
    NoopWaitingForRetry
}

internal enum BaseStationStartupSchedulerState
{
    Disabled,
    WaitingForSteamVr,
    Stabilizing,
    Scheduled,
    Running,
    Completed,
    Exhausted,
    Cancelled
}

internal sealed record SteamVrBaseStationEpoch(
    int Pid,
    string ProcessName,
    DateTimeOffset? ProcessStartTime,
    DateTimeOffset FirstDetectedAt)
{
    public string Identity => ProcessStartTime is { } startTime
        ? $"{ProcessName}:{Pid}:start:{startTime:O}"
        : $"{ProcessName}:{Pid}:detected:{FirstDetectedAt:O}";
}

internal struct ConsoleHotkeys
{
    public bool LaunchBrokenEyeVrcFaceTracking { get; set; }
    public bool LaunchOscGoesBrrr { get; set; }
    public bool BaseStationsOn { get; set; }
    public bool BaseStationsOff { get; set; }
    public bool OscRouterLaunchOrRestart { get; set; }
    public bool AfterLaunchAppsRoutine { get; set; }
    public bool ShowHelp { get; set; }
}

internal sealed record ManualBaseStationActionResult(bool Accepted, string Message);

internal sealed record SupervisorStatusSnapshot(
    DateTimeOffset Timestamp,
    string AppVersion,
    string Mode,
    string SteamVr,
    string Lifecycle,
    string CoreApps,
    string BaseStations,
    string OscRouter,
    string OscGoesBrrr,
    string? ShutdownProgress,
    string? ShutdownProgressElapsed,
    string? ShutdownBlockedBy,
    string? ShutdownBlockedElapsed,
    string? BlockingProcesses,
    string? OperatorWarning);

internal sealed record SupervisorCommandDefinition(
    string Name,
    string DisplayName,
    string Description,
    string Category,
    bool Dangerous,
    bool RequiresConfirmation,
    bool Available,
    string OutputKind,
    string LegacyTextCommand,
    string Notes,
    bool ActionSupported,
    string ActionSafetyCategory,
    bool TuiExecutable,
    string? BlockedReason);

internal sealed record SupervisorCommandCapabilitiesSnapshot(
    DateTimeOffset Timestamp,
    string AppVersion,
    string Protocol,
    SupervisorCommandDefinition[] Commands,
    string Notes);

internal sealed record SupervisorCommandResult(
    DateTimeOffset Timestamp,
    string? RequestId,
    string Command,
    bool Success,
    string Message,
    string ResultType,
    object? Data,
    string? Error);

internal sealed record SupervisorReadOnlyJsonRequest(
    string? RequestId,
    string? Resource,
    int? MaxLines);

internal sealed record SupervisorActionJsonRequest(
    string? RequestId,
    string? Command,
    bool? Confirmed);

internal sealed record SupervisorLifecycleJsonRequest(
    string? RequestId,
    string? Action,
    string? Source);

internal sealed record SupervisorLifecycleResultData(
    bool Accepted,
    bool AlreadyInProgress,
    string Status);

internal sealed record SupervisorGracefulShutdownRequestResult(
    bool Accepted,
    bool AlreadyInProgress,
    string Status,
    string Message);

internal sealed record SupervisorLogLine(
    int Index,
    DateTimeOffset? Timestamp,
    string Message,
    string Source,
    string Level,
    string Raw);

internal sealed record SupervisorRecentLogSnapshot(
    DateTimeOffset Timestamp,
    string AppVersion,
    string Source,
    int Count,
    SupervisorLogLine[] Lines,
    string Notes);

internal sealed class AppSupervisor
{
    private const int BrokenEyeStartupMaxAttempts = 10;
    private const string ForcedManualReloadMarkerFileName = "PimaxVrcSupervisorForcedManualReload.marker";
    private static readonly JsonSerializerOptions CommandBridgeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly TimeSpan BrokenEyeStartupCheckDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SteamVrBaseStationMinimumProcessAge = BaseStationStartupScheduler.MinimumProcessAge;
    private static readonly TimeSpan SteamVrBaseStationFallbackStabilization = BaseStationStartupScheduler.FallbackStabilization;
    private static readonly TimeSpan LovenseBluetoothRegistryRecentWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan DashboardReadinessTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TerminalUiImmediateExitObservation = TimeSpan.FromMilliseconds(750);

    private readonly SupervisorConfig _config;
    private readonly bool _steamVrStart;
    private readonly bool _managedSteamVrSession;
    private readonly bool _launchDesktopTuiAfterReady;
    private readonly bool _autoLaunchTaskBindingDeferredByUser;
    private readonly BaseStationGattClient _baseStationGattClient = new();
    private readonly BaseStationDiagnosticSink _baseStationDiagnostics;
    private readonly SteamVrTrackingReferenceReader _steamVrTrackingReferenceReader = new();
    private readonly MonitorLayoutController _monitorLayout = new();
    private readonly TimeSpan _pollInterval;
    private readonly SupervisorDiagnosticsSession _diagnostics;
    private readonly CancellationTokenSource _shutdown;
    private readonly SteamVrLifecycleCoordinator _steamVrLifecycle;
    private readonly Dictionary<int, Process> _watchedProcessHandles = new();
    private readonly SemaphoreSlim _oscGoesBrrrLaunchLock = new(1, 1);
    private readonly SemaphoreSlim _oscRouterLaunchLock = new(1, 1);
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private readonly SemaphoreSlim _coreAppRestartLock = new(1, 1);
    private readonly SemaphoreSlim _autoLaunchAppsRoutineLock = new(1, 1);
    private readonly SemaphoreSlim _manualBaseStationActionLock = new(1, 1);
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
    private readonly HashSet<string> _baseStationSteamVrConfirmedActive = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _baseStationPowerOnLastFailure = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _lastBaseStationPowerOnSkippedLogAt;
    private DateTimeOffset? _nextBaseStationPowerOnAttemptAt;
    private DateTimeOffset? _baseStationSecondPowerOnPassCompletedAt;
    private BaseStationStartupSchedulerState _baseStationStartupSchedulerState = BaseStationStartupSchedulerState.WaitingForSteamVr;
    private SteamVrBaseStationEpoch? _baseStationStartupEpoch;
    private DateTimeOffset? _baseStationStartupScheduledAt;
    private bool _baseStationStartupInitialWakeSentForEpoch;
    private bool _baseStationStartupStabilizationWaitLoggedForEpoch;
    private bool _baseStationStartupAlreadyScheduledLoggedForEpoch;
    private DateTimeOffset? _shutdownBlockedBySteamVrSince;
    private string? _shutdownProgress;
    private DateTimeOffset? _shutdownProgressSince;
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
    private SupervisorCommandServer? _commandServer;
    private bool _oscGoesBrrrBleScannerWarningShown;
    private DateTimeOffset? _watchedProcessMissingSince;
    private DateTimeOffset? _lastPimaxServiceLogEventSeenAt;
    private DateTimeOffset? _pendingPimaxServiceHidRemoveAt;
    private DateTimeOffset? _lastPimaxServiceReconnectAt;
    private DateTimeOffset? _lastHandledPimaxReconnectSignalAt;
    private DateTimeOffset? _lastMouthTrackerPnPEventSeenAt;
    private bool _mouthTrackerPnPEventWarningShown;
    private volatile bool _forcedManualReloadRequested;
    private SupervisorLifecyclePhase _lifecyclePhase = SupervisorLifecyclePhase.WaitingForVrChat;
    private int _gracefulShutdownRequested;
    private string? _operatorWarning;

    public AppSupervisor(
        SupervisorConfig config,
        bool steamVrStart,
        bool managedSteamVrSession,
        bool launchDesktopTuiAfterReady,
        bool autoLaunchTaskBindingDeferredByUser,
        SupervisorDiagnosticsSession diagnostics,
        CancellationTokenSource shutdown)
    {
        _config = config;
        _steamVrStart = steamVrStart;
        _managedSteamVrSession = managedSteamVrSession;
        _launchDesktopTuiAfterReady = launchDesktopTuiAfterReady;
        _autoLaunchTaskBindingDeferredByUser = autoLaunchTaskBindingDeferredByUser;
        _diagnostics = diagnostics;
        _shutdown = shutdown;
        _steamVrLifecycle = new SteamVrLifecycleCoordinator(managedSteamVrSession, Environment.ProcessId);
        _baseStationDiagnostics = BaseStationDiagnosticSink.ForProcess("Supervisor", AppVersion.Current);
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, config.PollIntervalSeconds));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Pimax VRC Supervisor {AppVersion.Current}");
        Console.WriteLine("---------------------");
        if (!string.IsNullOrWhiteSpace(_config.DisplayName))
        {
            Console.WriteLine($"Config: {_config.DisplayName}");
        }
        if (!string.IsNullOrWhiteSpace(_config.LoadedFromPath))
        {
            Console.WriteLine($"Config path: {_config.LoadedFromPath}");
        }
        WriteDebug(
            "supervisor starting"
            + $"; version={AppVersion.Current}"
            + $"; config={(string.IsNullOrWhiteSpace(_config.DisplayName) ? "default" : _config.DisplayName)}"
            + $"; configPath={(_config.LoadedFromPath ?? "none")}"
            + $"; steamVrStart={_steamVrStart}"
            + $"; managedSteamVrSession={_managedSteamVrSession}"
            + $"; diagnosticsFile={(_diagnostics.DiagnosticsPath ?? "none")}"
            + $"; debugFile={(_diagnostics.DebugPath ?? "none")}");

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("This supervisor is Windows-only.");
            WriteDebug("supervisor exiting; reason=non-windows");
            return;
        }

        if (!IsAdministrator())
        {
            Console.WriteLine("Warning: this process is not elevated. Build/run the exe directly so the manifest can request administrator permission.");
        }

        if (_config.FaceTrackerAutomationEnabled && !await EnsureExecutablePathsAsync(cancellationToken))
        {
            WriteDebug("supervisor exiting; reason=missing executable path");
            return;
        }

        var runInitialSetupQuestions = _config.RunInitialSetupQuestions;
        _mouthTrackerUser = _config.FaceTrackerAutomationEnabled
            && await EnsureMouthTrackerPreferenceAsync(runInitialSetupQuestions, cancellationToken);
        _turnOffSecondaryMonitors = _config.FaceTrackerAutomationEnabled
            && await EnsureTurnOffSecondaryMonitorsPreferenceAsync(runInitialSetupQuestions, cancellationToken);
        await EnsureStartupIntegrationPreferenceAsync(runInitialSetupQuestions, cancellationToken);
        await EnsureBaseStationPowerPreferenceAsync(runInitialSetupQuestions, cancellationToken);
        if (runInitialSetupQuestions && _config.AreInitialSetupQuestionsComplete())
        {
            _config.SaveInitialSetupQuestionsComplete();
        }
        _commandServer = SupervisorCommandServer.Start(this, cancellationToken);
        if (_launchDesktopTuiAfterReady)
        {
            Console.WriteLine("Supervisor received startup interface: Terminal UI.");
            await LaunchTerminalUiAfterDashboardReadyAsync(cancellationToken);
        }
        var restartedFromForcedManualReload = TryConsumeForcedManualReloadMarker();

        try
        {
            if (ShouldExitWithSteamVr() && !IsAnyProcessRunning(_config.SteamVrServerProcessNames))
            {
                Console.WriteLine($"SteamVR startup requested, but no SteamVR server process is running: {string.Join(", ", _config.SteamVrServerProcessNames)}");
                return;
            }
            if (ShouldExitWithSteamVr())
            {
                _ = ObserveSteamVrLifecycle("startup-initial-observation");
                WriteDiagnosticEvent("lifecycle; steamvr detected at startup; processes=" + DescribeRunningProcesses(_config.SteamVrServerProcessNames));
            }

            WriteDiagnosticEvent("lifecycle; waiting for pimax headset on startup");
            _lastPimaxConnected = await WaitForPimaxOnStartupAsync(cancellationToken);
            WriteDiagnosticEvent($"lifecycle; pimax startup wait complete; connected={_lastPimaxConnected}");

            await ObserveAndRunBaseStationStartupSchedulerAsync(
                "startup-before-watched-process",
                initialPassOnly: true,
                cancellationToken);
            var startupSteamVrDecision = ObserveSteamVrLifecycle("startup-before-watched-process");
            if (await ApplySteamVrLifecycleDecisionAsync(
                startupSteamVrDecision,
                "SteamVR shut down before VRChat started. Powering down base stations and exiting.",
                cancellationToken))
            {
                return;
            }

            if (!await WaitForWatchedProcessOnStartupAsync(cancellationToken))
            {
                return;
            }

            if (_mouthTrackerUser)
            {
                _lastMouthTrackerConnected = await IsMouthTrackerConnectedAsync(cancellationToken);
                if (_lastMouthTrackerConnected.Value)
                {
                    Console.WriteLine("Vive Face Tracker detected.");
                }
                else
                {
                    Console.WriteLine("Vive Face Tracker is not connected.");
                }
            }
            else
            {
                Console.WriteLine("Vive Face Tracker monitoring is disabled by config.");
            }

            await TryStartOscRouterAsync(cancellationToken);
            WriteDiagnosticEvent("lifecycle; managed app startup begin");
            await StartManagedAppsAsync(cancellationToken);
            WriteDiagnosticEvent("lifecycle; managed app startup complete");
            await InitializeOscGoesBrrrWorkflowAsync(cancellationToken);
            _lifecyclePhase = SupervisorLifecyclePhase.VrChatRunning;
            ShowOscRouterRetryPromptIfNeeded();
            if (restartedFromForcedManualReload)
            {
                Console.WriteLine("Supervisor restarted successfully from a forced manual reload.");
            }

            Console.WriteLine($"Pimax Crystal initial state: {DescribeConnection(_lastPimaxConnected.Value)}");
            Console.WriteLine(ShouldExitWithSteamVr()
                ? "Waiting for Pimax reconnects, VRChat shutdown, or SteamVR shutdown. Press Ctrl+C to stop."
                : "Waiting for Pimax reconnects or VRChat shutdown. Press Ctrl+C to stop.");
            Console.WriteLine("Press F1 for shortcuts.");

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_pollInterval, cancellationToken);
                var loopStartedAt = Stopwatch.GetTimestamp();
                try
                {
                    RefreshOscGoesBrrrWorkflowState();
                    await HandleConsoleHotkeysAsync(cancellationToken);
                    if (_lifecyclePhase == SupervisorLifecyclePhase.WaitingForVrChatRestartOrSteamVrExit)
                    {
                        var waitingSteamVrDecision = ObserveSteamVrLifecycle("waiting-for-vrchat-restart-or-steamvr-exit");
                        if (await ApplySteamVrLifecycleDecisionAsync(
                            waitingSteamVrDecision,
                            "SteamVR exited; running shutdown routine.",
                            cancellationToken))
                        {
                            return;
                        }

                        if (ObserveWatchedShutdownProcesses() == WatchedProcessState.Running)
                        {
                            Console.WriteLine("VRChat restarted; running startup routine.");
                            _shutdownBlockedBySteamVrSince = null;
                            await StartSessionAfterWatchedProcessRestartAsync(cancellationToken);
                            Console.WriteLine(ShouldExitWithSteamVr()
                                ? "Waiting for Pimax reconnects, VRChat shutdown, or SteamVR shutdown. Press Ctrl+C to stop."
                                : "Waiting for Pimax reconnects or VRChat shutdown. Press Ctrl+C to stop.");
                        }

                        continue;
                    }

                    if (_lastPimaxConnected == true)
                    {
                        await ObserveAndRunBaseStationStartupSchedulerAsync(
                            "main-loop",
                            initialPassOnly: false,
                            cancellationToken);
                    }

                    var steamVrDecision = ObserveSteamVrLifecycle("main-loop");
                    if (await ApplySteamVrLifecycleDecisionAsync(
                        steamVrDecision,
                        _lifecyclePhase == SupervisorLifecyclePhase.WaitingForVrChatRestartOrSteamVrExit
                            ? "SteamVR exited; running shutdown routine."
                            : "SteamVR has shut down. Restoring monitors, closing managed apps, and exiting.",
                        cancellationToken))
                    {
                        return;
                    }

                    var watchedProcessState = ObserveWatchedShutdownProcesses();
                    if (watchedProcessState == WatchedProcessState.NormalExit)
                    {
                        if (ShouldExitWithSteamVr() && IsAnyProcessRunning(_config.SteamVrServerProcessNames))
                        {
                            Console.WriteLine("VRChat closed; SteamVR still running. Waiting for VRChat restart or SteamVR exit.");
                            _shutdownBlockedBySteamVrSince = DateTimeOffset.UtcNow;
                            WriteDiagnosticEvent("shutdown; vrchat closed; waiting for steamvr exit; processes=" + DescribeRunningProcesses(_config.SteamVrServerProcessNames));
                            await StopManagedAppsWhileWaitingForWatchedProcessRestartAsync(cancellationToken);
                            _lifecyclePhase = SupervisorLifecyclePhase.WaitingForVrChatRestartOrSteamVrExit;
                            continue;
                        }

                        _lifecyclePhase = SupervisorLifecyclePhase.ShutdownRoutineRunning;
                        _shutdownBlockedBySteamVrSince = null;
                        SetShutdownProgress("running cleanup after VRChat exit");
                        Console.WriteLine("VRChat has shut down. Closing managed apps and exiting.");
                        await StopManagedAppsAfterWatchedProcessExitAsync(waitForSteamVrServerExitBeforeBaseStationPowerDown: false, cancellationToken);
                        return;
                    }
                    if (watchedProcessState == WatchedProcessState.CrashGraceExpired)
                    {
                        if (ShouldExitWithSteamVr() && IsAnyProcessRunning(_config.SteamVrServerProcessNames))
                        {
                            Console.WriteLine("VRChat did not relaunch after a likely crash. SteamVR still running. Waiting for VRChat restart or SteamVR exit.");
                            _shutdownBlockedBySteamVrSince = DateTimeOffset.UtcNow;
                            WriteDiagnosticEvent("shutdown; vrchat crash grace expired; waiting for steamvr exit; processes=" + DescribeRunningProcesses(_config.SteamVrServerProcessNames));
                            await StopManagedAppsWhileWaitingForWatchedProcessRestartAsync(cancellationToken);
                            _lifecyclePhase = SupervisorLifecyclePhase.WaitingForVrChatRestartOrSteamVrExit;
                            continue;
                        }

                        _lifecyclePhase = SupervisorLifecyclePhase.ShutdownRoutineRunning;
                        _shutdownBlockedBySteamVrSince = null;
                        SetShutdownProgress("running cleanup after VRChat crash grace");
                        Console.WriteLine("VRChat did not relaunch after a likely crash. Closing managed apps and exiting.");
                        await StopManagedAppsAfterWatchedProcessExitAsync(waitForSteamVrServerExitBeforeBaseStationPowerDown: false, cancellationToken);
                        return;
                    }

                    var pimaxConnected = await ReadDeviceConnectedOrPreviousAsync(
                        "Pimax Crystal",
                        IsPimaxConnectedAsync,
                        _lastPimaxConnected,
                        cancellationToken);
                    var mouthTrackerConnected = _mouthTrackerUser
                        ? await ReadDeviceConnectedOrPreviousAsync(
                            "Vive Face Tracker",
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
                    var faceTrackerReconnectAutomationEnabled = _config.FaceTrackerAutomationEnabled
                        && _config.FaceTrackerRestartOnReconnectEnabled;
                    var pimaxServiceReconnect = pimaxConnected && faceTrackerReconnectAutomationEnabled
                        ? DetectPimaxServiceLogReconnect()
                        : null;
                    var pimaxRuntimeReconnected = pimaxServiceReconnect is not null;
                    var pimaxReconnected = _lastPimaxConnected == false && pimaxConnected;
                    var mouthTrackerReconnectAutomationEnabled = _config.FaceTrackerAutomationEnabled
                        && _config.MouthTrackerRestartOnReconnectEnabled;
                    var mouthTrackerReconnected = _mouthTrackerUser
                        && _lastMouthTrackerConnected == false
                        && mouthTrackerConnected == true;
                    var mouthTrackerPnPReconnected = _mouthTrackerUser
                        && mouthTrackerReconnectAutomationEnabled
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

                            if (!faceTrackerReconnectAutomationEnabled)
                            {
                                Console.WriteLine("Pimax Crystal reconnected. Face tracker reconnect restart automation is disabled; leaving managed apps unchanged.");
                            }
                            else
                            {
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
                                    Console.WriteLine(ShouldExitWithSteamVr()
                                        ? "VRChat shut down during reconnect delay. Closing managed apps, then waiting for SteamVR shutdown before powering down base stations."
                                        : "VRChat shut down during reconnect delay. Closing managed apps and exiting.");
                                    await StopManagedAppsAfterWatchedProcessExitAsync(waitForSteamVrServerExitBeforeBaseStationPowerDown: ShouldExitWithSteamVr(), cancellationToken);
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
                                        "Vive Face Tracker",
                                        IsMouthTrackerConnectedAsync,
                                        _lastMouthTrackerConnected,
                                        cancellationToken)
                                    : (bool?)null;
                                Console.WriteLine($"Pimax Crystal state after restart: {DescribeConnection(pimaxConnected)}");
                            }
                        }
                    }
                    else if (_lastPimaxConnected != pimaxConnected)
                    {
                        Console.WriteLine($"Pimax Crystal state changed: {DescribeConnection(pimaxConnected)}");
                    }

                    if (_mouthTrackerUser && (mouthTrackerReconnected || mouthTrackerPnPReconnected) && !pimaxReconnected && !pimaxRuntimeReconnected && pimaxConnected)
                    {
                        if (!mouthTrackerReconnectAutomationEnabled)
                        {
                            Console.WriteLine(mouthTrackerPnPReconnected && !mouthTrackerReconnected
                                ? "Vive Face Tracker device event detected while Pimax Crystal stayed connected. Face tracker restart automation is disabled; leaving VRCFaceTracking unchanged."
                                : "Vive Face Tracker reconnected while Pimax Crystal stayed connected. Face tracker restart automation is disabled; leaving VRCFaceTracking unchanged.");
                        }
                        else
                        {
                            Console.WriteLine(mouthTrackerPnPReconnected && !mouthTrackerReconnected
                                ? "Vive Face Tracker device event detected while Pimax Crystal stayed connected. Restarting VRCFaceTracking."
                                : "Vive Face Tracker reconnected while Pimax Crystal stayed connected. Restarting VRCFaceTracking.");
                            await RestartVrcFaceTrackingAsync(cancellationToken);
                        }
                    }
                    else if (_mouthTrackerUser && _lastMouthTrackerConnected == true && mouthTrackerConnected == false)
                    {
                        Console.WriteLine("Vive Face Tracker is not connected.");
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
                finally
                {
                    _diagnostics.RecordMainLoop(Stopwatch.GetElapsedTime(loopStartedAt));
                    _diagnostics.WriteSummaryIfDue("main-loop");
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
            _commandServer?.Dispose();
            _commandServer = null;
            StopOscRouter();
            ClearWatchedProcessHandles();
        }
    }

    private async Task<bool> EnsureExecutablePathsAsync(CancellationToken cancellationToken)
    {
        ResolvedExecutablePath? brokenEyePath = null;
        if (_config.UseBrokenEye)
        {
            brokenEyePath = await ResolveExecutablePathAsync(
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
        }
        else
        {
            Console.WriteLine("Broken Eye is disabled. Skipping Broken Eye executable validation.");
        }

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

        var pathsChanged = !StringComparer.OrdinalIgnoreCase.Equals(_config.VrcFaceTrackingPath, vrcFaceTrackingPath.Path)
            || vrcFaceTrackingPath.WasSelected;
        if (brokenEyePath is not null)
        {
            pathsChanged = pathsChanged
                || !StringComparer.OrdinalIgnoreCase.Equals(_config.BrokenEyePath, brokenEyePath.Path)
                || brokenEyePath.WasSelected;
            _config.BrokenEyePath = brokenEyePath.Path;
            ValidateExecutable(_config.BrokenEyePath, "Broken Eye");
        }

        _config.VrcFaceTrackingPath = vrcFaceTrackingPath.Path;

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

    private async Task<bool> EnsureMouthTrackerPreferenceAsync(bool allowInitialSetupQuestion, CancellationToken cancellationToken)
    {
        if (_config.TryGetMouthTrackerUser(out var mouthTrackerUser))
        {
            return mouthTrackerUser;
        }

        if (!allowInitialSetupQuestion)
        {
            return false;
        }

        Console.WriteLine("MouthTrackerUser is not configured.");
        var answer = await AskMouthTrackerPreferenceAsync(cancellationToken);
        _config.SetMouthTrackerUser(answer);
        _config.SaveMouthTrackerPreference();

        Console.WriteLine(answer
            ? "Vive Face Tracker workflow enabled."
            : "Vive Face Tracker workflow disabled.");

        return answer;
    }

    private async Task<bool> EnsureTurnOffSecondaryMonitorsPreferenceAsync(bool allowInitialSetupQuestion, CancellationToken cancellationToken)
    {
        if (_config.TryGetTurnOffSecondaryMonitors(out var turnOffSecondaryMonitors))
        {
            return turnOffSecondaryMonitors;
        }

        if (!allowInitialSetupQuestion)
        {
            return false;
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

    private async Task EnsureStartupIntegrationPreferenceAsync(bool allowInitialSetupQuestion, CancellationToken cancellationToken)
    {
        var startupMode = _config.GetEffectiveStartupLaunchMode();
        if (startupMode == StartupLaunchMode.ScheduledTask)
        {
            await EnsureAutoLaunchScheduledTaskInstalledAsync(cancellationToken);
            await SteamVrStartupInstaller.DisableAsync(cancellationToken);
            await ScheduledTaskInstaller.DeleteSteamVrStartHelperAsync(cancellationToken);
            return;
        }

        if (startupMode == StartupLaunchMode.SteamVrManifest)
        {
            await ScheduledTaskInstaller.DeleteAutoLaunchTaskAsync(cancellationToken);
            await EnsureSteamVrStartupInstalledAsync(cancellationToken);
            return;
        }

        if (startupMode == StartupLaunchMode.None)
        {
            return;
        }

        if (!allowInitialSetupQuestion)
        {
            return;
        }

        Console.WriteLine("Startup launch mode is not configured.");
        var selectedStartupMode = await AskStartupIntegrationPreferenceAsync(cancellationToken);
        if (selectedStartupMode == StartupLaunchMode.None)
        {
            _config.SetAutoLaunchScheduledTask(false);
            _config.StartupLaunchMode = StartupLaunchMode.None;
            _config.SaveAutoLaunchScheduledTaskPreference();
            await ScheduledTaskInstaller.DeleteAutoLaunchTaskAsync(cancellationToken);
            await SteamVrStartupInstaller.DisableAsync(cancellationToken);
            await ScheduledTaskInstaller.DeleteSteamVrStartHelperAsync(cancellationToken);
            Console.WriteLine("Automatic supervisor startup disabled.");
            return;
        }

        if (selectedStartupMode == StartupLaunchMode.SteamVrManifest)
        {
            _config.SetAutoLaunchScheduledTask(false);
            _config.StartupLaunchMode = StartupLaunchMode.SteamVrManifest;
            _config.SaveAutoLaunchScheduledTaskPreference();
            await ScheduledTaskInstaller.DeleteAutoLaunchTaskAsync(cancellationToken);
            await EnsureSteamVrStartupInstalledAsync(cancellationToken);
            Console.WriteLine("Supervisor will start with the VR overlay.");
            return;
        }

        if (selectedStartupMode == StartupLaunchMode.ScheduledTask)
        {
            try
            {
                var taskResult = await ScheduledTaskInstaller.CreateOrUpdateAsync(
                    startWatcherImmediately: true,
                    skipCurrentSteamVrSession: true,
                    useDesktopTuiDefaultInterface: null,
                    _config.LoadedFromPath,
                    cancellationToken);
                _config.SetAutoLaunchScheduledTask(true);
                _config.StartupLaunchMode = StartupLaunchMode.ScheduledTask;
                _config.SaveAutoLaunchScheduledTaskPreference();
                await SteamVrStartupInstaller.DisableAsync(cancellationToken);
                await ScheduledTaskInstaller.DeleteSteamVrStartHelperAsync(cancellationToken);
                Console.WriteLine(taskResult.OperatorMessage);
                Console.WriteLine($"Task: {taskResult.TaskName}");
                Console.WriteLine($"Trigger: {taskResult.TriggerDescription}");
                Console.WriteLine("Supervisor will start with the console workflow.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not create elevated auto-launch scheduled task:");
                Console.WriteLine(ex.Message);
                Console.WriteLine("The preference was not saved, so the app can ask again next run.");
            }
        }
    }

    private async Task EnsureBaseStationPowerPreferenceAsync(bool allowInitialSetupQuestion, CancellationToken cancellationToken)
    {
        if (_config.TryGetBaseStationsEnabled(out var baseStationsEnabled))
        {
            return;
        }

        if (!allowInitialSetupQuestion)
        {
            return;
        }

        Console.WriteLine("BaseStationsEnabled is not configured.");
        var answer = await AskBaseStationPowerPreferenceAsync(cancellationToken);
        if (!answer)
        {
            _config.SetBaseStationsEnabled(false);
            _config.SaveBaseStationSettings();
            Console.WriteLine("Base station power automation disabled.");
            return;
        }

        _config.SetBaseStationsEnabled(true);
        Console.WriteLine("Base station power automation enabled. Scanning for base stations...");
        try
        {
            var scanSessionId = BaseStationDiagnosticSink.CreateId("bs-metadata-scan");
            var discovered = await BaseStationDiscovery.ScanAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken,
                _baseStationDiagnostics,
                scanSessionId,
                "SteamVR autostart");
            MergeDiscoveredBaseStations(discovered);
            Console.WriteLine($"Base station scan complete: {discovered.Count} station(s) found, {GetEnabledBaseStations().Length} enabled.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Base station scan failed: {ex.Message}");
        }

        TrySaveBaseStationSettings("base station setup settings");
        await TryPowerOnBaseStationsForSessionAsync(BaseStationCommandTiming.PowerOnPasses, cancellationToken);
    }

    private bool MergeDiscoveredBaseStations(IReadOnlyList<BaseStationDevice> discovered, bool addNewDevices = true)
    {
        if (discovered.Count == 0)
        {
            return false;
        }

        var merged = _config.BaseStations.ToList();
        var changed = false;
        foreach (var baseStation in discovered.Select(station => station.WithDefaults()))
        {
            var existing = merged.FirstOrDefault(candidate => string.Equals(candidate.BluetoothAddress, baseStation.BluetoothAddress, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                if (addNewDevices)
                {
                    merged.Add(baseStation);
                    changed = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(baseStation.Name))
            {
                existing.Name = baseStation.Name;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(existing.FriendlyName) && !string.IsNullOrWhiteSpace(baseStation.FriendlyName))
            {
                existing.FriendlyName = baseStation.FriendlyName;
                changed = true;
            }

            if (existing.Version == BaseStationVersion.Unknown && baseStation.Version != BaseStationVersion.Unknown)
            {
                existing.Version = baseStation.Version;
                changed = true;
            }
        }

        if (changed)
        {
            _config.BaseStations = merged.ToArray();
        }

        return changed;
    }

    private async Task EnsureSteamVrStartupInstalledAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Ensuring SteamVR manifest startup is installed.");
        try
        {
            var details = await SteamVrStartupInstaller.CreateOrUpdateAsync(cancellationToken);
            Console.WriteLine($"Installed SteamVR startup manifest: {details.ManifestPath}");
            Console.WriteLine($"App key: {details.AppKey}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not install SteamVR startup manifest:");
            Console.WriteLine(ex.Message);
        }
    }

    private async Task LaunchTerminalUiAfterDashboardReadyAsync(CancellationToken cancellationToken)
    {
        if (_commandServer is null)
        {
            Console.WriteLine("Terminal UI startup requested, but the dashboard bridge is not initialized. Falling back to Classic Console.");
            ConsoleWindow.ShowIfPresent();
            return;
        }

        Console.WriteLine("Terminal UI startup requested.");
        Console.WriteLine("Waiting for dashboard bridge readiness.");
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var readyTask = _commandServer.Ready;
            var completed = await Task.WhenAny(readyTask, Task.Delay(DashboardReadinessTimeout, cancellationToken));
            if (completed != readyTask)
            {
                Console.WriteLine($"Terminal UI launch failed after 0 attempt(s): dashboard bridge was not ready within {DashboardReadinessTimeout.TotalSeconds:0} seconds. Falling back to Classic Console.");
                ConsoleWindow.ShowIfPresent();
                return;
            }

            await readyTask;
            var readinessProbe = await ExecuteSupervisorCommandAsync(
                "query-json {\"resource\":\"status\"}",
                cancellationToken);
            if (!readinessProbe.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Terminal UI launch failed after 0 attempt(s): dashboard status snapshot was not serviceable. Falling back to Classic Console.");
                ConsoleWindow.ShowIfPresent();
                return;
            }

            Console.WriteLine($"Dashboard bridge ready after {Stopwatch.GetElapsedTime(startedAt).TotalSeconds:0.0}s.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Terminal UI launch failed after 0 attempt(s): {ex.Message}. Falling back to Classic Console.");
            ConsoleWindow.ShowIfPresent();
            return;
        }

        var lastFailure = "";
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                Console.WriteLine("Launching Terminal UI.");
                using var process = StartTerminalUiProcess();
                Console.WriteLine($"Terminal UI process started with PID {process.Id}.");
                await Task.Delay(TerminalUiImmediateExitObservation, cancellationToken);
                if (!process.HasExited)
                {
                    return;
                }

                lastFailure = $"Terminal UI exited immediately with code {process.ExitCode}.";
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = ex.Message;
            }
        }

        Console.WriteLine($"Terminal UI launch failed after 2 attempt(s): {lastFailure} Falling back to Classic Console.");
        ConsoleWindow.ShowIfPresent();
    }

    private Process StartTerminalUiProcess()
    {
        var supervisorPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        var launchSpec = TerminalUiLaunchArguments.BuildSupervisorOwned(
            supervisorPath ?? "",
            _config.LoadedFromPath,
            Environment.ProcessId);
        if (!File.Exists(launchSpec.ExecutablePath))
        {
            throw new FileNotFoundException("Terminal UI executable was not found.", launchSpec.ExecutablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = launchSpec.ExecutablePath,
            WorkingDirectory = launchSpec.WorkingDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        foreach (var argument in launchSpec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Terminal UI process could not be started.");
    }

    private async Task EnsureAutoLaunchScheduledTaskInstalledAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Validating elevated auto-launch scheduled task.");
        try
        {
            var valid = await ScheduledTaskInstaller.ValidateAutoLaunchTaskAsync(
                useDesktopTuiDefaultInterface: null,
                _config.LoadedFromPath,
                cancellationToken);
            if (!valid)
            {
                if (_autoLaunchTaskBindingDeferredByUser
                    && await IsDeferredAutoLaunchTaskStillBoundToAnotherReleaseAsync(cancellationToken))
                {
                    Console.WriteLine("Autostart remains bound to another release by your choice.");
                    Console.WriteLine("The current release will not modify it. You can rebind it later in Configurator.");
                }
                else
                {
                    Console.WriteLine("Scheduled task needs repair. Open Configurator and apply Startup integration to update it.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not validate elevated auto-launch scheduled task:");
            Console.WriteLine(ex.Message);
        }
    }

    private static async Task<bool> IsDeferredAutoLaunchTaskStillBoundToAnotherReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ScheduledTaskPathValidator.ValidateExistingTaskExecutableAsync(
                ScheduledTaskPathValidator.AutoLaunchTaskName,
                ScheduledTaskPathValidator.GetCurrentExecutableDirectory(),
                cancellationToken);
            return result is { Exists: true, PointsToCurrentDirectory: false, Issue: not null }
                && string.Equals(result.Issue.TaskName, ScheduledTaskPathValidator.AutoLaunchTaskName, StringComparison.OrdinalIgnoreCase)
                && result.Issue.Message.Contains("another release folder", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not confirm deferred autostart task binding: {ex.Message}");
            return false;
        }
    }

    private static Task<StartupLaunchMode> AskStartupIntegrationPreferenceAsync(CancellationToken cancellationToken)
        => AskPromptAsync(
            () => ThemedPrompt.Show(
                "How should Pimax VRC Supervisor start automatically?\r\n\r\nTerminal Mode creates an elevated Windows Scheduled Task that starts the supervisor when SteamVR is running. The supervisor waits for VRChat before starting managed apps.\r\n\r\nSteamVR Overlay starts through SteamVR with the dashboard overlay.",
                "Pimax VRC Supervisor",
                [
                    new("Terminal Mode", DialogResult.Yes),
                    new("SteamVR Overlay", DialogResult.OK),
                    new("No", DialogResult.No)
                ],
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1),
            result => result switch
            {
                DialogResult.Yes => StartupLaunchMode.ScheduledTask,
                DialogResult.OK => StartupLaunchMode.SteamVrManifest,
                _ => StartupLaunchMode.None
            },
            "Could not open scheduled task question dialog.",
            cancellationToken);

    private static Task<bool> AskTurnOffSecondaryMonitorsPreferenceAsync(CancellationToken cancellationToken)
        => AskYesNoPromptAsync(
            "Do you want to turn off secondary monitors when using the headset?",
            "Could not open secondary monitor question dialog.",
            cancellationToken);

    private static Task<bool> AskMouthTrackerPreferenceAsync(CancellationToken cancellationToken)
        => AskYesNoPromptAsync(
            "Do you use a Vive Face Tracker?",
            "Could not open Vive Face Tracker question dialog.",
            cancellationToken);

    private static Task<bool> AskBaseStationPowerPreferenceAsync(CancellationToken cancellationToken)
        => AskYesNoPromptAsync(
            "Turn on base station power automation?\r\n\r\nYes enables base station power automation, scans for base stations, saves the setting, and starts the normal power-on routine immediately.",
            "Could not open base station power question dialog.",
            cancellationToken);

    private static Task<bool> AskYesNoPromptAsync(string message, string errorMessage, CancellationToken cancellationToken)
        => AskPromptAsync(
            () => ThemedPrompt.Show(
                message,
                "Pimax VRC Supervisor",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1),
            result => result == DialogResult.Yes,
            errorMessage,
            cancellationToken);

    private static Task<T> AskPromptAsync<T>(
        Func<DialogResult> showPrompt,
        Func<DialogResult, T> mapResult,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                completion.TrySetResult(mapResult(showPrompt()));
            }
            catch (Exception ex)
            {
                completion.TrySetException(new InvalidOperationException(errorMessage, ex));
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
            Console.WriteLine("Pimax Crystal already connected.");
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

    private async Task<bool> WaitForWatchedProcessOnStartupAsync(CancellationToken cancellationToken)
    {
        if (ObserveWatchedShutdownProcesses() == WatchedProcessState.Running)
        {
            Console.WriteLine($"Watched process is running: {string.Join(", ", _config.WatchedShutdownProcessNames)}");
            WriteDiagnosticEvent("lifecycle; watched process already running; processes=" + DescribeRunningProcesses(_config.WatchedShutdownProcessNames));
            return true;
        }

        Console.WriteLine($"Waiting for watched process before starting managed apps: {string.Join(", ", _config.WatchedShutdownProcessNames)}");
        WriteDiagnosticEvent("lifecycle; watched process wait begin; names=" + string.Join(",", _config.WatchedShutdownProcessNames));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, cancellationToken);
            await ObserveAndRunBaseStationStartupSchedulerAsync(
                "watched-process-startup-wait",
                initialPassOnly: true,
                cancellationToken);
            var steamVrDecision = ObserveSteamVrLifecycle("watched-process-startup-wait");
            if (await ApplySteamVrLifecycleDecisionAsync(
                steamVrDecision,
                "SteamVR shut down while waiting for VRChat.",
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }

            if (ObserveWatchedShutdownProcesses() == WatchedProcessState.Running)
            {
                Console.WriteLine("Watched process detected. Starting managed apps.");
                WriteDiagnosticEvent("lifecycle; watched process wait complete; processes=" + DescribeRunningProcesses(_config.WatchedShutdownProcessNames));
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
        var scanStartedAt = Stopwatch.GetTimestamp();
        var foundReconnect = false;
        try
        {
            if (!_config.UsePimaxServiceLogReconnectDetector)
            {
                return null;
            }

            try
            {
                var logFile = GetNewestPimaxServiceLogFile();
                if (logFile is null)
                {
                    return null;
                }

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
                        foundReconnect = true;
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
        finally
        {
            _diagnostics.RecordPimaxLogScan(Stopwatch.GetElapsedTime(scanStartedAt), foundReconnect);
        }
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
                Console.WriteLine($"Could not scan Windows PnP events for Vive Face Tracker reconnects: {ex.Message}");
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
        var sawUnavailableExitCode = false;

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
            catch (InvalidOperationException)
            {
                sawUnavailableExitCode = true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                sawUnavailableExitCode = true;
            }
            catch (NotSupportedException)
            {
                sawUnavailableExitCode = true;
            }
            catch
            {
                sawUnavailableExitCode = true;
            }
        }

        if (sawUnavailableExitCode)
        {
            Console.WriteLine("Watched process exited; exit code unavailable for externally attached process.");
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
        if (!_config.FaceTrackerAutomationEnabled)
        {
            Console.WriteLine("Face tracker automation is disabled. Skipping automatic face-tracking startup.");
            WriteDiagnosticEvent("lifecycle; managed app startup skipped; reason=face-tracker-automation-disabled");
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        WriteDiagnosticEvent("lifecycle; managed app startup routine begin");
        _managedAppsStarted = true;
        PrepareMonitorLayoutForVrSession();
        await StartCoreAppsAsync(cancellationToken);
        await StartAutoLaunchAppsAsync(cancellationToken);
        WriteDiagnosticEvent($"lifecycle; managed app startup routine complete; elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}");
    }

    private async Task StartSessionAfterWatchedProcessRestartAsync(CancellationToken cancellationToken)
    {
        _lifecyclePhase = SupervisorLifecyclePhase.StartupRoutineRunning;
        try
        {
            if (_mouthTrackerUser)
            {
                _lastMouthTrackerConnected = await IsMouthTrackerConnectedAsync(cancellationToken);
                Console.WriteLine(_lastMouthTrackerConnected.Value
                    ? "Vive Face Tracker detected."
                    : "Vive Face Tracker is not connected.");
            }

            await TryStartOscRouterAsync(cancellationToken);
            WriteDiagnosticEvent("lifecycle; managed app startup begin after watched process restart");
            await StartManagedAppsAsync(cancellationToken);
            WriteDiagnosticEvent("lifecycle; managed app startup complete after watched process restart");
            await InitializeOscGoesBrrrWorkflowAsync(cancellationToken);
            ShowOscRouterRetryPromptIfNeeded();
            _lifecyclePhase = SupervisorLifecyclePhase.VrChatRunning;
        }
        catch
        {
            _lifecyclePhase = SupervisorLifecyclePhase.WaitingForVrChatRestartOrSteamVrExit;
            throw;
        }
    }

    private async Task StartCoreAppsAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            if (_config.UseBrokenEye)
            {
                await StartBrokenEyeWithRetriesAsync(cancellationToken);
                Console.WriteLine($"Waiting {_config.DelayBeforeVrcFaceTrackingSeconds} seconds before starting VRCFaceTracking...");
                await DelayWithCancellationAsync(TimeSpan.FromSeconds(_config.DelayBeforeVrcFaceTrackingSeconds), cancellationToken);
                await VerifyRunningAsync("Broken Eye", _config.BrokenEyeProcessNames, cancellationToken, requiredStableSeconds: 0);
            }
            else
            {
                Console.WriteLine("Broken Eye is disabled. Starting VRCFaceTracking without Broken Eye.");
            }

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
        finally
        {
            _diagnostics.RecordCoreAppStart(Stopwatch.GetElapsedTime(startedAt));
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
        if (!_config.FaceTrackerAutomationEnabled)
        {
            return;
        }

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
        if (_config.UseBrokenEye)
        {
            await StopProcessesAsync("Broken Eye", _config.BrokenEyeProcessNames, cancellationToken);
        }

        _managedAppsStarted = false;
    }

    private async Task RestartCoreAppsAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        if (!await _coreAppRestartLock.WaitAsync(0, cancellationToken))
        {
            Console.WriteLine("Core app restart is already in progress.");
            return;
        }

        try
        {
            Console.WriteLine(_config.UseBrokenEye
                ? "Restarting VRCFaceTracking and Broken Eye..."
                : "Restarting VRCFaceTracking...");
            await StopProcessesAsync("VRCFaceTracking", _config.VrcFaceTrackingProcessNames, cancellationToken);
            if (_config.UseBrokenEye)
            {
                await StopProcessesAsync("Broken Eye", _config.BrokenEyeProcessNames, cancellationToken);
            }
            await StartCoreAppsAsync(cancellationToken);
            Console.WriteLine("Core app restart complete.");
        }
        finally
        {
            _diagnostics.RecordCoreAppRestart(Stopwatch.GetElapsedTime(startedAt));
            _coreAppRestartLock.Release();
        }
    }

    internal async Task<string> ExecuteSupervisorCommandAsync(string command, CancellationToken cancellationToken)
    {
        var rawCommand = command.Trim();
        var commandVerb = GetCommandVerb(rawCommand).ToLowerInvariant();
        var commandName = string.Equals(commandVerb, "query-json", StringComparison.Ordinal)
            || string.Equals(commandVerb, "action-json", StringComparison.Ordinal)
            || string.Equals(commandVerb, "lifecycle-json", StringComparison.Ordinal)
            ? commandVerb
            : rawCommand.ToLowerInvariant();
        var startedAt = Stopwatch.GetTimestamp();
        var success = false;
        var response = "";
        if (_diagnostics.ShouldWriteCommandDebug(commandName))
        {
            WriteDebug("command received; name=" + (commandName.Length == 0 ? "empty" : commandName));
        }

        try
        {
            if (string.Equals(commandVerb, "query-json", StringComparison.Ordinal))
            {
                var result = await ExecuteReadOnlyJsonQueryAsync(GetCommandPayload(rawCommand), cancellationToken);
                response = JsonSerializer.Serialize(result, CommandBridgeJsonOptions);
                success = result.Success;
                return response;
            }

            if (string.Equals(commandVerb, "action-json", StringComparison.Ordinal))
            {
                var result = await ExecuteActionJsonAsync(GetCommandPayload(rawCommand), cancellationToken);
                response = JsonSerializer.Serialize(result, CommandBridgeJsonOptions);
                success = result.Success;
                return response;
            }

            if (string.Equals(commandVerb, "lifecycle-json", StringComparison.Ordinal))
            {
                var result = await ExecuteLifecycleJsonAsync(GetCommandPayload(rawCommand));
                response = JsonSerializer.Serialize(result, CommandBridgeJsonOptions);
                success = result.Success;
                return response;
            }

            response = commandName switch
            {
                "status" => BuildSupervisorStatus(),
                "status-json" => JsonSerializer.Serialize(BuildSupervisorStatusSnapshot(), CommandBridgeJsonOptions),
                "log" => JsonSerializer.Serialize(SupervisorConsoleLog.GetRecentLines(14)),
                "log-json" => JsonSerializer.Serialize(BuildSupervisorRecentLogSnapshot(14), CommandBridgeJsonOptions),
                "commands-json" => JsonSerializer.Serialize(BuildSupervisorCommandCapabilitiesSnapshot(), CommandBridgeJsonOptions),
                "pimax-connectivity-json" => JsonSerializer.Serialize(await BuildPimaxConnectivitySnapshotAsync(cancellationToken), PimaxConnectivityJson.Options),
                "restart-core-apps" => await RestartCoreAppsCommandAsync(cancellationToken),
                "start-osc-goes-brrr" => await StartOscGoesBrrrCommandAsync(cancellationToken),
                "base-stations-on" => await ManualPowerOnBaseStationsCommandAsync(cancellationToken),
                "base-stations-off" => await ManualPowerDownBaseStationsCommandAsync(cancellationToken),
                "restart-osc-router" => await RestartOscRouterCommandAsync(cancellationToken),
                "reload-autostart-apps" => await ReloadAutostartAppsCommandAsync(cancellationToken),
                "force-stop-supervisor" => ForceStopSupervisorAndReturn(),
                _ => "Unknown command: " + commandName
            };
            success = !response.StartsWith("Command failed:", StringComparison.OrdinalIgnoreCase);
            return response;

            string ForceStopSupervisorAndReturn()
            {
                ForceStopSupervisorFromDashboard();
                return "Supervisor hard stop requested.";
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            response = "Command failed: " + ex.Message;
            return response;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            _diagnostics.RecordCommand(commandName, elapsed, success);
            if (_diagnostics.ShouldWriteCommandDebug(commandName))
            {
                WriteDebug(
                    "command completed"
                    + $"; name={(commandName.Length == 0 ? "empty" : commandName)}"
                    + $"; success={success}"
                    + $"; elapsedMs={elapsed.TotalMilliseconds:0.0}"
                    + $"; response={TruncateDebugValue(response)}");
            }
        }
    }

    private async Task<string> RestartOscRouterCommandAsync(CancellationToken token)
    {
        await RestartOscRouterAsync(token, manualOverride: true);
        return "OSC router restart requested.";
    }

    private async Task<string> RestartCoreAppsCommandAsync(CancellationToken token)
    {
        await RestartCoreAppsAsync(token);
        return _config.UseBrokenEye
            ? "Restarted Broken Eye and VRCFaceTracking."
            : "Restarted VRCFaceTracking.";
    }

    private async Task<string> StartOscGoesBrrrCommandAsync(CancellationToken token)
    {
        await StartOscGoesBrrrFromDashboardAsync(token);
        return "OSCGoesBrrr workflow start requested.";
    }

    private async Task<string> ManualPowerOnBaseStationsCommandAsync(CancellationToken token)
        => (await ManualPowerOnBaseStationsAsync(token)).Message;

    private async Task<string> ManualPowerDownBaseStationsCommandAsync(CancellationToken token)
        => (await ManualPowerDownBaseStationsAsync(token)).Message;

    private async Task<string> ReloadAutostartAppsCommandAsync(CancellationToken token)
    {
        await RunAfterLaunchAppsRoutineAsync(token);
        return "Autostart app reload requested.";
    }

    private static string TruncateDebugValue(string value)
        => value.Length <= 300 ? value : value[..300] + "...";

    private string BuildSupervisorStatus()
    {
        var snapshot = BuildSupervisorStatusSnapshot();
        var shutdownProgress = snapshot.ShutdownProgress is null || snapshot.ShutdownProgressElapsed is null
            ? ""
            : $"; ShutdownProgress={snapshot.ShutdownProgress}({snapshot.ShutdownProgressElapsed})";
        var blockedBySteamVr = snapshot.ShutdownBlockedBy is null || snapshot.ShutdownBlockedElapsed is null || snapshot.BlockingProcesses is null
            ? ""
            : $"; ShutdownBlockedBy={snapshot.ShutdownBlockedBy}({snapshot.ShutdownBlockedElapsed}); BlockingProcesses={snapshot.BlockingProcesses}";
        var operatorWarning = string.IsNullOrWhiteSpace(snapshot.OperatorWarning)
            ? ""
            : $"; Warning={snapshot.OperatorWarning}";
        return $"Mode={snapshot.Mode}; SteamVR={snapshot.SteamVr}; Lifecycle={snapshot.Lifecycle}; CoreApps={snapshot.CoreApps}; BaseStations={snapshot.BaseStations}; OscRouter={snapshot.OscRouter}; OscGoesBrrr={snapshot.OscGoesBrrr}{shutdownProgress}{blockedBySteamVr}{operatorWarning}";
    }

    private SupervisorStatusSnapshot BuildSupervisorStatusSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var mode = _managedSteamVrSession ? "SteamVR" : "VRChat";
        var steamVrRunning = IsAnyProcessRunning(_config.SteamVrServerProcessNames) ? "running" : "not running";
        var coreApps = !_config.FaceTrackerAutomationEnabled
            ? "automation disabled"
            : IsFaceTrackingAppSetRunning()
                ? "running"
                : "incomplete";
        var baseStations = _config.BaseStationsEnabled
            ? $"{GetEnabledBaseStations().Length} enabled, powered={_baseStationsPoweredOn}"
            : "disabled";
        var oscRouter = _oscRouter is null ? "stopped" : "running";
        var oscGoesBrrr = GetOscGoesBrrrStatus();
        var lifecycle = _lifecyclePhase switch
        {
            SupervisorLifecyclePhase.WaitingForVrChat => "waiting-vrchat",
            SupervisorLifecyclePhase.VrChatRunning => "vrchat-running",
            SupervisorLifecyclePhase.WaitingForVrChatRestartOrSteamVrExit => "waiting-vrchat-or-steamvr",
            SupervisorLifecyclePhase.StartupRoutineRunning => "startup-running",
            SupervisorLifecyclePhase.ShutdownRoutineRunning => "shutdown-running",
            _ => "unknown"
        };
        var shutdownProgressElapsed = _shutdownProgress is null || _shutdownProgressSince is null
            ? null
            : FormatElapsed(now - _shutdownProgressSince.Value);
        var shutdownBlockedElapsed = _shutdownBlockedBySteamVrSince is null
            ? null
            : FormatElapsed(now - _shutdownBlockedBySteamVrSince.Value);
        var blockingProcesses = _shutdownBlockedBySteamVrSince is null
            ? null
            : DescribeRunningProcesses(_config.SteamVrServerProcessNames);

        return new SupervisorStatusSnapshot(
            now,
            AppVersion.Current,
            mode,
            steamVrRunning,
            lifecycle,
            coreApps,
            baseStations,
            oscRouter,
            oscGoesBrrr,
            _shutdownProgress,
            shutdownProgressElapsed,
            _shutdownBlockedBySteamVrSince is null ? null : "SteamVR",
            shutdownBlockedElapsed,
            blockingProcesses,
            _operatorWarning);
    }

    private SupervisorCommandCapabilitiesSnapshot BuildSupervisorCommandCapabilitiesSnapshot()
        => new(
            DateTimeOffset.UtcNow,
            AppVersion.Current,
            "line-oriented-tcp-v1",
            [
                CommandDefinition(
                    "status",
                    "Status",
                    "Returns the legacy parser-compatible text supervisor status.",
                    "Status",
                    "Text",
                    "Legacy parser-compatible text status."),
                CommandDefinition(
                    "status-json",
                    "Status JSON",
                    "Returns a compact structured supervisor status snapshot.",
                    "Status",
                    "Json",
                    "Compact structured status snapshot."),
                CommandDefinition(
                    "log",
                    "Recent Log",
                    "Returns recent console lines as a JSON string array.",
                    "Logs",
                    "JsonArray",
                    "Recent console-line array used by SteamVR dashboard."),
                CommandDefinition(
                    "log-json",
                    "Recent Log JSON",
                    "Returns a structured recent console-log snapshot.",
                    "Logs",
                    "Json",
                    "Structured recent console-log snapshot for future Terminal UI clients."),
                CommandDefinition(
                    "commands-json",
                    "Command Capabilities",
                    "Returns command capability metadata as compact JSON.",
                    "Status",
                    "Json",
                    "Read-only command capability metadata."),
                CommandDefinition(
                    "query-json",
                    "Read-only JSON Query",
                    "Executes a structured read-only JSON query for status, command capabilities, recent logs, or explicit diagnostics.",
                    "Status",
                    "Json",
                    "Read-only JSON request envelope for future Terminal UI clients. Does not execute action commands."),
                CommandDefinition(
                    "pimax-connectivity-json",
                    "Pimax Connectivity JSON",
                    "Collects a one-shot read-only Pimax Client connectivity diagnostic snapshot.",
                    "Diagnostics",
                    "Json",
                    "Explicit diagnostic query. May take several seconds and does not restart processes, services, SteamVR, or USB devices."),
                CommandDefinition(
                    "action-json",
                    "Structured Action JSON",
                    "Executes an allowlisted structured action request.",
                    "Actions",
                    "Json",
                    "Structured action envelope for audited regular console actions. The Terminal UI uses an explicit local allowlist.",
                    requiresConfirmation: true,
                    blockedReason: "Envelope only; individual action commands advertise TUI execution support."),
                CommandDefinition(
                    "lifecycle-json",
                    "Lifecycle JSON",
                    "Executes a narrow structured lifecycle request.",
                    "Lifecycle",
                    "Json",
                    "Confirmed Terminal UI shutdown flow only. Not a regular action card.",
                    requiresConfirmation: true,
                    actionSupported: false,
                    actionSafetyCategory: "Disruptive",
                    tuiExecutable: false,
                    blockedReason: "Lifecycle control is exposed only through the confirmed Terminal UI shutdown flow."),
                CommandDefinition(
                    "restart-core-apps",
                    "Restart Core Apps",
                    "Restarts configured face-tracking applications.",
                    "CoreApps",
                    "ActionResult",
                    "Disruptive: restarts configured face-tracking apps.",
                    requiresConfirmation: true,
                    actionSupported: true,
                    actionSafetyCategory: "Disruptive",
                    tuiExecutable: true,
                    blockedReason: null),
                CommandDefinition(
                    "start-osc-goes-brrr",
                    "Start OSCGoesBrrr",
                    "Launches or repairs the Intiface and OSCGoesBrrr workflow.",
                    "Osc",
                    "ActionResult",
                    "May launch or repair Intiface/OscGoesBrrr workflow.",
                    requiresConfirmation: true,
                    actionSupported: true,
                    actionSafetyCategory: "Disruptive",
                    tuiExecutable: true,
                    blockedReason: null),
                CommandDefinition(
                    "base-stations-on",
                    "Base Stations On",
                    "Runs the configured base-station power-on routine.",
                    "BaseStations",
                    "ActionResult",
                    "Sends configured base-station power-on routine.",
                    requiresConfirmation: true,
                    actionSupported: true,
                    actionSafetyCategory: "Disruptive",
                    tuiExecutable: true,
                    blockedReason: null),
                CommandDefinition(
                    "base-stations-off",
                    "Base Stations Off",
                    "Runs the configured base-station power-off routine.",
                    "BaseStations",
                    "ActionResult",
                    "Disruptive: powers off configured base stations.",
                    requiresConfirmation: true,
                    actionSupported: true,
                    actionSafetyCategory: "Disruptive",
                    tuiExecutable: true,
                    blockedReason: null),
                CommandDefinition(
                    "restart-osc-router",
                    "Restart OSC Router",
                    "Restarts or manually starts OSC routing.",
                    "Osc",
                    "ActionResult",
                    "Restarts or manually starts OSC routing.",
                    requiresConfirmation: true,
                    actionSupported: true,
                    actionSafetyCategory: "LowRisk",
                    tuiExecutable: true,
                    blockedReason: null),
                CommandDefinition(
                    "reload-autostart-apps",
                    "Reload Autostart Apps",
                    "Runs the configured Autostart apps reload/start routine.",
                    "CoreApps",
                    "ActionResult",
                    "Disruptive: reloads or starts configured Autostart apps.",
                    requiresConfirmation: true,
                    actionSupported: true,
                    actionSafetyCategory: "Disruptive",
                    tuiExecutable: true,
                    blockedReason: null),
                CommandDefinition(
                    "force-stop-supervisor",
                    "Force Stop Supervisor",
                    "Hard-stops the supervisor without cleanup routines.",
                    "Supervisor",
                    "ActionResult",
                    "Hard-stops supervisor without cleanup routines; future UIs must confirm.",
                    dangerous: true,
                    requiresConfirmation: true,
                    actionSafetyCategory: "Blocked",
                    blockedReason: "Blocked from structured Terminal UI action flow because it hard-stops without cleanup routines.")
            ],
            "Metadata. available=true means the command is accepted by the current bridge, not that the underlying configured subsystem is enabled or safe/executable from the Terminal UI.");

    private static SupervisorCommandDefinition CommandDefinition(
        string name,
        string displayName,
        string description,
        string category,
        string outputKind,
        string notes,
        bool dangerous = false,
        bool requiresConfirmation = false,
        bool actionSupported = false,
        string actionSafetyCategory = "ReadOnly",
        bool tuiExecutable = false,
        string? blockedReason = "Read-only or not exposed through structured Terminal UI action execution.")
        => new(
            name,
            displayName,
            description,
            category,
            dangerous,
            requiresConfirmation,
            true,
            outputKind,
            name,
            notes,
            actionSupported,
            actionSafetyCategory,
            tuiExecutable,
            blockedReason);

    private async Task<SupervisorCommandResult> ExecuteReadOnlyJsonQueryAsync(string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return ReadOnlyJsonQueryResult(
                requestId: null,
                success: false,
                message: "query-json requires a JSON request payload.",
                resultType: "error",
                data: null,
                error: "Missing JSON request payload.");
        }

        SupervisorReadOnlyJsonRequest? request;
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ReadOnlyJsonQueryResult(
                    requestId,
                    success: false,
                    message: "query-json request must be a JSON object.",
                    resultType: "error",
                    data: null,
                    error: "Request root was not an object.");
            }

            requestId = TryReadStringProperty(document.RootElement, "requestId");
            request = document.RootElement.Deserialize<SupervisorReadOnlyJsonRequest>(CommandBridgeJsonOptions);
        }
        catch (JsonException ex)
        {
            return ReadOnlyJsonQueryResult(
                requestId,
                success: false,
                message: "query-json request payload is not valid JSON.",
                resultType: "error",
                data: null,
                error: ex.Message);
        }

        requestId ??= request?.RequestId;
        var resource = request?.Resource?.Trim();
        if (string.IsNullOrWhiteSpace(resource))
        {
            return ReadOnlyJsonQueryResult(
                requestId,
                success: false,
                message: "query-json request requires a resource.",
                resultType: "error",
                data: null,
                error: "Missing resource. Supported resources: status, commands, log, pimax-connectivity.");
        }

        return resource.ToLowerInvariant() switch
        {
            "status" => ReadOnlyJsonQueryResult(
                requestId,
                success: true,
                message: "Status snapshot returned.",
                resultType: "status",
                data: BuildSupervisorStatusSnapshot(),
                error: null),
            "commands" => ReadOnlyJsonQueryResult(
                requestId,
                success: true,
                message: "Command capabilities returned.",
                resultType: "commands",
                data: BuildSupervisorCommandCapabilitiesSnapshot(),
                error: null),
            "log" => ReadOnlyJsonQueryResult(
                requestId,
                success: true,
                message: "Recent log snapshot returned.",
                resultType: "log",
                data: BuildSupervisorRecentLogSnapshot(Math.Clamp(request?.MaxLines ?? 14, 1, 80)),
                error: null),
            "pimax-connectivity" => ReadOnlyJsonQueryResult(
                requestId,
                success: true,
                message: "Pimax Client connectivity snapshot returned.",
                resultType: "pimaxConnectivity",
                data: await BuildPimaxConnectivitySnapshotAsync(cancellationToken),
                error: null),
            _ => ReadOnlyJsonQueryResult(
                requestId,
                success: false,
                message: $"Unsupported query-json resource: {resource}.",
                resultType: "error",
                data: null,
                error: "Supported resources: status, commands, log, pimax-connectivity.")
        };
    }

    private async Task<PimaxConnectivitySnapshot> BuildPimaxConnectivitySnapshotAsync(CancellationToken cancellationToken)
        => await new PimaxConnectivitySnapshotCollector().CollectAsync(_config, cancellationToken);

    private static SupervisorCommandResult ReadOnlyJsonQueryResult(
        string? requestId,
        bool success,
        string message,
        string resultType,
        object? data,
        string? error)
        => new(
            DateTimeOffset.UtcNow,
            requestId,
            "query-json",
            success,
            message,
            resultType,
            data,
            error);

    private async Task<SupervisorCommandResult> ExecuteActionJsonAsync(string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return ActionJsonResult(
                requestId: null,
                command: "action-json",
                success: false,
                message: "action-json requires a JSON request payload.",
                data: null,
                error: "Missing JSON request payload.");
        }

        SupervisorActionJsonRequest request;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ActionJsonResult(
                    requestId: null,
                    command: "action-json",
                    success: false,
                    message: "action-json request must be a JSON object.",
                    data: null,
                    error: "Request root was not an object.");
            }

            request = new SupervisorActionJsonRequest(
                TryReadStringProperty(document.RootElement, "requestId"),
                TryReadStringProperty(document.RootElement, "command"),
                TryReadBooleanProperty(document.RootElement, "confirmed"));
        }
        catch (JsonException ex)
        {
            return ActionJsonResult(
                requestId: null,
                command: "action-json",
                success: false,
                message: "action-json request payload is not valid JSON.",
                data: null,
                error: ex.Message);
        }

        var canonicalCommand = request.Command?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(canonicalCommand))
        {
            return ActionJsonResult(
                request.RequestId,
                "action-json",
                success: false,
                message: "action-json request requires a command.",
                data: null,
                error: "Missing command. Supported commands: restart-core-apps, start-osc-goes-brrr, base-stations-on, base-stations-off, restart-osc-router, reload-autostart-apps.");
        }

        if (string.Equals(canonicalCommand, "force-stop-supervisor", StringComparison.Ordinal))
        {
            return ActionJsonResult(
                request.RequestId,
                canonicalCommand,
                success: false,
                message: "force-stop-supervisor is blocked from structured Terminal UI action flow.",
                data: null,
                error: "Blocked command: hard-stops supervisor without cleanup routines.");
        }

        if (Volatile.Read(ref _gracefulShutdownRequested) == 1)
        {
            return ActionJsonResult(
                request.RequestId,
                canonicalCommand,
                success: false,
                message: "Supervisor shutdown is in progress; action requests are disabled.",
                data: null,
                error: "Shutdown in progress.");
        }

        return canonicalCommand switch
        {
            "restart-core-apps" => await ExecuteConfirmedActionAsync(request.RequestId, canonicalCommand, request.Confirmed, RestartCoreAppsCommandAsync, cancellationToken),
            "start-osc-goes-brrr" => await ExecuteConfirmedActionAsync(request.RequestId, canonicalCommand, request.Confirmed, StartOscGoesBrrrCommandAsync, cancellationToken),
            "base-stations-on" => await ExecuteConfirmedBaseStationActionAsync(request.RequestId, canonicalCommand, request.Confirmed, ManualPowerOnBaseStationsAsync, cancellationToken),
            "base-stations-off" => await ExecuteConfirmedBaseStationActionAsync(request.RequestId, canonicalCommand, request.Confirmed, ManualPowerDownBaseStationsAsync, cancellationToken),
            "restart-osc-router" => await ExecuteConfirmedActionAsync(request.RequestId, canonicalCommand, request.Confirmed, RestartOscRouterCommandAsync, cancellationToken),
            "reload-autostart-apps" => await ExecuteConfirmedActionAsync(request.RequestId, canonicalCommand, request.Confirmed, ReloadAutostartAppsCommandAsync, cancellationToken),
            "status" or "status-json" or "commands-json" or "log" or "log-json" or "query-json" or "pimax-connectivity-json" => ActionJsonResult(
                request.RequestId,
                canonicalCommand,
                success: false,
                message: $"{canonicalCommand} is read-only and cannot be executed through action-json.",
                data: null,
                error: "Use query-json or the existing read-only command surface."),
            _ => ActionJsonResult(
                request.RequestId,
                canonicalCommand,
                success: false,
                message: $"Unsupported action-json command: {canonicalCommand}.",
                data: null,
                error: "Supported commands: restart-core-apps, start-osc-goes-brrr, base-stations-on, base-stations-off, restart-osc-router, reload-autostart-apps.")
        };
    }

    private async Task<SupervisorCommandResult> ExecuteLifecycleJsonAsync(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return LifecycleJsonResult(
                requestId: null,
                command: "lifecycle-json",
                success: false,
                message: "lifecycle-json requires a JSON request payload.",
                accepted: false,
                alreadyInProgress: false,
                status: "rejected",
                error: "Missing JSON request payload.");
        }

        SupervisorLifecycleJsonRequest request;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return LifecycleJsonResult(
                    requestId: null,
                    command: "lifecycle-json",
                    success: false,
                    message: "lifecycle-json request must be a JSON object.",
                    accepted: false,
                    alreadyInProgress: false,
                    status: "rejected",
                    error: "Request root was not an object.");
            }

            request = new SupervisorLifecycleJsonRequest(
                TryReadStringProperty(document.RootElement, "requestId"),
                TryReadStringProperty(document.RootElement, "action"),
                TryReadStringProperty(document.RootElement, "source"));
        }
        catch (JsonException ex)
        {
            return LifecycleJsonResult(
                requestId: null,
                command: "lifecycle-json",
                success: false,
                message: "lifecycle-json request payload is not valid JSON.",
                accepted: false,
                alreadyInProgress: false,
                status: "rejected",
                error: ex.Message);
        }

        var canonicalAction = request.Action?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(canonicalAction))
        {
            return LifecycleJsonResult(
                request.RequestId,
                "lifecycle-json",
                success: false,
                message: "lifecycle-json request requires an action.",
                accepted: false,
                alreadyInProgress: false,
                status: "rejected",
                error: "Missing action. Supported action: request-graceful-shutdown.");
        }

        if (!string.Equals(canonicalAction, "request-graceful-shutdown", StringComparison.Ordinal))
        {
            return LifecycleJsonResult(
                request.RequestId,
                canonicalAction,
                success: false,
                message: $"Unsupported lifecycle-json action: {canonicalAction}.",
                accepted: false,
                alreadyInProgress: false,
                status: "rejected",
                error: "Supported action: request-graceful-shutdown.");
        }

        var source = NormalizeShutdownSource(request.Source);
        var result = await RequestGracefulShutdownAsync(source, startInBackground: true);
        return LifecycleJsonResult(
            request.RequestId,
            canonicalAction,
            success: true,
            message: result.Message,
            accepted: result.Accepted,
            alreadyInProgress: result.AlreadyInProgress,
            status: result.Status,
            error: null);
    }

    private static string NormalizeShutdownSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "Terminal UI";
        }

        return source.Trim() switch
        {
            var value when value.Equals("desktop-tui", StringComparison.OrdinalIgnoreCase) => "Terminal UI",
            var value when value.Equals("desktop-tui-window-close", StringComparison.OrdinalIgnoreCase) => "Terminal UI window close",
            var value => value
        };
    }

    private async Task<SupervisorCommandResult> ExecuteConfirmedActionAsync(
        string? requestId,
        string canonicalCommand,
        bool? confirmed,
        Func<CancellationToken, Task<string>> executeAsync,
        CancellationToken cancellationToken)
    {
        if (confirmed != true)
        {
            return ActionJsonResult(
                requestId,
                canonicalCommand,
                success: false,
                message: $"{canonicalCommand} requires confirmed=true.",
                data: null,
                error: "Structured action requires JSON boolean confirmed=true.");
        }

        try
        {
            var message = await executeAsync(cancellationToken);
            return ActionJsonResult(
                requestId,
                canonicalCommand,
                success: true,
                message,
                data: null,
                error: null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return ActionJsonResult(
                requestId,
                canonicalCommand,
                success: false,
                message: $"{canonicalCommand} action failed.",
                data: null,
                error: ex.Message);
        }
    }

    private async Task<SupervisorCommandResult> ExecuteConfirmedBaseStationActionAsync(
        string? requestId,
        string canonicalCommand,
        bool? confirmed,
        Func<CancellationToken, Task<ManualBaseStationActionResult>> executeAsync,
        CancellationToken cancellationToken)
    {
        if (confirmed != true)
        {
            return ActionJsonResult(
                requestId,
                canonicalCommand,
                success: false,
                message: $"{canonicalCommand} requires confirmed=true.",
                data: null,
                error: "Structured action requires JSON boolean confirmed=true.");
        }

        try
        {
            var result = await executeAsync(cancellationToken);
            return ActionJsonResult(
                requestId,
                canonicalCommand,
                success: result.Accepted,
                message: result.Message,
                data: null,
                error: result.Accepted ? null : result.Message);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return ActionJsonResult(
                requestId,
                canonicalCommand,
                success: false,
                message: $"{canonicalCommand} action failed.",
                data: null,
                error: ex.Message);
        }
    }

    private static SupervisorCommandResult ActionJsonResult(
        string? requestId,
        string command,
        bool success,
        string message,
        object? data,
        string? error)
        => new(
            DateTimeOffset.UtcNow,
            requestId,
            command,
            success,
            message,
            "action",
            data,
            error);

    private static SupervisorCommandResult LifecycleJsonResult(
        string? requestId,
        string command,
        bool success,
        string message,
        bool accepted,
        bool alreadyInProgress,
        string status,
        string? error)
        => new(
            DateTimeOffset.UtcNow,
            requestId,
            command,
            success,
            message,
            "lifecycle",
            new SupervisorLifecycleResultData(accepted, alreadyInProgress, status),
            error);

    private static string? TryReadStringProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? TryReadBooleanProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            ? property.GetBoolean()
            : null;

    private static string GetCommandVerb(string rawCommand)
    {
        var separatorIndex = FindCommandSeparator(rawCommand);
        return separatorIndex < 0 ? rawCommand : rawCommand[..separatorIndex];
    }

    private static string GetCommandPayload(string rawCommand)
    {
        var separatorIndex = FindCommandSeparator(rawCommand);
        return separatorIndex < 0 ? "" : rawCommand[(separatorIndex + 1)..].TrimStart();
    }

    private static int FindCommandSeparator(string rawCommand)
    {
        var spaceIndex = rawCommand.IndexOf(' ');
        var tabIndex = rawCommand.IndexOf('\t');
        return (spaceIndex, tabIndex) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => tabIndex,
            (_, < 0) => spaceIndex,
            _ => Math.Min(spaceIndex, tabIndex)
        };
    }

    private SupervisorRecentLogSnapshot BuildSupervisorRecentLogSnapshot(int maxLines)
    {
        var rawLines = SupervisorConsoleLog.GetRecentLines(maxLines);
        var lines = rawLines
            .Select((line, index) => BuildSupervisorLogLine(index, line))
            .ToArray();

        return new SupervisorRecentLogSnapshot(
            DateTimeOffset.UtcNow,
            AppVersion.Current,
            "console",
            lines.Length,
            lines,
            "Read-only snapshot of the existing recent console-line buffer. Per-line timestamps are best-effort local same-day values parsed from HH:mm:ss prefixes; timestamp is null when parsing fails.");
    }

    private static SupervisorLogLine BuildSupervisorLogLine(int index, string raw)
    {
        var message = raw;
        DateTimeOffset? timestamp = null;
        if (TryParseRecentConsoleTimestamp(raw, out var parsedTimestamp, out var parsedMessage))
        {
            timestamp = parsedTimestamp;
            message = parsedMessage;
        }

        return new SupervisorLogLine(
            index,
            timestamp,
            message,
            "console",
            "info",
            raw);
    }

    private static bool TryParseRecentConsoleTimestamp(string raw, out DateTimeOffset timestamp, out string message)
    {
        timestamp = default;
        message = raw;
        if (raw.Length < 9 || raw[8] != ' ')
        {
            return false;
        }

        var timeText = raw[..8];
        if (!TimeSpan.TryParseExact(timeText, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var timeOfDay))
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        timestamp = new DateTimeOffset(now.Date.Add(timeOfDay), now.Offset);
        message = raw[9..];
        return true;
    }

    private bool IsFaceTrackingAppSetRunning()
        => (!_config.UseBrokenEye || IsAnyProcessRunning(_config.BrokenEyeProcessNames))
            && IsAnyProcessRunning(_config.VrcFaceTrackingProcessNames);

    internal void WriteDebug(string message)
        => _diagnostics.WriteDebug(message);

    private void WriteDiagnosticEvent(string message)
    {
        _diagnostics.WriteEvent(message);
        WriteDebug(message);
    }

    private void SetShutdownProgress(string? progress)
    {
        _shutdownProgress = progress;
        _shutdownProgressSince = progress is null ? null : DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(progress))
        {
            WriteDiagnosticEvent("shutdown; progress=" + progress);
        }
    }

    internal bool IsForcedManualReloadRequested()
        => _forcedManualReloadRequested;

    private void ForceStopSupervisorFromDashboard()
    {
        _forcedManualReloadRequested = true;
        Console.WriteLine("Dashboard requested hard supervisor stop. Terminating supervisor immediately without cleanup routines.");
        WriteForcedManualReloadMarker();
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            using var currentProcess = Process.GetCurrentProcess();
            currentProcess.Kill(entireProcessTree: false);
        });
    }

    internal static string ForcedManualReloadMarkerPath
        => Path.Combine(Path.GetTempPath(), ForcedManualReloadMarkerFileName);

    internal static void WriteForcedManualReloadMarker()
    {
        try
        {
            File.WriteAllText(ForcedManualReloadMarkerPath, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not write forced manual reload marker: {ex.Message}");
        }
    }

    private static bool TryConsumeForcedManualReloadMarker()
    {
        try
        {
            var markerPath = ForcedManualReloadMarkerPath;
            if (!File.Exists(markerPath))
            {
                return false;
            }

            var text = File.ReadAllText(markerPath).Trim();
            File.Delete(markerPath);
            return DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var createdAt)
                && DateTimeOffset.UtcNow - createdAt < TimeSpan.FromMinutes(10);
        }
        catch
        {
            return false;
        }
    }

    private async Task<ManualBaseStationActionResult> ManualPowerOnBaseStationsAsync(CancellationToken cancellationToken)
    {
        if (!await _manualBaseStationActionLock.WaitAsync(0, cancellationToken))
        {
            return BaseStationActionBusy();
        }

        try
        {
            return await ManualPowerOnBaseStationsCoreAsync(cancellationToken);
        }
        finally
        {
            _manualBaseStationActionLock.Release();
        }
    }

    private async Task<ManualBaseStationActionResult> ManualPowerOnBaseStationsCoreAsync(CancellationToken cancellationToken)
    {
        var resultMessage = _config.BaseStationsEnabled
            ? "Base station power-on requested."
            : "Base stations are not enabled in config; manual startup routine requested.";
        var baseStations = GetEnabledBaseStations();
        if (baseStations.Length == 0)
        {
            Console.WriteLine("No enabled configured base stations to power on.");
            return new ManualBaseStationActionResult(true, resultMessage);
        }

        if (!_config.BaseStationsEnabled)
        {
            Console.WriteLine("Base station automation is not enabled in the configuration. Running manual startup routine anyway.");
        }

        _baseStationPowerOnAttempted = false;
        _baseStationsPoweredOn = false;
        _baseStationPowerOnComplete = false;
        _baseStationPowerOnPassesCompleted = 0;
        _baseStationSecondPowerOnPassCompletedAt = null;
        _baseStationPowerOnCommandSucceeded.Clear();
        _baseStationSteamVrConfirmedActive.Clear();
        _baseStationPowerOnLastFailure.Clear();
        _nextBaseStationPowerOnAttemptAt = null;
        await TryPowerOnBaseStationsForSessionAsync(BaseStationCommandTiming.PowerOnPasses, cancellationToken, manualOverride: true);
        return new ManualBaseStationActionResult(true, resultMessage);
    }

    private async Task<ManualBaseStationActionResult> ManualPowerDownBaseStationsAsync(CancellationToken cancellationToken)
    {
        if (!await _manualBaseStationActionLock.WaitAsync(0, cancellationToken))
        {
            return BaseStationActionBusy();
        }

        try
        {
            return await ManualPowerDownBaseStationsCoreAsync(cancellationToken);
        }
        finally
        {
            _manualBaseStationActionLock.Release();
        }
    }

    private async Task<ManualBaseStationActionResult> ManualPowerDownBaseStationsCoreAsync(CancellationToken cancellationToken)
    {
        var resultMessage = _config.BaseStationsEnabled
            ? "Base station power-off requested."
            : "Base stations are not enabled in config; manual shutdown routine requested.";
        var baseStations = GetEnabledBaseStations();
        if (baseStations.Length == 0)
        {
            Console.WriteLine("No enabled configured base stations to power off.");
            return new ManualBaseStationActionResult(true, resultMessage);
        }

        if (!_config.BaseStationsEnabled)
        {
            Console.WriteLine("Base station automation is not enabled in the configuration. Running manual shutdown routine anyway.");
        }

        _baseStationPowerOnAttempted = true;
        _baseStationsPoweredOn = true;
        await TryPowerDownBaseStationsForSessionAsync(cancellationToken, manualOverride: true);
        return new ManualBaseStationActionResult(true, resultMessage);
    }

    private static ManualBaseStationActionResult BaseStationActionBusy()
    {
        const string message = "Base station power action already in progress; ignoring overlapping request.";
        Console.WriteLine(message);
        return new ManualBaseStationActionResult(false, message);
    }

    private async Task RestartOscRouterAsync(CancellationToken cancellationToken, bool manualOverride = false)
    {
        StopOscRouter();
        await TryStartOscRouterAsync(cancellationToken, manualOverride);
        if (!manualOverride)
        {
            ShowOscRouterRetryPromptIfNeeded();
        }
    }

    private async Task<BaseStationWakeRoutineResult> TryPowerOnBaseStationsForSessionAsync(
        int targetPowerOnPasses,
        CancellationToken cancellationToken,
        bool manualOverride = false,
        bool waitForSteamVrTrackingConfirmation = true)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var result = await TryPowerOnBaseStationsForSessionCoreAsync(
            targetPowerOnPasses,
            cancellationToken,
            manualOverride,
            waitForSteamVrTrackingConfirmation);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        if (result is BaseStationWakeRoutineResult.Ran or BaseStationWakeRoutineResult.RanExhausted)
        {
            _diagnostics.RecordBaseStationWakeRoutine(elapsed);
        }
        else
        {
            _diagnostics.RecordBaseStationWakeNoop(elapsed);
            _diagnostics.WriteVerbose($"base-station wake noop; reason={result}; elapsedMs={elapsed.TotalMilliseconds:0.0}");
        }

        return result;
    }

    private async Task ObserveAndRunBaseStationStartupSchedulerAsync(
        string caller,
        bool initialPassOnly,
        CancellationToken cancellationToken)
    {
        _baseStationDiagnostics.WriteEvent(
            "startupTriggerReceived",
            "SteamVR autostart",
            configuredStationCount: GetEnabledBaseStations().Length,
            currentStage: _baseStationStartupSchedulerState.ToString(),
            outcome: caller);

        if (!_config.BaseStationsEnabled || GetEnabledBaseStations().Length == 0)
        {
            TransitionBaseStationStartupScheduler(BaseStationStartupSchedulerState.Disabled, caller, "base-station startup disabled or no enabled stations");
            return;
        }

        if (_baseStationPowerOnComplete)
        {
            if (_baseStationStartupSchedulerState == BaseStationStartupSchedulerState.Exhausted)
            {
                return;
            }

            TransitionBaseStationStartupScheduler(BaseStationStartupSchedulerState.Completed, caller, "base-station startup already complete");
            return;
        }

        if (_baseStationStartupSchedulerState == BaseStationStartupSchedulerState.Running)
        {
            return;
        }

        if (_steamVrLifecycle.ProbeActive)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var epoch = TryGetCurrentSteamVrBaseStationEpoch(now);
        if (epoch is null)
        {
            if (_baseStationStartupEpoch is not null
                && BaseStationStartupScheduler.IsPendingState(_baseStationStartupSchedulerState))
            {
                TransitionBaseStationStartupScheduler(BaseStationStartupSchedulerState.Cancelled, caller, "SteamVR disappeared before base-station startup executed");
            }

            _baseStationStartupEpoch = null;
            _baseStationStartupScheduledAt = null;
            _baseStationStartupInitialWakeSentForEpoch = false;
            _baseStationStartupStabilizationWaitLoggedForEpoch = false;
            _baseStationStartupAlreadyScheduledLoggedForEpoch = false;
            TransitionBaseStationStartupScheduler(BaseStationStartupSchedulerState.WaitingForSteamVr, caller, "waiting for SteamVR server");
            return;
        }

        if (_baseStationStartupEpoch?.Identity != epoch.Identity)
        {
            var previousPresence = _baseStationStartupEpoch is null ? "absent" : "present";
            _baseStationStartupEpoch = epoch;
            _baseStationStartupScheduledAt = CalculateBaseStationStartupDueAt(epoch, now);
            _baseStationStartupInitialWakeSentForEpoch = false;
            _baseStationStartupStabilizationWaitLoggedForEpoch = false;
            _baseStationStartupAlreadyScheduledLoggedForEpoch = false;
            WriteDiagnosticEvent(
                "steamvr_presence_transition"
                + $"; previousPresence={previousPresence}"
                + "; currentPresence=present"
                + $"; stateBefore={_baseStationStartupSchedulerState}"
                + "; stateAfter=Stabilizing"
                + $"; vrserverPid={epoch.Pid}"
                + $"; vrserverStartTime={FormatOptionalTimestamp(epoch.ProcessStartTime)}"
                + $"; firstDetectedAt={epoch.FirstDetectedAt:O}"
                + $"; epoch={epoch.Identity}"
                + $"; caller={caller}");
            TransitionBaseStationStartupScheduler(BaseStationStartupSchedulerState.Stabilizing, caller, "SteamVR server detected");
        }

        var dueAt = _baseStationStartupScheduledAt ?? now;
        if (now < dueAt)
        {
            var remaining = dueAt - now;
            if (_baseStationStartupSchedulerState != BaseStationStartupSchedulerState.Stabilizing)
            {
                TransitionBaseStationStartupScheduler(BaseStationStartupSchedulerState.Stabilizing, caller, "waiting for SteamVR stabilization");
            }

            if (!_baseStationStartupStabilizationWaitLoggedForEpoch)
            {
                _baseStationStartupStabilizationWaitLoggedForEpoch = true;
                WriteDiagnosticEvent(
                    "base_station_stabilization_wait"
                    + $"; state={_baseStationStartupSchedulerState}"
                    + $"; vrserverPid={epoch.Pid}"
                    + $"; vrserverStartTime={FormatOptionalTimestamp(epoch.ProcessStartTime)}"
                    + $"; firstDetectedAt={epoch.FirstDetectedAt:O}"
                    + $"; epoch={epoch.Identity}"
                    + $"; minimumDelaySeconds={SteamVrBaseStationMinimumProcessAge.TotalSeconds:0.0}"
                    + $"; fallbackMaximumSeconds={SteamVrBaseStationFallbackStabilization.TotalSeconds:0.0}"
                    + $"; processAgeSeconds={GetEpochAge(epoch, now).TotalSeconds:0.0}"
                    + $"; remainingDelaySeconds={remaining.TotalSeconds:0.0}"
                    + $"; caller={caller}");
            }

            return;
        }

        if (BaseStationStartupScheduler.ShouldSkipInitialPass(initialPassOnly, _baseStationStartupInitialWakeSentForEpoch))
        {
            if (!_baseStationStartupAlreadyScheduledLoggedForEpoch)
            {
                _baseStationStartupAlreadyScheduledLoggedForEpoch = true;
                WriteDiagnosticEvent(
                    "base_station_startup_scheduling"
                    + "; alreadyScheduled=True"
                    + $"; alreadyRunning={_baseStationStartupSchedulerState == BaseStationStartupSchedulerState.Running}"
                    + $"; alreadyCompleted={_baseStationPowerOnComplete}"
                    + $"; state={_baseStationStartupSchedulerState}"
                    + $"; epoch={epoch.Identity}"
                    + $"; caller={caller}");
            }

            return;
        }

        TransitionBaseStationStartupScheduler(BaseStationStartupSchedulerState.Scheduled, caller, initialPassOnly ? "initial base-station wake pass scheduled" : "base-station startup follow-up scheduled");
        _baseStationDiagnostics.WriteEvent(
            "schedulerArmed",
            "SteamVR autostart",
            configuredStationCount: GetEnabledBaseStations().Length,
            currentStage: "scheduled",
            outcome: initialPassOnly ? "initialPassOnly" : "fullRoutine");
        await RunScheduledBaseStationStartupAsync(epoch, caller, initialPassOnly, cancellationToken);
    }

    private async Task RunScheduledBaseStationStartupAsync(
        SteamVrBaseStationEpoch epoch,
        string caller,
        bool initialPassOnly,
        CancellationToken cancellationToken)
    {
        TransitionBaseStationStartupScheduler(BaseStationStartupSchedulerState.Running, caller, initialPassOnly ? "running initial base-station wake pass" : "running base-station startup routine");
        _baseStationDiagnostics.WriteEvent(
            "schedulerDelayCompleted",
            "SteamVR autostart",
            configuredStationCount: GetEnabledBaseStations().Length,
            currentStage: "running",
            outcome: "started");
        WriteDiagnosticEvent(
            "base_station_startup_execution"
            + "; phase=begin"
            + $"; initialPassOnly={initialPassOnly}"
            + $"; vrserverPid={epoch.Pid}"
            + $"; vrserverStartTime={FormatOptionalTimestamp(epoch.ProcessStartTime)}"
            + $"; firstDetectedAt={epoch.FirstDetectedAt:O}"
            + $"; epoch={epoch.Identity}"
            + $"; caller={caller}");

        try
        {
            BaseStationWakeRoutineResult result;
            if (initialPassOnly)
            {
                await TryPowerOnBaseStationsBeforeWatchedProcessAsync(cancellationToken);
                _baseStationStartupInitialWakeSentForEpoch = true;
                result = _baseStationPowerOnComplete
                    ? BaseStationWakeRoutineResult.Ran
                    : BaseStationWakeRoutineResult.NoopWaitingForRetry;
            }
            else
            {
                result = await TryPowerOnBaseStationsForSessionAsync(
                    BaseStationCommandTiming.PowerOnPasses,
                    cancellationToken);
            }

            var nextState = BaseStationStartupScheduler.StateAfterExecution(_baseStationPowerOnComplete, result);

            TransitionBaseStationStartupScheduler(nextState, caller, $"base-station startup execution result={result}");
            WriteDiagnosticEvent(
                "base_station_startup_execution"
                + "; phase=complete"
                + $"; result={result}"
                + $"; state={_baseStationStartupSchedulerState}"
                + $"; completed={_baseStationPowerOnComplete}"
                + $"; passesCompleted={_baseStationPowerOnPassesCompleted}"
                + $"; commandSucceeded={_baseStationPowerOnCommandSucceeded.Count}"
                + $"; epoch={epoch.Identity}"
                + $"; caller={caller}");
            _baseStationDiagnostics.WriteEvent(
                "sessionCompleted",
                "SteamVR autostart",
                configuredStationCount: GetEnabledBaseStations().Length,
                currentStage: _baseStationStartupSchedulerState.ToString(),
                outcome: result.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TransitionBaseStationStartupScheduler(BaseStationStartupSchedulerState.Cancelled, caller, "base-station startup cancelled");
            WriteDiagnosticEvent(
                "base_station_startup_execution"
                + "; phase=cancelled"
                + "; cancellationReason=shutdown"
                + $"; epoch={epoch.Identity}"
                + $"; caller={caller}");
            throw;
        }
    }

    private void TransitionBaseStationStartupScheduler(
        BaseStationStartupSchedulerState nextState,
        string caller,
        string reason)
    {
        var previousState = _baseStationStartupSchedulerState;
        if (previousState == nextState)
        {
            return;
        }

        _baseStationStartupSchedulerState = nextState;
        _baseStationDiagnostics.WriteEvent(
            nextState is BaseStationStartupSchedulerState.Stabilizing ? "schedulerDelayStarted" : "schedulerStateChanged",
            "SteamVR autostart",
            configuredStationCount: GetEnabledBaseStations().Length,
            currentStage: nextState.ToString(),
            outcome: reason);
        WriteDiagnosticEvent(
            "base_station_startup_scheduling"
            + $"; stateBefore={previousState}"
            + $"; stateAfter={nextState}"
            + $"; reason={reason}"
            + $"; alreadyCompleted={_baseStationPowerOnComplete}"
            + $"; alreadyRunning={nextState == BaseStationStartupSchedulerState.Running}"
            + $"; epoch={_baseStationStartupEpoch?.Identity ?? "none"}"
            + $"; caller={caller}");
    }

    private DateTimeOffset CalculateBaseStationStartupDueAt(SteamVrBaseStationEpoch epoch, DateTimeOffset now)
        => BaseStationStartupScheduler.CalculateDueAt(epoch, now);

    private static TimeSpan GetEpochAge(SteamVrBaseStationEpoch epoch, DateTimeOffset now)
        => BaseStationStartupScheduler.GetEpochAge(epoch, now);

    private SteamVrBaseStationEpoch? TryGetCurrentSteamVrBaseStationEpoch(DateTimeOffset detectedAt)
    {
        SteamVrBaseStationEpoch? latestEpoch = null;
        foreach (var processName in _config.SteamVrServerProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    var pid = 0;
                    var observedProcessName = processName;
                    DateTimeOffset? startTime = null;
                    try
                    {
                        pid = process.Id;
                        observedProcessName = process.ProcessName;
                    }
                    catch
                    {
                        continue;
                    }

                    try
                    {
                        var candidateStartTime = new DateTimeOffset(process.StartTime);
                        if (candidateStartTime <= detectedAt.AddSeconds(1))
                        {
                            startTime = candidateStartTime;
                        }
                    }
                    catch
                    {
                        // Process may exit or deny StartTime access while we are checking.
                    }

                    var firstDetectedAt = _baseStationStartupEpoch is { } existing
                        && existing.Pid == pid
                        && Nullable.Equals(existing.ProcessStartTime, startTime)
                        ? existing.FirstDetectedAt
                        : detectedAt;
                    var epoch = new SteamVrBaseStationEpoch(
                        pid,
                        observedProcessName,
                        startTime,
                        firstDetectedAt);
                    latestEpoch = SelectLatestSteamVrBaseStationEpoch(latestEpoch, epoch);
                }
            }
        }

        return latestEpoch;
    }

    private static SteamVrBaseStationEpoch SelectLatestSteamVrBaseStationEpoch(
        SteamVrBaseStationEpoch? current,
        SteamVrBaseStationEpoch candidate)
        => BaseStationStartupScheduler.SelectLatest(current, candidate);

    private static string FormatOptionalTimestamp(DateTimeOffset? timestamp)
        => timestamp is { } value ? value.ToString("O") : "unknown";

    private async Task TryPowerOnBaseStationsBeforeWatchedProcessAsync(CancellationToken cancellationToken)
    {
        WriteDiagnosticEvent("base-station startup wake begin; mode=initial-pass-before-managed-apps");
        await TryPowerOnBaseStationsForSessionAsync(
            1,
            cancellationToken,
            waitForSteamVrTrackingConfirmation: false);
        if (!_baseStationPowerOnComplete && _baseStationPowerOnPassesCompleted > 0)
        {
            Console.WriteLine("Base station wake pass sent. Continuing startup while SteamVR confirmation continues later.");
            WriteDiagnosticEvent(
                "base-station startup wake partial; continuing before openvr confirmation"
                + $"; passesCompleted={_baseStationPowerOnPassesCompleted}"
                + $"; commandSucceeded={_baseStationPowerOnCommandSucceeded.Count}");
        }

        while (!_baseStationPowerOnComplete
            && _baseStationPowerOnPassesCompleted > 0
            && _nextBaseStationPowerOnAttemptAt is { } nextAttemptAt)
        {
            var delay = nextAttemptAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                Console.WriteLine($"Waiting {delay.TotalSeconds:0} seconds before continuing base-station startup.");
                await Task.Delay(delay, cancellationToken);
            }

            var steamVrDecision = ObserveSteamVrLifecycle("base-station-startup-delay");
            if (steamVrDecision.Classification != SteamVrTerminationClassification.None)
            {
                return;
            }

            await TryPowerOnBaseStationsForSessionAsync(BaseStationCommandTiming.PowerOnPasses, cancellationToken);
        }
    }

    private async Task<BaseStationWakeRoutineResult> TryPowerOnBaseStationsForSessionCoreAsync(
        int targetPowerOnPasses,
        CancellationToken cancellationToken,
        bool manualOverride,
        bool waitForSteamVrTrackingConfirmation)
    {
        if (_baseStationPowerOnComplete)
        {
            return BaseStationWakeRoutineResult.NoopAlreadyComplete;
        }

        var baseStations = GetEnabledBaseStations();
        if ((!_config.BaseStationsEnabled && !manualOverride) || baseStations.Length == 0)
        {
            return BaseStationWakeRoutineResult.NoopNoStations;
        }

        if (!IsAnyProcessRunning(_config.SteamVrServerProcessNames))
        {
            if (_lastBaseStationPowerOnSkippedLogAt is null || DateTimeOffset.UtcNow - _lastBaseStationPowerOnSkippedLogAt.Value > TimeSpan.FromSeconds(30))
            {
                Console.WriteLine("Base station power-on waiting for SteamVR server.");
                _lastBaseStationPowerOnSkippedLogAt = DateTimeOffset.UtcNow;
            }

            return BaseStationWakeRoutineResult.NoopSteamVrNotRunning;
        }

        if (_nextBaseStationPowerOnAttemptAt is { } nextAttempt && DateTimeOffset.UtcNow < nextAttempt)
        {
            return BaseStationWakeRoutineResult.NoopWaitingForRetry;
        }

        var routineStartedAt = Stopwatch.GetTimestamp();
        baseStations = await RefreshIncompleteBaseStationMetadataAsync(baseStations, cancellationToken);
        WriteDiagnosticEvent(
            "base-station wake routine begin"
            + $"; targetPasses={targetPowerOnPasses}"
            + $"; manualOverride={manualOverride}"
            + $"; waitForOpenVrConfirmation={waitForSteamVrTrackingConfirmation}"
            + $"; stations={DescribeBaseStations(baseStations)}");
        var useSteamVrTrackingConfirmation = waitForSteamVrTrackingConfirmation
            && CanUseSteamVrTrackingConfirmationForStartup();
        WriteDiagnosticEvent(
            "base-station openvr confirmation availability"
            + $"; requested={waitForSteamVrTrackingConfirmation}"
            + $"; available={useSteamVrTrackingConfirmation}");
        if (useSteamVrTrackingConfirmation && targetPowerOnPasses >= BaseStationCommandTiming.PowerOnPasses)
        {
            targetPowerOnPasses = BaseStationCommandTiming.OpenVrPowerOnCycles;
        }

        var maximumPowerOnPasses = useSteamVrTrackingConfirmation
            ? BaseStationCommandTiming.OpenVrPowerOnCycles
            : BaseStationCommandTiming.PowerOnPasses;
        targetPowerOnPasses = Math.Clamp(targetPowerOnPasses, 1, maximumPowerOnPasses);
        var initialStates = await ReadBaseStationPowerStatesAsync(baseStations, cancellationToken, saveSettings: !manualOverride);
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
            WriteDiagnosticEvent($"base-station wake routine complete; result=already-awake; elapsedMs={Stopwatch.GetElapsedTime(routineStartedAt).TotalMilliseconds:0.0}");
            return BaseStationWakeRoutineResult.Ran;
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
                    WriteDiagnosticEvent($"base-station wake routine deferred; nextAttemptAt={thirdPassAt:O}; elapsedMs={Stopwatch.GetElapsedTime(routineStartedAt).TotalMilliseconds:0.0}");
                    return BaseStationWakeRoutineResult.Ran;
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
                    WriteDiagnosticEvent($"base-station wake routine complete; result=openvr-confirmed; elapsedMs={Stopwatch.GetElapsedTime(routineStartedAt).TotalMilliseconds:0.0}");
                    return BaseStationWakeRoutineResult.Ran;
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

                if (_baseStationSteamVrConfirmedActive.Count > 0)
                {
                    baseStationsToPowerOn = baseStationsToPowerOn
                        .Where(baseStation => !_baseStationSteamVrConfirmedActive.Contains(baseStation.BluetoothAddress))
                        .ToArray();
                    if (baseStationsToPowerOn.Length == 0)
                    {
                        Console.WriteLine("SteamVR confirmed the remaining enabled base station(s) active. Startup complete.");
                        _baseStationsPoweredOn = true;
                        _baseStationPowerOnComplete = true;
                        _nextBaseStationPowerOnAttemptAt = null;
                        WriteDiagnosticEvent($"base-station wake routine complete; result=openvr-remaining-confirmed; elapsedMs={Stopwatch.GetElapsedTime(routineStartedAt).TotalMilliseconds:0.0}");
                        return BaseStationWakeRoutineResult.Ran;
                    }
                }
            }
        }

        _baseStationsPoweredOn = _baseStationsPoweredOn || _baseStationPowerOnCommandSucceeded.Count > 0;
        if (targetPowerOnPasses < maximumPowerOnPasses)
        {
            _nextBaseStationPowerOnAttemptAt = null;
            WriteDiagnosticEvent($"base-station wake routine complete; result=partial-target; elapsedMs={Stopwatch.GetElapsedTime(routineStartedAt).TotalMilliseconds:0.0}");
            return BaseStationWakeRoutineResult.Ran;
        }

        var finalStates = await ReadBaseStationPowerStatesAsync(baseStations, cancellationToken, saveSettings: !manualOverride);
        if (useSteamVrTrackingConfirmation)
        {
            Console.WriteLine($"SteamVR did not confirm all enabled base stations after {BaseStationCommandTiming.OpenVrPowerOnCycles} startup cycle(s). Stopping startup retries.");
        }

        var confirmedOrCommandSucceeded = new HashSet<string>(_baseStationPowerOnCommandSucceeded, StringComparer.OrdinalIgnoreCase);
        confirmedOrCommandSucceeded.UnionWith(_baseStationSteamVrConfirmedActive);
        _baseStationPowerOnComplete = IsBaseStationPowerOnComplete(baseStations, finalStates, confirmedOrCommandSucceeded);
        if (_baseStationPowerOnComplete)
        {
            _nextBaseStationPowerOnAttemptAt = null;
            WriteDiagnosticEvent($"base-station wake routine complete; result=state-or-command-confirmed; elapsedMs={Stopwatch.GetElapsedTime(routineStartedAt).TotalMilliseconds:0.0}");
            return BaseStationWakeRoutineResult.Ran;
        }

        _baseStationPowerOnPassesCompleted = 0;
        _baseStationSecondPowerOnPassCompletedAt = null;
        _nextBaseStationPowerOnAttemptAt = null;
        LogSkippedBaseStationsAfterPowerOn(baseStations, finalStates, confirmedOrCommandSucceeded);
        _baseStationPowerOnComplete = true;
        WriteDiagnosticEvent($"base-station wake routine complete; result=skipped-unavailable; elapsedMs={Stopwatch.GetElapsedTime(routineStartedAt).TotalMilliseconds:0.0}");
        return BaseStationWakeRoutineResult.RanExhausted;
    }

    private async Task<BaseStationDevice[]> RefreshIncompleteBaseStationMetadataAsync(
        BaseStationDevice[] baseStations,
        CancellationToken cancellationToken)
    {
        if (!baseStations.Any(HasIncompleteBaseStationMetadata))
        {
            return baseStations;
        }

        Console.WriteLine("Base station metadata is incomplete. Scanning for current base-station details before power-on...");
        WriteDiagnosticEvent("base-station metadata scan begin; reason=incomplete-configured-metadata");
        try
        {
            var discovered = await BaseStationDiscovery.ScanAsync(TimeSpan.FromSeconds(10), cancellationToken);
            var changed = MergeDiscoveredBaseStations(discovered, addNewDevices: false);
            Console.WriteLine($"Base station metadata scan complete: {discovered.Count} station(s) found.");
            WriteDiagnosticEvent($"base-station metadata scan complete; discovered={discovered.Count}; changed={changed}");
            if (changed)
            {
                TrySaveBaseStationSettings("base station metadata scan");
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Base station metadata scan failed; continuing with configured metadata: {ex.Message}");
            WriteDiagnosticEvent($"base-station metadata scan failed; error={ex.Message}");
        }

        return GetEnabledBaseStations();
    }

    private static bool HasIncompleteBaseStationMetadata(BaseStationDevice baseStation)
        => string.IsNullOrWhiteSpace(baseStation.Name)
            || string.IsNullOrWhiteSpace(baseStation.FriendlyName)
            || baseStation.Version == BaseStationVersion.Unknown;

    private void TrySaveBaseStationSettings(string reason)
    {
        try
        {
            _config.SaveBaseStationSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not save {reason}; continuing with in-memory base-station settings: {ex.Message}");
            WriteDiagnosticEvent($"base-station settings save failed; reason={reason}; error={ex.Message}");
        }
    }

    private void LogSkippedBaseStationsAfterPowerOn(
        BaseStationDevice[] baseStations,
        BaseStationPowerState[] finalStates,
        HashSet<string> confirmedOrCommandSucceeded)
    {
        var skipped = baseStations
            .Where((baseStation, index) =>
                !IsAwakeBaseStationState(finalStates[index])
                && !confirmedOrCommandSucceeded.Contains(baseStation.BluetoothAddress))
            .ToArray();
        if (skipped.Length == 0)
        {
            return;
        }

        Console.WriteLine("Some detected base stations are not supported for automatic power control and were skipped.");
        foreach (var baseStation in skipped)
        {
            var failure = _baseStationPowerOnLastFailure.TryGetValue(baseStation.BluetoothAddress, out var message)
                ? message
                : "not confirmed awake after startup attempts";
            Console.WriteLine(
                $"Base station skipped: {baseStation.DisplayName} "
                + $"({baseStation.BluetoothAddress}, version={baseStation.EffectiveVersion}): {failure}");
        }
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
        WriteDiagnosticEvent($"base-station openvr confirmation wait begin; seconds={BaseStationCommandTiming.OpenVrTrackingCheckDelay.TotalSeconds:0}");
        await Task.Delay(BaseStationCommandTiming.OpenVrTrackingCheckDelay, cancellationToken);

        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            using var probe = BeginOpenVrProbe();
            _ = ObserveSteamVrLifecycle("base-station-openvr-probe-before");
            var trackingReferences = _steamVrTrackingReferenceReader.ReadActiveTrackingReferences();
            _ = ObserveSteamVrLifecycle("base-station-openvr-probe-after");
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var match = SteamVrBaseStationMatcher.Match(baseStations, trackingReferences);
            foreach (var address in match.ExactMatchedBluetoothAddresses)
            {
                _baseStationSteamVrConfirmedActive.Add(address);
            }

            WriteDiagnosticEvent(
                "base-station openvr confirmation result"
                + $"; elapsedMs={elapsed.TotalMilliseconds:0.0}"
                + $"; activeTrackingReferences={match.ActiveTrackingReferenceCount}"
                + $"; exactMatches={match.ExactMatchCount}/{baseStations.Length}"
                + $"; allMatchedExactly={match.AllMatchedExactly}"
                + $"; countFallbackMatched={match.CountFallbackMatched}");

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

            Console.WriteLine($"SteamVR reports {match.ExactMatchCount}/{baseStations.Length} exact base station match(es) and {match.ActiveTrackingReferenceCount} active tracking reference(s). Retrying only unconfirmed stations where possible.");
            return false;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _steamVrTrackingReferenceStartupAvailable = false;
            WriteDiagnosticEvent($"base-station openvr confirmation failed; error={ex.Message}");
            if (!_steamVrTrackingReferenceStartupUnavailableLogged)
            {
                Console.WriteLine($"SteamVR base-station tracking confirmation failed: {ex.Message}. Using BLE startup fallback.");
                _steamVrTrackingReferenceStartupUnavailableLogged = true;
            }

            return null;
        }
    }

    private async Task TryPowerDownBaseStationsForSessionAsync(CancellationToken cancellationToken, bool manualOverride = false)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await TryPowerDownBaseStationsForSessionCoreAsync(cancellationToken, manualOverride);
        }
        finally
        {
            _diagnostics.RecordBaseStationPowerDownRoutine(Stopwatch.GetElapsedTime(startedAt));
        }
    }

    private async Task TryPowerDownBaseStationsForSessionCoreAsync(CancellationToken cancellationToken, bool manualOverride)
    {
        if (!_baseStationsPoweredOn && !_baseStationPowerOnAttempted)
        {
            return;
        }

        var baseStations = GetEnabledBaseStations();
        if ((!_config.BaseStationsEnabled && !manualOverride) || baseStations.Length == 0)
        {
            _baseStationPowerOnAttempted = false;
            _baseStationsPoweredOn = false;
            _baseStationPowerOnComplete = false;
            _baseStationPowerOnPassesCompleted = 0;
            _baseStationSecondPowerOnPassCompletedAt = null;
            _baseStationPowerOnCommandSucceeded.Clear();
            _baseStationSteamVrConfirmedActive.Clear();
            _baseStationPowerOnLastFailure.Clear();
            return;
        }

        var mode = _config.BaseStationPowerDownMode;
        SetShutdownProgress($"base-station-{mode.ToString().ToLowerInvariant()} starting");
        Console.WriteLine($"Sending {mode.ToString().ToLowerInvariant()} to {baseStations.Length} base station(s)...");
        WriteDiagnosticEvent(
            "base-station power-down routine begin"
            + $"; mode={mode}"
            + $"; manualOverride={manualOverride}"
            + $"; stations={DescribeBaseStations(baseStations)}");
        var result = await BaseStationPowerDownRoutine.RunAsync(
            baseStations,
            mode,
            _baseStationGattClient,
            Console.WriteLine,
            manualOverride ? () => { } : _config.SaveBaseStationSettings,
            cancellationToken,
            SetShutdownProgress);
        WriteDiagnosticEvent(
            "base-station power-down routine complete"
            + $"; handled={result.HandledCount}/{result.StationCount}"
            + $"; allHandled={result.AllStationsHandled}"
            + $"; settingsChanged={result.SettingsChanged}");

        SetShutdownProgress(result.AllStationsHandled
            ? "base-station power-down complete"
            : $"base-station power-down incomplete {result.HandledCount}/{result.StationCount}");

        if (result.AllStationsHandled)
        {
            _baseStationPowerOnAttempted = false;
            _baseStationsPoweredOn = false;
            _baseStationPowerOnComplete = false;
            _baseStationPowerOnPassesCompleted = 0;
            _baseStationSecondPowerOnPassCompletedAt = null;
            _baseStationPowerOnCommandSucceeded.Clear();
            _baseStationSteamVrConfirmedActive.Clear();
            _baseStationPowerOnLastFailure.Clear();
        }
    }

    public async Task RunEmergencyCloseCleanupAsync()
    {
        await TryEmergencyCloseCleanupAsync();
    }

    internal async Task<SupervisorGracefulShutdownRequestResult> RequestGracefulShutdownAsync(string source, bool startInBackground)
    {
        if (_shutdown.IsCancellationRequested
            || Interlocked.Exchange(ref _gracefulShutdownRequested, 1) == 1)
        {
            return new SupervisorGracefulShutdownRequestResult(
                Accepted: true,
                AlreadyInProgress: true,
                Status: "already_in_progress",
                Message: "Graceful supervisor shutdown is already in progress.");
        }

        var message = string.Equals(source, "Ctrl+C", StringComparison.OrdinalIgnoreCase)
            ? "Ctrl+C requested. Restoring monitors and closing managed apps."
            : $"{source} requested graceful shutdown. Running Ctrl+C-equivalent cleanup.";

        Console.WriteLine(message);
        WriteDiagnosticEvent("shutdown; graceful request; source=" + source);
        MarkSteamVrShutdownIntent(source);
        _lifecyclePhase = SupervisorLifecyclePhase.ShutdownRoutineRunning;
        _shutdownBlockedBySteamVrSince = null;
        SetShutdownProgress("graceful shutdown requested by " + source);

        if (startInBackground)
        {
            _ = Task.Run(() => RunGracefulShutdownCleanupAndCancelAsync(source), CancellationToken.None);
        }
        else
        {
            await RunGracefulShutdownCleanupAndCancelAsync(source);
        }

        return new SupervisorGracefulShutdownRequestResult(
            Accepted: true,
            AlreadyInProgress: false,
            Status: "accepted",
            Message: "Graceful supervisor shutdown accepted.");
    }

    private async Task RunGracefulShutdownCleanupAndCancelAsync(string source)
    {
        try
        {
            await RunEmergencyCloseCleanupAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not complete graceful shutdown cleanup requested by {source}: {ex.Message}");
            WriteDiagnosticEvent($"shutdown; graceful cleanup failed; source={source}; error={ex.Message}");
        }
        finally
        {
            _shutdown.Cancel();
        }
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
        Func<BaseStationDevice, CancellationToken, BaseStationOperationDiagnostics?, Task> commandAsync,
        CancellationToken cancellationToken,
        int burstNumber,
        int totalBursts,
        int retryNumber,
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
                        var operation = _baseStationDiagnostics.CreateOperation(
                            "SteamVR autostart",
                            baseStation,
                            burstNumber,
                            retryNumber,
                            baseStations.Length,
                            BaseStationCommandTiming.PowerOnCommandTimeout);
                        _baseStationDiagnostics.WriteEvent(
                            "stationAttemptStarted",
                            "SteamVR autostart",
                            configuredStationCount: baseStations.Length,
                            operationId: operation.OperationId,
                            currentStage: "queued",
                            burstNumber: burstNumber,
                            retryNumber: retryNumber,
                            station: baseStation,
                            outcome: $"attempt {attempt}/{attemptsPerStation}; totalBursts={totalBursts}");
                        await RunBaseStationPowerOnCommandWithTimeoutAsync(
                            token => commandAsync(baseStation, token, operation),
                            cancellationToken,
                            operation);
                        successes++;
                        onSuccess?.Invoke(index);
                        _baseStationPowerOnLastFailure.Remove(baseStation.BluetoothAddress);
                        lastException = null;
                        operation.Succeeded();
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
                _baseStationPowerOnLastFailure[baseStation.BluetoothAddress] = lastException.Message;
                Console.WriteLine($"Base station {baseStation.DisplayName}: could not {action}: {lastException.Message}");
            }

            if (index < baseStations.Length - 1)
            {
                await Task.Delay(BaseStationCommandTiming.InterStationDelay, cancellationToken);
            }
        }

        return successes;
    }

    private static async Task RunBaseStationPowerOnCommandWithTimeoutAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        BaseStationOperationDiagnostics? diagnostics)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(BaseStationCommandTiming.PowerOnCommandTimeout);
        try
        {
            await action(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            var timeout = new TimeoutException($"Bluetooth power-on command did not finish within {BaseStationCommandTiming.PowerOnCommandTimeout.TotalSeconds:0} seconds. Stage: {diagnostics?.CurrentStage ?? "unknown"}.");
            diagnostics?.TimedOut(timeout);
            throw timeout;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            diagnostics?.Cancelled(ex);
            throw;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            diagnostics?.Failed(ex, cancellationRequested: false);
            throw;
        }
    }

    private async Task<bool[]> SendBaseStationPowerOnPassAsync(BaseStationDevice[] baseStations, int pass, int totalPasses, CancellationToken cancellationToken)
    {
        var stationSucceeded = new bool[baseStations.Length];
        var startedAt = Stopwatch.GetTimestamp();
        var burstCycles = ShouldUseUnsupportedV2PowerOnBurst(baseStations, pass) ? 2 : 1;
        if (pass > 1)
        {
            Console.WriteLine($"Repeating base station power-on pass {pass}/{totalPasses}...");
        }
        else
        {
            Console.WriteLine($"Powering on {baseStations.Length} base station(s)...");
        }

        WriteDiagnosticEvent(
            "base-station wake pass begin"
            + $"; pass={pass}/{totalPasses}"
            + $"; stationCount={baseStations.Length}"
            + $"; burstCycles={burstCycles}"
            + $"; burstDelayMs={(burstCycles > 1 ? BaseStationCommandTiming.UnsupportedV2PowerOnBurstDelay.TotalMilliseconds : 0):0}"
            + $"; stations={DescribeBaseStations(baseStations)}");
        _baseStationDiagnostics.WriteEvent(
            "burstStarted",
            "SteamVR autostart",
            configuredStationCount: baseStations.Length,
            currentStage: "burst",
            burstNumber: pass,
            retryNumber: pass - 1,
            outcome: $"pass {pass}/{totalPasses}; burstCycles={burstCycles}");

        for (var burstCycle = 1; burstCycle <= burstCycles; burstCycle++)
        {
            if (burstCycles > 1)
            {
                WriteDiagnosticEvent(
                    "base-station wake burst cycle begin"
                    + $"; pass={pass}/{totalPasses}"
                    + $"; cycle={burstCycle}/{burstCycles}"
                    + $"; stations={DescribeBaseStations(baseStations)}");
            }

            await SendBaseStationCommandsAsync(
                baseStations,
                GetBaseStationPowerOnAction(pass, burstCycles, burstCycle),
                (baseStation, token, operation) => _baseStationGattClient.PowerOnAsync(baseStation, token, operation),
                cancellationToken,
                pass,
                totalPasses,
                pass - 1,
                BaseStationCommandTiming.PowerOnAttempts,
                index => stationSucceeded[index] = true);

            if (burstCycles > 1)
            {
                WriteDiagnosticEvent(
                    "base-station wake burst cycle complete"
                    + $"; pass={pass}/{totalPasses}"
                    + $"; cycle={burstCycle}/{burstCycles}"
                    + $"; succeededSoFar={stationSucceeded.Count(value => value)}/{baseStations.Length}");
            }

            if (burstCycle < burstCycles)
            {
                Console.WriteLine($"Waiting {BaseStationCommandTiming.UnsupportedV2PowerOnBurstDelay.TotalSeconds:0.0} seconds before the next unsupported V2 base-station wake burst...");
                await Task.Delay(BaseStationCommandTiming.UnsupportedV2PowerOnBurstDelay, cancellationToken);
            }
        }

        WriteDiagnosticEvent(
            "base-station wake pass complete"
            + $"; pass={pass}/{totalPasses}"
            + $"; succeeded={stationSucceeded.Count(value => value)}/{baseStations.Length}"
            + $"; burstCycles={burstCycles}"
            + $"; elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}"
            + $"; successes={string.Join(",", baseStations.Where((_, index) => stationSucceeded[index]).Select(station => station.BluetoothAddress))}");
        _baseStationDiagnostics.WriteEvent(
            "burstCompleted",
            "SteamVR autostart",
            configuredStationCount: baseStations.Length,
            currentStage: "burst",
            burstNumber: pass,
            retryNumber: pass - 1,
            outcome: $"{stationSucceeded.Count(value => value)}/{baseStations.Length} succeeded");

        return stationSucceeded;
    }

    private static bool ShouldUseUnsupportedV2PowerOnBurst(BaseStationDevice[] baseStations, int pass)
        => pass == 1 && baseStations.Any(baseStation =>
            baseStation.EffectiveVersion == BaseStationVersion.V2
            && baseStation.PowerStateReadUnsupported);

    private static string GetBaseStationPowerOnAction(int pass, int burstCycles, int burstCycle)
    {
        var action = pass == 1 ? "power on" : $"power on pass {pass}";
        return burstCycles > 1
            ? $"{action} burst {burstCycle}/{burstCycles}"
            : action;
    }

    private async Task<BaseStationPowerState[]> ReadBaseStationPowerStatesAsync(BaseStationDevice[] baseStations, CancellationToken cancellationToken, bool saveSettings = true)
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

                states[index] = await ReadBaseStationPowerStateWithTimeoutAsync(baseStation, cancellationToken);
                if (states[index] == BaseStationPowerState.Unsupported)
                {
                    baseStation.PowerStateReadUnsupported = true;
                    _baseStationSettingsNeedSave = saveSettings;
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

        if (_baseStationSettingsNeedSave && saveSettings)
        {
            TrySaveBaseStationSettings("base station power-state metadata");
            _baseStationSettingsNeedSave = false;
        }

        return states;
    }

    private async Task<BaseStationPowerState> ReadBaseStationPowerStateWithTimeoutAsync(
        BaseStationDevice baseStation,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(BaseStationCommandTiming.PowerStateReadTimeout);
        try
        {
            return await _baseStationGattClient.ReadPowerStateAsync(baseStation, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException($"Bluetooth power-state read did not finish within {BaseStationCommandTiming.PowerStateReadTimeout.TotalSeconds:0} seconds.");
        }
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

    private async Task StopManagedAppsWhileWaitingForWatchedProcessRestartAsync(CancellationToken cancellationToken)
    {
        if (!await _cleanupLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await StopManagedAppsAsync(ManagedAppStopReason.SessionEnding, cancellationToken);
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    private async Task StopManagedAppsAfterWatchedProcessExitAsync(bool waitForSteamVrServerExitBeforeBaseStationPowerDown, CancellationToken cancellationToken)
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

            RestoreMonitorLayout();
            await StopLovenseAppsAsync(cancellationToken);
            await StopManagedAppsAsync(ManagedAppStopReason.SessionEnding, cancellationToken);
            if (waitForSteamVrServerExitBeforeBaseStationPowerDown)
            {
                await WaitForSteamVrServerExitAsync(cancellationToken);
            }

            await TryPowerDownBaseStationsForSessionAsync(cancellationToken);
            _cleanupStarted = true;
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    private async Task RestoreMonitorsAndStopManagedAppsCoreAsync(bool waitForSteamVrServerExit, CancellationToken cancellationToken)
    {
        if (waitForSteamVrServerExit)
        {
            SetShutdownProgress("waiting for SteamVR server exit");
            await WaitForSteamVrServerExitAsync(cancellationToken);
        }

        await TryPowerDownBaseStationsForSessionAsync(cancellationToken);
        RestoreMonitorLayout();
        await StopLovenseAppsAsync(cancellationToken);
        await StopManagedAppsAsync(ManagedAppStopReason.SessionEnding, cancellationToken);
    }

    private bool ShouldExitWithSteamVr()
        => _managedSteamVrSession;

    private SteamVrTerminationDecision ObserveSteamVrLifecycle(string caller)
    {
        var decision = _steamVrLifecycle.Observe(GetProcesses(_config.SteamVrServerProcessNames), caller);
        if (decision.Classification != SteamVrTerminationClassification.None)
        {
            WriteSteamVrLifecycleDecision(decision);
        }

        return decision;
    }

    private async Task<bool> ApplySteamVrLifecycleDecisionAsync(
        SteamVrTerminationDecision decision,
        string cleanupMessage,
        CancellationToken cancellationToken)
    {
        if (decision.Classification == SteamVrTerminationClassification.None)
        {
            return false;
        }

        if (decision.ShowPersistentWarning)
        {
            _operatorWarning = decision.Reason;
            Console.WriteLine(decision.Reason);
            return false;
        }

        if (decision.CleanupPolicy != SteamVrCleanupPolicy.NormalCleanup)
        {
            return false;
        }

        _lifecyclePhase = SupervisorLifecyclePhase.ShutdownRoutineRunning;
        _steamVrLifecycle.MarkCleanupStarted();
        _shutdownBlockedBySteamVrSince = null;
        SetShutdownProgress("running cleanup after SteamVR exit");
        Console.WriteLine(cleanupMessage);
        await RestoreMonitorsAndStopManagedAppsAsync(waitForSteamVrServerExit: false, cancellationToken);
        _steamVrLifecycle.MarkCompleted();
        return true;
    }

    private void WriteSteamVrLifecycleDecision(SteamVrTerminationDecision decision)
    {
        var payload = new
        {
            @event = "steamvr_lifecycle_decision",
            timestamp = DateTimeOffset.UtcNow,
            sessionId = decision.SessionId,
            supervisorPid = decision.SupervisorPid,
            runtimeSessionOwned = decision.RuntimeSessionOwned,
            stateBefore = decision.StateBefore.ToString(),
            stateAfter = decision.StateAfter.ToString(),
            decision = decision.Classification.ToString(),
            decisionReason = decision.Reason,
            cleanupPolicy = decision.CleanupPolicy.ToString(),
            persistentWarning = decision.ShowPersistentWarning,
            shutdownIntent = decision.ShutdownIntentSource is not null,
            shutdownIntentSource = decision.ShutdownIntentSource,
            probeActive = decision.ProbeActive,
            observedProcesses = decision.ObservedProcesses.Select(process => new
            {
                pid = process.Pid,
                processName = process.ProcessName,
                processStartTime = process.ProcessStartTime,
                origin = process.Origin.ToString(),
                firstObservedAt = process.FirstObservedAt,
                lastObservedAt = process.LastObservedAt,
                hasExited = process.HasExited,
                exitCodeAvailable = process.ExitCodeAvailable,
                exitCode = process.ExitCode,
                exitCodeError = process.ExitCodeError
            }).ToArray(),
            companionProcesses = DescribeSteamVrCompanionProcesses(),
            caller = decision.Caller,
            classificationWindowStartedAt = decision.ClassificationWindowStartedAt,
            classificationWindowCompletedAt = decision.ClassificationWindowCompletedAt
        };
        WriteDiagnosticEvent(JsonSerializer.Serialize(payload, CommandBridgeJsonOptions));
    }

    private Dictionary<string, string> DescribeSteamVrCompanionProcesses()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["vrserver"] = DescribeRunningProcesses(["vrserver"]),
            ["vrmonitor"] = DescribeRunningProcesses(["vrmonitor"]),
            ["vrcompositor"] = DescribeRunningProcesses(["vrcompositor"]),
            ["vrdashboard"] = DescribeRunningProcesses(["vrdashboard"])
        };

    private IDisposable BeginOpenVrProbe() => _steamVrLifecycle.BeginOpenVrProbe();

    private void MarkSteamVrShutdownIntent(string source) => _steamVrLifecycle.MarkSupervisorShutdownRequested(source);

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
            _shutdownBlockedBySteamVrSince = null;
            SetShutdownProgress(null);
            return;
        }

        Console.WriteLine($"Waiting for SteamVR server to exit: {string.Join(", ", _config.SteamVrServerProcessNames)}");
        var startedAt = DateTimeOffset.UtcNow;
        _shutdownBlockedBySteamVrSince ??= startedAt;
        var lastDetailLogAt = DateTimeOffset.MinValue;
        while (IsAnyProcessRunning(_config.SteamVrServerProcessNames))
        {
            var now = DateTimeOffset.UtcNow;
            if (lastDetailLogAt == DateTimeOffset.MinValue || now - lastDetailLogAt >= TimeSpan.FromSeconds(20))
            {
                lastDetailLogAt = now;
                var elapsed = now - startedAt;
                var processDetails = DescribeRunningProcesses(_config.SteamVrServerProcessNames);
                Console.WriteLine($"Still waiting for SteamVR server to exit after {FormatElapsed(elapsed)}: {processDetails}");
                WriteDiagnosticEvent($"shutdown; waiting for steamvr exit; elapsedSeconds={elapsed.TotalSeconds:0.0}; processes={processDetails}");
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }

        _shutdownBlockedBySteamVrSince = null;
        SetShutdownProgress("SteamVR server exited");
        Console.WriteLine("SteamVR server has exited.");
        WriteDiagnosticEvent($"shutdown; steamvr exit observed; elapsedSeconds={(DateTimeOffset.UtcNow - startedAt).TotalSeconds:0.0}");
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

    private async Task TryStartOscRouterAsync(CancellationToken cancellationToken, bool manualOverride = false)
    {
        if (!_config.OscRouterEnabled && !manualOverride)
        {
            Console.WriteLine("OSC router is disabled by config.");
            return;
        }

        if (!_config.OscRouterEnabled && manualOverride)
        {
            Console.WriteLine("OSC router is disabled in the configuration. Running manual dashboard start anyway.");
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
            Console.WriteLine("Press 5 to launch or restart OSC routing.");
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
                    Console.WriteLine($"Could not scan BLE advertisements for Lovense: {ex.Message}. Use console hotkey 2 to launch OSCGoesBrrr manually.");
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
        if (hotkeys.ShowHelp)
        {
            PrintConsoleShortcutHelp();
        }

        var routineInvoked = false;
        if (hotkeys.LaunchBrokenEyeVrcFaceTracking)
        {
            await RestartCoreAppsAsync(cancellationToken);
            routineInvoked = true;
        }

        if (hotkeys.LaunchOscGoesBrrr)
        {
            await HandleOscGoesBrrrConsoleHotkeyAsync(cancellationToken);
            routineInvoked = true;
        }

        if (hotkeys.BaseStationsOn)
        {
            await ManualPowerOnBaseStationsAsync(cancellationToken);
            routineInvoked = true;
        }

        if (hotkeys.BaseStationsOff)
        {
            await ManualPowerDownBaseStationsAsync(cancellationToken);
            routineInvoked = true;
        }

        if (hotkeys.OscRouterLaunchOrRestart)
        {
            await LaunchOrRestartOscRouterFromConsoleAsync(cancellationToken);
            routineInvoked = true;
        }

        if (hotkeys.AfterLaunchAppsRoutine)
        {
            await RunAfterLaunchAppsRoutineAsync(cancellationToken);
            routineInvoked = true;
        }

        if (routineInvoked)
        {
            PrintConsoleShortcutHelp();
        }
    }

    private async Task HandleOscGoesBrrrConsoleHotkeyAsync(CancellationToken cancellationToken)
    {
        await RunOscGoesBrrrManualRoutineAsync("console hotkey", throwOnFailure: false, cancellationToken);
    }

    private async Task LaunchOrRestartOscRouterFromConsoleAsync(CancellationToken cancellationToken)
    {
        var manualOverride = !_config.OscRouterEnabled;
        if (manualOverride)
        {
            Console.WriteLine("OSC router is disabled in the configuration. Running manual console launch/restart anyway.");
        }

        if (_oscRouter is null)
        {
            Console.WriteLine("Launching OSC routing startup...");
            await TryStartOscRouterAsync(cancellationToken, manualOverride);
            if (!manualOverride)
            {
                ShowOscRouterRetryPromptIfNeeded();
            }

            return;
        }

        Console.WriteLine("Restarting OSC routing...");
        await RestartOscRouterAsync(cancellationToken, manualOverride);
    }

    private async Task StartOscGoesBrrrFromDashboardAsync(CancellationToken cancellationToken)
        => await RunOscGoesBrrrManualRoutineAsync("dashboard command", throwOnFailure: true, cancellationToken);

    private async Task RunOscGoesBrrrManualRoutineAsync(
        string source,
        bool throwOnFailure,
        CancellationToken cancellationToken)
    {
        if (!_config.OscGoesBrrrEnabled)
        {
            Console.WriteLine("OSCGoesBrrr is not enabled in the configuration.");
            Console.WriteLine($"Running manual {source} OSCGoesBrrr routine anyway.");
        }

        var intifaceRunning = IsIntifaceRunning();
        var oscGoesBrrrRunning = IsOscGoesBrrrRunning();
        if (intifaceRunning && oscGoesBrrrRunning)
        {
            Console.WriteLine($"OSCGoesBrrr and Intiface are running. Restarting both apps from {source}...");
            await StopLovenseAppsAsync(cancellationToken, manualOverride: true);
            await StartLovenseOscAsync(cancellationToken, throwOnFailure);
            return;
        }

        if (intifaceRunning || oscGoesBrrrRunning)
        {
            Console.WriteLine($"OSCGoesBrrr workflow is incomplete. Repairing from {source}...");
        }
        else
        {
            Console.WriteLine($"Launching OSCGoesBrrr workflow from {source}...");
        }

        await StartLovenseOscAsync(cancellationToken, throwOnFailure);
    }

    private static void PrintConsoleShortcutHelp()
    {
        Console.WriteLine("=== Console Hotkeys ===");
        Console.WriteLine("1 = Broken Eye + VRCFaceTracking routine");
        Console.WriteLine("2 = OSCGoesBrrr + Intiface routine");
        Console.WriteLine("3 = Turn on all controlled base stations");
        Console.WriteLine("4 = Turn off all controlled base stations");
        Console.WriteLine("5 = OSC Router launch/restart");
        Console.WriteLine("6 = Reload Autostart apps");
        Console.WriteLine("F1 = Show console shortcuts");
        Console.WriteLine("Terminal UI primary shortcuts: 0 help, F5 refresh, 1-6 actions, Q quit Terminal UI, Enter confirm, Esc cancel.");
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

    private string GetOscGoesBrrrStatus()
    {
        if (!_config.OscGoesBrrrEnabled)
        {
            return "disabled";
        }

        var intifaceRunning = IsIntifaceRunning();
        var oscGoesBrrrRunning = IsOscGoesBrrrRunning();
        if (intifaceRunning && oscGoesBrrrRunning)
        {
            return "running";
        }

        if (_config.OscGoesBrrrHotkeyEnabled && !_config.OscGoesBrrrBleScannerEnabled)
        {
            return intifaceRunning || oscGoesBrrrRunning
                ? "partial"
                : "manual";
        }

        return "incomplete";
    }

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
                if (key.Key == ConsoleKey.D1)
                {
                    hotkeys.LaunchBrokenEyeVrcFaceTracking = true;
                }
                else if (key.Key == ConsoleKey.D2)
                {
                    hotkeys.LaunchOscGoesBrrr = true;
                }
                else if (key.Key == ConsoleKey.D3)
                {
                    hotkeys.BaseStationsOn = true;
                }
                else if (key.Key == ConsoleKey.D4)
                {
                    hotkeys.BaseStationsOff = true;
                }
                else if (key.Key == ConsoleKey.D5)
                {
                    hotkeys.OscRouterLaunchOrRestart = true;
                }
                else if (key.Key == ConsoleKey.D6)
                {
                    hotkeys.AfterLaunchAppsRoutine = true;
                }
                else if (key.Key == ConsoleKey.F1)
                {
                    hotkeys.ShowHelp = true;
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

        Console.WriteLine("Starting Intiface non-elevated...");
        var intifacePath = ResolveLaunchPathOrThrow(_config.IntifacePath, "Intiface");
        var intifaceStarted = StartOrAttach(
            intifacePath,
            _config.IntifaceProcessNames,
            runAsAdmin: false,
            startMinimized: _config.IntifaceStartMinimized);
        await VerifyRunningAsync("Intiface", _config.IntifaceProcessNames, cancellationToken);
        if (intifaceStarted && _config.IntifaceStartMinimized)
        {
            await MinimizeProcessWindowsAsync("Intiface", _config.IntifaceProcessNames, cancellationToken);
        }

        _lovenseIntifaceStarted = true;
    }

    private async Task StartLovenseOscAsync(CancellationToken cancellationToken, bool throwOnFailure = false)
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

                Console.WriteLine("Starting OscGoesBrrr non-elevated...");
                var oscGoesBrrrPath = ResolveLaunchPathOrThrow(_config.OscGoesBrrrPath, "OscGoesBrrr");
                var oscGoesBrrrStarted = StartOrAttach(
                    oscGoesBrrrPath,
                    _config.OscGoesBrrrProcessNames,
                    runAsAdmin: false,
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
                var message = $"Could not complete OscGoesBrrr startup: {ex.Message}";
                Console.WriteLine(message);
                if (throwOnFailure)
                {
                    throw new InvalidOperationException(message, ex);
                }
            }
        }
        finally
        {
            _oscGoesBrrrLaunchLock.Release();
        }
    }

    private async Task StopLovenseAppsAsync(CancellationToken cancellationToken, bool manualOverride = false)
    {
        if (!manualOverride && !_config.OscGoesBrrrEnabled && !_lovenseWorkflowTriggered && !_lovenseIntifaceStarted)
        {
            return;
        }

        var oscGoesBrrrRunning = manualOverride
            ? IsOscGoesBrrrRunning()
            : _config.OscGoesBrrrEnabled && IsOscGoesBrrrRunning();
        var intifaceRunning = manualOverride
            ? IsIntifaceRunning()
            : _config.OscGoesBrrrEnabled && IsIntifaceRunning();

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
        var apps = GetEnabledAutoLaunchApps(skipCoreAppDuplicates: true);
        if (apps.Length == 0)
        {
            return;
        }

        Console.WriteLine("Starting configured auto-launch apps...");
        foreach (var app in apps)
        {
            await StartAutoLaunchAppAsync(app, cancellationToken);
        }
    }

    private async Task RunAfterLaunchAppsRoutineAsync(CancellationToken cancellationToken)
    {
        if (!await _autoLaunchAppsRoutineLock.WaitAsync(0, cancellationToken))
        {
            Console.WriteLine("Reload Autostart apps is already in progress.");
            return;
        }

        try
        {
            var apps = GetEnabledAutoLaunchApps(skipCoreAppDuplicates: true);
            if (apps.Length == 0)
            {
                Console.WriteLine("No enabled Autostart apps configured.");
                return;
            }

            var runningApps = apps
                .Where(app => IsAnyProcessRunning(app.ProcessNames))
                .ToArray();
            var runningCount = runningApps.Length;

            if (runningCount == apps.Length)
            {
                Console.WriteLine("All configured Autostart apps are running. Reloading all Autostart apps...");
                foreach (var app in apps.Reverse())
                {
                    await StopProcessesAsync(app.DisplayName, app.ProcessNames, cancellationToken);
                }

                foreach (var app in apps)
                {
                    await StartAutoLaunchAppAsync(app, cancellationToken);
                }

                return;
            }

            if (runningCount == 0)
            {
                Console.WriteLine("No configured Autostart apps are running. Starting all Autostart apps...");
            }
            else
            {
                var missingCount = apps.Length - runningCount;
                Console.WriteLine($"{missingCount} configured Autostart app(s) are not running. Starting missing apps only...");
            }

            foreach (var app in apps)
            {
                if (IsAnyProcessRunning(app.ProcessNames))
                {
                    Console.WriteLine($"{app.DisplayName} is already running.");
                    continue;
                }

                await StartAutoLaunchAppAsync(app, cancellationToken);
            }
        }
        finally
        {
            _autoLaunchAppsRoutineLock.Release();
        }
    }

    private async Task StartAutoLaunchAppAsync(ManagedAutoLaunchApp app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(app.Path))
        {
            Console.WriteLine($"Skipping auto-launch app \"{app.DisplayName}\" because the executable does not exist: {app.Path}");
            return;
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

    private ManagedAutoLaunchApp[] GetEnabledAutoLaunchApps(bool skipCoreAppDuplicates = false)
    {
        return _config.AutoLaunchApps
            .Where(app => app.Enabled && !string.IsNullOrWhiteSpace(app.Path))
            .Select(CreateManagedAutoLaunchApp)
            .Where(app => app is not null)
            .Cast<ManagedAutoLaunchApp>()
            .Where(app => !skipCoreAppDuplicates || !ShouldSkipDuplicateCoreAutoLaunchApp(app))
            .ToArray();
    }

    private bool ShouldSkipDuplicateCoreAutoLaunchApp(ManagedAutoLaunchApp app)
    {
        var appIdentity = CreateAutoLaunchExecutableIdentity(app.Path, app.DisplayName);
        if (appIdentity is null)
        {
            return false;
        }

        var matchingCoreApp = GetCoreAppExecutableIdentities()
            .FirstOrDefault(coreIdentity => AutoLaunchExecutableIdentitiesMatch(coreIdentity, appIdentity));
        if (matchingCoreApp is null)
        {
            return false;
        }

        Console.WriteLine(
            $"Warning: Autostart app '{app.Path}' matches configured core app '{matchingCoreApp.Label}' and will be skipped. Remove it from Autostart apps in the Configurator.");
        return true;
    }

    private AutoLaunchExecutableIdentity[] GetCoreAppExecutableIdentities()
    {
        return new[]
            {
                CreateAutoLaunchExecutableIdentity(_config.BrokenEyePath, "Broken Eye"),
                CreateAutoLaunchExecutableIdentity(_config.VrcFaceTrackingPath, "VRCFaceTracking")
            }
            .Where(identity => identity is not null)
            .Cast<AutoLaunchExecutableIdentity>()
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

    private static AutoLaunchExecutableIdentity? CreateAutoLaunchExecutableIdentity(string path, string label)
    {
        var normalized = TrimExecutablePath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var expanded = ExpandExecutablePath(normalized);
        var fullPath = TryGetExecutableFullPath(expanded);
        var fileName = Path.GetFileName(fullPath ?? expanded);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = Path.GetFileName(normalized);
        }

        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : new AutoLaunchExecutableIdentity(label, normalized, fullPath, fileName);
    }

    private static string TrimExecutablePath(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static string ExpandExecutablePath(string path)
    {
        try
        {
            return Environment.ExpandEnvironmentVariables(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string? TryGetExecutableFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static bool AutoLaunchExecutableIdentitiesMatch(AutoLaunchExecutableIdentity first, AutoLaunchExecutableIdentity second)
    {
        if (!string.IsNullOrWhiteSpace(first.FullPath)
            && !string.IsNullOrWhiteSpace(second.FullPath)
            && string.Equals(first.FullPath, second.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(first.FileName, second.FileName, StringComparison.OrdinalIgnoreCase);
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
        path = Environment.ExpandEnvironmentVariables(path);
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
        path = Environment.ExpandEnvironmentVariables(path);
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
        path = Environment.ExpandEnvironmentVariables(path);
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

    private static string ResolveLaunchPathOrThrow(string path, string displayName)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(path);
        if (string.IsNullOrWhiteSpace(expandedPath))
        {
            throw new FileNotFoundException($"{displayName} executable path is not configured.");
        }

        if (!File.Exists(expandedPath))
        {
            throw new FileNotFoundException($"{displayName} executable was not found at {expandedPath}.");
        }

        return expandedPath;
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

    private static string DescribeRunningProcesses(string[] processNames)
    {
        var processes = GetProcesses(processNames);
        try
        {
            if (processes.Count == 0)
            {
                return "none";
            }

            return string.Join(",", processes.Select(DescribeProcess));
        }
        finally
        {
            processes.ForEach(process => process.Dispose());
        }
    }

    private static string DescribeProcess(Process process)
    {
        var startTime = "unknown";
        try
        {
            startTime = new DateTimeOffset(process.StartTime).ToString("O", CultureInfo.InvariantCulture);
        }
        catch
        {
        }

        string processName;
        try
        {
            processName = process.ProcessName;
        }
        catch
        {
            processName = "unknown";
        }

        return $"{processName}(pid={process.Id},started={startTime})";
    }

    private static string DescribeBaseStations(BaseStationDevice[] baseStations)
        => string.Join(
            ",",
            baseStations.Select(station =>
                $"{station.DisplayName}[{station.BluetoothAddress},version={station.EffectiveVersion},unsupportedRead={station.PowerStateReadUnsupported}]"));

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static List<Process> GetProcesses(string[] processNames)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var result = new List<Process>();
        try
        {
            foreach (var processName in processNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                result.AddRange(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)));
            }

            return result;
        }
        finally
        {
            SupervisorDiagnosticsSession.RecordProcessDetectionStatic(
                Stopwatch.GetElapsedTime(startedAt),
                result.Count,
                processNames);
        }
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

internal sealed class SupervisorCommandServer : IDisposable
{
    public const int TcpPort = 37957;
    private readonly AppSupervisor _supervisor;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _serverTask;

    private SupervisorCommandServer(AppSupervisor supervisor)
    {
        _supervisor = supervisor;
        _serverTask = Task.Run(() => RunTcpAsync(supervisor, _ready, _shutdown.Token), CancellationToken.None);
    }

    public Task Ready => _ready.Task;

    public static SupervisorCommandServer Start(AppSupervisor supervisor, CancellationToken cancellationToken)
    {
        var server = new SupervisorCommandServer(supervisor);
        supervisor.WriteDebug($"command bridge starting; tcp=127.0.0.1:{TcpPort}");
        return server;
    }

    public void Dispose()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        _shutdown.Cancel();
        _supervisor.WriteDebug("command bridge stopping");
        try
        {
            _serverTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown should never block supervisor cleanup.
        }

        _shutdown.Dispose();
    }

    private static async Task RunTcpAsync(AppSupervisor supervisor, TaskCompletionSource ready, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, TcpPort);
        try
        {
            listener.Start();
            Console.WriteLine($"Dashboard command TCP endpoint ready: 127.0.0.1:{TcpPort}");
            supervisor.WriteDebug($"command bridge TCP endpoint ready; endpoint=127.0.0.1:{TcpPort}");
            ready.TrySetResult();
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(
                    () => HandleCommandTcpClientAsync(supervisor, client, cancellationToken),
                    CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ready.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Dashboard command TCP endpoint error: {ex.Message}");
            supervisor.WriteDebug("command bridge TCP endpoint error: " + ex.Message);
            ready.TrySetException(ex);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task HandleCommandTcpClientAsync(AppSupervisor supervisor, TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        })
        {
            var command = await reader.ReadLineAsync(cancellationToken) ?? "";
            var response = await supervisor.ExecuteSupervisorCommandAsync(command, cancellationToken);
            await writer.WriteLineAsync(response.AsMemory(), CancellationToken.None);
        }
    }
}

internal sealed record SteamVrTrackingReference(uint DeviceIndex, string[] IdentityValues);

internal sealed record SteamVrBaseStationMatchResult(
    int ExactMatchCount,
    int ActiveTrackingReferenceCount,
    bool AllMatchedExactly,
    bool CountFallbackMatched,
    string[] ExactMatchedBluetoothAddresses);

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
        var matchedBluetoothAddresses = new List<string>();
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
                if (!string.IsNullOrWhiteSpace(baseStation.BluetoothAddress))
                {
                    matchedBluetoothAddresses.Add(baseStation.BluetoothAddress);
                }

                exactMatches++;
                break;
            }
        }

        var allMatchedExactly = baseStations.Length > 0 && exactMatches == baseStations.Length;
        var countFallbackMatched = !allMatchedExactly && trackingReferences.Count >= baseStations.Length;
        return new SteamVrBaseStationMatchResult(
            exactMatches,
            trackingReferences.Count,
            allMatchedExactly,
            countFallbackMatched,
            matchedBluetoothAddresses.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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
        Func<bool> skipEmergencyCleanup,
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
                if (skipEmergencyCleanup())
                {
                    Console.WriteLine("Console close requested for forced manual reload. Skipping emergency cleanup.");
                    return true;
                }

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
        Func<bool> skipEmergencyCleanup,
        Func<Task> emergencyCleanupAsync,
        Action launchDetachedBaseStationCleanup)
        => OperatingSystem.IsWindows()
            ? new ConsoleCloseHandler(shutdown, supervisorStopped, skipEmergencyCleanup, emergencyCleanupAsync, launchDetachedBaseStationCleanup)
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
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private const string WatcherMutexName = @"Local\PimaxVrcSupervisorAutoLaunchWatcher";
    private const string VrServerProcessName = "vrserver";
    private static readonly string SkipCurrentSteamVrSessionMarkerPath =
        Path.Combine(Path.GetTempPath(), "PimaxVrcSupervisorSkipCurrentSteamVrSession.marker");

    public static async Task RunAsync(
        bool skipCurrentSteamVrSession,
        bool useDesktopTuiDefaultInterface,
        string? configPath,
        CancellationToken cancellationToken)
    {
        using var mutex = new Mutex(initiallyOwned: true, WatcherMutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            return;
        }

        var supervisorPath = ScheduledTaskInstaller.GetSupervisorExecutablePath();
        var supervisorProcessName = Path.GetFileNameWithoutExtension(supervisorPath);
        var launchedForCurrentSteamVrSession = (skipCurrentSteamVrSession || TryConsumeSkipCurrentSteamVrSessionMarker())
            && IsProcessRunning(VrServerProcessName);

        while (!cancellationToken.IsCancellationRequested)
        {
            var vrServerRunning = IsProcessRunning(VrServerProcessName);
            var supervisorRunning = IsAnotherSupervisorRunning(supervisorProcessName);

            if (!vrServerRunning)
            {
                launchedForCurrentSteamVrSession = false;
            }
            else if (!launchedForCurrentSteamVrSession)
            {
                if (!supervisorRunning)
                {
                    Console.WriteLine(useDesktopTuiDefaultInterface
                        ? "Watcher selected startup interface: Terminal UI."
                        : "Watcher selected startup interface: Classic Console.");
                    StartSupervisor(supervisorPath, configPath, useDesktopTuiDefaultInterface);
                }

                launchedForCurrentSteamVrSession = true;
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

    private static void StartSupervisor(
        string supervisorPath,
        string? configPath,
        bool useDesktopTuiDefaultInterface)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = supervisorPath,
            WorkingDirectory = Path.GetDirectoryName(supervisorPath) ?? Environment.CurrentDirectory,
            UseShellExecute = true,
            WindowStyle = useDesktopTuiDefaultInterface
                ? ProcessWindowStyle.Hidden
                : ProcessWindowStyle.Normal
        };
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(configPath);
        }

        startInfo.ArgumentList.Add("--managed-steamvr-session");
        if (useDesktopTuiDefaultInterface)
        {
            Console.WriteLine("Starting Supervisor with Terminal UI startup intent.");
            startInfo.ArgumentList.Add("--desktop-tui-start");
            startInfo.ArgumentList.Add("--launch-desktop-tui-after-ready");
        }

        Process.Start(startInfo);
    }

    public static void RequestSkipCurrentSteamVrSession()
    {
        try
        {
            File.WriteAllText(SkipCurrentSteamVrSessionMarkerPath, DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        }
        catch
        {
            // If the marker cannot be written, the watcher still works normally.
        }
    }

    private static bool TryConsumeSkipCurrentSteamVrSessionMarker()
    {
        try
        {
            if (!File.Exists(SkipCurrentSteamVrSessionMarkerPath))
            {
                return false;
            }

            File.Delete(SkipCurrentSteamVrSessionMarkerPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed record ScheduledTaskDetails(string TaskName, string TriggerDescription);

internal enum ScheduledTaskApplyOutcome
{
    AlreadyValid,
    Created,
    Repaired,
    Rebound,
    RemovedOrDisabled,
    Deferred,
    Failed
}

internal sealed record ScheduledTaskApplyResult(
    ScheduledTaskApplyOutcome Outcome,
    string TaskName,
    string TriggerDescription,
    string? MismatchReason = null,
    string? OldAction = null,
    string? NewAction = null)
{
    public string OperatorMessage => Outcome switch
    {
        ScheduledTaskApplyOutcome.AlreadyValid => "Scheduled task already valid; no changes made.",
        ScheduledTaskApplyOutcome.Created => "Scheduled task created.",
        ScheduledTaskApplyOutcome.Rebound => $"Scheduled task rebound to the current release: {MismatchReason}",
        ScheduledTaskApplyOutcome.Repaired => $"Scheduled task repaired: {MismatchReason}",
        ScheduledTaskApplyOutcome.RemovedOrDisabled => "Scheduled task removed or disabled.",
        ScheduledTaskApplyOutcome.Deferred => "Scheduled task update deferred.",
        ScheduledTaskApplyOutcome.Failed => $"Scheduled task update failed: {MismatchReason}",
        _ => "Scheduled task update completed."
    };

    public string LogValue => Outcome switch
    {
        ScheduledTaskApplyOutcome.AlreadyValid => "already-valid",
        ScheduledTaskApplyOutcome.Created => "created",
        ScheduledTaskApplyOutcome.Rebound => "rebound",
        ScheduledTaskApplyOutcome.Repaired => "repaired",
        ScheduledTaskApplyOutcome.RemovedOrDisabled => "removed-or-disabled",
        ScheduledTaskApplyOutcome.Deferred => "deferred",
        ScheduledTaskApplyOutcome.Failed => "failed",
        _ => "unknown"
    };
}

internal sealed record PimaxServiceLogEvent(DateTimeOffset Timestamp, bool IsRemove, bool IsAdd);

internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal static class ScheduledTaskInstaller
{
    private const string TaskName = ScheduledTaskPathValidator.AutoLaunchTaskName;
    public const string SteamVrStartTaskName = ScheduledTaskPathValidator.SteamVrStartTaskName;
    private const string SupervisorExecutableName = "PimaxVrcSupervisor.exe";
    private const string WatcherExecutableName = "PimaxVrcSupervisorWatcher.exe";
    private const string WatcherArgument = "--watch-vrchat-auto-launch";
    private const string SteamVrStartArgument = "--steamvr-start";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    public static async Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(
            "schtasks.exe",
            ["/Query", "/TN", TaskName],
            cancellationToken);

        return result.ExitCode == 0;
    }

    public static async Task<bool> ExistsAsync(string taskName, CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(
            "schtasks.exe",
            ["/Query", "/TN", taskName],
            cancellationToken);

        return result.ExitCode == 0;
    }

    public static async Task<ScheduledTaskApplyResult> CreateOrUpdateAsync(
        bool startWatcherImmediately,
        bool skipCurrentSteamVrSession,
        bool? useDesktopTuiDefaultInterface,
        string? configPath,
        CancellationToken cancellationToken)
    {
        var supervisorPath = GetSupervisorExecutablePath();
        var supervisorWorkingDirectory = Path.GetDirectoryName(supervisorPath) ?? AppContext.BaseDirectory;
        ScheduledTaskPathValidator.ThrowIfInvalidScheduledTaskExecutablePath(
            TaskName,
            supervisorPath,
            ScheduledTaskPathValidator.GetCurrentExecutableDirectory());
        var existingTask = await TryReadExistingWatcherTaskAsync(cancellationToken);
        var selectedInterface = ResolvePersistentInterface(
            useDesktopTuiDefaultInterface,
            existingTask,
            out var persistentInterfaceSource);
        Console.WriteLine($"Persistent interface source: {persistentInterfaceSource}.");
        var watcherPath = GetWatcherExecutablePath(supervisorPath);
        if (existingTask?.ParsedArguments.UnsupportedReason is { Length: > 0 } unsupportedReason)
        {
            Console.WriteLine($"Existing watcher arguments cannot be preserved safely: {unsupportedReason}");
            throw new InvalidOperationException("Existing scheduled task contains unsupported watcher arguments. The task was not rewritten.");
        }

        var preservedUnknownArguments = existingTask?.ParsedArguments.UnknownArguments ?? [];
        if (preservedUnknownArguments.Length > 0)
        {
            Console.WriteLine("Unknown watcher arguments preserved: " + string.Join(" ", preservedUnknownArguments));
        }
        var desiredArguments = BuildWatcherArguments(
            skipCurrentSteamVrSession,
            selectedInterface,
            configPath,
            preservedUnknownArguments);
        var desiredParsedArguments = ParseWatcherArguments(desiredArguments);
        if (desiredParsedArguments.UnsupportedReason is { Length: > 0 } desiredUnsupportedReason)
        {
            throw new InvalidOperationException($"Generated watcher arguments were invalid: {desiredUnsupportedReason}");
        }

        if (IsExistingTaskSemanticallyValid(
            existingTask,
            watcherPath,
            supervisorWorkingDirectory,
            desiredParsedArguments,
            out var mismatchReason))
        {
            Console.WriteLine("scheduled_task_validation; operationMode=ApplyExplicitly; valid=True; mismatches=none; mutationAttempted=False; watcherDeploymentAttempted=False; result=already-valid");
            if (startWatcherImmediately)
            {
                if (skipCurrentSteamVrSession)
                {
                    AutoLaunchWatcher.RequestSkipCurrentSteamVrSession();
                }

                await RunProcessAsync(
                    "schtasks.exe",
                    ["/Run", "/TN", TaskName],
                    cancellationToken);
            }

            return new ScheduledTaskApplyResult(
                ScheduledTaskApplyOutcome.AlreadyValid,
                TaskName,
                "Hidden elevated watcher at Windows sign-in; launches supervisor when vrserver.exe is running.");
        }

        await StopAutoLaunchWatcherAsync(cancellationToken);
        watcherPath = CreateOrUpdateWatcherExecutable(supervisorPath);
        ScheduledTaskPathValidator.ThrowIfInvalidScheduledTaskExecutablePath(
            TaskName,
            watcherPath,
            ScheduledTaskPathValidator.GetCurrentExecutableDirectory());
        var applyOutcome = existingTask is null
            ? ScheduledTaskApplyOutcome.Created
            : IsTaskRebound(existingTask, watcherPath, supervisorWorkingDirectory)
                ? ScheduledTaskApplyOutcome.Rebound
                : ScheduledTaskApplyOutcome.Repaired;
        var oldAction = existingTask is null
            ? null
            : $"{existingTask.Executable} {existingTask.Arguments}".Trim();
        var newAction = $"{watcherPath} {desiredArguments}".Trim();
        if (existingTask is not null)
        {
            Console.WriteLine($"Old action: {oldAction}");
            Console.WriteLine($"New action: {newAction}");
        }

        Console.WriteLine($"scheduled_task_validation; operationMode=ApplyExplicitly; valid=False; mismatches={mismatchReason}; mutationAttempted=True; watcherDeploymentAttempted=True; result={new ScheduledTaskApplyResult(applyOutcome, TaskName, "").LogValue}");

        var taskXml = BuildTaskXml(
            watcherPath,
            supervisorWorkingDirectory,
            "Runs an elevated hidden watcher that starts Pimax VRC Supervisor when vrserver.exe is running.",
            desiredArguments,
            includeLogonTrigger: true);
        var taskXmlPath = Path.Combine(Path.GetTempPath(), $"PimaxVrcSupervisorAutoLaunch-{Guid.NewGuid():N}.xml");

        try
        {
            await File.WriteAllTextAsync(taskXmlPath, taskXml, Encoding.Unicode, cancellationToken);
            try
            {
                await RunProcessAsync(
                    "schtasks.exe",
                    ["/Create", "/TN", TaskName, "/XML", taskXmlPath, "/F"],
                    cancellationToken);
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"{ex.Message} Verifying scheduled task state before continuing.");
            }
        }
        finally
        {
            TryDeleteFile(taskXmlPath);
        }

        if (!await ExistsAsync(cancellationToken))
        {
            throw new InvalidOperationException("schtasks.exe reported success, but the task could not be queried afterward.");
        }

        var verifiedTask = await TryReadExistingWatcherTaskAsync(cancellationToken);
        if (!IsExistingTaskSemanticallyValid(
            verifiedTask,
            watcherPath,
            supervisorWorkingDirectory,
            desiredParsedArguments,
            out var verificationMismatch))
        {
            Console.WriteLine($"Scheduled task verification failed after registration: {verificationMismatch}");
            throw new InvalidOperationException("Scheduled task was registered, but its effective settings still do not match the expected hidden watcher task.");
        }

        if (startWatcherImmediately)
        {
            if (skipCurrentSteamVrSession)
            {
                AutoLaunchWatcher.RequestSkipCurrentSteamVrSession();
            }

            await RunProcessAsync(
                "schtasks.exe",
                ["/Run", "/TN", TaskName],
                cancellationToken);
        }

        return new ScheduledTaskApplyResult(
            applyOutcome,
            TaskName,
            "Hidden elevated watcher at Windows sign-in; launches supervisor when vrserver.exe is running.",
            mismatchReason,
            oldAction,
            newAction);
    }

    public static async Task<bool> ValidateAutoLaunchTaskAsync(
        bool? useDesktopTuiDefaultInterface,
        string? configPath,
        CancellationToken cancellationToken)
    {
        var supervisorPath = GetSupervisorExecutablePath();
        var supervisorWorkingDirectory = Path.GetDirectoryName(supervisorPath) ?? AppContext.BaseDirectory;
        var watcherPath = GetWatcherExecutablePath(supervisorPath);
        var existingTask = await TryReadExistingWatcherTaskAsync(cancellationToken);
        var selectedInterface = ResolvePersistentInterface(
            useDesktopTuiDefaultInterface,
            existingTask,
            out var persistentInterfaceSource);
        Console.WriteLine($"Persistent interface source: {persistentInterfaceSource}.");

        if (existingTask?.ParsedArguments.UnsupportedReason is { Length: > 0 } unsupportedReason)
        {
            Console.WriteLine($"scheduled_task_validation; operationMode=ValidateOnly; valid=False; mismatches=unsupported-arguments:{unsupportedReason}; mutationAttempted=False; watcherDeploymentAttempted=False; result=unsupported-arguments");
            Console.WriteLine($"Existing watcher arguments cannot be preserved safely: {unsupportedReason}");
            return false;
        }

        var preservedUnknownArguments = existingTask?.ParsedArguments.UnknownArguments ?? [];
        var desiredArguments = BuildWatcherArguments(
            skipCurrentSteamVrSession: true,
            selectedInterface,
            configPath,
            preservedUnknownArguments);
        var desiredParsedArguments = ParseWatcherArguments(desiredArguments);
        if (desiredParsedArguments.UnsupportedReason is { Length: > 0 } desiredUnsupportedReason)
        {
            Console.WriteLine($"scheduled_task_validation; operationMode=ValidateOnly; valid=False; mismatches=generated-arguments:{desiredUnsupportedReason}; mutationAttempted=False; watcherDeploymentAttempted=False; result=invalid-generated-arguments");
            return false;
        }

        var valid = IsExistingTaskSemanticallyValid(
            existingTask,
            watcherPath,
            supervisorWorkingDirectory,
            desiredParsedArguments,
            out var mismatchReason);
        Console.WriteLine(valid
            ? "Scheduled task already valid; no changes made."
            : $"Scheduled task validation warning: {mismatchReason}");
        Console.WriteLine(
            "scheduled_task_validation"
            + "; operationMode=ValidateOnly"
            + $"; valid={valid}"
            + $"; mismatches={(valid ? "none" : mismatchReason)}"
            + "; mutationAttempted=False"
            + "; watcherDeploymentAttempted=False"
            + $"; result={(valid ? "valid" : "mismatch")}");
        return valid;
    }

    private static string BuildWatcherArguments(
        bool skipCurrentSteamVrSession,
        bool useDesktopTuiDefaultInterface,
        string? configPath,
        IReadOnlyList<string>? preservedUnknownArguments = null)
        => ScheduledTaskSemantics.BuildWatcherArguments(
            skipCurrentSteamVrSession,
            useDesktopTuiDefaultInterface,
            configPath,
            preservedUnknownArguments);

    private static bool ResolvePersistentInterface(
        bool? requestedDesktopTuiDefaultInterface,
        ExistingWatcherTask? existingTask,
        out string source)
        => ScheduledTaskSemantics.ResolvePersistentInterface(
            requestedDesktopTuiDefaultInterface,
            existingTask,
            out source);

    private static bool IsExistingTaskSemanticallyValid(
        ExistingWatcherTask? existingTask,
        string watcherPath,
        string workingDirectory,
        ParsedWatcherArguments desiredArguments,
        out string mismatchReason)
        => ScheduledTaskSemantics.IsExistingTaskSemanticallyValid(
            existingTask,
            watcherPath,
            workingDirectory,
            desiredArguments,
            out mismatchReason);

    private static bool IsTaskRebound(
        ExistingWatcherTask existingTask,
        string watcherPath,
        string workingDirectory)
        => ScheduledTaskSemantics.IsTaskRebound(existingTask, watcherPath, workingDirectory);

    private static async Task<ExistingWatcherTask?> TryReadExistingWatcherTaskAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(
            "schtasks.exe",
            ["/Query", "/TN", TaskName, "/XML"],
            cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            return null;
        }

        try
        {
            var document = XDocument.Parse(result.Output);
            var action = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Exec");
            var executable = action?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Command")
                ?.Value
                .Trim();
            var arguments = action?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Arguments")
                ?.Value
                .Trim() ?? "";
            var workingDirectory = action?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "WorkingDirectory")
                ?.Value
                .Trim();
            var parsed = ParseWatcherArguments(arguments);
            var settingMismatches = GetWatcherTaskSettingMismatches(document);
            return new ExistingWatcherTask(
                executable ?? "",
                arguments,
                workingDirectory ?? "",
                parsed,
                HasTaskElement(document, "LogonTrigger"),
                TaskElementEquals(document, "LogonType", "InteractiveToken"),
                TaskElementEquals(document, "RunLevel", "HighestAvailable"),
                TaskElementEquals(document, "MultipleInstancesPolicy", "IgnoreNew"),
                TaskElementEquals(document, "StartWhenAvailable", "true"),
                TaskElementEquals(document, "AllowStartOnDemand", "true"),
                TaskElementEquals(document, "Enabled", "true"),
                TaskElementEquals(document, "Hidden", "true"),
                TaskElementEquals(document, "ExecutionTimeLimit", "PT0S"),
                settingMismatches);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not parse existing scheduled task XML; it will be repaired if needed: {ex.Message}");
            return null;
        }
    }

    private static bool HasTaskElement(XDocument document, string localName)
        => document
            .Descendants()
            .Any(element => element.Name.LocalName == localName);

    private static bool TaskElementEquals(XDocument document, string localName, string expected)
        => document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value
            .Trim()
            .Equals(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static string[] GetWatcherTaskSettingMismatches(XDocument document)
    {
        var mismatches = new List<string>();
        var task = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Task");
        var principal = task?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Principal");
        var settings = task?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Settings");
        var logonTrigger = task?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "LogonTrigger");

        AddEffectiveMismatch(mismatches, "TriggerType", logonTrigger is null ? null : "LogonTrigger", "LogonTrigger", defaultWhenMissing: null);
        AddEffectiveMismatch(mismatches, "TriggerEnabled", GetChildValue(logonTrigger, "Enabled"), "true", defaultWhenMissing: "true");
        AddEffectiveMismatch(mismatches, "LogonType", GetChildValue(principal, "LogonType"), "InteractiveToken", defaultWhenMissing: null);
        AddEffectiveMismatch(mismatches, "RunLevel", GetChildValue(principal, "RunLevel"), "HighestAvailable", defaultWhenMissing: null);
        AddEffectiveMismatch(mismatches, "MultipleInstancesPolicy", GetChildValue(settings, "MultipleInstancesPolicy"), "IgnoreNew", defaultWhenMissing: "IgnoreNew");
        AddEffectiveMismatch(mismatches, "DisallowStartIfOnBatteries", GetChildValue(settings, "DisallowStartIfOnBatteries"), "false", defaultWhenMissing: "false");
        AddEffectiveMismatch(mismatches, "StopIfGoingOnBatteries", GetChildValue(settings, "StopIfGoingOnBatteries"), "false", defaultWhenMissing: "false");
        AddEffectiveMismatch(mismatches, "AllowHardTerminate", GetChildValue(settings, "AllowHardTerminate"), "true", defaultWhenMissing: "true");
        AddEffectiveMismatch(mismatches, "StartWhenAvailable", GetChildValue(settings, "StartWhenAvailable"), "true", defaultWhenMissing: "true");
        AddEffectiveMismatch(mismatches, "RunOnlyIfNetworkAvailable", GetChildValue(settings, "RunOnlyIfNetworkAvailable"), "false", defaultWhenMissing: "false");
        AddEffectiveMismatch(mismatches, "AllowStartOnDemand", GetChildValue(settings, "AllowStartOnDemand"), "true", defaultWhenMissing: "true");
        AddEffectiveMismatch(mismatches, "Enabled", GetChildValue(settings, "Enabled"), "true", defaultWhenMissing: "true");
        AddEffectiveMismatch(mismatches, "Hidden", GetChildValue(settings, "Hidden"), "true", defaultWhenMissing: "false");
        AddEffectiveMismatch(mismatches, "RunOnlyIfIdle", GetChildValue(settings, "RunOnlyIfIdle"), "false", defaultWhenMissing: "false");
        AddEffectiveMismatch(mismatches, "WakeToRun", GetChildValue(settings, "WakeToRun"), "false", defaultWhenMissing: "false");
        AddEffectiveMismatch(mismatches, "ExecutionTimeLimit", NormalizeTaskDuration(GetChildValue(settings, "ExecutionTimeLimit")), "PT0S", defaultWhenMissing: "PT0S");
        AddEffectiveMismatch(mismatches, "Priority", GetChildValue(settings, "Priority"), "7", defaultWhenMissing: "7");

        var restartOnFailure = settings?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "RestartOnFailure");
        AddEffectiveMismatch(mismatches, "RestartInterval", NormalizeTaskDuration(GetChildValue(restartOnFailure, "Interval")), "PT1M", defaultWhenMissing: "PT1M");
        AddEffectiveMismatch(mismatches, "RestartCount", GetChildValue(restartOnFailure, "Count"), "3", defaultWhenMissing: "3");

        return mismatches.ToArray();
    }

    private static void AddEffectiveMismatch(List<string> mismatches, string name, string? existing, string expected, string? defaultWhenMissing)
    {
        var effectiveExisting = string.IsNullOrWhiteSpace(existing)
            ? defaultWhenMissing
            : existing.Trim();
        if (effectiveExisting is not null
            && string.Equals(effectiveExisting, expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        mismatches.Add($"Setting: {name}; Existing: {effectiveExisting ?? "<missing>"}; Expected: {expected}");
    }

    private static string? GetChildValue(XElement? parent, string localName)
        => parent?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value
            .Trim();

    private static string? NormalizeTaskDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Trim().Equals("P0D", StringComparison.OrdinalIgnoreCase)
            ? "PT0S"
            : value.Trim();
    }

    private static ParsedWatcherArguments ParseWatcherArguments(string arguments)
        => ScheduledTaskSemantics.ParseWatcherArguments(arguments);

    private static bool HasBalancedQuotes(string value)
    {
        var inQuotes = false;
        foreach (var character in value)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
            }
        }

        return !inQuotes;
    }

    private static List<string> SplitCommandLine(string arguments)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var character = arguments[index];
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }

    private static bool StringArraysEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string QuoteArgumentIfNeeded(string argument)
        => argument.Any(char.IsWhiteSpace) ? QuoteArgument(argument) : argument;

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        }

        return string.Equals(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(left)),
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteArgument(string argument)
        => string.IsNullOrEmpty(argument)
            ? "\"\""
            : "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    public static async Task<ScheduledTaskDetails> CreateOrUpdateSteamVrStartHelperAsync(CancellationToken cancellationToken)
    {
        var supervisorPath = GetSupervisorExecutablePath();
        var supervisorWorkingDirectory = Path.GetDirectoryName(supervisorPath) ?? AppContext.BaseDirectory;
        ScheduledTaskPathValidator.ThrowIfInvalidScheduledTaskExecutablePath(
            SteamVrStartTaskName,
            supervisorPath,
            ScheduledTaskPathValidator.GetCurrentExecutableDirectory());
        var taskXml = BuildDirectTaskXml(
            supervisorPath,
            supervisorWorkingDirectory,
            "Starts Pimax VRC Supervisor elevated when the SteamVR manifest host requests SteamVR startup mode.",
            SteamVrStartArgument);
        var taskXmlPath = Path.Combine(Path.GetTempPath(), $"PimaxVrcSupervisorSteamVrStart-{Guid.NewGuid():N}.xml");

        try
        {
            await File.WriteAllTextAsync(taskXmlPath, taskXml, Encoding.Unicode, cancellationToken);
            try
            {
                await RunProcessAsync(
                    "schtasks.exe",
                    ["/Create", "/TN", SteamVrStartTaskName, "/XML", taskXmlPath, "/F"],
                    cancellationToken);
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"{ex.Message} Verifying scheduled task state before continuing.");
            }
        }
        finally
        {
            TryDeleteFile(taskXmlPath);
        }

        if (!await ExistsAsync(SteamVrStartTaskName, cancellationToken))
        {
            throw new InvalidOperationException("schtasks.exe reported success, but the SteamVR start helper task could not be queried afterward.");
        }

        return new ScheduledTaskDetails(SteamVrStartTaskName, "Elevated on-demand task launched by the SteamVR host.");
    }

    public static async Task DeleteAutoLaunchTaskAsync(CancellationToken cancellationToken)
    {
        await StopAutoLaunchWatcherAsync(cancellationToken);
        await DeleteTaskAsync(TaskName, cancellationToken);
    }

    public static async Task DeleteSteamVrStartHelperAsync(CancellationToken cancellationToken)
        => await DeleteTaskAsync(SteamVrStartTaskName, cancellationToken);

    private static async Task DeleteTaskAsync(string taskName, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(taskName, cancellationToken))
        {
            return;
        }

        try
        {
            await RunProcessAsync(
                "schtasks.exe",
                ["/Delete", "/TN", taskName, "/F"],
                cancellationToken);
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine($"{ex.Message} Verifying scheduled task removal before continuing.");
            if (await ExistsAsync(taskName, cancellationToken))
            {
                throw;
            }
        }
    }

    private static async Task StopAutoLaunchWatcherAsync(CancellationToken cancellationToken)
    {
        await TryEndTaskAsync(TaskName, cancellationToken);
        await TryStopWatcherProcessesAsync(cancellationToken);
    }

    private static async Task TryEndTaskAsync(string taskName, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(taskName, cancellationToken))
        {
            return;
        }

        try
        {
            var result = await RunProcessCaptureAsync(
                "schtasks.exe",
                ["/End", "/TN", taskName],
                cancellationToken);
            if (result.ExitCode != 0
                && !result.Error.Contains("not currently running", StringComparison.OrdinalIgnoreCase)
                && !result.Output.Contains("not currently running", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Could not stop scheduled task {taskName}: {result.Error}{result.Output}".Trim());
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Could not stop scheduled task {taskName}: {ex.Message}");
        }
    }

    private static async Task TryStopWatcherProcessesAsync(CancellationToken cancellationToken)
    {
        var script = """
            $needle = '--watch-vrchat-auto-launch'
            $deadline = (Get-Date).AddSeconds(3)
            do {
                $watchers = @(Get-CimInstance Win32_Process |
                    Where-Object { $_.CommandLine -like "*$needle*" })
                foreach ($watcher in $watchers) {
                    Stop-Process -Id $watcher.ProcessId -Force -ErrorAction SilentlyContinue
                }
                if ($watchers.Count -eq 0) {
                    exit 0
                }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            exit 1
            """;

        try
        {
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var result = await RunProcessCaptureAsync(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encodedCommand],
                cancellationToken);
            if (result.ExitCode != 0)
            {
                Console.WriteLine($"Could not stop stale CLI watcher process(es): {result.Error}{result.Output}".Trim());
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Could not stop stale CLI watcher process(es): {ex.Message}");
        }
    }

    private static string BuildTaskXml(
        string supervisorPath,
        string supervisorWorkingDirectory,
        string description,
        string argument,
        bool includeLogonTrigger)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var identity = WindowsIdentity.GetCurrent();
        var triggerElement = includeLogonTrigger
            ? new XElement(ns + "Triggers",
                new XElement(ns + "LogonTrigger",
                    new XElement(ns + "Enabled", "true")))
            : null;

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", description)),
                triggerElement,
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
                    new XElement(ns + "RestartOnFailure",
                        new XElement(ns + "Interval", "PT1M"),
                        new XElement(ns + "Count", "3")),
                    new XElement(ns + "Priority", "7")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", supervisorPath),
                        new XElement(ns + "Arguments", argument),
                        new XElement(ns + "WorkingDirectory", supervisorWorkingDirectory)))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildDirectTaskXml(
        string supervisorPath,
        string supervisorWorkingDirectory,
        string description,
        string argument)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var identity = WindowsIdentity.GetCurrent();
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", description)),
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
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "WakeToRun", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", "7")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", supervisorPath),
                        new XElement(ns + "Arguments", argument),
                        new XElement(ns + "WorkingDirectory", supervisorWorkingDirectory)))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static string GetSupervisorExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && string.Equals(Path.GetFileName(processPath), SupervisorExecutableName, StringComparison.OrdinalIgnoreCase))
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

        var fallbackPath = Path.Combine(AppContext.BaseDirectory, SupervisorExecutableName);
        if (File.Exists(fallbackPath))
        {
            return fallbackPath;
        }

        throw new InvalidOperationException("Could not resolve PimaxVrcSupervisor.exe for the scheduled task action.");
    }

    private static string CreateOrUpdateWatcherExecutable(string supervisorPath)
    {
        var watcherPath = GetWatcherExecutablePath(supervisorPath);
        File.Copy(supervisorPath, watcherPath, overwrite: true);
        return watcherPath;
    }

    private static string GetWatcherExecutablePath(string supervisorPath)
        => Path.Combine(Path.GetDirectoryName(supervisorPath) ?? AppContext.BaseDirectory, WatcherExecutableName);

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

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ProcessTimeout);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw new TimeoutException($"{fileName} did not finish within {ProcessTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds.");
        }

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

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

}

internal sealed record SteamVrStartupDetails(string AppKey, string ManifestPath);

internal static class StartupIntegration
{
    public static async Task ApplyAsync(
        SupervisorConfig config,
        bool useDesktopTuiDefaultInterface,
        CancellationToken cancellationToken)
    {
        switch (config.GetEffectiveStartupLaunchMode())
        {
            case StartupLaunchMode.ScheduledTask:
                LogStep("Creating or updating VRChat auto-launch scheduled task...");
                var taskResult = await ScheduledTaskInstaller.CreateOrUpdateAsync(
                    startWatcherImmediately: true,
                    skipCurrentSteamVrSession: true,
                    useDesktopTuiDefaultInterface,
                    config.LoadedFromPath,
                    cancellationToken);
                LogStep(taskResult.OperatorMessage);
                LogStep("Disabling SteamVR startup manifest if present...");
                await SteamVrStartupInstaller.DisableAsync(cancellationToken);
                LogStep("Deleting SteamVR start helper scheduled task if present...");
                await ScheduledTaskInstaller.DeleteSteamVrStartHelperAsync(cancellationToken);
                break;
            case StartupLaunchMode.SteamVrManifest:
                LogStep("Deleting VRChat auto-launch scheduled task if present...");
                await ScheduledTaskInstaller.DeleteAutoLaunchTaskAsync(cancellationToken);
                LogStep("Creating or updating SteamVR startup manifest...");
                await SteamVrStartupInstaller.CreateOrUpdateAsync(cancellationToken);
                LogStep("SteamVR startup manifest is ready.");
                break;
            case StartupLaunchMode.None:
                LogStep("Deleting VRChat auto-launch scheduled task if present...");
                await ScheduledTaskInstaller.DeleteAutoLaunchTaskAsync(cancellationToken);
                LogStep("Disabling SteamVR startup manifest if present...");
                await SteamVrStartupInstaller.DisableAsync(cancellationToken);
                LogStep("Deleting SteamVR start helper scheduled task if present...");
                await ScheduledTaskInstaller.DeleteSteamVrStartHelperAsync(cancellationToken);
                LogStep("Automatic startup integrations are disabled.");
                break;
            default:
                LogStep("Startup mode is unspecified; no startup integration changes were applied.");
                break;
        }
    }

    private static void LogStep(string message)
    {
        Console.WriteLine(message);
        Console.Out.Flush();
    }
}

internal static class SteamVrStartupInstaller
{
    public const string AppKey = "pimax.vrcsupervisor.dashboard";
    private const string ManifestFileName = "PimaxVrcSupervisor.vrmanifest";
    private const string HostExecutableName = "PimaxVrcSupervisorSteamVrHost.exe";
    private const string HostIconRelativePath = @"Assets\vr-overlay-icon.png";
    private static readonly TimeSpan OpenVrRegistryTimeout = TimeSpan.FromSeconds(3);

    public static async Task<SteamVrStartupDetails> CreateOrUpdateAsync(CancellationToken cancellationToken)
    {
        await ScheduledTaskInstaller.CreateOrUpdateSteamVrStartHelperAsync(cancellationToken);

        var manifestPath = GetManifestPath();
        var hostPath = GetHostExecutablePath();
        var iconPath = GetHostIconPath(hostPath);
        if (!File.Exists(hostPath))
        {
            throw new FileNotFoundException("SteamVR host executable was not found. Publish/copy PimaxVrcSupervisorSteamVrHost.exe next to PimaxVrcSupervisor.exe.", hostPath);
        }

        var staleManifestPaths = GetKnownPimaxManifestPaths(manifestPath).ToArray();

        if (File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
        }

        await File.WriteAllTextAsync(manifestPath, BuildManifestJson(hostPath, iconPath), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        await RunOpenVrApplicationRegistryOperationAsync(
            () =>
            {
                using var registry = OpenVrApplicationRegistry.Open();
                registry.TrySetApplicationAutoLaunch(AppKey, false);
                foreach (var staleManifestPath in staleManifestPaths)
                {
                    registry.TryRemoveApplicationManifest(staleManifestPath);
                }

                registry.AddApplicationManifest(manifestPath);
                registry.SetApplicationAutoLaunch(AppKey, true);
            },
            "SteamVR manifest registration timed out.",
            cancellationToken);
        return new SteamVrStartupDetails(AppKey, manifestPath);
    }

    public static async Task DisableAsync(CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath();
        try
        {
            var staleManifestPaths = GetKnownPimaxManifestPaths(manifestPath).ToArray();
            await RunOpenVrApplicationRegistryOperationAsync(
                () =>
                {
                    using var registry = OpenVrApplicationRegistry.Open();
                    registry.TrySetApplicationAutoLaunch(AppKey, false);
                    foreach (var staleManifestPath in staleManifestPaths)
                    {
                        registry.TryRemoveApplicationManifest(staleManifestPath);
                    }
                },
                "SteamVR manifest disable timed out.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SteamVR manifest disable skipped: {ex.Message}");
        }

        if (File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
        }

        await Task.CompletedTask;
    }

    private static async Task RunOpenVrApplicationRegistryOperationAsync(
        Action operation,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        var operationTask = Task.Run(operation);
        var timeoutTask = Task.Delay(OpenVrRegistryTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(operationTask, timeoutTask);
        if (completedTask == operationTask)
        {
            await operationTask;
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"{timeoutMessage} OpenVR did not respond within {OpenVrRegistryTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds.");
    }

    private static string GetManifestPath()
        => Path.Combine(AppContext.BaseDirectory, ManifestFileName);

    private static string GetHostExecutablePath()
        => Path.Combine(Path.GetDirectoryName(ScheduledTaskInstaller.GetSupervisorExecutablePath()) ?? AppContext.BaseDirectory, HostExecutableName);

    private static string GetHostIconPath(string hostPath)
        => Path.Combine(Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory, HostIconRelativePath);

    private static string BuildManifestJson(string hostPath, string iconPath)
    {
        var workingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory;
        var manifest = new
        {
            source = "builtin",
            applications = new[]
            {
                new
                {
                    app_key = AppKey,
                    launch_type = "binary",
                    binary_path_windows = hostPath,
                    working_directory = workingDirectory,
                    image_path = iconPath,
                    is_dashboard_overlay = true,
                    strings = new
                    {
                        en_us = new
                        {
                            name = "Pimax VRC Supervisor",
                            description = "Pimax VRC Supervisor SteamVR dashboard controls"
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    private static IEnumerable<string> GetKnownPimaxManifestPaths(string currentManifestPath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(currentManifestPath)
        };

        var openVrPathsFile = GetOpenVrPathsFile();
        if (File.Exists(openVrPathsFile))
        {
            foreach (var registeredManifestPath in ReadRegisteredApplicationManifestPaths(openVrPathsFile))
            {
                if (IsPimaxManifestPath(registeredManifestPath))
                {
                    paths.Add(Path.GetFullPath(registeredManifestPath));
                }
            }

            foreach (var appConfigPath in GetSteamVrAppConfigPaths(openVrPathsFile))
            {
                foreach (var registeredManifestPath in ReadSteamVrAppConfigManifestPaths(appConfigPath))
                {
                    if (IsPimaxManifestPath(registeredManifestPath))
                    {
                        paths.Add(Path.GetFullPath(registeredManifestPath));
                    }
                }
            }
        }

        return paths;
    }

    private static bool IsPimaxManifestPath(string manifestPath)
    {
        if (string.Equals(Path.GetFileName(manifestPath), ManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("applications", out var applicationsElement)
                || applicationsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var applicationElement in applicationsElement.EnumerateArray())
            {
                if (applicationElement.TryGetProperty("app_key", out var appKeyElement)
                    && string.Equals(appKeyElement.GetString(), AppKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static IEnumerable<string> ReadRegisteredApplicationManifestPaths(string openVrPathsFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(openVrPathsFile));
        if (!document.RootElement.TryGetProperty("applications", out var applicationsElement)
            || applicationsElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in applicationsElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } manifestPath)
            {
                yield return manifestPath;
            }
        }
    }

    private static IEnumerable<string> GetSteamVrAppConfigPaths(string openVrPathsFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(openVrPathsFile));
        if (!document.RootElement.TryGetProperty("config", out var configElement)
            || configElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in configElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } configDirectory)
            {
                yield return Path.Combine(configDirectory.TrimEnd('\\', '/'), "appconfig.json");
            }
        }
    }

    private static IEnumerable<string> ReadSteamVrAppConfigManifestPaths(string appConfigPath)
    {
        if (!File.Exists(appConfigPath))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(appConfigPath));
        if (!document.RootElement.TryGetProperty("manifest_paths", out var manifestPathsElement)
            || manifestPathsElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in manifestPathsElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } manifestPath)
            {
                yield return manifestPath;
            }
        }
    }

    private static string GetOpenVrPathsFile()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openvr",
            "openvrpaths.vrpath");
}

internal sealed class OpenVrApplicationRegistry : IDisposable
{
    private const string OpenVrApplicationsFnTableVersion = "FnTable:IVRApplications_007";
    private const int VrApplicationUtility = 4;
    private const int VrInitErrorNone = 0;
    private const int VrApplicationErrorNone = 0;

    private readonly IntPtr _library;
    private readonly VrShutdownInternalDelegate _shutdownInternal;
    private readonly VrGetVrInitErrorAsEnglishDescriptionDelegate? _getInitErrorDescription;
    private readonly AddApplicationManifestDelegate _addApplicationManifest;
    private readonly RemoveApplicationManifestDelegate _removeApplicationManifest;
    private readonly SetApplicationAutoLaunchDelegate _setApplicationAutoLaunch;
    private readonly GetApplicationsErrorNameFromEnumDelegate _getApplicationsErrorNameFromEnum;
    private bool _initialized = true;

    private OpenVrApplicationRegistry(
        IntPtr library,
        VrShutdownInternalDelegate shutdownInternal,
        VrGetVrInitErrorAsEnglishDescriptionDelegate? getInitErrorDescription,
        OpenVrApplicationsFnTable table)
    {
        _library = library;
        _shutdownInternal = shutdownInternal;
        _getInitErrorDescription = getInitErrorDescription;
        _addApplicationManifest = CreateDelegate<AddApplicationManifestDelegate>(table.AddApplicationManifest);
        _removeApplicationManifest = CreateDelegate<RemoveApplicationManifestDelegate>(table.RemoveApplicationManifest);
        _setApplicationAutoLaunch = CreateDelegate<SetApplicationAutoLaunchDelegate>(table.SetApplicationAutoLaunch);
        _getApplicationsErrorNameFromEnum = CreateDelegate<GetApplicationsErrorNameFromEnumDelegate>(table.GetApplicationsErrorNameFromEnum);
    }

    public static OpenVrApplicationRegistry Open()
    {
        if (!TryFindOpenVrApiDll(out var openVrApiDllPath, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var library = NativeLibrary.Load(openVrApiDllPath);
        try
        {
            var initInternal = GetExportDelegate<VrInitInternalDelegate>(library, "VR_InitInternal");
            var shutdownInternal = GetExportDelegate<VrShutdownInternalDelegate>(library, "VR_ShutdownInternal");
            var getGenericInterface = GetExportDelegate<VrGetGenericInterfaceDelegate>(library, "VR_GetGenericInterface");
            var getInitErrorDescription = GetOptionalExportDelegate<VrGetVrInitErrorAsEnglishDescriptionDelegate>(library, "VR_GetVRInitErrorAsEnglishDescription");

            var initError = 0;
            _ = initInternal(ref initError, VrApplicationUtility);
            if (initError != VrInitErrorNone)
            {
                throw new InvalidOperationException($"OpenVR init failed: {DescribeOpenVrInitError(initError, getInitErrorDescription)}");
            }

            var interfaceError = 0;
            var applicationsTablePointer = getGenericInterface(OpenVrApplicationsFnTableVersion, ref interfaceError);
            if (applicationsTablePointer == IntPtr.Zero || interfaceError != VrInitErrorNone)
            {
                shutdownInternal();
                throw new InvalidOperationException($"OpenVR applications interface unavailable: {DescribeOpenVrInitError(interfaceError, getInitErrorDescription)}");
            }

            var table = Marshal.PtrToStructure<OpenVrApplicationsFnTable>(applicationsTablePointer);
            return new OpenVrApplicationRegistry(library, shutdownInternal, getInitErrorDescription, table);
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
    }

    public void AddApplicationManifest(string manifestPath)
        => ThrowIfApplicationError(_addApplicationManifest(manifestPath, false), "AddApplicationManifest");

    public void RemoveApplicationManifest(string manifestPath)
        => ThrowIfApplicationError(_removeApplicationManifest(manifestPath), "RemoveApplicationManifest");

    public void TryRemoveApplicationManifest(string manifestPath)
    {
        try
        {
            _removeApplicationManifest(manifestPath);
        }
        catch
        {
            // Re-registration should continue if the manifest was not already registered.
        }
    }

    public void SetApplicationAutoLaunch(string appKey, bool autoLaunch)
        => ThrowIfApplicationError(_setApplicationAutoLaunch(appKey, autoLaunch), "SetApplicationAutoLaunch");

    public void TrySetApplicationAutoLaunch(string appKey, bool autoLaunch)
    {
        try
        {
            _setApplicationAutoLaunch(appKey, autoLaunch);
        }
        catch
        {
            // The app may not be registered yet, or SteamVR may still hold an old manifest entry.
        }
    }

    public void Dispose()
    {
        try
        {
            if (_initialized)
            {
                _shutdownInternal();
                _initialized = false;
            }
        }
        finally
        {
            NativeLibrary.Free(_library);
        }
    }

    private void ThrowIfApplicationError(int error, string operation)
    {
        if (error == VrApplicationErrorNone)
        {
            return;
        }

        var errorNamePointer = _getApplicationsErrorNameFromEnum(error);
        var errorName = errorNamePointer == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(errorNamePointer);
        throw new InvalidOperationException($"{operation} failed: {(string.IsNullOrWhiteSpace(errorName) ? error.ToString(CultureInfo.InvariantCulture) : errorName)}");
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
    private struct OpenVrApplicationsFnTable
    {
        public IntPtr AddApplicationManifest;
        public IntPtr RemoveApplicationManifest;
        public IntPtr IsApplicationInstalled;
        public IntPtr GetApplicationCount;
        public IntPtr GetApplicationKeyByIndex;
        public IntPtr GetApplicationKeyByProcessId;
        public IntPtr LaunchApplication;
        public IntPtr LaunchTemplateApplication;
        public IntPtr LaunchApplicationFromMimeType;
        public IntPtr LaunchDashboardOverlay;
        public IntPtr CancelApplicationLaunch;
        public IntPtr IdentifyApplication;
        public IntPtr GetApplicationProcessId;
        public IntPtr GetApplicationsErrorNameFromEnum;
        public IntPtr GetApplicationPropertyString;
        public IntPtr GetApplicationPropertyBool;
        public IntPtr GetApplicationPropertyUint64;
        public IntPtr SetApplicationAutoLaunch;
        public IntPtr GetApplicationAutoLaunch;
        public IntPtr SetDefaultApplicationForMimeType;
        public IntPtr GetDefaultApplicationForMimeType;
        public IntPtr GetApplicationSupportedMimeTypes;
        public IntPtr GetApplicationsThatSupportMimeType;
        public IntPtr GetApplicationLaunchArguments;
        public IntPtr GetStartingApplication;
        public IntPtr GetSceneApplicationState;
        public IntPtr PerformApplicationPrelaunchCheck;
        public IntPtr GetSceneApplicationStateNameFromEnum;
        public IntPtr LaunchInternalProcess;
        public IntPtr RegisterSubprocess;
        public IntPtr GetCurrentSceneProcessId;
    }

    private delegate IntPtr VrInitInternalDelegate(ref int error, int applicationType);
    private delegate void VrShutdownInternalDelegate();
    private delegate IntPtr VrGetGenericInterfaceDelegate(string interfaceVersion, ref int error);
    private delegate IntPtr VrGetVrInitErrorAsEnglishDescriptionDelegate(int error);
    private delegate int AddApplicationManifestDelegate([MarshalAs(UnmanagedType.LPStr)] string manifestPath, [MarshalAs(UnmanagedType.I1)] bool temporary);
    private delegate int RemoveApplicationManifestDelegate([MarshalAs(UnmanagedType.LPStr)] string manifestPath);
    private delegate int SetApplicationAutoLaunchDelegate([MarshalAs(UnmanagedType.LPStr)] string appKey, [MarshalAs(UnmanagedType.I1)] bool autoLaunch);
    private delegate IntPtr GetApplicationsErrorNameFromEnumDelegate(int error);
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
    private const string ActiveConfigSelectionFileName = "supervisor.active-config.txt";

    public string DisplayName { get; init; } = "";
    public string BrokenEyePath { get; set; } = "";
    public string VrcFaceTrackingPath { get; set; } = "";
    public string IntifacePath { get; set; } = "";
    public string OscGoesBrrrPath { get; set; } = "";
    public bool UseBrokenEye { get; init; } = true;
    public bool BrokenEyeStartMinimized { get; set; }
    public bool VrcFaceTrackingStartMinimized { get; set; }
    public bool FaceTrackerAutomationEnabled { get; init; } = true;
    public bool FaceTrackerRestartOnReconnectEnabled { get; init; } = true;
    public bool MouthTrackerRestartOnReconnectEnabled { get; init; } = true;
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
    public bool BaseStationsEnabled { get; set; }
    public BaseStationPowerDownMode BaseStationPowerDownMode { get; init; } = BaseStationPowerDownMode.Sleep;
    public BaseStationDevice[] BaseStations { get; set; } = [];
    public bool RunInitialSetupQuestions { get; set; }
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
    public StartupLaunchMode StartupLaunchMode { get; set; } = StartupLaunchMode.Unspecified;
    public bool StopWithSteamVr { get; set; }
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
    public bool UseMouthTrackerPnPReconnectDetector { get; init; } = false;
    public string PimaxServiceLogDirectory { get; init; } = @"%LOCALAPPDATA%\Pimax\PiService\Log";
    public int PimaxServiceLogReconnectLookbackLines { get; init; } = 400;
    public int PollIntervalSeconds { get; init; } = 2;
    public int StartupTimeoutSeconds { get; init; } = 30;
    public int StartupStableSeconds { get; init; } = 5;
    public int DelayBeforeVrcFaceTrackingSeconds { get; init; } = 3;
    public int DelayBeforeOscGoesBrrrSeconds { get; set; } = 3;
    public int DelayBeforeOscGoesBrrrrSeconds
    {
        set => DelayBeforeOscGoesBrrrSeconds = value;
    }
    public int RestartDelayAfterReconnectSeconds { get; init; } = 10;
    public int WatchedProcessCrashRelaunchGraceSeconds { get; init; } = 300;
    public int ShutdownGraceSeconds { get; init; } = 8;
    public int DeviceProbeTimeoutSeconds { get; init; } = 10;
    public bool DiagnosticsLogSupervisor { get; init; }
    public bool DiagnosticsLogSteamVrOverlay { get; init; }
    public bool DiagnosticsLogDesktopTui { get; init; }
    public bool DiagnosticsDebugSupervisor { get; init; }
    public bool DiagnosticsDebugSteamVrOverlay { get; init; }
    public bool DiagnosticsVerbose { get; init; }
    public int DiagnosticsSummaryIntervalSeconds { get; init; } = 20;
    public string DiagnosticsLogDirectory { get; init; } = @"%TEMP%\PimaxVrcSupervisorDiagnostics";

    [JsonIgnore]
    public string? LoadedFromPath { get; private set; }

    [JsonIgnore]
    public bool BaseStationsEnabledConfigured { get; private set; }

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
        config.BaseStationsEnabledConfigured = IsJsonBooleanPropertyPresent(json, nameof(BaseStationsEnabled));
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
        SaveBooleanPreference(
            nameof(MouthTrackerUser),
            TryGetMouthTrackerUser(out var value) && value,
            "Vive Face Tracker preference");
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
        SaveBooleanPreference(
            nameof(TurnOffSecondaryMonitors),
            TryGetTurnOffSecondaryMonitors(out var value) && value,
            "secondary monitor preference");
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
        StartupLaunchMode = autoLaunchScheduledTask ? StartupLaunchMode.ScheduledTask : StartupLaunchMode.None;
    }

    public StartupLaunchMode GetEffectiveStartupLaunchMode()
    {
        if (StartupLaunchMode != StartupLaunchMode.Unspecified)
        {
            return StartupLaunchMode;
        }

        return TryGetAutoLaunchScheduledTask(out var autoLaunchScheduledTask)
            ? autoLaunchScheduledTask ? StartupLaunchMode.ScheduledTask : StartupLaunchMode.None
            : StartupLaunchMode.Unspecified;
    }

    public void SaveAutoLaunchScheduledTaskPreference()
    {
        var configPath = LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{\n}";

        json = ReplaceJsonBooleanOrStringProperty(json, nameof(AutoLaunchScheduledTask), TryGetAutoLaunchScheduledTask(out var value) && value);
        json = ReplaceJsonValueProperty(json, nameof(StartupLaunchMode), JsonSerializer.Serialize(GetEffectiveStartupLaunchMode(), JsonOptions()));
        json = ReplaceJsonBooleanOrStringProperty(json, nameof(StopWithSteamVr), GetEffectiveStartupLaunchMode() == StartupLaunchMode.SteamVrManifest);

        File.WriteAllText(configPath, json);
        Console.WriteLine($"Saved scheduled task preference to: {configPath}");
    }

    public bool TryGetBaseStationsEnabled(out bool baseStationsEnabled)
    {
        baseStationsEnabled = BaseStationsEnabled;
        return BaseStationsEnabledConfigured;
    }

    public void SetBaseStationsEnabled(bool baseStationsEnabled)
    {
        BaseStationsEnabled = baseStationsEnabled;
        BaseStationsEnabledConfigured = true;
    }

    public void SaveBaseStationSettings()
    {
        var configPath = LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{\n}";

        json = ReplaceJsonBooleanOrStringProperty(json, nameof(BaseStationsEnabled), BaseStationsEnabled);
        json = ReplaceJsonValueProperty(json, nameof(BaseStations), JsonSerializer.Serialize(BaseStations, JsonOptions()));

        File.WriteAllText(configPath, json);
        Console.WriteLine($"Saved base station settings to: {configPath}");
    }

    public bool AreInitialSetupQuestionsComplete()
        => (!FaceTrackerAutomationEnabled || (TryGetMouthTrackerUser(out _) && TryGetTurnOffSecondaryMonitors(out _)))
            && GetEffectiveStartupLaunchMode() != StartupLaunchMode.Unspecified
            && TryGetBaseStationsEnabled(out _);

    public void SaveInitialSetupQuestionsComplete()
    {
        RunInitialSetupQuestions = false;
        var configPath = LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{\n}";

        json = ReplaceJsonValueProperty(json, nameof(RunInitialSetupQuestions), "false");

        File.WriteAllText(configPath, json);
        Console.WriteLine($"Saved initial setup completion to: {configPath}");
    }

    private void SaveBooleanPreference(string propertyName, bool value, string description)
    {
        var configPath = LoadedFromPath ?? Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        var valueJson = value ? "true" : "false";
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{\n}";

        json = ReplaceJsonValueProperty(json, propertyName, valueJson);
        File.WriteAllText(configPath, json);

        if (!JsonBooleanPropertyEquals(configPath, propertyName, value))
        {
            json = SetJsonPropertyValue(json, propertyName, JsonValue.Create(value));
            File.WriteAllText(configPath, json);
        }

        if (!JsonBooleanPropertyEquals(configPath, propertyName, value))
        {
            throw new IOException($"Could not save {description} to {configPath}.");
        }

        Console.WriteLine($"Saved {description} to: {configPath}");
    }

    private static bool JsonBooleanPropertyEquals(string configPath, string propertyName, bool expectedValue)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(
                File.ReadAllText(configPath),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            return document.RootElement.TryGetProperty(propertyName, out var value)
                && ((expectedValue && value.ValueKind == JsonValueKind.True)
                    || (!expectedValue && value.ValueKind == JsonValueKind.False));
        }
        catch
        {
            return false;
        }
    }

    private static string SetJsonPropertyValue(string json, string propertyName, JsonNode? value)
    {
        var node = JsonNode.Parse(
            string.IsNullOrWhiteSpace(json) ? "{\n}" : json,
            nodeOptions: null,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject ?? [];
        node[propertyName] = value;
        return node.ToJsonString(JsonOptions()) + Environment.NewLine;
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
        var pattern = $"(\"{Regex.Escape(propertyName)}\"\\s*:\\s*)(?:\\[(?:.|\\r|\\n)*?\\]|\"(?:\\\\.|[^\"])*\"|true|false|null|-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)";
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
            TryGetActiveConfigSelectionPath(),
            Path.Combine(AppContext.BaseDirectory, "supervisor.config.json"),
            Path.Combine(Environment.CurrentDirectory, "supervisor.config.json")
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static string? TryGetActiveConfigSelectionPath()
    {
        try
        {
            var selectionPath = Path.Combine(AppContext.BaseDirectory, ActiveConfigSelectionFileName);
            if (!File.Exists(selectionPath))
            {
                return null;
            }

            var selectedConfig = File.ReadAllText(selectionPath).Trim();
            if (string.IsNullOrWhiteSpace(selectedConfig))
            {
                return null;
            }

            return Path.IsPathRooted(selectedConfig)
                ? Path.GetFullPath(selectedConfig)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, selectedConfig));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsJsonBooleanPropertyPresent(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            return document.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
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
