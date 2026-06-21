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
        Assert.All(snapshot.Events, e => Assert.StartsWith("process:", e.ProcessToken, StringComparison.Ordinal));
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

    [Fact]
    public void CreatorChainFixturePreservesTransientLauncherAfterExit()
    {
        var snapshot = LoadObservationFixture("phase28d2b2b-transient-creator-sanitized.json");
        var assessment = PimaxCreatorChainAnalyzer.FromObservation(snapshot);

        Assert.Equal(PimaxStartupCreatorChainSchema.Version, assessment.Schema);
        Assert.Equal("windowsExplorer", assessment.DeviceSettingRootResult);
        Assert.Equal("confirmed", assessment.Confidence);
        Assert.Equal("launcher", assessment.DeviceSettingCreator);
        Assert.Equal("DeviceSetting", assessment.PiPlayServiceCreator);
        Assert.Equal("DeviceSetting", assessment.PiServiceCreator);
        Assert.Equal("PiService", assessment.PiServerCreator);
        Assert.Contains(assessment.Nodes, node => node.ProcessName == "launcher" && node.ExitedDuringObservation);
        Assert.Contains(assessment.Edges, edge => edge.ChildProcessName == "DeviceSetting" && edge.CreatorExitedLater);
        Assert.False(assessment.BackendExecutable);
    }

    [Fact]
    public void EventProjectionKeepsDistinctTokensForCreatorAndChildren()
    {
        var snapshot = LoadObservationFixture("phase28d2b2b-transient-creator-sanitized.json");
        var events = PimaxStartupObserver.EventsFromIdentities(snapshot.Processes);

        Assert.Contains(events, item => item.EventType == "processStop" && item.ProcessName == "launcher");
        Assert.Contains(events, item => item.EventType == "processStart" && item.ProcessName == "DeviceSetting" && item.ParentToken == "process:0002");
        Assert.Equal(snapshot.Processes.Select(item => item.Token).Count(), snapshot.Processes.Select(item => item.Token).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(events, item => item.ProcessToken.All(char.IsDigit));
    }

    [Fact]
    public void CreatorChainClassifiesServiceControlManagerBroker()
    {
        var snapshot = LoadObservationFixture("phase28d2b2b-service-broker-sanitized.json");
        var assessment = PimaxCreatorChainAnalyzer.FromObservation(snapshot);

        Assert.Equal("serviceControlManager", assessment.DeviceSettingRootResult);
        Assert.Equal("confirmed", assessment.Confidence);
        Assert.Equal("PiServiceLauncher", assessment.DeviceSettingCreator);
        Assert.Equal("DeviceSetting", assessment.PiPlayServiceCreator);
        Assert.Equal("DeviceSetting", assessment.PiServiceCreator);
        Assert.Equal("PiService", assessment.PiServerCreator);
        Assert.False(assessment.BackendExecutable);
    }

    [Fact]
    public void CreatorChainClassifiesExistingPimaxBroker()
    {
        var snapshot = LoadObservationFixture("phase28d2b2b-existing-broker-sanitized.json");
        var assessment = PimaxCreatorChainAnalyzer.FromObservation(snapshot);

        Assert.Equal("existingPimaxProcess", assessment.DeviceSettingRootResult);
        Assert.Equal("confirmed", assessment.Confidence);
        Assert.Equal("PimaxShellBroker", assessment.DeviceSettingCreator);
        Assert.Contains(assessment.RootCandidates, item => item.ProcessName == "PimaxShellBroker" && item.Confidence == "confirmed");
        Assert.False(assessment.BackendExecutable);
    }

    [Fact]
    public void CreatorChainKeepsPidReuseTokensDistinct()
    {
        var snapshot = LoadObservationFixture("phase28d2b2b-pid-reuse-sanitized.json");
        var assessment = PimaxCreatorChainAnalyzer.FromObservation(snapshot);
        var launchers = snapshot.Processes.Where(item => item.ProcessName == "launcher").ToArray();

        Assert.Equal(2, launchers.Length);
        Assert.Equal(2, launchers.Select(item => item.Token).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(assessment.Edges, edge => edge.CreatorToken == "process:0002" && edge.ChildProcessName == "DeviceSetting");
        Assert.DoesNotContain(snapshot.Processes, item => item.Token.All(char.IsDigit));
        Assert.False(assessment.BackendExecutable);
    }

    [Fact]
    public void CreatorChainFixturePrivacyExcludesRawIdentifiers()
    {
        foreach (var name in new[]
        {
            "phase28d2b2b-transient-creator-sanitized.json",
            "phase28d2b2b-service-broker-sanitized.json",
            "phase28d2b2b-existing-broker-sanitized.json",
            "phase28d2b2b-pid-reuse-sanitized.json"
        })
        {
            var json = File.ReadAllText(FixturePath(name));

            Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\Users\", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("commandLine", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ProcessId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("USB\\", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ElevatedObserverRejectsNonElevatedLiveRunWithoutFallbackOrSelfElevation()
    {
        var request = new PimaxElevatedStartupObservationRequest(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            Fake: false,
            PreflightOnly: false,
            "pimax-startup-observe-elevated-test");

        var snapshot = await new PimaxStartupObserver().ObserveElevatedAsync(request, CancellationToken.None);

        if (snapshot.IsElevated)
        {
            return;
        }

        Assert.Equal(PimaxElevatedStartupObservationSchema.Version, snapshot.Schema);
        Assert.False(snapshot.Accepted);
        Assert.False(snapshot.WmiSnapshotFallbackAllowed);
        Assert.False(snapshot.SelfElevationAllowed);
        Assert.False(snapshot.PersistentElevationAllowed);
        Assert.Null(snapshot.Observation);
        Assert.Contains("Administrative elevation", string.Join("\n", snapshot.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ElevatedObserverFakePreflightReportsProviderContract()
    {
        var request = new PimaxElevatedStartupObservationRequest(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            Fake: true,
            PreflightOnly: true,
            "pimax-startup-observe-elevated-test");

        var snapshot = await new PimaxStartupObserver().ObserveElevatedAsync(request, CancellationToken.None);

        Assert.True(snapshot.Accepted);
        Assert.True(snapshot.ElevatedMode);
        Assert.True(snapshot.PreflightOnly);
        Assert.True(snapshot.Bounded);
        Assert.Equal("Microsoft-Windows-Kernel-Process", snapshot.Provider);
        Assert.Equal("elevated-process-start-stop-trace", snapshot.EventSource);
        Assert.True(snapshot.ParentIdentityCapturedAtProcessStart);
        Assert.False(snapshot.WmiSnapshotFallbackAllowed);
        Assert.Null(snapshot.Observation);
    }

    [Fact]
    public async Task ElevatedObserverFakeRunProducesNestedObservationAndKeepsBackendDisabled()
    {
        var request = new PimaxElevatedStartupObservationRequest(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            Fake: true,
            PreflightOnly: false,
            "pimax-startup-observe-elevated-test");

        var snapshot = await new PimaxStartupObserver().ObserveElevatedAsync(request, CancellationToken.None);

        Assert.True(snapshot.Accepted);
        Assert.NotNull(snapshot.Observation);
        Assert.Equal("fake-elevated-process-lifecycle", snapshot.Observation.EventSource);
        Assert.False(snapshot.Observation.CreatorChain?.BackendExecutable);
        Assert.Equal("windowsExplorer", snapshot.Observation.CreatorChain?.DeviceSettingRootResult);
        Assert.Equal("launcher", snapshot.Observation.CreatorChain?.DeviceSettingCreator);
    }

    [Fact]
    public void ElevatedObserverPublicContractExcludesPrivateIdentifiers()
    {
        var request = new PimaxElevatedStartupObservationRequest(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            Fake: true,
            PreflightOnly: true,
            "pimax-startup-observe-elevated-test");
        var snapshot = PimaxStartupObserver.BuildElevatedSnapshot(
            request,
            isElevated: true,
            accepted: true,
            warnings: [],
            errors: [],
            summary: "fixture",
            observation: null);
        var json = JsonSerializer.Serialize(snapshot, PimaxRepairJson.Options);

        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commandLine", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProcessId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parentProcessId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("S-1-", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ElevatedObserverStaticSafetyForbidsPersistentElevationAndMutation()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxStartupOrchestration.cs"));
        string[] forbidden =
        [
            "Verb = \"runas\"",
            "UseShellExecute = true",
            "ShellExecute",
            "Register-ScheduledTask",
            "New-Service",
            "CreateService",
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
            "WebRequest"
        ];

        foreach (var token in forbidden)
        {
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static PimaxStartupObservationSnapshot LoadObservationFixture(string name)
        => JsonSerializer.Deserialize<PimaxStartupObservationSnapshot>(File.ReadAllText(FixturePath(name)), PimaxRepairJson.Options)
            ?? throw new InvalidOperationException("Fixture could not be parsed.");

    private static string FixturePath(string name)
        => Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor.Tests", "Fixtures", name);
}
