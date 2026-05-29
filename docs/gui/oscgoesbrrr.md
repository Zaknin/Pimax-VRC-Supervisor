# OSCGoesBrrr Tab

The **OSCGoesBrrr** tab configures the optional Lovense/Intiface/OscGoesBrrr workflow.

## Feature Enablement

| Checkbox | Config Key | Description |
| --- | --- | --- |
| Enabled | `OscGoesBrrrEnabled` | Enables the OscGoesBrrr workflow during headset sessions. |
| Use manual console launch mode | `OscGoesBrrrHotkeyEnabled` | Keeps the workflow in manual console launch mode instead of prestarting Intiface and watching Windows Lovense device detection. Console hotkey `2` is available whenever `OscGoesBrrrEnabled` is true. |
| Enable BLE scanner | `OscGoesBrrrBleScannerEnabled` | Scans nearby BLE advertisements for Lovense names (e.g., `LVS-`) and auto-launches the workflow when one matches. |

## Executables

| Field | Config Key | Default Path |
| --- | --- | --- |
| Intiface executable | `IntifacePath` | `%APPDATA%\IntifaceCentral\intiface_central.exe` |
| OscGoesBrrr executable | `OscGoesBrrrPath` | `%LOCALAPPDATA%\Programs\OscGoesBrrr\OscGoesBrrr.exe` |

## Startup Behavior

| Checkbox | Config Key | Description |
| --- | --- | --- |
| Start Intiface minimized | `IntifaceStartMinimized` | Starts Intiface minimized. |
| Start OscGoesBrrr minimized | `OscGoesBrrrStartMinimized` | Starts OscGoesBrrr minimized. |
| Delay before OscGoesBrrr | `DelayBeforeOscGoesBrrrSeconds` | Seconds to wait after Intiface is running before starting OscGoesBrrr. Default: `3`. |

## Process Detection

| Field | Config Key | Description |
| --- | --- | --- |
| Intiface process name | `IntifaceProcessNames` | Process names used to detect, attach to, and close Intiface. `.exe` is optional. Default: `intiface_central.exe`. |
| OscGoesBrrr process name | `OscGoesBrrrProcessNames` | Process names used to detect, attach to, and close OscGoesBrrr. `.exe` is optional. Default: `OscGoesBrrr.exe`. |

## BLE Scanning

| Field | Config Key | Default | Description |
| --- | --- | --- | --- |
| BLE scan duration | `OscGoesBrrrBleScanSeconds` | `30` | Seconds each BLE scan burst runs. |
| BLE scan interval | `OscGoesBrrrBleScanIntervalSeconds` | `60` | Seconds to wait after each unsuccessful BLE scan before trying again. |

## Lovense Detection Rules

Each line in the text box is one possible Lovense match rule. Multiple keywords on the same line (comma-separated) must all match.

Default rules:
```
Lovense
LVS-
```

These match against BLE advertisement local names and Windows Bluetooth device names from the registry.

## Workflow Lifecycle

1. **Manual console mode:** Press `2` in a visible supervisor console to start Intiface, wait the configured delay, then start OscGoesBrrr.
2. **BLE scanner mode:** Scanner detects Lovense advertisement â†’ auto-launches workflow.
3. **Auto-repair:** If Intiface is running but OscGoesBrrr is missing, the BLE scanner repairs the workflow.
4. Pimax reconnects do **not** restart Intiface/OscGoesBrrr.
5. Normal session cleanup closes both apps.

See also: [GUI Manual Overview](index.md) Â· [OSC Router](osc-router.md) Â· [Detectors](detectors.md)
