# Terminal UI

Terminal UI is a keyboard and mouse dashboard for a running Supervisor.

Use it to:

- see Supervisor connection and session state
- run confirmed actions
- view recent logs
- close only the Terminal UI or choose an explicit Supervisor exit mode

## Basic Keys

| Key | Action |
|---|---|
| `0` | Help |
| `F5` | Refresh |
| `1`-`6` | Open action confirmation |
| `Enter` / `Space` | Confirm modal |
| `Esc` | Open exit options, or cancel an open modal |
| `Q` | Open exit options |

Mouse users can click action cards. Compact layouts only make the visible start badge clickable.

## Connected And Disconnected

When Terminal UI is connected, `Esc` or `Q` opens exit options. The default highlighted option is **Close TUI only**.

The choices are:

- **Close TUI only**: closes only `PimaxVrcSupervisorTui.exe`. Supervisor, Watcher, SteamVR, VRChat, XSOverlay, managed apps, base stations, and monitor topology are left unchanged.
- **Exit Supervisor - Keep Base Stations On**: stops Supervisor cleanly and restores secondary monitors only if Supervisor disabled them during the current session. Base-station power-off commands are suppressed.
- **Exit Supervisor - Turn Base Stations Off**: stops Supervisor cleanly, restores Supervisor-owned monitor changes, and runs the normal base-station shutdown path.
- **Cancel**: returns to the dashboard.

Pressing `Esc` while the exit options are already open cancels the dialog. It does not select an exit mode.

When Terminal UI is disconnected, **Close TUI only** exits only Terminal UI. The disconnected state is not treated as a preserve-base-stations Supervisor exit.

## Autostart Behavior

When Terminal Mode opens Terminal UI automatically, the Supervisor starts Terminal UI after the dashboard is ready. Terminal UI follows that paired Supervisor process and closes when it exits.

Manual Terminal UI launches stay open while disconnected until you exit.

When SteamVR exits from the normal SteamVR UI, the Supervisor treats the session as ending normally, runs cleanup, and exits. If SteamVR disappears in a way the Supervisor cannot reliably classify, the Supervisor uses the same safe cleanup path instead of showing a crash warning.

If Supervisor exits by an explicit Terminal UI Supervisor-exit choice while the scheduled Watcher is running, the Watcher skips automatic relaunch for the current SteamVR `vrserver` process identity. A later SteamVR session with a new PID/start-time identity can launch Supervisor normally.

SteamVR restart and crash recovery are deferred to Phase32B. Phase32A does not add SteamVR process adoption, log parsing, restart grace periods, or a new persistent diagnostics journal.

## Actions

Terminal UI can run the same normal session actions as the classic console:

- restart core face-tracking apps
- start OscGoesBrrr / Intiface workflow
- turn base stations on
- turn base stations off
- restart OSC Router
- reload Autostart apps

Actions are validated and confirmed. Force-stop behavior is not exposed in Terminal UI.
