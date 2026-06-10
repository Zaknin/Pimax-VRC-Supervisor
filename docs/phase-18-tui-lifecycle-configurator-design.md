# Phase 18 TUI Lifecycle And Configurator Integration Design

Phase 18A is an audit/design phase only. It does not change runtime behavior, backend commands, Configurator behavior, SteamVR host behavior, Rust TUI behavior, packaging output, or `Q` semantics.

## Current Architecture Summary

- `PimaxVrcSupervisor.exe` is the C# safety-critical backend. It owns startup, Windows elevation-sensitive operations, SteamVR/VRChat/Pimax monitoring, cleanup, monitor layout, base-station power, OSC routing, managed app lifecycle, diagnostics, the classic console UI, and the loopback TCP command bridge.
- `PimaxVrcSupervisorTui.exe` is a separate Rust/Ratatui desktop terminal UI. It connects to the already-running supervisor on `127.0.0.1:37957`, reads `query-json`, and executes only the audited regular `TuiAction` set through `action-json`.
- `PimaxVrcSupervisorConfigurator.exe` is the WinForms editor/launcher for `supervisor.config.json`. It currently has Launch Supervisor and Launch SteamVR buttons, but no TUI launch button.
- `PimaxVrcSupervisorSteamVrHost.exe` is the SteamVR dashboard overlay host. It remains on the existing SteamVR overlay and legacy bridge workflow.
- Release packaging uses a flat folder layout that already includes the Rust TUI executable beside the C# executables.

## Current Startup Modes

- Manual console launch runs the supervisor normally, shows the console, runs first-run prompts when needed, starts the command bridge, waits for Pimax, optionally powers base stations, waits for VRChat, starts managed apps, and monitors the session.
- `--watch-vrchat-auto-launch` is the hidden scheduled-task watcher. It hides its console window, waits for a SteamVR session, then launches the supervisor workflow according to the scheduled task logic.
- `--steamvr-start` is the SteamVR manifest/helper path. The supervisor hides its console window, is treated as SteamVR-started, and exits/cleans up with SteamVR lifecycle conditions.
- `--apply-startup-integration` and `--install-auto-launch-task` are setup/helper modes for installing or repairing scheduled task and SteamVR manifest integration.
- `--emergency-base-station-cleanup` is a detached helper path for best-effort base-station power-down after console close.
- Future TUI-primary workflow should start with an already-running supervisor or a Configurator-launched supervisor, without changing `Q` semantics or cleanup behavior in the first implementation phase.

## Current Shutdown And Cleanup Paths

- Normal session cleanup is driven by watched shutdown process state, normally VRChat exit, with crash/relaunch grace behavior before cleanup.
- SteamVR-bound modes also observe SteamVR server process exit and run cleanup when SteamVR exits.
- Ctrl+C triggers `Console.CancelKeyPress`, cancels the shutdown token, and runs best-effort emergency cleanup.
- Console close/logoff/shutdown uses `SetConsoleCtrlHandler`; it runs emergency cleanup and can launch the detached base-station cleanup helper.
- Cleanup restores monitors, stops managed apps and Lovense workflow apps, waits for SteamVR where appropriate, stops OSC routing, and powers down base stations.
- `force-stop-supervisor` exists as a legacy bridge command but is blocked from structured desktop TUI action flow because it bypasses cleanup.
- There is not yet a dedicated external graceful lifecycle command for "user intentionally asked supervisor to end the VR session."

## Current Bridge And Action Model

- The TCP bridge is line-oriented and accepts concurrent clients.
- Read-only structured clients use `query-json` for `status`, `commands`, and `log`.
- Structured actions use `action-json` with real JSON `confirmed=true`.
- The current structured TUI allowlist is exactly the six regular classic-console actions: `restart-core-apps`, `start-osc-goes-brrr`, `base-stations-on`, `base-stations-off`, `restart-osc-router`, and `reload-autostart-apps`.
- `force-stop-supervisor` remains blocked and not TUI-executable.
- No graceful shutdown command exists. It should not be conflated with `force-stop-supervisor`.

