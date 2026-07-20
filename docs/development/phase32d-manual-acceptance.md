# Phase 32D Manual Acceptance

Status: prepared; combined Phase32C and Phase32D live acceptance has not been run.

Use only the commit-bound `Phase32D-ExactlyOnceSteamVrRestartRequests-*` external deployment. Capture timestamps, Supervisor/TUI/overlay/SteamVR/VRChat and managed-app PIDs, short request/operation correlations, request disposition, shutdown issue count, and relevant console/debug lines. Normal UI must not expose full GUIDs.

## Global gates

- ProductVersion is `1.3.1`.
- Phase32C physical-device attribution and scoped recovery are present.
- Actions `1` through `6` remain immediate; action `7` remains confirmed.
- No automatic Action Result popup appears.
- No force-kill or automatic SteamVR shutdown retry exists.
- Base stations and monitor topology remain preserved during a requested restart.

## Pass 1 - No-action session soak

1. Start SteamVR, VRChat, Supervisor, TUI/overlay, and the full managed-app set.
2. Do not activate action `7`.
3. Soak for at least 30 minutes; 60 minutes is preferable.
4. Verify zero accepted restart requests, zero `vrstartup.exe -shutdown` helpers, no SteamVR/VRChat replacement, and stable managed-app PIDs except independent faults.

This is the primary live regression for the reported autonomous restart loop.

## Pass 2 - Cancel and reconnect safety

1. Open action-7 Restart confirmation and cancel it.
2. Verify no request identity and no shutdown.
3. Open confirmation again, then close/disconnect the client without confirming.
4. Reconnect and verify no request or shutdown.
5. Repeat from TUI and overlay where practical.

## Pass 3 - One TUI confirmation

1. Record process identities.
2. Confirm Restart once in the TUI.
3. Verify one accepted request, one operation, and `steamVrShutdownIssueCount=1`.
4. Verify one replacement runtime, conditional VRChat resume, and one managed-app recovery.
5. Wait at least 10 minutes and verify no autonomous second shutdown.
6. Verify action `7` becomes available for a new explicit confirmation.

## Pass 4 - Overlay restart and reconnect

1. Confirm Restart once from the overlay.
2. Verify one accepted request and one shutdown issuance.
3. Verify the overlay disappears with the old runtime, reconnects to the replacement, and displays the existing result.
4. Wait at least 10 minutes and verify reconnect did not resubmit or restart again.

## Pass 5 - Duplicate activation resistance

1. Use safe UI interaction to confirm once.
2. Where practical, exercise a fast double-click or Enter/click overlap.
3. Verify one request identity, one operation, and one shutdown issuance.
4. Verify duplicate activation is ignored or observes the active request.
5. Verify no second restart is queued after completion.

Deterministic automated tests remain authoritative for tight races.

## Pass 6 - Second legitimate restart

1. After a prior restart fully completes, open a new confirmation.
2. Confirm again.
3. Verify a different request identity, one new shutdown issuance, and one new replacement sequence.
4. Verify the earlier request identity was not reused.

## Pass 7 - Phase32C Index and Vive dongles

1. Record managed-app and SteamVR identities.
2. Remove/reinsert one Index-controller radio and one Vive Tracker radio separately.
3. Verify zero managed-app restarts, zero action-7 requests, zero SteamVR shutdown helpers, and no monitor/base-station change.
4. Verify neither event queues a later restart.

## Pass 8 - Genuine Pimax scoped recovery

Using the established safe Pimax reconnect procedure:

1. Verify physical-device attribution and readiness gating.
2. Verify only configured Pimax-dependent applications restart.
3. Verify XSOverlay and unrelated applications retain their PIDs.
4. Verify no action-7 request and no SteamVR shutdown issuance.

## Pass 9 - Normal final shutdown

1. Exit SteamVR normally after restart testing.
2. Verify managed applications close, configured base stations power off, monitors restore, and Supervisor/TUI exit.
3. Verify only the watcher remains where configured.
4. Verify normal shutdown is not converted into a restart request.
