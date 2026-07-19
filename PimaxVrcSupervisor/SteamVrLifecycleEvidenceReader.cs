using Microsoft.Win32;

internal sealed class SteamVrLifecycleEvidenceReader
{
    internal const string ShutdownRequestedMarker = "SteamVRSystemState_ShutdownRequested";
    internal const string RestartStateMarker = "SteamVRSystemState_Restart";
    internal const string RestartHmdMarker = "vrmonitor://reboothmd";
    internal const string RestartSystemMarker = "vrmonitor://restartsystem/";
    internal const string RestartStartupReasonMarker = "-startupreason steamvr_restart";
    private const int MaximumBytesPerRead = 128 * 1024;
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<string> _paths;
    private bool _shutdownRequested;
    private bool _restartRequested;
    private string? _marker;

    public SteamVrLifecycleEvidenceReader(IEnumerable<string>? paths = null)
    {
        _paths = (paths ?? DiscoverPaths()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void EstablishBaseline()
    {
        _shutdownRequested = false;
        _restartRequested = false;
        _marker = null;
        foreach (var path in _paths)
        {
            try
            {
                _offsets[path] = new FileInfo(path).Exists ? new FileInfo(path).Length : 0;
            }
            catch
            {
                _offsets[path] = 0;
            }
        }
    }

    public SteamVrLifecycleEvidence ReadCurrentSessionEvidence()
    {
        foreach (var path in _paths)
        {
            var content = ReadAddedContent(path);
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

        return new SteamVrLifecycleEvidence(_shutdownRequested, _restartRequested, _marker);
    }

    private string ReadAddedContent(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return string.Empty;
            }

            _offsets.TryGetValue(path, out var offset);
            if (info.Length < offset)
            {
                offset = 0;
            }

            var readOffset = Math.Max(offset, info.Length - MaximumBytesPerRead);
            var length = checked((int)Math.Min(MaximumBytesPerRead, info.Length - readOffset));
            if (length <= 0)
            {
                _offsets[path] = info.Length;
                return string.Empty;
            }

            var bytes = new byte[length];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(readOffset, SeekOrigin.Begin);
            _ = stream.Read(bytes, 0, bytes.Length);
            _offsets[path] = info.Length;
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
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
