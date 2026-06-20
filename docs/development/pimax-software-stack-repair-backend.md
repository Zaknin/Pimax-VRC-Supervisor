# Pimax Software-Stack Repair Backend

Phase 28D2-B implements the backend contract for a future `Repair Pimax Connection` Terminal UI action. The backend can discover exact repair targets, build a strict allowlist, execute only confirmed software-stack actions, and verify the real headset state with post-repair component health.

This phase does not add the TUI button. It does not invoke Pimax Play Connect, automate the GUI, reset USB, disable or enable devices, touch DisplayPort, restart SteamVR, restart VRChat, restart VRCFT, restart the Supervisor, restart the watcher, change the scheduled task, or deploy over the active Phase 29D-E package.

## Commands

Read-only:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-repair-targets-json
dotnet .\PimaxVrcSupervisor.dll pimax-repair-status-json
dotnet .\PimaxVrcSupervisor.dll pimax-repair-result-json
```

Operation:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-repair-start-json --mode software-stack-only --dry-run
dotnet .\PimaxVrcSupervisor.dll pimax-repair-cancel-json
```

Schemas:

```text
pimax-repair-targets-v1
pimax-repair-start-v1
pimax-repair-status-v1
pimax-repair-cancel-v1
pimax-repair-result-v1
```

`pimax-repair-start-json` is dry-run unless `--confirm` and a matching `--confirmation-token` are provided. Dry-run returns the confirmation token when an approved mutating plan exists.

## Approved Target Discovery

The target command combines:

- validated Pimax Play client discovery from the existing Pimax client controller;
- read-only Pimax-root process inventory;
- existing sanitized Pimax service probing.

Each target is classified as exactly one of:

- `approvedRestartableProcess`
- `approvedRestartableService`
- `observeOnly`
- `prohibited`

Phase 28D2-B approves only the exact validated top-level Pimax Play client process when path, hash, product identity, and shortcut relaunch target are known. Other Pimax-root processes are observe-only unless explicitly prohibited.

Pimax services, including `PiServiceLauncher` and Tobii Eye Tracking runtime services, remain observe-only in this phase. Previous evidence did not prove that restarting them is a safe or correct persistent recovery target.

## Allowlist Policy

Approved process requirements:

- exact executable path is known;
- file identity is validated at discovery and execution;
- the process is the top-level Pimax Play client candidate;
- relaunch source is a verified shortcut target;
- the process was running before repair;
- it is not Supervisor, SteamVR, VRChat, VRCFT, a crash handler, PVRHome, or unrelated software.

Approved service requirements for future phases:

- exact service name;
- signed user-mode Pimax binary;
- no kernel driver service;
- dependency graph understood;
- no unrelated dependent service;
- bounded stop/start semantics proven.

No service currently meets that approval bar in this backend.

## Prohibited Targets

The backend prohibits:

- drivers and kernel services;
- root hub, controller, USB, Bluetooth, or GPU services;
- SteamVR;
- VRChat;
- VRCFT;
- Supervisor and watcher processes;
- crash handlers and unrelated Pimax-root helper processes whose restart semantics are not proven.

The backend never uses wildcard process killing, wildcard service matching, filename-only launch, PATH search, shell command expansion, or arbitrary private command-line restoration.

## Execution Flow

The software-stack-only flow is:

1. Acquire the shared backend repair lock.
2. Capture one pre-repair component-health snapshot.
3. Capture approved target state.
4. Classify the failure.
5. Build the executable plan.
6. Return `noRepairNeeded` if health is already good.
7. Return `unsupportedAutomaticRecovery` if no approved action exists.
8. Require explicit confirmation for live mutation.
9. Gracefully close only approved Pimax Play client processes.
10. Stop only approved services in dependency order.
11. Start approved services in reverse dependency order.
12. Relaunch only approved processes that were running before repair.
13. Wait through one passive settle interval.
14. Capture one post-repair health snapshot.
15. If post-health is initializing, allow one delayed confirmation snapshot.
16. Compare pre/post health.
17. Determine final outcome from post-health.
18. Write durable diagnostics.
19. Release operation ownership.

