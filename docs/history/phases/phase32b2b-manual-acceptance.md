# Phase32B2B Manual Acceptance

Use this checklist with a Phase32B2B test deployment. Do not overwrite Phase32B2 or Phase32B2A deployments while testing.

## Pass 1 - TUI, SteamVR Stopped

1. Start Supervisor and Terminal UI with SteamVR stopped.
2. Confirm action `7 SteamVR` shows `Start SteamVR` and `[START]` even if the status text includes `not running`.
3. Confirm actions `1` through `6` execute immediately from keyboard and mouse without any confirmation dialog.
4. Press `7`, verify the Start SteamVR confirmation, then cancel and confirm no Action Result popup opens.
5. Press `7` again, confirm, and verify SteamVR starts without launching VRChat.
6. Confirm completion appears in Activity, Last Result, logs, and shared action state without opening an Action Result popup.

## Pass 2 - TUI, SteamVR Running

1. Start SteamVR and confirm action `7 SteamVR` shows `Restart SteamVR` and `[RESTART]`.
2. Press `7`, cancel, and confirm SteamVR process identity does not change and no Action Result popup opens.
3. Click action `7`, verify the Restart SteamVR confirmation appears instead of immediate execution.
4. Confirm the restart, verify SteamVR restarts, base stations remain on, monitors remain in VR mode, and completion is non-modal.
5. Reconnect or refresh Terminal UI after completion and verify the backend last action result does not open a popup.

## Pass 3 - Busy And Disconnected States

1. While a confirmed Start SteamVR action is running, verify action `7` shows `[BUSY]` and `Starting SteamVR`.
2. While a confirmed Restart SteamVR action is running, verify action `7` shows `[BUSY]` and `Restarting SteamVR`.
3. Stop Supervisor while Terminal UI is open and verify action `7` shows `[DISCONNECTED]` and `Could not contact Supervisor`.
4. Confirm no stale displayed `Start` or `Restart` command is reinterpreted after reconnect.

## Pass 4 - Classic Console

1. With SteamVR stopped, press `F1` and verify shortcut `7` is Start SteamVR.
2. Press `7`, verify confirmation, cancel, and confirm no start.
3. Press `7` again, confirm, and verify SteamVR starts without launching VRChat.
4. With SteamVR running, press `F1` and verify shortcut `7` is Restart SteamVR.
5. Confirm a restart and verify completion is reported as console/log text, not a modal surface.

## Pass 5 - SteamVR Overlay

1. Start SteamVR and VRChat.
2. Confirm the overlay still exposes `Restart SteamVR`, not Start SteamVR.
3. Click `Restart SteamVR`, verify confirmation is armed, then cancel by letting it expire.
4. Click `Restart SteamVR` again and confirm.
5. Verify the operation continues after the old overlay disappears and completion is visible through Supervisor state/logs without an overlay result popup.
