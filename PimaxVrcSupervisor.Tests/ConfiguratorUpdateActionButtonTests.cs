using Xunit;

public sealed class ConfiguratorUpdateActionButtonTests
{
    [Fact]
    public void DisabledActionButtonsUseThemePaletteWithoutInteractiveVisuals()
    {
        var button = ExtractConfiguratorSource(
            "internal sealed class ThemedActionButton",
            "internal sealed class ThemedTabButton");

        Assert.Contains("public Color DisabledBackColor", button, StringComparison.Ordinal);
        Assert.Contains("public Color DisabledForeColor", button, StringComparison.Ordinal);
        Assert.Contains("public Color DisabledBorderColor", button, StringComparison.Ordinal);
        Assert.Contains("backColor = DisabledBackColor;", button, StringComparison.Ordinal);
        Assert.Contains("Enabled ? ForeColor : DisabledForeColor", button, StringComparison.Ordinal);
        Assert.Contains("Enabled ? ColorOrFallback(FlatAppearance.BorderColor", button, StringComparison.Ordinal);
        Assert.Contains(": DisabledBorderColor", button, StringComparison.Ordinal);
        Assert.Contains("if (Enabled && Focused && ShowFocusCues)", button, StringComparison.Ordinal);
        Assert.DoesNotContain("backColor = SystemColors.Control;", button, StringComparison.Ordinal);
        Assert.DoesNotContain("Enabled ? ForeColor : SystemColors.GrayText", button, StringComparison.Ordinal);
    }

    [Fact]
    public void DisablingActionButtonClearsHoverAndPressedState()
    {
        var button = ExtractConfiguratorSource(
            "internal sealed class ThemedActionButton",
            "internal sealed class ThemedTabButton");
        var enabledChanged = ExtractSource(button, "protected override void OnEnabledChanged", "protected override void OnPaint");

        Assert.Contains("if (!Enabled)", enabledChanged, StringComparison.Ordinal);
        Assert.Contains("_hovered = false;", enabledChanged, StringComparison.Ordinal);
        Assert.Contains("_pressed = false;", enabledChanged, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableUpdateActionsRemainDisabledAndCannotDispatchCommands()
    {
        var updateActions = ExtractConfiguratorSource(
            "private Control BuildUpdatesTab()",
            "private static string FormatUpdateTimestamp");

        Assert.Contains("_dismissUpdateButton.Enabled = false;", updateActions, StringComparison.Ordinal);
        Assert.Contains("_clearUpdateDismissalButton.Enabled = false;", updateActions, StringComparison.Ordinal);
        Assert.Contains("_dismissUpdateButton.Click += async (_, _) => await RunUpdateActionAsync(UpdateUiAction.Dismiss);", updateActions, StringComparison.Ordinal);
        Assert.Contains("_clearUpdateDismissalButton.Click += async (_, _) => await RunUpdateActionAsync(UpdateUiAction.ClearDismissal);", updateActions, StringComparison.Ordinal);
        Assert.Contains("_dismissUpdateButton.Enabled = !_updateActionInProgress && _lastUpdateStatus is { UpdateAvailable: true, Dismissed: false };", updateActions, StringComparison.Ordinal);
        Assert.Contains("_clearUpdateDismissalButton.Enabled = !_updateActionInProgress && _lastUpdateStatus?.DismissedVersion is not null;", updateActions, StringComparison.Ordinal);
        Assert.DoesNotContain("_dismissUpdateButton.PerformClick", updateActions, StringComparison.Ordinal);
        Assert.DoesNotContain("_clearUpdateDismissalButton.PerformClick", updateActions, StringComparison.Ordinal);
    }

    private static string ExtractConfiguratorSource(string startMarker, string endMarker)
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor.ConfigEditor", "Program.cs"));
        return ExtractSource(source, startMarker, endMarker);
    }

    private static string ExtractSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find end marker: {endMarker}");
        return source[start..end];
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
