using Xunit;

public sealed class ScheduledTaskComparisonTests
{
    [Fact]
    public void ParserRecognizesCanonicalWatcherArgumentsAndConfigPath()
    {
        var parsed = ScheduledTaskSemantics.ParseWatcherArguments(
            @"--watch-vrchat-auto-launch --skip-current-vrserver-session --config ""D:\VR\supervisor.config.json"" --desktop-tui-default-interface");

        Assert.True(parsed.WatcherMode);
        Assert.True(parsed.SkipCurrentSteamVrSession);
        Assert.True(parsed.UseDesktopTuiDefaultInterface);
        Assert.Equal(@"D:\VR\supervisor.config.json", parsed.ConfigPath);
        Assert.Empty(parsed.UnknownArguments);
        Assert.Null(parsed.UnsupportedReason);
    }

    [Fact]
    public void ParserPreservesUnknownArguments()
    {
        var parsed = ScheduledTaskSemantics.ParseWatcherArguments("--watch-vrchat-auto-launch --future-option value");

        Assert.Equal(["--future-option", "value"], parsed.UnknownArguments);
    }

    [Fact]
    public void ParserReportsUnbalancedQuotesAsUnsupported()
    {
        var parsed = ScheduledTaskSemantics.ParseWatcherArguments("--watch-vrchat-auto-launch --config \"broken");

        Assert.False(parsed.WatcherMode);
        Assert.Equal("unbalanced quotes in watcher arguments.", parsed.UnsupportedReason);
    }

    [Fact]
    public void SemanticComparisonAcceptsMatchingTask()
    {
        using var temp = new TempDirectory();
        var watcher = temp.WriteFile("PimaxVrcSupervisorWatcher.exe", "");
        var desired = ScheduledTaskSemantics.ParseWatcherArguments(
            ScheduledTaskSemantics.BuildWatcherArguments(true, true, @"D:\config.json", ["--future"]));
        var existing = Task(watcher, temp.Path, desired);

        var valid = ScheduledTaskSemantics.IsExistingTaskSemanticallyValid(
            existing,
            watcher,
            temp.Path,
            desired,
            out var mismatch);

        Assert.True(valid);
        Assert.Equal("", mismatch);
    }

    [Theory]
    [InlineData("task executable path did not match the expected watcher.")]
    [InlineData("task working directory did not match the expected release folder.")]
    [InlineData("Terminal UI default-interface setting did not match the persistent preference.")]
    [InlineData("config path did not match.")]
    [InlineData("unknown watcher arguments did not match the preserved argument set.")]
    public void SemanticComparisonReportsSpecificMismatches(string expected)
    {
        using var temp = new TempDirectory();
        var watcher = temp.WriteFile("PimaxVrcSupervisorWatcher.exe", "");
        var desired = ScheduledTaskSemantics.ParseWatcherArguments(
            ScheduledTaskSemantics.BuildWatcherArguments(true, true, @"D:\config.json", ["--future"]));
        var existing = expected switch
        {
            "task executable path did not match the expected watcher." => Task(temp.WriteFile("OtherWatcher.exe", ""), temp.Path, desired),
            "task working directory did not match the expected release folder." => Task(watcher, temp.CreateDirectory("other"), desired),
            "Terminal UI default-interface setting did not match the persistent preference." => Task(watcher, temp.Path, desired with { UseDesktopTuiDefaultInterface = false }),
            "config path did not match." => Task(watcher, temp.Path, desired with { ConfigPath = @"D:\other.json" }),
            _ => Task(watcher, temp.Path, desired with { UnknownArguments = ["--other"] })
        };

        var valid = ScheduledTaskSemantics.IsExistingTaskSemanticallyValid(
            existing,
            watcher,
            temp.Path,
            desired,
            out var mismatch);

        Assert.False(valid);
        Assert.Equal(expected, mismatch);
    }

    [Fact]
    public void SemanticComparisonRejectsEffectiveTaskSettingMismatch()
    {
        using var temp = new TempDirectory();
        var watcher = temp.WriteFile("PimaxVrcSupervisorWatcher.exe", "");
        var desired = ScheduledTaskSemantics.ParseWatcherArguments(
            ScheduledTaskSemantics.BuildWatcherArguments(true, true, @"D:\config.json"));
        var existing = Task(
            watcher,
            temp.Path,
            desired,
            ["Setting: RunLevel; Existing: LeastPrivilege; Expected: HighestAvailable"]);

        var valid = ScheduledTaskSemantics.IsExistingTaskSemanticallyValid(
            existing,
            watcher,
            temp.Path,
            desired,
            out var mismatch);

        Assert.False(valid);
        Assert.Contains("task settings did not match", mismatch);
        Assert.Contains("RunLevel", mismatch);
    }

    [Fact]
    public void PersistentInterfacePrefersConfiguratorSelectionThenExistingTaskThenDefault()
    {
        var existing = Task(
            @"C:\release\PimaxVrcSupervisorWatcher.exe",
            @"C:\release",
            ScheduledTaskSemantics.ParseWatcherArguments("--watch-vrchat-auto-launch --desktop-tui-default-interface"));

        Assert.False(ScheduledTaskSemantics.ResolvePersistentInterface(false, existing, out var selectedSource));
        Assert.Equal("Configurator selection", selectedSource);
        Assert.True(ScheduledTaskSemantics.ResolvePersistentInterface(null, existing, out var existingSource));
        Assert.Equal("existing scheduled task", existingSource);
        Assert.True(ScheduledTaskSemantics.ResolvePersistentInterface(null, null, out var defaultSource));
        Assert.Equal("documented product default", defaultSource);
    }

    [Theory]
    [InlineData(0, null, "Scheduled task already valid; no changes made.")]
    [InlineData(1, null, "Scheduled task created.")]
    [InlineData(2, "settings", "Scheduled task repaired: settings")]
    [InlineData(3, "path", "Scheduled task rebound to the current release: path")]
    [InlineData(4, null, "Scheduled task removed or disabled.")]
    [InlineData(5, null, "Scheduled task update deferred.")]
    [InlineData(6, "boom", "Scheduled task update failed: boom")]
    public void ApplyResultMessageIsSpecificToActualOutcome(
        int outcomeValue,
        string? reason,
        string expected)
    {
        var outcome = (ScheduledTaskApplyOutcome)outcomeValue;
        var result = new ScheduledTaskApplyResult(outcome, "task", "trigger", reason);

        Assert.Equal(expected, result.OperatorMessage);
    }

    private static ExistingWatcherTask Task(
        string executable,
        string workingDirectory,
        ParsedWatcherArguments parsed,
        string[]? settingMismatches = null)
        => new(
            executable,
            "",
            workingDirectory,
            parsed,
            HasLogonTrigger: true,
            UsesInteractiveToken: true,
            UsesHighestAvailableRunLevel: true,
            IgnoreNewInstances: true,
            StartWhenAvailable: true,
            AllowStartOnDemand: true,
            Enabled: true,
            Hidden: true,
            ExecutionTimeLimitUnlimited: true,
            settingMismatches ?? []);
}
