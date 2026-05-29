# Pimax VRC Supervisor v1.2.3 Release Notes

This is the release-facing companion note for `PimaxVrcSupervisor-v1.2.3.zip`.

## Current Version

- Release tag: `v1.2.3`
- App version: `1.2.3`
- Assembly/file version: `1.2.3.0`
- Platform: Windows
- Runtime: self-contained .NET 9

## Included Files

- `PimaxVrcSupervisor.exe` - main supervisor console app
- `PimaxVrcSupervisorConfigurator.exe` - GUI configurator
- `PimaxVrcSupervisorSteamVrHost.exe` - SteamVR startup/dashboard host
- `supervisor.config.json` - documented configuration file
- `README.md` - full GitHub/user documentation
- `RELEASE_NOTES.md` - release-focused install, upgrade, and verification notes

## Highlights

- Makes the Ver2 SteamVR dashboard the default dashboard renderer.
- Removes the older v1.2.2 dashboard renderer from the active SteamVR host.
- Adds a top status strip for supervisor, SteamVR, core apps, OSC router, and base stations.
- Adds a clearer action grid, supervisor output panel, and footer/debug state line.
- Keeps hidden/inactive polling at 2000 ms and active visible polling at 100 ms.
- Preserves the existing command bridge, app key, overlay name, scheduled-task startup flow, and six dashboard commands.
- Keeps the existing dashboard commands, supervisor behavior, config schema, and SteamVR startup flow intact.

## Install

1. Download the right zip:
   - If you already have the .NET 9 Windows Desktop Runtime installed, download `PimaxVrcSupervisor-v1.2.3_noNET9.zip`.
   - If you do not have .NET 9 installed, download `PimaxVrcSupervisor-v1.2.3.zip`.
2. Extract it to a writable folder.
3. Choose one initial setup path:
   - 3a. Run `PimaxVrcSupervisor.exe` and answer the first-run prompts.
   - 3b. Use `PimaxVrcSupervisorConfigurator.exe` for the initial config.
4. Use `PimaxVrcSupervisorConfigurator.exe` for later configuration changes, including the **Basics**, **Startup**, **Auto Launch**, **Base Stations**, **OSC Router**, and **Raw JSON** tabs.

No separate .NET install is required for `PimaxVrcSupervisor-v1.2.3.zip`; the `_noNET9` zip requires .NET 9 to already be installed.

## Upgrade

1. Close any running supervisor or configurator instance.
2. Extract the new zip over the previous folder or into a fresh folder.
3. Keep your existing `supervisor.config.json` if it already contains your preferred paths and settings.
4. Open the configurator and press **Validate** to recheck executable paths and table entries.

## Verify

Expected companion assets:

- `PimaxVrcSupervisor-v1.2.3.zip.sha256`
- `PimaxVrcSupervisor-v1.2.3.zip.sigstore.json`

Checksum:

```powershell
Get-FileHash .\PimaxVrcSupervisor-v1.2.3.zip -Algorithm SHA256
Get-Content .\PimaxVrcSupervisor-v1.2.3.zip.sha256
```

Sigstore:

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.2.3.zip `
  --bundle .\PimaxVrcSupervisor-v1.2.3.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/(heads|tags)/.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

## Suggested GitHub Release Body

```markdown
Pimax VRC Supervisor v1.2.3 makes the Ver2 SteamVR dashboard the standard dashboard renderer.

The SteamVR dashboard host now uses the clearer Ver2 control surface with a top status strip, action grid, supervisor output panel, and footer state line. The older v1.2.2 dashboard renderer is no longer part of the active host path.

Supervisor commands, config behavior, SteamVR startup integration, base-station controls, OSC routing, and existing dashboard button behavior remain unchanged.

Download `PimaxVrcSupervisor-v1.2.3.zip`, extract it, and run `PimaxVrcSupervisor.exe`. Use `PimaxVrcSupervisorConfigurator.exe` to edit paths, detectors, startup mode, auto-launch apps, timings, OscGoesBrrr settings, OSC routes, and base-station settings.

SteamVR startup mode registers `PimaxVrcSupervisorSteamVrHost.exe` through a SteamVR app manifest and uses a separate on-demand elevated helper task to start the supervisor when SteamVR starts.

The release zip is accompanied by SHA-256 and Sigstore verification files.
```
