# Desktop TUI Load Baselines

## Phase 20D Collection

Collection date/time: 2026-06-12T02:00:35+04:00

Commit/build basis:

- `70ca6de Phase 20C: add Desktop TUI process load diagnostics`
- Release folder: `release\PimaxVrcSupervisor-v1.3.0-test`
- TUI executable: `PimaxVrcSupervisorTui.exe`, last written 2026-06-11 20:17:34

Temporary diagnostics config:

- Config: `%TEMP%\pimax-tui-phase20d.json`
- Diagnostics folder: `%TEMP%\PimaxVrcSupervisorPhase20D`
- Interval: 15 seconds
- Enabled option: `DiagnosticsLogDesktopTui`

## Scope

This was a diagnostics collection/reporting phase only. No source behavior, bridge protocol, render cadence, refresh cadence, Configurator behavior, Supervisor behavior, SteamVR host behavior, cleanup behavior, action allowlist, or release layout changed.

Disconnected collection was skipped because `PimaxVrcSupervisor.exe` was already running as PID `38344`. The process was not stopped automatically.

Connected collection was performed with a separate temporary TUI process, PID `51560`, while an existing TUI process, PID `45128`, remained running. Only the temporary TUI process was stopped after collection.

## Raw Summary Records

| connected | interval_seconds | renders | refreshes | input_wakeups | bridge_calls | bridge_failures | bridge_timeouts | bridge_ms_min | bridge_ms_avg | bridge_ms_max | tui_cpu_percent | tui_cpu_time_delta_ms | tui_cpu_time_total_ms | tui_working_set_mb | tui_private_memory_mb | tui_thread_count | tui_handle_count |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| true | 15.115755 | 7 | 6 | 52 | 18 | 0 | 0 | 0 | 8 | 52 | 0.0064605605211251445 | 15 | 31 | 7.7890625 | 2.0234375 | 4 | 72 |
| true | 15.0255307 | 5 | 5 | 1 | 15 | 0 | 0 | 0 | 5 | 21 | 0.02599741784827607 | 62 | 93 | 7.80078125 | 2.0234375 | 2 | 72 |
| true | 15.0257441 | 5 | 5 | 0 | 15 | 0 | 0 | 0 | 6 | 20 | 0.02599704862536558 | 62 | 156 | 7.80859375 | 2.0 | 2 | 72 |
| true | 15.0194694 | 5 | 5 | 0 | 15 | 0 | 0 | 0 | 6 | 21 | 0.026007909440529237 | 62 | 218 | 7.8046875 | 2.0 | 2 | 72 |

## Raw Grouped Statistics

| connected | samples | avg_renders | max_renders | avg_refreshes | avg_bridge_calls | avg_bridge_failures | avg_bridge_timeouts | avg_bridge_ms | max_bridge_ms | avg_cpu_percent | max_cpu_percent | avg_working_set_mb | max_working_set_mb | avg_private_memory_mb | max_private_memory_mb | avg_thread_count | max_thread_count | avg_handle_count | max_handle_count |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| true | 4 | 5.5 | 7 | 5.25 | 15.75 | 0 | 0 | 6.25 | 52 | 0.021 | 0.026 | 7.8 | 7.81 | 2.01 | 2.02 | 2.5 | 4 | 72 | 72 |

## Sample Summary Line

```json
{"actions_started":0,"bridge_calls":18,"bridge_failures":0,"bridge_ms_avg":8,"bridge_ms_max":52,"bridge_ms_min":0,"bridge_timeouts":0,"connected":true,"connection_changes":1,"event":"desktop_tui_diagnostics_summary","input_wakeups":52,"interval_seconds":15.115755,"lifecycle_requests":0,"pid":51560,"refreshes":6,"renders":7,"tui_cpu_percent":0.0064605605211251445,"tui_cpu_time_delta_ms":15,"tui_cpu_time_total_ms":31,"tui_handle_count":72,"tui_private_memory_mb":2.0234375,"tui_thread_count":4,"tui_working_set_mb":7.7890625}
```

## Sanity Checks

- `desktop_tui_diagnostics_started` marker was present.
- Four `desktop_tui_diagnostics_summary` records were parsed successfully.
- Existing Phase 20A/20B fields were present.
- Phase 20C process metrics were present in every summary.
- No `NaN`, `Infinity`, invalid JSON, or null process metric values were observed.

## Interpretation

Connected idle load looks healthy:

- Average render count was `5.5` per 15 seconds, far below the old connected pre-20B render baseline of about `75` per 15 seconds.
- Refresh cadence stayed near the intended 3 second connected cadence, with `5.25` refreshes and `15.75` bridge calls per 15 seconds.
- Bridge failures and timeouts were both `0`.
- Average TUI CPU was `0.021%`, with maximum `0.026%`.
- Working set and private memory were stable around `7.8 MB` and `2.01 MB`.
- Handle count stayed constant at `72`.
- Thread count settled at `2` after the startup interval; the first interval showed `4`, consistent with startup/transient activity.

Disconnected baseline was not collected in Phase 20D because an existing Supervisor process was running. Phase 20B disconnected diagnostics already showed the expected idle reduction after render/backoff changes: `renders=6/4` and `bridge_timeouts=3/2` across two 15 second intervals.

## Recommendation

No further connected-idle TUI optimization is recommended right now. The measured CPU, memory, thread, handle, render, and bridge timing values are low and stable.

If additional Phase 20 work is desired, the next useful step is a disconnected-only baseline run when the Supervisor can be safely stopped, or a longer 10-15 minute soak to confirm handle and memory stability over time.
