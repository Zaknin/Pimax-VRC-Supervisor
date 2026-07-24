using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace PimaxVrcSupervisor.Updates;

// The package staging boundary does not use managed path-based I/O after the
// trusted volume root is opened. Each descendant is opened or created relative
// to the already pinned parent handle, so a same-user path swap cannot redirect
// a later child operation.
internal sealed class HardenedUpdateStagingStore : IDisposable
{
    private readonly UpdatePackageVariant _installedVariant;
    private readonly string _updateDirectory;
    private bool _disposed;

    public HardenedUpdateStagingStore(UpdatePackageVariant installedVariant, string updateDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Hardened update staging requires Windows.");
        }

        _installedVariant = installedVariant;
        var requestedRoot = updateDirectory ?? throw new ArgumentNullException(nameof(updateDirectory));
#if !PHASE33B_TEST_TRUST
        var productionRoot = Path.GetFullPath(UpdateStateStore.GetDefaultUpdateDirectory());
        if (!string.Equals(Path.GetFullPath(requestedRoot), productionRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateContractException("package_root", "Production package staging is confined to the fixed LocalAppData update root.");
        }
#endif
        if (!Path.IsPathFullyQualified(requestedRoot))
        {
            throw new UpdateContractException("package_root", "The update staging root must be fully qualified.");
        }
        _updateDirectory = Path.GetFullPath(requestedRoot);
    }

