using Xunit;

public sealed class SteamVrOverlayActionCompositionTests
{
    [Fact]
    public void OverlayExposesVrRestartAsConfirmedFullWidthSeventhAction()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor.SteamVrHost", "Program.cs"));

        Assert.Contains("new(\"Restart SteamVR\", \"restart-vr-session\", new Rectangle(ButtonLeft, ButtonThirdRow, ContentWidth, ButtonHeight))", source, StringComparison.Ordinal);
        Assert.Contains("\"restart-vr-session\" => \"VRChat resumes only if it was running\"", source, StringComparison.Ordinal);
        Assert.Contains("RequiresButtonConfirmation(button.Command)", source, StringComparison.Ordinal);
        Assert.Contains("\"action-json {\\\"command\\\":\\\"restart-vr-session\\\",\\\"confirmed\\\":true,\\\"source\\\":\\\"SteamVR Overlay\\\"}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiresButtonConfirmation(\"restart-core-apps\")", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !HasGitMetadata(directory.FullName)) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static bool HasGitMetadata(string directory)
    {
        var path = Path.Combine(directory, ".git");
        return Directory.Exists(path) || File.Exists(path);
    }
}
