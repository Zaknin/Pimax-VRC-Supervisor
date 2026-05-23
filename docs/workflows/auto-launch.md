# Auto-Launch Workflow

This page describes how the supervisor manages auto-launch applications during the session lifecycle.

## Overview

Auto-launch apps are optional tools that start after Broken Eye and VRCFaceTracking are running. They are configured in the `AutoLaunchApps` array.

## Startup Sequence

After Broken Eye and VRCFaceTracking are running:

1. The supervisor iterates through enabled auto-launch apps.
2. For each app, it checks if the executable exists.
3. If the app is already running, it logs "Already running" and skips.
4. Otherwise, it starts the app (elevated or unelevated based on `RunAsAdmin`).
5. It verifies the app is running within the startup timeout.
6. If `StartMinimized` is true, it attempts to minimize the app's main window.

## Reconnect Behavior

| `RestartOnPimaxReconnect` | Behavior |
| --- | --- |
| `true` (default) | App is stopped and restarted during Pimax reconnect cleanup. |
| `false` | App stays running during Pimax reconnect. It is still closed when the session ends. |

## Shutdown Sequence

During session cleanup:

1. Auto-launch apps are stopped in reverse order.
2. Each app gets a graceful shutdown via `CloseMainWindow()`.
3. After `ShutdownGraceSeconds` (default 8), remaining apps are force-killed.
4. Process trees are killed entirely (`entireProcessTree: true`).

## OscGoesBrrr Apps

When `OscGoesBrrrEnabled` is `true`, Intiface and OscGoesBrrr are managed separately from auto-launch apps:

- They start via the OscGoesBrrr workflow (console hotkey `2`, BLE scanner, or immediate).
- They are **not** restarted during Pimax reconnects.
- They are closed during normal session cleanup (OscGoesBrrr first, then Intiface).

## Process Name Inference

The supervisor infers the process name from the executable path (filename without extension). If the configured process names don't match, a warning is logged.

## Example

```json
"AutoLaunchApps": [
  {
    "Name": "LIV",
    "Path": "C:\\Program Files\\LIV\\LIV.exe",
    "Enabled": true,
    "RestartOnPimaxReconnect": false,
    "RunAsAdmin": false,
    "StartMinimized": true
  }
]
```

This starts LIV after core apps, leaves it running during Pimax reconnects, and closes it when the session ends.

See also: [Workflows Overview](index.md) · [OSC Routing](osc-routing.md) · [VR Overlay Workflow](vr-overlay-workflow.md)
