# VR Overlay

## SteamVR Dashboard Host

`PimaxVrcSupervisorSteamVrHost.exe` is a SteamVR dashboard overlay app that provides in-VR controls for the supervisor.

## How It Works

1. When **Start with SteamVR** is enabled in the Config Editor, the host is registered as a SteamVR dashboard overlay app.
2. SteamVR starts the host, which launches the elevated supervisor with `--steamvr-start`.
3. The supervisor starts managed apps immediately after SteamVR startup.

## Dashboard Controls

The SteamVR host dashboard includes buttons for:

- Restarting Broken Eye / VRCFaceTracking
- Turning base stations on or off
- Restarting the OSC router

## Shutdown Behavior

**Stop with SteamVR** is forced on for SteamVR startup mode. When `vrserver.exe` exits:

1. Base stations are powered down (if needed).
2. Monitors are restored.
3. Managed apps are closed.
4. The supervisor exits.
