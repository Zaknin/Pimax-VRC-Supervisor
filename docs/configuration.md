# Configuration

The release includes a commented `supervisor.config.json`. This page covers the most important settings.

## Key Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| `BrokenEyePath` | empty | Prompts for Broken Eye on first run. |
| `VrcFaceTrackingPath` | empty | Prompts for VRCFaceTracking on first run. |
| `BrokenEyeStartMinimized` | `false` | Requests/minimizes Broken Eye after launch. |
| `VrcFaceTrackingStartMinimized` | `false` | Requests/minimizes VRCFaceTracking after launch. |
| `MouthTrackerUser` | empty | Empty means ask on first run; `true` enables mouth tracker monitoring. |
| `TurnOffSecondaryMonitors` | empty | Empty means ask on first run; `true` enables monitor layout handling. |
| `AutoLaunchScheduledTask` | empty | Empty means ask on first setup; `true` creates/repairs the task. |
| `StartupLaunchMode` | `Unspecified` | `None`, `ScheduledTask`, or `SteamVrManifest`. |
| `StopWithSteamVr` | `false` | Forced `true` for SteamVR manifest startup; cleanup runs when `vrserver.exe` exits. |
| `AutoLaunchApps` | `[]` | Extra apps started after Broken Eye and VRCFaceTracking. |
| `BaseStationsEnabled` | `false` | Enables native SteamVR base-station power automation. |
| `BaseStationPowerDownMode` | `Sleep` | Cleanup command: `Sleep` or `Standby` for Base Station 2.0. |
| `BaseStations` | `[]` | Configured base stations. Use the editor to scan and manage. |
| `OscGoesBrrrEnabled` | `false` | Enables Intiface/OscGoesBrrr workflow support. |
| `OscGoesBrrrHotkeyEnabled` | `true` | Press `L` to launch the workflow. |
| `OscGoesBrrrBleScannerEnabled` | `false` | Enables Lovense BLE advertisement scanning. |
| `OscRouterEnabled` | `false` | Enables in-process OSC UDP routing before app launch. |
| `OscRouterReceivePort` | `9001` | Local UDP port the OSC router listens on at `127.0.0.1`. |
| `OscRoutes` | `[]` | Output routes with `Enabled`, `Name`, and `AppReceivePort`. |
| `UsePimaxServiceLogReconnectDetector` | `true` | Watches PiService logs for fast runtime reconnects. |
| `UseMouthTrackerPnPReconnectDetector` | `true` | Watches Windows PnP events for fast mouth tracker reconnects. |
| `PollIntervalSeconds` | `2` | Normal device/process polling interval. |
| `RestartDelayAfterReconnectSeconds` | `10` | Stability wait before restarting apps after Pimax reconnect. |

## Auto-Launch Apps

Example extra app:

```json
"AutoLaunchApps": [
  {
    "Name": "Example overlay",
    "Path": "C:\\Tools\\ExampleOverlay\\ExampleOverlay.exe",
    "Enabled": true,
    "RestartOnPimaxReconnect": true,
    "RunAsAdmin": false,
    "StartMinimized": false
  }
]
```

Set `RestartOnPimaxReconnect` to `false` for apps that should stay running during the Pimax reconnect restart cycle. They will still be closed when the VRChat session ends.

## Base Stations

Example Base Station 2.0 row:

```json
"BaseStationsEnabled": true,
"BaseStationPowerDownMode": "Standby",
"BaseStations": [
  {
    "FriendlyName": "Front left",
    "Name": "LHB-00000000",
    "BluetoothAddress": "AA:BB:CC:DD:EE:FF",
    "Version": "V2",
    "Enabled": true,
    "Id": "",
    "PowerStateReadUnsupported": true
  }
]
```

`PowerStateReadUnsupported` is set automatically when a station or firmware does not support reading power state. The supervisor skips future state reads for that station; Config Editor **Refresh State** can retry detection manually.

## OscGoesBrrr Workflow

```json
"OscGoesBrrrEnabled": true,
"OscGoesBrrrHotkeyEnabled": true,
"OscGoesBrrrBleScannerEnabled": false,
"IntifacePath": "%APPDATA%\\IntifaceCentral\\intiface_central.exe",
"OscGoesBrrrPath": "%LOCALAPPDATA%\\Programs\\OscGoesBrrr\\OscGoesBrrr.exe",
"DelayBeforeOscGoesBrrrSeconds": 5,
"LovenseDetectors": [
  [ "Lovense" ],
  [ "LVS-" ]
]
```

## Startup Modes

The **Basics** tab in the Config Editor has a **Startup** section:

- **Create/evaluate VRChat auto-launch Scheduled Task** — a hidden elevated watcher starts the supervisor only after `VRChat.exe` and SteamVR `vrserver.exe` are both running.
- **Start with SteamVR** — registers `PimaxVrcSupervisorSteamVrHost.exe` as a SteamVR dashboard overlay app and creates a separate on-demand elevated helper task.
- **Stop with SteamVR** — forced on for SteamVR startup mode. When `vrserver.exe` exits, the supervisor powers down base stations, restores monitors, closes managed apps, and exits.
