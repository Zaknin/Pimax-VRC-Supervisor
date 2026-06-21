internal static class PimaxSoftwareGroupRole
{
    public const string LauncherRootProcess = "launcherRootProcess";
    public const string PimaxPlayUiProcess = "pimaxPlayUiProcess";
    public const string RuntimeProcess = "runtimeProcess";
    public const string ServiceOwnedProcess = "serviceOwnedProcess";
    public const string HelperProcess = "helperProcess";
    public const string DriverHost = "driverHost";
    public const string OptionalComponent = "optionalComponent";
    public const string UnknownMember = "unknownMember";
}

internal static class PimaxSoftwareGroupState
{
    public const string Complete = "complete";
    public const string Partial = "partial";
    public const string Unavailable = "unavailable";
    public const string Starting = "starting";
    public const string Stopping = "stopping";
    public const string Conflicting = "conflicting";
    public const string Unknown = "unknown";
}

internal static class PimaxEvidenceFreshness
{
    public const string Current = "current";
    public const string Stale = "stale";
    public const string Unowned = "unowned";
    public const string Contradicted = "contradicted";
    public const string Unknown = "unknown";
}

internal sealed record PimaxSoftwareGroupMember(
    string ProcessName,
    string Role,
    string? SanitizedPath,
    string SignerSummary,
    string SessionEvidence,
    string LifecycleCoupling,
    bool RequiredForRegistration,
    bool ProhibitedForMutation);

internal sealed record PimaxSoftwareGroupRecipe(
    bool Complete,
    string Confidence,
    string[] MissingRequirements,
    string HumanReadableSummary);

internal sealed record PimaxSoftwareGroupSnapshot(
    DateTimeOffset CapturedAt,
    string AssessmentOperationId,
    string State,
    string Freshness,
    PimaxSoftwareGroupMember[] Members,
    string[] RequiredMissingRoles,
    string[] Warnings,
    PimaxSoftwareGroupRecipe RestartRecipe,
    string HumanReadableSummary)
{
    public bool HasRole(string role)
        => Members.Any(member => string.Equals(member.Role, role, StringComparison.OrdinalIgnoreCase));
}

internal static class PimaxSoftwareGroupModel
{
    private static readonly string[] RequiredRoles =
    [
        PimaxSoftwareGroupRole.PimaxPlayUiProcess,
        PimaxSoftwareGroupRole.RuntimeProcess,
        PimaxSoftwareGroupRole.ServiceOwnedProcess
    ];

