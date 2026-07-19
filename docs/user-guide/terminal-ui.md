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
| `1`-`6` | Run the action immediately |
| `7` | Open SteamVR action confirmation |
| `Tab` / `Left` / `Right` | Move focus between modal buttons |
| `Enter` / `Space` | Activate the focused modal button |
| `Esc` | Open exit options, or cancel an open modal |
| `Q` | Open exit options |

Mouse users can click action cards and visible modal buttons. Compact layouts only make the visible start badge clickable.

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

When SteamVR exits from the normal SteamVR UI, the Supervisor restores Supervisor-owned monitors, runs cleanup, and exits. A short SteamVR restart/failure recovery window keeps stations powered while the existing Supervisor waits for and adopts a replacement runtime; it does not restart SteamVR or depend on VRChat.

`7 SteamVR` is dynamic and uses Supervisor-reported SteamVR control state. When SteamVR is stopped, it shows `Start SteamVR` and requires confirmation before sending `start-steamvr`; VRChat is not launched. When SteamVR is running, it shows `Restart SteamVR` and requires confirmation before sending `restart-vr-session`; VRChat is resumed only if it was already running when you confirmed the action. Both dialogs show clickable **Confirm** and **Cancel** buttons, default keyboard focus to **Cancel**, and support Tab, Left, Right, Enter, Space, Esc, and mouse clicks. Actions `1` through `6` remain immediate.

Accepted means the operation is running, not complete. Completion, warning, failure, rejection, or timeout appears later in the Activity, Last Result, logs, and shared action state without opening an Action Result popup.

Restart first asks the installed SteamVR runtime to shut down gracefully. It waits for the old `vrserver` process identity to disappear, starts SteamVR through the normal Steam launch path, and requires a genuinely new identity to become ready. It never force-kills SteamVR. A refusal or timeout leaves the controls available and reports a bounded failure. Base stations remain on and the current monitor topology is preserved during the bounded restart.

If Supervisor exits by an explicit Terminal UI Supervisor-exit choice while the scheduled Watcher is running, the Watcher skips automatic relaunch for the current SteamVR `vrserver` process identity. A later SteamVR session with a new PID/start-time identity can launch Supervisor normally.

Restart recovery uses bounded current-session SteamVR log hints plus replacement-process observation. It restores monitors immediately, has no multi-minute grace period, and does not add a persistent diagnostics journal.

## Actions

Terminal UI can run the same normal session actions as the classic console:

- restart core face-tracking apps
- start OscGoesBrrr / Intiface workflow
- turn base stations on
- turn base stations off
- restart OSC Router
- reload Autostart apps
- restart the VR session

Actions are validated by Supervisor. Actions `1` through `6` run immediately; action `7` is confirmed. Force-stop behavior is not exposed in Terminal UI.