## Configurator Integration Options

- Low-risk launch-only option: add a `Launch Desktop TUI` button near the existing Launch Supervisor / Launch SteamVR buttons. It should locate `PimaxVrcSupervisorTui.exe` beside the Configurator executable or in the current release folder, start it normally, and not alter supervisor lifecycle or config schema.
- Medium-risk workflow option: add `Start supervisor hidden and open Desktop TUI`. This should be deferred until the supervisor hidden launch path is explicitly designed and tested.
- Config option ideas such as `Desktop console mode` with `Classic console`, `Modern console`, and `Hidden` remain future work. They require schema, Configurator, launch, and migration design.
- User-facing wording should keep the three surfaces distinct: classic console, Desktop TUI, and SteamVR overlay.

## Tray And Minimize Feasibility

- A terminal TUI cannot reliably become a Windows tray app by itself because terminal windows are owned by the terminal host and do not expose normal WinForms tray behavior.
- A C# Configurator/launcher wrapper could provide tray behavior and launch/monitor the supervisor/TUI, but that is a separate lifecycle host and raises shutdown semantics questions.
- A separate small tray host is feasible but should be designed as a new component with clear ownership and IPC boundaries.
- A future Rust GUI/tray companion is possible but would add a different UI stack and packaging surface.
- No-tray remains a valid near-term option: Desktop TUI is a terminal UI, and users close/reopen it without affecting the supervisor.

## Recommended Implementation Sequence

1. Phase 18B: add a Configurator `Launch Desktop TUI` button. It starts `PimaxVrcSupervisorTui.exe` only, adds no config schema, and does not start/stop the supervisor. Implemented as a release-local launch button with duplicate-process detection.
2. Phase 18C: design and implement a hidden-supervisor plus TUI launch workflow if the launch-only button is not enough.
3. Phase 18D: design and implement a backend graceful supervisor shutdown/lifecycle command. It must be distinct from `force-stop-supervisor`, cleanup-owned by the backend, and strongly confirmed.
4. Phase 18E: decide TUI close/`Q` semantics. Preferred safe default is `Q` closes only the TUI; any supervisor shutdown should be a separate explicit command.
5. Phase 18F: decide tray/minimize architecture after the lifecycle command and user-facing semantics are stable.

## Phase 18B Implementation Status

- The Configurator has a `Launch Desktop TUI` button near the existing launch controls.
- The button launches `PimaxVrcSupervisorTui.exe` from the Configurator executable folder only.
- Missing executable and already-running TUI cases show clear messages.
- The button does not start the supervisor, stop the supervisor, launch hidden supervisor mode, add tray behavior, change config schema, change bridge commands, or change TUI `Q` semantics.

## Risks And Safety Constraints

- Do not make terminal close or `Q` implicitly stop the supervisor until a graceful shutdown command and confirmation UX exist.
- Do not expose `force-stop-supervisor` through the TUI.
- Do not make the Configurator launch hidden supervisor workflows until elevation, duplicate-instance, first-run prompt, and cleanup behavior are designed.
- Do not break SteamVR overlay compatibility or legacy bridge commands.
- Keep backend cleanup, monitor restore, base-station power-down, OSC routing, and process lifecycle ownership in the C# supervisor.

## Proposed User-Facing Wording

- `Launch Desktop TUI`: opens the terminal dashboard. It does not start or stop the supervisor.
- `Launch Supervisor`: starts the classic supervisor console using the current config.
- `Launch SteamVR`: starts SteamVR normally through Steam.
- `Q Quit TUI`: closes only the Desktop TUI. The supervisor keeps running.
- Future wording for shutdown should be explicit, for example `Stop supervisor and run cleanup`, not `Quit`.

## Proposed Future Phase Breakdown

- Phase 18B: Configurator `Launch Desktop TUI` button.
- Phase 18C: hidden supervisor plus TUI launch workflow.
- Phase 18D: backend graceful shutdown command design and implementation.
- Phase 18E: TUI close/`Q` shutdown semantics.
- Phase 18F: tray/minimize architecture decision.
