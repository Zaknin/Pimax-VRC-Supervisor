using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using PimaxVrcSupervisor.Updates;
using Xunit;

[Collection(ControlledNativeValidationCollection.Name)]
public sealed class PinnedDurableMetadataTests
{
    [Fact]
    public async Task CorruptCurrentRecoversOnlyThePinnedValidPreviousCopyWithoutDeletingEvidence()
    {
        RequireWindows();
        using var workspace = new ControlledNativeStagingWorkspace();
        ApplyCompliantDacl(workspace.UpdateRoot);
        using var staging = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
        using var directory = staging.OpenMetadataDirectory();

        await PinnedDurableMetadata.WriteAsync(directory, "current.json", "previous.json", Utf8("one"), 1024, CancellationToken.None);
        await PinnedDurableMetadata.WriteAsync(directory, "current.json", "previous.json", Utf8("two"), 1024, CancellationToken.None);
        using (var current = directory.OpenExistingFile("current.json", write: true))
        {
            RandomAccess.Write(current, Utf8("corrupt").Span, 0);
            directory.FlushPinnedFile(current);
        }

        var loaded = PinnedDurableMetadata.Load(directory, "current.json", "previous.json", 1024, ValidateValue);

        Assert.Equal("one", loaded.Value);
        Assert.True(loaded.UsedPrevious);
        Assert.True(loaded.CurrentInvalid);
        Assert.True(File.Exists(Path.Combine(workspace.UpdateRoot, "current.json")));
        Assert.True(File.Exists(Path.Combine(workspace.UpdateRoot, "previous.json")));
        workspace.AdoptExpectedFile("current.json");
        workspace.AdoptExpectedFile("previous.json");
    }

    [Fact]
    public async Task CrashAfterPreviousPreparationIsIdempotentlyRecoverableAndPreservesCurrentAbsence()
    {
        RequireWindows();
        using var workspace = new ControlledNativeStagingWorkspace();
        ApplyCompliantDacl(workspace.UpdateRoot);
        using var staging = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
        using var directory = staging.OpenMetadataDirectory();
        await PinnedDurableMetadata.WriteAsync(directory, "current.json", "previous.json", Utf8("one"), 1024, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => PinnedDurableMetadata.WriteAsync(
            directory,
            "current.json",
            "previous.json",
            Utf8("two"),
            1024,
            CancellationToken.None,
            afterPreviousPreparation: () => throw new InvalidOperationException("test crash")));

        var first = PinnedDurableMetadata.Load(directory, "current.json", "previous.json", 1024, ValidateValue);
        var second = PinnedDurableMetadata.Load(directory, "current.json", "previous.json", 1024, ValidateValue);
        Assert.Equal("one", first.Value);
        Assert.Equal(first, second);
        Assert.True(first.UsedPrevious);
        Assert.False(File.Exists(Path.Combine(workspace.UpdateRoot, "current.json")));
        Assert.True(File.Exists(Path.Combine(workspace.UpdateRoot, "previous.json")));
        workspace.AdoptExpectedFile("previous.json");
    }

    private static string ValidateValue(ReadOnlyMemory<byte> bytes)
    {
        var value = Encoding.UTF8.GetString(bytes.Span);
        if (value is not ("one" or "two"))
        {
            throw new UpdateContractException("test_metadata", "Invalid test metadata.");
        }

        return value;
    }

    private static ReadOnlyMemory<byte> Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static void ApplyCompliantDacl(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var currentUser = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(currentUser);
        security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Pinned durable metadata tests require Windows NTFS.");
        }
    }
}
