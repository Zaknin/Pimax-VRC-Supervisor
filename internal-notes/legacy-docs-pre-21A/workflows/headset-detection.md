# Headset Detection

The supervisor uses multiple methods to detect the Pimax Crystal headset and handle reconnects.

## USB Device Detection

The primary detection method uses `pnputil.exe /enum-devices /connected` to enumerate connected USB devices. The configured `PimaxDetectors` rules are checked against each device block.

### Default Pimax Detectors

```
USB\VID_34A4&PID_0012
USB\VID_34A4&PID_0018
USB\VID_34A4&PID_0020
USB\VID_34A4&PID_0040
USB\VID_34A4&PID_0042
USB\VID_34A4&PID_0044
USB\VID_34A4&PID_0046
Pimax, Crystal
Pimax, P3C
Pimax, WiGig
```

The `USB\VID_34A4` rules match the actual Pimax HMD/runtime interfaces. `EyeChip` alone is not enough to count as a headset reconnect.

## PiService Log Reconnect Detector

When `UsePimaxServiceLogReconnectDetector` is `true` (default), the supervisor also scans PiService logs for fast HID remove/add sequences that normal USB polling can miss.

### How It Works

1. The supervisor finds the newest `PiService__*.log` file in the configured log directory.
2. It scans the last `PimaxServiceLogReconnectLookbackLines` (default 400) lines.
3. It looks for "removed hid device" followed by "added hid device" in the log.
4. When a remove/add sequence is detected, it's treated as a Pimax runtime reconnect.

### Log Directory

Default: `%LOCALAPPDATA%\Pimax\PiService\Log`

## Reconnect Handling

When a Pimax reconnect is detected:

1. The supervisor logs the reconnect event.
2. It waits `RestartDelayAfterReconnectSeconds` (default 10) for a stable connection.
3. If the headset doesn't stay connected during the wait, it logs and waits for the next reconnect.
4. If VRChat shut down during the reconnect delay, cleanup runs immediately.
5. Otherwise, managed apps are stopped and restarted.

### Duplicate Signal Suppression

Reconnect signals within a coalesce window (max of 30 seconds, `RestartDelayAfterReconnectSeconds + PollIntervalSeconds + 5`) are ignored to prevent duplicate restarts.

## Mouth Tracker Detection

When `MouthTrackerUser` is `true`, the supervisor also monitors the Vive mouth tracker using:

1. **USB detection** via `MouthTrackerDetectors` rules.
2. **Windows PnP events** via `wevtutil.exe` (when `UseMouthTrackerPnPReconnectDetector` is `true`).

When the mouth tracker reconnects while the Pimax stays connected, only VRCFaceTracking is restarted.

### Default Mouth Tracker Detectors

```
USB\VID_0BB4&PID_0321&MI_00
HTC Multimedia Camera
VIVE, Camera
```

## Device Probe Timeout

The `DeviceProbeTimeoutSeconds` setting (default 10) controls how long the supervisor waits for `pnputil.exe` to return. If the command times out, the previous device state is kept.

See also: [Workflows Overview](index.md) Â· [Startup Flow](startup-flow.md) Â· [Base Station Power](base-station-power.md)
