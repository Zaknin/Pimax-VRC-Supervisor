using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PimaxVrcSupervisor;

internal static class XsOverlayOperationalDiagnosticsSchema
{
    public const string Version = "xs-overlay-transition-diagnostics-v1";
}

internal interface IXsOverlayDiagnosticSink
{
    void Write(XsOverlayMonitorTransitionEvent diagnosticEvent);
}

internal sealed class XsOverlayDiagnosticSink : IXsOverlayDiagnosticSink, IDisposable
{
    internal const long MaxActiveBytes = 1024 * 1024;
    internal const int RetainedRotatedFiles = 2;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _lock = new();
    private readonly string _directory;
    private readonly string _processName;
    private readonly string _applicationVersion;
    private readonly int _supervisorProcessId;
    private readonly long _maxActiveBytes;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Func<XsOverlayDiagnosticRecord, string> _serialize;
    private readonly Action<string> _createDirectory;
    private readonly Action<string, string> _appendLine;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, long> _fileLength;
    private readonly Action<string> _deleteFile;
    private readonly Action<string, string> _moveFile;

    public XsOverlayDiagnosticSink(
        string directory,
        string processName,
        string applicationVersion,
        int? supervisorProcessId = null,
        long? maxActiveBytes = null,
        Func<XsOverlayDiagnosticRecord, string>? serialize = null,
        Action<string>? createDirectory = null,
        Action<string, string>? appendLine = null,
        Func<string, bool>? fileExists = null,
        Func<string, long>? fileLength = null,
        Action<string>? deleteFile = null,
        Action<string, string>? moveFile = null)
    {
        _directory = directory;
        _processName = processName;
        _applicationVersion = applicationVersion;
        _supervisorProcessId = supervisorProcessId ?? Environment.ProcessId;
        _maxActiveBytes = maxActiveBytes ?? MaxActiveBytes;
        _serialize = serialize ?? (record => JsonSerializer.Serialize(record, JsonOptions));
        _createDirectory = createDirectory ?? (path => Directory.CreateDirectory(path));
        _appendLine = appendLine ?? AppendLine;
        _fileExists = fileExists ?? File.Exists;
        _fileLength = fileLength ?? (path => new FileInfo(path).Length);
        _deleteFile = deleteFile ?? File.Delete;
        _moveFile = moveFile ?? ((source, destination) => File.Move(source, destination, overwrite: true));
        ActivePath = Path.Combine(directory, "xs-overlay-supervisor.jsonl");
    }

    public string ActivePath { get; }

    public static XsOverlayDiagnosticSink ForProcess(string processName, string applicationVersion)
        => new(DefaultDirectory(), processName, applicationVersion);

