# Release Packaging

A release is a flat Windows folder. Keep the files together.

## Expected Key Files

- `PimaxVrcSupervisor.exe`
- `PimaxVrcSupervisorConfigurator.exe`
- `PimaxVrcSupervisorSteamVrHost.exe`
- `PimaxVrcSupervisorTui.exe`
- `supervisor.config.json`
- `Assets\vr-overlay-icon.png`

The full release package may also include bundled .NET runtime files. Do not delete those files unless you know you are using a package that expects .NET to be installed separately.

## Moving The App

If you move the release folder:

1. Open Configurator from the new folder.
2. Load your config.
3. Click **Validate**.
4. Save to refresh startup integration.

## Backups

Configurator may create backup config files when saving. Keep them if you are experimenting with startup or base-station settings.
