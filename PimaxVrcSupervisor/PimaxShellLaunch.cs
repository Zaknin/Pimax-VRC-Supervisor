using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

internal static class PimaxShellLaunchSchema
{
    public const string Version = "pimax-shell-launch-result-v1";
}

internal static class PimaxShellLaunchResultName
{
    public const string LaunchedAndRegistered = "launchedAndRegistered";
    public const string LaunchedButNotRegistered = "launchedButNotRegistered";
    public const string ShellLaunchFailed = "shellLaunchFailed";
    public const string PreconditionRefused = "preconditionRefused";
    public const string VerificationInconclusive = "verificationInconclusive";
}

internal sealed record PimaxShellLaunchRequest(TimeSpan VerificationTimeout, TimeSpan SampleInterval);

internal sealed record PimaxShellLaunchResult(
    string Schema,
    DateTimeOffset CollectedAt,
    string Result,
    bool Success,
    string ShortcutState,
    string? SanitizedShortcutPath,
    string ExecutionContextState,
    string PreLaunchState,
    string[] BlockingProcesses,
    bool ShellRequestAccepted,
    int ShellRequestCount,
    int RetryCount,
    int VerificationTimeoutSeconds,
    int SamplesCollected,
    bool SoftwareStackReady,
    bool HeadsetDetected,
    bool RegistrationHealthy,
    int StableHealthySampleCount,
    string[] Warnings,
    string[] Errors,
    string HumanReadableSummary);

internal sealed record PimaxShellLaunchProcessRecord(string Name, int ProcessId, int SessionId);

internal sealed record PimaxShellLaunchShortcut(
    string Path,
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string SourceRoot);

internal sealed record PimaxShellLaunchShortcutDiscoveryResult(
    bool Accepted,
    PimaxShellLaunchShortcut? Shortcut,
    string State,
    string[] Warnings,
    string[] Errors,
    PimaxShellLaunchShortcut[] Candidates,
    PimaxShellLaunchShortcut[] TrustedCandidates);

internal sealed record PimaxShellRequestResult(
    DateTimeOffset RequestedAt,
    bool Accepted,
    string? ExceptionType,
    string? SanitizedMessage,
    int ShellRequestCount,
    int RetryCount);

internal sealed record PimaxShellLaunchVerificationSample(
    DateTimeOffset CollectedAt,
    bool SoftwareStackReady,
    bool HeadsetDetected,
    bool RegistrationHealthy,
    string RegistrationState,
    string RegistrationConfidence,
    string[] Warnings,
    string[] Errors);

internal interface IPimaxShellLaunchProcessInventory
{
    PimaxShellLaunchProcessRecord[] Collect();
}

internal interface IPimaxShellShortcutReader
{
    PimaxShellLaunchShortcut Read(string path, string sourceRoot);
}

internal interface IPimaxShellLauncher
{
    PimaxShellRequestResult OpenOnce(PimaxShellLaunchShortcut shortcut);
}

internal interface IPimaxShellLaunchVerifier
{
    Task<PimaxShellLaunchVerificationSample> CollectAsync(CancellationToken cancellationToken);
}

internal sealed class SystemPimaxShellLaunchProcessInventory : IPimaxShellLaunchProcessInventory
{
    public PimaxShellLaunchProcessRecord[] Collect()
        => Process.GetProcesses()
            .Select(ToRecord)
            .Where(record => !string.IsNullOrWhiteSpace(record.Name))
            .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.ProcessId)
            .ToArray();

    private static PimaxShellLaunchProcessRecord ToRecord(Process process)
    {
        try
        {
            return new PimaxShellLaunchProcessRecord(SafeName(process), SafeId(process), SafeSessionId(process));
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string SafeName(Process process)
    {
        try { return process.ProcessName; } catch { return ""; }
    }

    private static int SafeId(Process process)
    {
        try { return process.Id; } catch { return -1; }
    }

    private static int SafeSessionId(Process process)
    {
        try { return process.SessionId; } catch { return -1; }
    }
}

internal sealed class ComPimaxShellShortcutReader : IPimaxShellShortcutReader
{
    public PimaxShellLaunchShortcut Read(string path, string sourceRoot)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        dynamic shortcut = shell.CreateShortcut(path);
        return new PimaxShellLaunchShortcut(
            path,
            (string)shortcut.TargetPath,
            (string)shortcut.Arguments,
            (string)shortcut.WorkingDirectory,
            sourceRoot);
    }
}

