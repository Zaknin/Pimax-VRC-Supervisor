using System.Security.AccessControl;
using System.Security.Principal;
using PimaxVrcSupervisor.Updates;
using Xunit;

[Collection(ControlledNativeValidationCollection.Name)]
public sealed class ProtectedStagingAclTests
{
    [Fact]
    public void CompliantCreationProtectsEveryPackageStagingRootAndCompliantReopenPreservesTheDescriptor()
    {
        RequireWindows();
        using var workspace = new ControlledNativeStagingWorkspace();
        var updateRoot = workspace.UpdateRoot;
        ApplyCompliantDacl(updateRoot);

        using (var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, updateRoot))
        using (store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9))
        {
        }
        workspace.AdoptExpectedDirectory("Packages", "1.4.0", "win-x64-with-dotnet9");
        workspace.AdoptExpectedDirectory("Packages", "1.4.0");
        workspace.AdoptExpectedDirectory("Packages");

        var roots = new[]
        {
            updateRoot,
            Path.Combine(updateRoot, "Packages"),
            Path.Combine(updateRoot, "Packages", "1.4.0"),
            Path.Combine(updateRoot, "Packages", "1.4.0", "win-x64-with-dotnet9")
        };
        var beforeReopen = roots.Select(ReadDescriptor).ToArray();
        Assert.All(beforeReopen, AssertCompliantDescriptor);

        using (var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, updateRoot))
        using (store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9))
        {
        }

        var afterReopen = roots.Select(ReadDescriptor).ToArray();
        Assert.Equal(beforeReopen, afterReopen);
    }

    [Fact]
    public void ExistingInheritedStagingAclFailsClosedWithoutRepair()
    {
        RequireWindows();
        using var workspace = new ControlledNativeStagingWorkspace();
        var updateRoot = workspace.UpdateRoot;
        ApplyCompliantDacl(updateRoot);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
        new DirectoryInfo(updateRoot).SetAccessControl(security);
        var before = ReadDescriptor(updateRoot);
        try
        {
            using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, updateRoot);
            var exception = Assert.Throws<UpdateContractException>(() =>
                store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9));

            Assert.Equal("package_acl", exception.Code);
            Assert.Equal(before, ReadDescriptor(updateRoot));
            Assert.False(Directory.Exists(Path.Combine(updateRoot, "Packages")));
        }
        finally
        {
            ApplyCompliantDacl(updateRoot);
        }
    }

    [Fact]
    public void PostValidationAclMutationBlocksFurtherFilesystemMutationAndPreservesTheMutatedDescriptor()
    {
        RequireWindows();
        using var workspace = new ControlledNativeStagingWorkspace();
        ApplyCompliantDacl(workspace.UpdateRoot);
        using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
        using var directory = store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9);
        var packageDirectory = Path.Combine(workspace.UpdateRoot, "Packages", "1.4.0", "win-x64-with-dotnet9");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
        new DirectoryInfo(packageDirectory).SetAccessControl(security);
        var before = ReadDescriptor(packageDirectory);
        try
        {
            var exception = Assert.Throws<UpdateContractException>(() =>
                directory.CreateNewFile("PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip.partial", write: true).Dispose());

            Assert.Equal("package_acl", exception.Code);
            Assert.Equal(before, ReadDescriptor(packageDirectory));
            Assert.Empty(Directory.EnumerateFiles(packageDirectory));
        }
        finally
        {
            directory.Dispose();
            ApplyCompliantDacl(packageDirectory);
            workspace.DisposeProductionHierarchy("1.4.0", "win-x64-with-dotnet9");
        }
    }

    private static string ReadDescriptor(string path)
    {
        const AccessControlSections sections = AccessControlSections.Owner | AccessControlSections.Access;
        return new DirectoryInfo(path).GetAccessControl(sections).GetSecurityDescriptorSddlForm(sections);
    }

    private static void ApplyCompliantDacl(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var currentUser = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(currentUser);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void AssertCompliantDescriptor(string descriptor)
    {
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm(descriptor);
        var currentUser = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(currentUser);
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(currentUser, security.GetOwner(typeof(SecurityIdentifier)));
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        var rule = Assert.Single(rules);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(currentUser, rule.IdentityReference);
        Assert.False(rule.IsInherited);
        Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
        Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, rule.InheritanceFlags);
        Assert.Equal(PropagationFlags.None, rule.PropagationFlags);
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Protected staging ACL tests require Windows NTFS.");
        }
    }
}
