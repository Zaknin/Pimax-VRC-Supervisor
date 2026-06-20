# Pimax Low-Intrusion Repair Orchestration

Phase 28D2-L defines the production backend foundation for a future `Repair Pimax Connection` action. It adds a repair capability model, non-mutating planner, state-machine contract, action descriptors, verification rules, low-intrusion observation policy, concurrency policy, and future backend/TUI protocol.

This phase does not perform repair. It does not restart Pimax processes or services, invoke Pimax Play Connect, automate Pimax Play, reset USB, touch DisplayPort, start SteamVR, change configuration, or modify the active deployment.

## Product Objective

The objective is to turn component health into an honest repair plan:

- what the Supervisor can safely observe now;
- what a later confirmed software repair phase may execute;
- what still requires Pimax Play or physical operator action;
- how success will be verified without confusing process/service state with headset recovery.

The implemented commands are:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-repair-capabilities-json
dotnet .\PimaxVrcSupervisor.dll pimax-repair-plan-json
```

Schemas:

```text
pimax-repair-capabilities-v1
pimax-repair-plan-v1
```

## Prior Evidence

### Phase 28C3D

The exact paired Windows USB cycle submitted one SuperSpeed request and one USB 2 request. Both native calls succeeded, both logical sides transitioned, runtime descendants returned, Vive and unrelated ports stayed stable, and the PC stayed stable.

Registration still remained unavailable: the headset stayed blue and Pimax Play still showed error `10500`. A physical Pimax USB reseat restored green registration afterward.

Conclusion: software logical port cycling is not an approved product recovery action and is not exposed through repair planning.

### Phase 28D1

Phase 28D1 added `pimax-component-health-json` and `pimax-connect-routine-observe-json`. Component health became the authoritative model for registration, core USB, SuperSpeed, DisplayPort, audio, microphone, EyeChip, tracking cameras, and the Vive face tracker.

Connect routine mapping ended as:

```text
D - opaque internal Pimax Play GUI behavior
```

No safe programmatic path was identified through named pipes, localhost APIs, process invocation, service operations, configuration changes, or supported command-line arguments.

### Phase 28D1-C

The confirmed low-intrusion blue-to-green recovery sequence was:

1. Headset is powered on but awaiting registration.
2. LED is blue and Pimax Play does not recognize the headset.
3. Press Pimax Play `Info -> Pimax Crystal -> Connect`.
4. Physically reseat the Pimax USB cable within about one to two seconds.
5. Registration changes to `registeredReady / confirmed`.
6. LED becomes green and error `10500` clears.

The heavy Connect observer appeared to perturb or delay registration, so it remains development-only and is not part of the repair workflow.

### Phase 28D2-BV

The Phase 28D2-BV live validation proved that `PimaxClient` is part of a coupled Pimax Play/runtime process group. A graceful `CloseMainWindow` against the selected `PimaxClient` also terminated other runtime members, Pimax Play closed, and the headset became blue/unavailable by operator observation. No force kill was used and no backend repair operation was started.

Fault-state machine evidence still reported `healthy / registeredReady` because previously enumerated USB/audio/device evidence remained present. Phase 28D2-B1 corrects that by requiring current Pimax Play/runtime ownership for `registeredReady / confirmed` and by downgrading stale or unowned evidence to non-healthy software-stack states. The sanitized public reference is PR #22 comment `4758761712`; private raw paths, PIDs, command lines, and device identities are not documented here.

## Capability Boundaries

Available now:

- component-health assessment;
- Pimax process-state assessment;
- Pimax service-state assessment;
- before/after component comparison;
- bounded wait design;
- human-readable diagnosis;
- Pimax Play/runtime process-group observation;
- Pimax Play launcher candidate discovery;
- Pimax process-group launch recipe modeling;
- operation progress reporting;
- cancellation before mutating software actions;
- final verification.

Unavailable or not approved:

- direct Pimax Play Connect invocation;
- supported Pimax discovery API;
- reliable GUI-free Connect equivalent;
- electrical USB disconnect/reconnect;
- approved software USB cycle for registration recovery;
- standalone `PimaxClient` restart;
- unvalidated Pimax Play/runtime group restart;
- DisplayPort electrical reconnect;
- automatic physical-cable recovery.

The capabilities command emits each capability with ID, display name, availability, confidence, source evidence, explanation, and product implication.

## Repair Classifications

The planner supports these classifications:

- `alreadyHealthy`
- `softwareStackUnhealthy`
- `softwareStackUnavailable`
- `softwareStackPartial`
- `staleRegistrationEvidence`
- `poweredOnAwaitingRegistration`
- `coreUsbMissing`
- `superSpeedMissing`
- `displayPathMissing`
- `audioOutputMissing`
- `microphoneMissing`
- `eyeChipMissing`
- `trackingInterfacesMissing`
- `viveFaceTrackerMissing`
- `multipleFailures`
- `conflictingEvidence`
- `unknown`

`viveFaceTrackerMissing` is optional accessory state. It does not make the Pimax headset unusable by itself.

`softwareStackUnavailable`, `softwareStackPartial`, and `staleRegistrationEvidence` return `unsupportedAutomaticRecovery` until a complete Pimax Play/runtime group launch and readiness recipe is validated from a stopped state. A discovered launcher candidate does not make the group executable.

`poweredOnAwaitingRegistration` explicitly reports that automatic registration is not guaranteed. Pimax Play Connect and a real physical USB reconnection may still be required.

## State Machine

Stages:

- `created`
- `preflight`
- `capturingPreHealth`
- `classifyingFailure`
- `buildingPlan`
- `awaitingConfirmation`
- `preparingSoftwareActions`
- `executingSoftwareActions`
- `settling`
- `capturingPostHealth`
- `verifyingOutcome`
- `completed`
- `cancelled`
- `failed`

Legal transitions are emitted in the capabilities contract. Invalid transitions are rejected by the model. Terminal states are `completed`, `cancelled`, and `failed`.

The operation state includes operation ID, correlation ID, start time, current stage, progress ordinal, cancellation state, timeout state, active action, completed actions, warnings, and final outcome.

## Action Descriptors

The model defines these future action types:

- `captureHealth`
- `verifyProcessState`
- `verifyServiceState`
- `requestOperatorConfirmation`
- `requireApprovedGroupRestartRecipe`
- `stopValidatedPimaxProcesses`
- `restartValidatedPimaxServices`
- `startValidatedPimaxProcesses`
- `waitForSoftwareStack`
- `waitForRegistration`
- `capturePostHealth`
- `compareHealth`
- `reportResult`
- `requirePimaxConnect`
- `requirePhysicalUsbReconnect`
- `requireDisplayPortReconnect`
- `noAction`

Each descriptor includes category, mutating flag, supported flag, approved flag, confirmation requirement, cancellation behavior, timeout, preconditions, success criteria, failure criteria, and explanation.

In this phase, mutating actions are descriptors only. They are not supported or approved for execution by the new commands. `requireApprovedGroupRestartRecipe` is the descriptor used when a group-level recovery would be needed but no complete stopped-state validated recipe exists.

## Process-Group Launch Boundary

Phase 28D2-B2 identifies a read-only Pimax Play launcher candidate through the official Start Menu shortcut:

```text
C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe
```

The shortcut has no arguments and uses the `pimaxui` directory as its working directory. The executable is a Pimax metadata-matched, 64-bit Windows GUI process with an `asInvoker` requested execution level. Installed-application registry evidence identifies Pimax Play, and no App Paths launcher override was observed.

BV2 later proved that direct process creation of this executable is not sufficient: it started `PimaxClient` but left the required runtime group partial. The backend now reports the process-group launch recipe as `shellActivationObserved` when the group is healthy: the normal Start Menu path is the candidate, direct executable launch is rejected, and backend execution remains disabled until an observer-backed comparison identifies the creator chain and a later one-shot phase validates a safe programmatic equivalent.

## Dependency-Aware Ordering

Registration problem:

1. Capture health.
2. Verify core USB.
3. Verify Pimax processes and services.
4. Require a complete Pimax Play/runtime group restart recipe before any software mutation.
5. Settle.
6. Verify registration.
7. Report Connect and physical USB limitations if registration remains unavailable.
8. Capture final health.

EyeChip missing:

1. Verify registration.
2. Verify USB 2 and SuperSpeed.
3. Verify Pimax software stack.
4. Require a complete group restart recipe before any software mutation.
5. Verify EyeChip.
6. Report eye tracking availability.

Display missing:

1. Verify registration.
2. Verify DisplayPort path.
3. Do not treat USB registration repair as guaranteed display repair.
4. Report DisplayPort reconnect limitation.

Audio missing:

1. Verify registration.
2. Verify MMDEVAPI/audio endpoint.
3. Require a complete group restart recipe before any software mutation.
4. Recheck endpoint.
5. Report sound availability.

## Outcome Model

Live planning outcomes in this phase are limited to:

- `noRepairNeeded`
- `repairPlanned`
- `physicalUsbConnectionRequired`
- `displayPortConnectionRequired`
- `unsupportedAutomaticRecovery`
- `conflictingEvidence`
- `unknown`

The planner does not emit `repaired`, because no repair is executed.

`softwareRepairCandidate` is not used for Pimax Play/runtime group restart while the recipe is incomplete; those states use `unsupportedAutomaticRecovery`.

## Verification Contract

A future repair must never be called successful merely because:

- a process restarted;
- a service entered Running;
- USB devices reappeared;
- the command completed without exception.

Full repair success requires:

- registration is `registeredReady / confirmed`;
- all required core components are present;
- no new blocking issue appears;
- no unrelated-device regression is detected.

Partial repair requires registration ready and core VR usable, while optional or feature-specific components may still be missing.

Failed repair examples include registration still unavailable, core USB still absent, software stack restart failure, a new blocking component disappearing, or contradictory evidence.

## Low-Intrusion Policy

Allowed during future critical initialization or re-enumeration windows:

- one pre-health snapshot;
- operation timestamps;
- lightweight existing Phase 29D-E recording;
- one post-health snapshot;
- bounded passive wait.

Avoid:

- repeated SetupAPI enumeration;
- continuous MMDEVAPI polling;
- continuous process/service snapshots;
- named-pipe inventory loops;
- localhost endpoint loops;
- continuous Pimax log tailing;
- repeated component-health commands.

Default future timing:

1. Take one pre-snapshot.
2. Perform the confirmed planned action in a later phase.
3. Wait through a passive settle window.
4. Take one post-snapshot.
5. Take at most one delayed confirmation snapshot if the first post-snapshot is still initializing.

## Concurrency Policy

Only one Pimax repair operation may run at a time.

Repair conflicts with:

- another repair;
- Pimax USB experiment;
- Configurator Scan when it touches the same stack;
- live Connect observer;
- active deployment update.

Component-health assessment may be allowed outside a critical repair window. TUI and CLI must share the same backend operation lock in the later execution phase. Cancellation must not leave partial action ownership behind.

This phase defines and tests the policy model. It does not add a live global mutex that changes current application behavior.

## Future Backend Commands

The stable future protocol names are:

- `pimax-repair-start-json`
- `pimax-repair-status-json`
- `pimax-repair-cancel-json`
- `pimax-repair-result-json`

Start response fields:

- accepted;
- operation ID;
- initial stage;
- requires confirmation;
- plan summary.

Status fields:

- operation ID;
- stage;
- current action;
- completed actions;
- warnings;
- elapsed time;
- cancellation availability.

Final result fields:

- outcome;
- pre-health;
- post-health;
- component changes;
- blocking issues;
- degraded features;
- human-readable summary;
- required operator action if unresolved.

## Future TUI Contract

Future presentation:

```text
Repair Pimax Connection

Current status: Pimax not registered

Planned actions:
[x] Assess headset components
[ ] Verify Pimax software stack
[ ] Verify approved group restart recipe
[ ] Wait for stack stabilization
[ ] Verify registration and features

Automatic limitations:
- Pimax Play Connect cannot currently be invoked
- Physical USB reconnection cannot be performed
```

The final TUI button should wait until backend execution is validated in a later phase. It remains blocked while the allowlist contains no executable Pimax Play/runtime group target.

## Phase 28D2-B Backend

The next backend phase is documented here:

```text
docs/development/pimax-software-stack-repair-backend.md
```

It implements the software-stack-only execution backend, target allowlist, durable diagnostics, cancellation, timeout handling, and post-health result verification while preserving the same Connect, USB, and DisplayPort limitations.

## Next Phase

Recommended next implementation phase:

```text
Phase 28D2-BV2 - One-Shot Validation of the Candidate Pimax Process-Group Launch Recipe
```

That phase must use an exact confirmation gate, launch the candidate once from an absent group, and stop without retry if required process-group or readiness evidence does not form. TUI exposure should wait until the allowlist contains an executable target with a validated side-effect declaration and restart recipe.
