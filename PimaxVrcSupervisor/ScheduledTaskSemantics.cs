using System.Text;

internal sealed record ExistingWatcherTask(
    string Executable,
    string Arguments,
    string WorkingDirectory,
    ParsedWatcherArguments ParsedArguments,
    bool HasLogonTrigger,
    bool UsesInteractiveToken,
    bool UsesHighestAvailableRunLevel,
    bool IgnoreNewInstances,
    bool StartWhenAvailable,
    bool AllowStartOnDemand,
    bool Enabled,
    bool Hidden,
    bool ExecutionTimeLimitUnlimited,
    string[] SettingMismatches);

internal sealed record ParsedWatcherArguments(
    bool WatcherMode,
    bool SkipCurrentSteamVrSession,
    bool UseDesktopTuiDefaultInterface,
    string? ConfigPath,
    string[] UnknownArguments,
    string? UnsupportedReason);

internal static class ScheduledTaskSemantics
{
    public const string WatcherArgument = "--watch-vrchat-auto-launch";

    public static string BuildWatcherArguments(
        bool skipCurrentSteamVrSession,
        bool useDesktopTuiDefaultInterface,
        string? configPath,
        IReadOnlyList<string>? preservedUnknownArguments = null)
    {
        var arguments = new List<string> { WatcherArgument };
        if (skipCurrentSteamVrSession)
        {
            arguments.Add("--skip-current-vrserver-session");
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            arguments.Add("--config");
            arguments.Add(QuoteArgument(configPath));
        }

        if (useDesktopTuiDefaultInterface)
        {
            arguments.Add("--desktop-tui-default-interface");
        }

        if (preservedUnknownArguments is not null)
        {
            arguments.AddRange(preservedUnknownArguments);
        }

        return string.Join(' ', arguments);
    }

    public static bool ResolvePersistentInterface(
        bool? requestedDesktopTuiDefaultInterface,
        ExistingWatcherTask? existingTask,
        out string source)
    {
        if (requestedDesktopTuiDefaultInterface is { } requested)
        {
            source = "Configurator selection";
            return requested;
        }

        if (existingTask is not null)
        {
            source = "existing scheduled task";
            return existingTask.ParsedArguments.UseDesktopTuiDefaultInterface;
        }

        source = "documented product default";
        return true;
    }

    public static bool IsExistingTaskSemanticallyValid(
        ExistingWatcherTask? existingTask,
        string watcherPath,
        string workingDirectory,
        ParsedWatcherArguments desiredArguments,
        out string mismatchReason)
    {
        if (existingTask is null)
        {
            mismatchReason = "task does not exist.";
            return false;
        }

        if (!PathsEqual(existingTask.Executable, watcherPath))
        {
            mismatchReason = "task executable path did not match the expected watcher.";
            return false;
        }

        if (!File.Exists(watcherPath))
        {
            mismatchReason = "expected watcher executable is missing.";
            return false;
        }

        if (!PathsEqual(existingTask.WorkingDirectory, workingDirectory))
        {
            mismatchReason = "task working directory did not match the expected release folder.";
            return false;
        }

        var existingArguments = existingTask.ParsedArguments;
        if (!existingArguments.WatcherMode)
        {
            mismatchReason = "watcher mode argument was missing.";
            return false;
        }

        if (existingArguments.SkipCurrentSteamVrSession != desiredArguments.SkipCurrentSteamVrSession)
        {
            mismatchReason = "skip-current-session setting did not match.";
            return false;
        }

        if (existingArguments.UseDesktopTuiDefaultInterface != desiredArguments.UseDesktopTuiDefaultInterface)
        {
            mismatchReason = "Terminal UI default-interface setting did not match the persistent preference.";
            return false;
        }

        if (!PathsEqual(existingArguments.ConfigPath, desiredArguments.ConfigPath))
        {
            mismatchReason = "config path did not match.";
            return false;
        }

        if (!StringArraysEqual(existingArguments.UnknownArguments, desiredArguments.UnknownArguments))
        {
            mismatchReason = "unknown watcher arguments did not match the preserved argument set.";
            return false;
        }

        if (existingTask.SettingMismatches.Length > 0)
        {
            mismatchReason = "task settings did not match the expected hidden watcher settings. "
                + string.Join("; ", existingTask.SettingMismatches);
            return false;
        }

        mismatchReason = "";
        return true;
    }

    public static bool IsTaskRebound(
        ExistingWatcherTask existingTask,
        string watcherPath,
        string workingDirectory)
        => !PathsEqual(existingTask.Executable, watcherPath)
            || !PathsEqual(existingTask.WorkingDirectory, workingDirectory);

    public static ParsedWatcherArguments ParseWatcherArguments(string arguments)
    {
        if (!HasBalancedQuotes(arguments))
        {
            return new ParsedWatcherArguments(
                WatcherMode: false,
                SkipCurrentSteamVrSession: false,
                UseDesktopTuiDefaultInterface: false,
                ConfigPath: null,
                UnknownArguments: [],
                UnsupportedReason: "unbalanced quotes in watcher arguments.");
        }

        var tokens = SplitCommandLine(arguments);
        var unknown = new List<string>();
        var watcherMode = false;
        var skipCurrentSteamVrSession = false;
        var useDesktopTuiDefaultInterface = false;
        string? configPath = null;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (string.Equals(token, WatcherArgument, StringComparison.OrdinalIgnoreCase))
            {
                watcherMode = true;
                continue;
            }

            if (string.Equals(token, "--skip-current-vrserver-session", StringComparison.OrdinalIgnoreCase))
            {
                skipCurrentSteamVrSession = true;
                continue;
            }

            if (string.Equals(token, "--desktop-tui-default-interface", StringComparison.OrdinalIgnoreCase))
            {
                useDesktopTuiDefaultInterface = true;
                continue;
            }

            if (string.Equals(token, "--config", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= tokens.Count)
                {
                    unknown.Add(token);
                    continue;
                }

                index++;
                configPath = tokens[index];
                continue;
            }

            if (token.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
            {
                configPath = token["--config=".Length..];
                continue;
            }

            unknown.Add(QuoteArgumentIfNeeded(token));
        }

        return new ParsedWatcherArguments(
            watcherMode,
            skipCurrentSteamVrSession,
            useDesktopTuiDefaultInterface,
            configPath,
            unknown.ToArray(),
            null);
    }

    private static bool HasBalancedQuotes(string value)
    {
        var inQuotes = false;
        foreach (var character in value)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
            }
        }

        return !inQuotes;
    }

    private static List<string> SplitCommandLine(string arguments)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var character = arguments[index];
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }

    private static bool StringArraysEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string QuoteArgumentIfNeeded(string argument)
        => argument.Any(char.IsWhiteSpace) ? QuoteArgument(argument) : argument;

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        }

        return string.Equals(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(left)),
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteArgument(string argument)
        => string.IsNullOrEmpty(argument)
            ? "\"\""
            : "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
