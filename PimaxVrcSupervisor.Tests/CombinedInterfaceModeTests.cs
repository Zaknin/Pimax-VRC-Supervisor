using System.Text.Json;
using PimaxVrcSupervisor;
using Xunit;

namespace PimaxVrcSupervisor.Tests;

public sealed class CombinedInterfaceModeTests
{
    [Theory]
    [InlineData("ScheduledTask", (int)StartupLaunchMode.ScheduledTask)]
    [InlineData("SteamVrManifest", (int)StartupLaunchMode.SteamVrManifest)]
    [InlineData(StartupLaunchPlanning.CombinedModeName, (int)StartupLaunchMode.ScheduledTaskAndSteamVrManifest)]
    public void ConfigurationParsesEveryInterfaceMode(string value, int expectedValue)
    {
        var config = JsonSerializer.Deserialize<SupervisorConfig>($$"""{"StartupLaunchMode":"{{value}}"}""");

        Assert.NotNull(config);
        Assert.Equal((StartupLaunchMode)expectedValue, config.GetEffectiveStartupLaunchMode());
    }

    [Fact]
    public void ExistingTerminalUiConfigurationMigratesToTerminalUiOnly()
    {
        var mode = StartupLaunchPlanning.Resolve(null, legacyAutoLaunchScheduledTask: true, legacyStopWithSteamVr: false, out var warning);

        Assert.Equal(StartupLaunchMode.ScheduledTask, mode);
        Assert.Null(warning);
    }

    [Fact]
    public void ExistingOverlayConfigurationMigratesToOverlayOnly()
    {
        var mode = StartupLaunchPlanning.Resolve(null, legacyAutoLaunchScheduledTask: false, legacyStopWithSteamVr: true, out var warning);

        Assert.Equal(StartupLaunchMode.SteamVrManifest, mode);
        Assert.Null(warning);
    }

