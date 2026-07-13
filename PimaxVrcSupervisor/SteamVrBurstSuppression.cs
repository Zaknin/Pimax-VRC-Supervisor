using System.Diagnostics;

namespace PimaxVrcSupervisor.BaseStations;

internal enum SteamVrBurstSuppressionOutcome
{
    AllConfirmed,
    PartialConfirmed,
    DeadlineExpired,
    Unavailable,
    Errored,
    Cancelled,
    Disabled
}

internal sealed record SteamVrBurstSuppressionResult(
    SteamVrBurstSuppressionOutcome Outcome,
    int ConfiguredStationCount,
    int ConfirmedActiveStationCount,
    string[] ConfirmedBluetoothAddresses,
    string[] MissingBluetoothAddresses,
    bool SteamVrAvailable,
    bool ConfirmationTimedOut,
    int PollCount,
    double ElapsedMilliseconds,
    int HighestConfirmedCount,
    bool SteamVrEverReachable,
    string Reason)
{
    public bool SuppressBurst => Outcome == SteamVrBurstSuppressionOutcome.AllConfirmed;
}

internal sealed record SteamVrBurstRetryPlan(
    string Disposition,
    BaseStationDevice[] Stations);

internal static class SteamVrBurstRetryPlanner
{
    public static SteamVrBurstRetryPlan Plan(
        SteamVrBurstSuppressionResult result,
        BaseStationDevice[] eligibleStations,
        bool subsetTargetingSafe = true)
    {
        if (result.SuppressBurst)
        {
            return new("skipped", []);
        }

        if (subsetTargetingSafe && result.Outcome == SteamVrBurstSuppressionOutcome.PartialConfirmed)
        {
            var missingSet = new HashSet<string>(
                result.MissingBluetoothAddresses,
                StringComparer.OrdinalIgnoreCase);
            var missingEligibleStations = eligibleStations
                .Where(station => missingSet.Contains(station.BluetoothAddress))
                .ToArray();
            if (missingEligibleStations.Length > 0
                && missingEligibleStations.Length < eligibleStations.Length)
            {
                return new("subsetTargeted", missingEligibleStations);
            }
        }

        return new("fullBurst", eligibleStations);
    }
}

internal sealed record SteamVrLaterWakePassExecution(
    string Disposition,
    BaseStationDevice[] Stations,
    bool[] Results,
    bool EndSequence);

internal static class SteamVrLaterWakePassCoordinator
{
    public static async Task<SteamVrLaterWakePassExecution> ExecuteAsync(
        SteamVrBurstSuppressionResult confirmation,
        BaseStationDevice[] eligibleStations,
        bool subsetTargetingSafe,
        Func<BaseStationDevice[], Task<bool[]>> executePassAsync)
    {
        var plan = SteamVrBurstRetryPlanner.Plan(confirmation, eligibleStations, subsetTargetingSafe);
        if (plan.Disposition == "skipped")
        {
            return new(plan.Disposition, [], [], EndSequence: true);
        }

        var results = await executePassAsync(plan.Stations);
        return new(plan.Disposition, plan.Stations, results, EndSequence: false);
    }
}

internal sealed class SteamVrBurstSuppressionChecker
{
    internal const string OperationName = "steamVrBurstSuppression";
    internal const string InnerBurstDecisionPoint = "unsupportedV2BurstSuppression";
    internal const string LaterWakePassDecisionPoint = "laterWakePassSuppression";

    private readonly BaseStationDiagnosticSink _diagnostics;
    private readonly TimeSpan _maximumDuration;
    private readonly TimeSpan _pollingInterval;

