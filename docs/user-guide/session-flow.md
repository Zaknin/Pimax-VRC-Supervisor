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

Supervisor watches configured process names, headset reconnect signals, and helper state. If reconnect handling is enabled, it can restart face-tracking tools after a Pimax reconnect.

The `Restart VR Session` action is available from the classic console, Terminal UI, and SteamVR Overlay. All three surfaces send the same Supervisor command, `restart-vr-session`; they do not run separate restart workflows. Supervisor captures the current SteamVR PID/start-time identity and whether VRChat is running when the action is accepted. It then asks Steam to restart SteamVR, waits for the old runtime to disappear, waits for a replacement runtime, and resumes VRChat through Steam only when VRChat was running at acceptance.

If the restart does not complete inside the bounded restart window, Supervisor clears the busy action state and leaves controls available. It does not power off base stations or restore monitors solely because the explicit restart failed. A later explicit Supervisor exit or normal session cleanup still owns final cleanup.

## End

When VRChat and/or SteamVR exits according to your configured mode, Supervisor runs cleanup:

- closes managed tools
- powers down base stations if enabled
- restores monitors only if Supervisor successfully disabled secondary monitors during the current session
- exits if the selected startup mode expects it to exit

Terminal UI and SteamVR Overlay are control surfaces. The Supervisor performs the session work.

Terminal UI has explicit exit choices. **Close TUI only** detaches the dashboard and does not enter Supervisor cleanup. **Exit Supervisor - Keep Base Stations On** runs Supervisor exit cleanup while suppressing base-station power-off. **Exit Supervisor - Turn Base Stations Off** runs the normal cleanup path. Explicit Supervisor exits suppress scheduled Watcher relaunch for the same SteamVR `vrserver` PID/start-time identity; a later SteamVR session can launch normally.
