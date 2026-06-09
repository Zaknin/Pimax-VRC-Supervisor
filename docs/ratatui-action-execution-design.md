# TUI Action Execution Safety Design

This document defines the safety model for desktop Ratatui TUI action execution. Phase 15 enables confirmed action parity for the regular classic-console actions while keeping `force-stop-supervisor` blocked.

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
- No action runs from a single accidental keypress.
- `Enter` confirms inside the confirmation modal.
- Confirmation shows command name, safety category, expected effect, and backend warning.
- `Esc`, `n`, and modal `q` cancel confirmation.
- Number keys, `H`, `F1`, `?`, removed help aliases, and dashboard keys are ignored while confirmation is visible.
- Action results appear in the backend/status area.
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
