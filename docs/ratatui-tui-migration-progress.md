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
- Near-term desktop TUI work should extend the existing local loopback TCP command bridge additively with structured read-only JSON surfaces. A future Windows named-pipe transport may be considered later after the backend protocol is stable.
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
- Ratatui is a separate desktop terminal/operator TUI, not a replacement for the SteamVR overlay.
- Do not change SteamVR overlay rendering, VR console/status/log rendering, OpenVR/D3D texture rendering, or dashboard button layout unless a phase explicitly requires it.
- Preserve the existing loopback TCP command bridge and extend it only additively until a later IPC transport phase is approved.

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

Embedding Ratatui directly into the C# supervisor would mix frontend rendering concerns with cleanup-sensitive Windows automation and would not fit Ratatui's Rust-native model. The safer migration path is to first make the C# backend expose structured read-only status, command metadata, and recent-log surfaces while preserving the existing console and SteamVR dashboard behavior.

The current SteamVR host already uses a local loopback TCP command bridge for dashboard commands. In the near term, this bridge should be extended additively with structured JSON commands such as status-json, commands-json, log-json, and query-json. Existing one-line text commands must remain compatible. A future Windows named-pipe transport can be considered later after the protocol is stable and the desktop Ratatui client requirements are clearer.

## Recommended Migration Roadmap

Phase 0 - Repository inspection and architecture documentation

Status: Completed

Phase 1 - Structured status snapshot and v1.3.0-test publish baseline

Status: Completed

Phase 2 - Command metadata/capabilities and structured DTO baseline

Status: Completed

Phase 3 - Structured recent log DTOs and read-only log-json surface

Status: Completed

Phase 4 - Read-only JSON request envelope for safe read-only commands

Status: Completed

Phase 5 - Minimal Rust Ratatui desktop frontend

Status: Completed and Rust build verified

Phase 6 - Improve read-only desktop Ratatui TUI UX

Status: Completed

Phase 7 - Packaging, integration, and optional future IPC transport review

Status: Completed

Phase 8 - Safe desktop TUI action execution design

Status: Completed

Phase 9 - Backend-only structured action-json allowlist

Status: Completed

Phase 10 - Display action metadata in read-only Ratatui TUI

Status: Completed

Phase 11 - Confirmed TUI action for restart-osc-router

Status: Completed

Phase 12 - TUI action UX hardening and input overlay cleanup

Status: Completed

Phase 13 - Unified layout-independent shortcut UX and help alignment

Status: Completed

Phase 14 - TUI help shortcut polish

Status: Completed

Phase 14B - TUI help debounce and alias text cleanup

Status: Completed

Phase 14C - Remove sticky TUI help debounce

Status: Completed

Phase 15 - Classic console action parity in TUI

Status: Completed

Phase 15C - TUI action parity runtime UX fixes

Status: Completed

Phase 16 - TUI background actions and Autostart duplicate protection

Status: Completed

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
- `status-json` was verified by code inspection and successful build only; runtime testing would require launching the elevated supervisor workflow in the local VR/SteamVR environment.
- `commands-json` was verified by code inspection and successful build only; runtime testing would require launching the elevated supervisor workflow in the local VR/SteamVR environment.
- `log-json` was verified by code inspection and successful build only; runtime testing would require launching the elevated supervisor workflow in the local VR/SteamVR environment.
- `query-json` was verified by code inspection and successful build only; runtime testing would require launching the elevated supervisor workflow in the local VR/SteamVR environment.

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

### Phase 1 - Structured status snapshot and v1.3.0-test publish baseline

Status: Completed

Summary:

- Added a compact structured `SupervisorStatusSnapshot` beside the existing string `BuildSupervisorStatus()` output.
- Added the `status-json` dashboard command, serialized as compact one-line camelCase JSON for the line-oriented TCP command bridge.
- Preserved the old text `status` command key names/order and the `log` command response used by the SteamVR dashboard.
- Updated all project version metadata to `1.3.0-test` with numeric assembly/file versions `1.3.0.0`.
- Published the local test output folder `release/PimaxVrcSupervisor-v1.3.0-test`.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor/PimaxVrcSupervisor.csproj`
- `PimaxVrcSupervisor.ConfigEditor/PimaxVrcSupervisor.ConfigEditor.csproj`
- `PimaxVrcSupervisor.SteamVrHost/PimaxVrcSupervisor.SteamVrHost.csproj`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- All three explicit Release builds succeeded with 0 warnings and 0 errors.
- `status-json` was not runtime-tested because launching the supervisor can trigger the local elevated VR/SteamVR/Pimax workflow. It was verified by code inspection and successful build.

Publish commands run:

- `New-Item -ItemType Directory -Force .\release\PimaxVrcSupervisor-v1.3.0-test | Out-Null`
- `dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`

Publish result:

- Publish succeeded for all three projects.
- Release folder: `release/PimaxVrcSupervisor-v1.3.0-test`
- Verified release output includes `PimaxVrcSupervisor.exe`, `PimaxVrcSupervisorConfigurator.exe`, `PimaxVrcSupervisorSteamVrHost.exe`, `supervisor.config.json`, runtime/dependency files, and `Assets/vr-overlay-icon.png`.
- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.

New status surface:

- New command: `status-json`
- JSON is compact one-line output for the current line-oriented TCP bridge.
- JSON fields are camelCase: `timestamp`, `appVersion`, `mode`, `steamVr`, `lifecycle`, `coreApps`, `baseStations`, `oscRouter`, `oscGoesBrrr`, `shutdownProgress`, `shutdownProgressElapsed`, `shutdownBlockedBy`, `shutdownBlockedElapsed`, and `blockingProcesses`.
- `timestamp` uses `DateTimeOffset.UtcNow`.
- `appVersion` uses `AppVersion.Current`.

Compatibility:

- `status` remains text-based and parser-compatible with the existing SteamVR host. It preserves `Mode`, `SteamVR`, `Lifecycle`, `CoreApps`, `BaseStations`, `OscRouter`, and `OscGoesBrrr` key names and order.
- Optional text suffixes remain compatible for `ShutdownProgress`, `ShutdownBlockedBy`, and `BlockingProcesses`.
- `log` still returns the recent console-line JSON string array used by the SteamVR dashboard.
- Existing dashboard action commands keep their names and behavior.

Version metadata changes:

- `Version`: `1.3.0-test`
- `InformationalVersion`: `1.3.0-test`
- `AssemblyVersion`: `1.3.0.0`
- `FileVersion`: `1.3.0.0`

Known issues:

- The initial snapshot intentionally mirrors the current string status and does not yet model nested process, app, base-station, or OSC route details.
- The existing command bridge remains the one-line string protocol over loopback TCP.
- `status-json` runtime behavior should be exercised in a safe supervisor session before relying on it for an external frontend.
- Release output is generated locally and ignored; it must not be committed unless release policy changes.

### Phase 2 - Command metadata/capabilities and structured DTO baseline

Status: Completed

Summary:

- Added compact command metadata DTOs for future Ratatui/JSON clients.
- Added the read-only `commands-json` dashboard command, serialized as compact one-line camelCase JSON for the existing line-oriented TCP bridge.
- Preserved all existing command execution behavior and response compatibility.
- Intentionally deferred generic structured JSON action execution.
- Re-published the local test output folder `release/PimaxVrcSupervisor-v1.3.0-test`.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `docs/ratatui-tui-migration-progress.md`

Models added:

- `SupervisorCommandDefinition`
- `SupervisorCommandCapabilitiesSnapshot`
- `SupervisorCommandResult`

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- All three explicit Release builds succeeded with 0 warnings and 0 errors.
- `commands-json` was not runtime-tested because launching the supervisor can trigger the local elevated VR/SteamVR/Pimax workflow. It was verified by code inspection and successful build.

Publish commands run:

- `New-Item -ItemType Directory -Force .\release\PimaxVrcSupervisor-v1.3.0-test | Out-Null`
- `dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`

Publish result:

- Publish succeeded for all three projects.
- Release folder: `release/PimaxVrcSupervisor-v1.3.0-test`
- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.

New command capability surface:

- New command: `commands-json`
- JSON is compact one-line output for the current line-oriented TCP bridge.
- Capability metadata covers: `status`, `status-json`, `log`, `commands-json`, `restart-core-apps`, `start-osc-goes-brrr`, `base-stations-on`, `base-stations-off`, `restart-osc-router`, and `force-stop-supervisor`.
- Metadata includes command category, output kind, risk flags, confirmation recommendation, availability, legacy command name, and notes.
- `available=true` means the command is accepted by the bridge, not that the underlying configured subsystem is enabled.

Compatibility:

- `status` remains text-based and parser-compatible with the existing SteamVR host.
- `status-json` remains compact one-line JSON.
- `log` still returns the recent console-line JSON string array used by the SteamVR dashboard.
- Existing dashboard action commands keep their names, response strings, timing diagnostics, and behavior.
- `PimaxVrcSupervisor.SteamVrHost` was not changed.

Deferred:

- Generic JSON action execution, such as a broad `command-json` executor, was intentionally not implemented.
- Structured action execution is deferred until command metadata, danger levels, and confirmation rules are stable.

Permanent UI boundary:

- The SteamVR overlay stays as-is.
- Ratatui is a separate desktop terminal/operator TUI.
- Do not replace VR console/status/log rendering with Ratatui.
- Do not change the VR overlay renderer, texture rendering, dashboard button layout, OpenVR/D3D rendering path, or console/log rendering inside VR for nearby backend-prep phases.

Known issues:

- `commands-json` runtime behavior should be exercised in a safe supervisor session before relying on it for an external frontend.
- The command metadata is descriptive and does not yet enforce confirmation.
- The current bridge remains a one-line string protocol over loopback TCP.
- Release output is generated locally and ignored; it must not be committed unless release policy changes.

### Phase 3 - Structured recent log DTOs and read-only log-json surface

Status: Completed

Summary:

- Added compact structured recent-log DTOs for future Ratatui/JSON clients.
- Added the read-only `log-json` dashboard command, serialized as compact one-line camelCase JSON for the existing line-oriented TCP bridge.
- Wrapped only the existing `SupervisorConsoleLog.GetRecentLines(...)` data source.
- Preserved the existing `log` command used by the SteamVR dashboard.
- Re-published the local test output folder `release/PimaxVrcSupervisor-v1.3.0-test`.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `docs/ratatui-tui-migration-progress.md`

Models added:

- `SupervisorLogLine`
- `SupervisorRecentLogSnapshot`

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- All three explicit Release builds succeeded with 0 warnings and 0 errors.
- `log-json` was not runtime-tested because launching the supervisor can trigger the local elevated VR/SteamVR/Pimax workflow. It was verified by code inspection and successful build.

Publish commands run:

- `New-Item -ItemType Directory -Force .\release\PimaxVrcSupervisor-v1.3.0-test | Out-Null`
- `dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`

Publish result:

- Publish succeeded for all three projects.
- Release folder: `release/PimaxVrcSupervisor-v1.3.0-test`
- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.

New recent-log surface:

- New command: `log-json`
- JSON is compact one-line output for the current line-oriented TCP bridge.
- `log-json` uses the same default recent-line count as `log`: 14 lines.
- Per-line timestamps are best-effort local same-day values parsed from the existing `HH:mm:ss` prefix. If parsing fails, the per-line `timestamp` is `null`.
- Each line preserves `raw`, uses source `console`, and uses level `info`.
- `commands-json` now includes `log-json` metadata.

Compatibility:

- `log` remains unchanged and still returns the recent console-line JSON string array used by the SteamVR dashboard.
- `status`, `status-json`, `commands-json`, and all existing dashboard action commands keep their names and behavior.
- `PimaxVrcSupervisor.SteamVrHost` was not changed.
- Existing `SupervisorConsoleLog`, `SupervisorDiagnosticsSession`, debug logging, and diagnostic logging were not replaced.

Deferred:

- Full diagnostic log-file access was intentionally not added.
- Filesystem log browsing was intentionally not added.
- Streaming events were intentionally deferred.
- `events-json` was intentionally not implemented in this phase.
- Generic JSON action execution remains deferred.

Known issues:

- `log-json` runtime behavior should be exercised in a safe supervisor session before relying on it for an external frontend.
- Per-line timestamps are best-effort because the existing console buffer stores only time of day, not date or offset.
- The structured recent-log surface is derived only from the bounded in-memory console buffer, not diagnostics files.
- Release output is generated locally and ignored; it must not be committed unless release policy changes.

### Phase 4 - Read-only JSON request envelope for safe read-only commands

Status: Completed

Summary:

- Added a compact read-only JSON request envelope for future desktop Ratatui/JSON clients.
- Added the `query-json` command for read-only resources only.
- Preserved all existing simple commands and the SteamVR dashboard protocol.
- Kept action command execution, generic `command-json`, and confirmation handling deferred.
- Re-published the local test output folder `release/PimaxVrcSupervisor-v1.3.0-test`.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `docs/ratatui-tui-migration-progress.md`

Models added or updated:

- `SupervisorReadOnlyJsonRequest`
- `SupervisorCommandResult` now includes optional `requestId`.

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- All three explicit Release builds succeeded with 0 warnings and 0 errors.
- `query-json` was not runtime-tested because launching the supervisor can trigger the local elevated VR/SteamVR/Pimax workflow. It was verified by code inspection and successful build.

Publish commands run:

- `New-Item -ItemType Directory -Force .\release\PimaxVrcSupervisor-v1.3.0-test | Out-Null`
- `dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`

Publish result:

- Publish succeeded for all three projects.
- Release folder: `release/PimaxVrcSupervisor-v1.3.0-test`
- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.

New read-only query surface:

- New command: `query-json`
- Syntax: `query-json {"requestId":"optional-id","resource":"status"}`
- Supported resources: `status`, `commands`, and `log`.
- `status` maps to the same data as `status-json`.
- `commands` maps to the same data as `commands-json`.
- `log` maps to the same data as `log-json` and supports `maxLines` clamped from 1 to 80, defaulting to 14.
- `requestId` is echoed on success and failure when the JSON request can be parsed far enough to read it. Malformed JSON that cannot be safely read returns `requestId=null`.

Compatibility:

- `status` remains legacy parser-compatible text.
- `status-json`, `commands-json`, and `log-json` remain raw compact snapshot JSON commands.
- `log` remains the recent console-line JSON string array used by the SteamVR dashboard.
- Existing dashboard action commands keep their names, response strings, timing diagnostics, and behavior.
- `PimaxVrcSupervisor.SteamVrHost` was not changed.
- The SteamVR overlay and VR dashboard rendering were not changed.

Deferred:

- `query-json` intentionally does not support action commands.
- Generic `command-json` action execution remains deferred.
- Confirmation handling remains deferred.
- Dangerous/disruptive command behavior was not changed.

Future Configurator naming note:

- Future Configurator desktop interface setting should likely be named `Desktop console mode`.
- User-facing options: `Classic console`, `Modern console`, `Hidden`.
- `Classic console`: current visible console UI and hotkeys.
- `Modern console`: future Ratatui desktop terminal UI.
- `Hidden`: backend/no-console mode for advanced/startup use.
- This setting must not replace or disable the SteamVR overlay dashboard.

Known issues:

- `query-json` runtime behavior should be exercised in a safe supervisor session before relying on it for an external frontend.
- The query envelope is read-only only; action-command support still needs a confirmation/danger model.
- The current bridge remains a one-line string protocol over loopback TCP.
- Release output is generated locally and ignored; it must not be committed unless release policy changes.

### Phase 5 - Minimal read-only Rust Ratatui desktop frontend

Status: Completed and Rust build verified

Summary:

- Added a new separate Rust crate for a read-only desktop Ratatui frontend.
- Crate path: `PimaxVrcSupervisor.Tui/`
- TUI binary name: `PimaxVrcSupervisorTui`
- The TUI uses the existing loopback TCP bridge at `127.0.0.1:37957`.
- The TUI sends only read-only `query-json` requests for `status`, `commands`, and `log`.
- The TUI never sends action commands and does not stop, restart, clean up, or mutate supervisor state.
- The old C# console UI and SteamVR overlay/dashboard behavior remain unchanged.

Files changed:

- `.gitignore`
- `PimaxVrcSupervisor.Tui/Cargo.toml`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/bridge.rs`
- `PimaxVrcSupervisor.Tui/src/models.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `docs/ratatui-tui-migration-progress.md`

Rust crate baseline:

- Package name: `pimax-vrc-supervisor-tui`
- Binary: `PimaxVrcSupervisorTui`
- Edition remains `2024`.
- Ratatui dependency remains `ratatui = "0.30.1"`.
- Crossterm dependency remains `crossterm = "0.29.0"`.
- No Ratatui dependency or feature adjustment was made because Cargo is not available locally to perform a build-resolution check.
- `PimaxVrcSupervisor.Tui/target/` was added to `.gitignore`.

Read-only bridge behavior:

- `query-json {"resource":"status"}`
- `query-json {"resource":"commands"}`
- `query-json {"resource":"log","maxLines":14}`
- Connects with short read/write/connect timeouts.
- Sends one command line and reads one response line.
- Backend unavailable and disconnected states render without panicking.

TUI behavior:

- Renders a title/status bar, supervisor status panel, bridge command list, recent logs, and footer.
- `r` refreshes read-only data.
- `q` and `Esc` quit the TUI only.
- No action-command execution, confirmation handling, IPC transport changes, Rust-to-C# embedding, or SteamVR host changes were added.

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `rustc --version`

Build/test result:

- All three explicit C# Release builds succeeded with 0 warnings and 0 errors.
- Rust build was blocked because `cargo` is not on PATH.
- Rust compiler verification was blocked because `rustc` is not on PATH.
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release` was not run because Cargo is not available locally.
- The TUI bridge was not runtime-tested because the Rust binary could not be built locally and launching the supervisor can trigger the elevated VR/SteamVR/Pimax workflow.

