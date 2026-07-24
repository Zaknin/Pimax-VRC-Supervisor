using PimaxVrcSupervisor.Updates;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

public sealed class HardenedUpdateStagingStoreTests
{
    [Fact]
    public void PinnedPackageDirectoryCreatesExclusiveOrdinaryPartial()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Handle-relative NT staging tests require Windows.");
        }

        using var temp = new TempDirectory();
        var updateRoot = Path.Combine(temp.Path, "Update");
        var packageDirectory = Path.Combine(updateRoot, "Packages", "1.4.0", "win-x64-with-dotnet9");
        using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, updateRoot);
        using var directory = store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9);
        using var partial = directory.CreateNewFile("PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip.partial", write: true);
        using var stream = new FileStream(partial, FileAccess.Write, 4096, isAsync: false);

        stream.Write([1, 2, 3, 4]);
        stream.Flush(flushToDisk: true);

        Assert.Throws<NativeStagingException>(() =>
            directory.CreateNewFile("PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip.partial", write: true).Dispose());
    }

    [Fact]
    public void PinnedPartialPromotesByHandleWithoutReplacingExistingFinal()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Handle-relative NT staging tests require Windows.");
        }

        using var temp = new TempDirectory();
        var updateRoot = Path.Combine(temp.Path, "Update");
        var packagePath = Path.Combine(updateRoot, "Packages", "1.4.0", "win-x64-with-dotnet9");
        Directory.CreateDirectory(packagePath);
        ApplyProtectedCurrentUserAcl(updateRoot);
        ApplyProtectedCurrentUserAcl(Path.Combine(updateRoot, "Packages"));
        ApplyProtectedCurrentUserAcl(Path.Combine(updateRoot, "Packages", "1.4.0"));
        ApplyProtectedCurrentUserAcl(packagePath);
        const string partialName = "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip.partial";
        const string finalName = "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip";
        using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, updateRoot);
        using var directory = store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9);
        using var partial = directory.CreateNewFile(partialName, write: true);
        RandomAccess.Write(partial, [1, 2, 3, 4], 0);

        directory.RenamePinnedFile(partial, finalName);

        Assert.True(File.Exists(Path.Combine(packagePath, finalName)));
        Assert.Throws<NativeStagingException>(() => directory.CreateNewFile(finalName, write: true).Dispose());
    }

    [Fact]
    public void RecoveryReadPinsThePackageAgainstConcurrentWriteAndDeleteOpens()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Handle-relative NT staging tests require Windows.");
        }

        using var temp = new TempDirectory();
        var updateRoot = Path.Combine(temp.Path, "Update");
        const string finalName = "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip";
        using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, updateRoot);
        using var directory = store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9);
        using (var created = directory.CreateNewFile(finalName, write: true))
        {
            RandomAccess.Write(created, [1, 2, 3, 4], 0);
            directory.FlushPinnedFile(created);
        }

        using var recovery = directory.TryOpenExistingFileForRecovery(finalName);

        Assert.NotNull(recovery);
        var finalPath = Path.Combine(updateRoot, "Packages", "1.4.0", "win-x64-with-dotnet9", finalName);
        Assert.Throws<IOException>(() =>
            new FileStream(finalPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());
    }

    [Fact]
    public void HardLinkedPackageFileFailsClosedBeforeItCanBePinned()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Hard-link staging tests require Windows.");
        }

        using var temp = new TempDirectory();
        const string partialName = "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip.partial";
        const string finalName = "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip";
        using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, Path.Combine(temp.Path, "Update"));
        using var directory = store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9);
        using (directory.CreateNewFile(partialName, write: true)) { }
        var partialPath = Path.Combine(temp.Path, "Update", "Packages", "1.4.0", "win-x64-with-dotnet9", partialName);
        var finalPath = Path.Combine(temp.Path, "Update", "Packages", "1.4.0", "win-x64-with-dotnet9", finalName);
        if (!CreateHardLink(finalPath, partialPath, IntPtr.Zero))
        {
            throw new InvalidOperationException("The controlled hard-link fixture could not be created.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }

        var exception = Assert.Throws<UpdateContractException>(() => directory.TryOpenExistingFile(partialName, write: false));

        Assert.Equal("package_reparse", exception.Code);
        Assert.True(File.Exists(finalPath));
    }

    [Fact]
    public void ExistingIntermediateJunctionFailsClosedBeforePackageDirectoryUse()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Reparse-point staging tests require Windows.");
        }

        using var run = ControlledNativeValidationRun.Create();
        var updateRoot = run.CreateDirectoryForTests("Update");
        ApplyProtectedCurrentUserAcl(updateRoot);
        var outside = run.CreateDirectoryForTests("outside");
        using var update = NativeValidationFileSystem.OpenRelativeDirectory(run.RunHandleForTests, "Update", writable: true);
        using var packages = NativeValidationFileSystem.CreateRelativeMountPointObject(update.Handle, "Packages");
        NativeValidationFileSystem.AssignMountPoint(packages, outside);
        var identity = NativeValidationFileSystem.ReadIdentity(packages);
        var reparsePoint = NativeValidationFileSystem.ReadReparsePoint(packages, identity);
        var finalPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(packages));
        try
        {
            using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, updateRoot);

            var exception = Assert.Throws<UpdateContractException>(() =>
                store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9));

            Assert.Equal("package_reparse", exception.Code);
            Assert.Empty(Directory.GetFileSystemEntries(outside));
        }
        finally
        {
            NativeValidationFileSystem.DeleteReparsePoint(packages, new NativeValidationInventoryItem(
                "Packages", identity, finalPath, NativeValidationInventoryItemKind.ReparsePoint, reparsePoint));
            NativeValidationFileSystem.MarkForDelete(packages);
        }
    }

    [Theory]
    [InlineData(2u, "NTFS", "NTFS", true, "removable_or_remote")]
    [InlineData(4u, "NTFS", "NTFS", true, "remote")]
    [InlineData(5u, "NTFS", "NTFS", true, "optical")]
    [InlineData(6u, "NTFS", "NTFS", true, "ram_disk")]
    [InlineData(0u, "NTFS", "NTFS", true, "unknown")]
    [InlineData(3u, "ReFS", "ReFS", true, "unsupported_filesystem")]
    [InlineData(3u, "NTFS", "ReFS", true, "handle_filesystem_mismatch")]
    [InlineData(3u, "NTFS", "NTFS", false, "query_failure")]
    public void UnsupportedOrAmbiguousVolumeCapabilityFailsClosed(
        uint driveType,
        string fileSystem,
        string handleFileSystem,
        bool querySucceeded,
        string _)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Native staging capability tests require Windows.");
        }

        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "Update");
        using var inspector = Native.PushVolumeInspectorForTests(new DelegatingInspector((_, volumeRoot, handle) =>
        {
            var identity = Native.ReadIdentity(handle, "test_volume");
            return new StagingVolumeCapability(querySucceeded, driveType, volumeRoot, "\\\\?\\Volume{test}\\", fileSystem, identity.VolumeSerialNumber, identity.VolumeSerialNumber, handleFileSystem);
        }));
        using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, root);

        var exception = Assert.Throws<UpdateContractException>(() => store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9));

        Assert.Equal("package_volume", exception.Code);
    }

    [Fact]
    public void PathAndHandleVolumeIdentityMismatchFailsClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Native staging capability tests require Windows.");
        }

        using var temp = new TempDirectory();
        using var inspector = Native.PushVolumeInspectorForTests(new DelegatingInspector((_, volumeRoot, handle) =>
        {
            var identity = Native.ReadIdentity(handle, "test_volume");
            return new StagingVolumeCapability(true, 3, volumeRoot, "\\\\?\\Volume{test}\\", "NTFS", identity.VolumeSerialNumber, identity.VolumeSerialNumber + 1, "NTFS");
        }));
        using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, Path.Combine(temp.Path, "Update"));

        var exception = Assert.Throws<UpdateContractException>(() => store.OpenPackageDirectory("1.4.0", UpdatePackageVariant.WithDotnet9));

        Assert.Equal("package_volume", exception.Code);
    }

    [Fact]
    public void RelativeStagingRootFailsBeforeAnyFilesystemMutation()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Native staging capability tests require Windows.");
        }

        var exception = Assert.Throws<UpdateContractException>(() =>
            new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, "relative-update-root"));

        Assert.Equal("package_root", exception.Code);
    }

    private sealed class DelegatingInspector(Func<string, string, Microsoft.Win32.SafeHandles.SafeFileHandle, StagingVolumeCapability> inspect) : IStagingVolumeInspector
    {
        public StagingVolumeCapability Inspect(string updateDirectory, string root, Microsoft.Win32.SafeHandles.SafeFileHandle rootHandle)
            => inspect(updateDirectory, root, rootHandle);
    }

    private static void ApplyProtectedCurrentUserAcl(string path)
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
