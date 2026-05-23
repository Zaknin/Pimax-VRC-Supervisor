# OSC Issues

## OSC Router Doesn't Start

| Check | Action |
| --- | --- |
| `OscRouterEnabled` | Must be `true` in the config. |
| Port available | Ensure no other app is using the configured receive port (default 9001). |
| Console message | Look for "Warning: OSC router could not bind to 127.0.0.1:9001 because the endpoint is already in use." |

### Retrying OSC Routing

If the port was in use at startup:

1. Close the app that's using the port.
2. Press `Space` in the supervisor console to retry.

## Duplicate OSC Packets

If an app receives duplicate OSC packets:

1. Check if VRChat discovered the app through OSCQuery.
2. If so, don't also route to that same app's receive port.
3. Either disable the OSCQuery discovery or disable the OSC route for that app.

## OSC Router Port Conflicts

Common apps that might use port 9001:

- Other OSC routers
- VRCFT (if configured to listen on 9001)
- Custom OSC tools

Change `OscRouterReceivePort` to an unused port (e.g., 9002, 9003).

## Apps Not Receiving OSC

| Check | Action |
| --- | --- |
| Route enabled | The route must be `Enabled: true`. |
| Correct port | The `AppReceivePort` must match the app's listen port. |
| App listening | The target app must be actively listening on the specified port. |
| Firewall | Windows Firewall may block local UDP traffic. |

## OscGoesBrrr Doesn't Start

| Check | Action |
| --- | --- |
| `OscGoesBrrrEnabled` | Must be `true`. |
| Intiface path | Verify `IntifacePath` points to a valid executable. |
| OscGoesBrrr path | Verify `OscGoesBrrrPath` points to a valid executable. |
| Manual console launch mode | If using manual mode, press `2` in a visible supervisor console. |
| BLE scanner | If using BLE mode, ensure Bluetooth is enabled and a Lovense device is nearby. |

## BLE Scanner Not Detecting Lovense

| Check | Action |
| --- | --- |
| Bluetooth LE | Windows Bluetooth LE must be enabled. |
| Device proximity | The Lovense device must be within BLE range. |
| Detector rules | Add custom rules in the **OSCGoesBrrr** tab if your device uses a non-standard name. |
| Registry fallback | The supervisor also checks the Windows Bluetooth registry for recently seen devices (within 1 hour). |

## OSC Router Not Forwarding After Reconnect

The OSC router is independent from Pimax reconnect handling. If it stops forwarding:

1. Check if the router is still running (console should show "OSC router is running").
2. Press `5` in a visible supervisor console to launch or restart OSC routing.
3. If the router crashed, restart the supervisor.

See also: [Troubleshooting Overview](index.md) · [Config Editor Issues](config-editor-issues.md) · [Base Station Issues](base-station-issues.md)
