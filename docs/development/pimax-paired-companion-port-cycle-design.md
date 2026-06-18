# Paired companion Pimax port-cycle design

Phase 28C3C is analysis and simulation only. The CLI-only `pimax-usb-paired-port-cycle-design-json` command emits one `pimax-usb-paired-port-cycle-design-v1` JSON document. It has no execute mode, never requests UAC, opens no hub handle, and cannot submit `IOCTL_USB_HUB_CYCLE_PORT`.

## Evidence index

The design independently used the hashed Phase 28C3B-R archive `Phase28C3B-R-20260618-222436`:

- `implementation-manifest.json`: source commit, exact request boundary, and full binary hashes.
- `connect-and-port-cycle/phase-28c3b-r-privileged-request.json`: immutable request hash, exact target, companion proof, observer and marker binding, nonce, expiry, Phase 29 boundary, Vive exclusion, and unrelated-port inventory.
- `connect-and-port-cycle/phase-28c3b-r-privileged-result.json`: helper PID, request entry/return timestamps, native results, and request count.
- `connect-and-port-cycle/observer-result.json` and `phase-28c3b-r-port-timeline.json`: 250 ms observer timeline, USB 2 changes, stable SuperSpeed companion, stable Vive, and stable unrelated ports.
- `connect-and-port-cycle/phase-28c3b-r-user-observation.json`: blue LED, error 10500, and unavailable registration.
- `manual-restoration/final-registration.json`: `registeredReady / confirmed` after the operator-guided physical reseat.
- `hashes/phase-28c3b-r-file-hashes.json`: all 122 listed lengths and SHA-256 values verified.

The locked hashes were supervisor EXE `ACA013F09F1D4492E56B1661EBAB91D0E35F1B43C620EF72EAA4C17AE5D5554A`, supervisor DLL `CA35382A425E1F35D4A30463FE1C06B9451B0AAC69775D7F83FC846759F8D0BB`, helper EXE `4F8F038A9FFC01395F2E1930E6AC20358793CC4E85927934AFF9B3B7C2DF05AB`, and helper DLL `C368C995131E8127F0C251EF685D339EC008CC68250B4B108062183595C56E77`.

## Single-call timing

The helper recorded DeviceIoControl entry at `2026-06-18T18:45:43.6268923Z` and return at `2026-06-18T18:45:43.6273411Z`, a call duration of `0.4488 ms`. The observer first recorded USB 2 descendant disappearance/change at `18:45:44.0034902Z`, `376.5979 ms` after entry and `376.1491 ms` after return. It first recorded the USB 2 branch returning/enumerating at `18:45:47.1952115Z`, `3568.3192 ms` after entry; connected descendants followed at `18:45:47.8080179Z`.

These are wall-clock timestamps from separate helper and observer processes. Both were on the same machine and UTC/local offsets align, but they are not a shared monotonic clock. The observer samples nominally every 250 ms and each collection itself takes time. The result is therefore **asynchronous acceptance, high confidence**: the API returned well before observable activity, but the evidence does not locate the kernel's exact initiation instant. Helper start-to-entry and helper completion-to-return are not separately instrumented, so only result-file boundaries can be stated for them.

The current adapter uses `CreateFileW` flags `0` and a null `OVERLAPPED` pointer. It is synchronous at the Win32 call boundary and does not support overlapped I/O. The fast return shows that this driver accepted the cycle before recovery; it does not establish atomic paired semantics.

## Candidate decision

| Candidate | Overlap | Physical resemblance | Partial failure | Determinism | Verdict |
| --- | --- | --- | --- | --- | --- |
| SuperSpeed then USB 2 | Conditional | Partial | One side may recover or fail first | Fixed order | Conditionally acceptable, but order adds no safety benefit |
| USB 2 then SuperSpeed | Conditional | Poor | Begins with the proven insufficient side | Fixed order | Rejected |
| Near-concurrent | Best available, never atomic | Best software approximation | Explicit one-side acceptance risk | Barrier and per-side journals | Selected |
| No paired software cycle | None | Physical reseat only | None from software | Highest | Required fallback if future guards cannot pass |

