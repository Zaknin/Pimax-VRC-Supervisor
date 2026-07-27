# Reconnect Handling

Reconnect handling recovers explicitly dependent face-tracking tools after a relevant physical device returns. A raw USB or PnP notification is only a reason to rescan. PiService P3B lifecycle markers can contribute disconnect, return, and readiness evidence, but only after structured USB/PnP inventory has positively attributed the connected hardware as Pimax. No individual notification is a restart command.

## Physical-Device Attribution

Supervisor compares the previous and current connected-device inventories. It groups composite interfaces by Windows `ContainerId` when available and classifies the physical device from structured identity such as VID/PID, hardware IDs, parent identity, class, and service.

Index-controller and Vive Tracker SteamVR radio dongles (`28DE:2101` on the validated system) are unrelated to Pimax eye/runtime recovery. Removing or reinserting one does not restart VRCFaceTracking, Broken Eye, XSOverlay, Autostart apps, SteamVR, the OSC Router, monitors, or base stations. The same safe behavior applies to arbitrary or unknown USB changes.

Friendly names containing words such as `VR`, `tracker`, `radio`, or `headset` are not sufficient attribution.

## Relevant Reconnect Sequence

A relevant recovery requires all of these steps:

1. A known physical Pimax runtime/eye-tracking or Vive Face Tracker identity was present.
2. A qualifying disconnect was observed: physical inventory absence for USB/PnP recovery, or the positive P3B disconnect marker for the PiService path.
3. The same physical identity returned, or the correlated P3B return matched the prior headset identity where PiService identity was available.
4. The required readiness signal was confirmed.

Removal alone never restarts an application. For Pimax Play/PiService 1.4.0 P3B, Supervisor correlates `HMD disconnected: Pimax P3B` with the matching `HMD connected` identity, observes HID-ready and display-restore progress, and accepts `P3B VersionChecker: Register success` as authoritative reconnect readiness. The existing `registeredReady / confirmed` assessment remains supported. A Vive Face Tracker return must be present and configured as a VRCFaceTracking dependency.

The PiService watcher establishes its cutoff when Supervisor starts, before startup waits, and processes only newer lifecycle events from the configured bounded lookback. This avoids replaying an older reconnect and keeps a disconnect observed by the active watcher available for later return/readiness correlation. Pending reconnects expire after a bounded interval, and repeated connected/readiness lines are deduplicated.

## Scoped Recovery

- Pimax eye/runtime recovery can restart VRCFaceTracking and Broken Eye when reconnect recovery is enabled.
- An Autostart app participates only when **Restart after Pimax reconnect** is explicitly enabled.
- Vive Face Tracker recovery can restart VRCFaceTracking when that dependency is enabled.
- XSOverlay is always excluded from USB reconnect recovery. Its lifecycle remains owned by SteamVR and monitor-topology handling.
- Unknown or unattributed changes are logged and ignored safely.

The manual **Restart Core Apps** action retains its broad manual behavior.

## Diagnostics

The normal log reports one concise decision per physical-device transition and each correlated PiService stage. It distinguishes disconnect, pending-arm, candidate return, identity rejection, readiness progress, expiry, duplicate suppression, dispatch, selected application names, and completion/failure. Optional debug diagnostics include the stable hashed instance/container identity, VID/PID, parent, class, service, and classifier reason without exposing long identifiers on the dashboard. Full headset serials are not written to these diagnostics.
