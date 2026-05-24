using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Sockets;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PimaxVrcSupervisor.SteamVrHost;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        ApplicationConfiguration.Initialize();
        using var shutdown = new CancellationTokenSource();
        using var host = new SteamVrDashboardHost();
        await host.RunAsync(shutdown.Token);
    }
}

internal sealed class SteamVrDashboardHost : IDisposable
{
    private const string HelperTaskName = "Pimax VRC Supervisor SteamVR Start";
    private const string CommandPipeName = "PimaxVrcSupervisor.Command";
    private const string ForcedManualReloadMarkerFileName = "PimaxVrcSupervisorForcedManualReload.marker";
    private const int CommandTcpPort = 37957;
    private const string OverlayKey = "pimax.vrcsupervisor.dashboard";
    private const string OverlayName = "Pimax VRC Supervisor";
    private const string OverlayIconRelativePath = @"Assets\vr-overlay-icon.png";
    private const int OverlayWidth = 1294;
    private const int OverlayHeight = 820;
    private static readonly TimeSpan OverlayFrameInterval = TimeSpan.FromMilliseconds(200);
    private const int ButtonTop = 154;
    private const int ButtonLeft = 70;
    private const int ButtonColumnGap = 28;
    private const int ButtonRowGap = 26;
    private const int ButtonWidth = 366;
    private const int ButtonHeight = 126;
    private const int ButtonRight = ButtonLeft + ButtonWidth + ButtonColumnGap;
    private const int ButtonThird = ButtonRight + ButtonWidth + ButtonColumnGap;
    private const int ButtonBottom = ButtonTop + ButtonHeight + ButtonRowGap;
    private const int ContentWidth = ButtonThird + ButtonWidth - ButtonLeft;
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), "PimaxVrcSupervisorSteamVrHost.log");
    private readonly object _renderLock = new();
    private readonly Queue<string> _surfacePaths = new();
    private int _surfaceGeneration;
    private readonly DashboardButton[] _buttons =
    [
        new("Restart VRC face tracking", "restart-core-apps", new Rectangle(ButtonLeft, ButtonTop, ButtonWidth, ButtonHeight)),
        new("Restart OSC router", "restart-osc-router", new Rectangle(ButtonRight, ButtonTop, ButtonWidth, ButtonHeight)),
        new("OSCGoesBrr", "start-osc-goes-brrr", new Rectangle(ButtonThird, ButtonTop, ButtonWidth, ButtonHeight)),
        new("Base stations on", "base-stations-on", new Rectangle(ButtonLeft, ButtonBottom, ButtonWidth, ButtonHeight)),
        new("Base stations off", "base-stations-off", new Rectangle(ButtonRight, ButtonBottom, ButtonWidth, ButtonHeight)),
        new("Restart Supervisor", "restart-supervisor", new Rectangle(ButtonThird, ButtonBottom, ButtonWidth, ButtonHeight))
    ];
    private OpenVrOverlaySession? _overlay;
    private GpuOverlayRenderer? _gpuRenderer;
    private string _status = "Starting supervisor...";
    private string[] _consoleLines = [];
    private string? _pressedCommand;
    private string? _runningCommand;
    private DateTimeOffset _lastCommandStartedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastOverlayRefreshAt = DateTimeOffset.MinValue;
    private OpenVrEventType? _lastEventType;
    private uint _overlayEventCount;
    private bool _commandInFlight;
    private bool _renderDirty = true;
    private bool _overlayActive = true;
    private bool _activeStateKnown;
    private bool _disposed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Log("Host starting from " + AppContext.BaseDirectory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log("Host process exiting.");
        await StartSupervisorAsync();

        try
        {
            _overlay = OpenVrOverlaySession.Open(OverlayKey, OverlayName);
            _overlay.SetOverlayWidthInMeters(2.5f);
            _overlay.SetOverlayMouseScale(OverlayWidth, OverlayHeight);
            _overlay.SetOverlayInputMethodMouse();
            _overlay.SetInteractiveDashboardFlags();
            TryInitializeGpuRenderer();
            RefreshOverlayTexture(force: true);
            var overlayIconPath = GetOverlayIconPath();
            if (File.Exists(overlayIconPath))
            {
                _overlay.SetThumbnailFromFile(overlayIconPath);
            }

            Log("Dashboard overlay created.");
            Log($"OpenVR event layout: size={Marshal.SizeOf<OpenVrEvent>()}; mouse=16,20; button=24; cursor=28.");
        }
        catch (Exception ex)
        {
            Log("Could not create overlay: " + ex);
            MessageBox.Show(ex.Message, "Could not create SteamVR dashboard overlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var lastStatusRefresh = DateTimeOffset.MinValue;
        var lastConsoleRefresh = DateTimeOffset.MinValue;
        while (!cancellationToken.IsCancellationRequested && IsSteamVrRunning())
        {
            var now = DateTimeOffset.UtcNow;
            var overlayActive = IsOverlayActive();
            if (!_activeStateKnown || overlayActive != _overlayActive)
            {
                _activeStateKnown = true;
                _overlayActive = overlayActive;
                Log("Dashboard overlay active=" + overlayActive);
                if (overlayActive)
                {
                    lastStatusRefresh = now;
                    lastConsoleRefresh = now;
                    MarkOverlayDirty();
                    _ = RefreshStatusAsync();
                    _ = RefreshConsoleAsync();
                }
                else
                {
                    MarkOverlayDirty();
                }
            }

            if (overlayActive)
            {
                ProcessOverlayEvents(cancellationToken);
                if (now - lastStatusRefresh > TimeSpan.FromSeconds(5))
                {
                    lastStatusRefresh = now;
                    if (!_commandInFlight)
                    {
                        _ = RefreshStatusAsync();
                    }
                }

                if (now - lastConsoleRefresh > TimeSpan.FromSeconds(2))
                {
                    lastConsoleRefresh = now;
                    _ = RefreshConsoleAsync();
                }

                RefreshOverlayTexture();
            }

            await Task.Delay(OverlayFrameInterval, cancellationToken);
        }

        Log($"Host loop exiting; cancellation={cancellationToken.IsCancellationRequested}; steamvr={IsSteamVrRunning()}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gpuRenderer?.Dispose();
        _overlay?.Dispose();
        while (_surfacePaths.TryDequeue(out var path))
        {
            TryDelete(path);
        }
    }

    private async Task<bool> StartSupervisorAsync(bool resetTaskState = false)
    {
        try
        {
            if (resetTaskState)
            {
                await TryEndHelperTaskAsync();
            }

            var taskPathIssue = await global::ScheduledTaskPathValidator.ValidateExistingTaskAsync(
                HelperTaskName,
                global::ScheduledTaskPathValidator.GetCurrentExecutableDirectory(),
                CancellationToken.None);
            if (taskPathIssue is not null)
            {
                throw new InvalidOperationException(global::ScheduledTaskPathValidator.FormatIssue(taskPathIssue));
            }

            Log("Requesting elevated supervisor via scheduled task.");
            await RunProcessAsync("schtasks.exe", ["/Run", "/TN", HelperTaskName], TimeSpan.FromSeconds(15));
            SetStatus("Supervisor start requested.");
            if (await WaitForSupervisorCommandBridgeAsync(TimeSpan.FromSeconds(20)))
            {
                return true;
            }

            Log("Supervisor command bridge did not become ready after scheduled task run; retrying once.");
            await TryEndHelperTaskAsync();
            await RunProcessAsync("schtasks.exe", ["/Run", "/TN", HelperTaskName], TimeSpan.FromSeconds(15));
            SetStatus("Supervisor start requested again.");
            return await WaitForSupervisorCommandBridgeAsync(TimeSpan.FromSeconds(20));
        }
        catch (Exception ex)
        {
            Log("Could not start elevated supervisor: " + ex);
            SetStatus("Could not start elevated supervisor: " + ex.Message);
            return false;
        }
    }

    private async Task<string> RestartSupervisorAsync()
    {
        var supervisorProcesses = GetSupervisorProcesses();
        if (supervisorProcesses.Length == 0)
        {
            Log("Supervisor restart requested; no supervisor process is running.");
            var started = await StartSupervisorAsync(resetTaskState: true);
            return started
                ? "Supervisor was not running. Startup completed."
                : "Supervisor was not running. Startup requested, but the command bridge is not ready.";
        }

        WriteForcedManualReloadMarker();
        var hardStopRequested = false;
        try
        {
            Log("Requesting elevated supervisor hard stop over command bridge.");
            var response = await SendCommandAsync("force-stop-supervisor", TimeSpan.FromSeconds(2));
            Log("Supervisor hard stop response: " + response);
            hardStopRequested = true;
        }
        catch (Exception ex)
        {
            Log("Supervisor hard stop command failed; falling back to direct process kill: " + ex.Message);
        }

        if (!hardStopRequested)
        {
            foreach (var process in supervisorProcesses)
            {
                try
                {
                    Log($"Killing supervisor pid={process.Id}.");
                    process.Kill(entireProcessTree: false);
                }
                catch (Exception ex)
                {
                    Log($"Could not kill supervisor pid={TryGetProcessId(process)}: {ex.Message}");
                }
            }
        }

        foreach (var process in supervisorProcesses)
        {
            try
            {
                using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeoutSource.Token);
                Log($"Supervisor pid={process.Id} exited.");
            }
            catch (OperationCanceledException)
            {
                Log($"Supervisor pid={TryGetProcessId(process)} did not exit within timeout.");
            }
            catch (Exception ex)
            {
                Log($"Could not wait for supervisor pid={TryGetProcessId(process)}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        var restartSucceeded = await StartSupervisorAsync(resetTaskState: true);
        return restartSucceeded
            ? "Supervisor hard restart completed."
            : "Supervisor hard restart requested, but the command bridge is not ready.";
    }

    private async Task TryEndHelperTaskAsync()
    {
        try
        {
            Log("Ending helper scheduled task state before restart.");
            await RunProcessAsync("schtasks.exe", ["/End", "/TN", HelperTaskName], TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            Log("Could not end helper scheduled task state: " + ex.Message);
        }
    }

    private static string ForcedManualReloadMarkerPath
        => Path.Combine(Path.GetTempPath(), ForcedManualReloadMarkerFileName);

    private void WriteForcedManualReloadMarker()
    {
        try
        {
            File.WriteAllText(ForcedManualReloadMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            Log("Could not write forced manual reload marker: " + ex.Message);
        }
    }

    private async Task<bool> WaitForSupervisorCommandBridgeAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                SetStatus(await SendCommandAsync("status", TimeSpan.FromSeconds(1)));
                return true;
            }
            catch (Exception ex)
            {
                Log("Supervisor command bridge is not ready yet: " + ex.Message);
                SetStatus("Waiting for elevated supervisor command bridge...");
                await Task.Delay(500);
            }
        }

        Log("Timed out waiting for elevated supervisor command bridge.");
        SetStatus("Waiting for elevated supervisor command bridge...");
        return false;
    }

    private static Process[] GetSupervisorProcesses()
    {
        var currentProcessId = Environment.ProcessId;
        return Process.GetProcessesByName("PimaxVrcSupervisor")
            .Where(process => process.Id != currentProcessId)
            .ToArray();
    }

    private static string TryGetProcessId(Process process)
    {
        try
        {
            return process.Id.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            SetStatus(await SendCommandAsync("status", TimeSpan.FromSeconds(2)));
        }
        catch
        {
            if (_status.StartsWith("Could not start", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SetStatus("Waiting for elevated supervisor command bridge...");
        }
    }

    private async Task RefreshConsoleAsync()
    {
        try
        {
            var response = await SendCommandPipeFirstAsync("log", TimeSpan.FromMilliseconds(700));
            var lines = JsonSerializer.Deserialize<string[]>(response) ?? [];
            SetConsoleLines(lines);
        }
        catch (Exception ex)
        {
            Log("Console refresh failed: " + ex.Message);
        }
    }

    private void ProcessOverlayEvents(CancellationToken cancellationToken)
    {
        if (_overlay is null)
        {
            return;
        }

        while (_overlay.PollNextOverlayEvent(out var vrEvent))
        {
            _overlayEventCount++;
            _lastEventType = vrEvent.EventType;
            var pointer = GetMousePointer(vrEvent, DateTimeOffset.UtcNow);
            if (vrEvent.EventType == OpenVrEventType.MouseMove)
            {
                // Hover highlighting is intentionally disabled; clicks are handled by MouseButtonDown.
            }
            else if (vrEvent.EventType == OpenVrEventType.TouchPadMove)
            {
                // TouchPadMove uses a different event payload and is not needed without hover highlighting.
            }
            else if (vrEvent.EventType == OpenVrEventType.ButtonPress)
            {
                Log($"ButtonPress ignored; cursor={pointer.CursorIndex}; raw={pointer.Raw.X:0.0},{pointer.Raw.Y:0.0}; dashboard mouse clicks are handled by MouseButtonDown.");
            }
            else if (vrEvent.EventType == OpenVrEventType.ButtonUnpress)
            {
                Log($"ButtonUnpress; pressed={_pressedCommand ?? "none"}");
                _pressedCommand = null;
            }
            else if (vrEvent.EventType == OpenVrEventType.MouseButtonDown)
            {
                var resolution = ResolveClickButton(pointer);
                LogPointerEvent("MouseButtonDown", pointer, resolution);
                _pressedCommand = resolution.Button?.Command;
                if (resolution.Button is not null)
                {
                    TryStartButtonCommand(resolution.Button, cancellationToken);
                }
                else
                {
                    Log("Click ignored: " + resolution.MissReason);
                }
            }
            else if (vrEvent.EventType == OpenVrEventType.MouseButtonUp)
            {
                LogPointerEvent("MouseButtonUp", pointer, new ClickResolution(null, pointer, "event", "button-up"));
                _pressedCommand = null;
            }
            else if (vrEvent.EventType == OpenVrEventType.MouseFocusLeave)
            {
                _pressedCommand = null;
            }
            else if (vrEvent.EventType == OpenVrEventType.OverlayClosed)
            {
                Log("OverlayClosed event received.");
                Environment.ExitCode = 0;
                return;
            }
        }
    }

    private static OverlayPointer GetMousePointer(OpenVrEvent vrEvent, DateTimeOffset timestamp)
    {
        var raw = new PointF(vrEvent.MouseX, vrEvent.MouseY);
        var layout = new PointF(vrEvent.MouseX, OverlayHeight - vrEvent.MouseY);
        return new OverlayPointer(
            vrEvent.CursorIndex,
            raw,
            layout,
            timestamp);
    }

    private bool IsOverlayActive()
    {
        try
        {
            return _overlay?.IsActiveDashboardOverlay() ?? true;
        }
        catch (Exception ex)
        {
            Log("Could not query dashboard active state; assuming active: " + ex.Message);
            return true;
        }
    }

    private ClickResolution ResolveClickButton(OverlayPointer eventPointer)
    {
        if (!IsValidOverlayPoint(eventPointer.Raw))
        {
            return new ClickResolution(null, eventPointer, "event", GetPointerInvalidReason(eventPointer));
        }

        var button = HitTestLayout(eventPointer.Layout);
        return new ClickResolution(
            button,
            eventPointer,
            "event",
            button is null ? GetNoHitReason(eventPointer) : "none");
    }

    private static bool IsTrackablePointer(OverlayPointer pointer)
        => IsValidOverlayPoint(pointer.Raw);

    private static bool IsValidOverlayPoint(PointF point)
        => HasFiniteCoordinates(point)
            && point.X >= 0
            && point.X < OverlayWidth
            && point.Y >= 0
            && point.Y < OverlayHeight;

    private static bool HasFiniteCoordinates(PointF point)
        => float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static string GetPointerInvalidReason(OverlayPointer pointer)
    {
        if (!HasFiniteCoordinates(pointer.Raw))
        {
            return "invalid coordinate";
        }

        if (pointer.Raw.X < 0 || pointer.Raw.X >= OverlayWidth)
        {
            return "invalid-x";
        }

        if (pointer.Raw.Y < 0 || pointer.Raw.Y >= OverlayHeight)
        {
            return "invalid-y";
        }

        return "outside overlay";
    }

    private static string GetNoHitReason(OverlayPointer pointer)
        => pointer.Layout.X < 0
            || pointer.Layout.X >= OverlayWidth
            || pointer.Layout.Y < 0
            || pointer.Layout.Y >= OverlayHeight
                ? "outside overlay"
                : "gap/margin click";

    private void LogPointerEvent(string eventName, OverlayPointer pointer, ClickResolution resolution)
        => Log(
            $"{eventName}; cursor={pointer.CursorIndex}; raw={pointer.Raw.X:0.0},{pointer.Raw.Y:0.0}; layout={pointer.Layout.X:0.0},{pointer.Layout.Y:0.0}; source={resolution.Source}; resolved={resolution.Button?.Command ?? "none"}; miss={resolution.MissReason}; pressed={_pressedCommand ?? "none"}");

    private DashboardButton? FindButtonByCommand(string? command)
        => string.IsNullOrWhiteSpace(command)
            ? null
            : _buttons.FirstOrDefault(candidate => string.Equals(candidate.Command, command, StringComparison.Ordinal));

    private void TryStartButtonCommand(DashboardButton button, CancellationToken cancellationToken)
    {
        if (_commandInFlight || DateTimeOffset.UtcNow - _lastCommandStartedAt < TimeSpan.FromMilliseconds(250))
        {
            Log($"Command ignored for {button.Command}; inFlight={_commandInFlight}");
            SetStatus("Already running a command...");
            return;
        }

        _commandInFlight = true;
        _runningCommand = button.Command;
        _lastCommandStartedAt = DateTimeOffset.UtcNow;
        Log("Command queued: " + button.Command);
        SetStatus("Clicked: " + button.Label);
        _ = ExecuteButtonAsync(button, cancellationToken);
    }

    private async Task ExecuteButtonAsync(DashboardButton button, CancellationToken cancellationToken)
    {
        try
        {
            Log("Command starting: " + button.Command);
            SetStatus("Running " + button.Label + "...");
            var response = string.Equals(button.Command, "restart-supervisor", StringComparison.Ordinal)
                ? await RestartSupervisorAsync()
                : await SendCommandAsync(button.Command, TimeSpan.FromSeconds(45));
            Log("Command response: " + response);
            SetStatus(response);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Log("Command failed: " + ex);
            SetStatus("Command failed: " + ex.Message);
        }
        finally
        {
            Log("Command finished: " + button.Command);
            _runningCommand = null;
            _commandInFlight = false;
            MarkOverlayDirty();
        }
    }

    private DashboardButton? HitTestLayout(PointF layoutPosition)
        => _buttons.FirstOrDefault(button => Contains(button.Bounds, layoutPosition));

    private static bool Contains(Rectangle bounds, PointF point)
        => point.X >= bounds.Left
            && point.X < bounds.Right
            && point.Y >= bounds.Top
            && point.Y < bounds.Bottom;

    private void SetStatus(string status)
    {
        if (string.Equals(_status, status, StringComparison.Ordinal))
        {
            return;
        }

        _status = status;
        MarkOverlayDirty();
    }

    private void SetConsoleLines(string[] lines)
    {
        if (_consoleLines.SequenceEqual(lines, StringComparer.Ordinal))
        {
            return;
        }

        _consoleLines = lines;
        MarkOverlayDirty();
    }

    private void MarkOverlayDirty()
    {
        _renderDirty = true;
    }

    private void TryInitializeGpuRenderer()
    {
        try
        {
            _gpuRenderer = new GpuOverlayRenderer(OverlayWidth, OverlayHeight);
            Log($"D3D11 overlay renderer ready; featureLevel={_gpuRenderer.FeatureLevel}.");
        }
        catch (Exception ex)
        {
            Log("D3D11 overlay renderer unavailable; static PNG fallback will be used: " + ex);
            _gpuRenderer?.Dispose();
            _gpuRenderer = null;
        }
    }

    private void RefreshOverlayTexture(bool force = false)
    {
        if (_overlay is null || (!force && (!_overlayActive || !_renderDirty)))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastOverlayRefreshAt < OverlayFrameInterval)
        {
            return;
        }

        if (_gpuRenderer is null && !force)
        {
            return;
        }

        lock (_renderLock)
        {
            try
            {
                using var bitmap = RenderOverlay();
                if (_gpuRenderer is not null)
                {
                    try
                    {
                        _gpuRenderer.Update(bitmap);
                        _overlay.SetOverlayTexture(_gpuRenderer.TexturePointer);
                    }
                    catch (Exception ex)
                    {
                        Log("D3D11 overlay refresh failed; switching to static PNG fallback: " + ex);
                        _gpuRenderer.Dispose();
                        _gpuRenderer = null;
                        var surfacePath = SaveOverlaySurface(bitmap);
                        _overlay.SetOverlayFromFile(surfacePath);
                    }
                }
                else
                {
                    var surfacePath = SaveOverlaySurface(bitmap);
                    _overlay.SetOverlayFromFile(surfacePath);
                }

                _renderDirty = false;
                _lastOverlayRefreshAt = now;
            }
            catch (Exception ex)
            {
                Log("Render refresh failed: " + ex);
            }
        }
    }

    private Bitmap RenderOverlay()
    {
        var bitmap = new Bitmap(OverlayWidth, OverlayHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.FromArgb(24, 26, 32));

        using var titleFont = new Font(FontFamily.GenericSansSerif, 34, FontStyle.Bold);
        using var buttonFont = new Font(FontFamily.GenericSansSerif, 23, FontStyle.Regular);
        using var panelTitleFont = new Font(FontFamily.GenericSansSerif, 15, FontStyle.Bold);
        using var statusFont = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Regular);
        using var consoleFont = new Font(FontFamily.GenericMonospace, 13, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.White);
        using var statusBrush = new SolidBrush(Color.FromArgb(210, 215, 225));
        using var mutedBrush = new SolidBrush(Color.FromArgb(165, 174, 190));
        using var accentBrush = new SolidBrush(Color.FromArgb(45, 128, 255));
        using var normalBrush = new SolidBrush(Color.FromArgb(48, 52, 62));
        using var panelBrush = new SolidBrush(Color.FromArgb(18, 20, 25));
        using var runningBrush = new SolidBrush(Color.FromArgb(63, 75, 96));

        graphics.FillRectangle(accentBrush, 0, 0, OverlayWidth, 8);
        DrawOverlayIcon(graphics, new Rectangle(68, 42, 76, 76));
        graphics.DrawString("Pimax VRC Supervisor", titleFont, titleBrush, 164, 48);
        graphics.DrawString("SteamVR dashboard controls", statusFont, statusBrush, 168, 98);

        foreach (var button in _buttons)
        {
            var running = string.Equals(_runningCommand, button.Command, StringComparison.Ordinal);
            using var path = RoundedRect(button.Bounds, 12);
            graphics.FillPath(running ? runningBrush : normalBrush, path);
            using var activeBorderPen = new Pen(
                running ? Color.FromArgb(45, 128, 255) : Color.FromArgb(110, 126, 150),
                running ? 4 : 2);
            graphics.DrawPath(activeBorderPen, path);
            DrawCenteredText(graphics, running ? "Running..." : button.Label, buttonFont, titleBrush, button.Bounds);
        }

        var statusBounds = new Rectangle(ButtonLeft, 458, ContentWidth, 42);
        using var statusFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisWord
        };
        graphics.DrawString(TrimStatus(BuildStatusLine()), statusFont, statusBrush, statusBounds, statusFormat);
        DrawConsolePanel(graphics, panelTitleFont, consoleFont, titleBrush, statusBrush, mutedBrush, panelBrush);
        return bitmap;
    }

    private void DrawConsolePanel(
        Graphics graphics,
        Font titleFont,
        Font consoleFont,
        Brush titleBrush,
        Brush textBrush,
        Brush mutedBrush,
        Brush panelBrush)
    {
        var bounds = new Rectangle(ButtonLeft, 505, ContentWidth, 260);
        using var path = RoundedRect(bounds, 10);
        graphics.FillPath(panelBrush, path);
        using var borderPen = new Pen(Color.FromArgb(82, 96, 118), 2);
        graphics.DrawPath(borderPen, path);
        graphics.DrawString("Supervisor output", titleFont, titleBrush, bounds.Left + 18, bounds.Top + 14);

        var lines = BuildConsoleDisplayLines(_consoleLines);
        if (lines.Length == 0)
        {
            lines = ["Waiting for supervisor output..."];
        }

        var y = bounds.Top + 48;
        var lineHeight = 18;
        var textBounds = new Rectangle(bounds.Left + 18, y, bounds.Width - 36, bounds.Height - 62);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        foreach (var line in lines.Take(textBounds.Height / lineHeight))
        {
            var brush = line.StartsWith("...", StringComparison.Ordinal) ? mutedBrush : textBrush;
            graphics.DrawString(line, consoleFont, brush, new Rectangle(textBounds.Left, y, textBounds.Width, lineHeight + 4), format);
            y += lineHeight;
        }
    }

    private static string[] BuildConsoleDisplayLines(string[] sourceLines)
    {
        const int maxChars = 142;
        const int maxLines = 11;
        var wrapped = new List<string>();
        foreach (var line in sourceLines)
        {
            var remaining = line.Trim();
            if (remaining.Length == 0)
            {
                continue;
            }

            while (remaining.Length > maxChars)
            {
                wrapped.Add(remaining[..maxChars]);
                remaining = "..." + remaining[maxChars..].TrimStart();
            }

            wrapped.Add(remaining);
        }

        return wrapped
            .TakeLast(maxLines)
            .ToArray();
    }

    private string SaveOverlaySurface(Bitmap bitmap)
    {
        var surfacePath = GetNextSurfacePath();
        bitmap.Save(surfacePath, ImageFormat.Png);
        TrackSurfacePath(surfacePath);
        return surfacePath;
    }

    private string GetNextSurfacePath()
        => Path.Combine(
            Path.GetTempPath(),
            $"PimaxVrcSupervisorSteamVrOverlay_{Environment.ProcessId}_{Interlocked.Increment(ref _surfaceGeneration):000000}.png");

    private void TrackSurfacePath(string surfacePath)
    {
        _surfacePaths.Enqueue(surfacePath);
        while (_surfacePaths.Count > 8)
        {
            TryDelete(_surfacePaths.Dequeue());
        }
    }

    private string BuildStatusLine()
        => FormatStatusForOverlay(_status);

    private static string FormatStatusForOverlay(string status)
    {
        if (!status.StartsWith("Mode=", StringComparison.OrdinalIgnoreCase))
        {
            return status;
        }

        var values = status
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        var coreApps = values.TryGetValue("CoreApps", out var coreAppsValue) ? coreAppsValue : "unknown";
        var oscRouter = values.TryGetValue("OscRouter", out var oscRouterValue) ? oscRouterValue : "unknown";
        var baseStations = values.TryGetValue("BaseStations", out var baseStationsValue) ? baseStationsValue : "unknown";
        return $"Ready. Core apps: {coreApps}. OSC router: {oscRouter}. Base stations: {baseStations}.";
    }

    private static void DrawOverlayIcon(Graphics graphics, Rectangle bounds)
    {
        var overlayIconPath = GetOverlayIconPath();
        if (!File.Exists(overlayIconPath))
        {
            return;
        }

        try
        {
            using var image = Image.FromFile(overlayIconPath);
            DrawImageContained(graphics, image, bounds);
        }
        catch
        {
        }
    }

    private static string GetOverlayIconPath()
        => Path.Combine(AppContext.BaseDirectory, OverlayIconRelativePath);

    private static void DrawImageContained(Graphics graphics, Image image, Rectangle bounds)
    {
        var scale = Math.Min(bounds.Width / (float)image.Width, bounds.Height / (float)image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var x = bounds.Left + (bounds.Width - width) / 2;
        var y = bounds.Top + (bounds.Height - height) / 2;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(image, new Rectangle(x, y, width, height));
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawCenteredText(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static string TrimStatus(string status)
        => status.Length <= 120 ? status : status[..117] + "...";

    private static bool IsSteamVrRunning()
    {
        var processes = Process.GetProcessesByName("vrserver");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    private async Task<string> SendCommandAsync(string command, TimeSpan timeout)
    {
        try
        {
            Log("Sending TCP command: " + command);
            var response = await SendTcpCommandAsync(command, timeout);
            Log("TCP command completed: " + command);
            return response;
        }
        catch (Exception tcpEx)
        {
            Log("TCP command failed, trying pipe: " + tcpEx.Message);
            var response = await SendPipeCommandAsync(command, timeout);
            Log("Pipe command completed: " + command);
            return response;
        }
    }

    private async Task<string> SendCommandPipeFirstAsync(string command, TimeSpan timeout)
    {
        try
        {
            return await SendPipeCommandAsync(command, timeout);
        }
        catch
        {
            return await SendTcpCommandAsync(command, timeout);
        }
    }

    private static async Task<string> SendTcpCommandAsync(string command, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", CommandTcpPort, timeoutSource.Token);
        await using var stream = client.GetStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await writer.WriteLineAsync(command.AsMemory(), timeoutSource.Token);
        return await reader.ReadLineAsync(timeoutSource.Token) ?? "No response.";
    }

    private static async Task<string> SendPipeCommandAsync(string command, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        await using var pipe = new NamedPipeClientStream(".", CommandPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutSource.Token);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await writer.WriteLineAsync(command.AsMemory(), timeoutSource.Token);
        return await reader.ReadLineAsync(timeoutSource.Token) ?? "No response.";
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(
                _logPath,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static async Task RunProcessAsync(string fileName, string[] arguments, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        await process.WaitForExitAsync(timeoutSource.Token);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}: {error}{output}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

internal sealed record DashboardButton(string Label, string Command, Rectangle Bounds);

internal readonly record struct OverlayPointer(uint CursorIndex, PointF Raw, PointF Layout, DateTimeOffset Timestamp);

internal readonly record struct ClickResolution(DashboardButton? Button, OverlayPointer Pointer, string Source, string MissReason);

internal sealed class GpuOverlayRenderer : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _rowPitch;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _texture;
    private bool _disposed;

    public GpuOverlayRenderer(int width, int height)
    {
        _width = width;
        _height = height;
        _rowPitch = width * 4;
        _device = D3D11.D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0);
        FeatureLevel = _device.FeatureLevel;
        _context = _device.ImmediateContext;

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };
        _texture = _device.CreateTexture2D(description);
    }

    public FeatureLevel FeatureLevel { get; }

    public IntPtr TexturePointer => _texture.NativePointer;

    public void Update(Bitmap bitmap)
    {
        if (bitmap.Width != _width || bitmap.Height != _height)
        {
            throw new ArgumentException("Bitmap size does not match the overlay texture.", nameof(bitmap));
        }

        var pixels = CopyBitmapPixels(bitmap);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            _context.UpdateSubresource(
                _texture,
                0,
                null,
                handle.AddrOfPinnedObject(),
                (uint)_rowPitch,
                (uint)(pixels.Length));
            _context.Flush();
        }
        finally
        {
            handle.Free();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _texture.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    private byte[] CopyBitmapPixels(Bitmap bitmap)
    {
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[_rowPitch * _height];
            for (var y = 0; y < _height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * _rowPitch, _rowPitch);
            }

            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}

internal enum OpenVrEventType : uint
{
    ButtonPress = 200,
    ButtonUnpress = 201,
    MouseMove = 300,
    MouseButtonDown = 301,
    MouseButtonUp = 302,
    MouseFocusEnter = 303,
    MouseFocusLeave = 304,
    TouchPadMove = 306,
    OverlayClosed = 534
}

[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct OpenVrEvent
{
    [FieldOffset(0)]
    public OpenVrEventType EventType;
    [FieldOffset(4)]
    public uint TrackedDeviceIndex;
    [FieldOffset(8)]
    public float EventAgeSeconds;
    [FieldOffset(16)]
    public float MouseX;
    [FieldOffset(20)]
    public float MouseY;
    [FieldOffset(24)]
    public uint MouseButton;
    [FieldOffset(28)]
    public uint CursorIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal struct OpenVrVector2
{
    public float X;
    public float Y;
}

internal enum OpenVrTextureType
{
    Invalid = -1,
    DirectX = 0,
    OpenGl = 1,
    Vulkan = 2,
    IoSurface = 3,
    DirectX12 = 4,
    DxgiSharedHandle = 5,
    Metal = 6
}

internal enum OpenVrColorSpace
{
    Auto = 0,
    Gamma = 1,
    Linear = 2
}

[StructLayout(LayoutKind.Sequential)]
internal struct OpenVrTexture
{
    public IntPtr Handle;
    public OpenVrTextureType EType;
    public OpenVrColorSpace EColorSpace;
}

internal sealed class OpenVrOverlaySession : IDisposable
{
    private const string OpenVrOverlayFnTableVersion = "FnTable:IVROverlay_028";
    private const int VrApplicationOverlay = 2;
    private const int VrInitErrorNone = 0;
    private const int VrOverlayErrorNone = 0;

    private readonly IntPtr _library;
    private readonly VrShutdownInternalDelegate _shutdownInternal;
    private readonly GetOverlayErrorNameFromEnumDelegate _getOverlayErrorNameFromEnum;
    private readonly DestroyOverlayDelegate _destroyOverlay;
    private readonly SetOverlayWidthInMetersDelegate _setOverlayWidthInMeters;
    private readonly SetOverlayFlagDelegate _setOverlayFlag;
    private readonly PollNextOverlayEventDelegate _pollNextOverlayEvent;
    private readonly SetOverlayInputMethodDelegate _setOverlayInputMethod;
    private readonly SetOverlayMouseScaleDelegate _setOverlayMouseScale;
    private readonly SetOverlayTextureDelegate _setOverlayTexture;
    private readonly SetOverlayRawDelegate _setOverlayRaw;
    private readonly SetOverlayFromFileDelegate _setOverlayFromFile;
    private readonly CreateDashboardOverlayDelegate _createDashboardOverlay;
    private readonly IsActiveDashboardOverlayDelegate _isActiveDashboardOverlay;
    private bool _initialized = true;
    private ulong _mainHandle;
    private ulong _thumbnailHandle;

    private OpenVrOverlaySession(
        IntPtr library,
        VrShutdownInternalDelegate shutdownInternal,
        OpenVrOverlayFnTable table,
        ulong mainHandle,
        ulong thumbnailHandle)
    {
        _library = library;
        _shutdownInternal = shutdownInternal;
        _getOverlayErrorNameFromEnum = CreateDelegate<GetOverlayErrorNameFromEnumDelegate>(table.GetOverlayErrorNameFromEnum);
        _destroyOverlay = CreateDelegate<DestroyOverlayDelegate>(table.DestroyOverlay);
        _setOverlayWidthInMeters = CreateDelegate<SetOverlayWidthInMetersDelegate>(table.SetOverlayWidthInMeters);
        _setOverlayFlag = CreateDelegate<SetOverlayFlagDelegate>(table.SetOverlayFlag);
        _pollNextOverlayEvent = CreateDelegate<PollNextOverlayEventDelegate>(table.PollNextOverlayEvent);
        _setOverlayInputMethod = CreateDelegate<SetOverlayInputMethodDelegate>(table.SetOverlayInputMethod);
        _setOverlayMouseScale = CreateDelegate<SetOverlayMouseScaleDelegate>(table.SetOverlayMouseScale);
        _setOverlayTexture = CreateDelegate<SetOverlayTextureDelegate>(table.SetOverlayTexture);
        _setOverlayRaw = CreateDelegate<SetOverlayRawDelegate>(table.SetOverlayRaw);
        _setOverlayFromFile = CreateDelegate<SetOverlayFromFileDelegate>(table.SetOverlayFromFile);
        _createDashboardOverlay = CreateDelegate<CreateDashboardOverlayDelegate>(table.CreateDashboardOverlay);
        _isActiveDashboardOverlay = CreateDelegate<IsActiveDashboardOverlayDelegate>(table.IsActiveDashboardOverlay);
        _mainHandle = mainHandle;
        _thumbnailHandle = thumbnailHandle;
    }

    public static OpenVrOverlaySession Open(string overlayKey, string overlayName)
    {
        if (!TryFindOpenVrApiDll(out var openVrApiDllPath, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var library = NativeLibrary.Load(openVrApiDllPath);
        try
        {
            var initInternal = GetExportDelegate<VrInitInternalDelegate>(library, "VR_InitInternal");
            var shutdownInternal = GetExportDelegate<VrShutdownInternalDelegate>(library, "VR_ShutdownInternal");
            var getGenericInterface = GetExportDelegate<VrGetGenericInterfaceDelegate>(library, "VR_GetGenericInterface");
            var getInitErrorDescription = GetOptionalExportDelegate<VrGetVrInitErrorAsEnglishDescriptionDelegate>(library, "VR_GetVRInitErrorAsEnglishDescription");

            var initError = 0;
            _ = initInternal(ref initError, VrApplicationOverlay);
            if (initError != VrInitErrorNone)
            {
                throw new InvalidOperationException($"OpenVR overlay init failed: {DescribeOpenVrInitError(initError, getInitErrorDescription)}");
            }

            var interfaceError = 0;
            var overlayTablePointer = getGenericInterface(OpenVrOverlayFnTableVersion, ref interfaceError);
            if (overlayTablePointer == IntPtr.Zero || interfaceError != VrInitErrorNone)
            {
                shutdownInternal();
                throw new InvalidOperationException($"OpenVR overlay interface unavailable: {DescribeOpenVrInitError(interfaceError, getInitErrorDescription)}");
            }

            var table = Marshal.PtrToStructure<OpenVrOverlayFnTable>(overlayTablePointer);
            var createDashboardOverlay = CreateDelegate<CreateDashboardOverlayDelegate>(table.CreateDashboardOverlay);
            ulong mainHandle = 0;
            ulong thumbnailHandle = 0;
            var overlayError = createDashboardOverlay(overlayKey, overlayName, ref mainHandle, ref thumbnailHandle);
            var session = new OpenVrOverlaySession(library, shutdownInternal, table, mainHandle, thumbnailHandle);
            session.ThrowIfOverlayError(overlayError, "CreateDashboardOverlay");
            return session;
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
    }

    public void SetOverlayWidthInMeters(float width)
        => ThrowIfOverlayError(_setOverlayWidthInMeters(_mainHandle, width), "SetOverlayWidthInMeters");

    public void SetOverlayInputMethodMouse()
        => ThrowIfOverlayError(_setOverlayInputMethod(_mainHandle, 1), "SetOverlayInputMethod");

    public void SetInteractiveDashboardFlags()
    {
        const int visibleInDashboard = 1 << 15;
        const int makeInteractiveIfVisible = 1 << 16;
        const int clickStabilization = 1 << 27;
        ThrowIfOverlayError(_setOverlayFlag(_mainHandle, visibleInDashboard, true), "SetOverlayFlag VisibleInDashboard");
        ThrowIfOverlayError(_setOverlayFlag(_mainHandle, makeInteractiveIfVisible, true), "SetOverlayFlag MakeOverlaysInteractiveIfVisible");
        TrySetOverlayFlag(clickStabilization, true);
    }

    private void TrySetOverlayFlag(int flag, bool enabled)
    {
        try
        {
            _ = _setOverlayFlag(_mainHandle, flag, enabled);
        }
        catch
        {
            // Optional OpenVR flags vary by runtime version.
        }
    }

    public void SetOverlayMouseScale(float width, float height)
    {
        var scale = new OpenVrVector2 { X = width, Y = height };
        ThrowIfOverlayError(_setOverlayMouseScale(_mainHandle, ref scale), "SetOverlayMouseScale");
    }

    public void SetOverlayFromFile(string path)
        => ThrowIfOverlayError(_setOverlayFromFile(_mainHandle, path), "SetOverlayFromFile");

    public void SetOverlayTexture(IntPtr textureHandle)
    {
        var texture = new OpenVrTexture
        {
            Handle = textureHandle,
            EType = OpenVrTextureType.DirectX,
            EColorSpace = OpenVrColorSpace.Auto
        };
        ThrowIfOverlayError(_setOverlayTexture(_mainHandle, ref texture), "SetOverlayTexture");
    }

    public void SetOverlayRaw(Bitmap bitmap)
    {
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            const int bytesPerPixel = 4;
            var rowBytes = bitmap.Width * bytesPerPixel;
            var rgba = new byte[rowBytes * bitmap.Height];
            var bgra = new byte[rowBytes];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bgra, 0, rowBytes);
                var rowOffset = y * rowBytes;
                for (var x = 0; x < rowBytes; x += bytesPerPixel)
                {
                    rgba[rowOffset + x] = bgra[x + 2];
                    rgba[rowOffset + x + 1] = bgra[x + 1];
                    rgba[rowOffset + x + 2] = bgra[x];
                    rgba[rowOffset + x + 3] = bgra[x + 3];
                }
            }

            var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
            try
            {
                ThrowIfOverlayError(
                    _setOverlayRaw(_mainHandle, handle.AddrOfPinnedObject(), (uint)bitmap.Width, (uint)bitmap.Height, bytesPerPixel),
                    "SetOverlayRaw");
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public void SetThumbnailFromFile(string path)
        => ThrowIfOverlayError(_setOverlayFromFile(_thumbnailHandle, path), "SetOverlayFromFile thumbnail");

    public bool PollNextOverlayEvent(out OpenVrEvent vrEvent)
    {
        vrEvent = default;
        return _pollNextOverlayEvent(_mainHandle, ref vrEvent, (uint)Marshal.SizeOf<OpenVrEvent>());
    }

    public bool IsActiveDashboardOverlay()
        => _isActiveDashboardOverlay(_mainHandle);

    public void Dispose()
    {
        try
        {
            if (_mainHandle != 0)
            {
                _destroyOverlay(_mainHandle);
                _mainHandle = 0;
            }

            if (_thumbnailHandle != 0)
            {
                _destroyOverlay(_thumbnailHandle);
                _thumbnailHandle = 0;
            }

            if (_initialized)
            {
                _shutdownInternal();
                _initialized = false;
            }
        }
        finally
        {
            NativeLibrary.Free(_library);
        }
    }

    private void ThrowIfOverlayError(int error, string operation)
    {
        if (error == VrOverlayErrorNone)
        {
            return;
        }

        var errorNamePointer = _getOverlayErrorNameFromEnum(error);
        var errorName = errorNamePointer == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(errorNamePointer);
        throw new InvalidOperationException($"{operation} failed: {(string.IsNullOrWhiteSpace(errorName) ? error.ToString() : errorName)}");
    }

    private static bool TryFindOpenVrApiDll(out string openVrApiDllPath, out string reason)
    {
        foreach (var runtimePath in GetOpenVrRuntimePaths())
        {
            var candidate = Path.Combine(runtimePath, "bin", "win64", "openvr_api.dll");
            if (File.Exists(candidate))
            {
                openVrApiDllPath = candidate;
                reason = "";
                return true;
            }
        }

        openVrApiDllPath = "";
        reason = "openvr_api.dll was not found in the configured SteamVR runtime";
        return false;
    }

    private static IEnumerable<string> GetOpenVrRuntimePaths()
    {
        var openVrPathsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openvr",
            "openvrpaths.vrpath");
        if (File.Exists(openVrPathsFile))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(openVrPathsFile));
            if (document.RootElement.TryGetProperty("runtime", out var runtimeElement) && runtimeElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in runtimeElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } runtimePath)
                    {
                        yield return runtimePath.TrimEnd('\\', '/');
                    }
                }
            }
        }

        const string steamVrUninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 250820";
        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var key = baseKey.OpenSubKey(steamVrUninstallKeyPath);
            if (key?.GetValue("InstallLocation") is string installLocation && !string.IsNullOrWhiteSpace(installLocation))
            {
                yield return installLocation.TrimEnd('\\', '/');
            }
        }
    }

    private static string DescribeOpenVrInitError(int error, VrGetVrInitErrorAsEnglishDescriptionDelegate? getDescription)
    {
        if (getDescription is null)
        {
            return error.ToString();
        }

        var descriptionPointer = getDescription(error);
        var description = descriptionPointer == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(descriptionPointer);
        return string.IsNullOrWhiteSpace(description) ? error.ToString() : $"{description} ({error})";
    }

    private static T GetExportDelegate<T>(IntPtr library, string exportName) where T : Delegate
        => CreateDelegate<T>(NativeLibrary.GetExport(library, exportName));

    private static T? GetOptionalExportDelegate<T>(IntPtr library, string exportName) where T : Delegate
        => NativeLibrary.TryGetExport(library, exportName, out var exportPointer)
            ? CreateDelegate<T>(exportPointer)
            : null;

    private static T CreateDelegate<T>(IntPtr functionPointer) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(functionPointer);

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenVrOverlayFnTable
    {
        public IntPtr FindOverlay;
        public IntPtr CreateOverlay;
        public IntPtr CreateSubviewOverlay;
        public IntPtr DestroyOverlay;
        public IntPtr GetOverlayKey;
        public IntPtr GetOverlayName;
        public IntPtr SetOverlayName;
        public IntPtr GetOverlayImageData;
        public IntPtr GetOverlayErrorNameFromEnum;
        public IntPtr SetOverlayRenderingPid;
        public IntPtr GetOverlayRenderingPid;
        public IntPtr SetOverlayFlag;
        public IntPtr GetOverlayFlag;
        public IntPtr GetOverlayFlags;
        public IntPtr SetOverlayColor;
        public IntPtr GetOverlayColor;
        public IntPtr SetOverlayAlpha;
        public IntPtr GetOverlayAlpha;
        public IntPtr SetOverlayTexelAspect;
        public IntPtr GetOverlayTexelAspect;
        public IntPtr SetOverlaySortOrder;
        public IntPtr GetOverlaySortOrder;
        public IntPtr SetOverlayWidthInMeters;
        public IntPtr GetOverlayWidthInMeters;
        public IntPtr SetOverlayCurvature;
        public IntPtr GetOverlayCurvature;
        public IntPtr SetOverlayPreCurvePitch;
        public IntPtr GetOverlayPreCurvePitch;
        public IntPtr SetOverlayTextureColorSpace;
        public IntPtr GetOverlayTextureColorSpace;
        public IntPtr SetOverlayTextureBounds;
        public IntPtr GetOverlayTextureBounds;
        public IntPtr GetOverlayTransformType;
        public IntPtr SetOverlayTransformAbsolute;
        public IntPtr GetOverlayTransformAbsolute;
        public IntPtr SetOverlayTransformTrackedDeviceRelative;
        public IntPtr GetOverlayTransformTrackedDeviceRelative;
        public IntPtr SetOverlayTransformTrackedDeviceComponent;
        public IntPtr GetOverlayTransformTrackedDeviceComponent;
        public IntPtr SetOverlayTransformCursor;
        public IntPtr GetOverlayTransformCursor;
        public IntPtr SetOverlayTransformProjection;
        public IntPtr SetSubviewPosition;
        public IntPtr ShowOverlay;
        public IntPtr HideOverlay;
        public IntPtr IsOverlayVisible;
        public IntPtr GetTransformForOverlayCoordinates;
        public IntPtr WaitFrameSync;
        public IntPtr PollNextOverlayEvent;
        public IntPtr GetOverlayInputMethod;
        public IntPtr SetOverlayInputMethod;
        public IntPtr GetOverlayMouseScale;
        public IntPtr SetOverlayMouseScale;
        public IntPtr ComputeOverlayIntersection;
        public IntPtr IsHoverTargetOverlay;
        public IntPtr SetOverlayIntersectionMask;
        public IntPtr TriggerLaserMouseHapticVibration;
        public IntPtr SetOverlayCursor;
        public IntPtr SetOverlayCursorPositionOverride;
        public IntPtr ClearOverlayCursorPositionOverride;
        public IntPtr SetOverlayTexture;
        public IntPtr ClearOverlayTexture;
        public IntPtr SetOverlayRaw;
        public IntPtr SetOverlayFromFile;
        public IntPtr GetOverlayTexture;
        public IntPtr ReleaseNativeOverlayHandle;
        public IntPtr GetOverlayTextureSize;
        public IntPtr CreateDashboardOverlay;
        public IntPtr IsDashboardVisible;
        public IntPtr IsActiveDashboardOverlay;
    }

    private delegate IntPtr VrInitInternalDelegate(ref int error, int applicationType);
    private delegate void VrShutdownInternalDelegate();
    private delegate IntPtr VrGetGenericInterfaceDelegate(string interfaceVersion, ref int error);
    private delegate IntPtr VrGetVrInitErrorAsEnglishDescriptionDelegate(int error);
    private delegate int DestroyOverlayDelegate(ulong overlayHandle);
    private delegate IntPtr GetOverlayErrorNameFromEnumDelegate(int error);
    private delegate int SetOverlayWidthInMetersDelegate(ulong overlayHandle, float widthInMeters);
    private delegate int SetOverlayFlagDelegate(ulong overlayHandle, int flag, [MarshalAs(UnmanagedType.I1)] bool enabled);
    private delegate bool PollNextOverlayEventDelegate(ulong overlayHandle, ref OpenVrEvent vrEvent, uint eventSize);
    private delegate int SetOverlayInputMethodDelegate(ulong overlayHandle, int inputMethod);
    private delegate int SetOverlayMouseScaleDelegate(ulong overlayHandle, ref OpenVrVector2 mouseScale);
    private delegate int SetOverlayTextureDelegate(ulong overlayHandle, ref OpenVrTexture texture);
    private delegate int SetOverlayRawDelegate(ulong overlayHandle, IntPtr buffer, uint width, uint height, uint bytesPerPixel);
    private delegate int SetOverlayFromFileDelegate(ulong overlayHandle, [MarshalAs(UnmanagedType.LPStr)] string filePath);
    private delegate int CreateDashboardOverlayDelegate([MarshalAs(UnmanagedType.LPStr)] string overlayKey, [MarshalAs(UnmanagedType.LPStr)] string overlayName, ref ulong mainHandle, ref ulong thumbnailHandle);
    private delegate bool IsActiveDashboardOverlayDelegate(ulong overlayHandle);
}
