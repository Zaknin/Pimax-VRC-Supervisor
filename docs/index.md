# Pimax VRC Supervisor

Pimax VRC Supervisor helps automate a Pimax VRChat session on Windows. It can start face-tracking tools, manage reconnects, control base stations, run OSC helpers, and clean up the session when VRChat or SteamVR closes.

The app is built around three user-facing tools:

- **Configurator** for setup and validation.
- **Supervisor** for watching the VR session and running the configured automation.
- **Terminal UI**, **SteamVR Overlay**, or both for session controls against one Supervisor.

## Where To Start

If this is your first time using the app:

1. Read [Installation](quick-start/installation.md).
2. Follow [First Setup](quick-start/first-setup.md).
3. Choose a startup mode in [Startup Modes](user-guide/startup-modes.md).
4. Use [Troubleshooting](troubleshooting/index.md) if something does not start.

## What It Can Automate

- Broken Eye and VRCFaceTracking startup.
- Face-tracking restart after headset reconnects.
- SteamVR Base Station power on and power off.
- Secondary monitor disable/restore during headset sessions.
- OSC routing for face tracking and VRChat.
- OscGoesBrrr and Intiface startup.
- Optional diagnostics for troubleshooting.

## Recommended Setup

For most users, use **Terminal UI only**. Choose **Terminal UI + SteamVR Overlay** when you want desktop and in-headset controls at the same time; both attach to one Supervisor owner.
