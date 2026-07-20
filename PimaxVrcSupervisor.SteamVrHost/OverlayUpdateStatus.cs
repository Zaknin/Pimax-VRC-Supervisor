using System.Text;
using System.Text.Json;

namespace PimaxVrcSupervisor.SteamVrHost;

internal sealed record OverlayUpdateStatus(
    string CurrentVersion,
    string? LatestVerifiedVersion,
    bool UpdateAvailable,
    bool Dismissed,
    bool VerificationConfigured)
{
    private const int MaximumResponseBytes = 128 * 1024;

    public string? IndicatorText
        => VerificationConfigured
            && UpdateAvailable
            && !Dismissed
            && LatestVerifiedVersion is not null
            && IsStableSemanticVersion(LatestVerifiedVersion)
            ? $"Verified update available: v{LatestVerifiedVersion}"
            : null;

    public static bool TryParseBridgeResponse(string response, out OverlayUpdateStatus status)
    {
        status = null!;
        if (string.IsNullOrWhiteSpace(response) || Encoding.UTF8.GetByteCount(response) > MaximumResponseBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !HasUniqueProperties(root)
                || !TryGetBoolean(root, "success", out var success)
                || !success
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !HasUniqueProperties(data)
                || !TryGetInteger(data, "schemaVersion", out var schemaVersion)
                || schemaVersion != 1
                || !TryGetRequiredString(data, "policy", 32, out var policy)
                || policy is not ("Disabled" or "Notify")
                || !TryGetRequiredString(data, "channel", 32, out var channel)
                || channel != "Stable"
                || !TryGetRequiredString(data, "currentVersion", 128, out var currentVersion)
                || !IsStableSemanticVersion(currentVersion)
                || !TryGetOptionalString(data, "latestVerifiedVersion", 128, out var latestVersion)
                || latestVersion is not null && !IsStableSemanticVersion(latestVersion)
                || !TryGetBoolean(data, "updateAvailable", out var updateAvailable)
                || updateAvailable && latestVersion is null
                || !TryGetBoolean(data, "dismissed", out var dismissed)
                || !TryGetOptionalString(data, "dismissedVersion", 128, out var dismissedVersion)
                || dismissedVersion is not null && !IsStableSemanticVersion(dismissedVersion)
                || !TryGetOptionalString(data, "lastAttemptAt", 64, out _)
                || !TryGetOptionalString(data, "lastSuccessfulCheckAt", 64, out _)
                || !TryGetOptionalString(data, "lastErrorCode", 128, out _)
                || !TryGetOptionalString(data, "lastErrorSummary", 512, out _)
                || !TryGetBoolean(data, "verificationConfigured", out var verificationConfigured)
                || !TryGetBoolean(data, "automaticCheckDue", out _)
                || !TryGetBoolean(data, "checkInProgress", out _)
                || !TryGetNullableObject(data, "operation"))
            {
                return false;
            }

            status = new OverlayUpdateStatus(
                currentVersion,
                latestVersion,
                updateAvailable,
                dismissed,
                verificationConfigured);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasUniqueProperties(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().All(property => names.Add(property.Name));
    }

    private static bool TryGetInteger(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryGetBoolean(JsonElement element, string name, out bool value)
    {
        value = default;
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetRequiredString(
        JsonElement element,
        string name,
        int maximumLength,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = property.GetString();
        if (!IsBoundedSafeString(candidate, maximumLength))
        {
            return false;
        }

        value = candidate!;
        return true;
    }

    private static bool TryGetOptionalString(
        JsonElement element,
        string name,
        int maximumLength,
        out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return IsBoundedSafeString(value, maximumLength);
    }

    private static bool TryGetNullableObject(JsonElement element, string name)
        => element.TryGetProperty(name, out var property)
            && property.ValueKind is JsonValueKind.Null or JsonValueKind.Object;

    private static bool IsBoundedSafeString(string? value, int maximumLength)
        => value is { Length: > 0 } && value.Length <= maximumLength && !value.Any(char.IsControl);

    private static bool IsStableSemanticVersion(string value)
    {
        var buildSeparator = value.IndexOf('+');
        var version = buildSeparator >= 0 ? value[..buildSeparator] : value;
        var build = buildSeparator >= 0 ? value[(buildSeparator + 1)..] : null;
        if (version.Contains('-', StringComparison.Ordinal)
            || build is not null && !ValidIdentifiers(build))
        {
            return false;
        }

        var core = version.Split('.');
        return core.Length == 3 && core.All(ValidNumericIdentifier);
    }

    private static bool ValidNumericIdentifier(string value)
        => value.Length > 0
            && value.All(char.IsAsciiDigit)
            && (value == "0" || value[0] != '0');

    private static bool ValidIdentifiers(string value)
        => value.Split('.').All(identifier =>
            identifier.Length > 0
            && identifier.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
}

internal sealed class OverlayUpdateStatusCache
{
    private readonly object _gate = new();
    private OverlayUpdateStatus? _current;

    public string? IndicatorText
    {
        get
        {
            lock (_gate)
            {
                return _current?.IndicatorText;
            }
        }
    }

    public bool TryApplyBridgeResponse(string response)
    {
        if (!OverlayUpdateStatus.TryParseBridgeResponse(response, out var next))
        {
            return false;
        }

        lock (_gate)
        {
            if (Equals(_current, next))
            {
                return false;
            }

            _current = next;
            return true;
        }
    }
}
