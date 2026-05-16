# Pimax VRC Supervisor

A Windows helper for Pimax Crystal + VRChat setups. It supervises Broken Eye and VRCFaceTracking, watches headset and optional Vive mouth tracker connectivity, and restarts the right apps when USB/device state changes.

## What It Does

- Waits for the Pimax headset before launching anything.
- Prompts for `Broken Eye.exe` and `VRCFaceTracking.exe` on first run if paths are not configured.
- Can create an elevated Scheduled Task on first setup to launch the supervisor when `VRChat.exe` starts while SteamVR is running.
- The Scheduled Task starts a hidden elevated watcher at Windows sign-in and starts that watcher immediately after setup.
- Starts Broken Eye first, retrying up to 10 times if it does not appear as running after 5 seconds, then starts VRCFaceTracking.
- Watches Pimax reconnects and restarts both managed apps after reconnect.
- Optionally watches the Vive mouth tracker / HTC Multimedia Camera and restarts only VRCFaceTracking when it reconnects.
- Watches `VRChat.exe` and closes managed apps when VRChat exits normally.
- If VRChat appears to crash, waits 5 minutes for it to relaunch before exiting.

## Requirements

- Windows
- .NET 9 runtime or SDK
- Pimax Crystal headset
- [Broken Eye](https://github.com/ghostiam/BrokenEye)
- [VRCFaceTracking](https://docs.vrcft.io/docs/vrcft-software/vrcft)
- Optional: Vive mouth tracker exposed as `HTC Multimedia Camera`

## First Run

Run `PimaxVrcSupervisor.exe`.

On first run, the app may ask:

- Where `Broken Eye.exe` is located.
- Where `VRCFaceTracking.exe` is located.
- Whether you use a Vive mouth tracker.
- Whether to create the elevated VRChat/SteamVR auto-launch Scheduled Task.

Your answers are saved into `supervisor.config.json` next to the exe.

## Configuration

Edit `supervisor.config.json` to adjust paths, process names, detector rules, polling intervals, startup delays, and shutdown behavior. The file includes inline comments for each setting.

Important defaults:

- `BrokenEyePath` starts empty.
- `VrcFaceTrackingPath` starts empty, but the file picker opens in the usual Steam install folder.
- `MouthTrackerUser` starts empty and asks a Yes/No question on first run.
- `AutoLaunchScheduledTask` starts empty and asks a Yes/No question on first setup.
- `PollIntervalSeconds` defaults to `5`.

## Auto-Launch Task

If enabled, the app creates a highest-privilege Scheduled Task named `Pimax VRC Supervisor Auto Launch`.

The task starts a hidden watcher with:

```text
PimaxVrcSupervisor.exe --watch-vrchat-auto-launch
```

The watcher polls for `VRChat.exe` and SteamVR. SteamVR is detected by checking its `vrserver.exe` process. When both are running and the normal supervisor is not already open, it launches `PimaxVrcSupervisor.exe`. This avoids relying on Windows Security audit/process-creation events.

To reinstall or repair the task directly:

```powershell
.\PimaxVrcSupervisor.exe --install-auto-launch-task
```

## Building From Source

```powershell
dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -o .\release\PimaxVrcSupervisor
```

The built app will be in:

```text
release\PimaxVrcSupervisor
```

## Notes

The executable requests administrator privileges because some launched tools may require elevation.
