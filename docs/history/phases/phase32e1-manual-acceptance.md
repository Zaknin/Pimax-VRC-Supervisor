# Phase 32E1 Manual Acceptance

Status: deterministic automated validation complete; post-fix live hardware acceptance not yet run.

## Root Cause

Phase 32C correctly made generic PiService HID remove/add bursts rescan hints and required structured physical-device attribution. The positive Pimax Play/PiService 1.4.0 P3B path, however, is a logical runtime reconnect that can leave the same Windows USB container continuously present. Supervisor parsed only legacy `removed hid device` / `added hid device` lines, so the real `HMD disconnected`, `HMD connected`, HID-ready, display-restore, and registration-success sequence never armed a pending physical reconnect. With no pending operation, readiness assessment and scoped dispatch were unreachable.

Normal, watcher-launched Terminal UI, and `--steamvr-start` overlay-only sessions all use the same `AppSupervisor` main loop. The defect was therefore in the shared PiService-to-attribution boundary, not an overlay-only dispatcher omission.

## Corrected Sequence

1. Supervisor initializes the PiService watcher cutoff before startup waits and seeds a prior positive P3B identity when available.
2. `HMD disconnected: Pimax P3B` arms a bounded pending operation only while structured inventory contains a positively classified Pimax runtime or eye device.
3. `HMD connected` must identify Pimax P3B / Pimax Crystal and must match the prior serial when a serial was available.
4. HID-ready and display-restore lines record incomplete readiness progress.
5. `P3B VersionChecker: Register success`, or the existing confirmed registration assessment, completes readiness.
6. The configured reconnect stability delay must also have elapsed.
7. One coalesced scoped plan restarts VRCFaceTracking, Broken Eye when enabled, and every enabled Autostart app whose **Restart after Pimax reconnect** dependency is enabled.
8. The pending state is consumed before dispatch, so repeated or overlapping lines cannot create a second plan.

## Automated Evidence

The deterministic regression fixture uses the confirmed live timestamps and text:

- `2026-07-20 16:48:00.141 HMD disconnected: Pimax P3B`
- `2026-07-20 16:48:19.310 HMD connected: Pimax P3B, product name: Pimax Crystal, serial number: ...`
- `2026-07-20 16:48:19.311 HID device is ready, continuing with HMD initialization`
- `2026-07-20 16:48:28.538 hmd display restore`
- `2026-07-20 16:48:30.737 P3B VersionChecker: Register success`

Coverage includes exactly-once plan creation, VRCFaceTracking, Broken Eye, a Boop Counter-style Pimax-dependent Autostart entry, unrelated-target exclusion, repeated line suppression, standalone-return rejection, expiry without readiness, serial mismatch, positive physical-attribution gating, the configured stability delay, and the shared launch-mode path. Existing Valve `28DE:2101` and Phase 32D exactly-once restart-request tests remain required gates.

## Live Acceptance

1. Deploy the exact Phase 32E1 build without changing the machine configuration.
2. Start the normal configured SteamVR/TUI/overlay session and record relevant process IDs.
3. Perform one established safe manual Pimax Crystal disconnect and return.
4. Confirm the log reports disconnect, pending arm, candidate return, readiness progress, one selected-application list, and one completion result.
5. Confirm VRCFaceTracking, its module process, Broken Eye, and each enabled Pimax-dependent Autostart application receive one new process identity.
6. Confirm SteamVR, VRChat, XSOverlay and helpers, Supervisor processes, base stations, monitors, and unrelated applications retain their prior identity/state.
7. Confirm no action 7 request exists and no `vrstartup.exe -shutdown` helper is created.
8. Wait beyond the pending lifetime and confirm no delayed duplicate recovery occurs.

Do not use automatic USB disable/enable, a forced reset, a new device-control path, or broad Restart Core Apps for this acceptance pass.
