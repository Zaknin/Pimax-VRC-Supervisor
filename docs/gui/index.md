# GUI Manual

## Configurator Overview

`PimaxVrcSupervisorConfigurator.exe` is a compact Windows GUI for editing `supervisor.config.json` without changing the config schema.

## Features

- Validates expanded executable and folder paths, including paths that use `%APPDATA%` or `%LOCALAPPDATA%`.
- Rechecks executable paths when **Validate** is pressed, so externally deleted or moved files are reported immediately.
- Shows clearer `Found` / `Not found` indicators.
- Keeps status messages from going stale across tabs.
- Keeps `Save` visually primary.
- Resizes Auto Startup, Base Stations, and OSC Router tables to use available tab space while preserving the bottom status/action bar.

## Tabs

### General

Startup choices, diagnostics, and editor utilities.

### Face Tracking

Broken Eye, VRCFaceTracking, mouth-tracker, and fast reconnect settings.

### Base Stations

Scan, rename, enable, test, identify, and power SteamVR base stations.

### OSC Router

Receive endpoint and output routes for local OSC routing.

### Auto Startup

Extra apps to launch with the VR session.

### OSCGoesBrrr

Intiface, OscGoesBrrr, manual console launch mode, BLE scanner, and Lovense rules.

### Processes

Watched process names and cleanup targets.

### Detectors

Pimax, mouth tracker, and Lovense detection rules.

### Timers

Poll intervals, startup delays, reconnect waits, and shutdown grace periods.

### Raw JSON

Direct config editing when you need it.

## Usage

```powershell
.\PimaxVrcSupervisorConfigurator.exe
```

After making changes, press **Save** to write the updated `supervisor.config.json`.
