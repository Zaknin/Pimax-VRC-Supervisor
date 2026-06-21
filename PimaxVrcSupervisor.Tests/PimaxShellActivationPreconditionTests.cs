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
    public void LiveEquivalentVrssServiceDescendantAndProtectedWatcherAreAccepted()
    {
        var vrss = Vrss();
        var watcher = Process("PimaxVrcSupervisorWatcher", "unknown", "unknown", path: @"C:\Users\operator\Documents\PimaxVrcSupervisor-TestDeployments\Phase29DE-ac140b5\PimaxVrcSupervisorWatcher.exe");

        var snapshot = Evaluate(
            PersistentSample(includeLauncher: false, extra: [vrss, watcher]),
            PersistentSample(includeLauncher: false, extra: [vrss, watcher]),
            PersistentSample(includeLauncher: false, extra: [vrss, watcher]));

        Assert.Equal(PimaxShellActivationPreconditionState.QuiescentForShellActivation, snapshot.ActivationPreconditionState);
        Assert.True(snapshot.ReadinessForControlledValidation);
        Assert.Contains("vrss_gaze_provider", snapshot.PermittedPersistentMembersPresent);
        Assert.DoesNotContain("PimaxVrcSupervisorWatcher", snapshot.UnclassifiedMembersPresent);
        Assert.DoesNotContain(snapshot.OwnershipEvidence, evidence => evidence.ProcessName == "PimaxVrcSupervisorWatcher");
        var evidence = Assert.Single(snapshot.OwnershipEvidence, evidence => evidence.ProcessName == "vrss_gaze_provider");
        Assert.Equal("persistentServiceDescendant", evidence.OwnershipClassification);
        Assert.Equal("persistentServiceDescendantFromPreservedEvidence", evidence.CreatorClassification);
        Assert.Equal("session0", evidence.SessionClassification);
        Assert.Equal("unsigned", evidence.SignatureState);
        Assert.Equal("exactExpectedRuntimePath", evidence.CanonicalPathClassification);
        Assert.Equal("probable", evidence.ProvenanceConfidence);
        Assert.Equal("expectedPiServiceLauncherPath", evidence.ServiceBinaryPathClassification);
        Assert.Equal("trustedSignedExpectedLauncher", evidence.ServiceSignerClassification);
    }

    [Theory]
    [InlineData("unknown", "unknown", "unknown", "expectedPiServiceLauncherPath", "trustedSignedExpectedLauncher", "session0", "exactExpectedRuntimePath")]
    [InlineData("persistentServiceDescendant", "deviceSetting", "none", "expectedPiServiceLauncherPath", "trustedSignedExpectedLauncher", "session0", "exactExpectedRuntimePath")]
    [InlineData("launchOrUserOwned", "contradictoryLiveParent", "none", "expectedPiServiceLauncherPath", "trustedSignedExpectedLauncher", "session0", "exactExpectedRuntimePath")]
    [InlineData("persistentServiceDescendant", "persistentServiceDescendantFromPreservedEvidence", "probable", "expectedPiServiceLauncherPath", "trustedSignedExpectedLauncher", "interactiveSession", "exactExpectedRuntimePath")]
    [InlineData("persistentServiceDescendant", "persistentServiceDescendantFromPreservedEvidence", "probable", "unexpectedPiServiceLauncherPath", "trustedSignedExpectedLauncher", "session0", "exactExpectedRuntimePath")]
    [InlineData("persistentServiceDescendant", "persistentServiceDescendantFromPreservedEvidence", "probable", "expectedPiServiceLauncherPath", "untrustedOrUnsignedLauncher", "session0", "exactExpectedRuntimePath")]
    [InlineData("persistentServiceDescendant", "persistentServiceDescendantFromPreservedEvidence", "probable", "expectedPiServiceLauncherPath", "trustedSignedExpectedLauncher", "session0", "notExactExpectedRuntimePath")]
    public void VrssRequiresExactPersistentServiceDescendantEvidence(string ownership, string creator, string confidence, string servicePath, string serviceSigner, string session, string pathClassification)
    {
        var vrss = Vrss(
            ownership: ownership,
            creator: creator,
            confidence: confidence,
            servicePath: servicePath,
            serviceSigner: serviceSigner,
            session: session,
            pathClassification: pathClassification);

        var snapshot = Evaluate(
            PersistentSample(includeLauncher: false, extra: [vrss]),
            PersistentSample(includeLauncher: false, extra: [vrss]),
            PersistentSample(includeLauncher: false, extra: [vrss]));

        Assert.Equal(PimaxShellActivationPreconditionState.UnclassifiedMembersPresent, snapshot.ActivationPreconditionState);
        Assert.False(snapshot.ReadinessForControlledValidation);
        Assert.Contains("vrss_gaze_provider", snapshot.UnclassifiedMembersPresent);
    }

    [Fact]
    public void SecondVrssInstanceBlocksActivation()
    {
        var first = Vrss();
        var second = Vrss(stabilityKey: "second", sha256: "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");

        var snapshot = Evaluate(
            PersistentSample(includeLauncher: false, extra: [first, second]),
            PersistentSample(includeLauncher: false, extra: [first, second]),
            PersistentSample(includeLauncher: false, extra: [first, second]));

        Assert.Equal(PimaxShellActivationPreconditionState.UnclassifiedMembersPresent, snapshot.ActivationPreconditionState);
        Assert.Contains("vrss_gaze_provider", snapshot.UnclassifiedMembersPresent);
    }

    [Fact]
    public void VrssHashChangeDuringSamplingIsUnstable()
    {
        var snapshot = Evaluate(
            PersistentSample(includeLauncher: false, extra: [Vrss(sha256: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]),
            PersistentSample(includeLauncher: false, extra: [Vrss(sha256: "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]),
            PersistentSample(includeLauncher: false, extra: [Vrss(sha256: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]));

        Assert.Equal(PimaxShellActivationPreconditionState.Incomplete, snapshot.ActivationPreconditionState);
        Assert.False(snapshot.Stable);
    }

    [Fact]
    public void SupervisorAliasesAreExcludedButUntrustedLookalikeIsNot()
    {
        var protectedAliases = new[]
        {
            Process("PimaxVrcSupervisor", "unknown", "unknown", path: @"C:\Users\operator\Documents\PimaxVrcSupervisor-TestDeployments\Phase29DE-ac140b5\PimaxVrcSupervisor.exe"),
            Process("PimaxVrcSupervisorConfigurator", "unknown", "unknown", path: @"C:\Users\operator\Documents\PimaxVrcSupervisor-TestDeployments\Phase29DE-ac140b5\PimaxVrcSupervisorConfigurator.exe"),
            Process("PimaxVrcSupervisorSteamVrHost", "unknown", "unknown", path: @"C:\Users\operator\Documents\PimaxVrcSupervisor-TestDeployments\Phase29DE-ac140b5\PimaxVrcSupervisorSteamVrHost.exe"),
            Process("PimaxVrcSupervisorTui", "unknown", "unknown", path: @"C:\Users\operator\Documents\PimaxVrcSupervisor-TestDeployments\Phase29DE-ac140b5\PimaxVrcSupervisorTui.exe")
        };
        var accepted = Evaluate(
            PersistentSample(includeLauncher: false, extra: protectedAliases),
            PersistentSample(includeLauncher: false, extra: protectedAliases),
            PersistentSample(includeLauncher: false, extra: protectedAliases));

        var lookalike = Process("PimaxVrcSupervisorWatcher", "unknown", "unknown", path: @"C:\Temp\PimaxVrcSupervisorWatcher.exe");
        var refused = Evaluate(
            PersistentSample(includeLauncher: false, extra: [lookalike]),
            PersistentSample(includeLauncher: false, extra: [lookalike]),
            PersistentSample(includeLauncher: false, extra: [lookalike]));

        Assert.True(accepted.ReadinessForControlledValidation);
        Assert.Equal(PimaxShellActivationPreconditionState.UnclassifiedMembersPresent, refused.ActivationPreconditionState);
        Assert.Contains("PimaxVrcSupervisorWatcher", refused.UnclassifiedMembersPresent);
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
        string stabilityKey = "stable",
        string? path = null,
        string session = "unknown",
        string signature = "unavailable",
        string sha256 = "unavailable",
        string pathClassification = "unknown",
        string provenanceSource = "notApplicable",
        string confidence = "none",
        string parentState = "unknown",
        string serviceIdentity = "none",
        string servicePath = "unknown",
        string serviceSigner = "unknown",
        string reason = "notClassified")
        => new(
            name,
            path ?? "<pimax>\\Runtime\\" + name + ".exe",
            ownership,
            creator,
            service,
            root,
            name + "-" + stabilityKey,
            session,
            signature,
            sha256,
            FileSizeBytes: sha256 == "unavailable" ? 0 : 123456,
            FileCreatedUtc: "2026-06-21T00:00:00.0000000Z",
            FileWrittenUtc: "2026-06-21T00:00:00.0000000Z",
            pathClassification,
            provenanceSource,
            confidence,
            parentState,
            serviceIdentity,
            servicePath,
            serviceSigner,
            reason);

    private static PimaxShellQuiescenceProcessSnapshot Vrss(
        string ownership = "persistentServiceDescendant",
        string creator = "persistentServiceDescendantFromPreservedEvidence",
        string confidence = "probable",
        string servicePath = "expectedPiServiceLauncherPath",
        string serviceSigner = "trustedSignedExpectedLauncher",
        string session = "session0",
        string pathClassification = "exactExpectedRuntimePath",
        string stabilityKey = "stable",
        string sha256 = "829327485C0B4B09CBF75F5FAE5E3AB5FC0D13FCFB7E273C682495094E6186CF")
        => Process(
            "vrss_gaze_provider",
            ownership,
            creator,
            service: "PiServiceLauncher",
            root: pathClassification == "exactExpectedRuntimePath" ? "expectedPimaxRuntimeRoot" : "duplicateOrUnexpectedPimaxRoot",
            stabilityKey: stabilityKey,
            path: pathClassification == "exactExpectedRuntimePath" ? @"C:\Program Files\Pimax\Runtime\vrss_gaze_provider.exe" : @"C:\Temp\vrss_gaze_provider.exe",
            session: session,
            signature: "unsigned",
            sha256: sha256,
            pathClassification: pathClassification,
            provenanceSource: confidence == "probable" ? "machineLocalServiceConfigurationAndOperatorConfirmedPhaseEvidence" : "liveParentProcessAndServiceTable",
            confidence: confidence,
            parentState: confidence == "confirmed" ? "parentPresent" : "parentExitedOrUnavailable",
            serviceIdentity: "PiServiceLauncher",
            servicePath: servicePath,
            serviceSigner: serviceSigner,
            reason: "synthetic VRSS service descendant evidence");

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
