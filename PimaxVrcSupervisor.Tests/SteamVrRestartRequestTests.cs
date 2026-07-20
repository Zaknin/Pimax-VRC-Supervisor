using Xunit;

namespace PimaxVrcSupervisor.Tests;

public sealed class SteamVrRestartRequestTests
{
    private static readonly SteamVrRuntimeSnapshot Runtime =
        new(100, DateTimeOffset.Parse("2026-07-20T00:00:00Z"));

    [Fact]
    public void FirstDeliveryIsAcceptedOnce()
    {
        var registry = CreateRegistry();

        var decision = registry.Accept(Request(), AcceptContext);

        Assert.True(decision.Accepted);
        Assert.Equal(SteamVrRestartRequestDisposition.Accepted, decision.Disposition);
        Assert.Equal(SteamVrRestartRequestState.Accepted, decision.Request?.State);
        Assert.Equal(Runtime.Identity, decision.Request?.OldRuntime);
    }

    [Fact]
    public void DuplicateWhileAcceptedObservesSameOperation()
    {
        var registry = CreateRegistry();
        var request = Request();
        var first = registry.Accept(request, AcceptContext);
        var contextCalls = 0;

        var duplicate = registry.Accept(request, () =>
        {
            contextCalls++;
            return AcceptContext();
        });

        Assert.Equal(SteamVrRestartRequestDisposition.DuplicateActive, duplicate.Disposition);
        Assert.Equal(first.Request?.OperationId, duplicate.Request?.OperationId);
        Assert.Equal(0, contextCalls);
    }

    [Fact]
    public void DuplicateWhileRunningObservesSameOperation()
    {
        var registry = CreateRegistry();
        var request = Request();
        var first = registry.Accept(request, AcceptContext);
        registry.MarkRunning(first.Request!.OperationId);

        var duplicate = registry.Accept(request, AcceptContext);

        Assert.Equal(SteamVrRestartRequestDisposition.DuplicateActive, duplicate.Disposition);
        Assert.Equal(SteamVrRestartRequestState.Running, duplicate.Request?.State);
    }

    [Theory]
    [InlineData((int)SteamVrRestartRequestState.Succeeded)]
    [InlineData((int)SteamVrRestartRequestState.Warning)]
    [InlineData((int)SteamVrRestartRequestState.Failed)]
    [InlineData((int)SteamVrRestartRequestState.Cancelled)]
    public void DuplicateAfterTerminalStateObservesResultWithoutExecution(
        int terminalStateValue)
    {
        var terminalState = (SteamVrRestartRequestState)terminalStateValue;
        var registry = CreateRegistry();
        var request = Request();
        var first = registry.Accept(request, AcceptContext);
        registry.MarkRunning(first.Request!.OperationId);
        registry.Complete(first.Request.OperationId, terminalState, "terminal", "detail");
        var contextCalls = 0;

        var duplicate = registry.Accept(request, () =>
        {
            contextCalls++;
            return AcceptContext();
        });

        Assert.Equal(SteamVrRestartRequestDisposition.DuplicateTerminal, duplicate.Disposition);
        Assert.Equal(terminalState, duplicate.Request?.State);
        Assert.Equal("terminal", duplicate.Request?.Result);
        Assert.Equal(0, contextCalls);
    }

    [Fact]
    public void RejectedRequestCannotBecomeAcceptedLater()
    {
        var registry = CreateRegistry();
        var request = Request();
        var rejected = registry.Accept(
            request,
            () => SteamVrRestartAcceptanceContext.Reject(
                SteamVrRestartRequestDisposition.RejectedBusy,
                "busy"));

        var duplicate = registry.Accept(request, AcceptContext);

        Assert.Equal(SteamVrRestartRequestDisposition.RejectedBusy, rejected.Disposition);
        Assert.Equal(SteamVrRestartRequestDisposition.DuplicateTerminal, duplicate.Disposition);
        Assert.Equal(SteamVrRestartRequestState.Rejected, duplicate.Request?.State);
    }

