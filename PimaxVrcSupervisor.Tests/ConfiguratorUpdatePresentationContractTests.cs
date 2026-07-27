using Xunit;

public sealed class ConfiguratorUpdatePresentationContractTests
{
    [Fact]
    public void UpdateLabelsAndPrimaryResultMappingUseHumanReadableWording()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor.ConfigEditor", "Program.cs"));

        Assert.Contains("\"Latest verified version\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Last check attempt\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Update verification\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Last check result\"", source, StringComparison.Ordinal);
        Assert.Contains("UpdateStatusUserMessage.ForResult(", source, StringComparison.Ordinal);
        Assert.Contains("UpdateStatusUserMessage.ForStatus(status)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("status.LastErrorCode + \" - \" + status.LastErrorSummary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatesLayoutRetainsSupportedMinimumSizeAndBoundedInlineText()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor.ConfigEditor", "Program.cs"));

        Assert.Contains("MinimumSize = new Size(1180, 860)", source, StringComparison.Ordinal);
        Assert.Contains("_updateInlineResultLabel = new() { AutoSize = true, MaximumSize = new Size(850, 0)", source, StringComparison.Ordinal);
        Assert.Contains("var layout = CreateFormLayout(3);", source, StringComparison.Ordinal);
        Assert.Contains("layout.AutoSize = true;", source, StringComparison.Ordinal);
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
