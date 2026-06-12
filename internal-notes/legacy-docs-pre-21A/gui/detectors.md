# Detectors Tab

The **Detectors** tab configures keyword rules used to detect Pimax headsets, mouth trackers, and Lovense devices.

## Pimax Detector Rules

Each line is one possible Pimax headset match rule. Comma-separated keywords on the same line must all match within a single connected-device block.

Default rules:
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

### Testing Pimax Rules

Click **Test Pimax Rules** to scan currently connected devices and report which rules match. The test uses `pnputil.exe /enum-devices /connected` and shows matched device blocks with description, class, manufacturer, status, and instance ID.

## Mouth Tracker Detector Rules

Each line is one possible mouth tracker match rule. When a mouth tracker appears after being missing, only VRCFaceTracking is restarted.

Default rules:
```
USB\VID_0BB4&PID_0321&MI_00
HTC Multimedia Camera
VIVE, Camera
```

> Mouth tracker detection is ignored when `MouthTrackerUser` is `false`.

### Testing Mouth Tracker Rules

Click **Test Mouth Tracker Rules** to scan connected devices and report matches.

## Lovense Detector Rules

Each line is one possible Lovense match rule. These match against BLE advertisement local names and Windows Bluetooth device names from the registry.

Default rules:
```
Lovense
LVS-
```

Direct Bluetooth/WebBluetooth Lovense toys usually advertise names starting with `LVS-`. Add exact Windows hardware IDs or device names here if your adapter exposes them differently.

## How Detection Works

The supervisor uses `pnputil.exe /enum-devices /connected` to enumerate connected devices. Each device block is checked against the detector rules. A rule matches when all keywords in a line are found within a single device block.

For Lovense devices, the supervisor also checks the Windows Bluetooth registry (`HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices`) for recently seen device names matching the detector rules.

See also: [GUI Manual Overview](index.md) Â· [Base Stations](base-stations.md) Â· [Timing](timing.md)
