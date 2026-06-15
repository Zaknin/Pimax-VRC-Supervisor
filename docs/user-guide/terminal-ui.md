# Terminal UI

Terminal UI is a keyboard and mouse dashboard for a running Supervisor.

Use it to:

- see Supervisor connection and session state
- run confirmed actions
- view recent logs
- shut down the Supervisor with cleanup

## Basic Keys

| Key | Action |
|---|---|
| `0` | Help |
| `F5` | Refresh |
| `1`-`6` | Open action confirmation |
| `Enter` / `Space` | Confirm modal |
| `Esc` | Cancel / back |
| `Q` | Shutdown flow when connected, exit only when disconnected |

Mouse users can click action cards. Compact layouts only make the visible start badge clickable.

## Connected And Disconnected

When Terminal UI is connected, `Q` opens a shutdown confirmation. Confirming runs Supervisor cleanup, closes managed apps as configured, and exits after the Supervisor disconnects.

When Terminal UI is disconnected, `Q` exits only Terminal UI. It does not start or stop the Supervisor.

## Autostart Behavior

When Terminal Mode opens Terminal UI automatically, the Supervisor starts Terminal UI after the dashboard is ready. Terminal UI follows that paired Supervisor process and closes when it exits.

Manual Terminal UI launches stay open while disconnected until you exit.

When SteamVR exits from the normal SteamVR UI, the Supervisor treats the session as ending normally, runs cleanup, and exits. If SteamVR disappears in a way the Supervisor cannot reliably classify, the Supervisor uses the same safe cleanup path instead of showing a crash warning.

## Actions

Terminal UI can run the same normal session actions as the classic console:

- restart core face-tracking apps
- start OscGoesBrrr / Intiface workflow
- turn base stations on
- turn base stations off
- restart OSC Router
- reload Autostart apps

Actions are validated and confirmed. Force-stop behavior is not exposed in Terminal UI.
