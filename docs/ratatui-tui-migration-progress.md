# Ratatui / CLI UI Migration Progress

Repository: `Zaknin/Pimax-VRC-Supervisor`  
Active branch: `cli-ui2`  
Base/reference branch: `vrmanifest-gui-overhaul-ver2`

## Starting Point

- Branch confirmed: `cli-ui2`
- Starting commit: `931a725` (`Add Ratatui migration progress tracker`)
- Tags pointing at current commit: none
- Date/time of Phase 0 inspection: `2026-06-09T10:22:37.6156182+04:00`

## Goal

Prepare the existing C# Pimax VRC Supervisor application for a future separate Rust Ratatui TUI frontend.

## Target Architecture

- The C# supervisor remains the backend and owns the safety-critical workflow.
- A separate Rust Ratatui frontend can become a dashboard/controller after the backend is prepared.
- The frontend should communicate with the backend through a local IPC bridge, preferably Windows named pipes.
- The backend remains responsible for Windows, VR, SteamVR, VRChat, cleanup, monitor, OSC, and base-station logic.
- The old console interface remains available as the fallback UI throughout migration.

## Hard Rules

- Work only on `cli-ui2`.
- Do not switch branches.
- Do not modify tags.
- Do not rewrite history.
- Preserve current behavior unless a phase explicitly changes it.
- Preserve all existing CLI modes.
- Preserve Ctrl+C and console-close emergency cleanup.
- Do not remove the old console interface.
- Do not add Rust/Ratatui until backend is prepared.
- Work in small buildable phases.
- Update this file at the end of every phase.

## Current Repository Architecture

- `PimaxVrcSupervisor` is the main C# supervisor executable. It owns process lifecycle, device detection, monitor layout changes, OSC routing, base-station handling, emergency cleanup, startup integration, diagnostics, command handling, and the old console UI.
- `PimaxVrcSupervisor.ConfigEditor` is the WinForms configurator/GUI. It edits `supervisor.config.json`, validates executable paths and scheduled tasks, scans and manages base stations, configures startup modes, OSC routes, face tracking, OSCGoesBrrr/Intiface, and automation settings.
- `PimaxVrcSupervisor.SteamVrHost` is the SteamVR dashboard/overlay helper. It starts the elevated supervisor through the SteamVR start scheduled task, renders the OpenVR dashboard overlay, and communicates with the supervisor command bridge on loopback TCP port `37957`.
- Shared support files include `PimaxVrcSupervisor/BaseStationSupport.cs` for Bluetooth/base-station models and commands, and `PimaxVrcSupervisor/ScheduledTaskPathValidation.cs` for scheduled-task path validation shared by the supervisor, configurator, and SteamVR host.
- Configuration lives in `PimaxVrcSupervisor/supervisor.config.json`.
- Documentation lives under `docs/`, with additional release and packaging notes at the repository root.

## Important Files

- `PimaxVrcSupervisor/Program.cs`: main supervisor entrypoint and primary implementation file. It contains CLI parsing, startup modes, supervision loop, process/device monitoring, console UI, dashboard command bridge, cleanup, scheduled-task integration, SteamVR manifest integration, monitor layout handling, OSC routing, and config model.
- `PimaxVrcSupervisor/BaseStationSupport.cs`: shared base-station models, BLE discovery, GATT control, power-on/off commands, and power-down routines.
- `PimaxVrcSupervisor/ScheduledTaskPathValidation.cs`: shared validation for managed Windows scheduled tasks and release-folder executable paths.
- `PimaxVrcSupervisor/supervisor.config.json`: default configuration and documented config fields for managed apps, devices, OSC routing, startup integration, timing, diagnostics, monitors, and base stations.
- `PimaxVrcSupervisor.ConfigEditor/Program.cs`: WinForms configuration editor, validation UI, startup integration repair/apply flow, base-station tools, raw JSON editor, and launch helpers.
- `PimaxVrcSupervisor.SteamVrHost/Program.cs`: SteamVR/OpenVR dashboard overlay host, scheduled-task supervisor launcher, dashboard rendering, command buttons, and status/console polling.
- `README.md`, `RELEASE_NOTES.md`, `RELEASE_PACKAGING.md`, and `docs/`: user documentation, workflow references, troubleshooting, and release packaging notes.

## Current Supervisor Responsibilities

