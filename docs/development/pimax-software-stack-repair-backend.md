# Pimax Software-Stack Repair Backend

Phase 28D2-B implements the backend contract for a future `Repair Pimax Connection` Terminal UI action. Phase 28D2-B1 corrects the target policy after live validation proved that `PimaxClient` is not an isolated restartable process.

This phase does not add the TUI button. It does not invoke Pimax Play Connect, automate the GUI, reset USB, disable or enable devices, touch DisplayPort, restart SteamVR, restart VRChat, restart VRCFT, restart the Supervisor, restart the watcher, change the scheduled task, or deploy over the active Phase 29D-E package.

## Commands

Read-only:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-repair-targets-json
dotnet .\PimaxVrcSupervisor.dll pimax-launch-recipe-json
dotnet .\PimaxVrcSupervisor.dll pimax-startup-sources-json
dotnet .\PimaxVrcSupervisor.dll pimax-startup-observe-json --fake
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
pimax-launch-recipe-v1
pimax-startup-sources-v1
pimax-startup-observation-v1
pimax-repair-start-v1
pimax-repair-status-v1
pimax-repair-cancel-v1
pimax-repair-result-v1
```

`pimax-repair-start-json` is dry-run unless `--confirm` and a matching `--confirmation-token` are provided. Current Pimax Play/runtime targets do not produce a confirmation token because no executable group restart recipe is approved.

## Phase 28D2-BV Correction

The controlled Phase 28D2-BV live validation closed the selected top-level `PimaxClient` window with `CloseMainWindow`. No force kill was used, and the backend repair operation was not started. The close still caused coupled Pimax runtime members, including `DeviceSetting`, `PiPlayService`, and `PiService`, to exit. Pimax Play closed, the headset became blue/unavailable by operator observation, and stale machine evidence still reported `healthy / registeredReady`.

The sanitized regression fixture is `phase28d2bv-coupled-exit-sanitized.json`. The raw private evidence is not committed. The public PR evidence reference is PR #22 comment `4758761712`.

## Approved Target Discovery

The target command combines:

- validated Pimax Play client discovery from the existing Pimax client controller;
- read-only Pimax-root process inventory;
- existing sanitized Pimax service probing.

Each target is classified as exactly one of:

- `approvedRestartableProcess`
- `approvedRestartableService`
- `observeOnly`
- `groupMemberNotIndependentlyRestartable`
- `restartRecipeIncomplete`
- `readyForControlledValidation`
- `prohibited`

Phase 28D2-B1 does not approve any standalone Pimax Play/runtime process. A validated `PimaxClient` is classified as `groupMemberNotIndependentlyRestartable` because closing it terminated other runtime members during Phase 28D2-BV.

The complete Pimax Play/runtime group is represented as a `processGroup` target. When the current group is complete, the target may still be listed as a non-executable validation candidate, but its launch recipe state is `shellActivationObserved`, not executable. Phase 28D2-BV2 rejected direct `PimaxClient.exe` process creation because it produced only a blank `PimaxClient` window and left `DeviceSetting`, `PiPlayService`, and `pi_server` missing. Automatic restart remains disabled until a formal observer-backed Start Menu comparison identifies the creator chain and a later one-shot phase validates a safe programmatic equivalent.

Pimax services, including `PiServiceLauncher` and Tobii Eye Tracking runtime services, remain observe-only in this phase. Previous evidence did not prove that restarting them is a safe or correct persistent recovery target.

## Allowlist Policy

Future executable group requirements:

- complete expected Pimax Play/runtime member set is understood;
- root launch method and safe start command are known;
- readiness criteria and runtime ownership are current;
- shutdown semantics and expected coupled exits are declared;
- no driver or prohibited service mutation is required;
- private transient command-line arguments are not required;
- unrelated processes are excluded;
- post-start component health verification is available.

Approved service requirements for future phases:

- exact service name;
- signed user-mode Pimax binary;
- no kernel driver service;
- dependency graph understood;
- no unrelated dependent service;
- bounded stop/start semantics proven.

No process, group, or service currently meets that approval bar in this backend.

## Software Group Model

The Pimax software group model distinguishes launcher/root process, Pimax Play UI process, runtime process, service-owned process, helper process, driver host, optional component, and unknown member roles. Group states are `complete`, `partial`, `unavailable`, `starting`, `stopping`, `conflicting`, and `unknown`.

Membership uses installation path, publisher/product metadata, known process role, service ownership, and observed lifecycle coupling. It does not infer membership from a `Pi` or `Pimax` filename prefix alone.

Every executable target or future group target must declare expected process exits, expected process starts, service changes, child-process effects, prohibited side effects, maximum process-tree expansion, rollback availability, and restart recipe confidence. Any unexpected process-tree change must stop further mutation and report a safety failure.

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
9. Revalidate target classification, group membership, side effects, signer/path, and restart recipe completeness.
10. Refuse mutation if the Pimax group recipe is incomplete or a standalone group member is selected.
11. Execute only future approved targets whose complete side-effect declarations match live state.
12. Stop immediately on unexpected process-tree changes.
13. Wait through one passive settle interval.
14. Capture one post-repair health snapshot.
15. If post-health is initializing, allow one delayed confirmation snapshot.
16. Compare pre/post health.
17. Determine final outcome from post-health.
18. Write durable diagnostics.
19. Release operation ownership.

There are no repeated retry loops.

## Process Policy

`PimaxClient` is observe-only as a coupled group member. The backend refuses a malformed catalog that marks it as an approved standalone target.

The backend does not force-kill by default.

A future group start must use a proven group launch recipe. The backend does not search `PATH`, launch by filename alone, or start unrelated processes.

## Phase 28D2-B2 Launch Recipe

Phase 28D2-B2 adds the read-only `pimax-launch-recipe-json`, `pimax-startup-sources-json`, and bounded `pimax-startup-observe-json` commands. They do not start Pimax, stop Pimax, invoke Connect, automate the GUI, cycle USB, touch DisplayPort, restart services, restart SteamVR, restart VRChat, restart VRCFT, restart the Supervisor, restart the watcher, or change scheduled tasks.

Private discovery confirmed the local launcher candidate:

```text
C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe
```

Sanitized launcher evidence:

- product name: `PimaxClient`;
- company name: `Pimax`;
- file/product version: `1.43.9.272`;
- architecture: 64-bit Windows GUI executable;
- requested execution level: `asInvoker`;
- Start Menu shortcut: `PimaxPlay.lnk`;
- shortcut target: the candidate executable;
- shortcut arguments: empty;
- shortcut working directory: `C:\Program Files\Pimax\PimaxClient\pimaxui`;
- installed application: `PimaxPlay version 1.43.9.272`;
- App Paths registration: none observed.

BV2 direct launch evidence rejects treating that executable path as sufficient. Direct process creation started `PimaxClient` but did not recreate the complete runtime group. A later unobserved operator restoration launch through the normal Windows Start Menu opened Pimax Play normally and automatically restored registration, but the observer was not running, so that launch is supporting evidence only.

The formal Phase 28D2-B2A observer-backed Start Menu comparison then produced `groupReadyAndRegistered` with `registeredReady / confirmed` and current freshness. The user-visible result was green LED, normal Pimax Play window, image present, audio and microphone present, eye tracking present, Vive face tracking detected, and no unrelated restart or PC instability. The required runtime group formed without manually starting `DeviceSetting`, `PiPlayService`, or `pi_server`.

Creator-chain evidence remains incomplete. The post-launch parent snapshot showed `PiPlayService` created by or currently parented to `DeviceSetting`, `PiService` parented to `DeviceSetting`, and `pi_server` parented to `PiService`. `DeviceSetting` itself was parented outside the still-running Pimax group or by an already-exited broker, so the root activation creator remains unresolved.

The raw SHA-256 hash, raw certificate subject, raw process IDs, command lines, and local registry details are kept in private discovery evidence and are not committed.

Lifecycle-root assessment is unresolved: the shortcut target is known, but the missing runtime members may be created by shell activation context, a service broker, a transient helper, state reset, or multiple mechanisms. `pimax-startup-sources-json` reports the activation-source candidates, and the formal `pimax-startup-observe-json` run is used to compare the Start Menu path against the rejected direct-launch evidence.

Expected required members:

- `PimaxClient`;
- `DeviceSetting`;
- `PiPlayService`;
- `pi_server`;
- `PiServiceLauncher`;
- `Tobii VR4PIMAXP3B Platform Runtime`.

Optional members include `PiService`, `PiPlatformService_64`, `PVRHome`, and `pi_overlay`.

Readiness states added by the launch recipe model:

- `groupReadyAndRegistered`;
- `groupReadyAwaitingRegistration`;
- `groupCompleteRegistrationUnknown`;
- `groupStarting`;
- `groupPartial`;
- `groupUnavailable`;
- `groupConflicting`;
- `groupLaunchFailed`;
- `timedOut`;
- `unknown`.

`groupReadyAwaitingRegistration` deliberately does not claim repair success:

```text
Pimax Play started successfully, but the headset is still awaiting
registration.

