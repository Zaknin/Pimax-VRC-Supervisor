# General Tab

The **General** tab is the first tab in the Config Editor and contains the core settings that most users need to configure.

## Core Executable Paths

| Field | Config Key | Description |
| --- | --- | --- |
| Broken Eye executable | `BrokenEyePath` | Full path to `Broken Eye.exe`. The supervisor starts this app first. |
| VRCFaceTracking executable | `VrcFaceTrackingPath` | Full path to `VRCFaceTracking.exe`. Starts after Broken Eye settles. |

Both paths support environment variables like `%APPDATA%` and `%LOCALAPPDATA%`. The editor validates expanded paths and shows **Found** / **Not found** indicators.

### Start Minimized Options

| Checkbox | Config Key | Description |
| --- | --- | --- |
| Start Broken Eye minimized | `BrokenEyeStartMinimized` | Starts Broken Eye minimized, then tries to minimize its main window after launch. |
| Start VRCFaceTracking minimized | `VrcFaceTrackingStartMinimized` | Starts VRCFaceTracking minimized, then tries to minimize its main window after launch. |

## Device Monitoring

| Checkbox | Config Key | Description |
| --- | --- | --- |
| Use Vive mouth tracker | `MouthTrackerUser` | Enables mouth tracker monitoring. When the mouth tracker reconnects, only VRCFaceTracking is restarted. |
| Turn off secondary monitors | `TurnOffSecondaryMonitors` | Saves the current monitor layout and disables secondary monitors during the VR session. Restored after VRChat and SteamVR close. |

## PiService Log Folder

| Field | Config Key | Description |
| --- | --- | --- |
| PiService log folder | `PimaxServiceLogDirectory` | Folder containing `PiService__*.log` files. Environment variables are expanded. Default: `%LOCALAPPDATA%\Pimax\PiService\Log` |

## Startup Section

The Startup section controls how the supervisor is launched:

| Checkbox | Config Key | Description |
| --- | --- | --- |
| Create/evaluate VRChat auto-launch Scheduled Task | `AutoLaunchScheduledTask` | Creates or repairs an elevated Windows Scheduled Task that watches for `VRChat.exe` and starts the supervisor when `vrserver.exe` is already running. |
| Start with SteamVR | `StartupLaunchMode` = `SteamVrManifest` | Registers the SteamVR dashboard host manifest and starts the supervisor when SteamVR starts. |

> **Note:** "Create/evaluate VRChat auto-launch Scheduled Task" and "Start with SteamVR" are mutually exclusive. Selecting one clears the other. SteamVR manifest startup exits automatically when `vrserver.exe` closes.

## Diagnostics Section

Diagnostics are off by default and are intended for short performance captures.

| Control | Config Key | Description |
| --- | --- | --- |
| Log supervisor diagnostics | `DiagnosticsLogSupervisor` | Writes supervisor CPU, memory, loop, command, process detection, app, and base-station timing summaries. |
| Log SteamVR overlay diagnostics | `DiagnosticsLogSteamVrOverlay` | Writes dashboard host visibility, loop, refresh, render, D3D upload, and texture submit timing summaries. |
| Verbose diagnostic timings | `DiagnosticsVerbose` | Adds individual timing lines for slow operations and command/render paths. |
| Summary interval | `DiagnosticsSummaryIntervalSeconds` | Seconds between summary lines. |
| Diagnostic log folder | `DiagnosticsLogDirectory` | Output folder for diagnostic text logs. Default is `%TEMP%\PimaxVrcSupervisorDiagnostics`. |

## Editor Utilities

The General tab includes two utility buttons:

- **Restore Defaults** — Replaces current editor values with default configuration values. The config file is not overwritten until you click Save.
- **About** — Shows editor version, supervisor executable path, and third-party notices.

## Keyboard Shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save As |
| Ctrl+O | Browse config |
| F5 or Ctrl+R | Reload |
| Ctrl+L | Launch Supervisor |
| Ctrl+Shift+V | Validate |

See also: [GUI Manual Overview](index.md) · [Base Stations](base-stations.md) · [Auto Launch](auto-launch.md)
