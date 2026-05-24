# VR Overlay Troubleshooting

## Overlay Does Not Appear in SteamVR Dashboard

| Check | Action |
| --- | --- |
| SteamVR startup mode | Ensure "Start with SteamVR" is enabled in the Config Editor General tab. |
| Manifest registration | Run `PimaxVrcSupervisor.exe --apply-startup-integration` and check for success. |
| SteamVR restart | Restart SteamVR after changing startup settings. |
| App key conflict | Verify no other app uses the `pimax.vrcsupervisor.dashboard` key. |

## Buttons Do Nothing When Clicked

| Check | Action |
| --- | --- |
| Supervisor running | Check if `PimaxVrcSupervisor.exe` is running (elevated). |
| Command bridge | The supervisor's command pipe (`PimaxVrcSupervisor.Command`) or TCP port (37957) must be accessible. |
| UAC prompt | The supervisor must be elevated. If a UAC prompt was dismissed, the command bridge won't be available. |
| Log file | Check `%TEMP%\PimaxVrcSupervisorSteamVrHost.log` for command errors. |

## Status Shows "Waiting for elevated supervisor command bridge..."

This means the overlay host cannot connect to the supervisor. Possible causes:

1. The supervisor hasn't started yet. Wait a few seconds and try again.
2. The supervisor failed to start. Check the supervisor log for errors.
3. The helper Scheduled Task is missing. Re-save the config with "Start with SteamVR" enabled.

## Overlay Appears Blank or Corrupted

| Check | Action |
| --- | --- |
| GPU renderer | The overlay uses D3D11 by default. If D3D11 is unavailable, it falls back to static PNG. Check the log for "D3D11 overlay renderer unavailable". |
| Overlay dimensions | The overlay is 1294×820 pixels. Very high or very low GPU memory can cause rendering issues. |

## Base Station Buttons Don't Work

| Check | Action |
| --- | --- |
| Base stations enabled | `BaseStationsEnabled` must be `true` in the config. |
| BLE adapter | Windows Bluetooth LE must be enabled. |
| Station configuration | At least one base station must be configured with a valid Bluetooth address. |

## OSCGoesBrr Button Doesn't Work

| Check | Action |
| --- | --- |
| OscGoesBrrrEnabled | Must be `true` in the config. |
| Intiface/OscGoesBrrr paths | Both executable paths must be valid. |

## Console Output Not Updating

The console refreshes every 2 seconds. If it's stale:

1. Check if the supervisor is still running.
2. Check the named pipe connection in the host log.
3. Try pressing a button — if the status updates but console doesn't, the pipe may be working but the `log` command may be timing out.

## Overlay Closes Immediately

| Check | Action |
| --- | --- |
| SteamVR running | The host loop exits when `vrserver.exe` is not running. |
| OpenVR initialization | Check the log for OpenVR init errors. |
| openvr_api.dll | Must be found in the SteamVR runtime directory. |

## Log File Location

```
%TEMP%\PimaxVrcSupervisorSteamVrHost.log
```

The log includes timestamps, OpenVR event details, button click coordinates, and command responses.

See also: [VR Overlay Overview](index.md) · [VR Overlay Controls](controls.md) · [VR Overlay Configuration](configuration.md)