Publish commands run:

- `New-Item -ItemType Directory -Force .\release\PimaxVrcSupervisor-v1.3.0-test | Out-Null`
- `dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`

Publish/copy result:

- C# publish succeeded for all three projects.
- Release folder: `release/PimaxVrcSupervisor-v1.3.0-test`
- Release folder inspection found `PimaxVrcSupervisor.exe`, `PimaxVrcSupervisorConfigurator.exe`, `PimaxVrcSupervisorSteamVrHost.exe`, `supervisor.config.json`, and `Assets/vr-overlay-icon.png`.
- `PimaxVrcSupervisorTui.exe` was not copied because the Rust release build could not be produced without Cargo.
- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.

Known issues:

- Rust build verification should be rerun after installing or exposing a Rust toolchain on PATH.
- Ratatui `0.30.1` and Rust edition `2024` compatibility were not locally verified because Cargo is unavailable.
- The TUI is intentionally read-only and currently depends on the existing loopback TCP bridge.
- Runtime bridge behavior should be tested in a safe supervisor session before relying on the TUI operationally.
- Release output is generated locally and ignored; it must not be committed unless release policy changes.

### Phase 5 build/toolchain verification

Status: Completed

Summary:

- Verified and built the Phase 5 Rust TUI before starting Phase 6.
- Rustup was installed through `winget install --id Rustlang.Rustup -e`.
- The current PowerShell PATH was refreshed with `$env:Path += ";$env:USERPROFILE\.cargo\bin"`.
- The active Rust toolchain was set explicitly to `stable-x86_64-pc-windows-msvc`.
- No C# backend behavior, SteamVR host behavior, action commands, tags, branches, or history were changed.

Toolchain status:

- `rustup toolchain install stable-x86_64-pc-windows-msvc` completed with the toolchain already available after installation.
- `rustup default stable-x86_64-pc-windows-msvc` set the default toolchain.
- `rustup show` reported default host `x86_64-pc-windows-msvc` and active default toolchain `stable-x86_64-pc-windows-msvc`.
- `rustc -Vv` reported `rustc 1.96.0 (ac68faa20 2026-05-25)`, host `x86_64-pc-windows-msvc`, LLVM `22.1.2`.
- `cargo -V` reported `cargo 1.96.0 (30a34c682 2026-05-25)`.
- `where.exe cl` did not find `cl.exe` in the normal shell, which is expected outside a Visual Studio developer environment.
- `vswhere` reported Visual Studio 2022 Build Tools with C++ tools at `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools`.

Build commands run:

- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`

Build result:

- Debug Cargo build succeeded.
- Release Cargo build succeeded.
- The normal shell build succeeded, so `vcvars64.bat` fallback was not needed.
- Cargo accepted Rust edition `2024`; no edition downgrade was needed.
- Ratatui `0.30.1` and Crossterm `0.29.0` built successfully.
- No source or dependency compatibility fixes were needed.
- `PimaxVrcSupervisor.Tui/Cargo.lock` was generated and should remain tracked because the TUI is an application binary crate.

Release copy result:

- Copied `PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe` into `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --short PimaxVrcSupervisor.Tui` reported `?? PimaxVrcSupervisor.Tui/Cargo.lock`.

Runtime test:

- The optional runtime TUI check was not run because it requires an attached interactive terminal to verify the disconnected screen and quit with `q` or `Esc`.
- The supervisor was not started and no elevated VR/SteamVR/Pimax workflow was triggered.

### Phase 6 - Improve read-only desktop Ratatui TUI UX

Status: Completed

Summary:

- Improved the Rust desktop TUI for read-only monitoring and troubleshooting.
- Kept the TUI on the existing one-line loopback TCP bridge and existing `query-json` resources.
- Preserved C# backend behavior, old console behavior, SteamVR overlay behavior, and all action-command behavior.

Files changed:

- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/bridge.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `docs/ratatui-tui-migration-progress.md`

UX improvements implemented:

- Added explicit Rust TUI constants for backend host, backend port, connect timeout, read/write timeout, refresh interval, and max log request size.
- Added bounded synchronous periodic refresh every 3 seconds.
- Manual `r` refresh remains available and retries immediately.
- Refresh attempts do not overlap because the app remains single-threaded and refreshes are synchronous with short timeouts.
- Added last successful refresh age and last error age/message display.
- Added a backend/error panel with `Backend unavailable at 127.0.0.1:37957` when disconnected.
- Increased read-only log request size to 80 lines using `query-json {"resource":"log","maxLines":80}`.
- Added scrollable recent logs with clamped scroll offset after refresh and scroll operations.
- Improved command capability display with name, category, output kind, `[danger]`, and `[confirm]` markers.
- Added a help popup.
- Added a compact fallback view for terminals smaller than 72x20.

Keybindings:

- `r`: manual read-only refresh.
- `q`: quit.
- `Esc`: close help if open; otherwise quit.
- `h` or `?`: toggle help.
- `Up` / `Down`: scroll logs by one line.
- `PageUp` / `PageDown`: scroll logs by one page.
- `Home` / `End`: jump log scroll to top or bottom.

Read-only behavior:

- The TUI still only sends `query-json` requests for `status`, `commands`, and `log`.
- No action commands were added or executed from the TUI.
- No `command-json`, confirmation handling, named-pipe IPC, streaming events, filesystem log browsing, or backend protocol changes were added.
- `PimaxVrcSupervisor.SteamVrHost` was not changed.
- The old console and SteamVR overlay remain unchanged.

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust formatting completed successfully.
- Rust debug build succeeded.
- Rust release build succeeded.
- All three explicit C# Release builds succeeded with 0 warnings and 0 errors.

Release copy result:

- Copied `PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe` into `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --short PimaxVrcSupervisor.Tui` reported only tracked Rust source changes during implementation; generated `target/` output remained ignored.

Runtime test:

- The optional runtime TUI check was not run because the current shell is not an attached interactive terminal suitable for verifying alternate-screen rendering, key handling, and clean terminal restoration.
- The supervisor was not started and no elevated VR/SteamVR/Pimax workflow was triggered.

Known risks:

- Runtime disconnected-state behavior should still be checked manually in an interactive terminal before relying on the TUI operationally.
- Periodic refresh is synchronous and bounded; a slow backend can still pause the UI briefly until the bridge timeout returns.
- The TUI remains a read-only TCP bridge client; action execution and confirmation design remain deferred.
- Release output is generated locally and ignored; it must not be committed unless release policy changes.

### Phase 7 - TUI launch/integration documentation and packaging planning

Status: Completed

Summary:

- Documented how the read-only Rust/Ratatui desktop TUI is built, launched, packaged, and positioned in the project.
- Kept this phase documentation-only.
- No C# backend behavior, Rust TUI behavior, SteamVR host behavior, old console behavior, protocol, config, cleanup, lifecycle, monitor, base-station, OSC, scheduled-task, or manifest behavior changed.

Files changed:

- `README.md`
- `RELEASE_PACKAGING.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`
- `mkdocs.yml`

Documentation updates:

- Added a `Read-only desktop TUI` section to `README.md`.
- Added `cli-ui2` / `1.3.0-test` Rust TUI build and local release-copy instructions to `README.md`.
- Added `docs/ratatui-tui.md` with purpose, architecture, launch requirements, keybindings, build commands, limitations, and future direction.
- Added `docs/ratatui-tui.md` to the existing MkDocs navigation under `Workflows` as `Desktop TUI`.
- Documented that the TUI uses the existing loopback TCP bridge and `query-json` for `status`, `commands`, and `log`.
- Documented that the TUI is read-only, does not start or stop the supervisor, does not execute action commands, does not replace the SteamVR overlay, and does not replace the classic console.
- Documented the manual runtime check result from migration context: the TUI can connect to a running supervisor at `127.0.0.1:37957` and display status, command capabilities, and logs.

Packaging notes:

- Added `PimaxVrcSupervisorTui.exe` to the flat release package layout in `RELEASE_PACKAGING.md`.
- Documented the Rust release build command and copy step for the local `release\PimaxVrcSupervisor-v1.3.0-test` folder.
- Documented that `PimaxVrcSupervisor.Tui\target\` and `release\` are generated output and must not be committed.
- Documented that `PimaxVrcSupervisor.Tui\Cargo.lock` should stay committed because the TUI is an application binary crate.

Intentionally skipped:

- `RELEASE_NOTES.md` was left unchanged because it is specific to v1.2.3 and has no suitable unreleased/development section.
- No launcher script was added because there is no existing `scripts/` folder and launch commands are documented instead.
- No `Desktop console mode` Configurator setting was implemented.

Future Configurator note:

- Future setting name: `Desktop console mode`.
- Options: `Classic console`, `Modern console`, `Hidden`.
- `Classic console`: current visible console UI and hotkeys.
- `Modern console`: future Ratatui desktop terminal UI.
- `Hidden`: backend/no-console mode for advanced/startup use.
- This setting must not replace or disable the SteamVR overlay dashboard.

Build/test commands run:

- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust debug build succeeded.
- Rust release build succeeded.
- All three explicit C# Release builds succeeded with 0 warnings and 0 errors.
- No runtime test was required for this documentation-only phase.
- The supervisor was not started.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported ignored Rust build output under `PimaxVrcSupervisor.Tui/target/`.

Known risks:

- Documentation now describes the TUI as a `cli-ui2` / `1.3.0-test` feature while the main README still describes the current public release as v1.2.3.
- Future action execution must still be designed before implementation, including confirmations and dangerous/disruptive command gating.

### Phase 8 - Safe desktop TUI action execution design

Status: Completed

Summary:

- Created a design-only safety plan for future desktop TUI action execution.
- No action execution, `action-json`, confirmation handling, backend behavior, Rust TUI behavior, SteamVR host behavior, old console behavior, protocol, config, cleanup, lifecycle, monitor, base-station, OSC, scheduled-task, manifest, launcher, release-note, or packaging behavior changed.

Files changed:

- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`
- `mkdocs.yml`

Design document:

- Added `docs/ratatui-action-execution-design.md`.
- Documented the current read-only TUI state and existing legacy action-command bridge.
- Documented that the SteamVR host remains on the legacy protocol and is not forced to change.
- Documented that `commands-json` `available=true` means accepted by the current backend bridge, not safe or executable from the desktop TUI.
- Proposed separate future action metadata fields: `actionSupported`, `actionSafetyCategory`, `tuiExecutable`, `requiresConfirmation`, and `blockedReason`.
- Defined action safety categories: `ReadOnly`, `LowRisk`, `Disruptive`, `Dangerous`, and `Blocked`.
- Classified `force-stop-supervisor` as `Blocked` for desktop TUI execution even though it exists as a legacy bridge command.

Initial classification:

- `ReadOnly`: `status`, `status-json`, `commands-json`, `log`, `log-json`, `query-json`.
- `LowRisk`: `restart-osc-router`.
- `Disruptive`: `start-osc-goes-brrr`, `restart-core-apps`, `base-stations-on`, `base-stations-off`.
- `Blocked`: `force-stop-supervisor`.

Recommended future implementation order:

- First candidate: backend-only structured `action-json` support for `restart-osc-router`.
- Possible second candidate: `start-osc-goes-brrr`.
- Deferred: `restart-core-apps`, `base-stations-on`, `base-stations-off`.
- Blocked by default: `force-stop-supervisor`.

Documentation updates:

