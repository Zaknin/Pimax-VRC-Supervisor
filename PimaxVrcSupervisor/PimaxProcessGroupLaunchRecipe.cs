using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Microsoft.Win32;

internal static class PimaxLaunchRecipeSchema
{
    public const string Version = "pimax-launch-recipe-v1";
}

internal static class PimaxProcessGroupLaunchRecipeState
{
    public const string Candidate = "candidate";
    public const string VerifiedReadOnly = "verifiedReadOnly";
    public const string ReadyForControlledValidation = "readyForControlledValidation";
    public const string DirectLaunchRejected = "directLaunchRejected";
    public const string ShellActivationObserved = "shellActivationObserved";
    public const string ActivationRootIdentified = "activationRootIdentified";
    public const string ReadyForShellActivationValidation = "readyForShellActivationValidation";
    public const string ActivationMechanismIdentified = "activationMechanismIdentified";
    public const string ReadyForActivationValidation = "readyForActivationValidation";
    public const string Validated = "validated";
    public const string Incomplete = "incomplete";
    public const string Rejected = "rejected";
    public const string Conflicting = "conflicting";
    public const string Unknown = "unknown";
}

internal static class PimaxProcessGroupReadinessState
{
    public const string GroupReadyAndRegistered = "groupReadyAndRegistered";
    public const string GroupReadyAwaitingRegistration = "groupReadyAwaitingRegistration";
    public const string GroupCompleteRegistrationUnknown = "groupCompleteRegistrationUnknown";
    public const string GroupStarting = "groupStarting";
    public const string GroupPartial = "groupPartial";
    public const string GroupUnavailable = "groupUnavailable";
    public const string GroupConflicting = "groupConflicting";
    public const string GroupLaunchFailed = "groupLaunchFailed";
    public const string TimedOut = "timedOut";
    public const string Unknown = "unknown";
}

