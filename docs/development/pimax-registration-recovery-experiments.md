# Pimax Registration Recovery Experiments

This page defines controlled, read-only-first recovery experiments for Pimax headset registration. The first implemented recovery experiment is a CLI-only, operator-confirmed Pimax Play client restart test. It is intentionally not automatic recovery.

## Evidence States

- `LikelyHeadsetOff`: the headset power-on evidence group is absent or non-present.
- `LikelyPoweredOnAwaitingRegistration`: the power-on evidence group is present, but Crystal runtime evidence is absent.
- `RegisteredReady`: the power-on group, Crystal runtime group, and filtered connectivity evidence agree that the headset is ready.
- `ConflictingEvidence`: filtered and expanded evidence disagree.
- `Unknown`: evidence is incomplete.

LED colors remain user-observed physical context. The software state names above are based on USB/PnP and runtime evidence, not direct LED detection.

Phase 28A5 found that the white-to-blue transition is visible in static USB/PnP evidence through the Generic USB Hub `VID_05E3/PID_0608` and Valve/Pimax-adjacent HID/composite records `VID_28DE/PID_2101` and `VID_28DE/PID_2300`. Crystal runtime evidence appears after the USB-assisted registration path and includes Crystal `VID_34A4/PID_0012` records, the `MI_00` camera, `MI_02` HID, `MI_03` audio, related audio endpoint records, and EyeChip `VID_2104/PID_0220`. The filtered Phase 28A4 green capture contained six relevant Crystal devices, not seven.

## Safety Model

Recovery experiments must require explicit manual confirmation before mutation, run one experiment at a time, avoid uncontrolled retries, support cancellation, apply timeouts to every step, identify the exact process/service/device target, assess before and after each action, and skip execution during Supervisor cleanup or active SteamVR use unless the operator explicitly approves. No broad USB host-controller reset is allowed.

The implemented client-restart experiment is limited to the verified Pimax Play UI/client process. It does not restart Pimax services, manipulate USB/PnP devices, automate Connect, start SteamVR, add a bridge command, add a Terminal UI action, or run in the background.

The first controlled client-restart trial did not recover registration for the captured blue/unregistered failure. The verified Pimax Play UI/client closed and relaunched, but the assessment remained `LikelyPoweredOnAwaitingRegistration` through the bounded observation window. The experiment framework was then hardened so the relaunched GUI process cannot contaminate CLI JSON stdout.

The Phase 28C2 service-restart experiment was not executed. Target validation failed because `PiServiceLauncher` behaves as a transient Connect-triggered launcher rather than a stable persistent runtime service, and Windows recorded abnormal launcher exits. No Pimax service should be restarted until the persistent registration path is mapped. The read-only [`pimax-connect-lifecycle-observe-json`](pimax-connect-lifecycle-observation.md) command collects synchronized service, process, registration, USB/PnP, event-log, and Pimax-log evidence for that mapping.

Phase 28C3 also stopped safely. No exact-devnode re-enumeration was implemented: no single PnP ancestor covered every Pimax USB branch without also including unrelated devices, and the first common scopes were the root hub and xHCI controller. Those broad mutation targets remain prohibited. Phase 28C3A therefore investigates the exact downstream external-hub connector with the read-only [`pimax-usb-physical-port-map-json`](pimax-external-hub-physical-port-mapping.md) command before any future recovery experiment is considered.

## Experiment Matrix

| Order | Experiment | Prerequisite state | Action | Expected transition | Success criterion | Failure criterion | Timeout | Rollback | Admin | SteamVR guard | Risk | Evidence captured | Status |
|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Wait-only control | `LikelyPoweredOnAwaitingRegistration` | Do nothing except repeat assessment. | None, or spontaneous registration. | State becomes `RegisteredReady` without mutation. | State remains awaiting registration. | Bounded by request, clamped by implementation. | None | No | SteamVR closed preferred. | Low | Assessment timeline. | Implemented as `pimax-recovery-experiment-json --experiment wait-control` |
| 2 | Restart Pimax Play UI/client only | Awaiting registration, Pimax services running. | Close and restart only the user-facing Pimax Play client after dry-run token confirmation. | Runtime client re-registers already-present devices. | `RegisteredReady`. | Client restarts but state remains awaiting registration. | Bounded; one restart only. | Relaunch client once if needed. | Maybe | Refuses while SteamVR is running. | Medium | Process target, safety checks, stage timeline, assessment timeline. | Implemented; first controlled trial did not recover the captured failure |
| 3 | Restart relevant Pimax runtime/service only | Awaiting registration, exact service identity known. | Restart only the narrow runtime/service target. | Service re-reads present power-on devices. | `RegisteredReady`. | Service fails or state unchanged. | 90s | Restore service running. | Yes | SteamVR must be closed unless approved. | Medium-high | Service status, assessment before/after. | Not implemented |
| 4 | Restart service, wait, then client | Awaiting registration. | Restart service, wait for readiness, restart client. | Runtime stack reinitializes in order. | `RegisteredReady`. | Timeout or state unchanged. | 120s | Restore service/client running. | Yes | SteamVR closed. | Medium-high | Service/process state, assessment before/after. | Not implemented |
| 5 | Initiate Pimax Connect scanning only | Awaiting registration, supported safe UI/API method exists. | Start Connect scan without USB manipulation. | Scan recognizes already-present power-on evidence. | `RegisteredReady`. | Scan times out. | 90s | Stop scan if supported. | Unknown | SteamVR closed. | Medium | Assessment before/during/after scan. | Not implemented |
| 6 | Exact-device software re-enumeration only | Awaiting registration, exact target identity known. | Re-enumerate only the relevant device, not hub/controller. | Fresh device arrival without Connect scan. | `RegisteredReady`. | Device remains awaiting registration or disappears. | 90s | Reassess and prompt operator. | Yes | SteamVR closed. | High | Target identity, before/after assessment. | Not implemented |
| 7 | Connect scan plus exact-device re-enumeration | Awaiting registration, exact target identity known. | Start Connect scan, then re-enumerate exact device. | Matches known physical USB reseat sequence in software. | `RegisteredReady`. | Timeout or device error. | 120s | Stop scan, reassess, prompt operator. | Yes | SteamVR closed. | High | Timeline, target identity, assessment before/after. | Not implemented |
| 8 | Physical USB reseat control | Awaiting registration, operator present. | Operator starts Connect scan and physically reseats only headset USB. | Known working control path. | `RegisteredReady`. | State remains awaiting registration. | Operator-defined | Reconnect USB and reassess. | No software admin | SteamVR closed. | Physical intervention | Timeline and before/after assessment. | Manual control only |

## Open Questions

- Whether a supported Pimax Play Connect scan API or command exists.
- Which service, if any, can be safely restarted without affecting unrelated Pimax devices.
- Whether exact-device software re-enumeration can target only headset-side identities without resetting hubs or controllers.
- Whether repeated event-timeline captures are needed before any software mutation experiment.
