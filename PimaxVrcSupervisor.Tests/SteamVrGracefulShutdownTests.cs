using Xunit;

namespace PimaxVrcSupervisor.Tests;

public sealed class SteamVrGracefulShutdownTests
{
    [Fact]
    public async Task AdapterInvokesInstalledShutdownCommandExactlyOnce()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "vrstartup.exe");
        File.WriteAllBytes(executable, []);
        var invocations = new List<(string Path, TimeSpan Timeout)>();
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => executable,
            (path, timeout, _) =>
            {
                invocations.Add((path, timeout));
                return Task.FromResult(new SteamVrShutdownProcessResult(false, 0, null));
            },
            TimeSpan.FromSeconds(7));

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("vrstartup.exe -shutdown", result.Mechanism);
        var invocation = Assert.Single(invocations);
        Assert.Equal(executable, invocation.Path);
        Assert.Equal(TimeSpan.FromSeconds(7), invocation.Timeout);
    }

    [Fact]
    public async Task AdapterTimeoutFailsWithoutRetry()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "vrstartup.exe");
        File.WriteAllBytes(executable, []);
        var invocationCount = 0;
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => executable,
            (_, _, _) =>
            {
                invocationCount++;
                return Task.FromResult(new SteamVrShutdownProcessResult(true, null, null));
            },
            TimeSpan.FromSeconds(3));

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task AdapterNonZeroExitIsRejected()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "vrstartup.exe");
        File.WriteAllBytes(executable, []);
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => executable,
            (_, _, _) => Task.FromResult(new SteamVrShutdownProcessResult(false, -1, null)),
            TimeSpan.FromSeconds(3));

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("rejected", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingInstalledRuntimeFailsBeforeProcessInvocation()
    {
        var invoked = false;
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => null,
            (_, _, _) =>
            {
                invoked = true;
                return Task.FromResult(new SteamVrShutdownProcessResult(false, 0, null));
            },
            TimeSpan.FromSeconds(3));

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(invoked);
    }

    [Fact]
    public async Task RuntimeDiscoveryFailureIsReturnedWithoutProcessInvocation()
    {
        var invoked = false;
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => throw new InvalidOperationException("runtime registry unavailable"),
            (_, _, _) =>
            {
                invoked = true;
                return Task.FromResult(new SteamVrShutdownProcessResult(false, 0, null));
            },
            TimeSpan.FromSeconds(3));

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("runtime registry unavailable", result.Error, StringComparison.Ordinal);
        Assert.False(invoked);
    }

    [Fact]
    public void RuntimePathResolverReadsConfiguredRuntimeArray()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "openvrpaths.vrpath");
        File.WriteAllText(path, """
            {
              "runtime": [
                "C:\\Steam\\SteamVR\\",
                "D:\\SteamLibrary\\steamapps\\common\\SteamVR"
              ]
            }
            """);

        var paths = SteamVrRuntimePathResolver.ReadRuntimePaths(path).ToArray();

        Assert.Equal(
            [@"C:\Steam\SteamVR", @"D:\SteamLibrary\steamapps\common\SteamVR"],
            paths);
    }

    [Fact]
    public void NormalShutdownAdapterContainsNoForcedTerminationFallback()
    {
        var source = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "SteamVrGracefulShutdown.cs"));

        Assert.DoesNotContain("Process.Kill", source, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stop-Process", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".Kill(", source, StringComparison.Ordinal);
    }

    private static string SourcePath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            Path.Combine(segments)));
}
