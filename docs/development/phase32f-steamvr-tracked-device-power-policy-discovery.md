# Phase 32F SteamVR Tracked-Device Power Policy Discovery

Status: discovery and read-only live inventory complete; no controller, tracker, HMD, base-station, USB, SteamVR-setting, Supervisor-config, or SteamVR process state was changed. Implementation and destructive live acceptance remain blocked pending review of the final-exit ownership contract and the unavailable per-device capability property described below.

Baseline: `phase/32e-pimax-reconnect-scoped-recovery` at `7797e090f7309d3a7549f7cf236501924884d390`.

## Installed SteamVR behavior

Discovery on 2026-07-20 found SteamVR app `250820`, build ID `23791826`. The installed default settings contain:

```json
"power": {
  "powerOffOnExit": true,
  "turnOffScreensTimeout": 5.0,
  "turnOffControllersTimeout": 300.0
}
```

The active `steamvr.vrsettings` overrides `turnOffControllersTimeout` to `1800` seconds and does not contain `powerOffOnExit`. Two live `IVRSettings_003.GetBool("power", "powerOffOnExit")` reads returned `true` with `VRSettingsError_None`. The value source is the installed `default.vrsettings`, whose value is `true`; there is no user override. The SteamVR settings schema exposes `/settings/power/powerOffOnExit` as the controller-off-on-exit toggle.

The user setting and installed default must be read through `IVRSettings` when available. Direct JSON editing is not recommended because SteamVR owns the file and may rewrite it. A missing user value is not equivalent to an explicit `false`; effective-value and value-source evidence must be kept separate.

## OpenVR supported boundary

The public OpenVR client API supports the required read-only discovery:

- iterate tracked-device indices from `0` through `k_unMaxTrackedDeviceCount - 1`;
- read `GetTrackedDeviceClass` and `IsTrackedDeviceConnected`;
- read `Prop_SerialNumber_String`, `Prop_ModelNumber_String`, and `Prop_DeviceCanPowerOff_Bool` while preserving each property error;
- initialize as `VRApplication_Background`, which does not start SteamVR when the runtime is absent.

Valve's public client API does not expose a generic per-device power-off method. `IVRDebug::DriverDebugRequest` is device-specific, and its request strings are not a portable power contract. Lighthouse console commands, USB disable/enable, dongle manipulation, private vrmonitor IPC, and vendor-specific debug strings are therefore rejected for this feature.

At the driver boundary, SteamVR calls `ITrackedDeviceServerDriver::EnterStandby` when a user requests device power-off, the system enters standby, or the system shuts down. The safest supported cross-device mechanism is consequently SteamVR's own normal shutdown with `power.powerOffOnExit`, allowing SteamVR and each device driver to decide whether and how a capable device enters standby.

Primary references:

