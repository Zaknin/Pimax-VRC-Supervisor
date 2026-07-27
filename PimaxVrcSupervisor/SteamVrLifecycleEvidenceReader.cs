using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;

internal sealed record SteamVrEvidenceReaderObservation(
    string Phase,
    int AvailableSourceCount,
    int UnavailableSourceCount,
    IReadOnlyList<string> SourceIdentities,
    IReadOnlyList<long> CurrentOffsets,
    bool TruncationDetected,
    bool RotationDetected,
    string? Marker,
    bool ShutdownRequested,
    bool RestartRequested,
    bool StaleMarkersCleared,
    string ParserHealth);

internal sealed record SteamVrEvidenceReadResult(
    string Content,
    string SourceIdentity,
    bool Available,
    long PreviousOffset,
    long CurrentOffset,
    long ReadOffset,
    bool Truncated,
    bool Rotated);

internal sealed class SteamVrLifecycleEvidenceReader
{
    internal const string ShutdownRequestedMarker = "SteamVRSystemState_ShutdownRequested";
    internal const string RestartStateMarker = "SteamVRSystemState_Restart";
    internal const string RestartHmdMarker = "vrmonitor://reboothmd";
    internal const string RestartSystemMarker = "vrmonitor://restartsystem/";
    internal const string RestartStartupReasonMarker = "-startupreason steamvr_restart";
    private const int MaximumBytesPerRead = 128 * 1024;
    private static readonly TimeSpan HealthSummaryInterval = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _sourceGenerations = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<string> _paths;
    private readonly Action<SteamVrEvidenceReaderObservation>? _observe;
    private bool _shutdownRequested;
    private bool _restartRequested;
    private string? _marker;
    private string? _lastObservationFingerprint;
    private DateTimeOffset _lastObservationAt;

