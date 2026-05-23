# Troubleshooting

## Common Issues

| Symptom | Try |
| --- | --- |
| The supervisor exits immediately | Check whether another normal supervisor instance is already running. |
| Broken Eye or VRCFaceTracking does not launch | Open the config editor and verify the executable path and process name. |
| Reconnects are not detected | Confirm `PimaxDetectors`, `UsePimaxServiceLogReconnectDetector`, and the PiService log directory. |
| Mouth tracker reconnects do nothing | Set `MouthTrackerUser` to `true` and verify `MouthTrackerDetectors`. |
| Monitors are not restored | Let SteamVR fully exit; the supervisor waits for `vrserver.exe` before restoring when monitor handling is enabled. |
| Base stations do not scan | Confirm Windows Bluetooth LE is enabled and try the Base Stations tab **Scan** button again. Add a manual row if Windows discovery exposes the address elsewhere. |
| Base stations wake slowly | Keep `PowerStateReadUnsupported` enabled for unsupported firmware. When OpenVR is available, SteamVR tracking can stop retries early; otherwise the supervisor sends a third delayed wake pass only to V1/unsupported stations. |
| Base stations stay on after console X | Use the latest release; console close starts a detached helper that sends the configured Sleep/Standby command after the main console exits. |
| OscGoesBrrr does not start | Check `OscGoesBrrrEnabled`, the Intiface/OscGoesBrrr paths, and whether manual console launch mode or BLE scanner mode is enabled. |

## Duplicate Instances

The supervisor prevents duplicate normal instances from racing each other. If the supervisor exits immediately, check whether another instance is already running.

## Path Validation

The Config Editor validates expanded executable and folder paths, including paths that use `%APPDATA%` or `%LOCALAPPDATA%`. Press **Validate** to recheck paths after external changes.

## Base Station Power States

`PowerStateReadUnsupported` is set automatically when a station or firmware does not support reading power state. The supervisor skips future state reads for that station. Use Config Editor **Refresh State** to manually retry detection.

## OSC Router Port Conflicts

If the configured OSC router receive endpoint is already in use, the supervisor logs a warning and continues startup with routing disabled. Press `Space` in the console to retry routing.
