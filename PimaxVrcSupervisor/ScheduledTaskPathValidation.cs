using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal sealed record ScheduledTaskPathValidationIssue(
    string TaskName,
    string TaskExecutablePath,
    string CurrentExecutableDirectory,
    string TaskExecutableDirectory,
    string Message);

internal sealed record ScheduledTaskExecutableValidationResult(
    string TaskName,
    bool Exists,
    bool PointsToCurrentDirectory,
    ScheduledTaskPathValidationIssue? Issue);

internal static class ScheduledTaskPathValidator
{
    public const string AutoLaunchTaskName = "Pimax VRC Supervisor Auto Launch";
    public const string SteamVrStartTaskName = "Pimax VRC Supervisor SteamVR Start";

    public static readonly string[] ManagedTaskNames =
    [
        AutoLaunchTaskName,
        SteamVrStartTaskName
    ];

    public static string GetCurrentExecutableDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDirectory(Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory);
        }

        var assemblyPath = AppContext.BaseDirectory;
        return NormalizeDirectory(assemblyPath);
    }

    public static ScheduledTaskPathValidationIssue? ValidateScheduledTaskExecutablePath(
        string taskName,
        string taskExecutablePath,
        string currentExecutableDirectory)
    {
        var normalizedCurrentDirectory = NormalizeDirectory(currentExecutableDirectory);
        if (string.IsNullOrWhiteSpace(taskExecutablePath))
        {
            return CreateIssue(
                taskName,
                "",
                normalizedCurrentDirectory,
                "",
                "The scheduled task executable path is empty.");
        }

        var normalizedExecutablePath = NormalizePath(taskExecutablePath);
        if (!File.Exists(normalizedExecutablePath))
        {
            return CreateIssue(
                taskName,
                normalizedExecutablePath,
                normalizedCurrentDirectory,
                Path.GetDirectoryName(normalizedExecutablePath) ?? "",
                $"The scheduled task executable does not exist: {normalizedExecutablePath}");
        }

        var taskExecutableDirectory = NormalizeDirectory(Path.GetDirectoryName(normalizedExecutablePath) ?? "");
        if (!string.Equals(taskExecutableDirectory, normalizedCurrentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return CreateIssue(
                taskName,
                normalizedExecutablePath,
                normalizedCurrentDirectory,
                taskExecutableDirectory,
                "This scheduled task points to an executable from another release folder.");
        }

        return null;
    }

    public static async Task<IReadOnlyList<ScheduledTaskPathValidationIssue>> ValidateExistingManagedTasksAsync(
        string currentExecutableDirectory,
        CancellationToken cancellationToken)
    {
        var issues = new List<ScheduledTaskPathValidationIssue>();
        foreach (var taskName in ManagedTaskNames)
        {
            var issue = await ValidateExistingTaskAsync(taskName, currentExecutableDirectory, cancellationToken);
            if (issue is not null)
            {
                issues.Add(issue);
            }
        }

        return issues;
    }

    public static IReadOnlyList<ScheduledTaskPathValidationIssue> ValidateExistingManagedTasks(string currentExecutableDirectory)
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            return ValidateExistingManagedTasksAsync(currentExecutableDirectory, cancellationTokenSource.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            return
            [
                CreateIssue(
                    "Scheduled Task validation",
                    "",
                    NormalizeDirectory(currentExecutableDirectory),
                    "",
                    $"Could not validate existing scheduled tasks: {ex.Message}")
            ];
        }
    }

    public static async Task<ScheduledTaskPathValidationIssue?> ValidateExistingTaskAsync(
        string taskName,
        string currentExecutableDirectory,
        CancellationToken cancellationToken)
    {
        var result = await ValidateExistingTaskExecutableAsync(taskName, currentExecutableDirectory, cancellationToken);
        return result.Issue;
    }

    public static ScheduledTaskExecutableValidationResult ValidateExistingTaskExecutable(
        string taskName,
        string currentExecutableDirectory)
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            return ValidateExistingTaskExecutableAsync(taskName, currentExecutableDirectory, cancellationTokenSource.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            return new ScheduledTaskExecutableValidationResult(
                taskName,
                true,
                false,
                CreateIssue(
                    taskName,
                    "",
                    NormalizeDirectory(currentExecutableDirectory),
                    "",
                    $"Could not validate existing scheduled task: {ex.Message}"));
        }
    }

    public static async Task<ScheduledTaskExecutableValidationResult> ValidateExistingTaskExecutableAsync(
        string taskName,
        string currentExecutableDirectory,
        CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(
            "schtasks.exe",
            ["/Query", "/TN", taskName, "/XML"],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            return new ScheduledTaskExecutableValidationResult(taskName, false, false, null);
        }

        if (!TryExtractExecutablePath(result.Output, out var executablePath, out var error))
        {
            return new ScheduledTaskExecutableValidationResult(
                taskName,
                true,
                false,
                CreateIssue(
                    taskName,
                    "",
                    NormalizeDirectory(currentExecutableDirectory),
                    "",
                    $"Could not read the scheduled task executable path: {error}"));
        }

        var issue = ValidateScheduledTaskExecutablePath(taskName, executablePath, currentExecutableDirectory);
        return new ScheduledTaskExecutableValidationResult(
            taskName,
            true,
            issue is null,
            issue);
    }

    public static void ThrowIfInvalidScheduledTaskExecutablePath(
        string taskName,
        string taskExecutablePath,
        string currentExecutableDirectory)
    {
        var issue = ValidateScheduledTaskExecutablePath(taskName, taskExecutablePath, currentExecutableDirectory);
        if (issue is not null)
        {
            throw new InvalidOperationException(FormatIssue(issue));
        }
    }

    public static string FormatIssue(ScheduledTaskPathValidationIssue issue)
    {
        var builder = new StringBuilder();
        builder.AppendLine(issue.Message);
        builder.AppendLine($"Scheduled task: {issue.TaskName}");
        builder.AppendLine($"Current Config Editor folder: {issue.CurrentExecutableDirectory}");
        if (!string.IsNullOrWhiteSpace(issue.TaskExecutableDirectory))
        {
            builder.AppendLine($"Scheduled task executable folder: {issue.TaskExecutableDirectory}");
        }

        if (!string.IsNullOrWhiteSpace(issue.TaskExecutablePath))
        {
            builder.AppendLine($"Scheduled task executable: {issue.TaskExecutablePath}");
        }

        builder.Append("Please update or recreate the scheduled task from the current release.");
        return builder.ToString();
    }

    public static string NormalizePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(expanded);
    }

    public static string NormalizeDirectory(string directory)
    {
        var normalized = NormalizePath(directory);
        return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static ScheduledTaskPathValidationIssue CreateIssue(
        string taskName,
        string taskExecutablePath,
        string currentExecutableDirectory,
        string taskExecutableDirectory,
        string message)
        => new(
            taskName,
            taskExecutablePath,
            currentExecutableDirectory,
            taskExecutableDirectory,
            message);

    private static bool TryExtractExecutablePath(string taskXml, out string executablePath, out string error)
    {
        executablePath = "";
        error = "";
        try
        {
            var document = XDocument.Parse(taskXml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var exec = document.Descendants(ns + "Exec").FirstOrDefault()
                ?? document.Descendants("Exec").FirstOrDefault();
            if (exec is null)
            {
                error = "Task XML does not contain an Exec action.";
                return false;
            }

            var command = (string?)exec.Element(ns + "Command") ?? (string?)exec.Element("Command") ?? "";
            var arguments = (string?)exec.Element(ns + "Arguments") ?? (string?)exec.Element("Arguments") ?? "";
            var workingDirectory = (string?)exec.Element(ns + "WorkingDirectory") ?? (string?)exec.Element("WorkingDirectory") ?? "";
            if (string.IsNullOrWhiteSpace(command))
            {
                error = "Task action command is empty.";
                return false;
            }

            if (string.Equals(Path.GetFileName(command), "powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (TryExtractPowerShellFilePath(arguments, out executablePath))
                {
                    return true;
                }

                error = "PowerShell task action does not contain a parseable -FilePath argument.";
                return false;
            }

            executablePath = ResolveTaskPath(command, workingDirectory);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryExtractPowerShellFilePath(string arguments, out string executablePath)
    {
        executablePath = "";
        var singleQuotedMatch = Regex.Match(arguments, @"-FilePath\s+'((?:''|[^'])+)'", RegexOptions.IgnoreCase);
        if (singleQuotedMatch.Success)
        {
            executablePath = singleQuotedMatch.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
            return true;
        }

        var doubleQuotedMatch = Regex.Match(arguments, "-FilePath\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (doubleQuotedMatch.Success)
        {
            executablePath = doubleQuotedMatch.Groups[1].Value;
            return true;
        }

        var unquotedMatch = Regex.Match(arguments, @"-FilePath\s+(\S+)", RegexOptions.IgnoreCase);
        if (unquotedMatch.Success)
        {
            executablePath = unquotedMatch.Groups[1].Value;
            return true;
        }

        return false;
    }

    private static string ResolveTaskPath(string path, string workingDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (Path.IsPathFullyQualified(expanded))
        {
            return expanded;
        }

        var baseDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Environment.ExpandEnvironmentVariables(workingDirectory);
        return Path.Combine(baseDirectory, expanded);
    }

    private static async Task<ScheduledTaskProcessResult> RunProcessCaptureAsync(
        string fileName,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ScheduledTaskProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw new TimeoutException($"{fileName} did not finish before scheduled task validation timed out.");
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private sealed record ScheduledTaskProcessResult(int ExitCode, string Output, string Error);
}
