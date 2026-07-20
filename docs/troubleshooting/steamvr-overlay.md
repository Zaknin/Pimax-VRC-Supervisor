# SteamVR Overlay Issues

## Overlay Does Not Appear

1. Set **Autostart mode** to **SteamVR Overlay**.
2. Save from Configurator.
3. Restart SteamVR.
4. Confirm `PimaxVrcSupervisorSteamVrHost.exe` exists.

## Overlay Opens But Supervisor Is Not Running

Use Configurator **Validate** and check whether Windows approval is needed for elevated startup.

## Terminal UI Opens Instead

You are likely using Terminal Mode. Switch **Autostart mode** to **SteamVR Overlay** and save.

## Restart SteamVR Does Not Start

`Restart SteamVR` requires a running SteamVR server process and a second confirmation click within 10 seconds. If the overlay disappears after confirmation, wait for SteamVR to return; the Supervisor keeps the action state and last result available to reconnecting clients. VRChat is relaunched only if it was running when the restart was accepted.

The old SteamVR runtime closing during an accepted restart is expected. Supervisor reports that transition as requested restart progress, not `SteamVR stopped unexpectedly`, and keeps monitors and base stations in their current managed state. A restored overlay should show current restart progress or the final success result without retaining a warning solely from the requested old-runtime exit. An unrelated SteamVR disappearance without active restart intent remains an unexpected-exit warning.

If the action reports that replacement SteamVR did not become healthy, restart SteamVR manually or use an explicit Supervisor exit. The failed action itself does not power off base stations or restore monitors.

For replay diagnosis, enable Supervisor debug diagnostics and correlate the short `requestId`, client source/session, disposition, operation, and `steamVrShutdownIssueCount`. `duplicate-active` and `duplicate-terminal` are observations, not new executions. The issue count must never exceed one. A blocked second internal issuance is logged as an invariant violation and still does not create another helper.
