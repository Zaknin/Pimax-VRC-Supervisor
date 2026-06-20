# Pimax Component Health And Connect Routine Mapping

Phase 28D1 adds a read-only component-health foundation for the future `Repair Pimax Connection` action and a bounded development observer for mapping what the Pimax Play `Info -> Pimax Crystal -> Connect` button changes.

## Phase 28C3D Conclusion

The paired companion-port cycle experiment proved that the exact Pimax SuperSpeed index 4 and USB 2 index 4 could be cycled near-concurrently while the Vive face tracker stayed on its separate index 2. Both native requests succeeded and the PC stayed stable, but Pimax registration still failed, the headset LED remained blue, and Pimax Play still showed error `10500`.

A real physical Pimax USB reseat after initiating the Pimax Play Connect routine restored `registeredReady / confirmed`, green LED, MMDEVAPI endpoints, Pimax recognition, and cleared the error. That means software USB cycling remains experimental evidence only. It is not exposed as a product repair action.

## Manual Recovery Workflow

The normal successful physical recovery sequence remains:

1. Open Pimax Play.
2. Open `Info`.
3. Select `Pimax Crystal`.
4. Press `Connect`.
5. Within about one to two seconds, physically disconnect and reconnect only the Pimax USB cable to the same port.

Connect alone usually does not register the headset. A USB reseat without first initiating Connect usually does not allow Pimax Play to find and register the headset. DisplayPort reseating is separate and is used for missing image or DisplayPort audio after the Pimax initialization path has been activated.

## Component Health Command

Command:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-component-health-json
```

Schema:

```text
pimax-component-health-v1
```

The command is one-shot and read-only. It does not restart processes or services, reset USB, automate Pimax Play, start SteamVR or VRCFT, modify registry, access the network, or alter the scheduled task.

The command reuses existing safe evidence sources:

| Source | Existing implementation |
| --- | --- |
| Pimax connectivity | `PimaxConnectivitySnapshotCollector` |
| Pimax registration | `PimaxRegistrationStateAssessor` |
| USB/PnP inventory | `PimaxUsbEnumerationSnapshotCollector` |
| Process and service evidence | Existing Pimax connectivity probes |
| SteamVR integration metadata | Existing SteamVR driver probe |
| Audio, microphone, EyeChip, HID, camera, and Vive evidence | Existing sanitized USB/PnP records |

## Component Criticality

Components use these criticality values:

| Criticality | Meaning |
| --- | --- |
| `requiredForRegistration` | Needed before Pimax Play can register the headset. |
| `requiredForCoreVr` | Needed for a usable core VR session. |
| `requiredForFeature` | Needed for a feature such as audio, microphone, or eye tracking. |
| `optionalAccessory` | Useful accessory; absence does not make the headset unusable. |
| `informational` | Integration or support evidence only. |

## Component Matrix

The health result includes:

- `pimaxPlay`
- `pimaxRuntime`
- `pimaxServices`
- `pimaxBackgroundProcesses`
- `coreUsb`
- `usb2Companion`
- `superSpeedCompanion`
- `pimaxRegistration`
- `headsetHid`
- `displayPortVideo`
- `headsetAudioOutput`
- `headsetMicrophone`
- `eyeChip`
- `eyeTracking`
- `trackingCameras`
- `viveFaceTracker`
- `mouthTrackerVrcftIntegration`
- `steamVrIntegration`
- `pimaxOpenVrOpenXrIntegration`

Each component includes an ID, display name, status, criticality, confidence, expected state, observed evidence, reason code, human-readable explanation, and suggested next action category.

## Overall States

The deterministic overall states are:

- `healthy`
- `usableWithDegradedFeatures`
- `notRegistered`
- `coreConnectionMissing`
- `initializing`
- `conflictingEvidence`
- `unknown`

Optional accessories such as the Vive face tracker do not make the entire headset unusable.

## Human-Readable Messages

The model emits deterministic messages for the main user-facing states:

- Windows detects Pimax USB but Pimax Play has not registered the headset.
- Core Pimax USB interface is not detected.
- Pimax SuperSpeed connection is missing.
- DisplayPort video path is not detected.
- Pimax headset audio output is not available.
- Pimax headset microphone is not available.
- EyeChip is not detected.
- Tracking camera interfaces are missing.
- Vive face tracker is not detected.
- Pimax headset connection is healthy.

Normal user-facing output does not include raw PnP IDs, serial numbers, machine names, usernames, or private full paths.

## Capability Summary

The command also emits a concise capability summary:

```text
Core VR: available|unavailable
Display: available|unavailable
Audio: available|unavailable
Microphone: available|unavailable
Eye tracking: available|unavailable
Face tracking: available|unavailable
Pimax registration: ready|not registered|unknown
```

## Connect Routine Observer

Command:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-connect-routine-observe-json `
  --duration-seconds 30 `
  --output-dir C:\Users\FucktoryVR\Documents\PimaxVrcSupervisorDiagnosticsArchive\PimaxRecovery\Phase28D1-ConnectMapping-<timestamp> `
  --scenario connect-only
