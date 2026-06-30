using System.Diagnostics;

namespace PimaxVrcSupervisor.BaseStations;

internal sealed record BaseStationWakeAttemptResult(
    BaseStationDevice Station,
    bool Succeeded,
    BaseStationCommandFailureStage FailureStage,
    Exception? Exception)
{
    public static BaseStationWakeAttemptResult Success(BaseStationDevice station)
        => new(station, true, BaseStationCommandFailureStage.None, null);

    public static BaseStationWakeAttemptResult Failure(
        BaseStationDevice station,
        BaseStationCommandFailureStage stage,
        Exception exception)
        => new(station, false, stage, exception);
}

internal sealed class BluetoothResolutionRefresh
{
    internal const string OperationName = "bluetoothResolutionRefresh";
    internal const string Trigger = "deviceResolution failure";
    private static readonly TimeSpan DefaultCleanupWait = TimeSpan.FromSeconds(2);

    private readonly IBaseStationDiscoveryScanner _scanner;
    private readonly BaseStationDiagnosticSink _diagnostics;
    private readonly TimeSpan _scanDuration;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _cleanupWait;
    private int _started;

    public BluetoothResolutionRefresh(
        IBaseStationDiscoveryScanner scanner,
        BaseStationDiagnosticSink diagnostics,
        TimeSpan? scanDuration = null,
        TimeSpan? timeout = null,
        TimeSpan? cleanupWait = null)
    {
        _scanner = scanner;
        _diagnostics = diagnostics;
        _scanDuration = scanDuration ?? BaseStationDiscovery.ConfiguratorScanDuration;
        _timeout = timeout ?? _scanDuration + DefaultCleanupWait;
        _cleanupWait = cleanupWait ?? DefaultCleanupWait;
    }