- Added a short `Action Safety Design` section to `docs/ratatui-tui.md`.
- Added `docs/ratatui-action-execution-design.md` to the existing MkDocs navigation near `Desktop TUI` as `TUI Action Safety Design`.
- `RELEASE_NOTES.md` was left unchanged because there is no suitable development/unreleased section.

Build/test commands run:

- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust debug build succeeded.
- Rust release build succeeded.
- All three explicit C# Release builds succeeded with 0 warnings and 0 errors.
- No runtime test was required for this design-only phase.
- The supervisor was not started.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported ignored Rust build output under `PimaxVrcSupervisor.Tui/target/`.

Known risks:

- Future implementation must keep backend validation authoritative; TUI confirmation cannot be the only safety layer.
- Legacy bridge availability and future desktop TUI executability must remain separate concepts.
- `force-stop-supervisor` remains especially sensitive because it bypasses cleanup routines.

### Phase 9 - Backend-only structured `action-json` allowlist

Status: Completed

Summary:

- Added backend-only structured `action-json` support for a tiny allowlist.
- The only allowed structured action is `restart-osc-router`.
- The Rust TUI remains read-only; no TUI action buttons, keybindings, selection, or confirmation modal were added.
- Legacy string commands remain compatible.
- The SteamVR host was not changed and still uses the legacy command protocol.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Backend behavior:

- Added one-line `action-json` command verb.
- Added `SupervisorActionJsonRequest` with `requestId`, `command`, and nullable boolean `confirmed`.
- Reused compact camelCase `SupervisorCommandResult` responses with `resultType="action"`.
- Preserved raw JSON payload after the `action-json` verb; only the leading verb is split.
- Matched action command names case-insensitively and returned canonical lowercase command names in structured responses.
- Required `confirmed=true` as a real JSON boolean for `restart-osc-router`; missing, `false`, `null`, and string `"true"` do not authorize execution.
- Shared the existing OSC router restart behavior through `RestartOscRouterCommandAsync(...)`, preserving the legacy response text `OSC router restart requested.`.
- Preserved dispatcher diagnostics/timing wrapping without adding duplicate diagnostics around the helper.

Allowlist and rejection behavior:

- Allowed: `restart-osc-router` with JSON boolean `confirmed=true`.
- Rejected read-only commands through `action-json`: `status`, `status-json`, `commands-json`, `log`, `log-json`, and `query-json`.
- Rejected deferred actions through `action-json`: `restart-core-apps`, `start-osc-goes-brrr`, `base-stations-on`, and `base-stations-off`.
- Explicitly blocked: `force-stop-supervisor`, because it hard-stops without cleanup routines and is not executable from the structured desktop TUI action flow.
- Unknown commands return structured failure.
- Malformed, missing, or non-object JSON returns structured failure and does not throw unhandled TCP exceptions to the client.
- `requestId` is echoed when it can be safely parsed as a string; otherwise it is `null`.

Command metadata:

- Added `action-json` to `commands-json` metadata.
- Added future action metadata fields additively: `actionSupported`, `actionSafetyCategory`, `tuiExecutable`, and `blockedReason`.
- Kept `available=true` meaning bridge availability only; it does not mean safe or executable from the desktop TUI.
- `restart-osc-router`: `actionSupported=true`, `actionSafetyCategory="LowRisk"`, `tuiExecutable=false`, `requiresConfirmation=true`.
- `force-stop-supervisor`: `actionSupported=false`, `actionSafetyCategory="Blocked"`, `tuiExecutable=false`, with a cleanup-bypass blocked reason.
- Deferred actions are classified with design-aligned safety categories and blocked reasons.

Documentation updates:

- Updated `docs/ratatui-action-execution-design.md` with Phase 9 implementation status.
- Updated `docs/ratatui-tui.md` to clarify that backend structured action support has started but the desktop TUI remains read-only.
- `RELEASE_NOTES.md` was not updated because there is no suitable development/unreleased section.
- Packaging behavior was not changed.

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`

Build/test result:

- Main supervisor Release build succeeded with 0 warnings and 0 errors.
- ConfigEditor Release build succeeded with 0 warnings and 0 errors.
- SteamVrHost Release build succeeded with 0 warnings and 0 errors.
- Rust debug build succeeded.
- Rust release build succeeded.

Runtime testing:

- Runtime bridge testing was not performed because this phase should not start the supervisor just for testing.
- Successful `restart-osc-router` was not runtime-tested because it should only be exercised in a known-safe active session.
- Verification was by code inspection plus successful C# and Rust builds.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported ignored Rust build output under `PimaxVrcSupervisor.Tui/target/`.

Known risks:

- `action-json` is backend-only; future TUI UX must still avoid accidental execution and must use explicit confirmation.
- Only `restart-osc-router` is allowlisted; expanding the allowlist needs separate review.
- `force-stop-supervisor` remains blocked from structured desktop TUI action flow despite legacy bridge availability.

### Phase 10 - Display action metadata in read-only Ratatui TUI

Status: Completed

Summary:

- Updated the Rust desktop TUI command capability display to include Phase 9 action metadata.
- Kept the TUI strictly read-only.
- No backend behavior, SteamVR host behavior, old console behavior, packaging behavior, config, cleanup, lifecycle, monitor, base-station, OSC, scheduled-task, manifest, or release-note behavior changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/models.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

TUI metadata display:

- Replaced ad hoc command-field extraction with a tolerant serde `CommandSummary` model using camelCase field mapping.
- Added display support for `actionSupported`, `actionSafetyCategory`, `tuiExecutable`, and `blockedReason`.
- Missing or null action metadata falls back safely: `actionSupported=false`, `actionSafetyCategory="-"`, `tuiExecutable=false`, and `blockedReason=""`.
- Command rows now show name, category, output kind, action safety category, and markers such as `[danger]`, `[confirm]`, `[backend-action]`, `[tui-disabled]`, and `[blocked]`.
- Blocked or deferred reasons are shown as a second detail line when present.
- Footer/help text now states that action metadata is for planning only, no action commands are executed, and backend `action-json` is not called by the TUI.

Read-only bridge verification:

- `PimaxVrcSupervisor.Tui/src/bridge.rs` was inspected with `rg`.
- The TUI still does not send `action-json`.
- The TUI still does not send legacy action commands such as `restart-osc-router`, `force-stop-supervisor`, `restart-core-apps`, `start-osc-goes-brrr`, `base-stations-on`, or `base-stations-off`.
- The only bridge command send path remains `query-json {request_json}`.
- Query helpers remain limited to `status`, `commands`, and `log` with max log request size 80.

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust formatting completed successfully.
- Rust debug build succeeded.
- Rust release build succeeded.
- Main supervisor Release build succeeded with 0 warnings and 0 errors.
- ConfigEditor Release build succeeded with 0 warnings and 0 errors.
- SteamVrHost Release build succeeded with 0 warnings and 0 errors.

Runtime testing:

- Runtime bridge testing was not performed because this phase should not start the supervisor just for testing.
- Verification was by code inspection plus successful C# and Rust builds.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported ignored Rust build output under `PimaxVrcSupervisor.Tui/target/`.

Known risks:

- The TUI display depends on Phase 9 action metadata being present in backend `commands-json`; older supervisors will show safe defaults.
- Future TUI action execution still needs a reviewed confirmation UX and backend-authoritative gating before any action keybindings are added.

### Phase 10B - Read-only action metadata display semantics cleanup

Status: Completed

Summary:

- Corrected a runtime UX issue where read-only commands could appear with `[blocked]` and a blocked reason when backend metadata included a default `blockedReason`.
- Treated `actionSafetyCategory="ReadOnly"` as authoritative for read-only command display.
- Kept this as a TUI display-only change; backend action metadata, the Phase 9 `action-json` allowlist, legacy commands, SteamVR host behavior, and old console behavior were not changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `docs/ratatui-tui-migration-progress.md`

Display behavior:

- `ReadOnly` commands now show `[read-only]`.
- `ReadOnly` commands no longer show `[blocked]`.
- `ReadOnly` commands no longer show `blockedReason` detail lines.
- `[blocked]` remains reserved for `actionSafetyCategory="Blocked"` commands such as `force-stop-supervisor`.
- Backend-supported actions such as `restart-osc-router` still show `[backend-action]` and `[tui-disabled]` while remaining non-executable from the TUI.

Read-only bridge status:

- The TUI remains read-only and still does not call `action-json`.
- The TUI still does not send legacy action commands.

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust formatting completed successfully.
- Rust debug build succeeded.
- Rust release build succeeded.
- Main supervisor Release build succeeded with 0 warnings and 0 errors.
- ConfigEditor Release build succeeded with 0 warnings and 0 errors.
- SteamVrHost Release build succeeded with 0 warnings and 0 errors.

Bridge inspection:

- `PimaxVrcSupervisor.Tui/src/bridge.rs` still sends only `query-json {request_json}`.
- Bridge helpers remain limited to `status`, `commands`, and `log`.
- No `action-json` or legacy action command strings were found in `bridge.rs`.

Runtime validation:

- Ran the latest Phase 10B TUI against the supervisor backend without launching SteamVR or VRChat.
- TUI connected successfully to `127.0.0.1:37957`.
- Confirmed read-only commands now display `[read-only]` instead of `[blocked]`.
- Confirmed backend action metadata is visible for `action-json`.
- Confirmed deferred action metadata/reasons are visible for unsupported actions.
- Confirmed footer states action metadata only and no `action-json` calls.
- No action commands were executed.

### Phase 11 - Confirmed TUI action for `restart-osc-router`

Status: Completed

Summary:

- Enabled the first controlled desktop TUI action execution path for exactly one command: `restart-osc-router`.
- Kept action execution confirmation-gated: pressing `o` only opens confirmation, and only `y` inside the modal sends the action request.
- Kept the backend `action-json` allowlist unchanged; it still supports only `restart-osc-router`.
- SteamVR host behavior, old console behavior, legacy command behavior, base-station handling, core-app restart, OSCGoesBrrr startup, `force-stop-supervisor`, cleanup, lifecycle, monitor, scheduled-task, manifest, config, and packaging behavior were not changed.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/bridge.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/models.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Backend metadata:

- `restart-osc-router` now reports `actionSupported=true`, `actionSafetyCategory="LowRisk"`, `tuiExecutable=true`, and `requiresConfirmation=true`.
- `restart-osc-router` no longer reports a blocked reason.
- `force-stop-supervisor` remains blocked.
- Base-station, core-app restart, and OSCGoesBrrr actions remain deferred and not TUI-executable.

TUI behavior:

- Added a narrow `execute_restart_osc_router()` bridge helper.
- The helper sends only `action-json {"command":"restart-osc-router","confirmed":true}`.
- No generic action executor was added.
- Added confirmation state for `restart-osc-router`.
- Added `o` to open the confirmation modal only when backend command metadata permits TUI execution.
- Added modal handling: `y` confirms and executes, while `n`, `Esc`, and modal `q` cancel without execution.
- Added action result/error display in the backend/status area.
- After a successful action response, the TUI refreshes status, command metadata, and logs once.

Bridge inspection:

- `PimaxVrcSupervisor.Tui/src/bridge.rs` was inspected with `rg`.
- `action-json` appears only inside `execute_restart_osc_router()`.
- `restart-osc-router` appears only in that narrow structured helper payload.
- No generic action/command executor was added.
- No legacy action command strings were found for `force-stop-supervisor`, `restart-core-apps`, `start-osc-goes-brrr`, `base-stations-on`, or `base-stations-off`.
- Existing read-only `query-json {request_json}` behavior remains for status, commands, and log.

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust formatting completed successfully.
- Rust debug build succeeded.
- Rust release build succeeded.
- Main supervisor Release build succeeded with 0 warnings and 0 errors.
- ConfigEditor Release build succeeded with 0 warnings and 0 errors.
- SteamVrHost Release build succeeded with 0 warnings and 0 errors.

Publish/copy result:

- Published updated `PimaxVrcSupervisor` output to `release\PimaxVrcSupervisor-v1.3.0-test`.
- Published updated `PimaxVrcSupervisor.ConfigEditor` output to `release\PimaxVrcSupervisor-v1.3.0-test`.
- Published updated `PimaxVrcSupervisor.SteamVrHost` output to `release\PimaxVrcSupervisor-v1.3.0-test`.
- Copied updated `PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe` into `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- Release-folder runtime testing, if performed later, should use these updated binaries.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported ignored Rust build output under `PimaxVrcSupervisor.Tui/target/`.

Runtime testing:

- Runtime action testing was not performed because no explicitly safe running supervisor session was provided for this phase.
- Verification was by code inspection, bridge string inspection, successful builds, and publish/copy verification.

Known risks:

- The first action UX should be exercised manually in a known-safe active session before adding more actions.
- Action result display is intentionally simple; Phase 12 should harden failure display and action history.
- No additional actions should be added until `restart-osc-router` is proven safe.


Runtime validation:

- Ran the updated Phase 11 release binaries.
- Started the supervisor backend without launching SteamVR or VRChat.
- TUI connected successfully to `127.0.0.1:37957`.
- Pressed `o` to open the restart OSC router confirmation modal.
- Confirmed with `y`.
- TUI sent the confirmed action through backend `action-json`.
- Backend executed the OSC router restart/start path.
- TUI closed the confirmation modal after execution.
- TUI displayed action result text: confirmed OSC restart via `action-json`.
- Status refreshed and showed `OSC router: running`.
- Supervisor remained running.
- No legacy action command was sent from the TUI.
- No base-station, core-app, OscGoesBrrr, or force-stop action was exposed.

### Phase 12 - TUI action UX hardening and input overlay cleanup

Status: Completed

Summary:

- Hardened Rust TUI input and action UX after the Phase 11 runtime validation.
- Fixed the help overlay key-repeat/toggle instability by processing only key press events and ignoring repeat/release events.
- Made overlay input priority explicit: confirmation modal first, help overlay second, normal dashboard last.
- Added duplicate action protection and clearer last-action status display.
- Kept `restart-osc-router` as the only executable TUI action.
- No backend allowlist, SteamVR host behavior, classic console behavior, cleanup/lifecycle behavior, config semantics, base-station action, core-app action, OSCGoesBrrr action, or `force-stop-supervisor` exposure changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Input behavior:

- `main.rs` now handles only `KeyEventKind::Press`.
- `KeyEventKind::Repeat` and `KeyEventKind::Release` are ignored, so holding `h` or `?` should no longer rapidly toggle help.
- Confirmation modal input owns the keyboard while visible: `y` confirms, `n`/`Esc`/`q` cancel, and help/dashboard keys are ignored.
- Help overlay input owns the keyboard while visible: `h`/`?`/`Esc`/`q` close help, and a second `q` from the normal dashboard quits.
- Normal dashboard input remains: `o` opens restart OSC router confirmation, `r` refreshes, log scroll keys scroll logs, `h`/`?` toggle help, and `q` quits.

