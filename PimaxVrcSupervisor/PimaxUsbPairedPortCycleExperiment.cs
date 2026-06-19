using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

internal static class PimaxUsbPairedExperimentSchema
{
    public const string Version = "pimax-usb-paired-port-cycle-experiment-v1";
    public const string Request = "pimax-paired-companion-port-cycle-request-v1";
    public const string Result = "pimax-paired-companion-port-cycle-result-v1";
    public const string Operation = "nearConcurrentPairedCompanionPortCycle";
    public const string Strategy = "nearConcurrent";
}

internal static class PimaxUsbPairedExperimentMode
{
    public const string DryRun = "dry-run";
    public const string Prepare = "prepare";
    public const string ExecuteElevatedHelper = "execute-elevated-helper";
    public const string ObserveResult = "observe-result";
}

internal static class PimaxUsbPairedExperimentJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal sealed record PimaxUsbPairedExperimentRequest(string Mode, string? TargetSignaturePath, string? ObserverStatusPath, string? MarkerFilePath,
    string? ConfirmationToken, string? ConfirmationPhrase, string? EvidenceDirectory, string? PrivilegedRequestPath, string? PrivilegedResultPath,
    string? SuperSpeedProgressPath, string? Usb2ProgressPath, string? HelperPath, string? ExpectedRequestSha256, bool LaunchHelper, int ObservationSeconds)
{
    public static PimaxUsbPairedExperimentRequest Parse(string[] args) => new(
        Option(args, "--mode") ?? PimaxUsbPairedExperimentMode.DryRun, Option(args, "--target-signature"), Option(args, "--observer-status"), Option(args, "--marker-file"),
        Option(args, "--confirmation-token"), Option(args, "--confirmation-phrase"), Option(args, "--evidence-dir"), Option(args, "--request-file"), Option(args, "--result-file"),
        Option(args, "--superspeed-progress"), Option(args, "--usb2-progress"), Option(args, "--helper-path"), Option(args, "--request-sha256"),
        args.Any(x => x.Equals("--launch-helper", StringComparison.OrdinalIgnoreCase)), Math.Clamp(ParseInt(Option(args, "--duration-seconds"), 90), 5, 180));
    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;
    private static string? Option(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return args[index][prefix.Length..];
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length) return args[index + 1];
        }
        return null;
    }
}

internal sealed record PimaxUsbPairedMarker(string Label, string MarkerId, int Sequence, DateTimeOffset Timestamp, string Type, string Source, string Action);
internal sealed record PimaxUsbPairedMarkerSequence(PimaxUsbPairedMarker ObserverStarted, PimaxUsbPairedMarker InfoOpened,
    PimaxUsbPairedMarker CrystalSelected, PimaxUsbPairedMarker ConnectReady, PimaxUsbPairedMarker ConnectScanStarted);

internal static class PimaxUsbPairedMarkerReader
{
    private static readonly string[] Labels = ["observer-started", "pimax-info-opened", "pimax-crystal-model-selected", "connect-ready-before-action", "connect-action-completed"];
    public static PimaxUsbPairedMarkerSequence? Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var parsed = new List<PimaxUsbPairedMarker>();
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                var label = root.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                if (!Labels.Contains(label, StringComparer.OrdinalIgnoreCase)) continue;
                var timestamp = root.TryGetProperty("timestamp", out var t) && t.TryGetDateTimeOffset(out var dt) ? dt : DateTimeOffset.MinValue;
                var sequence = root.TryGetProperty("sequence", out var s) && s.TryGetInt32(out var number) ? number : parsed.Count + 1;
                var type = root.TryGetProperty("type", out var ty) ? ty.GetString() ?? "operator-marker" : "operator-marker";
                var source = root.TryGetProperty("source", out var so) ? so.GetString() ?? "user-confirmed" : "user-confirmed";
                var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
                if (label.Equals("connect-action-completed", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(action)) action = PimaxUsbPortCycleObserverReader.ConnectAction;
                var id = root.TryGetProperty("markerId", out var mi) ? mi.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(id)) id = PimaxUsbPortCycleTargetValidator.Fingerprint(new { label, sequence, timestamp, type, source, action });
                parsed.Add(new(label, id, sequence, timestamp, type, source, action));
            }
            catch (JsonException) { }
        }
        var connect = parsed.FindLastIndex(x => x.Label.Equals(Labels[4], StringComparison.OrdinalIgnoreCase));
        if (connect < 0) return null;
        var selected = new PimaxUsbPairedMarker[5]; var before = connect + 1;
        for (var i = 4; i >= 0; i--)
        {
            var index = -1;
            for (var candidate = before - 1; candidate >= 0; candidate--)
                if (parsed[candidate].Label.Equals(Labels[i], StringComparison.OrdinalIgnoreCase)) { index = candidate; break; }
            if (index < 0) return null; selected[i] = parsed[index]; before = index;
        }
        if (!(selected[0].Sequence < selected[1].Sequence && selected[1].Sequence < selected[2].Sequence && selected[2].Sequence < selected[3].Sequence && selected[3].Sequence < selected[4].Sequence)) return null;
        return new(selected[0], selected[1], selected[2], selected[3], selected[4]);
    }
}

