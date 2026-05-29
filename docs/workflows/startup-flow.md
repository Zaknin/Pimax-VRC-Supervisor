# Startup Flow

This page describes the complete startup sequence of the supervisor from launch to the main monitoring loop.

## Command-Line Arguments

| Argument | Behavior |
| --- | --- |
| *(none)* | Normal manual startup. Waits for headset, waits for VRChat, starts apps, monitors VRChat. |
| `--steamvr-start` | SteamVR manifest mode. Starts with SteamVR, waits for headset and VRChat, exits with SteamVR. |
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

- **Broken Eye path** â€” Browse for `Broken Eye.exe`.
- **VRCFaceTracking path** â€” Browse for `VRCFaceTracking.exe`.
- **Mouth tracker** â€” "Do you use Vive mouth tracker?"
- **Secondary monitors** â€” "Turn off secondary monitors during headset sessions?"
- **Auto-launch task** â€” "Create an elevated Windows Scheduled Task?"

Answers are saved to `supervisor.config.json`.

### 3. SteamVR Check

If `--steamvr-start` is set, or the effective startup mode is `ScheduledTask`, verify that `vrserver.exe` is running. If not, exit immediately.

### 4. Headset Wait

Poll for Pimax Crystal connection using the configured `PimaxDetectors`. The supervisor waits indefinitely until the headset is detected.

### 5. Base Station Power-On

Run the full base-station startup routine after the headset is connected and before waiting for VRChat. When OpenVR tracking confirmation is available, the supervisor retries startup cycles until the enabled stations are active or the startup cycle limit is reached.

### 6. VRChat Wait

Wait for the configured watched process names, normally `VRChat.exe`, before starting managed apps. In SteamVR-bound modes, exit and clean up if `vrserver.exe` exits before VRChat starts.

### 7. Mouth Tracker Check

If `MouthTrackerUser` is `true`, check for mouth tracker presence and log the state.

### 8. OSC Router Start

If `OscRouterEnabled` is `true`, start the in-process OSC UDP router. If the port is already in use, log a warning and continue. In a visible console, hotkey `5` can launch or restart the OSC router later.

### 9. Managed App Start

1. Start **Broken Eye** with up to 10 retry attempts (5-second intervals).
2. Wait `DelayBeforeVrcFaceTrackingSeconds` (default 3).
3. Start **VRCFaceTracking**.
4. Start configured **auto-launch apps**.

### 10. OscGoesBrrr Workflow Init

- If BLE scanner is enabled, start the background scanner.
- If manual console launch mode is enabled, leave OscGoesBrrr waiting for console hotkey `2`.
- Otherwise, start Intiface immediately and check for Lovense devices.

### 11. Main Loop

Enter the polling loop:

1. Check console hotkeys (`1`-`6`, `F1`) when a visible console has focus.
2. In SteamVR-bound modes: check if `vrserver.exe` is still running.
3. Observe watched shutdown processes.
4. Poll Pimax headset connection.
5. Poll mouth tracker connection (if enabled).
6. Poll Lovense connection (if enabled and not using manual console launch mode/BLE).
7. Handle reconnects.
8. Sleep for `PollIntervalSeconds`.

## Shutdown Triggers

| Mode | Trigger |
| --- | --- |
| VRChat | `VRChat.exe` exits (with crash grace period); managed apps are closed. |
| SteamVR | `vrserver.exe` exits; SteamVR-bound modes power down base stations and exit. |
| Console close | Emergency cleanup helper launched. |
| Ctrl+C | Emergency cleanup. |

See also: [Workflows Overview](index.md) Â· [Headset Detection](headset-detection.md) Â· [Base Station Power](base-station-power.md)
