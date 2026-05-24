# Overlay Issues

## Overlay Doesn't Appear in SteamVR

| Check | Action |
| --- | --- |
| SteamVR startup mode | Enable "Start with SteamVR" in the Config Editor General tab and save. |
| Manifest applied | Run `PimaxVrcSupervisor.exe --apply-startup-integration`. |
| SteamVR restarted | Restart SteamVR after changing startup settings. |
| OpenVR DLL | Ensure `openvr_api.dll` exists in the SteamVR runtime directory. |

## Overlay Appears But Buttons Don't Work

| Check | Action |
| --- | --- |
| Supervisor running | Verify `PimaxVrcSupervisor.exe` is running (elevated). |
| Command bridge | The supervisor's command pipe or TCP port must be accessible. |
| UAC prompt | If the UAC prompt was dismissed, the supervisor won't be elevated and the command bridge won't start. |
| Log file | Check `%TEMP%\PimaxVrcSupervisorSteamVrHost.log` for errors. |

## Status Shows "Waiting for elevated supervisor command bridge..."

This means the overlay host cannot connect to the supervisor.

1. Wait a few seconds — the supervisor may still be starting.
2. Check if the supervisor is running in Task Manager.
3. Re-save the config with "Start with SteamVR" enabled to re-register the helper task.
4. Check the host log for connection errors.

## Overlay Rendering Issues

| Symptom | Solution |
| --- | --- |
| Blank overlay | The D3D11 renderer may have failed. The host falls back to static PNG. Check the log for "D11 overlay refresh failed". |
| Corrupted texture | Restart SteamVR. The overlay texture is recreated on each SteamVR session. |
| Wrong size | The overlay is 1294×820 pixels at 2.5m wide. This is fixed and not configurable. |

## Console Output Not Updating in VR

The console refreshes every 2 seconds via the `log` command.

1. Check if the supervisor is running.
2. The named pipe connection may be failing — check the host log.
3. Try pressing a button. If status updates but console doesn't, the pipe is working but the `log` command may be timing out.

## Overlay Closes Immediately

| Check | Action |
| --- | --- |
| SteamVR running | The host loop exits when `vrserver.exe` exits. |
| OpenVR init | Check the log for OpenVR initialization errors. |
| SteamVR runtime | Ensure the SteamVR runtime path is configured in `%LOCALAPPDATA%\openvr\openvrpaths.vrpath`. |

## Base Station Buttons Don't Work in Overlay

| Check | Action |
| --- | --- |
| `BaseStationsEnabled` | Must be `true`. |
| BLE adapter | Windows Bluetooth LE must be enabled. |
| Station config | At least one base station must be configured with a valid Bluetooth address. |
| Supervisor elevated | The supervisor must be running elevated to send BLE commands. |

## Log File

The overlay host logs to:

```
%TEMP%\PimaxVrcSupervisorSteamVrHost.log
```

Each entry includes a timestamp and message. Look for:
- "Could not create overlay" — OpenVR initialization failed.
- "Command failed" — The supervisor didn't respond.
- "D11 overlay renderer unavailable" — GPU rendering fell back to PNG.

See also: [Troubleshooting Overview](index.md) · [Base Station Issues](base-station-issues.md) · [Install Issues](install-issues.md)
