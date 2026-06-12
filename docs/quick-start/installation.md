# Installation

## Download

Download the latest release zip from the project releases page. If the release offers more than one package, choose:

- the full package if you are not sure whether .NET is installed
- the smaller package only if you already have the required .NET Windows Desktop Runtime

## Extract

Extract the zip to a writable folder such as:

```text
C:\Tools\PimaxVrcSupervisor
```

Avoid protected folders such as `Program Files` unless you understand the permissions involved.

## First Launch

Run:

```text
PimaxVrcSupervisorConfigurator.exe
```

Windows may ask for permission when the app creates or repairs startup integration. Approve it only if you are intentionally enabling autostart.

## Expected Files

The release folder should contain:

- `PimaxVrcSupervisor.exe`
- `PimaxVrcSupervisorConfigurator.exe`
- `PimaxVrcSupervisorSteamVrHost.exe`
- `PimaxVrcSupervisorTui.exe`
- `supervisor.config.json`
- `Assets\vr-overlay-icon.png`

If Terminal UI or SteamVR Overlay does not start, first confirm the matching executable exists in the same release folder.