    public HardenedPackageDirectory OpenPackageDirectory(string version, UpdatePackageVariant variant)
    {
        ThrowIfDisposed();
        if (variant != _installedVariant)
        {
            throw new UpdateContractException("package_variant_mismatch", "The package variant does not match the installed package variant.");
        }

        var parsed = SemanticVersion.Parse(version);
        if (!parsed.IsStable || !string.Equals(parsed.ToString(), version, StringComparison.Ordinal))
        {
            throw new UpdateContractException("package_version", "The package version must be normalized and stable.");
        }

        var chain = OpenUpdateRootChain();
        try
        {
            chain.Add(OpenOrCreateDirectory(chain[^1].Handle, "Packages", DirectoryMutationRights.AddSubdirectory, shareDelete: false, protectAcl: true));
            chain.Add(OpenOrCreateDirectory(chain[^1].Handle, version, DirectoryMutationRights.AddSubdirectory, shareDelete: false, protectAcl: true));
            chain.Add(OpenOrCreateDirectory(chain[^1].Handle, VariantDirectoryName(variant), DirectoryMutationRights.AddFile, shareDelete: false, protectAcl: true));
            var expected = Path.Combine(_updateDirectory, "Packages", version, VariantDirectoryName(variant));
            var actual = Native.NormalizeFinalPath(chain[^1].FinalPath);
            if (!string.Equals(actual, Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateContractException("package_containment", "The pinned package directory escaped its canonical staging path.");
            }

            return new HardenedPackageDirectory(
                chain,
                Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            DisposeChain(chain);
            throw;
        }
    }

    internal HardenedPackageDirectory OpenMetadataDirectory()
    {
        ThrowIfDisposed();
        var chain = OpenUpdateRootChain();
        try
        {
            var expected = Path.GetFullPath(_updateDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return new HardenedPackageDirectory(chain, expected);
        }
        catch
        {
            DisposeChain(chain);
            throw;
        }
    }

    private List<PinnedDirectory> OpenUpdateRootChain()
    {
        var root = Path.GetPathRoot(_updateDirectory);
        if (string.IsNullOrEmpty(root)
            || root.Length != 3
            || !char.IsAsciiLetter(root[0])
            || root[1] != ':'
            || root[2] != Path.DirectorySeparatorChar)
        {
            throw new UpdateContractException("package_root", "The update staging root must be beneath a local drive volume root.");
        }

        var chain = new List<PinnedDirectory>();
        try
        {
            var relative = _updateDirectory[root.Length..];
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            chain.Add(OpenVolumeAnchor(
                root,
                segments.Length <= 1 ? DirectoryMutationRights.AddSubdirectory : DirectoryMutationRights.None));
            Native.ValidateSupportedLocalVolume(_updateDirectory, root, chain[^1].Handle);
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                ValidateComponent(segment, "package_directory");
                var isFinalSegment = index == segments.Length - 1;
                var rights = index >= segments.Length - 2
                    ? DirectoryMutationRights.AddSubdirectory
                    : DirectoryMutationRights.None;
                chain.Add(OpenOrCreateDirectory(
                    chain[^1].Handle,
                    segment,
                    rights,
                    shareDelete: !isFinalSegment,
                    protectAcl: isFinalSegment));
            }

            return chain;
        }
        catch
        {
            DisposeChain(chain);
            throw;
        }
    }

    private static PinnedDirectory OpenVolumeAnchor(string root, DirectoryMutationRights rights)
    {
        var normalized = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var handle = Native.OpenAbsoluteDirectory(normalized, rights);
        return Native.ValidateDirectoryHandle(handle, "package_root");
    }

    private static PinnedDirectory OpenOrCreateDirectory(
        SafeFileHandle parent,
        string name,
        DirectoryMutationRights rights,
        bool shareDelete,
        bool protectAcl)
    {
        ValidateComponent(name, "package_directory");
        PinnedDirectory directory;
        try
        {
            directory = Native.OpenRelativeDirectory(parent, name, rights, shareDelete);
        }
        catch (NativeStagingException exception) when (exception.IsNotFound)
        {
            directory = Native.CreateRelativeDirectory(parent, name, rights, shareDelete, protectAcl);
        }
        catch (NativeStagingException exception)
        {
            // Native component-open failures are never retried through a pathname.
            // They are a fail-closed staging topology/security failure.
            throw new UpdateContractException("package_reparse", $"The staging directory component '{name}' could not be safely pinned.", exception);
        }

        try
        {
            if (protectAcl)
            {
                if (directory.Created)
                {
                    Native.ApplyProtectedCurrentUserDirectoryAcl(directory.Handle);
                }

                Native.ValidateProtectedCurrentUserDirectoryAcl(directory.Handle);
                directory.RequiresProtectedAcl = true;
            }

            return directory;
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    internal static void ValidateComponent(string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Length > 240
            || value.EndsWith(".", StringComparison.Ordinal)
            || value.EndsWith(" ", StringComparison.Ordinal)
            || value.Any(char.IsControl)
            || value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0
            || Path.IsPathFullyQualified(value)
            || IsReservedDeviceName(value))
        {
            throw new UpdateContractException(code, "The staging component is unsafe.");
        }
    }

    private static bool IsReservedDeviceName(string value)
    {
        var stem = value.Split('.', 2)[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9';
    }

    internal static string VariantDirectoryName(UpdatePackageVariant variant)
        => variant switch
        {
            UpdatePackageVariant.NoDotnet9 => "win-x64-no-dotnet9",
            UpdatePackageVariant.WithDotnet9 => "win-x64-with-dotnet9",
            _ => throw new UpdateContractException("package_variant", "The package variant is invalid.")
        };

    private static void DisposeChain(IEnumerable<PinnedDirectory> chain)
    {
        foreach (var directory in chain.Reverse())
        {
            directory.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HardenedUpdateStagingStore));
        }
    }

    public void Dispose() => _disposed = true;
}

[Flags]
internal enum DirectoryMutationRights
{
    None = 0,
    AddFile = 1,
    AddSubdirectory = 2,
}

internal sealed class HardenedPackageDirectory : IDisposable
{
    private readonly List<PinnedDirectory> _chain;
    private readonly string _expectedFinalPath;
    private bool _disposed;

    internal HardenedPackageDirectory(List<PinnedDirectory> chain, string expectedFinalPath)
    {
        _chain = chain;
        _expectedFinalPath = expectedFinalPath;
    }

    internal SafeFileHandle Handle
    {
        get
        {
            ThrowIfDisposed();
            return _chain[^1].Handle;
        }
    }

    public SafeFileHandle CreateNewFile(string name, bool write)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        HardenedUpdateStagingStore.ValidateComponent(name, "package_filename");
        return ValidateFileName(Native.CreateRelativeFile(Handle, name, write), name, "package_partial");
    }

    public SafeFileHandle OpenExistingFile(string name, bool write)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        HardenedUpdateStagingStore.ValidateComponent(name, "package_filename");
        return ValidateFileName(Native.OpenRelativeFile(Handle, name, write), name, "package_file");
    }

