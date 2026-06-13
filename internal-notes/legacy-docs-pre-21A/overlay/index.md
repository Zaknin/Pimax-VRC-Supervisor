# VR Overlay

## SteamVR Dashboard Host

`PimaxVrcSupervisorSteamVrHost.exe` is a SteamVR dashboard overlay app that provides in-VR controls for the supervisor.

## How It Works

1. When **SteamVR Overlay** is enabled in the Configurator, the host is registered as a SteamVR dashboard overlay app.
2. SteamVR starts the host, which launches the elevated supervisor with `--steamvr-start`.
3. The supervisor waits for the Pimax headset, powers on base stations if enabled, then waits for VRChat before starting managed apps.

## Dashboard Controls

The SteamVR host dashboard includes buttons for:

- Restarting Broken Eye / VRCFaceTracking
- Turning base stations on or off
- Restarting the OSC router

## Shutdown Behavior

SteamVR manifest startup exits with SteamVR. When `vrserver.exe` exits:

1. Base stations are powered down (if needed).
2. Monitors are restored.
3. Managed apps are closed.
4. The supervisor exits.