internal sealed record PimaxUsbPairedPrivilegedPayload(string Schema, string ExperimentId, string OperationKind, string SelectedStrategy,
    PimaxUsbPortCycleTargetSignature TargetSignature, PimaxUsbPortCyclePlan Plan, string ObserverStatusPath, string MarkerFilePath,
    PimaxUsbPortCycleStableObserverIdentity MarkerIdentity, PimaxUsbPairedMarkerSequence MarkerSequence, string ConfirmationToken, string TokenFingerprint,
    string ExactConfirmationBinding, string Nonce,
    DateTimeOffset CreatedAt, DateTimeOffset TokenExpiresAt, DateTimeOffset ExpiresAt, int MaximumTotalRequests, int MaximumSuperSpeedRequests,
    int MaximumUsb2Requests, string RetryPolicy, string FallbackPolicy, string CompensationPolicy, string OutputResultPath,
    string SuperSpeedProgressPath, string Usb2ProgressPath);
internal sealed record PimaxUsbPairedPrivilegedRequest(PimaxUsbPairedPrivilegedPayload Payload, string RequestSha256);
internal sealed record PimaxUsbPairedProgress(string ExperimentId, string Side, long MonotonicTimestamp, DateTimeOffset UtcTimestamp, int ProcessId,
    int ThreadId, int RequestCount, string Stage, bool? ReturnedSuccess = null, int? Win32Error = null, uint? NativeStatus = null, string? Error = null);
internal sealed record PimaxUsbPairedSideResult(string Side, bool HandleOpened, bool WorkerReady, long? ReadyTimestamp, long? EntryTimestamp,
    long? ReturnTimestamp, DateTimeOffset? EntryUtc, DateTimeOffset? ReturnUtc, int RequestCount, bool? ReturnedSuccess, int? Win32Error,
    uint? NativeStatus, bool Incomplete, string? Error);
internal sealed record PimaxUsbPairedPrivilegedResult(string Schema, string ExperimentId, int HelperPid, bool Elevated, string? RequestSha256,
    bool RequestValid, bool PrevalidationPassed, bool BothHandlesOpened, bool BothWorkersReady, int BarrierReleaseCount, long? BarrierReleaseTimestamp,
    DateTimeOffset? BarrierReleaseUtc, PimaxUsbPairedSideResult SuperSpeed, PimaxUsbPairedSideResult Usb2, int TotalRequestCount,
    double? SignedEntrySkewMilliseconds, double? AbsoluteEntrySkewMilliseconds, bool SubmissionSkewUnderTarget, string AggregationStatus,
    bool ManualRestorationRequired, DateTimeOffset CompletedAt, bool Success, string[] Warnings, string[] Errors);
internal sealed record PimaxUsbPairedExperimentResult(string SchemaVersion, string ExperimentId, string Mode, DateTimeOffset StartedAt,
    DateTimeOffset EndedAt, PimaxUsbPortCyclePlan? Plan, PimaxUsbPortCycleSafety Safety, string? ConfirmationToken,
    DateTimeOffset? ConfirmationTokenExpiresAt, string? PrivilegedRequestPath, string? PrivilegedRequestSha256,
    PimaxUsbPairedPrivilegedResult? PrivilegedResult, PimaxUsbPortCycleObservation? Observation, PimaxUsbPortCycleUacLaunch? UacLaunch,
    string[] Warnings, string[] Errors);

internal static class PimaxUsbPairedValidator
{
    public static (PimaxUsbPortCyclePlan? Plan, PimaxUsbPortCycleSafety Safety) Validate(PimaxUsbPortCycleTargetSignature signature, PimaxUsbPortCycleRuntimeState state, DateTimeOffset now)
    {
        var baseResult = PimaxUsbPortCycleTargetValidator.Validate(signature, state, now);
        if (!baseResult.Safety.Permitted || baseResult.Plan is null) return baseResult;
        var failures = new List<string>(baseResult.Safety.RefusalReasons);
        if (signature.PimaxUsb2Port.ConnectionIndex != 4) failures.Add("USB 2 target index must be 4.");
        if (signature.PimaxSuperSpeedPort.ConnectionIndex != 4) failures.Add("SuperSpeed target index must be 4.");
        if (!signature.Usb2Hub.Vid.Equals("05E3", StringComparison.OrdinalIgnoreCase) || !signature.Usb2Hub.Pid.Equals("0610", StringComparison.OrdinalIgnoreCase)) failures.Add("USB 2 target must be 05E3:0610.");
        if (!signature.SuperSpeedHub.Vid.Equals("05E3", StringComparison.OrdinalIgnoreCase) || !signature.SuperSpeedHub.Pid.Equals("0626", StringComparison.OrdinalIgnoreCase)) failures.Add("SuperSpeed target must be 05E3:0626.");
        if (signature.PimaxUsb2Port.ConnectorGroupId != signature.PimaxSuperSpeedPort.ConnectorGroupId) failures.Add("Pimax companion connector identities differ.");
        if (signature.ViveUsb2Port.ConnectionIndex != 2 || signature.ViveSuperSpeedPort.ConnectionIndex != 2) failures.Add("Vive exclusion must remain on index 2.");
        var plan = baseResult.Plan with
        {
            ExperimentKind = PimaxUsbPairedExperimentSchema.Operation,
            ExactRequestCount = 2,
            ExcludedOperations = ["retry", "fallback", "compensation cycle", "target substitution", "whole-hub reset", "controller reset", "device state change", "service or process action", "UI automation"]
        };
        plan = plan with { BindingSha256 = PimaxUsbPortCycleTargetValidator.StablePlanFingerprint(plan) };
        return (plan, new(failures.Count == 0, baseResult.Safety.ChecksPassed, failures.ToArray(), baseResult.Safety.Warnings));
    }

