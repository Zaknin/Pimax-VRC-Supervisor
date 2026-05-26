using System.Diagnostics;
using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PimaxVrcSupervisor.SteamVrHost;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var hostMutex = new Mutex(initiallyOwned: true, @"Local\PimaxVrcSupervisorSteamVrHost", out var ownsHostMutex);
        if (!ownsHostMutex)
        {
            MessageBox.Show(
                "Pimax VRC Supervisor SteamVR host is already running.",
                "Pimax VRC Supervisor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var shutdown = new CancellationTokenSource();
        using var diagnostics = HostDiagnosticsSession.Start(HostDiagnosticsOptions.Load(args));
        using var host = new SteamVrDashboardHost(diagnostics);
        await host.RunAsync(shutdown.Token);
    }
}

internal static class AppVersion
{
    public static string Current =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}

internal sealed record HostDiagnosticsOptions(
    bool Enabled,
    bool Verbose,
    bool DebugEnabled,
    TimeSpan SummaryInterval,
    string LogDirectory)
{
    private const string ActiveConfigSelectionFileName = "supervisor.active-config.txt";

    public static HostDiagnosticsOptions Load(string[] args)
    {
        var configPath = FindConfigPath();
        var enabled = false;
        var verbose = false;
        var debugEnabled = false;
        var intervalSeconds = 10;
        var logDirectory = @"%TEMP%\PimaxVrcSupervisorDiagnostics";
        if (configPath is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(
                    File.ReadAllText(configPath),
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    });
                var root = document.RootElement;
                enabled = GetBool(root, "DiagnosticsLogSteamVrOverlay", defaultValue: false);
                verbose = GetBool(root, "DiagnosticsVerbose", defaultValue: false);
                debugEnabled = GetBool(root, "DiagnosticsDebugSteamVrOverlay", defaultValue: false);
                intervalSeconds = GetInt(root, "DiagnosticsSummaryIntervalSeconds", defaultValue: 10);
                logDirectory = GetString(root, "DiagnosticsLogDirectory");
            }
            catch
            {
                // Diagnostics must never block overlay startup.
            }
        }

        enabled = enabled || HasFlag(args, "--diagnostics");
        verbose = verbose || HasFlag(args, "--diagnostics-verbose");
        debugEnabled = enabled && debugEnabled;
        if (TryGetCommandOption(args, "--diagnostics-log-dir", out var requestedDirectory) && !string.IsNullOrWhiteSpace(requestedDirectory))
        {
            logDirectory = requestedDirectory;
        }

        return new HostDiagnosticsOptions(
            enabled,
            verbose,
            debugEnabled,
            TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)),
            logDirectory);
    }

    public string ResolveLogDirectory()
    {
        var directory = string.IsNullOrWhiteSpace(LogDirectory)
            ? Path.Combine(Path.GetTempPath(), "PimaxVrcSupervisorDiagnostics")
            : Environment.ExpandEnvironmentVariables(LogDirectory.Trim());
        return Path.GetFullPath(directory);
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetCommandOption(string[] args, string name, out string? value)
    {
        value = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[index + 1];
            }

            return true;
        }

        return false;
    }

    private static bool GetBool(JsonElement root, string name, bool defaultValue)
        => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;

    private static int GetInt(JsonElement root, string name, int defaultValue)
        => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : defaultValue;

    private static string GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? FindConfigPath()
    {
        var candidates = new[]
        {
            TryGetActiveConfigSelectionPath(),
            Path.Combine(AppContext.BaseDirectory, "supervisor.config.json"),
            Path.Combine(Environment.CurrentDirectory, "supervisor.config.json")
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static string? TryGetActiveConfigSelectionPath()
    {
        try
        {
            var selectionPath = Path.Combine(AppContext.BaseDirectory, ActiveConfigSelectionFileName);
            if (!File.Exists(selectionPath))
            {
                return null;
            }

            var selectedConfig = File.ReadAllText(selectionPath).Trim();
            if (string.IsNullOrWhiteSpace(selectedConfig))
            {
                return null;
            }

            return Path.IsPathRooted(selectedConfig)
                ? Path.GetFullPath(selectedConfig)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, selectedConfig));
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class DebugLogSession : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter? _writer;
    private bool _disposed;

    private DebugLogSession(HostDiagnosticsOptions options)
    {
        Enabled = options.Enabled && options.DebugEnabled;
        if (!Enabled)
        {
            return;
        }

        Directory.CreateDirectory(options.ResolveLogDirectory());
        Path = System.IO.Path.Combine(
            options.ResolveLogDirectory(),
            $"steamvr-host-debug-{DateTime.Now:yyyyMMdd-HHmmss}-pid{Environment.ProcessId}.log");
        _writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        Write("debug started; role=steamvr-host; diagnosticsVerbose=" + options.Verbose);
    }

    public bool Enabled { get; }
    public string? Path { get; }

    public static DebugLogSession Create(HostDiagnosticsOptions options) => new(options);

    public void Write(string message)
    {
        if (!Enabled || _writer is null)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (_writer is not null)
            {
                _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} debug stopped");
                _writer.Dispose();
            }

            _disposed = true;
        }
    }
}

