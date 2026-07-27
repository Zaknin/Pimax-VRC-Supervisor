using Microsoft.Win32.SafeHandles;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;

/// <summary>
/// Test-only native filesystem boundary. It is deliberately restricted to GUID
/// children of <see cref="AuthorizedRoot"/> and never performs path-based child
/// I/O or recursive cleanup.
/// </summary>
internal sealed class ControlledNativeValidationRun : IDisposable
{
    private const string DefaultAuthorizedRoot = @"C:\PimaxVrcSupervisor-PrivilegedNativeValidation";
    internal static string AuthorizedRoot => NormalizeDirectoryPath(Environment.GetEnvironmentVariable("PHASE33B_NATIVE_VALIDATION_ROOT") ?? DefaultAuthorizedRoot);
    internal static string AuthorizedRootName => Path.GetFileName(AuthorizedRoot);
    internal static string AuthorizedVolumeRoot => Path.GetPathRoot(AuthorizedRoot)
        ?? throw new InvalidOperationException("The controlled native validation root must have a drive volume root.");
    private const string SentinelFileName = ".pimax-vrc-supervisor-native-validation-sentinel";

    private readonly PinnedNativeValidationDirectory _parent;
    private readonly PinnedNativeValidationDirectory _run;
    private readonly object _inventoryGate = new();
    private readonly List<NativeValidationInventoryMutation> _inventoryMutations = [];
    private readonly Guid _runId;
    private readonly int _runObjectIdentity;
    // Immutable snapshots prevent native calls from ever observing or mutating a
    // partially updated typed-cleanup inventory.
    private ImmutableArray<NativeValidationInventoryItem> _inventory = [];
    private long _inventoryMutationSequence;
    private readonly NativeValidationInventoryItem _sentinel;
    private readonly string _sentinelContents;
    private bool _disposed;

    private ControlledNativeValidationRun(Guid runId)
    {
        _runId = runId;
        _runObjectIdentity = RuntimeHelpers.GetHashCode(this);
        PinnedNativeValidationDirectory? parent = null;
        PinnedNativeValidationDirectory? run = null;
        NativeValidationInventoryItem? createdSentinel = null;
        try
        {
            parent = OpenAuthorizedRoot();
            var runName = runId.ToString("D");
            run = NativeValidationFileSystem.CreateRelativeDirectory(parent.Handle, runName, writable: true);
            NativeValidationFileSystem.VerifyDirectoryLocation(run, Path.Combine(AuthorizedRoot, runName));

            RunDirectory = NativeValidationFileSystem.NormalizeFinalPath(run.FinalPath);
            _sentinelContents = CreateSentinelContents(runId, parent, run);
            _sentinel = WriteNewRelativeFile(run, SentinelFileName, _sentinelContents);
            createdSentinel = _sentinel;
            AddInventoryItem(_sentinel);

            _parent = parent;
            _run = run;
            parent = null;
            run = null;
        }
        catch
        {
            if (run is not null)
            {
                try
                {
                    if (createdSentinel is not null)
                    {
                        DeleteCreatedItem(run.Handle, createdSentinel.Value);
                    }

                    if (NativeValidationFileSystem.EnumerateDirectChildren(run.Handle).Count == 0)
                    {
                        NativeValidationFileSystem.MarkForDelete(run.Handle);
                    }
                }
                catch
                {
                    // Preserve the constructor failure. Residual recovery remains identity- and
                    // sentinel-gated and will quarantine any incompletely retired run.
                }
            }

            run?.Dispose();
            parent?.Dispose();
            throw;
        }
    }

    internal string RunDirectory { get; }
    internal string SentinelPath => _sentinel.FinalPath;
    internal NativeValidationFileIdentity ParentIdentity => _parent.Identity;
    internal NativeValidationFileIdentity RunIdentity => _run.Identity;
    internal NativeValidationFileIdentity SentinelIdentity => _sentinel.Identity;
    internal Guid RunIdForTests => _runId;
    internal int RunObjectIdentityForTests => _runObjectIdentity;
    internal SafeFileHandle RunHandleForTests => _run.Handle;
    internal IReadOnlyList<string> InventoryNamesForTests => SnapshotInventory().Select(item => item.Name).ToArray();
    internal IReadOnlyList<NativeValidationInventoryMutation> InventoryMutationsForTests
    {
        get
        {
            lock (_inventoryGate)
            {
                return _inventoryMutations.ToArray();
            }
        }
    }

    internal NativeValidationFileIdentity InventoryIdentityForTests(string name)
    {
        lock (_inventoryGate)
        {
            return GetSingleInventoryItem(name).Identity;
        }
    }

    internal IReadOnlyList<string> EnumeratedRunChildNamesForTests()
    {
        lock (_inventoryGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return NativeValidationFileSystem.EnumerateDirectChildren(_run.Handle).Select(entry => entry.Name).ToArray();
        }
    }

    internal void DeleteInventoryItemForTests(string name, bool failBeforeNativeDeletion = false)
    {
        lock (_inventoryGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DeleteExactInventoryItem(GetSingleInventoryItem(name), failBeforeNativeDeletion);
        }
    }


