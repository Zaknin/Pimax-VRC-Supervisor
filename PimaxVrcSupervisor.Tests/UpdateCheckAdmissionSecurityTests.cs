using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class UpdateCheckAdmissionSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProductionMutexUsesProtectedGlobalSidOwnerAndAclWithoutFallback()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor",
            "UpdateCheckExclusion.cs"));

        Assert.Contains("Global\\PimaxVrcSupervisor.UpdateCheck.", source, StringComparison.Ordinal);
        Assert.Contains("WindowsIdentity.GetCurrent().User", source, StringComparison.Ordinal);
        Assert.Contains("SetOwner(sid)", source, StringComparison.Ordinal);
        Assert.Contains("SetAccessRuleProtection(isProtected: true, preserveInheritance: false)", source, StringComparison.Ordinal);
        Assert.Contains("MutexAcl.Create", source, StringComparison.Ordinal);
        Assert.Contains("MutexAcl.OpenExisting", source, StringComparison.Ordinal);
        Assert.Contains("ThreadingAclExtensions.GetAccessControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new MutexSecurity(_mutexName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new MutexSecurity(mutexName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Local\\PimaxVrcSupervisor.UpdateCheck.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Mutex(initiallyOwned: false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdmissionFailsClosedWhenSecureMutexCannotBeEstablished()
    {
        var admission = new UpdateCheckAdmission(new ThrowingUpdateCheckExclusion());

        var result = admission.TryAcquire();

        Assert.Equal(UpdateCheckAdmissionStatus.Unavailable, result.Status);
        Assert.Null(result.Lease);
        Assert.Equal("gate_unavailable", result.ErrorCode);
        Assert.False(admission.IsActive);
    }

    [Fact]
    public void MutexIdentityIsStableBoundedGlobalAndSidDerived()
    {
        var currentSid = CurrentSid();
        var otherSid = DifferentSid(currentSid);

        var first = UserScopedUpdateCheckExclusion.BuildMutexName(currentSid);
        var second = UserScopedUpdateCheckExclusion.BuildMutexName(currentSid);
        var other = UserScopedUpdateCheckExclusion.BuildMutexName(otherSid);

        Assert.StartsWith(@"Global\PimaxVrcSupervisor.UpdateCheck.", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.True(first.Length <= 128);
        Assert.DoesNotContain(currentSid.Value, first, StringComparison.Ordinal);
    }

    [Fact]
    public void CreationDescriptorUsesExpectedOwnerAndExactProtectedBoundedSidOnlyDacl()
    {
        var currentSid = CurrentSid();
        var security = UserScopedUpdateCheckExclusion.BuildMutexSecurity(currentSid);

        UserScopedUpdateCheckExclusion.ValidateMutexSecurityDescriptor(security, currentSid);

        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(currentSid, security.GetOwner(typeof(SecurityIdentifier)));
        var rule = Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<MutexAccessRule>());
        Assert.False(rule.IsInherited);
        Assert.Equal(currentSid, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(UserScopedUpdateCheckExclusion.RequiredRights, rule.MutexRights);
    }

    [Fact]
    public void ValidationReadsOwnerAndAccessFromTheReturnedMutexHandle()
    {
        var currentSid = CurrentSid();
        var name = UniqueMutexName();
        using var mutex = MutexAcl.Create(
            initiallyOwned: false,
            name,
            out var createdNew,
            UserScopedUpdateCheckExclusion.BuildMutexSecurity(currentSid));
        Mutex? inspected = null;

        UserScopedUpdateCheckExclusion.ValidateMutexSecurity(
            mutex,
            currentSid,
            returnedHandle =>
            {
                inspected = returnedHandle;
                return returnedHandle.GetAccessControl();
            });

        Assert.True(createdNew);
        Assert.Same(mutex, inspected);
    }

    [Fact]
    public void NormallyCreatedLiveMutexReportsExpectedCurrentUserOwner()
    {
        var currentSid = CurrentSid();
        var name = UniqueMutexName();
        var exclusion = UserScopedUpdateCheckExclusion.ForMutexName(name, currentSid);
        using var lease = exclusion.TryAcquire();
        using var observer = MutexAcl.OpenExisting(name, UserScopedUpdateCheckExclusion.RequiredRights);
        var security = observer.GetAccessControl();

        Assert.NotNull(lease);
        Assert.Equal(currentSid, security.GetOwner(typeof(SecurityIdentifier)));
        UserScopedUpdateCheckExclusion.ValidateMutexSecurity(observer, currentSid);
    }

    [Fact]
    public void ExactExpectedSidDaclWithForeignOwnerIsRejected()
    {
        var expectedSid = CurrentSid();
        var security = UserScopedUpdateCheckExclusion.BuildMutexSecurity(expectedSid);
        security.SetOwner(DifferentSid(expectedSid));

        Assert.Throws<UnauthorizedAccessException>(() =>
            UserScopedUpdateCheckExclusion.ValidateMutexSecurityDescriptor(security, expectedSid));
    }

    [Fact]
    public void OwnerLookupFailureFailsClosed()
    {
        var expectedSid = CurrentSid();
        var security = UserScopedUpdateCheckExclusion.BuildMutexSecurity(expectedSid);

        Assert.Throws<UnauthorizedAccessException>(() =>
            UserScopedUpdateCheckExclusion.ValidateMutexSecurityDescriptor(
                security,
                expectedSid,
                _ => throw new InvalidOperationException("owner lookup failed")));
    }

    [Fact]
    public void MissingOwnerFailsClosed()
    {
        var expectedSid = CurrentSid();
        var security = UserScopedUpdateCheckExclusion.BuildMutexSecurity(expectedSid);

        Assert.Throws<UnauthorizedAccessException>(() =>
            UserScopedUpdateCheckExclusion.ValidateMutexSecurityDescriptor(security, expectedSid, _ => null));
    }

    [Fact]
    public void MalformedNonSidOwnerFailsClosed()
    {
        var expectedSid = CurrentSid();
        var security = UserScopedUpdateCheckExclusion.BuildMutexSecurity(expectedSid);

        Assert.Throws<UnauthorizedAccessException>(() =>
            UserScopedUpdateCheckExclusion.ValidateMutexSecurityDescriptor(
                security,
                expectedSid,
                _ => new NTAccount("BUILTIN", "Users")));
    }

    [Fact]
    public async Task ForeignOwnerRejectionCreatesNoOperationRequestStateMutationOrQueuedExecution()
    {
        using var temp = new TempDirectory();
        var currentSid = CurrentSid();
        var foreignDescriptor = UserScopedUpdateCheckExclusion.BuildMutexSecurity(currentSid);
        foreignDescriptor.SetOwner(DifferentSid(currentSid));
        var exclusion = UserScopedUpdateCheckExclusion.ForMutexName(
            UniqueMutexName(),
            currentSid,
            _ => foreignDescriptor);
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var initialState = store.Load().State;
        var client = new FakeUpdateDiscoveryClient(UpdateDiscoveryCheckResult.Failure(
            UpdateErrorCategory.Http,
            "must_not_run"));
        var clock = new ManualUpdateScheduleClock(Now);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock, checkExclusion: exclusion);
        using var coordinator = new SupervisorUpdateCoordinator(
            SupervisorUpdatePolicy.Disabled,
            store,
            scheduler,
            clock,
            verificationConfigured: true);

        var first = coordinator.TryStartManualCheck(CancellationToken.None);
        await Task.Delay(50);
        var second = coordinator.TryStartManualCheck(CancellationToken.None);

        Assert.False(first.Accepted);
        Assert.False(first.AlreadyInProgress);
        Assert.Null(first.OperationId);
        Assert.Equal("gate_unavailable", first.ResultCode);
        Assert.False(second.Accepted);
        Assert.False(second.AlreadyInProgress);
        Assert.Null(second.OperationId);
        Assert.Equal("gate_unavailable", second.ResultCode);
        Assert.Null(coordinator.GetStatus().Operation);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(initialState, store.Load().State);
    }

    [Fact]
    public void BroadLiveAclFailsClosedWithoutRepair()
    {
        var currentSid = CurrentSid();
        var name = UniqueMutexName();
        var broadSecurity = Descriptor(
            currentSid,
            isProtected: true,
            new MutexAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                MutexRights.FullControl,
                AccessControlType.Allow));
        using var squatted = MutexAcl.Create(false, name, out var createdNew, broadSecurity);
        var before = squatted.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var admission = new UpdateCheckAdmission(UserScopedUpdateCheckExclusion.ForMutexName(name, currentSid));

        var result = admission.TryAcquire();
        var after = squatted.GetAccessControl().GetSecurityDescriptorBinaryForm();

        Assert.True(createdNew);
        Assert.Equal(UpdateCheckAdmissionStatus.Unavailable, result.Status);
        Assert.Equal("gate_unavailable", result.ErrorCode);
        Assert.Null(result.Lease);
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(InvalidDaclKind.Everyone)]
    [InlineData(InvalidDaclKind.AuthenticatedUsers)]
    [InlineData(InvalidDaclKind.ForeignSid)]
    [InlineData(InvalidDaclKind.GroupSid)]
    [InlineData(InvalidDaclKind.Deny)]
    [InlineData(InvalidDaclKind.AdditionalAce)]
    [InlineData(InvalidDaclKind.Unprotected)]
    [InlineData(InvalidDaclKind.UnexpectedRights)]
    public void ExactDaclValidationRejectsEveryUnexpectedAclShape(InvalidDaclKind kind)
    {
        var expectedSid = CurrentSid();
        var descriptor = InvalidDescriptor(expectedSid, kind);

        Assert.Throws<UnauthorizedAccessException>(() =>
            UserScopedUpdateCheckExclusion.ValidateMutexSecurityDescriptor(descriptor, expectedSid));
    }

    [Fact]
    public void AutomaticBridgeAndWorkerUseTheSameAdmissionApi()
    {
        var root = RepositoryRoot();
        var scheduler = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "UpdateDiscoveryScheduler.cs"));
        var coordinator = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "SupervisorUpdateCoordinator.cs"));
        var worker = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "StandaloneUpdateCheckWorker.cs"));

        Assert.Contains("TryAcquireAdmission(UpdateCheckKind.Automatic)", scheduler, StringComparison.Ordinal);
        Assert.Contains("TryAcquireAdmission(UpdateCheckKind.Manual)", coordinator, StringComparison.Ordinal);
        Assert.Contains("TryAcquireAdmission(UpdateCheckKind.Worker)", worker, StringComparison.Ordinal);
        Assert.Contains("CheckAdmittedAsync", scheduler, StringComparison.Ordinal);
        Assert.Contains("CheckAdmittedAsync", coordinator, StringComparison.Ordinal);
        Assert.Contains("CheckAdmittedAsync", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateCheckSubprocessHelperIsNotAReleaseProject()
    {
        var packageScript = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "package-release.ps1"));

        Assert.DoesNotContain("PimaxVrcSupervisor.UpdateCheckTestHelper", packageScript, StringComparison.Ordinal);
        Assert.Contains(".\\PimaxVrcSupervisor\\PimaxVrcSupervisor.csproj", packageScript, StringComparison.Ordinal);
        Assert.Contains(".\\PimaxVrcSupervisor.ConfigEditor\\PimaxVrcSupervisor.ConfigEditor.csproj", packageScript, StringComparison.Ordinal);
        Assert.Contains(".\\PimaxVrcSupervisor.SteamVrHost\\PimaxVrcSupervisor.SteamVrHost.csproj", packageScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessLocalContentionDoesNotAttemptSecondCrossProcessAcquisition()
    {
        var exclusion = new CountingUpdateCheckExclusion();
        var admission = new UpdateCheckAdmission(exclusion);
        using var first = admission.TryAcquire().Lease;

        var second = admission.TryAcquire();

        Assert.NotNull(first);
        Assert.Equal(UpdateCheckAdmissionStatus.AlreadyRunning, second.Status);
        Assert.Equal(1, exclusion.AcquireCount);
    }

    private static MutexSecurity InvalidDescriptor(SecurityIdentifier expectedSid, InvalidDaclKind kind)
    {
        var expectedRule = new MutexAccessRule(
            expectedSid,
            UserScopedUpdateCheckExclusion.RequiredRights,
            AccessControlType.Allow);
        return kind switch
        {
            InvalidDaclKind.Everyone => Descriptor(
                expectedSid,
                true,
                new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), UserScopedUpdateCheckExclusion.RequiredRights, AccessControlType.Allow)),
            InvalidDaclKind.AuthenticatedUsers => Descriptor(
                expectedSid,
                true,
                new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), UserScopedUpdateCheckExclusion.RequiredRights, AccessControlType.Allow)),
            InvalidDaclKind.ForeignSid => Descriptor(
                expectedSid,
                true,
                new MutexAccessRule(DifferentSid(expectedSid), UserScopedUpdateCheckExclusion.RequiredRights, AccessControlType.Allow)),
            InvalidDaclKind.GroupSid => Descriptor(
                expectedSid,
                true,
                new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), UserScopedUpdateCheckExclusion.RequiredRights, AccessControlType.Allow)),
            InvalidDaclKind.Deny => Descriptor(
                expectedSid,
                true,
                new MutexAccessRule(expectedSid, UserScopedUpdateCheckExclusion.RequiredRights, AccessControlType.Deny)),
            InvalidDaclKind.AdditionalAce => Descriptor(
                expectedSid,
                true,
                expectedRule,
                new MutexAccessRule(expectedSid, MutexRights.ChangePermissions, AccessControlType.Allow)),
            InvalidDaclKind.Unprotected => Descriptor(expectedSid, false, expectedRule),
            InvalidDaclKind.UnexpectedRights => Descriptor(
                expectedSid,
                true,
                new MutexAccessRule(expectedSid, MutexRights.FullControl, AccessControlType.Allow)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static MutexSecurity Descriptor(
        SecurityIdentifier owner,
        bool isProtected,
        params MutexAccessRule[] rules)
    {
        var security = new MutexSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected, preserveInheritance: false);
        foreach (var rule in rules)
        {
            security.AddAccessRule(rule);
        }

        return security;
    }

    private static SecurityIdentifier CurrentSid()
        => WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("Current SID unavailable in test.");

    private static SecurityIdentifier DifferentSid(SecurityIdentifier current)
    {
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        return current.Equals(localSystem)
            ? new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null)
            : localSystem;
    }

    private static string UniqueMutexName()
        => @"Global\PimaxVrcSupervisor.UpdateCheck.Test."
            + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PimaxVrcSupervisor.Tests", "PimaxVrcSupervisor.Tests.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    public enum InvalidDaclKind
    {
        Everyone,
        AuthenticatedUsers,
        ForeignSid,
        GroupSid,
        Deny,
        AdditionalAce,
        Unprotected,
        UnexpectedRights
    }

    private sealed class ThrowingUpdateCheckExclusion : IUpdateCheckExclusion
    {
        public IUpdateCheckLease? TryAcquire() => throw new UnauthorizedAccessException("test");
    }

    private sealed class CountingUpdateCheckExclusion : IUpdateCheckExclusion
    {
        public int AcquireCount { get; private set; }

        public IUpdateCheckLease? TryAcquire()
        {
            AcquireCount++;
            return new Lease();
        }

        private sealed class Lease : IUpdateCheckLease
        {
            public void Dispose()
            {
            }
        }
    }
}
