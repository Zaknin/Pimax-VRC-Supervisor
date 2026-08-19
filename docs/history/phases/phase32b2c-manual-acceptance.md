# Phase32B2C Manual Acceptance

Use this checklist with the external Phase32B2C test deployment. These are manual hardware/session tests to perform; automated validation does not claim them as complete.

## Pass 1 - TUI modal controls

1. Start Supervisor and Terminal UI with SteamVR stopped.
2. Activate action `7` and verify visible **Confirm** and **Cancel** buttons.
3. Click **Cancel** and verify SteamVR remains stopped.
4. Activate action `7` again, click **Confirm**, and verify SteamVR starts without VRChat.
5. Repeat with Tab, Left, Right, Enter, Space, and Esc.
6. Resize through wide, medium, and compact layouts. Verify labels remain visible and each mouse hitbox matches its displayed button.
7. Open Help and verify its visible **Close** button works with mouse, Enter, Space, and Esc.
8. Verify actions `1` through `6` remain immediate and no Action Result popup opens.

## Pass 2 - Restart without VRChat

1. Start SteamVR without VRChat.
2. Record the PID and creation time for `vrserver`, `vrcompositor`, and `vrmonitor`.
3. Activate Restart SteamVR and confirm with the mouse.
4. Verify the graceful shutdown stage begins and the old `vrserver` identity disappears.
5. Verify a different `vrserver` identity appears and becomes ready.
6. Verify VRChat remains absent and the last result says `SteamVR restarted.`
7. Verify action `7` returns to Restart and is not BUSY.
8. Verify no result popup opens, then perform a second restart.

## Pass 3 - Restart with VRChat

1. Start VRChat and the configured managed applications.
2. Record the SteamVR and VRChat process identities.
3. Restart SteamVR.
4. Verify VRChat closes through normal lifecycle handling and the old SteamVR runtime exits.
5. Verify a new SteamVR runtime starts and reaches ready state.
6. Verify VRChat returns automatically in VR mode and managed applications recover.
7. Verify base stations remain on and monitors remain in VR-session topology.
8. Verify the concise result says `SteamVR restarted and VRChat resumed.`
9. Exit SteamVR normally and verify managed-app cleanup, configured base-station power-down, monitor restoration, and normal Supervisor/TUI exit behavior.

## Pass 4 - Graceful-shutdown refusal or timeout

Perform this pass only when a safe reproducible refusal is available.

1. Cause or simulate a graceful-shutdown refusal.
2. Verify no force-kill occurs and no second runtime launch is attempted.
3. Verify a bounded failure result and cleared BUSY state.
4. Verify Supervisor controls remain available and the action can be retried.
5. If the old runtime exited but replacement start failed, verify action `7` changes to Start and monitor/base-station state remains preserved for manual recovery.

## Pass 5 - SteamVR overlay

1. Start SteamVR and VRChat, then open the Supervisor overlay.
2. Confirm Restart SteamVR from the overlay.
3. Verify the old overlay disappears while Supervisor continues the operation.
4. Verify the replacement runtime and overlay host return.
5. Verify VRChat resumes and the reconnected overlay can read current progress or the terminal result.
6. Verify no automatic result popup opens and a second restart remains possible.

## Pass 6 - Classic Supervisor console

1. Verify action `7` is Start while SteamVR is stopped and Restart while it is running.
2. Cancel once and verify no operation starts.
3. Confirm restart and verify the old identity exits before a new identity appears.
4. Verify conditional VRChat resume and concise progress/terminal output.
5. Verify actions `1` through `6` remain immediate.
6. Verify a later normal SteamVR exit still performs complete cleanup.