Pimax Play Connect and a physical USB reconnection may still be required.
```

Recipe blockers that still prevent execution:

- formal observer-backed Start Menu launch evidence has not yet proven the creator chain;
- `DeviceSetting` root creator ownership is not yet established;
- a safe programmatic shell-equivalent is not validated;
- registration after launch still requires post-health proof;
- no shutdown/retry/rollback behavior is approved for product repair.

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

`unsupportedAutomaticRecovery` is the expected result when the full Pimax Play/runtime group is unavailable, partial, stale, or lacks an approved restart recipe:

```text
Automatic Pimax software restart is unavailable.

A verified Pimax Play launcher candidate has been identified, but the
complete process-group launch and readiness recipe has not yet been
validated from a stopped state.

Automatic restart remains disabled.
```

If software restarts succeed but registration remains unavailable, the result is:

```text
The Pimax software stack restarted successfully, but Pimax Play still
has not registered the headset.

Pimax Play Connect and a physical USB reconnection may still be required.
```

## Component Report

Final results include component-level status for:

- Pimax registration;
- Pimax software group;
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

## Registration Ownership And Freshness

`registeredReady / confirmed` now requires contemporaneous evidence from the owning Pimax software group. USB, audio, and device remnants are not sufficient when Pimax Play/runtime ownership is absent.

Freshness states are `current`, `stale`, `unowned`, `contradicted`, and `unknown`. If the software group is unavailable, registration becomes `softwareStackUnavailable`; if a previous ready result no longer belongs to the current process/session evidence, registration becomes `registrationEvidenceStale`. Neither state is healthy.

## Live Validation Boundary

Live validation is optional. It may occur only after a dry run and only with explicit operator confirmation. It must not replace Phase 29D-E, run a second persistent watcher, change the scheduled task, invoke Connect, cycle USB, touch DisplayPort, restart SteamVR, restart VRChat, restart VRCFT, restart the Supervisor, or retry automatically.

## Future TUI Integration

The TUI phase remains blocked while no validated executable Pimax Play/runtime restart recipe exists. The next phase should validate the candidate exactly once from a stopped process-group state:

```text
Phase 28D2-BV2 - One-Shot Validation of the Candidate Pimax Process-Group Launch Recipe
```
