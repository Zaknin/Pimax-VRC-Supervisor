using System.Runtime.Versioning;

namespace PimaxShellValidationHarness;

internal interface IShortcutReader
{
    ShortcutCandidate Read(string path, string sourceRoot);
}

[SupportedOSPlatform("windows")]
internal sealed class ComShortcutReader : IShortcutReader
{
    public ShortcutCandidate Read(string path, string sourceRoot)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        dynamic shortcut = shell.CreateShortcut(path);
        return new ShortcutCandidate(
            path,
            (string)shortcut.TargetPath,
            (string)shortcut.Arguments,
            (string)shortcut.WorkingDirectory,
            sourceRoot);
    }
}

internal sealed class ShortcutDiscovery
{
    private const string ShortcutName = "PimaxPlay.lnk";
    private readonly IShortcutReader _reader;
    private readonly string[] _roots;

    public ShortcutDiscovery(IShortcutReader reader, string[]? roots = null)
    {
        _reader = reader;
        _roots = roots ?? DefaultRoots();
    }

    public ShortcutDiscoveryResult Discover()
    {
        var candidates = new List<ShortcutCandidate>();
        var errors = new List<string>();

        foreach (var root in _roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var link in Directory.EnumerateFiles(root, ShortcutName, SearchOption.AllDirectories))
            {
                try
                {
                    candidates.Add(_reader.Read(link, root));
                }
                catch (Exception ex)
                {
                    errors.Add($"Could not read shortcut {SanitizePath(link)}: {ex.Message}");
                }
            }
        }

        var trusted = candidates.Where(IsTrusted).DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (trusted.Length != 1)
        {
            errors.Add(trusted.Length == 0
                ? "No trusted official PimaxPlay.lnk shortcut was found."
                : "Multiple trusted official PimaxPlay.lnk shortcuts were found.");
            return new ShortcutDiscoveryResult(false, null, errors.ToArray(), candidates.ToArray());
        }

        return new ShortcutDiscoveryResult(true, trusted[0], errors.ToArray(), candidates.ToArray());
    }

    internal static bool IsTrusted(ShortcutCandidate candidate)
    {
        if (!candidate.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Uri.TryCreate(candidate.Path, UriKind.Absolute, out var sourceUri) && !sourceUri.IsFile)
        {
            return false;
        }

        if (candidate.Path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)
            || candidate.TargetPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)
            || Uri.TryCreate(candidate.TargetPath, UriKind.Absolute, out var targetUri) && !targetUri.IsFile)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Arguments))
        {
            return false;
        }

        var extension = Path.GetExtension(candidate.TargetPath);
        if (extension is ".ps1" or ".cmd" or ".bat" or ".vbs" or ".js" or ".url")
        {
            return false;
        }

        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(value => !string.IsNullOrWhiteSpace(value));

        var underProgramFiles = programFiles.Any(root =>
            candidate.TargetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        return underProgramFiles
            && candidate.TargetPath.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase)
            && candidate.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizePath(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile)
            ? path
            : path.Replace(profile, @"%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] DefaultRoots()
        => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };
}
