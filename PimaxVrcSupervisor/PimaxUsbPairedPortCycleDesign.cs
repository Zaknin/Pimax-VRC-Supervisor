using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class PimaxUsbPairedPortCycleDesignSchema
{
    public const string Version = "pimax-usb-paired-port-cycle-design-v1";
    public const string ProposedRequestVersion = "pimax-usb-paired-port-cycle-request-v1";
    public const string ProposedResultVersion = "pimax-usb-paired-port-cycle-result-v1";
}

internal static class PimaxUsbPairedPortCycleDesignJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal enum PimaxUsbPairedSide { SuperSpeed, Usb2 }
internal enum PimaxUsbSingleCallSemantics { SynchronousThroughRecovery, SynchronousThroughInitiation, AsynchronousAcceptance, TimingEvidenceInsufficient }

internal sealed record PimaxUsbPairedTarget(string HubVidPid, int ConnectionIndex, string LogicalSide, string ConnectorIdentity, bool IsRootHub = false, bool IsController = false);
internal sealed record PimaxUsbTimingEvidence(DateTimeOffset CallEntry, DateTimeOffset CallReturn, DateTimeOffset? FirstTransition, DateTimeOffset? Usb2Disappearance, DateTimeOffset? Usb2Return, int ObserverResolutionMilliseconds, bool SameClockDomain);
internal sealed record PimaxUsbTimingAnalysis(string Classification, double CallDurationMilliseconds, double? ReturnToFirstTransitionMilliseconds, double? EntryToFirstTransitionMilliseconds, double? EntryToDisappearanceMilliseconds, double? EntryToReturnMilliseconds, double? ReturnRelativeToUsb2ReturnMilliseconds, string Confidence, string[] Limitations);
internal sealed record PimaxUsbStrategyAssessment(string Candidate, string ExactTargetCertainty, string ExpectedOverlap, string PhysicalResemblance, string RequestLimit, string NoRetry, string Prevalidation, string PartialFailureContainment, string TimingAudit, string CrashBehavior, string UacCancellation, string ViveIsolation, string UnrelatedPortIsolation, string DeterministicTesting, string Complexity, string Recoverability, string PartialStateRisk, string Verdict);
internal sealed record PimaxUsbPartialFailureOutcome(string Scenario, int MaximumRequests, bool Retry, bool Fallback, bool ManualRestorationRequired, string Outcome);
internal sealed record PimaxUsbPairedSimulationSummary(string AggregationStatus, int TotalRequestCount, int SuperSpeedRequestCount, int Usb2RequestCount, double SubmissionSkewMilliseconds, bool BothReadyBeforeRelease, bool HardwareHandlesOpened, string[] Warnings, string[] Errors);
internal sealed record PimaxUsbPairedDesignResult(string Schema, string SelectedStrategy, string ReadinessVerdict, PimaxUsbTimingAnalysis TimingAnalysis, PimaxUsbPairedTarget[] ExactTargets, PimaxUsbStrategyAssessment[] DecisionMatrix, int MaximumRequestCount, int MaximumPerSideRequestCount, string SubmissionSkewPolicy, string[] RequiredMarkerSequence, string ConfirmationModel, string UacModel, PimaxUsbPartialFailureOutcome[] PartialFailureMatrix, PimaxUsbPairedSimulationSummary TestOnlySimulation, string HardwareExecutionGuard, string[] RefusalReasons, string ProposedRequestSchema, string ProposedResultSchema);

internal static class PimaxUsbPairedTimingAnalyzer
{
    public static PimaxUsbTimingAnalysis Analyze(PimaxUsbTimingEvidence e)
    {
        var duration = (e.CallReturn - e.CallEntry).TotalMilliseconds;
        double? first = e.FirstTransition is null ? null : (e.FirstTransition.Value - e.CallEntry).TotalMilliseconds;
        var limitations = new List<string> { "Helper and observer timestamps are wall-clock readings from separate processes, not one monotonic clock." };
        if (!e.SameClockDomain) limitations.Add("Native and observation timestamps use different clock domains.");
        if (e.ObserverResolutionMilliseconds <= 0) limitations.Add("Observer resolution is unavailable.");
        string classification;
        string confidence;
        if (e.FirstTransition is null || !e.SameClockDomain || e.ObserverResolutionMilliseconds <= 0)
        {
            classification = nameof(PimaxUsbSingleCallSemantics.TimingEvidenceInsufficient);
            confidence = "low";
        }
        else if (e.CallReturn < e.FirstTransition.Value)
        {
            classification = nameof(PimaxUsbSingleCallSemantics.AsynchronousAcceptance);
            confidence = first >= e.ObserverResolutionMilliseconds ? "high" : "moderate";
        }
        else if (e.Usb2Return is not null && e.CallReturn >= e.Usb2Return.Value)
        {
            classification = nameof(PimaxUsbSingleCallSemantics.SynchronousThroughRecovery);
            confidence = "moderate";
        }
        else
        {
            classification = nameof(PimaxUsbSingleCallSemantics.SynchronousThroughInitiation);
            confidence = "moderate";
        }
        return new(classification, duration,
            e.FirstTransition is null ? null : (e.FirstTransition.Value - e.CallReturn).TotalMilliseconds,
            first,
            e.Usb2Disappearance is null ? null : (e.Usb2Disappearance.Value - e.CallEntry).TotalMilliseconds,
            e.Usb2Return is null ? null : (e.Usb2Return.Value - e.CallEntry).TotalMilliseconds,
            e.Usb2Return is null ? null : (e.CallReturn - e.Usb2Return.Value).TotalMilliseconds,
            confidence, limitations.ToArray());
    }
}

