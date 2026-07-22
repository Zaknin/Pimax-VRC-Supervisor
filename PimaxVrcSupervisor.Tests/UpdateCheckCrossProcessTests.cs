using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using PimaxVrcSupervisor.Updates;
using Xunit;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalUpdateCheckMutexCollection
{
    public const string Name = "Global update-check mutex";
}

[Collection(GlobalUpdateCheckMutexCollection.Name)]
public sealed class UpdateCheckCrossProcessTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeparateProcessesRejectContentionAndAdmitAfterRelease()
    {
        using var temp = new TempDirectory();
        var ready = Path.Combine(temp.Path, "ready");
        var release = Path.Combine(temp.Path, "release");
        using var holder = StartProcess(HelperPath(), "hold", ready, release);
        try
        {
            await WaitForFileAsync(ready);

            var contended = await RunProcessAsync(HelperPath(), "try");

            Assert.Equal(3, contended.ExitCode);
            Assert.Equal("already_running", contended.StandardOutput.Trim());

            File.WriteAllText(release, "release");
            await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, holder.ExitCode);

            var admitted = await RunProcessAsync(HelperPath(), "try");
            Assert.Equal(0, admitted.ExitCode);
            Assert.Equal("acquired", admitted.StandardOutput.Trim());
        }
        finally
        {
            File.WriteAllText(release, "release");
            await StopHelperIfNeededAsync(holder);
        }
    }

    [Fact]
    public async Task AbandonedProcessOwnershipIsGenuinelyObservedRecoveredAndReleased()
    {
        using var temp = new TempDirectory();
        var ready = Path.Combine(temp.Path, "abandoned-ready");
        var currentSid = WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("The current Windows user SID is unavailable.");
        var mutexName = @"Global\PimaxVrcSupervisor.UpdateCheck.Test." + Guid.NewGuid().ToString("N");
        using var observer = MutexAcl.Create(
            initiallyOwned: false,
            mutexName,
            out var createdNew,
            UserScopedUpdateCheckExclusion.BuildMutexSecurity(currentSid));

        var abandoned = await RunProcessAsync(HelperPath(), "abandon-name", mutexName, ready);
        await WaitForFileAsync(ready);
        var abandonedObserved = 0;
        var recovery = UserScopedUpdateCheckExclusion.ForMutexName(
            mutexName,
            currentSid,
            abandonedObserver: () => Interlocked.Exchange(ref abandonedObserved, 1));
        var recovered = recovery.TryAcquire();

        Assert.True(createdNew);
        Assert.Equal(0, abandoned.ExitCode);
        Assert.NotNull(recovered);
        Assert.Equal(1, Volatile.Read(ref abandonedObserved));

        recovered.Dispose();
        Assert.True(observer.WaitOne(TimeSpan.Zero));
        observer.ReleaseMutex();
    }

    [Fact]
    public async Task BridgeRequestIsRejectedWhileExternalProcessOwnsAdmission()
    {
        using var temp = new TempDirectory();
        var ready = Path.Combine(temp.Path, "bridge-ready");
        var release = Path.Combine(temp.Path, "bridge-release");
        using var holder = StartProcess(HelperPath(), "hold", ready, release);
        try
        {
            await WaitForFileAsync(ready);
            var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
            var initialState = store.Load().State;
            var client = new FakeUpdateDiscoveryClient(UpdateDiscoveryCheckResult.Failure(
                UpdateErrorCategory.Http,
                "must_not_run"));
            var clock = new ManualUpdateScheduleClock(Now);
            var scheduler = new UpdateDiscoveryScheduler(store, client, clock);
            using var coordinator = new SupervisorUpdateCoordinator(
                SupervisorUpdatePolicy.Disabled,
                store,
                scheduler,
                clock,
                verificationConfigured: true);

            var acceptance = coordinator.TryStartManualCheck(CancellationToken.None);

            Assert.False(acceptance.Accepted);
            Assert.True(acceptance.AlreadyInProgress);
            Assert.Null(acceptance.OperationId);
            Assert.Equal("already_running", acceptance.ResultCode);
            Assert.Null(coordinator.GetStatus().Operation);
            Assert.Equal(0, client.CallCount);
            Assert.Equal(initialState, store.Load().State);
        }
        finally
        {
            File.WriteAllText(release, "release");
            await StopHelperIfNeededAsync(holder);
        }
    }

    [Fact]
    public async Task StandaloneWorkerIsRejectedWhileSupervisorOwnsAdmission()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var client = new BlockingUpdateDiscoveryClient();
        var clock = new ManualUpdateScheduleClock(Now);
        var scheduler = new UpdateDiscoveryScheduler(store, client, clock);
        using var coordinator = new SupervisorUpdateCoordinator(
            SupervisorUpdatePolicy.Disabled,
            store,
            scheduler,
            clock,
            verificationConfigured: true);

        var acceptance = coordinator.TryStartManualCheck(CancellationToken.None);
        await client.WaitForFirstCallAsync();
        try
        {
            var worker = await RunSupervisorWorkerAsync(
                "--update-check-once",
                "--source",
                "configurator");
            var result = JsonSerializer.Deserialize<StandaloneUpdateCheckResultV1>(
                worker.StandardOutput.Trim(),
                StandaloneUpdateCheckJson.Options);

            Assert.True(acceptance.Accepted);
            Assert.Equal(1, worker.ExitCode);
            Assert.NotNull(result);
            Assert.Equal("already_running", result!.ResultCode);
            Assert.False(result.Success);
        }
        finally
        {
            client.Release();
        }
    }

    [Fact]
    public async Task StandaloneWorkerReturnsAlreadyRunningWhileExternalProcessOwnsAdmission()
    {
        using var temp = new TempDirectory();
        var ready = Path.Combine(temp.Path, "worker-ready");
        var release = Path.Combine(temp.Path, "worker-release");
        using var holder = StartProcess(HelperPath(), "hold", ready, release);
        try
        {
            await WaitForFileAsync(ready);

            var worker = await RunSupervisorWorkerAsync(
                "--update-check-once",
                "--source",
                "configurator");
            var result = JsonSerializer.Deserialize<StandaloneUpdateCheckResultV1>(
                worker.StandardOutput.Trim(),
                StandaloneUpdateCheckJson.Options);

            Assert.Equal(1, worker.ExitCode);
            Assert.NotNull(result);
            Assert.Equal("already_running", result!.ResultCode);
            Assert.False(result.Success);
        }
        finally
        {
            File.WriteAllText(release, "release");
            await StopHelperIfNeededAsync(holder);
        }
    }

    private static Process StartProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = Path.IsPathRooted(fileName)
                ? Path.GetDirectoryName(fileName)!
                : AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The test helper process did not start.");
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, params string[] arguments)
    {
        using var process = StartProcess(fileName, arguments);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static Task<ProcessResult> RunSupervisorWorkerAsync(params string[] arguments)
        => RunProcessAsync("dotnet", [SupervisorAssemblyPath(), .. arguments]);

    private static async Task WaitForFileAsync(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The update-check test helper did not publish readiness.");
    }

    private static async Task StopHelperIfNeededAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static string HelperPath()
        => Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor.UpdateCheckTestHelper",
            "bin",
            Configuration(),
            "net9.0-windows10.0.19041.0",
            "PimaxVrcSupervisor.UpdateCheckTestHelper.exe");

    private static string SupervisorAssemblyPath()
        => Path.Combine(
            RepositoryRoot(),
            "PimaxVrcSupervisor",
            "bin",
            Configuration(),
            "net9.0-windows10.0.19041.0",
            "PimaxVrcSupervisor.dll");

    private static string Configuration()
        => AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