    public SafeFileHandle? TryOpenExistingFile(string name, bool write)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        HardenedUpdateStagingStore.ValidateComponent(name, "package_filename");
        try
        {
            return ValidateFileName(Native.OpenRelativeFile(Handle, name, write), name, "package_file");
        }
        catch (NativeStagingException exception) when (exception.IsNotFound)
        {
            return null;
        }
    }

    internal SafeFileHandle? TryOpenExistingFileForRecovery(string name)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        HardenedUpdateStagingStore.ValidateComponent(name, "package_filename");
        try
        {
            return ValidateFileName(Native.OpenRelativeFileForRecovery(Handle, name), name, "package_file");
        }
        catch (NativeStagingException exception) when (exception.IsNotFound)
        {
            return null;
        }
    }

    public bool TryDeleteExactFile(string name)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        HardenedUpdateStagingStore.ValidateComponent(name, "package_filename");
        try
        {
            using var handle = Native.OpenRelativeFileForDeletion(Handle, name);
            Native.MarkForDelete(handle);
            return true;
        }
        catch (NativeStagingException exception) when (exception.IsNotFound)
        {
            return false;
        }
    }

    public void DeletePinnedFile(SafeFileHandle file, NativeFileIdentity expectedIdentity, string expectedName)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        ArgumentNullException.ThrowIfNull(file);
        HardenedUpdateStagingStore.ValidateComponent(expectedName, "package_filename");
        var actualIdentity = Native.ReadIdentity(file, "package_cleanup");
        if (actualIdentity != expectedIdentity)
        {
            throw new UpdateContractException("package_cleanup", "The pinned staging file identity changed before cleanup.");
        }

        VerifyFinalFilePath(file, expectedName, "package_cleanup");
        Native.ValidateRegularFileHandle(file, "package_cleanup");
        Native.MarkForDelete(file);
    }

    public void RenamePinnedFile(SafeFileHandle file, string destinationName)
        => RenamePinnedFileCore(file, destinationName, replaceExisting: false);

    public void ReplacePinnedFile(SafeFileHandle file, string destinationName)
        => RenamePinnedFileCore(file, destinationName, replaceExisting: true);

    private void RenamePinnedFileCore(SafeFileHandle file, string destinationName, bool replaceExisting)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        ArgumentNullException.ThrowIfNull(file);
        HardenedUpdateStagingStore.ValidateComponent(destinationName, "package_filename");
        var identity = Native.ReadIdentity(file, "package_promotion");
        var sourcePath = Native.NormalizeFinalPath(Native.GetFinalPath(file, "package_promotion"));
        var sourceParent = Path.GetDirectoryName(sourcePath);
        if (!string.Equals(sourceParent, _expectedFinalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateContractException("package_containment", "Only a pinned direct child of this package directory can be promoted.");
        }
        Native.RenameRelative(file, Handle, destinationName, replaceExisting);
        Native.ValidateRegularFileHandle(file, "package_promotion");
        if (Native.ReadIdentity(file, "package_promotion") != identity)
        {
            throw new UpdateContractException("package_promotion", "The promoted package identity changed unexpectedly.");
        }

        VerifyFinalFilePath(file, destinationName, "package_promotion");
    }

    public NativeFileIdentity CaptureFileIdentity(SafeFileHandle file)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        ArgumentNullException.ThrowIfNull(file);
        return Native.ReadIdentity(file, "package_file");
    }

    public void FlushPinnedFile(SafeFileHandle file)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        ArgumentNullException.ThrowIfNull(file);
        Native.FlushPinnedFile(file);
    }

    public void FlushPinnedDirectory()
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        Native.FlushPinnedDirectory(Handle);
    }

    public long GetPinnedFileLength(SafeFileHandle file)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        ArgumentNullException.ThrowIfNull(file);
        return Native.GetPinnedFileLength(file);
    }

    public void VerifyFinalFilePath(SafeFileHandle file, string name, string code)
    {
        ThrowIfDisposed();
        EnsureCanonicalLocation();
        ArgumentNullException.ThrowIfNull(file);
        HardenedUpdateStagingStore.ValidateComponent(name, "package_filename");
        var expected = Path.Combine(Native.NormalizeFinalPath(_chain[^1].FinalPath), name);
        var actual = Native.NormalizeFinalPath(Native.GetFinalPath(file, code));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateContractException("package_containment", "The pinned package file escaped its canonical staging path.");
        }
    }

    private SafeFileHandle ValidateFileName(SafeFileHandle file, string name, string code)
    {
        try
        {
            VerifyFinalFilePath(file, name, code);
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private void EnsureCanonicalLocation()
    {
        foreach (var directory in _chain)
        {
            var identity = Native.ReadIdentity(directory.Handle, "package_containment");
            var actual = Native.NormalizeFinalPath(Native.GetFinalPath(directory.Handle, "package_containment"));
            if (identity != directory.Identity
                || (identity.Attributes & Native.FileAttributeDirectory) == 0
                || (identity.Attributes & Native.FileAttributeReparsePoint) != 0
                || identity.ReparseTag != 0
                || !string.Equals(actual, Native.NormalizeFinalPath(directory.FinalPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateContractException("package_containment", "A pinned staging ancestor changed identity, type, reparse state, or canonical location after it was opened.");
            }

            if (directory.RequiresProtectedAcl)
            {
                Native.ValidateProtectedCurrentUserDirectoryAcl(directory.Handle);
            }
        }

        var finalPath = Native.NormalizeFinalPath(Native.GetFinalPath(Handle, "package_containment"));
        if (!string.Equals(finalPath, _expectedFinalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateContractException("package_containment", "The pinned package directory was renamed or substituted after it was opened.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var index = _chain.Count - 1; index >= 0; index--)
        {
            _chain[index].Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HardenedPackageDirectory));
        }
    }
}

internal sealed class PinnedDirectory : IDisposable
{
    public PinnedDirectory(SafeFileHandle handle, NativeFileIdentity identity, string finalPath, bool created = false)
    {
        Handle = handle;
        Identity = identity;
        FinalPath = finalPath;
        Created = created;
    }

    public SafeFileHandle Handle { get; }
    public NativeFileIdentity Identity { get; }
    public string FinalPath { get; }
    public bool Created { get; }
    public bool RequiresProtectedAcl { get; set; }

    public void Dispose() => Handle.Dispose();
}

internal readonly record struct NativeFileIdentity(uint VolumeSerialNumber, ulong FileIndex, uint Attributes, uint ReparseTag, uint NumberOfLinks = 1);

internal sealed class NativeStagingException : IOException
{
    public NativeStagingException(string code, int status, string message) : base(message)
    {
        Code = code;
        Status = status;
    }

    public string Code { get; }
    public int Status { get; }
    public bool IsNotFound => Status is Native.StatusObjectNameNotFound or Native.StatusNoSuchFile;
}

// The production inspector is native-only. This internal seam exists solely to make the
// fail-closed classification matrix deterministic in the Windows test assembly; no setting,
// environment variable, or application configuration can replace it.
internal interface IStagingVolumeInspector
{
    StagingVolumeCapability Inspect(string updateDirectory, string root, SafeFileHandle rootHandle);
}

internal readonly record struct StagingVolumeCapability(
    bool QuerySucceeded,
    uint DriveType,
    string? VolumePath,
    string? VolumeGuidPath,
    string? FileSystem,
    uint PathVolumeSerialNumber,
    uint HandleVolumeSerialNumber,
    string? HandleFileSystem);

internal static class Native
{
    internal const int StatusSuccess = 0;
    internal const int StatusObjectNameNotFound = unchecked((int)0xC0000034);
    internal const int StatusNoSuchFile = unchecked((int)0xC000000F);
    private const uint FileReadData = 0x00000001;
    private const uint FileWriteData = 0x00000002;
    private const uint Delete = 0x00010000;
    private const uint ReadControl = 0x00020000;
    private const uint WriteDac = 0x00040000;
    private const uint Synchronize = 0x00100000;
    private const uint FileListDirectory = 0x00000001;
    private const uint FileAddFile = 0x00000002;
    private const uint FileAddSubdirectory = 0x00000004;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileWriteAttributes = 0x00000100;
    private const uint FileShareNone = 0;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileOpen = 0x00000001;
    private const uint FileCreate = 0x00000002;
    private const uint FileOpenIf = 0x00000003;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileSynchronousIoNonAlert = 0x00000020;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint FileAttributeNormal = 0x00000080;
    internal const uint FileAttributeDirectory = 0x00000010;
    internal const uint FileAttributeReparsePoint = 0x00000400;
    private const int FileAttributeTagInfo = 9;
    private const int FileRenameInformation = 10;
    private const int FileDispositionInfo = 4;
    private const uint FileTypeDisk = 1;
    private const uint DriveFixed = 3;
    private const uint OwnerSecurityInformation = 0x00000001;
    private const uint DaclSecurityInformation = 0x00000004;
    private const uint ProtectedDaclSecurityInformation = 0x80000000;
    private const uint MaximumVolumePathCharacters = 32768;
    private const string SupportedFileSystem = "NTFS";

    private static readonly IStagingVolumeInspector ProductionVolumeInspector = new WindowsStagingVolumeInspector();
    private static readonly AsyncLocal<IStagingVolumeInspector?> TestVolumeInspector = new();

    internal static IDisposable PushVolumeInspectorForTests(IStagingVolumeInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        var prior = TestVolumeInspector.Value;
        TestVolumeInspector.Value = inspector;
        return new RestoreVolumeInspector(prior);
    }

    internal static void ValidateSupportedLocalVolume(string updateDirectory, string root, SafeFileHandle rootHandle)
    {
        var capability = (TestVolumeInspector.Value ?? ProductionVolumeInspector).Inspect(updateDirectory, root, rootHandle);
        if (!capability.QuerySucceeded
            || capability.DriveType != DriveFixed
            || !string.Equals(capability.VolumePath, root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(capability.FileSystem, SupportedFileSystem, StringComparison.Ordinal)
            || !string.Equals(capability.HandleFileSystem, SupportedFileSystem, StringComparison.Ordinal)
            || capability.PathVolumeSerialNumber == 0
            || capability.HandleVolumeSerialNumber == 0
            || capability.PathVolumeSerialNumber != capability.HandleVolumeSerialNumber
            || capability.HandleVolumeSerialNumber != ReadIdentity(rootHandle, "package_volume").VolumeSerialNumber
            || string.IsNullOrEmpty(capability.VolumeGuidPath))
        {
            throw new UpdateContractException("package_volume", "The update staging root is not a supported local fixed NTFS volume.");
        }
    }

    internal static SafeFileHandle OpenAbsoluteDirectory(string root, DirectoryMutationRights rights)
    {
        var path = root.StartsWith("\\\\?\\", StringComparison.Ordinal) ? root : "\\\\?\\" + root;
        // A filesystem volume root is the fixed trusted anchor, not a mutable
        // descendant. Windows does not reliably grant exclusive sharing on it.
        var access = FileListDirectory | FileReadAttributes | Synchronize | DirectoryAccess(rights);
        var handle = CreateFileW(path, access, (uint)FileShare.ReadWrite | (uint)FileShare.Delete, IntPtr.Zero, OpenExisting,
            FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw FromLastError("package_root", "The staging volume root could not be opened.");
        }

        return handle;
    }

    internal static PinnedDirectory OpenRelativeDirectory(
        SafeFileHandle parent,
        string name,
        DirectoryMutationRights rights,
        bool shareDelete)
    {
        var access = FileListDirectory | FileReadAttributes | ReadControl | Synchronize | DirectoryAccess(rights);
        var shareAccess = FileShareReadWrite | (shareDelete ? (uint)FileShare.Delete : 0);

        var handle = NtOpenRelative(parent, name, access,
            shareAccess, FileOpen, FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal, "package_directory");
        return ValidateDirectoryHandle(handle, "package_reparse");
    }

    internal static PinnedDirectory CreateRelativeDirectory(
        SafeFileHandle parent,
        string name,
        DirectoryMutationRights rights,
        bool shareDelete,
        bool configureAcl)
    {
        var access = FileListDirectory | FileReadAttributes | ReadControl | Synchronize | DirectoryAccess(rights);
        if (configureAcl)
        {
            access |= WriteDac;
        }
        var shareAccess = FileShareReadWrite | (shareDelete ? (uint)FileShare.Delete : 0);

        var handle = NtOpenRelative(parent, name, access,
            shareAccess, FileCreate, FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeDirectory, "package_directory");
        return ValidateDirectoryHandle(handle, "package_reparse", created: true);
    }

    private static uint DirectoryAccess(DirectoryMutationRights rights)
    {
        const DirectoryMutationRights supported = DirectoryMutationRights.AddFile | DirectoryMutationRights.AddSubdirectory;
        if ((rights & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rights));
        }

        var access = 0u;
        if ((rights & DirectoryMutationRights.AddFile) != 0)
        {
            access |= FileAddFile;
        }
        if ((rights & DirectoryMutationRights.AddSubdirectory) != 0)
        {
            access |= FileAddSubdirectory;
        }
        return access;
    }

    internal static SafeFileHandle CreateRelativeFile(SafeFileHandle parent, string name, bool write)
        => ValidateRegularFileHandle(NtOpenRelative(parent, name,
            (write ? FileWriteData | Delete : FileReadData) | FileReadAttributes | Synchronize,
            FileShareNone, FileCreate, FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal, "package_partial"), "package_partial");

    internal static SafeFileHandle OpenRelativeFile(SafeFileHandle parent, string name, bool write)
        => ValidateRegularFileHandle(NtOpenRelative(parent, name,
            (write ? FileWriteData | Delete : FileReadData) | FileReadAttributes | Synchronize,
            FileShareNone, FileOpen, FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal, "package_file"), "package_reparse");

    internal static SafeFileHandle OpenRelativeFileForDeletion(SafeFileHandle parent, string name)
        => ValidateRegularFileHandle(NtOpenRelative(parent, name,
            Delete | FileReadAttributes | Synchronize,
            FileShareNone, FileOpen, FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal, "package_file"), "package_reparse");

    internal static SafeFileHandle OpenRelativeFileForRecovery(SafeFileHandle parent, string name)
        => ValidateRegularFileHandle(NtOpenRelative(parent, name,
            FileReadData | Delete | FileReadAttributes | Synchronize,
            FileShareNone,
            FileOpen, FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal, "package_file"), "package_reparse");

    internal static PinnedDirectory ValidateDirectoryHandle(SafeFileHandle handle, string code, bool created = false)
    {
        try
        {
            var identity = ReadIdentity(handle, code);
            if ((identity.Attributes & FileAttributeDirectory) == 0 || (identity.Attributes & FileAttributeReparsePoint) != 0 || identity.ReparseTag != 0)
            {
                throw new UpdateContractException("package_reparse", "A staging directory is not an ordinary pinned directory.");
            }

            var finalPath = GetFinalPath(handle, code);
            return new PinnedDirectory(handle, identity, finalPath, created);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static SafeFileHandle ValidateRegularFileHandle(SafeFileHandle handle, string code)
    {
        var identity = ReadIdentity(handle, code);
        if ((identity.Attributes & (FileAttributeDirectory | FileAttributeReparsePoint)) != 0
            || identity.ReparseTag != 0
            || identity.NumberOfLinks != 1
            || GetFileType(handle) != FileTypeDisk)
        {
            handle.Dispose();
            throw new UpdateContractException("package_reparse", "A staging file is not an ordinary pinned disk file.");
        }

        _ = GetFinalPath(handle, code);
        return handle;
    }

    internal static void RenameRelative(SafeFileHandle file, SafeFileHandle destinationDirectory, string destinationName, bool replaceExisting)
    {
        var nameBytes = Encoding.Unicode.GetBytes(destinationName);
        var fileNameOffset = IntPtr.Size == 8 ? 20 : 12;
        var size = checked(fileNameOffset + nameBytes.Length);
        var buffer = Marshal.AllocHGlobal(size);
        var destinationReferenced = false;
        try
        {
            destinationDirectory.DangerousAddRef(ref destinationReferenced);
            Marshal.Copy(new byte[size], 0, buffer, size);
            Marshal.WriteByte(buffer, 0, replaceExisting ? (byte)1 : (byte)0);
            Marshal.WriteIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4, destinationDirectory.DangerousGetHandle());
            var lengthOffset = IntPtr.Size == 8 ? 16 : 8;
            Marshal.WriteInt32(buffer, lengthOffset, nameBytes.Length);
            Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, lengthOffset + sizeof(uint)), nameBytes.Length);
            var status = NtSetInformationFile(file, out var ioStatus, buffer, (uint)size, FileRenameInformation);
            var completionStatus = unchecked((int)ioStatus.Status.ToInt64());
            if (status != StatusSuccess || completionStatus != StatusSuccess)
            {
                var failedStatus = status != StatusSuccess ? status : completionStatus;
                var win32 = RtlNtStatusToDosError(failedStatus);
                throw new NativeStagingException(
                    "package_promotion",
                    failedStatus,
                    $"The pinned package could not be promoted (NTSTATUS=0x{failedStatus:X8}, Win32={win32}).");
            }
        }
        finally
        {
            if (destinationReferenced)
            {
                destinationDirectory.DangerousRelease();
            }

            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static void MarkForDelete(SafeFileHandle file)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, 1);
            if (!SetFileInformationByHandle(file, FileDispositionInfo, buffer, sizeof(int)))
            {
                throw FromLastError("package_cleanup", "The pinned staging file could not be deleted.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeFileHandle NtOpenRelative(SafeFileHandle parent, string name, uint access, uint shareAccess, uint disposition, uint options, uint attributes, string code)
    {
        using var unicode = new NativeUnicodeString(name);
        var parentReferenced = false;
        try
        {
            parent.DangerousAddRef(ref parentReferenced);
            var objectAttributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicode.Pointer,
                Attributes = 0x00000040
            };
            var status = NtCreateFile(out var handle, access, ref objectAttributes, out _, IntPtr.Zero, attributes, shareAccess,
                disposition, options, IntPtr.Zero, 0);
            if (status != StatusSuccess)
            {
                handle?.Dispose();
                throw new NativeStagingException(code, status, $"A pinned staging component could not be opened or created (NTSTATUS=0x{status:X8}).");
            }

            return handle;
        }
        finally
        {
            if (parentReferenced)
            {
                parent.DangerousRelease();
            }
        }
    }

    internal static NativeFileIdentity ReadIdentity(SafeFileHandle handle, string code)
    {
        if (!GetFileInformationByHandle(handle, out var info))
        {
            throw FromLastError(code, "The pinned staging component identity could not be read.");
        }

        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out var tagInfo, (uint)Marshal.SizeOf<FileAttributeTagInformation>()))
        {
            throw FromLastError(code, "The pinned staging component attributes could not be read.");
        }

        return new NativeFileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, tagInfo.FileAttributes, tagInfo.ReparseTag, info.NumberOfLinks);
    }

    internal static void FlushPinnedFile(SafeFileHandle handle)
    {
        if (!FlushFileBuffers(handle))
        {
            throw FromLastError("package_flush", "The pinned staging file could not be durably flushed.");
        }
    }

    internal static void FlushPinnedDirectory(SafeFileHandle handle)
    {
        var status = NtFlushBuffersFile(handle, out var ioStatus);
        var completionStatus = unchecked((int)ioStatus.Status.ToInt64());
        if (status != StatusSuccess || completionStatus != StatusSuccess)
        {
            var failedStatus = status != StatusSuccess ? status : completionStatus;
            throw new NativeStagingException("package_directory_flush", failedStatus, "The pinned staging directory could not be durably flushed.");
        }
    }

    internal static long GetPinnedFileLength(SafeFileHandle handle)
    {
        if (!GetFileSizeEx(handle, out var length) || length < 0)
        {
            throw FromLastError("package_size", "The pinned staging file length could not be read.");
        }

        return length;
    }

    internal static string GetFinalPath(SafeFileHandle handle, string code)
    {
        var capacity = 1024u;
        while (capacity <= 32768)
        {
            var builder = new StringBuilder((int)capacity);
            var result = GetFinalPathNameByHandleW(handle, builder, capacity, 0);
            if (result == 0)
            {
                throw FromLastError(code, "The pinned staging component final path could not be read.");
            }

            if (result < capacity)
            {
                return builder.ToString();
            }

            capacity = result + 1;
        }

        throw new UpdateContractException(code, "The pinned staging component final path is too long.");
    }

    internal static string NormalizeFinalPath(string path)
    {
        const string devicePrefix = "\\\\?\\";
        var normalized = path.StartsWith(devicePrefix, StringComparison.Ordinal) ? path[devicePrefix.Length..] : path;
        return Path.GetFullPath(normalized).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static void ApplyProtectedCurrentUserDirectoryAcl(SafeFileHandle handle)
    {
        try
        {
            var currentUser = WindowsIdentity.GetCurrent().User
                ?? throw new UpdateContractException("package_acl", "The current Windows user SID is unavailable for protected update staging.");
            var dacl = new RawAcl(revision: 2, capacity: 1);
            dacl.InsertAce(0, new CommonAce(
                AceFlags.ContainerInherit | AceFlags.ObjectInherit,
                AceQualifier.AccessAllowed,
                unchecked((int)FileSystemRights.FullControl),
                currentUser,
                isCallback: false,
                opaque: null));
            var descriptor = new RawSecurityDescriptor(
                ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected,
                owner: currentUser,
                group: null,
                systemAcl: null,
                discretionaryAcl: dacl);
            var bytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(bytes, 0);
            if (!SetKernelObjectSecurity(handle, DaclSecurityInformation | ProtectedDaclSecurityInformation, bytes))
            {
                throw FromLastError("package_acl", "The protected staging directory DACL could not be applied.");
            }
        }
        catch (UpdateContractException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            throw new UpdateContractException("package_acl", "The protected staging directory DACL could not be constructed.", exception);
        }
    }

    internal static void ValidateProtectedCurrentUserDirectoryAcl(SafeFileHandle handle)
    {
        try
        {
            var currentUser = WindowsIdentity.GetCurrent().User
                ?? throw new UpdateContractException("package_acl", "The current Windows user SID is unavailable for protected update staging.");
            var descriptor = new RawSecurityDescriptor(ReadKernelObjectSecurity(handle, OwnerSecurityInformation | DaclSecurityInformation), 0);
            var dacl = descriptor.DiscretionaryAcl;
            if (descriptor.Owner != currentUser
                || (descriptor.ControlFlags & (ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected))
                    != (ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected)
                || dacl is null
                || dacl.Count != 1
                || dacl[0] is not CommonAce ace
                || ace.AceQualifier != AceQualifier.AccessAllowed
                || ace.SecurityIdentifier != currentUser
                || ace.AceFlags != (AceFlags.ContainerInherit | AceFlags.ObjectInherit)
                || ace.AccessMask != unchecked((int)FileSystemRights.FullControl))
            {
                throw new UpdateContractException("package_acl", "The staging directory ACL is not the exact protected current-user contract.");
            }
        }
        catch (UpdateContractException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            throw new UpdateContractException("package_acl", "The staging directory ACL descriptor is malformed or inaccessible.", exception);
        }
    }

    private static byte[] ReadKernelObjectSecurity(SafeFileHandle handle, uint securityInformation)
    {
        if (GetKernelObjectSecurity(handle, securityInformation, null, 0, out var required) || required == 0)
        {
            throw FromLastError("package_acl", "The protected staging directory security descriptor could not be read.");
        }

        var bytes = new byte[required];
        if (!GetKernelObjectSecurity(handle, securityInformation, bytes, (uint)bytes.Length, out required))
        {
            throw FromLastError("package_acl", "The protected staging directory security descriptor could not be read.");
        }

        return bytes;
    }

    private static NativeStagingException FromLastError(string code, string message)
    {
        var error = Marshal.GetLastWin32Error();
        return new NativeStagingException(code, error, $"{message} (Win32={error}).");
    }

    private sealed class RestoreVolumeInspector(IStagingVolumeInspector? prior) : IDisposable
    {
        private readonly IStagingVolumeInspector? _prior = prior;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                TestVolumeInspector.Value = _prior;
            }
        }
    }

    private sealed class WindowsStagingVolumeInspector : IStagingVolumeInspector
    {
        public StagingVolumeCapability Inspect(string updateDirectory, string root, SafeFileHandle rootHandle)
        {
            try
            {
                var volumePath = new StringBuilder((int)MaximumVolumePathCharacters);
                var volumeGuid = new StringBuilder((int)MaximumVolumePathCharacters);
                var fileSystem = new StringBuilder(256);
                var handleFileSystem = new StringBuilder(256);
                if (GetDriveTypeW(root) != DriveFixed
                    || !GetVolumePathNameW(updateDirectory, volumePath, (uint)volumePath.Capacity)
                    || !GetVolumeNameForVolumeMountPointW(volumePath.ToString(), volumeGuid, (uint)volumeGuid.Capacity)
                    || !GetVolumeInformationW(volumePath.ToString(), null, 0, out var pathSerial, out _, out _, fileSystem, (uint)fileSystem.Capacity)
                    || !GetVolumeInformationByHandleW(rootHandle, null, 0, out var handleSerial, out _, out _, handleFileSystem, (uint)handleFileSystem.Capacity))
                {
                    return default;
                }

                return new StagingVolumeCapability(
                    QuerySucceeded: true,
                    DriveType: DriveFixed,
                    VolumePath: volumePath.ToString(),
                    VolumeGuidPath: volumeGuid.ToString(),
                    FileSystem: fileSystem.ToString(),
                    PathVolumeSerialNumber: pathSerial,
                    HandleVolumeSerialNumber: handleSerial,
                    HandleFileSystem: handleFileSystem.ToString());
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return default;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    private sealed class NativeUnicodeString : IDisposable
    {
        private readonly IntPtr _buffer;
        private IntPtr _structure;

        public NativeUnicodeString(string value)
        {
            _buffer = Marshal.StringToHGlobalUni(value);
            var native = new UnicodeString
            {
                Length = checked((ushort)(value.Length * sizeof(char))),
                MaximumLength = checked((ushort)(value.Length * sizeof(char) + sizeof(char))),
                Buffer = _buffer
            };
            _structure = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(native, _structure, false);
        }

        public IntPtr Pointer => _structure;

        public void Dispose()
        {
            Marshal.FreeHGlobal(_buffer);
            if (_structure != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_structure);
                _structure = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInformation
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetDriveTypeW(string rootPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string fileName, StringBuilder volumePathName, uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string volumeMountPoint, StringBuilder volumeName, uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(string rootPathName, StringBuilder? volumeNameBuffer, uint volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder fileSystemNameBuffer, uint fileSystemNameSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandleW(SafeFileHandle file, StringBuilder? volumeNameBuffer, uint volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder fileSystemNameBuffer, uint fileSystemNameSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out FileAttributeTagInformation information, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, IntPtr information, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileSizeEx(SafeFileHandle handle, out long fileSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKernelObjectSecurity(SafeFileHandle handle, uint securityInformation, byte[]? securityDescriptor, uint length, out uint lengthNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint securityInformation, byte[] securityDescriptor);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out SafeFileHandle fileHandle, uint desiredAccess, ref ObjectAttributes objectAttributes, out IoStatusBlock ioStatusBlock,
        IntPtr allocationSize, uint fileAttributes, uint shareAccess, uint createDisposition, uint createOptions, IntPtr eaBuffer, uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(SafeFileHandle fileHandle, out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation, uint length, int fileInformationClass);

    [DllImport("ntdll.dll")]
    private static extern int NtFlushBuffersFile(SafeFileHandle fileHandle, out IoStatusBlock ioStatusBlock);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
}
