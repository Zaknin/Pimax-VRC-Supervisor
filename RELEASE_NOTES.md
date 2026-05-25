# Pimax VRC Supervisor v1.2.2 Release Notes

This is the release-facing companion note for `PimaxVrcSupervisor-v1.2.2.zip`.

## Current Version

- Release tag: `v1.2.2`
- App version: `1.2.2`
- Assembly/file version: `1.2.2.0`
- Platform: Windows
- Runtime: self-contained .NET 9

## Included Files

- `PimaxVrcSupervisor.exe` - main supervisor console app
- `PimaxVrcSupervisorConfigEditor.exe` - GUI config editor
- `PimaxVrcSupervisorSteamVrHost.exe` - SteamVR startup/dashboard host
- `supervisor.config.json` - documented configuration file
- `README.md` - full GitHub/user documentation
- `RELEASE_NOTES.md` - release-focused install, upgrade, and verification notes

## Highlights

- Reduces SteamVR dashboard idle wakeups by using a slower hidden/inactive poll interval.
- Uses a faster active loop only when the SteamVR dashboard is visible and this overlay is active.
- Avoids repeated `vrserver.exe` process enumeration by caching the SteamVR process handle.
- Caches the dashboard icon instead of loading and decoding it on each dirty render.
- Prevents overlapping status and console refresh requests from the dashboard host.
- Reuses the D3D11 pixel upload buffer to reduce active-dashboard GC pressure.
- Throttles repeated bridge/status/log failure messages while keeping useful diagnostics.
- Keeps the existing dashboard commands, supervisor behavior, config schema, and SteamVR startup flow intact.

## Install

1. Download the right zip:
   - If you already have the .NET 9 Windows Desktop Runtime installed, download `PimaxVrcSupervisor-v1.2.2_noNET9.zip`.
   - If you do not have .NET 9 installed, download `PimaxVrcSupervisor-v1.2.2.zip`.
2. Extract it to a writable folder.
3. Choose one initial setup path:
   - 3a. Run `PimaxVrcSupervisor.exe` and answer the first-run prompts.
   - 3b. Use `PimaxVrcSupervisorConfigEditor.exe` for the initial config.
4. Use `PimaxVrcSupervisorConfigEditor.exe` for later configuration changes, including the **Basics**, **Startup**, **Auto Launch**, **Base Stations**, **OSC Router**, and **Raw JSON** tabs.

No separate .NET install is required for `PimaxVrcSupervisor-v1.2.2.zip`; the `_noNET9` zip requires .NET 9 to already be installed.

## Upgrade

1. Close any running supervisor or config editor instance.
2. Extract the new zip over the previous folder or into a fresh folder.
3. Keep your existing `supervisor.config.json` if it already contains your preferred paths and settings.
4. Open the config editor and press **Validate** to recheck executable paths and table entries.

## Verify

Expected companion assets:

- `PimaxVrcSupervisor-v1.2.2.zip.sha256`
- `PimaxVrcSupervisor-v1.2.2.zip.sigstore.json`

Checksum:

```powershell
Get-FileHash .\PimaxVrcSupervisor-v1.2.2.zip -Algorithm SHA256
Get-Content .\PimaxVrcSupervisor-v1.2.2.zip.sha256
```

Sigstore:

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.2.2.zip `
  --bundle .\PimaxVrcSupervisor-v1.2.2.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/(heads|tags)/.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

## Suggested GitHub Release Body

```markdown
Pimax VRC Supervisor v1.2.2 focuses on SteamVR dashboard idle/active efficiency and release version consistency.

The SteamVR dashboard host now uses a slower hidden/inactive poll interval and a faster loop only when the dashboard is visible and this overlay is active. It also caches SteamVR process detection, caches the overlay icon, prevents overlapping dashboard refreshes, reuses the D3D11 upload buffer, and throttles repeated failure logs.

Supervisor commands, config behavior, SteamVR startup integration, base-station controls, OSC routing, and existing dashboard button behavior remain unchanged.

Download `PimaxVrcSupervisor-v1.2.2.zip`, extract it, and run `PimaxVrcSupervisor.exe`. Use `PimaxVrcSupervisorConfigEditor.exe` to edit paths, detectors, startup mode, auto-launch apps, timings, OscGoesBrrr settings, OSC routes, and base-station settings.

SteamVR startup mode registers `PimaxVrcSupervisorSteamVrHost.exe` through a SteamVR app manifest and uses a separate on-demand elevated helper task to start the supervisor when SteamVR starts.

The release zip is accompanied by SHA-256 and Sigstore verification files.
```
