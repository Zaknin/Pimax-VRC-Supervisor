# Base-station startup failure analysis

## Scope and safety boundary

Phase 29B added structured observation of the existing Supervisor startup and Configurator Scan paths. Phase 29C compares those records offline. It does not scan Bluetooth, connect to a station, issue a command, alter configuration, access or modify the scheduled task from the analysis command, change startup timing, or deploy a correction.

The deployed Phase 29B source is commit `d34715177cdde4887ee362d663b08901a8bc8da9`. Phase 29C is based on `ac5c37c4509bd94bb164b6ad580aa25d947bab2f`, where that instrumentation is already merged. The unmerged Phase 28C3B/C/D experiment stack is excluded.

## Evidence snapshot

The immutable snapshot is outside Git at:

```text
%USERPROFILE%\Documents\PimaxVrcSupervisorDiagnosticsArchive\BaseStationStartup\Phase29C-20260619-085435
```

The active Supervisor and Configurator files were copied to exact byte boundaries. The already-rotated `base-station-startup-supervisor.jsonl.1` segment was then appended with a separate boundary record because it contains the earlier startup sessions required for comparison. `evidence-snapshot.json`, `evidence-snapshot-append-rotated-supervisor.json`, and `artifact-hashes.json` preserve source sizes, timestamps, copied sizes, and SHA-256 hashes.

Integrity results:

| Input | Bytes | Valid records | Malformed |
| --- | ---: | ---: | ---: |
| Rotated Supervisor | 5,242,465 | 9,808 | 0 |
| Active Supervisor | 317,911 | 635 | 0 |
| Configurator | 1,097,941 | 1,569 | 0 |

Every line is parsed independently. The analyzer retains source line numbers and byte offsets and reports malformed/truncated lines, invalid and out-of-order timestamps, duplicate event IDs, schema versions, and missing session correlation instead of discarding them.

## Session selection

Selection uses timestamps, process and session IDs, operation IDs, scan-session IDs, station identities, event outcomes, and overlap windows. Names alone are not used.

- Normal: `bs-session-abad6804a98146b18dc7db98d5d9bb3d`, Supervisor PID 40836, 2026-06-18 08:12:29.902Z to 08:12:54.707Z. Eight attempts completed successfully, representing two startup cycles across four saved stations. There were no timeouts and no overlapping Configurator Scan.
- Failed without Scan: `bs-session-196da76d16e24dd3ae176f6e37cca03f`, Supervisor PID 33624, 2026-06-17 19:37:28.731Z to 21:18:18.474Z. Twenty-four attempts timed out, no attempt succeeded, and no Configurator Scan overlapped.
- Failed then Scan-assisted: `bs-session-b391b076b9b947058eae3bb5a0b029d7`, Supervisor PID 25940, beginning 2026-06-18 19:28:25.979Z. The initial burst recorded three device-resolution timeouts before Configurator Scan. The same Supervisor process then completed newly created operation IDs successfully while Scan was active.

The two complete June 16 sessions in the Phase 29B archive are supporting normal examples; both completed 4/4 operations successfully and overlap Configurator scans, so they are not the primary no-Scan control.

## Normalized timelines

All relative times below use the first record in the selected Supervisor session as zero.

### Normal

| UTC | Relative | Event |
| --- | ---: | --- |
| 08:12:29.931Z | +29 ms | Burst 1 started |
| 08:12:29.970Z | +67 ms | First adapter lookup completed |
| 08:12:29.987Z | +84 ms | First device resolution completed |
| 08:12:32.124Z | +2,222 ms | First station command succeeded |
| 08:12:54.707Z | +24,805 ms | Startup session completed |

### Failed without Scan

| UTC | Relative | Event |
| --- | ---: | --- |
| 19:37:28.821Z | +91 ms | Burst 1 started |
| 19:37:31.761Z | +3,030 ms | First adapter lookup completed |
| 19:37:31.762Z | +3,031 ms | Device resolution started |
| 19:37:36.979Z | +8,249 ms | First operation timed out in device resolution |
| 19:37:38.018Z | +9,288 ms | Supervisor created the second operation |
| Through 21:18:18.474Z | — | Twenty-four attempts; all timed out in device resolution |

### Failed then Scan-assisted

| UTC | Relative | Event |
| --- | ---: | --- |
| 19:28:26.007Z | +28 ms | Initial burst started |
| 19:28:34.023Z | +8,044 ms | First device-resolution timeout |
| 19:28:43.047Z | +17,068 ms | Second device-resolution timeout |
| 19:28:52.062Z | +26,083 ms | Third device-resolution timeout |
| 19:28:53.063Z | +27,084 ms | Supervisor created and started a retry operation |
| 19:28:53.513Z | +27,534 ms | Configurator Scan requested |
| 19:28:53.575Z | +27,596 ms | Configurator adapter lookup succeeded |
| 19:28:53.631Z | +27,652 ms | Configurator discovery watchers started |
| 19:28:54.115Z | +28,136 ms | Supervisor device resolution completed |
| 19:28:56.371Z | +30,392 ms | Supervisor operation succeeded |
| 19:29:03.654Z | +37,675 ms | Scan completed: four matched, zero new |
| 19:29:16.247Z | +50,268 ms | Initial Supervisor burst completed 4/4 |
| 19:30:00.839Z | +94,860 ms | Bounded Supervisor follow-up completed |

## Stage comparison