    public static string Fingerprint(PimaxUsbPairedPrivilegedPayload payload) => PimaxUsbPortCycleTargetValidator.Fingerprint(new
    {
        payload.Schema, payload.ExperimentId, payload.OperationKind, payload.SelectedStrategy,
        Target = PimaxUsbPortCycleTargetValidator.StableTargetSignatureFingerprint(payload.TargetSignature),
        payload.Plan.BindingSha256, payload.ObserverStatusPath, payload.MarkerFilePath, payload.MarkerIdentity, payload.MarkerSequence,
        payload.ConfirmationToken, payload.TokenFingerprint, payload.ExactConfirmationBinding, payload.Nonce, payload.CreatedAt, payload.TokenExpiresAt, payload.ExpiresAt,
        payload.MaximumTotalRequests, payload.MaximumSuperSpeedRequests, payload.MaximumUsb2Requests, payload.RetryPolicy,
        payload.FallbackPolicy, payload.CompensationPolicy, payload.OutputResultPath, payload.SuperSpeedProgressPath, payload.Usb2ProgressPath
    });

    public static void ValidateEnvelope(PimaxUsbPairedPrivilegedRequest envelope, string expectedHash, DateTimeOffset now)
    {
        var p = envelope.Payload;
        if (p.Schema != PimaxUsbPairedExperimentSchema.Request) throw new InvalidDataException("Paired helper accepts only the paired request schema.");
        if (Fingerprint(p) != envelope.RequestSha256 || envelope.RequestSha256 != expectedHash) throw new InvalidDataException("Paired request SHA-256 does not match.");
        if (p.OperationKind != PimaxUsbPairedExperimentSchema.Operation || p.SelectedStrategy != PimaxUsbPairedExperimentSchema.Strategy) throw new InvalidDataException("Paired operation kind or strategy is invalid.");
        if (p.MaximumTotalRequests != 2 || p.MaximumSuperSpeedRequests != 1 || p.MaximumUsb2Requests != 1) throw new InvalidDataException("Paired request limits are invalid.");
        if (p.RetryPolicy != "none" || p.FallbackPolicy != "none" || p.CompensationPolicy != "none") throw new InvalidDataException("Retry, fallback, and compensation must be none.");
        if (p.ExactConfirmationBinding != ConfirmationBinding(p.ExperimentId)) throw new InvalidDataException("Exact operator confirmation binding is invalid.");
        if (p.ExpiresAt <= now || p.TokenExpiresAt <= now) throw new InvalidDataException("Paired request or token expired.");
        if (string.IsNullOrWhiteSpace(p.Nonce)) throw new InvalidDataException("Paired request nonce is missing.");
        if (p.TargetSignature.PimaxUsb2Port.ConnectionIndex != 4 || p.TargetSignature.PimaxSuperSpeedPort.ConnectionIndex != 4) throw new InvalidDataException("Paired request indices are invalid.");
        if (p.TargetSignature.Usb2Hub.InterfacePath.Equals(p.TargetSignature.SuperSpeedHub.InterfacePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Paired targets must use distinct hub handles.");
        if (Path.GetFullPath(p.SuperSpeedProgressPath).Equals(Path.GetFullPath(p.Usb2ProgressPath), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Paired sides require distinct progress journals.");
    }
    public static string ConfirmationBinding(string experimentId) => PimaxUsbPortCycleTargetValidator.Fingerprint(new { ExperimentId = experimentId, PimaxUsbPairedExperimentRunner.ExactConfirmationPhrase });
}

internal interface IPimaxUsbPairedNativeHandle : IDisposable { PimaxUsbPairedNativeResponse SubmitOnce(); }
internal sealed record PimaxUsbPairedNativeResponse(bool ReturnedSuccess, int Win32Error, uint NativeStatus);
internal interface IPimaxUsbPairedNativeAdapter { IPimaxUsbPairedNativeHandle Open(string interfacePath, int connectionIndex); }

internal sealed class WindowsPimaxUsbPairedNativeAdapter : IPimaxUsbPairedNativeAdapter
{
    public IPimaxUsbPairedNativeHandle Open(string interfacePath, int connectionIndex) => new Handle(interfacePath, connectionIndex);
    private sealed class Handle : IPimaxUsbPairedNativeHandle
    {
        private const uint IoctlUsbHubCyclePort = 0x00220444;
        private readonly SafeFileHandle _handle;
        private readonly int _index;
        private int _submitted;
        public Handle(string path, int index)
        {
            if (index != 4) throw new InvalidOperationException("Only exact connection index 4 is permitted.");
            _index = index;
            _handle = Native.CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open exact paired USB hub handle.");
        }
        public PimaxUsbPairedNativeResponse SubmitOnce()
        {
            if (Interlocked.Exchange(ref _submitted, 1) != 0) throw new InvalidOperationException("A paired side cannot submit twice.");
            var buffer = new byte[8]; BitConverter.GetBytes((uint)_index).CopyTo(buffer, 0);
            var ok = Native.DeviceIoControl(_handle, IoctlUsbHubCyclePort, buffer, buffer.Length, buffer, buffer.Length, out _, IntPtr.Zero);
            return new(ok, ok ? 0 : Marshal.GetLastWin32Error(), BitConverter.ToUInt32(buffer, 4));
        }
        public void Dispose() => _handle.Dispose();
    }
    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputLength, byte[] output, int outputLength, out int returned, IntPtr overlapped);
    }
}

internal interface IPimaxUsbPairedProgressWriter { void Write(string path, PimaxUsbPairedProgress progress); }
internal sealed class PimaxUsbPairedProgressWriter : IPimaxUsbPairedProgressWriter
{
    private readonly object _gate = new();
    public void Write(string path, PimaxUsbPairedProgress progress)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine(JsonSerializer.Serialize(progress, PimaxUsbPairedExperimentJson.Options));
            writer.Flush(); stream.Flush(true);
        }
    }
}

