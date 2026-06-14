# Pimax VRC Supervisor v1.3.0 Release Notes

Pimax VRC Supervisor v1.3.0 is a Windows x64 release for Pimax VRChat sessions. It includes the Configurator, Supervisor, Terminal UI, SteamVR Overlay host, startup helper, watcher, default config, and release notes in one flat folder.

## Asset Republication Notice

Release assets updated on 2026-06-13 22:02:09 UTC:

- Corrected Terminal UI startup ordering so the Supervisor dashboard bridge is ready before Terminal UI opens.
- Corrected scheduled-task preservation of the Terminal UI preference.
- Corrected repeated-session fallback to Classic Console.
- Corrected initial Terminal UI window sizing.
- Improved full-layout System panel spacing.
- Added warning and manual-exit handling for unexpected SteamVR termination.

The version remains v1.3.0. Previous downloadable hashes are superseded by the checksums and Sigstore bundles attached to the current release.

## Package Variants

- `PimaxVrcSupervisor-v1.3.0-win-x64-with-dotnet9.zip`
  - Self-contained package.
  - Includes the .NET 9 Windows Desktop runtime files.
  - Recommended if you are not sure whether .NET 9 is installed.

- `PimaxVrcSupervisor-v1.3.0-win-x64-no-dotnet9.zip`
  - Smaller framework-dependent package.
  - Requires the .NET 9 Windows Desktop Runtime x64 to be installed.

## Highlights

- Terminal UI is the default desktop control surface from Configurator.
- Terminal Mode can start the Supervisor hidden and open Terminal UI during SteamVR sessions.
- Terminal UI provides confirmed controls for the six normal Supervisor session actions.
- Connected Terminal UI shutdown requests Supervisor cleanup; window close is best-effort and unconfirmed.
- Autostart-launched Terminal UI closes after its paired Supervisor exits.
- SteamVR Overlay mode remains available and separate from Terminal Mode.
- Base-station startup no longer requires a prior Configurator scan.
- Unsupported, read-unsupported, or temporarily unavailable Base Station 2.0 devices no longer block startup indefinitely.
- Configurator now uses Vive Face Tracker wording and keeps Windows PnP fast reconnect detection off by default.
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

The public release workflow signs the final zip packages with Sigstore, creates checksum files, and generates GitHub artifact attestations. Local packages built from source are not a substitute for final signed release assets.

## Notes

- The application is English-only; satellite language folders are intentionally excluded from release packages.
- The `.ico` files are embedded into executables and are not included as runtime assets.
- The SteamVR overlay PNG asset is required and remains in `Assets\`.
