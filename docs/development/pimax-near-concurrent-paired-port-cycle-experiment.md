# Near-concurrent paired Pimax port-cycle experiment

Phase 28C3D adds a CLI-only, operator-confirmed experiment. It does not add automatic recovery to the bridge, lifecycle actions, TUI, overlay, Configurator, or startup.

## Evidence basis

Phase 28C3B-R proved that one USB 2 index-4 cycle was accepted in 0.4488 ms and observable USB 2 activity began about 376.6 ms later. SuperSpeed did not transition and registration remained unavailable. Phase 28C3C therefore classified the call as asynchronous acceptance and selected a near-concurrent paired design instead of imposing an unsupported sequential order.

The exact evidence-bound targets are external SuperSpeed hub `05E3:0626` index 4 and external USB 2 hub `05E3:0610` index 4. Both must remain reciprocal companions on the proven Pimax connector. Vive remains excluded at index 2 on both sides, and unrelated ports must match the approved inventory.

## Command and helper

`pimax-usb-paired-port-cycle-experiment-json` emits `pimax-usb-paired-port-cycle-experiment-v1`. Its dry-run, prepare, and observe-result modes reuse the full topology, registration, process, marker-freshness, nonce, expiry, token, request-hash, and Phase 29B guards. Observe-result is read-only and correlates the final paired helper result with bounded topology and registration samples. The existing `pimax-usb-paired-port-cycle-design-json` command remains non-mutating.

Execution is restricted to `PimaxVrcSupervisor.PairedPortCycleHelper.exe`, which accepts only `pimax-paired-companion-port-cycle-request-v1`. The existing single-side helper retains its one-call-only contract and rejects this paired command.

## Non-atomic barrier

The helper opens both exact synchronous hub handles before mutation, prepares one dedicated worker per side, waits until both report ready, and performs final live validation. Only then may one shared gate release both workers. The scheduler decides which call enters first. The helper records signed and absolute entry skew with a monotonic high-resolution clock; the design target is below 50 ms and no retry occurs if actual skew is larger.

This operation is not atomic. One request can enter or succeed while the other fails. The hard boundaries are two requests total, one SuperSpeed request, and one USB 2 request. Counters increment immediately before native entry. There is no retry, fallback, compensation, target substitution, whole-hub reset, controller reset, device state operation, process/service action, or UI automation.

## Durable evidence

Each side has a write-through JSONL progress journal outside Git. Records include request validation, handle opening, target revalidation, worker readiness, barrier release, native entry/return, handle close, and worker completion, with experiment, side, monotonic and UTC timestamps, PID, thread ID, count, native result, and error. The final aggregate result is written atomically when possible. If it is absent, journals distinguish zero, one-side-possible, both-submitted, and unknown states. Incomplete evidence never causes a compensating software action.

## Operator boundary

The observer must be active before Pimax Play interaction. Required immutable markers are observer started, Info opened, Pimax Crystal selected, Connect readiness, and Connect pressed/scan started. The exact phrase is `CONFIRM NEAR-CONCURRENT PIMAX PAIRED PORT CYCLE EXPERIMENT`. Preparation consumes a one-time token, binds the exact confirmation phrase and experiment identity into the request hash, requires distinct side-specific progress journals, and creates a request valid for at most 60 seconds. A new UAC approval launches the dedicated helper once.

Both accepted, one-side rejected, exception-before-entry, exception-after-entry, helper crash, cancellation, and pre-release topology change have explicit results. Any partial state requires completion of read-only evidence followed by operator-guided physical restoration. Restoration uses a new Info, Pimax Crystal, Connect/scan, and physical Pimax USB-only reseat sequence; it is never automated.

## Classification

- Full paired transitions, full descendants, confirmed registration, green LED, stable Vive, and stable unrelated ports: paired recovery reproduced safely.
- Both sides transition without registration: full paired-cycle non-recovery.
- Only one side submits or transitions: partial paired outcome.
- Native rejection: retain physical reseat recovery.
- Vive or unrelated change: safety failure.
- Zero calls: correct preparation and repeat only with a fresh complete sequence.
- Spontaneous recovery before mutation: repeat timing controls.
- Possible submission with incomplete evidence: analyze journals without another mutation.

No experiment result authorizes automatic recovery. At the time this implementation was committed, the controlled hardware experiment had not yet been performed and no success claim was made.
