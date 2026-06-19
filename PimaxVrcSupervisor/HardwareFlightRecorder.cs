using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Win32;

namespace PimaxVrcSupervisor.Diagnostics;

public static class HardwareFlightRecorderSchema
{
    public const string Version = "supervisor-hardware-flight-recorder-v1";
    public const string SessionSummaryVersion = "supervisor-session-summary-v1";
}

public sealed record HardwareOperationContext(
    string Category,
    string Routine,
    string[] ResourceTags,
    string? OperationId = null,
    string? ParentOperationId = null,
    string? SessionId = null,
    string? StartupBurstId = null,
    string? StationOperationId = null,
    string? DeviceCategory = null,
    string? DeviceIdentity = null,
    double? ExpectedTimeoutMilliseconds = null);

public sealed record HardwareFlightRecord
{
    public string Schema { get; init; } = HardwareFlightRecorderSchema.Version;
    public string EventId { get; init; } = "";
    public long ProcessSequence { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public long MonotonicTicks { get; init; }
    public string BootIdentity { get; init; } = "";
    public string BootIdentityConfidence { get; init; } = "";
    public string ProcessSessionId { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public string BuildIdentity { get; init; } = "";
    public string Component { get; init; } = "";
    public string Routine { get; init; } = "";
    public string OperationCategory { get; init; } = "";
    public string? OperationId { get; init; }
    public string? ParentOperationId { get; init; }
    public string? SessionId { get; init; }
    public string? StartupBurstId { get; init; }
    public string? StationOperationId { get; init; }
    public string? DeviceCategory { get; init; }
    public string? DeviceIdentity { get; init; }
    public string Stage { get; init; } = "";
    public string? Result { get; init; }
    public string? ExceptionType { get; init; }
    public int? HResult { get; init; }
    public int? Win32Error { get; init; }
    public bool Timeout { get; init; }
    public bool CancellationRequested { get; init; }
    public double? DurationMilliseconds { get; init; }
    public double? ExpectedTimeoutMilliseconds { get; init; }
    public int ActiveOperationCount { get; init; }
    public string[] OverlappingOperationCategories { get; init; } = [];
    public string[] OverlappingOperationIds { get; init; } = [];
    public string[] SharedResourceTags { get; init; } = [];
    public string CleanShutdownState { get; init; } = "notApplicable";
    public string FlushClass { get; init; } = "normal";
    public string[] Warnings { get; init; } = [];
    public long DroppedEventCount { get; init; }
    public long LastDurableSequence { get; init; }
    public int QueueDepth { get; init; }
    public double ProcessUptimeMilliseconds { get; init; }
}

internal interface IHardwareFlightWriter : IDisposable
{
    void Write(string jsonLine, bool durable);
    void Flush(bool durable);
}

internal sealed class RotatingHardwareFlightWriter : IHardwareFlightWriter
{
    internal const long MaxFileBytes = 16L * 1024 * 1024;
    internal const int MaxFiles = 8;
    private static readonly Mutex CrossProcessMutex = new(false, @"Local\PimaxVrcSupervisorHardwareFlightRecorder");
    private readonly string _path;

    public RotatingHardwareFlightWriter(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Write(string jsonLine, bool durable)
    {
        var lockTaken = false;
        try
        {
            lockTaken = CrossProcessMutex.WaitOne(TimeSpan.FromMilliseconds(500));
            if (!lockTaken) throw new TimeoutException("Flight-recorder file mutex timed out.");
            var bytes = Encoding.UTF8.GetBytes(jsonLine + "\n");
            RotateIfNeeded(bytes.Length);
            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            stream.Write(bytes, 0, bytes.Length);
            if (durable) stream.Flush(flushToDisk: true);
        }
        finally
        {
            if (lockTaken) CrossProcessMutex.ReleaseMutex();
        }
    }

    public void Flush(bool durable) { }

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length + incomingBytes <= MaxFileBytes) return;
        for (var index = MaxFiles - 1; index >= 1; index--)
        {
            var source = index == 1 ? _path : $"{_path}.{index - 1}";
            var target = $"{_path}.{index}";
            if (!File.Exists(source)) continue;
            if (File.Exists(target)) File.Delete(target);
            File.Move(source, target);
        }
    }

    public void Dispose() { }
}

internal sealed record ActiveHardwareOperation(
    string OperationId,
    string Category,
    string Routine,
    DateTimeOffset StartedUtc,
    long StartedTicks,
    int ThreadId,
    string[] ResourceTags,
    string? ParentOperationId,
    string LastStage);

internal sealed class ActiveHardwareOperationRegistry
{
    private readonly ConcurrentDictionary<string, ActiveHardwareOperation> _active = new(StringComparer.Ordinal);
    public int Count => _active.Count;
    public bool TryAdd(ActiveHardwareOperation operation) => _active.TryAdd(operation.OperationId, operation);
    public bool TryUpdateStage(string operationId, string stage)
    {
        if (!_active.TryGetValue(operationId, out var current)) return false;
        return _active.TryUpdate(operationId, current with { LastStage = stage }, current);
    }
    public bool TryRemove(string operationId, out ActiveHardwareOperation? operation) => _active.TryRemove(operationId, out operation);
    public ActiveHardwareOperation[] Snapshot() => _active.Values.OrderBy(value => value.StartedUtc).ToArray();
}

internal sealed record FlightWriteRequest(string Json, bool Durable, long Sequence, TaskCompletionSource? Acknowledgement);

internal sealed class HardwareFlightRecorderEngine : IDisposable
{
    internal const int QueueCapacity = 4096;
    internal static readonly TimeSpan CriticalAcknowledgementLimit = TimeSpan.FromMilliseconds(50);
    private readonly IHardwareFlightWriter _writer;
    private readonly Channel<FlightWriteRequest> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _consumer;
    private readonly ActiveHardwareOperationRegistry _registry = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private long _sequence;
    private long _dropped;
    private long _lastDurable;
    private int _queueDepth;
    private int _writerFailures;
    private DateTimeOffset _lastFailureReported = DateTimeOffset.MinValue;