    public async Task<IReadOnlyList<BaseStationWakeAttemptResult>> RetryUnresolvedOnceAsync(
        IReadOnlyList<BaseStationWakeAttemptResult> initialResults,
        string wakeSequenceId,
        Func<IReadOnlyList<BaseStationDevice>, CancellationToken, Task<IReadOnlyList<BaseStationWakeAttemptResult>>> retryAsync,
        CancellationToken cancellationToken)
    {
        var unresolved = initialResults
            .Where(result => !result.Succeeded && result.FailureStage == BaseStationCommandFailureStage.DeviceResolution)
            .ToArray();
        if (unresolved.Length == 0 || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return initialResults;
        }

        var operationId = BaseStationDiagnosticSink.CreateId("bluetooth-resolution-refresh");
        var scanSessionId = BaseStationDiagnosticSink.CreateId("bs-resolution-refresh-scan");
        var startedAt = Stopwatch.GetTimestamp();
        var terminalOutcome = "failed";
        int? foundDeviceCount = null;
        int retrySuccessCount = 0;
        long? scanStartedAt = null;
        BaseStationDiscoveryCleanupResult? discoveryCleanup = null;
        Task<IReadOnlyList<BaseStationDevice>>? scanTask = null;

        Write("start", "started");
        Write("triggered", "triggered");
        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCancellation.CancelAfter(_timeout);
        try
        {
            scanStartedAt = Stopwatch.GetTimestamp();
            Write("scanStarted", "started");
            scanTask = _scanner.ScanAsync(
                _scanDuration,
                scanCancellation.Token,
                _diagnostics,
                scanSessionId,
                Trigger,
                cleanup => discoveryCleanup = cleanup);
            var discovered = await scanTask.WaitAsync(_timeout);
            foundDeviceCount = discovered.Count;
            Write("scanCompleted", "succeeded");
        }
        catch (TimeoutException ex)
        {
            scanCancellation.Cancel();
            Write("timedOut", "timeout", exception: ex);
        }
        catch (OperationCanceledException ex)
        {
            var timedOut = !cancellationToken.IsCancellationRequested && scanCancellation.IsCancellationRequested;
            Write(
                timedOut ? "timedOut" : "cancelled",
                timedOut ? "timeout" : "cancelled",
                exception: ex,
                cancellationRequested: cancellationToken.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            Write("failed", "failed", exception: ex, cancellationRequested: cancellationToken.IsCancellationRequested);
        }
        finally
        {
            if (scanTask is not null)
            {
                if (!scanTask.IsCompleted)
                {
                    scanCancellation.Cancel();
                    await ObserveCleanupCompletionAsync(scanTask);
                }

                var cleanupResult = discoveryCleanup?.Result
                    ?? (scanTask.IsCompleted ? "sharedDiscoveryCompleted; cleanupNotReported" : "cleanupWaitTimedOut");
                Write(
                    "watchersStopped",
                    discoveryCleanup?.Succeeded == true ? "succeeded" : "incomplete",
                    cleanupResult: cleanupResult);
            }
        }

        var combined = initialResults.ToArray();
        if (!cancellationToken.IsCancellationRequested)
        {
            Write("retryStarted", "started");
            try
            {
                var retryResults = await retryAsync(unresolved.Select(result => result.Station).ToArray(), cancellationToken);
                retrySuccessCount = retryResults.Count(result => result.Succeeded);
                foreach (var retryResult in retryResults)
                {
                    var index = Array.FindIndex(
                        combined,
                        result => ReferenceEquals(result.Station, retryResult.Station)
                            || string.Equals(
                                result.Station.BluetoothAddress,
                                retryResult.Station.BluetoothAddress,
                                StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                    {
                        combined[index] = retryResult;
                    }
                }

                terminalOutcome = retrySuccessCount == retryResults.Count ? "retrySucceeded" : "retryIncomplete";
                Write("retryCompleted", terminalOutcome);
            }
            catch (OperationCanceledException ex)
            {
                terminalOutcome = "cancelled";
                Write("cancelled", terminalOutcome, exception: ex, cancellationRequested: true);
            }
            catch (Exception ex)
            {
                terminalOutcome = "retryFailed";
                Write("failed", terminalOutcome, exception: ex);
            }
        }
        else
        {
            terminalOutcome = "cancelled";
        }

        Write(
            "complete",
            terminalOutcome,
            cleanupResult: discoveryCleanup?.Result,
            terminal: true);
        return combined;

        void Write(
            string eventType,
            string outcome,
            string? cleanupResult = null,
            Exception? exception = null,
            bool? cancellationRequested = null,
            bool? terminal = null)
        {
            _diagnostics.Write(new BaseStationDiagnosticEvent
            {
                OperationId = operationId,
                OperationName = OperationName,
                WakeSequenceId = wakeSequenceId,
                ScanSessionId = scanSessionId,
                Trigger = Trigger,
                TriggerFailureStage = BaseStationCommandFailureStage.DeviceResolution.ToString(),
                ConfiguredStationCount = initialResults.Count,
                UnresolvedStationCount = unresolved.Length,
                RetryStationCount = unresolved.Length,
                RetrySuccessCount = eventType is "retryCompleted" or "complete" ? retrySuccessCount : null,
                CurrentStage = eventType,
                EventType = eventType,
                TotalAttemptDurationMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                ScanDurationMilliseconds = _scanDuration.TotalMilliseconds,
                ScanElapsedMilliseconds = scanStartedAt is null
                    ? null
                    : Stopwatch.GetElapsedTime(scanStartedAt.Value).TotalMilliseconds,
                TimeoutLimitMilliseconds = _timeout.TotalMilliseconds,
                Outcome = outcome,
                FoundDeviceCount = foundDeviceCount,
                CleanupResult = cleanupResult,
                ErrorCategory = exception switch
                {
                    TimeoutException => "timeout",
                    OperationCanceledException => "cancellation",
                    not null => "exception",
                    _ => null
                },
                ExceptionType = exception?.GetType().Name,
                SanitizedErrorMessage = exception is null ? null : BaseStationDiagnosticSink.SanitizeMessage(exception.Message),
                CancellationRequested = cancellationRequested,
                Terminal = terminal
            });
        }
    }

    private async Task ObserveCleanupCompletionAsync(Task scanTask)
    {
        try
        {
            await scanTask.WaitAsync(_cleanupWait);
        }
        catch
        {
            // The operation result is already recorded; this wait only bounds cleanup observation.
        }
    }
}
