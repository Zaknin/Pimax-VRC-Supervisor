using System.Text.Json;
using PimaxVrcSupervisor.SteamVrHost;
using Xunit;

public sealed class SteamVrOverlayUpdateStatusTests
{
    [Fact]
    public void VerifiedAvailableCandidateProducesOnePassiveIndicator()
    {
        var cache = new OverlayUpdateStatusCache();

        Assert.True(cache.TryApplyBridgeResponse(Response()));
        Assert.Equal("Verified update available: v1.4.0", cache.IndicatorText);
        Assert.False(cache.TryApplyBridgeResponse(Response()));
        Assert.Equal("Verified update available: v1.4.0", cache.IndicatorText);
    }

    [Theory]
    [InlineData(null, false, false, true)]
    [InlineData("1.4.0", true, true, true)]
    [InlineData("1.4.0", true, false, false)]
    [InlineData("1.3.1", false, false, true)]
    [InlineData("1.2.0", false, false, true)]
    public void QuietStatesProduceNoProminentIndicator(
        string? latest,
        bool available,
        bool dismissed,
        bool verificationConfigured)
    {
        var cache = new OverlayUpdateStatusCache();

        Assert.True(cache.TryApplyBridgeResponse(Response(
            latest,
            available,
            dismissed,
            verificationConfigured)));

        Assert.Null(cache.IndicatorText);
    }

    [Fact]
    public void UnsupportedOrMalformedResponseFailsQuietlyAndRetainsCachedStatus()
    {
        var cache = new OverlayUpdateStatusCache();
        Assert.True(cache.TryApplyBridgeResponse(Response()));

        Assert.False(cache.TryApplyBridgeResponse(Response(schemaVersion: 2)));
        Assert.False(cache.TryApplyBridgeResponse("{not-json"));

        Assert.Equal("Verified update available: v1.4.0", cache.IndicatorText);
    }

    [Fact]
    public void InvalidShapeAndDuplicatePropertiesAreRejected()
    {
        var cache = new OverlayUpdateStatusCache();
        var duplicate = Response().Replace(
            "\"schemaVersion\":1",
            "\"schemaVersion\":1,\"schemaVersion\":1",
            StringComparison.Ordinal);

        Assert.False(cache.TryApplyBridgeResponse(duplicate));
        Assert.False(cache.TryApplyBridgeResponse(Response(latest: "v1.4.0")));
        Assert.False(cache.TryApplyBridgeResponse(Response(latest: null, available: true)));
        Assert.Null(cache.IndicatorText);
    }

    [Fact]
    public void UpdateProjectionHasNoHttpPackageOrActionSurface()
    {
        var presentationSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor.SteamVrHost",
            "OverlayUpdateStatus.cs"));
        var hostSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor.SteamVrHost",
            "Program.cs"));

        Assert.DoesNotContain("HttpClient", presentationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", presentationSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", presentationSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".zip", presentationSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Process.Start", presentationSource, StringComparison.Ordinal);
        Assert.Contains("query-json {\\\"resource\\\":\\\"update-status\\\"}", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", hostSource, StringComparison.Ordinal);
        Assert.Equal(
            1,
            hostSource.Split(
                "DrawVer2UpdateIndicator(graphics, subtitleFont)",
                StringSplitOptions.None).Length - 1);

        var indicatorStart = hostSource.IndexOf("private void DrawVer2UpdateIndicator", StringComparison.Ordinal);
        var indicatorEnd = hostSource.IndexOf("private void DrawVer2StatusStrip", indicatorStart, StringComparison.Ordinal);
        var indicatorMethod = hostSource[indicatorStart..indicatorEnd];
        Assert.DoesNotContain("_buttons", indicatorMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", indicatorMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("Send", indicatorMethod, StringComparison.Ordinal);
        Assert.Contains("new Rectangle(1030, 72, 390, 38)", indicatorMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ButtonTop", indicatorMethod, StringComparison.Ordinal);
    }

    private static string Response(
        string? latest = "1.4.0",
        bool available = true,
        bool dismissed = false,
        bool verificationConfigured = true,
        int schemaVersion = 1)
        => JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                schemaVersion,
                policy = "Notify",
                channel = "Stable",
                currentVersion = "1.3.1",
                latestVerifiedVersion = latest,
                updateAvailable = available,
                dismissed,
                dismissedVersion = dismissed ? latest : null,
                lastAttemptAt = "2026-07-21T12:00:00+00:00",
                lastSuccessfulCheckAt = "2026-07-21T12:00:00+00:00",
                lastErrorCode = verificationConfigured ? null : "verification_unavailable",
                lastErrorSummary = verificationConfigured ? null : "Verification is unavailable.",
                verificationConfigured,
                automaticCheckDue = false,
                checkInProgress = false,
                operation = (object?)null
            }
        });

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