The selected verdict is **Design C: near-concurrent paired submission**. The observed physical reconnect order, SuperSpeed then USB 2 about 0.754 seconds later, is a reference only. It does not prove a safe software order. Near-concurrent release avoids encoding an unsupported order and minimizes expected skew. There is no atomic Windows paired-port operation, so one request may be accepted while the other is rejected or never entered.

## Exact safety boundary

The only proposed targets are the evidence-proven reciprocal external-hub companions: SuperSpeed `05E3:0626` index 4 and USB 2 `05E3:0610` index 4. The command uses `sanitized-fixture:pimax-physical-connector`; the current machine's connector and device identities remain in the external evidence/request. Index 2 on both sides is the excluded Vive connector. Root hubs, controllers, other hubs, other indices, target substitution, retries, fallback, whole-hub reset, and compensating mutation are forbidden.

Before a future barrier release, the helper must open both exact handles, revalidate both targets, reciprocal companionship, full Pimax occupants, Vive exclusion, unrelated-port inventory, awaiting-registration state, Pimax Play running, SteamVR closed, active observer, fresh marker sequence, exact confirmation, nonce, expiry, and request hash. Any failure means zero calls. The marker order is observer started, Info opened, Pimax Crystal selected, Connect ready, then Connect/scan started. A new explicit UAC request is required only in a future hardware phase.

The hard limit is two total calls and one call per side. Dedicated workers reach a common ready barrier; neither submits early. One release starts both workers. Monotonic entry and return timestamps measure submission skew. Thread scheduling prevents a zero-skew or atomicity claim.

## Partial failure and crash model

Both accepted means read-only observation continues. If only one side is accepted, or either native call rejects or throws after entry, record a partial-pair failure, never retry either side, finish evidence, and require operator-guided physical restoration afterward. Cancellation or topology change before release produces zero calls. A change after release never triggers compensation.

A future helper should preallocate or append safely to two side-specific progress records before the barrier. Each record must distinguish ready, native-call-entered, returned, rejected, threw, and incomplete/unknown. This preserves whether a request may have entered when the helper crashes. The final aggregate result is written atomically when possible, but the operation itself is never described as atomic and a crash may leave only progress records.

## Proposed schemas

`pimax-usb-paired-port-cycle-request-v1` contains experiment ID, operation kind, selected strategy, both exact targets, reciprocal proof, Pimax connector and occupants, Vive exclusion, unrelated inventory, observer session, Info/selection/Connect markers, freshness, confirmation binding, expiry, one-time nonce, total maximum 2, per-side maximum 1, retry `none`, fallback `none`, partial-failure policy, and result path. Machine identities remain outside Git.

`pimax-usb-paired-port-cycle-result-v1` represents zero, one, or two submissions; per-side ready/entry/return timestamps; barrier time and skew; Win32/native status; counts; acceptance/rejection; pending/incomplete/crash state; immutable request hash; helper PID; elevation; strategy; topology and registration results; safety failures; warnings/errors; and manual-restoration requirement.

## Hardware lockout and future criteria

The design coordinator depends only on `IPimaxUsbPairedSimulationAdapter`. The command constructs the deterministic fake implementation. No paired schema is recognized by the single-side UAC helper, no paired launcher or execute mode exists, and no code in the paired file references `CreateFileW`, `DeviceIoControl`, or the real native adapter.

A future controlled hardware phase is eligible only after review confirms the exact pair can still be preopened and revalidated, side progress survives partial completion, two calls remain the absolute maximum, measured skew is retained, the observer and marker workflow is ready before UAC, and manual physical restoration is available. Paired behavior has not been tested and is not claimed to reproduce physical reseat.
