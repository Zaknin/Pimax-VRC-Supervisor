using Xunit;

public sealed class BaseStationStartupSchedulerTests
{
    [Fact]
    public void KnownStartTimeYoungerThanMinimumIsDueAtStartPlusMinimum()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var now = start.AddSeconds(1);
        var epoch = new SteamVrBaseStationEpoch(10, "vrserver", start, start);

        var due = BaseStationStartupScheduler.CalculateDueAt(epoch, now);

        Assert.Equal(start.AddSeconds(2), due);
    }

    [Fact]
    public void KnownStartTimeAlreadyOldEnoughIsDueImmediately()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var now = start.AddSeconds(10);
        var epoch = new SteamVrBaseStationEpoch(10, "vrserver", start, start);

        Assert.Equal(now, BaseStationStartupScheduler.CalculateDueAt(epoch, now));
    }

    [Fact]
    public void UnknownStartTimeUsesFirstDetectedFallback()
    {
        var detected = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var now = detected.AddSeconds(1);
        var epoch = new SteamVrBaseStationEpoch(10, "vrserver", null, detected);

        Assert.Equal(detected.AddSeconds(5), BaseStationStartupScheduler.CalculateDueAt(epoch, now));
    }

    [Fact]
    public void LatestEpochPrefersNewerReadableStartTime()
    {
        var older = new SteamVrBaseStationEpoch(10, "vrserver", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var newer = new SteamVrBaseStationEpoch(11, "vrserver", DateTimeOffset.Parse("2026-01-01T00:01:00Z"), DateTimeOffset.Parse("2026-01-01T00:01:00Z"));

        Assert.Same(newer, BaseStationStartupScheduler.SelectLatest(older, newer));
    }

    [Fact]
    public void LatestEpochFallsBackToFirstDetectedWhenStartTimesAreUnreadable()
    {
        var older = new SteamVrBaseStationEpoch(10, "vrserver", null, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var newer = new SteamVrBaseStationEpoch(11, "vrserver", null, DateTimeOffset.Parse("2026-01-01T00:01:00Z"));

        Assert.Same(newer, BaseStationStartupScheduler.SelectLatest(older, newer));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void InitialPassDuplicateGuardMirrorsCurrentScheduler(bool initialPassOnly, bool sent, bool expected)
    {
        Assert.Equal(expected, BaseStationStartupScheduler.ShouldSkipInitialPass(initialPassOnly, sent));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public void PendingStateIdentifiesCancellableBeforeExecutionStates(int stateValue, bool expected)
    {
        var state = (BaseStationStartupSchedulerState)stateValue;
        Assert.Equal(expected, BaseStationStartupScheduler.IsPendingState(state));
    }

    [Theory]
    [InlineData(true, 0, 5)]
    [InlineData(true, 1, 6)]
    [InlineData(false, 4, 1)]
    [InlineData(false, 5, 3)]
    [InlineData(false, 3, 0)]
    [InlineData(false, 2, 5)]
    [InlineData(false, 0, 3)]
    public void StateAfterExecutionMirrorsCurrentSchedulerResultMapping(
        bool complete,
        int resultValue,
        int expectedValue)
    {
        var result = (BaseStationWakeRoutineResult)resultValue;
        var expected = (BaseStationStartupSchedulerState)expectedValue;
        Assert.Equal(expected, BaseStationStartupScheduler.StateAfterExecution(complete, result));
    }
}
