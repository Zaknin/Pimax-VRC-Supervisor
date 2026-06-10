# TUI Action Execution Safety Design

This document defines the safety model for desktop Ratatui TUI action execution. Phase 15 enables confirmed action parity for the regular classic-console actions while keeping `force-stop-supervisor` blocked.

Lifecycle, Configurator launch, tray/minimize, and graceful shutdown planning are tracked separately in [TUI Lifecycle And Configurator Integration Design](phase-18-tui-lifecycle-configurator-design.md).

## Current State

The desktop TUI primarily monitors the supervisor. It uses the existing loopback TCP bridge at `127.0.0.1:37957` and sends read-only `query-json` requests:

```text
query-json {"resource":"status"}
query-json {"resource":"commands"}
query-json {"resource":"log","maxLines":80}
```

The backend also accepts legacy string action commands on the same bridge. The SteamVR dashboard host uses the legacy command protocol and must not be forced to change. The Ratatui TUI does not send legacy action commands.

Important metadata distinction: `commands-json` currently uses `available=true` to mean "accepted by the current backend bridge." It does not mean a command is safe, supported by future JSON action execution, or executable from the desktop TUI.

## Phase 9 Implementation Status

Phase 9 adds backend-only structured `action-json` support for one allowlisted command: `restart-osc-router`.

The desktop TUI remains read-only. No TUI action buttons, action keybindings, or confirmation modal are implemented. The SteamVR host remains on the legacy command protocol.

Phase 15 expands the allowlist to regular audited classic-console actions. `force-stop-supervisor` remains blocked from the structured desktop TUI action flow.

## Phase 10 Implementation Status

Phase 10 displays action metadata from `commands-json` in the read-only desktop TUI. The TUI shows action safety category, backend action support, TUI-disabled state, confirmation metadata, danger markers, and blocked/deferred reasons as informational data only.

The TUI still sends only read-only `query-json` requests for `status`, `commands`, and `log`. It does not call `action-json`, does not call legacy action commands, and does not expose action buttons, action keybindings, selection, or confirmation UI.

## Phase 11 Implementation Status

Phase 11 enables one confirmation-gated desktop TUI action: `restart-osc-router`.

The TUI opens confirmation with `o` only when backend metadata reports `actionSupported=true`, `tuiExecutable=true`, and `requiresConfirmation=true` for `restart-osc-router`. Only `y` inside the confirmation modal sends `action-json {"command":"restart-osc-router","confirmed":true}`.

No generic TUI action executor is added. The TUI does not send legacy action commands. `force-stop-supervisor`, base-station actions, core-app restart, and OSCGoesBrrr startup remain unavailable from the TUI.

## Phase 12 Implementation Status

Phase 12 hardens the Phase 11 action UX without adding actions or changing the backend allowlist.

The TUI processes only key press events and ignores key repeat/release events, which stabilizes help overlay toggling. Input priority is explicit: confirmation modal first, help overlay second, normal dashboard last. Confirmation consumes input while visible, so help and dashboard actions cannot leak through it.

The TUI tracks action in-progress state and rejects duplicate action attempts with `Action already in progress.` The latest action success, failure, cancellation, or rejection is shown in the backend/status area with command, outcome, relative time, and message.

## Phase 13/14 Implementation Status

Phase 13 made layout-independent keys the primary desktop TUI shortcuts. Phase 14 changes help to `H` only and removes `F1`, `?`, and Russian help aliases. Phase 14B debounce tuning was removed after runtime feedback; current behavior toggles Help immediately on `H`/`h` while still ignoring repeat/release events.

The classic console behavior remains unchanged and continues to use `1`-`6` plus `F1`.

## Phase 15 Implementation Status

Phase 15 mirrors the regular classic-console action order in the desktop TUI. Number keys `1`-`6` open confirmation for the canonical lowercase backend commands `restart-core-apps`, `start-osc-goes-brrr`, `base-stations-on`, `base-stations-off`, `restart-osc-router`, and `reload-autostart-apps`.

Every TUI action requires confirmation and sends `action-json` with `confirmed=true`. The TUI uses a local allowlist enum and does not send legacy action command strings. `force-stop-supervisor` remains blocked and is not exposed.

## Phase 15C Implementation Status

Phase 15C keeps the Phase 15 backend allowlist unchanged and fixes desktop TUI runtime UX. Read-only `query-json` polling keeps its short timeout, while confirmed `action-json` requests use a separate 30 second response timeout so longer successful actions are not shown as short polling timeouts.

