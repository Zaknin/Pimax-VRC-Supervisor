# Terminal UI Issues

## Terminal UI Stays Disconnected

Terminal UI needs a running Supervisor for live data.

Check:

1. Supervisor is running.
2. You launched Terminal UI from the same release folder.
3. Security software is not blocking local connections.

## Terminal UI Closes When SteamVR Exits

This is expected in **Terminal UI only**, where the owner is SteamVR-scoped. In **Terminal UI + SteamVR Overlay**, Supervisor and Terminal UI are persistent and should remain available after SteamVR exits.

## Q Does Not Just Close Terminal UI

When connected, `Q` opens Supervisor shutdown confirmation. When disconnected, `Q` exits only Terminal UI.

## Actions Are Disabled

Actions are disabled while disconnected, while shutdown is in progress, or when another conflicting action is already running.

`7 SteamVR` changes mode from Supervisor state. If SteamVR is stopped, it starts SteamVR after confirmation and does not launch VRChat. If SteamVR is running, it restarts SteamVR after confirmation and resumes VRChat only when VRChat was running at confirmation time.
