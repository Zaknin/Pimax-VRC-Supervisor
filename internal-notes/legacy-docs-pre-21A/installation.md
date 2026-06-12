# Installation

## Download

1. Go to the [GitHub releases](https://github.com/Zaknin/Pimax-VRC-Supervisor/releases) page.
2. Choose the correct zip:
   - **Already have .NET 9 Windows Desktop Runtime?** Download `PimaxVrcSupervisor-v1.2.0_noNET9.zip`.
   - **Do not have .NET 9?** Download `PimaxVrcSupervisor-v1.2.0.zip` (self-contained).

## Extract

Extract the zip somewhere writable, for example:

```text
C:\Tools\PimaxVrcSupervisor
```

The folder will contain:

- `PimaxVrcSupervisor.exe`
- `PimaxVrcSupervisorConfigurator.exe`
- `supervisor.config.json`
- `README.md`

## Initial Setup

Choose one of the following paths:

### Option A â€” Run the Supervisor

```powershell
.\PimaxVrcSupervisor.exe
```

On first launch, the supervisor prompts for:

- Path to `Broken Eye.exe`
- Path to `VRCFaceTracking.exe`
- Whether you use a Vive mouth tracker
- Whether to turn off secondary monitors during VR
- Whether to create the elevated VRChat/SteamVR auto-launch Scheduled Task
- Whether to start with SteamVR through the SteamVR manifest host

Answers are saved to `supervisor.config.json`.

### Option B â€” Use the Configurator

```powershell
.\PimaxVrcSupervisorConfigurator.exe
```

The editor provides a GUI for setting all configuration values without editing JSON by hand.

## Release Verification

Each release includes:

- `PimaxVrcSupervisor-v1.2.0.zip`
- `PimaxVrcSupervisor-v1.2.0.zip.sha256`
- `PimaxVrcSupervisor-v1.2.0.zip.sigstore.json`

### Verify the checksum

```powershell
Get-FileHash .\PimaxVrcSupervisor-v1.2.0.zip -Algorithm SHA256
Get-Content .\PimaxVrcSupervisor-v1.2.0.zip.sha256
```

### Verify the Sigstore bundle

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.2.0.zip `
  --bundle .\PimaxVrcSupervisor-v1.2.0.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/(heads|tags)/.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

Sigstore verification proves the zip was signed by the repository workflow and recorded in the transparency log.

## Build From Source

Install the .NET 9 SDK, then run:

```powershell
dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.2.0
dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.2.0
dotnet publish .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.2.0
```

The output folder will contain both executables, the config file, and the README.
