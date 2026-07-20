# Startup Modes

Choose an interface mode in **General > Interface mode**, then save to apply its startup integration.

## Off

Use this if you want to start the Supervisor manually.

Nothing is registered for automatic startup.

## Terminal UI only

Recommended for most users.

Terminal UI only starts a watcher when SteamVR is running. The watcher starts one hidden Supervisor owner, and that owner opens Terminal UI with the active config after its command bridge is ready. Terminal UI closes automatically after the paired Supervisor exits.

During normal startup, the Supervisor only validates the saved startup task and reports if it needs attention. It does not overwrite the watcher while a session is already running; use Configurator to reapply startup integration when you intentionally move or repair the install.

When a new release is started interactively for the first time, config import and autostart migration are separate choices. Keeping an existing autostart task leaves it bound to the previous release until you rebind it later in Configurator.

## SteamVR Overlay only

Use this if you want controls inside SteamVR instead of a terminal dashboard.

SteamVR Overlay only registers the SteamVR host. The host first tries to attach to an existing Supervisor command bridge and invokes its elevated fallback start task only when no bridge is available. It does not open Terminal UI.

## Terminal UI + SteamVR Overlay

Combined mode installs the Terminal UI watcher and registers the SteamVR dashboard host, but they do not own separate Supervisors. There is exactly one authoritative `PimaxVrcSupervisor.exe`; both UI executables observe and control its shared state.

The combined owner is persistent rather than SteamVR-owned. If Supervisor starts first, Terminal UI opens and the later SteamVR host attaches without starting another owner. When SteamVR exits, its overlay host exits while Supervisor and Terminal UI remain available. A later SteamVR session starts one overlay host, which reconnects to the same Supervisor.

Closing Terminal UI or losing the overlay host closes only that client. It does not stop Supervisor, SteamVR, the other client, or managed applications. During a Supervisor replacement, the persistent Terminal UI reconnects and SteamVR supplies a replacement overlay host. A real Supervisor shutdown disconnects the clients according to their normal lifecycle.

## Switching Safely

Select the new mode in Configurator and save once. Apply rewrites the two recognized scheduled tasks and the SteamVR registration as one plan: incompatible watcher/helper tasks and manifests are removed or replaced, and repeated Apply operations retain one task of each required kind. Restart SteamVR after changing an overlay-related mode so SteamVR reloads the registration.

## Which Should I Choose?

| Situation | Recommended mode |
|---|---|
| You want a clear desktop dashboard | Terminal UI only |
| You want controls only inside SteamVR | SteamVR Overlay only |
| You want both control surfaces | Terminal UI + SteamVR Overlay |
| You want to start everything manually | Off |
| You are troubleshooting startup | Off first, then the intended interface mode |
