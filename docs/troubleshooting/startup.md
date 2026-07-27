# Startup / Autostart Issues

## SteamVR Starts But Supervisor Does Not

1. Open Configurator.
2. Confirm **Autostart mode** is not **Off**.
3. Click **Validate**.
4. Save to reapply startup integration.
5. If you moved the release folder, save from the new folder.

## Terminal UI Does Not Open

1. Confirm **Interface mode** is **Terminal UI only** or **Terminal UI + SteamVR Overlay**.
2. Confirm `PimaxVrcSupervisorTui.exe` exists in the release folder.
3. Save again from Configurator.

## Startup Points To The Wrong Config

Open the intended config in Configurator and save/apply startup mode again. The startup integration uses the active config at the time it is saved.

## Classic Console Opens Instead Of Terminal UI

Re-save a Terminal UI interface mode from the current release folder. The scheduled task should then contain the Terminal UI startup intent after repair.

## Startup Task Needs Repair

If the Supervisor says startup integration needs repair, open Configurator from the release folder you want to use, load your config, and save/apply startup integration again. Normal Supervisor startup checks the task read-only so it will not overwrite the watcher during an active session.

If you chose to keep autostart bound to another release during direct-launch migration, the Supervisor will acknowledge that choice instead of repairing the task. Rebind later in Configurator when you are ready for the new release to own autostart.