    internal static ControlledNativeValidationRun Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Controlled native validation runs require Windows.");
        }

        return new ControlledNativeValidationRun(Guid.NewGuid());
    }

    // Test-only negative-validation entry point. The candidate is opened only as a
    // handle-relative direct child of the pinned authorized parent; it is never created.
    internal static void ValidateCandidateRootForTests(string candidateRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Controlled native validation runs require Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(candidateRoot);
        var candidate = NormalizeDirectoryPath(Path.GetFullPath(candidateRoot));
        if (!string.Equals(Path.GetDirectoryName(candidate), AuthorizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A root-validation probe must be a direct child of the fixed authorized root.");
        }

        var name = Path.GetFileName(candidate);
        ValidateDirectFileName(name);
        using var parent = OpenAuthorizedRoot();
        using var probe = NativeValidationFileSystem.OpenRelativeDirectory(parent.Handle, name, writable: false);
        NativeValidationFileSystem.VerifyDirectoryLocation(probe, candidate);
    }

    /// <summary>Creates a single ordinary file relative to the pinned run handle.</summary>
    internal string WriteFile(string fileName, string contents)
    {
        lock (_inventoryGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(contents);
            ValidateDirectFileName(fileName);
            EnsurePinnedTopology();

            var item = WriteNewRelativeFile(_run, fileName, contents);
            AddInventoryItem(item);
            return item.FinalPath;
        }
    }

    /// <summary>Creates an empty ordinary directory relative to the pinned run handle for link-validation tests.</summary>
    internal string CreateDirectoryForTests(string directoryName)
    {
        lock (_inventoryGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDirectFileName(directoryName);
            EnsurePinnedTopology();

            var item = CreateNewRelativeDirectory(_run, directoryName);
            AddInventoryItem(item);
            return item.FinalPath;
        }
    }

    /// <summary>
    /// Creates a mount-point junction solely from handles rooted at this run. The
    /// target must be an already inventoried ordinary direct-child directory.
    /// </summary>
    internal NativeValidationReparsePoint CreateInternalMountPointForTests(
        string junctionName,
        string targetDirectory,
        bool failBeforeMountPointAssignmentForTests = false)
    {
        lock (_inventoryGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
            ValidateDirectFileName(junctionName);
            EnsurePinnedTopology();
            if (InventoryContainsName(junctionName))
            {
                throw new InvalidOperationException("A controlled native validation child cannot be added to the inventory twice.");
            }

            var target = GetTrackedDirectChildDirectory(targetDirectory);
            using (var targetHandle = NativeValidationFileSystem.OpenRelativeDirectory(_run.Handle, target.Name, writable: false))
            {
                VerifyInventoryItem(targetHandle.Handle, target);
            }

            using var junction = NativeValidationFileSystem.CreateRelativeMountPointObject(_run.Handle, junctionName);
            var ordinaryIdentity = NativeValidationFileSystem.ReadIdentity(junction);
            var finalPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(junction));
            VerifyDirectChildPath(finalPath, junctionName);
            var ordinaryItem = new NativeValidationInventoryItem(
                junctionName,
                ordinaryIdentity,
                finalPath,
                NativeValidationInventoryItemKind.Directory,
                ReparsePoint: null);
            AddInventoryItem(ordinaryItem);
            if (failBeforeMountPointAssignmentForTests)
            {
                throw new InvalidOperationException("Injected controlled native validation mount-point assignment failure after inventory registration.");
            }

            NativeValidationFileSystem.AssignMountPoint(junction, target.FinalPath);
            var identity = NativeValidationFileSystem.ReadIdentity(junction);
            var reparsePoint = NativeValidationFileSystem.ReadReparsePoint(junction, identity);
            VerifyDirectChildPath(finalPath, junctionName);
            if (reparsePoint.Kind != NativeValidationReparsePointKind.MountPoint
                || !string.Equals(reparsePoint.SubstituteName, NativeValidationFileSystem.ToMountPointSubstituteName(target.FinalPath), StringComparison.Ordinal)
                || !string.Equals(reparsePoint.PrintName, target.FinalPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A controlled native validation junction did not retain its exact bounded mount-point target data.");
            }

            ReplaceInventoryItem(ordinaryItem, new NativeValidationInventoryItem(
                junctionName,
                identity,
                finalPath,
                NativeValidationInventoryItemKind.ReparsePoint,
                reparsePoint));
            return reparsePoint;
        }
    }

    internal NativeValidationReparsePoint CreateInternalRelativeSymbolicLinkForTests(
        string linkName,
        string targetName,
        bool failBeforeAssignmentForTests = false,
        bool failAfterAssignmentForTests = false,
        string? targetHandleNameForTests = null)
    {
        lock (_inventoryGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDirectFileName(linkName);
            ValidateDirectFileName(targetName);
            EnsurePinnedTopology();
            if (InventoryContainsName(linkName))
            {
                throw new InvalidOperationException("A controlled native validation child cannot be added to the inventory twice.");
            }

            var target = GetSingleInventoryItem(targetName);
            if (target.Kind != NativeValidationInventoryItemKind.RegularFile)
            {
                throw new InvalidOperationException("A controlled native validation symbolic-link target must be one tracked ordinary file.");
            }

            var targetHandleName = targetHandleNameForTests ?? targetName;
            ValidateDirectFileName(targetHandleName);
            using var targetHandle = NativeValidationFileSystem.OpenRelativeFile(_run.Handle, targetHandleName);
            VerifyInventoryItem(targetHandle, target);
            var targetContents = ReadBoundedPinnedFile(targetHandle);

            SafeFileHandle? link = null;
            try
            {
                link = NativeValidationFileSystem.CreateRelativeFile(_run.Handle, linkName);
                var ordinaryIdentity = NativeValidationFileSystem.ReadIdentity(link);
                var finalPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(link));
                VerifyDirectChildPath(finalPath, linkName);

                if (failBeforeAssignmentForTests)
                {
                    throw new InvalidOperationException("Injected controlled native validation symbolic-link failure before native assignment.");
                }

                NativeValidationFileSystem.AssignRelativeSymbolicLink(link, targetName);
                if (failAfterAssignmentForTests)
                {
                    throw new InvalidOperationException("Injected controlled native validation symbolic-link failure after native assignment.");
                }

                var identity = NativeValidationFileSystem.ReadIdentity(link);
                var reparsePoint = NativeValidationFileSystem.ReadReparsePoint(link, identity);
                VerifyDirectChildPath(finalPath, linkName);
                if (!NativeValidationFileSystem.SameObjectIdentity(ordinaryIdentity, identity)
                    || reparsePoint.Kind != NativeValidationReparsePointKind.SymbolicLink
                    || !reparsePoint.IsRelative
                    || !string.Equals(reparsePoint.SubstituteName, targetName, StringComparison.Ordinal)
                    || !string.Equals(reparsePoint.PrintName, targetName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A controlled native validation symbolic link did not retain its exact relative target data and object identity.");
                }

                VerifyInventoryItem(targetHandle, target);
                if (!targetContents.AsSpan().SequenceEqual(ReadBoundedPinnedFile(targetHandle)))
                {
                    throw new InvalidOperationException("A controlled native validation symbolic-link assignment changed its target contents.");
                }

                AddInventoryItem(new NativeValidationInventoryItem(
                    linkName,
                    identity,
                    finalPath,
                    NativeValidationInventoryItemKind.ReparsePoint,
                    reparsePoint));
                return reparsePoint;
            }
            catch (Exception assignmentFailure)
            {
                Exception? cleanupFailure = null;
                if (link is not null)
                {
                    try
                    {
                        NativeValidationFileSystem.MarkForDelete(link);
                    }
                    catch (Exception exception)
                    {
                        cleanupFailure = exception;
                    }
                    finally
                    {
                        link.Dispose();
                        link = null;
                    }

                    try
                    {
                        VerifyDirectChildIsAbsent(linkName);
                    }
                    catch (Exception exception)
                    {
                        cleanupFailure = cleanupFailure is null
                            ? exception
                            : new AggregateException(cleanupFailure, exception);
                    }
                }

                if (cleanupFailure is not null)
                {
                    throw new AggregateException(
                        "Controlled native validation symbolic-link assignment failed and its newly created object could not be proven absent.",
                        assignmentFailure,
                        cleanupFailure);
                }

                throw;
            }
            finally
            {
                link?.Dispose();
            }
        }
    }

    private static byte[] ReadBoundedPinnedFile(SafeFileHandle file)
    {
        const int maximumBytes = 1024 * 1024;
        var length = RandomAccess.GetLength(file);
        if (length < 0 || length > maximumBytes)
        {
            throw new InvalidOperationException("A controlled native validation symbolic-link target exceeded the bounded content snapshot.");
        }

        var contents = new byte[checked((int)length)];
        var offset = 0;
        while (offset < contents.Length)
        {
            var read = RandomAccess.Read(file, contents.AsSpan(offset), offset);
            if (read == 0)
            {
                throw new EndOfStreamException("A controlled native validation symbolic-link target became truncated during validation.");
            }

            offset = checked(offset + read);
        }

        return contents;
    }

    /// <summary>
    /// Adds a direct mount-point or symbolic-link object to the typed inventory.
    /// Inspection and eventual deletion use FILE_OPEN_REPARSE_POINT and never follow its target.
    /// </summary>
    internal NativeValidationReparsePoint TrackDirectReparseObjectForTests(string name)
    {
        lock (_inventoryGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDirectFileName(name);
            EnsurePinnedTopology();
            if (InventoryContainsName(name))
            {
                throw new InvalidOperationException("A controlled native validation child cannot be added to the inventory twice.");
            }

            using var reparseObject = NativeValidationFileSystem.OpenRelativeReparseObjectForDeletion(_run.Handle, name);
            var identity = NativeValidationFileSystem.ReadIdentity(reparseObject);
            var reparsePoint = NativeValidationFileSystem.ReadReparsePoint(reparseObject, identity);
            var finalPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(reparseObject));
            VerifyDirectChildPath(finalPath, name);
            AddInventoryItem(new NativeValidationInventoryItem(
                name,
                identity,
                finalPath,
                NativeValidationInventoryItemKind.ReparsePoint,
                reparsePoint));
            return reparsePoint;
        }
    }

    internal void AbandonIntactForRecovery()
    {
        lock (_inventoryGate)
        {
            if (_disposed)
            {
                return;
            }

            EnsurePinnedTopology();
            VerifySentinel();
            _disposed = true;
            _run.Dispose();
            _parent.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_inventoryGate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var inventory = _inventory.ToArray();
                EnsurePinnedTopology();
                if (!_inventory.SequenceEqual(inventory))
                {
                    throw new InvalidOperationException("The typed controlled native validation inventory changed during pinned-topology verification.");
                }

                VerifyExactInventoryEnumeration(inventory);
                VerifySentinel();

                // Delete exactly the typed inventory, by reopening each name below the
                // pinned run handle and requiring its native identity to match. The
                // bounded enumeration above fails closed before this phase if any direct
                // child is not represented in the typed inventory.
                var cleanupErrors = new List<Exception>();
                for (var index = inventory.Length - 1; index >= 0; index--)
                {
                    try
                    {
                        DeleteExactInventoryItem(inventory[index]);
                    }
                    catch (Exception exception)
                    {
                        cleanupErrors.Add(new InvalidOperationException(
                            $"Controlled native validation cleanup failed for '{inventory[index].Name}'.",
                            exception));
                    }
                }

                if (cleanupErrors.Count > 0)
                {
                    throw new AggregateException("Controlled native validation cleanup retained one or more exact owned artifacts.", cleanupErrors);
                }

                if (_inventory.Length != 0 || NativeValidationFileSystem.EnumerateDirectChildren(_run.Handle).Count != 0)
                {
                    throw new InvalidOperationException("The controlled native validation run retained inventory or native children after exact cleanup.");
                }

                NativeValidationFileSystem.MarkForDelete(_run.Handle);
                _disposed = true;
            }
            finally
            {
                if (_disposed)
                {
                    _run.Dispose();
                    _parent.Dispose();
                }
            }
        }
    }

    private static PinnedNativeValidationDirectory OpenAuthorizedRoot()
    {
        SafeFileHandle? volumeHandle = NativeValidationFileSystem.OpenVolumeAnchor(AuthorizedVolumeRoot);
        try
        {
            NativeValidationFileSystem.ValidateFixedNtfsVolume(volumeHandle, AuthorizedVolumeRoot);
            using var volume = NativeValidationFileSystem.ValidateDirectoryHandle(volumeHandle);
            volumeHandle = null; // Ownership moved into the pinned directory wrapper.

            var parent = NativeValidationFileSystem.OpenOrCreateRelativeDirectory(volume.Handle, AuthorizedRootName, writable: true);
            NativeValidationFileSystem.VerifyDirectoryLocation(parent, AuthorizedRoot);
            return parent;
        }
        finally
        {
            volumeHandle?.Dispose();
        }
    }

    private static NativeValidationInventoryItem WriteNewRelativeFile(PinnedNativeValidationDirectory parent, string name, string contents)
    {
        ValidateDirectFileName(name);
        using var file = NativeValidationFileSystem.CreateRelativeFile(parent.Handle, name);
        try
        {
            var identity = NativeValidationFileSystem.ReadIdentity(file);
            var finalPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(file));
            NativeValidationFileSystem.VerifyRegularFile(file, identity);
            var expected = Path.Combine(NativeValidationFileSystem.NormalizeFinalPath(parent.FinalPath), name);
            if (!string.Equals(finalPath, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A controlled native validation file escaped its pinned run directory.");
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            RandomAccess.Write(file, bytes, 0);
            NativeValidationFileSystem.FlushPinnedFile(file);
            return new NativeValidationInventoryItem(name, identity, finalPath, NativeValidationInventoryItemKind.RegularFile, null);
        }
        catch
        {
            TryMarkCreatedObjectForDelete(file);
            throw;
        }
    }

    private static NativeValidationInventoryItem CreateNewRelativeDirectory(PinnedNativeValidationDirectory parent, string name)
    {
        ValidateDirectFileName(name);
        using var directory = NativeValidationFileSystem.CreateRelativeDirectory(parent.Handle, name, writable: false);
        try
        {
            var expected = Path.Combine(NativeValidationFileSystem.NormalizeFinalPath(parent.FinalPath), name);
            var finalPath = NativeValidationFileSystem.NormalizeFinalPath(directory.FinalPath);
            if (!string.Equals(finalPath, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A controlled native validation directory escaped its pinned run directory.");
            }

            return new NativeValidationInventoryItem(name, directory.Identity, finalPath, NativeValidationInventoryItemKind.Directory, null);
        }
        catch
        {
            TryMarkCreatedObjectForDelete(directory.Handle);
            throw;
        }
    }

    private static void DeleteCreatedItem(SafeFileHandle parent, NativeValidationInventoryItem item)
    {
        using var handle = item.Kind switch
        {
            NativeValidationInventoryItemKind.RegularFile => NativeValidationFileSystem.OpenRelativeFileForDeletion(parent, item.Name),
            NativeValidationInventoryItemKind.Directory => NativeValidationFileSystem.OpenRelativeDirectoryForDeletion(parent, item.Name),
            NativeValidationInventoryItemKind.ReparsePoint => NativeValidationFileSystem.OpenRelativeReparseObjectForDeletion(parent, item.Name),
            _ => throw new InvalidOperationException("An unknown controlled native validation item type was encountered."),
        };
        VerifyInventoryItem(handle, item);
        if (item.Kind == NativeValidationInventoryItemKind.ReparsePoint)
        {
            NativeValidationFileSystem.DeleteReparsePoint(handle, item);
        }

        NativeValidationFileSystem.MarkForDelete(handle);
    }

    private static void TryMarkCreatedObjectForDelete(SafeFileHandle handle)
    {
        try
        {
            NativeValidationFileSystem.MarkForDelete(handle);
        }
        catch
        {
            // Preserve the primary creation/validation failure.
        }
    }

    private NativeValidationInventoryItem GetTrackedDirectChildDirectory(string targetDirectory)
    {
        var targetPath = NormalizeDirectoryPath(Path.GetFullPath(targetDirectory));
        var name = Path.GetFileName(targetPath);
        ValidateDirectFileName(name);
        VerifyDirectChildPath(targetPath, name);

        var matches = SnapshotInventory().Where(item => item.Kind == NativeValidationInventoryItemKind.Directory
            && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.FinalPath, targetPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("A controlled native validation junction target must be one typed ordinary direct-child directory.");
        }

        return matches[0];
    }

    private void EnsurePinnedTopology()
    {
        NativeValidationFileSystem.VerifyPinnedDirectory(_parent, AuthorizedRoot);
        NativeValidationFileSystem.VerifyPinnedDirectory(_run, RunDirectory);
    }

    private NativeValidationInventoryItem[] SnapshotInventory()
    {
        lock (_inventoryGate)
        {
            return _inventory.ToArray();
        }
    }

    private bool InventoryContainsName(string name)
    {
        lock (_inventoryGate)
        {
            return _inventory.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private NativeValidationInventoryItem GetSingleInventoryItem(string name)
    {
        ValidateDirectFileName(name);
        var matches = _inventory
            .Where(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("A controlled native validation inventory operation requires one exact registered child.");
        }

        return matches[0];
    }

    private void AddInventoryItem(NativeValidationInventoryItem item, [CallerMemberName] string caller = "")
    {
        lock (_inventoryGate)
        {
            _inventory = _inventory.Add(item);
            RecordInventoryMutation("add", item, caller);
        }
    }

    private void ReplaceInventoryItem(
        NativeValidationInventoryItem expected,
        NativeValidationInventoryItem replacement,
        [CallerMemberName] string caller = "")
    {
        lock (_inventoryGate)
        {
            var index = _inventory.IndexOf(expected);
            if (index < 0
                || !string.Equals(expected.Name, replacement.Name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(expected.FinalPath, replacement.FinalPath, StringComparison.OrdinalIgnoreCase)
                || !NativeValidationFileSystem.SameObjectIdentity(expected.Identity, replacement.Identity))
            {
                throw new InvalidOperationException("A controlled native validation inventory replacement did not preserve one exact native object.");
            }

            _inventory = _inventory.SetItem(index, replacement);
            RecordInventoryMutation("replace", replacement, caller);
        }
    }

    private void RemoveInventoryItem(NativeValidationInventoryItem item, [CallerMemberName] string caller = "")
    {
        lock (_inventoryGate)
        {
            var updated = _inventory.Remove(item);
            if (updated.Length != _inventory.Length - 1)
            {
                throw new InvalidOperationException("A controlled native validation inventory removal did not match one exact registered child.");
            }

            _inventory = updated;
            RecordInventoryMutation("remove", item, caller);
        }
    }

    private void RecordInventoryMutation(string operation, NativeValidationInventoryItem item, string caller)
        => _inventoryMutations.Add(new NativeValidationInventoryMutation(
            Sequence: ++_inventoryMutationSequence,
            RunId: _runId,
            RunObjectIdentity: _runObjectIdentity,
            Operation: operation,
            ObjectName: item.Name,
            ObjectType: item.Kind,
            Caller: caller,
            ManagedThreadId: Environment.CurrentManagedThreadId));

    private void VerifySentinel()
    {
        using var file = NativeValidationFileSystem.OpenRelativeFile(_run.Handle, SentinelFileName);
        VerifyInventoryItem(file, _sentinel);
        using var stream = new FileStream(file, FileAccess.Read, bufferSize: 4096, isAsync: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: false);
        if (!string.Equals(reader.ReadToEnd(), _sentinelContents, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The controlled native validation sentinel did not bind this pinned run identity and path.");
        }
    }

    private void VerifyExactInventoryEnumeration(IReadOnlyList<NativeValidationInventoryItem> inventory)
    {
        var entries = NativeValidationFileSystem.EnumerateDirectChildren(_run.Handle);
        if (entries.Count != inventory.Count
            || entries.Any(entry => !inventory.Any(item => string.Equals(item.Name, entry.Name, StringComparison.OrdinalIgnoreCase))))
        {
            var mutations = string.Join(";", InventoryMutationsForTests.Select(mutation =>
                $"{mutation.Sequence}:{mutation.RunId:D}:{mutation.RunObjectIdentity}:{mutation.Operation}:{mutation.ObjectName}:{mutation.ObjectType}:{mutation.Caller}:{mutation.ManagedThreadId}"));
            throw new NativeValidationException(
                unchecked((int)0xC0000101),
                $"A controlled native validation run contained a child outside its typed inventory. Run={_runId:D}/{_runObjectIdentity} Expected=[{string.Join(",", inventory.Select(item => item.Name))}] Actual=[{string.Join(",", entries.Select(entry => entry.Name))}] Mutations=[{mutations}].");
        }
    }

    private void DeleteExactInventoryItem(NativeValidationInventoryItem item, bool failBeforeNativeDeletion = false)
    {
        if (failBeforeNativeDeletion)
        {
            throw new InvalidOperationException("Injected controlled native validation deletion failure before native mutation.");
        }

        using (var handle = item.Kind switch
        {
            NativeValidationInventoryItemKind.RegularFile => NativeValidationFileSystem.OpenRelativeFileForDeletion(_run.Handle, item.Name),
            NativeValidationInventoryItemKind.Directory => NativeValidationFileSystem.OpenRelativeDirectoryForDeletion(_run.Handle, item.Name),
            NativeValidationInventoryItemKind.ReparsePoint => NativeValidationFileSystem.OpenRelativeReparseObjectForDeletion(_run.Handle, item.Name),
            _ => throw new InvalidOperationException("An unknown controlled native validation inventory type was encountered."),
        })
        {
            VerifyInventoryItem(handle, item);
            if (item.Kind == NativeValidationInventoryItemKind.ReparsePoint)
            {
                NativeValidationFileSystem.DeleteReparsePoint(handle, item);
            }

            NativeValidationFileSystem.MarkForDelete(handle);
        }

        VerifyDirectChildIsAbsent(item.Name);
        RemoveInventoryItem(item);
    }

    private void VerifyDirectChildIsAbsent(string name)
    {
        if (NativeValidationFileSystem.EnumerateDirectChildren(_run.Handle)
            .Any(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("A deleted controlled native validation child remained present in pinned enumeration.");
        }
    }

    private static void VerifyInventoryItem(SafeFileHandle file, NativeValidationInventoryItem expected)
    {
        var identity = NativeValidationFileSystem.ReadIdentity(file);
        switch (expected.Kind)
        {
            case NativeValidationInventoryItemKind.RegularFile:
                NativeValidationFileSystem.VerifyRegularFile(file, identity);
                break;
            case NativeValidationInventoryItemKind.Directory:
                NativeValidationFileSystem.VerifyOrdinaryDirectory(file, identity);
                break;
            case NativeValidationInventoryItemKind.ReparsePoint:
                var actualReparsePoint = NativeValidationFileSystem.ReadReparsePoint(file, identity);
                if (actualReparsePoint != expected.ReparsePoint)
                {
                    throw new InvalidOperationException("A controlled native validation reparse object changed its parsed target data.");
                }
                break;
            default:
                throw new InvalidOperationException("An unknown controlled native validation inventory type was encountered.");
        }

        var finalPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(file));
        if (identity != expected.Identity || !string.Equals(finalPath, expected.FinalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A controlled native validation inventory item was substituted or moved.");
        }
    }

    private void VerifyDirectChildPath(string finalPath, string name)
    {
        var expected = Path.Combine(RunDirectory, name);
        if (!string.Equals(finalPath, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A controlled native validation child escaped its pinned run directory.");
        }
    }

    private static string CreateSentinelContents(Guid runId, PinnedNativeValidationDirectory parent, PinnedNativeValidationDirectory run)
        => string.Join('\n',
            "PimaxVrcSupervisor controlled native validation run",
            $"run={runId:D}",
            $"parentPath={NativeValidationFileSystem.NormalizeFinalPath(parent.FinalPath)}",
            $"parentIdentity={parent.Identity}",
            $"runPath={NativeValidationFileSystem.NormalizeFinalPath(run.FinalPath)}",
            $"runIdentity={run.Identity}");

    private static void ValidateDirectFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName is "." or ".."
            || fileName != Path.GetFileName(fileName)
            || fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0
            || fileName.EndsWith(".", StringComparison.Ordinal)
            || fileName.EndsWith(" ", StringComparison.Ordinal)
            || fileName.Any(char.IsControl)
            || Path.IsPathFullyQualified(fileName))
        {
            throw new ArgumentException("Only one safe direct child name is allowed inside a controlled native validation run.", nameof(fileName));
        }
    }

    private static string NormalizeDirectoryPath(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

internal enum NativeValidationInventoryItemKind
{
    RegularFile,
    Directory,
    ReparsePoint,
}

internal enum NativeValidationReparsePointKind
{
    MountPoint,
    SymbolicLink,
}

internal readonly record struct NativeValidationReparsePoint(
    NativeValidationReparsePointKind Kind,
    string SubstituteName,
    string PrintName,
    bool IsRelative) : IDisposable
{
    public void Dispose()
    {
        // Parsed evidence never owns the native object or the run inventory.
    }
}

internal readonly record struct NativeValidationDirectoryEntry(string Name, uint Attributes);

internal readonly record struct NativeValidationInventoryItem(
    string Name,
    NativeValidationFileIdentity Identity,
    string FinalPath,
    NativeValidationInventoryItemKind Kind,
    NativeValidationReparsePoint? ReparsePoint);
internal readonly record struct NativeValidationInventoryMutation(
    long Sequence,
    Guid RunId,
    int RunObjectIdentity,
    string Operation,
    string ObjectName,
    NativeValidationInventoryItemKind ObjectType,
    string Caller,
    int ManagedThreadId);
internal readonly record struct NativeValidationFileIdentity(uint VolumeSerialNumber, ulong FileIndex, uint Attributes, uint ReparseTag);

internal sealed class PinnedNativeValidationDirectory : IDisposable
{
    internal PinnedNativeValidationDirectory(SafeFileHandle handle, NativeValidationFileIdentity identity, string finalPath)
    {
        Handle = handle;
        Identity = identity;
        FinalPath = finalPath;
    }

    internal SafeFileHandle Handle { get; }
    internal NativeValidationFileIdentity Identity { get; }
    internal string FinalPath { get; }
    public void Dispose() => Handle.Dispose();
}

/// <summary>Minimal P/Invoke surface for this test assembly only.</summary>
internal static class NativeValidationFileSystem
{
    private const int StatusSuccess = 0;
    private const int StatusNoMoreFiles = unchecked((int)0x80000006);
    private const int StatusObjectNameNotFound = unchecked((int)0xC0000034);
    private const int StatusNoSuchFile = unchecked((int)0xC000000F);
    private const uint FileReadData = 0x00000001;
    private const uint FileWriteData = 0x00000002;
    private const uint Delete = 0x00010000;
    private const uint Synchronize = 0x00100000;
    private const uint FileListDirectory = 0x00000001;
    private const uint FileAddFile = 0x00000002;
    private const uint FileAddSubdirectory = 0x00000004;
    private const uint FileReadAttributes = 0x00000080;
    private const uint ReadControl = 0x00020000;
    private const uint WriteDac = 0x00040000;
    private const uint OwnerSecurityInformation = 0x00000001;
    private const uint DaclSecurityInformation = 0x00000004;
    private const uint ProtectedDaclSecurityInformation = 0x80000000;
    private const uint FileShareNone = 0;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint FileShareReadWriteDelete = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileOpen = 0x00000001;
    private const uint FileCreate = 0x00000002;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileSynchronousIoNonAlert = 0x00000020;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int FileAttributeTagInfo = 9;
    private const int FileDirectoryInformation = 1;
    private const int FileDispositionInfo = 4;
    private const int FileRenameInformation = 10;
    private const int DirectoryInformationBufferSize = 4096;
    private const int MaximumDirectChildren = 1024;
    private const int ReparseDataBufferSize = 16 * 1024;
    private const uint FsctlGetReparsePoint = 0x000900A8;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint FsctlDeleteReparsePoint = 0x000900AC;
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const uint IoReparseTagSymbolicLink = 0xA000000C;
    private const uint ControlledUnsupportedReparseTag = 0x90000042;
    private const uint SymbolicLinkFlagRelative = 0x00000001;
    private const uint FileTypeDisk = 1;
    private const uint DriveFixed = 3;

    internal static SafeFileHandle OpenVolumeAnchor(string root)
    {
        var handle = CreateFileW("\\\\?\\" + root, FileReadAttributes | Synchronize, FileShareReadWriteDelete, IntPtr.Zero,
            OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw FromLastError("The C: volume root could not be opened.");
        }

        return handle;
    }

    internal static void ValidateFixedNtfsVolume(SafeFileHandle root, string volumeRoot)
    {
        var fileSystem = new StringBuilder(256);
        if (GetDriveTypeW(volumeRoot) != DriveFixed
            || !GetVolumeInformationByHandleW(root, null, 0, out var serial, out _, out _, fileSystem, (uint)fileSystem.Capacity)
            || serial == 0
            || !string.Equals(fileSystem.ToString(), "NTFS", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The controlled native validation root must be on a fixed NTFS volume.");
        }

        if (ReadIdentity(root).VolumeSerialNumber != serial)
        {
            throw new InvalidOperationException("The pinned validation volume identity did not match its native volume identity.");
        }
    }

    internal static PinnedNativeValidationDirectory OpenOrCreateRelativeDirectory(SafeFileHandle parent, string name, bool writable)
    {
        try
        {
            return OpenRelativeDirectory(parent, name, writable);
        }
        catch (NativeValidationException exception) when (exception.IsNotFound)
        {
            return CreateRelativeDirectory(parent, name, writable);
        }
    }

    internal static PinnedNativeValidationDirectory OpenRelativeDirectory(SafeFileHandle parent, string name, bool writable)
    {
        // The authorized parent is never deleted. Avoid requesting DELETE merely
        // to create a child so an unrelated non-delete-sharing reader cannot block
        // opening the pinned parent itself.
        var access = FileListDirectory | FileReadAttributes | Synchronize;
        if (writable)
        {
            // FILE_WRITE_DATA is required by FlushFileBuffers on the pinned
            // directory handles used by the recovery rename transaction.
            access |= FileWriteData | FileAddFile | FileAddSubdirectory;
        }

        return ValidateDirectoryHandle(NtOpenRelative(parent, name, access, FileShareReadWriteDelete, FileOpen,
            FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));
    }

    internal static PinnedNativeValidationDirectory CreateRelativeDirectory(SafeFileHandle parent, string name, bool writable)
    {
        var access = FileListDirectory | FileReadAttributes | Delete | Synchronize;
        if (writable)
        {
            access |= FileAddFile | FileAddSubdirectory;
        }

        return ValidateDirectoryHandle(NtOpenRelative(parent, name, access, FileShareReadWriteDelete, FileCreate,
            FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeDirectory));
    }

    internal static PinnedNativeValidationDirectory OpenOrCreateRelativeDirectoryForRecoverySecurity(
        SafeFileHandle parent,
        string name)
    {
        const uint access = FileListDirectory | FileReadAttributes | FileWriteData | FileAddFile | FileAddSubdirectory | ReadControl | WriteDac | Synchronize;
        try
        {
            return ValidateDirectoryHandle(NtOpenRelative(parent, name, access, FileShareReadWrite, FileOpen,
                FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));
        }
        catch (NativeValidationException exception) when (exception.IsNotFound)
        {
            return ValidateDirectoryHandle(NtOpenRelative(parent, name, access, FileShareReadWrite, FileCreate,
                FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeDirectory));
        }
    }

    internal static void SetProtectedDacl(SafeFileHandle handle, DirectorySecurity security)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(security);
        VerifyOrdinaryDirectory(handle, ReadIdentity(handle));
        var descriptor = security.GetSecurityDescriptorBinaryForm();
        if (!SetKernelObjectSecurity(handle, DaclSecurityInformation | ProtectedDaclSecurityInformation, descriptor))
        {
            throw FromLastError("The pinned directory DACL could not be applied.");
        }
    }

    internal static DirectorySecurity ReadDirectorySecurity(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        VerifyOrdinaryDirectory(handle, ReadIdentity(handle));
        if (GetKernelObjectSecurity(handle, OwnerSecurityInformation | DaclSecurityInformation, null, 0, out var length)
            || length == 0)
        {
            throw FromLastError("The pinned directory security descriptor size could not be read.");
        }

        var descriptor = new byte[length];
        if (!GetKernelObjectSecurity(handle, OwnerSecurityInformation | DaclSecurityInformation, descriptor, length, out var returned)
            || returned != length)
        {
            throw FromLastError("The pinned directory security descriptor could not be read.");
        }

        var security = new DirectorySecurity();
        security.SetSecurityDescriptorBinaryForm(descriptor);
        return security;
    }

    internal static SafeFileHandle CreateRelativeMountPointObject(SafeFileHandle parent, string name)
    {
        var handle = NtOpenRelative(parent, name, FileWriteData | Delete | FileReadAttributes | Synchronize,
            FileShareNone, FileCreate, FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeDirectory);
        try
        {
            VerifyOrdinaryDirectory(handle, ReadIdentity(handle));
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static void AssignMountPoint(SafeFileHandle handle, string targetDirectory)
    {
        VerifyOrdinaryDirectory(handle, ReadIdentity(handle));
        SetMountPointReparseData(handle, targetDirectory);
        var identity = ReadIdentity(handle);
        var reparsePoint = ReadReparsePoint(handle, identity);
        if (identity.ReparseTag != IoReparseTagMountPoint || reparsePoint.Kind != NativeValidationReparsePointKind.MountPoint)
        {
            throw new InvalidOperationException("A controlled native validation junction was not created as a mount-point reparse object.");
        }
    }

    internal static void AssignRelativeSymbolicLink(SafeFileHandle handle, string relativeTarget)
    {
        ValidateRelativeSymbolicLinkTarget(relativeTarget);
        var before = ReadIdentity(handle);
        VerifyRegularFile(handle, before);
        var finalPath = NormalizeFinalPath(GetFinalPath(handle));
        var buffer = BuildRelativeSymbolicLinkReparseBuffer(relativeTarget);
        if (!DeviceIoControlSet(handle, FsctlSetReparsePoint, buffer, checked((uint)buffer.Length), IntPtr.Zero, 0, out var bytesReturned, IntPtr.Zero)
            || bytesReturned != 0)
        {
            throw FromLastError("A controlled native validation symbolic link could not be assigned its relative reparse data.");
        }

        var after = ReadIdentity(handle);
        var reparsePoint = ReadReparsePoint(handle, after);
        if (!SameObjectIdentity(before, after)
            || after.ReparseTag != IoReparseTagSymbolicLink
            || reparsePoint.Kind != NativeValidationReparsePointKind.SymbolicLink
            || !reparsePoint.IsRelative
            || !string.Equals(reparsePoint.SubstituteName, relativeTarget, StringComparison.Ordinal)
            || !string.Equals(reparsePoint.PrintName, relativeTarget, StringComparison.Ordinal)
            || !string.Equals(NormalizeFinalPath(GetFinalPath(handle)), finalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A controlled native validation symbolic link failed exact native round-trip validation.");
        }
    }

    internal static byte[] BuildRelativeSymbolicLinkReparseBufferForTests(string relativeTarget)
        => BuildRelativeSymbolicLinkReparseBuffer(relativeTarget);

    internal static uint AssignUnsupportedReparsePoint(SafeFileHandle handle)
    {
        var before = ReadIdentity(handle);
        if ((before.Attributes & FileAttributeReparsePoint) != 0 || before.ReparseTag != 0 || GetFileType(handle) != FileTypeDisk)
        {
            throw new InvalidOperationException("A controlled native validation unsupported reparse object was not an ordinary disk object before assignment.");
        }
        var buffer = new byte[12];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, sizeof(uint)), ControlledUnsupportedReparseTag);
        BitConverter.TryWriteBytes(buffer.AsSpan(4, sizeof(ushort)), checked((ushort)4));
        buffer[8] = 0x46;
        buffer[9] = 0x33;
        buffer[10] = 0x33;
        buffer[11] = 0x42;
        if (!DeviceIoControlSet(handle, FsctlSetReparsePoint, buffer, checked((uint)buffer.Length), IntPtr.Zero, 0, out var bytesReturned, IntPtr.Zero)
            || bytesReturned != 0)
        {
            throw FromLastError("A controlled native validation unsupported reparse point could not be assigned.");
        }

        var after = ReadIdentity(handle);
        if (!SameObjectIdentity(before, after) || after.ReparseTag != ControlledUnsupportedReparseTag)
        {
            throw new InvalidOperationException("A controlled native validation unsupported reparse point changed identity or tag.");
        }
        ValidateUnsupportedReparsePoint(handle, after);
        return ControlledUnsupportedReparseTag;
    }

    internal static void ValidateUnsupportedReparsePoint(SafeFileHandle handle, NativeValidationFileIdentity identity)
    {
        if (identity.ReparseTag != ControlledUnsupportedReparseTag)
        {
            throw new InvalidOperationException("A controlled native validation unsupported reparse point had the wrong tag.");
        }

        var buffer = new byte[ReparseDataBufferSize];
        if (!DeviceIoControl(handle, FsctlGetReparsePoint, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out var bytesReturned, IntPtr.Zero)
            || bytesReturned != 12
            || BitConverter.ToUInt32(buffer, 0) != ControlledUnsupportedReparseTag
            || BitConverter.ToUInt16(buffer, 4) != 4
            || !buffer.AsSpan(8, 4).SequenceEqual(new byte[] { 0x46, 0x33, 0x33, 0x42 }))
        {
            throw new InvalidOperationException("A controlled native validation unsupported reparse point failed exact round-trip validation.");
        }
    }

    internal static NativeValidationReparsePoint ParseReparsePointBufferForTests(byte[] buffer, int bytesReturned, uint expectedTag)
        => ParseReparsePointBuffer(buffer, bytesReturned, expectedTag);

    internal static uint SymbolicLinkTagForTests => IoReparseTagSymbolicLink;

    internal static SafeFileHandle CreateRelativeFile(SafeFileHandle parent, string name)
        => ValidateRegularFileHandle(NtOpenRelative(parent, name, FileWriteData | Delete | FileReadAttributes | Synchronize,
            FileShareNone, FileCreate, FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));

    internal static SafeFileHandle OpenRelativeFile(SafeFileHandle parent, string name)
        => ValidateRegularFileHandle(NtOpenRelative(parent, name, FileReadData | FileReadAttributes | Synchronize,
            FileShareNone, FileOpen, FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));

    internal static SafeFileHandle OpenRelativeFileForDeletion(SafeFileHandle parent, string name)
        => ValidateRegularFileHandle(NtOpenRelative(parent, name, Delete | FileReadAttributes | Synchronize,
            FileShareNone, FileOpen, FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));

    internal static SafeFileHandle OpenRelativeDirectoryForRecoveryRename(SafeFileHandle parent, string name)
        => ValidateOrdinaryDirectoryFileHandle(NtOpenRelative(parent, name, FileListDirectory | Delete | FileReadAttributes | Synchronize,
            FileShareReadWriteDelete, FileOpen, FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));

    internal static SafeFileHandle OpenRelativeDirectoryForDeletion(SafeFileHandle parent, string name)
        => ValidateOrdinaryDirectoryFileHandle(NtOpenRelative(parent, name, FileListDirectory | Delete | FileReadAttributes | Synchronize,
            FileShareNone, FileOpen, FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));

    internal static SafeFileHandle OpenRelativeReparseObjectForDeletion(SafeFileHandle parent, string name)
        => ValidateReparsePointHandle(NtOpenRelative(parent, name, FileWriteData | Delete | FileReadAttributes | Synchronize,
            FileShareNone, FileOpen, FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));

    internal static SafeFileHandle OpenRelativeDirectoryReparseObjectForDeletion(SafeFileHandle parent, string name)
        => ValidateReparsePointHandle(NtOpenRelative(parent, name, FileWriteData | Delete | FileReadAttributes | Synchronize,
            FileShareNone, FileOpen, FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));


    internal static SafeFileHandle OpenRelativeReparseObjectForInspection(SafeFileHandle parent, string name)
        => ValidateReparsePointHandle(NtOpenRelative(parent, name, FileReadAttributes | Synchronize,
            FileShareReadWriteDelete, FileOpen, FileSynchronousIoNonAlert | FileOpenReparsePoint, FileAttributeNormal));

    internal static PinnedNativeValidationDirectory ValidateDirectoryHandle(SafeFileHandle handle)
    {
        try
        {
            var identity = ReadIdentity(handle);
            if ((identity.Attributes & FileAttributeDirectory) == 0 || (identity.Attributes & FileAttributeReparsePoint) != 0 || identity.ReparseTag != 0)
            {
                throw new InvalidOperationException("A controlled native validation directory was a reparse point or not an ordinary directory.");
            }

            return new PinnedNativeValidationDirectory(handle, identity, GetFinalPath(handle));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static SafeFileHandle ValidateRegularFileHandle(SafeFileHandle handle)
    {
        try
        {
            var identity = ReadIdentity(handle);
            VerifyRegularFile(handle, identity);
            _ = GetFinalPath(handle);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static SafeFileHandle ValidateOrdinaryDirectoryFileHandle(SafeFileHandle handle)
    {
        try
        {
            VerifyOrdinaryDirectory(handle, ReadIdentity(handle));
            _ = GetFinalPath(handle);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static SafeFileHandle ValidateReparsePointHandle(SafeFileHandle handle)
    {
        try
        {
            var identity = ReadIdentity(handle);
            if ((identity.Attributes & FileAttributeReparsePoint) == 0 || identity.ReparseTag == 0)
            {
                throw new InvalidOperationException("A controlled native validation reparse object was not a reparse point.");
            }
            _ = GetFinalPath(handle);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static void VerifyRegularFile(SafeFileHandle handle, NativeValidationFileIdentity identity)
    {
        if ((identity.Attributes & (FileAttributeDirectory | FileAttributeReparsePoint)) != 0 || identity.ReparseTag != 0 || GetFileType(handle) != FileTypeDisk)
        {
            throw new InvalidOperationException("A controlled native validation file was not an ordinary disk file.");
        }
    }

    internal static void VerifyOrdinaryDirectory(SafeFileHandle handle, NativeValidationFileIdentity identity)
    {
        if ((identity.Attributes & FileAttributeDirectory) == 0 || (identity.Attributes & FileAttributeReparsePoint) != 0 || identity.ReparseTag != 0)
        {
            throw new InvalidOperationException("A controlled native validation directory was a reparse point or not an ordinary directory.");
        }
    }

    internal static void VerifyDirectoryLocation(PinnedNativeValidationDirectory directory, string expectedPath)
    {
        var actual = NormalizeFinalPath(directory.FinalPath);
        if (!string.Equals(actual, NormalizeFinalPath(expectedPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A pinned controlled native validation directory escaped its authorized path.");
        }
    }

    internal static void VerifyPinnedDirectory(PinnedNativeValidationDirectory directory, string expectedPath)
    {
        var identity = ReadIdentity(directory.Handle);
        if (identity != directory.Identity)
        {
            throw new InvalidOperationException("A pinned controlled native validation directory identity changed.");
        }

        if ((identity.Attributes & FileAttributeDirectory) == 0 || (identity.Attributes & FileAttributeReparsePoint) != 0 || identity.ReparseTag != 0)
        {
            throw new InvalidOperationException("A pinned controlled native validation directory became unsafe.");
        }

        var actual = NormalizeFinalPath(GetFinalPath(directory.Handle));
        if (!string.Equals(actual, NormalizeFinalPath(expectedPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A pinned controlled native validation directory was renamed or substituted.");
        }
    }

    internal static NativeValidationFileIdentity ReadIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)
            || !GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out var tag, (uint)Marshal.SizeOf<FileAttributeTagInformation>()))
        {
            throw FromLastError("A controlled native validation identity could not be read.");
        }

        return new NativeValidationFileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, tag.FileAttributes, tag.ReparseTag);
    }

    internal static IReadOnlyList<NativeValidationDirectoryEntry> EnumerateDirectChildren(SafeFileHandle directory)
    {
        var entries = new List<NativeValidationDirectoryEntry>();
        var buffer = Marshal.AllocHGlobal(DirectoryInformationBufferSize);
        try
        {
            var restartScan = true;
            while (true)
            {
                var ioStatus = default(IoStatusBlock);
                var status = NtQueryDirectoryFile(
                    directory,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    ref ioStatus,
                    buffer,
                    DirectoryInformationBufferSize,
                    FileDirectoryInformation,
                    returnSingleEntry: true,
                    IntPtr.Zero,
                    restartScan);
                restartScan = false;
                if (status == StatusNoMoreFiles)
                {
                    return entries;
                }

                if (status != StatusSuccess)
                {
                    throw new NativeValidationException(status, $"A controlled native validation directory could not be enumerated (NTSTATUS=0x{status:X8}).");
                }

                var bytesReturned = ioStatus.Information.ToInt64();
                if (bytesReturned < 64 || bytesReturned > DirectoryInformationBufferSize)
                {
                    throw new InvalidOperationException("A controlled native validation directory returned an invalid entry size.");
                }

                var nameLength = Marshal.ReadInt32(buffer, 60);
                if (nameLength <= 0 || nameLength % sizeof(char) != 0 || 64L + nameLength > bytesReturned)
                {
                    throw new InvalidOperationException("A controlled native validation directory returned an invalid child name.");
                }

                var name = Marshal.PtrToStringUni(IntPtr.Add(buffer, 64), nameLength / sizeof(char));
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException("A controlled native validation directory returned an invalid child name.");
                }

                // NTFS reports the directory self and parent pseudo-entries even
                // though they are not direct children and cannot be inventory items.
                if (name is "." or "..")
                {
                    continue;
                }

                entries.Add(new NativeValidationDirectoryEntry(name, unchecked((uint)Marshal.ReadInt32(buffer, 56))));
                if (entries.Count > MaximumDirectChildren)
                {
                    throw new InvalidOperationException("A controlled native validation directory exceeded its bounded child enumeration limit.");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static NativeValidationReparsePoint ReadReparsePoint(SafeFileHandle handle, NativeValidationFileIdentity identity)
    {
        if ((identity.Attributes & FileAttributeReparsePoint) == 0 || identity.ReparseTag == 0)
        {
            throw new InvalidOperationException("A controlled native validation reparse object was not a reparse point.");
        }

        var buffer = new byte[ReparseDataBufferSize];
        if (!DeviceIoControl(handle, FsctlGetReparsePoint, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out var bytesReturned, IntPtr.Zero))
        {
            throw FromLastError("A controlled native validation reparse object could not be read.");
        }

        return ParseReparsePointBuffer(buffer, checked((int)bytesReturned), identity.ReparseTag);
    }

    private static NativeValidationReparsePoint ParseReparsePointBuffer(byte[] buffer, int bytesReturned, uint expectedTag)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (bytesReturned < 8 || bytesReturned > buffer.Length || bytesReturned > ReparseDataBufferSize)
        {
            throw new InvalidOperationException("A controlled native validation reparse object returned an invalid buffer length.");
        }

        var tag = BitConverter.ToUInt32(buffer, 0);
        var totalLength = checked(8 + BitConverter.ToUInt16(buffer, 4));
        if (tag != expectedTag || totalLength != bytesReturned)
        {
            throw new InvalidOperationException("A controlled native validation reparse object returned inconsistent tag data.");
        }

        return tag switch
        {
            IoReparseTagMountPoint => ParseMountPointReparsePoint(buffer, totalLength),
            IoReparseTagSymbolicLink => ParseSymbolicLinkReparsePoint(buffer, totalLength),
            _ => throw new InvalidOperationException("Only mount-point and symbolic-link reparse objects are allowed in controlled native validation runs."),
        };
    }

    internal static string ToMountPointSubstituteName(string targetDirectory)
    {
        var normalizedTarget = NormalizeFinalPath(targetDirectory);
        if (!Path.IsPathFullyQualified(normalizedTarget) || normalizedTarget.Length == 0)
        {
            throw new ArgumentException("A controlled native validation mount-point target must be an absolute directory path.", nameof(targetDirectory));
        }

        return @"\??\" + normalizedTarget;
    }

    private static void SetMountPointReparseData(SafeFileHandle handle, string targetDirectory)
    {
        var buffer = BuildMountPointReparseBuffer(targetDirectory);
        if (!DeviceIoControlSet(handle, FsctlSetReparsePoint, buffer, (uint)buffer.Length, IntPtr.Zero, 0, out var bytesReturned, IntPtr.Zero)
            || bytesReturned != 0)
        {
            throw FromLastError("A controlled native validation junction could not be assigned its mount-point reparse data.");
        }
    }

    private static byte[] BuildMountPointReparseBuffer(string targetDirectory)
    {
        var printName = NormalizeFinalPath(targetDirectory);
        var substituteName = ToMountPointSubstituteName(printName);
        var substituteBytes = Encoding.Unicode.GetBytes(substituteName);
        var printBytes = Encoding.Unicode.GetBytes(printName);
        var pathBufferLength = checked(substituteBytes.Length + sizeof(char) + printBytes.Length + sizeof(char));
        var dataLength = checked(8 + pathBufferLength);
        var totalLength = checked(8 + dataLength);
        if (substituteBytes.Length == 0
            || substituteBytes.Length > ushort.MaxValue
            || printBytes.Length > ushort.MaxValue
            || dataLength > ushort.MaxValue
            || totalLength > ReparseDataBufferSize)
        {
            throw new ArgumentException("A controlled native validation mount-point target exceeds the bounded reparse buffer.", nameof(targetDirectory));
        }

        var buffer = new byte[totalLength];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, sizeof(uint)), IoReparseTagMountPoint);
        BitConverter.TryWriteBytes(buffer.AsSpan(4, sizeof(ushort)), checked((ushort)dataLength));
        BitConverter.TryWriteBytes(buffer.AsSpan(8, sizeof(ushort)), (ushort)0);
        BitConverter.TryWriteBytes(buffer.AsSpan(10, sizeof(ushort)), checked((ushort)substituteBytes.Length));
        BitConverter.TryWriteBytes(buffer.AsSpan(12, sizeof(ushort)), checked((ushort)(substituteBytes.Length + sizeof(char))));
        BitConverter.TryWriteBytes(buffer.AsSpan(14, sizeof(ushort)), checked((ushort)printBytes.Length));
        substituteBytes.CopyTo(buffer, 16);
        printBytes.CopyTo(buffer, 16 + substituteBytes.Length + sizeof(char));
        return buffer;
    }

    private static byte[] BuildRelativeSymbolicLinkReparseBuffer(string relativeTarget)
    {
        ValidateRelativeSymbolicLinkTarget(relativeTarget);
        var substituteBytes = Encoding.Unicode.GetBytes(relativeTarget);
        var printBytes = Encoding.Unicode.GetBytes(relativeTarget);
        var pathBufferLength = checked(substituteBytes.Length + sizeof(char) + printBytes.Length + sizeof(char));
        var dataLength = checked(12 + pathBufferLength);
        var totalLength = checked(8 + dataLength);
        if (substituteBytes.Length == 0
            || substituteBytes.Length > ushort.MaxValue
            || printBytes.Length > ushort.MaxValue
            || dataLength > ushort.MaxValue
            || totalLength > ReparseDataBufferSize)
        {
            throw new ArgumentException("A controlled native validation symbolic-link target exceeds the bounded reparse buffer.", nameof(relativeTarget));
        }

        var buffer = new byte[totalLength];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, sizeof(uint)), IoReparseTagSymbolicLink);
        BitConverter.TryWriteBytes(buffer.AsSpan(4, sizeof(ushort)), checked((ushort)dataLength));
        BitConverter.TryWriteBytes(buffer.AsSpan(8, sizeof(ushort)), (ushort)0);
        BitConverter.TryWriteBytes(buffer.AsSpan(10, sizeof(ushort)), checked((ushort)substituteBytes.Length));
        BitConverter.TryWriteBytes(buffer.AsSpan(12, sizeof(ushort)), checked((ushort)(substituteBytes.Length + sizeof(char))));
        BitConverter.TryWriteBytes(buffer.AsSpan(14, sizeof(ushort)), checked((ushort)printBytes.Length));
        BitConverter.TryWriteBytes(buffer.AsSpan(16, sizeof(uint)), SymbolicLinkFlagRelative);
        substituteBytes.CopyTo(buffer, 20);
        printBytes.CopyTo(buffer, 20 + substituteBytes.Length + sizeof(char));
        return buffer;
    }

    private static NativeValidationReparsePoint ParseMountPointReparsePoint(byte[] buffer, int totalLength)
    {
        const int pathBufferOffset = 16;
        if (totalLength < pathBufferOffset || BitConverter.ToUInt16(buffer, 6) != 0)
        {
            throw new InvalidOperationException("A controlled native validation mount-point reparse object had an invalid header.");
        }

        var substituteOffset = BitConverter.ToUInt16(buffer, 8);
        var substituteLength = BitConverter.ToUInt16(buffer, 10);
        var printOffset = BitConverter.ToUInt16(buffer, 12);
        var printLength = BitConverter.ToUInt16(buffer, 14);
        var pathBufferLength = totalLength - pathBufferOffset;
        if (substituteOffset != 0
            || substituteLength == 0
            || substituteLength % sizeof(char) != 0
            || printOffset != substituteLength + sizeof(char)
            || printLength % sizeof(char) != 0
            || printOffset + printLength + sizeof(char) != pathBufferLength
            || buffer[pathBufferOffset + substituteLength] != 0
            || buffer[pathBufferOffset + substituteLength + 1] != 0
            || buffer[pathBufferOffset + printOffset + printLength] != 0
            || buffer[pathBufferOffset + printOffset + printLength + 1] != 0)
        {
            throw new InvalidOperationException("A controlled native validation mount-point reparse object did not use the exact bounded name layout.");
        }

        var substituteName = ReadReparseName(buffer, pathBufferOffset, totalLength, substituteOffset, substituteLength);
        var printName = ReadReparseName(buffer, pathBufferOffset, totalLength, printOffset, printLength);
        if (!substituteName.StartsWith(@"\??\", StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(printName)
            || !string.Equals(substituteName, ToMountPointSubstituteName(printName), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A controlled native validation mount-point reparse object had an invalid target identity.");
        }

        return new NativeValidationReparsePoint(NativeValidationReparsePointKind.MountPoint, substituteName, printName, IsRelative: false);
    }

    private static NativeValidationReparsePoint ParseSymbolicLinkReparsePoint(byte[] buffer, int totalLength)
    {
        const int pathBufferOffset = 20;
        if (totalLength < pathBufferOffset)
        {
            throw new InvalidOperationException("A controlled native validation reparse object had a truncated name buffer.");
        }

        var substituteOffset = BitConverter.ToUInt16(buffer, 8);
        var substituteLength = BitConverter.ToUInt16(buffer, 10);
        var printOffset = BitConverter.ToUInt16(buffer, 12);
        var printLength = BitConverter.ToUInt16(buffer, 14);
        var flags = BitConverter.ToUInt32(buffer, 16);
        var pathBufferLength = totalLength - pathBufferOffset;
        if (BitConverter.ToUInt16(buffer, 6) != 0
            || flags != SymbolicLinkFlagRelative
            || substituteOffset != 0
            || substituteLength == 0
            || substituteLength % sizeof(char) != 0
            || printOffset != substituteLength + sizeof(char)
            || printLength != substituteLength
            || printLength % sizeof(char) != 0
            || printOffset + printLength + sizeof(char) != pathBufferLength
            || buffer[pathBufferOffset + substituteLength] != 0
            || buffer[pathBufferOffset + substituteLength + 1] != 0
            || buffer[pathBufferOffset + printOffset + printLength] != 0
            || buffer[pathBufferOffset + printOffset + printLength + 1] != 0)
        {
            throw new InvalidOperationException("A controlled native validation symbolic link did not use the exact bounded relative-name layout.");
        }

        var substituteName = ReadReparseName(buffer, pathBufferOffset, totalLength, substituteOffset, substituteLength);
        var printName = ReadReparseName(buffer, pathBufferOffset, totalLength, printOffset, printLength);
        ValidateRelativeSymbolicLinkTarget(substituteName);
        if (!string.Equals(substituteName, printName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A controlled native validation symbolic link did not preserve identical substitute and print names.");
        }

        return new NativeValidationReparsePoint(NativeValidationReparsePointKind.SymbolicLink, substituteName, printName, IsRelative: true);
    }

    private static void ValidateRelativeSymbolicLinkTarget(string relativeTarget)
    {
        if (string.IsNullOrWhiteSpace(relativeTarget)
            || relativeTarget is "." or ".."
            || relativeTarget != Path.GetFileName(relativeTarget)
            || relativeTarget.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0
            || relativeTarget.EndsWith(".", StringComparison.Ordinal)
            || relativeTarget.EndsWith(" ", StringComparison.Ordinal)
            || relativeTarget.Any(char.IsControl)
            || Path.IsPathFullyQualified(relativeTarget))
        {
            throw new ArgumentException("A controlled native validation symbolic-link target must be one safe relative direct-child name.", nameof(relativeTarget));
        }
    }

    private static string ReadReparseName(byte[] buffer, int pathBufferOffset, int totalLength, int offset, int length)
    {
        var pathBufferLength = totalLength - pathBufferOffset;
        if (offset < 0 || length < 0 || offset % sizeof(char) != 0 || length % sizeof(char) != 0 || offset + length > pathBufferLength)
        {
            throw new InvalidOperationException("A controlled native validation reparse object had an invalid name offset.");
        }

        return Encoding.Unicode.GetString(buffer, pathBufferOffset + offset, length);
    }

    internal static void DeleteReparsePoint(SafeFileHandle handle, NativeValidationInventoryItem expected)
    {
        if (expected.Kind != NativeValidationInventoryItemKind.ReparsePoint || expected.ReparsePoint is not { } expectedReparsePoint)
        {
            throw new InvalidOperationException("Only an exactly inventoried controlled native validation reparse object can be removed.");
        }

        var before = ReadIdentity(handle);
        var actual = ReadReparsePoint(handle, before);
        if (before != expected.Identity || actual != expectedReparsePoint)
        {
            throw new InvalidOperationException("A controlled native validation reparse object changed before handle-only removal.");
        }

        var deleteBuffer = new byte[8];
        BitConverter.TryWriteBytes(deleteBuffer.AsSpan(0, sizeof(uint)), before.ReparseTag);
        if (!DeviceIoControlSet(handle, FsctlDeleteReparsePoint, deleteBuffer, (uint)deleteBuffer.Length, IntPtr.Zero, 0, out var bytesReturned, IntPtr.Zero)
            || bytesReturned != 0)
        {
            throw FromLastError("A controlled native validation reparse object could not be cleared by its own handle.");
        }

        var after = ReadIdentity(handle);
        var finalPath = NormalizeFinalPath(GetFinalPath(handle));
        if (!SameObjectIdentity(before, after)
            || (after.Attributes & FileAttributeReparsePoint) != 0
            || after.ReparseTag != 0
            || !string.Equals(finalPath, expected.FinalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A controlled native validation reparse object did not become its exact ordinary object after handle-only removal.");
        }
    }

    internal static void DeleteUnsupportedReparsePoint(
        SafeFileHandle handle,
        NativeValidationFileIdentity expectedIdentity,
        uint expectedTag,
        string expectedFinalPath)
    {
        var before = ReadIdentity(handle);
        if (before != expectedIdentity || before.ReparseTag != expectedTag || expectedTag != ControlledUnsupportedReparseTag)
        {
            throw new InvalidOperationException("A controlled native validation unsupported reparse object changed before handle-only removal.");
        }
        ValidateUnsupportedReparsePoint(handle, before);
        var deleteBuffer = new byte[8];
        BitConverter.TryWriteBytes(deleteBuffer.AsSpan(0, sizeof(uint)), expectedTag);
        if (!DeviceIoControlSet(handle, FsctlDeleteReparsePoint, deleteBuffer, (uint)deleteBuffer.Length, IntPtr.Zero, 0, out var bytesReturned, IntPtr.Zero)
            || bytesReturned != 0)
        {
            throw FromLastError("A controlled native validation unsupported reparse object could not be cleared by its own handle.");
        }

        var after = ReadIdentity(handle);
        if (!SameObjectIdentity(before, after)
            || (after.Attributes & FileAttributeReparsePoint) != 0
            || after.ReparseTag != 0
            || !string.Equals(NormalizeFinalPath(GetFinalPath(handle)), expectedFinalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A controlled native validation unsupported reparse object did not become its exact ordinary object after handle-only removal.");
        }
    }

    internal static bool SameObjectIdentity(NativeValidationFileIdentity left, NativeValidationFileIdentity right)
        => left.VolumeSerialNumber == right.VolumeSerialNumber && left.FileIndex == right.FileIndex;

    internal static string GetFinalPath(SafeFileHandle handle)
    {
        var capacity = 1024u;
        while (capacity <= 32768)
        {
            var builder = new StringBuilder((int)capacity);
            var result = GetFinalPathNameByHandleW(handle, builder, capacity, 0);
            if (result == 0)
            {
                throw FromLastError("A controlled native validation final path could not be read.");
            }

            if (result < capacity)
            {
                return builder.ToString();
            }

            capacity = result + 1;
        }

        throw new InvalidOperationException("A controlled native validation final path was too long.");
    }

    internal static string NormalizeFinalPath(string path)
    {
        const string devicePrefix = "\\\\?\\";
        var normalized = path.StartsWith(devicePrefix, StringComparison.Ordinal) ? path[devicePrefix.Length..] : path;
        return Path.GetFullPath(normalized).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static void MarkForDelete(SafeFileHandle handle)
    {
        var disposition = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(disposition, 1);
            if (!SetFileInformationByHandle(handle, FileDispositionInfo, disposition, sizeof(int)))
            {
                throw FromLastError("A controlled native validation inventory item could not be deleted.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(disposition);
        }
    }

    /// <summary>
    /// Renames an already-opened direct child below an already-pinned destination
    /// parent. The native FILE_RENAME_INFORMATION buffer contains only one direct
    /// destination name and ReplaceIfExists is always false.
    /// </summary>
    internal static void RenameRelativeNoReplace(SafeFileHandle source, SafeFileHandle destinationParent, string destinationName)
    {
        ValidateDirectRenameName(destinationName);
        var nameBytes = Encoding.Unicode.GetBytes(destinationName);
        var rootDirectoryOffset = IntPtr.Size == sizeof(long) ? 8 : 4;
        var fileNameLengthOffset = checked(rootDirectoryOffset + IntPtr.Size);
        var fileNameOffset = checked(fileNameLengthOffset + sizeof(uint));
        var bufferLength = checked(fileNameOffset + nameBytes.Length);
        var buffer = Marshal.AllocHGlobal(bufferLength);
        var sourceReferenced = false;
        var destinationReferenced = false;
        try
        {
            Marshal.Copy(new byte[bufferLength], 0, buffer, bufferLength);
            source.DangerousAddRef(ref sourceReferenced);
            destinationParent.DangerousAddRef(ref destinationReferenced);
            Marshal.WriteByte(buffer, 0, 0); // ReplaceIfExists = FALSE.
            Marshal.WriteIntPtr(buffer, rootDirectoryOffset, destinationParent.DangerousGetHandle());
            Marshal.WriteInt32(buffer, fileNameLengthOffset, nameBytes.Length);
            Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, fileNameOffset), nameBytes.Length);
            var status = NtSetInformationFile(source, out _, buffer, (uint)bufferLength, FileRenameInformation);
            if (status != StatusSuccess)
            {
                throw new NativeValidationException(status, $"A controlled native validation child could not be recovery-renamed (NTSTATUS=0x{status:X8}).");
            }
        }
        finally
        {
            if (destinationReferenced)
            {
                destinationParent.DangerousRelease();
            }

            if (sourceReferenced)
            {
                source.DangerousRelease();
            }

            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static void VerifyDirectDirectoryChildIsAbsent(SafeFileHandle parent, string name)
    {
        try
        {
            using var unexpected = OpenRelativeDirectory(parent, name, writable: false);
            throw new InvalidOperationException("A recovery-renamed direct child remained reachable below the pinned source parent.");
        }
        catch (NativeValidationException exception) when (exception.IsNotFound)
        {
            return;
        }
    }

    internal static void FlushDirectory(SafeFileHandle directory)
    {
        var status = NtFlushBuffersFile(directory, out _);
        if (status != StatusSuccess)
        {
            throw new NativeValidationException(status, $"A controlled native validation directory could not be flushed (NTSTATUS=0x{status:X8}).");
        }
    }

    internal static void FlushPinnedFile(SafeFileHandle file)
    {
        var status = NtFlushBuffersFile(file, out _);
        if (status != StatusSuccess)
        {
            throw new NativeValidationException(status, $"A controlled native validation file could not be flushed (NTSTATUS=0x{status:X8}).");
        }
    }

    internal static string ReadBoundedUtf8File(SafeFileHandle file, int maximumBytes)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        var length = RandomAccess.GetLength(file);
        if (length < 0 || length > maximumBytes)
        {
            throw new InvalidOperationException("A controlled native validation file exceeded its bounded read limit.");
        }

        var bytes = new byte[checked((int)length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = RandomAccess.Read(file, bytes.AsSpan(offset), offset);
            if (read == 0)
            {
                throw new EndOfStreamException("A controlled native validation file became truncated during its bounded read.");
            }

            offset = checked(offset + read);
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
    }

    private static void ValidateDirectRenameName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name != Path.GetFileName(name)
            || name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0
            || name.EndsWith(".", StringComparison.Ordinal)
            || name.EndsWith(" ", StringComparison.Ordinal)
            || name.Any(char.IsControl)
            || Path.IsPathFullyQualified(name))
        {
            throw new ArgumentException("A recovery rename destination must be one safe direct child name.", nameof(name));
        }
    }

    private static SafeFileHandle NtOpenRelative(SafeFileHandle parent, string name, uint access, uint shareAccess, uint disposition, uint options, uint attributes)
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
                Attributes = 0x00000040,
            };
            var status = NtCreateFile(out var handle, access, ref objectAttributes, out _, IntPtr.Zero, attributes, shareAccess, disposition, options, IntPtr.Zero, 0);
            if (status != StatusSuccess)
            {
                handle?.Dispose();
                throw new NativeValidationException(status, $"A controlled native validation child could not be opened (NTSTATUS=0x{status:X8}).");
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

    private static NativeValidationException FromLastError(string message)
        => new(Marshal.GetLastWin32Error(), message + $" (Win32={Marshal.GetLastWin32Error()}).");

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

    private sealed class NativeUnicodeString : IDisposable
    {
        private readonly IntPtr _buffer;
        private IntPtr _structure;

        internal NativeUnicodeString(string value)
        {
            _buffer = Marshal.StringToHGlobalUni(value);
            var native = new UnicodeString
            {
                Length = checked((ushort)(value.Length * sizeof(char))),
                MaximumLength = checked((ushort)(value.Length * sizeof(char) + sizeof(char))),
                Buffer = _buffer,
            };
            _structure = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(native, _structure, false);
        }

        internal IntPtr Pointer => _structure;

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetDriveTypeW(string rootPathName);

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

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint securityInformation, [In] byte[] securityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKernelObjectSecurity(SafeFileHandle handle, uint securityInformation, [Out] byte[]? securityDescriptor, uint length, out uint requiredLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr inputBuffer, uint inputBufferSize, [Out] byte[] outputBuffer, uint outputBufferSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlSet(SafeFileHandle device, uint controlCode, [In] byte[] inputBuffer, uint inputBufferSize, IntPtr outputBuffer, uint outputBufferSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryDirectoryFile(
        SafeFileHandle fileHandle,
        IntPtr eventHandle,
        IntPtr apcRoutine,
        IntPtr apcContext,
        ref IoStatusBlock ioStatusBlock,
        IntPtr fileInformation,
        int length,
        int fileInformationClass,
        [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry,
        IntPtr fileName,
        [MarshalAs(UnmanagedType.U1)] bool restartScan);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out SafeFileHandle fileHandle, uint desiredAccess, ref ObjectAttributes objectAttributes, out IoStatusBlock ioStatusBlock,
        IntPtr allocationSize, uint fileAttributes, uint shareAccess, uint createDisposition, uint createOptions, IntPtr eaBuffer, uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(SafeFileHandle fileHandle, out IoStatusBlock ioStatusBlock, IntPtr fileInformation, uint length, int fileInformationClass);

    [DllImport("ntdll.dll")]
    private static extern int NtFlushBuffersFile(SafeFileHandle fileHandle, out IoStatusBlock ioStatusBlock);
}

internal sealed class NativeValidationException(int status, string message) : IOException(message)
{
    internal int Status => status;
    internal bool IsNotFound => status is unchecked((int)0xC0000034) or unchecked((int)0xC000000F);
}
