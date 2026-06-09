# Desktop TUI

`PimaxVrcSupervisorTui.exe` is a separate Rust/Ratatui desktop terminal UI for monitoring a running Pimax VRC Supervisor backend and, starting in Phase 11, confirming one narrow OSC router restart action.

The TUI is part of the `cli-ui2` / `1.3.0-test` migration work. It is not a replacement for the SteamVR dashboard overlay or the classic supervisor console.

## Purpose

The TUI gives a desktop/operator view of the supervisor with tightly limited control behavior. It displays:

- supervisor status
- command capability metadata
- recent console-log lines
- disconnected/backend unavailable state
- a confirmation-gated OSC router restart action

The TUI can be closed without stopping the supervisor.

## Architecture

The TUI connects to the existing loopback TCP command bridge at `127.0.0.1:37957`.

It uses the read-only JSON request envelope:

```text
query-json {"resource":"status"}
query-json {"resource":"commands"}
query-json {"resource":"log","maxLines":80}
```

For the single Phase 11 action, it uses the structured backend action envelope:

```text
action-json {"command":"restart-osc-router","confirmed":true}
```

No legacy action command strings are sent by the TUI.

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

Primary shortcuts:

- `H` / `h`: help
- `F5`: refresh
- `1`: open Restart OSC Router confirmation
- `Enter`: confirm inside the confirmation modal
- `Esc`: close help, cancel confirmation, or quit
- `Up` / `Down`: scroll logs
- `PageUp` / `PageDown`: scroll logs by page
- `Home` / `End`: jump logs
- `Q` / `q`: close help first, otherwise quit; cancel confirmation when confirmation is open

Convenience aliases:

- `R` / `r`: refresh
- `O` / `o`: open Restart OSC Router confirmation
- `Y` / `y`: confirm inside the confirmation modal
- `N` / `n`: cancel inside the confirmation modal

Letter shortcuts are displayed uppercase, but lowercase input is also accepted. `F1`, `?`, and Russian help aliases do not open TUI help. Selected Russian-layout aliases remain limited to non-help refresh, restart, quit, confirm, and cancel keys, but are not listed in the main Help overlay.

Simple Russian-layout aliases are accepted for the same physical keys where terminal input provides them. Other layouts and IMEs should use the primary number/function/Enter/Esc shortcuts.

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

- Only `restart-osc-router` is executable from the TUI.
- `restart-osc-router` requires explicit confirmation and uses backend `action-json`.
- No legacy action commands are sent by the TUI.
- No base-station, core-app restart, OSCGoesBrrr startup, or force-stop actions are exposed.
- No backend auto-start.
- No named-pipe IPC.
- No streaming events.
- No filesystem diagnostic log browsing.
- No Configurator setting for desktop console mode yet.

## Action Safety Design

Action execution is intentionally narrow. The current TUI exposes only a confirmation-gated `restart-osc-router` action through backend `action-json` and does not execute legacy bridge action commands.

The safety and confirmation model for future action execution is documented separately in [TUI Action Execution Safety Design](ratatui-action-execution-design.md).

Phase 9 starts backend-only structured `action-json` support for the `restart-osc-router` allowlist entry. The desktop TUI still does not expose action execution, action buttons, action keybindings, or confirmation UI.

Phase 10 displays backend action metadata in the command capability panel for planning and review. The metadata is informational only: the TUI still sends only read-only `query-json` requests and does not call `action-json` or legacy action commands.

Phase 11 enables the first controlled TUI action for `restart-osc-router` only. Pressing `o` opens a confirmation modal; only `y` inside that modal sends `action-json {"command":"restart-osc-router","confirmed":true}`. All other actions remain unavailable, and `force-stop-supervisor` remains blocked.

Phase 12 hardens overlay input handling and action result display. The TUI ignores repeated/released key events, confirmation input takes priority over help and dashboard input, help closes before `q` quits, duplicate action attempts are rejected while an action is in progress, and the latest action result is shown in the backend/status area.

Phase 13 made layout-independent shortcuts primary with `F1` help. Phase 14 changed TUI help to `H` only and removed `F1`, `?`, and Russian help aliases. Phase 14B tried debounce tuning, but current behavior restores immediate `H`/`h` Help toggling while still ignoring repeat/release events. The classic console keeps its existing `1`-`6` and `F1` hotkeys.

## Future Direction

Future Configurator naming should use:

- setting: `Desktop console mode`
- options: `Classic console`, `Modern console`, `Hidden`

Meanings:

- `Classic console`: current visible console UI and hotkeys.
- `Modern console`: future Ratatui desktop terminal UI.
- `Hidden`: backend/no-console mode for advanced/startup use.

This setting must not replace or disable the SteamVR overlay dashboard. It is documented here for planning only and is not implemented in Phase 7.
