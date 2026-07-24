using System.Runtime.CompilerServices;
using Xunit;

public sealed class ControlledNativeValidationRunSafetyTests
{
    [Fact]
    public void NativeLayerHasNoRecursiveCleanupOrTemporaryRootDependency()
    {
        var sourceDirectory = Path.GetDirectoryName(GetThisSourcePath());
        Assert.NotNull(sourceDirectory);

        var controlledSources = Directory
            .GetFiles(sourceDirectory!, "ControlledNativeValidation*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var nativeSource = File.ReadAllText(Path.Combine(sourceDirectory!, "ControlledNativeValidationRun.cs"));

        Assert.Contains("NativeValidationInventoryItemKind", nativeSource, StringComparison.Ordinal);
        Assert.Contains("NtCreateFile", nativeSource, StringComparison.Ordinal);
        Assert.Contains("NtQueryDirectoryFile", nativeSource, StringComparison.Ordinal);
        Assert.Contains("NtSetInformationFile", nativeSource, StringComparison.Ordinal);
        Assert.Contains("RenameRelativeNoReplace", nativeSource, StringComparison.Ordinal);
        Assert.Contains("ReplaceIfExists = FALSE", nativeSource, StringComparison.Ordinal);
        Assert.Contains("NtFlushBuffersFile", nativeSource, StringComparison.Ordinal);
        Assert.Contains("FsctlGetReparsePoint", nativeSource, StringComparison.Ordinal);
        Assert.Contains("FsctlSetReparsePoint", nativeSource, StringComparison.Ordinal);
        Assert.Contains("FsctlDeleteReparsePoint", nativeSource, StringComparison.Ordinal);
        Assert.Contains("CreateInternalMountPointForTests", nativeSource, StringComparison.Ordinal);
        Assert.Contains("CreateInternalRelativeSymbolicLinkForTests", nativeSource, StringComparison.Ordinal);
        Assert.Contains("BuildMountPointReparseBuffer", nativeSource, StringComparison.Ordinal);
        Assert.Contains("BuildRelativeSymbolicLinkReparseBuffer", nativeSource, StringComparison.Ordinal);
        Assert.Contains("SymbolicLinkFlagRelative", nativeSource, StringComparison.Ordinal);
        Assert.Contains("OpenRelativeReparseObjectForDeletion", nativeSource, StringComparison.Ordinal);
        Assert.Contains("DeleteReparsePoint", nativeSource, StringComparison.Ordinal);
        Assert.Contains("MarkForDelete", nativeSource, StringComparison.Ordinal);

        foreach (var source in controlledSources)
        {
            Assert.DoesNotContain(string.Concat("Temp", "Directory"), source, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Directory", ".Delete("), source, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("File", ".CreateSymbolicLink"), source, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Directory", ".CreateSymbolicLink"), source, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Process", ".Start"), source, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("cmd", ".exe"), source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(string.Concat("mk", "link"), source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string GetThisSourcePath([CallerFilePath] string sourcePath = "") => sourcePath;
}
