# Reconnect Handling

Reconnect handling helps recover when the headset or related devices briefly disconnect.

## What It Watches

Depending on your settings, Supervisor can watch:

- Pimax headset state
- PiService logs
- Windows device events
- Vive Face Tracker reconnects

## What It Can Do

It can restart configured face-tracking tools after a reconnect and wait for short stability delays before relaunching.

## When To Leave It Off

Leave advanced detectors off if your setup is stable or if you are troubleshooting unrelated startup problems.
