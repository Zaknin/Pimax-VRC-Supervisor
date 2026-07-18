using Xunit;

public sealed class SupervisorExitIntentTests
{
    [Theory]
    [InlineData("normal-cleanup", true)]
    [InlineData("preserve-base-stations", false)]
    public void ExitIntentModesParseAndControlBaseStationPowerDown(
        string mode,
        bool expectedPowerDownAllowed)
    {
        Assert.True(SupervisorShutdownIntents.TryParse(mode, out var intent));
        Assert.Equal(expectedPowerDownAllowed, SupervisorShutdownIntents.AllowsBaseStationPowerDown(intent));
        Assert.Equal(mode, SupervisorShutdownIntents.ToProtocolMode(intent));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("keep-running")]
    public void MalformedExitIntentModeDoesNotParse(string? mode)
    {
        Assert.False(SupervisorShutdownIntents.TryParse(mode, out _));
    }

    [Fact]
    public void SameSteamVrSessionSuppressesWatcherRelaunch()
    {
        var session = new SteamVrSessionIdentity(42, DateTimeOffset.Parse("2026-07-18T01:02:03Z"));

        var decision = AutoLaunchWatcher.GetLaunchDecision(
            session,
            supervisorRunning: false,
            launchedForSteamVrSession: session);

        Assert.False(decision.ShouldLaunchSupervisor);
        Assert.True(decision.SuppressedForCurrentSession);
        Assert.Equal(session, decision.LaunchedForSteamVrSession);
    }

    [Fact]
    public void WatcherUserExitMessageRequiresExplicitUserExitMarkerReason()
    {
        var source = ProgramSource();
        var watcher = Slice(
            source,
            "internal static class AutoLaunchWatcher",
            "internal sealed record ScheduledTaskDetails");

        Assert.Contains("RequestSkipCurrentSteamVrSessionForUserExit", watcher, StringComparison.Ordinal);
        Assert.Contains("UserSupervisorExitMarkerReason", watcher, StringComparison.Ordinal);
        Assert.Contains("userExitSuppressedSteamVrSession == currentSteamVrSession", watcher, StringComparison.Ordinal);
    }

    [Fact]
    public void NewSteamVrSessionPermitsWatcherLaunch()
    {
        var oldSession = new SteamVrSessionIdentity(42, DateTimeOffset.Parse("2026-07-18T01:02:03Z"));
        var newSession = new SteamVrSessionIdentity(42, DateTimeOffset.Parse("2026-07-18T02:02:03Z"));

        var decision = AutoLaunchWatcher.GetLaunchDecision(
            newSession,
            supervisorRunning: false,
            launchedForSteamVrSession: oldSession);

        Assert.True(decision.ShouldLaunchSupervisor);
        Assert.False(decision.SuppressedForCurrentSession);
        Assert.Equal(newSession, decision.LaunchedForSteamVrSession);
    }

    [Fact]
    public void MissingSteamVrSessionClearsWatcherLaunchIdentity()
    {
        var oldSession = new SteamVrSessionIdentity(42, DateTimeOffset.Parse("2026-07-18T01:02:03Z"));

        var decision = AutoLaunchWatcher.GetLaunchDecision(
            currentSteamVrSession: null,
            supervisorRunning: false,
            launchedForSteamVrSession: oldSession);

        Assert.False(decision.ShouldLaunchSupervisor);
        Assert.False(decision.SuppressedForCurrentSession);
        Assert.Null(decision.LaunchedForSteamVrSession);
    }

    [Fact]
    public void LifecycleRouterRejectsSupervisorExitWithoutValidMode()
    {
        var source = ProgramSource();
        var lifecycleRouter = Slice(
            source,
            "private async Task<SupervisorCommandResult> ExecuteLifecycleJsonAsync",
            "private static string NormalizeShutdownSource");

        Assert.Contains("request-supervisor-exit", lifecycleRouter, StringComparison.Ordinal);
        Assert.Contains("SupervisorShutdownIntents.TryParse", lifecycleRouter, StringComparison.Ordinal);
        Assert.Contains("request-supervisor-exit requires a valid mode.", lifecycleRouter, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupBranchesBaseStationShutdownThroughAcceptedIntent()
    {
        var source = ProgramSource();
        var cleanup = Slice(
            source,
            "private async Task RestoreMonitorsAndStopManagedAppsCoreAsync(",
            "private bool ShouldExitWithSteamVr()");

        Assert.Contains("SupervisorShutdownIntents.AllowsBaseStationPowerDown(intent)", cleanup, StringComparison.Ordinal);
        Assert.Contains("SuppressBaseStationPowerDownForIntent(intent)", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorRestoreIsOwnershipGatedAndIdempotent()
    {
        var source = ProgramSource();
        var restore = Slice(
            source,
            "private void RestoreSupervisorOwnedMonitorLayout()",
            "private void SuppressBaseStationPowerDownForIntent");

        Assert.Contains("_monitorLayoutDisabledBySupervisor", restore, StringComparison.Ordinal);
        Assert.Contains("_monitorRestoreAttempted", restore, StringComparison.Ordinal);
        Assert.Contains("reason=not-owned-by-supervisor", restore, StringComparison.Ordinal);
        Assert.Contains("reason=already-attempted", restore, StringComparison.Ordinal);
    }

    [Fact]
    public void CloseTuiOnlySuppressesImmediateSupervisorOwnedTuiRelaunch()
    {
        var source = ProgramSource();
        var launch = Slice(
            source,
            "private async Task LaunchTerminalUiAfterDashboardReadyAsync",
            "private Process StartTerminalUiProcess()");

        Assert.Contains("_desktopTuiCloseOnlyRequested", launch, StringComparison.Ordinal);
        Assert.Contains("Supervisor continues without relaunching Terminal UI", launch, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitSupervisorExitHasDedicatedDiagnosticExitCode()
    {
        Assert.Equal(32, SupervisorProcessExitCodes.UserRequestedSupervisorExit);
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
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "PimaxVrcSupervisor")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
