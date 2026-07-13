# Phase 30B.4 and Phase 31A startup safety

## SteamVR exact-identity confirmation

`SteamVrBurstSuppressionChecker` is the shared bounded confirmation service for both the unsupported-V2 inner burst decision and later outer wake-pass decisions. It reuses `SteamVrBaseStationMatcher`; no second identity algorithm or count-only success path exists in the optimization gate.

- The maximum confirmation duration is 2 seconds.
- The normal poll interval is 150 milliseconds.
- `Stopwatch` supplies the monotonic elapsed-time/deadline measurement.
- The first poll is immediate. Subsequent delays are cancellation-aware and capped by the remaining deadline.
- Empty tracking-reference snapshots are treated as transient while the API remains available.
- A snapshot succeeds only when every enabled configured station is present by exact identity in the same snapshot. A correct active-reference count with wrong identities never suppresses a wake.
- Query errors, definitive OpenVR unavailability, cancellation, and a deadline with ambiguous data retain the safe wake fallback.
- The result records the terminal outcome, configured and exact-confirmed counts, missing identities, poll count, elapsed milliseconds, highest exact-confirmed count, and whether a query ever completed successfully.

The unsupported-V2 burst-2 decision still supports a missing-only retry when the best exact snapshot confirms a strict subset. Before every later outer pass, the same bounded service runs at the last possible point before `SendBaseStationPowerOnPassAsync`:

- all exact-confirmed: the pass delegate is not invoked, `_baseStationPowerOnComplete` is set, and all remaining passes end early;
- strict partial exact confirmation: only missing configured stations are passed to the existing serialized GATT path;
- unavailable, errored, cancelled, no exact matches, or otherwise ambiguous: the existing full pass remains;
- call sites that cannot prove subset safety must pass `subsetTargetingSafe: false`, which forces the full-pass fallback.

Phase 30B.2 streaming resolution recovery remains inside `SendBaseStationPowerOnPassAsync`, including first-resolution-failure interception, discovery streaming, serialized station wake, deduplication, final fallback, and terminal completion. The later gate only changes the station array supplied to that existing path or omits the pass after full exact confirmation. The existing delayed passive SteamVR status confirmation also remains in place.

Base-station JSONL diagnostics retain the Phase 30B.2 and Phase 30B.3 events and extend the Phase 30B.3 operation with:

- `steamVrBurstSuppressionCheckStarted`
- `steamVrConfirmationPollProgress` (only when the exact-confirmed count changes)
- `steamVrBurstSuppressionCheckCompleted`
- `laterWakePassSuppressed`
- `laterWakePassReducedToMissingStations`
- `laterWakePassSuppressionBypassed`
- `wakeSequenceCompletedEarly`

`currentStage` distinguishes `unsupportedV2BurstSuppression` from `laterWakePassSuppression`. Payloads include poll count, highest confirmed count, reachability, maximum duration, poll interval, pass/maximum pass, disposition, elapsed time, and fallback reason.

## XSOverlay-safe monitor transition

Live evidence established that XSOverlay remained stable until the Supervisor applied the Windows display-topology change for `TurnOffSecondaryMonitors=true`, at which point XSOverlay crashed. `XsOverlaySafeMonitorTransitionCoordinator` now owns the one transition-scoped mitigation:

1. Enumerate exact `XSOverlay` processes in the Supervisor's Windows session.
2. If none is running, use the existing `MonitorLayoutController.KeepPrimaryMonitorOnly` path with no stop, settle delay, or restart.
3. If exactly one is running, capture PID, session ID, start time, exact process identity, executable path, and a working directory derived from that path.
4. Refuse the transition before stopping anything when the executable is not a fully qualified existing `XSOverlay.exe`, its working directory cannot be derived, or multiple instances make ownership ambiguous.
5. Request a graceful main-window close and wait up to 2 seconds. If it remains the same PID/session/start-time/path identity, terminate only that PID without killing a process tree, then verify exit. PID reuse or a changed identity is never force-terminated.
6. Run the existing monitor shutdown, wait 2 seconds for topology stabilization, and attempt one restart from the captured executable path and working directory. No transient command line or environment block is replayed.
7. Before launching, enumerate exact instances again. An independently reappearing instance is accepted and prevents a duplicate launch. After launch, verify exactly one exact instance within 3 seconds.

The coordinator does not add a persistent restart loop and does not auto-start XSOverlay when it was not running before the transition. If restart information is unavailable or the exact original process cannot be stopped, monitor shutdown is skipped. If monitor shutdown or topology settlement fails after the Supervisor stopped XSOverlay, one best-effort restart still occurs. The same best-effort restart applies when cancellation arrives after a Supervisor-owned stop. SteamVR, VRChat, `vrserver`, `vrmonitor`, `vrcompositor`, and unrelated overlays are never targeted.

Structured Supervisor diagnostic messages use operation name `xsOverlaySafeMonitorShutdown` and the following events:

- `xsOverlayDetectionStarted`, `xsOverlayDetected`, `xsOverlayNotRunning`
- `xsOverlayRestartInformationCaptured`, `xsOverlayRestartInformationUnavailable`
- `xsOverlayStopStarted`, `xsOverlayGracefulStopCompleted`, `xsOverlayForcedStopStarted`, `xsOverlayStopCompleted`, `xsOverlayStopFailed`
- `secondaryMonitorShutdownStarted`, `secondaryMonitorShutdownCompleted`, `secondaryMonitorShutdownFailed`
- `displayTopologySettleStarted`, `displayTopologySettled`, `displayTopologySettleFailed`
- `xsOverlayRestartStarted`, `xsOverlayRestartCompleted`, `xsOverlayRestartFailed`, `xsOverlayRestartSkipped`
- `complete`

Diagnostics record the original/restarted PID, session, exact executable identity, redacted path tail, restart and stop mechanisms, bounded timings, monitor/restart result, final outcome, and skip reason. They do not record command lines or environment blocks.

All process, cancellation, race, and monitor-transition behavior is validated through `IXsOverlayProcessPlatform` and fake monitor/delay callbacks. Tests do not enumerate, stop, start, or otherwise interact with live XSOverlay, SteamVR, VRChat, or Windows display topology.
