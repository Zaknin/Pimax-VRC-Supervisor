# Startup / Autostart Issues

## SteamVR Starts But Supervisor Does Not

1. Open Configurator.
2. Confirm **Autostart mode** is not **Off**.
3. Click **Validate**.
4. Save to reapply startup integration.
5. If you moved the release folder, save from the new folder.

## Terminal UI Does Not Open In Terminal Mode

1. Confirm **Use Terminal UI as default interface** is checked.
2. Confirm `PimaxVrcSupervisorTui.exe` exists in the release folder.
3. Save again from Configurator.

## Startup Points To The Wrong Config

Open the intended config in Configurator and save/apply startup mode again. The startup integration uses the active config at the time it is saved.

## Classic Console Opens Instead Of Terminal UI

Check **Use Terminal UI as default interface**. If it is unchecked, Terminal Mode preserves classic console behavior.
