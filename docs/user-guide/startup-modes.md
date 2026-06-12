# Startup Modes

Choose startup mode in **General > Autostart mode**.

## Off

Use this if you want to start the Supervisor manually.

Nothing is registered for automatic startup.

## Terminal Mode

Recommended for most users.

Terminal Mode starts a watcher when SteamVR is running. If **Use Terminal UI as default interface** is enabled, the watcher starts the Supervisor hidden and opens Terminal UI with the active config.

If **Use Terminal UI as default interface** is unchecked, Terminal Mode keeps the classic visible console behavior.

When Terminal UI is opened by this autostart path, it closes automatically after the paired Supervisor exits.

## SteamVR Overlay

Use this if you want controls inside SteamVR instead of a terminal dashboard.

SteamVR Overlay mode registers the SteamVR host and starts the Supervisor through that workflow. It is separate from Terminal Mode and does not require Terminal UI.

## Which Should I Choose?

| Situation | Recommended mode |
|---|---|
| You want a clear desktop dashboard | Terminal Mode |
| You want controls inside SteamVR | SteamVR Overlay |
| You want to start everything manually | Off |
| You are troubleshooting startup | Off first, then Terminal Mode |
