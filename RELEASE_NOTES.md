# Pimax VRC Supervisor v1.0.9 Release Notes

This is the release-facing companion note for `PimaxVrcSupervisor-v1.0.9.zip`.

## Current Version

- Release tag: `v1.0.9`
- App version: `1.0.9`
- Assembly/file version: `1.0.9.0`
- Platform: Windows
- Runtime: self-contained .NET 9

## Included Files

- `PimaxVrcSupervisor.exe` - main supervisor console app
- `PimaxVrcSupervisorConfigEditor.exe` - GUI config editor
- `supervisor.config.json` - documented configuration file
- `README.md` - full GitHub/user documentation
- `RELEASE_NOTES.md` - release-focused install, upgrade, and verification notes

## Highlights

- Supervises Broken Eye and VRCFaceTracking for Pimax Crystal + VRChat sessions.
- Waits for the headset before launching managed apps.
- Restarts managed apps after stable Pimax reconnects.
- Watches PiService logs for short runtime HID reconnects.
- Optionally watches Vive mouth tracker reconnects and restarts only VRCFaceTracking.
- Optionally saves/restores the Windows monitor layout around the VR session.
- Can install an elevated VRChat/SteamVR auto-launch Scheduled Task.
- Supports optional Intiface/OscGoesBrrr launch by hotkey or Lovense BLE detection.
- Includes a GUI config editor for paths, auto-launch apps, detectors, timings, and raw JSON.

## Install

1. Download `PimaxVrcSupervisor-v1.0.9.zip`.
2. Extract it to a writable folder.
3. Run `PimaxVrcSupervisor.exe`.
4. Choose your Broken Eye and VRCFaceTracking executables when prompted.
5. Use `PimaxVrcSupervisorConfigEditor.exe` for later configuration changes.

No separate .NET install is required for the release zip.

## Upgrade

1. Close any running supervisor instance.
2. Extract the new zip over the previous folder or into a fresh folder.
3. Keep your existing `supervisor.config.json` if it already contains your preferred paths and settings.
4. Open the config editor once if you want to review new settings.

## Verify

Expected companion assets:

- `PimaxVrcSupervisor-v1.0.9.zip.sha256`
- `PimaxVrcSupervisor-v1.0.9.zip.sigstore.json`

Checksum:

```powershell
Get-FileHash .\PimaxVrcSupervisor-v1.0.9.zip -Algorithm SHA256
Get-Content .\PimaxVrcSupervisor-v1.0.9.zip.sha256
```

Sigstore:

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.0.9.zip `
  --bundle .\PimaxVrcSupervisor-v1.0.9.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/(heads|tags)/.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

## Suggested GitHub Release Body

```markdown
Pimax VRC Supervisor v1.0.9 is a self-contained Windows release for Pimax Crystal + VRChat sessions. It includes the main supervisor, the GUI config editor, the documented config file, and the bundled .NET runtime.

Download `PimaxVrcSupervisor-v1.0.9.zip`, extract it, and run `PimaxVrcSupervisor.exe`. Use `PimaxVrcSupervisorConfigEditor.exe` to edit paths, detectors, auto-launch apps, timings, and OscGoesBrrr settings.

The release zip is accompanied by SHA-256 and Sigstore verification files.
```
