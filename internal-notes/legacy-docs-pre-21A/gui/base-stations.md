# Base Stations Tab

The **Base Stations** tab manages SteamVR base station power control through native Bluetooth LE.

## Settings

| Field | Config Key | Description |
| --- | --- | --- |
| Enable base station power automation | `BaseStationsEnabled` | When enabled, the supervisor powers on enabled base stations after SteamVR and the Pimax headset are present, then powers them down when SteamVR exits in SteamVR-bound modes. |
| Power-down mode | `BaseStationPowerDownMode` | `Sleep` works for Base Station 1.0 and 2.0. `Standby` applies to Base Station 2.0; Base Station 1.0 falls back to sleep. |

## Actions

| Button | Description |
| --- | --- |
| **Scan** | Scans nearby Bluetooth LE devices for SteamVR base stations and merges them into the list. |
| **Refresh State** | Reads live power state from enabled Base Station 2.0 devices when firmware supports it. |
| **Turn On** | Powers on every enabled base station in the list. |
| **Turn Off** | Powers down every enabled base station using the selected power-down mode. |
| **Add Manual** | Adds a base station row manually if Windows discovery does not expose it. |
| **Delete** | Deletes the selected base station row. Changes are written only when you save the config. |

## Base Station Grid

The grid has the following columns:

| Column | Description |
| --- | --- |
| Enabled | Whether the station is active. |
| Friendly name | User-facing alias. |
| Bluetooth name | SteamVR identifier (e.g., `LHB-00000000`). |
| Bluetooth address | BLE address (e.g., `AA:BB:CC:DD:EE:FF`). |
| Version | `V1` or `V2`. Inferred from Bluetooth name prefix (`HTC BS` = V1, `LHB-` = V2). |
| V1 ID | The 8-character ID printed on the back label. Required for Base Station 1.0. |
| State | Read-only. Shows live power state when refreshed. |
| Power On | Sends a power-on command to this station. |
| Sleep | Sends a sleep command to this station. |
| Standby | Sends a standby command (Base Station 2.0 only). |
| Identify | Flashes the station LED (Base Station 2.0 only). |

### Row Actions

Each row has inline buttons for **Power On**, **Sleep**, **Standby**, and **Identify** commands. These send test commands to individual stations without affecting the session lifecycle.

## Power-On Behavior

When the session starts:

1. The supervisor checks if SteamVR `vrserver.exe` is running.
2. It reads the current power state of each enabled Base Station 2.0 (if firmware supports it).
3. Stations that are already awake are skipped.
4. Up to 3 wake passes are sent. Pass 3 (for V1/unsupported stations) runs 30 seconds after pass 2.
5. If OpenVR is available, SteamVR tracking is checked after each pass. Once all stations report active, startup completes early.
6. If OpenVR is unavailable, the supervisor relies on BLE commands and state reads.

## Power-Down Behavior

When the session ends:

1. The supervisor sends the configured power-down command (`Sleep` or `Standby`).
2. For stations that support power-state reads, it confirms the state after a 2-second delay.
3. If state read fails or is unsupported, a two-pass fallback is used.
4. `PowerStateReadUnsupported` is automatically set for stations that cannot report power state.

## Base Station Version Detection

| Bluetooth Name Prefix | Version |
| --- | --- |
| `HTC BS` | V1 |
| `LHB-` | V2 |

See also: [GUI Manual Overview](index.md) Â· [Basics](basics.md) Â· [Detectors](detectors.md)
