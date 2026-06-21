using System.Diagnostics;
using System.Management;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Win32;

internal static class PimaxStartupSourcesSchema
{
    public const string Version = "pimax-startup-sources-v1";
}

internal static class PimaxStartupObservationSchema
{
    public const string Version = "pimax-startup-observation-v1";
}

internal static class PimaxElevatedStartupObservationSchema
{
    public const string Version = "pimax-startup-observation-elevated-v1";
}

internal static class PimaxStartupCreatorChainSchema
{
    public const string Version = "pimax-startup-creator-chain-v1";
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

internal sealed record PimaxElevatedStartupObservationRequest(
    TimeSpan Duration,
    TimeSpan PollInterval,
    bool Fake,
    bool PreflightOnly,
    string ObservationId)
{
    public static PimaxElevatedStartupObservationRequest Parse(string[] args)
    {
        var baseRequest = PimaxStartupObservationRequest.Parse(args);
        var preflightOnly = args.Any(arg => string.Equals(arg, "--preflight-only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--capability-only", StringComparison.OrdinalIgnoreCase));
        return new PimaxElevatedStartupObservationRequest(
            baseRequest.Duration,
            baseRequest.PollInterval,
            baseRequest.Fake,
            preflightOnly,
            baseRequest.ObservationId.Replace("pimax-startup-observe-", "pimax-startup-observe-elevated-", StringComparison.OrdinalIgnoreCase));
    }

    public PimaxStartupObservationRequest ToStartupRequest(bool fake)
        => new(Duration, PollInterval, fake, ObservationId);
}

internal sealed record PimaxElevatedStartupObservationSnapshot(
    string Schema,
    string ObservationId,
    DateTimeOffset CollectedAt,
    bool Accepted,
    bool ElevatedMode,
    bool IsElevated,
    bool PreflightOnly,
    bool Bounded,
    double DurationSeconds,
    string Provider,
    string EventSource,
    string RequiredPrivilege,
    bool ParentIdentityCapturedAtProcessStart,
    bool ProcessStopCaptureRequired,
    bool WmiSnapshotFallbackAllowed,
    bool SelfElevationAllowed,
    bool PersistentElevationAllowed,
    string[] CapturedFields,
    string[] PrivacyRedactions,
    string[] Warnings,
    string[] Errors,
    string HumanReadableSummary,
    PimaxStartupObservationSnapshot? Observation);

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
    string HumanReadableSummary,
    PimaxProcessStartIdentity[] Processes,
    PimaxCreatorChainAssessment? CreatorChain);

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

internal sealed record PimaxProcessStartIdentity(
    string Token,
    string ParentToken,
    string ProcessName,
    string RelativePath,
    string SignerSummary,
    string ProductState,
    string Session,
    string Role,
    DateTimeOffset? StartTimestamp,
    DateTimeOffset? StopTimestamp,
    bool PresentInBaseline,
    bool ExitedDuringObservation,
    string CreatorConfidence);

internal sealed record PimaxCreatorChainNode(
    string Token,
    string ProcessName,
    string Role,
    bool PresentInBaseline,
    bool ExitedDuringObservation,
    string RelativePath);

internal sealed record PimaxCreatorChainEdge(
    string CreatorToken,
    string ChildToken,
    string ChildProcessName,
    DateTimeOffset? EventTimestamp,
    string EvidenceSource,
    string Confidence,
    bool CreatorExistedInBaseline,
    bool CreatorExitedLater,
    bool Direct);

internal sealed record PimaxActivationRootCandidate(
    string RootCategory,
    string Token,
    string ProcessName,
    string Evidence,
    string Confidence);

internal sealed record PimaxCreatorChainAssessment(
    string Schema,
    string ObservationId,
    string DeviceSettingRootResult,
    string Confidence,
    string DeviceSettingCreator,
    string PiPlayServiceCreator,
    string PiServiceCreator,
    string PiServerCreator,
    PimaxCreatorChainNode[] Nodes,
    PimaxCreatorChainEdge[] Edges,
    PimaxActivationRootCandidate[] RootCandidates,
    string[] UnresolvedGaps,
    bool BackendExecutable,
    string HumanReadableSummary);

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

