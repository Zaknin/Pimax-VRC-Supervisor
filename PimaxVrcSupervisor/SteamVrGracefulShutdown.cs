using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace PimaxVrcSupervisor;

internal sealed record SteamVrShutdownRequestResult(
    bool RequestIssued,
    string Mechanism,
    string? ExecutablePath,
    Task<SteamVrShutdownHelperDiagnostics>? HelperCompletion,
    string? Error)
{
    public static SteamVrShutdownRequestResult Issued(
        string executablePath,
        Task<SteamVrShutdownHelperDiagnostics>? helperCompletion = null)
        => new(true, "vrstartup.exe -shutdown", executablePath, helperCompletion, null);

    public static SteamVrShutdownRequestResult Failure(
        string? executablePath,
        string error)
        => new(false, "vrstartup.exe -shutdown", executablePath, null, error);
}

internal interface ISteamVrGracefulShutdownAdapter
{
    Task<SteamVrShutdownRequestResult> RequestShutdownAsync(CancellationToken cancellationToken);
}

internal sealed record SteamVrShutdownHelperDiagnostics(int? ExitCode, string? Error);

internal sealed record SteamVrShutdownProcessInvocation(
    bool RequestIssued,
    Task<SteamVrShutdownHelperDiagnostics>? Completion,
    string? Error);

internal sealed class VrStartupGracefulShutdownAdapter : ISteamVrGracefulShutdownAdapter
{
    private readonly Func<string?> _resolveExecutable;
    private readonly Func<string, SteamVrShutdownProcessInvocation> _startShutdown;

    public VrStartupGracefulShutdownAdapter()
        : this(
            SteamVrRuntimePathResolver.FindVrStartupExecutable,
            StartShutdownProcess)
    {
    }

    internal VrStartupGracefulShutdownAdapter(
        Func<string?> resolveExecutable,
        Func<string, SteamVrShutdownProcessInvocation> startShutdown)
    {
        _resolveExecutable = resolveExecutable;
        _startShutdown = startShutdown;
    }

    public Task<SteamVrShutdownRequestResult> RequestShutdownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? executablePath;
        try
        {
            executablePath = _resolveExecutable();
        }
        catch (Exception ex)
        {
            return Task.FromResult(SteamVrShutdownRequestResult.Failure(null, ex.Message));
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return Task.FromResult(
                SteamVrShutdownRequestResult.Failure(
                    executablePath,
                    "The installed SteamVR runtime's bin\\win64\\vrstartup.exe could not be found."));
        }

        SteamVrShutdownProcessInvocation invocation;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            invocation = _startShutdown(executablePath);
        }
        catch (Exception ex)
        {
            return Task.FromResult(SteamVrShutdownRequestResult.Failure(executablePath, ex.Message));
        }

        if (!invocation.RequestIssued)
        {
            return Task.FromResult(
                SteamVrShutdownRequestResult.Failure(
                    executablePath,
                    invocation.Error ?? "vrstartup.exe did not start."));
        }

        return Task.FromResult(SteamVrShutdownRequestResult.Issued(executablePath, invocation.Completion));
    }

    private static SteamVrShutdownProcessInvocation StartShutdownProcess(string executablePath)
    {
        var process = new Process
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

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return new SteamVrShutdownProcessInvocation(false, null, "vrstartup.exe did not start.");
            }

            return new SteamVrShutdownProcessInvocation(
                true,
                ObserveHelperCompletionAsync(process),
                null);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static async Task<SteamVrShutdownHelperDiagnostics> ObserveHelperCompletionAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
            return new SteamVrShutdownHelperDiagnostics(process.ExitCode, null);
        }
        catch (Exception ex)
        {
            return new SteamVrShutdownHelperDiagnostics(
                process.HasExited ? process.ExitCode : null,
                ex.Message);
        }
        finally
        {
            process.Dispose();
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