Action behavior:

- Added `action_in_progress`, `last_action_started_at`, and `last_action_completed_at` state.
- Duplicate action attempts while an action is in progress are rejected with `Action already in progress.`.
- Cancellations are recorded as `Action cancelled.`.
- Latest action status now includes command, outcome, relative time, and message in the backend/status area.
- No persistent action history was added in Phase 12 to keep the layout compact.

Bridge/backend status:

- `PimaxVrcSupervisor.Tui/src/bridge.rs` remains limited to read-only `query-json` helpers plus the narrow `execute_restart_osc_router()` helper.
- `action-json` appears only inside `execute_restart_osc_router()`.
- No generic action executor was added.
- No legacy action command strings were added for `force-stop-supervisor`, `restart-core-apps`, `start-osc-goes-brrr`, `base-stations-on`, or `base-stations-off`.
- `PimaxVrcSupervisor/Program.cs` was not changed in Phase 12.

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust formatting completed successfully.
- Rust debug build succeeded.
- Rust release build succeeded.
- Main supervisor Release build succeeded with 0 warnings and 0 errors.
- ConfigEditor Release build succeeded with 0 warnings and 0 errors.
- SteamVrHost Release build succeeded with 0 warnings and 0 errors.

Release copy result:

- Copied updated `PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe` into `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- C# publish was not rerun because no C# files changed in Phase 12.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported ignored Rust build output under `PimaxVrcSupervisor.Tui/target/`.

Runtime testing:

- Runtime testing was not performed in this phase because no explicitly safe running supervisor session was provided.
- Verification was by code inspection, bridge string inspection, successful builds, and release-folder TUI copy.

Known risks:

- Help-repeat and overlay-priority behavior should be validated manually in an interactive terminal against a safe supervisor session.
- Duplicate action protection is implemented around the current synchronous action call; future async/background action execution would need another review.
- No additional actions should be added until the OSC router restart UX is manually hardened.

### Phase 13 - Unified layout-independent shortcut UX and help alignment

Status: Completed

Summary:

- Moved the Rust desktop TUI toward layout-independent primary shortcuts.
- Kept `restart-osc-router` as the only executable TUI action.
- Kept execution confirmation-gated through the existing narrow backend `action-json` path.
- Added simple Russian-layout aliases for common physical shortcut keys where terminal character input provides them.
- Confirmed classic console behavior already uses `1`-`6` plus `F1`; behavior was left unchanged.
- Added only safe explanatory classic console and Configurator help text about modern TUI primary keys.
- No backend action allowlist, SteamVR host behavior, SteamVR overlay rendering, cleanup/lifecycle/monitor/base-station/scheduled-task/manifest behavior, config semantics, or executable action set changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `README.md`
- `RELEASE_PACKAGING.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

TUI shortcut behavior:

- Added a `Shortcut` normalization layer in `main.rs`.
- Primary shortcuts are now `F1` for help, `F5` for refresh, `1` for Restart OSC Router confirmation, `Enter` for modal confirmation, `Esc` for cancel/close/quit, and `Q`/`q` for quit/cancel.
- `?`, `H`/`h`, `R`/`r`, `O`/`o`, `Y`/`y`, and `N`/`n` remain convenience aliases.
- Simple Russian-layout aliases are mapped for physical `O`, `H`, `R`, `Q`, `Y`, and `N`.
- Top-row and numpad `1` are treated the same where Crossterm exposes them as `KeyCode::Char('1')`.
- `?` is documented as an alias only, not the primary help key.
- The help-toggle guard is `200 ms`. Runtime testing was not performed in this phase, so the guard was not increased to `400 ms`.

Overlay behavior:

- Confirmation modal still owns input first.
- `Enter` confirms in the confirmation modal.
- `Esc`, `Q`/`q`, and `N`/`n` cancel in the confirmation modal.
- `Y`/`y` remains a secondary confirm alias.
- `1` does not confirm inside the modal.
- Help overlay owns input second; `F1`, help aliases, `Esc`, and `Q`/`q` close help.
- Dashboard input owns normal shortcuts only when no overlay is visible.

Classic console and Configurator:

- Classic console behavior was inspected and confirmed as `1`-`6` plus `F1`.
- Classic console behavior was not changed.
- Added one classic console help line describing modern desktop TUI primary shortcuts.
- Configurator already had a console hotkeys panel; added one matching informational line for modern desktop TUI primary keys.
- Updated release packaging wording from read-only desktop TUI to desktop TUI without changing packaging behavior.
- No Configurator settings or `Desktop console mode` behavior were added.

Bridge/backend status:

- `PimaxVrcSupervisor.Tui/src/bridge.rs` remains limited to read-only `query-json` helpers plus `execute_restart_osc_router()`.
- `action-json` appears only inside `execute_restart_osc_router()`.
- No generic action executor was added.
- No legacy action command strings were added.
- Backend `action-json` allowlist remains `restart-osc-router` only.

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust formatting completed successfully.
- Rust debug build succeeded.
- Rust release build succeeded.
- Main supervisor Release build succeeded with 0 warnings and 0 errors.
- ConfigEditor Release build succeeded with 0 warnings and 0 errors.
- SteamVrHost Release build succeeded with 0 warnings and 0 errors.

Release copy result:

- Attempted to copy updated `PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe` into `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- Copy failed because the release-folder `PimaxVrcSupervisorTui.exe` was in use by another process.
- The process was not terminated automatically.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported ignored Rust build output under `PimaxVrcSupervisor.Tui/target/`.

Runtime testing:

- Runtime shortcut testing was not performed in this phase.
- Verification was by code inspection, bridge string inspection, classic console shortcut inspection, and successful builds.

Known risks:

- The `200 ms` help-toggle guard should be validated in an interactive terminal. If held `F1`, `H`, or `?` still flickers, increase the guard to `400 ms` and document that change.
- Russian-layout aliases depend on terminal character delivery and may not work through every IME.
- Other layouts and IMEs should use primary number/function/Enter/Esc shortcuts.

## Next Prompt Handling

Full phase prompts are prepared manually outside this file and pasted into Codex when needed.

### Phase 14 - TUI help shortcut polish

Status: Completed

Summary:

- Changed the Rust desktop TUI so `H`/`h` is the only help shortcut.
- Removed `F1`, `?`, and Russian `Р`/`р` as TUI help triggers.
- Added a `400 ms` quiet-interval H-key guard.
- Kept `restart-osc-router` as the only executable TUI action.
- Kept execution confirmation-gated through the narrow backend `action-json` helper.
- Left classic console behavior unchanged; it still uses `1`-`6` plus `F1`.

Files changed:

- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

TUI shortcut behavior:

- Dashboard: `H` opens help, `F5` refreshes, `1` opens Restart OSC Router confirmation, `Q` quits, and log navigation keys scroll logs.
- Help overlay: `H`, `Esc`, and `Q` close help; `1`, `O`, `Enter`, `F1`, `?`, and removed help aliases do not trigger dashboard actions.
- Confirmation modal: `Enter` confirms, `Esc`/`Q`/`N` cancel, `Y` remains a secondary confirm alias, and `1` never confirms.
- Letter shortcuts are displayed uppercase, but lowercase input is still accepted.
- Russian-layout aliases remain only for selected non-help refresh, restart, quit, confirm, and cancel keys where terminal character input provides them.

Help held-key guard:

- The TUI still ignores `KeyEventKind::Repeat` and `KeyEventKind::Release`.
- The TUI now also tracks the previous raw `H`/`h` help-key event timestamp.
- On each `H`/`h` event, it compares `now` against the previous timestamp before updating it.
- Help toggles only when the previous `H`/`h` event is missing or at least `400 ms` old.
- The previous `H`/`h` timestamp is updated on every `H`/`h` event, including ignored held/repeated events.

Classic console and Configurator:

- Classic console behavior was not changed.
- Existing modern desktop TUI informational text was updated from `F1 help` to `H help`.
- No Configurator settings or `Desktop console mode` behavior were added.

Bridge/backend status:

- `PimaxVrcSupervisor.Tui/src/bridge.rs` remains limited to read-only `query-json` helpers plus `execute_restart_osc_router()`.
- `action-json` appears only inside `execute_restart_osc_router()`.
- No generic action executor was added.
- No legacy action command strings were added.
- Backend `action-json` allowlist remains `restart-osc-router` only.

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust formatting completed successfully.
- Rust debug build succeeded.
- Rust release build succeeded.
- Main supervisor Release build succeeded with 0 warnings and 0 errors.
- ConfigEditor Release build succeeded with 0 warnings and 0 errors.
- SteamVrHost Release build succeeded with 0 warnings and 0 errors.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`, confirming Rust build output is ignored.

Runtime testing:

- Runtime shortcut testing was not performed during implementation unless explicitly recorded later.
- Expected runtime acceptance: `H` opens/closes help without held-key flicker; `F1`, `?`, and Russian help aliases do not open help; only confirmed `restart-osc-router` can execute.

Known risks:

- Russian-layout aliases depend on terminal character delivery and may not work through every IME.
- Runtime shortcut testing should still be done across keyboard layouts and terminals.

### Phase 14B - TUI help debounce and alias text cleanup

Status: Completed

Summary:

- Replaced the Phase 14 `400 ms` quiet-interval H-key guard with a fixed `100 ms` debounce.
- Debounce is based on the last successful Help toggle.
- Ignored `H`/`h` events no longer update the debounce timestamp.
- Kept `H`/`h` as the only Help trigger.
- Kept `F1`, `?`, and Russian help aliases disabled for Help.
- Removed the Russian-layout alias line from the main Help overlay.
- Kept Russian-layout aliases working for selected non-help shortcuts.
- Kept `restart-osc-router` as the only executable TUI action.
- No backend action allowlist, SteamVR host behavior, classic console behavior, Configurator behavior, config semantics, or release packaging changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

TUI behavior:

- Dashboard `1` still opens the Restart OSC Router confirmation.
- Modal `1` still does not confirm.
- Modal `Enter` and `Y`/`y` still confirm.
- Modal `Esc`, `Q`/`q`, and `N`/`n` still cancel.
- Help overlay still consumes input before dashboard shortcuts.
- Help opens/closes through `H`/`h`, `Esc`, and `Q`/`q`; `F1`, `?`, and Russian help aliases do not open Help.

Bridge/backend status:

- `PimaxVrcSupervisor.Tui/src/bridge.rs` remains limited to read-only `query-json` helpers plus `execute_restart_osc_router()`.
- `action-json` appears only inside `execute_restart_osc_router()`.
- No generic action executor was added.
- No legacy action command strings were added.
- Backend `action-json` allowlist remains `restart-osc-router` only.

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Rust formatting completed successfully.
- Rust debug build succeeded.
- Rust release build succeeded.
- Main supervisor Release build succeeded with 0 warnings and 0 errors.
- ConfigEditor Release build succeeded with 0 warnings and 0 errors.
- SteamVrHost Release build succeeded with 0 warnings and 0 errors.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`, confirming Rust build output is ignored.

Runtime testing:

- Runtime shortcut testing was not performed during implementation unless explicitly recorded later.
- Expected runtime acceptance: normal `H` presses feel more responsive than Phase 14, held `H` does not flicker badly, Help no longer lists Russian aliases, and only confirmed `restart-osc-router` can execute.

Post-14B tuning:

- Tuned the fixed Help debounce from `250 ms` to `100 ms` to make normal `H` presses more reactive.
- Kept debounce based on the last successful Help toggle.
- Kept `KeyEventKind::Repeat` and `KeyEventKind::Release` ignored.
- No shortcut mappings, executable actions, backend behavior, SteamVR host behavior, classic console behavior, or Configurator behavior changed.

### Phase 14C - Remove sticky TUI help debounce

Status: Completed

Summary:

- Removed the Help debounce/guard state after runtime feedback that quick press-release sequences still felt sticky.
- Restored immediate `H`/`h` Help toggling.
- Kept `F1`, `?`, and Russian help aliases disabled for Help.
- Kept Russian-layout aliases working for selected non-help shortcuts.
- Kept `KeyEventKind::Repeat` and `KeyEventKind::Release` ignored.
- Kept `restart-osc-router` as the only executable TUI action.
- No backend action allowlist, SteamVR host behavior, classic console behavior, Configurator behavior, config semantics, or release packaging changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

TUI behavior:

- Dashboard `H`/`h` toggles Help immediately.
- Help overlay `H`/`h` closes Help immediately.
- `F1`, `?`, and Russian help aliases do not open Help.
- Dashboard `1` still opens the Restart OSC Router confirmation.
- Modal `1` still does not confirm.
- Modal `Enter` and `Y`/`y` still confirm.
- Modal `Esc`, `Q`/`q`, and `N`/`n` still cancel.

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`

Build/test result:

- Rust formatting completed successfully.
- Rust debug build succeeded.
- Rust release build succeeded.

Generated output status:

- `git status --short release` reported no staged or unstaged tracked release changes.
- `git status --ignored --short release` reported `!! release/`, confirming generated release output is ignored.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`, confirming Rust build output is ignored.

Release copy result:

- Attempted to copy updated `PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe` into `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- Copy failed because the release-folder `PimaxVrcSupervisorTui.exe` was in use by another process.
- The process was not terminated automatically.

Runtime testing:

- Runtime shortcut testing was not performed during implementation unless explicitly recorded later.
- Expected runtime acceptance: quick `H` press-release sequences open/close Help without debounce stickiness; `F1`, `?`, and Russian help aliases do not open Help.

### Phase 15 - Classic console action parity in TUI

Status: Completed

Summary:

- Added confirmed desktop TUI parity for the regular classic-console `1`-`6` actions.
- Used canonical lowercase backend command names in structured metadata and responses.
- Kept Help immediate and `H`/`h` only.
- Kept `force-stop-supervisor` blocked and not TUI-executable.
- Kept classic console behavior, SteamVR overlay behavior, cleanup/lifecycle behavior, Configurator behavior, and config semantics unchanged.
- Did not perform release-preparation work.

Confirmed classic console order:

- `1`: `restart-core-apps`
- `2`: `start-osc-goes-brrr`
- `3`: `base-stations-on`
- `4`: `base-stations-off`
- `5`: `restart-osc-router`
- `6`: `reload-autostart-apps`
- `F1`: classic console shortcut help

Backend changes:

