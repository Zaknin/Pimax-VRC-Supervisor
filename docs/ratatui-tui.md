# Read-only Desktop TUI

`PimaxVrcSupervisorTui.exe` is a separate Rust/Ratatui desktop terminal UI for monitoring a running Pimax VRC Supervisor backend.

The TUI is part of the `cli-ui2` / `1.3.0-test` migration work. It is not a replacement for the SteamVR dashboard overlay or the classic supervisor console.

## Purpose

The TUI gives a desktop/operator view of the supervisor without adding backend control behavior. It displays:

- supervisor status
- command capability metadata
- recent console-log lines
- disconnected/backend unavailable state

The TUI can be closed without stopping the supervisor.

## Architecture

The TUI connects to the existing loopback TCP command bridge at `127.0.0.1:37957`.

It uses the read-only JSON request envelope:

```text
query-json {"resource":"status"}
query-json {"resource":"commands"}
query-json {"resource":"log","maxLines":80}
```

The C# supervisor remains the backend and keeps ownership of Windows, VR, SteamVR, VRChat, cleanup, monitor, OSC, base-station, scheduled-task, and SteamVR manifest behavior.

The SteamVR overlay remains unchanged. The TUI does not replace VR status/log rendering, dashboard button layout, texture rendering, or OpenVR/D3D paths.

## Launch Requirements

The supervisor backend must already be running for live data. The TUI does not start the supervisor, does not elevate, and does not start SteamVR or VRChat.

From a release folder that contains `PimaxVrcSupervisorTui.exe`:

```powershell
.\PimaxVrcSupervisorTui.exe
```

If the backend is not running, the TUI shows a disconnected/backend unavailable state and keeps retrying on periodic or manual refresh.

During the migration work, a manual runtime check confirmed the TUI can connect to a running supervisor at `127.0.0.1:37957` and display status, command capabilities, and logs. Documentation-only phases do not start the supervisor just to retest this.

## Keybindings

- `r`: refresh
- `h` or `?`: help
- `Up` / `Down`: scroll logs
- `PageUp` / `PageDown`: scroll logs by page
- `Home` / `End`: jump logs
- `q`: quit
- `Esc`: close help or quit

## Build

Windows builds require the Rust stable MSVC toolchain. Visual Studio Build Tools with the C++ workload may be required.

Build from the repository root:

```powershell
cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml
cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release
```

After the C# publish and Rust release build, copy the local test binary:

```powershell
Copy-Item .\PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe `
  .\release\PimaxVrcSupervisor-v1.3.0-test\PimaxVrcSupervisorTui.exe `
  -Force
```

Do not commit generated `target/` or `release/` output. Keep `PimaxVrcSupervisor.Tui/Cargo.lock` committed because the TUI is an application binary crate.

## Current Limitations

- Read-only only.
- No action command execution.
- No confirmation handling.
- No backend auto-start.
- No named-pipe IPC.
- No streaming events.
- No filesystem diagnostic log browsing.
- No Configurator setting for desktop console mode yet.

## Action Safety Design

Action execution is planned but not implemented. The current TUI remains read-only and does not execute legacy bridge action commands.

The safety and confirmation model for future action execution is documented separately in [TUI Action Execution Safety Design](ratatui-action-execution-design.md).

## Future Direction

Future Configurator naming should use:

- setting: `Desktop console mode`
- options: `Classic console`, `Modern console`, `Hidden`

Meanings:

- `Classic console`: current visible console UI and hotkeys.
- `Modern console`: future Ratatui desktop terminal UI.
- `Hidden`: backend/no-console mode for advanced/startup use.

This setting must not replace or disable the SteamVR overlay dashboard. It is documented here for planning only and is not implemented in Phase 7.
