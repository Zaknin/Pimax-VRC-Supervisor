using Microsoft.Win32.SafeHandles;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

internal sealed class ControlledNativeStagingWorkspace : IDisposable
{
    private readonly ControlledNativeValidationRun _run;
    private readonly List<WorkspaceItem> _inventory = [];
    private bool _disposed;

    internal ControlledNativeStagingWorkspace()
    {
        _run = ControlledNativeValidationRun.Create();
        UpdateRoot = _run.CreateDirectoryForTests("staging");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var currentUser = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current user SID is unavailable.");
        security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(UpdateRoot).SetAccessControl(security);
    }

    internal string UpdateRoot { get; }
    internal ControlledNativeValidationRun Run => _run;

    internal string PathFor(params string[] components)
        => components.Aggregate(UpdateRoot, Path.Combine);

    internal NativeValidationFileIdentity CreateDirectory(params string[] components)
    {
        ValidatePath(components);
        using var parent = OpenParent(components);
        using var directory = NativeValidationFileSystem.CreateRelativeDirectory(parent.Handle, components[^1], writable: true);
        ApplyCompliantDacl(PathFor(components));
        var item = CaptureItem(components, WorkspaceItemKind.Directory, directory.Handle, expectedReparsePoint: null);
        Register(item);
        return item.Identity;
    }

    internal NativeValidationFileIdentity CreateFile(byte[] contents, params string[] components)
    {
        ValidatePath(components);
        ArgumentNullException.ThrowIfNull(contents);
        using var parent = OpenParent(components);
        using var file = NativeValidationFileSystem.CreateRelativeFile(parent.Handle, components[^1]);
        RandomAccess.Write(file, contents, 0);
        NativeValidationFileSystem.FlushPinnedFile(file);
        var item = CaptureItem(components, WorkspaceItemKind.File, file, expectedReparsePoint: null);
        Register(item);
        return item.Identity;
    }

    internal NativeValidationReparsePoint CreateMountPoint(string targetDirectory, params string[] components)
    {
        ValidatePath(components);
        using var parent = OpenParent(components);
        using var link = NativeValidationFileSystem.CreateRelativeMountPointObject(parent.Handle, components[^1]);
        NativeValidationFileSystem.AssignMountPoint(link, targetDirectory);
        var identity = NativeValidationFileSystem.ReadIdentity(link);
        var reparsePoint = NativeValidationFileSystem.ReadReparsePoint(link, identity);
        Register(CaptureItem(components, WorkspaceItemKind.ReparsePoint, link, reparsePoint));
        return reparsePoint;
    }

    internal NativeValidationReparsePoint CreateRelativeSymbolicLink(string targetName, params string[] components)
    {
        ValidatePath(components);
        using var parent = OpenParent(components);
        using var link = NativeValidationFileSystem.CreateRelativeFile(parent.Handle, components[^1]);
        NativeValidationFileSystem.AssignRelativeSymbolicLink(link, targetName);
        var identity = NativeValidationFileSystem.ReadIdentity(link);
        var reparsePoint = NativeValidationFileSystem.ReadReparsePoint(link, identity);
        Register(CaptureItem(components, WorkspaceItemKind.ReparsePoint, link, reparsePoint));
        return reparsePoint;
    }

    internal uint CreateUnsupportedReparsePoint(params string[] components)
    {
        ValidatePath(components);
        using var parent = OpenParent(components);
        using var link = NativeValidationFileSystem.CreateRelativeMountPointObject(parent.Handle, components[^1]);
        var tag = NativeValidationFileSystem.AssignUnsupportedReparsePoint(link);
        Register(CaptureItem(components, WorkspaceItemKind.UnsupportedReparsePoint, link, expectedReparsePoint: null));
        return tag;
    }

    internal void AdoptExpectedDirectory(params string[] components)
        => AdoptExpected(components, WorkspaceItemKind.Directory);

    internal void AdoptExpectedFile(params string[] components)
        => AdoptExpected(components, WorkspaceItemKind.File);

    internal void AdoptExpectedReparsePoint(params string[] components)
        => AdoptExpected(components, WorkspaceItemKind.ReparsePoint);

    internal bool Exists(params string[] components)
    {
        ValidatePath(components);
        try
        {
            using var parent = OpenParent(components);
            return NativeValidationFileSystem.EnumerateDirectChildren(parent.Handle)
                .Any(entry => string.Equals(entry.Name, components[^1], StringComparison.OrdinalIgnoreCase));
        }
        catch (NativeValidationException exception) when (exception.IsNotFound)
        {
            return false;
        }
    }

