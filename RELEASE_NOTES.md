# Pimax VRC Supervisor v1.3.1

Pimax VRC Supervisor v1.3.1 is a focused reliability release for Pimax VRChat sessions. It keeps the v1.3.0 flat package layout and concentrates on SteamVR lifecycle ownership, Terminal UI lifetime, base-station startup timing, and safer release-folder migration.

## Highlights

- Reliable SteamVR lifecycle ownership for watcher-managed sessions.
- Safer handling for normal SteamVR UI Exit and ambiguous external termination.
- Adaptive, re-armable base-station startup when SteamVR appears late.
- Supervisor-owned Terminal UI startup and lifetime.
- Direct-launch migration support for configs and managed autostart.
- Correct scheduled-task validation and deferred-choice messaging.

## SteamVR Lifecycle

- Terminal Mode watcher launches managed sessions explicitly. Internally, those sessions use `--managed-steamvr-session`; manual Supervisor launches remain non-managed.
- Normal SteamVR UI Exit now follows the normal cleanup path instead of being treated as a crash.
- External SteamVR disappearance without reliable abnormal evidence is classified safely as ambiguous external termination and uses normal cleanup without a persistent crash warning.
- Short OpenVR startup probes used by readiness checks are isolated from managed session ownership, so transient probe-created `vrserver.exe` processes do not trigger cleanup or warnings.
- Repeated SteamVR sessions work without restarting the watcher.

## Terminal UI

- Terminal Mode now uses the watcher -> Supervisor -> dashboard bridge -> Terminal UI startup chain.
- The Supervisor owns Terminal UI process creation after the dashboard bridge is ready; the watcher no longer starts Terminal UI directly.
- Supervisor-launched Terminal UI receives the exact Supervisor PID internally and closes when that Supervisor process exits.
- Intentional shutdown no longer leaves Terminal UI briefly showing a disconnected screen.
- `Q -> Esc` cancels Supervisor shutdown.
- `Q -> Enter` performs graceful Supervisor cleanup and closes Terminal UI.
- Terminal UI window close still requests graceful Supervisor shutdown.

## Base Stations

- Base-station startup can re-arm when SteamVR starts after the Supervisor is already waiting for VRChat.
- Startup uses adaptive SteamVR readiness and stabilization timing instead of the older fixed long wait.
- In the tested environment, base-station startup began after approximately two seconds once the safe readiness conditions were met. This is not a universal fixed delay.
- Unsupported, read-unsupported, or temporarily unavailable Base Station 2.0 devices are skipped with warnings instead of blocking startup indefinitely.
- Supervisor startup no longer requires opening Configurator or running a manual base-station scan first.

## Configuration And Autostart Migration

- Direct interactive Supervisor launch can detect configuration from another release folder.
- Configuration import is transactional: the old config is copied, validated, moved into place, and then reloaded before first-run questions continue.
- Existing v1.3.0 files are not modified by configuration import.
- An explicit `--config` path remains authoritative. Interactive direct launch may offer to import it, but choosing to use the supplied file keeps using that path.
- Config migration and scheduled-task migration are independent decisions.
- Keeping an existing task bound to another release performs no task mutation and does not deploy or replace the watcher.
- Hidden and watcher-managed startup remains noninteractive and validate-only.

## Scheduled Tasks

- Runtime scheduled-task inspection remains `ValidateOnly`; normal runtime does not deploy or overwrite the watcher.
- Task validation preserves the Terminal UI/default-interface preference and safe unknown arguments.
- If the user deliberately keeps autostart bound to another release, later validation now acknowledges that choice instead of showing a generic repair warning.
- Configurator remains the intended place to rebind, repair, or disable startup integration later.

## Compatibility And Unchanged Behavior

- The six normal Terminal UI actions are unchanged.
- `query-json`, `action-json`, and `lifecycle-json` behavior is unchanged.
- Legacy hard-stop behavior remains blocked from structured Terminal UI flows.
- SteamVR Overlay mode remains separate from Terminal Mode.
- Manual Supervisor sessions do not exit merely because SteamVR exits.

## Manual Validation

The following behavior was manually validated for this release:

- Watcher starts exactly one managed Supervisor session.
- One connected Terminal UI opens after dashboard bridge readiness.
- Terminal UI closes immediately when its exact Supervisor process exits.
- Normal SteamVR UI Exit performs normal cleanup.
- Forced external `vrserver.exe` termination uses the safe ambiguous external termination path.
- Late SteamVR appearance re-arms base-station startup.
- Base stations powered on normally in the tested environment.
- Full Terminal UI System layout renders `Supervisor  OK` with correct spacing.
- Direct launch detected an older config, imported it byte-for-byte, and kept task migration as a separate decision.
- Deferred task choice produced accurate user-facing messaging.

## Upgrade Notes

1. Close the Supervisor, Configurator, Terminal UI, and SteamVR host if they are running.
2. Extract v1.3.1 into a new folder.
3. Launch Configurator or start Supervisor interactively from the new folder.
4. Import the previous configuration when offered, or keep using an explicitly supplied config if that is intentional.
5. Decide separately whether to keep, rebind, or disable managed autostart.
6. If you use Terminal Mode autostart and want it to point at v1.3.1, re-register startup integration from Configurator in the new folder.

## Package Variants

- `PimaxVrcSupervisor-v1.3.1-win-x64-with-dotnet9.zip`
  - Self-contained package.
  - Includes the .NET 9 Windows Desktop runtime files.
  - Recommended if you are not sure whether .NET 9 is installed.

- `PimaxVrcSupervisor-v1.3.1-win-x64-no-dotnet9.zip`
  - Smaller framework-dependent package.
  - Requires the .NET 9 Windows Desktop Runtime x64 to be installed.

## Release Security

The public release workflow creates SHA-256 checksum files, Sigstore bundle files, and GitHub artifact attestations for the final zip packages. Local packages built from source are useful for testing but are not a substitute for final workflow-generated release assets.

## Known Limitations

- Base-station timing is adaptive and environment-dependent; the observed approximately two-second startup is not guaranteed on every setup.
- If a scheduled task remains intentionally bound to another release, that older release continues to own autostart until you rebind it in Configurator.
- Terminal UI process-parent monitoring is used for Supervisor-launched Terminal UI. Manual Terminal UI launches still use normal disconnected behavior.
