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
}
