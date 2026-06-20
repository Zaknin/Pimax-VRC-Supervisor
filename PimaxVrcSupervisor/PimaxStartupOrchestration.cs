using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

internal static class PimaxStartupSourcesSchema
{
    public const string Version = "pimax-startup-sources-v1";
}

internal static class PimaxStartupObservationSchema
{
    public const string Version = "pimax-startup-observation-v1";
}

internal static class PimaxStartupMechanism
{
    public const string ShellActivationRequired = "shellActivationRequired";
    public const string ServiceBrokerRequired = "serviceBrokerRequired";
    public const string BootstrapHelperRequired = "bootstrapHelperRequired";
    public const string StateResetRequired = "stateResetRequired";
    public const string MultipleMechanisms = "multipleMechanisms";
    public const string ManualShellLaunchWorksMechanismStillUnresolved = "manualShellLaunchWorksMechanismStillUnresolved";
    public const string ManualShellLaunchAlsoPartial = "manualShellLaunchAlsoPartial";
    public const string ConflictingEvidence = "conflictingEvidence";
    public const string InsufficientEvidence = "insufficientEvidence";
    public const string Unknown = "unknown";
}

internal sealed record PimaxStartupSourcesSnapshot(
    string Schema,
    DateTimeOffset CollectedAt,
    PimaxStartupSource[] Sources,
    PimaxStartupActivationPath[] CandidateActivationPaths,
    PimaxStartupMechanismAssessment MechanismAssessment,
    string[] PrivacyRedactions,
    string[] Warnings,
    string[] Errors,
    string HumanReadableSummary);

internal sealed record PimaxStartupSource(
    string SourceType,
    string SourceId,
    string SanitizedIdentity,
    string Target,
    string Arguments,
    string WorkingDirectory,
    string SignerSummary,
    string ProductName,
    string Version,
    string Confidence,
    string PossibleRole,
    string[] Conflicts,
    string[] Blockers,
    PimaxShellActivationMetadata? ShellMetadata = null);

internal sealed record PimaxShellActivationMetadata(
    string ShortcutPath,
    string Description,
    string IconLocation,
    int WindowStyle,
    string Hotkey,
    bool RunAsAdministratorFlag,
    string AppUserModelId,
    string ToastActivatorClsid,
    string RelaunchCommand,
    string RelaunchDisplayName,
    string RelaunchIconResource,
    string DarwinDescriptor,
    string LinkTargetKind,
    string[] PropertyStoreValues);

internal sealed record PimaxStartupActivationPath(
    string ActivationType,
    string SourceId,
    string Evidence,
    string Confidence,
    bool ProgrammaticEquivalentKnown,
    bool SafeForBackendExecution,
    string[] RequiredValidation);

internal sealed record PimaxStartupMechanismAssessment(
    string Mechanism,
    string Confidence,
    string[] SupportingEvidence,
    string[] Blockers,
    bool BackendExecutable,
    string HumanReadableSummary);

internal sealed record PimaxStartupObservationRequest(
    TimeSpan Duration,
    TimeSpan PollInterval,
    bool Fake,
    string ObservationId)
{
    public static PimaxStartupObservationRequest Parse(string[] args)
    {
        var duration = TimeSpan.FromSeconds(ReadInt(args, "--duration-seconds", 30, 1, 180));
        var poll = TimeSpan.FromMilliseconds(ReadInt(args, "--poll-milliseconds", 250, 100, 2000));
        var fake = args.Any(arg => string.Equals(arg, "--fake", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--non-live", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));
        return new PimaxStartupObservationRequest(duration, poll, fake, $"pimax-startup-observe-{Guid.NewGuid():N}");
    }

    private static int ReadInt(string[] args, string name, int fallback, int min, int max)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var parsed))
            {
                return Math.Clamp(parsed, min, max);
            }
        }

        return fallback;
    }
}

