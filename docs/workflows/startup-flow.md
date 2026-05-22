# Startup Flow

This page describes the complete startup sequence of the supervisor from launch to the main monitoring loop.

## Command-Line Arguments

| Argument | Behavior |
| --- | --- |
| *(none)* | Normal manual startup. Waits for headset, starts apps, monitors VRChat. |
| `--steamvr-start` | SteamVR manifest mode. Starts immediately, monitors SteamVR instead of VRChat. |
| `--install-auto-launch-task` | Installs/updates the elevated auto-launch Scheduled Task. |
| `--apply-startup-integration` | Applies SteamVR manifest or Scheduled Task changes. |
| `--watch-vrchat-auto-launch` | Runs the hidden auto-launch watcher (used by the Scheduled Task). |
| `--config <path>` | Loads config from a specific path instead of the default. |
| `--emergency-base-station-cleanup <configPath>` | Runs detached base station cleanup on console close. |

## Startup Sequence

### 1. Initialization

1. Parse command-line arguments.
2. Load `supervisor.config.json`.
3. Check for duplicate instances (mutex `Local\PimaxVrcSupervisor`).
4. If `--steamvr-start`, hide the console window.
5. Install console log tee (captures last 80 lines for status display).

### 2. First-Run Prompts

If settings are not configured, the supervisor prompts via message boxes:

- **Broken Eye path** — Browse for `Broken Eye.exe`.
- **VRCFaceTracking path** — Browse for `VRCFaceTracking.exe`.
- **Mouth tracker** — "Do you use Vive mouth tracker?"
- **Secondary monitors** — "Turn off secondary monitors during headset sessions?"
- **Auto-launch task** — "Create an elevated Windows Scheduled Task?"

Answers are saved to `supervisor.config.json`.

### 3. SteamVR Check (SteamVR Mode Only)

If `--steamvr-start` is set, verify that `vrserver.exe` is running. If not, exit immediately.

### 4. Headset Wait

Poll for Pimax Crystal connection using the configured `PimaxDetectors`. The supervisor waits indefinitely until the headset is detected.

### 5. Initial Base Station Power-On

Send the first base station wake pass (pass 1 of 3) after the headset is connected.

### 6. Mouth Tracker Check

If `MouthTrackerUser` is `true`, check for mouth tracker presence and log the state.

### 7. OSC Router Start

If `OscRouterEnabled` is `true`, start the in-process OSC UDP router. If the port is already in use, log a warning and continue (retry later with `Space`).

### 8. Managed App Start

1. Start **Broken Eye** with up to 10 retry attempts (5-second intervals).
2. Wait `DelayBeforeVrcFaceTrackingSeconds` (default 5).
3. Start **VRCFaceTracking**.
4. Start configured **auto-launch apps**.

### 9. OscGoesBrrr Workflow Init

- If BLE scanner is enabled, start the background scanner.
- If hotkey is enabled, show "Press L to launch OSCGoesBrrr."
- Otherwise, start Intiface immediately and check for Lovense devices.

### 10. Final Base Station Power-On

Send remaining base station wake passes (passes 2 and 3 if needed).

### 11. Main Loop

Enter the polling loop:

1. Check console hotkeys (`L`, `Space`, `R`).
2. Check base station power-on progress.
3. If SteamVR mode: check if `vrserver.exe` is still running.
4. If VRChat mode: observe watched shutdown processes.
5. Poll Pimax headset connection.
6. Poll mouth tracker connection (if enabled).
7. Poll Lovense connection (if enabled and not using hotkey/BLE).
8. Handle reconnects.
9. Sleep for `PollIntervalSeconds`.

## Shutdown Triggers

| Mode | Trigger |
| --- | --- |
| VRChat | `VRChat.exe` exits (with crash grace period). |
| SteamVR | `vrserver.exe` exits. |
| Console close | Emergency cleanup helper launched. |
| Ctrl+C | Emergency cleanup. |

See also: [Workflows Overview](index.md) · [Headset Detection](headset-detection.md) · [Base Station Power](base-station-power.md)
