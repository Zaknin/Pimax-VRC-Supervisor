# Pimax VRC Supervisor v1.3.1 Release Notes

Pimax VRC Supervisor v1.3.1 is a Windows x64 maintenance release for Pimax VRChat sessions. It keeps the same application layout as v1.3.0 and focuses on SteamVR lifecycle reliability, Terminal UI startup ownership, and startup-task safety.

## Package Variants

- `PimaxVrcSupervisor-v1.3.1-win-x64-with-dotnet9.zip`
  - Self-contained package.
  - Includes the .NET 9 Windows Desktop runtime files.
  - Recommended if you are not sure whether .NET 9 is installed.

- `PimaxVrcSupervisor-v1.3.1-win-x64-no-dotnet9.zip`
  - Smaller framework-dependent package.
  - Requires the .NET 9 Windows Desktop Runtime x64 to be installed.

## What Changed Since v1.3.0

- Refactored SteamVR lifecycle ownership so only watcher-launched sessions are treated as managed SteamVR sessions.
- Normal SteamVR UI Exit no longer gets reported as a crash.
- External SteamVR disappearance without reliable abnormal evidence now uses safe normal cleanup instead of a persistent crash warning.
- OpenVR startup probes used by base-station checks are isolated from managed-session ownership, so transient probe-created `vrserver.exe` processes do not trigger cleanup or warnings.
- Terminal Mode still uses the validated watcher -> Supervisor -> dashboard bridge -> Terminal UI startup flow.
- The Supervisor now owns Terminal UI launch after dashboard readiness; the watcher does not launch Terminal UI directly.
- Normal Supervisor startup validates scheduled-task state read-only and no longer deploys or overwrites the watcher during runtime.
- Startup task preference parsing continues to preserve the Terminal UI/default interface setting and unknown safe arguments.
- Structured diagnostics now record decisive SteamVR lifecycle decisions and scheduled-task validation results.

## Current Behavior

- Terminal UI is the default desktop control surface from Configurator.
- Terminal Mode can start the Supervisor hidden and open Terminal UI during SteamVR sessions.
- Terminal UI provides confirmed controls for the six normal Supervisor session actions.
- Connected Terminal UI shutdown requests Supervisor cleanup; window close is best-effort and unconfirmed.
- Autostart-launched Terminal UI closes after its paired Supervisor exits.
- SteamVR Overlay mode remains available and separate from Terminal Mode.
- Base-station startup does not require a prior Configurator scan.
- Unsupported, read-unsupported, or temporarily unavailable Base Station 2.0 devices do not block startup indefinitely.
- Optional Terminal UI diagnostics can write lightweight interval summaries when enabled.

## Included Apps

- `PimaxVrcSupervisor.exe` - Supervisor and classic console.
- `PimaxVrcSupervisorConfigurator.exe` - GUI setup tool.
- `PimaxVrcSupervisorTui.exe` - Terminal UI dashboard.
- `PimaxVrcSupervisorSteamVrHost.exe` - SteamVR Overlay host.
- `PimaxVrcSupervisorStartupHelper.exe` - helper executable used by startup integration.
- `PimaxVrcSupervisorWatcher.exe` - watcher executable used by Terminal Mode startup.
- `supervisor.config.json` - default documented config.
- `README.md` and `RELEASE_NOTES.md`.
- `Assets\vr-overlay-icon.png` for the SteamVR Overlay.

## Install

1. Download the package variant you want.
2. Extract it to a writable folder.
3. Run `PimaxVrcSupervisorConfigurator.exe`.
4. Choose or create a config.
5. Set paths for the tools you use.
6. Choose an autostart mode.
7. Click **Validate**, then **Save**.
8. Click **Launch Supervisor**.

## Upgrade

1. Close the Supervisor, Configurator, Terminal UI, and SteamVR host if they are running.
2. Extract the new package into a fresh folder or over your previous folder.
3. Keep your existing config if it contains your preferred settings.
4. Open Configurator from the final folder and press **Validate** before launching a session.
5. If you use Terminal Mode autostart, re-register startup integration from the final folder so the scheduled task points at the new path.

## Release Security

The public release workflow signs final zip packages with Sigstore, creates checksum files, and generates GitHub artifact attestations. Local packages built from source are not a substitute for final signed release assets.

## Notes

- The application is English-only; satellite language folders are intentionally excluded from release packages.
- The `.ico` files are embedded into executables and are not included as runtime assets.
- The SteamVR overlay PNG asset is required and remains in `Assets\`.
