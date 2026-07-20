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

The overlay can expose session actions such as restarting face-tracking apps, base-station power controls, OSC Router restart, and `Restart SteamVR`.

`Restart SteamVR` is shown as a full-width action row. Click it once to arm confirmation, then click it again within 10 seconds to start the Supervisor-owned restart. Supervisor issues SteamVR's graceful shutdown request, watches the exact old runtime identity disappear, and then invokes the existing SteamVR Start path automatically. It does not wait for the shutdown helper to acknowledge or exit. If VRChat was running at confirmation time, Supervisor resumes VRChat through Steam after the replacement SteamVR runtime is healthy; if VRChat was not running, Supervisor does not launch it. The overlay cannot show Start SteamVR because it only exists while SteamVR is already running.

The second click creates one request identity and latches the submission before TCP delivery. Duplicate delivery is safe at the Supervisor. When the overlay disappears with the old runtime and reconnects to the replacement, it observes the existing operation/result and never recreates the confirmation.

During the bounded restart window, Supervisor keeps base stations and Supervisor-owned monitor topology in the managed session state. The overlay can disappear while SteamVR restarts; reconnecting clients can read the current action and last result from Supervisor status.

## If The Overlay Does Not Appear

1. Confirm **Autostart mode** is **SteamVR Overlay**.
2. Save from Configurator so startup integration is applied.
3. Restart SteamVR.
4. Check that `PimaxVrcSupervisorSteamVrHost.exe` exists in the release folder.
5. Use Configurator **Validate** to look for missing files.
