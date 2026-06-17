using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class WindowsPimaxRuntimeServiceController : IPimaxRuntimeServiceController
{
    private const string TargetServiceName = "PiServiceLauncher";
    private const string TargetExecutableName = "PiServiceLauncher.exe";

    public async Task<PimaxRuntimeServiceDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PimaxRuntimeServiceDiscoveryResult(null, [], [], ["Runtime service discovery is Windows-only."], PimaxRecoveryFailureCategory.TargetNotFound);
        }

        var script = """
$ErrorActionPreference = 'Stop'
function Resolve-ServiceExecutablePath([string]$pathName) {
    if ([string]::IsNullOrWhiteSpace($pathName)) { return $null }
    $trimmed = $pathName.Trim()
    if ($trimmed.StartsWith('"')) {
        $end = $trimmed.IndexOf('"', 1)
        if ($end -gt 1) { return $trimmed.Substring(1, $end - 1) }
    }
    $match = [regex]::Match($trimmed, '^(?<path>[A-Za-z]:\\.*?\.exe)(\s|$)')
    if ($match.Success) { return $match.Groups['path'].Value }
    return $trimmed
}
$services = Get-CimInstance Win32_Service | Where-Object {
    $_.Name -eq 'PiServiceLauncher' -or
    ($_.PathName -like '*Pimax*Runtime*PiServiceLauncher.exe*')
}
$rows = foreach ($svc in $services) {
    $exe = Resolve-ServiceExecutablePath $svc.PathName
    $file = $null
    $hash = $null
    $signatureStatus = $null
    $signatureSigner = $null
    if ($exe -and (Test-Path -LiteralPath $exe)) {
        $file = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
        $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        $signature = Get-AuthenticodeSignature -LiteralPath $exe
        $signatureStatus = $signature.Status.ToString()
        if ($signature.SignerCertificate) { $signatureSigner = $signature.SignerCertificate.Subject }
    }
    $service = Get-Service -Name $svc.Name -ErrorAction SilentlyContinue
    [pscustomobject]@{
        serviceName = $svc.Name
        displayName = $svc.DisplayName
        state = $svc.State
        startMode = $svc.StartMode
        processId = [int]$svc.ProcessId
        executablePath = $exe
        commandLine = $svc.PathName
        serviceType = $svc.ServiceType
        startName = $svc.StartName
        productName = if ($file) { $file.ProductName } else { $null }
        companyName = if ($file) { $file.CompanyName } else { $null }
        fileDescription = if ($file) { $file.FileDescription } else { $null }
        productVersion = if ($file) { $file.ProductVersion } else { $null }
        executableSha256 = $hash
        signatureStatus = $signatureStatus
        signatureSigner = $signatureSigner
        dependencies = if ($service) { @($service.ServicesDependedOn | ForEach-Object { $_.Name }) } else { @() }
        dependentServices = if ($service) { @($service.DependentServices | Where-Object { $_.Status -ne 'Stopped' } | ForEach-Object { $_.Name }) } else { @() }
    }
}
@($rows) | ConvertTo-Json -Depth 6
""";

        var output = await RunPowerShellAsync(script, cancellationToken);
        ServiceRow[] rows;
        try
        {
            using var document = JsonDocument.Parse(output);
            rows = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Select(ParseServiceRow).ToArray()
                : [ParseServiceRow(document.RootElement)];
        }
        catch (JsonException)
        {
            rows = [];
        }

        var candidates = rows.Select(ToDescriptor).ToArray();
        var warnings = new List<string>();
        var errors = new List<string>();
        var verified = candidates
            .Where(IsVerifiedRuntimeService)
            .ToArray();

        foreach (var candidate in candidates)
        {
            warnings.AddRange(candidate.VerificationWarnings);
        }

        if (verified.Length == 0)
        {
            errors.Add("No exact Pimax runtime service target matched PiServiceLauncher identity checks.");
            return new PimaxRuntimeServiceDiscoveryResult(null, candidates, warnings.ToArray(), errors.ToArray(), PimaxRecoveryFailureCategory.TargetNotFound);
        }

        if (verified.Length > 1)
        {
            errors.Add("More than one Pimax runtime service target matched; refusing ambiguous service restart.");
            return new PimaxRuntimeServiceDiscoveryResult(null, candidates, warnings.ToArray(), errors.ToArray(), PimaxRecoveryFailureCategory.TargetAmbiguous);
        }

        return new PimaxRuntimeServiceDiscoveryResult(verified[0], candidates, warnings.ToArray(), [], null);
    }

    public async Task<PimaxPrivilegedServiceResult> RestartWithUacHelperAsync(
        PimaxPrivilegedServiceRequest request,
        string requestSha256,
        string confirmationBinding,
        CancellationToken cancellationToken)
    {
        var evidenceDirectory = request.EvidenceDirectory;
        Directory.CreateDirectory(evidenceDirectory);
        var helperDirectory = Path.Combine(evidenceDirectory, "scripts");
        Directory.CreateDirectory(helperDirectory);
        var requestPath = Path.Combine(evidenceDirectory, "phase-28c2-privileged-service-request.json");
        var helperPath = Path.Combine(helperDirectory, "invoke-pimax-runtime-service-restart-elevated.ps1");
        await File.WriteAllTextAsync(helperPath, HelperScript, new UTF8Encoding(false), cancellationToken);

        if (File.Exists(request.OutputResultPath))
        {
            File.Delete(request.OutputResultPath);
        }

        using var process = new Process
        {
            StartInfo = BuildHelperStartInfo(helperPath, requestPath, request.OutputResultPath, requestSha256, confirmationBinding)
        };

        if (!process.Start())
        {
            return HelperLaunchFailure(request, requestSha256, "Windows did not start the elevated helper process.");
        }

        await process.WaitForExitAsync(cancellationToken);
        if (!File.Exists(request.OutputResultPath))
        {
            return HelperLaunchFailure(request, requestSha256, "Elevated helper did not produce a result file. UAC may have been canceled.");
        }

        var json = await File.ReadAllTextAsync(request.OutputResultPath, cancellationToken);
        return JsonSerializer.Deserialize<PimaxPrivilegedServiceResult>(json, PimaxRecoveryExperimentJson.Options)
            ?? HelperLaunchFailure(request, requestSha256, "Elevated helper result file was empty.");
    }

    internal static ProcessStartInfo BuildHelperStartInfo(
        string helperPath,
        string requestPath,
        string resultPath,
        string requestSha256,
        string confirmationBinding)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add("-RequestPath");
        startInfo.ArgumentList.Add(requestPath);
        startInfo.ArgumentList.Add("-ResultPath");
        startInfo.ArgumentList.Add(resultPath);
        startInfo.ArgumentList.Add("-ExpectedRequestSha256");
        startInfo.ArgumentList.Add(requestSha256);
        startInfo.ArgumentList.Add("-ConfirmationBinding");
        startInfo.ArgumentList.Add(confirmationBinding);
        return startInfo;
    }

    private static PimaxRuntimeServiceDescriptor ToDescriptor(ServiceRow row)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();
        if (string.Equals(row.ServiceName, TargetServiceName, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Service name matches PiServiceLauncher.");
        }

        if (!string.IsNullOrWhiteSpace(row.ExecutablePath)
            && string.Equals(Path.GetFileName(row.ExecutablePath), TargetExecutableName, StringComparison.OrdinalIgnoreCase)
            && row.ExecutablePath.Contains(Path.Combine("Pimax", "Runtime"), StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Executable path matches Pimax Runtime PiServiceLauncher.");
        }

        if (string.Equals(row.SignatureStatus, "Valid", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Executable Authenticode signature is valid.");
        }
        else
        {
            warnings.Add($"Executable signature status is {row.SignatureStatus ?? "unknown"}.");
        }

        if (string.IsNullOrWhiteSpace(row.ProductName) && string.IsNullOrWhiteSpace(row.CompanyName) && string.IsNullOrWhiteSpace(row.FileDescription))
        {
            warnings.Add("Executable version metadata does not expose product/company/description fields; identity relies on service name, path, hash, and signature.");
        }

        return new PimaxRuntimeServiceDescriptor(
            row.ServiceName ?? "",
            row.DisplayName ?? "",
            row.State ?? "",
            row.StartMode ?? "",
            row.ProcessId,
            row.ExecutablePath ?? "",
            row.CommandLine,
            row.ServiceType ?? "",
            row.StartName ?? "",
            row.ProductName,
            row.CompanyName,
            row.FileDescription,
            row.ProductVersion,
            row.ExecutableSha256,
            row.SignatureStatus,
            row.SignatureSigner,
            row.Dependencies ?? [],
            row.DependentServices ?? [],
            reasons.ToArray(),
            warnings.ToArray());
    }

    private static bool IsVerifiedRuntimeService(PimaxRuntimeServiceDescriptor candidate)
        => string.Equals(candidate.ServiceName, TargetServiceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(candidate.ExecutablePath), TargetExecutableName, StringComparison.OrdinalIgnoreCase)
            && candidate.ExecutablePath.Contains(Path.Combine("Pimax", "Runtime"), StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.ServiceType, "Own Process", StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.SignatureStatus, "Valid", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(script);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell service discovery failed with exit code {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }

    private static PimaxPrivilegedServiceResult HelperLaunchFailure(
        PimaxPrivilegedServiceRequest request,
        string requestSha256,
        string message)
    {
        var now = DateTimeOffset.Now;
        return new PimaxPrivilegedServiceResult(
            PimaxRecoveryExperimentSchema.Version,
            request.ExperimentId,
            requestSha256,
            false,
            Environment.ProcessId,
            now,
            now,
            false,
            request.ServiceName,
            null,
            null,
            null,
            null,
            new PimaxRecoveryOperationResult(false, false, message, []),
            null,
            null,
            new PimaxRecoveryOperationResult(false, false, message, []),
            null,
            false,
            false,
            [],
            false,
            null,
            0,
            false,
            "uacCancelled",
            [],
            [message]);
    }

    private static ServiceRow ParseServiceRow(JsonElement element)
        => new(
            StringProperty(element, "serviceName"),
            StringProperty(element, "displayName"),
            StringProperty(element, "state"),
            StringProperty(element, "startMode"),
            IntProperty(element, "processId"),
            StringProperty(element, "executablePath"),
            StringProperty(element, "commandLine"),
            StringProperty(element, "serviceType"),
            StringProperty(element, "startName"),
            StringProperty(element, "productName"),
            StringProperty(element, "companyName"),
            StringProperty(element, "fileDescription"),
            StringProperty(element, "productVersion"),
            StringProperty(element, "executableSha256"),
            StringProperty(element, "signatureStatus"),
            StringProperty(element, "signatureSigner"),
            StringArrayProperty(element, "dependencies"),
            StringArrayProperty(element, "dependentServices"));

    private static string? StringProperty(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    private static int IntProperty(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;

    private static string[] StringArrayProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(item => item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return value.EnumerateObject().Any()
                ? [value.ToString()]
                : [];
        }

        var single = value.ToString();
        return string.IsNullOrWhiteSpace(single) ? [] : [single];
    }

    private sealed record ServiceRow(
        string? ServiceName,
        string? DisplayName,
        string? State,
        string? StartMode,
        int ProcessId,
        string? ExecutablePath,
        string? CommandLine,
        string? ServiceType,
        string? StartName,
        string? ProductName,
        string? CompanyName,
        string? FileDescription,
        string? ProductVersion,
        string? ExecutableSha256,
        string? SignatureStatus,
        string? SignatureSigner,
        string[]? Dependencies,
        string[]? DependentServices);

    private const string HelperScript = """
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RequestPath,
    [Parameter(Mandatory=$true)][string]$ResultPath,
    [Parameter(Mandatory=$true)][string]$ExpectedRequestSha256,
    [Parameter(Mandatory=$true)][string]$ConfirmationBinding
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Result([hashtable]$result) {
    $directory = Split-Path -Parent -LiteralPath $ResultPath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $tmp = Join-Path $directory ([IO.Path]::GetFileName($ResultPath) + '.tmp')
    $json = $result | ConvertTo-Json -Depth 8 -Compress
    [IO.File]::WriteAllText($tmp, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tmp -Destination $ResultPath -Force
}

function Resolve-ServiceExecutablePath([string]$pathName) {
    if ([string]::IsNullOrWhiteSpace($pathName)) { return $null }
    $trimmed = $pathName.Trim()
    if ($trimmed.StartsWith('"')) {
        $end = $trimmed.IndexOf('"', 1)
        if ($end -gt 1) { return $trimmed.Substring(1, $end - 1) }
    }
    $match = [regex]::Match($trimmed, '^(?<path>[A-Za-z]:\\.*?\.exe)(\s|$)')
    if ($match.Success) { return $match.Groups['path'].Value }
    return $trimmed
}

function New-Operation([bool]$attempted, [bool]$success, [string]$message, [int[]]$processIds) {
    return @{ attempted = $attempted; success = $success; message = $message; processIds = @($processIds) }
}

function Failure([string]$category, [string]$message, [int]$mutationCount) {
    $now = [DateTimeOffset]::Now
    Write-Result @{
        schemaVersion = 'pimax-recovery-experiment-v1'
        experimentId = $script:experimentId
        requestSha256 = $ExpectedRequestSha256
        elevated = $script:elevated
        helperProcessId = $PID
        startedAt = $script:startedAt
        endedAt = $now
        serviceIdentityVerified = $false
        serviceName = $script:serviceName
        preStopState = $script:preStopState
        preStopProcessId = $script:preStopProcessId
        stopResult = New-Operation $false $false $message @()
        startResult = New-Operation $false $false $message @()
        executablePathVerified = $false
        executableHashVerified = $false
        dependencyState = @()
        safetyRestorationAttempted = $false
        mutationCount = $mutationCount
        success = $false
        failureCategory = $category
        warnings = @()
        errors = @($message)
    }
    exit 1
}

$script:startedAt = [DateTimeOffset]::Now
$script:experimentId = $null
$script:serviceName = $null
$script:preStopState = $null
$script:preStopProcessId = $null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$script:elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $script:elevated) {
    Failure 'notElevated' 'Elevated helper was not running as Administrator.' 0
}

$actualHash = (Get-FileHash -LiteralPath $RequestPath -Algorithm SHA256).Hash
if ($actualHash -ne $ExpectedRequestSha256) {
    Failure 'staleConfirmationPlan' 'Privileged request hash mismatch.' 0
}

$request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
$script:experimentId = [string]$request.experimentId
$script:serviceName = [string]$request.serviceName
if ([DateTimeOffset]::Parse([string]$request.expiresAt) -le [DateTimeOffset]::Now) {
    Failure 'staleConfirmationPlan' 'Privileged request expired.' 0
}
if ([string]$request.confirmationBinding -ne $ConfirmationBinding) {
    Failure 'staleConfirmationPlan' 'Confirmation binding mismatch.' 0
}

$steamVr = @(Get-Process -Name vrserver,vrmonitor,vrcompositor -ErrorAction SilentlyContinue)
if ($steamVr.Count -gt 0) {
    Failure 'steamVrRunning' 'SteamVR process is running.' 0
}

$svc = Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f ([string]$request.serviceName).Replace("'", "''"))
if (-not $svc) {
    Failure 'targetNotFound' 'Target service was not found.' 0
}
$script:preStopState = [string]$svc.State
$script:preStopProcessId = [int]$svc.ProcessId
$exe = Resolve-ServiceExecutablePath $svc.PathName
if ($exe -ne [string]$request.expectedExecutablePath) {
    Failure 'targetIdentityMismatch' 'Service executable path changed.' 0
}
if ($svc.DisplayName -ne [string]$request.expectedDisplayName) {
    Failure 'targetIdentityMismatch' 'Service display name changed.' 0
}
if ($svc.State -ne [string]$request.expectedState -or [int]$svc.ProcessId -ne [int]$request.expectedProcessId) {
    Failure 'staleConfirmationPlan' 'Service state or PID changed after dry run.' 0
}
if ($request.expectedExecutableSha256) {
    $exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    if ($exeHash -ne [string]$request.expectedExecutableSha256) {
        Failure 'targetIdentityMismatch' 'Service executable hash changed.' 0
    }
}

$service = New-Object System.ServiceProcess.ServiceController([string]$request.serviceName)
$activeDependents = @($service.DependentServices | Where-Object { $_.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped } | ForEach-Object { $_.ServiceName })
if ($activeDependents.Count -gt 0) {
    Failure 'dependentServicesActive' ('Active dependent services: ' + ($activeDependents -join ', ')) 0
}

$stopRequestedAt = [DateTimeOffset]::Now
$service.Stop()
$mutationCount = 1
try {
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds([int]$request.stopTimeoutSeconds))
} catch {
    Failure 'serviceStopTimeout' ('Timed out waiting for service stop: ' + $_.Exception.Message) $mutationCount
}
$stoppedAt = [DateTimeOffset]::Now
$startRequestedAt = [DateTimeOffset]::Now
$service.Start()
$mutationCount = 2
try {
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds([int]$request.startTimeoutSeconds))
} catch {
    $restoration = $null
    if ([bool]$request.safetyRestorationPermitted) {
        try {
            $service.Start()
            $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds([int]$request.startTimeoutSeconds))
            $restoration = New-Operation $true $true 'Safety restoration start succeeded.' @()
        } catch {
            $restoration = New-Operation $true $false ('Safety restoration start failed: ' + $_.Exception.Message) @()
        }
    }
    $now = [DateTimeOffset]::Now
    Write-Result @{
        schemaVersion = 'pimax-recovery-experiment-v1'
        experimentId = $script:experimentId
        requestSha256 = $ExpectedRequestSha256
        elevated = $true
        helperProcessId = $PID
        startedAt = $script:startedAt
        endedAt = $now
        serviceIdentityVerified = $true
        serviceName = $script:serviceName
        preStopState = $script:preStopState
        preStopProcessId = $script:preStopProcessId
        stopRequestedAt = $stopRequestedAt
        stoppedAt = $stoppedAt
        stopResult = New-Operation $true $true 'Service stopped.' @($script:preStopProcessId)
        startRequestedAt = $startRequestedAt
        startResult = New-Operation $true $false ('Timed out waiting for service start: ' + $_.Exception.Message) @()
        executablePathVerified = $true
        executableHashVerified = $true
        dependencyState = @()
        safetyRestorationAttempted = ($restoration -ne $null)
        safetyRestorationResult = $restoration
        mutationCount = $mutationCount
        success = $false
        failureCategory = if ($restoration -and -not $restoration.success) { 'safetyRestorationFailed' } else { 'serviceStartTimeout' }
        warnings = @()
        errors = @('Service did not return to Running within timeout.')
    }
    exit 1
}

$runningAt = [DateTimeOffset]::Now
$svcAfter = Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f ([string]$request.serviceName).Replace("'", "''"))
$exeAfter = Resolve-ServiceExecutablePath $svcAfter.PathName
$pathVerified = ($exeAfter -eq [string]$request.expectedExecutablePath)
$hashVerified = $true
if ($request.expectedExecutableSha256) {
    $hashVerified = ((Get-FileHash -LiteralPath $exeAfter -Algorithm SHA256).Hash -eq [string]$request.expectedExecutableSha256)
}
$nowEnd = [DateTimeOffset]::Now
Write-Result @{
    schemaVersion = 'pimax-recovery-experiment-v1'
    experimentId = $script:experimentId
    requestSha256 = $ExpectedRequestSha256
    elevated = $true
    helperProcessId = $PID
    startedAt = $script:startedAt
    endedAt = $nowEnd
    serviceIdentityVerified = $true
    serviceName = $script:serviceName
    preStopState = $script:preStopState
    preStopProcessId = $script:preStopProcessId
    stopRequestedAt = $stopRequestedAt
    stoppedAt = $stoppedAt
    stopResult = New-Operation $true $true 'Service stopped.' @($script:preStopProcessId)
    startRequestedAt = $startRequestedAt
    runningAt = $runningAt
    startResult = New-Operation $true $true 'Service started.' @([int]$svcAfter.ProcessId)
    postStartProcessId = [int]$svcAfter.ProcessId
    executablePathVerified = $pathVerified
    executableHashVerified = $hashVerified
    dependencyState = @()
    safetyRestorationAttempted = $false
    mutationCount = $mutationCount
    success = ($pathVerified -and $hashVerified)
    failureCategory = if ($pathVerified -and $hashVerified) { 'none' } else { 'servicePidPathMismatch' }
    warnings = @()
    errors = @()
}
""";
}
