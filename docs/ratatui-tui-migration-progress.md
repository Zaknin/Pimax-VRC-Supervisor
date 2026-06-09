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

The current SteamVR host already uses a local loopback TCP command bridge for dashboard commands. In the near term, this bridge should be extended additively with structured JSON commands such as status-json, commands-json, and log-json. Existing one-line text commands must remain compatible. A future Windows named-pipe transport can be considered later after the protocol is stable and the desktop Ratatui client requirements are clearer.

## Recommended Migration Roadmap

Recommended Migration Roadmap
Phase 0 - Repository inspection and architecture documentation

Status: Completed

Phase 1 - Structured status snapshot and v1.3.0-test publish baseline

Status: Completed

Phase 2 - Command metadata/capabilities and structured DTO baseline

Status: Completed

Phase 3 - Structured recent log DTOs and read-only log-json surface

Status: Not started

Phase 4 - Read-only JSON request envelope for safe read-only commands

Status: Not started

Phase 5 - Minimal Rust Ratatui desktop frontend

Status: Not started

Phase 6 - Full desktop Ratatui dashboard screens

Status: Not started

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

## Next Prompt Handling

Full phase prompts are prepared manually outside this file and pasted into Codex when needed.