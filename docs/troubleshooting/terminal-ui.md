# Terminal UI Issues

## Terminal UI Stays Disconnected

Terminal UI needs a running Supervisor for live data.

Check:

1. Supervisor is running.
2. You launched Terminal UI from the same release folder.
3. Security software is not blocking local connections.

## Terminal UI Closes When SteamVR Exits

This is expected when Terminal UI was opened by Terminal Mode autostart. Manual Terminal UI launches remain open until you exit.

## Q Does Not Just Close Terminal UI

When connected, `Q` opens Supervisor shutdown confirmation. When disconnected, `Q` exits only Terminal UI.

## Actions Are Disabled

Actions are disabled while disconnected, while shutdown is in progress, or when another conflicting action is already running.