    public HardwareFlightRecorderEngine(IHardwareFlightWriter writer)
    {
        _writer = writer;
        _queue = Channel.CreateBounded<FlightWriteRequest>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _consumer = Task.Run(ConsumeAsync);
    }

    public ActiveHardwareOperationRegistry Registry => _registry;
    public long Dropped => Interlocked.Read(ref _dropped);
    public long LastDurable => Interlocked.Read(ref _lastDurable);
    public int QueueDepth => Volatile.Read(ref _queueDepth);
    public double UptimeMilliseconds => _uptime.Elapsed.TotalMilliseconds;

    public void Enqueue(HardwareFlightRecord record, bool durable)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        record = record with
        {
            ProcessSequence = sequence,
            EventId = $"flight-event-{Guid.NewGuid():N}",
            DroppedEventCount = Dropped,
            LastDurableSequence = LastDurable,
            QueueDepth = QueueDepth,
            ProcessUptimeMilliseconds = UptimeMilliseconds
        };
        var acknowledgement = durable ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        var request = new FlightWriteRequest(JsonSerializer.Serialize(record, HardwareFlightRecorder.JsonOptions), durable, sequence, acknowledgement);
        if (!_queue.Writer.TryWrite(request))
        {
            Interlocked.Increment(ref _dropped);
            return;
        }
        Interlocked.Increment(ref _queueDepth);
        if (acknowledgement is not null)
        {
            acknowledgement.Task.Wait(CriticalAcknowledgementLimit);
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(_shutdown.Token))
            {
                Interlocked.Decrement(ref _queueDepth);
                try
                {
                    _writer.Write(request.Json, request.Durable);
                    if (request.Durable) Interlocked.Exchange(ref _lastDurable, request.Sequence);
                    request.Acknowledgement?.TrySetResult();
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _writerFailures);
                    Interlocked.Increment(ref _dropped);
                    request.Acknowledgement?.TrySetException(ex);
                    if (DateTimeOffset.UtcNow - _lastFailureReported > TimeSpan.FromMinutes(1))
                    {
                        _lastFailureReported = DateTimeOffset.UtcNow;
                        try { Console.Error.WriteLine($"Hardware flight recorder write failed ({_writerFailures}): {ex.GetType().Name}"); } catch { }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        if (!_consumer.Wait(TimeSpan.FromSeconds(2))) _shutdown.Cancel();
        try { _writer.Flush(durable: true); } catch { }
        _writer.Dispose();
        _shutdown.Dispose();
    }
}

public sealed class HardwareOperationScope : IDisposable
{
    private readonly HardwareFlightRecorderEngine? _engine;
    private readonly HardwareOperationContext _context;
    private readonly long _startedTicks;
    private int _terminal;

