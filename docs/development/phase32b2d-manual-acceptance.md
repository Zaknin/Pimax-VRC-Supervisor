# Phase32B2D Manual Acceptance

Use this checklist with the external Phase32B2D test deployment. These are live hardware and session tests to perform; automated validation does not claim them as complete.

## Pass 1 - Restart without VRChat

1. Start SteamVR without VRChat.
2. Record the PID and creation time for `vrserver`, `vrcompositor`, and `vrmonitor`.
3. Invoke Restart SteamVR and confirm with the mouse.
4. Verify the graceful shutdown request is issued.
5. Verify the operation does not fail after ten seconds merely because `vrstartup.exe` has not acknowledged or exited.
6. Verify the exact old `vrserver` identity disappears.
7. Verify Supervisor invokes SteamVR Start automatically.
8. Verify a different `vrserver` identity appears and becomes ready.
9. Verify VRChat remains absent and Last Result says `SteamVR restarted.`
10. Verify action `7` returns to Restart and no permanent BUSY state remains.
11. Perform a second complete restart.

## Pass 2 - Restart with VRChat

1. Start VRChat and the configured managed applications.
2. Record the runtime and application identities.
3. Restart SteamVR.
4. Verify VRChat closes through existing lifecycle handling.
5. Verify the old SteamVR runtime disappears and the replacement starts automatically.
6. Verify VRChat launches automatically in VR mode without a duplicate launch.
7. Verify managed applications recover through the existing lifecycle.
8. Verify enabled base stations remain powered and monitors remain in VR-session topology.
9. Verify Last Result says `SteamVR restarted and VRChat resumed.`
10. Verify another restart is available.

## Pass 3 - Normal final shutdown

1. After a successful restart, exit SteamVR normally.
2. Verify managed applications close.
3. Verify configured base stations turn off.
4. Verify Supervisor-owned monitor changes restore.
5. Verify Supervisor and Terminal UI exit according to the configured mode.
6. Verify only Watcher remains where configured.

## Pass 4 - Old-runtime refusal or timeout

Use a safe deterministic simulation or controlled condition.

1. Keep the captured old runtime identity present.
2. Verify no replacement launch occurs.
3. Verify the old-runtime wait ends at its bounded deadline.
4. Verify no process is force-killed.
5. Verify controls remain available and action state clears.
6. Verify retry or ordinary manual recovery remains possible.

## Pass 5 - SteamVR overlay

1. Start SteamVR and VRChat, then open the Supervisor overlay.
2. Initiate and confirm Restart SteamVR from the overlay.
3. Verify the old overlay disappears without canceling the Supervisor-owned operation.
4. Verify the old runtime exits and the replacement starts automatically.
5. Verify the overlay returns and VRChat resumes.
6. Verify a reconnected overlay can read current progress or the final result.
7. Verify another restart remains possible.

## Pass 6 - Modal regression

1. Verify visible Confirm and Cancel buttons remain mouse-clickable.
2. Verify Tab, Left, Right, Enter, Space, and Esc.
3. Verify wide, medium, and compact Terminal UI layouts.
4. Verify actions `1` through `6` remain immediate.
5. Verify no automatic Action Result popup appears.
