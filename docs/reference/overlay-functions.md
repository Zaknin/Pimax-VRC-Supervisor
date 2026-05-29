# Overlay Functions Reference

Major behavior-defining functions and classes in `PimaxVrcSupervisorSteamVrHost.exe`.

## SteamVrDashboardHost

The main overlay host class.

### Key Methods

| Method | Description |
| --- | --- |
| `RunAsync(CancellationToken)` | Main entry point. Starts supervisor, creates overlay, runs event loop. |
| `StartSupervisorAsync()` | Requests elevated supervisor via Scheduled Task. |
| `RefreshStatusAsync()` | Polls supervisor status via command. |
| `RefreshConsoleAsync()` | Fetches recent console lines from supervisor. |
| `ProcessOverlayEvents(CancellationToken)` | Processes OpenVR overlay events (mouse clicks, button presses). |
| `Dispose()` | Cleans up overlay and GPU renderer. |

### Rendering

| Method | Description |
| --- | --- |
| `RenderOverlay()` | Renders the dashboard panel to a Bitmap. |
| `RefreshOverlayTexture(bool)` | Updates the OpenVR overlay texture. |
| `TryInitializeGpuRenderer()` | Attempts to create a D3D11 GPU renderer. |

### Command Communication

| Method | Description |
| --- | --- |
| `SendCommandAsync(string, TimeSpan)` | Sends a command via TCP. |
| `SendTcpCommandAsync(string, TimeSpan)` | Sends a command via TCP. |

### Event Processing

| Method | Description |
| --- | --- |
| `ProcessOverlayEvents()` | Polls OpenVR overlay events and dispatches them. |
| `ResolveClickButton(OverlayPointer)` | Resolves a mouse click to a dashboard button. |
| `HitTestLayout(PointF)` | Tests if a point is within a button's bounds. |
| `TryStartButtonCommand(DashboardButton, CancellationToken)` | Starts executing a button command. |
| `ExecuteButtonAsync(DashboardButton, CancellationToken)` | Executes a button command and updates status. |

## OpenVrOverlaySession

Manages the OpenVR overlay lifecycle.

| Method | Description |
| --- | --- |
| `Open(string, string)` | Creates a dashboard overlay session. |
| `SetOverlayWidthInMeters(float)` | Sets the overlay width in VR space. |
| `SetOverlayMouseScale(float, float)` | Sets the mouse scale for the overlay. |
| `SetOverlayInputMethodMouse()` | Enables mouse input. |
| `SetInteractiveDashboardFlags()` | Sets flags for interactive dashboard behavior. |
| `SetOverlayTexture(IntPtr)` | Sets the overlay texture from a GPU handle. |
| `SetThumbnailFromFile(string)` | Sets the dashboard thumbnail icon. |
| `PollNextOverlayEvent(out OpenVrEvent)` | Polls for the next overlay event. |
| `IsActiveDashboardOverlay()` | Checks if the overlay is currently active. |
| `Dispose()` | Destroys the overlay and shuts down OpenVR. |

## GpuOverlayRenderer

D3D11 GPU texture renderer for the overlay.

| Member | Description |
| --- | --- |
| `FeatureLevel` | The D3D11 feature level (e.g., `Level_11_1`). |
| `TexturePointer` | Native pointer to the D3D11 texture. |
| `Update(Bitmap)` | Updates the texture from a Bitmap. |
| `Dispose()` | Releases D3D11 resources. |

## DashboardButton

```csharp
internal sealed record DashboardButton(string Label, string Command, Rectangle Bounds);
```

| Field | Description |
| --- | --- |
| `Label` | Display text on the button. |
| `Command` | Command sent to the supervisor when clicked. |
| `Bounds` | Button position and size in the overlay. |

## OpenVrEvent

64-byte OpenVR event structure.

| Field | Offset | Description |
| --- | --- | --- |
| `EventType` | 0 | Event type enum. |
| `TrackedDeviceIndex` | 4 | Device index. |
| `EventAgeSeconds` | 8 | Age of the event. |
| `MouseX` | 16 | Mouse X coordinate. |
| `MouseY` | 20 | Mouse Y coordinate. |
| `MouseButton` | 24 | Mouse button state. |
| `CursorIndex` | 28 | Cursor index. |

See also: [Reference Overview](index.md) Â· [Supervisor Functions](supervisor-functions.md) Â· [Configurator Functions](config-editor-functions.md)
