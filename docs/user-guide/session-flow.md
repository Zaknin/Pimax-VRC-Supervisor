# Session Flow

This page explains what usually happens during a VRChat session.

## Start

1. SteamVR starts.
2. Supervisor starts through your chosen startup mode or manual launch.
3. Supervisor waits for the Pimax headset and VRChat conditions.
4. Enabled base stations can be powered on.
5. Face-tracking and helper tools start.
6. Optional OSC and OscGoesBrrr workflows start.

## During A Session

Supervisor watches configured process names, headset reconnect signals, and helper state. If reconnect handling is enabled, it can restart face-tracking tools after a Pimax reconnect.

## End

When VRChat and/or SteamVR exits according to your configured mode, Supervisor runs cleanup:

- closes managed tools
- powers down base stations if enabled
- restores monitors if monitor management was used
- exits if the selected startup mode expects it to exit

Terminal UI and SteamVR Overlay are control surfaces. The Supervisor performs the session work.