internal sealed class PimaxUsbPairedNativeCoordinator(IPimaxUsbPairedNativeAdapter adapter, IPimaxUsbPairedProgressWriter progress)
{
    private int _total;
    private int _superSpeed;
    private int _usb2;
    private int _releaseCount;
    public async Task<PimaxUsbPairedPrivilegedResult> RunAsync(PimaxUsbPairedPrivilegedPayload payload, Func<CancellationToken, Task<bool>> finalValidation,
        bool elevated, string requestHash, CancellationToken cancellationToken)
    {
        var errors = new List<string>(); var warnings = new List<string>(); var frequency = Stopwatch.Frequency;
        IPimaxUsbPairedNativeHandle? ssHandle = null; IPimaxUsbPairedNativeHandle? usbHandle = null;
        ManualResetEventSlim? gate = null; var releaseAuthorized = 0;
        Task<PimaxUsbPairedSideResult>? ssTask = null; Task<PimaxUsbPairedSideResult>? usbTask = null;
        var ssPath = payload.SuperSpeedProgressPath; var usbPath = payload.Usb2ProgressPath;
        try
        {
            Journal(ssPath, payload, "SuperSpeed", 0, "request-validated"); Journal(usbPath, payload, "USB2", 0, "request-validated");
            ssHandle = adapter.Open(payload.TargetSignature.SuperSpeedHub.InterfacePath, 4); Journal(ssPath, payload, "SuperSpeed", 0, "handle-opened");
            usbHandle = adapter.Open(payload.TargetSignature.Usb2Hub.InterfacePath, 4); Journal(usbPath, payload, "USB2", 0, "handle-opened");
            var ready = new CountdownEvent(2); gate = new ManualResetEventSlim(false);
            ssTask = Dedicated(() => Worker("SuperSpeed", ssHandle, ssPath, payload, ready, gate, () => Volatile.Read(ref releaseAuthorized) == 1));
            usbTask = Dedicated(() => Worker("USB2", usbHandle, usbPath, payload, ready, gate, () => Volatile.Read(ref releaseAuthorized) == 1));
            ready.Wait(cancellationToken);
            if (!await finalValidation(cancellationToken))
            {
                errors.Add("Final safety validation failed after both handles opened."); gate.Set();
                var refused = await Task.WhenAll(ssTask, usbTask);
                return Result(payload, elevated, requestHash, true, false, true, true, 0, null, null, refused[0], refused[1], errors, warnings, frequency);
            }
            Journal(ssPath, payload, "SuperSpeed", 0, "target-revalidated"); Journal(usbPath, payload, "USB2", 0, "target-revalidated");
            if (_total != 0) throw new InvalidOperationException("Request count was nonzero before barrier release.");
            Journal(ssPath, payload, "SuperSpeed", 0, "pre-release-checkpoint"); Journal(usbPath, payload, "USB2", 0, "pre-release-checkpoint");
            var releaseTimestamp = Stopwatch.GetTimestamp(); var releaseUtc = DateTimeOffset.UtcNow;
            if (Interlocked.Increment(ref _releaseCount) != 1) throw new InvalidOperationException("Barrier may be released only once.");
            Volatile.Write(ref releaseAuthorized, 1);
            gate.Set();
            TryJournal(ssPath, payload, "SuperSpeed", 0, "barrier-released", warnings, releaseTimestamp);
            TryJournal(usbPath, payload, "USB2", 0, "barrier-released", warnings, releaseTimestamp);
            var sides = await Task.WhenAll(ssTask, usbTask);
            return Result(payload, elevated, requestHash, true, true, true, true, _releaseCount, releaseTimestamp, releaseUtc, sides[0], sides[1], errors, warnings, frequency);
        }
        catch (Exception ex)
        {
            gate?.Set();
            errors.Add(ex.Message);
            PimaxUsbPairedSideResult[]? completed = null;
            if (ssTask is not null && usbTask is not null) completed = await Task.WhenAll(ssTask, usbTask);
            var failedSs = completed?[0] ?? Empty("SuperSpeed", ssHandle is not null);
            var failedUsb = completed?[1] ?? Empty("USB2", usbHandle is not null);
            return Result(payload, elevated, requestHash, true, false, ssHandle is not null && usbHandle is not null,
                completed is not null, _releaseCount, null, null, failedSs, failedUsb, errors, warnings, frequency);
        }
        finally
        {
            gate?.Set();
            if (ssHandle is not null) { ssHandle.Dispose(); TryJournal(ssPath, payload, "SuperSpeed", _superSpeed, "handle-closed", warnings); }
            if (usbHandle is not null) { usbHandle.Dispose(); TryJournal(usbPath, payload, "USB2", _usb2, "handle-closed", warnings); }
        }
    }

