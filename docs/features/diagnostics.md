# Diagnostics

Diagnostics are optional logs for troubleshooting. Leave them off unless you are investigating a problem.

## Enable

Open **General > Diagnostics**.

1. Enable **Diagnostics**.
2. Select the diagnostic logs you need.
3. Choose a diagnostic log folder.
4. Save.

## Terminal UI Diagnostics

**Log Terminal UI load diagnostics** writes lightweight summary records about Terminal UI rendering, refreshes, bridge calls, and process load.

It does not record full bridge responses, commands, or per-frame logs.

## Pimax Client Connectivity Snapshot

The Supervisor can collect a one-shot, read-only Pimax Client connectivity snapshot. This is intended for cases where the headset is physically connected but Pimax Client does not appear to register it correctly.

The snapshot compares several evidence layers:

- Pimax Client installation metadata.
- Running Pimax Client and runtime processes.
- Pimax-related Windows services.
- Wired Pimax Crystal USB device evidence.
- Recent bounded PiService and Pimax Client log markers.
- Pimax SteamVR driver registration as secondary evidence.

It does not restart Pimax Client, start SteamVR, reset USB, change services, or repair anything automatically.

Advanced users can run the explicit JSON command from a console:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-connectivity-json > pimax-connectivity.json
```

Collect this snapshot before restarting Pimax Client or reconnecting USB when you are trying to capture a failed state.

## Manual Pimax Play Shell Relaunch

Terminal UI includes **Relaunch Pimax Play** for a manually requested Pimax Play restart path. It opens the official Windows Start Menu `PimaxPlay.lnk` shortcut with Windows Shell, then verifies Pimax software-stack and headset-registration health for up to 90 seconds.

Use it only after exiting Pimax Play from its tray menu. The action refuses to run while launch-owned Pimax Play processes are still present, when the shortcut is missing or untrusted, when the caller is elevated, or when the process is not running in a normal interactive Explorer session.

The action does not kill processes, stop or start services, reset USB, reset DisplayPort, automate Connect, retry, or write startup configuration. If Pimax Play launches but registration does not recover, use the existing manual USB reseat procedure as a separate operator-controlled fallback.

The Terminal UI bridge runs the JSON command as a hidden child process with null stdin and captured stdout/stderr. SDK and service diagnostics are drained into bounded buffers and are not written directly into the TUI console. This containment does not hide the actual Pimax Play UI opened by Windows Shell.

Phase 30A production validation confirmed one successful Shell request and zero retries, with Pimax Play launched and the headset registered. Phase 30A.1 only fixes the discovered console-inheritance defect and final Terminal UI result flow; the trusted shortcut, Shell-open mechanism, verification timeout, health checks, and result classifications are unchanged.

Advanced users can run the explicit JSON command from a non-elevated console:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-shell-launch-json > pimax-shell-launch.json
```

The command emits one JSON object with schema `pimax-shell-launch-result-v1`.

## Sharing Logs

When asking for help, share only the relevant log snippets and remove personal paths if needed.