    internal HardwareOperationScope(HardwareFlightRecorderEngine? engine, HardwareOperationContext context)
    {
        _engine = engine;
        _context = context with { OperationId = context.OperationId ?? $"hardware-op-{Guid.NewGuid():N}" };
        _startedTicks = Stopwatch.GetTimestamp();
        if (engine is null) return;
        if (!engine.Registry.TryAdd(new(_context.OperationId!, _context.Category, _context.Routine, DateTimeOffset.UtcNow,
                _startedTicks, Environment.CurrentManagedThreadId, _context.ResourceTags, _context.ParentOperationId, "operationStarted")))
            throw new InvalidOperationException($"Duplicate hardware operation ID: {_context.OperationId}");
        HardwareFlightRecorder.Record(_context, "operationStarted", durable: true);
    }

    public string OperationId => _context.OperationId!;
    public void Stage(string stage, string? result = null, bool durable = false, Exception? exception = null, bool timeout = false, bool cancelled = false)
    {
        _engine?.Registry.TryUpdateStage(OperationId, stage);
        HardwareFlightRecorder.Record(_context, stage, result, durable, exception, timeout, cancelled,
            Stopwatch.GetElapsedTime(_startedTicks).TotalMilliseconds);
    }
    public void Completed(string? result = "succeeded") => Terminal("operationCompleted", result, null, false, false);
    public void Failed(Exception exception) => Terminal("operationFailed", "failed", exception, false, false);
    public void TimedOut(Exception exception) => Terminal("operationTimedOut", "timeout", exception, true, false);
    public void Cancelled(Exception? exception = null) => Terminal("operationCancelled", "cancelled", exception, false, true);
    public void Abandoned(string warning) => Terminal("operationAbandoned", "abandoned", null, false, false, [warning]);

    private void Terminal(string stage, string? result, Exception? exception, bool timeout, bool cancelled, string[]? warnings = null)
    {
        if (Interlocked.Exchange(ref _terminal, 1) != 0) return;
        _engine?.Registry.TryRemove(OperationId, out _);
        HardwareFlightRecorder.Record(_context, stage, result, durable: true, exception, timeout, cancelled,
            Stopwatch.GetElapsedTime(_startedTicks).TotalMilliseconds, warnings);
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _terminal) == 0) Abandoned("Operation scope disposed without an explicit terminal stage.");
    }
}

public sealed record PreviousSessionAssessment(
    string Classification,
    string Confidence,
    string? PreviousProcessSessionId,
    string? PreviousBootIdentity,
    DateTimeOffset? LastDurableEventUtc,
    bool PreviousCleanShutdown,
    string[] Warnings);

