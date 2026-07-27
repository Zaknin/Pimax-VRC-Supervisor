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
        var invocations = new List<string>();
        var helperCompletion = Task.FromResult(new SteamVrShutdownHelperDiagnostics(0, null));
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => executable,
            path =>
            {
                invocations.Add(path);
                return new SteamVrShutdownProcessInvocation(true, helperCompletion, null);
            });

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.True(result.RequestIssued);
        Assert.Equal("vrstartup.exe -shutdown", result.Mechanism);
        var invocation = Assert.Single(invocations);
        Assert.Equal(executable, invocation);
        Assert.Same(helperCompletion, result.HelperCompletion);
    }

    [Fact]
    public async Task AdapterReturnsRequestIssuedWithoutWaitingForHelperExit()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "vrstartup.exe");
        File.WriteAllBytes(executable, []);
        var helperCompletion = new TaskCompletionSource<SteamVrShutdownHelperDiagnostics>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => executable,
            _ =>
            {
                invocationCount++;
                return new SteamVrShutdownProcessInvocation(true, helperCompletion.Task, null);
            });

        var requestTask = adapter.RequestShutdownAsync(CancellationToken.None);
        var result = await requestTask;

        Assert.True(requestTask.IsCompletedSuccessfully);
        Assert.True(result.RequestIssued);
        Assert.False(helperCompletion.Task.IsCompleted);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task AdapterDoesNotRequireHelperAcknowledgmentOrDiagnosticsChannel()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "vrstartup.exe");
        File.WriteAllBytes(executable, []);
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => executable,
            _ => new SteamVrShutdownProcessInvocation(true, null, null));

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.True(result.RequestIssued);
        Assert.Null(result.HelperCompletion);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task UndocumentedHelperExitCodeIsDiagnosticOnly()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "vrstartup.exe");
        File.WriteAllBytes(executable, []);
        var helperCompletion = Task.FromResult(new SteamVrShutdownHelperDiagnostics(-1, null));
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => executable,
            _ => new SteamVrShutdownProcessInvocation(true, helperCompletion, null));

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.True(result.RequestIssued);
        Assert.Equal(-1, (await result.HelperCompletion!).ExitCode);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ProcessCreationFailureIsReturnedAsInvocationFailure()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "vrstartup.exe");
        File.WriteAllBytes(executable, []);
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => executable,
            _ => new SteamVrShutdownProcessInvocation(false, null, "access denied"));

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.False(result.RequestIssued);
        Assert.Contains("access denied", result.Error, StringComparison.Ordinal);
        Assert.Null(result.HelperCompletion);
    }

    [Fact]
    public async Task ProcessCreationExceptionIsReturnedAsInvocationFailure()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "vrstartup.exe");
        File.WriteAllBytes(executable, []);
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => executable,
            _ => throw new UnauthorizedAccessException("launch denied"));

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.False(result.RequestIssued);
        Assert.Contains("launch denied", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingInstalledRuntimeFailsBeforeProcessInvocation()
    {
        var invoked = false;
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => null,
            _ =>
            {
                invoked = true;
                return new SteamVrShutdownProcessInvocation(
                    true,
                    Task.FromResult(new SteamVrShutdownHelperDiagnostics(0, null)),
                    null);
            });

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.False(result.RequestIssued);
        Assert.False(invoked);
    }

    [Fact]
    public async Task RuntimeDiscoveryFailureIsReturnedWithoutProcessInvocation()
    {
        var invoked = false;
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => throw new InvalidOperationException("runtime registry unavailable"),
            _ =>
            {
                invoked = true;
                return new SteamVrShutdownProcessInvocation(
                    true,
                    Task.FromResult(new SteamVrShutdownHelperDiagnostics(0, null)),
                    null);
            });

        var result = await adapter.RequestShutdownAsync(CancellationToken.None);

        Assert.False(result.RequestIssued);
        Assert.Contains("runtime registry unavailable", result.Error, StringComparison.Ordinal);
        Assert.False(invoked);
    }

    [Fact]
    public async Task CancellationBeforeInvocationPreventsProcessCreation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;
        var adapter = new VrStartupGracefulShutdownAdapter(
            () => "vrstartup.exe",
            _ =>
            {
                invoked = true;
                return new SteamVrShutdownProcessInvocation(true, null, null);
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => adapter.RequestShutdownAsync(cancellation.Token));

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

    [Fact]
    public void AdapterContainsNoHelperAcknowledgmentTimeoutGate()
    {
        var source = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "SteamVrGracefulShutdown.cs"));

        Assert.DoesNotContain("DefaultRequestTimeout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("acknowledge", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CancelAfter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RedirectStandardOutput", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RedirectStandardError", source, StringComparison.Ordinal);
    }

    private static string SourcePath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            Path.Combine(segments)));
}