    private Task<PimaxUsbPairedSideResult> Dedicated(Func<PimaxUsbPairedSideResult> action) => Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    private PimaxUsbPairedSideResult Worker(string side, IPimaxUsbPairedNativeHandle handle, string path, PimaxUsbPairedPrivilegedPayload payload,
        CountdownEvent ready, ManualResetEventSlim gate, Func<bool> authorized)
    {
        var readyAt = Stopwatch.GetTimestamp(); Journal(path, payload, side, 0, "worker-ready", readyAt); ready.Signal(); gate.Wait();
        if (!authorized()) return new(side, true, true, readyAt, null, null, null, null, 0, null, null, null, false, "Barrier release was not authorized.");
        long entry = 0; DateTimeOffset? entryUtc = null;
        try
        {
            var sideCount = side == "SuperSpeed" ? Interlocked.Increment(ref _superSpeed) : Interlocked.Increment(ref _usb2);
            var total = Interlocked.Increment(ref _total);
            if (sideCount != 1 || total > 2) throw new InvalidOperationException("Paired native request limit exceeded.");
            entry = Stopwatch.GetTimestamp(); entryUtc = DateTimeOffset.UtcNow; Journal(path, payload, side, sideCount, "native-call-entry", entry);
            var native = handle.SubmitOnce(); var returned = Stopwatch.GetTimestamp(); var returnUtc = DateTimeOffset.UtcNow;
            Journal(path, payload, side, sideCount, "native-call-return", returned, native);
            Journal(path, payload, side, sideCount, "worker-complete", returned, native);
            return new(side, true, true, readyAt, entry, returned, entryUtc, returnUtc, sideCount, native.ReturnedSuccess, native.Win32Error, native.NativeStatus, false, null);
        }
        catch (Exception ex)
        {
            try { Journal(path, payload, side, side == "SuperSpeed" ? _superSpeed : _usb2, "worker-error", error: ex.Message); } catch { }
            try { Journal(path, payload, side, side == "SuperSpeed" ? _superSpeed : _usb2, "worker-complete", error: ex.Message); } catch { }
            return new(side, true, true, readyAt, entry == 0 ? null : entry, Stopwatch.GetTimestamp(), entryUtc, DateTimeOffset.UtcNow,
                side == "SuperSpeed" ? _superSpeed : _usb2, null, null, null, entry != 0, ex.Message);
        }
    }

    private void Journal(string path, PimaxUsbPairedPrivilegedPayload payload, string side, int count, string stage, long? timestamp = null, PimaxUsbPairedNativeResponse? native = null, string? error = null)
        => progress.Write(path, new(payload.ExperimentId, side, timestamp ?? Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow, Environment.ProcessId,
            Environment.CurrentManagedThreadId, count, stage, native?.ReturnedSuccess, native?.Win32Error, native?.NativeStatus, error));
    private void TryJournal(string path, PimaxUsbPairedPrivilegedPayload payload, string side, int count, string stage, List<string> warnings, long? timestamp = null)
    {
        try { Journal(path, payload, side, count, stage, timestamp); }
        catch (Exception ex) { warnings.Add($"Could not record {side} {stage}: {ex.Message}"); }
    }
    private static PimaxUsbPairedSideResult Empty(string side, bool opened) => new(side, opened, false, null, null, null, null, null, 0, null, null, null, false, null);
    private PimaxUsbPairedPrivilegedResult Result(PimaxUsbPairedPrivilegedPayload p, bool elevated, string hash, bool requestValid, bool prevalidationPassed, bool opened, bool ready,
        int releases, long? released, DateTimeOffset? releasedUtc, PimaxUsbPairedSideResult ss, PimaxUsbPairedSideResult usb, List<string> errors, List<string> warnings, long frequency)
    {
        double? signed = ss.EntryTimestamp is null || usb.EntryTimestamp is null ? null : (usb.EntryTimestamp.Value - ss.EntryTimestamp.Value) * 1000d / frequency;
        var bothAccepted = ss.ReturnedSuccess == true && usb.ReturnedSuccess == true;
        var status = _total == 0 ? "zeroSubmitted" : _total == 2 ? bothAccepted ? "bothAccepted" : "bothSubmittedPartialFailure" : ss.RequestCount == 1 ? "onlySuperSpeedMayHaveSubmitted" : "onlyUsb2MayHaveSubmitted";
        return new(PimaxUsbPairedExperimentSchema.Result, p.ExperimentId, Environment.ProcessId, elevated, hash, requestValid, prevalidationPassed, opened, ready,
            releases, released, releasedUtc, ss, usb, _total, signed, signed is null ? null : Math.Abs(signed.Value), signed is not null && Math.Abs(signed.Value) < 50,
            status, _total > 0 && !bothAccepted, DateTimeOffset.UtcNow, prevalidationPassed && bothAccepted && _total == 2, warnings.ToArray(), errors.ToArray());
    }
}