internal sealed record PimaxStartupObservationSnapshot(
    string Schema,
    string ObservationId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationSeconds,
    string EventSource,
    bool Bounded,
    bool Fake,
    PimaxStartupEvent[] Events,
    PimaxStartupProcessCreator[] ProcessCreators,
    string[] RequiredMembersPresent,
    string[] RequiredMembersMissing,
    string[] UnexpectedMembers,
    string[] ServiceStateEvents,
    string[] PrivacyRedactions,
    string[] Warnings,
    string[] Errors,
    string HumanReadableSummary);

internal sealed record PimaxStartupEvent(
    int Sequence,
    DateTimeOffset ObservedAt,
    string EventType,
    string ProcessToken,
    string ParentToken,
    string ProcessName,
    string RelativePath,
    string SignerSummary,
    string Session,
    string GroupRole,
    string Classification);

internal sealed record PimaxStartupProcessCreator(
    string ProcessName,
    string CreatorToken,
    string CreatorEvidence,
    string Confidence);

internal sealed record PimaxStartupComparison(
    string DirectLaunchSource,
    string DirectLaunchResult,
    string ManualLaunchSource,
    string ManualLaunchResult,
    string EarliestDifferingEvent,
    string[] DirectMissingMembers,
    string[] ManualFormedMembers,
    string Mechanism,
    string Confidence,
    bool BackendExecutable,
    string[] Evidence,
    string[] Blockers);

internal static class PimaxStartupSourcesCollector
{
    private const string PimaxRoot = @"C:\Program Files\Pimax";
    private static readonly string[] HelperNames =
    [
        "PimaxClient.exe",
        "DeviceSetting.exe",
        "PiPlayService.exe",
        "pi_server.exe",
        "PiService.exe",
        "PiServiceLauncher.exe"
    ];

