using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PimaxVrcSupervisor.BaseStations;

internal static partial class BaseStationStartupDiagnosticsSchema
{
    public const string Version = "base-station-startup-diagnostics-v1";
}

internal sealed class BaseStationDiagnosticEvent
{
    public string SchemaVersion { get; init; } = BaseStationStartupDiagnosticsSchema.Version;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LocalTimestamp { get; init; } = DateTimeOffset.Now;
    public double? ElapsedMilliseconds { get; init; }
    public string Process { get; init; } = "";
    public int ProcessId { get; init; } = Environment.ProcessId;
    public string ApplicationVersion { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string? OperationId { get; init; }
    public string? OperationName { get; init; }
    public string? ScanSessionId { get; init; }
    public string? Trigger { get; init; }
    public int? BurstNumber { get; init; }
    public int? RetryNumber { get; init; }
    public int? ConfiguredStationCount { get; init; }
    public string? StationIdentity { get; init; }
    public string? StationLabel { get; init; }
    public string? CurrentStage { get; init; }
    public string EventType { get; init; } = "";
    public bool? StageStart { get; init; }
    public double? StageDurationMilliseconds { get; init; }
    public double? TotalAttemptDurationMilliseconds { get; init; }
    public double? ScanDurationMilliseconds { get; init; }
    public double? TimeoutLimitMilliseconds { get; init; }
    public string? AdapterState { get; init; }
    public string? DiscoveryState { get; init; }
    public bool? ConfiguredStationObserved { get; init; }
    public double? ObservationAgeMilliseconds { get; init; }
    public string? DeviceResolutionResult { get; init; }
    public string? GattServiceResult { get; init; }
    public string? CharacteristicResult { get; init; }
    public string? WriteResult { get; init; }
    public string? Outcome { get; init; }
    public int? FoundDeviceCount { get; init; }
    public string? SkipReason { get; init; }
    public string? CleanupResult { get; init; }
    public string? ErrorCategory { get; init; }
    public string? ExceptionType { get; init; }
    public string? SanitizedErrorMessage { get; init; }
    public bool? CancellationRequested { get; init; }
}

internal sealed partial class BaseStationDiagnosticSink
{
    internal const long MaxActiveBytes = 5 * 1024 * 1024;
    internal const int RetainedRotatedFiles = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _lock = new();
    private readonly string _directory;
    private readonly string _processName;
    private readonly string _applicationVersion;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public BaseStationDiagnosticSink(string directory, string processName, string applicationVersion)
    {
        _directory = directory;
        _processName = processName;
        _applicationVersion = applicationVersion;
        SessionId = CreateId("bs-session");
        ActivePath = Path.Combine(directory, $"base-station-startup-{NormalizeFileName(processName)}.jsonl");
    }

    public string SessionId { get; }
    public string ActivePath { get; }

    public static BaseStationDiagnosticSink ForProcess(string processName, string applicationVersion)
        => new(DefaultDirectory(), processName, applicationVersion);

    public static string DefaultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PimaxVrcSupervisor",
            "Diagnostics",
            "BaseStations");

