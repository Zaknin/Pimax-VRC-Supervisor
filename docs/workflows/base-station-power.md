# Base Station Power Workflow

This page describes how the supervisor manages SteamVR base station power during the session lifecycle.

## Power-On Sequence

### Prerequisites

- `BaseStationsEnabled` is `true`.
- At least one base station is configured with a valid Bluetooth address.
- SteamVR `vrserver.exe` is running.
- Pimax headset is connected.

### Wake Passes

The supervisor sends up to 3 wake passes:

| Pass | Timing | Target Stations |
| --- | --- | --- |
| 1 | After headset connected, before managed apps | All enabled stations that report not-awake. |
| 2 | After managed apps started | Same as pass 1. |
| 3 | 30 seconds after pass 2 | Only V1 stations and stations with `PowerStateReadUnsupported`. |

### OpenVR Tracking Confirmation

When OpenVR is available (SteamVR tracking API accessible):

- After each wake pass, the supervisor waits 10 seconds, then checks SteamVR tracking references.
- If all enabled base stations are confirmed active, startup completes early (up to 5 cycles).
- If OpenVR is unavailable, the supervisor falls back to BLE state reads.

### BLE State Reads

For Base Station 2.0 with readable firmware:

- The supervisor reads the power state before wake to avoid power-cycling already-awake stations.
- States: `Sleeping`, `Standby`, `Awake`, `Waking`, `Unknown`, `Unsupported`.
- If a station reports `Unsupported`, `PowerStateReadUnsupported` is set automatically and future state reads are skipped.

### Already-Awake Optimization

Stations that report `Awake` or `Waking` are skipped in all wake passes.

## Power-Down Sequence

### Trigger

Power-down runs when:
- VRChat exits (normal session end).
- SteamVR `vrserver.exe` exits (SteamVR mode).
- Console window is closed (emergency cleanup helper).

### Command

The configured `BaseStationPowerDownMode` is sent:
- `Sleep` — Works for Base Station 1.0 and 2.0.
- `Standby` — Base Station 2.0 only. Base Station 1.0 falls back to sleep.

### Confirmation

1. Send the power-down command.
2. Wait 2 seconds.
3. Read the power state.
4. If the state confirms the command (Sleeping or Standby), the station is handled.
5. If state read fails or doesn't confirm, a two-pass fallback is used.

### Fallback

Stations that can't confirm power-down (V1, unsupported firmware, or read failure) get two additional sleep command passes.

## Emergency Cleanup

When the console window is closed:

1. A detached helper process is launched with `--emergency-base-station-cleanup`.
2. The helper waits 6 seconds (to let the main process exit), then runs the power-down sequence.
3. This ensures base stations are powered down even if the console is closed abruptly.

## Base Station Version Behavior

| Version | Power State Read | Standby Support | Extended Wake Passes |
| --- | --- | --- | --- |
| V1 | Unsupported (always) | No | Yes |
| V2 (readable) | Supported | Yes | No |
| V2 (unsupported firmware) | Unsupported | Yes | Yes |

Version is inferred from BLE name prefix: `HTC BS` = V1, `LHB-` = V2.

See also: [Workflows Overview](index.md) · [Startup Flow](startup-flow.md) · [OSC Routing](osc-routing.md)
