# Updating

## Safe Update Steps

1. Close Terminal UI, Configurator, and the Supervisor.
2. Extract the new release into a new folder or over the old folder.
3. Keep your existing config file unless you intentionally want to reset.
4. Open Configurator.
5. Click **Validate**.
6. Click **Save** to refresh startup integration if you changed the release folder.

## Moving To A New Folder

If you extract to a new folder, startup integration may still point to the old release. Open Configurator from the new folder, choose your config, and save/apply the startup mode again.

If you start the Supervisor directly from a new folder and it has no completed local setup, it can offer to import a configuration from another release. Importing copies the config into the new folder and leaves the old config unchanged. The autostart task decision is separate: you can keep the old task, rebind it to the new folder, or turn managed autostart off.

If you intentionally start with an explicit config path, the Supervisor keeps using that config unless you choose to import a copy.

## Keep Backups

Configurator creates backups when saving. Keep at least one known-good config before changing paths, startup mode, or base-station settings.
