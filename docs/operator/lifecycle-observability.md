# Phase 34A — Lifecycle ownership and cleanup observability

Phase 34A adds a passive, crash-surviving lifecycle journal. It does **not** change cleanup admission, Watcher launch decisions, owner-lock policy, SteamVR restart handling, managed-app shutdown policy, base-station timing, delayed wake timing, or update behavior.

## Collect after an incident occurs naturally

Do not reproduce an unstable SteamVR restart and do not stop an active VR session to collect this data.

1. Let the affected Supervisor and Watcher processes finish their normal behavior.
2. Copy the complete lifecycle log set before starting a new VR session:

   ```powershell
   $source = Join-Path $env:LOCALAPPDATA 'PimaxVrcSupervisor\Diagnostics\Lifecycle'
   $destination = Join-Path $env:USERPROFILE ("Desktop\PimaxVrcSupervisor-lifecycle-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
   New-Item -ItemType Directory -Path $destination -Force | Out-Null
   Copy-Item (Join-Path $source 'lifecycle-events.jsonl*') -Destination $destination -Force
   Get-ChildItem $destination | Select-Object Name, Length, LastWriteTimeUtc
   ```

3. Also collect the existing Supervisor diagnostics for the same time window. Do not collect or attach raw SteamVR log files for Phase 34A; the journal records only normalized marker/state health, offsets, and hashed source identities.
4. Preserve the files exactly as collected. `lifecycle-events.jsonl` is the active JSONL journal; `.1` is the most recent rotation, followed by `.2` and `.3`.

The deterministic journal location is:

```text
%LOCALAPPDATA%\PimaxVrcSupervisor\Diagnostics\Lifecycle\lifecycle-events.jsonl
```

Events are JSON Lines. Each line has `timestampUtc`, a per-process monotonic `sequence`, `processId`, `processStartIdentity`, `lifecycleCorrelationId`, `component`, `eventName`, `reason`, `result`, and safe normalized `fields`.

## Correlation guide

Start with `lifecycleCorrelationId`, never with timestamps alone. A Watcher launch has its own Watcher correlation and includes `fields.childLifecycleCorrelationId`; the launched Supervisor uses that child ID as its `lifecycleCorrelationId`.

| Scenario | Expected event chain |
|---|---|
| One Supervisor cleans up then later wakes stations | One Supervisor correlation contains `cleanup.admission` with `result=admitted`, `baseStation.powerDown` with `reason=cleanup`, then `baseStation.powerOn` on the same correlation. A later pass has `fields.delayedStartupWakePass=true`. |
| Supervisor exits, then Watcher relaunches for a new vrserver | Supervisor correlation ends with `lifecycle.processExit`. Watcher records `watcher.decision` transition to `launch-supervisor`, then `watcher.childLaunch` with `childLifecycleCorrelationId`, `childProcessId`, and the new `vrserverIdentity`. A new Supervisor begins with that child correlation. |
| Emergency or console-close cleanup | `cleanup.consoleClose` is followed by `cleanup.admission` where `fields.consoleCloseOrEmergencyCleanup=true`. Detached emergency cleanup runs as component `EmergencyCleanup` and records `baseStation.powerDown` with `reason=detached-emergency-cleanup`. |
| Duplicate or abandoned ownership | `lifecycle.ownerLockAcquisition` records `result=rejected-existing-mutex` for a duplicate and includes `fields.abandonedState=unavailable-under-preserved-admission-semantics`. Phase 34A deliberately does not perform a second mutex wait or ownership operation to classify abandonment. |
| SteamVR restart evidence versus stale evidence | `steamVr.evidenceHealth` records source availability, read offsets, `truncationDetected`, `rotationDetected`, normalized marker visibility, `staleMarkersCleared`, parser health, and latest normalized lifecycle state. It intentionally contains no raw SteamVR log line. |

## Event categories

- `lifecycle.processStart`, `lifecycle.ownerLockAcquisition`, `lifecycle.processExit` — Supervisor/Watcher identity and constructor-only ownership admission.
- `cleanup.admission` — every cleanup admission/rejection, including the owner state, current/expected vrserver identity, restart/transitional status, emergency indication, and planned cleanup effects.
- `managedApplication.shutdown` — category, safe PID/start identities, normalized reason, selected graceful/forced strategy, and result.
- `baseStation.powerDown`, `baseStation.powerOn` — initiator, target station count, observed vrserver identity, result, and whether a wake was a delayed startup pass.
- `watcher.decision`, `watcher.childLaunch` — state transitions, meaningful periodic health summaries, launch suppression, and child launch attempts/results. Repeated polling observations are suppressed unless state changes or the periodic summary is due.
- `steamVr.evidenceHealth` — bounded evidence-reader health only.

## Retention and failure behavior

The active lifecycle journal rotates at 2 MiB and retains three rotated files. Writer access is cross-process serialized by a named mutex so concurrent Supervisor and Watcher writes remain line-integral. The sink flushes each event to disk. Any journal directory, lock, serialization, or I/O failure is swallowed by the observability layer; it must not change lifecycle decisions or actions.
