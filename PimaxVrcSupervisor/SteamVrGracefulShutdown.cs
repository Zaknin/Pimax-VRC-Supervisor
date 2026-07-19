using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace PimaxVrcSupervisor;

internal sealed record SteamVrShutdownRequestResult(
    bool Succeeded,
    bool TimedOut,
    string Mechanism,
    string? ExecutablePath,
    int? ExitCode,
    string? Error)
{
    public static SteamVrShutdownRequestResult Success(string executablePath, int exitCode)
        => new(true, false, "vrstartup.exe -shutdown", executablePath, exitCode, null);

    public static SteamVrShutdownRequestResult Failure(
        string? executablePath,
        string error,
        int? exitCode = null,
        bool timedOut = false)
        => new(false, timedOut, "vrstartup.exe -shutdown", executablePath, exitCode, error);
}

internal interface ISteamVrGracefulShutdownAdapter
{
    Task<SteamVrShutdownRequestResult> RequestShutdownAsync(CancellationToken cancellationToken);
}

internal sealed record SteamVrShutdownProcessResult(bool TimedOut, int? ExitCode, string? Error);

internal sealed class VrStartupGracefulShutdownAdapter : ISteamVrGracefulShutdownAdapter
{
    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<string?> _resolveExecutable;
    private readonly Func<string, TimeSpan, CancellationToken, Task<SteamVrShutdownProcessResult>> _runShutdown;
    private readonly TimeSpan _requestTimeout;

    public VrStartupGracefulShutdownAdapter()
        : this(
            SteamVrRuntimePathResolver.FindVrStartupExecutable,
            RunShutdownProcessAsync,
            DefaultRequestTimeout)
    {
    }

    internal VrStartupGracefulShutdownAdapter(
        Func<string?> resolveExecutable,
        Func<string, TimeSpan, CancellationToken, Task<SteamVrShutdownProcessResult>> runShutdown,
        TimeSpan requestTimeout)
    {
        _resolveExecutable = resolveExecutable;
        _runShutdown = runShutdown;
        _requestTimeout = requestTimeout;
    }

    public async Task<SteamVrShutdownRequestResult> RequestShutdownAsync(CancellationToken cancellationToken)
    {
        string? executablePath;
        try
        {
            executablePath = _resolveExecutable();
        }
        catch (Exception ex)
        {
            return SteamVrShutdownRequestResult.Failure(null, ex.Message);
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return SteamVrShutdownRequestResult.Failure(
                executablePath,
                "The installed SteamVR runtime's bin\\win64\\vrstartup.exe could not be found.");
        }

        SteamVrShutdownProcessResult processResult;
        try
        {
            processResult = await _runShutdown(executablePath, _requestTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return SteamVrShutdownRequestResult.Failure(executablePath, ex.Message);
        }

        if (processResult.TimedOut)
        {
            return SteamVrShutdownRequestResult.Failure(
                executablePath,
                $"SteamVR did not acknowledge the graceful shutdown request within {_requestTimeout.TotalSeconds:0} seconds.",
                processResult.ExitCode,
                timedOut: true);
        }

        if (processResult.Error is not null)
        {
            return SteamVrShutdownRequestResult.Failure(
                executablePath,
                processResult.Error,
                processResult.ExitCode);
        }

        if (processResult.ExitCode != 0)
        {
            return SteamVrShutdownRequestResult.Failure(
                executablePath,
                $"vrstartup.exe rejected the graceful shutdown request with exit code {processResult.ExitCode}.",
                processResult.ExitCode);
        }

        return SteamVrShutdownRequestResult.Success(executablePath, processResult.ExitCode.Value);
    }

    private static async Task<SteamVrShutdownProcessResult> RunShutdownProcessAsync(
        string executablePath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("-shutdown");

        if (!process.Start())
        {
            return new SteamVrShutdownProcessResult(false, null, "vrstartup.exe did not start.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return new SteamVrShutdownProcessResult(false, process.ExitCode, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SteamVrShutdownProcessResult(true, process.HasExited ? process.ExitCode : null, null);
        }
    }
}

internal static class SteamVrRuntimePathResolver
{
    public static string? FindVrStartupExecutable()
    {
        foreach (var runtimePath in EnumerateRuntimePaths())
        {
            var candidate = Path.Combine(runtimePath, "bin", "win64", "vrstartup.exe");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    internal static IEnumerable<string> ReadRuntimePaths(string openVrPathsFile)
    {
        if (!File.Exists(openVrPathsFile))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(openVrPathsFile));
        if (!document.RootElement.TryGetProperty("runtime", out var runtimeElement)
            || runtimeElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in runtimeElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } runtimePath)
            {
                yield return runtimePath.TrimEnd('\\', '/');
            }
        }
    }

    private static IEnumerable<string> EnumerateRuntimePaths()
    {
        var openVrPathsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openvr",
            "openvrpaths.vrpath");
        foreach (var runtimePath in ReadRuntimePaths(openVrPathsFile))
        {
            yield return runtimePath;
        }

        foreach (var registryPath in ReadSteamInstallPaths())
        {
            yield return Path.Combine(registryPath, "steamapps", "common", "SteamVR");
        }
    }

    private static IEnumerable<string> ReadSteamInstallPaths()
    {
        foreach (var (hive, subKey) in new[]
                 {
                     (RegistryHive.CurrentUser, @"Software\Valve\Steam"),
                     (RegistryHive.LocalMachine, @"Software\WOW6432Node\Valve\Steam"),
                     (RegistryHive.LocalMachine, @"Software\Valve\Steam")
                 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            var path = key?.GetValue("SteamPath") as string
                       ?? key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }
    }
}
