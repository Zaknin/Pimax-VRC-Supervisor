using System.Diagnostics;
using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class ConfiguratorStandaloneUpdateCheckLauncherTests
{
    [Fact]
    public async Task LaunchUsesExactSiblingExecutableAndFixedNoShellArguments()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "PimaxVrcSupervisor.exe");
        File.WriteAllBytes(executable, []);
        var runner = new RecordingProcessRunner(Output(ValidJson()));
        var launcher = new ConfiguratorStandaloneUpdateCheckLauncher(temp.Path, runner, TimeSpan.FromSeconds(2));

        var result = await launcher.RunAsync(CancellationToken.None);
        Assert.NotNull(runner.StartInfo);
        var startInfo = runner.StartInfo!;

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(executable), startInfo.FileName);
        Assert.Equal(Path.GetFullPath(temp.Path), startInfo.WorkingDirectory);
        Assert.Equal(new[] { "--update-check-once", "--source", "configurator" }, startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
    }

    [Fact]
    public async Task MissingSiblingExecutableDoesNotSearchPathOrStartProcess()
    {
        using var temp = new TempDirectory();
        var runner = new RecordingProcessRunner(Output(ValidJson()));
        var launcher = new ConfiguratorStandaloneUpdateCheckLauncher(temp.Path, runner, TimeSpan.FromSeconds(2));

        var result = await launcher.RunAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("worker_missing", result.ResultCode);
        Assert.Null(runner.StartInfo);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"operation\":\"update-check-once\"}")]
    public async Task MalformedWorkerOutputIsRejected(string stdout)
    {
        var result = await RunWithOutputAsync(Output(stdout));

        Assert.False(result.Success);
        Assert.Equal("worker_output_invalid", result.ResultCode);
    }

    [Fact]
    public async Task OversizedStdoutOrStderrIsRejectedWithoutEchoingContent()
    {
        var output = new UpdateCheckWorkerProcessOutput(
            ExitCode: 1,
            StandardOutput: "secret-output",
            StandardError: "secret-error",
            TimedOut: false,
            StandardOutputExceeded: true,
            StandardErrorExceeded: true);

        var result = await RunWithOutputAsync(output);

        Assert.False(result.Success);
        Assert.Equal("worker_output_oversized", result.ResultCode);
        Assert.DoesNotContain("secret", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkerTimeoutIsBoundedAndDoesNotExposeProcessOutput()
    {
        var output = new UpdateCheckWorkerProcessOutput(
            ExitCode: null,
            StandardOutput: "partial-secret",
            StandardError: "error-secret",
            TimedOut: true,
            StandardOutputExceeded: false,
            StandardErrorExceeded: false);

        var result = await RunWithOutputAsync(output);

        Assert.False(result.Success);
        Assert.Equal("worker_timeout", result.ResultCode);
        Assert.DoesNotContain("secret", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LauncherReturnsControlWhileWorkerProcessIsRunning()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "PimaxVrcSupervisor.exe"), []);
        var runner = new BlockingProcessRunner();
        var launcher = new ConfiguratorStandaloneUpdateCheckLauncher(temp.Path, runner, TimeSpan.FromSeconds(2));

        var run = launcher.RunAsync(CancellationToken.None);
        await runner.Started;

        Assert.False(run.IsCompleted);
        runner.Complete(Output(ValidJson()));
        Assert.True((await run).Success);
    }

    [Fact]
    public void AcceptedWorkerIsNeverKilledAndOutputReadersRemainBounded()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor.ConfigEditor",
            "ConfiguratorStandaloneUpdateCheckLauncher.cs"));

        Assert.Contains("WaitForExitAsync(CancellationToken.None)", source, StringComparison.Ordinal);
        Assert.Contains("ObserveCompletionAndDisposeAsync", source, StringComparison.Ordinal);
        Assert.Contains("StandaloneUpdateCheckJson.MaximumOutputBytes", source, StringComparison.Ordinal);
        Assert.Contains("MaximumStandardErrorCharacters", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Kill(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.GetEnvironmentVariable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ZipFile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractToDirectory", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultipleWorkerJsonResultsAreRejected()
    {
        var json = ValidJson();
        var result = await RunWithOutputAsync(Output(json + Environment.NewLine + json));

        Assert.False(result.Success);
        Assert.Equal("worker_output_invalid", result.ResultCode);
    }

    [Fact]
    public async Task ValidWorkerLevelFailureWithoutCachedStatusIsAccepted()
    {
        var json = StandaloneUpdateCheckJson.Serialize(StandaloneUpdateCheckCommand.Failed(
            "worker_failed",
            "The worker failed before loading cached status."));
        var output = new UpdateCheckWorkerProcessOutput(1, json, "", false, false, false);

        var result = await RunWithOutputAsync(output);

        Assert.False(result.Success);
        Assert.Equal("worker_failed", result.ResultCode);
        Assert.Null(result.Status);
    }

    private static async Task<StandaloneUpdateCheckResultV1> RunWithOutputAsync(UpdateCheckWorkerProcessOutput output)
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "PimaxVrcSupervisor.exe"), []);
        var launcher = new ConfiguratorStandaloneUpdateCheckLauncher(
            temp.Path,
            new RecordingProcessRunner(output),
            TimeSpan.FromSeconds(2));
        return await launcher.RunAsync(CancellationToken.None);
    }

    private static string ValidJson()
    {
        var status = new UpdateStatusSnapshotV1(
            1, "Disabled", "Stable", AppVersion.Current, null, false, false, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, true, false, false, null);
        return StandaloneUpdateCheckJson.Serialize(new StandaloneUpdateCheckResultV1(
            1, "update-check-once", "current", true, null, false, "current", status));
    }

    private static UpdateCheckWorkerProcessOutput Output(string stdout)
        => new(0, stdout, "", false, false, false);

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PimaxVrcSupervisor.Tests", "PimaxVrcSupervisor.Tests.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class RecordingProcessRunner(UpdateCheckWorkerProcessOutput output) : IUpdateCheckWorkerProcessRunner
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public Task<UpdateCheckWorkerProcessOutput> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            StartInfo = startInfo;
            return Task.FromResult(output);
        }
    }

    private sealed class BlockingProcessRunner : IUpdateCheckWorkerProcessRunner
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<UpdateCheckWorkerProcessOutput> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public Task<UpdateCheckWorkerProcessOutput> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            return _completed.Task;
        }

        public void Complete(UpdateCheckWorkerProcessOutput output) => _completed.TrySetResult(output);
    }
}
