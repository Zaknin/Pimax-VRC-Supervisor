using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

internal sealed record PimaxProcessRunResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    string? Error);

internal interface IPimaxProcessRunner
{
    Task<PimaxProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class PimaxProcessRunner : IPimaxProcessRunner
{
    public async Task<PimaxProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
                var output = await outputTask;
                var error = await errorTask;
                return new PimaxProcessRunResult(process.ExitCode, output, error, TimedOut: false, Error: null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKillStartedProcess(process);
                return new PimaxProcessRunResult(null, "", "", TimedOut: true, Error: "Timed out.");
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new PimaxProcessRunResult(null, "", "", TimedOut: false, Error: ex.Message);
        }
    }

    private static void TryKillStartedProcess(Process process)
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
            // Best-effort cleanup for the read-only helper process we started.
        }
    }
}

internal static class PimaxConnectivityRedactor
{
    private static readonly Regex UserProfileRegex = new(@"\\Users\\[^\\]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex LongIdRegex = new(@"(?<!VID_)(?<!PID_)(?<!MI_)(?<!REV_)\b[A-Fa-f0-9]{8,}\b", RegexOptions.Compiled);

    public static string? SanitizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sanitized = value;
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            sanitized = sanitized.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        sanitized = Regex.Replace(
            sanitized,
            @"[A-Za-z]:\\Users\\[^\\]+",
            "%USERPROFILE%",
            RegexOptions.IgnoreCase);
        return UserProfileRegex.Replace(sanitized, @"\Users\<user>");
    }

