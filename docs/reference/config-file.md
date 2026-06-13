# Config File Reference

Most users should edit settings through Configurator. The config file is JSON and is intended for advanced troubleshooting or backup review.

## Identity And Display Name

The config file path is the real identity. `DisplayName` is a friendly label shown in Configurator and startup output.

If `DisplayName` is empty, Configurator uses the filename. If multiple configs share the same display name, Configurator shows the filename too.

## Important Areas

| Area | What it controls |
|---|---|
| Startup | Off, Terminal Mode, or SteamVR Overlay behavior |
| Face tracking | Broken Eye, VRCFaceTracking, reconnect options |
| Base stations | Controlled stations and power-down mode |
| Auto Startup | Extra apps launched during sessions |
| OSC Router | Local OSC receive port and routes |
| OscGoesBrrr | OscGoesBrrr, Intiface, and device detection |
| Diagnostics | Optional troubleshooting logs |

## Editing Safely

Before hand-editing:

1. Close Supervisor.
2. Make a backup.
3. Edit only the setting you understand.
4. Open Configurator and click **Validate**.

Raw JSON in Configurator is safer than editing in an external editor because you can apply changes back to the normal tabs.
