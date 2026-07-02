using System.Diagnostics;
using System.Threading.Channels;

namespace PimaxVrcSupervisor.BaseStations;

internal sealed record BaseStationWakeAttemptResult(
    BaseStationDevice Station,
    bool Succeeded,
    BaseStationCommandFailureStage FailureStage,
    Exception? Exception,
    bool Attempted = true,
    double? ElapsedMilliseconds = null)
{
    public static BaseStationWakeAttemptResult Success(BaseStationDevice station, double? elapsedMilliseconds = null)
        => new(station, true, BaseStationCommandFailureStage.None, null, true, elapsedMilliseconds);

    public static BaseStationWakeAttemptResult Failure(
        BaseStationDevice station,
        BaseStationCommandFailureStage stage,
        Exception exception,
        bool attempted = true,
        double? elapsedMilliseconds = null)
        => new(station, false, stage, exception, attempted, elapsedMilliseconds);

    public static BaseStationWakeAttemptResult UnattemptedResolutionCandidate(BaseStationDevice station)
        => new(
            station,
            false,
            BaseStationCommandFailureStage.DeviceResolution,
            new BaseStationCommandException(
                BaseStationCommandFailureStage.DeviceResolution,
                $"Skipped pre-refresh wake attempt for {station.DisplayName} after an earlier station proved Bluetooth resolution state stale."),
            false,
            null);
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
        Func<BaseStationDevice, CancellationToken, Task<BaseStationWakeAttemptResult>> streamingWakeAsync,
        Func<IReadOnlyList<BaseStationDevice>, CancellationToken, Task<IReadOnlyList<BaseStationWakeAttemptResult>>> fallbackRetryAsync,
        CancellationToken cancellationToken)
    {
        var firstResolutionFailure = initialResults
            .Select((result, index) => new { Result = result, Index = index })
            .FirstOrDefault(item =>
                item.Result.Attempted
                && !item.Result.Succeeded
                && item.Result.FailureStage == BaseStationCommandFailureStage.DeviceResolution);
        var candidates = initialResults
            .Where(result => !result.Succeeded && result.FailureStage == BaseStationCommandFailureStage.DeviceResolution)
            .ToArray();
        if (firstResolutionFailure is null || candidates.Length == 0 || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return initialResults;
        }

        var operationId = BaseStationDiagnosticSink.CreateId("bluetooth-resolution-refresh");
        var scanSessionId = BaseStationDiagnosticSink.CreateId("bs-resolution-refresh-scan");
        var startedAt = Stopwatch.GetTimestamp();
        var terminalOutcome = "failed";
        int? foundDeviceCount = null;
        long? scanStartedAt = null;
        BaseStationDiscoveryCleanupResult? discoveryCleanup = null;
        Task<IReadOnlyList<BaseStationDevice>>? scanTask = null;
        var combined = initialResults.ToArray();
        RecoveryState? state = null;
        state = new RecoveryState(
            candidates.Select(candidate => candidate.Station),
            initialResults.Count(result => result.Succeeded),
            result =>
            {
                var index = FindMatchingResultIndex(combined, result.Station);
                if (index >= 0)
                {
                    combined[index] = result;
                }
            },
            write: Write);
        var observer = new StreamingDiscoveryObserver(state);
        var earlyStopRequested = false;
        string? earlyStopReason = null;

        Write(
            "start",
            "started",
            station: firstResolutionFailure.Result.Station,
            firstFailingStationIndex: firstResolutionFailure.Index);
        Write(
            "triggered",
            "triggered",
            station: firstResolutionFailure.Result.Station,
            firstFailingStationIndex: firstResolutionFailure.Index);
        Write(
            "firstResolutionFailure",
            "triggered",
            station: firstResolutionFailure.Result.Station,
            firstFailingStationIndex: firstResolutionFailure.Index);

        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCancellation.CancelAfter(_timeout);
        var workerTask = state.RunWorkerAsync(
            streamingWakeAsync,
            workerCancellation.Token,
            onAllSucceeded: () =>
            {
                if (earlyStopRequested)
                {
                    return;
                }

                earlyStopRequested = true;
                earlyStopReason = "allCandidatesSucceeded";
                Write("allCandidatesSucceeded", "succeeded", earlyStopReason: earlyStopReason);
                scanCancellation.Cancel();
            });

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
                cleanup => discoveryCleanup = cleanup,
                observer);
            var discovered = await scanTask.WaitAsync(_timeout);
            foundDeviceCount = discovered.Count;
            terminalOutcome = "scanCompleted";
            Write("scanCompleted", "succeeded");
        }
        catch (TimeoutException ex)
        {
            scanCancellation.Cancel();
            terminalOutcome = "timedOut";
            Write("timedOut", "timeout", exception: ex);
        }
        catch (OperationCanceledException ex) when (earlyStopRequested)
        {
            terminalOutcome = "scanStoppedEarly";
            Write("scanStoppedEarly", "succeeded", exception: ex, earlyStopReason: earlyStopReason);
        }
        catch (OperationCanceledException ex)
        {
            var timedOut = !cancellationToken.IsCancellationRequested && scanCancellation.IsCancellationRequested;
            terminalOutcome = timedOut ? "timedOut" : "cancelled";
            Write(
                timedOut ? "timedOut" : "cancelled",
                timedOut ? "timeout" : "cancelled",
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
            state.CompleteQueue();
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
                    cleanupResult: cleanupResult,
                    earlyStopReason: earlyStopReason);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                workerCancellation.Cancel();
            }

            await ObserveWorkerCompletionAsync(workerTask, workerCancellation);
        }

        var remaining = state.RemainingUnresolvedStations();
        if (!cancellationToken.IsCancellationRequested && remaining.Count > 0)
        {
            Write("fallbackRetryStarted", "started", fallbackRetryCount: remaining.Count);
            try
            {
                var retryResults = await fallbackRetryAsync(remaining, cancellationToken);
                var retrySuccessCount = 0;
                foreach (var retryResult in retryResults)
                {
                    state.RecordFallbackResult(retryResult);
                    if (retryResult.Succeeded)
                    {
                        retrySuccessCount++;
                    }
                }

                terminalOutcome = state.AllCandidatesSucceeded
                    ? "recovered"
                    : "retryIncomplete";
                Write(
                    "fallbackRetryCompleted",
                    terminalOutcome,
                    retrySuccessCount: retrySuccessCount,
                    fallbackRetryCount: remaining.Count);
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
        else if (state.AllCandidatesSucceeded)
        {
            terminalOutcome = earlyStopRequested ? "streamingRecovered" : terminalOutcome;
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            terminalOutcome = "cancelled";
        }

        Write(
            "complete",
            terminalOutcome,
            cleanupResult: discoveryCleanup?.Result,
            terminal: true,
            earlyStopReason: earlyStopReason);
        return combined;

        void Write(
            string eventType,
            string outcome,
            string? cleanupResult = null,
            Exception? exception = null,
            bool? cancellationRequested = null,
            bool? terminal = null,
            BaseStationDevice? station = null,
            int? firstFailingStationIndex = null,
            int? fallbackRetryCount = null,
            int? retrySuccessCount = null,
            string? earlyStopReason = null,
            string? skipReason = null)
        {
            var snapshot = state?.TrySnapshot();
            _diagnostics.Write(new BaseStationDiagnosticEvent
            {
                OperationId = operationId,
                OperationName = OperationName,
                WakeSequenceId = wakeSequenceId,
                ScanSessionId = scanSessionId,
                Trigger = Trigger,
                TriggerFailureStage = BaseStationCommandFailureStage.DeviceResolution.ToString(),
                ConfiguredStationCount = initialResults.Count,
                UnresolvedStationCount = candidates.Length,
                CandidateStationCount = candidates.Length,
                AlreadySuccessfulStationCount = snapshot?.AlreadySuccessfulCount,
                ObservedConfiguredStationCount = snapshot?.ObservedConfiguredCount,
                DuplicateObservationCount = snapshot?.DuplicateObservationCount,
                QueueCount = snapshot?.QueueCount,
                StreamingWakeAttemptCount = snapshot?.StreamingWakeAttemptCount,
                StreamingWakeSuccessCount = snapshot?.StreamingWakeSuccessCount,
                StreamingWakeFailureCount = snapshot?.StreamingWakeFailureCount,
                RetryStationCount = fallbackRetryCount,
                RetrySuccessCount = retrySuccessCount,
                FallbackRetryCount = fallbackRetryCount,
                FinalSuccessCount = snapshot?.FinalSuccessCount,
                FirstFailureElapsedMilliseconds = firstResolutionFailure.Result.ElapsedMilliseconds,
                FirstFailingStationIndex = firstFailingStationIndex,
                StationIdentity = station is null ? null : BaseStationDiagnosticSink.StationIdentity(station),
                StationLabel = station is null ? null : BaseStationDiagnosticSink.StationLabel(station),
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
                EarlyStopReason = earlyStopReason,
                SkipReason = skipReason,
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

    private async Task ObserveWorkerCompletionAsync(Task workerTask, CancellationTokenSource workerCancellation)
    {
        try
        {
            await workerTask.WaitAsync(_cleanupWait);
        }
        catch
        {
            workerCancellation.Cancel();
            try
            {
                await workerTask.WaitAsync(_cleanupWait);
            }
            catch
            {
                // The queue worker is bound by cancellation and diagnostics are best-effort.
            }
        }
    }

    private static int FindMatchingResultIndex(
        IReadOnlyList<BaseStationWakeAttemptResult> results,
        BaseStationDevice station)
    {
        var stationKey = StationKey(station);
        for (var index = 0; index < results.Count; index++)
        {
            if (string.Equals(StationKey(results[index].Station), stationKey, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string StationKey(BaseStationDevice station)
        => BaseStationDiagnosticSink.StationIdentity(station);

    private sealed class StreamingDiscoveryObserver(RecoveryState state) : IBaseStationDiscoveryObserver
    {
        public void OnStationObserved(BaseStationDiscoveryObservation observation)
            => state.TryQueueObservation(observation);
    }

    private sealed class RecoveryState
    {
        // Synchronization strategy:
        // - the discovery observer takes only this lock, deduplicates by hashed station identity,
        //   enqueues at most one item per candidate, and returns without running wake I/O;
        // - the channel has one reader, so streaming wake attempts are serialized deterministically;
        // - candidate state transitions are lock-protected so success, fallback, duplicate
        //   advertisements, queue completion, and cancellation cannot race into duplicate attempts;
        // - only the outer coordinator writes the terminal diagnostic event.
        private readonly object _lock = new();
        private readonly Dictionary<string, CandidateState> _candidates;
        private readonly Channel<BaseStationDevice> _queue = Channel.CreateUnbounded<BaseStationDevice>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        private readonly int _alreadySuccessfulCount;
        private readonly Action<BaseStationWakeAttemptResult> _recordResult;
        private readonly DiagnosticWrite _write;
        private bool _queueCompleted;
        private int _duplicateObservationCount;
        private int _queueCount;
        private int _streamingWakeAttemptCount;
        private int _streamingWakeSuccessCount;
        private int _streamingWakeFailureCount;

        public RecoveryState(
            IEnumerable<BaseStationDevice> candidates,
            int alreadySuccessfulCount,
            Action<BaseStationWakeAttemptResult> recordResult,
            DiagnosticWrite write)
        {
            _candidates = candidates
                .GroupBy(StationKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => new CandidateState(group.First()),
                    StringComparer.OrdinalIgnoreCase);
            _alreadySuccessfulCount = alreadySuccessfulCount;
            _recordResult = recordResult;
            _write = write;
        }

        public bool AllCandidatesSucceeded
        {
            get
            {
                lock (_lock)
                {
                    return _candidates.Values.All(candidate => candidate.Succeeded);
                }
            }
        }

        public RecoverySnapshot TrySnapshot()
        {
            lock (_lock)
            {
                return new RecoverySnapshot(
                    _alreadySuccessfulCount,
                    _candidates.Values.Count(candidate => candidate.Observed),
                    _duplicateObservationCount,
                    _queueCount,
                    _streamingWakeAttemptCount,
                    _streamingWakeSuccessCount,
                    _streamingWakeFailureCount,
                    _alreadySuccessfulCount + _candidates.Values.Count(candidate => candidate.Succeeded));
            }
        }

        public void TryQueueObservation(BaseStationDiscoveryObservation observation)
        {
            CandidateState? candidate;
            var queued = false;
            var duplicate = false;
            var unconfigured = false;
            lock (_lock)
            {
                if (_queueCompleted)
                {
                    return;
                }

                if (!_candidates.TryGetValue(StationKey(observation.Station), out candidate))
                {
                    unconfigured = true;
                    candidate = null;
                }
                else if (candidate.Succeeded || candidate.Queued || candidate.Active)
                {
                    _duplicateObservationCount++;
                    duplicate = true;
                }
                else
                {
                    candidate.Observed = true;
                    candidate.Queued = true;
                    _queueCount++;
                    queued = true;
                }
            }

            if (unconfigured)
            {
                _write("stationObserved", "ignored", station: observation.Station, skipReason: "unconfigured");
                return;
            }

            if (duplicate)
            {
                _write("stationObserved", "duplicateIgnored", station: candidate!.Station);
                return;
            }

            _write("stationObserved", "configured", station: candidate!.Station);
            if (queued && _queue.Writer.TryWrite(candidate.Station))
            {
                _write("stationQueued", "queued", station: candidate.Station);
                return;
            }

            lock (_lock)
            {
                candidate.Queued = false;
            }
        }

        public async Task RunWorkerAsync(
            Func<BaseStationDevice, CancellationToken, Task<BaseStationWakeAttemptResult>> streamingWakeAsync,
            CancellationToken cancellationToken,
            Action onAllSucceeded)
        {
            try
            {
                await foreach (var station in _queue.Reader.ReadAllAsync(cancellationToken))
                {
                    var key = StationKey(station);
                    lock (_lock)
                    {
                        if (!_candidates.TryGetValue(key, out var candidate) || candidate.Succeeded || candidate.Active)
                        {
                            continue;
                        }

                        candidate.Queued = false;
                        candidate.Active = true;
                        candidate.StreamingAttempted = true;
                        _streamingWakeAttemptCount++;
                    }

                    _write("stationWakeStarted", "started", station: station);
                    BaseStationWakeAttemptResult result;
                    try
                    {
                        result = await streamingWakeAsync(station, cancellationToken);
                    }
                    catch (OperationCanceledException ex)
                    {
                        result = BaseStationWakeAttemptResult.Failure(
                            station,
                            BaseStationCommandFailureStage.Cancelled,
                            ex);
                    }
                    catch (Exception ex)
                    {
                        result = BaseStationWakeAttemptResult.Failure(
                            station,
                            BaseStationCommandFailureStage.Unknown,
                            ex);
                    }

                    var allSucceeded = RecordStreamingResult(result);
                    _write(
                        result.Succeeded ? "stationWakeSucceeded" : "stationWakeFailed",
                        result.Succeeded ? "succeeded" : "failed",
                        station: result.Station);
                    if (allSucceeded)
                    {
                        onAllSucceeded();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Caller cancellation controls shutdown; completed station results are preserved.
            }
        }

        public void CompleteQueue()
        {
            lock (_lock)
            {
                if (_queueCompleted)
                {
                    return;
                }

                _queueCompleted = true;
            }

            _queue.Writer.TryComplete();
        }

        public IReadOnlyList<BaseStationDevice> RemainingUnresolvedStations()
        {
            lock (_lock)
            {
                return _candidates.Values
                    .Where(candidate => !candidate.Succeeded)
                    .Select(candidate => candidate.Station)
                    .ToArray();
            }
        }

        public void RecordFallbackResult(BaseStationWakeAttemptResult result)
        {
            lock (_lock)
            {
                if (!_candidates.TryGetValue(StationKey(result.Station), out var candidate) || candidate.Succeeded)
                {
                    return;
                }

                candidate.LastResult = result;
                candidate.Succeeded = result.Succeeded;
            }

            _recordResult(result);
        }

        private bool RecordStreamingResult(BaseStationWakeAttemptResult result)
        {
            lock (_lock)
            {
                if (!_candidates.TryGetValue(StationKey(result.Station), out var candidate))
                {
                    return false;
                }

                candidate.Active = false;
                candidate.LastResult = result;
                if (result.Succeeded)
                {
                    candidate.Succeeded = true;
                    _streamingWakeSuccessCount++;
                    _recordResult(result);
                }
                else
                {
                    _streamingWakeFailureCount++;
                }

                return _candidates.Values.All(item => item.Succeeded);
            }
        }

        private sealed class CandidateState(BaseStationDevice station)
        {
            public BaseStationDevice Station { get; } = station;
            public bool Observed { get; set; }
            public bool Queued { get; set; }
            public bool Active { get; set; }
            public bool StreamingAttempted { get; set; }
            public bool Succeeded { get; set; }
            public BaseStationWakeAttemptResult? LastResult { get; set; }
        }
    }

    private sealed record RecoverySnapshot(
        int AlreadySuccessfulCount,
        int ObservedConfiguredCount,
        int DuplicateObservationCount,
        int QueueCount,
        int StreamingWakeAttemptCount,
        int StreamingWakeSuccessCount,
        int StreamingWakeFailureCount,
        int FinalSuccessCount);

    private delegate void DiagnosticWrite(
        string eventType,
        string outcome,
        string? cleanupResult = null,
        Exception? exception = null,
        bool? cancellationRequested = null,
        bool? terminal = null,
        BaseStationDevice? station = null,
        int? firstFailingStationIndex = null,
        int? fallbackRetryCount = null,
        int? retrySuccessCount = null,
        string? earlyStopReason = null,
        string? skipReason = null);
}
