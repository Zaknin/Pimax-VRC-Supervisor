using Microsoft.Win32.SafeHandles;
using PimaxVrcSupervisor.Updates;
using Xunit;
using Xunit.Abstractions;

[Collection(ControlledNativeValidationCollection.Name)]
public sealed class HardenedUpdateStagingStoreNativeMatrixTests(ITestOutputHelper output)
{
    private const string Version = "1.4.0";
    private const string Variant = "win-x64-with-dotnet9";
    private const string PartialName = "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip.partial";
    private const string FinalName = "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip";

    [Fact]
    public void OrdinarySuccessStreamsFlushesPromotesWithoutOverwriteAndPreservesIdentity()
        => RunCase("ORDINARY_SUCCESS", () =>
        {
            using var workspace = new ControlledNativeStagingWorkspace();
            using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
            HardenedPackageDirectory? directory = null;
            try
            {
                directory = store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9);
                using var partial = directory.CreateNewFile(PartialName, write: true);
                var identityBefore = directory.CaptureFileIdentity(partial);
                var payload = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
                for (var offset = 0; offset < payload.Length; offset += 512)
                {
                    RandomAccess.Write(partial, payload.AsSpan(offset, Math.Min(512, payload.Length - offset)), offset);
                }
                directory.FlushPinnedFile(partial);
                Assert.Equal(payload.Length, directory.GetPinnedFileLength(partial));

                directory.RenamePinnedFile(partial, FinalName);
                directory.FlushPinnedDirectory();
                Assert.Equal(identityBefore, directory.CaptureFileIdentity(partial));
                directory.VerifyFinalFilePath(partial, FinalName, "native_matrix");
                Assert.Equal(payload.Length, directory.GetPinnedFileLength(partial));
                Assert.Throws<NativeStagingException>(() => directory.CreateNewFile(FinalName, write: true).Dispose());
                partial.Dispose();
                using (var reopened = directory.OpenExistingFile(FinalName, write: false))
                {
                    Assert.Equal(payload, NativeMatrixExtensions.ReadAllBytes(reopened, payload.Length));
                }
                Assert.True(directory.TryDeleteExactFile(FinalName));
                Assert.False(directory.TryDeleteExactFile(FinalName));
            }
            finally
            {
                directory?.Dispose();
                workspace.DisposeProductionHierarchy(Version, Variant, PartialName, FinalName);
            }
        });

    [Fact]
    public void IntermediateJunctionAndSymbolicLinkAreRejectedWithoutTargetTraversal()
        => RunCase("REPARSE_INTERMEDIATE", () =>
        {
            using (var workspace = new ControlledNativeStagingWorkspace())
            {
                workspace.CreateDirectory("junction-target");
                workspace.CreateMountPoint(workspace.PathFor("junction-target"), "Packages");
                using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
                var exception = Assert.Throws<UpdateContractException>(() => store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9));
                Assert.Equal("package_reparse", exception.Code);
                Assert.Empty(workspace.Enumerate("junction-target"));
            }

            using (var workspace = new ControlledNativeStagingWorkspace())
            {
                workspace.CreateDirectory("symbolic-target");
                workspace.CreateRelativeSymbolicLink("symbolic-target", "Packages");
                using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
                var exception = Assert.Throws<UpdateContractException>(() => store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9));
                Assert.Equal("package_reparse", exception.Code);
                Assert.Empty(workspace.Enumerate("symbolic-target"));
            }
        });

    [Fact]
    public void FinalJunctionAndSymbolicLinkAreRejectedWithoutTargetTraversal()
        => RunCase("REPARSE_FINAL", () =>
        {
            using (var workspace = new ControlledNativeStagingWorkspace())
            {
                workspace.CreateDirectory("Packages");
                workspace.CreateDirectory("Packages", Version);
                workspace.CreateDirectory("target");
                workspace.CreateMountPoint(workspace.PathFor("target"), "Packages", Version, Variant);
                using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
                var exception = Assert.Throws<UpdateContractException>(() => store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9));
                Assert.Equal("package_reparse", exception.Code);
                Assert.Empty(workspace.Enumerate("target"));
            }

            using (var workspace = new ControlledNativeStagingWorkspace())
            {
                workspace.CreateDirectory("Packages");
                workspace.CreateDirectory("Packages", Version);
                workspace.CreateDirectory("Packages", Version, "target");
                workspace.CreateRelativeSymbolicLink("target", "Packages", Version, Variant);
                using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
                var exception = Assert.Throws<UpdateContractException>(() => store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9));
                Assert.Equal("package_reparse", exception.Code);
                Assert.Empty(workspace.Enumerate("Packages", Version, "target"));
            }
        });

    [Fact]
    public void IntermediateUnsupportedReparseTagIsRejectedWithoutTraversal()
        => AssertUnsupportedReparseTagRejected(finalSlot: false);

    [Fact]
    public void FinalUnsupportedReparseTagIsRejectedWithoutTraversal()
        => AssertUnsupportedReparseTagRejected(finalSlot: true);

    private void AssertUnsupportedReparseTagRejected(bool finalSlot)
        => RunCase(finalSlot ? "REPARSE_UNSUPPORTED_FINAL" : "REPARSE_UNSUPPORTED_INTERMEDIATE", () =>
        {
            using var workspace = new ControlledNativeStagingWorkspace();
            if (finalSlot)
            {
                workspace.CreateDirectory("Packages");
                workspace.CreateDirectory("Packages", Version);
                _ = workspace.CreateUnsupportedReparsePoint("Packages", Version, Variant);
            }
            else
            {
                _ = workspace.CreateUnsupportedReparsePoint("Packages");
            }

            using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
            var failure = Record.Exception(() =>
                store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9).Dispose());
            store.Dispose();
            _ = workspace.QuarantineIntactThroughRecoveryContract();
            var exception = Assert.IsType<UpdateContractException>(failure);
            Assert.Equal("package_reparse", exception.Code);
        });

    [Fact]
    public void DirectoryComponentAndPackageFileTypeConfusionFailClosed()
        => RunCase("COLLISION_TYPE_CONFUSION", () =>
        {
            using (var workspace = new ControlledNativeStagingWorkspace())
            {
                workspace.CreateFile([1], "Packages");
                using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
                var exception = Assert.Throws<UpdateContractException>(() => store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9));
                Assert.Equal("package_reparse", exception.Code);
            }

            using (var workspace = new ControlledNativeStagingWorkspace())
            {
                workspace.CreateDirectory("Packages");
                workspace.CreateDirectory("Packages", Version);
                workspace.CreateDirectory("Packages", Version, Variant);
                workspace.CreateDirectory("Packages", Version, Variant, PartialName);
                using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
                using var directory = store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9);
                Assert.Throws<NativeStagingException>(() => directory.CreateNewFile(PartialName, write: true).Dispose());
                Assert.Throws<NativeStagingException>(() => directory.OpenExistingFile(PartialName, write: false).Dispose());
            }
        });

    [Fact]
    public void ExistingPartialFinalAndPromotionCollisionNeverOverwriteExistingBytesOrIdentity()
        => RunCase("COLLISION_NO_OVERWRITE", () =>
        {
            using var workspace = new ControlledNativeStagingWorkspace();
            workspace.CreateDirectory("Packages");
            workspace.CreateDirectory("Packages", Version);
            workspace.CreateDirectory("Packages", Version, Variant);
            var originalIdentity = workspace.CreateFile([9, 8, 7, 6], "Packages", Version, Variant, FinalName);
            workspace.CreateFile([5, 4, 3], "Packages", Version, Variant, PartialName);
            using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
            using var directory = store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9);

            Assert.Throws<NativeStagingException>(() => directory.CreateNewFile(PartialName, write: true).Dispose());
            using var final = directory.OpenExistingFile(FinalName, write: false);
            using var partial = directory.OpenExistingFile(PartialName, write: true);
            Assert.Throws<NativeStagingException>(() => directory.RenamePinnedFile(partial, FinalName));
            Assert.Equal(originalIdentity, directory.CaptureFileIdentity(final).ToValidationIdentity());
            Assert.Equal(new byte[] { 9, 8, 7, 6 }, NativeMatrixExtensions.ReadAllBytes(final, 4));
            Assert.Equal(3, directory.GetPinnedFileLength(partial));
        });

    [Fact]
    public void PinnedPartialAndPromotedHandleRejectReplacementAndRemainUsable()
        => RunCase("RACE_PARTIAL_AND_DESTINATION", () =>
        {
            using var workspace = new ControlledNativeStagingWorkspace();
            using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
            HardenedPackageDirectory? directory = null;
            try
            {
                directory = store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9);
                using var partial = directory.CreateNewFile(PartialName, write: true);
                RandomAccess.Write(partial, [1, 2, 3, 4], 0);
                directory.FlushPinnedFile(partial);
                var identity = directory.CaptureFileIdentity(partial);

                AssertReplacementDenied(workspace, PartialName);
                directory.RenamePinnedFile(partial, FinalName);
                AssertReplacementDenied(workspace, FinalName);
                Assert.Equal(identity, directory.CaptureFileIdentity(partial));
                Assert.Equal(4, directory.GetPinnedFileLength(partial));
                directory.DeletePinnedFile(partial, identity, FinalName);
            }
            finally
            {
                directory?.Dispose();
                workspace.DisposeProductionHierarchy(Version, Variant, PartialName, FinalName);
            }
        });

    [Fact]
    public void PinnedAncestorRejectsRenameAndPathReplacementBeforeFurtherMutation()
        => RunCase("RACE_ANCESTOR_IDENTITY", () =>
        {
            using var workspace = new ControlledNativeStagingWorkspace();
            using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
            HardenedPackageDirectory? directory = null;
            try
            {
                directory = store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9);
                var blocked = Assert.Throws<NativeValidationException>(() =>
                    NativeValidationFileSystem.OpenRelativeDirectoryForRecoveryRename(workspace.Run.RunHandleForTests, "staging").Dispose());
                Assert.Equal(unchecked((int)0xC0000043), blocked.Status);
                using var partial = directory.CreateNewFile(PartialName, write: true);
                var identity = directory.CaptureFileIdentity(partial);
                directory.DeletePinnedFile(partial, identity, PartialName);
            }
            finally
            {
                directory?.Dispose();
                workspace.DisposeProductionHierarchy(Version, Variant, PartialName, FinalName);
            }
        });

    [Fact]
    public void PinnedParentRejectsNewDeleteOpenAndLeavesSiblingParentAndUnrelatedObjectsUntouched()
        => RunCase("RIGHTS_CONTAINMENT", () =>
        {
            using var workspace = new ControlledNativeStagingWorkspace();
            _ = workspace.Run.CreateDirectoryForTests("sibling");
            var siblingIdentity = workspace.Run.InventoryIdentityForTests("sibling");
            var unrelatedPath = workspace.Run.WriteFile("unrelated.bin", "unchanged");
            using var store = new HardenedUpdateStagingStore(UpdatePackageVariant.WithDotnet9, workspace.UpdateRoot);
            HardenedPackageDirectory? directory = null;
            try
            {
                directory = store.OpenPackageDirectory(Version, UpdatePackageVariant.WithDotnet9);
                Assert.Throws<NativeValidationException>(() =>
                    NativeValidationFileSystem.OpenRelativeDirectoryForDeletion(workspace.Run.RunHandleForTests, "staging").Dispose());
                using var partial = directory.CreateNewFile(PartialName, write: true);
                RandomAccess.Write(partial, [42], 0);
                Assert.Throws<NativeValidationException>(() =>
                    NativeValidationFileSystem.OpenRelativeFileForDeletion(directory.Handle, PartialName).Dispose());
                var identity = directory.CaptureFileIdentity(partial);
                directory.DeletePinnedFile(partial, identity, PartialName);
                Assert.Equal(siblingIdentity, workspace.Run.InventoryIdentityForTests("sibling"));
                Assert.Equal("unchanged", File.ReadAllText(unrelatedPath));
            }
            finally
            {
                directory?.Dispose();
                workspace.DisposeProductionHierarchy(Version, Variant, PartialName, FinalName);
            }
        });

    private void RunCase(string name, Action action)
    {
        Assert.True(OperatingSystem.IsWindows(), $"ENVIRONMENT BLOCKED: {name} requires Windows.");
        try
        {
            action();
            output.WriteLine($"PASS: {name}");
        }
        catch (Exception exception)
        {
            output.WriteLine($"FAIL: {name}: {exception}");
            throw;
        }
    }

    private static void AssertReplacementDenied(ControlledNativeStagingWorkspace workspace, string fileName)
    {
        using var packages = NativeValidationFileSystem.OpenRelativeDirectory(workspace.Run.RunHandleForTests, "staging", writable: true);
        using var versions = NativeValidationFileSystem.OpenRelativeDirectory(packages.Handle, "Packages", writable: true);
        using var version = NativeValidationFileSystem.OpenRelativeDirectory(versions.Handle, Version, writable: true);
        using var variant = NativeValidationFileSystem.OpenRelativeDirectory(version.Handle, Variant, writable: true);
        Assert.Throws<NativeValidationException>(() => NativeValidationFileSystem.OpenRelativeFileForDeletion(variant.Handle, fileName).Dispose());
    }
}

internal static class NativeMatrixExtensions
{
    internal static byte[] ReadAllBytes(SafeFileHandle handle, int length)
    {
        var bytes = new byte[length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);
            if (read == 0)
            {
                throw new EndOfStreamException("A native matrix file ended before the expected length.");
            }
            offset += read;
        }
        return bytes;
    }

    internal static NativeValidationFileIdentity ToValidationIdentity(this NativeFileIdentity identity)
        => new(identity.VolumeSerialNumber, identity.FileIndex, identity.Attributes, identity.ReparseTag);
}
