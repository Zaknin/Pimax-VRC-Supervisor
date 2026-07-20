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
        Assert.Contains("BuildRestartActionCommand", source, StringComparison.Ordinal);
        Assert.Contains("requestId,", source, StringComparison.Ordinal);
        Assert.Contains("sourceClientType = \"steamvr-overlay\"", source, StringComparison.Ordinal);
        Assert.Contains("sourceClientInstanceId = clientInstanceId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiresButtonConfirmation(\"restart-core-apps\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayCreatesRestartIdentityOnlyAfterConfirmationIsActive()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor.SteamVrHost", "Program.cs"));
        var start = source.IndexOf("private void TryStartButtonCommand", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ExecuteButtonAsync", start, StringComparison.Ordinal);
        var method = source[start..end];

        var confirmationGate = method.IndexOf("RequiresButtonConfirmation", StringComparison.Ordinal);
        var requestCreation = method.IndexOf("Guid.NewGuid().ToString(\"N\")", StringComparison.Ordinal);
        Assert.True(confirmationGate >= 0 && requestCreation > confirmationGate);
        Assert.Contains("_commandInFlight = true", method, StringComparison.Ordinal);
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
