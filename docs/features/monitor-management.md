# Monitor Management

Supervisor can turn off secondary monitors during headset sessions and restore the layout afterward.

## When To Use It

Use this if secondary monitors interfere with performance, focus, or privacy during VR sessions.

## What To Expect

At session start, Supervisor saves the current monitor layout and disables secondary monitors. During cleanup, it restores the saved layout only when that automatic shutdown succeeded in the current Supervisor session.

If monitor shutdown is disabled, was never attempted, failed before changing topology, or was not owned by Supervisor, exit flows do not issue a monitor-restore operation just because Terminal UI or Supervisor exits.

## Caution

Test this feature before relying on it. Display drivers and unusual monitor layouts can behave differently.