    [Fact]
    public void DifferentRequestWhileBusyIsRejectedAndNeverQueued()
    {
        var registry = CreateRegistry();
        var first = registry.Accept(Request(), AcceptContext);
        var busyRequest = Request();
        var contextCalls = 0;
        var busy = registry.Accept(busyRequest, () =>
        {
            contextCalls++;
            return AcceptContext();
        });
        registry.Complete(
            first.Request!.OperationId,
            SteamVrRestartRequestState.Succeeded,
            "done",
            null);

        var redelivery = registry.Accept(busyRequest, AcceptContext);

        Assert.Equal(SteamVrRestartRequestDisposition.RejectedBusy, busy.Disposition);
        Assert.Equal(0, contextCalls);
        Assert.Equal(SteamVrRestartRequestDisposition.DuplicateTerminal, redelivery.Disposition);
        Assert.Equal(SteamVrRestartRequestState.Rejected, redelivery.Request?.State);
    }

    [Fact]
    public void NewRequestAfterCompletionCanBeAccepted()
    {
        var registry = CreateRegistry();
        var first = registry.Accept(Request(), AcceptContext);
        registry.Complete(
            first.Request!.OperationId,
            SteamVrRestartRequestState.Succeeded,
            "done",
            null);

        var second = registry.Accept(Request(), AcceptContext);

        Assert.True(second.Accepted);
        Assert.NotEqual(first.Request.RequestId, second.Request?.RequestId);
        Assert.NotEqual(first.Request.OperationId, second.Request?.OperationId);
    }

