# Pimax Client Issues

## Pimax Client Does Not Detect A Connected Headset

If the headset is powered on and connected but Pimax Client does not register it, collect a snapshot before changing anything.

Do not restart Pimax Client, unplug USB, start SteamVR, or reset devices until the failed state is captured.

From the release folder:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-connectivity-json > pimax-connectivity.json
```

The snapshot is read-only. It checks Windows USB evidence, Pimax Client/runtime processes, Pimax services, recent bounded logs, and SteamVR driver registration. SteamVR driver registration is only secondary evidence; Pimax Client should be able to detect the headset without SteamVR running.

## Relaunch Pimax Play From Terminal UI

If Pimax Play has been exited and you want to test the supported Shell relaunch path, open Terminal UI and select **Relaunch Pimax Play**.

Before confirming, exit Pimax Play from its tray menu and wait for shutdown. Supervisor will refuse the action if Pimax Play is still running.

The action opens the official Windows Start Menu shortcut once and waits up to 90 seconds for the Pimax software stack and headset registration. It does not stop processes, restart services, reset USB, reset DisplayPort, automate Connect, or retry. If the result is `launchedButNotRegistered`, the manual USB reseat procedure remains separate.

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
3. A recovery snapshot after the headset registers again.

This makes it easier to tell whether the failure boundary is Windows USB enumeration, Pimax Client/runtime registration, or another layer.
