# SteamVR Overlay

SteamVR Overlay mode gives you a dashboard inside SteamVR.

Use it if you prefer in-headset controls or want a SteamVR-native control surface instead of Terminal UI.

## How It Differs From Terminal Mode

| Terminal Mode | SteamVR Overlay |
|---|---|
| Opens Terminal UI on the desktop | Opens a SteamVR dashboard overlay |
| Good for keyboard and mouse | Good inside SteamVR |
| Can auto-close Terminal UI with the session | Follows SteamVR overlay startup flow |

The two modes are separate. Terminal UI does not replace SteamVR Overlay.

## Common Controls

The overlay can expose session actions such as restarting face-tracking apps, base-station power controls, and OSC Router restart.

## If The Overlay Does Not Appear

1. Confirm **Autostart mode** is **SteamVR Overlay**.
2. Save from Configurator so startup integration is applied.
3. Restart SteamVR.
4. Check that `PimaxVrcSupervisorSteamVrHost.exe` exists in the release folder.
5. Use Configurator **Validate** to look for missing files.
