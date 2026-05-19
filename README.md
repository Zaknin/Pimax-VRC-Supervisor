# Pimax VRC Supervisor

![Version](https://img.shields.io/badge/version-1.0.9-2563eb)
![Platform](https://img.shields.io/badge/platform-Windows-0f766e)
![Runtime](https://img.shields.io/badge/.NET-9.0-512bd4)
![Release](https://img.shields.io/badge/release-signed%20%2B%20attested-16a34a)

A small Windows companion app for Pimax Crystal and VRChat sessions. It waits for the headset, starts the tools you rely on, watches for device reconnects, and cleans everything up when VRChat is done.

The current release is **v1.0.9**.

## Why It Exists

Pimax + VRChat setups can be fragile when USB devices blink, the runtime reconnects, or face tracking starts in the wrong order. Pimax VRC Supervisor keeps that session flow predictable:

- wait until the Pimax headset is actually present
- launch Broken Eye, then VRCFaceTracking
- restart the right apps after headset or mouth tracker reconnects
- optionally manage secondary monitors during VR
- optionally auto-launch when VRChat starts while SteamVR is running
- optionally start Intiface and OscGoesBrrr for Lovense workflows

## Included Apps

| App | Purpose |
| --- | --- |
| `PimaxVrcSupervisor.exe` | Console supervisor that watches the VR session and manages apps/devices. |
| `PimaxVrcSupervisorConfigEditor.exe` | GUI editor for `supervisor.config.json`. |
| `supervisor.config.json` | Documented configuration file copied next to the executables. |

Both executables are stamped with version `1.0.9`.

## Features

### Session Startup

- Waits for a Pimax Crystal-compatible headset before launching managed apps.
- Prompts for `Broken Eye.exe` and `VRCFaceTracking.exe` on first run if paths are missing.
- Starts Broken Eye first, retries startup if needed, then starts VRCFaceTracking after a configurable delay.
- Starts optional user-defined apps after the main Broken Eye/VRCFaceTracking sequence.
- Can start apps minimized and then try to minimize their main windows after launch.
- Prevents duplicate normal supervisor instances from racing each other.

### Reconnect Handling

- Watches Pimax headset USB/runtime state.
- Watches Pimax PiService HID remove/add log events so short runtime reconnects can be caught between normal polls.
- Waits for a stable headset connection before restarting managed apps.
- Optionally watches Vive mouth tracker / HTC Multimedia Camera devices.
- Watches Windows Kernel-PnP events for fast mouth tracker reconnects.
- Restarts only VRCFaceTracking when the mouth tracker reconnects while the headset stays connected.

### VRChat Shutdown Cleanup

- Watches `VRChat.exe` and exits when the session ends.
- If VRChat appears to crash, waits for a configurable relaunch grace period before cleanup.
- Optionally waits for SteamVR `vrserver.exe` before restoring monitors.
- Closes Broken Eye, VRCFaceTracking, OscGoesBrrr workflow apps, and configured auto-launch apps.
- Uses graceful shutdown first, then force-closes after the configured timeout.

### Monitor Management

- Optional first-run prompt for secondary monitor handling.
- Saves the active Windows monitor layout before the session.
- Switches to monitor 1 only when multiple monitors are active.
- Restores the previous layout after VRChat and SteamVR close.

### Auto-Launch Task

- Optional elevated Scheduled Task named `Pimax VRC Supervisor Auto Launch`.
- Starts a hidden watcher at Windows sign-in.
- The watcher launches the supervisor only when `VRChat.exe` and SteamVR `vrserver.exe` are both running.
- The task can be repaired directly with:

```powershell
.\PimaxVrcSupervisor.exe --install-auto-launch-task
```

### OscGoesBrrr / Lovense Workflow

- Optional `OscGoesBrrrEnabled` workflow.
- Press `L` in the supervisor console to start Intiface, wait the configured delay, then start OscGoesBrrr.
- Optional BLE scanner can detect Lovense advertisements such as `LVS-` and auto-launch the same workflow.
- If Intiface is running but OscGoesBrrr is missing, the supervisor can repair the workflow.
- Pimax reconnects do not restart Intiface/OscGoesBrrr; normal session cleanup closes them.

## Requirements

- Windows 10/11
- No separate .NET install when using the self-contained release zip
- Pimax Crystal-compatible headset
- [Broken Eye](https://github.com/ghostiam/BrokenEye)
- [VRCFaceTracking](https://docs.vrcft.io/docs/vrcft-software/vrcft)
- Optional: Vive mouth tracker exposed as `HTC Multimedia Camera`
- Optional: Intiface + OscGoesBrrr for Lovense workflows

## Install

1. Download `PimaxVrcSupervisor-v1.0.9.zip` from the GitHub release.
2. Extract it somewhere writable, for example:

```text
C:\Tools\PimaxVrcSupervisor
```

3. Run `PimaxVrcSupervisor.exe`.
4. Answer the first-run prompts.
5. Use `PimaxVrcSupervisorConfigEditor.exe` later if you want to edit paths, timers, detectors, or auto-launch apps without touching JSON by hand.

The supervisor requests administrator privileges through its manifest because some managed tools and monitor actions may require elevation.

## First Run Prompts

On first launch, the supervisor may ask:

- where `Broken Eye.exe` is located
- where `VRCFaceTracking.exe` is located
- whether you use a Vive mouth tracker
- whether to turn off secondary monitors during headset sessions
- whether to create the elevated VRChat/SteamVR auto-launch Scheduled Task

Answers are saved to `supervisor.config.json` next to the exe.

## Config Editor

Run:

```powershell
.\PimaxVrcSupervisorConfigEditor.exe
```

The editor includes tabs for:

- **Basics**: main executable paths and first-run choices
- **Auto Launch**: extra apps to launch with the VR session
- **OSCGoesBrrr**: Intiface, OscGoesBrrr, hotkey, BLE scanner, and Lovense rules
- **Processes**: watched process names and cleanup targets
- **Detectors**: Pimax, mouth tracker, and Lovense detection rules
- **Timing**: poll intervals, startup delays, reconnect waits, and shutdown grace periods
- **Raw JSON**: direct config editing when you need it

## Key Configuration

The release includes a commented `supervisor.config.json`. Important settings:

| Setting | Default | Meaning |
| --- | --- | --- |
| `BrokenEyePath` | empty | Prompts for Broken Eye on first run. |
| `VrcFaceTrackingPath` | empty | Prompts for VRCFaceTracking on first run. |
| `BrokenEyeStartMinimized` | `false` | Requests/minimizes Broken Eye after launch. |
| `VrcFaceTrackingStartMinimized` | `false` | Requests/minimizes VRCFaceTracking after launch. |
| `MouthTrackerUser` | empty | Empty means ask on first run; true enables mouth tracker monitoring. |
| `TurnOffSecondaryMonitors` | empty | Empty means ask on first run; true enables monitor layout handling. |
| `AutoLaunchScheduledTask` | empty | Empty means ask on first setup; true creates/repairs the task. |
| `AutoLaunchApps` | `[]` | Extra apps started after Broken Eye and VRCFaceTracking. |
| `OscGoesBrrrEnabled` | `false` | Enables Intiface/OscGoesBrrr workflow support. |
| `OscGoesBrrrHotkeyEnabled` | `true` | Press `L` to launch the workflow. |
| `OscGoesBrrrBleScannerEnabled` | `false` | Enables Lovense BLE advertisement scanning. |
| `UsePimaxServiceLogReconnectDetector` | `true` | Watches PiService logs for fast runtime reconnects. |
| `UseMouthTrackerPnPReconnectDetector` | `true` | Watches Windows PnP events for fast mouth tracker reconnects. |
| `PollIntervalSeconds` | `2` | Normal device/process polling interval. |
| `RestartDelayAfterReconnectSeconds` | `10` | Stability wait before restarting apps after Pimax reconnect. |

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

Example OscGoesBrrr workflow:

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

## Release Verification

GitHub Actions publishes signed and attested release assets when a `v*` tag is pushed or the `Release` workflow is run manually.

Expected release files:

- `PimaxVrcSupervisor-v1.0.9.zip`
- `PimaxVrcSupervisor-v1.0.9.zip.sha256`
- `PimaxVrcSupervisor-v1.0.9.zip.sigstore.json`

Verify the checksum:

```powershell
Get-FileHash .\PimaxVrcSupervisor-v1.0.9.zip -Algorithm SHA256
Get-Content .\PimaxVrcSupervisor-v1.0.9.zip.sha256
```

Verify the Sigstore bundle with cosign:

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.0.9.zip `
  --bundle .\PimaxVrcSupervisor-v1.0.9.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/(heads|tags)/.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

Sigstore verification proves the zip was signed by the repository workflow and recorded in the transparency log. It does not make Windows SmartScreen show the app as a verified publisher unless Authenticode code-signing secrets are configured for the workflow.

## Build From Source

Install the .NET 9 SDK, then run:

```powershell
dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.0.9
dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.0.9
```

The output folder will contain both executables, the config file, and this README.

## Troubleshooting

| Symptom | Try |
| --- | --- |
| The supervisor exits immediately | Check whether another normal supervisor instance is already running. |
| Broken Eye or VRCFaceTracking does not launch | Open the config editor and verify the executable path and process name. |
| Reconnects are not detected | Confirm `PimaxDetectors`, `UsePimaxServiceLogReconnectDetector`, and the PiService log directory. |
| Mouth tracker reconnects do nothing | Set `MouthTrackerUser` to `true` and verify `MouthTrackerDetectors`. |
| Monitors are not restored | Let SteamVR fully exit; the supervisor waits for `vrserver.exe` before restoring when monitor handling is enabled. |
| OscGoesBrrr does not start | Check `OscGoesBrrrEnabled`, the Intiface/OscGoesBrrr paths, and whether the hotkey or BLE scanner mode is enabled. |

## Notes

- This is a Windows-only utility.
- The release zip is self-contained and intentionally large because it includes the Windows .NET runtime.
- The app edits only its nearby `supervisor.config.json` unless you choose a different config path in the editor.