    [Fact]
    public void TwoClientsDeliveringSameIdentityExecuteOnce()
    {
        var registry = CreateRegistry();
        var id = Guid.NewGuid().ToString("N");
        var first = registry.Accept(Request(id, "desktop-tui", "tui-a"), AcceptContext);
        var second = registry.Accept(Request(id, "steamvr-overlay", "overlay-b"), AcceptContext);

        Assert.True(first.Accepted);
        Assert.Equal(SteamVrRestartRequestDisposition.DuplicateActive, second.Disposition);
        Assert.Equal("desktop-tui", second.Request?.SourceClientType);
        Assert.Equal(first.Request?.OperationId, second.Request?.OperationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void MissingOrInvalidRequestIdentityFailsClosed(string requestId)
    {
        var registry = CreateRegistry();
        var contextCalls = 0;

        var decision = registry.Accept(Request(requestId), () =>
        {
            contextCalls++;
            return AcceptContext();
        });

        Assert.Equal(SteamVrRestartRequestDisposition.RejectedInvalid, decision.Disposition);
        Assert.Null(decision.Request);
        Assert.Equal(0, contextCalls);
    }

    [Theory]
    [InlineData("", "session")]
    [InlineData("client", "")]
    public void MissingSourceAttributionFailsClosed(string clientType, string clientInstance)
    {
        var registry = CreateRegistry();

        var decision = registry.Accept(
            Request(sourceClientType: clientType, sourceClientInstanceId: clientInstance),
            AcceptContext);

        Assert.Equal(SteamVrRestartRequestDisposition.RejectedInvalid, decision.Disposition);
    }

    [Fact]
    public void EquivalentGuidFormatsDeduplicate()
    {
        var registry = CreateRegistry();
        var guid = Guid.NewGuid();
        var first = registry.Accept(Request(guid.ToString("D")), AcceptContext);
        var second = registry.Accept(Request(guid.ToString("N").ToUpperInvariant()), AcceptContext);

        Assert.True(first.Accepted);
        Assert.Equal(SteamVrRestartRequestDisposition.DuplicateActive, second.Disposition);
    }

    [Fact]
    public void AcceptedStateMovesToRunningThenTerminalMonotonically()
    {
        var registry = CreateRegistry();
        var accepted = registry.Accept(Request(), AcceptContext);

        var running = registry.MarkRunning(accepted.Request!.OperationId);
        var terminal = registry.Complete(
            accepted.Request.OperationId,
            SteamVrRestartRequestState.Succeeded,
            "done",
            null);
        var attemptedRegression = registry.MarkRunning(accepted.Request.OperationId);

        Assert.Equal(SteamVrRestartRequestState.Running, running?.State);
        Assert.Equal(SteamVrRestartRequestState.Succeeded, terminal?.State);
        Assert.Equal(SteamVrRestartRequestState.Succeeded, attemptedRegression?.State);
    }

    [Fact]
    public void TerminalStateCannotBeOverwritten()
    {
        var registry = CreateRegistry();
        var accepted = registry.Accept(Request(), AcceptContext);
        var first = registry.Complete(
            accepted.Request!.OperationId,
            SteamVrRestartRequestState.Failed,
            "failed",
            "first");

        var second = registry.Complete(
            accepted.Request.OperationId,
            SteamVrRestartRequestState.Succeeded,
            "succeeded",
            null);

        Assert.Equal(SteamVrRestartRequestState.Failed, first?.State);
        Assert.Equal(SteamVrRestartRequestState.Failed, second?.State);
        Assert.Equal("first", second?.Error);
    }

    [Fact]
    public void ShutdownIssuanceCanBeClaimedExactlyOnce()
    {
        var registry = CreateRegistry();
        var accepted = registry.Accept(Request(), AcceptContext);
        registry.MarkRunning(accepted.Request!.OperationId);

        var first = registry.TryBeginShutdownIssuance(
            accepted.Request.OperationId,
            Runtime.Identity,
            ownedRuntimePresent: true,
            supervisorShuttingDown: false);
        var second = registry.TryBeginShutdownIssuance(
            accepted.Request.OperationId,
            Runtime.Identity,
            ownedRuntimePresent: true,
            supervisorShuttingDown: false);

        Assert.True(first.Allowed);
        Assert.False(second.Allowed);
        Assert.Equal("shutdown-already-attempted", second.Reason);
    }

    [Fact]
    public void SuccessfulProcessCreationRecordsOneIssuance()
    {
        var registry = CreateRegistry();
        var accepted = registry.Accept(Request(), AcceptContext);
        registry.TryBeginShutdownIssuance(
            accepted.Request!.OperationId,
            Runtime.Identity,
            ownedRuntimePresent: true,
            supervisorShuttingDown: false);

        var recorded = registry.RecordShutdownProcessCreation(
            accepted.Request.OperationId,
            succeeded: true,
            error: null);
        var duplicateRecord = registry.RecordShutdownProcessCreation(
            accepted.Request.OperationId,
            succeeded: true,
            error: null);

        Assert.Equal(1, recorded?.ShutdownIssueCount);
        Assert.Equal(1, duplicateRecord?.ShutdownIssueCount);
    }

    [Fact]
    public void FailedProcessCreationNeverReopensIssuance()
    {
        var registry = CreateRegistry();
        var accepted = registry.Accept(Request(), AcceptContext);
        registry.TryBeginShutdownIssuance(
            accepted.Request!.OperationId,
            Runtime.Identity,
            ownedRuntimePresent: true,
            supervisorShuttingDown: false);
        registry.RecordShutdownProcessCreation(
            accepted.Request.OperationId,
            succeeded: false,
            error: "creation failed");

        var retry = registry.TryBeginShutdownIssuance(
            accepted.Request.OperationId,
            Runtime.Identity,
            ownedRuntimePresent: true,
            supervisorShuttingDown: false);

        Assert.False(retry.Allowed);
        Assert.Equal("shutdown-already-attempted", retry.Reason);
        Assert.Equal(0, retry.Request?.ShutdownIssueCount);
        Assert.Equal("creation failed", retry.Request?.Error);
    }

    [Theory]
    [InlineData(true, true, false, "supervisor-shutdown")]
    [InlineData(false, false, false, "owned-runtime-not-present")]
    [InlineData(false, true, true, "runtime-ownership-mismatch")]
    public void ShutdownGuardRequiresActiveOwnedRuntimeAndNoFinalShutdown(
        bool supervisorShuttingDown,
        bool ownedRuntimePresent,
        bool wrongRuntime,
        string expectedReason)
    {
        var registry = CreateRegistry();
        var accepted = registry.Accept(Request(), AcceptContext);
        var identity = wrongRuntime
            ? new SteamVrRuntimeIdentity(Runtime.Pid + 1, Runtime.StartTime)
            : Runtime.Identity;

        var decision = registry.TryBeginShutdownIssuance(
            accepted.Request!.OperationId,
            identity,
            ownedRuntimePresent,
            supervisorShuttingDown);

        Assert.False(decision.Allowed);
        Assert.Equal(expectedReason, decision.Reason);
    }

    [Fact]
    public void TerminalOperationCannotIssueShutdown()
    {
        var registry = CreateRegistry();
        var accepted = registry.Accept(Request(), AcceptContext);
        registry.Complete(
            accepted.Request!.OperationId,
            SteamVrRestartRequestState.Failed,
            "failed",
            null);

        var decision = registry.TryBeginShutdownIssuance(
            accepted.Request.OperationId,
            Runtime.Identity,
            ownedRuntimePresent: true,
            supervisorShuttingDown: false);

        Assert.False(decision.Allowed);
        Assert.Equal("operation-terminal", decision.Reason);
    }

    [Fact]
    public void TerminalStorageRemainsBounded()
    {
        var registry = CreateRegistry(maximumEntries: 4);
        for (var index = 0; index < 20; index++)
        {
            var accepted = registry.Accept(Request(), AcceptContext);
            registry.Complete(
                accepted.Request!.OperationId,
                SteamVrRestartRequestState.Succeeded,
                "done",
                null);
        }

        Assert.InRange(registry.Count, 1, 4);
    }

    [Fact]
    public void ActiveEntryIsNeverPrunedByTerminalCapacity()
    {
        var registry = CreateRegistry(maximumEntries: 3);
        var active = registry.Accept(Request(), AcceptContext);
        for (var index = 0; index < 10; index++)
        {
            var terminal = registry.Accept(Request(), AcceptContext);
            registry.Complete(
                terminal.Request!.OperationId,
                SteamVrRestartRequestState.Succeeded,
                "done",
                null);
        }

        Assert.NotNull(registry.Get(active.Request!.RequestId));
    }

    [Fact]
    public void ExpiredTerminalEntriesAreRemovedDeterministically()
    {
        var now = DateTimeOffset.Parse("2026-07-20T00:00:00Z");
        var registry = CreateRegistry(
            clock: () => now,
            terminalRetention: TimeSpan.FromMinutes(10));
        var old = registry.Accept(Request(), AcceptContext);
        registry.Complete(
            old.Request!.OperationId,
            SteamVrRestartRequestState.Succeeded,
            "done",
            null);
        now = now.AddMinutes(11);

        registry.Accept(Request(), AcceptContext);

        Assert.Null(registry.Get(old.Request.RequestId));
    }

    [Fact]
    public void SourceCodeTiesOnlyProductionShutdownAdapterToPerOperationGuard()
    {
        var program = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var graceful = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "SteamVrGracefulShutdown.cs"));

        Assert.Contains("RequestSteamVrShutdownOnceAsync", program, StringComparison.Ordinal);
        Assert.Contains("TryBeginShutdownIssuance", program, StringComparison.Ordinal);
        Assert.Contains("shutdown-already-attempted", File.ReadAllText(SourcePath("PimaxVrcSupervisor", "SteamVrRestartRequests.cs")), StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(graceful, "ArgumentList.Add(\"-shutdown\")"));
        Assert.Equal(
            1,
            CountOccurrences(program, "_steamVrGracefulShutdown.RequestShutdownAsync(cancellationToken)"));
        var runtime = Slice(
            program,
            "private sealed class AppSupervisorSteamVrRestartRuntime",
            "private async Task RunSteamVrStartOperationAsync");
        Assert.Contains("RequestSteamVrShutdownOnceAsync", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("_steamVrGracefulShutdown.RequestShutdownAsync", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyTcpRestartFailsClosedAndConsoleMintsIdentityAfterConfirmation()
    {
        var program = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var console = Slice(program, "private void ExecuteConsoleSteamVrControl()", "private static bool ReadConsoleActionConfirmation()");

        Assert.Contains("requires a fresh confirmed action-json request", program, StringComparison.Ordinal);
        Assert.True(
            console.IndexOf("ReadConsoleActionConfirmation()", StringComparison.Ordinal)
            < console.IndexOf("RestartVrSessionFromConfirmedConsole()", StringComparison.Ordinal));
        Assert.Contains("Guid.NewGuid().ToString(\"N\")", program, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredRestartRequiresIdentityAndSourceAttribution()
    {
        var program = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var action = Slice(
            program,
            "private SupervisorCommandResult ExecuteConfirmedVrSessionRestartAction",
            "private string StartSteamVrLegacyCommand()");

        Assert.Contains("request.RequestId", action, StringComparison.Ordinal);
        Assert.Contains("request.SourceClientType", action, StringComparison.Ordinal);
        Assert.Contains("request.SourceClientInstanceId", action, StringComparison.Ordinal);
        Assert.Contains("request.Confirmed != true", action, StringComparison.Ordinal);
    }

    [Fact]
    public void UsbRecoveryCannotEnterRestartRequestAcceptance()
    {
        var program = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));
        var usb = Slice(
            program,
            "private async Task ExecuteScopedDeviceRecoveryPlanAsync",
            "private PimaxServiceReconnect? DetectPimaxServiceLogReconnect()");

        Assert.DoesNotContain("TryAcceptVrSessionRestart", usb, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestSteamVrShutdownOnceAsync", usb, StringComparison.Ordinal);
        Assert.DoesNotContain("RestartVrSessionCommandName", usb, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase32CAttributionAndXsOverlayExclusionRemainPresent()
    {
        var usbRecovery = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "UsbDeviceRecovery.cs"));
        var program = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "Program.cs"));

        Assert.Contains("ContainerStableId", usbRecovery, StringComparison.Ordinal);
        Assert.Contains("SteamVrRadioDongle", usbRecovery, StringComparison.Ordinal);
        Assert.Contains("IsXsOverlayApplication", program, StringComparison.Ordinal);
        Assert.Contains("ExecuteScopedDeviceRecoveryPlanAsync", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductVersionRemains131()
    {
        var project = File.ReadAllText(SourcePath("PimaxVrcSupervisor", "PimaxVrcSupervisor.csproj"));
        Assert.Contains("<Version>1.3.1</Version>", project, StringComparison.Ordinal);
    }

    private static SteamVrRestartRequestRegistry CreateRegistry(
        Func<DateTimeOffset>? clock = null,
        int maximumEntries = SteamVrRestartRequestRegistry.DefaultMaximumEntries,
        TimeSpan? terminalRetention = null)
        => new(clock, maximumEntries, terminalRetention);

    private static SteamVrRestartAcceptanceContext AcceptContext()
        => SteamVrRestartAcceptanceContext.Accept(Runtime, resumeVrChat: true);

    private static SteamVrRestartRequestEnvelope Request(
        string? requestId = null,
        string sourceClientType = "desktop-tui",
        string sourceClientInstanceId = "client-session")
        => new(
            requestId ?? Guid.NewGuid().ToString("N"),
            sourceClientType,
            sourceClientInstanceId,
            DateTimeOffset.Parse("2026-07-20T00:00:00Z"));

    private static int CountOccurrences(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string SourcePath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            Path.Combine(segments)));
}
