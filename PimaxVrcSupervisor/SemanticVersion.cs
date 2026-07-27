using System.Numerics;
using System.Text.RegularExpressions;

namespace PimaxVrcSupervisor.Updates;

internal sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)"
        + "(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?"
        + "(?:\\+([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private SemanticVersion(
        BigInteger major,
        BigInteger minor,
        BigInteger patch,
        string? prerelease,
        string? buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        BuildMetadata = buildMetadata;
    }

    public BigInteger Major { get; }

    public BigInteger Minor { get; }

    public BigInteger Patch { get; }

    public string? Prerelease { get; }

    public string? BuildMetadata { get; }

    public bool IsStable => Prerelease is null;

    public static SemanticVersion Parse(string value)
        => TryParse(value, out var version)
            ? version
            : throw new FormatException($"'{value}' is not a strict semantic version.");

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var match = Pattern.Match(value);
        if (!match.Success)
        {
            return false;
        }

        version = new SemanticVersion(
            BigInteger.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
            match.Groups[4].Success ? match.Groups[4].Value : null,
            match.Groups[5].Success ? match.Groups[5].Value : null);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0)
        {
            return comparison;
        }

        if (Prerelease is null)
        {
            return other.Prerelease is null ? 0 : 1;
        }

        if (other.Prerelease is null)
        {
            return -1;
        }

        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            comparison = ComparePrereleaseIdentifier(left[index], right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    public bool Equals(SemanticVersion? other)
        => other is not null
            && Major == other.Major
            && Minor == other.Minor
            && Patch == other.Patch
            && string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal)
            && string.Equals(BuildMetadata, other.BuildMetadata, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease, BuildMetadata);

    public override string ToString()
    {
        var value = $"{Major}.{Minor}.{Patch}";
        if (Prerelease is not null)
        {
            value += "-" + Prerelease;
        }

        if (BuildMetadata is not null)
        {
            value += "+" + BuildMetadata;
        }

        return value;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric && rightNumeric)
        {
            return BigInteger.Parse(left, System.Globalization.CultureInfo.InvariantCulture)
                .CompareTo(BigInteger.Parse(right, System.Globalization.CultureInfo.InvariantCulture));
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }
}
