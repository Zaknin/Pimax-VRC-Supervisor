# Desktop TUI

`PimaxVrcSupervisorTui.exe` is a separate Rust/Ratatui desktop terminal UI for monitoring a running Pimax VRC Supervisor backend and confirming regular classic-console actions.

The TUI is part of the `cli-ui2` / `1.3.0-test` migration work. It is not a replacement for the SteamVR dashboard overlay or the classic supervisor console.

## Purpose

The TUI gives a desktop/operator view of the supervisor with tightly limited control behavior. It displays:

- supervisor status
- command capability metadata
- recent console-log lines
- disconnected/backend unavailable state
- validated classic-console actions in the same `1`-`6` order

The TUI can be closed without stopping the supervisor.

## Architecture

The TUI connects to the existing loopback TCP command bridge at `127.0.0.1:37957`.

It uses the read-only JSON request envelope:

```text
query-json {"resource":"status"}
query-json {"resource":"commands"}
query-json {"resource":"log","maxLines":80}
```

For confirmed actions, it uses the structured backend action envelope with canonical lowercase command names:

```text
action-json {"command":"restart-core-apps","confirmed":true}
action-json {"command":"start-osc-goes-brrr","confirmed":true}
action-json {"command":"base-stations-on","confirmed":true}
action-json {"command":"base-stations-off","confirmed":true}
action-json {"command":"restart-osc-router","confirmed":true}
action-json {"command":"reload-autostart-apps","confirmed":true}
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

- `0`: help
- `H` / `h`: help alias on English keyboard layouts
- `F5`: refresh
- `1`: open Restart Core Apps confirmation
- `2`: open Start OSCGoesBrrr confirmation
- `3`: open Base Stations On confirmation
- `4`: open Base Stations Off confirmation
- `5`: open Restart OSC Router confirmation
- `6`: open Reload Autostart Apps confirmation
- `Enter` / `Space`: confirm inside the confirmation modal
- `Esc`: close Help, cancel confirmation, or quit the TUI
- `Up` / `Down`: scroll logs
- `PageUp` / `PageDown`: scroll logs by page
- Mouse wheel: scroll logs
- `Home`: jump to older logs
- `End` / `F`: resume latest log follow
- `Q` / `q`: quit only the Rust TUI from the dashboard; close Help in the Help overlay

Convenience aliases:

- `R` / `r`: refresh
- `F` / `f`: resume latest log follow

The footer lists direct action mappings on wide terminals: `0 Help`, `F5 Refresh`, `1 Core`, `2 OGB`, `3 BS On`, `4 BS Off`, `5 OSC`, `6 Autostart`, and `Q Quit TUI`. Compact terminals may use the shorter `1-6 Actions` label.

Help closes on any key press and consumes that key. For example, pressing `1`, `Enter`, `Q`, or `F5` while Help is visible closes Help only and does not trigger the dashboard action underneath.

Letter shortcuts are displayed uppercase, but lowercase input is also accepted. `F1`, `?`, and Russian help aliases do not open TUI help. Selected Russian-layout aliases remain limited to non-help refresh and quit keys, but are not listed in the main Help overlay.

Simple Russian-layout aliases are accepted for the same physical keys where terminal input provides them. Other layouts and IMEs should use the primary number/function/Enter/Esc shortcuts.

## Background Actions

Confirmed actions run in background worker threads. The confirmation modal closes immediately after `Enter` or `Space`, the running action appears in the action status panel, and the TUI remains responsive for log scrolling, Help, refresh, quit, and other safe actions.

The TUI tracks running actions by canonical backend command name:

- The same command cannot be started twice while it is already running.
- `base-stations-on` and `base-stations-off` cannot run at the same time; the supervisor backend also rejects overlapping manual base-station power actions from other entry points.
- Other different actions may run concurrently.

If `Q` is pressed while actions are running, only the Rust TUI exits. It does not cancel backend work, stop the supervisor, send `force-stop-supervisor`, or run cleanup routines. Pending action results may be lost after the TUI exits.

## Visual Design

Phase 17 gives the TUI a Pimax-inspired dark terminal theme with green healthy/active accents, orange warning/off states, red errors, grey borders, and card-like panels. The visual language is inspired by the official Pimax desktop app, but no Pimax assets, logos, images, icons, or proprietary resources are copied.

The dashboard now emphasizes a top backend status bar, a supervisor status card, action cards for the six confirmed actions, running-action/latest-result panels, a quieter backend/errors panel, clearer logs, a stronger confirmation modal, and an accurate small-terminal fallback. The visual polish does not change backend behavior, action allowlists, SteamVR overlay behavior, classic console behavior, or action safety semantics.

Phase 17B reduces normal operator UI noise by hiding risk-category wording from the dashboard and confirmation modal while keeping safety enforcement internal. Status rows and action cards use more stable alignment.

Phase 17B also adds original mouse-click support. Clicking Confirm in the modal behaves like `Enter`, clicking Cancel behaves like `Esc`, and clicks while Help is visible close Help only. Clicks outside a confirmation modal are ignored so dashboard controls underneath cannot fire. If mouse capture is unavailable, the TUI continues keyboard-only.

Phase 17C refines action-card and backend-unavailable behavior. Keyboard `1`-`6` still opens confirmation, while mouse clicks on action cards start the selected allowed action immediately after the same backend, metadata, duplicate-command, and Base Stations On/Off conflict checks. Ready cards show `START`; disconnected cards show `BACKEND OFF` with muted borders and cannot start workers, even when cached command metadata exists from an earlier connection. The Core Apps status row shows `WAITING` while lifecycle is `waiting-vrchat` instead of warning that helper apps are incomplete.

Phase 17D tightens the same operator layout. Backend-off state is authoritative across all six action cards, so disconnected cards never mix in conflict `BLOCKED` states. Modal controls remain mouse-clickable but render as neutral text. Recent Logs follow the newest entries by default, scrolling older pauses live follow, and `End` or `F` resumes latest log follow.

Phase 17E adds adaptive layout tiers. Terminals at `120x32` or larger use the full dashboard; terminals at `100x26` or larger use a useful compact dashboard with backend state, key statuses, six compact action rows, latest action result, recent logs, and footer controls; smaller terminals show a tiny resize fallback. Full action cards keep equal-height hints, normal card text uses foreground-only styling while badges keep intentional backgrounds, and mouse wheel scrolling integrates with log follow mode.

Phase 17F adds a priority small layout before the tiny fallback. Terminals at `80x20` or larger keep backend health, lifecycle, Core Apps, OSC Router, Base Stations, all six action states, latest activity, and a last-log line visible. Tiny fallback is now reserved for terminals below `80x20`.

Phase 17F also tightens badge styling. Short state words such as `OK`, `START`, `WAITING`, `RUNNING`, `OFF`, `STOPPED`, `ERROR`, `BACKEND OFF`, `BLOCKED`, and `UNAVAILABLE` use colored badge backgrounds. Normal labels, values, action names, click hints, modal body text, and log lines remain foreground-only without background underlay.

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

- Only the six audited regular classic-console actions are executable from the TUI.
- Keyboard actions require explicit confirmation; mouse action-card clicks start immediately after the same validation and use backend `action-json`.
- Read-only `query-json` polling keeps short timeouts; confirmed `action-json` requests use a separate longer timeout so successful backend work is not reported as a short polling timeout.
- Confirmed actions run in the background; duplicate commands are blocked in the TUI, and Base Stations On/Off overlap is blocked by both the TUI and supervisor backend.
- Recent Logs use live follow mode by default; manual upward scrolling pauses follow until `End` or `F` resumes it.
- Mouse wheel scrolling uses the same log follow model: wheel up scrolls older and pauses follow, while wheel down scrolls newer and resumes live follow at the latest entry.
- No legacy action commands are sent by the TUI.
- `force-stop-supervisor` remains blocked and is not exposed.
- No backend auto-start.
- No named-pipe IPC.
- No streaming events.
- No filesystem diagnostic log browsing.
- No Configurator setting for desktop console mode yet.

## Action Safety Design

Action execution is intentionally allowlisted. The current TUI exposes only regular classic-console actions through backend `action-json`; keyboard starts are confirmation-gated, mouse card starts are directly validated, and the TUI does not execute legacy bridge action commands.

The safety and confirmation model for future action execution is documented separately in [TUI Action Execution Safety Design](ratatui-action-execution-design.md).

Phase 9 starts backend-only structured `action-json` support for the `restart-osc-router` allowlist entry. The desktop TUI still does not expose action execution, action buttons, action keybindings, or confirmation UI.

Phase 10 displays backend action metadata in the command capability panel for planning and review. The metadata is informational only: the TUI still sends only read-only `query-json` requests and does not call `action-json` or legacy action commands.

Phase 11 enables the first controlled TUI action for `restart-osc-router` only. Pressing `o` opens a confirmation modal; only `y` inside that modal sends `action-json {"command":"restart-osc-router","confirmed":true}`. All other actions remain unavailable, and `force-stop-supervisor` remains blocked.

Phase 12 hardens overlay input handling and action result display. The TUI ignores repeated/released key events, confirmation input takes priority over help and dashboard input, help closes before `q` quits, duplicate action attempts are rejected while an action is in progress, and the latest action result is shown in the backend/status area.

Phase 13 made layout-independent shortcuts primary with `F1` help. Phase 14 changed TUI help to `H` only and removed `F1`, `?`, and Russian help aliases. Phase 14B tried debounce tuning, but current behavior restores immediate `H`/`h` Help toggling while still ignoring repeat/release events. The classic console keeps its existing `1`-`6` and `F1` hotkeys.

Phase 15 adds classic-console action parity for regular operator actions. Numbers `1`-`6` open confirmation modals in the same order as the classic console, `Enter` confirms, `Esc` cancels, and `force-stop-supervisor` remains blocked.

Phase 15C fixes runtime UX issues from parity testing. `0` is now primary Help, `H` remains an English-layout alias, Help closes on any key press and consumes that key, the footer lists direct `1`-`6` action mappings on wide terminals, dashboard `Q` quits only the Rust TUI, and confirmed actions use a separate 30 second response timeout.

Phase 16 moves confirmed TUI actions into background workers and allows safe concurrent actions. It blocks duplicate same-command starts and Base Stations On/Off overlap only. It also adds Configurator validation that refuses core app executables in Autostart apps, plus supervisor runtime protection that warns and skips manually configured duplicate Autostart entries.

Phase 16B adds the matching backend-local manual base-station action guard, so Base Stations On/Off overlap is rejected by the supervisor even when the request comes from classic console input, a legacy bridge client, structured `action-json`, or a future UI client.

Phase 17 improves visibility and operator usability with a Pimax-inspired dark/green theme, action cards, status badges, clearer running-action/latest-result panels, improved Help and confirmation overlays, less noisy logs, and a small-terminal fallback. It is a UI-only pass and does not change backend action behavior.

Phase 17B adds original mouse-click support and further reduces operator-screen noise. The dashboard no longer shows routine risk-category wording, and the confirmation modal focuses on action/effect/command.

Phase 17C makes mouse action-card clicks direct-start actions after validation, while keyboard shortcuts remain confirmation-gated. It also makes backend-off action cards consistently disabled and display-only, simplifies modal controls to `Enter`/`Space` confirm and `Esc` cancel, and adds the display-only Core Apps waiting state for `waiting-vrchat`.

Phase 17D makes backend-off card rendering authoritative, neutralizes modal control styling, makes full-layout card hints consistent, and adds Recent Logs live-follow mode.

Phase 17E adds adaptive full/compact/tiny layout tiers, cleans normal action-card text styling, keeps full-card action hints consistent, and adds mouse wheel log scrolling without backend or protocol changes.

Phase 17F extends the tiers to full/compact/small/tiny, makes backend-off state the first action-card and action-row priority in every layout, and centralizes badge-only background styling. It borrows only the adaptive resizing principle from ShockingVRC; no code, structure, assets, or GPL implementation are copied.

## Future Direction

Future Configurator naming should use:

- setting: `Desktop console mode`
- options: `Classic console`, `Modern console`, `Hidden`

Meanings:

- `Classic console`: current visible console UI and hotkeys.
- `Modern console`: future Ratatui desktop terminal UI.
- `Hidden`: backend/no-console mode for advanced/startup use.

This setting must not replace or disable the SteamVR overlay dashboard. It is documented here for planning only and is not implemented in Phase 7.
