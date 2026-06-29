using System.Diagnostics;

namespace PimaxVrcSupervisor.BaseStations;

internal interface IBaseStationDiscoveryScanner
{
    Task<IReadOnlyList<BaseStationDevice>> ScanAsync(
        TimeSpan duration,
        CancellationToken cancellationToken,
        BaseStationDiagnosticSink diagnostics,
        string scanSessionId,
        string trigger,
        Action<BaseStationDiscoveryCleanupResult> cleanupObserver);
}

internal sealed class SharedBaseStationDiscoveryScanner : IBaseStationDiscoveryScanner
{
    public Task<IReadOnlyList<BaseStationDevice>> ScanAsync(
        TimeSpan duration,
        CancellationToken cancellationToken,
        BaseStationDiagnosticSink diagnostics,
        string scanSessionId,
        string trigger,
        Action<BaseStationDiscoveryCleanupResult> cleanupObserver)
        => BaseStationDiscovery.ScanAsync(
            duration,
            cancellationToken,
            diagnostics,
            scanSessionId,
            trigger,
            cleanupObserver);
}

internal sealed record BluetoothStartupInitializationResult(
    string Outcome,
    int? FoundDeviceCount,
    string? SkipReason,
    string? CleanupResult);

internal sealed class BluetoothStartupInitializer
{
    internal const string OperationName = "bluetoothStartupInitialization";
    internal const string Trigger = "Watcher startup";
    private static readonly TimeSpan DefaultCleanupWait = TimeSpan.FromSeconds(2);

    private readonly IBaseStationDiscoveryScanner _scanner;
    private readonly BaseStationDiagnosticSink _diagnostics;
    private readonly TimeSpan _scanDuration;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _cleanupWait;
    private int _started;

    public BluetoothStartupInitializer(
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

    public async Task<BluetoothStartupInitializationResult> RunOnceAsync(
        global::SupervisorConfig config,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return new BluetoothStartupInitializationResult("alreadyRan", null, "processLifetimeGuard", null);
        }

        var operationId = BaseStationDiagnosticSink.CreateId("bluetooth-startup-init");
        var scanSessionId = BaseStationDiagnosticSink.CreateId("bs-watcher-startup-scan");
        var startedAt = Stopwatch.GetTimestamp();
        var configuredStationCount = config.BaseStations.Count(station => station.Enabled);
        var terminalOutcome = "failed";
        int? foundDeviceCount = null;
        string? skipReason = null;
        BaseStationDiscoveryCleanupResult? discoveryCleanup = null;
        Task<IReadOnlyList<BaseStationDevice>>? scanTask = null;

        Write("start", "started");
        if (!IsRelevant(config, out skipReason))
        {
            terminalOutcome = "skipped";
            Write("skipped", terminalOutcome, skipReason: skipReason);
            Write("complete", terminalOutcome, skipReason: skipReason);
            return new BluetoothStartupInitializationResult(terminalOutcome, null, skipReason, null);
        }

        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCancellation.CancelAfter(_timeout);
        try
        {
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
            terminalOutcome = "scanCompleted";
            Write("scanCompleted", "succeeded", foundDeviceCount: foundDeviceCount);
        }
        catch (TimeoutException ex)
        {
            scanCancellation.Cancel();
            terminalOutcome = "timedOut";
            Write("timedOut", "timeout", exception: ex);
        }
        catch (OperationCanceledException ex)
        {
            terminalOutcome = !cancellationToken.IsCancellationRequested && scanCancellation.IsCancellationRequested
                ? "timedOut"
                : "cancelled";
            Write(
                terminalOutcome,
                terminalOutcome == "timedOut" ? "timeout" : "cancelled",
                exception: ex,
                cancellationRequested: cancellationToken.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            terminalOutcome = "failed";
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
                var cleanupSucceeded = discoveryCleanup?.Succeeded == true;
                Write(
                    "watchersStopped",
                    cleanupSucceeded ? "succeeded" : "incomplete",
                    cleanupResult: cleanupResult);
            }

            Write(
                "complete",
                terminalOutcome,
                foundDeviceCount: foundDeviceCount,
                skipReason: skipReason,
                cleanupResult: discoveryCleanup?.Result);
        }

        return new BluetoothStartupInitializationResult(
            terminalOutcome,
            foundDeviceCount,
            skipReason,
            discoveryCleanup?.Result);

        void Write(
            string eventType,
            string outcome,
            int? foundDeviceCount = null,
            string? skipReason = null,
            string? cleanupResult = null,
            Exception? exception = null,
            bool? cancellationRequested = null)
        {
            _diagnostics.Write(new BaseStationDiagnosticEvent
            {
                OperationId = operationId,
                OperationName = OperationName,
                ScanSessionId = scanSessionId,
                Trigger = Trigger,
                ConfiguredStationCount = configuredStationCount,
                CurrentStage = eventType,
                EventType = eventType,
                TotalAttemptDurationMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                ScanDurationMilliseconds = _scanDuration.TotalMilliseconds,
                TimeoutLimitMilliseconds = _timeout.TotalMilliseconds,
                Outcome = outcome,
                FoundDeviceCount = foundDeviceCount,
                SkipReason = skipReason,
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
                CancellationRequested = cancellationRequested
            });
        }
    }

    internal static bool IsRelevant(global::SupervisorConfig config, out string? skipReason)
    {
        if (!config.BaseStationsEnabled)
        {
            skipReason = "baseStationsDisabled";
            return false;
        }

        if (!config.BaseStations.Any(station => station.Enabled))
        {
            skipReason = "noEnabledBaseStations";
            return false;
        }

        skipReason = null;
        return true;
    }

    private async Task ObserveCleanupCompletionAsync(Task scanTask)
    {
        try
        {
            await scanTask.WaitAsync(_cleanupWait);
        }
        catch
        {
            // The scan's terminal result is already recorded; this wait only bounds cleanup observation.
        }
    }
}