| Stage | Normal | Failed/no Scan | Failed + Scan |
| --- | --- | --- | --- |
| Supervisor startup | Scheduled and ran | Scheduled and ran | Scheduled and ran |
| Bluetooth adapter discovery | Succeeded | Succeeded | Succeeded before Scan |
| Saved station load | Four configured | Four configured | Four configured; Scan matched the same four |
| Initial station scan | None | None | Configurator active discovery began after three timeouts |
| Operation burst creation | Created normally | Created normally | Created normally before Scan |
| First Bluetooth attempt | Reached device resolution and GATT | Reached device resolution | Reached device resolution |
| Retry behavior | Existing bounded cycles | Existing retries all timed out | Retry operation started before Scan and succeeded during Scan |
| Timeout/failure | None | 24 device-resolution timeouts | Three initial device-resolution timeouts |
| Configurator overlap | None | None | Yes, approximately 10.14 seconds |
| Post-Scan device discovery | N/A | N/A | Configurator observed the same four saved stations |
| Supervisor resumed/new work | N/A | N/A | Existing in-flight operation progressed; later operations had new IDs |
| Final station state | Completed | Incomplete | Completed |

The earliest reliable divergence is `deviceResolution`. In the normal session the first device resolved about 17 ms after adapter lookup; in the failed session adapter lookup completed, but device resolution never completed and the attempt timed out.

## What Configurator Scan did

`ScanBaseStationsAsync` invokes `BaseStationDiscovery.ScanAsync` for ten seconds. That path:

1. calls `BluetoothAdapter.GetDefaultAsync` and checks LE support;
2. starts paired and unpaired `DeviceInformation` watchers;
3. starts an active `BluetoothLEAdvertisementWatcher`;
4. resolves a `BluetoothLEDevice` from a device ID only when an address property is absent;
5. records observed stations in process-local discovery state;
6. matches discovered addresses against the in-memory Configurator grid and upserts grid rows.

It does not create Supervisor operation-queue entries, send base-station power commands, clear a shared persistent cache, save configuration automatically, signal the Supervisor, or call a Supervisor retry function. Configurator and Supervisor are separate processes, so their static trackers and singleton state are not shared.

The evidence proves that Configurator and Supervisor overlapped. It also proves that Supervisor—not Configurator—sent the successful commands. The recovering Supervisor operation started 450 ms before Scan, then completed device resolution 562 ms after Scan began and 484 ms after Configurator discovery watchers started. This timing is consistent with OS-level discovery warm-up, but it is also consistent with elapsed readiness time. No logged OS device-arrival, cache invalidation, or explicit retry trigger distinguishes those explanations.

Scan found the same four saved stations and reported zero new entries. There is no evidence of reordered station operations or a cleared persistent reference. Supervisor generated new operation IDs for each attempt under its existing retry policy; Configurator did not resume a Supervisor operation object.

## Classification and correction boundary

Primary classification: **G — instrumentation is insufficient**, medium confidence.

Direct support:

- adapter lookup succeeded in normal, failed, and Scan-assisted sessions;
- failures consistently stopped in device resolution before GATT service access;
- active Configurator discovery immediately preceded successful Supervisor device resolution;
- Configurator emitted no station-command event;
- the successful Supervisor attempt was already in flight before Scan.

Contradictory/limiting evidence:

- Scan and elapsed time changed together;
- there is no no-Scan control with the same delay and Bluetooth readiness state;
- OS Bluetooth readiness, device-arrival, and cache-generation transitions were not logged;
- the current event model cannot prove whether active discovery changed OS-visible state or merely coincided with readiness.

Exactly one next phase is recommended: **Phase 29D-E — improve instrumentation before correction**.

- Trigger: existing startup attempts entering device resolution and existing Configurator discovery watcher transitions.
- Delay: zero new delay; preserve current timing.
- Retry count: unchanged; add no retry.
- Cooldown: unchanged; add no new burst source.
- Concurrency guard: retain the existing startup scheduler and operation IDs; add correlation only.
- Overlap behavior: record OS device-arrival/status, watcher lifecycle, `FromBluetoothAddressAsync`/`FromIdAsync` resolution route and duration, and whether a retry was already in flight.
- Logging: emit bounded state-transition records with stable session, operation, scan, and station identities.
- Failure behavior: observation only; never start discovery, retry, reset Bluetooth, or issue a command.

This direction cannot create repeated Bluetooth bursts because it changes no scheduler, retry, timer, watcher, or command path.

## Offline command

```powershell
dotnet PimaxVrcSupervisor.dll base-station-startup-analysis-json `
  --supervisor-log <rotated-supervisor.jsonl> `
  --supervisor-log <active-supervisor.jsonl> `
  --configurator-log <configurator.jsonl>
```

The command emits exactly one `base-station-startup-analysis-v1` JSON document. It accepts explicit input paths only and reports integrity, discovered and selected sessions, normalized timelines, operation summaries, divergence, Scan correlation, overlap, classification, evidence gaps, and the recommended next phase.

Sanitized fixtures under `PimaxVrcSupervisor.Tests/Fixtures/BaseStationStartup` preserve the three outcome patterns and relative Scan timing without real addresses, paths, names, host data, or raw private logs.

## Future validation

Phase 29D-E should first verify the added records offline with synthetic fixtures. A later explicitly authorized studio session should capture a controlled no-Scan delayed control and a Scan case while preserving identical startup timing. No live correction is justified until those records distinguish active discovery from passive readiness delay.
