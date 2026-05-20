# Pimax VRC Supervisor v1.1.1 Release Notes

This is the release-facing companion note for `PimaxVrcSupervisor-v1.1.1.zip`.

## Current Version

- Release tag: `v1.1.1`
- App version: `1.1.1`
- Assembly/file version: `1.1.1.0`
- Platform: Windows
- Runtime: self-contained .NET 9

## Included Files

- `PimaxVrcSupervisor.exe` - main supervisor console app
- `PimaxVrcSupervisorConfigEditor.exe` - GUI config editor
- `supervisor.config.json` - documented configuration file
- `README.md` - full GitHub/user documentation
- `RELEASE_NOTES.md` - release-focused install, upgrade, and verification notes

## Highlights

- Adds native Bluetooth LE SteamVR base-station management.
- Config Editor can scan, rename, enable/disable, manually add, identify, and test base-station power commands.
- Supervisor powers enabled base stations on after the Pimax headset is connected and SteamVR `vrserver.exe` is running.
- Base stations are not restarted on Pimax/device reconnects.
- Cleanup sends configured Sleep/Standby during session shutdown, including a detached helper for console-window close.
- Base Station 2.0 state reads are used when firmware supports them; unsupported reads are cached per station to speed later launches.
- Startup now overlaps base-station wake with the normal Broken Eye, VRCFaceTracking, OscGoesBrrr, and auto-launch sequence.
- When OpenVR is available, startup checks SteamVR tracking 10 seconds after each wake cycle and retries up to 5 cycles until all enabled base stations are active.
- If OpenVR is unavailable or cannot be queried, Base Station 1.0 and stations with unsupported state reads receive a third wake pass 30 seconds after the second pass.
- Fixes the Config Editor default Base Stations tab width so the Identify button is visible without manually resizing the window.
- Keeps existing reconnect handling, monitor restore, auto-launch task, mouth tracker, and OscGoesBrrr/Lovense workflow behavior.

## Install

1. Download `PimaxVrcSupervisor-v1.1.1.zip`.
2. Extract it to a writable folder.
3. Run `PimaxVrcSupervisor.exe`.
4. Choose your Broken Eye and VRCFaceTracking executables when prompted.
5. Use `PimaxVrcSupervisorConfigEditor.exe` for later configuration changes, including the new **Base Stations** tab.

No separate .NET install is required for the release zip.

## Upgrade

1. Close any running supervisor or config editor instance.
2. Extract the new zip over the previous folder or into a fresh folder.
3. Keep your existing `supervisor.config.json` if it already contains your preferred paths and settings.
4. Open the config editor and use the **Base Stations** tab to scan and configure stations.

## Verify

Expected companion assets:

- `PimaxVrcSupervisor-v1.1.1.zip.sha256`
- `PimaxVrcSupervisor-v1.1.1.zip.sigstore.json`

Checksum:

```powershell
Get-FileHash .\PimaxVrcSupervisor-v1.1.1.zip -Algorithm SHA256
Get-Content .\PimaxVrcSupervisor-v1.1.1.zip.sha256
```

Sigstore:

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.1.1.zip `
  --bundle .\PimaxVrcSupervisor-v1.1.1.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/(heads|tags)/.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

## Suggested GitHub Release Body

```markdown
Pimax VRC Supervisor v1.1.1 adds native SteamVR base-station management for Pimax Crystal + VRChat sessions and fixes the Config Editor Base Stations tab default-width layout.

Use the new Config Editor Base Stations tab to scan, rename, enable, test, identify, and manually add base stations. The supervisor powers enabled stations on after Pimax and SteamVR are ready, avoids restarting them during reconnect handling, and powers them down during cleanup using the configured Sleep/Standby mode.

Download `PimaxVrcSupervisor-v1.1.1.zip`, extract it, and run `PimaxVrcSupervisor.exe`. Use `PimaxVrcSupervisorConfigEditor.exe` to edit paths, detectors, auto-launch apps, timings, OscGoesBrrr settings, and base-station settings.

The release zip is accompanied by SHA-256 and Sigstore verification files.
```