- Supervises the Pimax + VRChat workflow from the C# backend.
- Loads config, validates and selects managed executable paths, and runs initial setup prompts when configured.
- Waits for Pimax headset connection and watched shutdown processes such as VRChat.
- Monitors SteamVR server process names and can exit/cleanup with SteamVR depending on startup mode.
- Detects Pimax headset state from configured Windows device rules and PiService log reconnect events.
- Detects Vive mouth tracker state from configured device rules and optional Kernel-PnP reconnect detection.
- Starts, attaches to, restarts, minimizes, and stops Broken Eye and VRCFaceTracking.
- Starts, detects, repairs, and stops the optional OSCGoesBrrr/Intiface workflow.
- Runs an in-process OSC UDP router on loopback when enabled.
- Starts optional auto-launch apps after the core face-tracking startup routine.
- Saves and restores monitor layout when secondary monitors are disabled for a VR session.
- Powers SteamVR base stations on and off through BLE/GATT, with OpenVR tracking-reference confirmation when available.
- Handles Windows scheduled task startup, SteamVR startup manifest registration, and SteamVR dashboard helper integration.
- Exposes an existing loopback TCP dashboard command bridge on port `37957` for the SteamVR host.
- Owns cleanup and safety behavior, including Ctrl+C, console close/logoff/shutdown, emergency cleanup, detached base-station cleanup, and shutdown progress tracking.

## Current Startup / CLI Modes

Known command-line options in `PimaxVrcSupervisor/Program.cs` include:

- `--config <path>`: load a specific config file.
- `--emergency-base-station-cleanup <path>`: run detached emergency base-station cleanup from a config file.
- `--delay-seconds <seconds>`: delay emergency base-station cleanup.
- `--install-auto-launch-task`: install the elevated auto-launch scheduled task.
- `--apply-startup-integration`: apply the configured startup integration mode.
- `--show-result`: show a message-box result after startup integration.
- `--hide-startup-helper`: hide the helper console window during startup integration when no result dialog is requested.
- `--watch-vrchat-auto-launch`: run the hidden watcher that launches the supervisor when `vrserver` is running.
- `--skip-current-vrserver-session`: tell the watcher not to launch for the already-running SteamVR session.
- `--steamvr-start`: run supervisor mode launched from SteamVR startup integration and hide the console window.
- Diagnostics flags include `--diagnostics`, `--diagnostics-verbose`, and `--diagnostics-log-dir`.

Configured startup modes are:

- `Unspecified`: first setup should ask while initial setup questions are enabled.
- `None`: automatic startup is disabled.
- `ScheduledTask`: creates an elevated Windows scheduled task that starts the supervisor when `vrserver.exe` is running.
- `SteamVrManifest`: registers a SteamVR dashboard host manifest and uses a SteamVR start helper scheduled task to start the elevated supervisor.

## Current Console / UI Behavior

- The old console interface is still the primary local fallback UI.
- Console output is currently direct `Console.WriteLine` across the supervisor, with `Console.Error.WriteLine` used for some helper errors.
- `SupervisorConsoleLog` installs a tee writer around `Console.Out` and keeps a bounded 80-line recent console buffer with timestamps for dashboard display.
- Console hotkeys are read with `Console.KeyAvailable` and `Console.ReadKey(intercept: true)` when input is not redirected.
- Hotkeys are: `1` for Broken Eye + VRCFaceTracking routine, `2` for OSCGoesBrrr + Intiface routine, `3` for base stations on, `4` for base stations off, `5` for OSC router launch/restart, `6` for auto-start apps reload, and `F1` for console shortcut help.
- Some setup and preference prompts use themed WinForms message boxes/dialogs from the supervisor.
- The configurator is a separate WinForms GUI and should not be confused with the future Ratatui frontend.
- The SteamVR host is a separate dashboard overlay process that renders current status, recent console lines, and command buttons.

## Current Cleanup / Safety Behavior

- Ctrl+C uses `Console.CancelKeyPress`, cancels normal shutdown, and runs blocking emergency cleanup before cancelling the supervisor token.
- Console close/logoff/shutdown uses a Windows `SetConsoleCtrlHandler` registration in `ConsoleCloseHandler`.
- Console-close cleanup is best-effort because Windows gives console handlers limited time.
- Forced manual reload can skip console-close emergency cleanup using a marker/flag path.
- Emergency cleanup restores monitor layout, stops managed apps, stops Lovense apps, and attempts base-station power-down with a timeout.
- A detached emergency base-station cleanup helper can be launched with `--emergency-base-station-cleanup` and `--delay-seconds`.
- Normal cleanup can wait for SteamVR server exit before base-station power-down and monitor restoration, depending on mode.
- Cleanup is guarded by locks and flags to avoid running the full cleanup routine multiple times.

## Ratatui Architecture Decision

Ratatui should be a separate Rust frontend instead of being embedded directly into the C# app. The existing C# supervisor is already the Windows-specific, safety-critical backend and should continue to own VR, SteamVR, VRChat, cleanup, monitor layout, OSC routing, managed-app lifecycle, base-station power, scheduled-task, and SteamVR manifest behavior.

Embedding Ratatui directly into the C# supervisor would mix frontend rendering concerns with cleanup-sensitive Windows automation and would not fit Ratatui's Rust-native model. The safer migration path is to first make the C# backend emit structured events, expose structured status, and accept structured commands while preserving the existing console. A future Rust Ratatui app can then connect over local IPC, preferably Windows named pipes, and act as a dashboard/controller without taking over backend ownership.

