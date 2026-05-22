# OSC Router Tab

The **OSC Router** tab configures the in-process OSC UDP router that forwards VRChat OSC output to multiple local apps.

## Settings

| Field | Config Key | Description |
| --- | --- | --- |
| Enable OSC routing | `OscRouterEnabled` | Starts the in-process OSC router before Broken Eye and VRCFaceTracking. |
| Supervisor receive port | `OscRouterReceivePort` | Local UDP port the OSC router listens on at `127.0.0.1`. Default: `9001`. |

## How OSC Routing Works

1. Apps keep sending OSC directly to VRChat at `127.0.0.1:9000`.
2. The supervisor listens for VRChat output on `127.0.0.1:OscRouterReceivePort`.
3. Every received OSC datagram is forwarded unchanged to each enabled route's target port.
4. No OSC address filtering is applied.

## OSC Routes Grid

| Column | Description |
| --- | --- |
| Enabled | Whether the route is active. |
| Name | Display name for the route. |
| Target app receive port | Local UDP port of the target app (1–65535). |

### Actions

| Button | Description |
| --- | --- |
| **Add Route** | Adds a new OSC route row. |
| **Delete** | Deletes the selected OSC route row. |

## Lifecycle

- The OSC router starts once, before Broken Eye and VRCFaceTracking.
- It is independent from SteamVR startup, VRChat waiting, Pimax reconnect restarts, app autostart, and app autoclose.
- If the configured receive endpoint is already in use, the supervisor logs a warning and continues with routing disabled.
- Press `Space` in the supervisor console to retry routing.

## OSCQuery Coexistence

OSCQuery-capable apps can coexist with the router because OSCQuery uses separate discovery and HTTP ports. Do not route to an app port that VRChat already discovered through OSCQuery unless duplicate incoming packets are desired.

## Validation

The editor validates that:
- Each enabled route has a name and a valid target port (1–65535).
- No two enabled routes share the same target port.

See also: [GUI Manual Overview](index.md) · [Auto Launch](auto-launch.md) · [OSCGoesBrrr](oscgoesbrrr.md)