- Expanded `action-json` to allow only the six audited regular actions.
- Added `reload-autostart-apps` as a small legacy bridge command reusing the same helper as `action-json`.
- Required JSON boolean `confirmed=true` for all structured actions.
- Kept read-only commands rejected through `action-json`.
- Kept `force-stop-supervisor` blocked with a cleanup-bypass blocked reason.
- Updated `commands-json` metadata so the six regular actions are `actionSupported=true`, `tuiExecutable=true`, and `requiresConfirmation=true`.
- Kept `available=true` semantics as bridge availability only.

Rust TUI changes:

- Added a closed local `TuiAction` enum for the six regular actions.
- Mapped dashboard number keys `1`-`6` to the classic-console action order.
- Replaced the single restart helper with an allowlisted `execute_tui_action(TuiAction)` bridge helper.
- The TUI still sends no legacy action command strings.
- Confirmation modals now show action display name, canonical backend command, safety category, expected effect, and warning.
- Duplicate-action protection and latest action result display apply to all actions.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/bridge.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/models.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three explicit C# Release builds completed successfully with 0 warnings and 0 errors.
- Bridge/source inspection confirmed the TUI sends `action-json` only through the closed `TuiAction` helper path and sends no legacy action command strings directly.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime shortcut/action testing was not performed during implementation.
- User will manually test the TUI in VR before release-preparation work.

Short Phase 16 direction:

- Build and run a manual/runtime shortcut test matrix across keyboard layouts.
- Continue polishing action confirmation safety and failure paths without expanding beyond the Phase 15 parity action set.

### Phase 17D - Backend-off consistency, neutral modal controls, action hints, and log follow

Status: Completed

Summary:

- Refined only the Rust desktop TUI UI/interaction layer after Phase 17C runtime visual testing.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, supervisor cleanup/lifecycle behavior, and Phase 16B base-station guard behavior unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs`.

Rust TUI changes:

- Added an explicit backend-off action outcome so backend-down rejections display `BACKEND OFF` instead of conflict `BLOCKED`.
- Kept backend-disconnected state authoritative before action validation and action-card rendering.
- Backend-down action cards show `BACKEND OFF` with muted borders and `backend unavailable`.
- Confirm modal controls remain mouse-clickable but now render as neutral text instead of green/orange highlighted badges.
- Increased the full operator layout minimum to `120x36`; smaller terminals show a resize fallback with the current terminal size.
- Full-layout `START` action cards consistently show `click or press <number>` hints.
- Added Recent Logs live-follow mode.
- Logs follow newest entries by default.
- `Up`/`PageUp` scroll older and pause live follow.
- `Down`/`PageDown` scroll newer and resume live follow when the newest view is reached.
- `End` and `F` resume latest log follow.
- Help/footer text now includes log follow controls while keeping `Q Quit TUI` visible.

Documentation changes:

- Updated README and Ratatui docs to document backend-off consistency, neutral modal controls, full-layout action hints, the larger recommended operator size, and Recent Logs live-follow behavior.
- Updated the action safety design to record that Phase 17D changes only TUI presentation/interaction and keeps mouse actions limited to existing `TuiAction` values.
- Recorded that no backend/C# behavior changed in this phase.

Files changed:

- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three explicit C# Release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17D diff.
- Source inspection confirmed `bridge.rs` still has no generic arbitrary command executor and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly.
- Source inspection confirmed `force-stop-supervisor` remains blocked/not TUI-executable and `Q`/`q` sends no backend command.
- Copied the rebuilt Rust release TUI binary to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe` for local runtime testing.

Runtime scenarios to verify when safe:

- Start TUI without supervisor: all cards show muted-border `BACKEND OFF`; no card shows `START`, `READY`, or conflict `BLOCKED`.
- Start supervisor after TUI: after refresh, permitted cards become `START` and clickable.
- Close supervisor while TUI is open: after failed refresh, cards return to `BACKEND OFF`; cached metadata does not keep cards looking ready.
- Confirm modal controls are neutral text while mouse Confirm/Cancel regions still work.
- In a full operator-size terminal, all six `START` cards show `click or press <number>` hints.
- Recent Logs follow latest by default, pause when scrolling older, and resume with `End` or `F`.

Generated output status:

- `release/` and `PimaxVrcSupervisor.Tui/target/` remain ignored generated output and must not be staged.

Short Phase 18 direction:

- Run a manual runtime matrix for backend-off/recovery/disconnect visuals, direct mouse actions, neutral modal controls, full-layout action hints, and Recent Logs live-follow behavior in a safe VR session.

### Phase 17C - TUI mouse actions and backend-unavailable state refinement

Status: Completed

Summary:

- Refined only the Rust desktop TUI interaction and display behavior after Phase 17B runtime testing.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, supervisor cleanup/lifecycle behavior, and Phase 16B base-station guard behavior unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs`.

Rust TUI changes:

- Added a shared validated action-start path used by keyboard-confirmed actions and direct mouse action-card starts.
- Left-clicking an action card now starts the selected existing `TuiAction` immediately after backend connection, metadata, duplicate-command, and Base Stations On/Off conflict checks.
- Keyboard `1`-`6` still opens confirmation and does not start actions directly.
- Confirmation now accepts `Enter`, `Space`, or mouse Confirm; `Esc` or mouse Cancel cancels.
- `Q` inside the modal does not quit the TUI and does not send a backend command.
- Backend-disconnected state now overrides stale command metadata before action validation and card rendering.
- Backend-down cards show `BACKEND OFF` with muted borders, do not appear startable, and cannot spawn action workers.
- Ready cards now show `START` and can show `click or press <number>` hints when space allows.
- Core Apps status now shows `WAITING` / `waiting for VRChat` while lifecycle contains `waiting-vrchat`.
- Help behavior remains `0` primary Help and `H`/`h` English-layout alias; Help closes on any key or mouse click and consumes it.

Documentation changes:

- Updated README and Ratatui docs to document direct mouse action starts, keyboard confirmation, `Enter`/`Space` modal confirmation, `Esc` modal cancellation, backend-off card behavior, and Core Apps waiting display.
- Updated the action safety design to record that mouse direct-start still uses only existing `TuiAction` values and the same validation path.
- Recorded that no backend/C# behavior changed in this phase.

Files changed:

- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three explicit C# Release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17C diff.
- Source inspection confirmed `bridge.rs` still has no generic arbitrary command executor and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly.
- Source inspection confirmed `force-stop-supervisor` remains blocked/not TUI-executable and `Q`/`q` sends no backend command.
- Copied the rebuilt Rust release TUI binary to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe` for local runtime testing.

Runtime scenarios to verify when safe:

- Start TUI without supervisor: cards show `BACKEND OFF`, not full red borders; clicking cards records a visible rejection/status and starts no worker.
- Start supervisor after TUI: after refresh, permitted cards become `START` and clickable.
- Close supervisor while TUI is open: after failed refresh, cards return to `BACKEND OFF`; cached metadata does not keep cards looking ready.

Generated output status:

- `release/` and `PimaxVrcSupervisor.Tui/target/` remain ignored generated output and must not be staged.

Short Phase 18 direction:

- Run a real-world VR-session runtime matrix for Phase 17C mouse direct starts, keyboard confirmation, backend reconnect/disconnect states, and action result display before adding new features.

### Phase 16 - TUI background actions and Autostart duplicate protection

Status: Completed

Summary:

- Added background TUI action execution for the existing six classic-console parity actions.
- Kept the backend `action-json` allowlist unchanged.
- Kept `force-stop-supervisor` blocked and not TUI-executable.
- Kept classic console behavior, SteamVR overlay behavior, cleanup/lifecycle behavior, and config semantics unchanged except for explicit duplicate validation/warnings.
- Did not perform release-preparation work.

Rust TUI changes:

- Confirmation modals close immediately after `Enter` or `Y`; the action continues in a background worker.
- Running actions are tracked by canonical backend command name.
- Duplicate same-command starts are blocked with `Action already running: <command>`.
- `base-stations-on` and `base-stations-off` are mutually exclusive while running.
- Other different actions can run concurrently.
- Action completion, bridge failure, timeout, or worker panic records a result and removes the canonical command from the running list.
- If the TUI exits while actions are running, `Q` quits only the TUI and does not cancel backend work or send any backend command.
- Pending action results may be lost after TUI exit.

Configurator and supervisor changes:

- Configurator save validation now refuses Autostart app rows that duplicate the configured Broken Eye or VRCFaceTracking executable.
- Duplicate detection trims whitespace, removes surrounding quotes, compares full paths case-insensitively where possible, and falls back to executable filename comparison.
- Configurator does not silently remove or mutate duplicate Autostart rows.
- Supervisor Autostart startup/reload now warns and skips manually configured Autostart entries that match configured core app executables.
- Remaining Autostart apps continue processing and the config file is not automatically modified.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully with no warnings.
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release` completed successfully with 0 warnings and 0 errors.
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release` initially failed on a dictionary enumeration mistake in the new duplicate message helper; that was fixed.
- Re-run `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release` completed successfully with 0 warnings and 0 errors.
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release` completed successfully with 0 warnings and 0 errors.
- Bridge/source inspection confirmed the TUI sends `action-json` only through `execute_tui_action(TuiAction)`, has no generic arbitrary command executor, and sends no legacy action command strings directly.
- Backend inspection confirmed the `action-json` allowlist remains the six Phase 15 classic-console parity actions and `force-stop-supervisor` remains blocked/not TUI-executable.
- Configurator inspection confirmed save-path duplicate validation is present.
- Supervisor inspection confirmed runtime Autostart duplicate-core skipping is present.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime shortcut/action testing was not performed during implementation.
- If testing from `release\PimaxVrcSupervisor-v1.3.0-test`, copy the rebuilt TUI exe and publish the supervisor/config editor only if those projects changed.
- If a release-folder executable is locked during runtime-test preparation, kill locked processes automatically per user instruction before retrying the copy/publish.

Backend concurrency audit result:

- Model A with subsystem-local backend guards.
- Evidence: TCP bridge accepts concurrent clients via per-client `Task.Run`; `ExecuteSupervisorCommandAsync` and `ExecuteActionJsonAsync` do not use a global serializer; individual subsystems use local locks for core apps, OSCGoesBrrr, OSC router, Autostart, and cleanup.
- Base-station manual actions share mutable base-station state, so the TUI keeps Base Stations On/Off mutual exclusion.
- Final TUI behavior chosen: background execution remains enabled; duplicate same-command starts are blocked; Base Stations On/Off overlap is blocked; no backend threading rewrite.

### Phase 16B - Backend base-station action guard

Status: Completed

Summary:

- Added a backend-local manual base-station action guard.
- Base Stations On/Off overlap is now protected both by the TUI conflict model and by the supervisor backend itself, so other entry points cannot mutate base-station state concurrently.
- Kept the backend `action-json` allowlist unchanged at the six Phase 15/16 classic-console parity actions.
- Kept `force-stop-supervisor` blocked and not TUI-executable.
- Kept SteamVR host behavior, classic console key mappings, cleanup/lifecycle behavior, TCP bridge concurrency, and TUI behavior unchanged.

Backend changes:

- Added one shared manual base-station action semaphore in `PimaxVrcSupervisor/Program.cs`.
- Added a small `ManualBaseStationActionResult` with `Accepted` and `Message`.
- Wrapped the common manual Base Stations On/Off helper path with non-blocking guard acquisition and `finally` release.
- Overlapping manual Base Stations On/Off requests return/log `Base station power action already in progress; ignoring overlapping request.`
- Legacy bridge and structured `action-json` responses use the guarded helper result message, so busy overlap is reported directly.
- Structured `action-json` Base Stations On/Off overlap returns `success=false` with the exact busy message.

Documentation changes:

- Updated `docs/ratatui-action-execution-design.md` and `docs/ratatui-tui.md` to record that Base Stations On/Off mutual exclusion is enforced by both the TUI and supervisor backend.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`

Build/test result:

- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Initial Cargo commands failed because `cargo` was not visible in the current shell PATH.
- After refreshing `$env:Path += ";$env:USERPROFILE\.cargo\bin"`, Rust debug and release builds completed successfully.
- Source inspection confirmed Base Stations On/Off share one backend-local guard and the guard release is in `finally`.
- Source inspection confirmed the backend `action-json` allowlist remains the six Phase 15/16 parity actions and `force-stop-supervisor` remains blocked/not TUI-executable.
- Source inspection confirmed the TUI still sends `action-json` only through `execute_tui_action(TuiAction)` and sends no legacy action command strings directly.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime base-station overlap testing was not performed during implementation.
- When safe, test Base Stations On/Off overlap from separate entry points and verify the overlapping request logs/returns the busy message while the supervisor continues running.

### Phase 18A - TUI lifecycle and Configurator integration audit

Status: Completed

Summary:

- Completed an audit/design-only phase for making the Rust TUI a future primary desktop operator surface.
- Added `docs/phase-18-tui-lifecycle-configurator-design.md`.
- Kept runtime behavior unchanged.
- Did not change backend behavior, bridge behavior, Configurator behavior, SteamVR host behavior, classic console behavior, Rust TUI behavior, package output, action semantics, shutdown behavior, tray behavior, or `Q` semantics.
- Did not modify `PimaxVrcSupervisor/Program.cs` or `PimaxVrcSupervisor.Tui/src/bridge.rs`.

Audit evidence recorded:

- Supervisor startup modes: manual console, hidden scheduled watcher via `--watch-vrchat-auto-launch`, SteamVR helper path via `--steamvr-start`, startup integration helper flags, and detached emergency base-station cleanup helper.
- Shutdown/cleanup paths: watched-process and SteamVR-driven cleanup, Ctrl+C emergency cleanup, console-close cleanup, detached base-station cleanup, and blocked structured `force-stop-supervisor`.
- Bridge/action model: line-oriented TCP bridge, `query-json`, `action-json`, the six allowlisted classic-console parity actions, no current graceful supervisor shutdown command, and `force-stop-supervisor` blocked from structured desktop TUI flow.
- Configurator integration: current bottom-bar `Launch Supervisor` and `Launch SteamVR` buttons exist; no Desktop TUI launch button exists yet.
- Packaging: flat release layout already includes `PimaxVrcSupervisorTui.exe`.

Design decisions recorded:

- Phase 18B should start with a low-risk Configurator `Launch Desktop TUI` button that starts only `PimaxVrcSupervisorTui.exe`.
- Hidden supervisor plus TUI launch workflow is deferred to Phase 18C.
- Graceful supervisor shutdown command design is deferred to Phase 18D and must remain distinct from `force-stop-supervisor`.
- TUI close/`Q` shutdown semantics are deferred to Phase 18E; current `Q` continues to mean close TUI only.
- Tray/minimize architecture is deferred to Phase 18F.

