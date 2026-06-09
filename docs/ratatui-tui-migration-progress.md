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

## Next Prompt Handling

Full phase prompts are prepared manually outside this file and pasted into Codex when needed.

Short Phase 7 direction:

- Prepare launch and integration documentation for `PimaxVrcSupervisorTui`.
- Add README guidance and optionally a simple local launcher script.
- Consider future Configurator naming for `Desktop console mode`: `Classic console`, `Modern console`, `Hidden`.
- Continue avoiding action command execution until confirmation handling is designed.
