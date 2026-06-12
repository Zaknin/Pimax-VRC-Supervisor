# Processes Tab

The **Processes** tab configures the process names the supervisor uses to detect, monitor, and close applications.

## Tool Processes

| Field | Config Key | Default | Description |
| --- | --- | --- | --- |
| Broken Eye process name | `BrokenEyeProcessNames` | `Broken Eye` | Process names used to detect, attach to, and close Broken Eye. Do not include `.exe`. |
| VRCFaceTracking process name | `VrcFaceTrackingProcessNames` | `VRCFaceTracking` | Process names used to detect, attach to, and close VRCFaceTracking. Do not include `.exe`. |

## Session Process

| Field | Config Key | Default | Description |
| --- | --- | --- | --- |
| Apps that trigger cleanup when closed | `WatchedShutdownProcessNames` | `VRChat` | When one of these processes has been seen running and later exits, the supervisor closes managed apps and exits. If the watched app exits with a non-zero crash code, the supervisor waits for relaunch before exiting. |

## SteamVR Process

| Field | Config Key | Default | Description |
| --- | --- | --- | --- |
| SteamVR server process name | `SteamVrServerProcessNames` | `vrserver` | After VRChat exits, monitors are restored only after these processes are gone. |

## OscGoesBrrr Processes

| Field | Config Key | Default | Description |
| --- | --- | --- | --- |
| Intiface process name | `IntifaceProcessNames` | `intiface_central.exe` | Process names used to detect, attach to, and close Intiface. `.exe` is optional. |
| OscGoesBrrr process name | `OscGoesBrrrProcessNames` | `OscGoesBrrr.exe` | Process names used to detect, attach to, and close OscGoesBrrr. `.exe` is optional. |

## Process Name Rules

- Process names are matched against running processes by name (without `.exe`).
- Multiple names can be specified as comma-separated values.
- The supervisor uses these names to:
  - Check if an app is already running before starting it.
  - Attach to running processes for exit-code monitoring.
  - Gracefully close apps during cleanup (via `CloseMainWindow`), then force-close if needed.

See also: [GUI Manual Overview](index.md) Â· [Auto Startup](auto-launch.md) Â· [OSCGoesBrrr](oscgoesbrrr.md)