Documentation changes:

- Added the Phase 18 lifecycle/configurator design document to MkDocs Workflows navigation as `TUI Lifecycle Integration Design`.
- Added small cross-links from README, Desktop TUI docs, and action safety design docs.

Files changed:

- `docs/phase-18-tui-lifecycle-configurator-design.md`
- `docs/ratatui-tui-migration-progress.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-action-execution-design.md`
- `README.md`
- `mkdocs.yml`

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`
- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`

Build/test result:

- All three C# release builds completed successfully with 0 warnings and 0 errors.
- `cargo fmt` completed successfully.
- Rust debug build completed successfully.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 18A diff.
- Source inspection confirmed `bridge.rs` has no Phase 18A diff.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

### Phase 18B - Configurator Launch Desktop TUI button

Status: Completed

Summary:

- Added a Configurator `Launch Desktop TUI` button beside the existing bottom-bar launch controls.
- The button starts only `PimaxVrcSupervisorTui.exe` from the Configurator executable folder via `AppContext.BaseDirectory`.
- The launch path is release-folder/local-folder based; it does not discover arbitrary TUI locations or add config schema.
- Missing executable handling shows a clear expected-path message and sets status to `Desktop TUI executable was not found.`
- Duplicate TUI handling detects an existing `PimaxVrcSupervisorTui` process, shows `Desktop TUI is already running.`, and does not start another copy.
- Successful launch sets status to `Desktop TUI launched.`
- Launch failures set status to `Desktop TUI launch failed.`
- The button does not require the supervisor to be running, does not start the supervisor, does not stop the supervisor, does not save config, and does not change TUI `Q` semantics.

Files changed:

- `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `docs/phase-18-tui-lifecycle-configurator-design.md`
- `docs/ratatui-tui-migration-progress.md`
- `docs/ratatui-tui.md`
- `README.md`

Behavior boundaries:

- `PimaxVrcSupervisor/Program.cs` was not modified.
- `PimaxVrcSupervisor.Tui/src/bridge.rs` was not modified.
- Rust TUI behavior was not modified.
- No backend actions, bridge commands, hidden supervisor mode, tray behavior, graceful shutdown command, config schema, SteamVR host behavior, classic console behavior, or `Q` semantics changed.

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`
- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`

Build/test result:

- All three C# release builds completed successfully with 0 warnings and 0 errors.
- `cargo fmt` completed successfully.
- Rust debug build completed successfully.
- Configurator publish completed successfully into `release/PimaxVrcSupervisor-v1.3.0-test`.
- Runtime Configurator button testing was not performed during implementation; it should be tested from a release folder containing `PimaxVrcSupervisorConfigurator.exe` and `PimaxVrcSupervisorTui.exe`.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- `release/` and Rust `target/` output remain generated/ignored and were not staged.

### Phase 18C - Primary TUI lifecycle with Ctrl+C-equivalent shutdown

Status: Completed

Summary:

- Added a dedicated `lifecycle-json` bridge verb for `request-graceful-shutdown`.
- Refactored the Ctrl+C cleanup sequence into a shared supervisor graceful-shutdown request path.
- Dashboard `Q` in the Rust TUI now opens `Stop supervisor and exit TUI?` confirmation instead of closing only the TUI.
- Confirming shutdown sends `lifecycle-json {"action":"request-graceful-shutdown","source":"Desktop TUI"}`.
- The supervisor logs the Desktop TUI shutdown source, runs Ctrl+C-equivalent emergency cleanup, cancels the supervisor token, and exits through the existing `RunAsync` unwind path.
- The TUI exits after accepted/already-in-progress shutdown plus backend disconnect, or after the 60 second post-accepted timeout.
- If the backend is not running, dashboard `Q` exits the TUI without starting or stopping anything.
- Added Configurator `Launch Supervisor + Desktop TUI`; it uses the normal supervisor launch path, skips duplicate supervisor launch, then opens the Desktop TUI.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/bridge.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/phase-18-tui-lifecycle-configurator-design.md`
- `docs/ratatui-tui-migration-progress.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-action-execution-design.md`

Backend details:

- `lifecycle-json` is explicit and narrow; it supports only `request-graceful-shutdown`.
- Lifecycle responses use compact camelCase `SupervisorCommandResult` JSON with `resultType="lifecycle"` and data fields `accepted`, `alreadyInProgress`, and `status`.
- Malformed, non-object, missing-action, and unknown lifecycle requests return structured failures without unhandled TCP exceptions.
- Repeated shutdown requests return `already_in_progress`.
- Regular `action-json` requests are rejected while graceful shutdown is in progress.
- `force-stop-supervisor` remains the legacy hard-stop command and remains blocked/unexposed from structured TUI action flow.

Configurator details:

- Existing `Launch Desktop TUI` remains unchanged and starts only `PimaxVrcSupervisorTui.exe`.
- New `Launch Supervisor + Desktop TUI` reuses the existing validation, unsaved-change, config path, UAC, and normal supervisor launch behavior.
- If `PimaxVrcSupervisor.exe` is already running, the combined launch skips starting another supervisor and proceeds to Desktop TUI launch.
- Hidden/non-interactive supervisor launch was deferred because the only current hidden supervisor mode, `--steamvr-start`, changes SteamVR lifecycle semantics.

TUI details:

- Dashboard `Q` and footer Quit click now open graceful shutdown confirmation.
- Shutdown confirmation uses `Enter`/`Space` to confirm and `Esc` to cancel.
- Normal action starts are disabled while shutdown is in progress.
- The TUI has no close-TUI-only dashboard shortcut in Phase 18C.
- The lifecycle client is a narrow `request_graceful_shutdown()` bridge helper; no generic command executor was added.

Deferred:

- Tray/minimize behavior.
- Terminal window X-close shutdown guarantee.
- Auto-start settings and config schema changes.
- General hidden supervisor mode.
- Graceful shutdown controls outside the confirmed TUI `Q` flow.

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`
- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `Copy-Item .\PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe .\release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe -Force`

Build/test result:

- All three C# release builds completed successfully with 0 warnings and 0 errors.
- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- Publish/copy to `release/PimaxVrcSupervisor-v1.3.0-test` completed successfully.
- Runtime lifecycle testing was not performed during implementation because it can trigger local supervisor cleanup, VR/SteamVR/VRChat workflows, and base-station behavior.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- `release/` and Rust `target/` output remain generated/ignored and were not staged.

### Phase 18D - Harden primary TUI lifecycle workflow

Status: Completed with release-folder supervisor publish blocked by a locked watcher process

Summary:

- Hardened the Phase 18C primary TUI lifecycle workflow without adding tray behavior, hidden supervisor launch mode, close-TUI-only dashboard `Q`, generic command execution, new lifecycle actions, or SteamVR host changes.
- Preserved `lifecycle-json` as the only graceful shutdown bridge path and kept it limited to `request-graceful-shutdown`.
- Preserved Ctrl+C-equivalent cleanup sharing, cleanup order, and existing idempotency guards.
- Kept `force-stop-supervisor` explicitly blocked from structured action flow, including when shutdown is already in progress.
- Kept the six regular `action-json` TUI actions unchanged.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `PimaxVrcSupervisor.Tui/src/app.rs`
- `README.md`
- `docs/phase-18-tui-lifecycle-configurator-design.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Backend hardening:

- The TCP bridge now writes an already-produced response with `CancellationToken.None`, so an accepted `lifecycle-json` response has a better chance to reach the TUI even if graceful shutdown cancellation begins quickly.
- `force-stop-supervisor` is rejected before the shutdown-in-progress action rejection, so structured requests for that command still receive the explicit blocked response.
- Malformed, missing, non-object, and unknown lifecycle requests remain structured compact JSON failures.

TUI hardening:

- Lifecycle request failures now record the actual backend/bridge rejection message in the last-action/error display instead of a generic shutdown failure.
- If shutdown is accepted but the backend remains reachable after 60 seconds, the TUI shows `Shutdown was requested, but the supervisor is still reachable. Check the supervisor logs.` for a short two-second notice period before exiting.
- Shutdown-in-progress still disables normal action starts and mouse action clicks.
- Backend-off dashboard `Q` still exits the TUI without sending any backend command.

Configurator hardening:

- `Launch Desktop TUI` still behaves as before for standalone TUI launch.
- `Launch Supervisor + Desktop TUI` now distinguishes launched, already-running, and failed Desktop TUI outcomes in its final status messages.
- Existing normal supervisor validation, unsaved-change, config path, UAC launch behavior, and duplicate supervisor skip behavior are preserved.
- Hidden/non-interactive supervisor launch remains deferred because `--steamvr-start` changes supervisor/SteamVR lifecycle semantics.

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`
- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `Copy-Item .\PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe .\release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe -Force`

Build/test result:

- All three C# release builds completed successfully with 0 warnings and 0 errors.
- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- `PimaxVrcSupervisor.ConfigEditor` and `PimaxVrcSupervisor.SteamVrHost` publish completed successfully after stopping the locked Configurator process.
- Rust TUI release copy completed successfully.
- `PimaxVrcSupervisor` publish to the ignored release folder was blocked because `PimaxVrcSupervisorWatcher.exe` PID 24116 held `release/PimaxVrcSupervisor-v1.3.0-test/PimaxVrcSupervisor.dll`; `Stop-Process -Id 24116 -Force` failed with `Access is denied`.
- Expected release executables were present in the release folder, but `PimaxVrcSupervisor.exe`/DLL output may be stale because the supervisor publish could not overwrite the locked DLL.
- Runtime lifecycle testing was not performed because it can trigger local supervisor cleanup, VR/SteamVR/VRChat workflows, and base-station behavior.

Source inspection:

- `PimaxVrcSupervisor.SteamVrHost` has no source diff.
- `PimaxVrcSupervisor.Tui/src/bridge.rs` has no source diff.
- The TUI still sends `action-json` only through `execute_tui_action(TuiAction)`.
- The TUI sends no legacy action command strings directly.
- `lifecycle-json` remains the only graceful shutdown bridge path.
- `force-stop-supervisor` remains blocked/not TUI-executable.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- `release/` and Rust `target/` output remain generated/ignored and were not staged.

Short Phase 18E direction:

- Resolve the locked release watcher situation before runtime testing from the release folder, then run the real-world lifecycle matrix for Configurator combined launch, TUI shutdown cancel/confirm, backend-off `Q`, duplicate launch behavior, and post-shutdown timeout behavior.
- Continue to defer hidden supervisor mode, terminal X-close shutdown guarantees, and tray/minimize behavior until separate designs are reviewed.

### Phase 18E - Safe hidden supervisor mode for primary TUI workflow

Status: Completed

Summary:

- Added explicit `--desktop-tui-start` supervisor startup mode for Configurator-launched primary Desktop TUI sessions.
- `--desktop-tui-start` hides the supervisor console early with the existing `ConsoleWindow.HideIfPresent()` helper.
- Normal Configurator `Launch Supervisor` remains a visible classic-console launch and does not pass `--desktop-tui-start`.
- `Launch Supervisor + Desktop TUI` now starts the supervisor with `--desktop-tui-start`, then opens the Desktop TUI through the existing same-folder launcher.
- `steamVrStart` remains true only for `--steamvr-start`; `--desktop-tui-start` does not change SteamVR lifecycle semantics.
- `--steamvr-start` remains SteamVR-specific, and `--watch-vrchat-auto-launch` remains the scheduled hidden watcher path.
- TUI `Q` shutdown semantics, `lifecycle-json`, `action-json`, action allowlists, cleanup order, SteamVR host behavior, and classic console behavior were unchanged.

Audit decision:

- `PimaxVrcSupervisor` is a console-subsystem app (`OutputType Exe`), so a brief console creation before managed startup code runs is still possible.
- Existing code already had `ConsoleWindow.HideIfPresent()` for SteamVR, watcher, and startup-helper paths.
- A dedicated flag was sufficient for Phase 18E; no Windows-subsystem wrapper, tray app, config schema field, or generic hidden launcher was added.
- `CreateNoWindow` was not used for the elevated Configurator launch path because the existing `UseShellExecute=true` / `runas` flow is required for UAC behavior.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `README.md`
- `docs/phase-18-tui-lifecycle-configurator-design.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`
- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.3.0-test`
- `Copy-Item .\PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe .\release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe -Force`

Build/test result:

- All three C# release builds completed successfully with 0 warnings and 0 errors.
- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- Release publish for all three C# projects completed successfully.
- Rust TUI release executable copy completed successfully.
- Expected key release executables were present: `PimaxVrcSupervisor.exe`, `PimaxVrcSupervisorConfigurator.exe`, `PimaxVrcSupervisorSteamVrHost.exe`, and `PimaxVrcSupervisorTui.exe`.
- Runtime lifecycle testing was not performed during implementation because it can trigger local supervisor cleanup, VR/SteamVR/VRChat workflows, and base-station behavior.

Source inspection:

- `--desktop-tui-start` is explicit and only used by the Configurator combined launch path.
- Normal `Launch Supervisor` does not pass `--desktop-tui-start`.
- Configurator combined launch does not use `--steamvr-start`.
- `steamVrStart` is still derived only from `--steamvr-start`.
- `PimaxVrcSupervisor.SteamVrHost` has no source diff.
- `PimaxVrcSupervisor.Tui/src/bridge.rs` has no source diff.
- `force-stop-supervisor` remains blocked/not TUI-executable.
- No generic command executor was added.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- `release/` and Rust `target/` output remain generated/ignored and were not staged.

Short Phase 18F direction:

- Run the real-world release-folder lifecycle matrix for visible `Launch Supervisor`, hidden `Launch Supervisor + Desktop TUI`, duplicate launch behavior, TUI shutdown cancel/confirm, and backend-off `Q`.
- Continue to defer terminal X-close shutdown guarantees and tray/minimize behavior until separate designs are reviewed.

### Phase 17L - Compact and small TUI action click zones

Status: Completed

Summary:

