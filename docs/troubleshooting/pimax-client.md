# Pimax Client Issues

## Pimax Client Does Not Detect A Connected Headset

If the headset is powered on and connected but Pimax Client does not register it, collect a snapshot before changing anything.

Do not restart Pimax Client, unplug USB, start SteamVR, or reset devices until the failed state is captured.

From the release folder:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-connectivity-json > pimax-connectivity.json
```

The snapshot is read-only. It checks Windows USB evidence, Pimax Client/runtime processes, Pimax services, recent bounded logs, and SteamVR driver registration. SteamVR driver registration is only secondary evidence; Pimax Client should be able to detect the headset without SteamVR running.

## How To Read The Result

Common assessment values:

| Assessment | Meaning |
|---|---|
| `connected` | Windows shows the wired Crystal USB profile and recent runtime logs report the headset connected. |
| `windowsDevicesPresentRuntimeNotConfirmed` | Windows sees the wired headset, but recent runtime-connected evidence was not found. |
| `windowsDevicesAbsent` | The device probe completed and did not find the wired Crystal USB profile. |
| `windowsDevicesPartialOrProblem` | Windows sees part of the headset profile or reports a device problem. |
| `pimaxClientNotRunning` | Pimax Client appears installed but no confirmed Pimax Client/runtime process was found. |
| `insufficientEvidence` | One or more required probes failed, timed out, or produced only stale evidence. |

Historical log entries do not prove the current state. Check the event timestamps and confidence before drawing conclusions.

## Capture A Useful Comparison

For intermittent failures, keep three snapshots when possible:

1. A healthy snapshot while Pimax Client detects the headset.
2. A failed snapshot before restarting anything.
3. A later snapshot after the headset registers again.

This makes it easier to tell whether the failure boundary is Windows USB enumeration, Pimax Client/runtime registration, or another layer.

## Unrelated USB Changes

Removing or reinserting an Index-controller or Vive Tracker radio dongle should produce a concise unrelated-device decision and zero Supervisor-managed application restarts. VRCFaceTracking, Broken Eye, XSOverlay, Autostart apps, SteamVR, monitors, and base stations should keep their current state.

If an unrelated dongle appears to trigger recovery:

1. Record the PIDs of the affected applications before touching the dongle.
2. Enable Supervisor debug diagnostics.
3. Reproduce one controlled removal and reinsertion.
4. Preserve the physical-device classification, VID/PID, stable container identity, and selected recovery-plan lines.

An unknown device classification fails closed and does not restart applications. Do not add a friendly-name rule as a workaround.