internal sealed class PimaxUsbPairedExperimentRunner(IPimaxUsbPortCycleStateCollector collector, Func<DateTimeOffset>? clock = null)
{
    public const string ExactConfirmationPhrase = "CONFIRM NEAR-CONCURRENT PIMAX PAIRED PORT CYCLE EXPERIMENT";
    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.UtcNow);
    public async Task<PimaxUsbPairedExperimentResult> RunAsync(PimaxUsbPairedExperimentRequest request, CancellationToken cancellationToken)
    {
        var started = _now(); var id = "phase28c3d-" + Guid.NewGuid().ToString("N");
        if (request.Mode == PimaxUsbPairedExperimentMode.ExecuteElevatedHelper)
        {
            var privileged = await PimaxUsbPairedElevatedExecutor.ExecuteAsync(request, cancellationToken);
            return Result(privileged.ExperimentId, request.Mode, started, null, new(privileged.Success, [], privileged.Errors, privileged.Warnings), privileged: privileged);
        }
        if (request.Mode == PimaxUsbPairedExperimentMode.ObserveResult) return await ObserveAsync(request, id, started, cancellationToken);
        PimaxUsbPortCycleTargetSignature? signature;
        try { signature = JsonSerializer.Deserialize<PimaxUsbPortCycleTargetSignature>(File.ReadAllText(request.TargetSignaturePath!), PimaxUsbPortCycleJson.Options); }
        catch (Exception ex) { return Result(id, request.Mode, started, null, new(false, [], ["A valid target signature is required."], []), errors: [ex.Message]); }
        if (signature is null) return Result(id, request.Mode, started, null, new(false, [], ["A valid target signature is required."], []));
        var current = await collector.CollectAsync(signature, request.ObserverStatusPath, request.MarkerFilePath, cancellationToken);
        var validation = PimaxUsbPairedValidator.Validate(signature, current, _now());
        if (request.Mode == PimaxUsbPairedExperimentMode.DryRun)
        {
            string? issuedToken = null; DateTimeOffset? expires = null;
            if (validation.Safety.Permitted && validation.Plan is not null) (issuedToken, expires) = PimaxUsbPortCycleConfirmationToken.Create(id, validation.Plan, _now());
            return Result(id, request.Mode, started, validation.Plan, validation.Safety, issuedToken, expires);
        }
        if (request.Mode != PimaxUsbPairedExperimentMode.Prepare) return Result(id, request.Mode, started, validation.Plan, new(false, validation.Safety.ChecksPassed, [.. validation.Safety.RefusalReasons, "Unsupported mode."], validation.Safety.Warnings));
        if (!validation.Safety.Permitted || validation.Plan?.Observer is null) return Result(id, request.Mode, started, validation.Plan, validation.Safety);
        if (request.ConfirmationPhrase != ExactConfirmationPhrase) return Result(id, request.Mode, started, validation.Plan, new(false, validation.Safety.ChecksPassed, ["Exact confirmation phrase was not supplied."], validation.Safety.Warnings));
        var tokenId = TokenExperimentId(request.ConfirmationToken);
        if (tokenId is null) return Result(id, request.Mode, started, validation.Plan, new(false, validation.Safety.ChecksPassed, ["Confirmation token payload could not be read."], []));
        var token = PimaxUsbPortCycleConfirmationToken.Validate(request.ConfirmationToken, tokenId, validation.Plan, _now(), request.EvidenceDirectory, true);
        if (!token.Accepted) return Result(tokenId, request.Mode, started, validation.Plan, new(false, validation.Safety.ChecksPassed, [token.Reason!], []));
        if (!PimaxUsbPortCycleTargetValidator.IsMarkerFresh(validation.Plan.Observer, _now())) return Result(tokenId, request.Mode, started, validation.Plan, new(false, validation.Safety.ChecksPassed, ["Connect scan marker became stale."], []));
        if (new[] { request.PrivilegedRequestPath, request.PrivilegedResultPath, request.SuperSpeedProgressPath, request.Usb2ProgressPath, request.ObserverStatusPath, request.MarkerFilePath }.Any(string.IsNullOrWhiteSpace))
            return Result(tokenId, request.Mode, started, validation.Plan, new(false, validation.Safety.ChecksPassed, ["All request, result, progress, and observer paths are required."], []));
        var markers = PimaxUsbPairedMarkerReader.Read(request.MarkerFilePath);
        if (markers is null) return Result(tokenId, request.Mode, started, validation.Plan, new(false, validation.Safety.ChecksPassed, ["Complete immutable marker sequence is required."], []));
        var payload = new PimaxUsbPairedPrivilegedPayload(PimaxUsbPairedExperimentSchema.Request, tokenId, PimaxUsbPairedExperimentSchema.Operation,
            PimaxUsbPairedExperimentSchema.Strategy, signature, validation.Plan, Path.GetFullPath(request.ObserverStatusPath!), Path.GetFullPath(request.MarkerFilePath!),
            PimaxUsbPortCycleTargetValidator.StableObserver(validation.Plan.Observer)!, markers, request.ConfirmationToken!, PimaxUsbPortCycleTargetValidator.Fingerprint(request.ConfirmationToken!),
            PimaxUsbPairedValidator.ConfirmationBinding(tokenId), token.Nonce!,
            _now(), token.ExpiresAt!.Value, _now().AddSeconds(60), 2, 1, 1, "none", "none", "none", Path.GetFullPath(request.PrivilegedResultPath!),
            Path.GetFullPath(request.SuperSpeedProgressPath!), Path.GetFullPath(request.Usb2ProgressPath!));
        var hash = PimaxUsbPairedValidator.Fingerprint(payload); AtomicWrite(request.PrivilegedRequestPath!, new PimaxUsbPairedPrivilegedRequest(payload, hash));
        PimaxUsbPortCycleUacLaunch? launch = null; if (request.LaunchHelper) launch = PimaxUsbPairedUacLauncher.Launch(request.HelperPath, request.PrivilegedRequestPath!, hash);
        return Result(tokenId, request.Mode, started, validation.Plan, validation.Safety, request.ConfirmationToken, token.ExpiresAt, request.PrivilegedRequestPath, hash, launch: launch);
    }
    private async Task<PimaxUsbPairedExperimentResult> ObserveAsync(PimaxUsbPairedExperimentRequest request, string id, DateTimeOffset started, CancellationToken cancellationToken)
    {
        try
        {
            var signature = JsonSerializer.Deserialize<PimaxUsbPortCycleTargetSignature>(File.ReadAllText(request.TargetSignaturePath!), PimaxUsbPortCycleJson.Options)
                ?? throw new InvalidDataException("Target signature is missing.");
            PimaxUsbPairedPrivilegedResult? privileged = null;
            if (!string.IsNullOrWhiteSpace(request.PrivilegedResultPath) && File.Exists(request.PrivilegedResultPath))
                privileged = JsonSerializer.Deserialize<PimaxUsbPairedPrivilegedResult>(File.ReadAllText(request.PrivilegedResultPath), PimaxUsbPairedExperimentJson.Options);
            var baseline = await collector.CollectAsync(signature, request.ObserverStatusPath, request.MarkerFilePath, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(request.ObservationSeconds), cancellationToken);
            var final = await collector.CollectAsync(signature, request.ObserverStatusPath, request.MarkerFilePath, cancellationToken);
            var usb2 = Changed(signature.Usb2Hub, 4, baseline.Topology, final.Topology);
            var superSpeed = Changed(signature.SuperSpeedHub, 4, baseline.Topology, final.Topology);
            var vive = !Changed(signature.Usb2Hub, 2, baseline.Topology, final.Topology) && !Changed(signature.SuperSpeedHub, 2, baseline.Topology, final.Topology);
            var unrelated = PimaxUsbPortCycleTargetValidator.StablePhysicalInventoryFingerprint(PimaxUsbPortCycleTargetValidator.Inventory(baseline.Topology, FindHub(baseline.Topology, signature.Usb2Hub), FindHub(baseline.Topology, signature.SuperSpeedHub)))
                == PimaxUsbPortCycleTargetValidator.StablePhysicalInventoryFingerprint(PimaxUsbPortCycleTargetValidator.Inventory(final.Topology, FindHub(final.Topology, signature.Usb2Hub), FindHub(final.Topology, signature.SuperSpeedHub)));
            var expected = signature.PimaxUsb2Port.DescendantPnpInstanceIds.Concat(signature.PimaxSuperSpeedPort.DescendantPnpInstanceIds).Distinct(StringComparer.OrdinalIgnoreCase);
            var actual = final.Topology.Ports.Where(port => port.PhysicalConnectorGroupId == signature.PimaxUsb2Port.ConnectorGroupId).SelectMany(port => port.DescendantPnpInstanceIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = expected.Where(value => !actual.Contains(value)).ToArray();
            var outcome = PimaxUsbPortCycleObservationClassifier.Classify(usb2, superSpeed, vive, unrelated, missing.Length, final.Registration.Assessment.State == PimaxRegistrationState.RegisteredReady);
            var observation = new PimaxUsbPortCycleObservation(started, _now(), outcome, usb2, superSpeed, vive, unrelated, missing, final.Registration.Assessment.State, final.Registration.Assessment.Confidence);
            return Result(privileged?.ExperimentId ?? id, request.Mode, started, null, new(true, [], [], []), privileged: privileged, observation: observation);
        }
        catch (Exception ex) { return Result(id, request.Mode, started, null, new(false, [], [ex.Message], []), errors: [ex.ToString()]); }
    }
    private static bool Changed(PimaxUsbPortCycleHubIdentity hub, int index, PimaxUsbPhysicalPortSnapshot first, PimaxUsbPhysicalPortSnapshot second)
    {
        var before = Port(first, hub, index); var after = Port(second, hub, index);
        return before?.ConnectionStatus != after?.ConnectionStatus || before?.DriverKey != after?.DriverKey || PimaxUsbPortCycleTargetValidator.Fingerprint(before?.DescendantPnpInstanceIds ?? []) != PimaxUsbPortCycleTargetValidator.Fingerprint(after?.DescendantPnpInstanceIds ?? []);
    }
    private static PimaxUsbPortRecord? Port(PimaxUsbPhysicalPortSnapshot snapshot, PimaxUsbPortCycleHubIdentity hub, int index) { var found = snapshot.Hubs.SingleOrDefault(value => value.InterfacePath.Equals(hub.InterfacePath, StringComparison.OrdinalIgnoreCase)); return found is null ? null : snapshot.Ports.SingleOrDefault(value => value.HubId == found.HubId && value.ConnectionIndex == index); }
    private static PimaxUsbHubRecord FindHub(PimaxUsbPhysicalPortSnapshot snapshot, PimaxUsbPortCycleHubIdentity identity) => snapshot.Hubs.Single(value => value.InterfacePath.Equals(identity.InterfacePath, StringComparison.OrdinalIgnoreCase));
    private static void AtomicWrite<T>(string path, T value) { var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temp = full + ".tmp-" + Guid.NewGuid().ToString("N"); File.WriteAllText(temp, JsonSerializer.Serialize(value, PimaxUsbPairedExperimentJson.Options), new UTF8Encoding(false)); File.Move(temp, full, false); }
    private static string? TokenExperimentId(string? token) { try { var body = token!.Split('.', 2)[0].Replace('-', '+').Replace('_', '/'); var bytes = Convert.FromBase64String(body.PadRight(body.Length + (4 - body.Length % 4) % 4, '=')); using var doc = JsonDocument.Parse(bytes); return doc.RootElement.GetProperty("experimentId").GetString(); } catch { return null; } }
    private PimaxUsbPairedExperimentResult Result(string id, string mode, DateTimeOffset started, PimaxUsbPortCyclePlan? plan, PimaxUsbPortCycleSafety safety,
        string? token = null, DateTimeOffset? expires = null, string? requestPath = null, string? requestHash = null, PimaxUsbPairedPrivilegedResult? privileged = null,
        PimaxUsbPortCycleObservation? observation = null, PimaxUsbPortCycleUacLaunch? launch = null, string[]? warnings = null, string[]? errors = null)
        => new(PimaxUsbPairedExperimentSchema.Version, id, mode, started, _now(), plan, safety, token, expires, requestPath, requestHash, privileged, observation, launch, warnings ?? [], errors ?? []);
}