- Refined only Rust TUI rendering/click-region behavior and docs after Phase 17K.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, cleanup/lifecycle behavior, action semantics, `Q` behavior, and the Phase 16B base-station guard unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs` or `PimaxVrcSupervisor.Tui/src/bridge.rs`.
- Did not add supervisor shutdown, tray behavior, Configurator launch options, graceful stop actions, new backend actions, new crates, or copied GPL/reference code.

Rust TUI changes:

- Kept full-layout 2x3 action cards whole-card clickable.
- Replaced compact whole-row action click regions with `[START]`-badge-only click regions.
- Replaced small whole-cell action click regions with `[START]`-badge-only click regions.
- Registered compact/small action click regions only when the rendered action state is `START`.
- Left labels, display names, row backgrounds, small-cell labels, gutters, empty panel space, and non-startable states non-clickable in compact/small layouts.
- Preserved the Phase 17I badge rule: interactive `[START]` remains bracketed, while normal status badges and non-startable action states remain unbracketed.
- Preserved keyboard `1`-`6` confirmation behavior, mouse wheel logs, Help/modal mouse consumption, dashboard `Q`, modal `Q`, and action validation behavior.

Documentation changes:

- Updated README and Ratatui docs to record compact/small `[START]`-only click zones.
- Updated the action safety design to record Phase 17L as rendering/click-region-only.
- Recorded that no backend/C# behavior, bridge behavior, shutdown/tray/lifecycle behavior, Configurator behavior, or action semantics changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17L diff.
- Source inspection confirmed `bridge.rs` has no Phase 17L diff, no generic arbitrary command executor, and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly, `force-stop-supervisor` remains unexposed, and dashboard/modal `Q` sends no backend command.

Release/copy result:

- Rebuilt `PimaxVrcSupervisorTui.exe` was copied to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- No C# publish was performed because no C# files changed.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime visual/mouse testing was not performed during implementation.
- When safe, verify full cards remain whole-card clickable, compact/small `[START]` clicks start actions, compact/small labels/descriptions/background/gutters do nothing, keyboard `1`-`6` still opens confirmation, and no supervisor shutdown/tray/lifecycle behavior was introduced.

Follow-up spacing adjustment:

- Kept compact actions as a one-column list.
- Added optional vertical spacing between compact action rows only when the compact action panel height can fit six actions plus five spacer rows.
- Kept compact `[START]` column alignment and `[START]`-only click regions.
- Kept the small layout as the current 3-column action grid with no vertical gaps and `[START]`-only click regions.
- Full layout remained unchanged.
- No backend, bridge, action behavior, `Q` behavior, lifecycle, shutdown, tray, SteamVR host, classic console, or Configurator behavior changed.

Short Phase 18 direction:

- Run a manual VR-session runtime matrix for the final adaptive TUI layouts and decide separately whether TUI-as-primary lifecycle, tray minimize, Configurator launch, or graceful supervisor shutdown should be designed in a later phase.

### Phase 17K - Compact and small TUI action spacing

Status: Completed

Summary:

- Refined only Rust TUI rendering/docs after Phase 17J.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, cleanup/lifecycle behavior, action semantics, `Q` behavior, and the Phase 16B base-station guard unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs` or `PimaxVrcSupervisor.Tui/src/bridge.rs`.
- Did not add supervisor shutdown, tray behavior, Configurator launch options, graceful stop actions, new backend actions, new crates, or copied GPL/reference code.

Rust TUI changes:

- Added a small fixed gutter between the three small-layout action cells.
- Kept each small-layout action cell composed as number, short label, and nearby `[START]`.
- Avoided right-aligning `[START]` to the far edge of small action cells.
- Kept compact action labels and action-state badges aligned in fixed columns.
- Tightened the compact action-state column so display names do not drift too far right.
- Preserved the Phase 17I badge rule: interactive `[START]` remains bracketed, while normal status badges such as `OK`, `READY`, `WAITING`, `OFF`, and `STOPPED` remain unbracketed.
- Preserved keyboard, mouse, log follow, dashboard `Q`, modal `Q`, and action validation behavior.

Documentation changes:

- Updated README and Ratatui docs to record compact/small action spacing polish.
- Updated the action safety design to record Phase 17K as rendering-only.
- Recorded that no backend/C# behavior, bridge behavior, shutdown/tray/lifecycle behavior, Configurator behavior, or action semantics changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17K diff.
- Source inspection confirmed `bridge.rs` has no Phase 17K diff, no generic arbitrary command executor, and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly, `force-stop-supervisor` remains unexposed, and dashboard/modal `Q` sends no backend command.

Release/copy result:

- Rebuilt `PimaxVrcSupervisorTui.exe` was copied to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- No C# publish was performed because no C# files changed.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime visual/mouse testing was not performed during implementation.
- When safe, verify compact rows keep aligned label and `[START]` columns without excessive display-name offset, small action cells have visible gutters, `[START]` stays close to labels, status badges remain unbracketed, and no supervisor shutdown/tray/lifecycle behavior was introduced.

Short Phase 18 direction:

- Run a manual VR-session runtime matrix for the final adaptive TUI layouts and decide separately whether TUI-as-primary lifecycle, tray minimize, Configurator launch, or graceful supervisor shutdown should be designed in a later phase.

### Phase 17J - Compact TUI action badge column alignment

Status: Completed

Summary:

- Refined only Rust TUI rendering/docs after Phase 17I runtime feedback.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, cleanup/lifecycle behavior, action semantics, and the Phase 16B base-station guard unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs`.
- Did not add supervisor shutdown, tray behavior, Configurator launch options, graceful stop actions, new backend actions, new crates, or copied GPL/reference code.

Rust TUI changes:

- Updated compact dashboard action-row rendering to use fixed label and badge columns.
- Kept compact labels as `1 Core`, `2 OGB`, `3 BS On`, `4 BS Off`, `5 OSC`, and `6 Auto`.
- Aligned `[START]`, `RUNNING`, `BACKEND OFF`, `BLOCKED`, and `UNAVAILABLE` in one compact action-state column.
- Kept display names aligned after the action-state column and truncated only the display name when compact width is tight.
- Preserved full-layout 2x3 cards and the small-layout 3-column action grid.
- Preserved the Phase 17I badge rule: interactive `[START]` remains bracketed, while normal status badges and non-startable action states remain unbracketed.
- Preserved keyboard, mouse, log follow, dashboard `Q`, modal `Q`, and action validation behavior.

Documentation changes:

- Updated README and Ratatui docs to record fixed compact label/action-state columns.
- Updated the action safety design to record Phase 17J as rendering-only.
- Recorded that no backend/C# behavior, shutdown/tray/lifecycle behavior, Configurator behavior, or action semantics changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17J diff.
- Source inspection confirmed `bridge.rs` has no Phase 17J diff, no generic arbitrary command executor, and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly, `force-stop-supervisor` remains unexposed, and dashboard/modal `Q` sends no backend command.

Release/copy result:

- Rebuilt `PimaxVrcSupervisorTui.exe` was copied to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- No C# publish was performed because no C# files changed.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime visual/mouse testing was not performed during implementation.
- When safe, verify compact action rows align their action-state column, `[START]` stays bracketed, status badges stay unbracketed, full/small layouts did not regress, and no supervisor shutdown/tray/lifecycle behavior was introduced.

Short Phase 18 direction:

- Run a manual VR-session runtime matrix for the final adaptive TUI layouts and decide separately whether TUI-as-primary lifecycle, tray minimize, Configurator launch, or graceful supervisor shutdown should be designed in a later phase.

### Phase 17I - Correct TUI bracket usage and compact action spacing

Status: Completed

Summary:

- Refined only Rust TUI rendering/docs after Phase 17H.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, cleanup/lifecycle behavior, action semantics, and the Phase 16B base-station guard unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs`.
- Did not add supervisor shutdown, tray behavior, Configurator launch options, graceful stop actions, new backend actions, new crates, or copied GPL/reference code.

Rust TUI changes:

- Split status badge rendering from interactive action button rendering.
- Changed `theme::badge(...)` back to unbracketed colored status text.
- Added a separate bracketed action-button badge helper for interactive action controls such as `[START]`.
- Updated normal status badges in the header, status panels, compact/small status lines, system area, and last-result display to render unbracketed words such as `OK`, `READY`, `WAITING`, `OFF`, and `BACKEND OFF`.
- Kept interactive start controls bracketed as `[START]` in full action cards, compact action rows, and small action cells.
- Kept non-startable action states unbracketed: `RUNNING`, `BACKEND OFF`, `BLOCKED`, and `UNAVAILABLE`.
- Adjusted compact action rows so `1 Core [START]` stays close to the action label before the display name.
- Preserved backend-off-first action state logic, click regions, keyboard behavior, mouse behavior, log follow behavior, dashboard `Q`, modal `Q`, and action validation behavior.

Documentation changes:

- Updated README and Ratatui docs to record that brackets are reserved for interactive action buttons such as `[START]`.
- Updated the action safety design to record Phase 17I as rendering-only.
- Recorded that normal status badges keep colored backgrounds without brackets.
- Recorded that no backend/C# behavior, shutdown/tray/lifecycle behavior, Configurator behavior, or action semantics changed.

Files changed:

- `PimaxVrcSupervisor.Tui/src/theme.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17I diff.
- Source inspection confirmed `bridge.rs` has no Phase 17I diff, no generic arbitrary command executor, and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly, `force-stop-supervisor` remains unexposed, and dashboard/modal `Q` sends no backend command.

Release/copy result:

- Rebuilt `PimaxVrcSupervisorTui.exe` was copied to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- No C# publish was performed because no C# files changed.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime visual/mouse testing was not performed during implementation.
- When safe, verify status badges render unbracketed, startable action buttons render as `[START]`, compact action rows keep `[START]` close to the label, small action cells stay compact, and no supervisor shutdown/tray/lifecycle behavior was introduced.

Short Phase 18 direction:

- Run a manual VR-session runtime matrix for the final adaptive TUI layouts and decide separately whether TUI-as-primary lifecycle, tray minimize, Configurator launch, or graceful supervisor shutdown should be designed in a later phase.

### Phase 17H - Bracket badge styling and small layout polish

Status: Completed

Summary:

- Refined only Rust TUI rendering/docs after Phase 17G.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, cleanup/lifecycle behavior, action semantics, and the Phase 16B base-station guard unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs`.
- Did not add supervisor shutdown, tray behavior, Configurator launch options, graceful stop actions, new backend actions, new crates, or copied GPL/reference code.

Rust TUI changes:

- Changed the shared badge helper to render bracket-style badges as a single colored span, such as `[OK]`, `[START]`, `[WAITING]`, and `[BACKEND OFF]`.
- Kept badge backgrounds only on the complete bracket badge span.
- Kept normal labels, action names, values, logs, footer text, and modal controls foreground-only.
- Updated the small action-cell renderer so each cell composes label plus nearby badge first, then pads the rest of the fixed-width cell.
- Kept small labels as `Core`, `OGB`, `On`, `Off`, `OSC`, and `Auto`.
- Updated the small Essential Status lifecycle row to use `Life [READY] value` style.
- Preserved full, compact, small, and tiny layout thresholds.
- Preserved keyboard, mouse, log follow, dashboard `Q`, modal `Q`, and action validation behavior.

Documentation changes:

- Updated README and Ratatui docs to record bracket-style badge text and compact small-layout action cells.
- Updated the action safety design to record Phase 17H as rendering-only.
- Recorded future TUI-as-primary lifecycle, tray minimize, Configurator launch, and graceful shutdown as future work only.

Files changed:

- `PimaxVrcSupervisor.Tui/src/theme.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17H diff.
- Source inspection confirmed `bridge.rs` has no Phase 17H diff, no generic arbitrary command executor, and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly, `force-stop-supervisor` remains unexposed, and dashboard/modal `Q` sends no backend command.

Release/copy result:

- Rebuilt `PimaxVrcSupervisorTui.exe` was copied to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- No C# publish was performed because no C# files changed.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime visual/mouse testing was not performed during implementation.
- When safe, verify `80x20` small Essential Status uses bracket badges, action cells render as `1 Core [START]` with badges close to labels, full/compact layouts show bracket badges, modal controls remain neutral, and no supervisor shutdown/tray/lifecycle behavior was introduced.

Short Phase 18 direction:

- Run a manual VR-session runtime matrix for the final adaptive TUI layouts and decide separately whether TUI-as-primary lifecycle, tray minimize, Configurator launch, or graceful supervisor shutdown should be designed in a later phase.

### Phase 17G - Small layout alignment and final TUI visual polish

Status: Completed

Summary:

- Refined only the Rust desktop TUI rendering/docs after Phase 17F runtime feedback.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, cleanup/lifecycle behavior, action semantics, and the Phase 16B base-station guard unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs`.
- Did not add supervisor shutdown, tray minimize, Configurator launch options, new backend actions, new crates, or copied GPL/reference code.

Rust TUI changes:

- Aligned the small `80x20` action area as a fixed two-row, three-column grid.
- Kept row order as `1/2/3` and `4/5/6`.
- Rendered each small action into a fixed-width cell with the number/short label on the left and the state badge on the right.
- Used compact small-layout labels: `Core`, `OGB`, `On`, `Off`, `OSC`, and `Auto`.
- Preserved per-cell click regions that route only to `ClickAction::SelectAction(TuiAction)`.
- Preserved badge-only backgrounds for state words; action numbers, labels, status values, last result text, last log text, footer text, and modal text remain foreground-only.
- Preserved full, compact, small, and tiny adaptive layout thresholds.
- Preserved mouse wheel log scrolling, log follow behavior, keyboard confirmation behavior, direct mouse action start behavior, and dashboard `Q` quitting only the TUI.

Documentation changes:

- Updated README and Ratatui docs to record the small-layout action-grid alignment and unchanged `Q` semantics.
- Updated the action safety design to record Phase 17G as rendering-only.
- Recorded future TUI-as-primary lifecycle ideas, tray minimize, and graceful supervisor shutdown as future design work only.

Files changed:

- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17G diff.
- Source inspection confirmed `bridge.rs` has no Phase 17G diff, no generic arbitrary command executor, and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly, `force-stop-supervisor` remains unexposed, and dashboard/modal `Q` sends no backend command.

Release/copy result:

- Rebuilt `PimaxVrcSupervisorTui.exe` was copied to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- No C# publish was performed because no C# files changed.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime visual/mouse testing was not performed during implementation.
- When safe, verify the `80x20` small layout action labels and badges line up cleanly, no action row wraps, badge backgrounds appear only on badge words, full and compact layouts did not regress, and no supervisor shutdown/tray/lifecycle behavior was introduced.

Short Phase 18 direction:

- Run a manual VR-session runtime matrix for the final adaptive TUI layouts and decide separately whether TUI-as-primary lifecycle, tray minimize, or graceful supervisor shutdown should be designed in a later phase.

### Phase 17F - Priority adaptive TUI layout, badge styling, and log controls

Status: Completed

Summary:

