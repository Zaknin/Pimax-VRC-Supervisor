# Phase32B2E Manual Acceptance

Use this checklist with the external Phase32B2E test deployment. These live hardware and session tests are prepared for execution; automated validation does not claim them as complete.

## Pass 1 - TUI requested restart without VRChat

1. Start SteamVR without VRChat.
2. Invoke Restart SteamVR from Terminal UI and confirm.
3. Verify the old runtime exits.
4. Verify `SteamVR stopped unexpectedly` does not appear.
5. Verify `Expected SteamVR shutdown detected for requested restart.` appears once.
6. Verify replacement SteamVR starts and becomes ready.
7. Verify the final state says `SteamVR restarted.`
8. Verify Action Status is not left at WARN solely because the old runtime exited.
9. Verify enabled base stations remain on and monitors remain in VR topology.

## Pass 2 - TUI requested restart with VRChat

1. Start VRChat and the configured managed applications.
2. Restart SteamVR.
3. Verify the old runtime receives the expected requested-shutdown classification.
4. Verify replacement SteamVR becomes ready.
5. Verify VRChat returns automatically in VR mode.
6. Verify managed applications recover through the existing lifecycle.
7. Verify the final status says `SteamVR restarted and VRChat resumed.`
8. Verify no stale unexpected-exit warning remains.

## Pass 3 - SteamVR overlay

1. Start SteamVR, with or without VRChat as appropriate.
2. Initiate and confirm restart from the overlay.
3. Verify the old overlay disappears.
4. Verify Supervisor does not call the requested old-runtime shutdown unexpected.
5. Verify replacement SteamVR and the overlay return.
6. Verify the restored overlay shows progress or success without stale WARN.
7. Verify another restart remains available.

## Pass 4 - Genuine unexpected exit

Use a safe controlled test or deterministic harness; do not force-kill production processes solely for this pass.

1. Cause or simulate SteamVR disappearance without accepted restart intent.
2. Verify it remains classified as unexpected.
3. Verify the existing warning and bounded safety behavior remain.
4. Verify it is not labeled as requested restart.

## Pass 5 - Normal final shutdown

1. After a successful restart, exit SteamVR normally from its UI.
2. Verify managed applications close.
3. Verify configured base stations turn off.
4. Verify Supervisor-owned monitor changes restore.
5. Verify Supervisor and Terminal UI exit according to the configured mode.
6. Verify only Watcher remains where configured.
7. Verify the final shutdown is not called unexpected or requested restart.

## Pass 6 - UI regression

1. Verify action `7` confirmation buttons remain mouse-clickable.
2. Verify Tab, Left, Right, Enter, Space, and Esc controls.
3. Verify actions `1` through `6` remain immediate.
4. Verify no automatic Action Result popup appears.
5. Verify wide, medium, and compact Terminal UI layouts remain usable.
