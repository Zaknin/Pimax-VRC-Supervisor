# Face Tracking Startup

Pimax VRC Supervisor can start Broken Eye and VRCFaceTracking for your VRChat session.

## Configure

Open **Face Tracking** in Configurator.

Set:

- Broken Eye executable
- VRCFaceTracking executable
- whether each tool should start minimized
- process names used for detection

## Recommended Use

Leave startup enabled if these tools are part of every VRChat session. Disable the feature if you prefer starting them manually.

## Reconnect Dependencies

Pimax eye/runtime recovery can restart Broken Eye and VRCFaceTracking when Pimax reconnect recovery is enabled. Vive Face Tracker recovery can restart only VRCFaceTracking when that separate dependency is enabled. Controller and body-tracker radio dongles are not face-tracking dependencies.

For extra Autostart apps, **Restart after Pimax reconnect** is an explicit dependency setting and defaults off for new or previously unconfigured entries. XSOverlay is excluded even if it appears in Autostart apps.

## Tips

- Use **Validate** after changing paths.
- If a tool starts but is not detected, check the process name fields.
- If a relevant reconnect does not restart a tool, confirm its dependency setting and check the concise device-attribution decision in the Supervisor log.
