using Xunit;

public sealed class PimaxShellActivationPreconditionTests
{
    [Fact]
    public void PreviousAbortFixtureIsQuiescentDespitePartialGeneralHealth()
    {
        var snapshot = Evaluate(
            PersistentSample(),
            PersistentSample(),
            PersistentSample());

        Assert.Equal(PimaxHealthOverallStatus.SoftwareStackPartial, snapshot.SoftwareGroupHealthState);
        Assert.Equal(PimaxSoftwareGroupState.Partial, snapshot.GeneralSoftwareGroupState);
        Assert.Equal(PimaxShellActivationPreconditionState.QuiescentForShellActivation, snapshot.ActivationPreconditionState);
        Assert.True(snapshot.Quiescent);
        Assert.True(snapshot.ReadinessForControlledValidation);
        Assert.Equal(3, snapshot.StableSampleCount);
        Assert.Equal("serviceOwned", snapshot.PiServiceLauncherClassification);
        Assert.Equal(PimaxRegistrationState.RegistrationEvidenceStale, snapshot.RegistrationEvidenceState);
        Assert.False(snapshot.StaleRegistrationBlocking);
        Assert.False(snapshot.BackendExecutable);
        Assert.False(snapshot.AutomaticRecoveryAllowed);
    }

    [Theory]
    [InlineData("PimaxClient")]
    [InlineData("DeviceSetting")]
    [InlineData("PiPlayService")]
    [InlineData("PiService")]
    [InlineData("pi_server")]
    [InlineData("PVRHome")]
    [InlineData("pi_overlay")]
    [InlineData("vrss_gaze_provider")]
    [InlineData("lighthouse_console")]
    [InlineData("launcher")]
    [InlineData("fastlist-0.3.0-x64")]
    public void RequiredAbsentMembersBlockActivation(string processName)
    {
        var snapshot = Evaluate(
            PersistentSample(Process(processName, "launchOrUserOwned", "other")),
            PersistentSample(Process(processName, "launchOrUserOwned", "other")),
            PersistentSample(Process(processName, "launchOrUserOwned", "other")));

        Assert.Equal(PimaxShellActivationPreconditionState.LaunchOwnedMembersPresent, snapshot.ActivationPreconditionState);
        Assert.False(snapshot.ReadinessForControlledValidation);
        Assert.Contains(processName, snapshot.CoreMembersPresent.Concat(snapshot.LaunchOwnedMembersPresent), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void PiServiceLauncherServiceOwnedInstanceIsAccepted()
    {
        var snapshot = Evaluate(PersistentSample(), PersistentSample(), PersistentSample());

        Assert.Equal("serviceOwned", snapshot.PiServiceLauncherClassification);
        Assert.Contains("PiServiceLauncher", snapshot.PermittedPersistentMembersPresent);
    }

    [Theory]
    [InlineData("deviceSettingOwned", "deviceSetting", "none", "deviceSettingOwned")]
    [InlineData("unknown", "unknown", "none", "unknownParent")]
    [InlineData("stale", "unknown", "none", "staleOwnership")]
    [InlineData("launchOrUserOwned", "other", "none", "unknownParent")]
    public void PiServiceLauncherUnapprovedOwnershipBlocksActivation(string ownership, string creator, string service, string expected)
    {
        var launcher = Process("PiServiceLauncher", ownership, creator, service);
        var snapshot = Evaluate(
            PersistentSample(includeLauncher: false, extra: [launcher]),
            PersistentSample(includeLauncher: false, extra: [launcher]),
            PersistentSample(includeLauncher: false, extra: [launcher]));

        Assert.Equal(expected, snapshot.PiServiceLauncherClassification);
        Assert.Equal(PimaxShellActivationPreconditionState.PersistentOwnershipUnresolved, snapshot.ActivationPreconditionState);
        Assert.False(snapshot.ReadinessForControlledValidation);
    }

    [Fact]
    public void TwoPiServiceLaunchersAreAmbiguousAndRefused()
    {
        var serviceOwned = Process("PiServiceLauncher", "serviceOwned", "services", "PiServiceLauncher");
        var unknown = Process("PiServiceLauncher", "unknown", "unknown", "none", stabilityKey: "second");
        var snapshot = Evaluate(PersistentSample(serviceOwned, unknown), PersistentSample(serviceOwned, unknown), PersistentSample(serviceOwned, unknown));

        Assert.Equal("ambiguous", snapshot.PiServiceLauncherClassification);
        Assert.Equal(PimaxShellActivationPreconditionState.PersistentOwnershipUnresolved, snapshot.ActivationPreconditionState);
    }

    [Fact]
    public void PermittedPersistentComponentsMayRemainAloneOrTogether()
    {
        var platform = Evaluate(PersistentSample(includeLauncher: false, includeTobii: false), PersistentSample(includeLauncher: false, includeTobii: false), PersistentSample(includeLauncher: false, includeTobii: false));
        var tobii = Evaluate(PersistentSample(includeLauncher: false, includePlatform: false), PersistentSample(includeLauncher: false, includePlatform: false), PersistentSample(includeLauncher: false, includePlatform: false));
        var both = Evaluate(PersistentSample(includeLauncher: false), PersistentSample(includeLauncher: false), PersistentSample(includeLauncher: false));

        Assert.True(platform.ReadinessForControlledValidation);
        Assert.True(tobii.ReadinessForControlledValidation);
        Assert.True(both.ReadinessForControlledValidation);
    }

    [Fact]
    public void UnknownPimaxExecutableBlocksActivation()
    {
        var unknown = Process("PimaxMysteryHelper", "unknown", "unknown", "none");
        var snapshot = Evaluate(PersistentSample(unknown), PersistentSample(unknown), PersistentSample(unknown));

        Assert.Equal(PimaxShellActivationPreconditionState.UnclassifiedMembersPresent, snapshot.ActivationPreconditionState);
        Assert.Contains("PimaxMysteryHelper", snapshot.UnclassifiedMembersPresent);
    }

    [Fact]
    public void DuplicateInstallationRootBlocksActivation()
    {
        var duplicate = Process("PiPlatformService_64", "persistentPlatform", "approvedServiceHost", "none", root: "duplicateOrUnexpectedPimaxRoot");
        var snapshot = Evaluate(PersistentSample(duplicate), PersistentSample(duplicate), PersistentSample(duplicate));

        Assert.Equal(PimaxShellActivationPreconditionState.DuplicateInstallation, snapshot.ActivationPreconditionState);
        Assert.Equal("unexpectedPimaxRootObserved", snapshot.DuplicateInstallationEvidence);
    }

    [Fact]
    public void StaleAndInsufficientRegistrationAreNonBlockingOnlyWhenQuiescent()
    {
        var stale = Evaluate(PersistentSample(registration: PimaxRegistrationState.RegistrationEvidenceStale), PersistentSample(registration: PimaxRegistrationState.RegistrationEvidenceStale), PersistentSample(registration: PimaxRegistrationState.RegistrationEvidenceStale));
        var unknown = Evaluate(PersistentSample(registration: PimaxRegistrationState.Unknown), PersistentSample(registration: PimaxRegistrationState.Unknown), PersistentSample(registration: PimaxRegistrationState.Unknown));
        var staleWithCore = Evaluate(
            PersistentSampleWithRegistration(PimaxRegistrationState.RegistrationEvidenceStale, Process("PimaxClient", "launchOrUserOwned", "other")),
            PersistentSampleWithRegistration(PimaxRegistrationState.RegistrationEvidenceStale, Process("PimaxClient", "launchOrUserOwned", "other")),
            PersistentSampleWithRegistration(PimaxRegistrationState.RegistrationEvidenceStale, Process("PimaxClient", "launchOrUserOwned", "other")));

        Assert.True(stale.ReadinessForControlledValidation);
        Assert.True(unknown.ReadinessForControlledValidation);
        Assert.False(stale.RegistrationEvidenceState == PimaxRegistrationState.RegisteredReady);
        Assert.True(staleWithCore.StaleRegistrationBlocking);
        Assert.False(staleWithCore.ReadinessForControlledValidation);
    }

    [Fact]
    public void ContradictoryRegistrationBlocksActivation()
    {
        var snapshot = Evaluate(PersistentSample(registration: PimaxRegistrationState.ConflictingEvidence), PersistentSample(registration: PimaxRegistrationState.ConflictingEvidence), PersistentSample(registration: PimaxRegistrationState.ConflictingEvidence));

        Assert.Equal(PimaxShellActivationPreconditionState.RegistrationContradictory, snapshot.ActivationPreconditionState);
        Assert.False(snapshot.ReadinessForControlledValidation);
    }

    [Fact]
    public void ThreeIdenticalSamplesAreRequiredForStability()
    {
        var one = Evaluate(PersistentSample());
        var changed = Evaluate(
            PersistentSample(includeTobii: false),
            PersistentSample(includeTobii: true),
            PersistentSample(includeTobii: false));

        Assert.Equal(PimaxShellActivationPreconditionState.Incomplete, one.ActivationPreconditionState);
        Assert.False(one.ReadinessForControlledValidation);
        Assert.Equal(PimaxShellActivationPreconditionState.Incomplete, changed.ActivationPreconditionState);
        Assert.False(changed.Stable);
    }

    [Fact]
    public void RecoveryLeaseBlocksActivation()
    {
        var sample = PersistentSample() with { RecoveryLeaseActive = true };
        var snapshot = Evaluate(sample, sample, sample);

        Assert.Equal(PimaxShellActivationPreconditionState.RecoveryLeaseActive, snapshot.ActivationPreconditionState);
        Assert.False(snapshot.ReadinessForControlledValidation);
    }

    [Fact]
    public void CapabilityUsesQuiescenceInsteadOfGeneralPartialHealth()
    {
        var coordinator = new PimaxShellActivationCoordinator(targetInspector: new FakeTargetInspector());
        var precondition = Evaluate(PersistentSample(), PersistentSample(), PersistentSample());

        var capability = coordinator.BuildCapability([ValidShortcut()], precondition);

        Assert.Equal(PimaxHealthOverallStatus.SoftwareStackPartial, capability.CurrentComponentHealthState);
        Assert.Equal(PimaxSoftwareGroupState.Partial, capability.CurrentSoftwareGroupState);
        Assert.Equal(PimaxShellActivationCapabilityState.ReadyForControlledValidation, capability.CapabilityState);
        Assert.Equal(PimaxShellActivationPreconditionState.QuiescentForShellActivation, capability.ActivationPrecondition?.ActivationPreconditionState);
        Assert.True(capability.ReadinessForControlledValidation);
        Assert.False(capability.BackendExecutable);
    }

    [Fact]
    public void CapabilityStillRequiresTrustedShellEntry()
    {
        var coordinator = new PimaxShellActivationCoordinator(targetInspector: new FakeTargetInspector());
        var precondition = Evaluate(PersistentSample(), PersistentSample(), PersistentSample());

        var capability = coordinator.BuildCapability([Shortcut(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\PimaxPlay.lnk", @"C:\Windows\System32\cmd.exe")], precondition);

        Assert.Equal(PimaxShellActivationCapabilityState.ShellEntryInvalid, capability.CapabilityState);
        Assert.False(capability.ReadinessForControlledValidation);
    }

    private static PimaxShellActivationPreconditionSnapshot Evaluate(params PimaxShellQuiescenceSample[] samples)
        => PimaxShellActivationPreconditionCoordinator.Evaluate(
            samples,
            PimaxShellActivationCapabilityState.ReadyForControlledValidation,
            requiredStableSamples: 3,
            TimeSpan.FromSeconds(1),
            DateTimeOffset.Parse("2026-06-21T00:00:00Z"));

    private static PimaxShellQuiescenceSample PersistentSample(
        params PimaxShellQuiescenceProcessSnapshot[] extra)
        => PersistentSample(includePlatform: true, includeLauncher: true, includeTobii: true, registration: PimaxRegistrationState.RegistrationEvidenceStale, extra);

    private static PimaxShellQuiescenceSample PersistentSampleWithRegistration(string registration, params PimaxShellQuiescenceProcessSnapshot[] extra)
        => PersistentSample(includePlatform: true, includeLauncher: true, includeTobii: true, registration: registration, extra);

    private static PimaxShellQuiescenceSample PersistentSample(
        bool includePlatform = true,
        bool includeLauncher = true,
        bool includeTobii = true,
        string registration = PimaxRegistrationState.RegistrationEvidenceStale,
        params PimaxShellQuiescenceProcessSnapshot[] extra)
    {
        var processes = new List<PimaxShellQuiescenceProcessSnapshot>();
        if (includePlatform) processes.Add(Process("PiPlatformService_64", "persistentPlatform", "approvedServiceHost"));
        if (includeLauncher) processes.Add(Process("PiServiceLauncher", "serviceOwned", "services", "PiServiceLauncher"));
        if (includeTobii) processes.Add(Process("Tobii VR4PIMAXP3B Platform Runtime", "persistentPlatform", "approvedServiceHost"));
        processes.AddRange(extra);
        return new PimaxShellQuiescenceSample(
            DateTimeOffset.Parse("2026-06-21T00:00:00Z"),
            Health(registration),
            processes.ToArray(),
            RecoveryLeaseActive: false);
    }

    private static PimaxShellQuiescenceProcessSnapshot Process(
        string name,
        string ownership,
        string creator,
        string service = "none",
        string root = "expectedPimaxRoot",
        string stabilityKey = "stable")
        => new(name, "<pimax>\\Runtime\\" + name + ".exe", ownership, creator, service, root, name + "-" + stabilityKey);

    private static PimaxComponentHealthSnapshot Health(string registration)
    {
        var assessment = new PimaxRegistrationAssessmentResult(registration, registration == PimaxRegistrationState.ConflictingEvidence ? PimaxRegistrationConfidence.Probable : PimaxRegistrationConfidence.Insufficient, PimaxEvidenceFreshness.Stale, "synthetic", [], [], [], [], [], new PimaxRegistrationEvidence(false, 0, 0, false, 0, 0, false, false, false, false, false, [], []));
        var group = PimaxSoftwareGroupModel.FromMembers(
            DateTimeOffset.Parse("2026-06-21T00:00:00Z"),
            "test",
            new PimaxSoftwareGroupMember("PiPlatformService_64", PimaxSoftwareGroupRole.OptionalComponent, "<pimax>\\Runtime\\PiPlatformService_64.exe", "Pimax publisher metadata present.", "current", "persistent", false, false),
            new PimaxSoftwareGroupMember("PiServiceLauncher", PimaxSoftwareGroupRole.ServiceOwnedProcess, "<pimax>\\Runtime\\PiServiceLauncher.exe", "Pimax publisher metadata present.", "service", "persistent", true, false),
            new PimaxSoftwareGroupMember("Tobii VR4PIMAXP3B Platform Runtime", PimaxSoftwareGroupRole.OptionalComponent, "<pimax>\\Runtime\\Tobii.exe", "Pimax publisher metadata present.", "current", "persistent", false, false));
        return new PimaxComponentHealthSnapshot(
            PimaxComponentHealthSchema.Version,
            DateTimeOffset.Parse("2026-06-21T00:00:00Z"),
            "health",
            PimaxHealthOverallStatus.SoftwareStackPartial,
            assessment,
            "insufficient",
            [],
            [],
            [],
            [],
            "synthetic",
            "insufficient",
            new PimaxHealthCapabilitySummary("unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "synthetic"),
            new PimaxHealthSanitizedEvidence(PimaxConnectivitySchema.Version, PimaxUsbEnumerationSchema.Version, PimaxRegistrationAssessmentSchema.Version, "synthetic", registration, 0, 0, [], [], group.Members.Select(member => member.ProcessName).ToArray(), ["PiServiceLauncher"], PimaxEvidenceFreshness.Stale, group),
            [],
            []);
    }

    private static PimaxShellShortcutCandidate ValidShortcut()
        => Shortcut(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\PimaxPlay.lnk", @"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe");

    private static PimaxShellShortcutCandidate Shortcut(string path, string target)
        => new(path, "commonStartMenu", new PimaxShellShortcutInfo(target, "", @"C:\Program Files\Pimax\PimaxClient\pimaxui"));

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
