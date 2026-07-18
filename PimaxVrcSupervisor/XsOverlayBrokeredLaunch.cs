using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PimaxVrcSupervisor;

internal enum XsOverlayLaunchRoute
{
    SteamApp,
    OpenVrManifest,
    ShellRegistered,
    SafeDirect,
    Unavailable
}

internal enum XsOverlayWindowVerificationResult
{
    NoUnwantedDesktopWindow,
    UnwantedDesktopWindowDetected,
    Unavailable
}

internal sealed record XsOverlayLaunchTarget(
    XsOverlayLaunchRoute RouteType,
    string? ValidatedAppId,
    string? RegistrationSource,
    string? ExecutableIdentity,
    bool InstallationValidated,
    string? FailureReason,
    bool SafeForAutomaticRestart,
    string? OpenVrApplicationKey = null);

internal sealed record XsOverlayLaunchRequestResult(
    bool Requested,
    string RequestMethod,
    string? FailureReason = null);

internal interface IXsOverlayLauncher
{
    XsOverlayLaunchTarget Discover(XsOverlayProcessSnapshot process);
    Task<XsOverlayLaunchRequestResult> RequestLaunchAsync(XsOverlayLaunchTarget target, CancellationToken cancellationToken);
}

internal interface IXsOverlayWindowObserver
{
    Task<XsOverlayWindowVerificationResult> ObserveAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken);
}

internal interface IXsOverlayLaunchFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern);
}

internal sealed class WindowsXsOverlayLaunchFileSystem : IXsOverlayLaunchFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        => Directory.Exists(path) ? Directory.EnumerateFiles(path, searchPattern) : [];
}

internal sealed class WindowsXsOverlayLauncher : IXsOverlayLauncher
{
    private const string SteamProtocolPrefix = "steam://rungameid/";
    private readonly IXsOverlayLaunchFileSystem _fileSystem;
    private readonly Func<IEnumerable<string>> _steamInstallations;
    private readonly Func<IEnumerable<string>> _openVrManifestPaths;

    public WindowsXsOverlayLauncher(
        IXsOverlayLaunchFileSystem? fileSystem = null,
        Func<IEnumerable<string>>? steamInstallations = null,
        Func<IEnumerable<string>>? openVrManifestPaths = null)
    {
        _fileSystem = fileSystem ?? new WindowsXsOverlayLaunchFileSystem();
        _steamInstallations = steamInstallations ?? GetSteamInstallations;
        _openVrManifestPaths = openVrManifestPaths ?? GetRegisteredOpenVrManifestPaths;
    }