```

Schema:

```text
pimax-connect-routine-observation-v1
```

The observer requires an explicit duration and output directory. Duration is clamped to 20-45 seconds. It exits after the bounded window and leaves no background child, scheduled task, or persistent monitor.

The observer collects:

- Pimax process snapshots.
- Pimax service snapshots.
- localhost TCP/UDP endpoint inventory.
- relevant named-pipe inventory.
- Pimax log metadata and hashes.
- component-health baseline and final snapshots.
- bounded file-change metadata.

It does not inject into processes, hook APIs, patch binaries, automate the GUI, capture payloads, modify registry, restart anything, invoke Connect, or inspect unrelated user files.

## Privacy Model

The observer and health command sanitize paths and messages through the existing Pimax redaction helpers. Outputs avoid raw serials, raw PnP instance IDs, full private paths, usernames, machine names, and arbitrary network payloads. Log handling is bounded to metadata and sanitized relevant lines.

## Live Observation Results

Evidence root:

```text
C:\Users\FucktoryVR\Documents\PimaxVrcSupervisorDiagnosticsArchive\PimaxRecovery\Phase28D1-ConnectMapping-20260620-141657
```

| Observation | Result |
| --- | --- |
| Connect only | Baseline, final, and post-observation health stayed `notRegistered / likelyPoweredOnAwaitingRegistration`. Display, audio, and microphone evidence remained available, but Pimax registration did not return. PimaxClient `main.log` grew and localhost endpoint churn was observed. |
| Connect plus immediate USB reseat | Physical recovery was reproduced by post-observation health: `healthy / registeredReady / confirmed`, with display, audio, microphone, EyeChip, and tracking cameras available. However, the bounded observer did not capture a clean in-window transition to `registeredReady`. |

The heavy first observer appeared to perturb or delay Pimax registration. During that run, the operator reported that the headset did not connect while the observer was active and that it registered green immediately after the observer stopped. The observer final state was still `notRegistered / likelyPoweredOnAwaitingRegistration`, while post-observation health was `healthy / registeredReady / confirmed`.

The lighter observer removed per-loop component-health polling and kept only lightweight process, service, endpoint, named-pipe, and file metadata sampling. That reduced probe pressure, but the final observer state was still not a clean supported Connect routine map: it ended as `conflictingEvidence / conflictingEvidence`, and the post-observation health snapshot was `healthy / registeredReady / confirmed`.

No safe direct IPC request, process/service orchestration command, or persistent configuration transition was identified. The only confirmed successful recovery path remains the manual Pimax Play Connect action followed by the physical Pimax USB reseat.

## Connect-Routine Classification

Observed classification:

- A: Direct local IPC identified.
- B: Process or service orchestration identified.
- C: Persistent configuration or state transition identified.
- D: Only opaque internal GUI behavior observed.
- E: Evidence insufficient or contradictory.

Selected classification:

```text
D - Only opaque internal GUI behavior observed.
```

The live evidence also contains contradictory timing around the successful registration transition, because registration completed only after the heavier observer stopped and after the lighter observer had already left the observation window. That contradiction is treated as a stop condition for Connect-routine mapping rather than a reason to add stronger live polling.

Phase 28D2-L builds on this component-health model with a non-mutating repair capability and planning layer:

```text
docs/development/pimax-low-intrusion-repair-orchestration.md
```

The selected next implementation phase after repair planning is:

```text
Phase 28D2-B - Implement Confirmed Pimax Software-Stack Repair Actions and Post-Repair Verification
```

## Remaining Limitations

The component-health model is conservative. DisplayPort and OpenVR/OpenXR evidence are only as strong as the currently safe observable metadata. The Connect routine observer identifies local state transitions; it does not prove causality or reproduce the Connect action.
