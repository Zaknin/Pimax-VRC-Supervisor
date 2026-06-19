using System.Text.Json;
using Xunit;
using PimaxVrcSupervisor.Diagnostics;

namespace PimaxVrcSupervisor.Tests;

public sealed class UvcIsolationSessionCorrelationTests
{
    [Fact]
    public void StartCreatesSessionDirectorySchemasAndHashesAndRefusesOverwrite()
    {
        using var temp = new TempDirectory();
        var sessionPath = Path.Combine(temp.Path, "session-a");
        var environment = new FakeUvcEnvironment();

        var result = new UvcIsolationSessionService(environment).Start(UvcIsolationStartRequest.Parse([
            "uvc-isolation-session-start-json",
            "--output", sessionPath,
            "--label", "sleep wake reconnect",
            "--scenario", "sleepWakeReconnect",
            "--vive-connected", "no",
            "--sleep-wake", "yes",
            "--notes", "synthetic"
        ], null));

        Assert.Equal(UvcIsolationSchemas.StartResult, result.Schema);
        Assert.True(File.Exists(Path.Combine(sessionPath, "session.json")));
        Assert.True(File.Exists(Path.Combine(sessionPath, "start-system-state.json")));
        Assert.True(File.Exists(Path.Combine(sessionPath, "start-process-state.json")));
        Assert.True(File.Exists(Path.Combine(sessionPath, "start-uvc-inventory.json")));
        Assert.True(File.Exists(Path.Combine(sessionPath, "start-config-snapshot.json")));
        Assert.True(File.ReadAllLines(Path.Combine(sessionPath, "SHA256SUMS.txt")).Length >= 5);
        Assert.Single(environment.UvcCaptures);
        Assert.Throws<IOException>(() => new UvcIsolationSessionService(environment).Start(UvcIsolationStartRequest.Parse([
            "--output", sessionPath, "--label", "overwrite", "--scenario", "custom"
        ], null)));
    }

    [Fact]
    public void StartHandlesUnavailableConfigAndNoUvcDevices()
    {
        using var temp = new TempDirectory();
        var environment = new FakeUvcEnvironment
        {
            Config = new(UvcIsolationSchemas.ConfigSnapshot, DateTimeOffset.UtcNow, "unavailable", null, null, null, null, null, null, null, null, null, null, null, "test", ["config unavailable"]),
            Uvc = new(UvcIsolationSchemas.UvcInventory, DateTimeOffset.UtcNow, [], ["no UVC devices"], [])
        };

        var result = new UvcIsolationSessionService(environment).Start(new(
            Path.Combine(temp.Path, "session"),
            "no devices",
            ["custom"],
            new("unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", null, "operator"),
            null,
            null));

        Assert.Contains(result.Warnings, item => item.Contains("config unavailable"));
        Assert.Contains(result.Warnings, item => item.Contains("no UVC devices"));
    }