    internal string[] Enumerate(params string[] directoryComponents)
    {
        using var directory = OpenDirectory(directoryComponents);
        return NativeValidationFileSystem.EnumerateDirectChildren(directory.Handle)
            .Select(entry => entry.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal NativeValidationRecoveryCandidate QuarantineIntactThroughRecoveryContract()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var runName = Path.GetFileName(_run.RunDirectory);
        var runIdentity = _run.RunIdentity;
        _inventory.Clear();
        _disposed = true;
        _run.AbandonIntactForRecovery();
        var report = ControlledNativeValidationRecovery.Execute();
        var matches = report.Candidates.Where(item =>
            string.Equals(item.EntryName, runName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (report.SourceDirectChildCountAfter != 0
            || matches.Length != 1
            || matches[0].Outcome is not ("QuarantinedIntact" or "QuarantinedAmbiguousIntact")
            || matches[0].Identity is not { } identity
            || !NativeValidationFileSystem.SameObjectIdentity(runIdentity, identity)
            || matches[0].QuarantinePath is not { } quarantinePath
            || !Directory.Exists(quarantinePath))
        {
            throw new InvalidOperationException("The controlled staging run was not proven intact in recovery quarantine.");
        }

        return matches[0];
    }

    internal void DeleteTracked(params string[] components)
    {
        var item = Find(components);
        DeleteExact(item);
        _inventory.Remove(item);
    }

    internal void DisposeProductionHierarchy(string version, string variant, params string[] fileNames)
    {
        foreach (var fileName in fileNames)
        {
            if (Exists("Packages", version, variant, fileName))
            {
                AdoptExpectedFile("Packages", version, variant, fileName);
            }
        }

        if (Exists("Packages", version, variant))
        {
            AdoptExpectedDirectory("Packages", version, variant);
        }
        if (Exists("Packages", version))
        {
            AdoptExpectedDirectory("Packages", version);
        }
        if (Exists("Packages"))
        {
            AdoptExpectedDirectory("Packages");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var errors = new List<Exception>();
        foreach (var item in _inventory
                     .OrderByDescending(item => item.Components.Length)
                     .ThenByDescending(item => item.Sequence)
                     .ToArray())
        {
            try
            {
                DeleteExact(item);
                _inventory.Remove(item);
            }
            catch (Exception exception)
            {
                errors.Add(new InvalidOperationException($"Controlled staging cleanup failed for '{string.Join('\\', item.Components)}'.", exception));
            }
        }

        if (errors.Count == 0)
        {
            try
            {
                _run.Dispose();
                _disposed = true;
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Controlled staging cleanup retained one or more exact owned objects.", errors);
        }
    }

    private static void ApplyCompliantDacl(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var currentUser = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current user SID is unavailable.");
        security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private void AdoptExpected(string[] components, WorkspaceItemKind kind)
    {
        ValidatePath(components);
        if (_inventory.Any(item => ComponentsEqual(item.Components, components)))
        {
            return;
        }

        using var parent = OpenParent(components);
        switch (kind)
        {
            case WorkspaceItemKind.Directory:
                using (var directory = NativeValidationFileSystem.OpenRelativeDirectory(parent.Handle, components[^1], writable: true))
                {
                    Register(CaptureItem(components, kind, directory.Handle, expectedReparsePoint: null));
                }
                break;
            case WorkspaceItemKind.File:
                using (var file = NativeValidationFileSystem.OpenRelativeFileForDeletion(parent.Handle, components[^1]))
                {
                    Register(CaptureItem(components, kind, file, expectedReparsePoint: null));
                }
                break;
            case WorkspaceItemKind.ReparsePoint:
                using (var link = NativeValidationFileSystem.OpenRelativeReparseObjectForInspection(parent.Handle, components[^1]))
                {
                    var identity = NativeValidationFileSystem.ReadIdentity(link);
                    Register(CaptureItem(components, kind, link, NativeValidationFileSystem.ReadReparsePoint(link, identity)));
                }
                break;
            default:
                throw new InvalidOperationException("Unsupported controlled staging inventory type.");
        }
    }

    private void DeleteExact(WorkspaceItem item)
    {
        using var parent = OpenParent(item.Components);
        SafeFileHandle? handle = null;
        try
        {
            handle = item.Kind switch
            {
                WorkspaceItemKind.Directory => NativeValidationFileSystem.OpenRelativeDirectoryForDeletion(parent.Handle, item.Components[^1]),
                WorkspaceItemKind.File => NativeValidationFileSystem.OpenRelativeFileForDeletion(parent.Handle, item.Components[^1]),
                WorkspaceItemKind.ReparsePoint => NativeValidationFileSystem.OpenRelativeReparseObjectForDeletion(parent.Handle, item.Components[^1]),
                WorkspaceItemKind.UnsupportedReparsePoint => NativeValidationFileSystem.OpenRelativeDirectoryReparseObjectForDeletion(parent.Handle, item.Components[^1]),
                _ => throw new InvalidOperationException("Unsupported controlled staging inventory type."),
            };
            var actualIdentity = NativeValidationFileSystem.ReadIdentity(handle);
            var actualPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(handle));
            if (!NativeValidationFileSystem.SameObjectIdentity(item.Identity, actualIdentity)
                || !string.Equals(item.FinalPath, actualPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A controlled staging object changed identity or escaped before cleanup.");
            }

            if (item.Kind == WorkspaceItemKind.Directory)
            {
                var children = NativeValidationFileSystem.EnumerateDirectChildren(handle);
                if (children.Count != 0)
                {
                    throw new InvalidOperationException(
                        "A controlled staging directory was not empty at exact cleanup. Actual=["
                        + string.Join(',', children.Select(child => child.Name))
                        + "].");
                }
            }

            if (item.Kind == WorkspaceItemKind.ReparsePoint)
            {
                var actual = NativeValidationFileSystem.ReadReparsePoint(handle, actualIdentity);
                if (actual != item.ExpectedReparsePoint)
                {
                    throw new InvalidOperationException("A controlled staging reparse point changed target data before cleanup.");
                }
                NativeValidationFileSystem.DeleteReparsePoint(handle, new NativeValidationInventoryItem(
                    item.Components[^1], item.Identity, item.FinalPath,
                    NativeValidationInventoryItemKind.ReparsePoint, item.ExpectedReparsePoint));
            }
            else if (item.Kind == WorkspaceItemKind.UnsupportedReparsePoint)
            {
                NativeValidationFileSystem.DeleteUnsupportedReparsePoint(
                    handle,
                    item.Identity,
                    item.Identity.ReparseTag,
                    item.FinalPath);
            }

            NativeValidationFileSystem.MarkForDelete(handle);
        }
        finally
        {
            handle?.Dispose();
        }

        NativeValidationFileSystem.VerifyDirectDirectoryChildIsAbsent(parent.Handle, item.Components[^1]);
        NativeValidationFileSystem.FlushDirectory(parent.Handle);
    }

    private WorkspaceItem CaptureItem(
        string[] components,
        WorkspaceItemKind kind,
        SafeFileHandle handle,
        NativeValidationReparsePoint? expectedReparsePoint)
    {
        var identity = NativeValidationFileSystem.ReadIdentity(handle);
        var finalPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(handle));
        var expectedPath = PathFor(components);
        if (!string.Equals(finalPath, expectedPath, StringComparison.OrdinalIgnoreCase)
            || identity.VolumeSerialNumber != _run.RunIdentity.VolumeSerialNumber
            || identity.FileIndex == 0)
        {
            throw new InvalidOperationException("A controlled staging object did not prove exact same-run containment and identity.");
        }

        return new WorkspaceItem(components.ToArray(), kind, identity, finalPath, expectedReparsePoint, _inventory.Count + 1L);
    }

    private PinnedNativeValidationDirectory OpenParent(string[] components)
    {
        if (components.Length == 1)
        {
            return NativeValidationFileSystem.OpenRelativeDirectory(_run.RunHandleForTests, "staging", writable: true);
        }

        return OpenDirectory(components[..^1]);
    }

    private PinnedNativeValidationDirectory OpenDirectory(string[] components)
    {
        SafeFileHandle currentParent = _run.RunHandleForTests;
        PinnedNativeValidationDirectory? current = null;
        try
        {
            var names = new[] { "staging" }.Concat(components).ToArray();
            foreach (var name in names)
            {
                var next = NativeValidationFileSystem.OpenRelativeDirectory(currentParent, name, writable: true);
                current?.Dispose();
                current = next;
                currentParent = next.Handle;
            }

            return current ?? throw new InvalidOperationException("A controlled staging parent could not be opened.");
        }
        catch
        {
            current?.Dispose();
            throw;
        }
    }

    private WorkspaceItem Find(string[] components)
        => _inventory.Single(item => ComponentsEqual(item.Components, components));

    private void Register(WorkspaceItem item)
    {
        if (_inventory.Any(existing => ComponentsEqual(existing.Components, item.Components)))
        {
            throw new InvalidOperationException("A controlled staging object cannot be registered twice.");
        }
        _inventory.Add(item);
    }

    private static bool ComponentsEqual(string[] left, string[] right)
        => left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);

    private static void ValidatePath(string[] components)
    {
        if (components.Length == 0)
        {
            throw new ArgumentException("A controlled staging path must contain at least one component.", nameof(components));
        }
        foreach (var component in components)
        {
            if (string.IsNullOrWhiteSpace(component)
                || component is "." or ".."
                || component.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0)
            {
                throw new ArgumentException("A controlled staging path contained an unsafe component.", nameof(components));
            }
        }
    }

    private enum WorkspaceItemKind
    {
        File,
        Directory,
        ReparsePoint,
        UnsupportedReparsePoint,
    }

    private sealed record WorkspaceItem(
        string[] Components,
        WorkspaceItemKind Kind,
        NativeValidationFileIdentity Identity,
        string FinalPath,
        NativeValidationReparsePoint? ExpectedReparsePoint,
        long Sequence);
}
