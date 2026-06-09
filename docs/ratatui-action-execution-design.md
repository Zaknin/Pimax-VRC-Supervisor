# TUI Action Execution Safety Design

This document defines the safety model for future desktop Ratatui TUI action execution. It is design-only: no action execution, confirmation handling, or backend protocol changes are implemented in Phase 8.

## Current State

The desktop TUI is read-only. It uses the existing loopback TCP bridge at `127.0.0.1:37957` and sends only read-only `query-json` requests:

```text
query-json {"resource":"status"}
query-json {"resource":"commands"}
query-json {"resource":"log","maxLines":80}
```

The backend also accepts legacy string action commands on the same bridge. The SteamVR dashboard host uses the legacy command protocol and must not be forced to change. The Ratatui TUI currently displays command capability metadata as informational data only.

Important metadata distinction: `commands-json` currently uses `available=true` to mean "accepted by the current backend bridge." It does not mean a command is safe, supported by future JSON action execution, or executable from the desktop TUI.

## Phase 9 Implementation Status

Phase 9 adds backend-only structured `action-json` support for one allowlisted command: `restart-osc-router`.

The desktop TUI remains read-only. No TUI action buttons, action keybindings, or confirmation modal are implemented. The SteamVR host remains on the legacy command protocol.

All other action commands remain rejected by `action-json`, and `force-stop-supervisor` remains blocked from the structured desktop TUI action flow.

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
| `restart-osc-router` | `LowRisk` | first candidate | Restarts or manually starts OSC routing; does not power hardware or bypass cleanup. |
| `start-osc-goes-brrr` | `Disruptive` | possible second candidate | May launch or repair Intiface/OscGoesBrrr workflow. |
| `restart-core-apps` | `Disruptive` | deferred | Restarts configured face-tracking apps during the session. |
| `base-stations-on` | `Disruptive` | deferred | Powers configured base stations and touches hardware state. |
| `base-stations-off` | `Disruptive` | deferred | Powers down configured base stations and can disrupt tracking. |
| `force-stop-supervisor` | `Blocked` | never by default | Legacy bridge command exists, but it hard-stops without cleanup routines. |

`force-stop-supervisor` is the key distinction between backend bridge availability and TUI executability: it may remain accepted by the legacy bridge, but the desktop TUI design classifies it as `Blocked`.

## Future TUI UX

The command list remains informational until structured backend action support exists.

When action support is implemented in a later phase:

- The TUI should show a separate selectable action list or clearly mark executable rows.
- A command may be selectable only when `actionSupported=true` and `tuiExecutable=true`.
- No action should run from a single accidental keypress.
- Confirmation must show command name, safety category, risk warning, expected effect, and blocked reason when applicable.
- `Esc` cancels confirmation.
- Action results should appear in a popup, status area, or recent activity line.
- Blocked commands should not be executable and should explain why when selected or inspected.

## Future Backend Protocol

Phase 9 starts a structured `action-json` command for a tiny backend-only allowlist. Do not repurpose `query-json`; it remains read-only.

Example request:

```json
{
  "requestId": "optional-id",
  "command": "restart-osc-router",
  "confirmed": true
}
```

Example response:

```json
{
  "timestamp": "...",
  "requestId": "optional-id",
  "command": "restart-osc-router",
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

## Recommended Implementation Order

Phase 9 implements backend-only structured `action-json` support for a tiny allowlist, currently only `restart-osc-router`, with no TUI action buttons yet.

Possible later candidate:

- `start-osc-goes-brrr`, after confirming expected launch/repair behavior and messaging.

Deferred candidates:

- `restart-core-apps`
- `base-stations-on`
- `base-stations-off`

Blocked by default:

- `force-stop-supervisor`

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
