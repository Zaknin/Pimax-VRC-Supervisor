# Pimax VRC Supervisor Manual

![Version](https://img.shields.io/badge/version-1.2.0-2563eb)
![Platform](https://img.shields.io/badge/platform-Windows-0f766e)
![Runtime](https://img.shields.io/badge/.NET-9.0-512bd4)

A small Windows companion app for Pimax Crystal and VRChat sessions. It waits for the headset, starts the tools you rely on, watches for device reconnects, and cleans everything up when VRChat is done.

## What It Does

Pimax + VRChat setups can be fragile when USB devices blink, the runtime reconnects, or face tracking starts in the wrong order. Pimax VRC Supervisor keeps that session flow predictable:

- Wait until the Pimax headset is actually present
- Launch Broken Eye, then VRCFaceTracking
- Restart the right apps after headset or mouth tracker reconnects
- Optionally manage secondary monitors during VR
- Optionally manage SteamVR base-station power through native Bluetooth LE
- Optionally auto-launch when VRChat starts while SteamVR is running
- Optionally start from SteamVR through a VR app manifest with a small SteamVR-launched control dashboard
- Optionally start Intiface and OscGoesBrrr for Lovense workflows
- Optionally route local OSC packets to multiple OSC apps

## Included Apps

| App | Purpose |
| --- | --- |
| `PimaxVrcSupervisor.exe` | Console supervisor that watches the VR session and manages apps/devices. |
| `PimaxVrcSupervisorConfigEditor.exe` | GUI editor for `supervisor.config.json`. |
| `supervisor.config.json` | Documented configuration file copied next to the executables. |

## Quick Links

- [Getting Started](getting-started.md) — overview of features and concepts
- [Installation](installation.md) — download, extract, and first-run setup
- [Configuration](configuration.md) — key settings and examples
- [GUI Manual](gui/index.md) — using the Config Editor
- [VR Overlay](overlay/index.md) — SteamVR dashboard host
- [Workflows](workflows/index.md) — OscGoesBrrr, OSC Router, and more
- [Reference](reference/index.md) — full configuration reference
- [Troubleshooting](troubleshooting/index.md) — common issues and fixes