internal sealed class PimaxShellShortcutDiscovery
{
    private const string ShortcutName = "PimaxPlay.lnk";
    private static readonly string[] RejectedTargetExtensions = [".ps1", ".cmd", ".bat", ".vbs", ".js", ".url"];

    private readonly IPimaxShellShortcutReader _reader;
    private readonly string[] _roots;

    public PimaxShellShortcutDiscovery(IPimaxShellShortcutReader reader, string[]? roots = null)
    {
        _reader = reader;
        _roots = roots ?? DefaultRoots();
    }

    public PimaxShellLaunchShortcutDiscoveryResult Discover()
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var candidates = new List<PimaxShellLaunchShortcut>();

        foreach (var root in _roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var link in Directory.EnumerateFiles(root, ShortcutName, SearchOption.AllDirectories))
            {
                try
                {
                    candidates.Add(_reader.Read(link, root));
                }
                catch (Exception ex)
                {
                    errors.Add($"Could not read shortcut {SanitizePath(link)}: {SanitizeMessage(ex.Message)}");
                }
            }
        }

        var trusted = candidates.Where(IsTrusted).DistinctBy(candidate => CanonicalPath(candidate.Path), StringComparer.OrdinalIgnoreCase).ToArray();
        if (trusted.Length != 1)
        {
            errors.Add(trusted.Length == 0
                ? "No trusted official PimaxPlay.lnk shortcut was found in the bounded Start Menu Programs roots."
                : "Multiple trusted official PimaxPlay.lnk shortcuts were found in the bounded Start Menu Programs roots.");
            return new PimaxShellLaunchShortcutDiscoveryResult(false, null, trusted.Length == 0 ? "missing" : "duplicate", warnings.ToArray(), errors.ToArray(), candidates.ToArray(), trusted);
        }

        return new PimaxShellLaunchShortcutDiscoveryResult(true, trusted[0], "trusted", warnings.ToArray(), errors.ToArray(), candidates.ToArray(), trusted);
    }

    internal static bool IsTrusted(PimaxShellLaunchShortcut shortcut)
    {
        var source = CanonicalPath(shortcut.Path);
        var sourceRoot = CanonicalPath(shortcut.SourceRoot);
        var target = CanonicalPath(shortcut.TargetPath);
        var workingDirectory = string.IsNullOrWhiteSpace(shortcut.WorkingDirectory)
            ? ""
            : CanonicalPath(shortcut.WorkingDirectory);

        if (string.IsNullOrWhiteSpace(source)
            || string.IsNullOrWhiteSpace(sourceRoot)
            || string.IsNullOrWhiteSpace(target)
            || !source.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            || source.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsLocalPath(source) || !IsLocalPath(target) || !IsPathInsideRoot(source, sourceRoot))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(shortcut.Arguments))
        {
            return false;
        }

        var targetExtension = Path.GetExtension(target);
        if (!targetExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || RejectedTargetExtensions.Contains(targetExtension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsUnderPimaxProgramFiles(target))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory)
            && (!IsLocalPath(workingDirectory) || !IsUnderPimaxProgramFiles(workingDirectory)))
        {
            return false;
        }

        return true;
    }

    public static string SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile)
            ? path
            : path.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "";
        }

        return SanitizePath(message)
            .Replace(Environment.MachineName, "%COMPUTERNAME%", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] DefaultRoots()
        => [
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        ];

    private static bool IsLocalPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsFile;
    }

    private static bool IsUnderPimaxProgramFiles(string path)
    {
        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        return programFiles
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Any(root => IsPathInsideRoot(path, Path.Combine(root, "Pimax")));
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        var canonicalPath = CanonicalPath(path);
        var canonicalRoot = CanonicalPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return canonicalPath.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)
            || canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || canonicalPath.StartsWith(canonicalRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }
        catch
        {
            return path;
        }
    }
}

internal sealed class DefaultPimaxShellLauncher : IPimaxShellLauncher
{
    private int _shellRequestCount;

