internal static class BaseStationStartupScheduler
{
    public static readonly TimeSpan MinimumProcessAge = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan FallbackStabilization = TimeSpan.FromSeconds(5);

    public static DateTimeOffset CalculateDueAt(SteamVrBaseStationEpoch epoch, DateTimeOffset now)
    {
        if (epoch.ProcessStartTime is { } startTime)
        {
            var dueAt = startTime.Add(MinimumProcessAge);
            return dueAt <= now ? now : dueAt;
        }

        var fallbackDueAt = epoch.FirstDetectedAt.Add(FallbackStabilization);
        return fallbackDueAt <= now ? now : fallbackDueAt;
    }

    public static TimeSpan GetEpochAge(SteamVrBaseStationEpoch epoch, DateTimeOffset now)
        => now - (epoch.ProcessStartTime ?? epoch.FirstDetectedAt);

    public static SteamVrBaseStationEpoch SelectLatest(
        SteamVrBaseStationEpoch? current,
        SteamVrBaseStationEpoch candidate)
    {
        if (current is null)
        {
            return candidate;
        }

        if (candidate.ProcessStartTime is { } candidateStart
            && current.ProcessStartTime is { } currentStart)
        {
            return candidateStart > currentStart ? candidate : current;
        }

        if (candidate.ProcessStartTime is not null && current.ProcessStartTime is null)
        {
            return candidate;
        }

        if (candidate.ProcessStartTime is null && current.ProcessStartTime is not null)
        {
            return current;
        }

        return candidate.FirstDetectedAt > current.FirstDetectedAt ? candidate : current;
    }

    public static bool ShouldSkipInitialPass(bool initialPassOnly, bool initialWakeSentForEpoch)
        => initialPassOnly && initialWakeSentForEpoch;

    public static bool IsPendingState(BaseStationStartupSchedulerState state)
        => state is BaseStationStartupSchedulerState.Stabilizing
            or BaseStationStartupSchedulerState.Scheduled;

    public static BaseStationStartupSchedulerState StateAfterExecution(
        bool powerOnComplete,
        BaseStationWakeRoutineResult result)
    {
        if (powerOnComplete)
        {
            return result == BaseStationWakeRoutineResult.RanExhausted
                ? BaseStationStartupSchedulerState.Exhausted
                : BaseStationStartupSchedulerState.Completed;
        }

        return result switch
        {
            BaseStationWakeRoutineResult.NoopSteamVrNotRunning => BaseStationStartupSchedulerState.WaitingForSteamVr,
            BaseStationWakeRoutineResult.NoopWaitingForRetry => BaseStationStartupSchedulerState.Scheduled,
            BaseStationWakeRoutineResult.NoopNoStations => BaseStationStartupSchedulerState.Disabled,
            BaseStationWakeRoutineResult.NoopAlreadyComplete => BaseStationStartupSchedulerState.Completed,
            _ => BaseStationStartupSchedulerState.Scheduled
        };
    }
}
