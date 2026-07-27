# Session Flow

This page explains what usually happens during a VRChat session.

## Start

1. SteamVR starts.
2. Supervisor starts through your chosen startup mode or manual launch.
3. Supervisor waits for the Pimax headset and VRChat conditions.
4. Enabled base stations can be powered on after SteamVR is available.
5. Face-tracking and helper tools start.
6. Optional OSC and OscGoesBrrr workflows start.

## During A Session

Supervisor watches configured process names, physical-device inventory, headset readiness, and helper state. USB/PnP notifications are coalesced into inventory rescans. Only a positively attributed disconnect and return of the same relevant device, followed by readiness, can create a scoped recovery plan. Index-controller dongles, Vive Tracker dongles, arbitrary USB changes, and unknown devices cause no managed-application restart. XSOverlay is not part of device reconnect recovery.

Action `7 SteamVR` is dynamic in the classic console and Terminal UI. When SteamVR is stopped, it sends the explicit `start-steamvr` command after confirmation and does not launch VRChat. When SteamVR is running, it sends the explicit `restart-vr-session` command after confirmation. Supervisor captures whether VRChat is running when the restart is accepted, asks Steam to restart SteamVR, waits for the runtime transition, and resumes VRChat through Steam only when VRChat was running at acceptance.

Each Restart confirmation creates one logical request. Duplicate delivery or client reconnect observes that request instead of running it again, and one accepted request can create at most one SteamVR shutdown helper. A second restart requires a new confirmation and a new request identity. USB/PnP recovery never creates an action-7 request.

An accepted SteamVR action means the Supervisor owns the operation and progress is available in the action status. It is not a completed result. A terminal result appears only after start or restart completion, warning, failure, timeout, or Supervisor shutdown.

If the restart does not complete inside the bounded restart window, Supervisor clears the busy action state and leaves controls available. It does not power off base stations or restore monitors solely because the explicit restart failed. A later explicit Supervisor exit or normal session cleanup still owns final cleanup.

## End

When VRChat and/or SteamVR exits according to your configured mode, Supervisor runs cleanup:

- closes managed tools
- powers down base stations if enabled
- restores monitors only if Supervisor successfully disabled secondary monitors during the current session
- exits if the selected startup mode expects it to exit

Terminal UI and SteamVR Overlay are control surfaces. The Supervisor performs the session work.

Terminal UI has explicit exit choices. **Close TUI only** detaches the dashboard and does not enter Supervisor cleanup. **Exit Supervisor - Keep Base Stations On** runs Supervisor exit cleanup while suppressing base-station power-off. **Exit Supervisor - Turn Base Stations Off** runs the normal cleanup path. Explicit Supervisor exits suppress scheduled Watcher relaunch for the same SteamVR `vrserver` PID/start-time identity; a later SteamVR session can launch normally.