public sealed class HardwareFlightRecorderSession : IDisposable
{
    private readonly string _summaryPath;
    private readonly System.Threading.Timer _heartbeat;
    private int _clean;
    private int _disposed;
    internal HardwareFlightRecorderSession(string summaryPath, PreviousSessionAssessment assessment)
    {
        _summaryPath = summaryPath;
        PreviousSession = assessment;
        HardwareFlightRecorder.RecordLifecycle("processSessionStarted", "running", durable: true);
        HardwareFlightRecorder.RecordLifecycle("previousSessionAssessed", assessment.Classification, durable: true,
            warnings: assessment.Warnings);
        _heartbeat = new System.Threading.Timer(_ => Heartbeat(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        try { SystemEvents.SessionEnding += OnSessionEnding; } catch { }
    }

    public PreviousSessionAssessment PreviousSession { get; }
    public void ShutdownRequested(string source) => HardwareFlightRecorder.RecordLifecycle("shutdownRequested", source, durable: true);
    public void MarkCleanShutdown(string result = "completed")
    {
        if (Interlocked.Exchange(ref _clean, 1) != 0) return;
        HardwareFlightRecorder.RecordLifecycle("cleanShutdownStarted", result, durable: true);
        HardwareFlightRecorder.RecordLifecycle("cleanShutdownCompleted", result, durable: true, cleanShutdownState: "clean");
    }

    private void Heartbeat()
    {
        HardwareFlightRecorder.RecordLifecycle("heartbeat", "healthy", durable: false);
        var due = HardwareFlightRecorder.ActiveOperationCount > 0 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        _heartbeat.Change(due, Timeout.InfiniteTimeSpan);
    }
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
        HardwareFlightRecorder.RecordLifecycle("unhandledExceptionObserved", args.IsTerminating ? "terminating" : "observed", true,
            args.ExceptionObject as Exception);
    private static void OnProcessExit(object? sender, EventArgs args) => HardwareFlightRecorder.RecordLifecycle("processExitObserved", "observed", true);
    private static void OnSessionEnding(object sender, SessionEndingEventArgs args) => HardwareFlightRecorder.RecordLifecycle("windowsSessionEndingObserved", args.Reason.ToString(), true);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _heartbeat.Dispose();
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        try { SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
        HardwareFlightRecorder.RecordLifecycle("processExitObserved", "session-disposed", true);
        HardwareFlightRecorder.Shutdown();
    }
}

public static class HardwareFlightRecorder
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static readonly object Gate = new();
    private static HardwareFlightRecorderEngine? _engine;
    private static BootIdentity _boot = BootIdentity.Capture();
    private static string _processSessionId = "uninitialized";
    private static string _processName = "unknown";
    private static string _buildIdentity = "unknown";
    private static IHardwareFlightWriter? _summaryWriter;
    private static long _summarySequence;

    public static int ActiveOperationCount => _engine?.Registry.Count ?? 0;
    public static HardwareFlightRecorderSession StartDefault(string processName)
    {
        lock (Gate)
        {
            if (_engine is not null) throw new InvalidOperationException("Hardware flight recorder is already initialized.");
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PimaxVrcSupervisor", "Diagnostics", "FlightRecorder");
            var path = Path.Combine(root, "supervisor-hardware-flight-recorder.jsonl");
            var summary = Path.Combine(root, "supervisor-session-summary.jsonl");
            _processSessionId = $"process-session-{Guid.NewGuid():N}";
            _processName = processName;
            _buildIdentity = typeof(HardwareFlightRecorder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            _boot = BootIdentity.Capture();
            var assessment = AssessPreviousSession(summary, _boot, processName);
            _engine = new HardwareFlightRecorderEngine(new RotatingHardwareFlightWriter(path));
            _summaryWriter = new RotatingHardwareFlightWriter(summary);
            return new HardwareFlightRecorderSession(summary, assessment);
        }
    }

    internal static HardwareFlightRecorderSession StartForTest(string processName, IHardwareFlightWriter writer, string summaryPath, BootIdentity boot)
    {
        lock (Gate)
        {
            _processSessionId = $"process-session-{Guid.NewGuid():N}";
            _processName = processName;
            _buildIdentity = "test";
            _boot = boot;
            var assessment = AssessPreviousSession(summaryPath, boot, processName);
            _engine = new HardwareFlightRecorderEngine(writer);
            _summaryWriter = null;
            return new HardwareFlightRecorderSession(summaryPath, assessment);
        }
    }

    public static HardwareOperationScope Begin(HardwareOperationContext context) => new(_engine, context);

    public static async Task<T> NativeCallAsync<T>(HardwareOperationScope scope, string resource, Func<Task<T>> call)
    {
        scope.Stage("nativeOrLibraryCallStarted", resource, durable: true);
        try
        {
            var result = await call();
            scope.Stage("nativeOrLibraryCallReturned", resource, durable: true);
            return result;
        }
        catch (OperationCanceledException ex)
        {
            scope.Cancelled(ex);
            throw;
        }
        catch (Exception ex)
        {
            scope.Failed(ex);
            throw;
        }
    }

    internal static void Record(HardwareOperationContext context, string stage, string? result = null, bool durable = false,
        Exception? exception = null, bool timeout = false, bool cancelled = false, double? durationMilliseconds = null,
        string[]? warnings = null)
    {
        var engine = _engine;
        if (engine is null) return;
        var active = engine.Registry.Snapshot();
        engine.Enqueue(BuildRecord(context, stage, result, durable, exception, timeout, cancelled, durationMilliseconds,
            warnings, active), durable);
    }

    public static void RecordLifecycle(string stage, string result, bool durable, Exception? exception = null,
        string[]? warnings = null, string cleanShutdownState = "notApplicable")
    {
        var engine = _engine;
        if (engine is null) return;
        var context = new HardwareOperationContext("lifecycle", "processLifecycle", []);
        var active = engine.Registry.Snapshot();
        var record = BuildRecord(context, stage, result, durable, exception, false, false, null, warnings, active) with
        {
            CleanShutdownState = cleanShutdownState,
            ActiveOperationCount = active.Length
        };
        engine.Enqueue(record, durable);
        if (durable)
        {
            var summaryRecord = record with
            {
                Schema = HardwareFlightRecorderSchema.SessionSummaryVersion,
                EventId = $"summary-event-{Guid.NewGuid():N}",
                ProcessSequence = Interlocked.Increment(ref _summarySequence)
            };
            try { _summaryWriter?.Write(JsonSerializer.Serialize(summaryRecord, JsonOptions), durable: true); } catch { }
        }
    }

    private static HardwareFlightRecord BuildRecord(HardwareOperationContext context, string stage, string? result, bool durable,
        Exception? exception, bool timeout, bool cancelled, double? durationMilliseconds, string[]? warnings,
        ActiveHardwareOperation[] active) => new()
    {
        TimestampUtc = DateTimeOffset.UtcNow,
        MonotonicTicks = Stopwatch.GetTimestamp(),
        BootIdentity = _boot.Fingerprint,
        BootIdentityConfidence = _boot.Confidence,
        ProcessSessionId = _processSessionId,
        ProcessName = _processName,
        ProcessId = Environment.ProcessId,
        ThreadId = Environment.CurrentManagedThreadId,
        BuildIdentity = _buildIdentity,
        Component = context.Category,
        Routine = context.Routine,
        OperationCategory = context.Category,
        OperationId = context.OperationId,
        ParentOperationId = context.ParentOperationId,
        SessionId = context.SessionId,
        StartupBurstId = context.StartupBurstId,
        StationOperationId = context.StationOperationId,
        DeviceCategory = context.DeviceCategory,
        DeviceIdentity = SanitizeIdentity(context.DeviceIdentity),
        Stage = stage,
        Result = result,
        ExceptionType = exception?.GetType().Name,
        HResult = exception?.HResult,
        Win32Error = exception is System.ComponentModel.Win32Exception win32 ? win32.NativeErrorCode : null,
        Timeout = timeout,
        CancellationRequested = cancelled,
        DurationMilliseconds = durationMilliseconds,
        ExpectedTimeoutMilliseconds = context.ExpectedTimeoutMilliseconds,
        ActiveOperationCount = active.Length,
        OverlappingOperationCategories = active.Where(item => item.OperationId != context.OperationId).Select(item => item.Category).Distinct().Order().ToArray(),
        OverlappingOperationIds = active.Where(item => item.OperationId != context.OperationId).Select(item => item.OperationId).Order().Take(32).ToArray(),
        SharedResourceTags = context.ResourceTags,
        FlushClass = durable ? "critical" : "normal",
        Warnings = warnings ?? []
    };

    public static string? SanitizeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    internal static PreviousSessionAssessment AssessPreviousSession(string summaryPath, BootIdentity currentBoot, string processName)
    {
        try
        {
            if (!File.Exists(summaryPath)) return new("none", "high", null, null, null, true, []);
            var records = File.ReadLines(summaryPath).Select(line => { try { return JsonSerializer.Deserialize<HardwareFlightRecord>(line, JsonOptions); } catch { return null; } }).Where(record => record is not null).ToArray();
            var lastStart = records.LastOrDefault(record => record!.Stage == "processSessionStarted"
                && string.Equals(record.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
            if (lastStart is null) return new("unknownInterruption", "low", null, null, records.LastOrDefault()?.TimestampUtc, false, ["No previous session-start marker."]);
            var tail = records.SkipWhile(record => !ReferenceEquals(record, lastStart))
                .Where(record => string.Equals(record!.ProcessSessionId, lastStart.ProcessSessionId, StringComparison.Ordinal)).ToArray();
            var clean = tail.Any(record => record!.Stage == "cleanShutdownCompleted");
            if (clean) return new("cleanExit", "high", lastStart.ProcessSessionId, lastStart.BootIdentity, tail.Last()!.TimestampUtc, true, []);
            var classification = string.Equals(lastStart.BootIdentity, currentBoot.Fingerprint, StringComparison.Ordinal)
                ? "sameBootProcessInterruption" : "bootChangedWithIncompleteSession";
            return new(classification, currentBoot.Confidence, lastStart.ProcessSessionId, lastStart.BootIdentity, tail.Last()!.TimestampUtc, false,
                ["Missing clean-shutdown completion is interruption evidence, not proof of application or system causality."]);
        }
        catch (Exception ex)
        {
            return new("unknownInterruption", "low", null, null, null, false, [$"Previous-session assessment failed: {ex.GetType().Name}"]);
        }
    }

    internal static void Shutdown()
    {
        lock (Gate)
        {
            _engine?.Dispose();
            _engine = null;
            _summaryWriter?.Dispose();
            _summaryWriter = null;
        }
    }
}

internal sealed record BootIdentity(DateTimeOffset ApproximateBootTimeUtc, long UptimeMilliseconds, int WindowsSessionId, string Fingerprint, string Confidence)
{
    public static BootIdentity Capture()
    {
        var uptime = Math.Max(0, Environment.TickCount64);
        var estimated = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(uptime);
        var normalized = new DateTimeOffset(estimated.Year, estimated.Month, estimated.Day, estimated.Hour, estimated.Minute, 0, TimeSpan.Zero);
        var session = Process.GetCurrentProcess().SessionId;
        var input = $"{normalized:O}|{session}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16].ToLowerInvariant();
        return new(normalized, uptime, session, fingerprint, "approximateMinuteFromGetTickCount64");
    }
}
