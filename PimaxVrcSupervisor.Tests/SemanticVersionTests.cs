using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("1.2.3")]
    [InlineData("10.20.30-alpha.1+build.5")]
    [InlineData("999999999999999999999999.0.1")]
    public void StrictParserAcceptsValidSemanticVersions(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var parsed));
        Assert.Equal(value, parsed.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1.2.3")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3+")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.3 ")]
    public void StrictParserRejectsMalformedSemanticVersions(string value)
        => Assert.False(SemanticVersion.TryParse(value, out _));

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.9.9", "2.0.0")]
    public void ComparisonFollowsSemanticVersionPrecedence(string lower, string higher)
    {
        var left = SemanticVersion.Parse(lower);
        var right = SemanticVersion.Parse(higher);

        Assert.True(left < right);
        Assert.True(right > left);
    }

    [Fact]
    public void BuildMetadataDoesNotChangePrecedence()
    {
        var left = SemanticVersion.Parse("1.2.3+build.1");
        var right = SemanticVersion.Parse("1.2.3+build.2");

        Assert.Equal(0, left.CompareTo(right));
        Assert.NotEqual(left, right);
    }
}
