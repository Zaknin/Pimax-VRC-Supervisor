# Pimax VRC Supervisor

Pimax VRC Supervisor is a Windows utility for Pimax VRChat setups. It helps start face-tracking tools, manage session cleanup, control base stations, run OSC helpers, and provide a simple Configurator plus Terminal UI for day-to-day control.

The release-candidate user manual is published at [Pimax VRC Supervisor Manual](https://zaknin.github.io/Pimax-VRC-Supervisor/).

## Who It Is For

Use Pimax VRC Supervisor if you use a Pimax headset with VRChat and want help with one or more of these tasks:

- Starting Broken Eye and VRCFaceTracking for headset sessions.
- Restarting face-tracking tools after reconnects.
- Powering SteamVR Base Stations on and off.
- Starting OscGoesBrrr and Intiface.
- Routing OSC messages between VRChat, face tracking, and helper tools.
- Turning off secondary monitors during headset sessions and restoring them afterward.
- Starting from SteamVR through Terminal Mode or SteamVR Overlay mode.

## Main Parts

- **Configurator**: the main setup tool. Use it to select paths, choose startup mode, configure features, validate settings, and save your config.
- **Supervisor**: the background/session controller that watches SteamVR, VRChat, headset state, and configured tools.
- **Terminal UI**: a terminal dashboard for monitoring the Supervisor and running confirmed session actions.
- **SteamVR Overlay**: an optional SteamVR dashboard interface for session controls inside SteamVR.

## Quick Start

1. Download the release zip from the project releases page.
2. Extract it to a writable folder.
3. Run `PimaxVrcSupervisorConfigurator.exe`.
4. Open **General** and choose an **Autostart mode**.
5. Fill in required tool paths on the feature tabs you use.
6. Click **Validate**.
7. Click **Save**.
8. Click **Launch Supervisor**.

For most users, start with **Terminal Mode** and keep **Use Terminal UI as default interface** enabled. This starts the Supervisor for SteamVR sessions and opens Terminal UI automatically.

## Documentation

Start here:

- [User Manual](https://zaknin.github.io/Pimax-VRC-Supervisor/)
- [Quick Start](https://zaknin.github.io/Pimax-VRC-Supervisor/quick-start/)
- [Installation](https://zaknin.github.io/Pimax-VRC-Supervisor/quick-start/installation/)
- [Startup Modes](https://zaknin.github.io/Pimax-VRC-Supervisor/user-guide/startup-modes/)
- [Troubleshooting](https://zaknin.github.io/Pimax-VRC-Supervisor/troubleshooting/)

## Safety Notes

- Confirmed actions in Terminal UI can restart tools, start helper apps, or change base-station power state.
- Connected Terminal UI shutdown runs Supervisor cleanup. It may close managed apps and restore session state.
- SteamVR Overlay mode is separate from Terminal Mode. Choose the one that fits how you want to control the session.
- Raw JSON editing is advanced. Prefer the Configurator tabs unless you know exactly what you need to change.

## Build Notes

Most users do not need to build from source. Developers can use the existing .NET 9 and Rust project files, but build instructions are intentionally kept out of the user manual.
