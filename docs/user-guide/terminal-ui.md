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

In **Terminal UI only**, the Supervisor starts Terminal UI after the dashboard is ready. Terminal UI follows that paired Supervisor process and closes when it exits.

In **Terminal UI + SteamVR Overlay**, Terminal UI is restart-persistent. Closing it—including the Windows close button—does not request Supervisor shutdown while SteamVR is running. It stays open across a Supervisor-requested action-7 runtime replacement and is not duplicated. Every automatically launched TUI is associated with the exact Supervisor owner PID. A later normal SteamVR exit signals final cleanup and closes Terminal UI; exact owner-process monitoring also closes it if the Supervisor disappears unexpectedly.

Manual Terminal UI launches stay open while disconnected until you exit.

In every SteamVR-associated mode, a normal SteamVR exit runs the existing cleanup and exit behavior. Only a Supervisor-requested action-7 replacement preserves the combined Supervisor, Terminal UI, base-station state, and monitor state while SteamVR and its overlay host are replaced.

`7 SteamVR` is dynamic and uses Supervisor-reported SteamVR control state. When SteamVR is stopped, it shows `Start SteamVR` and requires confirmation before sending `start-steamvr`; VRChat is not launched. When SteamVR is running, it shows `Restart SteamVR` and requires confirmation before sending `restart-vr-session`; VRChat is resumed only if it was already running when you confirmed the action. Both dialogs show clickable **Confirm** and **Cancel** buttons, default keyboard focus to **Cancel**, and support Tab, Left, Right, Enter, Space, Esc, and mouse clicks. Actions `1` through `6` remain immediate.

The request identity is created only when **Confirm** is activated. The modal closes and latches that submission, so double-click, repeated Enter, or an Enter/mouse overlap cannot submit the same confirmation twice. Reconnecting the Terminal UI restores current operation or result display without resubmitting the command. A later deliberate restart uses a new confirmation and a new request identity.

Accepted means the operation is running, not complete. Completion, warning, failure, rejection, or timeout appears later in the Activity, Last Result, logs, and shared action state without opening an Action Result popup.

Restart first launches the installed SteamVR runtime's graceful shutdown request. Successful launch is enough to begin watching the old `vrserver`; Terminal UI does not wait for `vrstartup.exe` to acknowledge or exit. When the captured PID/start-time identity disappears, Supervisor starts SteamVR automatically through the normal Steam launch path unless a different runtime is already present, then requires the replacement to become ready. It never force-kills SteamVR. An invocation failure or bounded old-runtime timeout leaves the controls available. If SteamVR closes but cannot be started again, action `7` becomes Start SteamVR for manual recovery. Base stations remain on and the current monitor topology is preserved during the bounded restart.

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
