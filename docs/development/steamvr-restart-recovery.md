# SteamVR Restart Recovery

Phase32B keeps a managed Supervisor session continuous across the short gaps created by SteamVR's Restart control or a user-selected recovery after a failure. It does not launch SteamVR, press a SteamVR dialog button, depend on VRChat, or change the base-station transport and discovery model.

## Discovery evidence

The Restart control wrote `SteamVRSystemState_Restart` plus `vrmonitor://reboothmd`, then recreated the SteamVR process group. The first replacement `vrserver` appeared in about 0.86 seconds. That replacement failed to find the headset display, requested `vrmonitor://restartsystem/`, and a second replacement appeared about 1.08 seconds later. `vrmonitor` was recreated, so its continuity is not a usable signal.

Normal Exit wrote `SteamVRSystemState_ShutdownRequested`, then terminated the full process group without an automatic replacement during the 90-second capture. `Quit gracefully` occurred in both flows, so it is not a discriminator. Forced `vrserver` termination displayed SteamVR's critical-error dialog and did not itself create a replacement runtime.

## Lifecycle model

The managed-session recovery coordinator uses explicit `Running`, `LossDetected`, `RecoveryPending`, `ReplacementAdopted`, `SessionEnding`, and `Completed` states. Runtime identity is `vrserver` PID plus start time; a replacement with a different identity is adopted by the original Supervisor process.

Monitor handling is an explicit, typed recovery policy rather than an implied side effect of every runtime loss. Supervisor restores only a monitor layout it previously changed, and every restore remains ownership-gated and idempotent. A restore request is not reported as a successful restore if the monitor operation fails.

## Classification and timing

Current-session log additions are read from bounded portions of `vrmonitor`/`vrserver` logs, after a baseline is established while the managed runtime is active. Truncation, rotation, missing files, locks, and malformed data are nonfatal. Log parsing is a high-confidence hint only; replacement-process observation is authoritative.

| Signal | Decision |
|---|---|
| Fresh `ShutdownRequested`, with no restart marker or replacement | promptly restore an owned monitor layout and run normal cleanup |
| Fresh restart state/URL/startup-reason marker | preserve the current monitor state and stations for the 3-second fast replacement window |
| New `vrserver` identity before monitor restoration | adopt it; keep the current monitor state and retain the managed session |
| No useful evidence for the first 3 seconds | preserve the current monitor state and stations while classification remains open |
| Still ambiguous after 3 seconds | restore an owned monitor layout once, while keeping stations on for the remaining 17 seconds |
| Replacement appears after that restoration | adopt it, keep stations on, and never disable monitors again automatically |
| Adopted replacement disappears | use a 5-second chained-restart gap; preserve monitor state before restoration and leave it on after restoration |

The coordinator clears consecutive recovery history after 30 seconds of stable replacement runtime. It bounds unstable recovery to three consecutive adoptions and about 60 seconds total; it never shuts stations down under an active valid runtime. It does not create a restart loop.

## Cleanup and Watcher coordination

Final managed-app cleanup and station shutdown are deferred while recovery is pending. Confirmed normal exit, recovery timeout, explicit normal Supervisor exit, and existing Windows/Supervisor shutdown paths retain normal cleanup. The Phase32A preserve-base-stations exit cancels recovery and keeps stations on.

The Watcher observes the original Supervisor as active during adoption and therefore neither launches another Supervisor nor claims the replacement identity. Once the original Supervisor completes cleanup, a genuinely later SteamVR identity can launch normally. Phase32A's explicit same-session suppression remains unchanged.

Diagnostics use existing optional Supervisor events only. No persistent diagnostics journal or Phase31D XSOverlay/BaseStations schema change is added.

## Phase32B.1 acceptance boundary

The unaccepted `Phase32B-SteamVrRecovery-FD-7248525` deployment package is superseded for live testing. Phase32B.1 is source, unit-test, and documentation work only: it does not bind, launch, modify, or otherwise use either deployment package, and it does not add an automatic SteamVR launch path.
