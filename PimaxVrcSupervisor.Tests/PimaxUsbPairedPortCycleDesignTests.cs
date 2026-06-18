using Xunit;

public sealed class PimaxUsbPairedPortCycleDesignTests
{
    private static readonly PimaxUsbPairedTarget SuperSpeed = new("05E3:0626", 4, "SuperSpeed", "sanitized-fixture:pimax-physical-connector");
    private static readonly PimaxUsbPairedTarget Usb2 = new("05E3:0610", 4, "USB 2", "sanitized-fixture:pimax-physical-connector");

    [Fact]
    public void EvidenceClassifiesAsAsynchronousAcceptance()
    {
        var entry = DateTimeOffset.Parse("2026-06-18T18:45:43.6268923Z");
        var result = PimaxUsbPairedTimingAnalyzer.Analyze(new(entry, entry.AddMilliseconds(.4488), entry.AddMilliseconds(376.5979), entry.AddMilliseconds(376.5979), entry.AddMilliseconds(3568.3192), 250, true));
        Assert.Equal(nameof(PimaxUsbSingleCallSemantics.AsynchronousAcceptance), result.Classification);
        Assert.InRange(result.CallDurationMilliseconds, .448, .450);
        Assert.Equal("high", result.Confidence);
    }

    [Fact]
    public void ClockDomainMismatchIsInsufficient()
    {
        var now = DateTimeOffset.UtcNow;
        var result = PimaxUsbPairedTimingAnalyzer.Analyze(new(now, now.AddMilliseconds(1), now.AddMilliseconds(400), null, null, 250, false));
        Assert.Equal(nameof(PimaxUsbSingleCallSemantics.TimingEvidenceInsufficient), result.Classification);
    }

    [Fact]
    public void MissingResolutionIsInsufficient()
    {
        var now = DateTimeOffset.UtcNow;
        var result = PimaxUsbPairedTimingAnalyzer.Analyze(new(now, now.AddMilliseconds(1), now.AddMilliseconds(400), null, null, 0, true));
        Assert.Equal(nameof(PimaxUsbSingleCallSemantics.TimingEvidenceInsufficient), result.Classification);
    }

    [Theory]
    [InlineData("05E3:0626", 3, "05E3:0610", 4)]
    [InlineData("05E3:0626", 4, "05E3:0610", 3)]
    [InlineData("05E3:9999", 4, "05E3:0610", 4)]
    public async Task WrongPairIsRejected(string ssVidPid, int ssIndex, string usbVidPid, int usbIndex)
    {
        var adapter = new FakeAdapter();
        var coordinator = new PimaxUsbPairedSimulationCoordinator(adapter, new FakeClock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(new(ssVidPid, ssIndex, "SuperSpeed", SuperSpeed.ConnectorIdentity), new(usbVidPid, usbIndex, "USB 2", Usb2.ConnectorIdentity), true, default));
        Assert.Equal(0, adapter.Submissions);
    }

    [Fact]
    public async Task MismatchedConnectorIsRejected()
    {
        var coordinator = new PimaxUsbPairedSimulationCoordinator(new FakeAdapter(), new FakeClock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(SuperSpeed with { ConnectorIdentity = "connector:vive" }, Usb2, true, default));
    }

    [Fact]
    public async Task ValidationFailureProducesZeroCallsAndOpensNoHandles()
    {
        var adapter = new FakeAdapter();
        var result = await new PimaxUsbPairedSimulationCoordinator(adapter, new FakeClock()).RunAsync(SuperSpeed, Usb2, false, default);
        Assert.Equal(0, result.TotalRequestCount);
        Assert.Equal(0, adapter.Opened);
    }

    [Fact]
    public async Task CancellationBeforeReleaseProducesZeroCalls()
    {
        var adapter = new FakeAdapter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new PimaxUsbPairedSimulationCoordinator(adapter, new FakeClock()).RunAsync(SuperSpeed, Usb2, true, cancellation.Token);
        Assert.Equal(0, result.TotalRequestCount);
    }

    [Fact]
    public async Task BothHandlesOpenAndWorkersReadyBeforeRelease()
    {
        var adapter = new FakeAdapter();
        var result = await new PimaxUsbPairedSimulationCoordinator(adapter, new FakeClock()).RunAsync(SuperSpeed, Usb2, true, default);
        Assert.Equal(2, adapter.Opened);
        Assert.Equal(2, adapter.Disposed);
        Assert.Equal(2, result.TotalRequestCount);
        Assert.True(result.SuperSpeed.ReadyTimestamp <= result.BarrierReleaseTimestamp);
        Assert.True(result.Usb2.ReadyTimestamp <= result.BarrierReleaseTimestamp);
        Assert.NotNull(result.SubmissionSkewMilliseconds);
    }