internal interface IPimaxUsbPairedSimulationAdapter
{
    ValueTask<IAsyncDisposable> OpenLogicalHandleAsync(PimaxUsbPairedTarget target, CancellationToken cancellationToken);
    ValueTask<PimaxUsbPairedNativeResult> SubmitOnceAsync(PimaxUsbPairedSide side, PimaxUsbPairedTarget target, CancellationToken cancellationToken);
}

internal interface IPimaxUsbMonotonicClock { long Timestamp { get; } double ElapsedMilliseconds(long start, long end); }
internal sealed class PimaxUsbStopwatchClock : IPimaxUsbMonotonicClock
{
    public long Timestamp => Stopwatch.GetTimestamp();
    public double ElapsedMilliseconds(long start, long end) => (end - start) * 1000d / Stopwatch.Frequency;
}
internal sealed class PimaxUsbScriptedClock : IPimaxUsbMonotonicClock
{
    private long _timestamp;
    public long Timestamp => Interlocked.Increment(ref _timestamp);
    public double ElapsedMilliseconds(long start, long end) => end - start;
}

internal sealed record PimaxUsbPairedNativeResult(bool Accepted, int Win32Error, uint NativeStatus);
internal sealed record PimaxUsbPairedWorkerResult(PimaxUsbPairedSide Side, long ReadyTimestamp, long? EntryTimestamp, long? ReturnTimestamp, int RequestCount, PimaxUsbPairedNativeResult? NativeResult, bool Incomplete, string? Error);
internal sealed record PimaxUsbPairedCoordinatorResult(long BarrierReleaseTimestamp, PimaxUsbPairedWorkerResult SuperSpeed, PimaxUsbPairedWorkerResult Usb2, int TotalRequestCount, double? SubmissionSkewMilliseconds, string AggregationStatus, bool ManualRestorationRequired);

internal sealed class PimaxUsbPairedSimulationCoordinator
{
    private readonly IPimaxUsbPairedSimulationAdapter _adapter;
    private readonly IPimaxUsbMonotonicClock _clock;
    public PimaxUsbPairedSimulationCoordinator(IPimaxUsbPairedSimulationAdapter adapter, IPimaxUsbMonotonicClock clock) { _adapter = adapter; _clock = clock; }