    [Fact]
    public void AnnotationIsAppendOnlyAndSnapshotIsExplicit()
    {
        using var temp = new TempDirectory();
        var sessionPath = CreateSession(temp, new FakeUvcEnvironment());
        var environment = new FakeUvcEnvironment();
        var service = new UvcIsolationSessionService(environment);

        service.Annotate(new(sessionPath, "Vive physically disconnected", DateTimeOffset.Parse("2026-06-19T20:00:00Z"), "operator", false, null));
        service.Annotate(new(sessionPath, "session still stable", null, "operator", true, "one-shot snapshot"));

        var annotations = File.ReadAllLines(Path.Combine(sessionPath, "annotations.jsonl"));
        Assert.True(annotations.Length == 2);
        Assert.Contains("operator", annotations[0]);
        Assert.Single(environment.UvcCaptures);
        Assert.Contains(Directory.EnumerateFiles(sessionPath), path => Path.GetFileName(path).Contains("annotation-") && path.EndsWith("-uvc-inventory.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FinishCapturesSameBootAndChangedBootSessions()
    {
        using var temp = new TempDirectory();
        var environment = new FakeUvcEnvironment();
        var sessionPath = CreateSession(temp, environment);
        var service = new UvcIsolationSessionService(environment);

        var stable = service.Finish(new(sessionPath, "stable", "same boot", null, null));

        Assert.False(stable.BootIdentityChanged);
        Assert.True(File.Exists(Path.Combine(sessionPath, "session-final.json")));
        using var temp2 = new TempDirectory();
        var rebootEnv = new FakeUvcEnvironment();
        var rebootPath = CreateSession(temp2, rebootEnv);
        rebootEnv.BootFingerprint = "boot-after-reboot";
        var rebooted = new UvcIsolationSessionService(rebootEnv).Finish(new(rebootPath, "unexpectedReboot", null, null, null));
        Assert.True(rebooted.BootIdentityChanged);
    }

    [Fact]
    public void FinishHashesDumpAndParsesWindbgReport()
    {
        using var temp = new TempDirectory();
        var dump = temp.WriteFile("sample.dmp", "not a real dump");
        var report = temp.WriteFile("windbg.txt", """
DRIVER_IRQL_NOT_LESS_OR_EQUAL (d1)
Arg1: 0000000000000000
MODULE_NAME: usbvideo
IMAGE_NAME: usbvideo.sys
SYMBOL_NAME: usbvideo!CaptureProcessDataPayload+0xb8
FAILURE_BUCKET_ID: AV_usbvideo!CaptureProcessDataPayload
PROCESS_NAME: System
""");
        var sessionPath = CreateSession(temp, new FakeUvcEnvironment());

        var result = new UvcIsolationSessionService(new FakeUvcEnvironment()).Finish(new(sessionPath, "bugcheck", null, dump, report));

        Assert.Equal("available", result.CrashMetadata.Dump!.Status);
        Assert.Equal("0xD1", result.CrashMetadata.Windbg!.BugcheckCode);
        Assert.Equal("usbvideo!CaptureProcessDataPayload+0xb8", result.CrashMetadata.Windbg.SymbolName);
        Assert.True(result.CrashMetadata.MatchesRepeatedUsbVideoPayloadBucket);
    }

    [Fact]
    public void WindbgParserHandlesMissingMalformedAndUnrelatedReports()
    {
        var empty = UvcWindbgTextParser.Parse("unrelated text");
        Assert.NotEmpty(empty.Warnings);

        var unrelated = UvcWindbgTextParser.Parse("""
KMODE_EXCEPTION_NOT_HANDLED (1e)
MODULE_NAME: nt
FAILURE_BUCKET_ID: AV_nt!Other
""");
        Assert.Equal("0x1E", unrelated.BugcheckCode);
        Assert.Equal("AV_nt!Other", unrelated.FailureBucketId);
    }

    [Fact]
    public void UvcInventoryClassifiesVivePimaxAndUnknownDeterministically()
    {
        var parent = Raw("USB\\VID_1234&PID_5678\\PARENT", "1234", "5678", "USB Root Hub");
        var byId = new Dictionary<string, PimaxUsbRawDeviceRecord>(StringComparer.OrdinalIgnoreCase)
        {
            [parent.InstanceId] = parent
        };
        var vive = WindowsUvcIsolationEnvironment.ToUvcRecord(Raw("USB\\VID_0BB4&PID_0321&MI_00\\A", "0BB4", "0321", "HTC Multimedia Camera", parent.InstanceId), byId);
        var pimax = WindowsUvcIsolationEnvironment.ToUvcRecord(Raw("USB\\VID_34A4&PID_0012&MI_00\\B", "34A4", "0012", "UVC Camera", parent.InstanceId), byId);
        var unknown = WindowsUvcIsolationEnvironment.ToUvcRecord(Raw("USB\\VID_ABCD&PID_EF01&MI_00\\C", "ABCD", "EF01", "USB Video", parent.InstanceId), byId);

        Assert.Equal("viveOrHtcUvc", vive.Classification);
        Assert.Equal("pimaxLikeUvc", pimax.Classification);
        Assert.Equal("unknownUvc", unknown.Classification);
        Assert.DoesNotContain("\\A", vive.SanitizedInstanceHash);
    }

    [Fact]
    public void AnalyzerReportsGroupsContradictionsRepeatedBucketAndOneNextTest()
    {
        using var temp = new TempDirectory();
        var stable = CreateSession(temp, new FakeUvcEnvironment(), "stable-pimax-only", ["pimaxOnly"]);
        new UvcIsolationSessionService(new FakeUvcEnvironment()).Finish(new(stable, "stable", null, null, null));
        var crash = CreateSession(temp, new FakeUvcEnvironment(), "crash-vive-vrcft", ["viveConnectedVrcftRunning", "sleepWakeReconnect"]);
        var report = temp.WriteFile("usbvideo.txt", """
DRIVER_IRQL_NOT_LESS_OR_EQUAL (d1)
SYMBOL_NAME: usbvideo!CaptureProcessDataPayload+0xb8
FAILURE_BUCKET_ID: AV_usbvideo!CaptureProcessDataPayload
""");
        new UvcIsolationSessionService(new FakeUvcEnvironment()).Finish(new(crash, "bugcheck", null, null, report));
        var crash2 = CreateSession(temp, new FakeUvcEnvironment(), "crash-no-auto", ["supervisorReconnectAutomationDisabled"]);
        new UvcIsolationSessionService(new FakeUvcEnvironment()).Finish(new(crash2, "bugcheck", null, null, report));

        var result = new UvcIsolationAnalyzer().Analyze(new([stable, crash, crash2], [], [], null, null));

        Assert.Equal(UvcIsolationSchemas.Analysis, result.Schema);
        Assert.Contains(result.ComparisonGroups, group => group.Dimension == "failureBucket" && group.CrashedSessions == 2);
        Assert.Contains(result.Findings, finding => finding.Topic == "repeatedFailureBucket" && finding.Label == "repeated association");
        Assert.Single(new[] { result.RecommendedNextTest });
    }

    [Fact]
    public void StaticSafetyScanContainsNoForbiddenActionsInNewImplementation()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "UvcIsolationSessionCorrelation.cs"));
        var forbidden = new[]
        {
            "System.Threading.Timer", "FileSystemWatcher", "ManagementEventWatcher", "RegisterDeviceNotification",
            "CM_Disable_DevNode", "SetupDiCallClassInstaller", "IOCTL_USB_HUB_CYCLE_PORT", "Process.Start(",
            "Kill(", "Stop-Process", "Restart-Service", "Set-ScheduledTask", "schtasks", "windbg.exe", "cdb.exe", "ProcessStartInfo",
            "CrashDumpEnabled", "HttpClient"
        };
        foreach (var token in forbidden) Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizedBaselineFixtureContainsNoPrivateIdentifiers()
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "UvcIsolation", "june19-sanitized-session.json"));
        using var doc = JsonDocument.Parse(fixture);
        Assert.Equal("AV_usbvideo!CaptureProcessDataPayload", doc.RootElement.GetProperty("failureBucket").GetString());
        Assert.DoesNotContain("C:\\Users", fixture, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("USB\\VID_0BB4&PID_0321&MI_00\\", fixture, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".dmp", fixture, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateSession(TempDirectory temp, FakeUvcEnvironment environment, string label = "session", string[]? scenarios = null)
    {
        var path = Path.Combine(temp.Path, Guid.NewGuid().ToString("N"));
        new UvcIsolationSessionService(environment).Start(new(
            path,
            label,
            scenarios ?? ["sleepWakeReconnect"],
            new("yes", "yes", "yes", "yes", "yes", "yes", "unknown", null, "operator"),
            null,
            null));
        return path;
    }

    private static PimaxUsbRawDeviceRecord Raw(string id, string vid, string pid, string name, string? parent = null)
        => new(id, parent, "container", "USB", true, true, false, "Camera", null, name, name, null, "usbvideo", "Microsoft", "10.0", null, "Started", null, "Started", [id, $"USB\\VID_{vid}&PID_{pid}&MI_00"], ["USB\\Class_0E"], vid, pid, null, "00", "Port_#0001.Hub_#0001", ["PCIROOT(0)#USBROOT(0)#USB(1)"], [], "test");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeUvcEnvironment : IUvcIsolationEnvironment
    {
        public string BootFingerprint { get; set; } = "boot-a";
        public List<DateTimeOffset> UvcCaptures { get; } = [];
        public UvcSupervisorConfigSnapshot Config { get; set; } = new(
            UvcIsolationSchemas.ConfigSnapshot,
            DateTimeOffset.Parse("2026-06-19T20:00:00Z"),
            "available",
            "sha256:config",
            "supervisor.config.json",
            true,
            false,
            false,
            false,
            false,
            false,
            true,
            "VRCFaceTracking.exe",
            "sha256:vrcft",
            "test",
            []);
        public UvcInventorySnapshot Uvc { get; set; } = new(
            UvcIsolationSchemas.UvcInventory,
            DateTimeOffset.Parse("2026-06-19T20:00:00Z"),
            [
                new("HTC Multimedia Camera", "sha256:vive", "0BB4", "0321", "00", "sha256:c1", "usbvideo", "usbvideo.inf", "Microsoft", "10.0", "Started", "sha256:p", null, null, ["sha256:l"], null, null, [], [], "viveOrHtcUvc", "high", ["test"]),
                new("UVC Camera", "sha256:pimax", "34A4", "0012", "00", "sha256:c2", "usbvideo", "usbvideo.inf", "Microsoft", "10.0", "Started", "sha256:p", null, null, ["sha256:l"], null, null, [], [], "pimaxLikeUvc", "medium", ["test"])
            ],
            [],
            []);

        public UvcSupervisorConfigSnapshot CaptureConfig(string? configPath) => Config;

        public UvcProcessSnapshot CaptureProcesses()
            => new(
                UvcIsolationSchemas.ProcessState,
                DateTimeOffset.UtcNow,
                [
                    new("VRCFaceTracking", true, [new(10, DateTimeOffset.Parse("2026-06-19T20:00:00Z"), "VRCFaceTracking", "VRCFaceTracking.exe", "sha256:exe", "1.0", "present", [])]),
                    new("SteamVR", true, [new(11, DateTimeOffset.Parse("2026-06-19T20:00:00Z"), "vrserver", "vrserver.exe", null, null, "present", [])]),
                    new("VRChat", true, [new(12, DateTimeOffset.Parse("2026-06-19T20:00:00Z"), "VRChat", "VRChat.exe", null, null, "present", [])])
                ],
                []);

        public UvcInventorySnapshot CaptureUvcInventory()
        {
            UvcCaptures.Add(DateTimeOffset.UtcNow);
            return Uvc;
        }

        public UvcSystemStateSnapshot CaptureSystemState()
            => new(
                UvcIsolationSchemas.SystemState,
                DateTimeOffset.UtcNow,
                new(DateTimeOffset.Parse("2026-06-19T19:00:00Z"), 1000, 1, BootFingerprint, "test"),
                "sha256:machine",
                new("available", DateTimeOffset.UtcNow.ToString("O"), "Watcher", 99, 0, 0, 1, "%LOCALAPPDATA%\\file.jsonl", []),
                []);

        public WindowsEventCorrelationResult CaptureWindowsEvents(DateTimeOffset startUtc, DateTimeOffset endUtc)
            => new(
                WindowsEventCorrelationSchema.Version,
                DateTimeOffset.UtcNow,
                startUtc,
                endUtc,
                null,
                null,
                null,
                [new("System", "available", 1, null)],
                [new("System", "Microsoft-Windows-WER-SystemErrorReporting", 1001, 4, endUtc, "bugCheck", "Synthetic BugCheck")],
                new("unknown", null, null, [], []),
                []);
    }
}
