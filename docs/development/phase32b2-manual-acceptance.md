# Phase32B2 Manual Acceptance

Use this checklist with a Phase32B2 test deployment. Do not overwrite a Phase32B1 deployment while testing.

## Pass A - Classic Console

1. Start SteamVR and Supervisor from the test deployment.
2. Start VRChat.
3. Press `F1` and confirm shortcut `7` describes SteamVR control.
4. Press `7`.
5. Confirm Supervisor reports that the SteamVR restart operation is running.
6. Confirm SteamVR restarts through Steam, Supervisor remains alive, base stations remain powered, and Supervisor-owned monitor topology is preserved during the restart window.
7. Confirm VRChat resumes through Steam after replacement SteamVR is healthy and managed apps return.

## Pass B - Terminal UI

1. Open `PimaxVrcSupervisorTui.exe` from the same deployment.
2. Confirm action row `7 SteamVR` is visible as the third full-width action row.
3. Press `7`, confirm with `Enter`, and verify the action is accepted.
4. Close and reopen Terminal UI during the restart, if practical, and confirm current action or last result is visible after reconnect.

## Pass C - SteamVR Overlay

1. Start through SteamVR Overlay mode.
2. Confirm `Restart SteamVR` is visible as the third full-width row in the overlay.
3. Click `Restart SteamVR` once and confirm the button asks for a second click.
4. Click it again within 10 seconds.
5. Confirm the overlay sends the Supervisor `restart-vr-session` action and may disappear while SteamVR restarts.
6. After SteamVR returns, confirm the overlay reconnects and shows Supervisor status.

## Pass D - No VRChat Resume

1. Start SteamVR with Supervisor running but keep VRChat closed.
2. Trigger Restart SteamVR from any one surface.
3. Confirm the action does not launch VRChat.
4. Confirm replacement SteamVR is adopted and VRChat is not launched.

## Failure Checks

- Triggering the action while SteamVR is absent is rejected.
- Triggering the action again while one is active is rejected.
- A failed restart clears the busy state and leaves Supervisor controls available.
- A failed restart does not power off base stations or restore monitors solely because the restart failed.
- Explicit Supervisor exit and normal final session cleanup still take precedence.
