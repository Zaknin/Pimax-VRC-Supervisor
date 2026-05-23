# Reference

## Full Configuration Reference

For the complete configuration schema, see the commented `supervisor.config.json` included in the release.

## Settings Table

| Setting | Type | Default | Description |
| --- | --- | --- | --- |
| `BrokenEyePath` | string | `""` | Path to `Broken Eye.exe`. Prompts on first run if empty. |
| `VrcFaceTrackingPath` | string | `""` | Path to `VRCFaceTracking.exe`. Prompts on first run if empty. |
| `BrokenEyeStartMinimized` | bool | `false` | Start Broken Eye minimized. |
| `VrcFaceTrackingStartMinimized` | bool | `false` | Start VRCFaceTracking minimized. |
| `MouthTrackerUser` | bool? | `null` | `true` enables mouth tracker monitoring. Null prompts on first run. |
| `TurnOffSecondaryMonitors` | bool? | `null` | `true` enables monitor layout handling. Null prompts on first run. |
| `AutoLaunchScheduledTask` | bool? | `null` | `true` creates/repairs the auto-launch task. Null prompts on first setup. |
| `StartupLaunchMode` | string | `"Unspecified"` | `None`, `ScheduledTask`, or `SteamVrManifest`. |
| `StopWithSteamVr` | bool | `false` | Compatibility field. SteamVR cleanup follows `StartupLaunchMode = SteamVrManifest`. |
| `AutoLaunchApps` | array | `[]` | Extra apps to launch with the session. |
| `BaseStationsEnabled` | bool | `false` | Enable base station power automation. |
| `BaseStationPowerDownMode` | string | `"Sleep"` | `Sleep` or `Standby` for cleanup. |
| `BaseStations` | array | `[]` | Base station configuration rows. |
| `OscGoesBrrrEnabled` | bool | `false` | Enable Intiface/OscGoesBrrr workflow. |
| `OscGoesBrrrHotkeyEnabled` | bool | `true` | Legacy name for manual console launch mode. Console hotkey `2` launches the workflow when `OscGoesBrrrEnabled` is true. |
| `OscGoesBrrrBleScannerEnabled` | bool | `false` | Enable Lovense BLE scanning. |
| `IntifacePath` | string | `""` | Path to Intiface. |
| `OscGoesBrrrPath` | string | `""` | Path to OscGoesBrrr. |
| `DelayBeforeOscGoesBrrrSeconds` | int | `5` | Delay between Intiface and OscGoesBrrr startup. |
| `LovenseDetectors` | array | `[]` | BLE advertisement prefixes to match. |
| `OscRouterEnabled` | bool | `false` | Enable OSC UDP routing. |
| `OscRouterReceivePort` | int | `9001` | Local UDP port for VRChat OSC output. |
| `OscRoutes` | array | `[]` | Output routes for OSC forwarding. |
| `UsePimaxServiceLogReconnectDetector` | bool | `true` | Watch PiService logs for reconnects. |
| `UseMouthTrackerPnPReconnectDetector` | bool | `true` | Watch PnP events for mouth tracker. |
| `PollIntervalSeconds` | int | `2` | Polling interval in seconds. |
| `RestartDelayAfterReconnectSeconds` | int | `10` | Wait before restarting apps after reconnect. |

## AutoLaunchApp Object

| Field | Type | Description |
| --- | --- | --- |
| `Name` | string | Display name. |
| `Path` | string | Executable path. |
| `Enabled` | bool | Whether the app is active. |
| `RestartOnPimaxReconnect` | bool | Restart on Pimax reconnect. |
| `RunAsAdmin` | bool | Launch elevated. |
| `StartMinimized` | bool | Start minimized. |

## BaseStation Object

| Field | Type | Description |
| --- | --- | --- |
| `FriendlyName` | string | Human-readable name. |
| `Name` | string | SteamVR identifier (e.g., `LHB-00000000`). |
| `BluetoothAddress` | string | BLE address (e.g., `AA:BB:CC:DD:EE:FF`). |
| `Version` | string | `V1` or `V2`. |
| `Enabled` | bool | Whether the station is active. |
| `Id` | string | Internal ID. |
| `PowerStateReadUnsupported` | bool | Set automatically when firmware cannot report power state. |

## OscRoute Object

| Field | Type | Description |
| --- | --- | --- |
| `Enabled` | bool | Whether the route is active. |
| `Name` | string | Display name. |
| `AppReceivePort` | int | Local UDP port of the target app. |
