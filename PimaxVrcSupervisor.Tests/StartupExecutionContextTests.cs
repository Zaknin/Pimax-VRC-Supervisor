using Xunit;

public sealed class StartupExecutionContextTests
{
    [Fact]
    public void OrdinaryLaunchIsInteractiveAndDoesNotHideConsole()
    {
        var context = StartupExecutionContext.Parse([]);

        Assert.True(context.IsInteractiveSupervisorLaunch);
        Assert.False(context.ShouldHideConsole);
        Assert.False(context.CanApplyStartupIntegration);
        Assert.False(context.ExplicitConfigSupplied);
        Assert.Null(context.ExplicitConfigPath);
    }

    [Fact]
    public void ConfigOptionWithoutValueRemainsExplicitlySupplied()
    {
        var context = StartupExecutionContext.Parse(["--config"]);

        Assert.True(context.ExplicitConfigSupplied);
        Assert.Null(context.ExplicitConfigPath);
    }

    [Fact]
    public void ExplicitConfigOptionIsAuthoritativeForSpaceSeparatedForm()
    {
        var context = StartupExecutionContext.Parse(["--config", @"D:\configs\supervisor.json"]);

        Assert.True(context.ExplicitConfigSupplied);
        Assert.Equal(@"D:\configs\supervisor.json", context.ExplicitConfigPath);
    }

    [Fact]
    public void InlineConfigEqualsFormPreservesCurrentSupervisorSemantics()
    {
        var context = StartupExecutionContext.Parse(["--config=ignored.json"]);

        Assert.False(context.ExplicitConfigSupplied);
        Assert.Null(context.ExplicitConfigPath);
    }

    [Theory]
    [InlineData("--managed-steamvr-session")]
    [InlineData("--desktop-tui-start")]
    [InlineData("--launch-desktop-tui-after-ready")]
    [InlineData("--watch-vrchat-auto-launch")]
    [InlineData("--apply-startup-integration")]
    [InlineData("--steamvr-start")]
    [InlineData("--install-auto-launch-task")]
    [InlineData("--emergency-base-station-cleanup")]
    public void InternalOrManagedModesAreNotInteractiveMigrationLaunches(string flag)
    {
        var args = flag == "--emergency-base-station-cleanup"
            ? new[] { flag, "config.json" }
            : [flag];

        var context = StartupExecutionContext.Parse(args);

        Assert.False(context.IsInteractiveSupervisorLaunch);
    }

    [Fact]
    public void StartupIntegrationMutationRequiresExplicitApplyFlag()
    {
        Assert.False(StartupExecutionContext.Parse([]).CanApplyStartupIntegration);
        Assert.True(StartupExecutionContext.Parse(["--apply-startup-integration"]).CanApplyStartupIntegration);
    }

    [Theory]
    [InlineData("pimax-shell-activation-capability-json")]
    [InlineData("pimax-shell-activate-json")]
    [InlineData("pimax-component-health-json")]
    [InlineData("pimax-repair-plan-json")]
    [InlineData("pimax-repair-start-json")]
    [InlineData("pimax-startup-observe-elevated-json")]
    public void PimaxDevelopmentCommandsBypassInteractiveFirstRunAndStartupMutation(string command)
    {
        var context = StartupExecutionContext.Parse([command, "--apply-startup-integration"]);

        Assert.True(context.IsPimaxDevelopmentCommand);
        Assert.Equal(command, context.PimaxDevelopmentCommand);
        Assert.False(context.IsInteractiveSupervisorLaunch);
        Assert.False(context.CanApplyStartupIntegration);
        Assert.False(context.ShouldHideConsole);
    }

    [Fact]
    public void TerminalUiLaunchIntentDoesNotOwnSteamVrLifecycle()
    {
        var context = StartupExecutionContext.Parse(["--launch-desktop-tui-after-ready"]);

        Assert.True(context.LaunchDesktopTuiAfterReady);
        Assert.False(context.SteamVrStart);
        Assert.False(context.ManagedSteamVrSession);
    }

    [Fact]
    public void SteamVrStartImpliesManagedSteamVrSession()
    {
        var context = StartupExecutionContext.Parse(["--steamvr-start"]);

        Assert.True(context.SteamVrStart);
        Assert.True(context.ManagedSteamVrSession);
    }

    [Fact]
    public void HiddenHelperOnlyHidesWhenResultDialogIsNotRequested()
    {
        var hidden = StartupExecutionContext.Parse(["--apply-startup-integration", "--hide-startup-helper"]);
        var visibleResult = StartupExecutionContext.Parse(["--apply-startup-integration", "--hide-startup-helper", "--show-result"]);

        Assert.True(hidden.ShouldHideConsole);
        Assert.False(visibleResult.ShouldHideConsole);
    }
}