internal sealed class HostDiagnosticsSession : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter? _writer;
    private readonly DebugLogSession _debugLog;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly OperationStats _activeLoop = new();
    private readonly OperationStats _hiddenLoop = new();
    private readonly OperationStats _dashboardClosedLoop = new();
    private readonly OperationStats _inactiveDashboardLoop = new();
    private readonly OperationStats _actualLoopInterval = new();
    private readonly OperationStats _statusRefresh = new();
    private readonly OperationStats _consoleRefresh = new();
    private readonly OperationStats _render = new();
    private readonly OperationStats _gpuUpload = new();
    private readonly OperationStats _gpuFlush = new();
    private readonly OperationStats _setOverlayTexture = new();
    private readonly ConcurrentDictionary<string, OperationStats> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, OperationStats> _renderSkipsByReason = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastSummaryAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastCpuSampleAt = DateTimeOffset.UtcNow;
    private TimeSpan _lastCpuTime;
    private long _dirtyMarks;
    private long _renderSkips;
    private long _dashboardVisibleTransitions;
    private long _overlayActiveTransitions;
    private long _viewedTransitions;
    private long _statusFailures;
    private long _consoleFailures;
    private bool _viewStateKnown;
    private bool _lastDashboardVisible;
    private bool _lastOverlayActive;
    private bool _lastViewed;
    private bool _disposed;

    private HostDiagnosticsSession(HostDiagnosticsOptions options)
    {
        _debugLog = DebugLogSession.Create(options);
        Enabled = options.Enabled;
        Verbose = options.Verbose;
        SummaryInterval = options.SummaryInterval;
        _lastCpuTime = _process.TotalProcessorTime;
        if (!Enabled)
        {
            return;
        }

        Directory.CreateDirectory(options.ResolveLogDirectory());
        Path = System.IO.Path.Combine(
            options.ResolveLogDirectory(),
            $"steamvr-host-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}-pid{Environment.ProcessId}.log");
        _writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        Write("diagnostics started; role=steamvr-host; verbose=" + Verbose);
        if (_debugLog.Enabled)
        {
            Write("steamvr host debug file=" + _debugLog.Path);
            _debugLog.Write("steamvr host diagnostics file=" + Path);
            _debugLog.Write("steamvr host debug file=" + _debugLog.Path);
        }
    }

    public bool Enabled { get; }
    public bool Verbose { get; }
    public bool DebugEnabled => _debugLog.Enabled;
    public TimeSpan SummaryInterval { get; }
    public string? Path { get; }
    public string? DebugPath => _debugLog.Path;

    public static HostDiagnosticsSession Start(HostDiagnosticsOptions options) => new(options);

    public void RecordLoop(bool active, bool dashboardVisible, TimeSpan actualInterval)
    {
        (active ? _activeLoop : _hiddenLoop).Record(actualInterval);
        if (!active)
        {
            (dashboardVisible ? _inactiveDashboardLoop : _dashboardClosedLoop).Record(actualInterval);
        }

        _actualLoopInterval.Record(actualInterval);
    }

    public void RecordOverlayViewState(bool dashboardVisible, bool overlayActive, bool viewed)
    {
        if (!Enabled)
        {
            return;
        }

        if (!_viewStateKnown)
        {
            _viewStateKnown = true;
            _lastDashboardVisible = dashboardVisible;
            _lastOverlayActive = overlayActive;
            _lastViewed = viewed;
            WriteVerbose($"overlay view state; dashboardVisible={dashboardVisible}; overlayActive={overlayActive}; viewed={viewed}");
            return;
        }

        if (dashboardVisible != _lastDashboardVisible)
        {
            _lastDashboardVisible = dashboardVisible;
            Interlocked.Increment(ref _dashboardVisibleTransitions);
            WriteVerbose("dashboard visible transition; visible=" + dashboardVisible);
        }

        if (overlayActive != _lastOverlayActive)
        {
            _lastOverlayActive = overlayActive;
            Interlocked.Increment(ref _overlayActiveTransitions);
            WriteVerbose("overlay active transition; active=" + overlayActive);
        }

        if (viewed != _lastViewed)
        {
            _lastViewed = viewed;
            Interlocked.Increment(ref _viewedTransitions);
            WriteVerbose("overlay viewed transition; viewed=" + viewed);
        }
    }

    public void RecordDirtyMark()
        => Interlocked.Increment(ref _dirtyMarks);

    public void RecordRenderSkip(string reason = "unspecified")
    {
        Interlocked.Increment(ref _renderSkips);
        if (Enabled)
        {
            _renderSkipsByReason.GetOrAdd(reason, _ => new OperationStats()).Record(TimeSpan.Zero);
        }
    }

    public void RecordStatusRefresh(TimeSpan elapsed, bool success)
    {
        _statusRefresh.Record(elapsed, success);
        if (!success)
        {
            Interlocked.Increment(ref _statusFailures);
        }

        WriteSlowOrVerbose("status refresh", elapsed, success);
    }

    public void RecordConsoleRefresh(TimeSpan elapsed, bool success)
    {
        _consoleRefresh.Record(elapsed, success);
        if (!success)
        {
            Interlocked.Increment(ref _consoleFailures);
        }

        WriteSlowOrVerbose("console refresh", elapsed, success);
    }

    public void RecordRender(TimeSpan elapsed)
    {
        _render.Record(elapsed);
        WriteSlowOrVerbose("render overlay", elapsed, success: true);
    }

    public void RecordGpuUpload(GpuUploadTiming timing)
    {
        _gpuUpload.Record(timing.Total);
        _gpuFlush.Record(timing.Flush);
        if (Verbose || timing.Total > TimeSpan.FromMilliseconds(25))
        {
            Write($"gpu upload; totalMs={timing.Total.TotalMilliseconds:0.0}; copyMs={timing.Copy.TotalMilliseconds:0.0}; updateMs={timing.UpdateSubresource.TotalMilliseconds:0.0}; flushMs={timing.Flush.TotalMilliseconds:0.0}");
        }
    }

    public void RecordSetOverlayTexture(TimeSpan elapsed)
    {
        _setOverlayTexture.Record(elapsed);
        WriteSlowOrVerbose("set overlay texture", elapsed, success: true);
    }

    public void RecordCommand(string command, TimeSpan elapsed, bool success)
    {
        _commands.GetOrAdd(command, _ => new OperationStats()).Record(elapsed, success);
        if (Verbose || elapsed > TimeSpan.FromMilliseconds(250))
        {
            Write($"command; name={command}; elapsedMs={elapsed.TotalMilliseconds:0.0}; success={success}");
        }
    }

    public void WriteSummaryIfDue(string context)
    {
        if (!Enabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastSummaryAt < SummaryInterval)
        {
            return;
        }

        _lastSummaryAt = now;
        _process.Refresh();
        var active = _activeLoop.SnapshotAndReset();
        var hidden = _hiddenLoop.SnapshotAndReset();
        var dashboardClosed = _dashboardClosedLoop.SnapshotAndReset();
        var inactiveDashboard = _inactiveDashboardLoop.SnapshotAndReset();
        var actualLoop = _actualLoopInterval.SnapshotAndReset();
        var status = _statusRefresh.SnapshotAndReset();
        var console = _consoleRefresh.SnapshotAndReset();
        var render = _render.SnapshotAndReset();
        var gpuUpload = _gpuUpload.SnapshotAndReset();
        var gpuFlush = _gpuFlush.SnapshotAndReset();
        var setTexture = _setOverlayTexture.SnapshotAndReset();
        var renderSkipSummary = string.Join(
            "; ",
            _renderSkipsByReason.OrderBy(pair => pair.Key).Select(pair =>
            {
                var snapshot = pair.Value.SnapshotAndReset();
                return $"{pair.Key}:count={snapshot.Count}";
            }));
        var commandSummary = string.Join(
            "; ",
            _commands.OrderBy(pair => pair.Key).Select(pair =>
            {
                var snapshot = pair.Value.SnapshotAndReset();
                return $"{pair.Key}:count={snapshot.Count},fail={snapshot.Failures},avgMs={snapshot.AverageMilliseconds:0.0},maxMs={snapshot.Max.TotalMilliseconds:0.0}";
            }));
        Write(
            "summary"
            + $"; context={context}"
            + $"; cpuPct={CalculateCpuPercent(now):0.0}"
            + $"; workingSetMb={BytesToMegabytes(_process.WorkingSet64):0.0}"
            + $"; privateMb={BytesToMegabytes(_process.PrivateMemorySize64):0.0}"
            + $"; gcHeapMb={BytesToMegabytes(GC.GetTotalMemory(false)):0.0}"
            + $"; gc0={GC.CollectionCount(0)}; gc1={GC.CollectionCount(1)}; gc2={GC.CollectionCount(2)}"
            + $"; threads={SafeThreadCount()}; handles={SafeHandleCount()}"
            + $"; activeLoops=count={active.Count},avgMs={active.AverageMilliseconds:0.0},maxMs={active.Max.TotalMilliseconds:0.0}"
            + $"; hiddenLoops=count={hidden.Count},avgMs={hidden.AverageMilliseconds:0.0},maxMs={hidden.Max.TotalMilliseconds:0.0}"
            + $"; dashboardClosedLoops=count={dashboardClosed.Count},avgMs={dashboardClosed.AverageMilliseconds:0.0},maxMs={dashboardClosed.Max.TotalMilliseconds:0.0}"
            + $"; inactiveDashboardLoops=count={inactiveDashboard.Count},avgMs={inactiveDashboard.AverageMilliseconds:0.0},maxMs={inactiveDashboard.Max.TotalMilliseconds:0.0}"
            + $"; actualLoop=count={actualLoop.Count},avgMs={actualLoop.AverageMilliseconds:0.0},maxMs={actualLoop.Max.TotalMilliseconds:0.0}"
            + $"; dirtyMarks={Interlocked.Exchange(ref _dirtyMarks, 0)}; renderSkips={Interlocked.Exchange(ref _renderSkips, 0)}; renderSkipReasons=[{renderSkipSummary}]"
            + $"; dashboardVisibleTransitions={Interlocked.Exchange(ref _dashboardVisibleTransitions, 0)}; overlayActiveTransitions={Interlocked.Exchange(ref _overlayActiveTransitions, 0)}; viewedTransitions={Interlocked.Exchange(ref _viewedTransitions, 0)}"
            + $"; status=count={status.Count},fail={status.Failures},avgMs={status.AverageMilliseconds:0.0},maxMs={status.Max.TotalMilliseconds:0.0},failureEvents={Interlocked.Exchange(ref _statusFailures, 0)}"
            + $"; console=count={console.Count},fail={console.Failures},avgMs={console.AverageMilliseconds:0.0},maxMs={console.Max.TotalMilliseconds:0.0},failureEvents={Interlocked.Exchange(ref _consoleFailures, 0)}"
            + $"; render=count={render.Count},avgMs={render.AverageMilliseconds:0.0},maxMs={render.Max.TotalMilliseconds:0.0}"
            + $"; gpuUpload=count={gpuUpload.Count},avgMs={gpuUpload.AverageMilliseconds:0.0},maxMs={gpuUpload.Max.TotalMilliseconds:0.0}"
            + $"; gpuFlush=count={gpuFlush.Count},avgMs={gpuFlush.AverageMilliseconds:0.0},maxMs={gpuFlush.Max.TotalMilliseconds:0.0}"
            + $"; setOverlayTexture=count={setTexture.Count},avgMs={setTexture.AverageMilliseconds:0.0},maxMs={setTexture.Max.TotalMilliseconds:0.0}"
            + $"; commands=[{commandSummary}]");
    }

    public void WriteVerbose(string message)
    {
        if (Verbose)
        {
            Write(message);
        }
    }

    public bool ShouldWriteCommandDebug(string command)
        => DebugEnabled && (Verbose || !IsRoutineDashboardPollCommand(command));

    public void WriteDebug(string message)
        => _debugLog.Write(message);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (_writer is not null)
            {
                _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} diagnostics stopped");
                _writer.Dispose();
            }

            _debugLog.Dispose();
            _process.Dispose();
            _disposed = true;
        }
    }

    private void WriteSlowOrVerbose(string name, TimeSpan elapsed, bool success)
    {
        if (Verbose || elapsed > TimeSpan.FromMilliseconds(25))
        {
            Write($"{name}; elapsedMs={elapsed.TotalMilliseconds:0.0}; success={success}");
        }
    }

    private void Write(string message)
    {
        if (!Enabled || _writer is null)
        {
            return;
        }

        lock (_lock)
        {
            if (!_disposed)
            {
                _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}");
            }
        }
    }

    private double CalculateCpuPercent(DateTimeOffset now)
    {
        var totalCpu = _process.TotalProcessorTime;
        var cpuDelta = totalCpu - _lastCpuTime;
        var wallDelta = now - _lastCpuSampleAt;
        _lastCpuTime = totalCpu;
        _lastCpuSampleAt = now;
        return wallDelta.TotalMilliseconds <= 0
            ? 0
            : cpuDelta.TotalMilliseconds / wallDelta.TotalMilliseconds / Environment.ProcessorCount * 100.0;
    }

    private int SafeThreadCount()
    {
        try
        {
            return _process.Threads.Count;
        }
        catch
        {
            return -1;
        }
    }

    private int SafeHandleCount()
    {
        try
        {
            return _process.HandleCount;
        }
        catch
        {
            return -1;
        }
    }

    private static double BytesToMegabytes(long bytes)
        => bytes / 1024.0 / 1024.0;

    private static bool IsRoutineDashboardPollCommand(string command)
        => string.Equals(command, "status", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "log", StringComparison.OrdinalIgnoreCase);
}

internal sealed class OperationStats
{
    private long _count;
    private long _failures;
    private long _totalTicks;
    private long _maxTicks;

    public void Record(TimeSpan elapsed, bool success = true)
    {
        Interlocked.Increment(ref _count);
        if (!success)
        {
            Interlocked.Increment(ref _failures);
        }

        Interlocked.Add(ref _totalTicks, elapsed.Ticks);
        var ticks = elapsed.Ticks;
        while (true)
        {
            var current = Volatile.Read(ref _maxTicks);
            if (ticks <= current || Interlocked.CompareExchange(ref _maxTicks, ticks, current) == current)
            {
                break;
            }
        }
    }

    public OperationStatsSnapshot SnapshotAndReset()
    {
        var count = Interlocked.Exchange(ref _count, 0);
        var failures = Interlocked.Exchange(ref _failures, 0);
        var totalTicks = Interlocked.Exchange(ref _totalTicks, 0);
        var maxTicks = Interlocked.Exchange(ref _maxTicks, 0);
        return new OperationStatsSnapshot(count, failures, TimeSpan.FromTicks(totalTicks), TimeSpan.FromTicks(maxTicks));
    }
}

internal readonly record struct OperationStatsSnapshot(long Count, long Failures, TimeSpan Total, TimeSpan Max)
{
    public double AverageMilliseconds => Count == 0 ? 0 : Total.TotalMilliseconds / Count;
}