    public SteamVrLifecycleEvidenceReader(
        IEnumerable<string>? paths = null,
        Action<SteamVrEvidenceReaderObservation>? observe = null)
    {
        _paths = (paths ?? DiscoverPaths()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _observe = observe;
    }

    public void EstablishBaseline()
    {
        _shutdownRequested = false;
        _restartRequested = false;
        _marker = null;
        var sourceIdentities = new List<string>();
        var offsets = new List<long>();
        var unavailable = 0;
        foreach (var path in _paths)
        {
            sourceIdentities.Add(SourceIdentity(path));
            try
            {
                var info = new FileInfo(path);
                var offset = info.Exists ? info.Length : 0;
                _offsets[path] = offset;
                _sourceGenerations[path] = info.Exists ? SourceGeneration(path, info) : 0;
                offsets.Add(offset);
                if (!info.Exists)
                {
                    unavailable++;
                }
            }
            catch
            {
                _offsets[path] = 0;
                offsets.Add(0);
                unavailable++;
            }
        }

        EmitObservation(new SteamVrEvidenceReaderObservation(
            "baseline",
            _paths.Count - unavailable,
            unavailable,
            sourceIdentities,
            offsets,
            TruncationDetected: false,
            RotationDetected: false,
            Marker: null,
            ShutdownRequested: false,
            RestartRequested: false,
            StaleMarkersCleared: true,
            ParserHealth: unavailable == _paths.Count ? "unavailable" : "healthy"));
    }

    public SteamVrLifecycleEvidence ReadCurrentSessionEvidence()
    {
        var results = _paths.Select(ReadAddedContent).ToArray();
        foreach (var read in results)
        {
            var content = read.Content;
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            if (content.Contains(RestartStateMarker, StringComparison.OrdinalIgnoreCase))
            {
                _restartRequested = true;
                _marker ??= RestartStateMarker;
            }
            else if (content.Contains(RestartHmdMarker, StringComparison.OrdinalIgnoreCase))
            {
                _restartRequested = true;
                _marker ??= RestartHmdMarker;
            }
            else if (content.Contains(RestartSystemMarker, StringComparison.OrdinalIgnoreCase))
            {
                _restartRequested = true;
                _marker ??= RestartSystemMarker;
            }
            else if (content.Contains(RestartStartupReasonMarker, StringComparison.OrdinalIgnoreCase))
            {
                _restartRequested = true;
                _marker ??= RestartStartupReasonMarker;
            }

            if (content.Contains(ShutdownRequestedMarker, StringComparison.OrdinalIgnoreCase))
            {
                _shutdownRequested = true;
                _marker ??= ShutdownRequestedMarker;
            }
        }

        var unavailable = results.Count(result => !result.Available);
        var truncated = results.Any(result => result.Truncated);
        var rotated = results.Any(result => result.Rotated);
        EmitObservation(new SteamVrEvidenceReaderObservation(
            "read",
            results.Length - unavailable,
            unavailable,
            results.Select(result => result.SourceIdentity).ToArray(),
            results.Select(result => result.CurrentOffset).ToArray(),
            TruncationDetected: truncated,
            RotationDetected: rotated,
            _marker,
            _shutdownRequested,
            _restartRequested,
            StaleMarkersCleared: false,
            ParserHealth: unavailable == results.Length ? "unavailable" : "healthy"));

        return new SteamVrLifecycleEvidence(_shutdownRequested, _restartRequested, _marker);
    }

    private SteamVrEvidenceReadResult ReadAddedContent(string path)
    {
        var sourceIdentity = SourceIdentity(path);
        _offsets.TryGetValue(path, out var previousOffset);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new SteamVrEvidenceReadResult("", sourceIdentity, false, previousOffset, previousOffset, previousOffset, false, false);
            }

            var currentGeneration = SourceGeneration(path, info);
            _sourceGenerations.TryGetValue(path, out var previousGeneration);
            var rotated = previousGeneration != 0 && previousGeneration != currentGeneration;
            var truncated = info.Length < previousOffset;
            // Rotation is diagnostics-only. Preserve the pre-Phase-34A parser contract:
            // only a shorter source resets the read offset and can alter lifecycle evidence.
            var resetToStart = truncated;
            var offset = resetToStart ? 0 : previousOffset;
            var readOffset = Math.Max(offset, info.Length - MaximumBytesPerRead);
            var length = checked((int)Math.Min(MaximumBytesPerRead, info.Length - readOffset));
            if (length <= 0)
            {
                _offsets[path] = info.Length;
                _sourceGenerations[path] = currentGeneration;
                return new SteamVrEvidenceReadResult("", sourceIdentity, true, previousOffset, info.Length, readOffset, truncated, rotated);
            }

            var bytes = new byte[length];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(readOffset, SeekOrigin.Begin);
            var bytesRead = stream.Read(bytes, 0, bytes.Length);
            _offsets[path] = info.Length;
            _sourceGenerations[path] = currentGeneration;
            return new SteamVrEvidenceReadResult(
                Encoding.UTF8.GetString(bytes, 0, bytesRead),
                sourceIdentity,
                true,
                previousOffset,
                info.Length,
                readOffset,
                truncated,
                rotated);
        }
        catch
        {
            return new SteamVrEvidenceReadResult("", sourceIdentity, false, previousOffset, previousOffset, previousOffset, false, false);
        }
    }

    private void EmitObservation(SteamVrEvidenceReaderObservation observation)
    {
        try
        {
            var fingerprint = string.Join("|",
                observation.Phase,
                observation.AvailableSourceCount,
                observation.UnavailableSourceCount,
                string.Join(",", observation.CurrentOffsets),
                observation.TruncationDetected,
                observation.Marker,
                observation.ShutdownRequested,
                observation.RestartRequested,
                observation.StaleMarkersCleared,
                observation.ParserHealth);
            var now = DateTimeOffset.UtcNow;
            if (fingerprint == _lastObservationFingerprint && now - _lastObservationAt < HealthSummaryInterval)
            {
                return;
            }

            _lastObservationFingerprint = fingerprint;
            _lastObservationAt = now;
            _observe?.Invoke(observation);
        }
        catch
        {
            // Evidence observation is diagnostic-only and must not change lifecycle decisions.
        }
    }

    private static string SourceIdentity(string path)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant()[..16];

    private static long SourceGeneration(string path, FileInfo fallback)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (GetFileInformationByHandle(stream.SafeFileHandle, out var information))
                {
                    return unchecked((long)(((ulong)information.FileIndexHigh << 32)
                        | information.FileIndexLow));
                }
            }
            catch
            {
                // Use the safe metadata fallback below when a source is transient or inaccessible.
            }
        }

        return fallback.CreationTimeUtc.Ticks;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private static IEnumerable<string> DiscoverPaths()
    {
        var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        var logs = Path.Combine(steamPath, "logs");
        foreach (var name in new[] { "vrmonitor.txt", "vrserver.txt", "vrmonitor.previous.txt", "vrserver.previous.txt" })
        {
            yield return Path.Combine(logs, name);
        }
    }
}