    public PimaxShellRequestResult OpenOnce(PimaxShellLaunchShortcut shortcut)
    {
        var requestedAt = DateTimeOffset.Now;
        try
        {
            if (Interlocked.Exchange(ref _shellRequestCount, 1) != 0)
            {
                throw new InvalidOperationException("Shell one-shot guard rejected a second activation request.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = shortcut.Path,
                Verb = "open",
                UseShellExecute = true
            })?.Dispose();

            return new PimaxShellRequestResult(requestedAt, true, null, null, _shellRequestCount, 0);
        }
        catch (Exception ex)
        {
            return new PimaxShellRequestResult(
                requestedAt,
                false,
                ex.GetType().Name,
                PimaxShellShortcutDiscovery.SanitizeMessage(ex.Message),
                _shellRequestCount,
                0);
        }
    }
}

internal sealed class DefaultPimaxShellLaunchVerifier(SupervisorConfig config) : IPimaxShellLaunchVerifier
{
    private static readonly HashSet<string> RequiredStackNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PimaxClient",
        "DeviceSetting",
        "PiPlayService",
        "PiService",
        "pi_server"
    };

    public async Task<PimaxShellLaunchVerificationSample> CollectAsync(CancellationToken cancellationToken)
    {
        var processes = Process.GetProcesses()
            .Select(process =>
            {
                try { return process.ProcessName; }
                catch { return ""; }
                finally { process.Dispose(); }
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registration = await new PimaxRegistrationAssessmentCoordinator().CollectAsync(config, cancellationToken);
        var softwareReady = RequiredStackNames.All(processes.Contains);
        var headsetDetected = registration.Assessment.Evidence.HeadsetPowerOnGroupPresent
            || registration.Assessment.Evidence.CrystalRuntimeGroupPresent;
        var registrationHealthy = string.Equals(registration.Assessment.State, PimaxRegistrationState.RegisteredReady, StringComparison.OrdinalIgnoreCase)
            && string.Equals(registration.Assessment.Confidence, PimaxRegistrationConfidence.Confirmed, StringComparison.OrdinalIgnoreCase);

        return new PimaxShellLaunchVerificationSample(
            DateTimeOffset.Now,
            softwareReady,
            headsetDetected,
            registrationHealthy,
            registration.Assessment.State,
            registration.Assessment.Confidence,
            registration.Warnings,
            registration.Errors);
    }
}

internal sealed class PimaxShellLaunchRunner
{
    private static readonly HashSet<string> BlockingProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PimaxClient",
        "DeviceSetting",
        "PiPlayService",
        "PiService",
        "pi_server",
        "PVRHome",
        "pi_overlay",
        "lighthouse_console"
    };

    private static readonly HashSet<string> PermittedPersistentProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PiPlatformService_64",
        "platform_runtime_VR4PIMAXP3B_service",
        "PiServiceLauncher",
        "vrss_gaze_provider"
    };

    private readonly IPimaxShellLaunchProcessInventory _processInventory;
    private readonly PimaxShellShortcutDiscovery _shortcutDiscovery;
    private readonly IPimaxShellLauncher _launcher;
    private readonly IPimaxShellLaunchVerifier _verifier;
    private readonly Func<bool> _isWindows;
    private readonly Func<bool> _isElevated;
    private readonly Func<bool> _isUserInteractive;
    private readonly Func<int> _currentSessionId;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly string[] _commandLineArgs;

    public PimaxShellLaunchRunner(
        IPimaxShellLaunchProcessInventory processInventory,
        PimaxShellShortcutDiscovery shortcutDiscovery,
        IPimaxShellLauncher launcher,
        IPimaxShellLaunchVerifier verifier,
        Func<bool>? isWindows = null,
        Func<bool>? isElevated = null,
        Func<bool>? isUserInteractive = null,
        Func<int>? currentSessionId = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        string[]? commandLineArgs = null)
    {
        _processInventory = processInventory;
        _shortcutDiscovery = shortcutDiscovery;
        _launcher = launcher;
        _verifier = verifier;
        _isWindows = isWindows ?? (() => OperatingSystem.IsWindows());
        _isElevated = isElevated ?? IsElevated;
        _isUserInteractive = isUserInteractive ?? (() => Environment.UserInteractive);
        _currentSessionId = currentSessionId ?? (() => Process.GetCurrentProcess().SessionId);
        _delayAsync = delayAsync ?? Task.Delay;
        _commandLineArgs = commandLineArgs ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
    }

    public async Task<PimaxShellLaunchResult> RunAsync(PimaxShellLaunchRequest request, CancellationToken cancellationToken)
    {
        var collectedAt = DateTimeOffset.Now;
        var warnings = new List<string>();
        var errors = new List<string>();
        var processes = _processInventory.Collect();
        var executionContextRefusal = ValidateExecutionContext(processes);
        if (executionContextRefusal is not null)
        {
            errors.Add(executionContextRefusal);
            return Result(collectedAt, PimaxShellLaunchResultName.PreconditionRefused, false, "notChecked", null, "refused", "notChecked", [], null, request, [], warnings, errors, "Relaunch refused: " + executionContextRefusal);
        }

        var shortcut = _shortcutDiscovery.Discover();
        warnings.AddRange(shortcut.Warnings);
        if (!shortcut.Accepted || shortcut.Shortcut is null)
        {
            errors.AddRange(shortcut.Errors);
            return Result(collectedAt, PimaxShellLaunchResultName.PreconditionRefused, false, shortcut.State, null, "accepted", "notChecked", [], null, request, [], warnings, errors, "Relaunch refused: the official Pimax Play Start Menu shortcut is not trusted.");
        }

        var blocking = FindBlockingProcesses(processes);
        if (blocking.Length > 0)
        {
            errors.Add("Pimax Play is still running. Exit Pimax Play from its tray menu, wait for shutdown to complete, and try again.");
            return Result(collectedAt, PimaxShellLaunchResultName.PreconditionRefused, false, shortcut.State, PimaxShellShortcutDiscovery.SanitizePath(shortcut.Shortcut.Path), "accepted", "blocked", blocking, null, request, [], warnings, errors, "Relaunch refused because Pimax Play is still running.");
        }

        var shell = _launcher.OpenOnce(shortcut.Shortcut);
        if (!shell.Accepted)
        {
            if (!string.IsNullOrWhiteSpace(shell.ExceptionType))
            {
                errors.Add($"{shell.ExceptionType}: {shell.SanitizedMessage}");
            }

            return Result(collectedAt, PimaxShellLaunchResultName.ShellLaunchFailed, false, shortcut.State, PimaxShellShortcutDiscovery.SanitizePath(shortcut.Shortcut.Path), "accepted", "clear", [], shell, request, [], warnings, errors, "Windows could not launch the official Pimax Play shortcut.");
        }

        var samples = await CollectVerificationSamplesAsync(request, cancellationToken);
        warnings.AddRange(samples.SelectMany(sample => sample.Warnings));
        errors.AddRange(samples.SelectMany(sample => sample.Errors));
        var stableHealthySamples = CountConsecutiveHealthySamples(samples);
        var result = Classify(shell, samples, stableHealthySamples);
        var summary = result switch
        {
            PimaxShellLaunchResultName.LaunchedAndRegistered => "Pimax Play launched successfully and the headset is registered.",
            PimaxShellLaunchResultName.LaunchedButNotRegistered => "Pimax Play launched, but headset registration did not recover. You may still need to use the existing manual USB reseat procedure.",
            PimaxShellLaunchResultName.VerificationInconclusive => "Pimax Play launch was accepted, but the health result could not be classified safely.",
            _ => "Pimax Play launch completed with an unexpected classification."
        };

        return Result(collectedAt, result, string.Equals(result, PimaxShellLaunchResultName.LaunchedAndRegistered, StringComparison.Ordinal), shortcut.State, PimaxShellShortcutDiscovery.SanitizePath(shortcut.Shortcut.Path), "accepted", "clear", [], shell, request, samples, warnings, errors, summary);
    }

    internal static string[] FindBlockingProcesses(PimaxShellLaunchProcessRecord[] processes)
        => processes
            .Where(process => BlockingProcessNames.Contains(process.Name)
                && !PermittedPersistentProcessNames.Contains(process.Name))
            .Select(process => process.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static int CountConsecutiveHealthySamples(PimaxShellLaunchVerificationSample[] samples)
    {
        var count = 0;
        for (var index = samples.Length - 1; index >= 0; index--)
        {
            if (!samples[index].SoftwareStackReady || !samples[index].RegistrationHealthy)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private async Task<PimaxShellLaunchVerificationSample[]> CollectVerificationSamplesAsync(PimaxShellLaunchRequest request, CancellationToken cancellationToken)
    {
        var samples = new List<PimaxShellLaunchVerificationSample>();
        var deadline = DateTimeOffset.UtcNow.Add(request.VerificationTimeout);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = await _verifier.CollectAsync(cancellationToken);
            samples.Add(sample);
            if (CountConsecutiveHealthySamples(samples.ToArray()) >= 2)
            {
                break;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            await _delayAsync(request.SampleInterval, cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return samples.ToArray();
    }

    private string? ValidateExecutionContext(PimaxShellLaunchProcessRecord[] processes)
    {
        if (!_isWindows())
        {
            return "The Pimax Play Shell relaunch is only supported on Windows.";
        }

        if (_isElevated())
        {
            return "The proven launch mechanism requires a normal non-elevated interactive Windows session.";
        }

        if (!_isUserInteractive())
        {
            return "The Pimax Play Shell relaunch requires an interactive desktop session.";
        }

        var sessionId = _currentSessionId();
        if (sessionId <= 0)
        {
            return "The Pimax Play Shell relaunch cannot run from service/session 0.";
        }

        if (_commandLineArgs.Any(arg =>
            arg.Equals("--watch-vrchat-auto-launch", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--steamvr-start", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--install-auto-launch-task", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--apply-startup-integration", StringComparison.OrdinalIgnoreCase)))
        {
            return "The Pimax Play Shell relaunch cannot run from watcher, scheduled-task, or startup-integration context.";
        }

        var explorerInSession = processes.Any(process =>
            process.SessionId == sessionId
            && process.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase));
        if (!explorerInSession)
        {
            return "No explorer.exe instance was found in the current interactive session.";
        }

        return null;
    }

    private static string Classify(PimaxShellRequestResult shell, PimaxShellLaunchVerificationSample[] samples, int stableHealthySamples)
    {
        if (!shell.Accepted)
        {
            return PimaxShellLaunchResultName.ShellLaunchFailed;
        }

        if (stableHealthySamples >= 2)
        {
            return PimaxShellLaunchResultName.LaunchedAndRegistered;
        }

        if (samples.Length == 0 || samples.Any(sample => sample.Errors.Length > 0))
        {
            return PimaxShellLaunchResultName.VerificationInconclusive;
        }

        if (samples.Any(sample => sample.SoftwareStackReady))
        {
            return PimaxShellLaunchResultName.LaunchedButNotRegistered;
        }

        return PimaxShellLaunchResultName.VerificationInconclusive;
    }

    private static PimaxShellLaunchResult Result(
        DateTimeOffset collectedAt,
        string result,
        bool success,
        string shortcutState,
        string? sanitizedShortcutPath,
        string executionContextState,
        string preLaunchState,
        string[] blockingProcesses,
        PimaxShellRequestResult? shell,
        PimaxShellLaunchRequest request,
        PimaxShellLaunchVerificationSample[] samples,
        List<string> warnings,
        List<string> errors,
        string summary)
    {
        var latest = samples.LastOrDefault();
        return new PimaxShellLaunchResult(
            PimaxShellLaunchSchema.Version,
            collectedAt,
            result,
            success,
            shortcutState,
            sanitizedShortcutPath,
            executionContextState,
            preLaunchState,
            blockingProcesses,
            shell?.Accepted == true,
            shell?.ShellRequestCount ?? 0,
            shell?.RetryCount ?? 0,
            (int)request.VerificationTimeout.TotalSeconds,
            samples.Length,
            latest?.SoftwareStackReady == true,
            latest?.HeadsetDetected == true,
            latest?.RegistrationHealthy == true,
            CountConsecutiveHealthySamples(samples),
            warnings.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            summary);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

internal static class PimaxShellLaunchJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

internal static class PimaxShellLaunchCommand
{
    public static async Task<PimaxShellLaunchResult> RunAsync(
        SupervisorConfig config,
        string[] commandLineArgs,
        CancellationToken cancellationToken)
    {
        var runner = new PimaxShellLaunchRunner(
            new SystemPimaxShellLaunchProcessInventory(),
            new PimaxShellShortcutDiscovery(new ComPimaxShellShortcutReader()),
            new DefaultPimaxShellLauncher(),
            new DefaultPimaxShellLaunchVerifier(config),
            commandLineArgs: commandLineArgs);

        return await runner.RunAsync(
            new PimaxShellLaunchRequest(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(2)),
            cancellationToken);
    }
}