`0` is the primary Help shortcut because it is layout-independent in the terminal. `H`/`h` remain English-layout aliases. `F1`, `?`, and Russian Help aliases remain unmapped. Help closes on any key press and consumes that key.

## Phase 16 Implementation Status

Phase 16 runs confirmed TUI actions in background workers while keeping the same six-command backend allowlist. Actions are tracked by canonical command name. The TUI blocks duplicate same-command starts and blocks Base Stations On/Off overlap, but otherwise allows safe non-conflicting actions to run concurrently.

Action completion, timeout, bridge failure, or worker panic records a failed or successful result and removes the matching canonical command from the running list.

Phase 16 also protects against duplicated core app launches: the Configurator refuses to save Broken Eye or VRCFaceTracking as Autostart apps, and the supervisor warns and skips manually configured duplicate Autostart entries at runtime.

## Phase 16B Implementation Status

Phase 16B adds a backend-local manual base-station action guard. Base Stations On/Off overlap is now protected both by the TUI conflict model and by the supervisor backend itself, so classic console input, legacy bridge clients, structured `action-json`, and future clients cannot mutate base-station state concurrently.

## Phase 17 Implementation Status

Phase 17 is a desktop TUI presentation-only pass. It adds a Pimax-inspired dark/green terminal theme, action cards, status badges, clearer running-action/latest-result display, and stronger Help/confirmation panels without changing the backend action allowlist, `action-json` semantics, TUI shortcut behavior, SteamVR overlay behavior, classic console behavior, or the Phase 16B backend base-station guard.

## Phase 18C Lifecycle Boundary

Phase 18C adds confirmed supervisor shutdown from the TUI, but it is intentionally not a regular `action-json` action. Dashboard `Q` opens a shutdown confirmation and sends the dedicated lifecycle request `lifecycle-json {"action":"request-graceful-shutdown","source":"Desktop TUI"}`. The backend runs the Ctrl+C-equivalent cleanup path and the TUI exits after shutdown is accepted and the backend exits/disconnects or times out.

The six regular `action-json` actions remain unchanged. `force-stop-supervisor` remains blocked and is not used by the shutdown flow.

## Phase 17B Implementation Status

Phase 17B keeps the same action safety model but removes routine risk-category wording from the normal operator UI. Mouse-click support is implemented with original project code and maps only to existing `TuiAction` values and overlay controls. Action-card clicks open confirmation only, modal Confirm/Cancel clicks mirror keyboard behavior, and no backend command is sent without the existing confirmation step.

## Phase 17C Implementation Status

Phase 17C keeps the backend action allowlist and bridge protocol unchanged, but refines desktop TUI interaction. Keyboard `1`-`6` actions remain confirmation-gated. Mouse action-card clicks start only existing `TuiAction` values immediately after the same backend connection, metadata, duplicate-command, and Base Stations On/Off conflict checks used by keyboard-confirmed actions.

The confirmation modal now documents only `Enter`/`Space` or mouse Confirm for execution and `Esc` or mouse Cancel for cancellation. Backend-unavailable state overrides cached command metadata before action cards are rendered or actions are started, so cards show disabled `BACKEND OFF` state and cannot spawn workers while disconnected.

## Phase 17D Implementation Status

Phase 17D keeps the same action allowlist, bridge protocol, and backend guardrails. It makes backend-off card state authoritative across all six actions, keeps backend-down rejections as `BACKEND OFF` instead of conflict `BLOCKED`, and leaves mouse actions limited to existing `TuiAction` enum values.

The modal controls remain clickable but render as neutral text. Recent Logs add live-follow mode; log navigation does not affect action safety or backend command execution.

## Phase 17E Implementation Status

Phase 17E remains UI-only. It adds adaptive full, compact, and tiny layout tiers, cleaner action-card foreground text, consistent full-card action hints, and mouse wheel log scrolling. Compact and full action regions still map only to existing `TuiAction` values, and wheel scrolling affects only the local log viewport/follow state. No backend allowlist, bridge protocol, SteamVR host, classic console, Configurator, or Phase 16B base-station guard behavior changed.

## Phase 17F Implementation Status