There are no repeated retry loops.

## Process Policy

Process stop prefers graceful close through the exact validated process. If the process refuses to close before timeout, the operation stops and reports `softwareRepairFailed`.

The backend does not force-kill by default.

Process start uses only the validated relaunch target. It does not search `PATH`, launch by filename alone, or start unrelated processes.

## Service Policy

Service restart is implemented behind an exact target interface and covered by fake-based tests, but live Windows services are observe-only until a target is proven safe.

The backend does not stop kernel services, driver services, unrelated dependents, USB services, Bluetooth services, or GPU services.

## Cancellation

Cancellation is accepted only at safe boundaries:

- before mutation: no software action is performed;
- between actions: no next action starts;
- during settle: the remaining settle interval ends and final state is reported where safe.

Cancellation does not claim rollback.

## Timeouts

Bounded defaults:

- process graceful close: 8 seconds;
- service stop: 15 seconds;
- service start: 20 seconds;
- process start readiness: 20 seconds;
- passive settle: 10 seconds;
- operation timeout request is clamped to 30-300 seconds.

On timeout, the backend stops additional actions, captures post-health where safe, reports the timed-out stage, and does not retry automatically.

## Low-Intrusion Verification

The backend applies the Phase 28D1-C observation policy:

- one pre-health snapshot;
- process/service operation events;
- one passive settle interval;
- one post-health snapshot;
- at most one delayed confirmation snapshot;
- no continuous SetupAPI, MMDEVAPI, PnP, named-pipe, endpoint, or log polling loops.

Success is controlled by post-health, not by process or service state.

## Durable Diagnostics

Append-only diagnostics are written under:

```text
%LOCALAPPDATA%\PimaxVrcSupervisor\Diagnostics\PimaxRepair\pimax-repair-operations.jsonl
```

Schema:

```text
pimax-repair-operation-v1
```

The log records operation ID, correlation ID, build identity, timestamp, stage, action, sanitized target identity, result, duration, timeout, cancellation, exception type, error text, pre-health summary, post-health summary, final outcome, and warnings.

Logging failure is isolated and must not crash the repair engine. The writer uses bounded rotation.

## Final Outcomes

Supported outcomes:

- `noRepairNeeded`
- `repaired`
- `repairedWithDegradedFeatures`
- `softwareStackHealthyButNotRegistered`
- `coreUsbMissing`
- `displayPathMissing`
- `softwareRepairFailed`
- `cancelled`
- `timedOut`
- `unsupportedAutomaticRecovery`
- `conflictingEvidence`
- `unknown`

`repaired` requires post-registration `registeredReady / confirmed`, required core components present, no new blocking regression, and improvement from unhealthy pre-health to healthy post-health.

If software restarts succeed but registration remains unavailable, the result is:

```text
The Pimax software stack restarted successfully, but Pimax Play still
has not registered the headset.

Pimax Play Connect and a physical USB reconnection may still be required.
```

## Component Report

Final results include component-level status for:

- Pimax registration;
- core USB;
- USB 2 companion;
- SuperSpeed companion;
- DisplayPort;
- audio output;
- microphone;
- EyeChip;
- eye tracking;
- cameras/tracking;
- headset HID;
- Vive face tracker;
- Pimax runtime;
- Pimax Play;
- Pimax services.

## Live Validation Boundary

Live validation is optional. It may occur only after a dry run and only with explicit operator confirmation. It must not replace Phase 29D-E, run a second persistent watcher, change the scheduled task, invoke Connect, cycle USB, touch DisplayPort, restart SteamVR, restart VRChat, restart VRCFT, restart the Supervisor, or retry automatically.

## Future TUI Integration

The next phase may add a Terminal UI entry only after the backend contracts, allowlist, diagnostics, and safety behavior remain stable:

```text
Phase 28D3 - Add Repair Pimax Connection to the Terminal UI
```