- Refined only the Rust desktop TUI UI/interaction layer after Phase 17E runtime feedback.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, cleanup/lifecycle behavior, and the Phase 16B base-station guard unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs`.
- Borrowed only the adaptive-layout principle from ShockingVRC; no ShockingVRC code, UI structure, event code, assets, or GPL implementation were copied.

Rust TUI changes:

- Kept full dashboard at `120x32` or larger.
- Kept compact useful dashboard at `100x26` or larger.
- Added a small essential dashboard at `80x20` or larger.
- Reserved tiny fallback for terminals below `80x20`.
- Small layout keeps backend state, lifecycle, Core Apps, OSC Router, Base Stations, six action states, latest activity, a last-log line, and Help/Quit controls visible.
- Centralized badge-only state styling in `theme.rs`.
- Status/state words such as `OK`, `START`, `WAITING`, `RUNNING`, `OFF`, `STOPPED`, `ERROR`, `BACKEND OFF`, `BLOCKED`, and `UNAVAILABLE` use colored badge backgrounds.
- Normal labels, values, action names, click hints, modal body text, and log lines use foreground-only styles with no background underlay.
- Preserved the action-state priority for every full card, compact row, and small row: backend disconnected, running, Base Stations On/Off conflict, blocked metadata, unavailable metadata, executable START.
- Backend disconnected still short-circuits before cached metadata, so disconnected cards/rows cannot show stale `START`, conflict `BLOCKED`, or metadata `UNAVAILABLE`.
- Full action cards remain a 2x3 grid with equal-height cards and `click or press <number>` hints.
- Compact/small action rows reuse only the existing `TuiAction` values and the same click-region model.
- Log titles keep follow controls visible, including `Wheel` scrolling and `End/F` follow.

Documentation changes:

- Updated README and Ratatui docs to record the full/compact/small/tiny adaptive tiers, badge-only background rule, backend-off priority, and mouse/log-control visibility.
- Updated the action safety design to record Phase 17F as UI-only and to document the ShockingVRC principle-only distinction.

Files changed:

- `PimaxVrcSupervisor.Tui/src/theme.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17F diff.
- Source inspection confirmed `bridge.rs` still has no generic arbitrary command executor and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly, `force-stop-supervisor` remains unexposed, and `Q`/`q` sends no backend command.

Release/copy result:

- Rebuilt `PimaxVrcSupervisorTui.exe` was copied to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- No C# publish was performed because no C# files changed.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime visual/mouse testing was not performed during implementation.
- When safe, verify `130x34` uses the full dashboard, `100x26` uses the compact dashboard, `80x20` uses the small essential dashboard, tiny terminals show resize fallback, badge backgrounds appear only on state words, normal text has no background underlay, and backend-off cards/rows always override cached metadata.

Short Phase 18 direction:

- Run a manual VR-session test matrix for the adaptive tiers, badge readability, backend-off/recovery states, mouse wheel log scrolling, and action-card/row click behavior before adding more UI behavior.

### Phase 17E - Adaptive TUI layout, cleaner action cards, and mouse log scrolling

Status: Completed

Summary:

- Refined only the Rust desktop TUI UI/interaction layer after Phase 17D.
- Kept backend behavior, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, cleanup/lifecycle behavior, and the Phase 16B base-station guard unchanged.
- Did not modify `PimaxVrcSupervisor/Program.cs`.

Rust TUI changes:

- Replaced the single `120x36` full-layout gate with adaptive tiers:
  - full dashboard at `120x32` or larger
  - compact dashboard at `100x26` or larger
  - tiny resize fallback below compact size
- Added a useful compact dashboard with backend state, key supervisor statuses, six compact action rows, latest action/backend result, recent logs, and footer controls.
- Kept compact action rows backed only by existing `TuiAction` values; compact row clicks use the same action click-region model.
- Adjusted the full dashboard spacing so `130x34` fits the full dashboard path.
- Kept full action cards equal-height and consistently showing `click or press <number>` hints.
- Removed unintended background styling from normal action-card names and hints while preserving intentional badge backgrounds for `START`, `RUNNING`, `BACKEND OFF`, `BLOCKED`, and `UNAVAILABLE`.
- Added mouse wheel log scrolling: wheel up scrolls older and pauses live follow; wheel down scrolls newer and resumes follow when the latest entries are reached.
- Help overlay now consumes any mouse event and closes Help; the confirmation modal consumes mouse events and does not allow wheel scrolling through to logs.
- Preserved Phase 17D log-follow semantics: live by default, `Up`/`PageUp` pause, `Down`/`PageDown` move newer, and `End`/`F` resume latest.

Documentation changes:

- Updated README and Ratatui docs to document adaptive layout tiers, compact dashboard behavior, cleaner action-card text, consistent full-card hints, and mouse wheel log scrolling.
- Updated the action safety design to record Phase 17E as UI-only with no backend, protocol, allowlist, SteamVR host, classic console, Configurator, or Phase 16B guard changes.

Files changed:

- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17E diff.
- Source inspection confirmed `bridge.rs` still has no generic arbitrary command executor and sends `action-json` only through `execute_tui_action(TuiAction)`.
- Source inspection confirmed the TUI sends no legacy action command strings directly, `force-stop-supervisor` remains unexposed, and `Q`/`q` sends no backend command.

Release/copy result:

- Rebuilt `PimaxVrcSupervisorTui.exe` was copied to `release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe`.
- No C# publish was performed because no C# files changed.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime visual/mouse testing was not performed during implementation.
- When safe, verify `130x34` uses the full dashboard, compact sizes show useful status/actions/logs, tiny sizes show the resize fallback, full cards show hints, normal action-card text has no background underlay, and mouse wheel scrolling integrates with log follow mode.

Short Phase 18 direction:

- Run a manual VR-session test matrix for the adaptive full/compact/tiny layouts, mouse wheel log scrolling, backend-off/recovery states, and action-card click behavior before adding more UI behavior.

### Phase 17 - Pimax-inspired TUI visibility and usability polish

Status: Completed

Summary:

- Improved the Rust desktop TUI visibility and operator usability with a Pimax-inspired dark/green terminal theme.
- Added visual polish only; no backend action behavior, backend allowlist, SteamVR host behavior, classic console behavior, Phase 16B base-station guard behavior, or action safety semantics changed.
- Did not copy Pimax assets, logos, images, icons, or proprietary UI resources.

Rust TUI changes:

- Added `PimaxVrcSupervisor.Tui/src/theme.rs` and registered it with `mod theme;`.
- Added a centralized Ratatui RGB palette, shared panel blocks, badge styling, and rounded bordered panels.
- Reworked the dashboard into a clearer top header, supervisor status card, six action cards, action status area, quieter backend/errors panel, logs panel, footer, Help overlay, confirmation modal, and small-terminal fallback.
- Action cards show the existing six `TuiAction` entries with `READY`, `RUNNING`, `BLOCKED`, or `UNAVAILABLE` display states derived from existing metadata and running-action state.
- Added `TuiAction` display helpers for all actions, number keys, and short labels.
- Kept input behavior unchanged: `0`/`H` help, `F5` refresh, `1`-`6` confirmation, `Q` quits only the TUI, Help consumes any key, and confirmation owns input.
- Kept bridge behavior unchanged: read-only `query-json` polling and allowlisted `action-json` through `execute_tui_action(TuiAction)` only.

Documentation changes:

- Updated README and Ratatui docs to record the Pimax-inspired theme, improved action cards/status badges, stronger confirmation/help overlays, improved running-action/latest-result display, cleaner logs, and small-terminal fallback.
- Updated the action safety design to record Phase 17 as presentation-only.

Files changed:

- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/models.rs`
- `PimaxVrcSupervisor.Tui/src/theme.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed the TUI still sends `action-json` only through `execute_tui_action(TuiAction)` and has no generic arbitrary command executor.
- Source inspection confirmed the TUI sends no legacy action command strings directly.
- Source inspection confirmed the backend `action-json` allowlist remains unchanged and `force-stop-supervisor` remains blocked/not TUI-executable.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` was not modified in Phase 17.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime visual testing was not performed during implementation.
- Real-world VR testing remains the next recommended step after this UI pass.

### Phase 17B - TUI operator usability and mouse interaction polish

Status: Completed

Summary:

- Reduced normal operator UI noise after the Phase 17 visual polish.
- Added original mouse-click support for action cards and basic controls.
- Kept backend behavior, backend allowlist, bridge protocol, SteamVR host behavior, classic console behavior, Configurator behavior, Phase 16B base-station guard behavior, cleanup/lifecycle behavior, and action safety semantics unchanged.
- Did not copy ShockingVRC/GPL code, function names, layout implementation, event-loop code, theme code, assets, or text.
- Did not add new crates.

Rust TUI changes:

- Added an original click-region model using `ClickAction` and `ClickRegion`.
- Rebuilt click regions during each render frame.
- Enabled mouse capture with Crossterm; if mouse capture setup fails, the TUI continues keyboard-only and shows a nonfatal mouse status message.
- Terminal cleanup disables mouse capture together with raw mode and alternate-screen cleanup.
- Left-clicking action cards opens the existing confirmation modal only.
- Help consumes mouse clicks the same way it consumes key presses: any click closes Help and does not trigger dashboard controls underneath.
- Confirmation modal click handling is limited to Confirm and Cancel regions; clicks outside the modal are ignored.
- Mouse Confirm mirrors `Enter`; mouse Cancel mirrors `Esc`.
- Removed routine risk-category wording from normal dashboard and confirmation modal text.
- Changed ready action cards to use muted borders with green `READY` badges, while running/blocked/unavailable states carry stronger emphasis.
- Improved status label/badge/value alignment, action card alignment, the quieter `System` panel, action status layout, logs title/hint, and footer labels.

Files changed:

- `PimaxVrcSupervisor.Tui/src/app.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/models.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- Initial Rust debug build caught an owned-string lifetime issue in the action-card header helper; that was fixed.
- Intermediate Rust builds succeeded with two dead-code warnings; obsolete helper methods were removed.
- Final `cargo fmt`, Rust debug build, and Rust release build completed successfully.
- All three C# release builds completed successfully with 0 warnings and 0 errors.
- Source inspection confirmed the TUI still sends `action-json` only through `execute_tui_action(TuiAction)` and has no generic arbitrary command executor.
- Source inspection confirmed the TUI sends no legacy action command strings directly.
- Source inspection confirmed `PimaxVrcSupervisor/Program.cs` has no Phase 17B diff.
- Source inspection confirmed `force-stop-supervisor` remains blocked/not TUI-executable and `Q`/`q` sends no backend command.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime mouse testing was not performed during implementation.
- When safe, verify action-card clicks open confirmation only, modal Confirm/Cancel clicks mirror keyboard behavior, Help clicks close Help only, and dashboard clicks do not pass through overlays.

Short Phase 18 direction:

- Run a manual VR-session test matrix for the polished TUI, concurrent actions, duplicate blocking, backend/TUI Base Stations On/Off mutual exclusion, and duplicate Autostart validation/runtime skipping.

### Phase 15C - TUI action parity runtime UX fixes

Status: Completed

Summary:

- Fixed runtime UX issues found after Phase 15 parity testing.
- Kept the backend action allowlist unchanged at the six regular classic-console parity actions.
- Kept `force-stop-supervisor` blocked and not TUI-executable.
- Kept classic console behavior, SteamVR overlay behavior, cleanup/lifecycle behavior, Configurator behavior, and config semantics unchanged.
- Did not perform release-preparation work.

Rust TUI changes:

- Kept read-only `query-json` requests on the existing short `1s` read/write timeout.
- Added a separate `30s` read/write timeout for confirmed `action-json` requests, used only through `execute_tui_action(TuiAction)`.
- Added `0` as the primary layout-independent Help shortcut.
- Kept `H`/`h` as English-layout Help aliases.
- Kept `F1`, `?`, and Russian Help aliases unmapped.
- Changed Help overlay input so any key press closes Help and consumes that key without triggering dashboard actions underneath.
- Updated footer/header text to show direct action mappings on wide terminals: `0 Help`, `F5 Refresh`, `1 Core`, `2 OGB`, `3 BS On`, `4 BS Off`, `5 OSC`, `6 Autostart`, and `Q Quit TUI`.
- Kept confirmation modal behavior safe: `Enter`/`Y` confirm, `Esc`/`Q`/`N` cancel, and number/help/dashboard keys do not switch or execute actions inside the modal.
- Cleaned the Backend / Errors panel so action safety, backend state, latest error, and latest action result appear once each.

Documentation changes:

- Updated README and Ratatui docs to document `0 Help`, `H` as an English-layout alias, Help closing on any key press, direct footer mappings, `Q Quit TUI`, and the separate action timeout.
- Updated action safety design to record that Phase 15C does not expand the backend allowlist.
- Updated existing C# informational text to point users at `0 help`, `1-6 actions`, and `Q quit TUI`; classic console `1`-`6`/`F1` behavior remains unchanged.

Files changed:

- `PimaxVrcSupervisor/Program.cs`
- `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `PimaxVrcSupervisor.Tui/src/bridge.rs`
- `PimaxVrcSupervisor.Tui/src/main.rs`
- `PimaxVrcSupervisor.Tui/src/ui.rs`
- `README.md`
- `docs/ratatui-action-execution-design.md`
- `docs/ratatui-tui.md`
- `docs/ratatui-tui-migration-progress.md`

Build/test commands run:

- `cargo fmt --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml`
- `cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release`
- `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release`
- `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release`

Build/test result:

- `cargo fmt` completed successfully.
- Rust debug and release builds completed successfully.
- All three explicit C# Release builds completed successfully with 0 warnings and 0 errors.
- Bridge/source inspection confirmed the TUI sends `action-json` only through `execute_tui_action(TuiAction)`, has no generic arbitrary command executor, and sends no legacy action command strings directly.
- Backend inspection confirmed the `action-json` allowlist remains the six Phase 15 classic-console parity actions and `force-stop-supervisor` remains blocked/not TUI-executable.

Generated output status:

- `git status --short release` produced no staged/tracked release output.
- `git status --ignored --short release` reported `!! release/`.
- `git status --ignored --short PimaxVrcSupervisor.Tui/target` reported `!! PimaxVrcSupervisor.Tui/target/`.
- Generated `release/` and Rust `target/` output remain ignored and were not staged.

Runtime testing:

- Runtime shortcut/action testing was not performed during implementation.
- If testing from `release\PimaxVrcSupervisor-v1.3.0-test`, copy the updated release TUI binary first; if it is locked, document the copy failure and do not kill the process automatically.

Short Phase 16 direction:

- Build and run a manual/runtime shortcut test matrix across keyboard layouts.
- Continue polishing action confirmation safety and failure paths without expanding beyond the Phase 15 parity action set.
