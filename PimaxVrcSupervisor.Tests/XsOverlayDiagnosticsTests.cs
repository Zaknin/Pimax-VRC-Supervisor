using System.Text.Json;
using PimaxVrcSupervisor;
using Xunit;

public sealed class XsOverlayDiagnosticsTests
{
    [Fact]
    public void Write_PersistsOneJsonRecordWithRequiredEnvelopeAndPayload()
    {
        using var temp = new TempDirectory();
        using var sink = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test", supervisorProcessId: 1234);
        var diagnosticEvent = Event(
            "xsOverlayLaunchTargetDiscovered",
            launchRoute: XsOverlayLaunchRoute.SteamApp.ToString(),
            validatedAppId: "1173510",
            registrationSource: @"...\steamapps\appmanifest_1173510.acf");

        sink.Write(diagnosticEvent);

        using var document = ReadSingleEvent(sink.ActivePath);
        var root = document.RootElement;
        Assert.Equal(XsOverlayOperationalDiagnosticsSchema.Version, root.GetProperty("schemaVersion").GetString());
        Assert.Equal("xsOverlaySafeMonitorShutdown", root.GetProperty("operation").GetString());
        Assert.Equal("xsOverlayLaunchTargetDiscovered", root.GetProperty("eventType").GetString());
        Assert.Equal("xso-test", root.GetProperty("correlationId").GetString());
        Assert.Equal(1234, root.GetProperty("supervisorProcessId").GetInt32());
        Assert.Equal(100, root.GetProperty("originalPid").GetInt32());
        Assert.Equal("SteamApp", root.GetProperty("launchRoute").GetString());
        Assert.Equal("1173510", root.GetProperty("validatedAppId").GetString());
        Assert.Equal(@"...\steamapps\appmanifest_1173510.acf", root.GetProperty("registrationSource").GetString());
        Assert.False(root.TryGetProperty("payload", out _));
    }

