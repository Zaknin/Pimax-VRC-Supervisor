using System.Text.Json;
using Xunit;

public sealed class PimaxStartupOrchestrationTests
{
    [Fact]
    public void StartupSourceAssessmentKeepsBackendExecutionDisabledUntilCreatorChainIsProven()
    {
        var source = new PimaxStartupSource(
            "startMenuShortcut",
            "shortcut:fixture",
            "PimaxPlay.lnk",
            @"<pimax>\PimaxClient\pimaxui\PimaxClient.exe",
            "",
            @"<pimax>\PimaxClient\pimaxui",
            "Pimax certificate subject present",
            "PimaxClient",
            "1.43.9.272",
            "probable",
            "visible user activation entry",
            [],
            []);

        var assessment = PimaxStartupSourcesCollector.Assess(
            [source],
            [
                new PimaxStartupActivationPath(
                    "startMenuShortcut",
                    source.SourceId,
                    source.PossibleRole,
                    "probable",
                    ProgrammaticEquivalentKnown: false,
                    SafeForBackendExecution: false,
                    ["formal observer-backed Start Menu launch"])
            ]);

        Assert.Equal(PimaxStartupMechanism.ManualShellLaunchWorksMechanismStillUnresolved, assessment.Mechanism);
        Assert.False(assessment.BackendExecutable);
        Assert.Contains("programmatic", string.Join("\n", assessment.Blockers), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FakeObserverEmitsBoundedSanitizedProcessLifecycle()
    {
        var request = new PimaxStartupObservationRequest(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            Fake: true,
            "pimax-startup-observe-test");

        var snapshot = await new PimaxStartupObserver().ObserveAsync(request, CancellationToken.None);

        Assert.Equal(PimaxStartupObservationSchema.Version, snapshot.Schema);
        Assert.True(snapshot.Bounded);
        Assert.True(snapshot.Fake);
        Assert.Equal("fake-process-lifecycle", snapshot.EventSource);
        Assert.Contains(snapshot.Events, e => e.ProcessName == "DeviceSetting" && e.EventType == "processStart");
        Assert.Contains(snapshot.Events, e => e.ProcessName == "PiPlayService" && e.EventType == "processStart");
        Assert.Contains(snapshot.Events, e => e.ProcessName == "pi_server" && e.EventType == "processStart");
        Assert.All(snapshot.Events, e => Assert.StartsWith("proc-", e.ProcessToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComparisonNeverMarksManualShellSuccessAsBackendExecutable()
    {
        var request = new PimaxStartupObservationRequest(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            Fake: true,
            "pimax-startup-observe-test");
        var snapshot = await new PimaxStartupObserver().ObserveAsync(request, CancellationToken.None);

        var comparison = PimaxStartupObserver.Compare(snapshot);

        Assert.Equal("directProcessCreation", comparison.DirectLaunchSource);
        Assert.Equal("manualStartMenuActivation", comparison.ManualLaunchSource);
        Assert.False(comparison.BackendExecutable);
        Assert.Contains("DeviceSetting", comparison.ManualFormedMembers);
        Assert.Contains("safe programmatic equivalent", string.Join("\n", comparison.Blockers), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicObserverOutputExcludesRawPrivateIdentifiers()
    {
        var request = new PimaxStartupObservationRequest(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            Fake: true,
            "pimax-startup-observe-test");
        var snapshot = await new PimaxStartupObserver().ObserveAsync(request, CancellationToken.None);

        var json = JsonSerializer.Serialize(snapshot, PimaxRepairJson.Options);

        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commandLine", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environmentVariables", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("USB\\", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaticSafetyDoesNotAddForbiddenMutationOrHardwareProbing()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxStartupOrchestration.cs"));
        string[] forbidden =
        [
            "Process.Start(",
            ".Kill(",
            "Stop-Service",
            "Start-Service",
            "Restart-Service",
            "IOCTL_USB_HUB_CYCLE_PORT",
            "CM_Reenumerate",
            "SetupDiCallClassInstaller",
            "SendInput",
            "mouse_event",
            "keybd_event",
            "HttpClient",
            "WebRequest",
            "NamedPipe",
            "localhost"
        ];

        foreach (var token in forbidden)
        {
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FormalStartMenuFixtureKeepsCreatorChainPartialAndBackendDisabled()
    {
        var fixturePath = Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor.Tests",
            "Fixtures",
            "phase28d2b2a-startmenu-observation-sanitized.json");
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = document.RootElement;
        var json = root.GetRawText();

        Assert.Equal("groupReadyAndRegistered", root.GetProperty("formalStartMenuLaunch").GetProperty("result").GetString());
        Assert.Equal(PimaxStartupMechanism.ManualShellLaunchWorksMechanismStillUnresolved, root.GetProperty("mechanismResult").GetString());
        Assert.False(root.GetProperty("backendExecutable").GetBoolean());
        Assert.Equal("DeviceSetting", root.GetProperty("parentSnapshotSummary").GetProperty("piPlayServiceCreator").GetString());
        Assert.Equal("PiService", root.GetProperty("parentSnapshotSummary").GetProperty("piServerCreator").GetString());
        Assert.Equal("unresolved", root.GetProperty("parentSnapshotSummary").GetProperty("rootCreatorStatus").GetString());
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"USB\\", json, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
