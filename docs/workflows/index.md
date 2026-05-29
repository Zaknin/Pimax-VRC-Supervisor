# Workflows

## OscGoesBrrr / Lovense Workflow

The optional `OscGoesBrrrEnabled` workflow manages Intiface and OscGoesBrrr for Lovense device integration.

### Hotkey Mode

Press `L` in the supervisor console to:

1. Start Intiface.
2. Wait the configured delay.
3. Start OscGoesBrrr.

### BLE Scanner Mode

When `OscGoesBrrrBleScannerEnabled` is `true`, the supervisor scans for Lovense BLE advertisements (e.g., `LVS-`) and auto-launches the same workflow.

### Auto-Repair

If Intiface is running but OscGoesBrrr is missing, the supervisor can repair the workflow.

### Lifecycle

- Pimax reconnects do **not** restart Intiface/OscGoesBrrr.
- Normal session cleanup closes them.

## OSC Router

The optional in-process OSC UDP router sends VRChat OSC output to multiple local apps.

### How It Works

1. Apps keep sending OSC directly to VRChat at `127.0.0.1:9000`.
2. The supervisor listens for VRChat output on `127.0.0.1:OscRouterReceivePort` (default `9001`).
3. Every received OSC datagram is forwarded unchanged to each enabled app receive port at `127.0.0.1`.

### Lifecycle

- Starts once before Broken Eye and VRCFaceTracking.
- Independent from SteamVR startup, VRChat waiting, Pimax reconnect restarts, app autostart, and app autoclose.
- If the configured receive endpoint is already in use, the supervisor logs a warning, continues startup with routing disabled, then lets you press `Space` in the console to retry.

### OSCQuery Coexistence

OSCQuery-capable apps can coexist with the router because OSCQuery uses separate discovery and HTTP ports. Do not route to an app port VRChat already discovered through OSCQuery unless duplicate incoming packets are desired.
