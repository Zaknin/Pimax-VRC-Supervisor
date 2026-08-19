# Phase32B2A Manual Acceptance

Use this checklist with a Phase32B2A test deployment. Do not claim these live checks complete until they are run on hardware.

## Pass 1 - TUI, SteamVR Stopped

1. Start Supervisor and Terminal UI with SteamVR stopped.
2. Confirm action `7 SteamVR` shows `Start SteamVR` and `[START]`.
3. Confirm actions `1` through `6` keep their existing order and execute without a SteamVR confirmation dialog.
4. Press `7`, verify the Start SteamVR confirmation, then cancel and confirm no command starts.
5. Click action `7`, verify the same Start SteamVR confirmation, then confirm.
6. Confirm SteamVR starts, VRChat does not launch, the terminal result is `SteamVR started.`, and no busy state remains.

## Pass 2 - TUI, SteamVR Running Without VRChat

1. Confirm SteamVR is running and VRChat is absent.
2. Confirm action `7 SteamVR` shows `Restart SteamVR` and `[RESTART]`.
3. Press `7`, cancel, and confirm SteamVR process identity does not change.
4. Click action `7`, confirm the Restart SteamVR confirmation appears instead of immediate execution.
5. Confirm the restart, verify SteamVR restarts, VRChat is not launched, base stations remain on, monitors remain in VR mode, and the terminal result is `SteamVR restarted.`
6. Confirm no busy state remains and a second restart is accepted.

## Pass 3 - TUI, SteamVR And VRChat Running

1. Start VRChat and allow managed applications to settle.
2. Press `7`, confirm Restart SteamVR, and verify VRChat closes during the restart.
3. Confirm SteamVR restarts, VRChat launches again through Steam VR mode, managed applications recover, and the terminal result is `SteamVR restarted and VRChat resumed.`
4. Confirm a later normal SteamVR exit still performs complete cleanup.

## Pass 4 - TUI Sizes

For wide, medium, and compact layouts:

1. Verify action titles, descriptions, and badges align deliberately.
2. Verify action `7 SteamVR` renders Start, Restart, and Busy states without clipping or overlap.
3. Verify confirmation text remains readable.

## Pass 5 - SteamVR Overlay

1. Start SteamVR and VRChat.
2. Confirm overlay actions `1` through `6` remain immediate.
3. Click `Restart SteamVR`, verify confirmation is armed, cancel by letting it expire, and confirm nothing starts.
4. Click `Restart SteamVR` again and confirm.
5. Verify the operation continues after the old overlay disappears, SteamVR and VRChat recover, the restored overlay shows progress or the terminal result, and another restart can be initiated.

## Pass 6 - Classic Console

1. With SteamVR stopped, press `F1` and verify shortcut `7` is Start SteamVR.
2. Press `7`, verify confirmation, cancel, and confirm no start.
3. Press `7` again, confirm, and verify SteamVR starts without launching VRChat.
4. With SteamVR running, press `F1` and verify shortcut `7` is Restart SteamVR.
5. Confirm a restart, verify terminal completion, and verify a second restart is available.
