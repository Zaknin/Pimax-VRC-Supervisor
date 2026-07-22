using System.Text.RegularExpressions;
using Xunit;

public sealed class UpdateWorkerShippingContractTests
{
    [Fact]
    public void DedicatedWorkerIsAShippedAsInvokerVersion131Executable()
    {
        var root = RepositoryRoot();
        var projectPath = Path.Combine(root, "PimaxVrcSupervisor.UpdateWorker", "PimaxVrcSupervisor.UpdateWorker.csproj");
        var manifestPath = Path.Combine(root, "PimaxVrcSupervisor.UpdateWorker", "app.manifest");

        Assert.True(File.Exists(projectPath), "The production UpdateWorker project is required.");
        Assert.True(File.Exists(manifestPath), "The production UpdateWorker manifest is required.");

        var project = File.ReadAllText(projectPath);
        var manifest = File.ReadAllText(manifestPath);
        Assert.Contains("<AssemblyName>PimaxVrcSupervisor.UpdateWorker</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<Version>1.3.1</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>1.3.1.0</FileVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", project, StringComparison.Ordinal);
        Assert.Contains("<requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\"/>", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("requireAdministrator", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("highestAvailable", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguratorLaunchesOnlyTheDedicatedSiblingWorker()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor.ConfigEditor",
            "ConfiguratorStandaloneUpdateCheckLauncher.cs"));

        Assert.Contains("PimaxVrcSupervisor.UpdateWorker.exe", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string SupervisorExecutableName", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = false", source, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardOutput = true", source, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardError = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageIncludesTheProductionWorkerExactlyOnceAndExcludesTheTestHelper()
    {
        var packageScript = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "package-release.ps1"));

        Assert.Single(Regex.Matches(packageScript, Regex.Escape("PimaxVrcSupervisor.UpdateWorker.csproj")).Cast<Match>());
        Assert.Single(Regex.Matches(packageScript, Regex.Escape("PimaxVrcSupervisor.UpdateWorker.exe")).Cast<Match>());
        Assert.DoesNotContain("PimaxVrcSupervisor.UpdateCheckTestHelper", packageScript, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerEntrypointIsRestrictedToTheFixedOneShotCommandAndSharedProductionComponents()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor.UpdateWorker",
            "Program.cs"));

        Assert.Contains("StandaloneUpdateCheckCommand.HasExactArguments", source, StringComparison.Ordinal);
        Assert.Contains("StandaloneUpdateCheckWorker.RunProductionAsync", source, StringComparison.Ordinal);
        foreach (var forbidden in new[]
                 {
                     "AppSupervisor",
                     "SteamVr",
                     "BaseStation",
                     "Monitor",
                     "ManagedApplication",
                     "AutoLaunchWatcher",
                     "StartupIntegration",
                     "TerminalUi",
                     "Overlay",
                     "ZipFile",
                     "ExtractToDirectory",
                     "HttpClient",
                     "Process.Start"
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PimaxVrcSupervisor.Tests", "PimaxVrcSupervisor.Tests.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
