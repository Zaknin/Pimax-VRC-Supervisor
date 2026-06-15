using System.Globalization;

internal sealed record StartupExecutionContext(
    string[] Args,
    bool DesktopTuiStart,
    bool LaunchDesktopTuiAfterReady,
    bool SteamVrStart,
    bool ManagedSteamVrSession,
    bool WatchVrchatAutoLaunch,
    bool ApplyStartupIntegration,
    bool ShowStartupIntegrationResult,
    bool HideStartupIntegrationHelperWindow,
    bool DesktopTuiDefaultInterface,
    bool InstallAutoLaunchTask,
    bool EmergencyBaseStationCleanup,
    bool ExplicitConfigOptionPresent,
    string? ExplicitConfigPath,
    string? EmergencyBaseStationCleanupConfigPath,
    int EmergencyBaseStationCleanupDelaySeconds)
{
    public bool ExplicitConfigSupplied => ExplicitConfigOptionPresent;

    public bool ShouldHideConsole
        => DesktopTuiStart
            || LaunchDesktopTuiAfterReady
            || SteamVrStart
            || WatchVrchatAutoLaunch
            || (ApplyStartupIntegration && HideStartupIntegrationHelperWindow && !ShowStartupIntegrationResult);

    public bool IsInteractiveSupervisorLaunch
        => !DesktopTuiStart
            && !LaunchDesktopTuiAfterReady
            && !SteamVrStart
            && !ManagedSteamVrSession
            && !WatchVrchatAutoLaunch
            && !ApplyStartupIntegration
            && !InstallAutoLaunchTask
            && !EmergencyBaseStationCleanup;

    public bool CanApplyStartupIntegration => ApplyStartupIntegration;

    public static StartupExecutionContext Parse(IEnumerable<string> args)
    {
        var commandLineArgs = args.ToArray();
        var desktopTuiStart = HasFlag(commandLineArgs, "--desktop-tui-start");
        var launchDesktopTuiAfterReady = HasFlag(commandLineArgs, "--launch-desktop-tui-after-ready");
        var steamVrStart = HasFlag(commandLineArgs, "--steamvr-start");
        var managedSteamVrSession = steamVrStart || HasFlag(commandLineArgs, "--managed-steamvr-session");
        var applyStartupIntegration = HasFlag(commandLineArgs, "--apply-startup-integration");
        var showStartupIntegrationResult = HasFlag(commandLineArgs, "--show-result");
        var hideStartupIntegrationHelperWindow = HasFlag(commandLineArgs, "--hide-startup-helper");
        var emergencyBaseStationCleanup = TryGetCommandOption(commandLineArgs, "--emergency-base-station-cleanup", out var emergencyConfigPath);
        var cleanupDelaySeconds = TryGetCommandOption(commandLineArgs, "--delay-seconds", out var delayText)
            && int.TryParse(delayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDelay)
                ? Math.Max(0, parsedDelay)
                : 0;
        var explicitConfigOptionPresent =
            TryGetCommandOption(commandLineArgs, "--config", out var explicitConfigPath);

        return new StartupExecutionContext(
            commandLineArgs,
            desktopTuiStart,
            launchDesktopTuiAfterReady,
            steamVrStart,
            managedSteamVrSession,
            HasFlag(commandLineArgs, "--watch-vrchat-auto-launch"),
            applyStartupIntegration,
            showStartupIntegrationResult,
            hideStartupIntegrationHelperWindow,
            HasFlag(commandLineArgs, "--desktop-tui-default-interface"),
            HasFlag(commandLineArgs, "--install-auto-launch-task"),
            emergencyBaseStationCleanup,
            explicitConfigOptionPresent,
            explicitConfigPath,
            emergencyConfigPath,
            cleanupDelaySeconds);
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