    public XsOverlayLaunchTarget Discover(XsOverlayProcessSnapshot process)
    {
        var executablePath = process.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath)
            || !Path.IsPathFullyQualified(executablePath)
            || !string.Equals(Path.GetFileName(executablePath), "XSOverlay.exe", StringComparison.OrdinalIgnoreCase)
            || !_fileSystem.FileExists(executablePath))
        {
            return Unavailable("the original XSOverlay executable identity could not be validated");
        }

        var steamInstallations = _steamInstallations()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.TrimEnd('\\', '/'))
            .Where(path => _fileSystem.FileExists(Path.Combine(path, "steam.exe")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var library in GetSteamLibraries(steamInstallations, executablePath))
        {
            var target = DiscoverSteamApplication(library, executablePath);
            if (target is not null)
            {
                return target;
            }
        }

        foreach (var manifestPath in _openVrManifestPaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var target = DiscoverOpenVrApplication(manifestPath, executablePath);
            if (target is not null)
            {
                return target;
            }
        }

        return Unavailable(steamInstallations.Length == 0
            ? "Steam installation was unavailable and no validated registered OpenVR XSOverlay application was found"
            : "no validated Steam or registered OpenVR XSOverlay launch route was found");
    }

    public Task<XsOverlayLaunchRequestResult> RequestLaunchAsync(XsOverlayLaunchTarget target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!target.SafeForAutomaticRestart)
        {
            return Task.FromResult(new XsOverlayLaunchRequestResult(false, "refused", target.FailureReason ?? "launch target is not safe"));
        }

        try
        {
            switch (target.RouteType)
            {
                case XsOverlayLaunchRoute.SteamApp when !string.IsNullOrWhiteSpace(target.ValidatedAppId):
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SteamProtocolPrefix + target.ValidatedAppId,
                        UseShellExecute = true
                    })?.Dispose();
                    return Task.FromResult(new XsOverlayLaunchRequestResult(true, "shellSteamUri"));

                case XsOverlayLaunchRoute.OpenVrManifest when !string.IsNullOrWhiteSpace(target.OpenVrApplicationKey):
                    using (var registry = OpenVrApplicationRegistry.Open())
                    {
                        registry.LaunchApplication(target.OpenVrApplicationKey);
                    }

                    return Task.FromResult(new XsOverlayLaunchRequestResult(true, "openVrRegisteredApplication"));

                default:
                    return Task.FromResult(new XsOverlayLaunchRequestResult(false, "refused", "no supported brokered launch request is available"));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(new XsOverlayLaunchRequestResult(false, "launchFailed", ex.Message));
        }
    }

    private IEnumerable<string> GetSteamLibraries(IEnumerable<string> steamInstallations, string executablePath)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var installation in steamInstallations)
        {
            libraries.Add(installation);
            var libraryFolders = Path.Combine(installation, "steamapps", "libraryfolders.vdf");
            if (!_fileSystem.FileExists(libraryFolders))
            {
                continue;
            }

            try
            {
                foreach (Match match in Regex.Matches(_fileSystem.ReadAllText(libraryFolders), "\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    libraries.Add(match.Groups["path"].Value.Replace("\\\\", "\\"));
                }
            }
            catch (Exception)
            {
                // A malformed library configuration is not trusted as a launch source.
            }
        }

        var steamAppsDirectory = FindSteamAppsDirectory(executablePath);
        if (steamAppsDirectory is not null)
        {
            libraries.Add(Directory.GetParent(steamAppsDirectory)?.FullName ?? "");
        }

        return libraries.Where(path => !string.IsNullOrWhiteSpace(path) && _fileSystem.DirectoryExists(Path.Combine(path, "steamapps")));
    }

    private XsOverlayLaunchTarget? DiscoverSteamApplication(string libraryPath, string executablePath)
    {
        var steamAppsPath = Path.Combine(libraryPath, "steamapps");
        foreach (var manifestPath in _fileSystem.EnumerateFiles(steamAppsPath, "appmanifest_*.acf"))
        {
            try
            {
                var manifest = _fileSystem.ReadAllText(manifestPath);
                var appId = ReadVdfValue(manifest, "appid");
                var name = ReadVdfValue(manifest, "name");
                var installDirectory = ReadVdfValue(manifest, "installdir");
                if (string.IsNullOrWhiteSpace(appId)
                    || !appId.All(char.IsDigit)
                    || string.IsNullOrWhiteSpace(name)
                    || !name.Contains("XSOverlay", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(installDirectory))
                {
                    continue;
                }

                var installedExecutable = Path.Combine(steamAppsPath, "common", installDirectory, "XSOverlay.exe");
                if (!_fileSystem.FileExists(installedExecutable)
                    || !string.Equals(Path.GetFullPath(installedExecutable), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return new(
                    XsOverlayLaunchRoute.SteamApp,
                    appId,
                    manifestPath,
                    installedExecutable,
                    InstallationValidated: true,
                    FailureReason: null,
                    SafeForAutomaticRestart: true);
            }
            catch (Exception)
            {
                // A malformed app manifest is not launch authority.
            }
        }

        return null;
    }

    private XsOverlayLaunchTarget? DiscoverOpenVrApplication(string manifestPath, string executablePath)
    {
        try
        {
            if (!_fileSystem.FileExists(manifestPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(_fileSystem.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("applications", out var applications)
                || applications.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var application in applications.EnumerateArray())
            {
                var key = application.TryGetProperty("app_key", out var keyElement) ? keyElement.GetString() : null;
                var binary = application.TryGetProperty("binary_path_windows", out var binaryElement) ? binaryElement.GetString() : null;
                var launchType = application.TryGetProperty("launch_type", out var launchTypeElement) ? launchTypeElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(key)
                    || !key.Contains("xsoverlay", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(launchType, "binary", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(binary))
                {
                    continue;
                }

                var resolvedBinary = Path.IsPathFullyQualified(binary)
                    ? binary
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath) ?? "", binary));
                if (!string.Equals(Path.GetFileName(resolvedBinary), "XSOverlay.exe", StringComparison.OrdinalIgnoreCase)
                    || !_fileSystem.FileExists(resolvedBinary)
                    || !string.Equals(Path.GetFullPath(resolvedBinary), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return new(
                    XsOverlayLaunchRoute.OpenVrManifest,
                    ValidatedAppId: null,
                    manifestPath,
                    resolvedBinary,
                    InstallationValidated: true,
                    FailureReason: null,
                    SafeForAutomaticRestart: true,
                    OpenVrApplicationKey: key);
            }
        }
        catch (Exception)
        {
            // Invalid or unreadable manifests are not trusted.
        }

        return null;
    }

    private static XsOverlayLaunchTarget Unavailable(string reason)
        => new(XsOverlayLaunchRoute.Unavailable, null, null, null, false, reason, false);

    private static string? ReadVdfValue(string text, string key)
    {
        var match = Regex.Match(text, $"\"{Regex.Escape(key)}\"\\s+\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? FindSteamAppsDirectory(string executablePath)
    {
        var current = new FileInfo(executablePath).Directory;
        while (current is not null)
        {
            if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static IEnumerable<string> GetSteamInstallations()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var key = root.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string steamPath && !string.IsNullOrWhiteSpace(steamPath))
                {
                    paths.Add(steamPath);
                }
            }
            catch (Exception)
            {
                // Registry lookup is advisory only; it never modifies Steam state.
            }
        }

        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        return paths;
    }

    private static IEnumerable<string> GetRegisteredOpenVrManifestPaths()
    {
        var pathsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openvr", "openvrpaths.vrpath");
        if (!File.Exists(pathsFile))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(pathsFile));
            var paths = new List<string>();
            if (document.RootElement.TryGetProperty("applications", out var applications) && applications.ValueKind == JsonValueKind.Array)
            {
                paths.AddRange(applications.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(path => !string.IsNullOrWhiteSpace(path))!);
            }

            if (document.RootElement.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Array)
            {
                foreach (var configDirectory in config.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(path => !string.IsNullOrWhiteSpace(path)))
                {
                    var appConfig = Path.Combine(configDirectory!, "appconfig.json");
                    if (!File.Exists(appConfig))
                    {
                        continue;
                    }

                    using var appConfigDocument = JsonDocument.Parse(File.ReadAllText(appConfig));
                    if (appConfigDocument.RootElement.TryGetProperty("manifest_paths", out var manifests) && manifests.ValueKind == JsonValueKind.Array)
                    {
                        paths.AddRange(manifests.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(path => !string.IsNullOrWhiteSpace(path))!);
                    }
                }
            }

            return paths;
        }
        catch (Exception)
        {
            return [];
        }
    }
}

internal sealed class WindowsXsOverlayWindowObserver : IXsOverlayWindowObserver
{
    public Task<XsOverlayWindowVerificationResult> ObserveAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(XsOverlayWindowVerificationResult.Unavailable);
        }

        try
        {
            using var current = Process.GetProcessById(process.ProcessId);
            if (current.HasExited || current.MainWindowHandle == IntPtr.Zero)
            {
                return Task.FromResult(XsOverlayWindowVerificationResult.NoUnwantedDesktopWindow);
            }

            return Task.FromResult(IsWindowVisible(current.MainWindowHandle)
                ? XsOverlayWindowVerificationResult.UnwantedDesktopWindowDetected
                : XsOverlayWindowVerificationResult.NoUnwantedDesktopWindow);
        }
        catch (Exception)
        {
            return Task.FromResult(XsOverlayWindowVerificationResult.Unavailable);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);
}