Phase 17F remains UI-only and keeps the same action safety model. It adds a small essential dashboard tier between compact and tiny fallback, preserves backend/supervisor health and action states longer during terminal resize, and centralizes badge-only state styling. Backend disconnected state is still the first action-state check for every full card, compact row, and small row, so cached metadata cannot make a disconnected action look startable or conflict-blocked.

The adaptive layout borrows only the principle of graceful resizing from ShockingVRC. No ShockingVRC code, UI structure, event code, assets, or GPL implementation are copied.

## Phase 17G Implementation Status

Phase 17G is a rendering-only polish pass. It aligns the small `80x20` action rows into a stable two-row, three-column grid while preserving the same `TuiAction` allowlist, badge styling, mouse behavior, keyboard confirmation behavior, and backend-off priority. It does not add supervisor shutdown, tray behavior, Configurator launch options, backend protocol changes, or new action semantics.

## Phase 17H Implementation Status

Phase 17H is also rendering-only. It changes status/action badges to bracket text such as `[START]`, `[OK]`, and `[BACKEND OFF]`, keeps small-layout badges close to their labels, and leaves modal controls neutral. It does not change `Q` semantics, supervisor lifecycle, backend action handling, bridge protocol, tray behavior, Configurator behavior, or action allowlists.

## Phase 17I Implementation Status

Phase 17I is rendering-only and corrects badge semantics without changing action safety. Normal status badges render as colored words without brackets, while brackets are reserved for interactive action buttons such as `[START]`. Non-startable action states remain unbracketed, compact and small action rows keep `[START]` close to the label, and no backend, bridge, lifecycle, shutdown, tray, Configurator, or action allowlist behavior changed.

## Phase 17J Implementation Status

Phase 17J is rendering-only and aligns the compact dashboard action rows with fixed label and badge columns. It preserves the Phase 17I badge rule: interactive `[START]` stays bracketed, non-startable action states and normal status badges remain unbracketed, and no backend, bridge protocol, lifecycle, shutdown, tray, Configurator, SteamVR host, classic console, or action allowlist behavior changed.

## Phase 17K Implementation Status

Phase 17K is rendering-only and fine-tunes compact/small action spacing. Compact rows keep aligned label and action-state columns without pushing display names too far right, and the small layout adds a fixed gutter between action cells while keeping `[START]` close to each label. It preserves the same `TuiAction` allowlist, badge rules, bridge behavior, `Q` semantics, and backend safety model.

## Phase 17L Implementation Status

Phase 17L is rendering/click-region-only. Full action cards remain whole-card clickable, while compact and small layouts register action click regions only over the visible `[START]` badge. Labels, descriptions, row backgrounds, gutters, and non-startable states are not action click targets. It preserves the same `TuiAction` allowlist, keyboard confirmation flow, bridge behavior, `Q` semantics, and backend safety model.

Phase 17L follow-up keeps compact actions as a one-column list and adds optional vertical row spacing only when the compact action panel has enough height. Small layout keeps the current 3-column action grid with no vertical gaps. Click behavior and backend safety are unchanged.

## Future Action Metadata

Future command metadata should add action-specific fields instead of overloading `available`:

| Field | Meaning |
| --- | --- |
| `actionSupported` | Backend has structured JSON action support for this command. |
| `actionSafetyCategory` | One of `ReadOnly`, `LowRisk`, `Disruptive`, `Dangerous`, or `Blocked`. |
| `tuiExecutable` | Desktop TUI may expose this command as executable. |
| `requiresConfirmation` | User confirmation is required before execution. |
| `blockedReason` | Human-readable reason a command is not executable from the TUI. |

The backend remains the final authority. TUI-side metadata is not sufficient by itself to authorize an action.

## Safety Categories

| Category | Confirmation policy | Meaning |
| --- | --- | --- |
| `ReadOnly` | None | Data retrieval only. |
| `LowRisk` | Simple confirmation | Local service or workflow nudge with limited blast radius. |
| `Disruptive` | Explicit confirmation | Restarts apps, launches workflows, or affects hardware/session state. |
| `Dangerous` | Stronger confirmation or admin-only future design | Can bypass safety routines or materially affect cleanup/lifecycle behavior. |
| `Blocked` | Not executable | Must not appear as executable in the desktop TUI. |

## Current Command Classification

