# Auto Launch Tab

The **Auto Launch** tab configures optional apps that start after Broken Eye and VRCFaceTracking are running.

## Auto-Launch Apps Grid

| Column | Description |
| --- | --- |
| Name | Display name. If empty, inferred from the executable filename. |
| Executable path | Full path to the application executable. |
| Browse... | Opens a file browser to select the executable. |
| Enabled | Whether the app is active. |
| Restart after Pimax reconnect | If checked, the app is restarted when the Pimax headset reconnects. If unchecked, it stays running during reconnect cleanup but is still closed when the session ends. |
| Run as administrator | Launches the app elevated via UAC. |
| Start minimized | Starts the app minimized, then tries to minimize its main window after launch. |

### Actions

| Button | Description |
| --- | --- |
| **Add App** | Adds a new auto-launch app row. |
| **Delete** | Deletes the selected auto-launch app row. |

## Process Name Inference

The supervisor infers the process name from the configured executable path (filename without extension). If the configured process names don't match the inferred name, a warning is logged.

## Lifecycle

1. Auto-launch apps start after Broken Eye and VRCFaceTracking are running.
2. During a Pimax reconnect, apps with "Restart after Pimax reconnect" enabled are stopped and restarted. Others stay running.
3. During session cleanup, all enabled auto-launch apps are closed (graceful shutdown first, then force-close after timeout).

## Example Configuration

```json
"AutoLaunchApps": [
  {
    "Name": "Example overlay",
    "Path": "C:\\Tools\\ExampleOverlay\\ExampleOverlay.exe",
    "Enabled": true,
    "RestartOnPimaxReconnect": true,
    "RunAsAdmin": false,
    "StartMinimized": false
  }
]
```

## Validation

The editor validates that:
- Each enabled row has a non-empty executable path.
- The executable path exists on disk.
- The path points to an `.exe` file.

See also: [GUI Manual Overview](index.md) · [Basics](basics.md) · [Processes](processes.md)