    [Fact]
    public void Write_AppendsCompleteJsonLinesInCallbackOrder()
    {
        using var temp = new TempDirectory();
        using var sink = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test");
        File.WriteAllText(sink.ActivePath, "{\"existing\":true}" + Environment.NewLine);

        sink.Write(Event("xsOverlayDetectionStarted"));
        sink.Write(Event("complete", outcome: "completed"));

        var lines = File.ReadAllLines(sink.ActivePath);
        Assert.Equal(3, lines.Length);
        Assert.Equal("xsOverlayDetectionStarted", JsonDocument.Parse(lines[1]).RootElement.GetProperty("eventType").GetString());
        Assert.Equal("complete", JsonDocument.Parse(lines[2]).RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public void Write_CreatesMissingDirectory()
    {
        using var temp = new TempDirectory();
        var directory = Path.Combine(temp.Path, "missing", "XSOverlay");
        using var sink = new XsOverlayDiagnosticSink(directory, "Supervisor", "test");

        sink.Write(Event("xsOverlayDetectionStarted"));

        Assert.True(Directory.Exists(directory));
        Assert.True(File.Exists(sink.ActivePath));
    }

    [Fact]
    public void Write_DoesNotRotateWhenActiveFileRemainsBelowThreshold()
    {
        using var temp = new TempDirectory();
        using var sink = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test", maxActiveBytes: 4096);
        File.WriteAllText(sink.ActivePath, "{\"existing\":true}" + Environment.NewLine);

        sink.Write(Event("xsOverlayDetectionStarted"));

        Assert.True(File.Exists(sink.ActivePath));
        Assert.False(File.Exists(sink.ActivePath + ".1"));
    }

    [Fact]
    public void Write_RotatesActiveFileAndRetainsTwoGenerations()
    {
        using var temp = new TempDirectory();
        using var sink = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test", maxActiveBytes: 16);
        File.WriteAllText(sink.ActivePath, "active");
        File.WriteAllText(sink.ActivePath + ".1", "rotated-one");
        File.WriteAllText(sink.ActivePath + ".2", "rotated-two");

        sink.Write(Event("xsOverlayDetectionStarted"));

        Assert.Contains("xsOverlayDetectionStarted", File.ReadAllText(sink.ActivePath), StringComparison.Ordinal);
        Assert.Equal("active", File.ReadAllText(sink.ActivePath + ".1"));
        Assert.Equal("rotated-one", File.ReadAllText(sink.ActivePath + ".2"));
        Assert.False(File.Exists(sink.ActivePath + ".3"));
    }

    [Fact]
    public void Write_FailuresNeverEscape()
    {
        using var temp = new TempDirectory();

        using var createFailure = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test", createDirectory: _ => throw new UnauthorizedAccessException());
        using var serializeFailure = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test", serialize: _ => throw new InvalidOperationException("serialize"));
        using var appendFailure = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test", appendLine: (_, _) => throw new IOException("append"));
        using var rotationFailure = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test", maxActiveBytes: 1, moveFile: (_, _) => throw new IOException("move"));
        File.WriteAllText(rotationFailure.ActivePath, "x");

        createFailure.Write(Event("xsOverlayDetectionStarted"));
        serializeFailure.Write(Event("xsOverlayDetectionStarted"));
        appendFailure.Write(Event("xsOverlayDetectionStarted"));
        rotationFailure.Write(Event("xsOverlayDetectionStarted"));
    }

    [Fact]
    public void Dispatch_OffersOneEventToPersistentSinkAndOptionalSupervisorDiagnostics()
    {
        var sink = new CountingSink();
        var optionalMessages = new List<string>();

        XsOverlayDiagnosticDispatch.Write(
            Event("xsOverlayDetectionStarted"),
            sink,
            optionalMessages.Add,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Equal(1, sink.Count);
        Assert.Single(optionalMessages);
        Assert.Contains("\"eventType\":\"xsOverlayDetectionStarted\"", optionalMessages[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_SinkFailureDoesNotBlockOptionalSupervisorDiagnostics()
    {
        var optionalMessages = new List<string>();

        XsOverlayDiagnosticDispatch.Write(
            Event("complete", outcome: "completed"),
            new ThrowingSink(),
            optionalMessages.Add,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Single(optionalMessages);
    }

    [Fact]
    public async Task CoordinatorEventsPersistWhenOptionalSupervisorDiagnosticsAreDisabled()
    {
        using var temp = new TempDirectory();
        using var sink = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test");
        var platform = new FakePlatform();
        var coordinator = new XsOverlaySafeMonitorTransitionCoordinator(
            platform,
            new FakeLauncher(),
            new FakeWindowObserver(),
            _ => Task.CompletedTask,
            sink.Write,
            _ => { },
            (_, _) => Task.CompletedTask,
            gracefulStopTimeout: TimeSpan.FromMilliseconds(1),
            processPollInterval: TimeSpan.FromMilliseconds(1),
            topologySettleDuration: TimeSpan.FromMilliseconds(1),
            restartVerificationTimeout: TimeSpan.FromMilliseconds(1),
            operationIdProvider: () => "xso-transition");

        var result = await coordinator.RunAsync(true, CancellationToken.None);

        Assert.True(result.MonitorShutdownSucceeded);
        var lines = File.ReadAllLines(sink.ActivePath);
        Assert.Equal(["xsOverlayDetectionStarted", "xsOverlayNotRunning", "secondaryMonitorShutdownStarted", "secondaryMonitorShutdownCompleted", "xsOverlayRestartSkipped", "complete"],
            lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("eventType").GetString()!).ToArray());
        Assert.All(lines, line =>
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("xso-transition", document.RootElement.GetProperty("correlationId").GetString());
        });
    }

    [Fact]
    public void EventSpecificPayloadsPreserveFailureOutcomeAndWindowState()
    {
        using var temp = new TempDirectory();
        using var sink = new XsOverlayDiagnosticSink(temp.Path, "Supervisor", "test");

        sink.Write(Event("xsOverlayLaunchTargetUnavailable", skipReason: "no registered launch route"));
        sink.Write(Event("xsOverlayDirectRestartRefused", skipReason: "no registered launch route"));
        sink.Write(Event("xsOverlayStopCompleted", stopMechanism: "force", stopElapsedMilliseconds: 12.5));
        sink.Write(Event("secondaryMonitorShutdownCompleted", monitorResult: "succeeded"));
        sink.Write(Event("displayTopologySettled", topologySettleElapsedMilliseconds: 2000));
        sink.Write(Event("xsOverlayBrokeredRestartRequested", launchRoute: "SteamApp", restartResult: "shellSteamUri"));
        sink.Write(Event("xsOverlayBrokeredRestartCompleted", restartedPid: 200, restartResult: "oneInstanceVerified"));
        sink.Write(Event("xsOverlayWindowVerificationCompleted", windowVerificationResult: XsOverlayWindowVerificationResult.NoUnwantedDesktopWindow.ToString()));
        sink.Write(Event("xsOverlayWindowVerificationCompleted", windowVerificationResult: XsOverlayWindowVerificationResult.UnwantedDesktopWindowDetected.ToString()));
        sink.Write(Event("xsOverlayWindowVerificationCompleted", windowVerificationResult: XsOverlayWindowVerificationResult.Unavailable.ToString()));
        sink.Write(Event("xsOverlayUnwantedDesktopWindowDetected", windowVerificationResult: XsOverlayWindowVerificationResult.UnwantedDesktopWindowDetected.ToString()));
        sink.Write(Event("complete", outcome: "completed"));

        var records = File.ReadAllLines(sink.ActivePath)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        Assert.Equal("no registered launch route", records[0].GetProperty("skipReason").GetString());
        Assert.Equal("no registered launch route", records[1].GetProperty("skipReason").GetString());
        Assert.Equal("force", records[2].GetProperty("stopMechanism").GetString());
        Assert.Equal(12.5, records[2].GetProperty("stopElapsedMilliseconds").GetDouble());
        Assert.Equal("succeeded", records[3].GetProperty("monitorResult").GetString());
        Assert.Equal(2000, records[4].GetProperty("topologySettleElapsedMilliseconds").GetDouble());
        Assert.Equal("SteamApp", records[5].GetProperty("launchRoute").GetString());
        Assert.Equal("shellSteamUri", records[5].GetProperty("restartResult").GetString());
        Assert.Equal(200, records[6].GetProperty("restartedPid").GetInt32());
        Assert.Equal("oneInstanceVerified", records[6].GetProperty("restartResult").GetString());
        Assert.Equal("NoUnwantedDesktopWindow", records[7].GetProperty("windowVerificationResult").GetString());
        Assert.Equal("UnwantedDesktopWindowDetected", records[8].GetProperty("windowVerificationResult").GetString());
        Assert.Equal("Unavailable", records[9].GetProperty("windowVerificationResult").GetString());
        Assert.Equal("UnwantedDesktopWindowDetected", records[10].GetProperty("windowVerificationResult").GetString());
        Assert.Equal("completed", records[11].GetProperty("outcome").GetString());
    }

    private static JsonDocument ReadSingleEvent(string path)
        => JsonDocument.Parse(Assert.Single(File.ReadAllLines(path)));

    private static XsOverlayMonitorTransitionEvent Event(
        string eventType,
        string? launchRoute = null,
        string? validatedAppId = null,
        string? registrationSource = null,
        string? skipReason = null,
        string? outcome = null,
        string? stopMechanism = null,
        double? stopElapsedMilliseconds = null,
        string? monitorResult = null,
        double? topologySettleElapsedMilliseconds = null,
        string? restartResult = null,
        int? restartedPid = null,
        string? windowVerificationResult = null)
        => new()
        {
            OperationId = "xso-test",
            CorrelationId = "xso-test",
            TimestampUtc = DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            EventType = eventType,
            OriginalPid = 100,
            RestartedPid = restartedPid,
            SessionId = 1,
            ExecutableIdentity = "XSOverlay",
            SafeExecutablePath = @"...\XSOverlay\XSOverlay.exe",
            RestartMechanism = "brokered",
            LaunchRoute = launchRoute,
            ValidatedAppId = validatedAppId,
            RegistrationSource = registrationSource,
            WindowVerificationResult = windowVerificationResult,
            StopMechanism = stopMechanism,
            StopElapsedMilliseconds = stopElapsedMilliseconds,
            MonitorResult = monitorResult,
            TopologySettleElapsedMilliseconds = topologySettleElapsedMilliseconds,
            RestartResult = restartResult,
            Outcome = outcome,
            SkipReason = skipReason
        };

    private sealed class CountingSink : IXsOverlayDiagnosticSink
    {
        public int Count { get; private set; }
        public void Write(XsOverlayMonitorTransitionEvent diagnosticEvent) => Count++;
    }

    private sealed class ThrowingSink : IXsOverlayDiagnosticSink
    {
        public void Write(XsOverlayMonitorTransitionEvent diagnosticEvent) => throw new IOException("sink failed");
    }

    private sealed class FakePlatform : IXsOverlayProcessPlatform
    {
        public Task<IReadOnlyList<XsOverlayProcessSnapshot>> FindRunningAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<XsOverlayProcessSnapshot>>([]);

        public Task<bool> RequestGracefulCloseAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> IsSameProcessRunningAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> ForceTerminateAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class FakeLauncher : IXsOverlayLauncher
    {
        public XsOverlayLaunchTarget Discover(XsOverlayProcessSnapshot process)
            => new(XsOverlayLaunchRoute.Unavailable, null, null, null, false, "not needed", false);

        public Task<XsOverlayLaunchRequestResult> RequestLaunchAsync(XsOverlayLaunchTarget target, CancellationToken cancellationToken)
            => Task.FromResult(new XsOverlayLaunchRequestResult(false, "notNeeded", "not needed"));
    }

    private sealed class FakeWindowObserver : IXsOverlayWindowObserver
    {
        public Task<XsOverlayWindowVerificationResult> ObserveAsync(XsOverlayProcessSnapshot process, CancellationToken cancellationToken)
            => Task.FromResult(XsOverlayWindowVerificationResult.Unavailable);
    }
}