The current SteamVR host already uses a local loopback TCP command bridge for dashboard commands. That bridge is useful current context, but the future Ratatui IPC should be designed intentionally rather than coupled to the dashboard helper's existing implementation.

## Recommended Migration Roadmap

### Phase 0 - Repository inspection and architecture documentation
Status: Completed

### Phase 1 - Console output and event abstraction
Status: Not started

### Phase 2 - Structured status snapshot model
Status: Not started

### Phase 3 - Backend command model
Status: Not started

### Phase 4 - Local IPC server
Status: Not started

### Phase 5 - Minimal Rust Ratatui frontend
Status: Not started

### Phase 6 - Full dashboard screens
Status: Not started

### Phase 7 - Packaging and integration
Status: Not started

## Known Risks

- `PimaxVrcSupervisor/Program.cs` is a large monolithic file, so small UI/event refactors can accidentally touch unrelated lifecycle or cleanup logic.
- Direct `Console.WriteLine` calls are spread through supervisor responsibilities, including startup, monitoring, cleanup, base-station, OSC router, scheduled-task, and manifest paths.
- Cleanup behavior is safety-sensitive and must preserve Ctrl+C, console-close/logoff/shutdown, emergency cleanup, detached base-station cleanup, and monitor restoration.
- Process lifecycle handling is complex: VRChat normal exit, VRChat crash/relaunch grace, SteamVR exit, supervisor duplicate prevention, watcher startup, and dashboard-triggered commands all interact.
- SteamVR/VRChat startup and shutdown behavior must not regress when console output is abstracted.
- Monitor layout restore must remain reliable even when shutdown happens through error, Ctrl+C, console close, or SteamVR/VRChat exits.
- Base-station power behavior includes BLE timing, retries, unsupported state reads, OpenVR confirmation, and detached emergency cleanup; future UI work must not alter command ordering or shutdown safety.
- The existing SteamVR dashboard bridge uses loopback TCP port `37957`; future Ratatui IPC should account for it without breaking the SteamVR host.
- Root `dotnet build` does not work because there is no project or solution file at the repository root; future phases may need explicit project builds or a solution file if build verification policy changes.

## Phase Log

### Phase 0 - Repository inspection and architecture documentation

Status: Completed

Summary:

- Confirmed the branch is `cli-ui2`.
- Inspected repository structure, project files, main entrypoints, configuration, and documentation layout.
- Documented the current C# backend responsibilities and the target separate Rust Ratatui frontend architecture.
- Confirmed the old console interface and cleanup behavior must remain during migration.

Files changed:

- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `dotnet build`

Build/test result:

- Failed immediately with `MSB1003`: the repository root does not contain a project or solution file.

Known issues:

- Main supervisor behavior is concentrated in a large `Program.cs`.
- Console output is spread through many backend responsibilities.
- Cleanup, monitor restore, base-station power, SteamVR/VRChat lifecycle, and startup integration are safety-sensitive.
- Existing SteamVR dashboard bridge uses loopback TCP and should not be broken while preparing future IPC.
- Root build ambiguity should be resolved or future prompts should run explicit project builds.

## Next Codex Prompt

You are working on the `cli-ui2` branch of `Zaknin/Pimax-VRC-Supervisor`.

Phase 1 - Console output and event abstraction.

Rules:

- Work only on `cli-ui2`.
- Do not switch branches.
- Do not modify tags.
- Do not rewrite history.
- Preserve current behavior.
- Preserve all existing CLI modes.
- Preserve Ctrl+C and console-close emergency cleanup.
- Do not remove the old console interface.
- Do not add IPC yet.
- Do not add Rust or Ratatui yet.
- Keep changes small and buildable.
- Update `docs/ratatui-tui-migration-progress.md` at the end of the phase.

Tasks:

1. Read `docs/ratatui-tui-migration-progress.md`.
2. Inspect the current console output implementation, especially `SupervisorConsoleLog` and direct `Console.WriteLine` usage in `PimaxVrcSupervisor/Program.cs`.
3. Add a small event/log abstraction for supervisor output, using names such as `SupervisorEvent` and `ISupervisorEventSink` or equivalent.
4. `SupervisorEvent` should include timestamp, severity, category, source, and message fields.
5. Add a console sink that preserves the current console output behavior.
6. Add or evolve a bounded in-memory event/log buffer. If replacing or extending `SupervisorConsoleLog`, preserve the current recent-console-lines behavior used by the SteamVR dashboard.
7. Start with only small safe replacements of direct console output. Do not attempt to replace every `Console.WriteLine` in one phase.
8. Do not change startup behavior, process lifecycle behavior, cleanup behavior, dashboard commands, scheduled tasks, monitor handling, base-station handling, OSC routing, or config semantics.
9. Run `dotnet build`. If the root build still fails because no solution/project exists at the root, document that result and, if safe, run explicit project builds for the existing `.csproj` files.
10. Summarize files changed, build/test result, remaining risks, and the next phase.
