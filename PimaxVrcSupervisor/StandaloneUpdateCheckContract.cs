using System.Text.Json;
using System.Text.Json.Serialization;

namespace PimaxVrcSupervisor.Updates;

internal sealed record StandaloneUpdateCheckResultV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("resultCode")] string ResultCode,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("latestVerifiedVersion")] string? LatestVerifiedVersion,
    [property: JsonPropertyName("updateAvailable")] bool UpdateAvailable,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("status")] UpdateStatusSnapshotV1? Status);

internal static class StandaloneUpdateCheckJson
{
    public const int MaximumOutputBytes = 16 * 1024;

    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(StandaloneUpdateCheckResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var json = JsonSerializer.Serialize(result, Options);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumOutputBytes)
        {
            throw new InvalidOperationException("The standalone update result exceeded its output bound.");
        }

        return json;
    }
}
