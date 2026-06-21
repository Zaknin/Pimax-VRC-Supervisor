using System.Text.Json;
using Xunit;

public sealed class PimaxShellActivationTests
{
    [Fact]
    public void CapabilitySelectsSingleTrustedOfficialStartMenuShortcutOnly()
    {
        var coordinator = new PimaxShellActivationCoordinator(targetInspector: new FakeTargetInspector());

        var capability = coordinator.BuildCapability(
            [ValidShortcut()],
            Group(PimaxSoftwareGroupState.Unavailable),
            PimaxHealthOverallStatus.SoftwareStackUnavailable);

        Assert.Equal(PimaxShellActivationCapabilitySchema.Version, capability.Schema);
        Assert.Equal(PimaxShellActivationCapabilityState.ReadyForControlledValidation, capability.CapabilityState);
        Assert.NotNull(capability.SelectedShellEntry);
        Assert.Equal("Windows Shell open verb against official Start Menu .lnk", capability.ActivationMethod);
        Assert.False(capability.DirectExecutableFallbackAllowed);
        Assert.False(capability.RuntimeComponentFallbackAllowed);
        Assert.False(capability.ServiceMutationAllowed);
        Assert.False(capability.RetryAllowed);
        Assert.False(capability.BackendExecutable);
        Assert.True(capability.ReadinessForControlledValidation);
    }

