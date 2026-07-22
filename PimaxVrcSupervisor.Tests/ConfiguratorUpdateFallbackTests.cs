using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class ConfiguratorUpdateFallbackTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunningSupervisorUsesBridgeWithoutLaunchingWorker()
    {
        var bridge = FakeBridge.WithTerminalStatus(Status("current", success: true));
        var launcher = new FakeLauncher(WorkerResult("current", success: true));
        var runner = new ConfiguratorUpdateCheckRunner(bridge, launcher, (_, _) => Task.CompletedTask);

        var result = await runner.TryRunAsync(null, CancellationToken.None);

        Assert.True(result.Acceptance.Accepted);
        Assert.Equal(1, bridge.StartCount);
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task BridgeUnavailableBeforeAcceptanceLaunchesWorkerOnce()
    {
        var bridge = FakeBridge.UnavailableBeforeAcceptance();
        var launcher = new FakeLauncher(WorkerResult("current", success: true));
        var runner = new ConfiguratorUpdateCheckRunner(bridge, launcher, (_, _) => Task.CompletedTask);
        UpdateStatusSnapshotV1? observed = null;

        var result = await runner.TryRunAsync(status => observed = status, CancellationToken.None);

        Assert.Equal(1, bridge.StartCount);
        Assert.Equal(1, launcher.LaunchCount);
        Assert.Equal("current", result.Acceptance.Message);
        Assert.Equal("current", result.Acceptance.ResultCode);
        Assert.NotNull(observed);
    }

    [Fact]
    public async Task ConnectedSupervisorRejectionDoesNotLaunchWorker()
    {
        var bridge = FakeBridge.Rejected("release_mutable");
        var launcher = new FakeLauncher(WorkerResult("current", success: true));
        var runner = new ConfiguratorUpdateCheckRunner(bridge, launcher, (_, _) => Task.CompletedTask);

        var result = await runner.TryRunAsync(null, CancellationToken.None);

        Assert.False(result.Acceptance.Accepted);
        Assert.Equal("release_mutable", result.Summary);
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task AcceptedBridgeValidationFailureDoesNotLaunchWorker()
    {
        var bridge = FakeBridge.WithTerminalStatus(Status("release_mutable", success: false));
        var launcher = new FakeLauncher(WorkerResult("current", success: true));
        var runner = new ConfiguratorUpdateCheckRunner(bridge, launcher, (_, _) => Task.CompletedTask);

        var result = await runner.TryRunAsync(null, CancellationToken.None);

        Assert.Equal("release_mutable", result.Summary);
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task BridgeFailureAfterAcceptanceDoesNotLaunchWorker()
    {
        var bridge = FakeBridge.UnavailableAfterAcceptance();
        var launcher = new FakeLauncher(WorkerResult("current", success: true));
        var runner = new ConfiguratorUpdateCheckRunner(bridge, launcher, (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<SupervisorBridgeUnavailableException>(
            () => runner.TryRunAsync(null, CancellationToken.None));
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task DuplicateClicksLaunchOneStandaloneWorkerOperation()
    {
        var bridge = FakeBridge.UnavailableBeforeAcceptance();
        var launcher = new BlockingLauncher(WorkerResult("current", success: true));
        var runner = new ConfiguratorUpdateCheckRunner(bridge, launcher, (_, _) => Task.CompletedTask);

        var first = runner.TryRunAsync(null, CancellationToken.None);
        await launcher.Started;
        var duplicate = await runner.TryRunAsync(null, CancellationToken.None);

        Assert.False(duplicate.Acceptance.Accepted);
        Assert.True(duplicate.Acceptance.AlreadyInProgress);
        Assert.Equal(1, launcher.LaunchCount);

        launcher.Complete();
        Assert.True((await first).Acceptance.Accepted);
    }

    [Fact]
    public void BridgeWireCarriesBoundedImmediateResultCode()
    {
        var root = RepositoryRoot();
        var supervisor = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "Program.cs"));
        var contract = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "UpdateBridgeContract.cs"));

        Assert.Contains("resultCode = acceptance.ResultCode", supervisor, StringComparison.Ordinal);
        Assert.Contains("ReadBoundedString(data, \"resultCode\")", contract, StringComparison.Ordinal);
        Assert.Contains("new UpdateActionAcceptance(accepted, alreadyInProgress, operationId, message, resultCode)", contract, StringComparison.Ordinal);
    }

    private static StandaloneUpdateCheckResultV1 WorkerResult(string code, bool success)
    {
        var status = Status(code, success);
        return new StandaloneUpdateCheckResultV1(
            1,
            "update-check-once",
            code,
            success,
            status.LatestVerifiedVersion,
            status.UpdateAvailable,
            code,
            status);
    }

    private static UpdateStatusSnapshotV1 Status(string code, bool success)
        => new(
            1,
            "Disabled",
            "Stable",
            AppVersion.Current,
            null,
            false,
            false,
            null,
            Now,
            success ? Now : null,
            success ? null : code,
            success ? null : code,
            true,
            false,
            false,
            new UpdateCheckOperationSnapshot("op-1", success ? "completed" : "failed", success, code, code, Now, Now));

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

    private sealed class FakeLauncher(StandaloneUpdateCheckResultV1 result) : IStandaloneUpdateCheckLauncher
    {
        public int LaunchCount { get; private set; }

        public Task<StandaloneUpdateCheckResultV1> RunAsync(CancellationToken cancellationToken)
        {
            LaunchCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class BlockingLauncher(StandaloneUpdateCheckResultV1 result) : IStandaloneUpdateCheckLauncher
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LaunchCount { get; private set; }

        public Task Started => _started.Task;

        public async Task<StandaloneUpdateCheckResultV1> RunAsync(CancellationToken cancellationToken)
        {
            LaunchCount++;
            _started.TrySetResult();
            await _release.Task;
            return result;
        }

        public void Complete() => _release.TrySetResult();
    }

    private sealed class FakeBridge : IConfiguratorUpdateBridge
    {
        private readonly Func<Task<UpdateActionAcceptance>> _start;
        private readonly Func<Task<UpdateStatusSnapshotV1>> _query;

        private FakeBridge(
            Func<Task<UpdateActionAcceptance>> start,
            Func<Task<UpdateStatusSnapshotV1>> query)
        {
            _start = start;
            _query = query;
        }

        public int StartCount { get; private set; }

        public static FakeBridge WithTerminalStatus(UpdateStatusSnapshotV1 status)
            => new(
                () => Task.FromResult(new UpdateActionAcceptance(true, false, "op-1", "accepted")),
                () => Task.FromResult(status));

        public static FakeBridge Rejected(string message)
            => new(
                () => Task.FromResult(new UpdateActionAcceptance(false, false, null, message)),
                () => throw new InvalidOperationException());

        public static FakeBridge UnavailableBeforeAcceptance()
            => new(
                () => throw new SupervisorBridgeUnavailableException(),
                () => throw new InvalidOperationException());

        public static FakeBridge UnavailableAfterAcceptance()
            => new(
                () => Task.FromResult(new UpdateActionAcceptance(true, false, "op-1", "accepted")),
                () => throw new SupervisorBridgeUnavailableException());

        public Task<UpdateActionAcceptance> StartCheckAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            return _start();
        }

        public Task<UpdateStatusSnapshotV1> QueryStatusAsync(CancellationToken cancellationToken) => _query();

        public Task<UpdateActionAcceptance> DismissAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<UpdateActionAcceptance> ClearDismissalAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
