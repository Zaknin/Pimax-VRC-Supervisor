# Pimax VRC Supervisor v1.2.0 Release Notes

This is the release-facing companion note for `PimaxVrcSupervisor-v1.2.0.zip`.

## Current Version

- Release tag: `v1.2.0`
- App version: `1.2.0`
- Assembly/file version: `1.2.0.0`
- Platform: Windows
- Runtime: self-contained .NET 9

## Included Files

- `PimaxVrcSupervisor.exe` - main supervisor console app
- `PimaxVrcSupervisorConfigEditor.exe` - GUI config editor
- `supervisor.config.json` - documented configuration file
- `README.md` - full GitHub/user documentation
- `RELEASE_NOTES.md` - release-focused install, upgrade, and verification notes

## Highlights

- Improves the Config Editor layout while keeping the compact Windows utility style.
- Applies consistent subtle rounded styling to real action buttons without changing grid cells, checkboxes, combo boxes, or scrollbars.
- Keeps `Save` as the only visually primary footer action.
- Improves path indicators so found/missing states are clearer and no longer clip in the Basics tab.
- Rechecks expanded executable and folder paths when **Validate** is pressed, including paths that use `%APPDATA%` or `%LOCALAPPDATA%`.
- Reports missing or non-`.exe` executable paths in validation immediately, including Auto Launch app rows.
- Prevents stale operation status messages from looking current after switching tabs; old fresh messages appear as `Last action:` and expired messages return to persistent state.
- Resizes Auto Launch, Base Stations, and OSC Router tables to show about 10 rows by default and use available tab space without covering the footer.
- Keeps the existing console supervisor runtime behavior, config schema, launch flow, detector tests, base-station controls, OSC routing, and Raw JSON workflows intact.

## Install

1. Download the right zip:
   - If you already have the .NET 9 Windows Desktop Runtime installed, download `PimaxVrcSupervisor-v1.2.0_noNET9.zip`.
   - If you do not have .NET 9 installed, download `PimaxVrcSupervisor-v1.2.0.zip`.
2. Extract it to a writable folder.
3. Choose one initial setup path:
   - 3a. Run `PimaxVrcSupervisor.exe` and answer the first-run prompts.
   - 3b. Use `PimaxVrcSupervisorConfigEditor.exe` for the initial config.
4. Use `PimaxVrcSupervisorConfigEditor.exe` for later configuration changes, including the **Basics**, **Auto Launch**, **Base Stations**, **OSC Router**, and **Raw JSON** tabs.

No separate .NET install is required for `PimaxVrcSupervisor-v1.2.0.zip`; the `_noNET9` zip requires .NET 9 to already be installed.

## Upgrade

1. Close any running supervisor or config editor instance.
2. Extract the new zip over the previous folder or into a fresh folder.
3. Keep your existing `supervisor.config.json` if it already contains your preferred paths and settings.
4. Open the config editor and press **Validate** to recheck executable paths and table entries.

## Verify

Expected companion assets:

- `PimaxVrcSupervisor-v1.2.0.zip.sha256`
- `PimaxVrcSupervisor-v1.2.0.zip.sigstore.json`

Checksum:

```powershell
Get-FileHash .\PimaxVrcSupervisor-v1.2.0.zip -Algorithm SHA256
Get-Content .\PimaxVrcSupervisor-v1.2.0.zip.sha256
```

Sigstore:

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.2.0.zip `
  --bundle .\PimaxVrcSupervisor-v1.2.0.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/(heads|tags)/.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

## Suggested GitHub Release Body

```markdown
Pimax VRC Supervisor v1.2.0 focuses on Config Editor usability and validation.

The editor now uses consistent compact action-button styling, clearer path indicators, dynamic table sizing, and less stale status messaging across tabs. Pressing Validate now rechecks expanded executable paths immediately, including Auto Launch rows and paths using environment variables.

Runtime supervisor behavior, config keys, detector tests, base-station controls, OSC routing, and Raw JSON workflows are unchanged.

Download `PimaxVrcSupervisor-v1.2.0.zip`, extract it, and run `PimaxVrcSupervisor.exe`. Use `PimaxVrcSupervisorConfigEditor.exe` to edit paths, detectors, auto-launch apps, timings, OscGoesBrrr settings, OSC routes, and base-station settings.

The release zip is accompanied by SHA-256 and Sigstore verification files.
```
