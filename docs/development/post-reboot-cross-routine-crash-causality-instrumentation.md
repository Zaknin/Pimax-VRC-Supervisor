# Post-reboot and cross-routine crash-causality instrumentation

## Scope

Phase 29D-E extends the Phase 29B/29C diagnostics without changing hardware behavior. It adds observation around calls that already occur. It does not add Bluetooth discovery, USB/PnP enumeration, process polling, station commands, retries, startup delays, recovery actions, or device/service resets.

Phase 29C found that normal base-station operations completed device resolution immediately, while failed sessions timed out in `deviceResolution`. During the Scan-assisted session, the successful Supervisor retry had already started approximately 450 ms before Configurator Scan. Active discovery and elapsed readiness changed together, so causality remained unresolved.

The motivating post-reboot scenario is an incomplete Supervisor/VR session followed by a Windows restart, failed base-station resolution, and later recovery while Configurator Scan was active. This does not establish that Supervisor, Bluetooth, headset, tracker, USB, or PnP activity caused a reboot, freeze, or BSOD.

## Implementation map

| Category | Existing path wrapped |
| --- | --- |
| Base-station scheduling | `AppSupervisor.RunScheduledBaseStationStartupAsync` |
| Base-station operations | `SendBaseStationCommandsAsync` and existing timeout wrapper |
| Adapter lookup | `BaseStationDiscovery.HasBluetoothLeAdapterAsync` |
| Device resolution | `BluetoothLEDevice.FromBluetoothAddressAsync` and existing `FromIdAsync` fallback |
| Bluetooth/GATT | service and characteristic queries, read, and write calls |
| Configurator Scan | existing `BaseStationDiscovery.ScanAsync` watcher lifecycle |
| Pimax headset | existing bounded `pnputil /enum-devices /connected` detection |
| Vive face tracker | the same existing PnP detection path with a tracker category |
| Mouth/Lovense tracker | existing PnP detection; existing registry fallback remains unchanged |
| Pimax USB | existing SetupAPI/Configuration Manager inventory collector |
| Pimax connectivity | existing connectivity snapshot collector |
| Lifecycle | Supervisor/Watcher/Configurator session start, shutdown, process exit, unhandled exception, and Windows session ending |

No new probe is called to create a recorder event. Heartbeats read recorder state only.

## Flight recorder

Primary path:

```text
%LOCALAPPDATA%\PimaxVrcSupervisor\Diagnostics\FlightRecorder\supervisor-hardware-flight-recorder.jsonl
```

Session summary:

```text
%LOCALAPPDATA%\PimaxVrcSupervisor\Diagnostics\FlightRecorder\supervisor-session-summary.jsonl
```

Schemas:

- `supervisor-hardware-flight-recorder-v1`
- `supervisor-session-summary-v1`

Each record contains a unique event ID, process sequence, UTC and monotonic time, approximate boot fingerprint, process session/build/process/thread identity, operation hierarchy, sanitized device identity, stage/result/error details, duration and timeout state, active-operation snapshot, overlaps, resource tags, durability class, queue health, dropped-event count, and process uptime.

Raw device identifiers are never written by the recorder. Stable identities are SHA-256-derived 16-character correlation hashes.

## Operation model and overlap

The standard stages are:

```text
operationStarted
nativeOrLibraryCallStarted
nativeOrLibraryCallReturned
operationCompleted
operationFailed
operationTimedOut
operationCancelled
operationAbandoned
```

An in-memory concurrent registry tracks active operation ID, category, routine, start time, thread, parent, resource tags, and last stage. It is diagnostic only: it does not lock, delay, serialize, cancel, or schedule hardware work.

Every event snapshots current overlap. Shared tags include `bluetoothAdapter`, `bluetoothDeviceInventory`, `usbPnp`, `pimaxRuntime`, `pimaxHeadset`, `viveFaceTracker`, `mouthTracker`, `baseStations`, and `steamVrLifecycle`.

Cross-process Configurator/Supervisor overlap is reconstructed offline by boot identity and UTC time. The process-local registry intentionally does not introduce inter-process coordination into hardware routines.

## Durability and storage

- Concurrent producers use a bounded 4,096-record channel with one writer task.
- Normal records are queued without a durability wait.
- Critical records request an immediate disk flush and wait at most 50 ms for acknowledgement.
- A cross-process named mutex serializes append/rotation between Supervisor, Watcher, and Configurator writers.
- Mutex acquisition is bounded at 500 ms inside the background writer, never indefinitely in a hardware routine.
- Writer failures and queue overflow increment dropped-event counters.
- Console failure reporting is throttled to once per minute and never recurses through the recorder.
- Recorder failures never suppress or replace the original hardware operation or exception.
- The active file rotates at 16 MiB with seven retained rotations: eight files and 128 MiB maximum per recorder stream.
- A truncated final JSONL record is retained as an integrity error while earlier records remain usable.

The session-summary stream uses the same bounded rotation policy and contains critical lifecycle events only.