    public static PimaxStartupSourcesSnapshot Collect(DateTimeOffset? now = null)
    {
        var sources = new List<PimaxStartupSource>();
        var warnings = new List<string>();
        var errors = new List<string>();

        sources.AddRange(CollectShortcuts(warnings));
        sources.AddRange(CollectInstalledApplications());
        sources.AddRange(CollectAppPaths());
        sources.AddRange(CollectProtocols());
        sources.AddRange(CollectComRegistrations());
        sources.AddRange(CollectRunEntries());
        sources.AddRange(CollectStartupFolders());
        sources.AddRange(CollectServices());
        sources.AddRange(CollectScheduledTasks(warnings));
        sources.AddRange(CollectHelperExecutables(warnings));

        var candidates = BuildActivationPaths(sources).ToArray();
        var assessment = Assess(sources, candidates);
        return new PimaxStartupSourcesSnapshot(
            PimaxStartupSourcesSchema.Version,
            now ?? DateTimeOffset.Now,
            sources
                .DistinctBy(source => $"{source.SourceType}|{source.SanitizedIdentity}|{source.Target}", StringComparer.OrdinalIgnoreCase)
                .OrderBy(source => source.SourceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(source => source.SanitizedIdentity, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            candidates,
            assessment,
            ["raw PIDs", "raw command lines", "user profile paths", "machine name", "certificate serial numbers", "raw PnP IDs", "raw Pimax log contents"],
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            assessment.HumanReadableSummary);
    }

    internal static PimaxStartupMechanismAssessment Assess(IReadOnlyCollection<PimaxStartupSource> sources, IReadOnlyCollection<PimaxStartupActivationPath> paths)
    {
        var shortcut = sources.FirstOrDefault(source => source.SourceType == "startMenuShortcut" && source.Target.Contains("PimaxClient.exe", StringComparison.OrdinalIgnoreCase));
        var service = sources.Any(source => source.SourceType == "service" && source.SanitizedIdentity.Contains("PiServiceLauncher", StringComparison.OrdinalIgnoreCase));
        var bootstrap = sources.Any(source => source.SourceType == "helperExecutable" && !source.Target.Contains(@"PimaxClient\pimaxui\PimaxClient.exe", StringComparison.OrdinalIgnoreCase));
        var evidence = new List<string>();
        if (shortcut is not null) evidence.Add("Start Menu PimaxPlay shortcut targets the visible PimaxClient launcher.");
        if (service) evidence.Add("Pimax service ownership is present and must be correlated during the formal observer run.");
        if (bootstrap) evidence.Add("Signed or installation-root Pimax helper executables exist and may participate in startup.");
        var blockers = new List<string>
        {
            "The formal observer-backed Start Menu launch has not yet identified the creator chain.",
            "A safe programmatic shell-equivalent has not been validated."
        };
        var mechanism = shortcut is not null
            ? PimaxStartupMechanism.ManualShellLaunchWorksMechanismStillUnresolved
            : PimaxStartupMechanism.InsufficientEvidence;
        return new PimaxStartupMechanismAssessment(
            mechanism,
            shortcut is null ? "insufficient" : "probable",
            evidence.ToArray(),
            blockers.ToArray(),
            false,
            shortcut is null
                ? "Pimax startup activation sources are insufficiently identified."
                : "The Start Menu activation source is visible, but backend execution remains disabled until observer evidence proves a safe creator chain and programmatic equivalent.");
    }

    private static IEnumerable<PimaxStartupActivationPath> BuildActivationPaths(IEnumerable<PimaxStartupSource> sources)
    {
        foreach (var source in sources)
        {
            if (source.SourceType is "startMenuShortcut" or "appPath" or "protocolHandler" or "comLocalServer" or "service" or "scheduledTask")
            {
                yield return new PimaxStartupActivationPath(
                    source.SourceType,
                    source.SourceId,
                    source.PossibleRole,
                    source.Confidence,
                    false,
                    false,
                    ["formal observer-backed Start Menu launch", "creator-chain proof", "separate one-shot programmatic validation"]);
            }
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectShortcuts(List<string> warnings)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(path).Contains("Pimax", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch (Exception ex)
            {
                warnings.Add($"Shortcut enumeration skipped for a Start Menu root: {ex.GetType().Name}");
                continue;
            }

            foreach (var file in files)
            {
                var shortcut = TryReadShortcut(file);
                if (shortcut is null) continue;
                yield return FromPath(
                    "startMenuShortcut",
                    file,
                    shortcut.TargetPath,
                    shortcut.Arguments,
                    shortcut.WorkingDirectory,
                    "visible user activation entry",
                    shortcut.Metadata);
            }
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectInstalledApplications()
    {
        foreach (var root in RegistryRoots(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", includeUsers: true)
                     .Concat(RegistryRoots(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", includeUsers: false)))
        {
            using var key = root.Key;
            if (key is null) continue;
            foreach (var name in SafeSubKeyNames(key))
            {
                using var child = key.OpenSubKey(name);
                var displayName = SafeString(child?.GetValue("DisplayName"));
                var publisher = SafeString(child?.GetValue("Publisher"));
                if (!IsPimaxText(displayName) && !IsPimaxText(publisher)) continue;
                yield return Source(
                    "installedApplication",
                    root.Hive + "\\" + name,
                    displayName,
                    SafeString(child?.GetValue("DisplayIcon")),
                    "",
                    SafeString(child?.GetValue("InstallLocation")),
                    publisher,
                    displayName,
                    SafeString(child?.GetValue("DisplayVersion")),
                    "probable",
                    "installed application metadata");
            }
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectAppPaths()
    {
        foreach (var root in RegistryRoots(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", includeUsers: true))
        {
            using var key = root.Key;
            if (key is null) continue;
            foreach (var name in SafeSubKeyNames(key).Where(IsPimaxText))
            {
                using var child = key.OpenSubKey(name);
                yield return Source(
                    "appPath",
                    root.Hive + "\\" + name,
                    name,
                    SafeString(child?.GetValue("")),
                    "",
                    SafeString(child?.GetValue("Path")),
                    "",
                    name,
                    "",
                    "possible",
                    "registered executable search path");
            }
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectProtocols()
    {
        foreach (var root in RegistryRoots(@"SOFTWARE\Classes", includeUsers: true))
        {
            using var key = root.Key;
            if (key is null) continue;
            foreach (var name in SafeSubKeyNames(key).Where(IsPimaxText))
            {
                using var child = key.OpenSubKey(name);
                using var command = key.OpenSubKey(name + @"\shell\open\command");
                using var delegateExecute = key.OpenSubKey(name + @"\shell\open");
                if (child?.GetValue("URL Protocol") is null && command is null && delegateExecute is null) continue;
                yield return Source(
                    "protocolHandler",
                    root.Hive + "\\" + name,
                    name,
                    SafeString(command?.GetValue("")),
                    "",
                    "",
                    "",
                    name,
                    "",
                    "possible",
                    delegateExecute?.GetValue("DelegateExecute") is null ? "protocol shell command" : "protocol DelegateExecute handler");
            }
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectComRegistrations()
    {
        foreach (var root in RegistryRoots(@"SOFTWARE\Classes\CLSID", includeUsers: true))
        {
            using var key = root.Key;
            if (key is null) continue;
            foreach (var name in SafeSubKeyNames(key))
            {
                using var child = key.OpenSubKey(name);
                var display = SafeString(child?.GetValue(""));
                using var localServer = child?.OpenSubKey("LocalServer32");
                using var inproc = child?.OpenSubKey("InprocServer32");
                var target = SafeString(localServer?.GetValue(""));
                var inprocTarget = SafeString(inproc?.GetValue(""));
                if (!IsPimaxText(display) && !IsPimaxText(target) && !IsPimaxText(inprocTarget)) continue;
                yield return Source(
                    localServer is null ? "comInprocServer" : "comLocalServer",
                    root.Hive + "\\" + name,
                    display.Length == 0 ? name : display,
                    target.Length == 0 ? inprocTarget : target,
                    "",
                    "",
                    "",
                    display,
                    "",
                    localServer is null ? "possible" : "probable",
                    localServer is null ? "COM in-process activation metadata" : "COM local-server activation metadata");
            }
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectRunEntries()
    {
        foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" })
        {
            foreach (var root in RegistryRoots(path, includeUsers: true))
            {
                using var key = root.Key;
                if (key is null) continue;
                foreach (var name in key.GetValueNames().Where(name => IsPimaxText(name) || IsPimaxText(SafeString(key.GetValue(name)))))
                {
                    yield return Source("runEntry", root.Hive + "\\" + name, name, SafeString(key.GetValue(name)), "", "", "", name, "", "possible", "user or machine startup registration");
                }
            }
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectStartupFolders()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) }.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root).Where(path => IsPimaxText(Path.GetFileName(path))))
            {
                yield return Source("startupFolder", SanitizePath(file), Path.GetFileName(file), SanitizePath(file), "", SanitizePath(Path.GetDirectoryName(file)), "", Path.GetFileName(file), "", "possible", "Startup folder registration");
            }
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectServices()
    {
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null) yield break;
        foreach (var name in SafeSubKeyNames(services))
        {
            using var service = services.OpenSubKey(name);
            var display = SafeString(service?.GetValue("DisplayName"));
            var imagePath = SafeString(service?.GetValue("ImagePath"));
            if (!IsPimaxText(name) && !IsPimaxText(display) && !IsPimaxText(imagePath)) continue;
            var triggers = service?.OpenSubKey("TriggerInfo") is null ? "no trigger metadata observed" : "service trigger metadata present";
            yield return Source("service", "service:" + name, display.Length == 0 ? name : display, imagePath, "", "", "", display, "", "probable", triggers);
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectScheduledTasks(List<string> warnings)
    {
        var taskRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
        if (!Directory.Exists(taskRoot)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(taskRoot, "*", SearchOption.AllDirectories)
                .Where(path => IsPimaxText(path) || IsPimaxText(SafeReadAllText(path)))
                .ToArray();
        }
        catch (Exception ex)
        {
            warnings.Add($"Scheduled task file enumeration skipped: {ex.GetType().Name}");
            yield break;
        }

        foreach (var file in files)
        {
            var text = SafeReadAllText(file);
            yield return Source("scheduledTask", SanitizePath(file), Path.GetFileName(file), ExtractXmlTag(text, "Command"), ExtractXmlTag(text, "Arguments"), "", "", Path.GetFileName(file), "", "possible", "scheduled task metadata");
        }
    }

    private static IEnumerable<PimaxStartupSource> CollectHelperExecutables(List<string> warnings)
    {
        if (!Directory.Exists(PimaxRoot)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(PimaxRoot, "*.exe", SearchOption.AllDirectories)
                .Where(path => HelperNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    || IsPimaxText(Path.GetFileName(path)))
                .Take(200)
                .ToArray();
        }
        catch (Exception ex)
        {
            warnings.Add($"Pimax helper inventory skipped: {ex.GetType().Name}");
            yield break;
        }

        foreach (var file in files)
        {
            yield return FromPath("helperExecutable", file, file, "", Path.GetDirectoryName(file) ?? "", "installation helper or runtime member", null);
        }
    }

    private static PimaxStartupSource FromPath(string type, string identity, string target, string arguments, string workingDirectory, string role, PimaxShellActivationMetadata? metadata)
    {
        var info = File.Exists(target) ? FileVersionInfo.GetVersionInfo(target) : null;
        var signer = File.Exists(target) ? SignerSummary(target, info) : "not a filesystem executable or file missing";
        return Source(
            type,
            StableId(type, identity),
            SanitizePath(identity),
            SanitizePath(target),
            SanitizeArguments(arguments),
            SanitizePath(workingDirectory),
            signer,
            info?.ProductName ?? "",
            info?.FileVersion ?? "",
            File.Exists(target) ? "probable" : "possible",
            role,
            shellMetadata: metadata);
    }

    private static PimaxStartupSource Source(
        string type,
        string id,
        string identity,
        string target,
        string arguments,
        string workingDirectory,
        string signer,
        string product,
        string version,
        string confidence,
        string role,
        PimaxShellActivationMetadata? shellMetadata = null)
    {
        var blockers = new List<string>();
        if (type is "startMenuShortcut" && !target.Contains("PimaxClient.exe", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("shortcut target is not the expected visible PimaxClient launcher");
        }

        return new PimaxStartupSource(
            type,
            StableId(type, id),
            SanitizePath(identity),
            SanitizePath(target),
            SanitizeArguments(arguments),
            SanitizePath(workingDirectory),
            signer,
            product,
            version,
            confidence,
            role,
            [],
            blockers.ToArray(),
            shellMetadata);
    }

    private static ShortcutRead? TryReadShortcut(string path)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            var shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [path]);
            if (shortcut is null) return null;
            var type = shortcut.GetType();
            var target = SafeString(type.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
            var args = SafeString(type.InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
            var working = SafeString(type.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
            var description = SafeString(type.InvokeMember("Description", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
            var icon = SafeString(type.InvokeMember("IconLocation", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
            var windowStyle = int.TryParse(SafeString(type.InvokeMember("WindowStyle", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)), out var parsed) ? parsed : 0;
            var hotkey = SafeString(type.InvokeMember("Hotkey", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
            var metadata = new PimaxShellActivationMetadata(
                SanitizePath(path),
                description,
                SanitizePath(icon),
                windowStyle,
                hotkey,
                false,
                "",
                "",
                "",
                "",
                "",
                "",
                target.Length == 0 ? "shellNamespaceOrAdvertisedTarget" : "filesystemTarget",
                []);
            return new ShortcutRead(target, args, working, metadata);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<(string Hive, RegistryKey? Key)> RegistryRoots(string subKey, bool includeUsers)
    {
        yield return ("HKLM:" + subKey, Registry.LocalMachine.OpenSubKey(subKey));
        yield return ("HKCU:" + subKey, Registry.CurrentUser.OpenSubKey(subKey));
        if (includeUsers)
        {
            yield return ("HKCR:" + subKey, Registry.ClassesRoot.OpenSubKey(subKey.Replace(@"SOFTWARE\Classes\", "", StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static string[] SafeSubKeyNames(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); }
        catch { return []; }
    }

    private static string SignerSummary(string path, FileVersionInfo? info)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return certificate.Subject.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
                ? "Pimax certificate subject present"
                : "publisher certificate present";
        }
        catch
        {
            return IsPimaxText(info?.CompanyName) ? "signature inspection unavailable; product metadata references Pimax" : "signature inspection unavailable";
        }
    }

    internal static string SanitizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        const string pimaxRoot = @"C:\Program Files\Pimax";
        var sanitized = value.Replace(Environment.MachineName, "<machine>", StringComparison.OrdinalIgnoreCase)
            .Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
        if (sanitized.StartsWith(pimaxRoot, StringComparison.OrdinalIgnoreCase))
        {
            return "<pimax>" + sanitized[pimaxRoot.Length..];
        }

        if (Path.IsPathFullyQualified(sanitized))
        {
            var fileName = Path.GetFileName(sanitized);
            return string.IsNullOrWhiteSpace(fileName) ? "<local-path>" : "<local-path>\\" + fileName;
        }

        return PimaxConnectivityRedactor.SanitizePath(sanitized) ?? "";
    }

    private static string SanitizeArguments(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return SanitizePath(value).Contains("<user>", StringComparison.OrdinalIgnoreCase) ? "<redacted-arguments>" : SanitizePath(value);
    }

    private static string StableId(string type, string identity)
        => type + ":" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((type + "|" + identity).ToLowerInvariant())))[..16].ToLowerInvariant();

    private static string SafeReadAllText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return ""; }
    }

    private static string ExtractXmlTag(string text, string name)
    {
        var open = "<" + name + ">";
        var close = "</" + name + ">";
        var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        var end = text.IndexOf(close, StringComparison.OrdinalIgnoreCase);
        return start < 0 || end <= start ? "" : SanitizePath(text[(start + open.Length)..end]);
    }

    private static bool IsPimaxText(string? value)
        => value?.Contains("Pimax", StringComparison.OrdinalIgnoreCase) == true
            || value?.Contains("PiPlay", StringComparison.OrdinalIgnoreCase) == true
            || value?.Contains("PiService", StringComparison.OrdinalIgnoreCase) == true;

    private static string SafeString(object? value) => value?.ToString() ?? "";

    private sealed record ShortcutRead(string TargetPath, string Arguments, string WorkingDirectory, PimaxShellActivationMetadata Metadata);
}

internal sealed class PimaxStartupObserver
{
    private static readonly string[] RequiredMembers = ["PimaxClient", "DeviceSetting", "PiPlayService", "pi_server", "PiServiceLauncher", "Tobii VR4PIMAXP3B Platform Runtime"];
    private static readonly string[] ExpectedNames = ["PimaxClient", "DeviceSetting", "PiPlayService", "pi_server", "PiService", "PiServiceLauncher", "PiPlatformService_64", "PVRHome", "pi_overlay", "UnityCrashHandler64"];

    public async Task<PimaxStartupObservationSnapshot> ObserveAsync(PimaxStartupObservationRequest request, CancellationToken cancellationToken)
    {
        if (request.Fake)
        {
            var at = DateTimeOffset.Now;
            return BuildSnapshot(request, at, at, FakeEvents(at), [], ["fake non-live validation mode"]);
        }

        var started = DateTimeOffset.Now;
        var events = new List<PimaxStartupEvent>();
        var known = CaptureProcesses();
        var tokens = new TokenMap();
        var sequence = 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(request.Duration);

        while (!linked.IsCancellationRequested)
        {
            await Task.Delay(request.PollInterval, linked.Token).ContinueWith(_ => { }, CancellationToken.None);
            var current = CaptureProcesses();
            foreach (var added in current.Keys.Except(known.Keys).OrderBy(id => current[id].Name, StringComparer.OrdinalIgnoreCase))
            {
                var process = current[added];
                events.Add(ToEvent(++sequence, "processStart", process, tokens.TokenFor(added), "unknown"));
            }

            foreach (var removed in known.Keys.Except(current.Keys).OrderBy(id => known[id].Name, StringComparer.OrdinalIgnoreCase))
            {
                var process = known[removed];
                events.Add(ToEvent(++sequence, "processStop", process, tokens.TokenFor(removed), "unknown"));
            }

            known = current;
        }

        var ended = DateTimeOffset.Now;
        return BuildSnapshot(request, started, ended, events, [], []);
    }

    internal static PimaxStartupComparison Compare(PimaxStartupObservationSnapshot observation)
    {
        var formed = observation.RequiredMembersPresent;
        var missing = observation.RequiredMembersMissing;
        var mechanism = missing.Length == 0
            ? PimaxStartupMechanism.ManualShellLaunchWorksMechanismStillUnresolved
            : PimaxStartupMechanism.ManualShellLaunchAlsoPartial;
        var earliest = observation.Events.FirstOrDefault()?.ProcessName ?? "no startup event captured";
        return new PimaxStartupComparison(
            "directProcessCreation",
            "groupPartial; PimaxClient launched but DeviceSetting, PiPlayService, and pi_server did not return",
            "manualStartMenuActivation",
            missing.Length == 0 ? "groupCompleteOrReady" : "groupPartial",
            earliest,
            ["DeviceSetting", "PiPlayService", "pi_server"],
            formed,
            mechanism,
            observation.Events.Length == 0 ? "insufficient" : "probable",
            false,
            observation.Events.Select(e => $"{e.EventType}:{e.ProcessName}:{e.GroupRole}").ToArray(),
            ["safe programmatic equivalent remains unvalidated"]);
    }

    private static PimaxStartupObservationSnapshot BuildSnapshot(
        PimaxStartupObservationRequest request,
        DateTimeOffset started,
        DateTimeOffset ended,
        IReadOnlyCollection<PimaxStartupEvent> events,
        IReadOnlyCollection<string> errors,
        IReadOnlyCollection<string> warnings)
    {
        var present = RequiredMembers
            .Where(required => events.Any(e => string.Equals(e.ProcessName, required, StringComparison.OrdinalIgnoreCase) && e.EventType == "processStart")
                || events.Any(e => RequiredAliasMatches(required, e.ProcessName, e.RelativePath) && e.EventType == "processStart")
                || Process.GetProcessesByName(required).Length > 0)
            .ToArray();
        var missing = RequiredMembers.Except(present, StringComparer.OrdinalIgnoreCase).ToArray();
        return new PimaxStartupObservationSnapshot(
            PimaxStartupObservationSchema.Version,
            request.ObservationId,
            started,
            ended,
            Math.Round((ended - started).TotalSeconds, 3),
            request.Fake ? "fake-process-lifecycle" : "bounded-process-snapshot-diff",
            true,
            request.Fake,
            events.OrderBy(e => e.Sequence).ToArray(),
            events.Select(e => new PimaxStartupProcessCreator(e.ProcessName, e.ParentToken, e.ParentToken == "unknown" ? "parent unavailable from safe process snapshot source" : "parent token observed", e.ParentToken == "unknown" ? "unknown" : "possible"))
                .DistinctBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            present,
            missing,
            events.Where(e => e.Classification == "unexpected").Select(e => e.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            [],
            ["raw PIDs", "raw command lines", "environment blocks", "window contents", "handles", "user name", "machine name"],
            warnings.ToArray(),
            errors.ToArray(),
            missing.Length == 0 ? "Observed Pimax process lifecycle reached the required member set." : "Bounded process lifecycle observation completed without proving all required members started during the window.");
    }

    private static PimaxStartupEvent[] FakeEvents(DateTimeOffset at)
        =>
        [
            new(1, at, "processStart", "proc-0001", "unknown", "PimaxClient", @"<pimax>\PimaxClient\pimaxui\PimaxClient.exe", "Pimax publisher metadata present.", "interactive", PimaxSoftwareGroupRole.PimaxPlayUiProcess, "expected"),
            new(2, at.AddMilliseconds(120), "processStart", "proc-0002", "unknown", "DeviceSetting", @"<pimax>\Runtime\DeviceSetting.exe", "Pimax installation-root executable.", "interactive", PimaxSoftwareGroupRole.RuntimeProcess, "expected"),
            new(3, at.AddMilliseconds(240), "processStart", "proc-0003", "unknown", "PiPlayService", @"<pimax>\Runtime\PiPlayService.exe", "Pimax installation-root executable.", "interactive", PimaxSoftwareGroupRole.RuntimeProcess, "expected"),
            new(4, at.AddMilliseconds(360), "processStart", "proc-0004", "unknown", "pi_server", @"<pimax>\Runtime\pi_server.exe", "Pimax installation-root executable.", "interactive", PimaxSoftwareGroupRole.RuntimeProcess, "expected"),
            new(5, at.AddMilliseconds(480), "processStart", "proc-0005", "unknown", "PiServiceLauncher", @"<pimax>\Runtime\PiServiceLauncher.exe", "Pimax installation-root executable.", "service", PimaxSoftwareGroupRole.ServiceOwnedProcess, "expected"),
            new(6, at.AddMilliseconds(600), "processStart", "proc-0006", "unknown", "Tobii VR4PIMAXP3B Platform Runtime", @"<pimax>\Runtime\EyeTrackingServer\platform_runtime\platform_runtime_VR4PIMAXP3B_service.exe", "Pimax installation-root executable.", "service", PimaxSoftwareGroupRole.ServiceOwnedProcess, "expected")
        ];

    private static Dictionary<int, ObservedProcess> CaptureProcesses()
    {
        var result = new Dictionary<int, ObservedProcess>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                var path = SafeProcessPath(process);
                if (!IsRelevant(name, path)) continue;
                result[process.Id] = new ObservedProcess(name, path, SafeStartTime(process), process.SessionId);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static PimaxStartupEvent ToEvent(int sequence, string eventType, ObservedProcess process, string token, string parentToken)
    {
        var role = RoleForName(process.Name);
        var expected = ExpectedNames.Contains(process.Name, StringComparer.OrdinalIgnoreCase)
            || process.Path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase);
        return new PimaxStartupEvent(
            sequence,
            DateTimeOffset.Now,
            eventType,
            token,
            parentToken,
            process.Name,
            PimaxStartupSourcesCollector.SanitizePath(process.Path),
            process.Path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) ? "Pimax installation-root executable." : "path unavailable or outside Pimax root",
            process.SessionId < 0 ? "unknown" : "session-" + process.SessionId,
            role,
            expected ? "expected" : "unexpected");
    }

    private static string RoleForName(string name)
        => name.Equals("PimaxClient", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.PimaxPlayUiProcess :
            name.Equals("DeviceSetting", StringComparison.OrdinalIgnoreCase) || name.Equals("PiPlayService", StringComparison.OrdinalIgnoreCase) || name.Equals("pi_server", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.RuntimeProcess :
            name.Equals("PiService", StringComparison.OrdinalIgnoreCase) || name.Equals("PiServiceLauncher", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.ServiceOwnedProcess :
            PimaxSoftwareGroupRole.OptionalComponent;

    private static bool IsRelevant(string name, string path)
        => ExpectedNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || name.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VR4PIMAX", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase);

    private static bool RequiredAliasMatches(string required, string processName, string relativePath)
        => required.Equals("Tobii VR4PIMAXP3B Platform Runtime", StringComparison.OrdinalIgnoreCase)
            && (processName.Contains("VR4PIMAX", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("VR4PIMAX", StringComparison.OrdinalIgnoreCase));

    private static string SafeProcessPath(Process process)
    {
        try { return process.MainModule?.FileName ?? ""; }
        catch { return ""; }
    }

    private static DateTimeOffset? SafeStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return null; }
    }

    private sealed record ObservedProcess(string Name, string Path, DateTimeOffset? StartTime, int SessionId);

    private sealed class TokenMap
    {
        private readonly Dictionary<int, string> _tokens = [];

        public string TokenFor(int processId)
        {
            if (_tokens.TryGetValue(processId, out var token)) return token;
            token = "proc-" + (_tokens.Count + 1).ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
            _tokens[processId] = token;
            return token;
        }
    }
}