    public async Task<PimaxUsbPairedCoordinatorResult> RunAsync(PimaxUsbPairedTarget superSpeed, PimaxUsbPairedTarget usb2, bool validationPassed, CancellationToken cancellationToken)
    {
        ValidateTargets(superSpeed, usb2);
        if (!validationPassed || cancellationToken.IsCancellationRequested) return Empty("prevalidationFailed");
        await using var superSpeedHandle = await _adapter.OpenLogicalHandleAsync(superSpeed, cancellationToken);
        await using var usb2Handle = await _adapter.OpenLogicalHandleAsync(usb2, cancellationToken);
        if (cancellationToken.IsCancellationRequested) return Empty("cancelledBeforeRelease");

        var ready = new CountdownEvent(2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ss = Worker(PimaxUsbPairedSide.SuperSpeed, superSpeed, ready, release.Task, cancellationToken);
        var u2 = Worker(PimaxUsbPairedSide.Usb2, usb2, ready, release.Task, cancellationToken);
        ready.Wait(cancellationToken);
        var released = _clock.Timestamp;
        release.SetResult();
        var results = await Task.WhenAll(ss, u2);
        var left = results.Single(x => x.Side == PimaxUsbPairedSide.SuperSpeed);
        var right = results.Single(x => x.Side == PimaxUsbPairedSide.Usb2);
        var total = left.RequestCount + right.RequestCount;
        if (total > 2 || left.RequestCount > 1 || right.RequestCount > 1) throw new InvalidOperationException("Paired simulation request limit violated.");
        double? skew = left.EntryTimestamp is null || right.EntryTimestamp is null ? null : Math.Abs(_clock.ElapsedMilliseconds(left.EntryTimestamp.Value, right.EntryTimestamp.Value));
        var complete = left.NativeResult?.Accepted == true && right.NativeResult?.Accepted == true;
        return new(released, left, right, total, skew, complete ? "bothAccepted" : "partialPairFailure", !complete);
    }

    private async Task<PimaxUsbPairedWorkerResult> Worker(PimaxUsbPairedSide side, PimaxUsbPairedTarget target, CountdownEvent ready, Task release, CancellationToken cancellationToken)
    {
        var readyAt = _clock.Timestamp;
        ready.Signal();
        await release;
        long? entry = null;
        try
        {
            entry = _clock.Timestamp;
            var result = await _adapter.SubmitOnceAsync(side, target, cancellationToken);
            return new(side, readyAt, entry, _clock.Timestamp, 1, result, false, null);
        }
        catch (Exception ex)
        {
            return new(side, readyAt, entry, _clock.Timestamp, entry is null ? 0 : 1, null, entry is not null, ex.Message);
        }
    }

    internal static void ValidateTargets(PimaxUsbPairedTarget ss, PimaxUsbPairedTarget usb2)
    {
        if (ss != new PimaxUsbPairedTarget("05E3:0626", 4, "SuperSpeed", "sanitized-fixture:pimax-physical-connector") || usb2 != new PimaxUsbPairedTarget("05E3:0610", 4, "USB 2", "sanitized-fixture:pimax-physical-connector")) throw new InvalidOperationException("The exact reciprocal Pimax companion pair is required.");
        if (ss.IsRootHub || usb2.IsRootHub || ss.IsController || usb2.IsController) throw new InvalidOperationException("Root hubs and controllers are forbidden.");
    }

    private PimaxUsbPairedCoordinatorResult Empty(string status)
    {
        var emptySs = new PimaxUsbPairedWorkerResult(PimaxUsbPairedSide.SuperSpeed, 0, null, null, 0, null, false, null);
        var emptyU2 = new PimaxUsbPairedWorkerResult(PimaxUsbPairedSide.Usb2, 0, null, null, 0, null, false, null);
        return new(0, emptySs, emptyU2, 0, null, status, false);
    }
}

internal sealed class PimaxUsbDeterministicSimulationAdapter : IPimaxUsbPairedSimulationAdapter
{
    private sealed class LogicalHandle : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private readonly TaskCompletionSource _bothSubmitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _submissionCount;
    public ValueTask<IAsyncDisposable> OpenLogicalHandleAsync(PimaxUsbPairedTarget target, CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable>(new LogicalHandle());
    public async ValueTask<PimaxUsbPairedNativeResult> SubmitOnceAsync(PimaxUsbPairedSide side, PimaxUsbPairedTarget target, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _submissionCount) == 2) _bothSubmitted.SetResult();
        await _bothSubmitted.Task.WaitAsync(cancellationToken);
        return new(true, 0, 0);
    }
}

