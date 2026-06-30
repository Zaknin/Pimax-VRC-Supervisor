# Base Station Startup Diagnostics

This page describes the Phase 29A instrumentation for intermittent base-station startup failures.

## Scope

The instrumentation observes Supervisor base-station startup and Configurator Scan behavior. The Watcher startup initializer and the targeted resolution-failure refresh use the same shared discovery implementation as Configurator Scan. Neither discovery operation sends power commands, resets Bluetooth, restarts services, or alters Configurator Scan.

## Issue Being Measured

Repeated runtime observations show this pattern in some SteamVR autostart sessions:

1. SteamVR autostart begins.
2. Supervisor sends the normal base-station power-on burst.
3. All saved stations time out.
4. Configurator is opened and Scan is pressed for the same saved stations.
5. A later Supervisor burst succeeds.

This correlation is not proof that Scan causes success. It may also be explained by elapsed time, Bluetooth adapter readiness, BLE advertisement availability, `BluetoothLEDevice` resolution, GATT service readiness, or cached discovery state.

## Persistent Events

Structured events use schema:

```text
base-station-startup-diagnostics-v1
```

Events are written as JSONL under:

```text
%LOCALAPPDATA%\PimaxVrcSupervisor\Diagnostics\BaseStations
```

Process roles use separate active files:

- normal wake attempts and `bluetoothResolutionRefresh`: `base-station-startup-supervisor.jsonl`
- Configurator Scan: `base-station-startup-configurator.jsonl`
- `bluetoothStartupInitialization`: `base-station-startup-watcher.jsonl`, but only when the auto-launch Watcher process actually runs

Each active file is capped at about 5 MB and keeps up to three rotated copies. Diagnostic write failures are best-effort and do not affect base-station operations. A SteamVR-start task that directly launches `PimaxVrcSupervisor.exe --steamvr-start` does not execute the Watcher initializer and therefore does not create the Watcher file.

## Key Fields

Important fields include:

- `sessionId`
- `operationId`
- `operationName`
- `wakeSequenceId`
- `scanSessionId`
- `process`
- `eventType`
- `trigger`
- `burstNumber`
- `retryNumber`
- `configuredStationCount`
- `stationIdentity`
- `currentStage`
- `stageDurationMilliseconds`
- `totalAttemptDurationMilliseconds`
- `scanElapsedMilliseconds`
- `timeoutLimitMilliseconds`
- `configuredStationObserved`
- `observationAgeMilliseconds`
- `deviceResolutionResult`
- `gattServiceResult`
- `characteristicResult`
- `writeResult`
- `outcome`
- `unresolvedStationCount`
- `retryStationCount`
- `retrySuccessCount`
- `triggerFailureStage`
- `terminal`
- `exceptionType`
- `sanitizedErrorMessage`

Station identity is a deterministic short hash. Full Bluetooth addresses are not written to the structured diagnostics.

## Stages

The Supervisor records startup scheduling, burst execution, and per-station command stages such as:

- `startupTriggerReceived`
- `schedulerArmed`
- `schedulerDelayStarted`
- `schedulerDelayCompleted`
- `burstStarted`
- `stationAttemptStarted`
- `bluetoothAdapterLookupStarted`
- `bluetoothAdapterLookupCompleted`
- `deviceResolutionStarted`
- `deviceResolutionCompleted`
- `gattServiceQueryStarted`
- `gattServiceQueryCompleted`
- `characteristicResolutionStarted`
- `characteristicResolutionCompleted`
- `powerWriteStarted`
- `powerWriteCompleted`
- `stationAttemptTimedOut`
- `stationAttemptFailed`
- `stationAttemptSucceeded`
- `burstCompleted`
- `sessionCompleted`

Configurator Scan records:

- `configuratorScanStarted`
- `configuratorWatcherStarted`
- `configuratorStationObserved`
- `configuratorSavedStationMatched`
- `configuratorWatcherStopped`
- `configuratorScanCompleted`

The Watcher startup initializer uses operation name `bluetoothStartupInitialization`. The normal wake fallback uses `bluetoothResolutionRefresh` and records:

- `start`
- `triggered`
- `scanStarted`
- `scanCompleted`, `timedOut`, `cancelled`, or `failed`
- `watchersStopped`
- `retryStarted`
- `retryCompleted`
- `complete`

Only `complete` has `terminal = true`, so each refresh operation has exactly one terminal event.

Only stages that the code can observe are emitted.

## Timeout Analysis

For a timeout, inspect the `stationAttemptTimedOut` event:

```text
eventType = stationAttemptTimedOut
currentStage = gattServiceQuery
configuredStationObserved = true
observationAgeMilliseconds = 1400
burstNumber = 1
retryNumber = 0
timeoutLimitMilliseconds = 8000
```

The `currentStage` field identifies the last active stage when the existing 8-second command timeout fired.

## Collecting A Package

After reproducing a session, run:

```powershell
.\scripts\collect-base-station-startup-diagnostics.ps1
```

The collector creates:

```text
%TEMP%\PimaxVrcSupervisorDiagnostics\BaseStationStartup-<timestamp>
```

It includes:

- Supervisor base-station event files
- Configurator scan event files
- sanitized relevant log tails
- process state
- Bluetooth adapter metadata
- file hashes
- `base-station-diagnostics-manifest.json`
- `base-station-diagnostics-notes.md`

The collector is read-only. It does not scan for devices, start or stop processes, restart Bluetooth, send base-station commands, or modify configuration.

## Comparing Sessions

Useful comparisons for Phase 29B:

- failed first burst followed by Configurator Scan
- failed first burst without Configurator Scan
- successful first burst
- sessions where stations were recently observed before command attempts
- sessions where timeout stage is `deviceResolution`
- sessions where timeout stage is `gattServiceQuery`
- sessions where timeout stage is `characteristicResolution`
- sessions where timeout stage is `powerWrite`

## Privacy

Structured diagnostics intentionally avoid full Bluetooth addresses and raw device identifiers. Station correlation uses deterministic short hashes so the same configured station can be compared across sessions without exposing the address.

## Next Analysis

Phase 29B should compare repeated startup sessions and determine the most likely discovery/readiness failure stage before any behavior change is considered.
