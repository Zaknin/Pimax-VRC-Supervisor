# UVC isolation session correlation tooling

Phase 29G-A adds offline, operator-invoked tooling for comparing UVC isolation sessions across several days. It does not change Supervisor runtime behavior, deployment, scheduled tasks, USB devices, services, drivers, dump settings, SteamVR, Pimax Play, or VRCFaceTracking.

## Investigation boundary

Recent dumps share a recurring Windows kernel failure bucket:

```text
DRIVER_IRQL_NOT_LESS_OR_EQUAL (0xD1)
AV_usbvideo!CaptureProcessDataPayload
```

The stack points through USBXHCI request completion into `usbvideo.sys`, but current triage evidence does not preserve enough IRP or device-object state to prove which UVC device was involved. Known current UVC identities include an HTC/Vive-like `VID_0BB4&PID_0321&MI_00` camera and a second `VID_34A4&PID_0012&MI_00` UVC camera. The second device is likely Pimax-related, but this tooling deliberately records that as evidence and confidence, not causal proof.

## Method

Each session changes one controlled variable at a time. The operator declares physical observations explicitly, and the tool records machine observations separately. Physical cable actions are never inferred solely from PnP state.

Useful scenario tags include:

| Scenario | Purpose |
| --- | --- |
| `viveDisconnected` | Vive absent control |
| `viveConnectedVrcftStopped` | Vive present without VRCFaceTracking |
| `viveConnectedVrcftRunning` | Vive present with VRCFaceTracking |
| `supervisorReconnectAutomationDisabled` | Automation disabled control |
| `supervisorReconnectAutomationEnabled` | Automation enabled comparison |
| `pimaxOnly` | Pimax-only UVC control |
| `sleepWakeReconnect` | Sleep, wake, then physical reconnect |
| `rebootControl` | Clean reboot without sleep/wake |

## Commands

Start a session:

```powershell
dotnet .\PimaxVrcSupervisor.dll uvc-isolation-session-start-json `
  --output C:\tmp\uvc-session-001 `
  --label "sleep wake reconnect" `
  --scenario sleepWakeReconnect `
  --vive-connected no `
  --vive-disconnected-before-sleep yes `
  --sleep-wake yes `
  --notes "VRCFaceTracking launches normally"
```

The start command creates the session directory, refuses to overwrite an existing directory, captures one read-only system snapshot, process snapshot, UVC/PnP inventory, config snapshot, recorder health record, and `SHA256SUMS.txt`, then exits.

Append an operator observation:

```powershell
dotnet .\PimaxVrcSupervisor.dll uvc-isolation-session-annotate-json `
  --session C:\tmp\uvc-session-001 `
  --observation "Vive physically reconnected to same port" `
  --source operator
```

Annotations are append-only. They do not inspect hardware unless `--capture-snapshot` is supplied, in which case exactly one immediate process/UVC snapshot is written.

Finish a session:

```powershell
dotnet .\PimaxVrcSupervisor.dll uvc-isolation-session-finish-json `
  --session C:\tmp\uvc-session-001 `
  --result bugcheck `
  --dump C:\Windows\Minidump\example.dmp `
  --windbg-report C:\tmp\example-cdb-analysis.txt
```

The finish command captures end state, bounded Windows-event evidence, recorder health, dump metadata and hash when accessible, parsed WinDbg text fields, duration, boot-identity change, `session-final.json`, and updated hashes. It does not copy dumps unless a future explicit destination option is added.

Compare sessions:

```powershell
dotnet .\PimaxVrcSupervisor.dll uvc-isolation-analysis-json `
  --session C:\tmp\uvc-session-001 `
  --session C:\tmp\uvc-session-002 `
  --markdown-output C:\tmp\uvc-isolation-analysis.md
```

The analyzer groups sessions by Supervisor reconnect automation, headset reconnect restart, Vive connected state, VRCFaceTracking state, sleep/wake versus reboot, Pimax-only versus Pimax+Vive, stable versus crash, and repeated failure bucket.

## Privacy and bounds

The session files hash machine identity, PnP instance IDs, containers, parent IDs, location paths, executable paths, and config paths. They preserve basenames and VID/PID evidence where useful. They do not store raw device instance IDs, serials, full user paths, minidump bytes, EVTX files, or Windows Error Reporting archives.

Windows-event reads are time-bounded. Large recorder reads use tail windows. JSONL parsing and checksum generation are bounded to the session directory. Commands do not leave background children running.

## Interpretation limits

Labels such as observed association, repeated association, condition not required, condition possibly required, contradictory evidence, and insufficient evidence are isolation-matrix labels only. A single stable session cannot prove that a device, process, driver, or Supervisor setting is safe or causal.

The first recommended recorded session after implementation is:

```text
Supervisor Vive detection disabled; face-tracker reconnect restart disabled;
headset-reconnect restart disabled; VRCFaceTracking launches normally;
Vive physically disconnected before sleep; PC sleeps; PC resumes;
Vive physically reconnected to the same port; normal VR session;
observe for 30-60 minutes; record stable or confirmed bugcheck result.
```
