# Reconnect Handling

Reconnect handling recovers explicitly dependent face-tracking tools after a relevant physical device returns. A raw USB, PnP, or PiService notification is only a reason to rescan. It is never a restart command.

## Physical-Device Attribution

Supervisor compares the previous and current connected-device inventories. It groups composite interfaces by Windows `ContainerId` when available and classifies the physical device from structured identity such as VID/PID, hardware IDs, parent identity, class, and service.

Index-controller and Vive Tracker SteamVR radio dongles (`28DE:2101` on the validated system) are unrelated to Pimax eye/runtime recovery. Removing or reinserting one does not restart VRCFaceTracking, Broken Eye, XSOverlay, Autostart apps, SteamVR, the OSC Router, monitors, or base stations. The same safe behavior applies to arbitrary or unknown USB changes.

Friendly names containing words such as `VR`, `tracker`, `radio`, or `headset` are not sufficient attribution.

## Relevant Reconnect Sequence

A relevant recovery requires all of these steps:

1. A known physical Pimax runtime/eye-tracking or Vive Face Tracker identity was present.
2. That same physical identity became absent.
3. The same identity returned.
4. The required readiness signal was confirmed.

Removal alone never restarts an application. A Pimax return waits for the existing `registeredReady / confirmed` assessment. A Vive Face Tracker return must be present and configured as a VRCFaceTracking dependency.

## Scoped Recovery

- Pimax eye/runtime recovery can restart VRCFaceTracking and Broken Eye when reconnect recovery is enabled.
- An Autostart app participates only when **Restart after Pimax reconnect** is explicitly enabled.
- Vive Face Tracker recovery can restart VRCFaceTracking when that dependency is enabled.
- XSOverlay is always excluded from USB reconnect recovery. Its lifecycle remains owned by SteamVR and monitor-topology handling.
- Unknown or unattributed changes are logged and ignored safely.

The manual **Restart Core Apps** action retains its broad manual behavior.

## Diagnostics

The normal log reports one concise decision per physical-device transition. Optional debug diagnostics include the stable hashed instance/container identity, VID/PID, parent, class, service, and classifier reason without exposing long identifiers on the dashboard.
