# Configuration Fields Reference

Complete reference for all fields in `supervisor.config.json`.

## Top-Level Fields

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `DisplayName` | string | `"Default"` | Friendly name shown in the editor and console startup banner. |
| `BrokenEyePath` | string | `""` | Full path to Broken Eye executable. Prompts on first run if empty. |
| `BrokenEyeStartMinimized` | bool | `false` | Start Broken Eye minimized. |
| `VrcFaceTrackingPath` | string | `""` | Full path to VRCFaceTracking executable. Prompts on first run if empty. |
| `VrcFaceTrackingStartMinimized` | bool | `false` | Start VRCFaceTracking minimized. |
| `OscGoesBrrrEnabled` | bool | `false` | Enable OscGoesBrrr workflow. |
| `OscGoesBrrrHotkeyEnabled` | bool | `true` | Legacy name for manual console launch mode. Console hotkey `2` launches the workflow when `OscGoesBrrrEnabled` is true. |
| `OscGoesBrrrBleScannerEnabled` | bool | `false` | Enable Lovense BLE scanning. |
| `OscRouterEnabled` | bool | `false` | Enable OSC UDP routing. |
| `OscRouterReceivePort` | int | `9001` | Local UDP port for OSC router at `127.0.0.1`. |
| `OscRoutes` | array | `[]` | Output routes for OSC forwarding. |
| `IntifacePath` | string | `"%APPDATA%\\IntifaceCentral\\intiface_central.exe"` | Path to Intiface. |
| `IntifaceStartMinimized` | bool | `false` | Start Intiface minimized. |
| `OscGoesBrrrPath` | string | `"%LOCALAPPDATA%\\Programs\\OscGoesBrrr\\OscGoesBrrr.exe"` | Path to OscGoesBrrr. |
| `OscGoesBrrrStartMinimized` | bool | `false` | Start OscGoesBrrr minimized. |
| `BrokenEyeProcessNames` | string[] | `["Broken Eye"]` | Process names for Broken Eye (no `.exe`). |
| `VrcFaceTrackingProcessNames` | string[] | `["VRCFaceTracking"]` | Process names for VRCFaceTracking (no `.exe`). |
| `IntifaceProcessNames` | string[] | `["intiface_central.exe"]` | Process names for Intiface. |
| `OscGoesBrrrProcessNames` | string[] | `["OscGoesBrrr.exe"]` | Process names for OscGoesBrrr. |
| `AutoLaunchApps` | array | `[]` | Extra apps to launch with the session. |
| `WatchedShutdownProcessNames` | string[] | `["VRChat"]` | Processes that trigger cleanup when closed. |
| `SteamVrServerProcessNames` | string[] | `["vrserver"]` | SteamVR server process names. |
| `BaseStationsEnabled` | bool | `false` | Enable base station power automation. |
| `BaseStationPowerDownMode` | string | `"Sleep"` | `Sleep` or `Standby`. |
| `BaseStations` | array | `[]` | Base station configuration rows. |
| `MouthTrackerUser` | bool \| string | `""` | `true`/`false` or `""` to ask on first run. |
| `TurnOffSecondaryMonitors` | bool \| string | `""` | `true`/`false` or `""` to ask on first run. |
| `AutoLaunchScheduledTask` | bool \| string | `""` | `true`/`false` or `""` to ask on first setup. |
| `StartupLaunchMode` | string | `"Unspecified"` | `None`, `ScheduledTask`, `SteamVrManifest`, or `Unspecified`. |
| `StopWithSteamVr` | bool | `false` | Compatibility field. SteamVR cleanup follows `StartupLaunchMode = SteamVrManifest`. |
| `PimaxDetectors` | string[][] | See below | Pimax headset detection rules. |
| `MouthTrackerDetectors` | string[][] | See below | Mouth tracker detection rules. |
| `LovenseDetectors` | string[][] | See below | Lovense detection rules. |
| `UsePimaxServiceLogReconnectDetector` | bool | `true` | Watch PiService logs for fast reconnects. |
| `UseMouthTrackerPnPReconnectDetector` | bool | `true` | Watch PnP events for mouth tracker reconnects. |
| `PimaxServiceLogDirectory` | string | `"%LOCALAPPDATA%\\Pimax\\PiService\\Log"` | PiService log folder. |
| `PimaxServiceLogReconnectLookbackLines` | int | `400` | Lines to scan each poll. |
| `PollIntervalSeconds` | int | `2` | Polling interval in seconds. |
| `StartupTimeoutSeconds` | int | `30` | Max seconds to wait for apps to appear. |
| `StartupStableSeconds` | int | `5` | Seconds app must stay running for startup verification. |
| `DelayBeforeVrcFaceTrackingSeconds` | int | `5` | Delay between Broken Eye and VRCFaceTracking. |
| `DelayBeforeOscGoesBrrrSeconds` | int | `5` | Delay between Intiface and OscGoesBrrr. |
| `OscGoesBrrrBleScanSeconds` | int | `30` | BLE scan duration. |
| `OscGoesBrrrBleScanIntervalSeconds` | int | `60` | BLE scan interval. |
| `RestartDelayAfterReconnectSeconds` | int | `10` | Wait before restarting apps after reconnect. |
| `WatchedProcessCrashRelaunchGraceSeconds` | int | `300` | Crash relaunch grace period. |
| `ShutdownGraceSeconds` | int | `8` | Graceful shutdown timeout. |
| `DeviceProbeTimeoutSeconds` | int | `10` | Device query timeout. |

## AutoLaunchApp Object

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `Name` | string | `""` | Display name. |
| `Path` | string | `""` | Executable path. |
| `Enabled` | bool | `true` | Whether the app is active. |
| `RestartOnPimaxReconnect` | bool? | `true` | Restart on Pimax reconnect. |
| `CloseOnPimaxDisconnect` | bool? | `null` | Legacy alias for `RestartOnPimaxReconnect`. |
| `RunAsAdmin` | bool | `false` | Launch elevated. |
| `StartMinimized` | bool | `false` | Start minimized. |

## BaseStation Object

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `FriendlyName` | string | `""` | Human-readable name. |
| `Name` | string | `""` | SteamVR identifier. |
| `BluetoothAddress` | string | `""` | BLE address. |
| `Version` | string | `"Unknown"` | `V1` or `V2`. |
| `Enabled` | bool | `true` | Whether the station is active. |
| `Id` | string | `""` | V1 8-character ID. |
| `PowerStateReadUnsupported` | bool | `false` | Auto-set when firmware can't report power state. |

## OscRoute Object

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `Name` | string | `""` | Display name. |
| `AppReceivePort` | int | `0` | Target app UDP port (1–65535). |
| `Enabled` | bool | `true` | Whether the route is active. |

See also: [Reference Overview](index.md) · [Supervisor Functions](supervisor-functions.md) · [Config Editor Functions](config-editor-functions.md)
