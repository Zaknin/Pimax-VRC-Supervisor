# Supervisor Functions Reference

Major behavior-defining functions and classes in `PimaxVrcSupervisor.exe`.

## AppSupervisor

The main supervisor class that orchestrates the entire session lifecycle.

### Key Methods

| Method | Description |
| --- | --- |
| `RunAsync(CancellationToken)` | Main entry point. Runs the full startup sequence and monitoring loop. |
| `ExecuteSupervisorCommandAsync(string, CancellationToken)` | Executes a command from the VR overlay or dashboard. Supports: `status`, `log`, `restart-core-apps`, `start-osc-goes-brrr`, `base-stations-on`, `base-stations-off`, `restart-osc-router`. |
| `RunEmergencyCloseCleanupAsync()` | Emergency cleanup triggered by Ctrl+C or console close. |

### Startup Methods

| Method | Description |
| --- | --- |
| `EnsureExecutablePathsAsync()` | Resolves and validates executable paths. Prompts via file browser if missing. |
| `EnsureMouthTrackerPreferenceAsync()` | Prompts for mouth tracker preference if not configured. |
| `EnsureTurnOffSecondaryMonitorsPreferenceAsync()` | Prompts for monitor preference if not configured. |
| `EnsureStartupIntegrationPreferenceAsync()` | Prompts for startup mode if not configured. |
| `WaitForPimaxOnStartupAsync()` | Polls until the Pimax headset is detected. |
| `StartManagedAppsAsync()` | Starts Broken Eye, VRCFaceTracking, and auto-launch apps. |
| `StartCoreAppsAsync()` | Starts Broken Eye (with retries) then VRCFaceTracking. |
| `TryStartOscRouterAsync()` | Starts the OSC UDP router. |
| `InitializeOscGoesBrrrWorkflowAsync()` | Initializes the OscGoesBrrr workflow. |

### Monitoring Methods

| Method | Description |
| --- | --- |
| `ObserveWatchedShutdownProcesses()` | Checks if VRChat is running, crashed, or exited normally. |
| `IsPimaxConnectedAsync()` | Checks Pimax headset connection via USB detectors. |
| `IsMouthTrackerConnectedAsync()` | Checks mouth tracker connection. |
| `IsLovenseConnectedAsync()` | Checks Lovense device via USB and Bluetooth registry. |
| `DetectPimaxServiceLogReconnect()` | Scans PiService logs for HID remove/add sequences. |
| `DetectMouthTrackerPnPReconnectAsync()` | Scans Windows PnP events for mouth tracker reconnects. |
| `HandleConsoleHotkeysAsync()` | Processes `L`, `Space`, and `R` key presses. |

### Reconnect Handling

| Method | Description |
| --- | --- |
| `WaitForPimaxStableConnectedAsync()` | Waits for a stable headset connection after reconnect. |
| `StopManagedAppsAsync(ManagedAppStopReason)` | Stops managed apps (either for reconnect or session end). |
| `RestartCoreAppsAsync()` | Restarts Broken Eye and VRCFaceTracking. |
| `RestartVrcFaceTrackingAsync()` | Restarts only VRCFaceTracking (for mouth tracker reconnect). |

### Base Station Methods

| Method | Description |
| --- | --- |
| `TryPowerOnBaseStationsForSessionAsync()` | Sends wake passes to base stations. |
| `TryPowerDownBaseStationsForSessionAsync()` | Sends power-down commands to base stations. |
| `TryConfirmBaseStationStartupWithSteamVrAsync()` | Checks SteamVR tracking references for active base stations. |
| `ManualPowerOnBaseStationsAsync()` | Manual power-on (triggered by VR overlay). |
| `ManualPowerDownBaseStationsAsync()` | Manual power-down (triggered by VR overlay). |

### OSC Router Methods

| Method | Description |
| --- | --- |
| `TryStartOscRouterAsync()` | Starts the OSC router with port conflict handling. |
| `RetryOscRouterAsync()` | Retries OSC router startup (triggered by `Space` key). |
| `StopOscRouter()` | Stops and disposes the OSC router. |

### OscGoesBrrr Methods

| Method | Description |
| --- | --- |
| `StartLovenseIntifaceAsync()` | Starts Intiface. |
| `StartLovenseOscAsync()` | Starts the full workflow (Intiface → delay → OscGoesBrrr). |
| `StopLovenseAppsAsync()` | Stops OscGoesBrrr and Intiface. |
| `ScanForLovenseBleAdvertisementAsync()` | Scans for Lovense BLE advertisements. |

## SupervisorCommandServer

Named pipe and TCP command server for VR overlay communication.

| Member | Description |
| --- | --- |
| `PipeName` | `"PimaxVrcSupervisor.Command"` |
| `TcpPort` | `37957` |
| `Start()` | Starts both pipe and TCP listeners. |
| `Dispose()` | Stops both listeners. |

## BaseStationEmergencyCleanup

Detached helper for base station cleanup on console close.

| Method | Description |
| --- | --- |
| `TryLaunchDetached()` | Launches a detached cleanup process. |
| `RunAsync()` | Runs the power-down sequence with initial delay. |

## SteamVrTrackingReferenceReader

Reads active SteamVR tracking references via OpenVR API.

| Method | Description |
| --- | --- |
| `IsAvailable(out string)` | Checks if OpenVR is available. |
| `ReadActiveTrackingReferences()` | Returns active tracking reference devices. |

See also: [Reference Overview](index.md) · [Configuration Fields](configuration-fields.md) · [Config Editor Functions](config-editor-functions.md)