    [Fact]
    public void MalformedModeFallsBackToOffWithVisibleWarning()
    {
        var mode = StartupLaunchPlanning.Resolve("two-supervisors", null, legacyStopWithSteamVr: false, out var warning);

        Assert.Equal(StartupLaunchMode.None, mode);
        Assert.Contains("invalid", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disabled", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfiguratorCompatibleSaveLoadRoundTripPreservesCombinedMode()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "combined.config.json");
        File.WriteAllText(path, "{\"StartupLaunchMode\":\"ScheduledTaskAndSteamVrManifest\",\"AutoLaunchScheduledTask\":true,\"StopWithSteamVr\":false}");

        var config = SupervisorConfig.Load(path);
        config.SaveAutoLaunchScheduledTaskPreference();
        var reloaded = SupervisorConfig.Load(path);

        Assert.Equal(StartupLaunchMode.ScheduledTaskAndSteamVrManifest, reloaded.GetEffectiveStartupLaunchMode());
        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(
            StartupLaunchPlanning.CombinedModeName,
            saved.RootElement.GetProperty("StartupLaunchMode").GetString());
    }

    [Fact]
    public void ConfiguratorExposesOneExplicitThreeModeControlAndPersistsCombinedValue()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor.ConfigEditor", "Program.cs"));

        Assert.Contains("Terminal UI only", source, StringComparison.Ordinal);
        Assert.Contains("SteamVR Overlay only", source, StringComparison.Ordinal);
        Assert.Contains("Terminal UI + SteamVR Overlay", source, StringComparison.Ordinal);
        Assert.Contains("AutostartModeCombined => StartupLaunchPlanning.CombinedModeName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddFullWidth(layout, _useDesktopTuiAsDefaultInterfaceCheckBox", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalUiOnlyTaskPlanUsesOneSteamVrScopedOwnerAndNoOverlay()
    {
        var plan = StartupLaunchPlanning.Create(StartupLaunchMode.ScheduledTask);

        Assert.True(plan.InstallWatcherTask);
        Assert.True(plan.WatcherUsesTerminalUi);
        Assert.False(plan.EnableSteamVrManifest);
        Assert.Equal(SupervisorOwnerLifetime.SteamVrSession, plan.OwnerLifetime);
        Assert.Equal(1, plan.AuthoritativeSupervisorOwnerCount);
    }

    [Fact]
    public void OverlayOnlyTaskPlanUsesAttachFirstSteamVrOwnerFallback()
    {
        var plan = StartupLaunchPlanning.Create(StartupLaunchMode.SteamVrManifest);

        Assert.False(plan.InstallWatcherTask);
        Assert.True(plan.EnableSteamVrManifest);
        Assert.Equal(SteamVrHelperOwnerMode.SteamVrSession, plan.SteamVrHelperOwnerMode);
        Assert.True(plan.OverlayAttachesBeforeStartingOwner);
        Assert.Equal(1, plan.AuthoritativeSupervisorOwnerCount);
    }

    [Fact]
    public void CombinedTaskPlanUsesPersistentTerminalUiOwnerAndAttachFirstOverlay()
    {
        var plan = StartupLaunchPlanning.Create(StartupLaunchMode.ScheduledTaskAndSteamVrManifest);

        Assert.True(plan.InstallWatcherTask);
        Assert.True(plan.EnableSteamVrManifest);
        Assert.True(plan.WatcherUsesTerminalUi);
        Assert.Equal(SupervisorOwnerLifetime.Persistent, plan.OwnerLifetime);
        Assert.Equal(SteamVrHelperOwnerMode.PersistentTerminalUi, plan.SteamVrHelperOwnerMode);
        Assert.True(plan.OverlayAttachesBeforeStartingOwner);
        Assert.Equal(1, plan.AuthoritativeSupervisorOwnerCount);
    }

    [Fact]
    public void CombinedHelperStartsSamePersistentOwnerShapeWithoutSteamVrOwnership()
    {
        var plan = StartupLaunchPlanning.Create(StartupLaunchMode.ScheduledTaskAndSteamVrManifest);
        var arguments = plan.BuildSteamVrHelperArguments(@"D:\config path\supervisor.json");

        Assert.Contains("--desktop-tui-start", arguments, StringComparison.Ordinal);
        Assert.Contains("--launch-desktop-tui-after-ready", arguments, StringComparison.Ordinal);
        Assert.Contains("--persistent-supervisor-owner", arguments, StringComparison.Ordinal);
        Assert.Contains("--config \"D:\\config path\\supervisor.json\"", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("--steamvr-start", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("--managed-steamvr-session", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void CombinedWatcherArgumentsPersistOwnerLifetimeWithoutDuplicates()
    {
        var arguments = ScheduledTaskSemantics.BuildWatcherArguments(
            skipCurrentSteamVrSession: true,
            useDesktopTuiDefaultInterface: true,
            persistentSupervisorOwner: true,
            configPath: @"D:\config.json");
        var parsed = ScheduledTaskSemantics.ParseWatcherArguments(arguments);

        Assert.True(parsed.PersistentSupervisorOwner);
        Assert.True(parsed.UseDesktopTuiDefaultInterface);
        Assert.Equal(1, Count(arguments, "--persistent-supervisor-owner"));
        Assert.Equal(1, Count(arguments, "--desktop-tui-default-interface"));
    }

    [Fact]
    public void SteamVrStopAndClientDisconnectsDoNotOwnCombinedSupervisor()
    {
        var plan = StartupLaunchPlanning.Create(StartupLaunchMode.ScheduledTaskAndSteamVrManifest);

        Assert.Equal(SupervisorOwnerLifetime.Persistent, plan.OwnerLifetime);
        Assert.False(plan.TuiOwnsSupervisor);
        Assert.False(plan.OverlayOwnsSupervisor);
    }

    [Fact]
    public void OverlayHostAttachesBeforeFallbackAndItsMutexLimitsEachSteamVrSessionToOneHost()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor.SteamVrHost", "Program.cs"));
        var attach = source.IndexOf("Attached to the existing Supervisor command bridge", StringComparison.Ordinal);
        var helper = source.IndexOf("Requesting elevated supervisor via scheduled task", StringComparison.Ordinal);

        Assert.True(attach >= 0 && helper > attach);
        Assert.Contains(@"Local\PimaxVrcSupervisorSteamVrHost", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingTerminalUiWindowDoesNotRequestSupervisorShutdown()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor.Tui", "src", "console_close.rs"));

        Assert.Contains("client-only action", source, StringComparison.Ordinal);
        Assert.DoesNotContain("request-graceful-shutdown", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase32E1RecoveryRemainsInterfaceAgnostic()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "Program.cs"));
        var start = source.IndexOf("private void ObserveUsbDeviceInventory", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task RunReadyDeviceRecoveryPlansAsync", start, StringComparison.Ordinal);
        var recovery = source[start..end];

        Assert.DoesNotContain("SteamVrHost", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DesktopTui", recovery, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !HasGitMetadata(directory.FullName)) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static bool HasGitMetadata(string directory)
    {
        var path = Path.Combine(directory, ".git");
        return Directory.Exists(path) || File.Exists(path);
    }
}
