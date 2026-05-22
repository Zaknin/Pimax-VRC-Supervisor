# Getting Started

## Overview

Pimax VRC Supervisor is a Windows companion app that manages the lifecycle of a Pimax Crystal + VRChat session. It automates the startup order of face-tracking tools, handles device reconnects, and cleans up when you are done.

## Why It Exists

Pimax + VRChat setups can be fragile when:

- USB devices disconnect and reconnect
- The Pimax runtime restarts
- Face tracking starts in the wrong order
- Base stations need power management

The supervisor keeps that session flow predictable by watching devices and processes, then starting and stopping tools at the right time.

## Session Lifecycle

### 1. Startup

1. The supervisor waits for a Pimax Crystal-compatible headset to be detected.
2. It launches **Broken Eye**, retries if needed, then starts **VRCFaceTracking** after a configurable delay.
3. Optional user-defined apps start after the main sequence.
4. If enabled, SteamVR base stations are powered on after the headset is connected and SteamVR is running.

### 2. Runtime Monitoring

- **Pimax reconnects**: The supervisor watches USB/runtime state and PiService logs. When the headset reconnects, it waits for a stable link, then restarts Broken Eye and VRCFaceTracking.
- **Mouth tracker reconnects**: If a Vive mouth tracker is configured, the supervisor watches Windows PnP events. Only VRCFaceTracking is restarted; other apps stay running.
- **OSC routing**: An in-process OSC UDP router can forward VRChat output to multiple local apps throughout the session.

### 3. Shutdown

1. When `VRChat.exe` exits, the supervisor begins cleanup.
2. If VRChat appears to crash, a configurable grace period allows for relaunch before cleanup.
3. Managed apps are closed gracefully, then force-closed after a timeout.
4. Secondary monitors are restored to their previous layout.
5. Base stations receive the configured sleep or standby command.

## Startup Modes

| Mode | How it starts | How it stops |
| --- | --- | --- |
| **Manual** | You run `PimaxVrcSupervisor.exe` | VRChat exits |
| **Scheduled Task** | An elevated watcher starts the supervisor when VRChat and SteamVR are both running | VRChat exits |
| **SteamVR Manifest** | SteamVR launches a dashboard overlay host, which starts the supervisor elevated | SteamVR `vrserver.exe` exits |

## Requirements

- Windows 10/11
- No separate .NET install when using the self-contained release zip
- Pimax Crystal-compatible headset
- [Broken Eye](https://github.com/ghostiam/BrokenEye)
- [VRCFaceTracking](https://docs.vrcft.io/docs/vrcft-software/vrcft)
- Optional: Vive mouth tracker exposed as `HTC Multimedia Camera`
- Optional: Intiface + OscGoesBrrr for Lovense workflows
