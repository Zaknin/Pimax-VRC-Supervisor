# Phase 32C Manual Acceptance

Status: prepared; live hardware acceptance has not been run.

## Purpose

Phase 32C prevents a generic PiService HID remove/add burst from becoming a Pimax recovery command. PiService and Kernel-PnP events are now rescan hints. Supervisor compares structured USB/PnP inventories, groups interfaces by stable physical identity, and requires a disconnect and return of the same relevant device before considering recovery.

Validated identity families for this phase:

| Physical role | Structured identity | Recovery behavior |
|---|---|---|
| Index/Vive Tracker SteamVR radio | `VID_28DE&PID_2101` | Unrelated; no application recovery |
| Pimax runtime | Exact configured Pimax VID/PID, including `VID_34A4&PID_0012` | Wait for `registeredReady / confirmed`, then scoped recovery |
| Pimax EyeChip | `VID_2104&PID_0220` | Wait for Pimax readiness, then scoped recovery |
| Pimax tracking interface | `VID_28DE&PID_2300` | No face-application recovery by itself |
| Vive Face Tracker | Exact configured identity, including `VID_0BB4&PID_0321` | VRCFaceTracking only when explicitly enabled |
| Other or unknown device | Any other or insufficient structured identity | Log and ignore safely |

Composite interfaces are coalesced by `ContainerId`. Friendly name, generic USB/HID class, and vendor-wide matching are not recovery authority.

## Evidence To Capture

For each pass, save:

- deployment directory and commit identity;
- Supervisor, TUI, SteamVR, VRChat, VRCFaceTracking, Broken Eye, and XSOverlay PIDs;
- Boop Counter and OSC Router PIDs when configured;
- the concise USB attribution decision;
- debug classification with stable hashed container/instance identity and VID/PID;
- any recovery-plan line;
- monitor and base-station state before and after;
- start and finish timestamps.

Do not expose full machine-specific instance paths in ordinary screenshots or dashboard output.

## Pass 1 - Index-Controller Radio

1. Start SteamVR, VRChat, Supervisor, and all configured managed applications.
2. Wait until startup and action state are idle.
3. Record all relevant PIDs and monitor/base-station state.
4. Remove one Index-controller USB radio dongle.
5. Wait longer than the device polling/settle interval.
6. Verify one physical-device decision reports an unrelated SteamVR radio removal.
7. Verify every recorded application PID is unchanged.
8. Verify no Restart Core Apps, Autostart reload, SteamVR action, monitor transition, or base-station action occurs.
9. Reinsert the same dongle and wait for Windows/SteamVR discovery.
10. Verify the unrelated reconnect decision and again verify zero managed-application restarts.

Expected result: zero Supervisor-managed application restarts on removal and reinsertion.

## Pass 2 - Vive Tracker Radio

Repeat Pass 1 with one Vive Tracker USB radio dongle.

Expected result: zero Supervisor-managed application restarts on removal and reinsertion, with a concise unrelated/no-recovery decision.

## Pass 3 - Multiple Radio Dongles

1. Record the settled process identities.
2. Remove and reinsert two unrelated SteamVR radio dongles in a controlled sequence.
3. Verify each physical container remains independently attributed.
4. Verify composite/interface notifications do not create repeated decisions.
5. Verify no recovery plan, restart storm, process identity change, monitor action, or base-station action.

Expected result: both devices remain unrelated and zero recovery plans execute.

## Pass 4 - Genuine Pimax Reconnect

Use only the established safe manual Pimax reconnect procedure. Do not use abandoned automatic Pimax connection recovery, USB disable/enable, a forced reset, or a new device-control path.

1. Record dependent and unrelated application PIDs.
2. Disconnect the genuine Pimax physical device.
3. Verify the attributed removal logs that removal alone does not restart applications.
4. Return the same physical Pimax device.
5. Verify recovery waits while registration is not ready.
6. Verify `registeredReady / confirmed` is observed before a plan executes.
7. Verify one scoped plan includes only currently configured Pimax-dependent applications.
8. Verify VRCFaceTracking and Broken Eye restart only when enabled as dependencies.
9. Verify explicitly Pimax-dependent Autostart apps restart once.
10. Verify XSOverlay, SteamVR, VRChat, Boop Counter, OSC Router, unrelated Autostart apps, monitors, and base stations retain their prior identity/state.

Expected result: one readiness-gated scoped recovery plan and no broad Restart Core Apps operation.

## Pass 5 - Vive Face Tracker

Run this pass only when the physical tracker is available.

1. Record application PIDs.
2. Disconnect the physical Vive Face Tracker.
3. Verify removal alone does not restart an application.
4. Reconnect the same physical tracker.
5. Verify VRCFaceTracking restarts only when **Restart VRCFaceTracking after Vive Face Tracker reconnect** is enabled.
6. Verify Broken Eye does not restart.
7. Verify XSOverlay and unrelated applications retain their PIDs.
8. Verify no broad Restart Core Apps operation occurs.

If the hardware is unavailable, retain deterministic automated coverage as the acceptance evidence and mark this pass not run.

## Pass 6 - Action 7 SteamVR Regression

1. Record the current runtime identity and whether VRChat is running.
2. Invoke action 7 and confirm it.
3. Verify graceful shutdown uses `vrstartup.exe -shutdown`.
4. Verify the old runtime exits and an exact replacement runtime becomes healthy.
5. Verify expected-shutdown classification remains correct.
6. Verify VRChat resumes only when it was running at acceptance.
7. Verify managed applications recover through the existing SteamVR lifecycle.
8. Verify USB attribution dispatches no competing recovery plan.
9. Verify XSOverlay reconnect, monitor preservation, and base-station preservation remain correct.
10. Exit SteamVR normally and verify complete final cleanup.

Expected result: Phase 32B2E behavior is unchanged.

## Global Acceptance Gates

- Actions 1 through 6 remain immediate.
- Action 7 remains the only confirmation-gated action.
- No actions 8 or 9 exist.
- No automatic Action Result popup appears.
- Classic console, TUI modal controls, and overlay controls are unchanged.
- Manual Restart Core Apps retains broad manual semantics.
- ProductVersion remains `1.3.1`.
- No release is published and no branch is pushed.
