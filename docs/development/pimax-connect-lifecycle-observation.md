# Pimax Connect Lifecycle Observation

`pimax-connect-lifecycle-observe-json` is a CLI-only, read-only diagnostic command for correlating a manually initiated Pimax Play Connect attempt with Pimax services, processes, registration evidence, USB/PnP checkpoints, Windows events, and bounded Pimax log tails.

## Why This Observer Exists

The proposed Phase 28C2 service-restart experiment was not executed. Target validation showed that `PiServiceLauncher` is not a stable persistent runtime service: Connect can start it briefly, Windows has recorded `ucrtbase.dll` / `0xc0000409` / `BEX64` crashes, and persistent runtime processes can remain after it exits. Restarting it would therefore test an unverified target.

The observer maps the lifecycle first. It does not decide that a process owns registration merely because of its name or parent relationship.

## Command

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-connect-lifecycle-observe-json `
  --scenario connect-no-usb-reseat `
  --duration-seconds 60 `
  --output-dir "C:\path\to\local-evidence" `
  --marker-file "C:\path\to\markers.jsonl"
```

The command writes exactly one JSON result to standard output with schema `pimax-connect-lifecycle-observation-v1`. Full USB/PnP and filtered-connectivity checkpoint files are written only to the explicitly selected evidence directory.

## Markers

The marker file is append-only JSONL. A controller may append records while the observer runs:

```json
{"label":"connect-pressed","source":"user-confirmed","note":"Connect scan started"}
{"label":"usb-reseat-completed","source":"user-confirmed"}
```

Supported scenario markers include `connect-pressed`, `connect-scan-visible`, `usb-reseat-started`, `usb-reseat-completed`, `green-registered-confirmed`, and `user-ui-observation`. The observer records the marker; it never infers or performs the user action.

## Sampling Design

- Service and process state is sampled at a bounded 250-500 ms cadence.
- Filtered connectivity and registration evidence is sampled at a slower bounded cadence.
- The expanded USB/PnP inventory is cached between explicit checkpoints rather than enumerated on every fast sample.
- Only transitions are retained for high-frequency process and service evidence.
- Event-log and Pimax-log evidence is collected in bounded form at session end.
- Probe failures are recorded as warnings or errors without stopping independent probes.

## Read-Only Boundary

The observer does not start, stop, or restart services; kill or restart processes; automate Pimax Play; change USB/PnP state; run `pnputil` or `devcon`; add bridge commands; or expose actions in Configurator, Terminal UI, or SteamVR Overlay.

Evidence folders are local and may contain machine-specific paths or identifiers. They must remain outside the repository and must not be committed.

## Expected Cross-Scenario Classifications

After comparable idle, Connect-only, and Connect-plus-USB observations are collected, analysis may classify the result as fresh USB arrival decisive, launcher crash correlated, persistent runtime transition correlated, Connect-only recovery, combined trigger with unresolved owner, or inconclusive. A single observer result deliberately remains `pendingCrossScenarioAnalysis`.