    public void Write(BaseStationDiagnosticEvent diagnosticEvent)
    {
        try
        {
            var enriched = Enrich(diagnosticEvent);
            var line = JsonSerializer.Serialize(enriched, JsonOptions);
            lock (_lock)
            {
                Directory.CreateDirectory(_directory);
                RotateIfNeeded(line);
                File.AppendAllText(ActivePath, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never alter base-station behavior.
        }
    }

    public void WriteEvent(
        string eventType,
        string? trigger = null,
        int? configuredStationCount = null,
        string? operationId = null,
        string? scanSessionId = null,
        string? currentStage = null,
        int? burstNumber = null,
        int? retryNumber = null,
        BaseStationDevice? station = null,
        string? outcome = null,
        string? adapterState = null,
        string? discoveryState = null,
        Exception? exception = null,
        bool? cancellationRequested = null)
    {
        var observed = station is null ? null : BaseStationObservationTracker.TryGetObservationAge(station, DateTimeOffset.UtcNow);
        Write(new BaseStationDiagnosticEvent
        {
            EventType = eventType,
            Trigger = trigger,
            ConfiguredStationCount = configuredStationCount,
            OperationId = operationId,
            ScanSessionId = scanSessionId,
            CurrentStage = currentStage,
            BurstNumber = burstNumber,
            RetryNumber = retryNumber,
            StationIdentity = station is null ? null : StationIdentity(station),
            StationLabel = station is null ? null : StationLabel(station),
            Outcome = outcome,
            AdapterState = adapterState,
            DiscoveryState = discoveryState,
            ConfiguredStationObserved = observed is not null,
            ObservationAgeMilliseconds = observed?.TotalMilliseconds,
            ErrorCategory = exception is null ? null : "exception",
            ExceptionType = exception?.GetType().Name,
            SanitizedErrorMessage = exception is null ? null : SanitizeMessage(exception.Message),
            CancellationRequested = cancellationRequested
        });
    }

    public BaseStationOperationDiagnostics CreateOperation(
        string trigger,
        BaseStationDevice station,
        int burstNumber,
        int retryNumber,
        int configuredStationCount,
        TimeSpan timeoutLimit)
        => new(
            this,
            CreateId("bs-op"),
            trigger,
            station,
            burstNumber,
            retryNumber,
            configuredStationCount,
            timeoutLimit);

    public static string CreateId(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}";

    public static string StationIdentity(BaseStationDevice station)
    {
        var identitySource = string.IsNullOrWhiteSpace(station.BluetoothAddress)
            ? $"{station.Name}|{station.FriendlyName}|{station.Id}|{station.EffectiveVersion}"
            : NormalizeAddress(station.BluetoothAddress);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identitySource.ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    public static string StationLabel(BaseStationDevice station)
    {
        var label = string.IsNullOrWhiteSpace(station.DisplayName)
            ? "configured-station"
            : station.DisplayName.Trim();
        return SanitizeMessage(label, maxLength: 96);
    }

    public static string SanitizeMessage(string? message, int maxLength = 256)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "";
        }

        var sanitized = BluetoothAddressPattern().Replace(message, "[redacted-address]");
        sanitized = ControlCharacterPattern().Replace(sanitized, " ").Trim();
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    internal static bool ContainsBluetoothAddress(string text)
        => BluetoothAddressPattern().IsMatch(text);

    private BaseStationDiagnosticEvent Enrich(BaseStationDiagnosticEvent diagnosticEvent)
        => new()
        {
            SchemaVersion = BaseStationStartupDiagnosticsSchema.Version,
            TimestampUtc = diagnosticEvent.TimestampUtc,
            LocalTimestamp = diagnosticEvent.LocalTimestamp,
            ElapsedMilliseconds = diagnosticEvent.ElapsedMilliseconds ?? _clock.Elapsed.TotalMilliseconds,
            Process = string.IsNullOrWhiteSpace(diagnosticEvent.Process) ? _processName : diagnosticEvent.Process,
            ProcessId = diagnosticEvent.ProcessId,
            ApplicationVersion = string.IsNullOrWhiteSpace(diagnosticEvent.ApplicationVersion) ? _applicationVersion : diagnosticEvent.ApplicationVersion,
            SessionId = string.IsNullOrWhiteSpace(diagnosticEvent.SessionId) ? SessionId : diagnosticEvent.SessionId,
            OperationId = diagnosticEvent.OperationId,
            OperationName = diagnosticEvent.OperationName,
            ScanSessionId = diagnosticEvent.ScanSessionId,
            Trigger = diagnosticEvent.Trigger,
            BurstNumber = diagnosticEvent.BurstNumber,
            RetryNumber = diagnosticEvent.RetryNumber,
            ConfiguredStationCount = diagnosticEvent.ConfiguredStationCount,
            StationIdentity = diagnosticEvent.StationIdentity,
            StationLabel = diagnosticEvent.StationLabel,
            CurrentStage = diagnosticEvent.CurrentStage,
            EventType = diagnosticEvent.EventType,
            StageStart = diagnosticEvent.StageStart,
            StageDurationMilliseconds = diagnosticEvent.StageDurationMilliseconds,
            TotalAttemptDurationMilliseconds = diagnosticEvent.TotalAttemptDurationMilliseconds,
            ScanDurationMilliseconds = diagnosticEvent.ScanDurationMilliseconds,
            TimeoutLimitMilliseconds = diagnosticEvent.TimeoutLimitMilliseconds,
            AdapterState = diagnosticEvent.AdapterState,
            DiscoveryState = diagnosticEvent.DiscoveryState,
            ConfiguredStationObserved = diagnosticEvent.ConfiguredStationObserved,
            ObservationAgeMilliseconds = diagnosticEvent.ObservationAgeMilliseconds,
            DeviceResolutionResult = diagnosticEvent.DeviceResolutionResult,
            GattServiceResult = diagnosticEvent.GattServiceResult,
            CharacteristicResult = diagnosticEvent.CharacteristicResult,
            WriteResult = diagnosticEvent.WriteResult,
            Outcome = diagnosticEvent.Outcome,
            FoundDeviceCount = diagnosticEvent.FoundDeviceCount,
            SkipReason = diagnosticEvent.SkipReason,
            CleanupResult = diagnosticEvent.CleanupResult,
            ErrorCategory = diagnosticEvent.ErrorCategory,
            ExceptionType = diagnosticEvent.ExceptionType,
            SanitizedErrorMessage = diagnosticEvent.SanitizedErrorMessage,
            CancellationRequested = diagnosticEvent.CancellationRequested
        };

    private void RotateIfNeeded(string nextLine)
    {
        var nextBytes = Encoding.UTF8.GetByteCount(nextLine) + Environment.NewLine.Length;
        if (!File.Exists(ActivePath) || new FileInfo(ActivePath).Length + nextBytes <= MaxActiveBytes)
        {
            return;
        }

        for (var index = RetainedRotatedFiles; index >= 1; index--)
        {
            var path = $"{ActivePath}.{index}";
            if (!File.Exists(path))
            {
                continue;
            }

            if (index == RetainedRotatedFiles)
            {
                File.Delete(path);
            }
            else
            {
                File.Move(path, $"{ActivePath}.{index + 1}", overwrite: true);
            }
        }

        File.Move(ActivePath, $"{ActivePath}.1", overwrite: true);
    }

    private static string NormalizeAddress(string address)
        => new(address.Where(Uri.IsHexDigit).ToArray());

    private static string NormalizeFileName(string value)
    {
        var cleaned = new string(value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "process" : cleaned.Trim('-');
    }

    [GeneratedRegex(@"(?i)(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}|(?<![0-9a-f])[0-9a-f]{12}(?![0-9a-f])")]
    private static partial Regex BluetoothAddressPattern();

    [GeneratedRegex(@"[\r\n\t\p{Cc}]+")]
    private static partial Regex ControlCharacterPattern();
}

internal sealed class BaseStationOperationDiagnostics
{
    private readonly BaseStationDiagnosticSink _sink;
    private readonly string _operationId;
    private readonly string _trigger;
    private readonly BaseStationDevice _station;
    private readonly int _burstNumber;
    private readonly int _retryNumber;
    private readonly int _configuredStationCount;
    private readonly TimeSpan _timeoutLimit;
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private long _stageStartedAt;

    public BaseStationOperationDiagnostics(
        BaseStationDiagnosticSink sink,
        string operationId,
        string trigger,
        BaseStationDevice station,
        int burstNumber,
        int retryNumber,
        int configuredStationCount,
        TimeSpan timeoutLimit)
    {
        _sink = sink;
        _operationId = operationId;
        _trigger = trigger;
        _station = station;
        _burstNumber = burstNumber;
        _retryNumber = retryNumber;
        _configuredStationCount = configuredStationCount;
        _timeoutLimit = timeoutLimit;
    }

    public string OperationId => _operationId;
    public string CurrentStage { get; private set; } = "notStarted";

    public void BeginStage(string stage)
    {
        CurrentStage = stage;
        _stageStartedAt = Stopwatch.GetTimestamp();
        Write(stage + "Started", stageStart: true);
    }

    public void CompleteStage(
        string stage,
        string? deviceResolutionResult = null,
        string? gattServiceResult = null,
        string? characteristicResult = null,
        string? writeResult = null)
    {
        Write(
            stage + "Completed",
            currentStage: stage,
            stageDuration: Stopwatch.GetElapsedTime(_stageStartedAt),
            deviceResolutionResult: deviceResolutionResult,
            gattServiceResult: gattServiceResult,
            characteristicResult: characteristicResult,
            writeResult: writeResult);
    }

    public void Succeeded()
        => Write("stationAttemptSucceeded", outcome: "succeeded");

    public void Failed(Exception exception, bool cancellationRequested)
        => Write("stationAttemptFailed", outcome: "failed", exception: exception, cancellationRequested: cancellationRequested);

    public void TimedOut(Exception exception)
        => Write("stationAttemptTimedOut", outcome: "timeout", exception: exception, cancellationRequested: false);

    public void Cancelled(Exception exception)
        => Write("stationAttemptCancelled", outcome: "cancelled", exception: exception, cancellationRequested: true);

    private void Write(
        string eventType,
        string? currentStage = null,
        bool? stageStart = null,
        TimeSpan? stageDuration = null,
        string? deviceResolutionResult = null,
        string? gattServiceResult = null,
        string? characteristicResult = null,
        string? writeResult = null,
        string? outcome = null,
        Exception? exception = null,
        bool? cancellationRequested = null)
    {
        var observed = BaseStationObservationTracker.TryGetObservationAge(_station, DateTimeOffset.UtcNow);
        _sink.Write(new BaseStationDiagnosticEvent
        {
            EventType = eventType,
            OperationId = _operationId,
            Trigger = _trigger,
            BurstNumber = _burstNumber,
            RetryNumber = _retryNumber,
            ConfiguredStationCount = _configuredStationCount,
            StationIdentity = BaseStationDiagnosticSink.StationIdentity(_station),
            StationLabel = BaseStationDiagnosticSink.StationLabel(_station),
            CurrentStage = currentStage ?? CurrentStage,
            StageStart = stageStart,
            StageDurationMilliseconds = stageDuration?.TotalMilliseconds,
            TotalAttemptDurationMilliseconds = _total.Elapsed.TotalMilliseconds,
            TimeoutLimitMilliseconds = _timeoutLimit.TotalMilliseconds,
            ConfiguredStationObserved = observed is not null,
            ObservationAgeMilliseconds = observed?.TotalMilliseconds,
            DeviceResolutionResult = deviceResolutionResult,
            GattServiceResult = gattServiceResult,
            CharacteristicResult = characteristicResult,
            WriteResult = writeResult,
            Outcome = outcome,
            ErrorCategory = exception is null ? null : (exception is TimeoutException ? "timeout" : "exception"),
            ExceptionType = exception?.GetType().Name,
            SanitizedErrorMessage = exception is null ? null : BaseStationDiagnosticSink.SanitizeMessage(exception.Message),
            CancellationRequested = cancellationRequested
        });
    }
}

internal static class BaseStationObservationTracker
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> ObservedAtByStation = new(StringComparer.OrdinalIgnoreCase);

    public static void Record(BaseStationDevice station, DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(station.BluetoothAddress))
        {
            return;
        }

        ObservedAtByStation[Normalize(station.BluetoothAddress)] = observedAt;
    }

    public static TimeSpan? TryGetObservationAge(BaseStationDevice station, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(station.BluetoothAddress)
            || !ObservedAtByStation.TryGetValue(Normalize(station.BluetoothAddress), out var observedAt))
        {
            return null;
        }

        return now >= observedAt ? now - observedAt : TimeSpan.Zero;
    }

    internal static void ClearForTests() => ObservedAtByStation.Clear();

    private static string Normalize(string address)
        => new string(address.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
}
