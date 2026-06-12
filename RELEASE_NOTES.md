# Pimax VRC Supervisor v1.3.0-test Release Candidate Notes

This is a local release-candidate package for Windows x64 testing before merging the Terminal UI work toward the main release line.

## Current Version

- Package version: `v1.3.0-test`
- App version: `1.3.0-test`
- Assembly/file version: `1.3.0.0`
- Platform: Windows x64
- Runtime variants: with bundled .NET 9 and without bundled .NET 9

## Package Variants

- `PimaxVrcSupervisor-v1.3.0-test-win-x64-with-dotnet9.zip`
  - Self-contained package.
  - Includes the .NET 9 Windows Desktop runtime files.
  - Recommended if you are not sure whether .NET 9 is installed.

- `PimaxVrcSupervisor-v1.3.0-test-win-x64-no-dotnet9.zip`
  - Smaller framework-dependent package.
  - Requires the .NET 9 Windows Desktop Runtime x64 to be installed.

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

## Highlights

- Terminal UI is the primary Configurator launch interface by default.
- Terminal Mode can start the Supervisor hidden and open Terminal UI for SteamVR sessions.
- Terminal UI supports confirmed actions for the six regular Supervisor session controls.
- Terminal UI connected shutdown requests Supervisor cleanup; window close is best-effort and unconfirmed.
- Autostart-launched Terminal UI closes after its paired Supervisor exits.
- Configurator uses a single **Autostart mode** selector: Off, Terminal Mode, or SteamVR Overlay.
- Optional Terminal UI diagnostics can write lightweight interval summaries when enabled.
- SteamVR Overlay mode remains available and separate from Terminal Mode.

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
2. Extract the new package into a fresh folder or over a previous test folder.
3. Keep your existing `supervisor.config.json` if it contains your preferred settings.
4. Open the Configurator and press **Validate** before launching a session.

## Notes

- The application is English-only; satellite language folders are intentionally excluded from these local packages.
- The `.ico` files are embedded into executables and are not included as runtime assets.
- The SteamVR overlay PNG asset is required and remains in `Assets\`.
- These local packages are not signed release artifacts and do not create a GitHub Release or tag.
