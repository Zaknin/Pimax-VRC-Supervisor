using Xunit;

public sealed class PimaxPassiveBehaviorRetentionTests
{
    [Fact]
    public void StartupDetectionAndReconnectSymbolsRemainAvailable()
    {
        var source = ProgramSource();
        string[] retainedSymbols =
        [
            "WaitForPimaxOnStartupAsync",
            "IsPimaxConnectedAsync",
            "IsDeviceConnectedAsync",
            "ReadDeviceConnectedOrPreviousAsync",
            "WaitForPimaxStableConnectedAsync",
            "PimaxDetectors",
            "DetectPimaxServiceLogReconnect",
            "UsePimaxServiceLogReconnectDetector",
            "FaceTrackerRestartOnReconnectEnabled",
            "ManagedAppStopReason.PimaxReconnect",
        ];

        Assert.All(retainedSymbols, symbol => Assert.Contains(symbol, source, StringComparison.Ordinal));
    }

    [Fact]
    public void AbsentHeadsetStartupWaitRemainsObservationOnly()
    {
        var source = ProgramSource();
        var waitMethod = Slice(
            source,
            "private async Task<bool> WaitForPimaxOnStartupAsync",
            "private async Task<bool> WaitForPimaxStableConnectedAsync");

        Assert.Contains("ReadDeviceConnectedOrPreviousAsync", waitMethod, StringComparison.Ordinal);
        Assert.Contains("Task.Delay", waitMethod, StringComparison.Ordinal);
        string[] mutationCalls = [".Kill(", "Process.Start(", "StartService(", "StopService(", "ControlService("];
        Assert.All(mutationCalls, call => Assert.DoesNotContain(call, waitMethod, StringComparison.Ordinal));
    }

    [Fact]
    public void RemovedImplementationSymbolsAreAbsentFromProductionAndTuiSources()
    {
        var root = RepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "PimaxVrcSupervisor"),
            Path.Combine(root, "PimaxVrcSupervisor.Tui", "src"),
        };
        var source = string.Join(
            Environment.NewLine,
            sourceRoots.SelectMany(path => Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly))
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        string[] removedSymbols =
        [
            string.Concat("Pimax", "ShellLaunch"),
            string.Concat("PimaxRecovery", "Experiment"),
            string.Concat("WindowsPimaxClient", "ProcessController"),
            string.Concat("PimaxRecoveryTimeline", "CaptureHelper"),
        ];

        Assert.All(removedSymbols, symbol => Assert.DoesNotContain(symbol, source, StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownStructuredActionStillReturnsAFailureInsteadOfExecutingACommand()
    {
        var source = ProgramSource();
        var actionRouter = Slice(
            source,
            "private async Task<SupervisorCommandResult> ExecuteActionJsonAsync",
            "private async Task<SupervisorCommandResult> ExecuteLifecycleJsonAsync");

        Assert.Contains("Unsupported action-json command", actionRouter, StringComparison.Ordinal);
        Assert.Contains("success: false", actionRouter, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedExplicitCommandGuardIsNonzeroAndPrecedesConfigurationLoad()
    {
        var source = ProgramSource();
        var guard = source.IndexOf("startupContext.UnsupportedExplicitCommand", StringComparison.Ordinal);
        var firstConfigLoad = source.IndexOf("SupervisorConfig.Load", StringComparison.Ordinal);

        Assert.True(guard >= 0);
        Assert.True(firstConfigLoad > guard);
        var guardBody = source[guard..firstConfigLoad];
        Assert.Contains("Unknown or unsupported command", guardBody, StringComparison.Ordinal);
        Assert.Contains("Environment.ExitCode = 2", guardBody, StringComparison.Ordinal);
        Assert.Contains("return;", guardBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryDefaultConfigurationRemainsLoadable()
    {
        var configPath = Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "supervisor.config.json");

        var config = SupervisorConfig.Load(configPath);

        Assert.Equal("Default", config.DisplayName);
        Assert.True(config.FaceTrackerAutomationEnabled);
        Assert.True(config.FaceTrackerRestartOnReconnectEnabled);
        Assert.Equal(Path.GetFullPath(configPath), config.LoadedFromPath);
    }

    private static string ProgramSource()
        => File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "Program.cs"));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        Assert.True(end > start, $"Missing source marker: {endMarker}");
        return source[start..end];
    }

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