    [Fact]
    public async Task EachSideSubmitsAtMostOnce()
    {
        var adapter = new FakeAdapter();
        var result = await new PimaxUsbPairedSimulationCoordinator(adapter, new FakeClock()).RunAsync(SuperSpeed, Usb2, true, default);
        Assert.Equal(1, result.SuperSpeed.RequestCount);
        Assert.Equal(1, result.Usb2.RequestCount);
        Assert.Equal(1, adapter.SideCounts[PimaxUsbPairedSide.SuperSpeed]);
        Assert.Equal(1, adapter.SideCounts[PimaxUsbPairedSide.Usb2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task OneSideRejectionIsPartialAndNeverRetried(int rejectedValue)
    {
        var rejected = (PimaxUsbPairedSide)rejectedValue;
        var adapter = new FakeAdapter { RejectedSide = rejected };
        var result = await new PimaxUsbPairedSimulationCoordinator(adapter, new FakeClock()).RunAsync(SuperSpeed, Usb2, true, default);
        Assert.Equal("partialPairFailure", result.AggregationStatus);
        Assert.True(result.ManualRestorationRequired);
        Assert.Equal(2, adapter.Submissions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task OneSideExceptionIsIncompleteAndNeverRetried(int failedValue)
    {
        var failed = (PimaxUsbPairedSide)failedValue;
        var adapter = new FakeAdapter { ThrowingSide = failed };
        var result = await new PimaxUsbPairedSimulationCoordinator(adapter, new FakeClock()).RunAsync(SuperSpeed, Usb2, true, default);
        Assert.Equal("partialPairFailure", result.AggregationStatus);
        Assert.Equal(2, result.TotalRequestCount);
        Assert.True((failed == PimaxUsbPairedSide.SuperSpeed ? result.SuperSpeed : result.Usb2).Incomplete);
        Assert.Equal(2, adapter.Submissions);
    }

    [Fact]
    public async Task DesignCommandIsSimulationOnly()
    {
        var result = await PimaxUsbPairedPortCycleDesignCommand.RunAsync(default);
        Assert.Equal(PimaxUsbPairedPortCycleDesignSchema.Version, result.Schema);
        Assert.Equal("nearConcurrentPairedSubmission", result.SelectedStrategy);
        Assert.False(result.TestOnlySimulation.HardwareHandlesOpened);
        Assert.Equal(2, result.MaximumRequestCount);
        Assert.Contains("no execute mode exists", result.HardwareExecutionGuard, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeClock : IPimaxUsbMonotonicClock
    {
        private long _value;
        public long Timestamp => Interlocked.Increment(ref _value);
        public double ElapsedMilliseconds(long start, long end) => end - start;
    }

    private sealed class FakeAdapter : IPimaxUsbPairedSimulationAdapter
    {
        private sealed class Handle(FakeAdapter owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { Interlocked.Increment(ref owner.Disposed); return ValueTask.CompletedTask; }
        }
        public int Opened;
        public int Disposed;
        public int Submissions;
        public PimaxUsbPairedSide? RejectedSide;
        public PimaxUsbPairedSide? ThrowingSide;
        public Dictionary<PimaxUsbPairedSide, int> SideCounts { get; } = new() { [PimaxUsbPairedSide.SuperSpeed] = 0, [PimaxUsbPairedSide.Usb2] = 0 };

        public ValueTask<IAsyncDisposable> OpenLogicalHandleAsync(PimaxUsbPairedTarget target, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Opened);
            return ValueTask.FromResult<IAsyncDisposable>(new Handle(this));
        }

        public ValueTask<PimaxUsbPairedNativeResult> SubmitOnceAsync(PimaxUsbPairedSide side, PimaxUsbPairedTarget target, CancellationToken cancellationToken)
        {
            lock (SideCounts) SideCounts[side]++;
            Interlocked.Increment(ref Submissions);
            if (ThrowingSide == side) throw new InvalidOperationException("synthetic failure after entry");
            return ValueTask.FromResult(new PimaxUsbPairedNativeResult(RejectedSide != side, RejectedSide == side ? 1 : 0, 0));
        }
    }
}
