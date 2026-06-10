# Pimax VRC Supervisor

![Version](https://img.shields.io/badge/version-1.2.3-2563eb)
![Platform](https://img.shields.io/badge/platform-Windows-0f766e)
![Runtime](https://img.shields.io/badge/.NET-9.0-512bd4)
![Release](https://img.shields.io/badge/release-signed%20%2B%20attested-16a34a)

A small Windows companion app for Pimax Crystal and VRChat sessions. It waits for the headset, starts the tools you rely on, watches for device reconnects, and cleans everything up when VRChat is done.

The current release is **v1.2.3**.

## Why It Exists

Pimax + VRChat setups can be fragile when USB devices blink, the runtime reconnects, or face tracking starts in the wrong order. Pimax VRC Supervisor keeps that session flow predictable:

- wait until the Pimax headset is actually present
- launch Broken Eye, then VRCFaceTracking
- restart the right apps after headset or mouth tracker reconnects
- optionally manage secondary monitors during VR
- optionally manage SteamVR base-station power through native Bluetooth LE
- optionally auto-launch when SteamVR starts, then wait for VRChat before starting managed apps
- optionally start from SteamVR through a VR app manifest with a small SteamVR-launched control dashboard
- optionally start Intiface and OscGoesBrrr for Lovense workflows
- optionally route local OSC packets to multiple OSC apps

## Included Apps

| App | Purpose |
| --- | --- |
| `PimaxVrcSupervisor.exe` | Console supervisor that watches the VR session and manages apps/devices. |
| `PimaxVrcSupervisorConfigurator.exe` | GUI editor for `supervisor.config.json`. |
| `supervisor.config.json` | Documented configuration file copied next to the executables. |

All shipped executables are stamped with version `1.2.3`.

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

### Base Station Power

- Optional native Bluetooth LE control for SteamVR Base Station 1.0 and 2.0.
- Configurator can scan for stations, rename them, enable/disable rows, add manual rows, and send Power On, Sleep, Standby, and Identify test commands.
- Supervisor powers enabled stations on only after the Pimax headset is connected and SteamVR `vrserver.exe` is running.
- Base stations are not restarted during Pimax, mouth tracker, or other device reconnect handling.
- When OpenVR is available, startup checks SteamVR tracking 10 seconds after each wake cycle and retries up to 5 cycles until all enabled base stations are active.
- If OpenVR is unavailable or cannot be queried, startup sends two normal wake passes; Base Station 1.0 and stations whose firmware does not support power-state reads get a third wake pass 30 seconds later.
- Base Station 2.0 firmware with readable state is checked before wake so already-awake stations are not power-cycled.
- Firmware that reports power-state reads as unsupported is cached per station as `PowerStateReadUnsupported` to speed later launches. Use Configurator **Refresh State** to manually retry detection.
- Session cleanup sends the configured Sleep or Standby command. Console-window close also starts a detached base-station cleanup helper so slow BLE shutdown can finish after the console closes.

### Monitor Management

- Optional first-run prompt for secondary monitor handling.
- Saves the active Windows monitor layout before the session.
- Switches to monitor 1 only when multiple monitors are active.
- Restores the previous layout after VRChat and SteamVR close.

### Auto-Launch Task

- Optional elevated Scheduled Task named `Pimax VRC Supervisor Auto Launch`.
- Starts a hidden watcher at Windows sign-in.
- The watcher launches the supervisor when SteamVR `vrserver.exe` is running; the supervisor waits for VRChat before starting managed apps.
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

### OSC Router

- Optional in-process OSC UDP router for sending VRChat OSC output to multiple local apps.
- Apps keep sending OSC directly to VRChat at `127.0.0.1:9000`.
- Listens for VRChat output on `127.0.0.1:OscRouterReceivePort`, default `127.0.0.1:9001`.
- Forwards every received OSC datagram unchanged to each enabled app receive port at `127.0.0.1`; no OSC address filtering is applied.
- Starts once before Broken Eye and VRCFaceTracking and is independent from SteamVR startup, VRChat waiting, Pimax reconnect restarts, app autostart, and app autoclose.
- If the configured receive endpoint is already in use, the supervisor logs a warning, continues startup with routing disabled temporarily, then lets you press `Space` in the console to retry routing.
- OSCQuery-capable apps can coexist with the router because OSCQuery uses separate discovery and HTTP ports. Do not also route to an app port VRChat already discovered through OSCQuery unless duplicate incoming packets are desired.

### Configurator

- Compact Windows GUI for editing `supervisor.config.json` without changing the config schema.
- Validates expanded executable and folder paths, including paths that use `%APPDATA%` or `%LOCALAPPDATA%`.
- Rechecks executable paths when **Validate** is pressed, so externally deleted or moved files are reported immediately.
- Shows clearer `Found` / `Not found` indicators, keeps status messages from going stale across tabs, and keeps `Save` visually primary.
- Resizes Auto Startup, Base Stations, and OSC Router tables to use available tab space while preserving the bottom status/action bar.

## Requirements

- Windows 10/11
- No separate .NET install when using the self-contained release zip
- Pimax Crystal-compatible headset
- [Broken Eye](https://github.com/ghostiam/BrokenEye)
- [VRCFaceTracking](https://docs.vrcft.io/docs/vrcft-software/vrcft)
- Optional: Vive mouth tracker exposed as `HTC Multimedia Camera`
- Optional: Intiface + OscGoesBrrr for Lovense workflows

## Install

1. Download the right zip from the GitHub release:
   - If you already have the .NET 9 Windows Desktop Runtime installed, download `PimaxVrcSupervisor-v1.2.3_noNET9.zip`.
   - If you do not have .NET 9 installed, download `PimaxVrcSupervisor-v1.2.3.zip`.
2. Extract it somewhere writable, for example:

```text
C:\Tools\PimaxVrcSupervisor
```

3. Choose one initial setup path:
   - 3a. Run `PimaxVrcSupervisor.exe` and answer the first-run prompts.
   - 3b. Use `PimaxVrcSupervisorConfigurator.exe` for the initial config.
4. Use `PimaxVrcSupervisorConfigurator.exe` later if you want to edit paths, timers, detectors, or auto-launch apps without touching JSON by hand.

The supervisor requests administrator privileges through its manifest because some managed tools and monitor actions may require elevation.

## First Run Prompts

On first launch, the supervisor may ask:

- where `Broken Eye.exe` is located
- where `VRCFaceTracking.exe` is located
- whether you use a Vive mouth tracker
- whether to turn off secondary monitors during headset sessions
- whether to create the elevated VRChat/SteamVR auto-launch Scheduled Task
- whether to start with SteamVR through the SteamVR manifest host

Answers are saved to `supervisor.config.json` next to the exe.

## Configurator

Run:

```powershell
.\PimaxVrcSupervisorConfigurator.exe
```

The editor includes tabs for:

- **General**: Startup choices, diagnostics, and editor utilities
- **Face Tracking**: Broken Eye, VRCFaceTracking, mouth-tracker, and fast reconnect settings
- **Base Stations**: scan, rename, enable, test, identify, and power SteamVR base stations
- **OSC Router**: receive endpoint and output routes for local OSC routing
- **Auto Startup**: extra apps to launch with the VR session
- **OSCGoesBrrr**: Intiface, OscGoesBrrr, manual console launch mode, BLE scanner, and Lovense rules
- **Processes**: watched process names and cleanup targets
- **Detectors**: Pimax, mouth tracker, and Lovense detection rules
- **Timers**: poll intervals, startup delays, reconnect waits, and shutdown grace periods
- **Raw JSON**: direct config editing when you need it

## SteamVR Startup

The **Basics** tab has a **Startup** section:

- **Start in CLI mode when SteamVR is running** uses a hidden elevated watcher that starts the supervisor after SteamVR `vrserver.exe` is running. The supervisor waits for the Pimax headset, powers on base stations if enabled, then waits for VRChat before starting managed apps.
- **SteamVR Overlay** registers `PimaxVrcSupervisorSteamVrHost.exe` as a SteamVR dashboard overlay app and creates a separate on-demand elevated helper task. SteamVR starts the host, the host starts the elevated supervisor with `--steamvr-start`, and the supervisor waits for VRChat before starting managed apps.
SteamVR manifest startup exits with SteamVR. When `vrserver.exe` exits, the supervisor powers down base stations if needed, restores monitors, closes managed apps, and exits.

The SteamVR host dashboard includes buttons for restarting Broken Eye/VRCFaceTracking, turning base stations on or off, and restarting the OSC router.

## Desktop TUI

The `cli-ui2` / `1.3.0-test` work adds `PimaxVrcSupervisorTui.exe`, a separate Rust/Ratatui desktop terminal UI for monitoring a running supervisor and running validated regular classic-console actions.

The TUI connects to the existing supervisor backend at `127.0.0.1:37957` and uses the structured `query-json` bridge to display supervisor status, command capabilities, and recent logs. Its executable actions use confirmed `action-json` requests only; it does not send legacy action commands, does not replace the SteamVR dashboard overlay, and does not replace the classic console.

You can close the TUI without stopping the supervisor. If the backend is not running, the TUI opens in a disconnected/backend unavailable state.

Confirmed actions run in the background so the TUI stays responsive. Different safe actions may run at the same time, but the same command cannot be started twice while running and Base Stations On/Off are mutually exclusive. `Q` quits only the Rust TUI; it never cancels backend work, stops the supervisor, sends `force-stop-supervisor`, or runs cleanup routines.

Keybindings:

- `0`: help
- `H` / `h`: help alias on English keyboard layouts
- `F5`: refresh
- `1`: open Restart Core Apps confirmation
- `2`: open Start OSCGoesBrrr confirmation
- `3`: open Base Stations On confirmation
- `4`: open Base Stations Off confirmation
- `5`: open Restart OSC Router confirmation
- `6`: open Reload Autostart Apps confirmation
- `Enter` / `Space`: confirm inside the confirmation modal
- `Esc`: cancel confirmation, close Help, or quit the TUI
- `Up` / `Down`: scroll logs
- `PageUp` / `PageDown`: scroll logs by page
- Mouse wheel: scroll logs
- `Home`: jump to older logs
- `End` / `F`: resume latest log follow
- `Q` / `q`: quit only the Rust TUI from the dashboard; close Help in the Help overlay

Help closes on any key press and consumes that key, so pressing `1` while Help is visible closes Help without opening an action confirmation. Letter shortcuts are displayed uppercase, but lowercase input is also accepted. Selected Russian-layout aliases remain limited to non-help keys. `F1`, `?`, and Russian help aliases do not open TUI help; the main Help overlay keeps those alias details out of the shortcut list. `Q` never stops the supervisor backend, sends shutdown commands, or runs cleanup routines. `force-stop-supervisor` remains blocked from the TUI.

The Configurator refuses to save an Autostart app that duplicates the configured Broken Eye or VRCFaceTracking executable. If a user manually creates that duplicate in JSON, the supervisor warns and skips that Autostart entry at runtime instead of launching the core app twice.

Phase 17 adds a Pimax-inspired dark terminal theme for the TUI with green status accents, clearer action cards, stronger confirmation panels, a cleaner Help overlay, improved running-action/latest-result display, and a more accurate small-terminal fallback. This is visual/operator polish only: no Pimax assets are copied, no backend action behavior changes, and the SteamVR overlay and classic console remain unchanged.

Phase 17B reduces normal operator-screen noise and adds original mouse support. Help, refresh, quit, confirm, and cancel have simple click regions. If mouse capture is unavailable, the TUI continues in keyboard-only mode and still cleans up mouse capture with terminal shutdown.

Phase 17C refines mouse and backend-unavailable behavior. Keyboard `1`-`6` still opens confirmation, but clicking an action card starts that allowed action immediately after the same backend, metadata, duplicate, and Base Stations On/Off conflict checks. Confirmation now uses `Enter`, `Space`, or click Confirm; `Esc` or click Cancel cancels. Backend-unavailable cards show `BACKEND OFF` with muted borders and cannot start actions, even if cached metadata exists from a previous connection. Core Apps shows `WAITING` while the supervisor lifecycle is waiting for VRChat.

Phase 17D makes backend-off action-card state authoritative across all six cards, keeps modal controls clickable but visually neutral, and uses a larger operator layout so full action cards consistently show `click or press N` hints. Recent Logs now follow the newest entries by default; scrolling up pauses live follow, and `End` or `F` resumes the latest log view.

Phase 17E adds adaptive layout tiers: full dashboard at `120x32` or larger, a useful compact dashboard at `100x26` or larger, and a tiny resize fallback below that. Full action cards keep consistent click/press hints with cleaner foreground-only text, compact mode still shows status/actions/latest result/logs, and mouse wheel scrolling works with the same Recent Logs live-follow behavior. No backend or C# behavior changed.

Phase 17F refines the adaptive priority model: full dashboard remains primary, compact remains useful, a small essential dashboard appears at `80x20` or larger, and tiny fallback is used only below that. Status words such as `OK`, `START`, `WAITING`, and `BACKEND OFF` use colored badge backgrounds, while normal labels, values, action names, hints, modal text, and logs stay foreground-only. Backend-disconnected state still overrides every action card or row before cached metadata can show `START`, `BLOCKED`, or `UNAVAILABLE`.

Phase 17G aligns the small `80x20` action grid so action labels and badges scan cleanly in two fixed rows. It is visual polish only: `Q` still quits only the TUI, no supervisor shutdown or tray behavior was added, and future TUI-as-primary lifecycle work remains a separate design phase.

Phase 17H switches TUI status/action badges to bracket text such as `[OK]`, `[START]`, and `[BACKEND OFF]`, and keeps the small `80x20` labels closer to their badges. Normal labels, values, logs, footer text, and modal controls remain foreground-only. Supervisor shutdown, tray behavior, Configurator launch options, and graceful-stop actions remain future work.

## Key Configuration

The release includes a commented `supervisor.config.json`. Important settings:

| Setting | Default | Meaning |
| --- | --- | --- |
| `BrokenEyePath` | empty | Prompts for Broken Eye on first run. |
| `UseBrokenEye` | `true` | Includes Broken Eye in face-tracking startup, cleanup, and restart routines, including manual dashboard restarts. |
| `VrcFaceTrackingPath` | empty | Prompts for VRCFaceTracking on first run. |
| `BrokenEyeStartMinimized` | `false` | Requests/minimizes Broken Eye after launch. |
| `VrcFaceTrackingStartMinimized` | `false` | Requests/minimizes VRCFaceTracking after launch. |
| `MouthTrackerUser` | empty | Empty means ask on first run; true enables mouth tracker monitoring. |
| `TurnOffSecondaryMonitors` | empty | Empty means ask on first run; true enables monitor layout handling. |
| `AutoLaunchScheduledTask` | empty | Empty means ask on first setup; true creates/repairs the task. |
| `StartupLaunchMode` | `Unspecified` | `None`, `ScheduledTask`, or `SteamVrManifest`. SteamVR mode starts when SteamVR starts. |
| `StopWithSteamVr` | `false` | Compatibility field. SteamVR cleanup follows `StartupLaunchMode = SteamVrManifest`. |
| `AutoLaunchApps` | `[]` | Extra apps started after Broken Eye and VRCFaceTracking. |
| `BaseStationsEnabled` | `false` | Enables native SteamVR base-station power automation. |
| `BaseStationPowerDownMode` | `Sleep` | Cleanup command: `Sleep` or `Standby` for Base Station 2.0. Base Station 1.0 falls back to sleep. |
| `BaseStations` | `[]` | Configured base stations. Use the editor to scan and manage these rows. |
| `FaceTrackerAutomationEnabled` | `true` | Automatically starts configured face-tracking applications during headset sessions. |
| `FaceTrackerRestartOnReconnectEnabled` | `true` | Restarts configured face-tracking apps after a Pimax headset reconnect. |
| `MouthTrackerRestartOnReconnectEnabled` | `true` | Restarts VRCFaceTracking after a mouth tracker reconnect while the headset stays connected. |
| `OscGoesBrrrEnabled` | `false` | Enables Intiface/OscGoesBrrr workflow support. |
| `OscGoesBrrrHotkeyEnabled` | `true` | Legacy name for manual console launch mode. Press `2` in a visible console to launch the workflow. |
| `OscGoesBrrrBleScannerEnabled` | `false` | Enables Lovense BLE advertisement scanning. |
| `OscRouterEnabled` | `false` | Enables in-process OSC UDP routing before app launch. |
| `OscRouterReceivePort` | `9001` | Local UDP port the OSC router listens on at `127.0.0.1`. |
| `OscRoutes` | `[]` | Output routes with `Enabled`, `Name`, and `AppReceivePort`. Old `OutputPort` route values still load. |
| `UsePimaxServiceLogReconnectDetector` | `true` | Watches PiService logs for fast runtime reconnects. |
| `UseMouthTrackerPnPReconnectDetector` | `true` | Watches Windows PnP events for fast mouth tracker reconnects. |
| `PollIntervalSeconds` | `2` | Normal device/process polling interval. |
| `RestartDelayAfterReconnectSeconds` | `10` | Stability wait before restarting apps after Pimax reconnect. |
| `DiagnosticsLogSupervisor` | `false` | Writes passive supervisor performance diagnostics when enabled. |
| `DiagnosticsLogSteamVrOverlay` | `false` | Writes passive SteamVR dashboard host diagnostics when enabled. |
| `DiagnosticsDebugSupervisor` | `false` | Writes supervisor debug-event logs. |
| `DiagnosticsDebugSteamVrOverlay` | `false` | Writes SteamVR overlay debug-event logs. |
| `DiagnosticsDebugSteamVrPointer` | `false` | Draws the visible SteamVR overlay pointer marker for hover hit-test troubleshooting when overlay debug logging is enabled. |
| `DiagnosticsVerbose` | `false` | Adds per-operation timing lines for short test captures. |
| `DiagnosticsSummaryIntervalSeconds` | `20` | Seconds between diagnostic summaries. |
| `DiagnosticsLogDirectory` | `%TEMP%\PimaxVrcSupervisorDiagnostics` | Folder for diagnostic text logs. |

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

`PowerStateReadUnsupported` is set automatically when a station or firmware does not support reading power state. The supervisor skips future state reads for that station; Configurator **Refresh State** can retry detection manually.

Example OscGoesBrrr workflow:

```json
"OscGoesBrrrEnabled": true,
"OscGoesBrrrHotkeyEnabled": true,
"OscGoesBrrrBleScannerEnabled": false,
"IntifacePath": "%APPDATA%\\IntifaceCentral\\intiface_central.exe",
"OscGoesBrrrPath": "%LOCALAPPDATA%\\Programs\\OscGoesBrrr\\OscGoesBrrr.exe",
"DelayBeforeOscGoesBrrrSeconds": 3,
"LovenseDetectors": [
  [ "Lovense" ],
  [ "LVS-" ]
]
```

## Release Verification

GitHub Actions publishes signed and attested release assets when a `v*` tag is pushed or the `Release` workflow is run manually.

Expected release files:

- `PimaxVrcSupervisor-v1.2.3.zip`
- `PimaxVrcSupervisor-v1.2.3.zip.sha256`
- `PimaxVrcSupervisor-v1.2.3.zip.sigstore.json`

Verify the checksum:

```powershell
Get-FileHash .\PimaxVrcSupervisor-v1.2.3.zip -Algorithm SHA256
Get-Content .\PimaxVrcSupervisor-v1.2.3.zip.sha256
```

Verify the Sigstore bundle with cosign:

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.2.3.zip `
  --bundle .\PimaxVrcSupervisor-v1.2.3.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/(heads|tags)/.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

Sigstore verification proves the zip was signed by the repository workflow and recorded in the transparency log. It does not make Windows SmartScreen show the app as a verified publisher unless Authenticode code-signing secrets are configured for the workflow.

## Build From Source

Install the .NET 9 SDK, then run:

```powershell
dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.2.3
dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.2.3
dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.2.3
```

The output folder will contain both executables, the config file, and this README.

For the `cli-ui2` / `1.3.0-test` read-only desktop TUI, install the Rust stable MSVC toolchain. Visual Studio Build Tools with the C++ workload may also be required on Windows.

Build the TUI with:

```powershell
cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml
cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release
```

After the C# publish and Rust release build, copy the local test TUI executable into the release folder with:

```powershell
Copy-Item .\PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe `
  .\release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe `
  -Force
```

## Troubleshooting

| Symptom | Try |
| --- | --- |
| The supervisor exits immediately | Check whether another normal supervisor instance is already running. |
| Broken Eye or VRCFaceTracking does not launch | Open the configurator and verify the executable path and process name. |
| Reconnects are not detected | Confirm `PimaxDetectors`, `UsePimaxServiceLogReconnectDetector`, and the PiService log directory. |
| Mouth tracker reconnects do nothing | Set `MouthTrackerUser` to `true` and verify `MouthTrackerDetectors`. |
| Monitors are not restored | Let SteamVR fully exit; the supervisor waits for `vrserver.exe` before restoring when monitor handling is enabled. |
| Base stations do not scan | Confirm Windows Bluetooth LE is enabled and try the Base Stations tab **Scan** button again. Add a manual row if Windows discovery exposes the address elsewhere. |
| Base stations wake slowly | Keep `PowerStateReadUnsupported` enabled for unsupported firmware. When OpenVR is available, SteamVR tracking can stop retries early; otherwise the supervisor sends a third delayed wake pass only to V1/unsupported stations. |
| Base stations stay on after console X | Use the latest release; console close starts a detached helper that sends the configured Sleep/Standby command after the main console exits. |
| OscGoesBrrr does not start | Check `OscGoesBrrrEnabled`, the Intiface/OscGoesBrrr paths, and whether manual console launch mode or BLE scanner mode is enabled. |

## Documentation

The full user manual and technical reference are available online:

- [Documentation Home](docs/index.md)
- [GUI Manual](docs/gui/index.md)
- [VR Overlay](docs/overlay/index.md)
- [Reference](docs/reference/index.md)
- [Troubleshooting](docs/troubleshooting/index.md)

## Notes

- This is a Windows-only utility.
- `PimaxVrcSupervisor-v1.2.3.zip` is the full self-contained release and includes the Windows .NET runtime.
- `PimaxVrcSupervisor-v1.2.3_noNET9.zip` is smaller and requires the .NET 9 Windows Desktop Runtime to already be installed.
- The app edits only its nearby `supervisor.config.json` unless you choose a different config path in the editor.
