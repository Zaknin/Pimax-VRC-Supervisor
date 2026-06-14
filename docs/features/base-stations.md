# Base Station Power

Base station power automation can wake and sleep supported SteamVR Base Stations.

If the Supervisor starts before SteamVR, it waits for SteamVR to appear and then runs the configured base-station startup routine. You do not need to open Configurator or run a manual scan first.

## Configure

Open **Base Stations** in Configurator.

1. Click **Scan**.
2. Enable the base stations you want controlled.
3. Choose the power-down mode.
4. Test power actions while it is safe to do so.

## Safety

Only enable stations that belong to your play space. Manual on/off actions can affect tracking immediately.

## Troubleshooting

If base stations do not respond, check Bluetooth availability, base-station firmware support, SteamVR state, and whether the station is enabled in the table. Unsupported or temporarily unavailable stations are skipped with a warning instead of blocking startup indefinitely.
