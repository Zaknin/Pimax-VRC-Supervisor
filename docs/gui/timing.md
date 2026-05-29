# Timing Tab

The **Timing** tab controls startup waits, reconnect delays, crash recovery, shutdown grace periods, and device probing.

## Reconnect Detection

| Field | Config Key | Default | Range | Description |
| --- | --- | --- | --- | --- |
| Supervisor check interval | `PollIntervalSeconds` | `2` | 1â€“3600 | How often the supervisor checks device and watched-process state. |
| Restart delay after reconnect | `RestartDelayAfterReconnectSeconds` | `10` | 0â€“3600 | Seconds to wait after Pimax reconnects before restarting managed apps. |

## Startup Verification

| Field | Config Key | Default | Range | Description |
| --- | --- | --- | --- | --- |
| Startup timeout | `StartupTimeoutSeconds` | `30` | 1â€“3600 | Maximum seconds to wait for launched apps to appear before startup is considered failed. |
| Required stable time | `StartupStableSeconds` | `5` | 0â€“3600 | Seconds an app must remain running before startup verification succeeds. |
| Delay before VRCFaceTracking | `DelayBeforeVrcFaceTrackingSeconds` | `3` | 0â€“3600 | Seconds to wait after starting Broken Eye before starting VRCFaceTracking. |

## Crash and Shutdown Behavior

| Field | Config Key | Default | Range | Description |
| --- | --- | --- | --- | --- |
| Crash relaunch grace period | `WatchedProcessCrashRelaunchGraceSeconds` | `300` | 0â€“86400 | If VRChat exits with a likely crash code, seconds to wait for it to relaunch before cleanup. |
| Shutdown grace period | `ShutdownGraceSeconds` | `8` | 0â€“3600 | Seconds to wait for graceful app shutdown before force-closing process trees. |

## Device Probing

| Field | Config Key | Default | Range | Description |
| --- | --- | --- | --- | --- |
| Device probe timeout | `DeviceProbeTimeoutSeconds` | `10` | 1â€“3600 | Maximum seconds to wait for the Windows device query command (`pnputil.exe`). |

## Validation Warnings

The editor warns when timing values are outside recommended ranges:

| Setting | Low Warning | High Warning |
| --- | --- | --- |
| PollIntervalSeconds | < 1 | > 120 |
| StartupTimeoutSeconds | < 3 | > 300 |
| StartupStableSeconds | < 1 | > 120 |
| RestartDelayAfterReconnectSeconds | < 1 | > 300 |
| ShutdownGraceSeconds | < 1 | > 120 |
| DeviceProbeTimeoutSeconds | < 2 | > 120 |

See also: [GUI Manual Overview](index.md) Â· [Detectors](detectors.md) Â· [Raw JSON](raw-json.md)