    public SteamVrBurstSuppressionChecker(
        BaseStationDiagnosticSink diagnostics,
        TimeSpan maximumDuration,
        TimeSpan? pollingInterval = null)
    {
        if (maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }

        _diagnostics = diagnostics;
        _maximumDuration = maximumDuration;
        _pollingInterval = pollingInterval ?? BaseStationCommandTiming.SteamVrConfirmationPollingInterval;
        if (_pollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollingInterval));
        }
    }

    public async Task<SteamVrBurstSuppressionResult> CheckAsync(
        BaseStationDevice[] enabledConfiguredStations,
        bool enabled,
        Func<(bool Available, string Reason)> getAvailability,
        Func<CancellationToken, Task<IReadOnlyList<SteamVrTrackingReference>>> readActiveTrackingReferencesAsync,
        string wakeSequenceId,
        int pass,
        int burstCycle,
        CancellationToken cancellationToken,
        string decisionPoint = InnerBurstDecisionPoint)
    {
        var operationId = BaseStationDiagnosticSink.CreateId("bs-steamvr-confirmation");
        Write(
            "steamVrBurstSuppressionCheckStarted",
            "started",
            decisionPoint,
            configuredStationCount: enabledConfiguredStations.Length,
            operationId: operationId,
            wakeSequenceId: wakeSequenceId,
            pass: pass,
            burstCycle: burstCycle,
            steamVrAvailable: null,
            confirmationTimedOut: false,
            pollCount: 0,
            highestConfirmedCount: 0,
            steamVrEverReachable: false,
            reason: enabled ? "polling for exact identities" : "disabled");
        var startedAt = Stopwatch.GetTimestamp();

        SteamVrBurstSuppressionResult result;
        if (!enabled || enabledConfiguredStations.Length == 0)
        {
            result = CreateResult(
                SteamVrBurstSuppressionOutcome.Disabled,
                enabledConfiguredStations,
                [],
                steamVrEverReachable: false,
                confirmationTimedOut: false,
                pollCount: 0,
                elapsedMilliseconds: Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                highestConfirmedCount: 0,
                reason: enabled ? "no enabled configured stations" : "suppression check disabled");
        }
        else
        {
            result = await PollAsync(
                enabledConfiguredStations,
                getAvailability,
                readActiveTrackingReferencesAsync,
                operationId,
                wakeSequenceId,
                pass,
                burstCycle,
                decisionPoint,
                startedAt,
                cancellationToken);
        }

        Write(
            "steamVrBurstSuppressionCheckCompleted",
            OutcomeName(result.Outcome),
            decisionPoint,
            configuredStationCount: result.ConfiguredStationCount,
            confirmedActiveStationCount: result.ConfirmedActiveStationCount,
            missingStationCount: result.MissingBluetoothAddresses.Length,
            operationId: operationId,
            wakeSequenceId: wakeSequenceId,
            pass: pass,
            burstCycle: burstCycle,
            steamVrAvailable: result.SteamVrAvailable,
            confirmationTimedOut: result.ConfirmationTimedOut,
            pollCount: result.PollCount,
            highestConfirmedCount: result.HighestConfirmedCount,
            steamVrEverReachable: result.SteamVrEverReachable,
            reason: result.Reason,
            elapsedMilliseconds: result.ElapsedMilliseconds);
        return result;
    }

    public void WriteDisposition(
        string eventType,
        SteamVrBurstSuppressionResult result,
        string wakeSequenceId,
        int pass,
        int burstCycle,
        string disposition,
        int? retryStationCount = null,
        int? retrySuccessCount = null,
        int? maximumPassCount = null,
        string decisionPoint = InnerBurstDecisionPoint)
        => Write(
            eventType,
            OutcomeName(result.Outcome),
            decisionPoint,
            configuredStationCount: result.ConfiguredStationCount,
            confirmedActiveStationCount: result.ConfirmedActiveStationCount,
            missingStationCount: result.MissingBluetoothAddresses.Length,
            wakeSequenceId: wakeSequenceId,
            pass: pass,
            burstCycle: burstCycle,
            maximumPassCount: maximumPassCount,
            steamVrAvailable: result.SteamVrAvailable,
            confirmationTimedOut: result.ConfirmationTimedOut,
            pollCount: result.PollCount,
            highestConfirmedCount: result.HighestConfirmedCount,
            steamVrEverReachable: result.SteamVrEverReachable,
            disposition: disposition,
            reason: result.Reason,
            retryStationCount: retryStationCount,
            retrySuccessCount: retrySuccessCount,
            elapsedMilliseconds: result.ElapsedMilliseconds);

    private async Task<SteamVrBurstSuppressionResult> PollAsync(
        BaseStationDevice[] configuredStations,
        Func<(bool Available, string Reason)> getAvailability,
        Func<CancellationToken, Task<IReadOnlyList<SteamVrTrackingReference>>> readAsync,
        string operationId,
        string wakeSequenceId,
        int pass,
        int burstCycle,
        string decisionPoint,
        long startedAt,
        CancellationToken cancellationToken)
    {
        var pollCount = 0;
        var highestConfirmedCount = 0;
        var lastReportedCount = -1;
        var steamVrEverReachable = false;
        IReadOnlyCollection<string> bestConfirmedAddresses = [];

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var elapsed = Stopwatch.GetElapsedTime(startedAt);
                if (elapsed >= _maximumDuration)
                {
                    return DeadlineResult();
                }

                (bool Available, string Reason) availability;
                try
                {
                    availability = getAvailability();
                }
                catch (Exception ex)
                {
                    return CreateResult(
                        SteamVrBurstSuppressionOutcome.Errored,
                        configuredStations,
                        bestConfirmedAddresses,
                        steamVrEverReachable,
                        confirmationTimedOut: false,
                        pollCount,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                        highestConfirmedCount,
                        ex.Message);
                }

                if (!availability.Available)
                {
                    return CreateResult(
                        SteamVrBurstSuppressionOutcome.Unavailable,
                        configuredStations,
                        bestConfirmedAddresses,
                        steamVrEverReachable,
                        confirmationTimedOut: false,
                        pollCount,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                        highestConfirmedCount,
                        availability.Reason ?? "SteamVR tracking confirmation is unavailable");
                }

                pollCount++;
                Task<IReadOnlyList<SteamVrTrackingReference>>? readTask = null;
                try
                {
                    var remaining = _maximumDuration - Stopwatch.GetElapsedTime(startedAt);
                    if (remaining <= TimeSpan.Zero)
                    {
                        return DeadlineResult();
                    }

                    readTask = readAsync(cancellationToken);
                    var trackingReferences = await readTask.WaitAsync(remaining, cancellationToken);
                    steamVrEverReachable = true;
                    var match = SteamVrBaseStationMatcher.Match(configuredStations, trackingReferences);
                    if (match.ExactMatchCount > highestConfirmedCount)
                    {
                        highestConfirmedCount = match.ExactMatchCount;
                        bestConfirmedAddresses = match.ExactMatchedBluetoothAddresses;
                    }

                    if (match.ExactMatchCount != lastReportedCount)
                    {
                        lastReportedCount = match.ExactMatchCount;
                        Write(
                            "steamVrConfirmationPollProgress",
                            match.AllMatchedExactly ? "allConfirmed" : "incomplete",
                            decisionPoint,
                            configuredStationCount: configuredStations.Length,
                            confirmedActiveStationCount: match.ExactMatchCount,
                            missingStationCount: configuredStations.Length - match.ExactMatchCount,
                            operationId: operationId,
                            wakeSequenceId: wakeSequenceId,
                            pass: pass,
                            burstCycle: burstCycle,
                            steamVrAvailable: true,
                            confirmationTimedOut: false,
                            pollCount: pollCount,
                            highestConfirmedCount: highestConfirmedCount,
                            steamVrEverReachable: true,
                            elapsedMilliseconds: Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                            reason: match.AllMatchedExactly
                                ? "all configured identities confirmed in one SteamVR snapshot"
                                : "exact identity confirmation remains incomplete");
                    }

                    if (match.AllMatchedExactly)
                    {
                        return CreateResult(
                            SteamVrBurstSuppressionOutcome.AllConfirmed,
                            configuredStations,
                            match.ExactMatchedBluetoothAddresses,
                            steamVrEverReachable,
                            confirmationTimedOut: false,
                            pollCount,
                            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                            highestConfirmedCount,
                            "all enabled configured stations matched active SteamVR tracking references by exact identity");
                    }
                }
                catch (TimeoutException)
                {
                    ObserveLateFault(readTask);
                    return DeadlineResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return CreateResult(
                        SteamVrBurstSuppressionOutcome.Errored,
                        configuredStations,
                        bestConfirmedAddresses,
                        steamVrEverReachable,
                        confirmationTimedOut: false,
                        pollCount,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                        highestConfirmedCount,
                        ex.Message);
                }

                var delay = _maximumDuration - Stopwatch.GetElapsedTime(startedAt);
                if (delay <= TimeSpan.Zero)
                {
                    return DeadlineResult();
                }

                await Task.Delay(delay < _pollingInterval ? delay : _pollingInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(
                SteamVrBurstSuppressionOutcome.Cancelled,
                configuredStations,
                bestConfirmedAddresses,
                steamVrEverReachable,
                confirmationTimedOut: false,
                pollCount,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                highestConfirmedCount,
                "SteamVR exact-identity confirmation was cancelled");
        }

        SteamVrBurstSuppressionResult DeadlineResult()
        {
            var outcome = highestConfirmedCount > 0
                ? SteamVrBurstSuppressionOutcome.PartialConfirmed
                : SteamVrBurstSuppressionOutcome.DeadlineExpired;
            return CreateResult(
                outcome,
                configuredStations,
                bestConfirmedAddresses,
                steamVrEverReachable,
                confirmationTimedOut: true,
                pollCount,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                highestConfirmedCount,
                $"SteamVR exact-identity confirmation remained incomplete through the {_maximumDuration.TotalMilliseconds:0} ms deadline");
        }
    }

    private static void ObserveLateFault(Task? task)
    {
        if (task is null)
        {
            return;
        }

        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static SteamVrBurstSuppressionResult CreateResult(
        SteamVrBurstSuppressionOutcome outcome,
        BaseStationDevice[] configuredStations,
        IReadOnlyCollection<string> confirmedAddresses,
        bool steamVrEverReachable,
        bool confirmationTimedOut,
        int pollCount,
        double elapsedMilliseconds,
        int highestConfirmedCount,
        string reason)
    {
        var confirmed = confirmedAddresses
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var confirmedSet = new HashSet<string>(confirmed, StringComparer.OrdinalIgnoreCase);
        var missing = configuredStations
            .Where(station => !confirmedSet.Contains(station.BluetoothAddress))
            .Select(station => station.BluetoothAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(
            outcome,
            configuredStations.Length,
            confirmed.Length,
            confirmed,
            missing,
            steamVrEverReachable,
            confirmationTimedOut,
            pollCount,
            elapsedMilliseconds,
            highestConfirmedCount,
            steamVrEverReachable,
            reason);
    }

    private void Write(
        string eventType,
        string outcome,
        string decisionPoint,
        int configuredStationCount,
        string? operationId = null,
        string? wakeSequenceId = null,
        int? pass = null,
        int? burstCycle = null,
        int? maximumPassCount = null,
        int? confirmedActiveStationCount = null,
        int? missingStationCount = null,
        bool? steamVrAvailable = null,
        bool? confirmationTimedOut = null,
        int? pollCount = null,
        int? highestConfirmedCount = null,
        bool? steamVrEverReachable = null,
        string? disposition = null,
        string? reason = null,
        int? retryStationCount = null,
        int? retrySuccessCount = null,
        double? elapsedMilliseconds = null)
        => _diagnostics.Write(new BaseStationDiagnosticEvent
        {
            OperationId = operationId,
            OperationName = OperationName,
            WakeSequenceId = wakeSequenceId,
            Trigger = "SteamVR autostart",
            EventType = eventType,
            CurrentStage = decisionPoint,
            ConfiguredStationCount = configuredStationCount,
            ConfirmedActiveStationCount = confirmedActiveStationCount,
            MissingStationCount = missingStationCount,
            SteamVrAvailable = steamVrAvailable,
            ConfirmationTimedOut = confirmationTimedOut,
            PollCount = pollCount,
            HighestConfirmedCount = highestConfirmedCount,
            SteamVrEverReachable = steamVrEverReachable,
            ConfirmationMaximumDurationMilliseconds = _maximumDuration.TotalMilliseconds,
            ConfirmationPollingIntervalMilliseconds = _pollingInterval.TotalMilliseconds,
            BurstDisposition = disposition,
            ActualAction = disposition,
            Reason = BaseStationDiagnosticSink.SanitizeMessage(reason),
            BurstNumber = pass,
            RetryNumber = burstCycle,
            MaximumPassCount = maximumPassCount,
            RetryStationCount = retryStationCount,
            RetrySuccessCount = retrySuccessCount,
            TotalAttemptDurationMilliseconds = elapsedMilliseconds,
            CancellationRequested = outcome == "cancelled",
            Outcome = outcome,
            Terminal = eventType.EndsWith("Completed", StringComparison.Ordinal)
        });

    internal static string OutcomeName(SteamVrBurstSuppressionOutcome outcome)
        => outcome switch
        {
            SteamVrBurstSuppressionOutcome.AllConfirmed => "allConfirmed",
            SteamVrBurstSuppressionOutcome.PartialConfirmed => "partialConfirmed",
            SteamVrBurstSuppressionOutcome.DeadlineExpired => "deadlineExpired",
            SteamVrBurstSuppressionOutcome.Unavailable => "unavailable",
            SteamVrBurstSuppressionOutcome.Errored => "errored",
            SteamVrBurstSuppressionOutcome.Cancelled => "cancelled",
            SteamVrBurstSuppressionOutcome.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
}