internal sealed record PimaxLaunchRecipeSnapshot(
    string Schema,
    DateTimeOffset CollectedAt,
    PimaxProcessGroupLaunchCandidate[] LauncherCandidates,
    PimaxProcessGroupLaunchCandidate? SelectedCandidate,
    PimaxProcessGroupLaunchRecipe Recipe,
    PimaxProcessGroupReadiness Readiness,
    PimaxProcessGroupLaunchEvidence Evidence,
    string[] Blockers,
    string HumanReadableSummary,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxProcessGroupLaunchCandidate(
    string CandidateId,
    string Source,
    string SanitizedPath,
    bool PathExists,
    bool PathInExpectedInstallRoot,
    bool ProductMetadataMatches,
    bool SignerMatches,
    string SignerSummary,
    string ProductName,
    string CompanyName,
    string FileVersion,
    string ProductVersion,
    string Architecture,
    string Subsystem,
    string RequestedExecutionLevel,
    string HashPolicy,
    string? ShortcutArguments,
    string? ShortcutWorkingDirectory,
    string? RegistryDisplayName,
    string? RegistryDisplayVersion,
    string? RegistryPublisher,
    string ValidationState,
    string[] Blockers);

internal sealed record PimaxProcessGroupLaunchRecipe(
    string State,
    bool Executable,
    string LauncherExecutable,
    string SanitizedPath,
    string ExpectedSigner,
    string VersionPolicy,
    string Arguments,
    string WorkingDirectory,
    string EnvironmentRequirements,
    string UserSessionRequirements,
    string ElevationRequirement,
    string SingleInstanceBehavior,
    string ExpectedInitialProcess,
    string[] ExpectedProcessGroupMembers,
    string[] OptionalProcessGroupMembers,
    string[] ExpectedProcessStartOrder,
    int ExpectedMaximumStartSeconds,
    string LifecycleRoot,
    string LifecycleRootConfidence,
    string[] ReadinessCriteria,
    string[] FailureCriteria,
    string[] ProhibitedSideEffects,
    string CancellationBoundary,
    string HumanReadableSummary);

internal sealed record PimaxProcessGroupReadiness(
    string State,
    string[] RequiredMembersPresent,
    string[] RequiredMembersMissing,
    string[] OptionalMembersPresent,
    bool PathsAndSignersValid,
    bool UnexpectedGroupMembersPresent,
    string RegistrationState,
    string RegistrationConfidence,
    string EvidenceFreshness,
    string ComponentHealthStatus,
    string HumanReadableMessage);

internal sealed record PimaxProcessGroupLaunchEvidence(
    string StartMenuShortcut,
    string StartMenuTarget,
    string StartMenuArguments,
    string StartMenuWorkingDirectory,
    string InstalledApplication,
    string InstalledApplicationVersion,
    string AppPathsRegistration,
    string CurrentLifecycleEvidence,
    string[] CurrentRequiredMembers,
    string[] CurrentOptionalMembers,
    string[] PrivacyRedactions);

internal sealed record PimaxLaunchRecipeCandidateEvidence(
    string Source,
    string Path,
    bool Exists,
    bool InExpectedInstallRoot,
    bool ProductMetadataMatches,
    bool SignerMatches,
    string SignerSummary,
    string ProductName,
    string CompanyName,
    string FileVersion,
    string ProductVersion,
    string Architecture,
    string Subsystem,
    string RequestedExecutionLevel,
    string HashPolicy,
    string? ShortcutArguments,
    string? ShortcutWorkingDirectory,
    string? RegistryDisplayName,
    string? RegistryDisplayVersion,
    string? RegistryPublisher);

internal sealed record PimaxLaunchRecipeEvidenceInput(
    PimaxLaunchRecipeCandidateEvidence[] Candidates,
    string[] RequiredMembersPresent,
    string[] OptionalMembersPresent,
    string RegistrationState,
    string RegistrationConfidence,
    string EvidenceFreshness,
    string ComponentHealthStatus,
    string LifecycleRoot,
    string LifecycleRootConfidence,
    string[] Warnings,
    string[] Errors);

internal static class PimaxProcessGroupLaunchRecipeModel
{
    public const string CandidateLauncherPath = @"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe";
    public const string CandidateWorkingDirectory = @"C:\Program Files\Pimax\PimaxClient\pimaxui";
    private static readonly string[] RequiredMembers = ["PimaxClient", "DeviceSetting", "PiPlayService", "pi_server", "PiServiceLauncher", "Tobii VR4PIMAXP3B Platform Runtime"];
    private static readonly string[] OptionalMembers = ["PiService", "PiPlatformService_64", "PVRHome", "pi_overlay"];

    public static async Task<PimaxLaunchRecipeSnapshot> CollectAsync(SupervisorConfig config, CancellationToken cancellationToken)
    {
        var health = await new PimaxComponentHealthCoordinator().CollectAsync(config, cancellationToken);
        var candidates = DiscoverCandidates();
        var group = health.SourceEvidence.SoftwareGroup;
        var input = new PimaxLaunchRecipeEvidenceInput(
            candidates,
            RequiredMembers.Where(member => group.Members.Any(observed => string.Equals(observed.ProcessName, member, StringComparison.OrdinalIgnoreCase))).ToArray(),
            OptionalMembers.Where(member => group.Members.Any(observed => string.Equals(observed.ProcessName, member, StringComparison.OrdinalIgnoreCase))).ToArray(),
            health.RegistrationAssessment.State,
            health.RegistrationAssessment.Confidence,
            health.RegistrationAssessment.EvidenceFreshness,
            health.OverallStatus,
            "Windows Explorer-rooted official Start Menu Shell activation starts PimaxClient, transient launcher helpers, DeviceSetting, and the runtime group.",
            "confirmed",
            [],
            []);
        return Build(input, DateTimeOffset.Now);
    }

    internal static PimaxLaunchRecipeSnapshot Build(PimaxLaunchRecipeEvidenceInput input, DateTimeOffset collectedAt)
    {
        var warnings = new List<string>(input.Warnings);
        var errors = new List<string>(input.Errors);
        var candidateModels = input.Candidates.Select(ToCandidate).ToArray();
        var selected = candidateModels
            .Where(candidate => candidate.PathExists && candidate.PathInExpectedInstallRoot && candidate.ProductMetadataMatches && candidate.SignerMatches)
            .OrderBy(candidate => candidate.Source == "startMenuShortcut" ? 0 : 1)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .FirstOrDefault();
        var blockers = new List<string>();
        if (selected is null)
        {
            blockers.Add("No launcher candidate passed path, signer, and product metadata validation.");
        }

        var missingMembers = RequiredMembers.Except(input.RequiredMembersPresent, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missingMembers.Length > 0)
        {
            blockers.Add("Current healthy process-group snapshot is missing required members: " + string.Join(", ", missingMembers));
        }

        if (selected is not null && string.IsNullOrWhiteSpace(selected.ShortcutWorkingDirectory))
        {
            blockers.Add("Shortcut working directory is unknown.");
        }

        var state = blockers.Count == 0
            ? PimaxProcessGroupLaunchRecipeState.ReadyForShellActivationValidation
            : selected is null
                ? PimaxProcessGroupLaunchRecipeState.Incomplete
                : PimaxProcessGroupLaunchRecipeState.VerifiedReadOnly;
        var readiness = BuildReadiness(input, missingMembers, selected is not null);
        var recipe = new PimaxProcessGroupLaunchRecipe(
            state,
            false,
            "PimaxClient.exe",
            SanitizePath(CandidateLauncherPath) ?? @"<pimax>\PimaxClient\pimaxui\PimaxClient.exe",
            selected?.SignerSummary ?? "Pimax publisher signature required.",
            selected is null ? "unknown" : $"Pimax Play {selected.FileVersion}; exact path and Pimax publisher metadata required.",
            selected?.ShortcutArguments ?? "",
            SanitizePath(selected?.ShortcutWorkingDirectory ?? CandidateWorkingDirectory) ?? "<pimax>\\PimaxClient\\pimaxui",
            "Use the installed Pimax environment. Do not inject transient command-line, IPC, token, or PID values.",
            "Launch only in the interactive user session during a later explicit stopped-state validation.",
            selected?.RequestedExecutionLevel == "asInvoker" ? "asInvoker; no elevation indicated by launcher manifest." : "unknown",
            "Direct process creation was rejected by Phase 28D2-BV2; B2C confirmed the normal Start Menu Shell activation root as Windows Explorer.",
            "Official Windows Start Menu PimaxPlay Shell activation through PimaxPlay.lnk; the shortcut target is PimaxClient.exe, but direct process creation is rejected.",
            RequiredMembers,
            OptionalMembers,
            ["Start Menu PimaxPlay activation", "PimaxClient", "DeviceSetting", "PiPlayService", "PiServiceLauncher", "pi_server", "optional PVRHome/pi_overlay"],
            90,
            input.LifecycleRoot,
            input.LifecycleRootConfidence,
            [
                "Direct PimaxClient.exe process creation is rejected for complete process-group recovery.",
                "B2C confirmed the Explorer-rooted manual Start Menu Shell activation chain.",
                "The B2D Shell adapter must be validated once programmatically before backend execution is enabled.",
                "Required members are present from the same current software-group snapshot.",
                "Launcher path and Pimax signer/product metadata validate.",
                "No unexpected required group member is missing.",
                "Registration is current and owned by the active group before reporting full repair success."
            ],
            [
                "Direct process creation produces PimaxClient without the required runtime members.",
                "Start Menu activation produces PimaxClient but required group members do not form.",
                "Required member path or signer validation fails.",
                "Registration remains unavailable after group formation.",
                "Unexpected conflicting Pimax-root process appears.",
                "Controlled validation times out."
            ],
            [
                "Do not stop or close Pimax Play in this phase.",
                "Do not restart services.",
                "Do not invoke Pimax Play Connect.",
                "Do not automate GUI input.",
                "Do not cycle USB or touch DisplayPort.",
                "Do not restart SteamVR, VRChat, VRCFT, Supervisor, or watcher.",
                "Do not change scheduled tasks."
            ],
            "Cancellation is only safe before any future launch request is issued.",
            state == PimaxProcessGroupLaunchRecipeState.ReadyForShellActivationValidation
                ? "Direct PimaxClient.exe process creation is rejected. Official Start Menu Shell activation is the confirmed manual mechanism and a programmatic Shell adapter exists, but equivalence remains unvalidated, so backend execution is disabled."
                : "The launch recipe remains incomplete and is not executable.");

        var evidence = new PimaxProcessGroupLaunchEvidence(
            selected?.Source == "startMenuShortcut" ? "PimaxPlay.lnk" : "not matched",
            selected?.SanitizedPath ?? "unknown",
            selected?.ShortcutArguments ?? "",
            selected?.ShortcutWorkingDirectory is null ? "unknown" : SanitizePath(selected.ShortcutWorkingDirectory) ?? selected.ShortcutWorkingDirectory,
            selected?.RegistryDisplayName ?? "PimaxPlay version 1.43.9.272",
            selected?.RegistryDisplayVersion ?? "unknown",
            "none",
            input.LifecycleRoot + " Programmatic equivalence remains unvalidated.",
            input.RequiredMembersPresent,
            input.OptionalMembersPresent,
            ["raw PIDs", "raw command lines", "user profile paths", "machine name", "certificate serial numbers", "raw PnP IDs"]);

        return new PimaxLaunchRecipeSnapshot(
            PimaxLaunchRecipeSchema.Version,
            collectedAt,
            candidateModels,
            selected,
            recipe,
            readiness,
            evidence,
            blockers.ToArray(),
            recipe.HumanReadableSummary,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static PimaxProcessGroupReadiness BuildReadiness(PimaxLaunchRecipeEvidenceInput input, string[] missingMembers, bool candidateValid)
    {
        var complete = missingMembers.Length == 0;
        var registered = input.RegistrationState == PimaxRegistrationState.RegisteredReady
            && input.RegistrationConfidence == PimaxRegistrationConfidence.Confirmed
            && input.EvidenceFreshness == PimaxEvidenceFreshness.Current;
        var state = !complete ? PimaxProcessGroupReadinessState.GroupPartial :
            registered && candidateValid ? PimaxProcessGroupReadinessState.GroupReadyAndRegistered :
            input.RegistrationState == PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration ? PimaxProcessGroupReadinessState.GroupReadyAwaitingRegistration :
            PimaxProcessGroupReadinessState.GroupCompleteRegistrationUnknown;
        var message = state switch
        {
            PimaxProcessGroupReadinessState.GroupReadyAndRegistered => "Pimax Play/runtime group is complete and current registration is confirmed.",
            PimaxProcessGroupReadinessState.GroupReadyAwaitingRegistration => "Pimax Play started successfully, but the headset is still awaiting\nregistration.\n\nPimax Play Connect and a physical USB reconnection may still be required.",
            PimaxProcessGroupReadinessState.GroupPartial => "Required Pimax Play/runtime group members are missing.",
            _ => "Pimax Play/runtime group is complete, but registration ownership is not confirmed."
        };
        return new PimaxProcessGroupReadiness(
            state,
            input.RequiredMembersPresent,
            missingMembers,
            input.OptionalMembersPresent,
            candidateValid,
            false,
            input.RegistrationState,
            input.RegistrationConfidence,
            input.EvidenceFreshness,
            input.ComponentHealthStatus,
            message);
    }

    private static PimaxProcessGroupLaunchCandidate ToCandidate(PimaxLaunchRecipeCandidateEvidence evidence)
    {
        var blockers = new List<string>();
        if (!evidence.Exists) blockers.Add("launcher file missing");
        if (!evidence.InExpectedInstallRoot) blockers.Add("launcher is outside expected Pimax install root");
        if (!evidence.ProductMetadataMatches) blockers.Add("product metadata does not identify PimaxClient");
        if (!evidence.SignerMatches) blockers.Add("signer does not match expected Pimax publisher evidence");
        if (evidence.ShortcutArguments is null) blockers.Add("launch arguments unknown");
        if (evidence.ShortcutWorkingDirectory is null) blockers.Add("working directory unknown");
        return new PimaxProcessGroupLaunchCandidate(
            CandidateId(evidence.Source, evidence.Path),
            evidence.Source,
            SanitizePath(evidence.Path) ?? evidence.Path,
            evidence.Exists,
            evidence.InExpectedInstallRoot,
            evidence.ProductMetadataMatches,
            evidence.SignerMatches,
            evidence.SignerSummary,
            evidence.ProductName,
            evidence.CompanyName,
            evidence.FileVersion,
            evidence.ProductVersion,
            evidence.Architecture,
            evidence.Subsystem,
            evidence.RequestedExecutionLevel,
            evidence.HashPolicy,
            evidence.ShortcutArguments,
            evidence.ShortcutWorkingDirectory is null ? null : SanitizePath(evidence.ShortcutWorkingDirectory),
            evidence.RegistryDisplayName,
            evidence.RegistryDisplayVersion,
            evidence.RegistryPublisher,
            blockers.Count == 0 ? PimaxProcessGroupLaunchRecipeState.VerifiedReadOnly : PimaxProcessGroupLaunchRecipeState.Incomplete,
            blockers.ToArray());
    }

    private static PimaxLaunchRecipeCandidateEvidence[] DiscoverCandidates()
    {
        var result = new List<PimaxLaunchRecipeCandidateEvidence>();
        var shortcut = DiscoverStartMenuShortcut();
        var registry = DiscoverInstalledApplication();
        var launcher = File.Exists(CandidateLauncherPath)
            ? CandidateFromFile(shortcut?.TargetPath ?? CandidateLauncherPath, shortcut, registry, shortcut is null ? "candidateGroupLauncher" : "startMenuShortcut")
            : MissingCandidate(shortcut?.TargetPath ?? CandidateLauncherPath, shortcut, registry, shortcut is null ? "candidateGroupLauncher" : "startMenuShortcut");
        result.Add(launcher);
        return result.ToArray();
    }

    private static PimaxLaunchRecipeCandidateEvidence CandidateFromFile(string path, ShortcutEvidence? shortcut, InstalledApplicationEvidence? registry, string source)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        var signer = SignerSummary(path, info);
        return new PimaxLaunchRecipeCandidateEvidence(
            source,
            path,
            true,
            path.StartsWith(@"C:\Program Files\Pimax\", StringComparison.OrdinalIgnoreCase),
            string.Equals(info.ProductName, "PimaxClient", StringComparison.OrdinalIgnoreCase)
                && string.Equals(info.CompanyName, "Pimax", StringComparison.OrdinalIgnoreCase),
            signer.Matches,
            signer.Summary,
            info.ProductName ?? "",
            info.CompanyName ?? "",
            info.FileVersion ?? "",
            info.ProductVersion ?? "",
            IsPe64(path) ? "x64" : "x86-or-unknown",
            "WindowsGui",
            RequestedExecutionLevel(path),
            "Exact hash captured privately; public recipe pins expected path, signer, product, and version.",
            shortcut?.Arguments ?? "",
            shortcut?.WorkingDirectory ?? CandidateWorkingDirectory,
            registry?.DisplayName,
            registry?.DisplayVersion,
            registry?.Publisher);
    }

    private static PimaxLaunchRecipeCandidateEvidence MissingCandidate(string path, ShortcutEvidence? shortcut, InstalledApplicationEvidence? registry, string source)
        => new(source, path, false, path.StartsWith(@"C:\Program Files\Pimax\", StringComparison.OrdinalIgnoreCase), false, false, "missing", "", "", "", "", "unknown", "unknown", "unknown", "none", shortcut?.Arguments, shortcut?.WorkingDirectory, registry?.DisplayName, registry?.DisplayVersion, registry?.Publisher);

    private static ShortcutEvidence? DiscoverStartMenuShortcut()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).Where(path => Path.GetFileName(path).Contains("Pimax", StringComparison.OrdinalIgnoreCase)))
            {
                var shortcut = TryReadShortcut(file);
                if (shortcut is not null && string.Equals(shortcut.TargetPath, CandidateLauncherPath, StringComparison.OrdinalIgnoreCase))
                {
                    return shortcut;
                }
            }
        }

        return null;
    }

    private static ShortcutEvidence? TryReadShortcut(string path)
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
            return new ShortcutEvidence(
                path,
                SafeString(type.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)),
                SafeString(type.InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)),
                SafeString(type.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)));
        }
        catch
        {
            return null;
        }
    }

    private static InstalledApplicationEvidence? DiscoverInstalledApplication()
    {
        var fallback = default(InstalledApplicationEvidence);
        foreach (var rootName in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var root = Registry.LocalMachine.OpenSubKey(rootName);
            if (root is null) continue;
            foreach (var keyName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(keyName);
                var displayName = SafeString(key?.GetValue("DisplayName"));
                var publisher = SafeString(key?.GetValue("Publisher"));
                if (!displayName.Contains("PimaxPlay", StringComparison.OrdinalIgnoreCase) && !publisher.Contains("Pimax", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var entry = new InstalledApplicationEvidence(displayName, SafeString(key?.GetValue("DisplayVersion")), publisher);
                if (displayName.Contains("PimaxPlay", StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }

                fallback ??= entry;
            }
        }

        return fallback;
    }

    private static (bool Matches, string Summary) SignerSummary(string path, FileVersionInfo info)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var subject = certificate.Subject.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
                ? "Pimax certificate subject present"
                : "publisher certificate present";
            var metadataMatches = string.Equals(info.CompanyName, "Pimax", StringComparison.OrdinalIgnoreCase);
            return (metadataMatches, subject + "; product metadata publisher=Pimax");
        }
        catch
        {
            return (string.Equals(info.CompanyName, "Pimax", StringComparison.OrdinalIgnoreCase), "signature inspection unavailable; product metadata publisher=Pimax required");
        }
    }

    private static bool IsPe64(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var peOffset = BitConverter.ToInt32(bytes, 0x3c);
            return BitConverter.ToUInt16(bytes, peOffset + 24) == 0x20b;
        }
        catch
        {
            return false;
        }
    }

    private static string RequestedExecutionLevel(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            if (text.Contains("level=\"asInvoker\"", StringComparison.OrdinalIgnoreCase)) return "asInvoker";
            if (text.Contains("level=\"requireAdministrator\"", StringComparison.OrdinalIgnoreCase)) return "requireAdministrator";
            if (text.Contains("level=\"highestAvailable\"", StringComparison.OrdinalIgnoreCase)) return "highestAvailable";
        }
        catch
        {
        }

        return "unknown";
    }

    private static string CandidateId(string source, string path)
    {
        var basis = $"{source}|{path}".ToLowerInvariant();
        return "launcher:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(basis)))[..16].ToLowerInvariant();
    }

    private static string? SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        const string pimaxRoot = @"C:\Program Files\Pimax";
        return path.StartsWith(pimaxRoot, StringComparison.OrdinalIgnoreCase)
            ? "<pimax>" + path[pimaxRoot.Length..]
            : PimaxConnectivityRedactor.SanitizePath(path);
    }

    private static string SafeString(object? value) => value?.ToString() ?? "";

    private sealed record ShortcutEvidence(string ShortcutPath, string TargetPath, string Arguments, string WorkingDirectory);
    private sealed record InstalledApplicationEvidence(string DisplayName, string DisplayVersion, string Publisher);
}