    public static PimaxSoftwareGroupSnapshot FromConnectivity(PimaxConnectivitySnapshot? connectivity, DateTimeOffset capturedAt, string operationId)
    {
        if (connectivity is null)
        {
            return Unknown(capturedAt, operationId);
        }

        var members = connectivity.Processes.Processes
            .Select(ToMember)
            .Concat(connectivity.Services.Services.Select(ToServiceMember))
            .Where(member => member.Role != PimaxSoftwareGroupRole.UnknownMember || IsTrustedPimaxPath(member.SanitizedPath))
            .DistinctBy(member => $"{member.ProcessName}|{member.Role}|{member.SanitizedPath}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(member => member.Role, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Build(capturedAt, operationId, members);
    }

    public static PimaxSoftwareGroupSnapshot FromMembers(DateTimeOffset capturedAt, string operationId, params PimaxSoftwareGroupMember[] members)
        => Build(capturedAt, operationId, members);

    public static PimaxSoftwareGroupSnapshot Unknown(DateTimeOffset capturedAt, string operationId)
        => new(
            capturedAt,
            operationId,
            PimaxSoftwareGroupState.Unknown,
            PimaxEvidenceFreshness.Unknown,
            [],
            RequiredRoles,
            ["Pimax software process-group evidence is unavailable."],
            IncompleteRecipe(),
            "Pimax software process-group state is unknown.");

    private static PimaxSoftwareGroupSnapshot Build(DateTimeOffset capturedAt, string operationId, PimaxSoftwareGroupMember[] members)
    {
        var requiredMissing = RequiredRoles.Where(role => !members.Any(member => member.RequiredForRegistration && string.Equals(member.Role, role, StringComparison.OrdinalIgnoreCase))).ToArray();
        var warnings = new List<string>();
        if (members.Any(member => member.Role == PimaxSoftwareGroupRole.UnknownMember))
        {
            warnings.Add("Unknown Pimax-root process-group member was observed.");
        }

        var state =
            members.Any(member => member.Role == PimaxSoftwareGroupRole.UnknownMember) ? PimaxSoftwareGroupState.Conflicting :
            members.Length == 0 || !members.Any(member => member.RequiredForRegistration) ? PimaxSoftwareGroupState.Unavailable :
            requiredMissing.Length == 0 ? PimaxSoftwareGroupState.Complete :
            PimaxSoftwareGroupState.Partial;
        var freshness = state switch
        {
            PimaxSoftwareGroupState.Complete => PimaxEvidenceFreshness.Current,
            PimaxSoftwareGroupState.Unavailable => PimaxEvidenceFreshness.Unowned,
            PimaxSoftwareGroupState.Partial => PimaxEvidenceFreshness.Contradicted,
            PimaxSoftwareGroupState.Conflicting => PimaxEvidenceFreshness.Contradicted,
            _ => PimaxEvidenceFreshness.Unknown
        };
        var summary = state switch
        {
            PimaxSoftwareGroupState.Complete => "Pimax Play and required runtime/software owner evidence are currently present.",
            PimaxSoftwareGroupState.Unavailable => PimaxComponentHealthMessages.SoftwareStackUnavailable,
            PimaxSoftwareGroupState.Partial => "The Pimax software stack is partial; group-level recovery semantics are required before mutation.",
            PimaxSoftwareGroupState.Conflicting => "The Pimax software stack contains unknown or conflicting members; mutation is prohibited.",
            _ => "Pimax software process-group state is unknown."
        };
        return new PimaxSoftwareGroupSnapshot(
            capturedAt,
            operationId,
            state,
            freshness,
            members,
            requiredMissing,
            warnings.ToArray(),
            IncompleteRecipe(),
            summary);
    }

    private static PimaxSoftwareGroupMember ToMember(PimaxProcessInfo process)
    {
        var role = RoleForProcess(process);
        return new PimaxSoftwareGroupMember(
            process.ProcessName,
            role,
            PimaxConnectivityRedactor.SanitizePath(process.ExecutablePath),
            IsPimaxPublisher(process.CompanyName) ? "Pimax publisher metadata present." : "Publisher metadata unavailable or not Pimax.",
            process.StartTime is null ? "process session unavailable" : "current process session observed",
            CouplingForRole(role),
            role is PimaxSoftwareGroupRole.PimaxPlayUiProcess or PimaxSoftwareGroupRole.RuntimeProcess,
            role is PimaxSoftwareGroupRole.DriverHost or PimaxSoftwareGroupRole.UnknownMember);
    }

    private static PimaxSoftwareGroupMember ToServiceMember(PimaxServiceInfo service)
    {
        var role = service.Role.Contains("Core", StringComparison.OrdinalIgnoreCase)
            || service.Name.Contains("PiService", StringComparison.OrdinalIgnoreCase)
            || service.Name.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
                ? PimaxSoftwareGroupRole.ServiceOwnedProcess
                : PimaxSoftwareGroupRole.OptionalComponent;
        return new PimaxSoftwareGroupMember(
            service.Name,
            role,
            PimaxConnectivityRedactor.SanitizePath(service.BinaryPath),
            "Service metadata observed.",
            string.Equals(service.State, "RUNNING", StringComparison.OrdinalIgnoreCase) ? "service running in current snapshot" : "service not running in current snapshot",
            CouplingForRole(role),
            role == PimaxSoftwareGroupRole.ServiceOwnedProcess,
            false);
    }

    private static string RoleForProcess(PimaxProcessInfo process)
    {
        if (process.ProcessName.Equals("PimaxClient", StringComparison.OrdinalIgnoreCase))
        {
            return PimaxSoftwareGroupRole.PimaxPlayUiProcess;
        }

        if (process.ProcessName.Equals("PiService", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("PiPlayService", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("DeviceSetting", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("pi_server", StringComparison.OrdinalIgnoreCase))
        {
            return process.ProcessName.Equals("PiService", StringComparison.OrdinalIgnoreCase)
                ? PimaxSoftwareGroupRole.ServiceOwnedProcess
                : PimaxSoftwareGroupRole.RuntimeProcess;
        }

        if (process.ProcessName.Equals("PVRHome", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("pi_overlay", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("PiPlatformService_64", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
        {
            return PimaxSoftwareGroupRole.OptionalComponent;
        }

        return process.Role.Contains("Runtime", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.RuntimeProcess :
            process.Role.Contains("Service", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.ServiceOwnedProcess :
            process.Role.Contains("Optional", StringComparison.OrdinalIgnoreCase) ? PimaxSoftwareGroupRole.OptionalComponent :
            PimaxSoftwareGroupRole.UnknownMember;
    }

    private static string CouplingForRole(string role) => role switch
    {
        PimaxSoftwareGroupRole.PimaxPlayUiProcess => "Coupled: Phase 28D2-BV observed closing PimaxClient terminate required runtime members.",
        PimaxSoftwareGroupRole.RuntimeProcess => "Coupled runtime member; standalone restart is not approved.",
        PimaxSoftwareGroupRole.ServiceOwnedProcess => "Service-owned runtime member; service restart is not approved.",
        PimaxSoftwareGroupRole.OptionalComponent => "Optional lifecycle member; observe-only.",
        _ => "Lifecycle coupling unknown; observe-only."
    };

    private static bool IsTrustedPimaxPath(string? path)
        => path?.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase) == true
            || path?.StartsWith("<pimax>", StringComparison.OrdinalIgnoreCase) == true
            || path?.StartsWith("<drive>:\\Program Files\\Pimax", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsPimaxPublisher(string? publisher)
        => publisher?.Contains("Pimax", StringComparison.OrdinalIgnoreCase) == true;

    public static PimaxSoftwareGroupRecipe IncompleteRecipe()
        => new(
            false,
            "insufficient",
            [
                "complete expected member set understood",
                "root launch method understood",
                "safe start command known",
                "process readiness criteria known",
                "runtime ownership understood",
                "shutdown semantics understood",
                "post-start health verification approved"
            ],
            "A complete safe Pimax Play/runtime group restart recipe has not been approved.");

    public static PimaxSoftwareGroupRecipe ReadyForControlledValidationRecipe()
        => new(
            false,
            PimaxProcessGroupLaunchRecipeState.ReadyForShellActivationValidation,
            [
                "one formal observer-backed Start Menu launch comparison",
                "post-launch process-group formation proof",
                "post-launch readiness and registration verification",
                "B2C Explorer-rooted creator-chain proof",
                "B2D Shell adapter programmatic-equivalent validation before backend execution"
            ],
            "Direct PimaxClient.exe process creation is rejected. Normal Start Menu Shell activation is the confirmed manual mechanism, but the recipe is not executable until the B2D adapter is validated once programmatically.");
}