internal sealed record PimaxStartupCreatorChainRequest(
    string? InputPath,
    bool Fake)
{
    public static PimaxStartupCreatorChainRequest Parse(string[] args)
    {
        string? input = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--input", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[i], "--fixture", StringComparison.OrdinalIgnoreCase))
            {
                input = args[i + 1];
            }
        }

        var fake = args.Any(arg => string.Equals(arg, "--fake", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--non-live", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));
        return new PimaxStartupCreatorChainRequest(input, fake);
    }
}

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
    internal static readonly string[] RequiredMembers = ["PimaxClient", "DeviceSetting", "PiPlayService", "pi_server", "PiServiceLauncher", "Tobii VR4PIMAXP3B Platform Runtime"];
    internal static readonly string[] ExpectedNames = ["PimaxClient", "DeviceSetting", "PiPlayService", "pi_server", "PiService", "PiServiceLauncher", "PiPlatformService_64", "PVRHome", "pi_overlay", "UnityCrashHandler64", "launcher", "fastlist-0.3.0-x64", "lighthouse_console", "platform_runtime_VR4PIMAXP3B_service"];
    private static readonly string[] BrokerNames = ["explorer", "ShellExperienceHost", "StartMenuExperienceHost", "RuntimeBroker", "ApplicationFrameHost", "services", "svchost", "taskhostw", "dllhost"];
    private const string ElevatedProvider = "Microsoft-Windows-Kernel-Process";
    private const string ElevatedEventSource = "elevated-process-start-stop-trace";

    public async Task<PimaxStartupObservationSnapshot> ObserveAsync(PimaxStartupObservationRequest request, CancellationToken cancellationToken)
        => await ObserveProcessTraceAsync(request, cancellationToken, allowSnapshotFallback: true, eventSourceOnSuccess: "wmi-process-start-stop-trace");

    public async Task<PimaxElevatedStartupObservationSnapshot> ObserveElevatedAsync(PimaxElevatedStartupObservationRequest request, CancellationToken cancellationToken)
    {
        var isElevated = IsCurrentProcessElevated();
        if (!isElevated && !request.Fake)
        {
            return BuildElevatedSnapshot(
                request,
                isElevated,
                accepted: false,
                warnings: [],
                errors: ["Administrative elevation is required to enable the bounded process-creator trace. The command refuses to self-elevate or install a persistent elevated component."],
                summary: "Elevated startup observation refused because the current process is not running as administrator.",
                observation: null);
        }

        if (request.PreflightOnly)
        {
            return BuildElevatedSnapshot(
                request,
                isElevated || request.Fake,
                accepted: true,
                warnings: request.Fake ? ["fake non-live elevated capability preflight"] : [],
                errors: [],
                summary: "Elevated startup observation preflight passed. The formal observer must still be started before the one allowed Start Menu launch.",
                observation: null);
        }

        try
        {
            var observation = request.Fake
                ? await ObserveProcessTraceAsync(request.ToStartupRequest(fake: true), cancellationToken, allowSnapshotFallback: false, eventSourceOnSuccess: ElevatedEventSource)
                : await ObserveProcessTraceAsync(request.ToStartupRequest(fake: false), cancellationToken, allowSnapshotFallback: false, eventSourceOnSuccess: ElevatedEventSource);
            return BuildElevatedSnapshot(
                request,
                isElevated || request.Fake,
                accepted: true,
                warnings: request.Fake ? ["fake non-live elevated observation"] : [],
                errors: [],
                summary: "Elevated bounded process-creator observation completed without WMI snapshot fallback.",
                observation: observation);
        }
        catch (ManagementException ex)
        {
            return BuildElevatedSnapshot(
                request,
                isElevated || request.Fake,
                accepted: false,
                warnings: [],
                errors: [$"Process trace subscription failed: {ex.ErrorCode}."],
                summary: "Elevated startup observation stopped before formal launch because process trace subscription was unavailable.",
                observation: null);
        }
        catch (Exception ex)
        {
            return BuildElevatedSnapshot(
                request,
                isElevated || request.Fake,
                accepted: false,
                warnings: [],
                errors: [$"Process trace setup failed: {ex.GetType().Name}."],
                summary: "Elevated startup observation stopped before formal launch because the bounded creator observer could not start.",
                observation: null);
        }
    }

    internal static PimaxElevatedStartupObservationSnapshot BuildElevatedSnapshot(
        PimaxElevatedStartupObservationRequest request,
        bool isElevated,
        bool accepted,
        IReadOnlyCollection<string> warnings,
        IReadOnlyCollection<string> errors,
        string summary,
        PimaxStartupObservationSnapshot? observation)
        => new(
            PimaxElevatedStartupObservationSchema.Version,
            request.ObservationId,
            DateTimeOffset.Now,
            accepted,
            ElevatedMode: true,
            isElevated,
            request.PreflightOnly,
            Bounded: true,
            Math.Round(request.Duration.TotalSeconds, 3),
            ElevatedProvider,
            observation?.EventSource ?? ElevatedEventSource,
            "Administrator token required for formal process-creator trace subscription.",
            ParentIdentityCapturedAtProcessStart: accepted,
            ProcessStopCaptureRequired: true,
            WmiSnapshotFallbackAllowed: false,
            SelfElevationAllowed: false,
            PersistentElevationAllowed: false,
            ["process token", "parent process token", "process start timestamp", "process stop timestamp", "image name", "sanitized image path", "session", "event source"],
            ["raw PIDs", "raw parent PIDs", "raw command lines", "environment blocks", "handles", "user SID", "user name", "machine name", "certificate serial numbers", "raw event payloads"],
            warnings.ToArray(),
            errors.ToArray(),
            summary,
            observation);

    private static async Task<PimaxStartupObservationSnapshot> ObserveProcessTraceAsync(
        PimaxStartupObservationRequest request,
        CancellationToken cancellationToken,
        bool allowSnapshotFallback,
        string eventSourceOnSuccess)
    {
        if (request.Fake)
        {
            var at = DateTimeOffset.Now;
            return BuildSnapshot(request, at, at, FakeIdentities(at), [], ["fake non-live validation mode"], eventSourceOnSuccess == ElevatedEventSource ? "fake-elevated-process-lifecycle" : "fake-process-lifecycle");
        }

        var started = DateTimeOffset.Now;
        var lockObject = new object();
        var tokenMap = new TokenMap();
        var identities = CaptureBaseline(tokenMap);
        var sequence = 0;
        var errors = new List<string>();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(request.Duration);

        using var startWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
        using var stopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
        startWatcher.EventArrived += (_, args) =>
        {
            try
            {
                var identity = IdentityFromStartEvent(args.NewEvent, tokenMap);
                if (!IsRelevant(identity.ProcessName, identity.RelativePath)) return;
                lock (lockObject)
                {
                    identities.Add(identity);
                    sequence++;
                }
            }
            catch (Exception ex)
            {
                lock (lockObject) errors.Add("Process start event skipped: " + ex.GetType().Name);
            }
        };
        stopWatcher.EventArrived += (_, args) =>
        {
            try
            {
                var pid = SafeUInt(args.NewEvent["ProcessID"]);
                var time = TimeCreated(args.NewEvent["TIME_CREATED"]) ?? DateTimeOffset.Now;
                lock (lockObject)
                {
                    foreach (var identity in identities.Where(item => tokenMap.MatchesPid(item.Token, pid) && item.StopTimestamp is null).ToArray())
                    {
                        var index = identities.IndexOf(identity);
                        identities[index] = identity with { StopTimestamp = time, ExitedDuringObservation = true };
                    }

                    sequence++;
                }
            }
            catch (Exception ex)
            {
                lock (lockObject) errors.Add("Process stop event skipped: " + ex.GetType().Name);
            }
        };

        try
        {
            startWatcher.Start();
            stopWatcher.Start();
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            if (!allowSnapshotFallback)
            {
                throw;
            }

            return await ObserveByPollingAsync(request, started, tokenMap, identities, ["Process trace subscription denied; using bounded WMI process snapshot fallback."], cancellationToken);
        }

        try
        {
            while (!linked.IsCancellationRequested)
            {
                await Task.Delay(request.PollInterval, linked.Token).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
        finally
        {
            TryStop(startWatcher);
            TryStop(stopWatcher);
        }

        var ended = DateTimeOffset.Now;
        return BuildSnapshot(request, started, ended, identities, errors, [], eventSourceOnSuccess);
    }

    private static async Task<PimaxStartupObservationSnapshot> ObserveByPollingAsync(
        PimaxStartupObservationRequest request,
        DateTimeOffset started,
        TokenMap tokenMap,
        List<PimaxProcessStartIdentity> identities,
        IReadOnlyCollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(request.Duration);
        while (!linked.IsCancellationRequested)
        {
            try
            {
                var observedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var identity in CaptureCurrentProcesses(tokenMap))
                {
                    observedTokens.Add(identity.Token);
                    var existing = identities.FindIndex(item => item.Token == identity.Token);
                    if (existing < 0)
                    {
                        identities.Add(identity);
                    }
                    else if (identities[existing].StopTimestamp is not null)
                    {
                        identities[existing] = identities[existing] with
                        {
                            StopTimestamp = null,
                            ExitedDuringObservation = false
                        };
                    }
                }

                var now = DateTimeOffset.Now;
                for (var i = 0; i < identities.Count; i++)
                {
                    var identity = identities[i];
                    if (identity.PresentInBaseline || identity.StopTimestamp is not null || !IsChainRelevantProcess(identity)) continue;
                    if (!observedTokens.Contains(identity.Token))
                    {
                        identities[i] = identity with { StopTimestamp = now, ExitedDuringObservation = true };
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add("Process snapshot sample skipped: " + ex.GetType().Name);
            }

            await Task.Delay(request.PollInterval, linked.Token).ContinueWith(_ => { }, CancellationToken.None);
        }

        return BuildSnapshot(request, started, DateTimeOffset.Now, identities, errors, warnings, "wmi-process-snapshot-fallback");
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
            observation.Events.Select(e => $"{e.EventType}:{e.ProcessName}:{e.GroupRole}:{e.ParentToken}").ToArray(),
            ["safe programmatic equivalent remains unvalidated"]);
    }

    private static PimaxStartupObservationSnapshot BuildSnapshot(
        PimaxStartupObservationRequest request,
        DateTimeOffset started,
        DateTimeOffset ended,
        IReadOnlyCollection<PimaxProcessStartIdentity> identities,
        IReadOnlyCollection<string> errors,
        IReadOnlyCollection<string> warnings,
        string eventSource)
    {
        var events = EventsFromIdentities(identities);
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
            eventSource,
            true,
            request.Fake,
            events.OrderBy(e => e.Sequence).ToArray(),
            events.Where(e => e.EventType == "processStart")
                .Select(e => new PimaxStartupProcessCreator(e.ProcessName, e.ParentToken, e.ParentToken is "unknown" or "external-parent" ? "parent unavailable or outside captured baseline" : "parent token preserved from event-time process trace", e.ParentToken is "unknown" or "external-parent" ? "unknown" : "probable"))
                .DistinctBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            present,
            missing,
            events.Where(e => e.Classification == "unexpected").Select(e => e.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            [],
            ["raw PIDs", "raw command lines", "environment blocks", "window contents", "handles", "user name", "machine name"],
            warnings.ToArray(),
            errors.ToArray(),
            missing.Length == 0 ? "Observed Pimax process lifecycle reached the required member set." : "Bounded process lifecycle observation completed without proving all required members started during the window.",
            identities.OrderBy(item => item.StartTimestamp).ThenBy(item => item.Token, StringComparer.Ordinal).ToArray(),
            PimaxCreatorChainAnalyzer.Assess(request.ObservationId, identities.ToArray()));
    }

    private static PimaxProcessStartIdentity[] FakeIdentities(DateTimeOffset at)
        =>
        [
            Identity("baseline:0001", "external-parent", "explorer", "<system>\\explorer.exe", "windowsShell", at.AddMinutes(-10), null, true),
            Identity("process:0001", "baseline:0001", "PimaxClient", @"<pimax>\PimaxClient\pimaxui\PimaxClient.exe", PimaxSoftwareGroupRole.PimaxPlayUiProcess, at, null, false),
            Identity("process:0002", "process:0001", "launcher", @"<pimax>\Runtime\launcher.exe", PimaxSoftwareGroupRole.HelperProcess, at.AddMilliseconds(100), at.AddMilliseconds(180), false),
            Identity("process:0003", "process:0002", "DeviceSetting", @"<pimax>\Runtime\DeviceSetting.exe", PimaxSoftwareGroupRole.RuntimeProcess, at.AddMilliseconds(220), null, false),
            Identity("process:0004", "process:0003", "PiPlayService", @"<pimax>\Runtime\PiPlayService.exe", PimaxSoftwareGroupRole.RuntimeProcess, at.AddMilliseconds(340), null, false),
            Identity("process:0005", "process:0003", "PiService", @"<pimax>\Runtime\PiService.exe", PimaxSoftwareGroupRole.ServiceOwnedProcess, at.AddMilliseconds(460), null, false),
            Identity("process:0006", "process:0005", "pi_server", @"<pimax>\Runtime\pi_server.exe", PimaxSoftwareGroupRole.RuntimeProcess, at.AddMilliseconds(580), null, false),
            Identity("process:0007", "service:tobii", "Tobii VR4PIMAXP3B Platform Runtime", @"<pimax>\Runtime\EyeTrackingServer\platform_runtime\platform_runtime_VR4PIMAXP3B_service.exe", PimaxSoftwareGroupRole.ServiceOwnedProcess, at.AddMilliseconds(700), null, false)
        ];

    private static PimaxProcessStartIdentity Identity(string token, string parentToken, string name, string path, string role, DateTimeOffset? start, DateTimeOffset? stop, bool baseline)
        => new(
            token,
            parentToken,
            name,
            path,
            path.StartsWith("<pimax>", StringComparison.OrdinalIgnoreCase) ? "Pimax installation-root executable." : "broker or external creator candidate",
            "privateArgumentsNotCollected",
            role.Contains("service", StringComparison.OrdinalIgnoreCase) ? "service" : "interactive-or-broker",
            role,
            start,
            stop,
            baseline,
            stop is not null,
            parentToken is "unknown" or "external-parent" ? "unknown" : "probable");

    private static List<PimaxProcessStartIdentity> CaptureBaseline(TokenMap tokenMap)
    {
        var result = new List<PimaxProcessStartIdentity>();
        try
        {
            result.AddRange(CaptureCurrentProcesses(tokenMap, baseline: true));
        }
        catch
        {
            return result;
        }
        return result;
    }

    private static List<PimaxProcessStartIdentity> CaptureCurrentProcesses(TokenMap tokenMap, bool baseline = false)
    {
        var result = new List<PimaxProcessStartIdentity>();
        using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CreationDate, SessionId FROM Win32_Process");
        foreach (ManagementObject process in searcher.Get())
        {
            var name = NormalizeProcessName(SafeString(process["Name"]));
            var path = SafeString(process["ExecutablePath"]);
            if (!IsRelevant(name, path) && !IsBroker(name)) continue;
            var pid = SafeUInt(process["ProcessId"]);
            var parentPid = SafeUInt(process["ParentProcessId"]);
            var start = ManagementTime(SafeString(process["CreationDate"]));
            var token = tokenMap.TokenFor(pid, start, baseline, name);
            var parentToken = tokenMap.TokenForKnownPid(parentPid) ?? (parentPid == 0 ? "unknown" : "external-parent");
            result.Add(new PimaxProcessStartIdentity(
                token,
                parentToken,
                name,
                SanitizeProcessPath(path, name),
                path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) ? "Pimax installation-root executable." : "broker or service-control candidate",
                baseline ? "commandLineNotCollected" : "privateArgumentsNotCollected",
                SafeString(process["SessionId"]).Length == 0 ? "unknown" : "session-" + SafeString(process["SessionId"]),
                RoleForName(name),
                start,
                null,
                baseline,
                false,
                parentToken is "unknown" or "external-parent" ? "unknown" : (baseline ? "possible" : "probable")));
        }

        return result;
    }

    private static bool IsChainRelevantProcess(PimaxProcessStartIdentity identity)
        => IsRelevant(identity.ProcessName, identity.RelativePath) || IsBroker(identity.ProcessName);

    private static PimaxProcessStartIdentity IdentityFromStartEvent(ManagementBaseObject item, TokenMap tokenMap)
    {
        var pid = SafeUInt(item["ProcessID"]);
        var parentPid = SafeUInt(item["ParentProcessID"]);
        var name = NormalizeProcessName(SafeString(item["ProcessName"]));
        var timestamp = TimeCreated(item["TIME_CREATED"]) ?? DateTimeOffset.Now;
        var path = TryProcessPath(pid);
        var token = tokenMap.TokenFor(pid, timestamp, baseline: false, name);
        var parentToken = tokenMap.TokenForKnownPid(parentPid) ?? (parentPid == 0 ? "unknown" : "external-parent");
        var role = RoleForName(name);
        return new PimaxProcessStartIdentity(
            token,
            parentToken,
            name,
            SanitizeProcessPath(path, name),
            path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) ? "Pimax installation-root executable." : "path unavailable or outside Pimax root",
            "privateArgumentsNotCollected",
            "unknown",
            role,
            timestamp,
            null,
            false,
            false,
            parentToken is "unknown" or "external-parent" ? "unknown" : "probable");
    }

    internal static PimaxStartupEvent[] EventsFromIdentities(IEnumerable<PimaxProcessStartIdentity> identities)
    {
        var sequence = 0;
        var events = new List<PimaxStartupEvent>();
        foreach (var identity in identities.Where(item => !item.PresentInBaseline).OrderBy(item => item.StartTimestamp).ThenBy(item => item.Token, StringComparer.Ordinal))
        {
            events.Add(Event(++sequence, identity, "processStart", identity.StartTimestamp ?? DateTimeOffset.Now));
            if (identity.StopTimestamp is not null)
            {
                events.Add(Event(++sequence, identity, "processStop", identity.StopTimestamp.Value));
            }
        }

        return events.OrderBy(e => e.ObservedAt).ThenBy(e => e.Sequence).Select((item, index) => item with { Sequence = index + 1 }).ToArray();
    }

    private static PimaxStartupEvent Event(int sequence, PimaxProcessStartIdentity identity, string type, DateTimeOffset at)
    {
        var expected = ExpectedNames.Contains(identity.ProcessName, StringComparer.OrdinalIgnoreCase)
            || identity.RelativePath.StartsWith("<pimax>", StringComparison.OrdinalIgnoreCase)
            || IsBroker(identity.ProcessName);
        return new PimaxStartupEvent(
            sequence,
            at,
            type,
            identity.Token,
            identity.ParentToken,
            identity.ProcessName,
            identity.RelativePath,
            identity.SignerSummary,
            identity.Session,
            identity.Role,
            expected ? "expected" : "unexpected");
    }

    internal static string RoleForName(string name)
        => name.Equals("PimaxClient", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.PimaxPlayUiProcess :
            name.Equals("DeviceSetting", StringComparison.OrdinalIgnoreCase) || name.Equals("PiPlayService", StringComparison.OrdinalIgnoreCase) || name.Equals("pi_server", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.RuntimeProcess :
            name.Equals("PiService", StringComparison.OrdinalIgnoreCase) || name.Equals("PiServiceLauncher", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.ServiceOwnedProcess :
            name.Equals("launcher", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.HelperProcess :
            IsBroker(name) ? "windowsShell" :
            PimaxSoftwareGroupRole.OptionalComponent;

    private static bool IsRelevant(string name, string path)
        => ExpectedNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || name.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VR4PIMAX", StringComparison.OrdinalIgnoreCase)
            || name.Contains("PiService", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase);

    private static bool IsBroker(string name)
        => BrokerNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool RequiredAliasMatches(string required, string processName, string relativePath)
        => required.Equals("Tobii VR4PIMAXP3B Platform Runtime", StringComparison.OrdinalIgnoreCase)
            && (processName.Contains("VR4PIMAX", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("VR4PIMAX", StringComparison.OrdinalIgnoreCase));

    private static string TryProcessPath(uint pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId={pid}");
            foreach (ManagementObject process in searcher.Get())
            {
                return SafeString(process["ExecutablePath"]);
            }
        }
        catch
        {
        }

        return "";
    }

    private static string SanitizeProcessPath(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return IsBroker(name) ? "<system>\\" + name + ".exe" : "";
        }

        return PimaxStartupSourcesCollector.SanitizePath(path);
    }

    private static string NormalizeProcessName(string name)
        => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(name) : name;

    private static DateTimeOffset? ManagementTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return ManagementDateTimeConverter.ToDateTime(value); }
        catch { return null; }
    }

    private static DateTimeOffset? TimeCreated(object? value)
    {
        if (value is null) return null;
        try
        {
            var raw = Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            return DateTimeOffset.FromFileTime((long)raw);
        }
        catch
        {
            return null;
        }
    }

    private static uint SafeUInt(object? value)
    {
        try { return Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    private static string SafeString(object? value) => value?.ToString() ?? "";

    private static bool IsCurrentProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void TryStop(ManagementEventWatcher watcher)
    {
        try { watcher.Stop(); }
        catch { }
    }

    private sealed class TokenMap
    {
        private readonly Dictionary<string, string> _tokensByIdentity = [];
        private readonly Dictionary<uint, string> _latestByPid = [];
        private readonly Dictionary<string, uint> _pidByToken = [];

        public string TokenFor(uint processId, DateTimeOffset? start, bool baseline, string name)
        {
            var basis = processId + "|" + (start?.ToString("O") ?? "unknown") + "|" + name;
            if (_tokensByIdentity.TryGetValue(basis, out var token)) return token;
            token = (baseline ? "baseline:" : "process:") + (_tokensByIdentity.Count + 1).ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
            _tokensByIdentity[basis] = token;
            _latestByPid[processId] = token;
            _pidByToken[token] = processId;
            return token;
        }

        public string? TokenForKnownPid(uint processId)
            => _latestByPid.TryGetValue(processId, out var token) ? token : null;

        public bool MatchesPid(string token, uint processId)
            => _pidByToken.TryGetValue(token, out var pid) && pid == processId;
    }
}

internal static class PimaxCreatorChainAnalyzer
{
    public static PimaxCreatorChainAssessment Assess(string observationId, IReadOnlyCollection<PimaxProcessStartIdentity> identities)
    {
        var nodes = identities
            .Where(item => item.PresentInBaseline || IsChainRelevant(item))
            .Select(item => new PimaxCreatorChainNode(item.Token, item.ProcessName, item.Role, item.PresentInBaseline, item.ExitedDuringObservation, item.RelativePath))
            .ToArray();
        var edges = identities
            .Where(item => !item.PresentInBaseline && item.ParentToken is not "unknown" and not "external-parent")
            .Select(item =>
            {
                var creator = identities.FirstOrDefault(parent => parent.Token == item.ParentToken);
                return new PimaxCreatorChainEdge(
                    item.ParentToken,
                    item.Token,
                    item.ProcessName,
                    item.StartTimestamp,
                    "event-time process parent token",
                    creator is null ? "possible" : "confirmed",
                    creator?.PresentInBaseline == true,
                    creator?.ExitedDuringObservation == true,
                    true);
            })
            .ToArray();
        var device = First(identities, "DeviceSetting");
        var piPlay = First(identities, "PiPlayService");
        var piService = First(identities, "PiService");
        var piServer = First(identities, "pi_server");
        var deviceCreator = CreatorName(identities, device);
        var root = RootCandidate(identities, device);
        var gaps = new List<string>();
        var rootResult = "insufficientEvidence";
        var confidence = "insufficient";
        if (device is null)
        {
            gaps.Add("DeviceSetting start was not captured.");
        }
        else if (device.ParentToken is "unknown" or "external-parent")
        {
            rootResult = device.ParentToken == "external-parent" ? "unknownExternalCreator" : "creatorExitedBeforeCapture";
            gaps.Add("DeviceSetting parent was not resolved to a preserved process token.");
        }
        else if (root is not null)
        {
            rootResult = CategoryFor(root);
            confidence = root.PresentInBaseline || root.ExitedDuringObservation ? "confirmed" : "probable";
        }

        var rootCandidates = root is null
            ? Array.Empty<PimaxActivationRootCandidate>()
            : [new PimaxActivationRootCandidate(CategoryFor(root), root.Token, root.ProcessName, "Root reached by following preserved parent tokens from DeviceSetting.", confidence)];
        return new PimaxCreatorChainAssessment(
            PimaxStartupCreatorChainSchema.Version,
            observationId,
            rootResult,
            confidence,
            deviceCreator,
            CreatorName(identities, piPlay),
            CreatorName(identities, piService),
            CreatorName(identities, piServer),
            nodes,
            edges,
            rootCandidates,
            gaps.ToArray(),
            false,
            Summary(rootResult, deviceCreator));
    }

    public static PimaxCreatorChainAssessment FromObservation(PimaxStartupObservationSnapshot snapshot)
        => Assess(snapshot.ObservationId, snapshot.Processes);

    private static PimaxProcessStartIdentity? First(IEnumerable<PimaxProcessStartIdentity> identities, string name)
        => identities
            .Where(item => string.Equals(item.ProcessName, name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.PresentInBaseline)
            .ThenBy(item => item.ExitedDuringObservation)
            .ThenBy(item => item.StartTimestamp)
            .FirstOrDefault();

    private static string CreatorName(IEnumerable<PimaxProcessStartIdentity> identities, PimaxProcessStartIdentity? child)
    {
        if (child is null) return "notCaptured";
        if (child.ParentToken is "unknown" or "external-parent") return child.ParentToken;
        var parent = identities.FirstOrDefault(item => item.Token == child.ParentToken);
        return parent?.ProcessName ?? child.ParentToken;
    }

    private static PimaxProcessStartIdentity? RootCandidate(IReadOnlyCollection<PimaxProcessStartIdentity> identities, PimaxProcessStartIdentity? child)
    {
        var current = child;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (current is not null && visited.Add(current.Token))
        {
            if (current.ParentToken is "unknown" or "external-parent") return current == child ? null : current;
            current = identities.FirstOrDefault(item => item.Token == current.ParentToken);
        }

        return current;
    }

    private static string CategoryFor(PimaxProcessStartIdentity identity)
        => identity.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ? "windowsExplorer" :
            identity.ProcessName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ? "startMenuExperienceHost" :
            identity.ProcessName is "ShellExperienceHost" or "RuntimeBroker" or "ApplicationFrameHost" or "svchost" or "taskhostw" or "dllhost" ? "windowsShellBroker" :
            identity.ProcessName.Equals("launcher", StringComparison.OrdinalIgnoreCase) ? "pimaxBootstrapHelper" :
            identity.ProcessName.Equals("PiServiceLauncher", StringComparison.OrdinalIgnoreCase) ? "piServiceLauncher" :
            identity.ProcessName.Contains("PiService", StringComparison.OrdinalIgnoreCase) ? "pimaxServiceBroker" :
            identity.ProcessName.Equals("services", StringComparison.OrdinalIgnoreCase) ? "serviceControlManager" :
            identity.PresentInBaseline ? "existingPimaxProcess" :
            "unknownExternalCreator";

    private static bool IsChainRelevant(PimaxProcessStartIdentity item)
        => PimaxStartupObserver.RequiredMembers.Contains(item.ProcessName, StringComparer.OrdinalIgnoreCase)
            || item.ProcessName is "PiService" or "launcher"
            || item.RelativePath.StartsWith("<pimax>", StringComparison.OrdinalIgnoreCase)
            || item.Role == "windowsShell";

    private static string Summary(string rootResult, string deviceCreator)
        => rootResult switch
        {
            "windowsExplorer" or "startMenuExperienceHost" or "windowsShellBroker" => "DeviceSetting was traced back to the Windows shell activation chain; backend execution still requires separate programmatic validation.",
            "pimaxBootstrapHelper" => "DeviceSetting was traced to a transient Pimax bootstrap helper; backend execution remains disabled until a safe adapter is validated.",
            "pimaxServiceBroker" or "piServiceLauncher" or "serviceControlManager" => "DeviceSetting was traced to a Pimax service-control path; backend execution remains disabled pending validation.",
            "unknownExternalCreator" => "DeviceSetting parent was external to the preserved observation graph.",
            "creatorExitedBeforeCapture" => "DeviceSetting creator exited before it could be resolved.",
            _ => $"DeviceSetting creator remains unresolved: {deviceCreator}."
        };
}
