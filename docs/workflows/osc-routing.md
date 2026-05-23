# OSC Routing Workflow

This page describes the OSC UDP routing system that forwards VRChat OSC output to multiple local apps.

## How It Works

1. VRChat sends OSC output to `127.0.0.1:9000` (the standard VRChat OSC port).
2. The supervisor listens on `127.0.0.1:OscRouterReceivePort` (default `9001`).
3. Every received OSC datagram is forwarded unchanged to each enabled route's target port.
4. No OSC address filtering is applied — all packets are forwarded.

## Configuration

| Setting | Config Key | Default | Description |
| --- | --- | --- | --- |
| Enable OSC routing | `OscRouterEnabled` | `false` | Starts the in-process OSC router. |
| Receive port | `OscRouterReceivePort` | `9001` | Local UDP port at `127.0.0.1`. |
| Routes | `OscRoutes` | `[]` | Output routes with name, target port, and enabled state. |

### Route Object

```json
{
  "Name": "Example app",
  "AppReceivePort": 9003,
  "Enabled": true
}
```

## Lifecycle

- The OSC router starts **once**, before Broken Eye and VRCFaceTracking.
- It is **independent** from:
  - SteamVR startup
  - VRChat waiting
  - Pimax reconnect restarts
  - App autostart
  - App autoclose
- The router is **not** restarted during Pimax reconnect handling.

## Port Conflict Handling

If the configured receive port is already in use:

1. The supervisor logs a warning.
2. Startup continues with routing disabled.
3. The console shows "Press 5 to launch or restart OSC routing."
4. Pressing `5` in a visible supervisor console retries or restarts routing.

## OSCQuery Coexistence

OSCQuery-capable apps can coexist with the router because OSCQuery uses separate discovery and HTTP ports. However, do not route to an app port that VRChat already discovered through OSCQuery unless duplicate incoming packets are desired.

## Example Setup

To forward VRChat OSC to VRCFaceTracking (port 9003) and another app (port 9005):

```json
"OscRouterEnabled": true,
"OscRouterReceivePort": 9001,
"OscRoutes": [
  {
    "Name": "VRCFaceTracking",
    "AppReceivePort": 9003,
    "Enabled": true
  },
  {
    "Name": "Other app",
    "AppReceivePort": 9005,
    "Enabled": true
  }
]
```

Apps should continue sending OSC directly to VRChat at `127.0.0.1:9000`. The router only handles incoming packets from VRChat's output.

See also: [Workflows Overview](index.md) · [Base Station Power](base-station-power.md) · [Auto Launch](auto-launch.md)
