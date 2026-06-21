using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PimaxShellValidationHarness;

internal static class ArtifactWriter
{
    public static string CreateResultDirectory(Guid correlationId)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(HarnessConstants.ResultRoot, $"PimaxShellOneShot-{timestamp}-{correlationId:N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
        => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, HarnessConstants.JsonOptions), cancellationToken);

    public static async Task WriteManifestAsync(string resultDirectory, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(resultDirectory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFileName(file), "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hash = await Sha256FileAsync(file, cancellationToken);
            builder.Append(hash).Append("  ").Append(Path.GetFileName(file)).AppendLine();
        }

        await File.WriteAllTextAsync(Path.Combine(resultDirectory, "SHA256SUMS.txt"), builder.ToString(), cancellationToken);
    }

    public static string BuildCommit()
        => BuildInfo.Commit;

    public static async Task<string> ExecutableHashAsync(CancellationToken cancellationToken)
        => await Sha256FileAsync(
            Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PimaxShellValidationHarness.exe"),
            cancellationToken);

    public static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
