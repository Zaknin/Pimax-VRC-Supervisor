using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PimaxVrcSupervisor.Updates;

internal sealed record UpdateCheckWorkerProcessOutput(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool StandardOutputExceeded,
    bool StandardErrorExceeded);

internal interface IUpdateCheckWorkerProcessRunner
{
    Task<UpdateCheckWorkerProcessOutput> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class ConfiguratorStandaloneUpdateCheckLauncher : IStandaloneUpdateCheckLauncher
{
    private const string UpdateWorkerExecutableName = "PimaxVrcSupervisor.UpdateWorker.exe";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);
    private static readonly Regex StableVersionPattern = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly string _installationDirectory;
    private readonly IUpdateCheckWorkerProcessRunner _processRunner;
    private readonly TimeSpan _timeout;

    public ConfiguratorStandaloneUpdateCheckLauncher()
        : this(AppContext.BaseDirectory, new SystemUpdateCheckWorkerProcessRunner(), DefaultTimeout)
    {
    }

    internal ConfiguratorStandaloneUpdateCheckLauncher(
        string installationDirectory,
        IUpdateCheckWorkerProcessRunner processRunner,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _installationDirectory = Path.GetFullPath(installationDirectory);
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _timeout = timeout;
    }

    public async Task<StandaloneUpdateCheckResultV1> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executablePath = Path.GetFullPath(Path.Combine(_installationDirectory, UpdateWorkerExecutableName));
        if (!string.Equals(
                Path.GetDirectoryName(executablePath)?.TrimEnd(Path.DirectorySeparatorChar),
                _installationDirectory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(executablePath))
        {
            return Failure("worker_missing", "The sibling Supervisor update-check worker is unavailable.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = _installationDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--update-check-once");
        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add("configurator");

        UpdateCheckWorkerProcessOutput output;
        try
        {
            output = await _processRunner.RunAsync(startInfo, _timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return Failure("worker_start_failed", "The sibling Supervisor update-check worker could not be started.");
        }

        if (output.TimedOut)
        {
            return Failure("worker_timeout", "The standalone update check exceeded its bounded wait time and may finish in the background.");
        }

        if (output.StandardOutputExceeded || output.StandardErrorExceeded)
        {
            return Failure("worker_output_oversized", "The standalone update-check worker returned oversized output.");
        }

        return ParseResult(output.StandardOutput, output.ExitCode);
    }

    private static StandaloneUpdateCheckResultV1 ParseResult(string standardOutput, int? exitCode)
    {
        if (Encoding.UTF8.GetByteCount(standardOutput) > StandaloneUpdateCheckJson.MaximumOutputBytes)
        {
            return Failure("worker_output_oversized", "The standalone update-check worker returned oversized output.");
        }

        var lines = standardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
        {
            return Failure("worker_output_invalid", "The standalone update-check worker returned an invalid result.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<StandaloneUpdateCheckResultV1>(
                lines[0],
                StandaloneUpdateCheckJson.Options);
            if (result is null
                || result.SchemaVersion != 1
                || !string.Equals(result.Operation, "update-check-once", StringComparison.Ordinal)
                || !IsSafeIdentifier(result.ResultCode, 64)
                || string.IsNullOrEmpty(result.Summary)
                || result.Summary.Length > 256
                || result.Summary.Any(char.IsControl)
                || result.LatestVerifiedVersion is not null && !StableVersionPattern.IsMatch(result.LatestVerifiedVersion)
                || result.Success && result.Status is null
                || result.Status is not null
                    && (result.Status.SchemaVersion != 1
                        || !string.Equals(result.Status.Channel, "Stable", StringComparison.Ordinal)
                        || !string.Equals(result.LatestVerifiedVersion, result.Status.LatestVerifiedVersion, StringComparison.Ordinal)
                        || result.UpdateAvailable != result.Status.UpdateAvailable)
                || exitCode is null
                || (exitCode == 0) != result.Success)
            {
                return Failure("worker_output_invalid", "The standalone update-check worker returned an invalid result.");
            }

            return result;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Failure("worker_output_invalid", "The standalone update-check worker returned an invalid result.");
        }
    }

    private static bool IsSafeIdentifier(string? value, int maximumLength)
        => value is not null
            && value.Length is > 0
            && value.Length <= maximumLength
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static StandaloneUpdateCheckResultV1 Failure(string code, string summary)
        => new(
            SchemaVersion: 1,
            Operation: "update-check-once",
            ResultCode: code,
            Success: false,
            LatestVerifiedVersion: null,
            UpdateAvailable: false,
            Summary: summary,
            Status: null);
}

internal sealed class SystemUpdateCheckWorkerProcessRunner : IUpdateCheckWorkerProcessRunner
{
    private const int MaximumStandardErrorCharacters = 4 * 1024;

    public async Task<UpdateCheckWorkerProcessOutput> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The update-check worker process did not start.");
        }

        var standardOutput = ReadBoundedAsync(
            process.StandardOutput,
            StandaloneUpdateCheckJson.MaximumOutputBytes);
        var standardError = ReadBoundedAsync(process.StandardError, MaximumStandardErrorCharacters);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        var completed = await Task.WhenAny(exit, Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false);
        if (completed != exit)
        {
            _ = ObserveCompletionAndDisposeAsync(process, exit, standardOutput, standardError);
            return new UpdateCheckWorkerProcessOutput(
                ExitCode: null,
                StandardOutput: "",
                StandardError: "",
                TimedOut: true,
                StandardOutputExceeded: false,
                StandardErrorExceeded: false);
        }

        await exit.ConfigureAwait(false);
        var stdout = await standardOutput.ConfigureAwait(false);
        var stderr = await standardError.ConfigureAwait(false);
        var exitCode = process.ExitCode;
        process.Dispose();
        return new UpdateCheckWorkerProcessOutput(
            exitCode,
            stdout.Text,
            stderr.Text,
            TimedOut: false,
            stdout.Exceeded,
            stderr.Exceeded);
    }

    private static async Task<BoundedText> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 4096));
        var buffer = new char[1024];
        var exceeded = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            var available = Math.Max(0, maximumCharacters - builder.Length);
            if (available > 0)
            {
                builder.Append(buffer, 0, Math.Min(available, count));
            }

            exceeded |= count > available;
        }

        return new BoundedText(builder.ToString(), exceeded);
    }

    private static async Task ObserveCompletionAndDisposeAsync(
        Process process,
        Task exit,
        Task<BoundedText> standardOutput,
        Task<BoundedText> standardError)
    {
        try
        {
            await exit.ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private sealed record BoundedText(string Text, bool Exceeded);
}