internal static class PimaxUsbPairedPortCycleDesignCommand
{
    public static async Task<PimaxUsbPairedDesignResult> RunAsync(CancellationToken cancellationToken)
    {
        var ss = new PimaxUsbPairedTarget("05E3:0626", 4, "SuperSpeed", "sanitized-fixture:pimax-physical-connector");
        var usb2 = new PimaxUsbPairedTarget("05E3:0610", 4, "USB 2", "sanitized-fixture:pimax-physical-connector");
        var timing = PimaxUsbPairedTimingAnalyzer.Analyze(new(
            DateTimeOffset.Parse("2026-06-18T18:45:43.6268923+00:00"), DateTimeOffset.Parse("2026-06-18T18:45:43.6273411+00:00"),
            DateTimeOffset.Parse("2026-06-18T18:45:44.0034902+00:00"), DateTimeOffset.Parse("2026-06-18T18:45:44.0034902+00:00"), DateTimeOffset.Parse("2026-06-18T18:45:47.1952115+00:00"), 250, true));
        var simulation = await new PimaxUsbPairedSimulationCoordinator(new PimaxUsbDeterministicSimulationAdapter(), new PimaxUsbScriptedClock()).RunAsync(ss, usb2, true, cancellationToken);
        var matrix = Matrix();
        var partial = new[]
        {
            new PimaxUsbPartialFailureOutcome("both accepted", 2, false, false, false, "Continue read-only observation."),
            new PimaxUsbPartialFailureOutcome("SuperSpeed accepted / USB 2 rejected", 2, false, false, true, "Record partial-pair failure; finish evidence; require physical restoration."),
            new PimaxUsbPartialFailureOutcome("USB 2 accepted / SuperSpeed rejected", 2, false, false, true, "Record partial-pair failure; finish evidence; require physical restoration."),
            new PimaxUsbPartialFailureOutcome("worker throws before either submission", 0, false, false, false, "Abort without mutation."),
            new PimaxUsbPartialFailureOutcome("worker throws after companion submission", 2, false, false, true, "Preserve side progress as incomplete/unknown; no compensation."),
            new PimaxUsbPartialFailureOutcome("helper crash after one request", 1, false, false, true, "Recover preallocated side progress; final aggregate may be absent."),
            new PimaxUsbPartialFailureOutcome("UAC cancelled or topology changes before release", 0, false, false, false, "Abort without mutation."),
            new PimaxUsbPartialFailureOutcome("topology changes after release", 2, false, false, true, "No retry or compensating mutation; observe and restore physically if required.")
        };
        return new(PimaxUsbPairedPortCycleDesignSchema.Version, "nearConcurrentPairedSubmission", "readyForFutureControlledHardwareExperiment",
            timing, [ss, usb2], matrix, 2, 1, "Both dedicated workers must be ready before one barrier release; record monotonic entry timestamps and measured skew; no atomicity is claimed.",
            ["observer-started", "pimax-info-opened", "pimax-crystal-model-selected", "connect-ready-before-action", "connect-action-completed"],
            "Bind the complete stable topology, observer identity, fresh Connect/scan marker, exact phrase, expiry, and one-time nonce.",
            "A future phase requires a new explicit UAC request after zero-call prevalidation; this design command never requests elevation.", partial,
            new(simulation.AggregationStatus, simulation.TotalRequestCount, simulation.SuperSpeed.RequestCount, simulation.Usb2.RequestCount, simulation.SubmissionSkewMilliseconds ?? 0, simulation.SuperSpeed.ReadyTimestamp <= simulation.BarrierReleaseTimestamp && simulation.Usb2.ReadyTimestamp <= simulation.BarrierReleaseTimestamp, false, [], []),
            "The command constructs only IPimaxUsbPairedSimulationAdapter; no paired request schema is accepted by the existing helper and no execute mode exists.", [],
            PimaxUsbPairedPortCycleDesignSchema.ProposedRequestVersion, PimaxUsbPairedPortCycleDesignSchema.ProposedResultVersion);
    }

    private static PimaxUsbStrategyAssessment[] Matrix() =>
    [
        Row("A: sequential SuperSpeed then USB 2", "conditional", "conditional", "partial", "acceptable", "acceptable", "acceptable", "conditional", "acceptable", "conditional", "acceptable", "acceptable", "acceptable", "acceptable", "low", "acceptable", "conditional", "Conditionally acceptable, but adds order dependence without a safety benefit."),
        Row("B: sequential USB 2 then SuperSpeed", "conditional", "conditional", "poor", "acceptable", "acceptable", "acceptable", "conditional", "acceptable", "conditional", "acceptable", "acceptable", "acceptable", "acceptable", "low", "acceptable", "unacceptable", "Rejected: the proven USB 2-only request left registration unavailable."),
        Row("C: near-concurrent paired submission", "acceptable", "acceptable", "best available", "acceptable", "acceptable", "acceptable", "conditional", "acceptable", "conditional", "acceptable", "acceptable", "acceptable", "acceptable", "moderate", "acceptable", "conditional", "Selected with explicit non-atomic partial-failure handling."),
        Row("D: no paired software experiment", "acceptable", "none", "physical only", "acceptable", "acceptable", "acceptable", "acceptable", "not applicable", "acceptable", "acceptable", "acceptable", "acceptable", "acceptable", "low", "acceptable", "acceptable", "Fallback product direction if future preconditions cannot be met.")
    ];

    private static PimaxUsbStrategyAssessment Row(string c, string exact, string overlap, string physical, string limit, string retry, string prevalidation, string partial, string timing, string crash, string uac, string vive, string unrelated, string testing, string complexity, string recovery, string partialRisk, string verdict)
        => new(c, exact, overlap, physical, limit, retry, prevalidation, partial, timing, crash, uac, vive, unrelated, testing, complexity, recovery, partialRisk, verdict);
}
