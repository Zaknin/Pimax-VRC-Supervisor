# Pimax VRC Supervisor

A Windows helper for Pimax Crystal + VRChat setups. It supervises Broken Eye and VRCFaceTracking, watches headset and optional Vive mouth tracker connectivity, and restarts the right apps when USB/device state changes.

## What It Does

- Waits for the Pimax headset before launching anything.
- Prompts for `Broken Eye.exe` and `VRCFaceTracking.exe` on first run if paths are not configured.
- Starts Broken Eye first, waits, verifies it is running, then starts VRCFaceTracking.
- Watches Pimax reconnects and restarts both managed apps after reconnect.
- Optionally watches the Vive mouth tracker / HTC Multimedia Camera and restarts only VRCFaceTracking when it reconnects.
- Watches `VRChat.exe` and closes managed apps when VRChat exits normally.
- If VRChat appears to crash, waits 5 minutes for it to relaunch before exiting.

## Requirements

- Windows
- .NET 9 runtime or SDK
- Pimax Crystal headset
- Broken Eye
- VRCFaceTracking
- Optional: Vive mouth tracker exposed as `HTC Multimedia Camera`

## First Run

Run `PimaxVrcSupervisor.exe`.

On first run, the app may ask:

- Where `Broken Eye.exe` is located.
- Where `VRCFaceTracking.exe` is located.
- Whether you use a Vive mouth tracker.

Your answers are saved into `supervisor.config.json` next to the exe.

## Configuration

Edit `supervisor.config.json` to adjust paths, process names, detector rules, polling intervals, startup delays, and shutdown behavior. The file includes inline comments for each setting.

Important defaults:

- `BrokenEyePath` starts empty for sharing/public releases.
- `VrcFaceTrackingPath` starts empty, but the file picker opens in the usual Steam install folder.
- `MouthTrackerUser` starts empty and asks a Yes/No question on first run.
- `PollIntervalSeconds` defaults to `5`.

## Building From Source

```powershell
dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -o .\release\PimaxVrcSupervisor
```

The built app will be in:

```text
release\PimaxVrcSupervisor
```

## Notes

The executable requests administrator privileges because some launched tools may require elevation. If you want to avoid repeated UAC prompts, start it from an already elevated launcher or use a Windows Scheduled Task configured to run with highest privileges.
