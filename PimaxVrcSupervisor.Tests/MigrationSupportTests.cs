using System.Text;
using Xunit;

public sealed class MigrationSupportTests
{
    [Fact]
    public void CurrentReleaseFolderIsExcludedAndSiblingCandidatesAreDiscovered()
    {
        using var temp = new TempDirectory();
        var current = temp.CreateDirectory("PimaxVrcSupervisor-v1.3.1");
        var sibling = temp.CreateDirectory("PimaxVrcSupervisor-v1.3.0");
        var currentConfig = Path.Combine(current, ConfigMigrationSupport.DefaultConfigFileName);
        var siblingConfig = Path.Combine(sibling, ConfigMigrationSupport.DefaultConfigFileName);
        File.WriteAllText(currentConfig, "{}", Encoding.UTF8);
        File.WriteAllText(siblingConfig, "{}", Encoding.UTF8);

        var candidates = ConfigMigrationSupport.FindCandidates(current, null, []);

        Assert.DoesNotContain(candidates, candidate => candidate.Path == currentConfig);
        Assert.Contains(candidates, candidate => candidate.Path == siblingConfig);
    }

    [Fact]
    public void DuplicateFullPathsAreDeduplicated()
    {
        using var temp = new TempDirectory();
        var current = temp.CreateDirectory("current");
        var external = temp.WriteFile("external.json", "{}");

        var candidates = ConfigMigrationSupport.FindCandidates(current, external, [external]);

        Assert.Single(candidates);
    }

    [Fact]
    public void ActiveConfigSelectionReturnsOnlyExistingPath()
    {
        using var temp = new TempDirectory();
        var release = temp.CreateDirectory("release");
        File.WriteAllText(Path.Combine(release, ConfigMigrationSupport.ActiveConfigSelectionFileName), "selected.json");
        Assert.Null(ConfigMigrationSupport.TryGetActiveConfigSelectionPath(release));

        var selected = Path.Combine(release, "selected.json");
        File.WriteAllText(selected, "{}");

        Assert.Equal(Path.GetFullPath(selected), ConfigMigrationSupport.TryGetActiveConfigSelectionPath(release));
    }

    [Fact]
    public void ImportCopiesSourceByteForByteAndUpdatesMarkerAfterSuccess()
    {
        using var temp = new TempDirectory();
        var current = temp.CreateDirectory("current");
        var source = temp.WriteFile("source.json", "{\n  // comment\n  \"DisplayName\": \"Sample\",\n}\n");

        var result = ConfigMigrationSupport.ImportConfig(source, current);

        Assert.Equal(ConfigMigrationOutcome.Imported, result.Outcome);
        Assert.NotNull(result.DestinationPath);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.DestinationPath!));
        Assert.Equal(
            Path.GetFullPath(result.DestinationPath!),
            ConfigMigrationSupport.TryGetActiveConfigSelectionPath(current));
    }

    [Fact]
    public void InvalidJsonLeavesNoPartialFinalFile()
    {
        using var temp = new TempDirectory();
        var current = temp.CreateDirectory("current");
        var source = temp.WriteFile("broken.json", "{ broken");

        var result = ConfigMigrationSupport.ImportConfig(source, current);

        Assert.Equal(ConfigMigrationOutcome.Failed, result.Outcome);
        Assert.NotNull(result.DestinationPath);
        Assert.False(File.Exists(result.DestinationPath!));
        Assert.Null(ConfigMigrationSupport.TryGetActiveConfigSelectionPath(current));
    }

    [Fact]
    public void DefaultNameConflictUsesMovedConfigName()
    {
        using var temp = new TempDirectory();
        var current = temp.CreateDirectory("current");
        var sourceDirectory = temp.CreateDirectory("previous");
        var source = Path.Combine(sourceDirectory, ConfigMigrationSupport.DefaultConfigFileName);
        File.WriteAllText(source, "{}");
        File.WriteAllText(Path.Combine(current, ConfigMigrationSupport.DefaultConfigFileName), "{}");

        var destination = ConfigMigrationSupport.ChooseImportDestination(source, current);

        Assert.Equal(Path.Combine(current, "supervisor_moved.config.json"), destination);
    }

    [Fact]
    public void RepeatedConflictCreatesAnotherUniqueFilename()
    {
        using var temp = new TempDirectory();
        var current = temp.CreateDirectory("current");
        var source = temp.WriteFile("custom.json", "{}");
        File.WriteAllText(Path.Combine(current, "custom.json"), "{}");

        var destination = ConfigMigrationSupport.ChooseImportDestination(source, current);

        Assert.NotEqual(Path.Combine(current, "custom.json"), destination);
        Assert.StartsWith(Path.Combine(current, "custom_"), destination, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".json", destination, StringComparison.OrdinalIgnoreCase);
    }
}