internal static class PimaxUsbPairedUacLauncher
{
    public static PimaxUsbPortCycleUacLaunch Launch(string? helper, string request, string hash)
    {
        if (string.IsNullOrWhiteSpace(helper) || !File.Exists(helper)) return new(true, false, false, null, "Paired helper executable was not found.");
        try
        {
            var start = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(helper))! };
            start.ArgumentList.Add("pimax-usb-paired-port-cycle-experiment-json"); start.ArgumentList.Add("--mode"); start.ArgumentList.Add(PimaxUsbPairedExperimentMode.ExecuteElevatedHelper);
            start.ArgumentList.Add("--request-file"); start.ArgumentList.Add(Path.GetFullPath(request)); start.ArgumentList.Add("--request-sha256"); start.ArgumentList.Add(hash);
            var process = Process.Start(start); return new(true, process is not null, false, process?.Id, process is null ? "Paired helper did not start." : null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return new(true, false, true, null, "UAC was cancelled."); }
        catch (Exception ex) { return new(true, false, false, null, ex.Message); }
    }
}

internal static class PimaxUsbPairedElevatedExecutor
{
    internal static IPimaxUsbPairedNativeAdapter NativeAdapter { get; set; } = new WindowsPimaxUsbPairedNativeAdapter();
    internal static IPimaxUsbPairedProgressWriter ProgressWriter { get; set; } = new PimaxUsbPairedProgressWriter();
    public static async Task<PimaxUsbPairedPrivilegedResult> ExecuteAsync(PimaxUsbPairedExperimentRequest request, CancellationToken cancellationToken)
    {
        PimaxUsbPairedPrivilegedRequest? envelope = null; var errors = new List<string>(); var elevated = IsElevated();
        try
        {
            if (!IsPermittedExecutionContext(Environment.ProcessPath, elevated)) throw new InvalidOperationException("Paired execution requires the dedicated elevated paired helper.");
            envelope = JsonSerializer.Deserialize<PimaxUsbPairedPrivilegedRequest>(File.ReadAllText(request.PrivilegedRequestPath!), PimaxUsbPairedExperimentJson.Options) ?? throw new InvalidDataException("Paired request is empty.");
            PimaxUsbPairedValidator.ValidateEnvelope(envelope, request.ExpectedRequestSha256!, DateTimeOffset.UtcNow);
            var collector = new WindowsPimaxUsbPortCycleStateCollector(SupervisorConfig.Load(null));
            async Task<bool> Revalidate(CancellationToken token)
            {
                var state = await collector.CollectAsync(envelope.Payload.TargetSignature, envelope.Payload.ObserverStatusPath, envelope.Payload.MarkerFilePath, token);
                var validation = PimaxUsbPairedValidator.Validate(envelope.Payload.TargetSignature, state, DateTimeOffset.UtcNow);
                if (!validation.Safety.Permitted || validation.Plan?.BindingSha256 != envelope.Payload.Plan.BindingSha256) return false;
                if (PimaxUsbPortCycleTargetValidator.Fingerprint(PimaxUsbPortCycleTargetValidator.StableObserver(validation.Plan.Observer)) != PimaxUsbPortCycleTargetValidator.Fingerprint(envelope.Payload.MarkerIdentity)) return false;
                if (PimaxUsbPortCycleTargetValidator.Fingerprint(PimaxUsbPairedMarkerReader.Read(envelope.Payload.MarkerFilePath)) != PimaxUsbPortCycleTargetValidator.Fingerprint(envelope.Payload.MarkerSequence)) return false;
                var confirmation = PimaxUsbPortCycleConfirmationToken.Validate(envelope.Payload.ConfirmationToken, envelope.Payload.ExperimentId, validation.Plan, DateTimeOffset.UtcNow, null, false);
                return confirmation.Accepted && confirmation.Nonce == envelope.Payload.Nonce && PimaxUsbPortCycleTargetValidator.IsMarkerFresh(validation.Plan.Observer, DateTimeOffset.UtcNow);
            }
            if (!await Revalidate(cancellationToken)) throw new InvalidOperationException("Paired helper initial prevalidation failed.");
            var result = await new PimaxUsbPairedNativeCoordinator(NativeAdapter, ProgressWriter).RunAsync(envelope.Payload, Revalidate, elevated, envelope.RequestSha256, cancellationToken);
            AtomicWrite(envelope.Payload.OutputResultPath, result); return result;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message); var id = envelope?.Payload.ExperimentId ?? "unknown";
            var emptySs = new PimaxUsbPairedSideResult("SuperSpeed", false, false, null, null, null, null, null, 0, null, null, null, false, null);
            var emptyUsb = emptySs with { Side = "USB2" };
            var result = new PimaxUsbPairedPrivilegedResult(PimaxUsbPairedExperimentSchema.Result, id, Environment.ProcessId, elevated, envelope?.RequestSha256,
                false, false, false, false, 0, null, null, emptySs, emptyUsb, 0, null, null, false, "zeroSubmitted", false, DateTimeOffset.UtcNow, false, [], errors.ToArray());
            if (envelope is not null) AtomicWrite(envelope.Payload.OutputResultPath, result); return result;
        }
    }
    internal static bool IsPermittedExecutionContext(string? path, bool elevated) => elevated && string.Equals(Path.GetFileNameWithoutExtension(path), "PimaxVrcSupervisor.PairedPortCycleHelper", StringComparison.OrdinalIgnoreCase);
    private static bool IsElevated() { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
    private static void AtomicWrite<T>(string path, T value) { var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temp = full + ".tmp-" + Guid.NewGuid().ToString("N"); File.WriteAllText(temp, JsonSerializer.Serialize(value, PimaxUsbPairedExperimentJson.Options), new UTF8Encoding(false)); File.Move(temp, full, false); }
}
