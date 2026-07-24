using System.Security.AccessControl;
using System.Security.Principal;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PimaxVrcSupervisorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        ProtectDirectory(Path);
    }

    public string Path { get; }

    public string CreateDirectory(string name)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(path);
        ProtectHierarchy(path);
        return path;
    }

    public string WriteFile(string name, string contents)
    {
        var path = System.IO.Path.Combine(Path, name);
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            ProtectHierarchy(directory);
        }

        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void ProtectDirectory(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var currentUser = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current user SID is unavailable.");
        security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private void ProtectHierarchy(string path)
    {
        var relative = System.IO.Path.GetRelativePath(Path, path);
        var current = Path;
        foreach (var component in relative.Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]))
        {
            if (string.IsNullOrEmpty(component) || component == ".")
            {
                continue;
            }

            current = System.IO.Path.Combine(current, component);
            ProtectDirectory(current);
        }
    }

}