    public static string SanitizeMessage(string value)
    {
        var sanitized = SanitizePath(value) ?? "";
        sanitized = EmailRegex.Replace(sanitized, "<email>");
        sanitized = Regex.Replace(sanitized, @"serial='[^']+'", "serial='<redacted>'", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"""(id|userId|uploadDataSessionId)""\s*:\s*""?[^,""}]+""?", @"""$1"":""<redacted>""", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"""(SNCode|DeviceId|HardwareSn)""\s*:\s*""[^""]+""", @"""$1"":""<redacted>""", RegexOptions.IgnoreCase);
        return sanitized;
    }

    public static string SanitizeInstanceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var sanitized = value.Trim();
        sanitized = Regex.Replace(
            sanitized,
            @"^(USB\\VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}(?:&MI_[0-9A-Fa-f]{2})?)\\.+$",
            "$1\\<id>",
            RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(
            sanitized,
            @"^(HID\\VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}(?:&MI_[0-9A-Fa-f]{2})?)\\.+$",
            "$1\\<id>",
            RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(
            sanitized,
            @"^(SWD\\MMDEVAPI)\\.+$",
            "$1\\<id>",
            RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(
            sanitized,
            @"^(ROOT\\PIMAXAIRLINK)\\.+$",
            "$1\\<id>",
            RegexOptions.IgnoreCase);

        return LongIdRegex.Replace(sanitized, "<id>");
    }
}

internal static class PimaxInstallationProbe
{
    public static PimaxInstallationObservation Collect()
    {
        var products = new List<PimaxInstalledProduct>();
        var warnings = new List<string>();
        var errors = new List<string>();

        if (!OperatingSystem.IsWindows())
        {
            return new PimaxInstallationObservation(
                PimaxProbeStatus.Unavailable,
                [],
                [],
                ["Pimax installation registry probes are Windows-only."],
                []);
        }

        AddProducts(products, errors, RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "hklm64Uninstall");
        AddProducts(products, errors, RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "hklm32Uninstall");
        AddProducts(products, errors, RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "hkcuUninstall");

        products = products
            .GroupBy(product => $"{product.DisplayName}|{product.DisplayVersion}|{product.InstallLocation}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(product => product.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roots = products
            .Select(product => product.InstallLocation)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax"))
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(PimaxConnectivityRedactor.SanitizePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();

        if (products.Count == 0 && roots.Length > 0)
        {
            warnings.Add("No Pimax uninstall registration was found; bounded Program Files fallback exists.");
            return new PimaxInstallationObservation(
                PimaxProbeStatus.Available,
                [new PimaxInstalledProduct("Pimax Program Files fallback", null, null, roots[0], "programFilesFallback")],
                roots,
                warnings.ToArray(),
                errors.ToArray());
        }

        return new PimaxInstallationObservation(
            products.Count > 0 ? PimaxProbeStatus.Available : PimaxProbeStatus.NotFound,
            products.ToArray(),
            roots,
            warnings.ToArray(),
            errors.ToArray());
    }

    private static void AddProducts(
        List<PimaxInstalledProduct> products,
        List<string> errors,
        RegistryHive hive,
        RegistryView view,
        string subKeyPath,
        string sourceKind)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
            if (uninstallKey is null)
            {
                return;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var displayName = ReadString(subKey, "DisplayName");
                var publisher = ReadString(subKey, "Publisher");
                var installLocation = ReadString(subKey, "InstallLocation");
                var haystack = $"{displayName}\n{publisher}\n{installLocation}";
                if (!IsPimaxProduct(haystack))
                {
                    continue;
                }

                products.Add(new PimaxInstalledProduct(
                    displayName ?? subKeyName,
                    ReadString(subKey, "DisplayVersion"),
                    publisher,
                    PimaxConnectivityRedactor.SanitizePath(installLocation),
                    sourceKind));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add($"{sourceKind}: access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            errors.Add($"{sourceKind}: {ex.Message}");
        }
    }

    private static string? ReadString(RegistryKey key, string name)
        => key.GetValue(name) as string;

    private static bool IsPimaxProduct(string value)
        => value.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PiService", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PVR", StringComparison.OrdinalIgnoreCase);
}

internal static class PimaxProcessProbe
{
    private static readonly string[] KnownRootFallbacks =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pimax")
    ];

    public static PimaxProcessObservation Collect(PimaxInstallationObservation installation)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var processes = new List<PimaxProcessInfo>();
        var roots = GetUnsanitizedRoots(installation).ToArray();

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    var processName = SafeGetProcessName(process);
                    if (processName.StartsWith("PimaxVrcSupervisor", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var executablePath = SafeGetExecutablePath(process);
                    if (!IsUnderAnyRoot(executablePath, roots))
                    {
                        continue;
                    }

                    var versionInfo = executablePath is null ? null : FileVersionInfo.GetVersionInfo(executablePath);
                    processes.Add(new PimaxProcessInfo(
                        processName,
                        SafeGetProcessId(process),
                        ParentProcessId: null,
                        PimaxConnectivityRedactor.SanitizePath(executablePath),
                        DetermineRole(processName, executablePath),
                        SafeGetStartTime(process),
                        versionInfo?.CompanyName,
                        versionInfo?.FileDescription,
                        versionInfo?.ProductName,
                        versionInfo?.FileVersion,
                        versionInfo?.ProductVersion));
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        if (processes.Count == 0 && roots.Length == 0)
        {
            warnings.Add("No confirmed Pimax installation root was available for process matching.");
        }

        return new PimaxProcessObservation(
            errors.Count > 0 ? PimaxProbeStatus.Error : PimaxProbeStatus.Available,
            processes.OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase).ThenBy(process => process.ProcessId).ToArray(),
            warnings.ToArray(),
            errors.ToArray());
    }

    private static IEnumerable<string> GetUnsanitizedRoots(PimaxInstallationObservation installation)
    {
        foreach (var product in installation.Products)
        {
            if (!string.IsNullOrWhiteSpace(product.InstallLocation))
            {
                var expanded = Environment.ExpandEnvironmentVariables(product.InstallLocation);
                if (!expanded.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase) && Directory.Exists(expanded))
                {
                    yield return Path.GetFullPath(expanded);
                }
            }
        }

        foreach (var root in KnownRootFallbacks)
        {
            if (Directory.Exists(root))
            {
                yield return Path.GetFullPath(root);
            }
        }
    }

    private static bool IsUnderAnyRoot(string? path, string[] roots)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        return roots.Any(root => fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string SafeGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }

    private static int SafeGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return 0;
        }
    }

    private static string? SafeGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? SafeGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }

    private static string DetermineRole(string processName, string? executablePath)
    {
        if (processName.Equals("PimaxClient", StringComparison.OrdinalIgnoreCase))
        {
            return executablePath?.Contains(@"\PimaxClient\pimaxui\", StringComparison.OrdinalIgnoreCase) == true
                ? "ClientMainOrElectronChild"
                : "UnknownPimaxComponent";
        }

        if (processName.Equals("PiService", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("PiServiceLauncher", StringComparison.OrdinalIgnoreCase))
        {
            return "ServiceProcess";
        }

        if (processName.Equals("DeviceSetting", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("PiPlayService", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("pi_server", StringComparison.OrdinalIgnoreCase))
        {
            return "RuntimeProcess";
        }

        if (processName.Equals("PVRHome", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("pi_overlay", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("PiPlatformService_64", StringComparison.OrdinalIgnoreCase))
        {
            return "OptionalComponent";
        }

        if (processName.Contains("crash", StringComparison.OrdinalIgnoreCase))
        {
            return "CrashHandlerChild";
        }

        return "UnknownPimaxComponent";
    }
}

internal static class PimaxServiceProbe
{
    private const string ServicesRegistryPath = @"SYSTEM\CurrentControlSet\Services";

    public static async Task<PimaxServiceObservation> CollectAsync(
        IPimaxProcessRunner processRunner,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var services = new List<PimaxServiceInfo>();
        var errors = new List<string>();

        if (!OperatingSystem.IsWindows())
        {
            return new PimaxServiceObservation(PimaxProbeStatus.Unavailable, [], ["Service probes are Windows-only."], []);
        }

        foreach (var service in EnumerateServiceRegistry(errors))
        {
            var query = await QueryServiceStateAsync(processRunner, service.Name, timeout, cancellationToken);
            services.Add(service with
            {
                State = query.State ?? service.State,
                ProcessId = query.ProcessId ?? service.ProcessId
            });
        }

        return new PimaxServiceObservation(
            errors.Count > 0 ? PimaxProbeStatus.Error : PimaxProbeStatus.Available,
            services.OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            [],
            errors.ToArray());
    }

    private static IEnumerable<PimaxServiceInfo> EnumerateServiceRegistry(List<string> errors)
    {
        using var servicesKey = Registry.LocalMachine.OpenSubKey(ServicesRegistryPath);
        if (servicesKey is null)
        {
            yield break;
        }

        foreach (var serviceName in servicesKey.GetSubKeyNames())
        {
            RegistryKey? serviceKey = null;
            try
            {
                serviceKey = servicesKey.OpenSubKey(serviceName);
                if (serviceKey is null)
                {
                    continue;
                }

                var displayName = serviceKey.GetValue("DisplayName") as string;
                var imagePath = serviceKey.GetValue("ImagePath") as string;
                var haystack = $"{serviceName}\n{displayName}\n{imagePath}";
                if (!IsPimaxServiceCandidate(haystack))
                {
                    continue;
                }

                yield return new PimaxServiceInfo(
                    serviceName,
                    displayName,
                    State: null,
                    StartMode: MapStartMode(serviceKey.GetValue("Start")),
                    ProcessId: null,
                    PimaxConnectivityRedactor.SanitizePath(imagePath),
                    DetermineRole(serviceName, displayName, imagePath));
            }
            finally
            {
                serviceKey?.Dispose();
            }
        }

        static bool IsPimaxServiceCandidate(string value)
            => value.Contains("PiServiceLauncher", StringComparison.OrdinalIgnoreCase)
                || value.Contains("PIMAXP3B", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Tobii", StringComparison.OrdinalIgnoreCase)
                || value.Contains(@"\Pimax\", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(string? State, int? ProcessId)> QueryServiceStateAsync(
        IPimaxProcessRunner processRunner,
        string serviceName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("sc.exe", $"queryex \"{serviceName}\"", timeout, cancellationToken);
        if (result.TimedOut || result.Error is not null || result.ExitCode is not 0)
        {
            return (null, null);
        }

        string? state = null;
        int? processId = null;
        foreach (var line in result.StandardOutput.SplitLines())
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(':', 2);
                if (parts.Length == 2)
                {
                    var stateParts = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    state = stateParts.Length >= 2 ? stateParts[1] : parts[1].Trim();
                }
            }
            else if (trimmed.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(':', 2);
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPid) && parsedPid > 0)
                {
                    processId = parsedPid;
                }
            }
        }

        return (state, processId);
    }

    private static string? MapStartMode(object? value)
        => value switch
        {
            0 => "boot",
            1 => "system",
            2 => "auto",
            3 => "manual",
            4 => "disabled",
            _ => null
        };

    private static string DetermineRole(string name, string? displayName, string? imagePath)
    {
        var value = $"{name}\n{displayName}\n{imagePath}";
        if (value.Contains("PiServiceLauncher", StringComparison.OrdinalIgnoreCase))
        {
            return "CoreServiceCandidate";
        }

        if (value.Contains("PIMAXP3B", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Tobii", StringComparison.OrdinalIgnoreCase))
        {
            return "OptionalEyeTrackingService";
        }

        return "UnknownPimaxService";
    }
}

internal static class PimaxDeviceProbe
{
    private static readonly string[] ObservedHealthyCrystalRoles =
    [
        "CrystalCompositeRoot",
        "CrystalCameraInterface",
        "CrystalHidInterface",
        "CrystalAudioInterface",
        "CrystalAudioEndpoint"
    ];

    public static async Task<PimaxDeviceObservation> CollectAsync(
        IPimaxProcessRunner processRunner,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("pnputil.exe", "/enum-devices /connected", timeout, cancellationToken);
        if (result.TimedOut)
        {
            return new PimaxDeviceObservation(PimaxProbeStatus.Error, [], [], false, false, false, [], [], ["pnputil timed out."]);
        }

        if (result.Error is not null)
        {
            return new PimaxDeviceObservation(PimaxProbeStatus.Error, [], [], false, false, false, [], [], [result.Error]);
        }

        if (result.ExitCode is not 0)
        {
            return new PimaxDeviceObservation(PimaxProbeStatus.Error, [], [], false, false, false, [], [], [$"pnputil exited with code {result.ExitCode}: {result.StandardError.Trim()}"]);
        }

        return ParsePnPDevices(result.StandardOutput);
    }

    public static PimaxDeviceObservation ParsePnPDevices(string output)
    {
        var relevant = new List<PimaxDeviceInfo>();
        var auxiliary = new List<PimaxDeviceInfo>();

        foreach (var block in SplitDeviceBlocks(output))
        {
            var device = ParseDeviceBlock(block);
            if (device is null)
            {
                continue;
            }

            if (IsWiredCrystalDevice(device))
            {
                relevant.Add(device);
            }
            else if (IsAuxiliaryPimaxDevice(device))
            {
                auxiliary.Add(device);
            }
        }

        var roles = relevant.Select(device => device.Role).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingRoles = relevant.Any(device => device.Role == "CrystalCompositeRoot")
            ? ObservedHealthyCrystalRoles.Except(roles, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        var hasRelevantProblem = relevant.Any(HasProblem);
        var composite = relevant.FirstOrDefault(device => device.Role == "CrystalCompositeRoot");
        var compositeHealthy = composite is not null && !HasProblem(composite) && missingRoles.Length == 0;

        return new PimaxDeviceObservation(
            PimaxProbeStatus.Available,
            relevant.OrderBy(device => device.Role, StringComparer.OrdinalIgnoreCase).ThenBy(device => device.SanitizedInstanceId, StringComparer.OrdinalIgnoreCase).ToArray(),
            auxiliary.OrderBy(device => device.Role, StringComparer.OrdinalIgnoreCase).ThenBy(device => device.SanitizedInstanceId, StringComparer.OrdinalIgnoreCase).ToArray(),
            composite is not null,
            compositeHealthy,
            hasRelevantProblem,
            missingRoles,
            [],
            []);
    }

    private static string[] SplitDeviceBlocks(string output)
        => Regex
            .Split(output.Trim(), @"(?:\r?\n){2,}")
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToArray();

    private static PimaxDeviceInfo? ParseDeviceBlock(string block)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in block.SplitLines())
        {
            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            fields[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        if (!fields.TryGetValue("Instance ID", out var instanceId) || string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        var friendlyName = fields.TryGetValue("Device Description", out var description) ? description : null;
        var className = fields.TryGetValue("Class Name", out var classValue) ? classValue : "";
        var status = fields.TryGetValue("Status", out var statusValue) ? statusValue : null;
        var driver = fields.TryGetValue("Driver Name", out var driverValue) ? driverValue : null;
        var role = DetermineRole(instanceId, className, friendlyName);
        var hardwareIds = ExtractHardwareIds(instanceId);

        return new PimaxDeviceInfo(
            role,
            className,
            friendlyName,
            PimaxConnectivityRedactor.SanitizeInstanceId(instanceId),
            hardwareIds,
            status,
            ProblemCode: null,
            DriverOrService: driver,
            ContainerId: null,
            ParentId: null);
    }

    private static string[] ExtractHardwareIds(string instanceId)
    {
        var match = Regex.Match(instanceId, @"VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}(?:&MI_[0-9A-Fa-f]{2})?", RegexOptions.IgnoreCase);
        return match.Success
            ? [match.Value.ToUpperInvariant()]
            : [];
    }

    private static string DetermineRole(string instanceId, string className, string? friendlyName)
    {
        var value = $"{instanceId}\n{className}\n{friendlyName}";
        if (value.Contains(@"ROOT\PimaxAirLink", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Pimax AirLink", StringComparison.OrdinalIgnoreCase))
        {
            return "AuxiliaryAirLink";
        }

        if (value.Contains("NVIDIA High Definition Audio", StringComparison.OrdinalIgnoreCase))
        {
            return "AuxiliaryDisplayAudioEndpoint";
        }

        if (friendlyName?.Contains("Pimax Streaming Microphone", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "CrystalAudioEndpoint";
        }

        if (!value.Contains("VID_34A4&PID_0012", StringComparison.OrdinalIgnoreCase))
        {
            return value.Contains("Pimax", StringComparison.OrdinalIgnoreCase)
                ? "OtherPimaxDevice"
                : "Unrelated";
        }

        if (Regex.IsMatch(instanceId, @"^USB\\VID_34A4&PID_0012\\", RegexOptions.IgnoreCase))
        {
            return "CrystalCompositeRoot";
        }

        if (value.Contains("&MI_00", StringComparison.OrdinalIgnoreCase) || className.Equals("Camera", StringComparison.OrdinalIgnoreCase))
        {
            return "CrystalCameraInterface";
        }

        if (value.Contains("&MI_02", StringComparison.OrdinalIgnoreCase) || className.Equals("HIDClass", StringComparison.OrdinalIgnoreCase))
        {
            return "CrystalHidInterface";
        }

        if (value.Contains("&MI_03", StringComparison.OrdinalIgnoreCase) || friendlyName?.Contains("AC Interface", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "CrystalAudioInterface";
        }

        return "OtherPimaxDevice";
    }

    private static bool IsWiredCrystalDevice(PimaxDeviceInfo device)
        => device.Role.StartsWith("Crystal", StringComparison.OrdinalIgnoreCase)
            || device.Role == "OtherPimaxDevice";

    private static bool IsAuxiliaryPimaxDevice(PimaxDeviceInfo device)
        => device.Role.StartsWith("Auxiliary", StringComparison.OrdinalIgnoreCase);

    private static bool HasProblem(PimaxDeviceInfo device)
        => device.ProblemCode is > 0
            || (!string.IsNullOrWhiteSpace(device.Status)
                && !device.Status.Equals("Started", StringComparison.OrdinalIgnoreCase)
                && !device.Status.Equals("OK", StringComparison.OrdinalIgnoreCase));
}

internal static class PimaxRuntimeEvidenceProbe
{
    private const int MaxFilesPerSource = 3;
    private const int MaxTailBytes = 256 * 1024;
    private const int MaxEventsPerSource = 25;
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(5);

    public static PimaxRuntimeEvidenceObservation Collect(SupervisorConfig config, DateTimeOffset collectedAt)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var events = new List<PimaxRuntimeEvidenceEvent>();
        var windowStartedAt = collectedAt - FreshnessWindow;

        AddEventsFromFolder(
            "PiService",
            Environment.ExpandEnvironmentVariables(config.PimaxServiceLogDirectory),
            "PiService__*.log",
            ParsePiServiceLine,
            collectedAt,
            events,
            warnings,
            errors);
        AddEventsFromFolder(
            "PimaxClient",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PimaxClient", "logs"),
            "*.log*",
            ParsePimaxClientLine,
            collectedAt,
            events,
            warnings,
            errors);

        var orderedEvents = events
            .OrderByDescending(ev => ev.EventTimestamp ?? ev.SourceLastWriteTime)
            .Take(MaxEventsPerSource * 2)
            .ToArray();
        var freshConnected = orderedEvents
            .Where(ev => ev.IsFresh && ev.State == PimaxRuntimeEvidenceState.Connected)
            .OrderByDescending(ev => ev.EventTimestamp)
            .FirstOrDefault();
        var freshDisconnected = orderedEvents
            .Where(ev => ev.IsFresh && ev.State is PimaxRuntimeEvidenceState.DisconnectedOrError or PimaxRuntimeEvidenceState.DisplayLost)
            .OrderByDescending(ev => ev.EventTimestamp)
            .FirstOrDefault();

        return new PimaxRuntimeEvidenceObservation(
            errors.Count > 0 && orderedEvents.Length == 0 ? PimaxProbeStatus.Error : orderedEvents.Length > 0 ? PimaxProbeStatus.Available : PimaxProbeStatus.Inconclusive,
            windowStartedAt,
            (int)FreshnessWindow.TotalSeconds,
            orderedEvents,
            freshConnected,
            freshDisconnected,
            warnings.ToArray(),
            errors.ToArray());
    }

    private static void AddEventsFromFolder(
        string source,
        string folder,
        string searchPattern,
        Func<string, DateTimeOffset?, (string State, string Message)?> parser,
        DateTimeOffset collectedAt,
        List<PimaxRuntimeEvidenceEvent> events,
        List<string> warnings,
        List<string> errors)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                warnings.Add($"{source} log folder was not found.");
                return;
            }

            var sourceEvents = new List<PimaxRuntimeEvidenceEvent>();
            foreach (var file in Directory.EnumerateFiles(folder, searchPattern)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(MaxFilesPerSource))
            {
                var lines = ReadTailLines(file.FullName, MaxTailBytes);
                var ageReference = DateTimeOffset.Now;
                sourceEvents.AddRange(ParseLines(source, file, lines, parser, ageReference));
            }
            events.AddRange(sourceEvents
                .OrderByDescending(ev => ev.EventTimestamp ?? ev.SourceLastWriteTime)
                .Take(MaxEventsPerSource));
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add($"{source}: access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            errors.Add($"{source}: {ex.Message}");
        }
    }

    private static IEnumerable<PimaxRuntimeEvidenceEvent> ParseLines(
        string source,
        FileInfo file,
        string[] lines,
        Func<string, DateTimeOffset?, (string State, string Message)?> parser,
        DateTimeOffset ageReference)
    {
        DateTimeOffset? currentTimestamp = null;
        foreach (var line in lines)
        {
            var parsedTimestamp = TryParseLogTimestamp(line);
            if (parsedTimestamp is not null)
            {
                currentTimestamp = parsedTimestamp;
            }

            var parsed = parser(line, currentTimestamp);
            if (parsed is null)
            {
                continue;
            }

            var rawAge = currentTimestamp is null ? (double?)null : (ageReference - currentTimestamp.Value).TotalSeconds;
            var eventAge = rawAge is null ? (double?)null : Math.Max(0, rawAge.Value);
            yield return new PimaxRuntimeEvidenceEvent(
                source,
                parsed.Value.State,
                currentTimestamp,
                file.LastWriteTimeUtc,
                eventAge,
                currentTimestamp is not null && ageReference - currentTimestamp.Value <= FreshnessWindow && currentTimestamp.Value <= ageReference.AddSeconds(30),
                currentTimestamp is null ? "unavailable" : "parsed",
                PimaxConnectivityRedactor.SanitizeMessage(parsed.Value.Message));
        }
    }

    private static (string State, string Message)? ParsePiServiceLine(string line, DateTimeOffset? timestamp)
    {
        if (line.Contains("connected hmd name:", StringComparison.OrdinalIgnoreCase))
        {
            return (PimaxRuntimeEvidenceState.Connected, line);
        }

        if (line.Contains("removed hid device", StringComparison.OrdinalIgnoreCase))
        {
            return (PimaxRuntimeEvidenceState.HidRemoved, line);
        }

        if (line.Contains("added hid device", StringComparison.OrdinalIgnoreCase))
        {
            return (PimaxRuntimeEvidenceState.HidAdded, line);
        }

        if (line.Contains("HmdDisplayLost", StringComparison.OrdinalIgnoreCase))
        {
            return (PimaxRuntimeEvidenceState.DisplayLost, line);
        }

        if (line.Contains("HmdDisplayRestore", StringComparison.OrdinalIgnoreCase))
        {
            return (PimaxRuntimeEvidenceState.DisplayRestored, line);
        }

        return null;
    }

    private static (string State, string Message)? ParsePimaxClientLine(string line, DateTimeOffset? timestamp)
    {
        if (line.Contains("HMD_hmdName change: Pimax Crystal", StringComparison.OrdinalIgnoreCase)
            || line.Contains("HMD_hmdName: 'Pimax Crystal'", StringComparison.OrdinalIgnoreCase))
        {
            return (PimaxRuntimeEvidenceState.Connected, line);
        }

        if (line.Contains("HMD_hmdName change: null", StringComparison.OrdinalIgnoreCase)
            || line.Contains("HMD_errorCode change: 10600", StringComparison.OrdinalIgnoreCase)
            || line.Contains("runtimeErrorCode: 10600", StringComparison.OrdinalIgnoreCase))
        {
            return (PimaxRuntimeEvidenceState.DisconnectedOrError, line);
        }

        if (line.Contains("HMDData Changed", StringComparison.OrdinalIgnoreCase))
        {
            return (PimaxRuntimeEvidenceState.Inconclusive, line);
        }

        return null;
    }

    private static DateTimeOffset? TryParseLogTimestamp(string line)
    {
        var pimaxClientMatch = Regex.Match(line, @"^\[(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\]");
        if (pimaxClientMatch.Success && TryParseTimestamp(pimaxClientMatch.Groups["timestamp"].Value, out var pimaxClientTimestamp))
        {
            return pimaxClientTimestamp;
        }

        if (line.Length >= 23 && TryParseTimestamp(line[..23], out var serviceTimestamp))
        {
            return serviceTimestamp;
        }

        return null;
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
    {
        if (DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed))
        {
            timestamp = parsed;
            return true;
        }

        timestamp = default;
        return false;
    }

    private static string[] ReadTailLines(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var bytesToRead = (int)Math.Min(maxBytes, length);
        stream.Seek(-bytesToRead, SeekOrigin.End);
        var buffer = new byte[bytesToRead];
        var read = stream.Read(buffer, 0, bytesToRead);
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        return text.SplitLines();
    }
}

internal static class PimaxSteamVrDriverProbe
{
    public static PimaxSteamVrDriverObservation Collect()
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var registeredPaths = new List<string>();
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openvr", "openvrpaths.vrpath"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "config", "openvrpaths.vrpath"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "config", "openvrpaths.vrpath")
        };

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                var text = File.ReadAllText(path);
                foreach (Match match in Regex.Matches(text, @"""(?<path>[^""]*Pimax[^""]*SteamVRSupport[^""]*)""", RegexOptions.IgnoreCase))
                {
                    registeredPaths.Add(match.Groups["path"].Value.Replace(@"\\", @"\"));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add($"{PimaxConnectivityRedactor.SanitizePath(path)}: access denied: {ex.Message}");
            }
            catch (Exception ex)
            {
                errors.Add($"{PimaxConnectivityRedactor.SanitizePath(path)}: {ex.Message}");
            }
        }

        var distinctPaths = registeredPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(PimaxConnectivityRedactor.SanitizePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();
        var manifestFound = registeredPaths
            .Select(path => Path.Combine(path, "driver.vrdrivermanifest"))
            .Any(File.Exists);

        var status = errors.Count > 0 && distinctPaths.Length == 0
            ? PimaxProbeStatus.Error
            : distinctPaths.Length > 0
                ? PimaxProbeStatus.Available
                : PimaxProbeStatus.NotFound;

        if (distinctPaths.Length == 0)
        {
            warnings.Add("Pimax SteamVR driver registration was not found. SteamVR is not required for Pimax Client connectivity.");
        }

        return new PimaxSteamVrDriverObservation(status, distinctPaths, manifestFound, warnings.ToArray(), errors.ToArray());
    }
}

internal static class StringLineExtensions
{
    public static string[] SplitLines(this string value)
        => value.Split(["\r\n", "\n"], StringSplitOptions.None);
}