- [Valve OpenVR header](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr.h)
- [Valve OpenVR driver API documentation](https://github.com/ValveSoftware/openvr/blob/master/docs/Driver_API_Documentation.md#itrackeddeviceserverdriver)

## Live tracked-device inventory

On 2026-07-20, a temporary external `VRApplication_Background` probe attached to the already-running SteamVR runtime through `FnTable:IVRSystem_026` and `FnTable:IVRSettings_003`. It enumerated every non-invalid slot twice. Both captures returned the same eight devices, indices, connection states, serials, models, property values, and property errors.

| Index | Class | Connected | Serial | Serial/model reads | Model | `Prop_DeviceCanPowerOff_Bool` | Capability property read |
| ---: | --- | --- | --- | --- | --- | --- | --- |
| 0 | HMD | `true` | `P30100P201382100320` | `TrackedProp_Success` | Pimax Crystal | returned `false` | `TrackedProp_UnknownProperty` (4) |
| 1 | TrackingReference | `true` | `LHB-22CEE79A` | `TrackedProp_Success` | Valve SR Imp | returned `false` | `TrackedProp_UnknownProperty` (4) |
| 2 | Controller | `true` | `LHR-9A72154F` | `TrackedProp_Success` | Knuckles Right | returned `false` | `TrackedProp_UnknownProperty` (4) |
| 3 | Controller | `true` | `LHR-2991749B` | `TrackedProp_Success` | Knuckles Left | returned `false` | `TrackedProp_UnknownProperty` (4) |
| 4 | GenericTracker | `true` | `LHR-4595ED95` | `TrackedProp_Success` | VIVE Tracker 3.0 MV | returned `false` | `TrackedProp_UnknownProperty` (4) |
| 5 | GenericTracker | `true` | `LHR-C9C81BCA` | `TrackedProp_Success` | VIVE Tracker 3.0 MV | returned `false` | `TrackedProp_UnknownProperty` (4) |
| 6 | GenericTracker | `true` | `LHR-F550BEC7` | `TrackedProp_Success` | VIVE Tracker 3.0 MV | returned `false` | `TrackedProp_UnknownProperty` (4) |
| 7 | TrackingReference | `true` | `LHB-2BB29CAB` | `TrackedProp_Success` | Valve SR Imp | returned `false` | `TrackedProp_UnknownProperty` (4) |

The capability result is not a successful `false`. OpenVR returned the default boolean value together with `TrackedProp_UnknownProperty` for every device, including both Index controllers and all three Vive Trackers. The installed driver/runtime combination therefore exposes no usable public `Prop_DeviceCanPowerOff_Bool` capability signal for these devices. Phase 32F must preserve the error and must not coerce it to either capable or incapable.

### Completed read-only inventory gate

Raw and summarized evidence is stored outside the repository in `Phase32F-read-only-inventory-20260720-225725`. The two authoritative raw captures have SHA-256 values:

- capture 1: `39725d82e3037329d433b7cf4b2f7c1b1045fc9a9fb98231ef4488993b550cde`;
- capture 2: `42604ceab5cdbe86c4f06a9a46e043798a229c350dfd06bf4edf3419fcb3411a`.

The differing hashes reflect capture timestamps. Parsed device arrays and power-setting objects are identical. Each capture proves that `vrserver`, `vrmonitor`, `vrcompositor`, and `vrdashboard` retained the same PIDs and start times before and after the read. SHA-256, length, and last-write metadata also remained identical for the user SteamVR settings, installed default settings, and Supervisor configuration.

The probe did not expose or call a debug request, settings setter, standby/power operation, `vrstartup.exe`, process launcher, or USB API. An initial non-authoritative probe attempt failed locally because its temporary `IVRSystem_026` table projection omitted the documented `ComputeDistortionSet` slot. It produced no inventory; the failure and containment check are retained in the external evidence bundle. The corrected table was verified against Valve's header before the two authoritative captures.

## Policy recommendation

Add a Configurator enum with a backwards-compatible default:

| Policy | Recommended meaning |
| --- | --- |
| `FollowSteamVr` | Never mutate the SteamVR setting. On a native SteamVR exit, SteamVR uses its effective `power.powerOffOnExit` value. This is the default and preserves current behavior. |
| `PowerOffOnFinalExit` | For a Supervisor-owned **final normal SteamVR shutdown only**, use a bounded setting transaction that makes the effective value `true` before the one normal shutdown request. Let SteamVR power off only devices its drivers consider capable. Restore the prior setting after runtime exit, with crash-safe recovery evidence. |
| `LeavePoweredOnFinalExit` | For a Supervisor-owned **final normal SteamVR shutdown only**, use the same transaction with an effective value of `false`, then restore the prior setting after runtime exit. Do not send any per-device wake or keepalive command. |

The two deterministic policies cannot be guaranteed for a SteamVR exit that was already initiated externally: by the time Supervisor observes process loss, SteamVR may already have read the setting and entered driver shutdown. Implementation must not pretend that a post-exit write changed that exit. Review must choose one of these honest contracts:

1. deterministic modes apply only when Supervisor owns and requests the final normal SteamVR shutdown; external SteamVR UI Exit follows SteamVR; or
2. Supervisor gains a reviewed final-exit flow that requests SteamVR shutdown, while the existing reactive UI-exit path remains observational.

No public supported mechanism was found that can deterministically power off capable controllers and trackers while leaving SteamVR running. Phase 32F must not substitute a private or vendor-specific command to fill that gap.

The live result narrows the recommendation further: `Prop_DeviceCanPowerOff_Bool` can be recorded diagnostically, but it cannot be an execution allow-list on this system because every intended controller and tracker returns `TrackedProp_UnknownProperty`. Phase 32F2 must either rely exclusively on SteamVR's native `powerOffOnExit` behavior during a Supervisor-owned final normal SteamVR shutdown, or remain blocked. It must not add per-device fallback commands.

## Eligibility and exclusions

Policy execution requires one affirmative, immutable cleanup context created at final-exit acceptance:

```text
FinalNormalSessionCleanup
and SupervisorOwnsFinalSteamVrShutdown
and not VrSessionRestartActive
and not SteamVrReplacementOrRecovery
and not PimaxScopedRecovery
and not ClientReconnect
and not UsbTopologyRecovery
```

All conditions fail closed. The context must not be inferred later from a missing process. It must be passed explicitly to the single shutdown coordinator that owns the final request.

The setting transaction and any future eligible-device diagnostics must never run for:

- action-7 restart, including request acceptance, old-runtime shutdown, replacement adoption, readiness wait, VRChat resume, or managed-app restoration;
- automatic or observed SteamVR runtime replacement, chained replacement, ambiguous loss, or manual-recovery waiting;
- Pimax scoped reconnect recovery or PiService readiness processing;
- TUI/overlay/client disconnect, reconnect, window close, or request redelivery;
- Index controller or Vive Tracker dongle removal/reinsertion, or any other USB topology observation;
- emergency cleanup, crash cleanup, process force-kill, watchdog activity, or Supervisor startup probes.

Device-class exclusions are defense in depth. Diagnostic inventory may record every class, but a capability view may label a device capable only when a connected `Controller` or `GenericTracker` returns `TrackedProp_Success` and `Prop_DeviceCanPowerOff_Bool == true`. The current capture labels no device capable because the property is unknown. This diagnostic result does not authorize per-device commands and does not override SteamVR's native shutdown handling. `HMD`, `TrackingReference`, `DisplayRedirect`, invalid/unknown classes, and every Windows USB/PnP object remain excluded. Base-station power remains exclusively owned by the accepted Bluetooth base-station lifecycle.

## Preservation of Phase 32D and Phase 32E

Phase 32F must remain downstream of the accepted lifecycle boundaries:

- Phase 32D keeps exactly one accepted action-7 operation and exactly one `vrstartup.exe -shutdown` issuance, with no retry or queued duplicate.
- Expected old-runtime disappearance during action 7 remains classified as restart activity and cannot enter normal cleanup.
- Replacement adoption, monitor preservation, base-station preservation, conditional VRChat resume, and managed-app restoration are unchanged.
- Phase 32E Pimax reconnect remains a scoped application recovery. It cannot request SteamVR shutdown or enter tracked-device power policy code.
- SteamVR radio dongles remain unrelated USB topology. Removal or reinsertion produces no recovery plan, shutdown request, setting transaction, or later deferred action.
- Client reconnect and duplicate command delivery remain observational and cannot replay either restart or final-exit policy.

## Test strategy before destructive live acceptance

### Deterministic automated tests

1. Config parsing accepts exactly the three policy names, defaults missing values to `FollowSteamVr`, and rejects invalid values without mutating SteamVR.
2. A table-driven eligibility test covers final normal cleanup plus every excluded action/recovery/reconnect/USB context above.
3. `FollowSteamVr` makes zero setting writes for every lifecycle path.
4. Deterministic modes make one bounded setting transaction only for a Supervisor-owned final normal shutdown, and make zero writes for action 7 and all recovery paths.
5. Setting reads distinguish explicit user values, installed defaults, missing values, and API errors. Any read/write/journal failure fails closed before shutdown-policy ownership is claimed.
6. Transaction tests cover prior `true`, prior `false`, absent user override, runtime disappearance, cancellation, process crash between write and restore, idempotent recovery, and no duplicate restore.
7. Inventory tests preserve indices and property errors, include all connected classes in evidence, mark only successful `true` controller/tracker properties as capable, and retain `TrackedProp_UnknownProperty` without coercion.
8. HMDs, tracking references/base stations, display redirects, invalid classes, and USB/PnP identities are never eligible.
9. Existing Phase 32D/32E tests remain unchanged and required, especially shutdown issue count, expected-shutdown classification, replacement adoption, Pimax scoped dispatch, client redelivery, and Valve `28DE:2101` dongle exclusion.
10. Source guards reject `lighthouse_console`, device debug requests, SetupAPI/USB mutation, direct SteamVR JSON editing, and a second `vrstartup.exe -shutdown` call site.

### Read-only live tests before review

1. Complete: with SteamVR already running and devices powered, two captures returned stable index/serial/class/model/capability results.
2. Deferred: do not stop SteamVR for this discovery pass. A later already-stopped observation may confirm background init returns no-server without launching SteamVR.
3. Complete: `IVRSettings` returned effective `true`; the user file has no override and the installed default is `true`.
4. Complete: process identities and guarded file hashes/timestamps were unchanged. No device, base-station, monitor, USB, or configuration mutation was requested.

### Destructive live acceptance after explicit review only

1. Prove `FollowSteamVr` matches both native setting values without Supervisor writes.
2. Prove `PowerOffOnFinalExit` powers off connected capable Index controllers and Vive Trackers during one Supervisor-owned final normal SteamVR exit, while the HMD, base stations, and USB devices receive no Phase 32F command.
3. Prove `LeavePoweredOnFinalExit` leaves those devices powered during the same final-exit shape.
4. For each deterministic policy, run action 7 and prove controllers/trackers stay powered, the setting transaction is absent, one shutdown request occurs, replacement adoption succeeds, and Phase 32D state is preserved.
5. Exercise Pimax scoped recovery, client reconnect, and separate Index/Vive dongle remove/reinsert tests and prove zero policy execution and zero delayed action.
6. Verify the prior SteamVR setting is restored after the final exit and after simulated interrupted-transaction recovery.

No Phase 32F2 implementation or destructive live acceptance should begin until the final-exit ownership contract and the `TrackedProp_UnknownProperty` implication are reviewed.