    [Theory]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\PimaxPlay.lnk", @"\\server\share\PimaxClient.exe", "", PimaxShellActivationCapabilityState.ShellEntryInvalid)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\PimaxPlay.lnk", "https://example.test/PimaxClient.exe", "", PimaxShellActivationCapabilityState.ShellEntryInvalid)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\PimaxPlay.lnk", @"C:\Windows\System32\cmd.exe", "", PimaxShellActivationCapabilityState.ShellEntryInvalid)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\PimaxPlay.lnk", @"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe", "--unexpected", PimaxShellActivationCapabilityState.ShellEntryInvalid)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\PimaxClient.exe", @"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe", "", PimaxShellActivationCapabilityState.ShellEntryInvalid)]
    public void DiscoveryRejectsInvalidShortcutTargetsAndDirectExecutableSubstitutes(string shortcutPath, string targetPath, string args, string expected)
    {
        var coordinator = new PimaxShellActivationCoordinator(targetInspector: new FakeTargetInspector());

        var capability = coordinator.BuildCapability(
            [Shortcut(shortcutPath, targetPath, args, @"C:\Program Files\Pimax\PimaxClient\pimaxui")],
            Group(PimaxSoftwareGroupState.Unavailable),
            PimaxHealthOverallStatus.SoftwareStackUnavailable);

        Assert.Equal(expected, capability.CapabilityState);
        Assert.Null(capability.SelectedShellEntry);
        Assert.Contains(capability.Candidates.Single().Blockers, blocker => blocker.Length > 0);
    }

    [Fact]
    public void DuplicateTrustedShortcutsAreAmbiguous()
    {
        var coordinator = new PimaxShellActivationCoordinator(targetInspector: new FakeTargetInspector());

        var capability = coordinator.BuildCapability(
            [
                ValidShortcut(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\PimaxPlay.lnk", "commonStartMenu"),
                ValidShortcut(@"C:\Users\user\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\PimaxPlay.lnk", "currentUserStartMenu")
            ],
            Group(PimaxSoftwareGroupState.Unavailable),
            PimaxHealthOverallStatus.SoftwareStackUnavailable);

        Assert.Equal(PimaxShellActivationCapabilityState.ShellEntryAmbiguous, capability.CapabilityState);
        Assert.Null(capability.SelectedShellEntry);
    }

    [Theory]
    [InlineData(PimaxSoftwareGroupState.Complete, PimaxShellActivationCapabilityState.SoftwareGroupAlreadyRunning)]
    [InlineData(PimaxSoftwareGroupState.Partial, PimaxShellActivationCapabilityState.SoftwareGroupPartial)]
    [InlineData(PimaxSoftwareGroupState.Conflicting, PimaxShellActivationCapabilityState.SoftwareGroupPartial)]
    [InlineData(PimaxSoftwareGroupState.Unknown, PimaxShellActivationCapabilityState.SoftwareGroupStateUnknown)]
    public void PreconditionsRefuseNonStoppedSoftwareGroupStates(string groupState, string expected)
    {
        var coordinator = new PimaxShellActivationCoordinator(targetInspector: new FakeTargetInspector());

        var capability = coordinator.BuildCapability([ValidShortcut()], Group(groupState), "syntheticHealth");

        Assert.Equal(expected, capability.CapabilityState);
        Assert.False(capability.ReadinessForControlledValidation);
        Assert.False(capability.BackendExecutable);
    }

    [Fact]
    public async Task ActivationCommandIsPolicyDisabledEvenWithExactConfirmation()
    {
        var coordinator = new PimaxShellActivationCoordinator();

        var result = await coordinator.ActivateAsync(
            new SupervisorConfig(),
            new PimaxShellActivationCommandLine(PimaxShellActivationCoordinator.ConfirmationString),
            CancellationToken.None);

        Assert.Equal(PimaxShellActivationResultSchema.Version, result.Schema);
        Assert.False(result.Accepted);
        Assert.True(result.ConfirmationAccepted);
        Assert.Equal(PimaxShellActivationState.Refused, result.State);
        Assert.Equal("implementationCompleteLiveValidationRequired", result.PolicyRefusalReason);
        Assert.True(result.ExactlyOneShellRequest);
        Assert.True(result.NoRetryPolicy);
        Assert.True(result.NoDirectLaunchPolicy);
        Assert.True(result.NoServiceMutationPolicy);
        Assert.False(result.BackendExecutable);
    }

    [Fact]
    public async Task ValidationCommandRefusesMissingConfirmationBeforeShellRequest()
    {
        var requestor = new FakeActivationRequestor();
        var coordinator = ValidationCoordinator(requestor: requestor);

        var result = await coordinator.ActivateValidationAsync(
            new SupervisorConfig(),
            new PimaxShellActivationCommandLine(null, Guid.NewGuid().ToString()),
            CancellationToken.None);

        Assert.Equal(PimaxShellActivationValidationSchema.Version, result.Schema);
        Assert.False(result.ValidationExecutionAccepted);
        Assert.False(result.ConfirmationAccepted);
        Assert.Equal(0, result.ShellRequestCount);
        Assert.False(result.ActivationRequestResult.Attempted);
        Assert.False(result.BackendExecutable);
        Assert.False(result.AutomaticRecoveryAllowed);
        Assert.Contains("confirmation", string.Join("\n", result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task ValidationCommandRequiresValidCorrelationId(string? correlationId)
    {
        var coordinator = ValidationCoordinator();

        var result = await coordinator.ActivateValidationAsync(
            new SupervisorConfig(),
            new PimaxShellActivationCommandLine(PimaxShellActivationCoordinator.ValidationConfirmationString, correlationId),
            CancellationToken.None);

        Assert.False(result.ValidationExecutionAccepted);
        Assert.Equal(0, result.ShellRequestCount);
        Assert.Contains("Correlation ID", string.Join("\n", result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidationCommandRefusesElevatedOrExplorerMismatchedContext()
    {
        var coordinator = ValidationCoordinator(context: new FakeExecutionContext(elevated: true, explorerMatched: false));

        var result = await coordinator.ActivateValidationAsync(
            new SupervisorConfig(),
            new PimaxShellActivationCommandLine(PimaxShellActivationCoordinator.ValidationConfirmationString, Guid.NewGuid().ToString()),
            CancellationToken.None);

        Assert.False(result.ValidationExecutionAccepted);
        Assert.True(result.ElevatedState);
        Assert.False(result.ExplorerSessionMatched);
        Assert.Equal(0, result.ShellRequestCount);
    }

    [Fact]
    public async Task ValidationCommandAcceptsStoppedGroupAndRequestsShellExactlyOnce()
    {
        var requestor = new FakeActivationRequestor();
        var correlationId = Guid.NewGuid().ToString();
        var coordinator = ValidationCoordinator(requestor: requestor);

        var result = await coordinator.ActivateValidationAsync(
            new SupervisorConfig(),
            new PimaxShellActivationCommandLine(PimaxShellActivationCoordinator.ValidationConfirmationString, correlationId),
            CancellationToken.None);

        Assert.True(result.ValidationExecutionAccepted);
        Assert.True(result.Accepted);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(1, result.ShellRequestCount);
        var requestedPath = Assert.Single(requestor.RequestedPaths);
        Assert.EndsWith("PimaxPlay.lnk", requestedPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.RetryCount);
        Assert.False(result.DirectFallbackAttempted);
        Assert.False(result.RuntimeComponentLaunchAttempted);
        Assert.False(result.ServiceMutationAttempted);
        Assert.False(result.ProcessTerminationAttempted);
        Assert.Equal(PimaxSoftwareGroupState.Complete, result.FinalSoftwareGroupState);
        Assert.Equal(3, result.StableSampleCount);
        Assert.False(result.BackendExecutable);
        Assert.False(result.TuiExposureAllowed);
        Assert.False(result.ConfiguratorExposureAllowed);
        Assert.False(result.WatcherExecutionAllowed);
    }

    [Fact]
    public async Task ValidationCommandRefusesAlreadyRunningGroupWithoutShellRequest()
    {
        var requestor = new FakeActivationRequestor();
        var coordinator = ValidationCoordinator(requestor: requestor, healthStates: [Health(CompleteRuntimeGroup(), PimaxHealthOverallStatus.Healthy)]);

        var result = await coordinator.ActivateValidationAsync(
            new SupervisorConfig(),
            new PimaxShellActivationCommandLine(PimaxShellActivationCoordinator.ValidationConfirmationString, Guid.NewGuid().ToString()),
            CancellationToken.None);

        Assert.False(result.ValidationExecutionAccepted);
        Assert.Equal(PimaxShellActivationPreconditionState.LaunchOwnedMembersPresent, result.ShellEntryValidationResult);
        Assert.Equal(0, result.ShellRequestCount);
        Assert.Empty(requestor.RequestedPaths);
    }

    [Fact]
    public void ReadinessRequiresThreeConsecutiveHealthySamplesAndToleratesChurn()
    {
        var samples = new[]
        {
            new PimaxShellReadinessSample(TimeSpan.FromSeconds(1), Group(PimaxSoftwareGroupState.Unavailable), "starting"),
            new PimaxShellReadinessSample(TimeSpan.FromSeconds(5), Group(PimaxSoftwareGroupState.Partial, "PimaxClient"), "starting"),
            new PimaxShellReadinessSample(TimeSpan.FromSeconds(10), Group(PimaxSoftwareGroupState.Partial, "PimaxClient", "DeviceSetting", "PiPlayService"), "starting"),
            new PimaxShellReadinessSample(TimeSpan.FromSeconds(20), CompleteRuntimeGroup(), "healthy"),
            new PimaxShellReadinessSample(TimeSpan.FromSeconds(21), CompleteRuntimeGroup(), "healthy"),
            new PimaxShellReadinessSample(TimeSpan.FromSeconds(22), CompleteRuntimeGroup(), "healthy")
        };

        var observation = PimaxShellActivationCoordinator.EvaluateReadiness(samples);

        Assert.Equal(PimaxShellActivationState.Healthy, observation.State);
        Assert.Equal(3, observation.ConsecutiveHealthySamples);
        Assert.Empty(observation.RequiredMembersMissing);
        Assert.Contains("Headset registration is still reported separately", observation.HumanReadableSummary);
    }

    [Fact]
    public void ReadinessTimesOutWithinBoundedNinetySeconds()
    {
        var observation = PimaxShellActivationCoordinator.EvaluateReadiness(
            [
                new PimaxShellReadinessSample(TimeSpan.FromSeconds(15), Group(PimaxSoftwareGroupState.Partial, "PimaxClient"), "starting"),
                new PimaxShellReadinessSample(TimeSpan.FromSeconds(90), Group(PimaxSoftwareGroupState.Partial, "PimaxClient", "DeviceSetting"), "partial")
            ]);

        Assert.Equal(PimaxShellActivationState.TimedOut, observation.State);
        Assert.Contains("90", observation.HumanReadableSummary);
    }

    [Fact]
    public void PublicJsonRedactsPrivatePathAndRawIdentifiers()
    {
        var coordinator = new PimaxShellActivationCoordinator(targetInspector: new FakeTargetInspector());
        var capability = coordinator.BuildCapability([ValidShortcut()], Group(PimaxSoftwareGroupState.Unavailable), "syntheticHealth");
        var json = JsonSerializer.Serialize(capability, PimaxRepairJson.Options);

        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProcessId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SerialNumber", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaticSafetyContainsNoRuntimeComponentFallbackOrServiceMutation()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxShellActivation.cs"));
        string[] forbidden =
        [
            "DeviceSetting.exe",
            "PiPlayService.exe",
            "PiService.exe",
            "PiServiceLauncher.exe",
            "pi_server.exe",
            "lighthouse_console.exe",
            ".Kill(",
            "Stop-Service",
            "Start-Service",
            "Restart-Service",
            "SetScheduledTask",
            "Register-ScheduledTask",
            "SendInput",
            "mouse_event",
            "keybd_event",
            "IOCTL_USB_HUB_CYCLE_PORT"
        ];

        foreach (var token in forbidden)
        {
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PimaxShellShortcutCandidate ValidShortcut(
        string path = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\PimaxPlay.lnk",
        string source = "commonStartMenu")
        => Shortcut(path, @"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe", "", @"C:\Program Files\Pimax\PimaxClient\pimaxui", source);

    private static PimaxShellActivationCoordinator ValidationCoordinator(
        FakeActivationRequestor? requestor = null,
        IPimaxShellExecutionContextInspector? context = null,
        PimaxComponentHealthSnapshot[]? healthStates = null)
    {
        var states = new Queue<PimaxComponentHealthSnapshot>(healthStates ??
        [
            Health(CompleteRuntimeGroup(), PimaxHealthOverallStatus.Healthy),
            Health(CompleteRuntimeGroup(), PimaxHealthOverallStatus.Healthy),
            Health(CompleteRuntimeGroup(), PimaxHealthOverallStatus.Healthy)
        ]);
        var runningPrecondition = healthStates?.Length == 1
            && healthStates[0].SourceEvidence.SoftwareGroup.State == PimaxSoftwareGroupState.Complete;

        return new PimaxShellActivationCoordinator(
            shortcutReader: new FakeShortcutReader(),
            targetInspector: new FakeTargetInspector(),
            activationRequestor: requestor ?? new FakeActivationRequestor(),
            executionContextInspector: context ?? new FakeExecutionContext(),
            healthCollector: (_, _) => Task.FromResult(states.Count > 1 ? states.Dequeue() : states.Peek()),
            quiescenceProbe: new FakeQuiescenceProbe(runningPrecondition),
            shortcutDiscovery: () => [ValidShortcut()],
            validationTimeout: TimeSpan.FromSeconds(1),
            validationPollInterval: TimeSpan.FromMilliseconds(1));
    }

    private static PimaxShellShortcutCandidate Shortcut(string path, string target, string args, string workingDirectory, string source = "commonStartMenu")
        => new(path, source, new PimaxShellShortcutInfo(target, args, workingDirectory));

    private static PimaxSoftwareGroupSnapshot Group(string state, params string[] members)
    {
        var groupMembers = members.Select(name => new PimaxSoftwareGroupMember(
            name,
            name == "PimaxClient" ? PimaxSoftwareGroupRole.PimaxPlayUiProcess :
                name == "PiService" ? PimaxSoftwareGroupRole.ServiceOwnedProcess :
                PimaxSoftwareGroupRole.RuntimeProcess,
            @"<pimax>\Runtime\" + name + ".exe",
            "Pimax publisher metadata present.",
            "synthetic current sample",
            "coupled",
            true,
            false)).ToArray();
        return new PimaxSoftwareGroupSnapshot(DateTimeOffset.UtcNow, "test", state, PimaxEvidenceFreshness.Current, groupMembers, [], [], PimaxSoftwareGroupModel.IncompleteRecipe(), "synthetic");
    }

    private static PimaxSoftwareGroupSnapshot CompleteRuntimeGroup()
        => Group(PimaxSoftwareGroupState.Complete, "PimaxClient", "DeviceSetting", "PiPlayService", "PiService", "pi_server");

    private static PimaxComponentHealthSnapshot Health(PimaxSoftwareGroupSnapshot group, string overall)
    {
        var registration = new PimaxRegistrationAssessmentResult(PimaxRegistrationState.RegisteredReady, PimaxRegistrationConfidence.Confirmed, PimaxEvidenceFreshness.Current, "synthetic", [], [], [], [], [], new PimaxRegistrationEvidence(true, 1, 1, true, 1, 1, true, true, true, true, true, [], []));
        return new PimaxComponentHealthSnapshot(
            PimaxComponentHealthSchema.Version,
            DateTimeOffset.UtcNow,
            "health",
            overall,
            registration,
            "probable",
            [],
            [],
            [],
            [],
            "summary",
            "confirmed",
            new PimaxHealthCapabilitySummary("available", "available", "available", "available", "available", "available", "ready", "summary"),
            new PimaxHealthSanitizedEvidence(PimaxConnectivitySchema.Version, PimaxUsbEnumerationSchema.Version, PimaxRegistrationAssessmentSchema.Version, "synthetic", registration.State, 1, 1, [], [], group.Members.Select(member => member.ProcessName).ToArray(), ["PiServiceLauncher"], registration.EvidenceFreshness, group),
            [],
            []);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeTargetInspector : IPimaxShellTargetInspector
    {
        public PimaxShellTargetEvidence Inspect(string targetPath)
            => new(
                !targetPath.Contains("missing", StringComparison.OrdinalIgnoreCase),
                targetPath.EndsWith("PimaxClient.exe", StringComparison.OrdinalIgnoreCase) ? "PimaxClient" : "Unknown",
                targetPath.StartsWith(@"C:\Program Files\Pimax\", StringComparison.OrdinalIgnoreCase) ? "Pimax" : "Unknown",
                "1.43.9.272",
                "1.43.9.272",
                targetPath.StartsWith(@"C:\Program Files\Pimax\", StringComparison.OrdinalIgnoreCase) ? "Pimax certificate subject present" : "untrusted",
                targetPath.StartsWith(@"C:\Program Files\Pimax\", StringComparison.OrdinalIgnoreCase),
                "asInvoker");
    }

    private sealed class FakeShortcutReader : IPimaxShellShortcutReader
    {
        public PimaxShellShortcutInfo? Read(string shortcutPath)
            => new(@"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe", "", @"C:\Program Files\Pimax\PimaxClient\pimaxui");
    }

    private sealed class FakeActivationRequestor : IPimaxShellActivationRequestor
    {
        public List<string> RequestedPaths { get; } = [];

        public Task<PimaxShellActivationRequestResult> RequestAsync(string shortcutPath, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(shortcutPath);
            return Task.FromResult(new PimaxShellActivationRequestResult(true, true, "accepted"));
        }
    }

    private sealed class FakeExecutionContext(bool elevated = false, bool explorerMatched = true, bool interactive = true, bool sessionZero = false) : IPimaxShellExecutionContextInspector
    {
        public PimaxShellActivationExecutionContext Inspect()
            => new(
                IsWindows: true,
                IsLocalSystem: false,
                IsSessionZero: sessionZero,
                IsServiceContext: !interactive || sessionZero,
                IsScheduledWatcherContext: false,
                IsInteractive: interactive,
                IsElevated: elevated,
                CurrentSessionId: sessionZero ? 0 : 1,
                ExplorerSessionMatched: explorerMatched,
                ExplorerSessionEvidence: explorerMatched ? ["explorerSession:1"] : [],
                Summary: "synthetic");
    }

    private sealed class FakeQuiescenceProbe(bool running) : IPimaxShellQuiescenceProbe
    {
        public Task<PimaxShellQuiescenceSample> CollectAsync(SupervisorConfig config, CancellationToken cancellationToken)
        {
            var group = running ? CompleteRuntimeGroup() : Group(PimaxSoftwareGroupState.Unavailable);
            var health = Health(group, running ? PimaxHealthOverallStatus.Healthy : PimaxHealthOverallStatus.SoftwareStackPartial);
            var processes = running
                ? group.Members
                    .Select(member => new PimaxShellQuiescenceProcessSnapshot(member.ProcessName, member.SanitizedPath ?? "", "launchOrUserOwned", "other", "none", "expectedPimaxRoot", member.ProcessName))
                    .ToArray()
                :
                [
                    new PimaxShellQuiescenceProcessSnapshot("PiPlatformService_64", "<pimax>\\Runtime\\PiPlatformService_64.exe", "persistentPlatform", "approvedServiceHost", "none", "expectedPimaxRoot", "platform"),
                    new PimaxShellQuiescenceProcessSnapshot("PiServiceLauncher", "<pimax>\\Runtime\\PiServiceLauncher.exe", "serviceOwned", "services", "PiServiceLauncher", "expectedPimaxRoot", "launcher"),
                    new PimaxShellQuiescenceProcessSnapshot("Tobii VR4PIMAXP3B Platform Runtime", "<pimax>\\Runtime\\Tobii.exe", "persistentPlatform", "approvedServiceHost", "none", "expectedPimaxRoot", "tobii")
                ];
            return Task.FromResult(new PimaxShellQuiescenceSample(DateTimeOffset.UtcNow, health, processes, RecoveryLeaseActive: false));
        }
    }
}
