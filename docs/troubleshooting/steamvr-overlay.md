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

## VR Restart Does Not Start

`VR Restart` requires a running SteamVR server process and a second confirmation click within 10 seconds. If the overlay disappears after confirmation, wait for SteamVR to return; the Supervisor keeps the action state and last result available to reconnecting clients.

If the action reports that replacement SteamVR did not become healthy, restart SteamVR manually or use an explicit Supervisor exit. The failed action itself does not power off base stations or restore monitors.
