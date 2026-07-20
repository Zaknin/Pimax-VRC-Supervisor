using System.Text.Json;
using System.Text.Json.Serialization;

namespace PimaxVrcSupervisor;

internal enum StartupLaunchMode
{
    Unspecified,
    None,
    ScheduledTask,
    SteamVrManifest,
    ScheduledTaskAndSteamVrManifest,
    ScheduledTaskClassicConsole
}

internal enum SupervisorOwnerLifetime
{
    None,
    SteamVrSession,
    Persistent
}

internal enum SteamVrHelperOwnerMode
{
    None,
    SteamVrSession,
    PersistentTerminalUi
}

internal sealed record StartupLaunchPlan(
    StartupLaunchMode Mode,
    bool InstallWatcherTask,
    bool EnableSteamVrManifest,
    bool WatcherUsesTerminalUi,
    SupervisorOwnerLifetime OwnerLifetime,
    SteamVrHelperOwnerMode SteamVrHelperOwnerMode,
    bool OverlayAttachesBeforeStartingOwner)
{
    public bool StartsTerminalUi => WatcherUsesTerminalUi || SteamVrHelperOwnerMode == SteamVrHelperOwnerMode.PersistentTerminalUi;
    public bool StartsSteamVrOverlay => EnableSteamVrManifest;
    public int AuthoritativeSupervisorOwnerCount => Mode == StartupLaunchMode.None || Mode == StartupLaunchMode.Unspecified ? 0 : 1;
    public bool TuiOwnsSupervisor => false;
    public bool OverlayOwnsSupervisor => false;

    public string BuildSteamVrHelperArguments(string? configPath)
    {
        var arguments = new List<string>();
        switch (SteamVrHelperOwnerMode)
        {
            case SteamVrHelperOwnerMode.SteamVrSession:
                arguments.Add("--steamvr-start");
                break;
            case SteamVrHelperOwnerMode.PersistentTerminalUi:
                arguments.Add("--desktop-tui-start");
                arguments.Add("--launch-desktop-tui-after-ready");
                arguments.Add("--persistent-supervisor-owner");
                break;
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            arguments.Add("--config");
            arguments.Add(QuoteArgument(configPath));
        }

        return string.Join(' ', arguments);
    }

    private static string QuoteArgument(string argument)
        => argument.Any(char.IsWhiteSpace)
            ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : argument;
}

internal static class StartupLaunchPlanning
{
    public const string CombinedModeName = "ScheduledTaskAndSteamVrManifest";

    public static StartupLaunchPlan Create(StartupLaunchMode mode)
        => mode switch
        {
            StartupLaunchMode.ScheduledTask => new(
                mode,
                InstallWatcherTask: true,
                EnableSteamVrManifest: false,
                WatcherUsesTerminalUi: true,
                OwnerLifetime: SupervisorOwnerLifetime.SteamVrSession,
                SteamVrHelperOwnerMode: SteamVrHelperOwnerMode.None,
                OverlayAttachesBeforeStartingOwner: false),
            StartupLaunchMode.ScheduledTaskClassicConsole => new(
                mode,
                InstallWatcherTask: true,
                EnableSteamVrManifest: false,
                WatcherUsesTerminalUi: false,
                OwnerLifetime: SupervisorOwnerLifetime.SteamVrSession,
                SteamVrHelperOwnerMode: SteamVrHelperOwnerMode.None,
                OverlayAttachesBeforeStartingOwner: false),
            StartupLaunchMode.SteamVrManifest => new(
                mode,
                InstallWatcherTask: false,
                EnableSteamVrManifest: true,
                WatcherUsesTerminalUi: false,
                OwnerLifetime: SupervisorOwnerLifetime.SteamVrSession,
                SteamVrHelperOwnerMode: SteamVrHelperOwnerMode.SteamVrSession,
                OverlayAttachesBeforeStartingOwner: true),
            StartupLaunchMode.ScheduledTaskAndSteamVrManifest => new(
                mode,
                InstallWatcherTask: true,
                EnableSteamVrManifest: true,
                WatcherUsesTerminalUi: true,
                OwnerLifetime: SupervisorOwnerLifetime.Persistent,
                SteamVrHelperOwnerMode: SteamVrHelperOwnerMode.PersistentTerminalUi,
                OverlayAttachesBeforeStartingOwner: true),
            _ => new(
                mode,
                InstallWatcherTask: false,
                EnableSteamVrManifest: false,
                WatcherUsesTerminalUi: false,
                OwnerLifetime: SupervisorOwnerLifetime.None,
                SteamVrHelperOwnerMode: SteamVrHelperOwnerMode.None,
                OverlayAttachesBeforeStartingOwner: false)
        };

    public static StartupLaunchMode Resolve(
        string? configuredValue,
        bool? legacyAutoLaunchScheduledTask,
        bool legacyStopWithSteamVr,
        out string? warning)
    {
        warning = null;
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            if (Enum.TryParse<StartupLaunchMode>(configuredValue, ignoreCase: true, out var parsed)
                && Enum.GetName(parsed) is { } canonicalName
                && string.Equals(configuredValue, canonicalName, StringComparison.OrdinalIgnoreCase))
            {
                return parsed;
            }

            warning = $"StartupLaunchMode value '{configuredValue}' is invalid. Automatic startup is disabled until a valid mode is saved.";
            return StartupLaunchMode.None;
        }

        if (legacyStopWithSteamVr)
        {
            return StartupLaunchMode.SteamVrManifest;
        }

        return legacyAutoLaunchScheduledTask switch
        {
            true => StartupLaunchMode.ScheduledTask,
            false => StartupLaunchMode.None,
            null => StartupLaunchMode.Unspecified
        };
    }
}

internal sealed class SafeStartupLaunchModeJsonConverter : JsonConverter<StartupLaunchMode>
{
    public override StartupLaunchMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            Console.Error.WriteLine("Configuration warning: StartupLaunchMode must be a string. Automatic startup is disabled until a valid mode is saved.");
            return StartupLaunchMode.None;
        }

        var configuredValue = reader.GetString();
        var mode = StartupLaunchPlanning.Resolve(configuredValue, null, legacyStopWithSteamVr: false, out var warning);
        if (warning is not null)
        {
            Console.Error.WriteLine("Configuration warning: " + warning);
        }

        return mode;
    }

    public override void Write(Utf8JsonWriter writer, StartupLaunchMode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
