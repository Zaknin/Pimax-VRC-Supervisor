# Base Station Issues

## Base Stations Don't Scan

| Check | Action |
| --- | --- |
| Bluetooth LE | Ensure Windows Bluetooth LE is enabled. |
| BLE adapter | The Config Editor requires a Bluetooth LE adapter. Check Device Manager. |
| Scan duration | The default scan is 10 seconds. Try scanning multiple times. |
| Manual add | If Windows discovery doesn't expose the station, add it manually with the **Add Manual** button. |

## Base Stations Wake Slowly

| Cause | Solution |
| --- | --- |
| V1 firmware | Base Station 1.0 always requires 3 wake passes. This is expected. |
| Unsupported firmware | Stations with `PowerStateReadUnsupported` skip state reads, which speeds up subsequent launches. |
| OpenVR unavailable | When OpenVR is available, SteamVR tracking can confirm stations early, reducing retries. |

## Base Stations Stay On After Console Close

The supervisor launches a detached emergency cleanup helper when the console closes. If stations stay on:

1. Ensure you're using the latest release (this feature was added recently).
2. Check that `PimaxVrcSupervisor.exe` can be found at the expected path.
3. The helper waits 6 seconds before sending power-down commands.

## "PowerStateReadUnsupported" Not Set Automatically

This flag is set when:
- A Base Station 2.0 firmware doesn't support power state reads.
- The state read returns `Unsupported`.

To manually retry detection, click **Refresh State** in the Config Editor's **Base Stations** tab.

## Base Station Commands Fail

| Check | Action |
| --- | --- |
| Bluetooth connection | Ensure the PC can communicate with the station via BLE. |
| V1 ID | Base Station 1.0 requires the 8-character ID printed on the back label. |
| Standby on V1 | Standby is not supported for Base Station 1.0. Use Sleep. |
| Identify on V1 | Identify is not supported for Base Station 1.0. |

## Duplicate Bluetooth Addresses

The Config Editor validates that no two base station rows share the same Bluetooth address. If you see this error, remove or correct the duplicate row.

## Base Station Version Detection

The supervisor infers version from the BLE name:

| BLE Name Prefix | Version |
| --- | --- |
| `HTC BS` | V1 |
| `LHB-` | V2 |

If the version is wrong, manually set it in the **Version** column of the base stations grid.

## OpenVR Tracking Confirmation Unavailable

If the log shows "SteamVR base-station tracking confirmation unavailable":

1. SteamVR may not be fully started.
2. The `openvr_api.dll` may not be found in the SteamVR runtime.
3. The supervisor falls back to BLE state reads automatically.

See also: [Troubleshooting Overview](index.md) · [OSC Issues](osc-issues.md) · [Overlay Issues](overlay-issues.md)
