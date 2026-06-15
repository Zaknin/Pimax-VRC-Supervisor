using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal enum ConfigMigrationOutcome
{
    NoCandidate,
    UsingExplicit,
    Imported,
    KeptExternal,
    NewConfig,
    Cancelled,
    Failed
}

internal sealed record ConfigMigrationCandidate(
    string Path,
    string Source,
    bool IsExplicit);

internal sealed record ConfigImportResult(
    ConfigMigrationOutcome Outcome,
    string? SourcePath,
    string? DestinationPath,
    string Message);

internal static class ConfigMigrationSupport
{
    public const string DefaultConfigFileName = "supervisor.config.json";
    public const string ActiveConfigSelectionFileName = "supervisor.active-config.txt";

    public static string NormalizePath(string path)
        => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

    public static string NormalizeDirectory(string directory)
        => NormalizePath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool IsPathInDirectory(string path, string directory)
    {
        var pathDirectory = Path.GetDirectoryName(NormalizePath(path)) ?? "";
        return string.Equals(
            NormalizeDirectory(pathDirectory),
            NormalizeDirectory(directory),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryGetActiveConfigSelectionPath(string releaseDirectory)
    {
        try
        {
            var selectionPath = Path.Combine(releaseDirectory, ActiveConfigSelectionFileName);
            if (!File.Exists(selectionPath))
            {
                return null;
            }

            var selectedConfig = File.ReadAllText(selectionPath).Trim();
            if (string.IsNullOrWhiteSpace(selectedConfig))
            {
                return null;
            }

            var fullPath = Path.IsPathRooted(selectedConfig)
                ? NormalizePath(selectedConfig)
                : NormalizePath(Path.Combine(releaseDirectory, selectedConfig));
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    public static void TrySaveActiveConfigSelection(string releaseDirectory, string configPath)
    {
        try
        {
            var fullPath = NormalizePath(configPath);
            var appDirectory = NormalizeDirectory(releaseDirectory);
            var configDirectory = NormalizeDirectory(Path.GetDirectoryName(fullPath) ?? "");
            var value = string.Equals(appDirectory, configDirectory, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(fullPath)
                : fullPath;
            File.WriteAllText(
                Path.Combine(releaseDirectory, ActiveConfigSelectionFileName),
                value,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Active config selection is a convenience marker; callers still receive the selected path.
        }
    }

    public static IReadOnlyList<ConfigMigrationCandidate> FindCandidates(
        string currentReleaseDirectory,
        string? explicitConfigPath,
        IEnumerable<string?> additionalCandidates)
    {
        var candidates = new List<ConfigMigrationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(explicitConfigPath, "explicit --config", isExplicit: true);
        foreach (var candidate in additionalCandidates)
        {
            AddCandidate(candidate, "remembered config", isExplicit: false);
        }

        foreach (var candidate in FindSiblingReleaseConfigCandidates(currentReleaseDirectory))
        {
            AddCandidate(candidate.Path, candidate.Source, isExplicit: false);
        }

        return candidates;

        void AddCandidate(string? path, string source, bool isExplicit)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = NormalizePath(path);
            }
            catch
            {
                return;
            }

            if (!File.Exists(fullPath)
                || IsPathInDirectory(fullPath, currentReleaseDirectory)
                || !seen.Add(fullPath))
            {
                return;
            }

            candidates.Add(new ConfigMigrationCandidate(fullPath, source, isExplicit));
        }
    }

    public static ConfigImportResult ImportConfig(
        string sourcePath,
        string currentReleaseDirectory)
    {
        var normalizedSource = NormalizePath(sourcePath);
        if (!File.Exists(normalizedSource))
        {
            return new ConfigImportResult(
                ConfigMigrationOutcome.Failed,
                normalizedSource,
                null,
                "The selected configuration file does not exist.");
        }

        Directory.CreateDirectory(currentReleaseDirectory);
        var destinationPath = ChooseImportDestination(normalizedSource, currentReleaseDirectory);
        var tempPath = Path.Combine(
            currentReleaseDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(normalizedSource, tempPath, overwrite: false);
            ValidateConfigFile(tempPath);
            File.Move(tempPath, destinationPath, overwrite: false);
            TrySaveActiveConfigSelection(currentReleaseDirectory, destinationPath);
            return new ConfigImportResult(
                ConfigMigrationOutcome.Imported,
                normalizedSource,
                destinationPath,
                "Configuration imported into the current release.");
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempPath);
            return new ConfigImportResult(
                ConfigMigrationOutcome.Failed,
                normalizedSource,
                destinationPath,
                $"Could not import configuration: {ex.Message}");
        }
    }

    public static string ChooseImportDestination(string sourcePath, string currentReleaseDirectory)
    {
        var sourceFileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            sourceFileName = DefaultConfigFileName;
        }

        var defaultPath = Path.Combine(currentReleaseDirectory, DefaultConfigFileName);
        var baseFileName = sourceFileName;
        if (string.Equals(sourceFileName, DefaultConfigFileName, StringComparison.OrdinalIgnoreCase))
        {
            baseFileName = File.Exists(defaultPath)
                ? "supervisor_moved.config.json"
                : DefaultConfigFileName;
        }

        var destinationPath = Path.Combine(currentReleaseDirectory, baseFileName);
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var stem = Path.GetFileNameWithoutExtension(baseFileName);
        var extension = Path.GetExtension(baseFileName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var candidate = Path.Combine(currentReleaseDirectory, $"{stem}_{timestamp}{extension}");
        var counter = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(currentReleaseDirectory, $"{stem}_{timestamp}-{counter}{extension}");
            counter++;
        }

        return candidate;
    }

    private static IEnumerable<ConfigMigrationCandidate> FindSiblingReleaseConfigCandidates(string currentReleaseDirectory)
    {
        var current = NormalizeDirectory(currentReleaseDirectory);
        var parent = Directory.GetParent(current)?.FullName;
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(parent, "PimaxVrcSupervisor*"))
        {
            if (string.Equals(NormalizeDirectory(directory), current, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var active = TryGetActiveConfigSelectionPath(directory);
            if (!string.IsNullOrWhiteSpace(active))
            {
                yield return new ConfigMigrationCandidate(active, "sibling release active config", false);
            }

            foreach (var fileName in new[] { DefaultConfigFileName, "supervisor_moved.config.json" })
            {
                var path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                {
                    yield return new ConfigMigrationCandidate(path, "sibling release config", false);
                }
            }
        }
    }

    private static void ValidateConfigFile(string path)
    {
        var json = File.ReadAllText(path);
        _ = JsonSerializer.Deserialize<ConfigValidationModel>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new JsonStringEnumConverter() }
            }) ?? new ConfigValidationModel();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class ConfigValidationModel
    {
    }
}