## Lifecycle and boot classification

Lifecycle events include:

```text
processSessionStarted
previousSessionAssessed
shutdownRequested
cleanShutdownStarted
cleanShutdownCompleted
unhandledExceptionObserved
processExitObserved
windowsSessionEndingObserved
heartbeat
```

Boot identity uses `Environment.TickCount64` once per process to estimate boot UTC, normalized to a minute, plus the Windows session ID. The resulting fingerprint is low-risk and requires no WMI loop. Its declared confidence is `approximateMinuteFromGetTickCount64`; it cannot distinguish closely spaced boots within the same normalized minute/session.

Previous sessions are assessed only against the same process role:

- `cleanExit`: a durable clean-shutdown marker exists;
- `sameBootProcessInterruption`: no clean marker and the boot fingerprint is unchanged;
- `bootChangedWithIncompleteSession`: no clean marker and the boot fingerprint changed;
- `unknownInterruption`: required markers are missing or contradictory.

These classifications do not identify a process crash, BSOD, power loss, forced reboot, or hard reset without supporting Windows evidence.

## Heartbeats

Heartbeats run approximately every five seconds while operations are active and every 30 seconds while idle. They contain recorder health, active operations, queue depth, dropped records, process uptime, and last durable sequence. They do not poll Bluetooth, USB, PnP, Pimax, SteamVR, trackers, or processes.

## Windows-event correlation

CLI command and schema:

```text
windows-event-correlation-json
windows-event-correlation-v1
```

Required arguments are explicit `--start-utc` and `--end-utc`; the interval is limited to 24 hours. Optional inputs are `--flight-recorder`, `--process-session-id`, `--operation-id`, and `--output`.

The command reads bounded records from available System, Application, Bluetooth, Driver Framework, and Kernel-PnP channels. It classifies:

- Kernel-Power, EventLog, and Kernel-General lifecycle records;
- BugCheck and WER system-error records;
- WHEA hardware errors;
- Application Error, .NET Runtime, and Windows Error Reporting;
- Bluetooth/BTHUSB;
- Kernel-PnP, UserPnp, DriverFrameworks, and USB providers;
- relevant Service Control Manager records.

Missing channels, access denial, and query errors are reported per channel. The collector does not enable channels, alter retention, clear logs, create sources, change registry values, or require elevation for the normal path.

Crash-dump reporting reads existing CrashControl values and recent dump-file metadata only. It does not modify dump policy or parse/upload dumps.

When a new Supervisor session detects an incomplete previous Supervisor session, startup continues immediately. After 30 seconds, one low-priority bounded snapshot is attempted for two minutes before the previous last durable event through five minutes after current startup. It has a 20-second timeout, writes once under `Diagnostics\SystemEvents`, and never retries in a loop.

## Offline analyzer

Command and schema:

```text
hardware-flight-recorder-analysis-json
hardware-flight-recorder-analysis-v1
```

The analyzer accepts repeated `--flight-recorder` paths, an optional `--windows-events` document, and an optional `--process-session-id`. It reports:

- file/session integrity and rotation boundaries;
- boot and clean/incomplete classification;
- final durable event;
- operations active at interruption;
- unmatched native/library-call starts;
- overlap and category timelines;
- Configurator Scan activity;
- reboot/crash and Bluetooth/PnP events;
- candidate relationships, confidence, contradictions, and causal warnings.

Labels are limited to direct evidence, strong correlation, weak correlation, contradictory evidence, and insufficient evidence. Temporal order alone is never reported as causality.

## Performance and behavioral limits

Synthetic tests cover concurrent producers, queue saturation, writer failure, rotation, critical flush, and 2,000 queued events. The recorder does not benchmark through real hardware. Actual disk-flush latency and event-log query cost remain machine-dependent; the 50 ms critical acknowledgement cap and background writer bound their direct effect on hardware callers.

Instrumentation adds allocations and bounded disk activity. It cannot prove that timing perturbation is exactly zero, but it does not change retry counts, timeout constants, task scheduling, operation ordering, or probe frequency.

## Privacy

Fixtures use synthetic sessions, operations, devices, boots, providers, and timestamps. Windows messages are bounded and redact the machine name and user profile. Committed files contain no raw MAC addresses, instance IDs, user/machine names, event exports, or dumps.

## Deployment and rollback

Phase 29D-E is published to a new isolated deployment only after commit and push. The Phase 29B deployment remains untouched. Before activation, the current scheduled-task XML/action is preserved. Activation requires explicit confirmation and changes only the task action/working path; it does not run or stop the task, stop the current Supervisor, reboot Windows, or invoke hardware.

Rollback restores the preserved Phase 29B watcher, arguments, and working directory.

## Future evidence

Exactly one recommended evidence phase follows deployment: capture natural clean shutdown, reboot, incomplete-session, base-station resolution failure, delay-only, Configurator Scan, and unexpected restart cases with the flight recorder and bounded Windows snapshot. Do not intentionally trigger a crash.
