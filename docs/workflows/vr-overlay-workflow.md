# VR Overlay Workflow

This page describes the SteamVR dashboard overlay workflow and how it interacts with the supervisor.

## Components

| Component | Executable | Role |
| --- | --- | --- |
| Dashboard Host | `PimaxVrcSupervisorSteamVrHost.exe` | SteamVR dashboard overlay. Renders buttons, sends commands. |
| Supervisor | `PimaxVrcSupervisor.exe` | Main supervisor process. Receives commands, manages session. |
| Helper Task | "Pimax VRC Supervisor SteamVR Start" | Elevated Scheduled Task that starts the supervisor. |

## Startup Flow

1. SteamVR starts and launches the dashboard host (registered via SteamVR manifest).
2. The host requests the elevated supervisor via `schtasks.exe /Run /TN "Pimax VRC Supervisor SteamVR Start"`.
3. The supervisor starts with `--steamvr-start` and hides its console window.
4. The host creates the OpenVR dashboard overlay (1500×900 pixels, 2.5m wide).
5. The host begins polling supervisor status (every 5 seconds) and console output (every 2 seconds).

## Command Flow

1. User clicks a button in the VR overlay.
2. The host resolves the click to a command (e.g., `restart-core-apps`).
3. The host sends the command to the supervisor via TCP (`127.0.0.1:37957`) or named pipe (`PimaxVrcSupervisor.Command`).
4. The supervisor executes the command and returns a response.
5. The host displays the response in the status line.

### Command Protocol

The host and supervisor communicate using a simple text protocol:

- **Request:** Single line text command (e.g., `status`, `restart-core-apps`).
- **Response:** Single line text response (e.g., `Mode=SteamVR; SteamVR=running; CoreApps=running; ...`).

### Fallback

The host tries TCP first for commands. If TCP fails, it falls back to the named pipe. For console refresh (`log` command), it tries the pipe first.

## Dashboard Buttons

| Button | Command | Description |
| --- | --- | --- |
| Restart VRC face tracking | `restart-core-apps` | Restarts Broken Eye and VRCFaceTracking. |
| Restart OSC router | `restart-osc-router` | Stops and restarts the OSC UDP router. |
| OSCGoesBrr | `start-osc-goes-brrr` | Launches the Intiface/OscGoesBrrr workflow. |
| Base stations on | `base-stations-on` | Powers on all enabled base stations. |
| Base stations off | `base-stations-off` | Powers down all enabled base stations. |

## Shutdown Flow

1. SteamVR shuts down (`vrserver.exe` exits).
2. The host loop detects SteamVR is no longer running.
3. The host disposes the overlay and exits.
4. The supervisor detects SteamVR exit and runs cleanup:
   - Powers down base stations.
   - Restores monitors.
   - Closes managed apps.
   - Exits.

## Logging

The host logs to `%TEMP%\PimaxVrcSupervisorSteamVrHost.log`. Each entry includes a timestamp and message. The log captures:
- Overlay creation and OpenVR events
- Button clicks with coordinates
- Command execution and responses
- Errors and fallback behavior

When `DiagnosticsLogSteamVrOverlay` is enabled, the host also writes passive performance diagnostics to `%TEMP%\PimaxVrcSupervisorDiagnostics` or `DiagnosticsLogDirectory`. These summaries include hidden/active loop intervals, status/log IPC latency, render time, D3D upload and flush time, OpenVR texture submit time, process CPU, RAM, GC, thread, and handle counts.

## Console Output in VR

The overlay displays the last lines of supervisor console output, refreshed every 2 seconds. Lines are fetched via the `log` command, which returns the last 14 lines from the supervisor's internal console buffer (max 80 lines).

See also: [Workflows Overview](index.md) · [Auto Launch](auto-launch.md) · [VR Overlay Controls](../overlay/controls.md)
