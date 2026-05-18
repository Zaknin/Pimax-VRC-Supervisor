# Pimax VRC Supervisor

A Windows helper for Pimax Crystal + VRChat setups. It supervises Broken Eye and VRCFaceTracking, watches headset and optional Vive mouth tracker connectivity, and restarts the right apps when USB/device state changes.

## What It Does

- Waits for the Pimax headset before launching anything.
- Prompts for `Broken Eye.exe` and `VRCFaceTracking.exe` on first run if paths are not configured.
- Can create an elevated Scheduled Task on first setup to launch the supervisor when `VRChat.exe` starts while SteamVR is running.
- The Scheduled Task starts a hidden elevated watcher at Windows sign-in and starts that watcher immediately after setup.
- If enabled, before starting Broken Eye saves the active Windows monitor layout and switches to monitor 1 only when multiple monitors are active.
- Starts Broken Eye first, retrying up to 10 times if it does not appear as running after 5 seconds, then starts VRCFaceTracking.
- Starts optional user-defined apps after the main Broken Eye/VRCFaceTracking sequence has completed.
- Watches Pimax HMD/runtime reconnects, waits for the connection to stay stable, and restarts both managed apps after reconnect.
- Watches Pimax PiService HID remove/add log events so short reconnects are still caught even when Windows USB state is back before the next poll.
- Optionally watches the Vive mouth tracker / HTC Multimedia Camera and restarts only VRCFaceTracking when it reconnects.
- Watches Windows PnP events for the Vive mouth tracker so fast tracker restarts can still restart VRCFaceTracking.
- Watches `VRChat.exe`; if monitor handling is enabled, waits for `vrserver.exe`, restores the previous monitor layout, then closes managed apps and user-defined auto-launch apps.
- If VRChat appears to crash, waits 5 minutes for it to relaunch before exiting.
- Prevents duplicate normal supervisor instances from racing each other; the hidden auto-launch watcher can still run alongside one supervisor.

## Requirements

- Windows
- No separate .NET install is needed when using the self-contained release zip
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
- Whether to turn off secondary monitors while using the headset.
- Whether to create the elevated VRChat/SteamVR auto-launch Scheduled Task.

Your answers are saved into `supervisor.config.json` next to the exe.

You can also run `PimaxVrcSupervisorConfigEditor.exe` to edit the same config file with a small GUI. It provides browse buttons for executable paths, checkboxes for yes/no settings, an Auto Launch tab for user-defined apps, editors for process names and detector rules, and numeric controls for timing values.

## Configuration

Edit `supervisor.config.json` to adjust paths, process names, detector rules, polling intervals, startup delays, and shutdown behavior. The file includes inline comments for each setting.

Important defaults:

- `BrokenEyePath` starts empty.
- `VrcFaceTrackingPath` starts empty, but the file picker opens in the usual Steam install folder.
- `MouthTrackerUser` starts empty and asks a Yes/No question on first run.
- `TurnOffSecondaryMonitors` starts empty and asks a Yes/No question on first run.
- `AutoLaunchScheduledTask` starts empty and asks a Yes/No question on first setup.
- `AutoLaunchApps` defaults to an empty list. Each item can define `Name`, `Path`, `Enabled`, `RestartOnPimaxReconnect`, and `RunAsAdmin`. The process name is inferred from the exe filename.
- `SteamVrServerProcessNames` defaults to `vrserver` and controls when monitors are restored after VRChat exits if secondary monitor handling is enabled.
- `PollIntervalSeconds` defaults to `2`.
- `PimaxDetectors` defaults to Pimax HMD/runtime USB IDs (`VID_34A4`) instead of the eye tracker-only `EyeChip` device, so reconnects are detected when the headset path actually drops and returns.
- `UsePimaxServiceLogReconnectDetector` defaults to `true` and watches `%LOCALAPPDATA%\Pimax\PiService\Log\PiService__*.log` for fast runtime HID reconnects.
- `UseMouthTrackerPnPReconnectDetector` defaults to `true` and watches Windows Kernel-PnP events for fast Vive mouth tracker reconnects.

Example auto-launch app:

```json
"AutoLaunchApps": [
  {
    "Name": "Example overlay",
    "Path": "C:\\Tools\\ExampleOverlay\\ExampleOverlay.exe",
    "Enabled": true,
    "RestartOnPimaxReconnect": true,
    "RunAsAdmin": false
  }
]
```

Set `RestartOnPimaxReconnect` to `false` for apps that should stay running during the Pimax reconnect restart cycle. They will still be closed when the VRChat session ends.
Set `RunAsAdmin` to `true` only for extra auto-launch apps that must run elevated. Broken Eye and VRCFaceTracking are still started through the supervisor's elevated launch path.

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

## Signed and Attested GitHub Releases

GitHub Actions builds, Sigstore-signs, attests, and publishes new release zips when you push a `v*` tag, such as `v1.0.9`, or run the `Release` workflow manually.

The free signing path uses GitHub Actions OIDC and Sigstore keyless signing. It does not require a certificate, password, private key, or repository secret. The workflow publishes:

- `PimaxVrcSupervisor-<version>.zip`
- `PimaxVrcSupervisor-<version>.zip.sha256`
- `PimaxVrcSupervisor-<version>.zip.sigstore.json`

If you publish a release through GitHub's release UI with uploaded `.zip` assets, the `Sign Release Assets` workflow signs those zip files and uploads matching `.sha256` and `.sigstore.json` files. If you add assets after a release is already published, run that workflow manually for the release tag.

The Sigstore bundle proves the zip was signed by this repository's release workflow and is recorded in Sigstore's transparency log. This helps users verify release integrity, but it does not make Windows treat the app as a verified publisher for SmartScreen.

To verify a release zip with cosign:

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.0.9.zip `
  --bundle .\PimaxVrcSupervisor-v1.0.9.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/tags/v.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

Optional: add these repository secrets to Authenticode-sign the app binaries before packaging:

- `WINDOWS_SIGNING_CERTIFICATE_BASE64`: base64 text for your `.pfx` code-signing certificate.
- `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`: password for the `.pfx` certificate.

To create the base64 certificate value from PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\to\certificate.pfx"))
```

The workflow signs:

- `PimaxVrcSupervisor.exe`
- `PimaxVrcSupervisor.dll`
- `PimaxVrcSupervisorConfigEditor.exe`
- `PimaxVrcSupervisorConfigEditor.dll`

## Building From Source

```powershell
dotnet publish .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.0.8
dotnet publish .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj -c Release -r win-x64 --self-contained true -o .\release\PimaxVrcSupervisor-v1.0.8
```

The built app will be in:

```text
release\PimaxVrcSupervisor-v1.0.8
```

## Notes

The executable requests administrator privileges because some launched tools may require elevation.
