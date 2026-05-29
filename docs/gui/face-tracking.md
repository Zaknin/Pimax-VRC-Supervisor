# Face Tracking Tab

The **Face Tracking** tab contains Broken Eye, VRCFaceTracking, and mouth-tracker settings.

## Application Paths

| Checkbox | Config Key | Default | Description |
| --- | --- | --- | --- |
| Enable Face Tracking Auto Startup | `FaceTrackerAutomationEnabled` | `true` | Automatically starts configured face-tracking applications during headset sessions. |
| Use Broken Eye | `UseBrokenEye` | `true` | Includes Broken Eye in startup, cleanup, and restart routines, including manual dashboard restarts. |

| Field | Config Key | Description |
| --- | --- | --- |
| Broken Eye executable | `BrokenEyePath` | Full path to `Broken Eye.exe`. The supervisor starts this app first. |
| VRCFaceTracking executable | `VrcFaceTrackingPath` | Full path to `VRCFaceTracking.exe`. Starts after Broken Eye settles. |

Both paths support environment variables like `%APPDATA%` and `%LOCALAPPDATA%`. The editor validates expanded paths and shows **Found** / **Not found** indicators.

## Startup Behavior

| Checkbox | Config Key | Description |
| --- | --- | --- |
| Start Broken Eye minimized | `BrokenEyeStartMinimized` | Starts Broken Eye minimized, then tries to minimize its main window after launch. |
| Start VRCFaceTracking minimized | `VrcFaceTrackingStartMinimized` | Starts VRCFaceTracking minimized, then tries to minimize its main window after launch. |

## Mouth Tracker

| Checkbox | Config Key | Description |
| --- | --- | --- |
| Use Vive mouth tracker | `MouthTrackerUser` | Enables mouth tracker monitoring. |
| Enable automatic restart on mouth tracker reconnects | `MouthTrackerRestartOnReconnectEnabled` | Restarts VRCFaceTracking after a mouth tracker reconnect while the headset stays connected. Disabled when mouth tracker use is off. |

## Reconnect Detection

| Control | Config Key | Description |
| --- | --- | --- |
| Enable automatic restart on headset reconnects | `FaceTrackerRestartOnReconnectEnabled` | Restarts the configured face-tracking apps after a Pimax headset reconnect. Disabled when automation is off. |
| Watch Pimax PiService logs for fast reconnects | `UsePimaxServiceLogReconnectDetector` | Scans PiService logs for quick headset HID remove/add reconnects that normal USB polling can miss. |
| Watch Windows PnP events for fast mouth tracker reconnects | `UseMouthTrackerPnPReconnectDetector` | Scans Windows Kernel-PnP events for short mouth-tracker reconnects. |
| PiService log folder | `PimaxServiceLogDirectory` | Folder containing `PiService__*.log` files. |
| Pimax log scan depth | `PimaxServiceLogReconnectLookbackLines` | Number of recent PiService log lines scanned each polling cycle. |
