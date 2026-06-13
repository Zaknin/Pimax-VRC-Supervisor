# Pimax VRC Supervisor

![Version](https://img.shields.io/badge/version-1.3.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-teal)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![Release](https://img.shields.io/badge/release-signed%20%2B%20attested-brightgreen)

Pimax VRC Supervisor is a Windows companion app for Pimax VRChat sessions. It starts the tools you rely on, watches for headset and tracker reconnects, gives you a Terminal UI for session controls, and cleans up managed apps when the session ends.

It is built for users who want less manual startup work and a cleaner SteamVR/VRChat session flow.

## What It Does

- Starts Broken Eye and VRCFaceTracking in the right order.
- Restarts face-tracking tools after headset, Vive Face Tracker, or Pimax runtime reconnects.
- Opens a Terminal UI dashboard for monitoring and confirmed session actions.
- Supports a SteamVR Overlay mode for in-headset controls.
- Powers SteamVR Base Stations on and off when configured.
- Skips unsupported or unavailable base stations without blocking startup indefinitely.
- Restores monitor/session state during cleanup.
- Runs an optional OSC Router for local OSC fan-out.
- Starts OscGoesBrrr and Intiface workflows when configured.
- Writes optional diagnostics for troubleshooting.

## Recommended For

- Pimax headset users.
- VRChat users.
- VRCFaceTracking and Broken Eye users.
- Users who want SteamVR startup automation without maintaining scripts.
- Users who want base-station power and session cleanup helpers.

## Download

Pimax VRC Supervisor is provided in two Windows x64 variants:

- **with-dotnet9**: larger, self-contained package with the required .NET 9 Windows Desktop runtime bundled.
- **no-dotnet9**: smaller framework-dependent package that requires the .NET 9 Windows Desktop Runtime x64 to be installed.

Extract the zip to a writable folder such as:

```text
C:\Tools\PimaxVrcSupervisor
```

Keep the files together in the extracted folder. The Configurator, Supervisor, SteamVR host, Terminal UI, config file, and assets are designed to run from the same flat release folder.

## Documentation

Read the full user manual:

[Pimax VRC Supervisor Manual](https://zaknin.github.io/Pimax-VRC-Supervisor/)

Useful starting points:

- [Quick Start](https://zaknin.github.io/Pimax-VRC-Supervisor/quick-start/)
- [Installation](https://zaknin.github.io/Pimax-VRC-Supervisor/quick-start/installation/)
- [Startup Modes](https://zaknin.github.io/Pimax-VRC-Supervisor/user-guide/startup-modes/)
- [Terminal UI](https://zaknin.github.io/Pimax-VRC-Supervisor/user-guide/terminal-ui/)
- [Troubleshooting](https://zaknin.github.io/Pimax-VRC-Supervisor/troubleshooting/)

## Quick Start

1. Extract the release zip.
2. Run `PimaxVrcSupervisorConfigurator.exe`.
3. Select or create a config.
4. Set paths for the tools you use.
5. Choose an **Autostart mode**.
6. Click **Validate**.
7. Click **Save**.
8. Click **Launch Supervisor**.

For most users, start with **Terminal Mode** and keep **Use Terminal UI as default interface** enabled.

## Included Apps

| App | Purpose |
| --- | --- |
| `PimaxVrcSupervisor.exe` | Session Supervisor and classic console. |
| `PimaxVrcSupervisorConfigurator.exe` | GUI setup and validation tool. |
| `PimaxVrcSupervisorTui.exe` | Terminal UI dashboard and controls. |
| `PimaxVrcSupervisorSteamVrHost.exe` | SteamVR Overlay host. |
| `PimaxVrcSupervisorStartupHelper.exe` | Startup integration helper. |
| `PimaxVrcSupervisorWatcher.exe` | Terminal Mode watcher. |

## Safety Notes

- Base-station controls affect real hardware. Test with one station first if you are unsure.
- Monitor management can change active displays during headset sessions.
- Terminal UI actions are confirmation-gated because they can restart apps or change session state.
- Connected Terminal UI shutdown runs Supervisor cleanup and may close managed apps.
- Use Diagnostics when troubleshooting; leave extra diagnostics off during normal use unless needed.

## Build From Source

Most users should use the release zip. Developers need the .NET 9 SDK, Rust stable MSVC toolchain, and Windows build tools. Release packaging is documented in `RELEASE_PACKAGING.md`.
