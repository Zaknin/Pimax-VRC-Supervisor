# Phase 18 TUI Lifecycle And Configurator Integration Design

Phase 18A was an audit/design phase only. Phase 18B added a TUI-only Configurator launcher. Phase 18C makes the Desktop TUI the primary operator lifecycle surface by adding a confirmed Ctrl+C-equivalent shutdown request.

## Current Architecture Summary

- `PimaxVrcSupervisor.exe` is the C# safety-critical backend. It owns startup, Windows elevation-sensitive operations, SteamVR/VRChat/Pimax monitoring, cleanup, monitor layout, base-station power, OSC routing, managed app lifecycle, diagnostics, the classic console UI, and the loopback TCP command bridge.
- `PimaxVrcSupervisorTui.exe` is a separate Rust/Ratatui desktop terminal UI. It connects to the already-running supervisor on `127.0.0.1:37957`, reads `query-json`, and executes only the audited regular `TuiAction` set through `action-json`.
- `PimaxVrcSupervisorConfigurator.exe` is the WinForms editor/launcher for `supervisor.config.json`. It has Launch Supervisor, Launch SteamVR, Launch Desktop TUI, and Launch Supervisor + Desktop TUI buttons.
- `PimaxVrcSupervisorSteamVrHost.exe` is the SteamVR dashboard overlay host. It remains on the existing SteamVR overlay and legacy bridge workflow.
- Release packaging uses a flat folder layout that already includes the Rust TUI executable beside the C# executables.

## Current Startup Modes

- Manual console launch runs the supervisor normally, shows the console, runs first-run prompts when needed, starts the command bridge, waits for Pimax, optionally powers base stations, waits for VRChat, starts managed apps, and monitors the session.
- `--watch-vrchat-auto-launch` is the hidden scheduled-task watcher. It hides its console window, waits for a SteamVR session, then launches the supervisor workflow according to the scheduled task logic.
- `--steamvr-start` is the SteamVR manifest/helper path. The supervisor hides its console window, is treated as SteamVR-started, and exits/cleans up with SteamVR lifecycle conditions.
- `--apply-startup-integration` and `--install-auto-launch-task` are setup/helper modes for installing or repairing scheduled task and SteamVR manifest integration.
- `--emergency-base-station-cleanup` is a detached helper path for best-effort base-station power-down after console close.
- Phase 18C TUI-primary workflow starts with an already-running supervisor or a Configurator-launched supervisor. Dashboard `Q` now requires confirmation and requests supervisor cleanup through `lifecycle-json`.

## Current Shutdown And Cleanup Paths

- Normal session cleanup is driven by watched shutdown process state, normally VRChat exit, with crash/relaunch grace behavior before cleanup.
- SteamVR-bound modes also observe SteamVR server process exit and run cleanup when SteamVR exits.
- Ctrl+C triggers `Console.CancelKeyPress`, runs best-effort emergency cleanup, cancels the shutdown token, and waits for supervisor exit.
- Console close/logoff/shutdown uses `SetConsoleCtrlHandler`; it runs emergency cleanup and can launch the detached base-station cleanup helper.
- Cleanup restores monitors, stops managed apps and Lovense workflow apps, waits for SteamVR where appropriate, stops OSC routing, and powers down base stations.
- `force-stop-supervisor` exists as a legacy bridge command but is blocked from structured desktop TUI action flow because it bypasses cleanup.
- Phase 18C adds a dedicated external graceful lifecycle command for "user intentionally asked supervisor to end the VR session."

## Current Bridge And Action Model

- The TCP bridge is line-oriented and accepts concurrent clients.
- Read-only structured clients use `query-json` for `status`, `commands`, and `log`.
- Structured actions use `action-json` with real JSON `confirmed=true`.
- The current structured TUI allowlist is exactly the six regular classic-console actions: `restart-core-apps`, `start-osc-goes-brrr`, `base-stations-on`, `base-stations-off`, `restart-osc-router`, and `reload-autostart-apps`.
- `force-stop-supervisor` remains blocked and not TUI-executable.
- `lifecycle-json {"action":"request-graceful-shutdown","source":"Desktop TUI"}` requests Ctrl+C-equivalent cleanup. It is not a regular action card and is not conflated with `force-stop-supervisor`.

## Configurator Integration Options

- Low-risk launch-only option: add a `Launch Desktop TUI` button near the existing Launch Supervisor / Launch SteamVR buttons. It should locate `PimaxVrcSupervisorTui.exe` beside the Configurator executable or in the current release folder, start it normally, and not alter supervisor lifecycle or config schema.
- Primary workflow option: add `Launch Supervisor + Desktop TUI`. Phase 18C uses the existing normal supervisor launch path plus the TUI launcher. Hidden/non-interactive supervisor launch is deferred because `--steamvr-start` hides the console but changes SteamVR lifecycle semantics.
- Config option ideas such as `Desktop console mode` with `Classic console`, `Modern console`, and `Hidden` remain future work. They require schema, Configurator, launch, and migration design.
- User-facing wording should keep the three surfaces distinct: classic console, Desktop TUI, and SteamVR overlay.

## Tray And Minimize Feasibility

- A terminal TUI cannot reliably become a Windows tray app by itself because terminal windows are owned by the terminal host and do not expose normal WinForms tray behavior.
- A C# Configurator/launcher wrapper could provide tray behavior and launch/monitor the supervisor/TUI, but that is a separate lifecycle host and raises shutdown semantics questions.
- A separate small tray host is feasible but should be designed as a new component with clear ownership and IPC boundaries.
- A future Rust GUI/tray companion is possible but would add a different UI stack and packaging surface.
- No-tray remains the current option. Desktop TUI is a terminal UI; Phase 18C does not add tray/minimize behavior.

