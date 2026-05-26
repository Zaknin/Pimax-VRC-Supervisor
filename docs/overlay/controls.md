# VR Overlay Controls

The SteamVR dashboard overlay provides in-VR controls for the supervisor through a dashboard panel that appears when you open the SteamVR dashboard.

## What the Overlay Is

The overlay is a SteamVR dashboard application registered by `PimaxVrcSupervisorSteamVrHost.exe`. It renders a 1500×900 pixel panel with buttons for common supervisor actions and a live view of the supervisor console output.

## Dashboard Buttons

The overlay has 5 buttons arranged in a 3×2 grid:

| Button | Command | Description |
| --- | --- | --- |
| Restart VRC face tracking | `restart-core-apps` | Restarts Broken Eye and VRCFaceTracking. |
| Restart OSC router | `restart-osc-router` | Stops and restarts the OSC UDP router. |
| OSCGoesBrr | `start-osc-goes-brrr` | Launches the Intiface/OscGoesBrrr workflow. |
| Base stations on | `base-stations-on` | Powers on all enabled base stations. |
| Base stations off | `base-stations-off` | Powers down all enabled base stations. |

### Button Layout

```
+-------------------------+-------------------------+-------------------------+
| Restart VRC face        | Restart OSC router      | OSCGoesBrr              |
| tracking                |                         |                         |
+-------------------------+-------------------------+-------------------------+
| Base stations on        | Base stations off       |                         |
+-------------------------+-------------------------+
```

## Status Display

Below the buttons, the overlay shows a status line summarizing:

- Core apps state (running / incomplete)
- OSC router state (running / stopped)
- Base stations state (count and power state)

## Console Output

Below the status, a console panel shows the last lines of supervisor output, refreshed every 2 seconds. Lines longer than 142 characters are wrapped. Up to 11 lines are displayed.

## How to Use

1. Open the SteamVR dashboard (press the system button on your controller).
2. Look for the "Pimax VRC Supervisor" tab.
3. Click any button to send the corresponding command.
4. The status line updates to show the command result.

## Command Flow

1. You click a button in the VR overlay.
2. The host sends the command to the supervisor via TCP (`127.0.0.1:37957`).
3. The supervisor executes the command and returns a response.
4. The host displays the response in the status line.

## Button State Feedback

- While a command is running, the button shows "Running..." with a highlighted border.
- If a command is already in flight, subsequent clicks are ignored with an "Already running a command..." status.

See also: [VR Overlay Overview](index.md) · [VR Overlay Configuration](configuration.md) · [VR Overlay Troubleshooting](troubleshooting.md)
