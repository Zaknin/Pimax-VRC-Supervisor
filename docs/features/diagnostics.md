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

## Sharing Logs

When asking for help, share only the relevant log snippets and remove personal paths if needed.
