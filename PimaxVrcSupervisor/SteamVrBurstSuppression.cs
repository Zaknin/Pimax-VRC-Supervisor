using System.Diagnostics;

namespace PimaxVrcSupervisor.BaseStations;

internal enum SteamVrBurstSuppressionOutcome
{
    AllConfirmed,
    PartialConfirmed,
    Unavailable,
    TimedOut,
    Errored,
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
        BaseStationDevice[] eligibleStations)
    {
        if (result.SuppressBurst)
        {
            return new("skipped", []);
        }

        if (result.Outcome == SteamVrBurstSuppressionOutcome.PartialConfirmed)
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

internal sealed class SteamVrBurstSuppressionChecker
{
    internal const string OperationName = "steamVrBurstSuppression";

    private readonly BaseStationDiagnosticSink _diagnostics;
    private readonly TimeSpan _timeout;

    public SteamVrBurstSuppressionChecker(BaseStationDiagnosticSink diagnostics, TimeSpan timeout)
    {
        _diagnostics = diagnostics;
        _timeout = timeout;
    }

    public async Task<SteamVrBurstSuppressionResult> CheckAsync(
        BaseStationDevice[] enabledConfiguredStations,
        bool enabled,
        Func<(bool Available, string Reason)> getAvailability,
        Func<CancellationToken, Task<IReadOnlyList<SteamVrTrackingReference>>> readActiveTrackingReferencesAsync,
        string wakeSequenceId,
        int pass,
        int burstCycle,
        CancellationToken cancellationToken)
    {
        var operationId = BaseStationDiagnosticSink.CreateId("bs-burst-suppression");
        var startedAt = Stopwatch.GetTimestamp();
        Write(
            "steamVrBurstSuppressionCheckStarted",
            "started",
            configuredStationCount: enabledConfiguredStations.Length,
            operationId: operationId,
            wakeSequenceId: wakeSequenceId,
            pass: pass,
            burstCycle: burstCycle,
            steamVrAvailable: null,
            confirmationTimedOut: false,
            reason: enabled ? "checking exact identities" : "disabled");

        SteamVrBurstSuppressionResult? result = null;
        if (!enabled || enabledConfiguredStations.Length == 0)
        {
            result = CreateResult(
                SteamVrBurstSuppressionOutcome.Disabled,
                enabledConfiguredStations,
                [],
                steamVrAvailable: false,
                confirmationTimedOut: false,
                reason: enabled ? "no enabled configured stations" : "suppression check disabled");
        }
        else
        {
            (bool Available, string Reason) availability;
            try
            {
                availability = getAvailability();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                result = CreateResult(
                    SteamVrBurstSuppressionOutcome.Errored,
                    enabledConfiguredStations,
                    [],
                    steamVrAvailable: false,
                    confirmationTimedOut: false,
                    reason: ex.Message);
                availability = default;
            }

            if (result is null)
            {
                if (!availability.Available)
                {
                    result = CreateResult(
                        SteamVrBurstSuppressionOutcome.Unavailable,
                        enabledConfiguredStations,
                        [],
                        steamVrAvailable: false,
                        confirmationTimedOut: false,
                        reason: availability.Reason ?? "SteamVR tracking confirmation is unavailable");
                }
                else
                {
                    Task<IReadOnlyList<SteamVrTrackingReference>>? readTask = null;
                    try
                    {
                        readTask = readActiveTrackingReferencesAsync(cancellationToken);
                        var trackingReferences = await readTask.WaitAsync(_timeout, cancellationToken);
                        var match = SteamVrBaseStationMatcher.Match(enabledConfiguredStations, trackingReferences);
                        result = CreateResult(
                            match.AllMatchedExactly
                                ? SteamVrBurstSuppressionOutcome.AllConfirmed
                                : SteamVrBurstSuppressionOutcome.PartialConfirmed,
                            enabledConfiguredStations,
                            match.ExactMatchedBluetoothAddresses,
                            steamVrAvailable: true,
                            confirmationTimedOut: false,
                            reason: match.AllMatchedExactly
                                ? "all enabled configured stations matched active SteamVR tracking references by exact identity"
                                : $"exact identity confirmation incomplete ({match.ExactMatchCount}/{enabledConfiguredStations.Length}); count fallback is not eligible");
                    }
                    catch (TimeoutException)
                    {
                        if (readTask is not null)
                        {
                            _ = readTask.ContinueWith(
                                completed => _ = completed.Exception,
                                CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                                TaskScheduler.Default);
                        }

                        result = CreateResult(
                            SteamVrBurstSuppressionOutcome.TimedOut,
                            enabledConfiguredStations,
                            [],
                            steamVrAvailable: true,
                            confirmationTimedOut: true,
                            reason: $"SteamVR exact-identity confirmation exceeded {_timeout.TotalMilliseconds:0} ms");
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        result = CreateResult(
                            SteamVrBurstSuppressionOutcome.Errored,
                            enabledConfiguredStations,
                            [],
                            steamVrAvailable: true,
                            confirmationTimedOut: false,
                            reason: ex.Message);
                    }
                }
            }
        }

        var completedResult = result ?? throw new InvalidOperationException("Burst suppression check did not produce a result.");
        Write(
            "steamVrBurstSuppressionCheckCompleted",
            OutcomeName(completedResult.Outcome),
            configuredStationCount: completedResult.ConfiguredStationCount,
            confirmedActiveStationCount: completedResult.ConfirmedActiveStationCount,
            missingStationCount: completedResult.MissingBluetoothAddresses.Length,
            operationId: operationId,
            wakeSequenceId: wakeSequenceId,
            pass: pass,
            burstCycle: burstCycle,
            steamVrAvailable: completedResult.SteamVrAvailable,
            confirmationTimedOut: completedResult.ConfirmationTimedOut,
            reason: completedResult.Reason,
            elapsedMilliseconds: Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        return completedResult;
    }

    public void WriteDisposition(
        string eventType,
        SteamVrBurstSuppressionResult result,
        string wakeSequenceId,
        int pass,
        int burstCycle,
        string disposition,
        int? retryStationCount = null,
        int? retrySuccessCount = null)
        => Write(
            eventType,
            OutcomeName(result.Outcome),
            configuredStationCount: result.ConfiguredStationCount,
            confirmedActiveStationCount: result.ConfirmedActiveStationCount,
            missingStationCount: result.MissingBluetoothAddresses.Length,
            wakeSequenceId: wakeSequenceId,
            pass: pass,
            burstCycle: burstCycle,
            steamVrAvailable: result.SteamVrAvailable,
            confirmationTimedOut: result.ConfirmationTimedOut,
            disposition: disposition,
            reason: result.Reason,
            retryStationCount: retryStationCount,
            retrySuccessCount: retrySuccessCount);

    private static SteamVrBurstSuppressionResult CreateResult(
        SteamVrBurstSuppressionOutcome outcome,
        BaseStationDevice[] configuredStations,
        IReadOnlyCollection<string> confirmedAddresses,
        bool steamVrAvailable,
        bool confirmationTimedOut,
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
            steamVrAvailable,
            confirmationTimedOut,
            reason);
    }

    private void Write(
        string eventType,
        string outcome,
        int configuredStationCount,
        string? operationId = null,
        string? wakeSequenceId = null,
        int? pass = null,
        int? burstCycle = null,
        int? confirmedActiveStationCount = null,
        int? missingStationCount = null,
        bool? steamVrAvailable = null,
        bool? confirmationTimedOut = null,
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
            CurrentStage = "unsupportedV2BurstSuppression",
            ConfiguredStationCount = configuredStationCount,
            ConfirmedActiveStationCount = confirmedActiveStationCount,
            MissingStationCount = missingStationCount,
            SteamVrAvailable = steamVrAvailable,
            ConfirmationTimedOut = confirmationTimedOut,
            BurstDisposition = disposition,
            Reason = BaseStationDiagnosticSink.SanitizeMessage(reason),
            BurstNumber = pass,
            RetryNumber = burstCycle,
            RetryStationCount = retryStationCount,
            RetrySuccessCount = retrySuccessCount,
            TotalAttemptDurationMilliseconds = elapsedMilliseconds,
            Outcome = outcome
        });

    internal static string OutcomeName(SteamVrBurstSuppressionOutcome outcome)
        => outcome switch
        {
            SteamVrBurstSuppressionOutcome.AllConfirmed => "allConfirmed",
            SteamVrBurstSuppressionOutcome.PartialConfirmed => "partialConfirmed",
            SteamVrBurstSuppressionOutcome.Unavailable => "unavailable",
            SteamVrBurstSuppressionOutcome.TimedOut => "timedOut",
            SteamVrBurstSuppressionOutcome.Errored => "errored",
            SteamVrBurstSuppressionOutcome.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
}