internal sealed class SteamVrDashboardHost : IDisposable
{
    private const string HelperTaskName = "Pimax VRC Supervisor SteamVR Start";
    private const string ForcedManualReloadMarkerFileName = "PimaxVrcSupervisorForcedManualReload.marker";
    private const int CommandTcpPort = 37957;
    private const string OverlayKey = "pimax.vrcsupervisor.dashboard";
    private const string OverlayName = "Pimax VRC Supervisor";
    private const string OverlayIconRelativePath = @"Assets\vr-overlay-icon.png";
    private const int OverlayWidth = 1500;
    private const int OverlayHeight = 900;
    private static readonly TimeSpan ActiveOverlayFrameInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan InactiveDashboardPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan HiddenOverlayPollInterval = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan RepeatedFailureLogInterval = TimeSpan.FromSeconds(10);
    private const int ButtonTop = 210;
    private const int ButtonLeft = 78;
    private const int ButtonColumnGap = 32;
    private const int ButtonRowGap = 30;
    private const int ButtonWidth = 425;
    private const int ButtonHeight = 130;
    private const int ButtonRight = ButtonLeft + ButtonWidth + ButtonColumnGap;
    private const int ButtonThird = ButtonRight + ButtonWidth + ButtonColumnGap;
    private const int ButtonBottom = ButtonTop + ButtonHeight + ButtonRowGap;
    private const int ContentWidth = ButtonThird + ButtonWidth - ButtonLeft;
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), "PimaxVrcSupervisorSteamVrHost.log");
    private readonly object _renderLock = new();
    private readonly SemaphoreSlim _loopWakeSignal = new(0, 1);
    private readonly HostDiagnosticsSession _diagnostics;
    private readonly DashboardButton[] _buttons =
    [
        new("Restart VRC face tracking", "restart-core-apps", new Rectangle(ButtonLeft, ButtonTop, ButtonWidth, ButtonHeight)),
        new("Restart OSC router", "restart-osc-router", new Rectangle(ButtonRight, ButtonTop, ButtonWidth, ButtonHeight)),
        new("OSCGoesBrrr", "start-osc-goes-brrr", new Rectangle(ButtonThird, ButtonTop, ButtonWidth, ButtonHeight)),
        new("Base stations on", "base-stations-on", new Rectangle(ButtonLeft, ButtonBottom, ButtonWidth, ButtonHeight)),
        new("Base stations off", "base-stations-off", new Rectangle(ButtonRight, ButtonBottom, ButtonWidth, ButtonHeight)),
        new("Restart Supervisor", "restart-supervisor", new Rectangle(ButtonThird, ButtonBottom, ButtonWidth, ButtonHeight))
    ];
    private OpenVrOverlaySession? _overlay;
    private GpuOverlayRenderer? _gpuRenderer;
    private Image? _overlayIconImage;
    private Process? _steamVrProcess;
    private string _status = "Starting supervisor...";
    private DashboardStatus _dashboardStatus = DashboardStatus.Pending;
    private string[] _consoleLines = [];
    private string[] _consoleDisplayLines = [];
    private string? _pressedCommand;
    private string? _runningCommand;
    private DateTimeOffset _lastCommandStartedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastOverlayRefreshAt = DateTimeOffset.MinValue;
    private OpenVrEventType? _lastEventType;
    private uint _overlayEventCount;
    private int _statusRefreshInFlight;
    private int _consoleRefreshInFlight;
    private bool _commandInFlight;
    private bool _renderDirty = true;
    private bool _renderUrgent;
    private bool _overlayViewed = true;
    private bool _viewedStateKnown;
    private DateTimeOffset _lastBridgeRetryLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStatusRefreshFailureLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastConsoleRefreshFailureLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDashboardVisibleQueryFailureLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastOverlayActiveQueryFailureLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRenderRefreshFailureLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastGpuRendererUnavailableLogAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public SteamVrDashboardHost(HostDiagnosticsSession diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Log("Host starting from " + AppContext.BaseDirectory);
        Log("Dashboard renderer: Ver2 only.");
        WriteDebug(
            "host starting"
            + $"; version={AppVersion.Current}"
            + $"; baseDirectory={AppContext.BaseDirectory}"
            + $"; diagnosticsFile={(_diagnostics.Path ?? "none")}"
            + $"; debugFile={(_diagnostics.DebugPath ?? "none")}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log("Host process exiting.");
        await StartSupervisorAsync();

        try
        {
            _overlay = OpenVrOverlaySession.Open(OverlayKey, OverlayName);
            _overlay.SetOverlayWidthInMeters(2.5f);
            _overlay.SetOverlayMouseScale(OverlayWidth, OverlayHeight);
            _overlay.SetOverlayInputMethodMouse();
            _overlay.SetInteractiveDashboardFlags();
            TryInitializeGpuRenderer();
            LoadOverlayIconImage();
            RefreshOverlayTexture(force: true);
            var overlayIconPath = GetOverlayIconPath();
            if (File.Exists(overlayIconPath))
            {
                _overlay.SetThumbnailFromFile(overlayIconPath);
            }

            Log("Dashboard overlay created.");
            Log($"OpenVR event layout: size={Marshal.SizeOf<OpenVrEvent>()}; mouse=16,20; button=24; cursor=28.");
            WriteDebug($"dashboard overlay created; eventLayoutSize={Marshal.SizeOf<OpenVrEvent>()}");
        }
        catch (Exception ex)
        {
            Log("Could not create overlay: " + ex);
            WriteDebug("could not create overlay: " + ex);
            MessageBox.Show(ex.Message, "Could not create SteamVR dashboard overlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var lastStatusRefresh = DateTimeOffset.MinValue;
        var lastConsoleRefresh = DateTimeOffset.MinValue;
        var lastLoopAt = DateTimeOffset.UtcNow;
        while (!cancellationToken.IsCancellationRequested && IsSteamVrRunning())
        {
            var now = DateTimeOffset.UtcNow;
            var viewState = GetOverlayViewState();
            var overlayViewed = viewState.Viewed;
            _diagnostics.RecordOverlayViewState(viewState.DashboardVisible, viewState.OverlayActive, viewState.Viewed);
            _diagnostics.RecordLoop(overlayViewed, viewState.DashboardVisible, now - lastLoopAt);
            lastLoopAt = now;
            if (!_viewedStateKnown || overlayViewed != _overlayViewed)
            {
                _viewedStateKnown = true;
                _overlayViewed = overlayViewed;
                Log($"Dashboard view state changed: dashboardVisible={viewState.DashboardVisible}; overlayActive={viewState.OverlayActive}; viewed={overlayViewed}");
                WriteDebug($"view state changed; dashboardVisible={viewState.DashboardVisible}; overlayActive={viewState.OverlayActive}; viewed={overlayViewed}");
                if (overlayViewed)
                {
                    lastStatusRefresh = now;
                    lastConsoleRefresh = now;
                    MarkOverlayDirty(urgent: true);
                    RefreshOverlayTexture(force: true);
                    _ = RefreshStatusAsync();
                    _ = RefreshConsoleAsync();
                }
                else
                {
                    MarkOverlayDirty();
                }
            }

            if (overlayViewed)
            {
                ProcessOverlayEvents(cancellationToken);
                if (now - lastStatusRefresh > TimeSpan.FromSeconds(5))
                {
                    lastStatusRefresh = now;
                    if (!_commandInFlight)
                    {
                        _ = RefreshStatusAsync();
                    }
                }

                if (now - lastConsoleRefresh > TimeSpan.FromSeconds(2))
                {
                    lastConsoleRefresh = now;
                    _ = RefreshConsoleAsync();
                }

                RefreshOverlayTexture();
            }

            _diagnostics.WriteSummaryIfDue(overlayViewed ? "active-loop" : "hidden-loop");
            await WaitForNextLoopAsync(viewState, cancellationToken);
        }

        Log($"Host loop exiting; cancellation={cancellationToken.IsCancellationRequested}; steamvr={IsSteamVrRunning()}");
        WriteDebug($"host loop exiting; cancellation={cancellationToken.IsCancellationRequested}; steamvr={IsSteamVrRunning()}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _diagnostics.WriteSummaryIfDue("dispose");
        _loopWakeSignal.Dispose();
        _gpuRenderer?.Dispose();
        _overlay?.Dispose();
        _overlayIconImage?.Dispose();
        _steamVrProcess?.Dispose();
    }

    private async Task WaitForNextLoopAsync(OverlayViewState viewState, CancellationToken cancellationToken)
    {
        var delay = viewState.Viewed
            ? ActiveOverlayFrameInterval
            : viewState.DashboardVisible
                ? InactiveDashboardPollInterval
                : HiddenOverlayPollInterval;

        await _loopWakeSignal.WaitAsync(delay, cancellationToken);
        while (_loopWakeSignal.Wait(0))
        {
        }
    }

    private void WakeOverlayLoop()
    {
        try
        {
            _loopWakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task<bool> StartSupervisorAsync(bool resetTaskState = false)
    {
        try
        {
            if (resetTaskState)
            {
                await TryEndHelperTaskAsync();
            }

            var currentReleaseFolder = global::ScheduledTaskPathValidator.GetCurrentExecutableDirectory();
            var taskValidation = await global::ScheduledTaskPathValidator.ValidateExistingTaskExecutableAsync(
                HelperTaskName,
                currentReleaseFolder,
                CancellationToken.None);
            if (!taskValidation.Exists)
            {
                throw new InvalidOperationException(
                    "The SteamVR start helper scheduled task is missing.\r\n" +
                    $"Scheduled task: {HelperTaskName}\r\n" +
                    $"Current release folder: {currentReleaseFolder}\r\n" +
                    "Please reapply SteamVR startup integration from the current release.");
            }

            if (taskValidation.Issue is not null)
            {
                throw new InvalidOperationException(global::ScheduledTaskPathValidator.FormatIssue(taskValidation.Issue));
            }

            Log("Requesting elevated supervisor via scheduled task.");
            WriteDebug("requesting elevated supervisor via scheduled task");
            await RunProcessAsync("schtasks.exe", ["/Run", "/TN", HelperTaskName], TimeSpan.FromSeconds(15));
            SetStatus("Supervisor start requested.");
            if (await WaitForSupervisorCommandBridgeAsync(TimeSpan.FromSeconds(20)))
            {
                WriteDebug("supervisor command bridge ready after scheduled task run");
                return true;
            }

            Log("Supervisor command bridge did not become ready after scheduled task run; retrying once.");
            WriteDebug("supervisor command bridge not ready; retrying scheduled task run");
            await TryEndHelperTaskAsync();
            await RunProcessAsync("schtasks.exe", ["/Run", "/TN", HelperTaskName], TimeSpan.FromSeconds(15));
            SetStatus("Supervisor start requested again.");
            if (await WaitForSupervisorCommandBridgeAsync(TimeSpan.FromSeconds(20)))
            {
                WriteDebug("supervisor command bridge ready after scheduled task retry");
                return true;
            }

            throw new InvalidOperationException(
                "The helper task ran, but the elevated Supervisor command bridge did not become available.\r\n" +
                $"Scheduled task: {HelperTaskName}\r\n" +
                $"Current release folder: {currentReleaseFolder}\r\n" +
                "Check the supervisor config and reapply SteamVR startup integration if the task points to an old release.");
        }
        catch (Exception ex)
        {
            Log("Could not start elevated supervisor: " + ex);
            WriteDebug("could not start elevated supervisor: " + ex);
            SetStatus("Could not start elevated supervisor: " + ex.Message);
            return false;
        }
    }

    private async Task<string> RestartSupervisorAsync()
    {
        var supervisorProcesses = GetSupervisorProcesses();
        if (supervisorProcesses.Length == 0)
        {
            Log("Supervisor restart requested; no supervisor process is running.");
            WriteDebug("supervisor restart requested; no supervisor process is running");
            var started = await StartSupervisorAsync(resetTaskState: true);
            return started
                ? "Supervisor was not running. Startup completed."
                : "Supervisor was not running. Startup requested, but the command bridge is not ready.";
        }

        WriteForcedManualReloadMarker();
        var hardStopRequested = false;
        try
        {
            Log("Requesting elevated supervisor hard stop over command bridge.");
            WriteDebug("requesting elevated supervisor hard stop over command bridge");
            var response = await SendCommandAsync("force-stop-supervisor", TimeSpan.FromSeconds(2));
            Log("Supervisor hard stop response: " + response);
            WriteDebug("supervisor hard stop response: " + response);
            hardStopRequested = true;
        }
        catch (Exception ex)
        {
            Log("Supervisor hard stop command failed; falling back to direct process kill: " + ex.Message);
            WriteDebug("supervisor hard stop command failed; fallback=direct-kill; error=" + ex.Message);
        }

        if (!hardStopRequested)
        {
            foreach (var process in supervisorProcesses)
            {
                try
                {
                    Log($"Killing supervisor pid={process.Id}.");
                    process.Kill(entireProcessTree: false);
                }
                catch (Exception ex)
                {
                    Log($"Could not kill supervisor pid={TryGetProcessId(process)}: {ex.Message}");
                }
            }
        }

        foreach (var process in supervisorProcesses)
        {
            try
            {
                using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeoutSource.Token);
                Log($"Supervisor pid={process.Id} exited.");
            }
            catch (OperationCanceledException)
            {
                Log($"Supervisor pid={TryGetProcessId(process)} did not exit within timeout.");
            }
            catch (Exception ex)
            {
                Log($"Could not wait for supervisor pid={TryGetProcessId(process)}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        var restartSucceeded = await StartSupervisorAsync(resetTaskState: true);
        return restartSucceeded
            ? "Supervisor hard restart completed."
            : "Supervisor hard restart requested, but the command bridge is not ready.";
    }

    private async Task TryEndHelperTaskAsync()
    {
        try
        {
            Log("Ending helper scheduled task state before restart.");
            await RunProcessAsync("schtasks.exe", ["/End", "/TN", HelperTaskName], TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            Log("Could not end helper scheduled task state: " + ex.Message);
        }
    }

    private static string ForcedManualReloadMarkerPath
        => Path.Combine(Path.GetTempPath(), ForcedManualReloadMarkerFileName);

    private void WriteForcedManualReloadMarker()
    {
        try
        {
            File.WriteAllText(ForcedManualReloadMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            Log("Could not write forced manual reload marker: " + ex.Message);
        }
    }

    private async Task<bool> WaitForSupervisorCommandBridgeAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                SetStatus(await SendCommandAsync("status", TimeSpan.FromSeconds(1)));
                return true;
            }
            catch (Exception ex)
            {
                LogThrottled(ref _lastBridgeRetryLogAt, "Supervisor command bridge is not ready yet: " + ex.Message);
                SetStatus("Waiting for elevated supervisor command bridge...");
                await Task.Delay(500);
            }
        }

        Log("Timed out waiting for elevated supervisor command bridge.");
        WriteDebug("timed out waiting for elevated supervisor command bridge");
        SetStatus("Waiting for elevated supervisor command bridge...");
        return false;
    }

    private static Process[] GetSupervisorProcesses()
    {
        var currentProcessId = Environment.ProcessId;
        return Process.GetProcessesByName("PimaxVrcSupervisor")
            .Where(process => process.Id != currentProcessId)
            .ToArray();
    }

    private static string TryGetProcessId(Process process)
    {
        try
        {
            return process.Id.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (Interlocked.Exchange(ref _statusRefreshInFlight, 1) == 1)
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var success = false;
        try
        {
            SetStatus(await SendCommandAsync("status", TimeSpan.FromSeconds(2)), markDirty: IsOverlayCurrentlyViewed(), wakeLoop: true);
            success = true;
        }
        catch (Exception ex)
        {
            if (_status.StartsWith("Could not start", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            LogThrottled(ref _lastStatusRefreshFailureLogAt, "Status refresh failed: " + ex.Message);
            SetStatus("Waiting for elevated supervisor command bridge...", markDirty: IsOverlayCurrentlyViewed(), wakeLoop: true);
        }
        finally
        {
            _diagnostics.RecordStatusRefresh(Stopwatch.GetElapsedTime(startedAt), success);
            Volatile.Write(ref _statusRefreshInFlight, 0);
        }
    }

    private async Task RefreshConsoleAsync()
    {
        if (Interlocked.Exchange(ref _consoleRefreshInFlight, 1) == 1)
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var success = false;
        try
        {
            var response = await SendCommandAsync("log", TimeSpan.FromMilliseconds(700));
            var lines = JsonSerializer.Deserialize<string[]>(response) ?? [];
            SetConsoleLines(lines, markDirty: IsOverlayCurrentlyViewed(), wakeLoop: true);
            success = true;
        }
        catch (Exception ex)
        {
            LogThrottled(ref _lastConsoleRefreshFailureLogAt, "Console refresh failed: " + ex.Message);
        }
        finally
        {
            _diagnostics.RecordConsoleRefresh(Stopwatch.GetElapsedTime(startedAt), success);
            Volatile.Write(ref _consoleRefreshInFlight, 0);
        }
    }

    private void ProcessOverlayEvents(CancellationToken cancellationToken)
    {
        if (_overlay is null)
        {
            return;
        }

        while (_overlay.PollNextOverlayEvent(out var vrEvent))
        {
            _overlayEventCount++;
            _lastEventType = vrEvent.EventType;
            var pointer = GetMousePointer(vrEvent, DateTimeOffset.UtcNow);
            if (vrEvent.EventType == OpenVrEventType.MouseMove)
            {
                // Hover highlighting is intentionally disabled; clicks are handled by MouseButtonDown.
            }
            else if (vrEvent.EventType == OpenVrEventType.TouchPadMove)
            {
                // TouchPadMove uses a different event payload and is not needed without hover highlighting.
            }
            else if (vrEvent.EventType == OpenVrEventType.ButtonPress)
            {
                Log($"ButtonPress ignored; cursor={pointer.CursorIndex}; raw={pointer.Raw.X:0.0},{pointer.Raw.Y:0.0}; dashboard mouse clicks are handled by MouseButtonDown.");
            }
            else if (vrEvent.EventType == OpenVrEventType.ButtonUnpress)
            {
                Log($"ButtonUnpress; pressed={_pressedCommand ?? "none"}");
                _pressedCommand = null;
            }
            else if (vrEvent.EventType == OpenVrEventType.MouseButtonDown)
            {
                var resolution = ResolveClickButton(pointer);
                LogPointerEvent("MouseButtonDown", pointer, resolution);
                _pressedCommand = resolution.Button?.Command;
                if (resolution.Button is not null)
                {
                    TryStartButtonCommand(resolution.Button, cancellationToken);
                }
                else
                {
                    Log("Click ignored: " + resolution.MissReason);
                }
            }
            else if (vrEvent.EventType == OpenVrEventType.MouseButtonUp)
            {
                LogPointerEvent("MouseButtonUp", pointer, new ClickResolution(null, pointer, "event", "button-up"));
                _pressedCommand = null;
            }
            else if (vrEvent.EventType == OpenVrEventType.MouseFocusLeave)
            {
                _pressedCommand = null;
            }
            else if (vrEvent.EventType == OpenVrEventType.OverlayClosed)
            {
                Log("OverlayClosed event received.");
                WriteDebug("overlay closed event received");
                Environment.ExitCode = 0;
                return;
            }
        }
    }

    private static OverlayPointer GetMousePointer(OpenVrEvent vrEvent, DateTimeOffset timestamp)
    {
        var raw = new PointF(vrEvent.MouseX, vrEvent.MouseY);
        var layout = new PointF(vrEvent.MouseX, OverlayHeight - vrEvent.MouseY);
        return new OverlayPointer(
            vrEvent.CursorIndex,
            raw,
            layout,
            timestamp);
    }

    private OverlayViewState GetOverlayViewState()
    {
        var dashboardVisible = IsDashboardVisible();
        if (!dashboardVisible)
        {
            return new OverlayViewState(DashboardVisible: false, OverlayActive: false, Viewed: false);
        }

        var overlayActive = IsOverlayActive();
        return new OverlayViewState(DashboardVisible: true, OverlayActive: overlayActive, Viewed: overlayActive);
    }

    private bool IsDashboardVisible()
    {
        try
        {
            return _overlay?.IsDashboardVisible() ?? true;
        }
        catch (Exception ex)
        {
            LogThrottled(ref _lastDashboardVisibleQueryFailureLogAt, "Could not query dashboard visibility; falling back to active overlay state: " + ex.Message);
            return IsOverlayActive();
        }
    }

    private bool IsOverlayActive()
    {
        try
        {
            return _overlay?.IsActiveDashboardOverlay() ?? true;
        }
        catch (Exception ex)
        {
            LogThrottled(ref _lastOverlayActiveQueryFailureLogAt, "Could not query dashboard active state; assuming active: " + ex.Message);
            return true;
        }
    }

    private ClickResolution ResolveClickButton(OverlayPointer eventPointer)
    {
        if (!IsValidOverlayPoint(eventPointer.Raw))
        {
            return new ClickResolution(null, eventPointer, "event", GetPointerInvalidReason(eventPointer));
        }

        var button = HitTestLayout(eventPointer.Layout);
        return new ClickResolution(
            button,
            eventPointer,
            "event",
            button is null ? GetNoHitReason(eventPointer) : "none");
    }

    private static bool IsTrackablePointer(OverlayPointer pointer)
        => IsValidOverlayPoint(pointer.Raw);

    private static bool IsValidOverlayPoint(PointF point)
        => HasFiniteCoordinates(point)
            && point.X >= 0
            && point.X < OverlayWidth
            && point.Y >= 0
            && point.Y < OverlayHeight;

    private static bool HasFiniteCoordinates(PointF point)
        => float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static string GetPointerInvalidReason(OverlayPointer pointer)
    {
        if (!HasFiniteCoordinates(pointer.Raw))
        {
            return "invalid coordinate";
        }

        if (pointer.Raw.X < 0 || pointer.Raw.X >= OverlayWidth)
        {
            return "invalid-x";
        }

        if (pointer.Raw.Y < 0 || pointer.Raw.Y >= OverlayHeight)
        {
            return "invalid-y";
        }

        return "outside overlay";
    }

    private static string GetNoHitReason(OverlayPointer pointer)
        => pointer.Layout.X < 0
            || pointer.Layout.X >= OverlayWidth
            || pointer.Layout.Y < 0
            || pointer.Layout.Y >= OverlayHeight
                ? "outside overlay"
                : "gap/margin click";

    private void LogPointerEvent(string eventName, OverlayPointer pointer, ClickResolution resolution)
        => Log(
            $"{eventName}; cursor={pointer.CursorIndex}; raw={pointer.Raw.X:0.0},{pointer.Raw.Y:0.0}; layout={pointer.Layout.X:0.0},{pointer.Layout.Y:0.0}; source={resolution.Source}; resolved={resolution.Button?.Command ?? "none"}; miss={resolution.MissReason}; pressed={_pressedCommand ?? "none"}");

    private DashboardButton? FindButtonByCommand(string? command)
        => string.IsNullOrWhiteSpace(command)
            ? null
            : _buttons.FirstOrDefault(candidate => string.Equals(candidate.Command, command, StringComparison.Ordinal));

    private void TryStartButtonCommand(DashboardButton button, CancellationToken cancellationToken)
    {
        if (_commandInFlight || DateTimeOffset.UtcNow - _lastCommandStartedAt < TimeSpan.FromMilliseconds(250))
        {
            Log($"Command ignored for {button.Command}; inFlight={_commandInFlight}");
            WriteDebug($"command ignored; name={button.Command}; inFlight={_commandInFlight}");
            SetStatus("Already running a command...", urgent: true);
            return;
        }

        _commandInFlight = true;
        _runningCommand = button.Command;
        _lastCommandStartedAt = DateTimeOffset.UtcNow;
        Log("Command queued: " + button.Command);
        WriteDebug("command queued; name=" + button.Command);
        SetStatus("Clicked: " + button.Label, urgent: true);
        _ = ExecuteButtonAsync(button, cancellationToken);
    }

    private async Task ExecuteButtonAsync(DashboardButton button, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var success = false;
        try
        {
            Log("Command starting: " + button.Command);
            WriteDebug("command starting; name=" + button.Command);
            SetStatus("Running " + button.Label + "...", urgent: true);
            var response = string.Equals(button.Command, "restart-supervisor", StringComparison.Ordinal)
                ? await RestartSupervisorAsync()
                : await SendCommandAsync(button.Command, TimeSpan.FromSeconds(45));
            Log("Command response: " + response);
            WriteDebug("command response; name=" + button.Command + "; response=" + TruncateDebugValue(response));
            SetStatus(response, markDirty: IsOverlayCurrentlyViewed(), urgent: true, wakeLoop: true);
            success = !response.StartsWith("Command failed:", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Log("Command failed: " + ex);
            WriteDebug("command failed; name=" + button.Command + "; error=" + ex);
            SetStatus("Command failed: " + ex.Message, markDirty: IsOverlayCurrentlyViewed(), urgent: true, wakeLoop: true);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            _diagnostics.RecordCommand(button.Command, elapsed, success);
            Log("Command finished: " + button.Command);
            WriteDebug($"command finished; name={button.Command}; success={success}; elapsedMs={elapsed.TotalMilliseconds:0.0}");
            _runningCommand = null;
            _commandInFlight = false;
            if (IsOverlayCurrentlyViewed())
            {
                MarkOverlayDirty(urgent: true, wakeLoop: true);
                _ = RefreshStatusAsync();
                _ = RefreshConsoleAsync();
            }
        }
    }

    private DashboardButton? HitTestLayout(PointF layoutPosition)
        => _buttons.FirstOrDefault(button => Contains(button.Bounds, layoutPosition));

    private static bool Contains(Rectangle bounds, PointF point)
        => point.X >= bounds.Left
            && point.X < bounds.Right
            && point.Y >= bounds.Top
            && point.Y < bounds.Bottom;

    private void SetStatus(string status, bool markDirty = true, bool urgent = false, bool wakeLoop = false)
    {
        if (string.Equals(_status, status, StringComparison.Ordinal))
        {
            return;
        }

        _status = status;
        if (DashboardStatus.TryParse(status, out var parsedStatus))
        {
            _dashboardStatus = parsedStatus;
        }

        if (markDirty)
        {
            MarkOverlayDirty(urgent, wakeLoop);
        }
    }

    private void SetConsoleLines(string[] lines, bool markDirty = true, bool urgent = false, bool wakeLoop = false)
    {
        if (_consoleLines.SequenceEqual(lines, StringComparer.Ordinal))
        {
            return;
        }

        _consoleLines = lines;
        _consoleDisplayLines = BuildConsoleDisplayLines(lines);
        if (markDirty)
        {
            MarkOverlayDirty(urgent, wakeLoop);
        }
    }

    private void MarkOverlayDirty(bool urgent = false, bool wakeLoop = false)
    {
        _renderDirty = true;
        if (urgent)
        {
            _renderUrgent = true;
        }

        _diagnostics.RecordDirtyMark();
        if (wakeLoop)
        {
            WakeOverlayLoop();
        }
    }

    private bool IsOverlayCurrentlyViewed()
        => Volatile.Read(ref _overlayViewed);

    private void TryInitializeGpuRenderer()
    {
        try
        {
            _gpuRenderer = new GpuOverlayRenderer(OverlayWidth, OverlayHeight);
            Log($"D3D11 overlay renderer ready; featureLevel={_gpuRenderer.FeatureLevel}.");
            WriteDebug($"D3D11 overlay renderer ready; featureLevel={_gpuRenderer.FeatureLevel}");
        }
        catch (Exception ex)
        {
            _gpuRenderer?.Dispose();
            _gpuRenderer = null;
            Log("D3D11 overlay renderer unavailable; Ver2 dashboard requires D3D11 texture rendering: " + ex);
            WriteDebug("D3D11 overlay renderer unavailable: " + ex);
            throw new InvalidOperationException("D3D11 overlay renderer unavailable; Ver2 dashboard requires D3D11 texture rendering.", ex);
        }
    }

    private void LoadOverlayIconImage()
    {
        var overlayIconPath = GetOverlayIconPath();
        if (!File.Exists(overlayIconPath))
        {
            return;
        }

        try
        {
            _overlayIconImage = Image.FromFile(overlayIconPath);
        }
        catch (Exception ex)
        {
            Log("Could not load overlay icon image: " + ex.Message);
            WriteDebug("could not load overlay icon image: " + ex.Message);
        }
    }

    private void RefreshOverlayTexture(bool force = false)
    {
        if (_overlay is null)
        {
            _diagnostics.RecordRenderSkip("no-overlay");
            return;
        }

        if (!force && !_overlayViewed)
        {
            _diagnostics.RecordRenderSkip("hidden");
            return;
        }

        if (!force && !_renderDirty)
        {
            _diagnostics.RecordRenderSkip("not-dirty");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var urgent = _renderUrgent;
        if (!force && !urgent && now - _lastOverlayRefreshAt < ActiveOverlayFrameInterval)
        {
            _diagnostics.RecordRenderSkip("throttled");
            return;
        }

        if (_gpuRenderer is null)
        {
            _diagnostics.RecordRenderSkip("gpu-unavailable");
            LogThrottled(ref _lastGpuRendererUnavailableLogAt, "Render skipped because the D3D11 renderer is unavailable.");
            return;
        }

        lock (_renderLock)
        {
            try
            {
                var renderStartedAt = Stopwatch.GetTimestamp();
                using var bitmap = RenderOverlay();
                _diagnostics.RecordRender(Stopwatch.GetElapsedTime(renderStartedAt));
                var uploadTiming = _gpuRenderer.Update(bitmap);
                _diagnostics.RecordGpuUpload(uploadTiming);
                var setTextureStartedAt = Stopwatch.GetTimestamp();
                _overlay.SetOverlayTexture(_gpuRenderer.TexturePointer);
                _diagnostics.RecordSetOverlayTexture(Stopwatch.GetElapsedTime(setTextureStartedAt));

                _renderDirty = false;
                _renderUrgent = false;
                _lastOverlayRefreshAt = now;
            }
            catch (Exception ex)
            {
                _renderDirty = false;
                _renderUrgent = false;
                LogThrottled(ref _lastRenderRefreshFailureLogAt, "Render refresh failed: " + ex);
            }
        }
    }

    private Bitmap RenderOverlay()
        => RenderVer2Overlay();

    private Bitmap RenderVer2Overlay()
    {
        var bitmap = new Bitmap(OverlayWidth, OverlayHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Ver2Palette.Background);

        using var titleFont = new Font(FontFamily.GenericSansSerif, 31, FontStyle.Bold);
        using var subtitleFont = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Regular);
        using var sectionFont = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold);
        using var statusFont = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold);
        using var statusValueFont = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular);
        using var buttonFont = new Font(FontFamily.GenericSansSerif, 19, FontStyle.Bold);
        using var buttonSubFont = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Regular);
        using var consoleFont = new Font(FontFamily.GenericMonospace, 12, FontStyle.Regular);
        using var footerFont = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Regular);
        using var textBrush = new SolidBrush(Ver2Palette.Text);
        using var mutedBrush = new SolidBrush(Ver2Palette.Muted);

        DrawVer2Header(graphics, titleFont, subtitleFont, textBrush, mutedBrush);
        DrawVer2StatusStrip(graphics, _dashboardStatus, statusFont, statusValueFont);
        DrawVer2Buttons(graphics, buttonFont, buttonSubFont);
        DrawVer2ConsolePanel(graphics, sectionFont, consoleFont, textBrush, mutedBrush);
        DrawVer2Footer(graphics, footerFont, mutedBrush);
        return bitmap;
    }

    private void DrawVer2Header(
        Graphics graphics,
        Font titleFont,
        Font subtitleFont,
        Brush textBrush,
        Brush mutedBrush)
    {
        using var topLine = new LinearGradientBrush(
            new Rectangle(0, 0, OverlayWidth, 10),
            Ver2Palette.Accent,
            Ver2Palette.AccentAlt,
            LinearGradientMode.Horizontal);
        graphics.FillRectangle(topLine, 0, 0, OverlayWidth, 10);
        DrawOverlayIcon(graphics, new Rectangle(78, 38, 72, 72));
        graphics.DrawString("Pimax VRC Supervisor", titleFont, textBrush, 166, 43);
        graphics.DrawString("SteamVR dashboard control surface", subtitleFont, mutedBrush, 170, 92);
    }

    private void DrawVer2StatusStrip(Graphics graphics, DashboardStatus status, Font labelFont, Font valueFont)
    {
        var top = 126;
        DrawStatusPill(graphics, new Rectangle(ButtonLeft, top, ButtonWidth, 58), "Supervisor", GetSupervisorStateText(), GetSupervisorStateColor(), labelFont, valueFont);
        DrawStatusPill(graphics, new Rectangle(ButtonRight, top, ButtonWidth, 58), "Base station control", FormatStatusValue(status.BaseStations), GetBaseStationColor(status.BaseStations), labelFont, valueFont);
        DrawStatusPill(graphics, new Rectangle(ButtonThird, top, ButtonWidth, 58), "OSCGoesBrrr", FormatStatusValue(status.OscGoesBrrr), GetStatusColor(status.OscGoesBrrr), labelFont, valueFont);
    }

    private void DrawStatusPill(Graphics graphics, Rectangle bounds, string label, string value, Color indicator, Font labelFont, Font valueFont)
    {
        FillRoundedRectangle(graphics, bounds, 7, Ver2Palette.Panel);
        DrawRoundedRectangle(graphics, bounds, 7, Ver2Palette.Border, 1);
        using var indicatorBrush = new SolidBrush(indicator);
        graphics.FillEllipse(indicatorBrush, bounds.Left + 12, bounds.Top + 24, 10, 10);
        using var labelBrush = new SolidBrush(Ver2Palette.Muted);
        using var valueBrush = new SolidBrush(Ver2Palette.Text);
        graphics.DrawString(label, labelFont, labelBrush, bounds.Left + 30, bounds.Top + 10);
        using var valueFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        graphics.DrawString(value, valueFont, valueBrush, new Rectangle(bounds.Left + 30, bounds.Top + 32, bounds.Width - 40, 20), valueFormat);
    }

    private void DrawVer2Buttons(Graphics graphics, Font buttonFont, Font subFont)
    {
        foreach (var button in _buttons)
        {
            var running = string.Equals(_runningCommand, button.Command, StringComparison.Ordinal);
            var bounds = button.Bounds;
            FillRoundedRectangle(graphics, bounds, 10, running ? Ver2Palette.RunningPanel : Ver2Palette.Button);
            DrawRoundedRectangle(graphics, bounds, 10, running ? Ver2Palette.Accent : Ver2Palette.BorderStrong, running ? 3 : 1);

            using var textBrush = new SolidBrush(Ver2Palette.Text);
            using var mutedBrush = new SolidBrush(Ver2Palette.Muted);
            var title = running ? "Running..." : button.Label;
            DrawText(graphics, title, buttonFont, textBrush, new Rectangle(bounds.Left + 28, bounds.Top + 27, bounds.Width - 56, 36), StringAlignment.Center, StringAlignment.Center);
            DrawText(graphics, GetButtonHint(button.Command), subFont, mutedBrush, new Rectangle(bounds.Left + 28, bounds.Top + 76, bounds.Width - 56, 24), StringAlignment.Center, StringAlignment.Center);
        }
    }

    private void DrawVer2ConsolePanel(Graphics graphics, Font titleFont, Font consoleFont, Brush textBrush, Brush mutedBrush)
    {
        var bounds = new Rectangle(ButtonLeft, 550, ContentWidth, 295);
        FillRoundedRectangle(graphics, bounds, 10, Ver2Palette.PanelDark);
        DrawRoundedRectangle(graphics, bounds, 10, Ver2Palette.Border, 1);
        graphics.DrawString("Supervisor output", titleFont, textBrush, bounds.Left + 18, bounds.Top + 14);

        var lines = _consoleDisplayLines;
        if (lines.Length == 0)
        {
            lines = ["Waiting for supervisor output..."];
        }

        var y = bounds.Top + 48;
        var lineHeight = 18;
        var textBounds = new Rectangle(bounds.Left + 18, y, bounds.Width - 36, bounds.Height - 62);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        foreach (var line in lines.Take(textBounds.Height / lineHeight))
        {
            var brush = line.StartsWith("...", StringComparison.Ordinal) ? mutedBrush : textBrush;
            graphics.DrawString(line, consoleFont, brush, new Rectangle(textBounds.Left, y, textBounds.Width, lineHeight + 4), format);
            y += lineHeight;
        }
    }

    private void DrawVer2Footer(Graphics graphics, Font footerFont, Brush mutedBrush)
    {
        var state = _commandInFlight
            ? $"Command running: {_runningCommand ?? "unknown"}"
            : "Ready for dashboard input";
        var refresh = _lastOverlayRefreshAt == DateTimeOffset.MinValue
            ? "not rendered"
            : _lastOverlayRefreshAt.ToLocalTime().ToString("HH:mm:ss");
        var text = $"{state}   |   Last render={refresh}";
        graphics.DrawString(text, footerFont, mutedBrush, ButtonLeft, 858);
    }

    private static string[] BuildConsoleDisplayLines(string[] sourceLines)
    {
        const int maxChars = 142;
        const int maxLines = 11;
        var wrapped = new List<string>();
        foreach (var line in sourceLines)
        {
            var remaining = line.Trim();
            if (remaining.Length == 0)
            {
                continue;
            }

            while (remaining.Length > maxChars)
            {
                wrapped.Add(remaining[..maxChars]);
                remaining = "..." + remaining[maxChars..].TrimStart();
            }

            wrapped.Add(remaining);
        }

        return wrapped
            .TakeLast(maxLines)
            .ToArray();
    }

    private string GetSupervisorStateText()
    {
        if (_status.StartsWith("Could not start", StringComparison.OrdinalIgnoreCase))
        {
            return "startup failed";
        }

        if (_status.StartsWith("Waiting", StringComparison.OrdinalIgnoreCase))
        {
            return "connecting";
        }

        return _commandInFlight ? "busy" : "connected";
    }

    private Color GetSupervisorStateColor()
        => GetSupervisorStateText() switch
        {
            "connected" => Ver2Palette.Good,
            "busy" => Ver2Palette.Warn,
            "connecting" => Ver2Palette.Warn,
            _ => Ver2Palette.Bad
        };

    private static Color GetStatusColor(string value)
    {
        if (value.Contains("running", StringComparison.OrdinalIgnoreCase)
            || value.Contains("powered=True", StringComparison.OrdinalIgnoreCase))
        {
            return Ver2Palette.Good;
        }

        if (value.Contains("disabled", StringComparison.OrdinalIgnoreCase)
            || value.Contains("stopped", StringComparison.OrdinalIgnoreCase)
            || value.Contains("not running", StringComparison.OrdinalIgnoreCase)
            || value.Contains("incomplete", StringComparison.OrdinalIgnoreCase)
            || value.Contains("partial", StringComparison.OrdinalIgnoreCase))
        {
            return Ver2Palette.Warn;
        }

        return Ver2Palette.Neutral;
    }

    private static string FormatStatusValue(string value)
    {
        if (value.Contains("powered=True", StringComparison.OrdinalIgnoreCase))
        {
            return value.Replace("powered=True", "on", StringComparison.OrdinalIgnoreCase);
        }

        if (value.Contains("powered=False", StringComparison.OrdinalIgnoreCase))
        {
            return value.Replace("powered=False", "off", StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }

    private static Color GetBaseStationColor(string value)
    {
        if (value.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return Ver2Palette.Neutral;
        }

        return GetStatusColor(value);
    }

    private static string GetButtonHint(string command)
        => command switch
        {
            "restart-core-apps" => "Broken Eye + VRCFaceTracking",
            "restart-osc-router" => "Restart OSC route bridge",
            "start-osc-goes-brrr" => "Launch or repair workflow",
            "base-stations-on" => "Wake configured stations",
            "base-stations-off" => "Power down configured stations",
            "restart-supervisor" => "Hard restart supervisor",
            _ => command
        };

    private static void FillRoundedRectangle(Graphics graphics, Rectangle bounds, int radius, Color color)
    {
        using var path = RoundedRect(bounds, radius);
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }

    private static void DrawRoundedRectangle(Graphics graphics, Rectangle bounds, int radius, Color color, int width)
    {
        using var path = RoundedRect(bounds, radius);
        using var pen = new Pen(color, width);
        graphics.DrawPath(pen, path);
    }

    private static void DrawText(
        Graphics graphics,
        string text,
        Font font,
        Brush brush,
        Rectangle bounds,
        StringAlignment horizontal,
        StringAlignment vertical)
    {
        using var format = new StringFormat
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private void DrawOverlayIcon(Graphics graphics, Rectangle bounds)
    {
        if (_overlayIconImage is null)
        {
            return;
        }

        DrawImageContained(graphics, _overlayIconImage, bounds);
    }

    private static string GetOverlayIconPath()
        => Path.Combine(AppContext.BaseDirectory, OverlayIconRelativePath);

    private static void DrawImageContained(Graphics graphics, Image image, Rectangle bounds)
    {
        var scale = Math.Min(bounds.Width / (float)image.Width, bounds.Height / (float)image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var x = bounds.Left + (bounds.Width - width) / 2;
        var y = bounds.Top + (bounds.Height - height) / 2;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(image, new Rectangle(x, y, width, height));
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private bool IsSteamVrRunning()
    {
        if (_steamVrProcess is not null)
        {
            try
            {
                if (!_steamVrProcess.HasExited)
                {
                    return true;
                }
            }
            catch
            {
                // Fall through and rescan if the cached handle is no longer usable.
            }

            _steamVrProcess.Dispose();
            _steamVrProcess = null;
        }

        var processes = Process.GetProcessesByName("vrserver");
        if (processes.Length == 0)
        {
            return false;
        }

        _steamVrProcess = processes[0];
        for (var index = 1; index < processes.Length; index++)
        {
            processes[index].Dispose();
        }

        return true;
    }

    private async Task<string> SendCommandAsync(string command, TimeSpan timeout)
    {
        var debugCommand = _diagnostics.ShouldWriteCommandDebug(command);
        Log("Sending TCP command: " + command);
        if (debugCommand)
        {
            WriteDebug("IPC TCP command sending; name=" + command);
        }

        var response = await SendTcpCommandAsync(command, timeout);
        Log("TCP command completed: " + command);
        if (debugCommand)
        {
            WriteDebug("IPC TCP command completed; name=" + command + "; response=" + TruncateDebugValue(response));
        }

        return response;
    }

    private static async Task<string> SendTcpCommandAsync(string command, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", CommandTcpPort, timeoutSource.Token);
        await using var stream = client.GetStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await writer.WriteLineAsync(command.AsMemory(), timeoutSource.Token);
        return await reader.ReadLineAsync(timeoutSource.Token) ?? "No response.";
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(
                _logPath,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }

    private void LogThrottled(ref DateTimeOffset lastLogAt, string message)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - lastLogAt < RepeatedFailureLogInterval)
        {
            return;
        }

        lastLogAt = now;
        Log(message);
        WriteDebug("throttled failure: " + message);
    }

    private void WriteDebug(string message)
        => _diagnostics.WriteDebug(message);

    private static string TruncateDebugValue(string value)
        => value.Length <= 300 ? value : value[..300] + "...";

    private static async Task RunProcessAsync(string fileName, string[] arguments, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        await process.WaitForExitAsync(timeoutSource.Token);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}: {error}{output}");
        }
    }

}

internal sealed record DashboardButton(string Label, string Command, Rectangle Bounds);

internal readonly record struct OverlayViewState(bool DashboardVisible, bool OverlayActive, bool Viewed);

internal readonly record struct OverlayPointer(uint CursorIndex, PointF Raw, PointF Layout, DateTimeOffset Timestamp);

internal readonly record struct ClickResolution(DashboardButton? Button, OverlayPointer Pointer, string Source, string MissReason);

internal sealed record DashboardStatus(
    string Mode,
    string SteamVr,
    string CoreApps,
    string BaseStations,
    string OscRouter,
    string OscGoesBrrr)
{
    public static DashboardStatus Pending { get; } = new("checking", "checking", "checking", "checking", "checking", "checking");

    public static DashboardStatus Parse(string status)
        => TryParse(status, out var parsedStatus) ? parsedStatus : Pending;

    public static bool TryParse(string status, out DashboardStatus parsedStatus)
    {
        if (!status.StartsWith("Mode=", StringComparison.OrdinalIgnoreCase))
        {
            parsedStatus = Pending;
            return false;
        }

        var values = status
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        parsedStatus = new DashboardStatus(
            Get(values, "Mode"),
            Get(values, "SteamVR"),
            Get(values, "CoreApps"),
            Get(values, "BaseStations"),
            Get(values, "OscRouter"),
            Get(values, "OscGoesBrrr"));
        return true;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "checking";
}

internal static class Ver2Palette
{
    public static readonly Color Background = Color.FromArgb(12, 14, 18);
    public static readonly Color Panel = Color.FromArgb(28, 32, 40);
    public static readonly Color PanelDark = Color.FromArgb(18, 21, 27);
    public static readonly Color Button = Color.FromArgb(38, 44, 55);
    public static readonly Color RunningPanel = Color.FromArgb(50, 58, 72);
    public static readonly Color Border = Color.FromArgb(72, 82, 98);
    public static readonly Color BorderStrong = Color.FromArgb(106, 120, 144);
    public static readonly Color Text = Color.FromArgb(242, 246, 252);
    public static readonly Color Muted = Color.FromArgb(156, 168, 186);
    public static readonly Color Accent = Color.FromArgb(68, 142, 255);
    public static readonly Color AccentAlt = Color.FromArgb(56, 210, 173);
    public static readonly Color Good = Color.FromArgb(84, 214, 119);
    public static readonly Color Warn = Color.FromArgb(255, 190, 92);
    public static readonly Color Bad = Color.FromArgb(255, 112, 112);
    public static readonly Color Neutral = Color.FromArgb(142, 155, 176);
}

internal readonly record struct GpuUploadTiming(TimeSpan Copy, TimeSpan UpdateSubresource, TimeSpan Flush, TimeSpan Total);

internal sealed class GpuOverlayRenderer : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _rowPitch;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _texture;
    private readonly byte[] _pixels;
    private bool _disposed;

    public GpuOverlayRenderer(int width, int height)
    {
        _width = width;
        _height = height;
        _rowPitch = width * 4;
        _pixels = new byte[_rowPitch * height];
        _device = D3D11.D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0);
        FeatureLevel = _device.FeatureLevel;
        _context = _device.ImmediateContext;

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };
        _texture = _device.CreateTexture2D(description);
    }

    public FeatureLevel FeatureLevel { get; }

    public IntPtr TexturePointer => _texture.NativePointer;

    public GpuUploadTiming Update(Bitmap bitmap)
    {
        if (bitmap.Width != _width || bitmap.Height != _height)
        {
            throw new ArgumentException("Bitmap size does not match the overlay texture.", nameof(bitmap));
        }

        var totalStartedAt = Stopwatch.GetTimestamp();
        var copyStartedAt = Stopwatch.GetTimestamp();
        CopyBitmapPixels(bitmap);
        var copyElapsed = Stopwatch.GetElapsedTime(copyStartedAt);
        var handle = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
        TimeSpan updateElapsed;
        TimeSpan flushElapsed;
        try
        {
            var updateStartedAt = Stopwatch.GetTimestamp();
            _context.UpdateSubresource(
                _texture,
                0,
                null,
                handle.AddrOfPinnedObject(),
                (uint)_rowPitch,
                (uint)(_pixels.Length));
            updateElapsed = Stopwatch.GetElapsedTime(updateStartedAt);
            var flushStartedAt = Stopwatch.GetTimestamp();
            _context.Flush();
            flushElapsed = Stopwatch.GetElapsedTime(flushStartedAt);
        }
        finally
        {
            handle.Free();
        }

        return new GpuUploadTiming(copyElapsed, updateElapsed, flushElapsed, Stopwatch.GetElapsedTime(totalStartedAt));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _texture.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    private void CopyBitmapPixels(Bitmap bitmap)
    {
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < _height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), _pixels, y * _rowPitch, _rowPitch);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}

internal enum OpenVrEventType : uint
{
    ButtonPress = 200,
    ButtonUnpress = 201,
    MouseMove = 300,
    MouseButtonDown = 301,
    MouseButtonUp = 302,
    MouseFocusEnter = 303,
    MouseFocusLeave = 304,
    TouchPadMove = 306,
    OverlayClosed = 534
}

[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct OpenVrEvent
{
    [FieldOffset(0)]
    public OpenVrEventType EventType;
    [FieldOffset(4)]
    public uint TrackedDeviceIndex;
    [FieldOffset(8)]
    public float EventAgeSeconds;
    [FieldOffset(16)]
    public float MouseX;
    [FieldOffset(20)]
    public float MouseY;
    [FieldOffset(24)]
    public uint MouseButton;
    [FieldOffset(28)]
    public uint CursorIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal struct OpenVrVector2
{
    public float X;
    public float Y;
}

internal enum OpenVrTextureType
{
    Invalid = -1,
    DirectX = 0,
    OpenGl = 1,
    Vulkan = 2,
    IoSurface = 3,
    DirectX12 = 4,
    DxgiSharedHandle = 5,
    Metal = 6
}

internal enum OpenVrColorSpace
{
    Auto = 0,
    Gamma = 1,
    Linear = 2
}

[StructLayout(LayoutKind.Sequential)]
internal struct OpenVrTexture
{
    public IntPtr Handle;
    public OpenVrTextureType EType;
    public OpenVrColorSpace EColorSpace;
}

internal sealed class OpenVrOverlaySession : IDisposable
{
    private const string OpenVrOverlayFnTableVersion = "FnTable:IVROverlay_028";
    private const int VrApplicationOverlay = 2;
    private const int VrInitErrorNone = 0;
    private const int VrOverlayErrorNone = 0;

    private readonly IntPtr _library;
    private readonly VrShutdownInternalDelegate _shutdownInternal;
    private readonly GetOverlayErrorNameFromEnumDelegate _getOverlayErrorNameFromEnum;
    private readonly DestroyOverlayDelegate _destroyOverlay;
    private readonly SetOverlayWidthInMetersDelegate _setOverlayWidthInMeters;
    private readonly SetOverlayFlagDelegate _setOverlayFlag;
    private readonly PollNextOverlayEventDelegate _pollNextOverlayEvent;
    private readonly SetOverlayInputMethodDelegate _setOverlayInputMethod;
    private readonly SetOverlayMouseScaleDelegate _setOverlayMouseScale;
    private readonly SetOverlayTextureDelegate _setOverlayTexture;
    private readonly SetOverlayFromFileDelegate _setOverlayFromFile;
    private readonly CreateDashboardOverlayDelegate _createDashboardOverlay;
    private readonly IsDashboardVisibleDelegate _isDashboardVisible;
    private readonly IsActiveDashboardOverlayDelegate _isActiveDashboardOverlay;
    private bool _initialized = true;
    private ulong _mainHandle;
    private ulong _thumbnailHandle;

    private OpenVrOverlaySession(
        IntPtr library,
        VrShutdownInternalDelegate shutdownInternal,
        OpenVrOverlayFnTable table,
        ulong mainHandle,
        ulong thumbnailHandle)
    {
        _library = library;
        _shutdownInternal = shutdownInternal;
        _getOverlayErrorNameFromEnum = CreateDelegate<GetOverlayErrorNameFromEnumDelegate>(table.GetOverlayErrorNameFromEnum);
        _destroyOverlay = CreateDelegate<DestroyOverlayDelegate>(table.DestroyOverlay);
        _setOverlayWidthInMeters = CreateDelegate<SetOverlayWidthInMetersDelegate>(table.SetOverlayWidthInMeters);
        _setOverlayFlag = CreateDelegate<SetOverlayFlagDelegate>(table.SetOverlayFlag);
        _pollNextOverlayEvent = CreateDelegate<PollNextOverlayEventDelegate>(table.PollNextOverlayEvent);
        _setOverlayInputMethod = CreateDelegate<SetOverlayInputMethodDelegate>(table.SetOverlayInputMethod);
        _setOverlayMouseScale = CreateDelegate<SetOverlayMouseScaleDelegate>(table.SetOverlayMouseScale);
        _setOverlayTexture = CreateDelegate<SetOverlayTextureDelegate>(table.SetOverlayTexture);
        _setOverlayFromFile = CreateDelegate<SetOverlayFromFileDelegate>(table.SetOverlayFromFile);
        _createDashboardOverlay = CreateDelegate<CreateDashboardOverlayDelegate>(table.CreateDashboardOverlay);
        _isDashboardVisible = CreateDelegate<IsDashboardVisibleDelegate>(table.IsDashboardVisible);
        _isActiveDashboardOverlay = CreateDelegate<IsActiveDashboardOverlayDelegate>(table.IsActiveDashboardOverlay);
        _mainHandle = mainHandle;
        _thumbnailHandle = thumbnailHandle;
    }

    public static OpenVrOverlaySession Open(string overlayKey, string overlayName)
    {
        if (!TryFindOpenVrApiDll(out var openVrApiDllPath, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var library = NativeLibrary.Load(openVrApiDllPath);
        try
        {
            var initInternal = GetExportDelegate<VrInitInternalDelegate>(library, "VR_InitInternal");
            var shutdownInternal = GetExportDelegate<VrShutdownInternalDelegate>(library, "VR_ShutdownInternal");
            var getGenericInterface = GetExportDelegate<VrGetGenericInterfaceDelegate>(library, "VR_GetGenericInterface");
            var getInitErrorDescription = GetOptionalExportDelegate<VrGetVrInitErrorAsEnglishDescriptionDelegate>(library, "VR_GetVRInitErrorAsEnglishDescription");

            var initError = 0;
            _ = initInternal(ref initError, VrApplicationOverlay);
            if (initError != VrInitErrorNone)
            {
                throw new InvalidOperationException($"OpenVR overlay init failed: {DescribeOpenVrInitError(initError, getInitErrorDescription)}");
            }

            var interfaceError = 0;
            var overlayTablePointer = getGenericInterface(OpenVrOverlayFnTableVersion, ref interfaceError);
            if (overlayTablePointer == IntPtr.Zero || interfaceError != VrInitErrorNone)
            {
                shutdownInternal();
                throw new InvalidOperationException($"OpenVR overlay interface unavailable: {DescribeOpenVrInitError(interfaceError, getInitErrorDescription)}");
            }

            var table = Marshal.PtrToStructure<OpenVrOverlayFnTable>(overlayTablePointer);
            var createDashboardOverlay = CreateDelegate<CreateDashboardOverlayDelegate>(table.CreateDashboardOverlay);
            ulong mainHandle = 0;
            ulong thumbnailHandle = 0;
            var overlayError = createDashboardOverlay(overlayKey, overlayName, ref mainHandle, ref thumbnailHandle);
            var session = new OpenVrOverlaySession(library, shutdownInternal, table, mainHandle, thumbnailHandle);
            session.ThrowIfOverlayError(overlayError, "CreateDashboardOverlay");
            return session;
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
    }

    public void SetOverlayWidthInMeters(float width)
        => ThrowIfOverlayError(_setOverlayWidthInMeters(_mainHandle, width), "SetOverlayWidthInMeters");

    public void SetOverlayInputMethodMouse()
        => ThrowIfOverlayError(_setOverlayInputMethod(_mainHandle, 1), "SetOverlayInputMethod");

    public void SetInteractiveDashboardFlags()
    {
        const int visibleInDashboard = 1 << 15;
        const int makeInteractiveIfVisible = 1 << 16;
        const int clickStabilization = 1 << 27;
        ThrowIfOverlayError(_setOverlayFlag(_mainHandle, visibleInDashboard, true), "SetOverlayFlag VisibleInDashboard");
        ThrowIfOverlayError(_setOverlayFlag(_mainHandle, makeInteractiveIfVisible, true), "SetOverlayFlag MakeOverlaysInteractiveIfVisible");
        TrySetOverlayFlag(clickStabilization, true);
    }

    private void TrySetOverlayFlag(int flag, bool enabled)
    {
        try
        {
            _ = _setOverlayFlag(_mainHandle, flag, enabled);
        }
        catch
        {
            // Optional OpenVR flags vary by runtime version.
        }
    }

    public void SetOverlayMouseScale(float width, float height)
    {
        var scale = new OpenVrVector2 { X = width, Y = height };
        ThrowIfOverlayError(_setOverlayMouseScale(_mainHandle, ref scale), "SetOverlayMouseScale");
    }

    public void SetOverlayTexture(IntPtr textureHandle)
    {
        var texture = new OpenVrTexture
        {
            Handle = textureHandle,
            EType = OpenVrTextureType.DirectX,
            EColorSpace = OpenVrColorSpace.Auto
        };
        ThrowIfOverlayError(_setOverlayTexture(_mainHandle, ref texture), "SetOverlayTexture");
    }

    public void SetThumbnailFromFile(string path)
        => ThrowIfOverlayError(_setOverlayFromFile(_thumbnailHandle, path), "SetOverlayFromFile thumbnail");

    public bool PollNextOverlayEvent(out OpenVrEvent vrEvent)
    {
        vrEvent = default;
        return _pollNextOverlayEvent(_mainHandle, ref vrEvent, (uint)Marshal.SizeOf<OpenVrEvent>());
    }

    public bool IsDashboardVisible()
        => _isDashboardVisible();

    public bool IsActiveDashboardOverlay()
        => _isActiveDashboardOverlay(_mainHandle);

    public void Dispose()
    {
        try
        {
            if (_mainHandle != 0)
            {
                _destroyOverlay(_mainHandle);
                _mainHandle = 0;
            }

            if (_thumbnailHandle != 0)
            {
                _destroyOverlay(_thumbnailHandle);
                _thumbnailHandle = 0;
            }

            if (_initialized)
            {
                _shutdownInternal();
                _initialized = false;
            }
        }
        finally
        {
            NativeLibrary.Free(_library);
        }
    }

    private void ThrowIfOverlayError(int error, string operation)
    {
        if (error == VrOverlayErrorNone)
        {
            return;
        }

        var errorNamePointer = _getOverlayErrorNameFromEnum(error);
        var errorName = errorNamePointer == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(errorNamePointer);
        throw new InvalidOperationException($"{operation} failed: {(string.IsNullOrWhiteSpace(errorName) ? error.ToString() : errorName)}");
    }

    private static bool TryFindOpenVrApiDll(out string openVrApiDllPath, out string reason)
    {
        foreach (var runtimePath in GetOpenVrRuntimePaths())
        {
            var candidate = Path.Combine(runtimePath, "bin", "win64", "openvr_api.dll");
            if (File.Exists(candidate))
            {
                openVrApiDllPath = candidate;
                reason = "";
                return true;
            }
        }

        openVrApiDllPath = "";
        reason = "openvr_api.dll was not found in the configured SteamVR runtime";
        return false;
    }

    private static IEnumerable<string> GetOpenVrRuntimePaths()
    {
        var openVrPathsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openvr",
            "openvrpaths.vrpath");
        if (File.Exists(openVrPathsFile))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(openVrPathsFile));
            if (document.RootElement.TryGetProperty("runtime", out var runtimeElement) && runtimeElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in runtimeElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } runtimePath)
                    {
                        yield return runtimePath.TrimEnd('\\', '/');
                    }
                }
            }
        }

        const string steamVrUninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 250820";
        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var key = baseKey.OpenSubKey(steamVrUninstallKeyPath);
            if (key?.GetValue("InstallLocation") is string installLocation && !string.IsNullOrWhiteSpace(installLocation))
            {
                yield return installLocation.TrimEnd('\\', '/');
            }
        }
    }

    private static string DescribeOpenVrInitError(int error, VrGetVrInitErrorAsEnglishDescriptionDelegate? getDescription)
    {
        if (getDescription is null)
        {
            return error.ToString();
        }

        var descriptionPointer = getDescription(error);
        var description = descriptionPointer == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(descriptionPointer);
        return string.IsNullOrWhiteSpace(description) ? error.ToString() : $"{description} ({error})";
    }

    private static T GetExportDelegate<T>(IntPtr library, string exportName) where T : Delegate
        => CreateDelegate<T>(NativeLibrary.GetExport(library, exportName));

    private static T? GetOptionalExportDelegate<T>(IntPtr library, string exportName) where T : Delegate
        => NativeLibrary.TryGetExport(library, exportName, out var exportPointer)
            ? CreateDelegate<T>(exportPointer)
            : null;

    private static T CreateDelegate<T>(IntPtr functionPointer) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(functionPointer);

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenVrOverlayFnTable
    {
        public IntPtr FindOverlay;
        public IntPtr CreateOverlay;
        public IntPtr CreateSubviewOverlay;
        public IntPtr DestroyOverlay;
        public IntPtr GetOverlayKey;
        public IntPtr GetOverlayName;
        public IntPtr SetOverlayName;
        public IntPtr GetOverlayImageData;
        public IntPtr GetOverlayErrorNameFromEnum;
        public IntPtr SetOverlayRenderingPid;
        public IntPtr GetOverlayRenderingPid;
        public IntPtr SetOverlayFlag;
        public IntPtr GetOverlayFlag;
        public IntPtr GetOverlayFlags;
        public IntPtr SetOverlayColor;
        public IntPtr GetOverlayColor;
        public IntPtr SetOverlayAlpha;
        public IntPtr GetOverlayAlpha;
        public IntPtr SetOverlayTexelAspect;
        public IntPtr GetOverlayTexelAspect;
        public IntPtr SetOverlaySortOrder;
        public IntPtr GetOverlaySortOrder;
        public IntPtr SetOverlayWidthInMeters;
        public IntPtr GetOverlayWidthInMeters;
        public IntPtr SetOverlayCurvature;
        public IntPtr GetOverlayCurvature;
        public IntPtr SetOverlayPreCurvePitch;
        public IntPtr GetOverlayPreCurvePitch;
        public IntPtr SetOverlayTextureColorSpace;
        public IntPtr GetOverlayTextureColorSpace;
        public IntPtr SetOverlayTextureBounds;
        public IntPtr GetOverlayTextureBounds;
        public IntPtr GetOverlayTransformType;
        public IntPtr SetOverlayTransformAbsolute;
        public IntPtr GetOverlayTransformAbsolute;
        public IntPtr SetOverlayTransformTrackedDeviceRelative;
        public IntPtr GetOverlayTransformTrackedDeviceRelative;
        public IntPtr SetOverlayTransformTrackedDeviceComponent;
        public IntPtr GetOverlayTransformTrackedDeviceComponent;
        public IntPtr SetOverlayTransformCursor;
        public IntPtr GetOverlayTransformCursor;
        public IntPtr SetOverlayTransformProjection;
        public IntPtr SetSubviewPosition;
        public IntPtr ShowOverlay;
        public IntPtr HideOverlay;
        public IntPtr IsOverlayVisible;
        public IntPtr GetTransformForOverlayCoordinates;
        public IntPtr WaitFrameSync;
        public IntPtr PollNextOverlayEvent;
        public IntPtr GetOverlayInputMethod;
        public IntPtr SetOverlayInputMethod;
        public IntPtr GetOverlayMouseScale;
        public IntPtr SetOverlayMouseScale;
        public IntPtr ComputeOverlayIntersection;
        public IntPtr IsHoverTargetOverlay;
        public IntPtr SetOverlayIntersectionMask;
        public IntPtr TriggerLaserMouseHapticVibration;
        public IntPtr SetOverlayCursor;
        public IntPtr SetOverlayCursorPositionOverride;
        public IntPtr ClearOverlayCursorPositionOverride;
        public IntPtr SetOverlayTexture;
        public IntPtr ClearOverlayTexture;
        public IntPtr SetOverlayRaw;
        public IntPtr SetOverlayFromFile;
        public IntPtr GetOverlayTexture;
        public IntPtr ReleaseNativeOverlayHandle;
        public IntPtr GetOverlayTextureSize;
        public IntPtr CreateDashboardOverlay;
        public IntPtr IsDashboardVisible;
        public IntPtr IsActiveDashboardOverlay;
    }

    private delegate IntPtr VrInitInternalDelegate(ref int error, int applicationType);
    private delegate void VrShutdownInternalDelegate();
    private delegate IntPtr VrGetGenericInterfaceDelegate(string interfaceVersion, ref int error);
    private delegate IntPtr VrGetVrInitErrorAsEnglishDescriptionDelegate(int error);
    private delegate int DestroyOverlayDelegate(ulong overlayHandle);
    private delegate IntPtr GetOverlayErrorNameFromEnumDelegate(int error);
    private delegate int SetOverlayWidthInMetersDelegate(ulong overlayHandle, float widthInMeters);
    private delegate int SetOverlayFlagDelegate(ulong overlayHandle, int flag, [MarshalAs(UnmanagedType.I1)] bool enabled);
    private delegate bool PollNextOverlayEventDelegate(ulong overlayHandle, ref OpenVrEvent vrEvent, uint eventSize);
    private delegate int SetOverlayInputMethodDelegate(ulong overlayHandle, int inputMethod);
    private delegate int SetOverlayMouseScaleDelegate(ulong overlayHandle, ref OpenVrVector2 mouseScale);
    private delegate int SetOverlayTextureDelegate(ulong overlayHandle, ref OpenVrTexture texture);
    private delegate int SetOverlayFromFileDelegate(ulong overlayHandle, [MarshalAs(UnmanagedType.LPStr)] string filePath);
    private delegate int CreateDashboardOverlayDelegate([MarshalAs(UnmanagedType.LPStr)] string overlayKey, [MarshalAs(UnmanagedType.LPStr)] string overlayName, ref ulong mainHandle, ref ulong thumbnailHandle);
    private delegate bool IsDashboardVisibleDelegate();
    private delegate bool IsActiveDashboardOverlayDelegate(ulong overlayHandle);
}
