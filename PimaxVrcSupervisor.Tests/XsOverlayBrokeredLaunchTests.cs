using PimaxVrcSupervisor;
using Xunit;

public sealed class XsOverlayBrokeredLaunchTests
{
    private const string SteamRoot = @"C:\Steam";
    private const string LibraryRoot = @"D:\SteamLibrary";
    private const string Executable = @"D:\SteamLibrary\steamapps\common\XSOverlay_Beta\XSOverlay.exe";
    private const string Manifest = @"D:\SteamLibrary\steamapps\appmanifest_1173510.acf";

    [Fact]
    public void InstalledXsOverlaySteamManifest_SelectsValidatedSteamRoute()
    {
        var fileSystem = SteamFixture();
        var launcher = CreateLauncher(fileSystem);

        var target = launcher.Discover(Process());

        Assert.Equal(XsOverlayLaunchRoute.SteamApp, target.RouteType);
        Assert.Equal("1173510", target.ValidatedAppId);
        Assert.True(target.InstallationValidated);
        Assert.True(target.SafeForAutomaticRestart);
        Assert.Equal(Manifest, target.RegistrationSource);
    }

    [Fact]
    public void SteamManifestForAnotherApplication_IsRejected()
    {
        var fileSystem = SteamFixture(name: "Unrelated application");

        var target = CreateLauncher(fileSystem).Discover(Process());

        Assert.Equal(XsOverlayLaunchRoute.Unavailable, target.RouteType);
        Assert.False(target.SafeForAutomaticRestart);
    }

    [Fact]
    public void SteamLibraryDiscovery_UsesAdditionalLibraryFromLibraryFolders()
    {
        var fileSystem = SteamFixture();

        var target = CreateLauncher(fileSystem).Discover(Process());

        Assert.Equal(XsOverlayLaunchRoute.SteamApp, target.RouteType);
        Assert.Equal("1173510", target.ValidatedAppId);
    }

    [Fact]
    public void SteamUnavailable_EvaluatesValidatedOpenVrManifest()
    {
        var fileSystem = new FakeFileSystem();
        const string openVrManifest = @"C:\OpenVR\xsoverlay.vrmanifest";
        fileSystem.AddFile(Executable);
        fileSystem.AddText(openVrManifest, """
            { "applications": [
              { "app_key": "xsoverlay.dashboard", "launch_type": "binary", "binary_path_windows": "D:\\SteamLibrary\\steamapps\\common\\XSOverlay_Beta\\XSOverlay.exe" }
            ] }
            """);
        var launcher = new WindowsXsOverlayLauncher(fileSystem, () => [], () => [openVrManifest]);

        var target = launcher.Discover(Process());

        Assert.Equal(XsOverlayLaunchRoute.OpenVrManifest, target.RouteType);
        Assert.Equal("xsoverlay.dashboard", target.OpenVrApplicationKey);
        Assert.True(target.SafeForAutomaticRestart);
    }

    [Fact]
    public void MalformedOrUntrustedOpenVrManifest_IsRejected()
    {
        var fileSystem = new FakeFileSystem();
        const string openVrManifest = @"C:\OpenVR\xsoverlay.vrmanifest";
        fileSystem.AddFile(Executable);
        fileSystem.AddText(openVrManifest, "{ not valid json");
        var launcher = new WindowsXsOverlayLauncher(fileSystem, () => [], () => [openVrManifest]);

        var target = launcher.Discover(Process());

        Assert.Equal(XsOverlayLaunchRoute.Unavailable, target.RouteType);
        Assert.False(target.SafeForAutomaticRestart);
    }

    private static WindowsXsOverlayLauncher CreateLauncher(FakeFileSystem fileSystem)
        => new(fileSystem, () => [SteamRoot], () => []);

    private static FakeFileSystem SteamFixture(string name = "XSOverlay")
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(Path.Combine(SteamRoot, "steam.exe"));
        fileSystem.AddDirectory(Path.Combine(SteamRoot, "steamapps"));
        fileSystem.AddText(Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"), """
            "libraryfolders"
            {
              "1" { "path" "D:\\SteamLibrary" }
            }
            """);
        fileSystem.AddDirectory(Path.Combine(LibraryRoot, "steamapps"));
        fileSystem.AddText(Manifest,
            "\"AppState\"\n{\n"
            + "  \"appid\" \"1173510\"\n"
            + $"  \"name\" \"{name}\"\n"
            + "  \"installdir\" \"XSOverlay_Beta\"\n}");
        fileSystem.AddFile(Executable);
        return fileSystem;
    }

    private static XsOverlayProcessSnapshot Process()
        => new(100, 1, "XSOverlay", DateTime.UnixEpoch, Executable);

    private sealed class FakeFileSystem : IXsOverlayLaunchFileSystem
    {
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _text = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path)
        {
            _files.Add(path);
            AddDirectory(Path.GetDirectoryName(path)!);
        }

        public void AddText(string path, string text)
        {
            AddFile(path);
            _text[path] = text;
        }

        public void AddDirectory(string path)
        {
            for (var current = path; !string.IsNullOrWhiteSpace(current); current = Path.GetDirectoryName(current))
            {
                _directories.Add(current);
            }
        }

        public bool FileExists(string path) => _files.Contains(path);
        public bool DirectoryExists(string path) => _directories.Contains(path);
        public string ReadAllText(string path) => _text[path];
        public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
            => _files.Where(file => string.Equals(Path.GetDirectoryName(file), path, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(file).StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(file).Equals(".acf", StringComparison.OrdinalIgnoreCase));
    }
}
