# General Tab

The **General** tab is the first tab in the Configurator and contains startup, diagnostics, and editor utility settings. Broken Eye, VRCFaceTracking, mouth-tracker, and fast reconnect settings are on the **Face Tracking** tab.

## Device Monitoring

| Checkbox | Config Key | Description |
| --- | --- | --- |
| Turn off secondary monitors | `TurnOffSecondaryMonitors` | Saves the current monitor layout and disables secondary monitors during the VR session. Restored after VRChat and SteamVR close. |

## Startup Section

The Startup section controls how the supervisor is launched:

| Checkbox | Config Key | Description |
| --- | --- | --- |
| Start in CLI mode when SteamVR is running | `AutoLaunchScheduledTask` | Creates or repairs an elevated Windows Scheduled Task that starts the supervisor when `vrserver.exe` is running. The supervisor waits for VRChat before starting managed apps. |
| SteamVR Overlay | `StartupLaunchMode` = `SteamVrManifest` | Registers the SteamVR dashboard host manifest and starts the supervisor when SteamVR starts. |

> **Note:** "Start in CLI mode when SteamVR is running" and "SteamVR Overlay" are mutually exclusive. Selecting one clears the other. SteamVR manifest startup exits automatically when `vrserver.exe` closes.

## Diagnostics Section

Diagnostics are off by default and are intended for short performance captures.

| Control | Config Key | Description |
| --- | --- | --- |
| Enable Diagnostics | GUI master switch | Enables the diagnostics controls in the editor. When off, all diagnostics and debug options are saved as disabled. |
| Log supervisor diagnostics | `DiagnosticsLogSupervisor` | Writes supervisor CPU, memory, loop, command, process detection, app, and base-station timing summaries. |
| Log SteamVR overlay diagnostics | `DiagnosticsLogSteamVrOverlay` | Writes dashboard host visibility, loop, refresh, render, D3D upload, and texture submit timing summaries. |
| Log Supervisor Debug | `DiagnosticsDebugSupervisor` | Writes supervisor debug-event logs when diagnostics are enabled in the GUI. |
| Log SteamVR Overlay Debug | `DiagnosticsDebugSteamVrOverlay` | Writes overlay debug-event logs when diagnostics are enabled in the GUI. |
| Show SteamVR overlay pointer marker | `DiagnosticsDebugSteamVrPointer` | Draws the visible overlay pointer marker for hover hit-test troubleshooting when overlay debug logging is enabled. |
| Verbose diagnostic timings | `DiagnosticsVerbose` | Adds individual timing lines for slow operations and command/render paths. |
| Summary interval | `DiagnosticsSummaryIntervalSeconds` | Seconds between summary lines. |
| Diagnostic log folder | `DiagnosticsLogDirectory` | Output folder for diagnostic text logs. Default is `%TEMP%\PimaxVrcSupervisorDiagnostics`. |

The diagnostic log folder field and Open button remain available even when diagnostics are disabled, so existing logs can still be inspected.

## Editor Utilities

The General tab includes two utility buttons:

- **Restore Defaults** â€” Replaces current editor values with default configuration values. The config file is not overwritten until you click Save.
- **About** â€” Shows editor version, supervisor executable path, and third-party notices.

## Keyboard Shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save As |
| Ctrl+O | Browse config |
| F5 or Ctrl+R | Reload |
| Ctrl+L | Launch Supervisor |
| Ctrl+Shift+V | Validate |

See also: [GUI Manual Overview](index.md) Â· [Base Stations](base-stations.md) Â· [Auto Startup](auto-launch.md)