## Recommended Implementation Sequence

1. Phase 18B: add a Configurator `Launch Desktop TUI` button. It starts `PimaxVrcSupervisorTui.exe` only, adds no config schema, and does not start/stop the supervisor. Implemented as a release-local launch button with duplicate-process detection.
2. Phase 18C: add `Launch Supervisor + Desktop TUI`, `lifecycle-json`, and confirmed TUI `Q` shutdown semantics.
3. Phase 18D: harden/manual-test the primary TUI lifecycle workflow and decide whether a general hidden supervisor mode is safe.
4. Phase 18E: design terminal close/X-close behavior if needed; do not assume it is equivalent to confirmed `Q`.
5. Phase 18F: decide tray/minimize architecture after lifecycle semantics are stable.

## Phase 18B Implementation Status

- The Configurator has a `Launch Desktop TUI` button near the existing launch controls.
- The button launches `PimaxVrcSupervisorTui.exe` from the Configurator executable folder only.
- Missing executable and already-running TUI cases show clear messages.
- The button does not start the supervisor, stop the supervisor, launch hidden supervisor mode, add tray behavior, change config schema, change bridge commands, or change TUI `Q` semantics.

## Phase 18C Implementation Status

- The Configurator has a `Launch Supervisor + Desktop TUI` button. It reuses the existing supervisor validation, unsaved-change, config path, and UAC launch behavior, skips launching a duplicate supervisor, and then starts the Desktop TUI.
- The supervisor bridge has a dedicated `lifecycle-json` command for `request-graceful-shutdown`. It returns structured lifecycle results and starts the same cleanup sequence used by Ctrl+C.
- The Desktop TUI dashboard `Q` opens a shutdown confirmation. `Enter` or `Space` sends the lifecycle request; `Esc` cancels.
- After an accepted or already-in-progress shutdown response, the TUI waits for backend disconnect or a 60 second timeout, then exits.
- Hidden supervisor launch is deferred because the only current hidden supervisor path, `--steamvr-start`, changes supervisor lifecycle behavior.
- No tray/minimize behavior, config schema, auto-start setting, terminal X-close guarantee, generic command executor, or `force-stop-supervisor` exposure was added.

## Phase 18D Hardening Status

- The primary TUI shutdown flow remains the same confirmed `Q` workflow; no close-TUI-only dashboard path was restored.
- The TCP command bridge now preserves the final lifecycle response write even if graceful-shutdown cancellation begins quickly.
- The TUI surfaces lifecycle rejection messages directly and keeps the post-60-second timeout warning visible briefly before exiting.
- The Configurator combined launch reports supervisor/TUI duplicate and launch-result states more precisely.
- The local ignored `release/PimaxVrcSupervisor-v1.3.0-test` folder should be refreshed after successful source builds so runtime tests use current C#, Configurator, SteamVR host, and TUI binaries.

## Phase 18E Hidden Primary-TUI Startup Status

- The supervisor has a dedicated `--desktop-tui-start` flag for Configurator-launched primary Desktop TUI sessions.
- `--desktop-tui-start` hides the console early through the existing `ConsoleWindow.HideIfPresent()` helper, but keeps `steamVrStart=false`.
- The normal Configurator `Launch Supervisor` button does not pass this flag and remains a visible classic-console launch.
- `--steamvr-start` remains SteamVR-specific because it both hides the console and changes supervisor/SteamVR lifecycle behavior.
- `--watch-vrchat-auto-launch` remains the scheduled hidden watcher path and is not reused for the primary Desktop TUI workflow.
- No tray/minimize behavior, terminal X-close guarantee, config schema change, wrapper executable, or SteamVR host change was added.

## Risks And Safety Constraints

- Do not make terminal close/X-close implicitly stop the supervisor until a separate design exists.
- Do not expose `force-stop-supervisor` through the TUI.
- Do not make the Configurator launch hidden supervisor workflows until elevation, duplicate-instance, first-run prompt, and cleanup behavior are designed.
- Do not break SteamVR overlay compatibility or legacy bridge commands.
- Keep backend cleanup, monitor restore, base-station power-down, OSC routing, and process lifecycle ownership in the C# supervisor.

## Proposed User-Facing Wording

- `Launch Desktop TUI`: opens the terminal dashboard. It does not start or stop the supervisor.
- `Launch Supervisor + Desktop TUI`: starts the supervisor with hidden Desktop TUI startup mode if needed, then opens the terminal dashboard.
- `Launch Supervisor`: starts the classic supervisor console using the current config.
- `Launch SteamVR`: starts SteamVR normally through Steam.
- `Q`: opens `Stop supervisor and exit TUI?` confirmation. Confirming runs Ctrl+C-equivalent supervisor cleanup.

## Proposed Future Phase Breakdown

- Phase 18B: Configurator `Launch Desktop TUI` button.
- Phase 18C: primary TUI lifecycle with Ctrl+C-equivalent shutdown.
- Phase 18D: lifecycle runtime hardening and hidden supervisor mode review.
- Phase 18E: dedicated hidden primary-TUI supervisor startup mode.
- Phase 18F: terminal close/X-close behavior design.
- Phase 18G: tray/minimize architecture decision.
