# VR Overlay Configuration

The SteamVR dashboard overlay is configured through the Config Editor's **General** tab and the SteamVR manifest system.

## Enabling SteamVR Startup

To use the overlay, you must enable SteamVR startup mode:

1. Open the Config Editor.
2. Go to the **General** tab.
3. In the **Startup** section, check **Start with SteamVR**.
4. Save the config.

This registers `PimaxVrcSupervisorSteamVrHost.exe` as a SteamVR dashboard overlay app and creates an on-demand elevated helper Scheduled Task named "Pimax VRC Supervisor SteamVR Start".

## How It Works

1. SteamVR starts and launches the dashboard host (`PimaxVrcSupervisorSteamVrHost.exe`).
2. The host requests the elevated supervisor to start via the helper Scheduled Task.
3. The supervisor starts with `--steamvr-start` and hides its console window.
4. The host creates a SteamVR dashboard overlay and begins polling for status and console output.
5. When SteamVR shuts down, the host loop exits and the supervisor performs cleanup.

## Manifest Registration

The SteamVR manifest is created/updated when:

- You save the config with "Start with SteamVR" enabled.
- You run `PimaxVrcSupervisor.exe --apply-startup-integration`.

The manifest registers:
- **App key:** `pimax.vrcsupervisor.dashboard`
- **Overlay name:** "Pimax VRC Supervisor"
- **Overlay dimensions:** 1500 × 900 pixels, 2.5 meters wide

## Overlay Settings

| Setting | Value | Description |
| --- | --- | --- |
| Overlay width | 1500 px | Panel width in pixels. |
| Overlay height | 900 px | Panel height in pixels. |
| Physical width | 2.5 m | Width in VR space. |
| Button size | 366 × 126 px | Each dashboard button. |
| Button gap | 28 px horizontal, 26 px vertical | Spacing between buttons. |
| Console refresh | 2 seconds | How often console output is fetched. |
| Status refresh | 5 seconds | How often status is polled. |

## Command Communication

The overlay host communicates with the supervisor through two channels:

| Channel | Endpoint | Priority |
| --- | --- | --- |
| TCP | `127.0.0.1:37957` | Primary for commands |
| Named Pipe | `PimaxVrcSupervisor.Command` | Fallback for commands, primary for console refresh |

The host tries TCP first for commands. If TCP fails, it falls back to the named pipe. For console refresh (`log` command), it tries the pipe first.

## Logging

The host writes logs to `%TEMP%\PimaxVrcSupervisorSteamVrHost.log`. This file includes:
- Overlay creation events
- Button clicks and command execution
- OpenVR event details (for debugging)
- Errors and fallback behavior

## Disabling

To disable the overlay:

1. Open the Config Editor.
2. Go to the **General** tab.
3. Uncheck **Start with SteamVR** (or set startup mode to `None`).
4. Save the config.

This removes the SteamVR manifest and deletes the helper Scheduled Task.

See also: [VR Overlay Overview](index.md) · [VR Overlay Controls](controls.md) · [VR Overlay Troubleshooting](troubleshooting.md)