| Command | Category | Future TUI execution | Reason |
| --- | --- | --- | --- |
| `status` | `ReadOnly` | no action surface | Legacy text status. |
| `status-json` | `ReadOnly` | no action surface | Structured status snapshot. |
| `commands-json` | `ReadOnly` | no action surface | Capability metadata. |
| `log` | `ReadOnly` | no action surface | Legacy recent log array for SteamVR dashboard. |
| `log-json` | `ReadOnly` | no action surface | Structured recent log snapshot. |
| `query-json` | `ReadOnly` | no action surface | Read-only request envelope. |
| `restart-core-apps` | `Disruptive` | enabled with confirmation | Restarts configured face-tracking apps during the session. |
| `start-osc-goes-brrr` | `Disruptive` | enabled with confirmation | May launch or repair Intiface/OscGoesBrrr workflow. |
| `base-stations-on` | `Disruptive` | enabled with confirmation | Powers configured base stations and touches hardware state. |
| `base-stations-off` | `Disruptive` | enabled with confirmation | Powers down configured base stations and can disrupt tracking. |
| `restart-osc-router` | `LowRisk` | enabled with confirmation | Restarts or manually starts OSC routing; does not power hardware or bypass cleanup. |
| `reload-autostart-apps` | `Disruptive` | enabled with confirmation | Reloads or starts configured Autostart apps. |
| `force-stop-supervisor` | `Blocked` | never by default | Legacy bridge command exists, but it hard-stops without cleanup routines. |

`force-stop-supervisor` is the key distinction between backend bridge availability and TUI executability: it may remain accepted by the legacy bridge, but the desktop TUI design classifies it as `Blocked`.

## Future TUI UX

The command list remains metadata-oriented; it is not a generic action picker.

For Phase 15 actions:

- Number keys `1`-`6` open confirmation only.
- Mouse action-card clicks start allowed actions directly after validation.
- No action runs from a single accidental keypress.
- `Enter` and `Space` confirm inside the confirmation modal.
- Confirmation closes immediately after confirmation; action execution continues in the background.
- Confirmation shows command name, expected effect, and backend-send note.
- `Esc` cancels confirmation.
- Number keys, `0`, `H`, `F1`, `?`, removed help aliases, and dashboard keys are ignored while confirmation is visible.
- Help closes on any key press and consumes the key instead of passing it through to dashboard shortcuts.
- Action results appear in the backend/status area.
- Duplicate same-command starts are rejected while that command is running.
- `base-stations-on` and `base-stations-off` are mutually exclusive while running; this is enforced in both the TUI and the supervisor backend.
- Blocked commands should not be executable and should explain why when selected or inspected.

## Future Backend Protocol

Phase 9 starts a structured `action-json` command for a tiny allowlist. Phase 15 expands the allowlist to audited regular classic-console actions. Do not repurpose `query-json`; it remains read-only.

Example request:

```json
{
  "requestId": "optional-id",
  "command": "restart-core-apps",
  "confirmed": true
}
```

Example response:

```json
{
  "timestamp": "...",
  "requestId": "optional-id",
  "command": "restart-core-apps",
  "success": true,
  "message": "...",
  "resultType": "action",
  "data": null,
  "error": null
}
```

The response reuses the existing `SupervisorCommandResult` shape where practical. Legacy string commands remain compatible, and the SteamVR host is not required to consume `action-json`.

## Backend Guardrails

The backend must:

- validate an explicit allowlist of structured action commands
- reject unknown commands
- reject malformed JSON with structured failure responses
- reject read-only resources sent as actions
- reject blocked commands such as `force-stop-supervisor`
- reject unconfirmed commands when confirmation is required
- return structured failure instead of throwing unhandled TCP exceptions
- keep cleanup, lifecycle, monitor, base-station, OSC, scheduled-task, and SteamVR manifest behavior backend-owned

TUI confirmation is only a user-experience layer. Backend validation must enforce the same or stricter policy.

## Current Implementation Boundary

The current implementation boundary is the six regular classic-console actions. Future phases should focus on manual VR-session testing, failure-path polish, and release readiness. `force-stop-supervisor` remains blocked until a separate reviewed safety design exists.

## Future Test Strategy

Future implementation should verify:

- C# and Rust builds still pass
- malformed JSON returns structured failure
- unknown commands are rejected
- blocked commands are rejected
- confirmation-required commands fail when `confirmed=false`
- `query-json` remains read-only
- legacy SteamVR host commands still work
- manual TUI confirmation cannot execute an action accidentally
- the TUI does not auto-start the supervisor