    public static string DefaultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PimaxVrcSupervisor",
            "Diagnostics",
            "XSOverlay");

    public void Write(XsOverlayMonitorTransitionEvent diagnosticEvent)
    {
        try
        {
            var record = CreateRecord(diagnosticEvent);
            var line = _serialize(record);
            lock (_lock)
            {
                _createDirectory(_directory);
                RotateIfNeeded(line);
                _appendLine(ActivePath, line);
            }
        }
        catch
        {
            // XSOverlay operational diagnostics must never affect transition behavior.
        }
    }

    public void Dispose()
    {
        try
        {
            _elapsed.Stop();
        }
        catch
        {
            // Best-effort only; writes are synchronous and unbuffered.
        }
    }

    private XsOverlayDiagnosticRecord CreateRecord(XsOverlayMonitorTransitionEvent diagnosticEvent)
    {
        var operation = string.IsNullOrWhiteSpace(diagnosticEvent.OperationName)
            ? XsOverlaySafeMonitorTransitionCoordinator.OperationName
            : diagnosticEvent.OperationName;
        var correlationId = string.IsNullOrWhiteSpace(diagnosticEvent.CorrelationId)
            ? diagnosticEvent.OperationId
            : diagnosticEvent.CorrelationId;

        return new XsOverlayDiagnosticRecord
        {
            SchemaVersion = XsOverlayOperationalDiagnosticsSchema.Version,
            TimestampUtc = diagnosticEvent.TimestampUtc,
            ElapsedMilliseconds = _elapsed.Elapsed.TotalMilliseconds,
            Operation = operation,
            OperationName = operation,
            OperationId = diagnosticEvent.OperationId,
            CorrelationId = correlationId,
            EventType = diagnosticEvent.EventType,
            Process = _processName,
            SupervisorProcessId = _supervisorProcessId,
            ApplicationVersion = _applicationVersion,
            OriginalPid = diagnosticEvent.OriginalPid,
            RestartedPid = diagnosticEvent.RestartedPid,
            SessionId = diagnosticEvent.SessionId,
            ExecutableIdentity = diagnosticEvent.ExecutableIdentity,
            SafeExecutablePath = diagnosticEvent.SafeExecutablePath,
            RestartMechanism = diagnosticEvent.RestartMechanism,
            LaunchRoute = diagnosticEvent.LaunchRoute,
            ValidatedAppId = diagnosticEvent.ValidatedAppId,
            RegistrationSource = diagnosticEvent.RegistrationSource,
            WindowVerificationResult = diagnosticEvent.WindowVerificationResult,
            StopMechanism = diagnosticEvent.StopMechanism,
            StopElapsedMilliseconds = diagnosticEvent.StopElapsedMilliseconds,
            MonitorResult = diagnosticEvent.MonitorResult,
            TopologySettleElapsedMilliseconds = diagnosticEvent.TopologySettleElapsedMilliseconds,
            RestartResult = diagnosticEvent.RestartResult,
            Outcome = diagnosticEvent.Outcome,
            SkipReason = diagnosticEvent.SkipReason
        };
    }

    private void RotateIfNeeded(string nextLine)
    {
        var nextBytes = Encoding.UTF8.GetByteCount(nextLine) + Environment.NewLine.Length;
        if (!_fileExists(ActivePath))
        {
            return;
        }

        var currentLength = _fileLength(ActivePath);
        if (currentLength < _maxActiveBytes && currentLength + nextBytes <= _maxActiveBytes)
        {
            return;
        }

        for (var index = RetainedRotatedFiles; index >= 1; index--)
        {
            var path = $"{ActivePath}.{index}";
            if (!_fileExists(path))
            {
                continue;
            }

            if (index == RetainedRotatedFiles)
            {
                _deleteFile(path);
            }
            else
            {
                _moveFile(path, $"{ActivePath}.{index + 1}");
            }
        }

        _moveFile(ActivePath, $"{ActivePath}.1");
    }

    private static void AppendLine(string path, string line)
        => File.AppendAllText(path, line + Environment.NewLine, Utf8NoBom);
}

internal sealed class XsOverlayDiagnosticRecord
{
    public string SchemaVersion { get; init; } = XsOverlayOperationalDiagnosticsSchema.Version;
    public DateTimeOffset TimestampUtc { get; init; }
    public double? ElapsedMilliseconds { get; init; }
    public string Operation { get; init; } = "";
    public string OperationName { get; init; } = "";
    public string OperationId { get; init; } = "";
    public string CorrelationId { get; init; } = "";
    public string EventType { get; init; } = "";
    public string Process { get; init; } = "";
    public int SupervisorProcessId { get; init; }
    public string ApplicationVersion { get; init; } = "";
    public int? OriginalPid { get; init; }
    public int? RestartedPid { get; init; }
    public int? SessionId { get; init; }
    public string? ExecutableIdentity { get; init; }
    public string? SafeExecutablePath { get; init; }
    public string? RestartMechanism { get; init; }
    public string? LaunchRoute { get; init; }
    public string? ValidatedAppId { get; init; }
    public string? RegistrationSource { get; init; }
    public string? WindowVerificationResult { get; init; }
    public string? StopMechanism { get; init; }
    public double? StopElapsedMilliseconds { get; init; }
    public string? MonitorResult { get; init; }
    public double? TopologySettleElapsedMilliseconds { get; init; }
    public string? RestartResult { get; init; }
    public string? Outcome { get; init; }
    public string? SkipReason { get; init; }
}

internal static class XsOverlayDiagnosticDispatch
{
    public static void Write(
        XsOverlayMonitorTransitionEvent diagnosticEvent,
        IXsOverlayDiagnosticSink persistentSink,
        Action<string> optionalSupervisorDiagnostics,
        JsonSerializerOptions supervisorJsonOptions)
    {
        try
        {
            persistentSink.Write(diagnosticEvent);
        }
        catch
        {
            // Sink implementations are expected to be best-effort, but dispatch is also fail-closed.
        }

        optionalSupervisorDiagnostics(JsonSerializer.Serialize(diagnosticEvent, supervisorJsonOptions));
    }
}
