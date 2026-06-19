using System.Text.Json;
using Xunit;

public sealed class PimaxUsbPairedPortCycleExperimentTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixtureNow = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExactPairProducesTwoCallPlan()
    {
        var (signature, state) = PimaxUsbPortCycleExperimentTests.Fixture();
        var result = PimaxUsbPairedValidator.Validate(signature, state, new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero));
        Assert.True(result.Safety.Permitted);
        Assert.Equal(2, result.Plan!.ExactRequestCount);
        Assert.Equal(PimaxUsbPairedExperimentSchema.Operation, result.Plan.ExperimentKind);
    }

    [Theory]
    [InlineData(true, false, 4, 4)]
    [InlineData(false, true, 4, 4)]
    [InlineData(false, false, 3, 4)]
    [InlineData(false, false, 4, 3)]
    public void WrongHubOrIndexIsRejected(bool wrongUsb, bool wrongSs, int usbIndex, int ssIndex)
    {
        var (signature, state) = PimaxUsbPortCycleExperimentTests.Fixture();
        signature = signature with
        {
            Usb2Hub = wrongUsb ? signature.Usb2Hub with { Pid = "9999" } : signature.Usb2Hub,
            SuperSpeedHub = wrongSs ? signature.SuperSpeedHub with { Pid = "9999" } : signature.SuperSpeedHub,
            PimaxUsb2Port = signature.PimaxUsb2Port with { ConnectionIndex = usbIndex },
            PimaxSuperSpeedPort = signature.PimaxSuperSpeedPort with { ConnectionIndex = ssIndex }
        };
        Assert.False(PimaxUsbPairedValidator.Validate(signature, state, new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero)).Safety.Permitted);
    }

    [Fact]
    public void ConnectorMismatchAndViveOverlapAreRejected()
    {
        var (signature, state) = PimaxUsbPortCycleExperimentTests.Fixture();
        signature = signature with { PimaxSuperSpeedPort = signature.PimaxSuperSpeedPort with { ConnectorGroupId = "other" } };
        Assert.False(PimaxUsbPairedValidator.Validate(signature, state, new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero)).Safety.Permitted);
        signature = signature with { PimaxSuperSpeedPort = signature.PimaxSuperSpeedPort with { ConnectorGroupId = signature.PimaxUsb2Port.ConnectorGroupId }, ViveUsb2Port = signature.ViveUsb2Port with { ConnectorGroupId = signature.PimaxUsb2Port.ConnectorGroupId } };
        Assert.False(PimaxUsbPairedValidator.Validate(signature, state, new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero)).Safety.Permitted);
    }

    [Fact]
    public void EnvelopeRejectsWrongSchemaHashLimitsPoliciesAndExpiry()
    {
        var envelope = Envelope();
        PimaxUsbPairedValidator.ValidateEnvelope(envelope, envelope.RequestSha256, Now);
        Assert.Throws<InvalidDataException>(() => PimaxUsbPairedValidator.ValidateEnvelope(envelope with { RequestSha256 = "wrong" }, "wrong", Now));
        Assert.Throws<InvalidDataException>(() => PimaxUsbPairedValidator.ValidateEnvelope(Rehash(envelope, envelope.Payload with { Schema = "single-side" }), Rehash(envelope, envelope.Payload with { Schema = "single-side" }).RequestSha256, Now));
        var limits = Rehash(envelope, envelope.Payload with { MaximumTotalRequests = 3 });
        Assert.Throws<InvalidDataException>(() => PimaxUsbPairedValidator.ValidateEnvelope(limits, limits.RequestSha256, Now));
        var retry = Rehash(envelope, envelope.Payload with { RetryPolicy = "once" });
        Assert.Throws<InvalidDataException>(() => PimaxUsbPairedValidator.ValidateEnvelope(retry, retry.RequestSha256, Now));
        var confirmation = Rehash(envelope, envelope.Payload with { ExactConfirmationBinding = "wrong" });
        Assert.Throws<InvalidDataException>(() => PimaxUsbPairedValidator.ValidateEnvelope(confirmation, confirmation.RequestSha256, Now));
        var duplicateProgress = Rehash(envelope, envelope.Payload with { Usb2ProgressPath = envelope.Payload.SuperSpeedProgressPath });
        Assert.Throws<InvalidDataException>(() => PimaxUsbPairedValidator.ValidateEnvelope(duplicateProgress, duplicateProgress.RequestSha256, Now));
        Assert.Throws<InvalidDataException>(() => PimaxUsbPairedValidator.ValidateEnvelope(envelope, envelope.RequestSha256, Now.AddMinutes(2)));
    }

    [Fact]
    public async Task BothHandlesOpenBeforeOneBarrierAndTwoCallsMaximum()
    {
        var adapter = new FakeAdapter(); var progress = new MemoryProgress(); var payload = Envelope().Payload;
        var result = await new PimaxUsbPairedNativeCoordinator(adapter, progress).RunAsync(payload, _ => Task.FromResult(true), true, "hash", default);
        Assert.True(result.BothHandlesOpened);
        Assert.True(result.BothWorkersReady);
        Assert.Equal(1, result.BarrierReleaseCount);
        Assert.Equal(1, result.SuperSpeed.RequestCount);
        Assert.Equal(1, result.Usb2.RequestCount);
        Assert.Equal(2, result.TotalRequestCount);
        Assert.Equal(2, adapter.OpenCount);
        Assert.Equal(2, adapter.DisposeCount);
        Assert.Contains(progress.Items, x => x.Stage == "barrier-released");
        Assert.Equal(2, progress.Items.Count(x => x.Stage == "native-call-entry"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HandleFailureProducesZeroCallsAndDisposesOpenedHandle(int failOpen)
    {
        var adapter = new FakeAdapter { FailOpenNumber = failOpen };
        var result = await new PimaxUsbPairedNativeCoordinator(adapter, new MemoryProgress()).RunAsync(Envelope().Payload, _ => Task.FromResult(true), true, "hash", default);
        Assert.Equal(0, result.TotalRequestCount);
        Assert.Equal(failOpen - 1, adapter.DisposeCount);
    }

    [Fact]
    public async Task FinalValidationFailureAfterBothHandlesProducesZeroCalls()
    {
        var adapter = new FakeAdapter();
        var result = await new PimaxUsbPairedNativeCoordinator(adapter, new MemoryProgress()).RunAsync(Envelope().Payload, _ => Task.FromResult(false), true, "hash", default);
        Assert.Equal(0, result.TotalRequestCount);
        Assert.Equal(2, adapter.DisposeCount);
        Assert.Equal(0, result.BarrierReleaseCount);
        Assert.True(result.RequestValid);
        Assert.False(result.PrevalidationPassed);
    }

    [Fact]
    public async Task DurableCheckpointFailureKeepsBarrierClosedAndProducesZeroCalls()
    {
        var adapter = new FakeAdapter();
        var result = await new PimaxUsbPairedNativeCoordinator(adapter, new FailingProgress("pre-release-checkpoint"))
            .RunAsync(Envelope().Payload, _ => Task.FromResult(true), true, "hash", default);
        Assert.Equal(0, result.TotalRequestCount);
        Assert.Equal(0, result.BarrierReleaseCount);
        Assert.Equal(0, adapter.Handles.Values.Sum(handle => handle.Calls));
        Assert.False(result.PrevalidationPassed);
    }

    [Theory]
    [InlineData("SuperSpeed")]
    [InlineData("USB2")]
    public async Task NativeRejectionIsNeverRetriedAndRequiresRestoration(string rejected)
    {
        var adapter = new FakeAdapter { RejectedSide = rejected };
        var result = await new PimaxUsbPairedNativeCoordinator(adapter, new MemoryProgress()).RunAsync(Envelope().Payload, _ => Task.FromResult(true), true, "hash", default);
        Assert.Equal(2, result.TotalRequestCount);
        Assert.True(result.ManualRestorationRequired);
        Assert.Equal(1, adapter.Handles[rejected].Calls);
    }

    [Theory]
    [InlineData("SuperSpeed")]
    [InlineData("USB2")]
    public async Task ExceptionAfterCounterIncrementIsIncompleteAndNeverRetried(string failed)
    {
        var adapter = new FakeAdapter { ThrowingSide = failed };
        var result = await new PimaxUsbPairedNativeCoordinator(adapter, new MemoryProgress()).RunAsync(Envelope().Payload, _ => Task.FromResult(true), true, "hash", default);
        var side = failed == "SuperSpeed" ? result.SuperSpeed : result.Usb2;
        Assert.True(side.Incomplete);
        Assert.Equal(1, side.RequestCount);
        Assert.Equal(1, adapter.Handles[failed].Calls);
        Assert.True(result.ManualRestorationRequired);
    }

    [Fact]
    public void DedicatedHelperIdentityIsRequired()
    {
        Assert.False(PimaxUsbPairedElevatedExecutor.IsPermittedExecutionContext(@"C:\bin\PimaxVrcSupervisor.PairedPortCycleHelper.exe", false));
        Assert.False(PimaxUsbPairedElevatedExecutor.IsPermittedExecutionContext(@"C:\bin\PimaxVrcSupervisor.PortCycleHelper.exe", true));
        Assert.True(PimaxUsbPairedElevatedExecutor.IsPermittedExecutionContext(@"C:\bin\PimaxVrcSupervisor.PairedPortCycleHelper.exe", true));
    }

    [Fact]
    public void FullMarkerSequenceIsRequiredAndFingerprintBound()
    {
        using var directory = new TempDirectory(); var path = Path.Combine(directory.Path, "markers.jsonl");
        File.WriteAllLines(path, new[]
        {
            Marker("observer-started", 1), Marker("pimax-info-opened", 2), Marker("pimax-crystal-model-selected", 3),
            Marker("connect-ready-before-action", 4), Marker("connect-action-completed", 5)
        });
        var sequence = Assert.IsType<PimaxUsbPairedMarkerSequence>(PimaxUsbPairedMarkerReader.Read(path));
        Assert.Equal("pimax-info-opened", sequence.InfoOpened.Label);
        File.WriteAllLines(path, new[] { Marker("observer-started", 1), Marker("pimax-crystal-model-selected", 3), Marker("connect-ready-before-action", 4), Marker("connect-action-completed", 5) });
        Assert.Null(PimaxUsbPairedMarkerReader.Read(path));
    }

    [Fact]
    public async Task FakeDryRunAndPreparationWriteBoundRequestWithoutUac()
    {
        var (signature, state) = PimaxUsbPortCycleExperimentTests.Fixture(); using var directory = new TempDirectory();
        var signaturePath = Path.Combine(directory.Path, "target.json"); var markerPath = Path.Combine(directory.Path, "markers.jsonl");
        var statusPath = Path.Combine(directory.Path, "status.json"); var requestPath = Path.Combine(directory.Path, "request.json");
        var resultPath = Path.Combine(directory.Path, "result.json"); var ssPath = Path.Combine(directory.Path, "ss.jsonl"); var usbPath = Path.Combine(directory.Path, "usb.jsonl");
        File.WriteAllText(signaturePath, JsonSerializer.Serialize(signature, PimaxUsbPortCycleJson.Options));
        File.WriteAllText(statusPath, "{}");
        File.WriteAllLines(markerPath, new[] { MarkerAt("observer-started", 1, FixtureNow.AddSeconds(-6)), MarkerAt("pimax-info-opened", 2, FixtureNow.AddSeconds(-5)), MarkerAt("pimax-crystal-model-selected", 3, FixtureNow.AddSeconds(-4)), MarkerAt("connect-ready-before-action", 4, FixtureNow.AddSeconds(-3)), MarkerAt("connect-action-completed", 5, FixtureNow.AddSeconds(-2)) });
        var runner = new PimaxUsbPairedExperimentRunner(new FixedCollector(state), () => FixtureNow);
        var dry = await runner.RunAsync(new(PimaxUsbPairedExperimentMode.DryRun, signaturePath, statusPath, markerPath, null, null, directory.Path, requestPath, resultPath, ssPath, usbPath, null, null, false, 90), default);
        Assert.True(dry.Safety.Permitted); Assert.NotNull(dry.ConfirmationToken);
        var prepared = await runner.RunAsync(new(PimaxUsbPairedExperimentMode.Prepare, signaturePath, statusPath, markerPath, dry.ConfirmationToken, PimaxUsbPairedExperimentRunner.ExactConfirmationPhrase, directory.Path, requestPath, resultPath, ssPath, usbPath, null, null, false, 90), default);
        Assert.True(prepared.Safety.Permitted); Assert.False(prepared.UacLaunch?.Attempted ?? false); Assert.True(File.Exists(requestPath));
        var envelope = JsonSerializer.Deserialize<PimaxUsbPairedPrivilegedRequest>(File.ReadAllText(requestPath), PimaxUsbPairedExperimentJson.Options)!;
        Assert.Equal(2, envelope.Payload.MaximumTotalRequests); Assert.Equal(PimaxUsbPairedExperimentSchema.Request, envelope.Payload.Schema);
    }

    [Fact]
    public async Task ObserveResultReadsPairedResultAndProducesObservationWithoutMutation()
    {
        var (signature, state) = PimaxUsbPortCycleExperimentTests.Fixture(); using var directory = new TempDirectory();
        var signaturePath = Path.Combine(directory.Path, "target.json");
        File.WriteAllText(signaturePath, JsonSerializer.Serialize(signature, PimaxUsbPortCycleJson.Options));
        var runner = new PimaxUsbPairedExperimentRunner(new FixedCollector(state), () => FixtureNow);
        var observed = await runner.RunAsync(new(PimaxUsbPairedExperimentMode.ObserveResult, signaturePath, null, null, null, null,
            directory.Path, null, Path.Combine(directory.Path, "missing-result.json"), null, null, null, null, false, 0), default);
        Assert.True(observed.Safety.Permitted);
        Assert.NotNull(observed.Observation);
        Assert.Equal(0, observed.PrivilegedResult?.TotalRequestCount ?? 0);
    }

    [Fact]
    public void PairedNativeAdapterHasOneGenericCallSiteAndNoRetryLoop()
    {
        var root = FindRoot(); var source = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "PimaxUsbPairedPortCycleExperiment.cs"));
        var start = source.IndexOf("internal sealed class WindowsPimaxUsbPairedNativeAdapter", StringComparison.Ordinal);
        var end = source.IndexOf("internal interface IPimaxUsbPairedProgressWriter", start, StringComparison.Ordinal);
        var adapter = source[start..end];
        Assert.Equal(1, Count(adapter, "Native.DeviceIoControl("));
        Assert.DoesNotContain("for (", adapter);
        Assert.DoesNotContain("while (", adapter);
        foreach (var token in new[] { "CM_Reenumerate", "Disable-PnpDevice", "Enable-PnpDevice", "pnputil", "devcon", "Restart-Service", "SendInput", "Phase29B-d347151" }) Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleSideContractAndDesignHardwareLockoutRemainIntact()
    {
        var root = FindRoot();
        var single = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "PimaxUsbPortCycleExperiment.cs"));
        Assert.Contains("if (connectionIndex != 4)", single);
        Assert.Contains("count = 1", single);
        var design = File.ReadAllText(Path.Combine(root, "PimaxVrcSupervisor", "PimaxUsbPairedPortCycleDesign.cs"));
        Assert.DoesNotContain("DeviceIoControl", design);
        Assert.DoesNotContain("CreateFileW", design);
    }

    private static PimaxUsbPairedPrivilegedRequest Envelope()
    {
        var (signature, state) = PimaxUsbPortCycleExperimentTests.Fixture();
        var plan = PimaxUsbPairedValidator.Validate(signature, state, new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero)).Plan!;
        var marker = new PimaxUsbPairedMarker("observer-started", "1", 1, Now, "operator-marker", "user-confirmed", "");
        var markers = new PimaxUsbPairedMarkerSequence(marker, marker with { Label = "pimax-info-opened", MarkerId = "2", Sequence = 2 }, marker with { Label = "pimax-crystal-model-selected", MarkerId = "3", Sequence = 3 }, marker with { Label = "connect-ready-before-action", MarkerId = "4", Sequence = 4 }, marker with { Label = "connect-action-completed", MarkerId = "5", Sequence = 5, Action = PimaxUsbPortCycleObserverReader.ConnectAction });
        var payload = new PimaxUsbPairedPrivilegedPayload(PimaxUsbPairedExperimentSchema.Request, "experiment", PimaxUsbPairedExperimentSchema.Operation,
            PimaxUsbPairedExperimentSchema.Strategy, signature, plan, "status", "markers", PimaxUsbPortCycleTargetValidator.StableObserver(plan.Observer)!,
            markers, "token", "token-hash", PimaxUsbPairedValidator.ConfirmationBinding("experiment"), "nonce", Now, Now.AddMinutes(1), Now.AddMinutes(1), 2, 1, 1, "none", "none", "none", "result", "ss.jsonl", "usb.jsonl");
        return new(payload, PimaxUsbPairedValidator.Fingerprint(payload));
    }
    private static PimaxUsbPairedPrivilegedRequest Rehash(PimaxUsbPairedPrivilegedRequest envelope, PimaxUsbPairedPrivilegedPayload payload) => new(payload, PimaxUsbPairedValidator.Fingerprint(payload));
    private static string Marker(string label, int sequence) => JsonSerializer.Serialize(new { label, markerId = $"marker-{sequence}", sequence, timestamp = Now.AddSeconds(sequence), type = "operator-marker", source = "user-confirmed" });
    private static string MarkerAt(string label, int sequence, DateTimeOffset timestamp) => JsonSerializer.Serialize(new { label, markerId = $"marker-{sequence}", sequence, timestamp, type = "operator-marker", source = "user-confirmed" });
    private static int Count(string text, string value) { var count = 0; for (var offset = 0; (offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0; offset += value.Length) count++; return count; }
    private static string FindRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent; return d?.FullName ?? throw new DirectoryNotFoundException(); }

    private sealed class MemoryProgress : IPimaxUsbPairedProgressWriter
    {
        public List<PimaxUsbPairedProgress> Items { get; } = [];
        public void Write(string path, PimaxUsbPairedProgress progress) { lock (Items) Items.Add(progress); }
    }
    private sealed class FailingProgress(string stage) : IPimaxUsbPairedProgressWriter
    {
        public void Write(string path, PimaxUsbPairedProgress progress)
        {
            if (progress.Stage == stage) throw new IOException("synthetic durable write failure");
        }
    }
    private sealed class FixedCollector(PimaxUsbPortCycleRuntimeState state) : IPimaxUsbPortCycleStateCollector
    {
        public Task<PimaxUsbPortCycleRuntimeState> CollectAsync(PimaxUsbPortCycleTargetSignature signature, string? observerStatusPath, string? markerFilePath, CancellationToken cancellationToken) => Task.FromResult(state);
    }
    private sealed class FakeAdapter : IPimaxUsbPairedNativeAdapter
    {
        public int OpenCount; public int DisposeCount; public int? FailOpenNumber; public string? RejectedSide; public string? ThrowingSide;
        public Dictionary<string, FakeHandle> Handles { get; } = new();
        public IPimaxUsbPairedNativeHandle Open(string path, int index)
        {
            var number = Interlocked.Increment(ref OpenCount); if (FailOpenNumber == number) throw new InvalidOperationException("open failed");
            var side = path.Contains("0626", StringComparison.OrdinalIgnoreCase) ? "SuperSpeed" : "USB2";
            var handle = new FakeHandle(this, side); Handles[side] = handle; return handle;
        }
    }
    private sealed class FakeHandle(FakeAdapter owner, string side) : IPimaxUsbPairedNativeHandle
    {
        public int Calls;
        public PimaxUsbPairedNativeResponse SubmitOnce()
        {
            Interlocked.Increment(ref Calls);
            if (owner.ThrowingSide == side) throw new InvalidOperationException("synthetic native exception");
            return new(owner.RejectedSide != side, owner.RejectedSide == side ? 5 : 0, 0);
        }
        public void Dispose() => Interlocked.Increment(ref owner.DisposeCount);
    }
}
