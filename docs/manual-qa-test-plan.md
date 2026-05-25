# Manual QA Test Plan — Pimax VRC Supervisor / vrmanifest-gui-overhaul

Codex pre-check date: 2026-05-24

Status legend:
- `Codex pre-check: Pass` means Codex verified the item locally with non-interactive commands.
- `Human QA required` means the test needs hardware, GUI interaction, SteamVR/VRChat runtime behavior, UAC, or visual confirmation.
- Leave human `Actual result` fields blank until you run the checklist yourself.

Codex pre-check summary:
- .NET SDK found: `9.0.314`; Windows runtime environment: Windows `10.0.26200`, `win-x64`.
- `dotnet build` succeeded for all three projects with `0 Warning(s)` and `0 Error(s)`.
- `dotnet publish -c Release -r win-x64 --self-contained true` succeeded for all three projects into `publish-manual-qa-codex`.
- Published executables found: `PimaxVrcSupervisor.exe`, `PimaxVrcSupervisorConfigEditor.exe`, `PimaxVrcSupervisorSteamVrHost.exe`.
- Published config/docs/assets found: `supervisor.config.json`, `README.md`, `RELEASE_NOTES.md`, `Assets/vr-overlay-icon.png`.
- Published executable versions found: file version `1.2.1.0`, product version `1.2.1` for all three executables.
- Source documentation mismatch found: `README.md` and `RELEASE_NOTES.md` still reference `1.2.0`, while all project files stamp `1.2.1`.

## 0. Scope discovered from code

- Console supervisor lifecycle: `PimaxVrcSupervisor/Program.cs`, especially `AppSupervisor.RunAsync`, `StartManagedAppsAsync`, `StartCoreAppsAsync`, `StopManagedAppsAsync`, `RestartCoreAppsAsync`.
- First-run prompts: `EnsureExecutablePathsAsync`, `EnsureMouthTrackerPreferenceAsync`, `EnsureTurnOffSecondaryMonitorsPreferenceAsync`, `EnsureStartupIntegrationPreferenceAsync`, `EnsureBaseStationPowerPreferenceAsync`.
- Config loading/saving: `SupervisorConfig.Load`, `SaveExecutableSettings`, `SaveMouthTrackerPreference`, `SaveTurnOffSecondaryMonitorsPreference`, `SaveAutoLaunchScheduledTaskPreference`, `SaveBaseStationSettings`.
- Command-line flags: `--config`, `--install-auto-launch-task`, `--apply-startup-integration`, `--show-result`, `--watch-vrchat-auto-launch`, `--steamvr-start`, `--emergency-base-station-cleanup`, `--delay-seconds`.
- Headset detection and reconnects: `IsPimaxConnectedAsync`, `DetectPimaxServiceLogReconnect`, `WaitForPimaxStableConnectedAsync`.
- Mouth tracker detection: `IsMouthTrackerConnectedAsync`, `DetectMouthTrackerPnPReconnectAsync`, `RestartVrcFaceTrackingAsync`.
- VRChat/SteamVR process detection: `ObserveWatchedShutdownProcesses`, `WatchedShutdownProcessNames`, `SteamVrServerProcessNames`.
- Auto-launch apps: `AutoLaunchAppConfig`, `GetEnabledAutoLaunchApps`, `RunAfterLaunchAppsRoutineAsync`.
- OSCGoesBrrr/Intiface workflow: `InitializeOscGoesBrrrWorkflowAsync`, `RunOscGoesBrrrManualRoutineAsync`, `StartLovenseOscAsync`, BLE scanner methods.
- OSC router: `OscRouter`, `TryStartOscRouterAsync`, `RestartOscRouterAsync`, `RetryOscRouterAsync`.
- Base station control: `PimaxVrcSupervisor/BaseStationSupport.cs`, `BaseStationDiscovery`, `BaseStationGattClient`, `BaseStationPowerDownRoutine`, supervisor power-on/down methods.
- Monitor handling: `MonitorLayoutController`, `DisplayLayoutSnapshot`.
- Scheduled tasks and startup integration: `ScheduledTaskInstaller`, `StartupIntegration`, `SteamVrStartupInstaller`, `ScheduledTaskPathValidator`.
- Config editor GUI: `PimaxVrcSupervisor.ConfigEditor/Program.cs`, `ConfigEditorForm`.
- GUI tabs: General, Auto Launch, Base Stations, Detectors, Processes, OSC Router, OSCGoesBrrr, Timing, Raw JSON.
- GUI validation: `ValidateCurrentConfig`, `ValidatePath`, `ValidateAutoLaunchApps`, `ValidateOscRoutes`, `ValidateBaseStations`, `ValidateTimingValues`, `ValidateRawJsonText`.
- SteamVR overlay host: `PimaxVrcSupervisor.SteamVrHost/Program.cs`, `SteamVrDashboardHost`, `OpenVrOverlaySession`, `GpuOverlayRenderer`.
- Overlay commands: `restart-core-apps`, `restart-osc-router`, `start-osc-goes-brrr`, `base-stations-on`, `base-stations-off`, `restart-supervisor`.
- Existing automated tests: none found in repository.
- Solution file: none found; build uses individual `.csproj` files.

## 1. Test environment requirements

- Windows 10/11. The projects target `net9.0-windows10.0.19041.0`.
- Administrator/elevated rights for `PimaxVrcSupervisor.exe`; `app.manifest` requests `requireAdministrator`.
- .NET 9 SDK for source builds; self-contained release output should not require a separate runtime.
- SteamVR installed for manifest registration, `vrserver.exe`, OpenVR overlay, and base-station tracking checks.
- VRChat installed for watched-process lifecycle tests.
- Pimax Crystal-compatible headset for full headset detection and reconnect testing.
- Optional Vive mouth tracker exposed as `HTC Multimedia Camera`.
- Optional Broken Eye executable.
- Optional VRCFaceTracking executable.
- Optional Intiface and OscGoesBrrr executables.
- Optional SteamVR base stations and Bluetooth LE adapter.
- Optional multiple monitors for monitor layout tests.
- Optional UDP/OSC testing tool such as Packet Sender, Protokol, or another local UDP listener.
- Back up before testing:
  - `supervisor.config.json` next to the tested executables.
  - Any named `*.config.json` profiles in the app folder.
  - Windows Scheduled Tasks named `Pimax VRC Supervisor Auto Launch` and `Pimax VRC Supervisor SteamVR Start`.
  - `%LOCALAPPDATA%\openvr\openvrpaths.vrpath`.
  - Existing `PimaxVrcSupervisor.vrmanifest` files.
  - Current monitor layout.
  - Current base station power state.

## 2. Build and packaging verification

### BUILD-001 — Clean debug build
Purpose:
Verify all projects compile from source.

Preconditions:
- .NET 9 SDK installed.
- Branch is `vrmanifest-gui-overhaul`.

Steps:
1. Open PowerShell in the repository root.
2. Run `dotnet build .\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj`.
3. Run `dotnet build .\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj`.
4. Run `dotnet build .\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj`.

Expected result:
- [x] `PimaxVrcSupervisor` builds successfully.
- [x] `PimaxVrcSupervisor.ConfigEditor` builds successfully.
- [x] `PimaxVrcSupervisor.SteamVrHost` builds successfully.
- [x] No build warnings.
- [x] No build errors.

Codex pre-check:
- [x] Pass. All three `dotnet build` commands succeeded with `0 Warning(s)` and `0 Error(s)`.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- Codex verified with local `dotnet build` commands on 2026-05-24.

### BUILD-002 — Release publish output
Purpose:
Verify self-contained release-style output.

Preconditions:
- BUILD-001 passed.

Steps:
1. Publish all three projects with `-c Release -r win-x64 --self-contained true` to the same output folder.
2. Inspect the output folder.

Expected result:
- [x] `PimaxVrcSupervisor.exe` exists.
- [x] `PimaxVrcSupervisorConfigEditor.exe` exists.
- [x] `PimaxVrcSupervisorSteamVrHost.exe` exists.
- [x] `supervisor.config.json` exists.
- [x] `README.md` exists.
- [x] `RELEASE_NOTES.md` exists.
- [x] .NET self-contained runtime files exist.
- [x] Vortice overlay dependencies exist.

Codex pre-check:
- [x] Pass. Published successfully to `publish-manual-qa-codex`.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- Codex verified with local `dotnet publish` commands on 2026-05-24.

### BUILD-003 — Resource and version stamping
Purpose:
Verify expected resources and versions are present.

Preconditions:
- BUILD-002 passed.

Steps:
1. Inspect published `Assets` folder.
2. Inspect file/product version on all three executables.
3. Check source app manifest for elevation requirement.

Expected result:
- [x] `Assets\vr-overlay-icon.png` is copied to publish output.
- [x] All three executables report file version `1.2.1.0`.
- [x] All three executables report product version `1.2.1`.
- [x] Supervisor source manifest requests `requireAdministrator`.
- [ ] Human QA: Explorer shows expected executable icons.
- [ ] Human QA: launching `PimaxVrcSupervisor.exe` from Explorer requests UAC elevation.

Codex pre-check:
- [x] Pass for static resource/version/manifest checks.
- [ ] Human QA required for Explorer icon rendering and UAC prompt.

Actual result:
- [ ] Pass
- [ ] Fail
Notes:
- Codex verified static resource/version/manifest checks. Human QA still needs to verify visible icons and UAC launch behavior.

### BUILD-004 — Documentation version consistency
Purpose:
Catch version mismatches before release.

Preconditions:
- Repository checked out.

Steps:
1. Compare project versions with README/release notes versions.

Expected result:
- [ ] README version references match project version.
- [ ] Release notes version references match project version.
- [x] Any mismatch is recorded.

Codex pre-check:
- [ ] Fail for version consistency. Project files stamp `1.2.1`, but `README.md` and `RELEASE_NOTES.md` still reference `1.2.0`.

Actual result:
- [ ] Pass
- [x] Fail
Notes:
- Codex found `1.2.1` in project/executable versions and `1.2.0` in README/release notes.

### BUILD-005 — Launch behavior from publish folder
Purpose:
Verify executables launch from a release-like folder.

Preconditions:
- BUILD-002 passed.
- Human tester can interact with UAC and GUI windows.

Steps:
1. Launch `PimaxVrcSupervisorConfigEditor.exe`.
2. Launch `PimaxVrcSupervisor.exe`.
3. If SteamVR is installed and running, launch `PimaxVrcSupervisorSteamVrHost.exe`.

Expected result:
- [x] Config Editor opens and loads a config.
- [x] Supervisor starts and prints version/config banner.
- [x] SteamVR host starts or shows a clear SteamVR/OpenVR error.
- [x] No app crashes immediately without a useful message.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

## 3. Configuration tests

### CONFIG-001 — Load default commented config
Purpose:
Verify default config loads despite comments.

Preconditions:
- Published output contains `supervisor.config.json`.

Steps:
1. Open Config Editor.
2. Confirm all tabs populate.
3. Start Supervisor with the same config.

Expected result:
- [x] Config Editor loads without JSON errors.
- [x] Supervisor loads without JSON errors.
- [x] Visible defaults match `PimaxVrcSupervisor/supervisor.config.json`.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### CONFIG-002 — Explicit `--config` path
Purpose:
Verify Supervisor can load a specific config.

Preconditions:
- Create a separate test config.

Steps:
1. Run `PimaxVrcSupervisor.exe --config "FULL_PATH_TO_TEST_CONFIG"`.
2. Change `DisplayName` in the test config.
3. Run again.

Expected result:
- [x] Supervisor uses the explicit config.
- [x] Startup banner shows the test config `DisplayName`.
- [x] Missing explicit config path does not silently edit the wrong file.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### CONFIG-003 — Save and reload config
Purpose:
Verify GUI saves current editor values.

Preconditions:
- Back up the config file.

Steps:
1. Change `DisplayName`, executable paths, startup mode, one timing value, one route, and one app row.
2. Click Save.
3. Reopen the JSON file.
4. Reload Config Editor.

Expected result:
- [x] Changed fields are saved.
- [x] JSON remains valid.
- [x] Reloaded editor shows saved values.
- [x] Unrelated fields are not unexpectedly removed.

Actual result:
- [X] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### CONFIG-004 — Legacy/fallback config fields
Purpose:
Verify backward-compatible config fields still load.

Preconditions:
- Test config can be edited manually.

Steps:
1. Add `OscGoesBrrrrPath` with four `r`s and leave `OscGoesBrrrPath` empty.
2. Add an OSC route using `OutputPort`.
3. Add auto-launch app with `CloseOnPimaxDisconnect`.
4. Load config in Config Editor and Supervisor.

Expected result:
- [ ] Legacy OscGoesBrrr path is accepted when new field is empty.
- [ ] `OutputPort` loads as target app receive port.
- [ ] Auto-launch reconnect behavior follows legacy alias.
- [ ] Saving writes current field names where the editor owns that data.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

## 4. Console mode tests — `PimaxVrcSupervisor.exe`

### CONSOLE-001 — First-run executable path prompts
Purpose:
Verify missing Broken Eye and VRCFaceTracking paths are prompted and saved.

Preconditions:
- Test config has empty or invalid `BrokenEyePath` and `VrcFaceTrackingPath`.

Steps:
1. Start `PimaxVrcSupervisor.exe`.
2. Select `Broken Eye.exe` when prompted.
3. Select `VRCFaceTracking.exe` when prompted.
4. Inspect config.

Expected result:
- [x] Console reports missing/not found paths.
- [x] Browse dialogs open.
- [x] Selected paths are saved.
- [x] Process names are inferred or mismatch warnings are shown.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### CONSOLE-002 — First-run preference prompts
Purpose:
Verify first-run prompt sequence and saved preferences.

Preconditions:
- `MouthTrackerUser`, `TurnOffSecondaryMonitors`, `AutoLaunchScheduledTask`, `StartupLaunchMode`, and `BaseStationsEnabled` are unset/empty where possible.

Steps:
1. Start Supervisor.
2. Answer mouth tracker prompt.
3. Answer monitor prompt.
4. Choose startup mode.
5. Answer base station prompt if shown.
6. Inspect config.

Expected result:
- [ ] Each prompt appears once.
- [ ] Answers save as JSON values.
- [ ] Startup choice maps to `None`, `ScheduledTask`, or `SteamVrManifest`.
- [ ] Re-running does not ask again for configured choices.

Actual result:
- [ ] Pass
- [x] Fail
Notes:
- [ ] Human QA required.
missing prompt for monitors, mouth tracker, auto launch scheduled

2026-05-25 retest: still failing with restored default config. Supervisor only prompted for Broken Eye and VRCFaceTracking; first-run preference prompts did not appear.
2026-05-25 fix status: Config Editor now preserves unset first-run preference fields when loading/restoring defaults and saving unless those binary controls are changed. Note: published release folder still had a newer tester-edited config with concrete values after publish; restore defaults/save or replace with the source default config before retesting CONSOLE-002 from that folder.
2026-05-25 follow-up fix status: First-run boolean preference saves now verify the written config and repair with a structured JSON update fallback if text replacement does not persist the selected Yes/No answer.
2026-05-25 follow-up fix status: If either GUI first-run binary preference is changed, Config Editor now saves both mouth-tracker and secondary-monitor choices explicitly as true/false so later Supervisor launches do not treat the untouched one as unset.
2026-05-25 follow-up fix status: Configured `StartupLaunchMode=None` now skips startup-integration cleanup during normal Supervisor launch. Cleanup still runs for first-run No selection and explicit GUI/CLI startup integration apply.
2026-05-25 follow-up fix status: First-run prompts are now controlled by `RunInitialSetupQuestions`. Default config sets it true, Supervisor writes it false after setup choices are saved, and Config Editor does not expose it.

immeditely after selecing broken eye and vrcft exe output

Dashboard command TCP endpoint ready: 127.0.0.1:37957
Mouth tracker monitoring is disabled by config.
OSC router is disabled by config.
Starting Broken Eye (attempt 1/10)...
Checking whether Broken Eye is running in 5 seconds...
Broken Eye is running after attempt 1.
Waiting 5 seconds before starting VRCFaceTracking...



### CONSOLE-003 — Duplicate instance prevention
Purpose:
Verify normal supervisor instances do not race.

Preconditions:
- One Supervisor instance is already running.

Steps:
1. Start a second `PimaxVrcSupervisor.exe`.

Expected result:
- [ ] Second instance prints duplicate-instance message.
- [x] Existing instance continues running.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.
second instance immediately closes for supervisor. For the config editor, I can start a second instance.

### CONSOLE-004 — Startup with no headset
Purpose:
Verify startup waits for Pimax headset.

Preconditions:
- Pimax headset disconnected.
- Valid core app paths configured.

Steps:
1. Start Supervisor.
2. Watch console output.

Expected result:
- [x] Console prints disconnected initial state.
- [x] Console prints `Waiting for the headset to connect...`.
- [x] Managed apps do not launch before headset detection.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Hardware-dependent.
- [ ] Human QA required.

### CONSOLE-005 — Startup with headset connected
Purpose:
Verify normal startup sequencing.

Preconditions:
- Pimax headset connected.
- Broken Eye and VRCFaceTracking paths valid.

Steps:
1. Start SteamVR if needed.
2. Start Supervisor.
3. Observe process launch order.

Expected result:
- [ ] Pimax is detected.
- [ ] OSC router starts first if enabled.
- [ ] Broken Eye starts before VRCFaceTracking.
- [ ] Delay before VRCFaceTracking matches config.
- [ ] Auto-launch apps start after core apps.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Hardware-dependent.
- [x] Human QA required.

### CONSOLE-006 — Reconnect handling
Purpose:
Verify Pimax reconnect restarts managed apps.

Preconditions:
- Pimax headset connected.
- Managed apps running.

Steps:
1. Disconnect or disable the Pimax device.
2. Reconnect it.
3. Wait for reconnect delay.

Expected result:
- [X] Reconnect is detected by device polling or PiService logs.
- [X] Supervisor waits for stable connection.
- [X] Broken Eye and VRCFaceTracking restart.
- [x] Auto-launch apps obey `RestartOnPimaxReconnect`.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [X] Hardware-dependent.
- [X] Human QA required.

### CONSOLE-007 — Mouth tracker handling
Purpose:
Verify mouth tracker reconnect restarts only VRCFaceTracking.

Preconditions:
- `MouthTrackerUser=true`.
- Vive mouth tracker available.

Steps:
1. Start Supervisor and managed apps.
2. Disconnect mouth tracker.
3. Reconnect mouth tracker.

Expected result:
- [x] Mouth tracker reconnect is detected.
- [x] Only VRCFaceTracking restarts.
- [x] Broken Eye and auto-launch apps remain running.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Hardware-dependent.
- [ ] Human QA required.

### CONSOLE-008 — VRChat shutdown cleanup
Purpose:
Verify cleanup after VRChat exits.

Preconditions:
- VRChat can be started.
- Managed apps are running under Supervisor.

Steps:
1. Start VRChat.
2. Start Supervisor.
3. Close VRChat normally.

Expected result:
- [ ] Supervisor detects watched process exit.
- [ ] Auto-launch apps, VRCFaceTracking, Broken Eye, Intiface/OscGoesBrrr close as configured.
- [ ] Supervisor exits after cleanup.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### CONSOLE-009 — VRChat crash grace period
Purpose:
Verify crash/relaunch behavior.

Preconditions:
- Set `WatchedProcessCrashRelaunchGraceSeconds` to a short test value.

Steps:
1. Start watched process.
2. Kill it abnormally.
3. Relaunch within the grace period.
4. Repeat and do not relaunch.

Expected result:
- [ ] Supervisor waits for relaunch after likely crash.
- [ ] Relaunch continues supervision.
- [ ] No relaunch triggers cleanup after grace period.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### CONSOLE-010 — Auto-launch apps
Purpose:
Verify extra app startup and cleanup.

Preconditions:
- Configure at least one enabled and one disabled auto-launch app.

Steps:
1. Start Supervisor with headset connected.
2. Observe processes.
3. End session.

Expected result:
- [ ] Enabled app launches.
- [ ] Disabled app does not launch.
- [ ] `RunAsAdmin` and `StartMinimized` behave as configured.
- [ ] Enabled app is closed during cleanup.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### CONSOLE-011 — OSCGoesBrrr / Intiface workflow
Purpose:
Verify manual and repair workflow.

Preconditions:
- Intiface and OscGoesBrrr paths configured.

Steps:
1. Start Supervisor.
2. Press `F1`.
3. Press `2`.
4. Repeat when one workflow app is already running.

Expected result:
- [x] Shortcut help lists OSCGoesBrrr action.
- [x] Intiface starts first.
- [x] OscGoesBrrr starts after configured delay.
- [x] Incomplete workflow is repaired.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.
Intiface doesn't really work properly when started in elevated mode, so all will start and restart in non-elevated mode.
2026-05-25 retest: OK. Intiface and OscGoesBrrr both launch as non-elevated tasks.

### CONSOLE-012 — OSC router forwarding
Purpose:
Verify UDP routing.

Preconditions:
- `OscRouterEnabled=true`.
- Two local UDP listeners are available.

Steps:
1. Start Supervisor.
2. Send UDP datagram to `127.0.0.1:OscRouterReceivePort`.
3. Observe target listeners.

Expected result:
- [x] Router logs startup details.
- [x] Enabled routes receive unchanged datagrams.
- [x] Disabled routes receive nothing.

Actual result:
- [X] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### CONSOLE-013 — OSC router port conflict
Purpose:
Verify conflict handling and retry.

Preconditions:
- Another tool is bound to the configured receive port.

Steps:
1. Start Supervisor.
2. Free the port.
3. Press `5`.

Expected result:
- [X] Startup continues despite bind failure.
- [X] Console explains retry option.
- [X] Pressing `5` starts/restarts router.

Actual result:
- [X] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

Press F1 for shortcuts.
=== Console Hotkeys ===
1 = Broken Eye + VRCFaceTracking routine
2 = OSCGoesBrrr + Intiface routine
3 = Turn on all controlled base stations
4 = Turn off all controlled base stations
5 = OSC Router launch/restart
6 = Reload Autostart apps
F1 = Show console shortcuts
Launching OSC routing startup...
Warning: OSC router could not bind to 127.0.0.1:9001 because the endpoint is already in use. OSC routing is disabled temporarily. - WORKS OK AFTER PORT CLEAR AND RESTART AS INTENDED

### CONSOLE-014 — Base station power control
Purpose:
Verify automatic and manual base station control.

Preconditions:
- `BaseStationsEnabled=true`.
- Enabled base station rows configured.
- SteamVR and Bluetooth LE available.

Steps:
1. Start SteamVR.
2. Start Supervisor with headset connected.
3. Observe power-on routine.
4. End VRChat and SteamVR.
5. Observe power-down routine.

Expected result:
- [X] Base stations power on after Pimax and `vrserver.exe`.
- [X] OpenVR tracking checks stop retries when stations are active, if available.
- [X] Cleanup sends Sleep or Standby as configured.
- [x] V1 stations fall back to Sleep when Standby is requested.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [X] Hardware-dependent.
- [X] Human QA required.
2026-05-25 retest: OK. Power-on retry no longer tries to start already identified active stations.

=== Console Hotkeys ===
1 = Broken Eye + VRCFaceTracking routine
2 = OSCGoesBrrr + Intiface routine
3 = Turn on all controlled base stations
4 = Turn off all controlled base stations
5 = OSC Router launch/restart
6 = Reload Autostart apps
F1 = Show console shortcuts
No enabled configured base stations to power on.

Works after manually adding base stations in the config app, but works without base station automation turned on in the config app as it was intended. 

SteamVR reports 2/4 exact base station match(es) and 2 active tracking reference(s). Continuing startup.
Repeating base station power-on pass 3/5...
Base station LHB-22CEE79A: power on pass 3 succeeded.
Base station LHB-2BB29CAB: power on pass 3 succeeded.
Base station LHB-60A737E1: power on pass 3 succeeded.
Base station LHB-6E23CDDC: power on pass 3 succeeded.
Waiting 10 seconds before checking SteamVR base-station tracking...
SteamVR reports 3/4 exact base station match(es) and 3 active tracking reference(s). Continuing startup.
Repeating base station power-on pass 4/5...
Base station LHB-22CEE79A: power on pass 4 succeeded.

Suggested Improvement: As it is verifying base stations by their unique identifications, only send additional restart commands to the base stations that are not found in SteamVR and are not actively tracking if their status is unsupported and they don't report their status being turned on. 


### CONSOLE-015 — Monitor handling
Purpose:
Verify secondary monitors are disabled and restored.

Preconditions:
- Multiple monitors connected.
- `TurnOffSecondaryMonitors=true`.

Steps:
1. Start Supervisor with headset connected.
2. Observe monitor layout.
3. Close VRChat and SteamVR.

Expected result:
- [X] Current monitor layout is saved.
- [x] Extra monitors are disabled during session.
- [X] Original layout is restored during cleanup.

Actual result:
- [X] Pass
- [ ] Fail
Notes:
- [X] Hardware-dependent.
- [X] Human QA required.

### CONSOLE-016 — Ctrl+C and console close cleanup
Purpose:
Verify emergency cleanup.

Preconditions:
- Managed apps running.

Steps:
1. Press Ctrl+C in Supervisor console.
2. Repeat with console window close button.

Expected result:
- [X] Shutdown request is logged.
- [X] Monitors and managed apps are cleaned up.
- [X] Detached base-station cleanup helper starts on console close when configured.

Actual result:
- [X] Pass
- [ ] Fail
Notes:
- [X] Human QA required.

### CONSOLE-017 — Command-line flags
Purpose:
Verify special CLI modes.

Preconditions:
- Back up scheduled tasks and config.

Steps:
1. Run `--install-auto-launch-task`.
2. Run `--apply-startup-integration --show-result` for `None`, `ScheduledTask`, and `SteamVrManifest`.
3. Run `--watch-vrchat-auto-launch`.
4. Run `--emergency-base-station-cleanup --delay-seconds 0`.

Expected result:
- [ ] Auto-launch task is created/updated.
- [ ] Startup integration creates/deletes the expected tasks and manifests.
- [ ] Watcher launches Supervisor only when VRChat and SteamVR are both running.
- [ ] Emergency cleanup attempts base station power-down and exits.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### CONSOLE-018 — Missing path and error handling
Purpose:
Verify missing paths fail visibly and safely.

Preconditions:
- Configure nonexistent paths for required and optional apps.

Steps:
1. Start Supervisor.
2. Exercise core startup and OSCGoesBrrr workflow.

Expected result:
- [X] Required core paths trigger browse prompts.
- [X] Optional missing paths produce clear errors.
- [X] Supervisor does not corrupt config or crash silently.

Actual result:
- [X] Pass
- [ ] Fail
Notes:
- [X] Human QA required.
Main Apps, OSCGoesBrrr Reacts as intended. 
Additional auto startup. Create a clean message and continue on. 
2026-05-25 retest: OK. Missing auto-launch app path gives a cleaner message and continues.

Starting configured auto-launch apps...
Skipping Boop Counter: executable was not found at D:\VRCHATexes\Boop Counter\Boop Couer.exe
BLE scanner enabled for OSCGoesBrrr. Scanning for 30 seconds every 60 seconds.

## 5. GUI mode tests — `PimaxVrcSupervisorConfigEditor.exe`

### GUI-001 — Initial load and all tabs
Purpose:
Verify Config Editor opens and loads config.

Preconditions:
- Published folder has `supervisor.config.json`.

Steps:
1. Launch Config Editor.
2. Inspect path bar, status bar, and tabs.

Expected result:
- [ ] Window title includes version `1.2.1`.
- [ ] Config selector/path are populated.
- [ ] Tabs are visible: General, Auto Launch, Base Stations, Detectors, Processes, OSC Router, OSCGoesBrrr, Timing, Raw JSON.
- [ ] Status is Ready or a clear loaded message.

Actual result:
- [X] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### GUI-002 — General tab
Purpose:
Verify paths, startup choices, and first-run choices.

Preconditions:
- Config Editor open.

Steps:
1. Change `DisplayName`.
2. Set Broken Eye and VRCFaceTracking paths.
3. Toggle start minimized options.
4. Toggle mouth tracker and monitor options.
5. Test scheduled task, SteamVR, and no automatic startup choices.

Expected result:
- [X] Path indicators update.
- [X] Startup choices remain mutually consistent.
- [X] Save writes expected fields.
- [X] Status messages remain current across tab changes.

Actual result:
- [X] Pass
- [ ] Fail
Notes:
- [X] Human QA required.
Display name doesn't save. I can enter it, I press save, but it doesn't get saved at all. I have tried to save as and create alternative config and change the display name on it. It felt in the same way. 
2026-05-25 retest: OK.
### GUI-003 — Browse buttons
Purpose:
Verify all executable browse buttons.

Preconditions:
- Config Editor open.

Steps:
1. Browse for Broken Eye.
2. Browse for VRCFaceTracking.
3. Browse for Intiface and OscGoesBrrr.
4. Browse from an Auto Launch row.

Expected result:
- [ ] File picker opens.
- [ ] Selecting an exe fills the field.
- [ ] Auto Launch row name is inferred when blank.
- [ ] Cancel does not change existing values.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### GUI-004 — Save, Save As, Reload, restore backup
Purpose:
Verify file persistence.

Preconditions:
- Writable test config.

Steps:
1. Make a visible change.
2. Save.
3. Make another change.
4. Reload.
5. Save As to a named config.
6. Restore a backup if available.

Expected result:
- [ ] Unsaved marker appears and clears correctly.
- [x] Save writes valid JSON.
- [x] Reload handles unsaved changes clearly.
- [x] Save As updates current config path.
- [x] Backup restore requires confirmation.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.
unsaved marker is completely missing. 
2026-05-25 retest: OK.

### GUI-005 — Validation
Purpose:
Verify errors and warnings.

Preconditions:
- Config Editor open.

Steps:
1. Validate a good config.
2. Add an enabled auto-launch row with blank path.
3. Add duplicate enabled OSC route ports.
4. Add enabled base station row missing identity fields.
5. Validate and attempt Save.

Expected result:
- [x] Good config reports success.
- [x] Warnings are shown without blocking unless they are errors.
- [ ] Errors block Save.
- [X] Grid row error text appears where applicable.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.
Error does not block saving. For example, I was able to save after adding an empty "auto app launch" line and an empty manually added "base station" via gui
2026-05-25 retest: OK. Follow-up: Save behavior was later changed to JSON-only validation by request; full validation remains manual.

### GUI-006 — Auto Launch tab
Purpose:
Verify app table behavior.

Preconditions:
- Config Editor open.

Steps:
1. Add app.
2. Browse for path.
3. Toggle Enabled, Restart after Pimax reconnect, Run as administrator, Start minimized.
4. Delete row.
5. Save/reload.

Expected result:
- [ ] Rows add/delete correctly.
- [ ] Values save to `AutoLaunchApps`.
- [ ] Deleted rows stay deleted after reload.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### GUI-007 — Base Stations tab
Purpose:
Verify scan, edit, and command actions.

Preconditions:
- Bluetooth LE and base stations available for full coverage.

Steps:
1. Toggle automation.
2. Choose Sleep/Standby.
3. Scan.
4. Add Manual row.
5. Edit fields.
6. Test Power On, Sleep, Standby, Identify, Refresh State, Turn On, Turn Off.

Expected result:
- [x] Scan finds stations or reports a clear adapter/station message.
- [x] Manual rows save/reload.
- [ ] Unsupported V1/V2 actions are blocked or fail clearly.
- [ ] Status messages show progress and failures.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Hardware-dependent.
- [x] Human QA required.
I am able to add version v1 id and save it on version 2 base stations, It shouldn't happen. There are no errors shown in this situation. Saving, starting manually updating state, Removing stations from GUI  works fine. 
2026-05-25 retest: OK.


### GUI-008 — Detectors tab
Purpose:
Verify detector rule editing and test actions.

Preconditions:
- Config Editor open.

Steps:
1. Edit Pimax, mouth tracker, and Lovense detector rules.
2. Click each detector test button.
3. Copy detector details if matches are shown.

Expected result:
- [x] Rules parse as string matrix.
- [x] Empty rules produce warning/information.
- [x] Matched device blocks are shown.
- [x] Copy button copies details.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.

### GUI-009 — Processes tab
Purpose:
Verify process settings.

Preconditions:
- Config Editor open.

Steps:
1. Change all process name fields.
2. Save/reload.

Expected result:
- [x] Values save as string arrays.
- [x] Empty process lists warn during validation.
- [x] Supervisor uses updated names on next run.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.

### GUI-010 — OSC Router tab
Purpose:
Verify OSC router config.

Preconditions:
- Config Editor open.

Steps:
1. Enable OSC routing.
2. Change receive port.
3. Add routes.
4. Disable one route.
5. Try duplicate enabled ports.
6. Save/reload.

Expected result:
- [x] Receive port saves.
- [x] Route name, port, and enabled state save.
- [x] Duplicate enabled ports are validation errors.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.
They detect duplicate ports, but the save button still allows saving even with duplicate ports. 
2026-05-25 retest: OK. Follow-up: Save behavior was later changed to JSON-only validation by request; full validation remains manual.

### GUI-011 — OSCGoesBrrr tab
Purpose:
Verify Intiface/OscGoesBrrr settings.

Preconditions:
- Config Editor open.

Steps:
1. Enable workflow.
2. Toggle manual console launch mode.
3. Toggle BLE scanner.
4. Set paths and minimized options.
5. Edit process names and Lovense detectors.
6. Save/reload.

Expected result:
- [x] Fields save to matching config keys.
- [x] Missing workflow paths warn when workflow is enabled.
- [x] Lovense detector warnings are relevant.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.
You can still save with the wrong selected executable. Even after selecting the right executable after all proper ones were deleted, it will stay as not found, at least visually in gui until you will validate it
2026-05-25 retest: OK.

### GUI-012 — Timing tab
Purpose:
Verify numeric timing fields.

Preconditions:
- Config Editor open.

Steps:
1. Change every timing field to allowed values.
2. Try very low/high values.
3. Save/reload.

Expected result:
- [x] Numeric controls enforce min/max.
- [x] Very low/high values warn.
- [x] Saved values reload exactly.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### GUI-013 — Raw JSON tab
Purpose:
Verify valid and invalid Raw JSON workflows.

Preconditions:
- Config Editor open.

Steps:
1. Edit valid JSON and click Format JSON.
2. Click Apply JSON to editor.
3. Confirm other tabs update.
4. Introduce malformed JSON.
5. Try Apply, Format, and Save.

Expected result:
- [ ] Valid JSON status is shown.
- [ ] Format pretty-prints.
- [ ] Apply updates other tabs.
- [ ] Invalid JSON shows line/position.
- [ ] Invalid JSON is not saved.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.

### GUI-014 — Launch Supervisor from GUI
Purpose:
Verify Launch Supervisor button.

Preconditions:
- Config Editor open.

Steps:
1. Make unsaved changes.
2. Click Launch Supervisor.
3. Test Cancel, Launch Without Saving, and Save and Launch.

Expected result:
- [no] Unsaved changes prompt appears.
- [x] Cancel does not launch.
- [x] Launch Without Saving starts using current on-disk config.
- [x] Save and Launch saves first.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.
Range detector is working only for the first time after application launch. Examples. Tester launches application, turns off one of the enabled stations, Asterisk marking unsaved config appears. Sometimes it does not appear, so questions do not appear at all. When Mark appears, the test passes properly. 
2026-05-25 retest: OK.


2026-05-25 fix status: Launch Supervisor now passes the currently loaded config with `--config`, and Config Editor writes the active selection for direct no-argument supervisor launches from the same folder.

## 6. Overlay / SteamVR mode tests — `PimaxVrcSupervisorSteamVrHost.exe`

### OVERLAY-001 — SteamVR manifest registration
Purpose:
Verify SteamVR startup manifest install.

Preconditions:
- SteamVR installed.
- `StartupLaunchMode=SteamVrManifest`.

Steps:
1. Run Supervisor or `--apply-startup-integration`.
2. Inspect release folder and SteamVR startup apps.

Expected result:
- [x] `PimaxVrcSupervisor.vrmanifest` is created.
- [x] App key is `pimax.vrcsupervisor.dashboard`.
- [x] Manifest points to `PimaxVrcSupervisorSteamVrHost.exe`.
- [x] Auto-launch is enabled.
- [x] Helper task `Pimax VRC Supervisor SteamVR Start` exists.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### OVERLAY-002 — Host failure without SteamVR
Purpose:
Verify clear failure when SteamVR is unavailable.

Preconditions:
- SteamVR not running.

Steps:
1. Launch `PimaxVrcSupervisorSteamVrHost.exe`.

Expected result:
- [x] Host exits or shows clear OpenVR/SteamVR error.
- [x] No unhandled crash dialog.
- [x] Log is written to `%TEMP%\PimaxVrcSupervisorSteamVrHost.log`.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.

### OVERLAY-003 — Dashboard visibility and rendering
Purpose:
Verify overlay appears and is readable.

Preconditions:
- SteamVR running.
- Manifest/helper installed.

Steps:
1. Start SteamVR.
2. Open SteamVR dashboard.
3. Locate Pimax VRC Supervisor overlay.

Expected result:
- [x] Overlay appears as `Pimax VRC Supervisor`.
- [x] Thumbnail/icon is visible.
- [x] Overlay surface is readable and not blank.
- [x] Console log panel updates.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Hardware-dependent.
- [x] Human QA required.

### OVERLAY-004 — `--steamvr-start` flow
Purpose:
Verify host starts elevated hidden Supervisor.

Preconditions:
- Helper task installed.
- SteamVR running.

Steps:
1. Start SteamVR host through manifest or directly.
2. Inspect processes.

Expected result:
- [x] Host requests helper scheduled task.
- [x] `PimaxVrcSupervisor.exe` starts with `--steamvr-start`.
- [x] Supervisor console is hidden.
- [x] Command bridge becomes available on TCP `37957` or named pipe.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### OVERLAY-005 — Overlay controls
Purpose:
Verify all dashboard buttons dispatch actions.

Preconditions:
- Overlay visible.
- Supervisor running.

Steps:
1. Click `Restart VRC face tracking`.
2. Click `Restart OSC router`.
3. Click `OSCGoesBrr`.
4. Click `Base stations on`.
5. Click `Base stations off`.
6. Click `Restart Supervisor`.

Expected result:
- [x] Core apps restart.
- [x] OSC router restarts or reports clear failure.
- [x] OSCGoesBrrr workflow starts or reports clear failure.
- [x] Base station actions run or report disabled/config errors.
- [x] Supervisor restarts and overlay reconnects.

Actual result:
- [x] Pass
- [] Fail
Notes:
- [x] Hardware-dependent for base station portions.
- [x] Human QA required.



### OVERLAY-006 — Helper task missing or wrong
Purpose:
Verify helper task failure handling.

Preconditions:
- Delete, disable, or mispoint `Pimax VRC Supervisor SteamVR Start`.

Steps:
1. Launch SteamVR host.
2. Observe overlay status and temp log.

Expected result:
- [ ] Host reports it could not start elevated Supervisor.
- [ ] Path validation error includes current and task executable folders when applicable.
- [x] Overlay remains usable enough to show the failure if OpenVR is available.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.
Report that the bridge is not available, Buttons are pressable as they should. Supervisor is not running as expected. 
2026-05-25 retest: OK.

### OVERLAY-007 — Stop with SteamVR
Purpose:
Verify SteamVR shutdown behavior.

Preconditions:
- Supervisor started with `--steamvr-start`.
- Managed apps running.

Steps:
1. Close SteamVR or stop `vrserver.exe`.

Expected result:
- [ ] Supervisor detects SteamVR shutdown.
- [ ] Supervisor restores monitors and closes managed apps.
- [ ] Base stations power down if configured.
- [ ] Supervisor and host exit.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

## 7. Edge and error tests

### EDGE-001 — Invalid JSON at startup
Purpose:
Verify malformed config behavior.

Preconditions:
- Back up config.

Steps:
1. Make `supervisor.config.json` malformed.
2. Launch Supervisor.

Expected result:
- [x] JSON parse issue is reported.
- [x] Cleanup attempt is made if needed.
- [x] No unrelated files are modified.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.
When the manual is started, it stays in process. Blips for a second with error.
It is possible to start multiple instances of not working steamVR host manually by clicking them in explorer.  But they at least start Steam VR by itself. When invoked by SteamVR, it shows an error for a brief second and goes into hidden mode. Overlay in SteamVR does not show up, so I would say it is a mixed result. 
2026-05-25 retest: OK.



### EDGE-002 — Missing SteamVR host executable
Purpose:
Verify manifest install failure.

Preconditions:
- Test from a folder missing `PimaxVrcSupervisorSteamVrHost.exe`.

Steps:
1. Set `StartupLaunchMode=SteamVrManifest`.
2. Run `PimaxVrcSupervisor.exe --apply-startup-integration --show-result`.

Expected result:
- [x] Error says SteamVR host executable was not found.
- [ ] Invalid manifest is not left registered.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.

### EDGE-003 — Bluetooth LE unavailable
Purpose:
Verify base station scan/control failure.

Preconditions:
- Disable Bluetooth or use a machine without BLE.

Steps:
1. Enable base station automation.
2. Scan from Config Editor.
3. Run Supervisor base station routine.

Expected result:
- [x] GUI reports `Bluetooth LE adapter not found.`
- [x] Supervisor logs scan/control failure and continues where possible.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.

### EDGE-004 — Device probe timeout/failure
Purpose:
Verify device query failures are handled.

Preconditions:
- Set `DeviceProbeTimeoutSeconds` very low.

Steps:
1. Start Supervisor.
2. Observe device detection output.

Expected result:
- [ ] Console reports device state read failure.
- [ ] Previous state is retained.
- [ ] Supervisor continues polling.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### EDGE-005 — Scheduled task points to another release
Purpose:
Verify stale scheduled task warning.

Preconditions:
- Create managed scheduled task pointing to another folder.

Steps:
1. Open Config Editor from current release folder.
2. Run validation or startup integration.

Expected result:
- [ ] Config Editor warns task points to another release.
- [ ] Message includes task name and both paths.
- [x] Recreating integration updates task path.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [x] Human QA required.
No warnings. Selection was as it was in the interface with the task pointing to the wrong folder, but unselecting and selecting the option again recreates the task properly. 
2026-05-25 retest: OK.
2026-05-25 follow-up: When switching startup modes and saving, Config Editor can appear hung for a long time while startup integration is applied. Local task query timings were fast, so the likely delay is the synchronous startup-integration child process, especially Task Scheduler create/delete or OpenVR manifest registration/disable calls.
2026-05-25 fix status: Config Editor now starts elevated startup-integration apply asynchronously, drains output while waiting, keeps the UI responsive with status, and keeps the existing 30-second timeout. OpenVR registry calls used by SteamVR manifest enable/disable are also bounded to 10 seconds so unavailable OpenVR IPC fails fast and cleanup can continue.
2026-05-25 follow-up fix status: The elevated startup-integration helper now logs its config path, selected mode, and each task/manifest step so the temporary console window is not blank while work is running.
2026-05-25 follow-up fix status: Startup integration now bounds `schtasks.exe` create/delete/query work to 5 seconds and verifies final task state after create/delete timeouts. OpenVR manifest registry timeout reduced from 10 seconds to 5 seconds.
2026-05-25 follow-up fix status: Startup integration task/OpenVR helper timeouts reduced to 3 seconds. Default config comments were updated to document `RunInitialSetupQuestions` and first-setup empty-value behavior for manual editing.

## 8. End-to-end workflow tests

### E2E-001 — Console VRChat session
Purpose:
Verify main console workflow.

Preconditions:
- Pimax headset, SteamVR, VRChat, Broken Eye, and VRCFaceTracking configured.

Steps:
1. Start SteamVR.
2. Start VRChat.
3. Start Supervisor.
4. Confirm managed apps launch.
5. Perform one Pimax reconnect if possible.
6. Close VRChat.
7. Close SteamVR.

Expected result:
- [ ] Startup sequence completes.
- [ ] Reconnect handling works.
- [ ] Managed apps close.
- [ ] Monitors/base stations restore or power down according to config.
- [ ] Supervisor exits cleanly.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Hardware-dependent.
- [ ] Human QA required.

### E2E-002 — Config Editor to Supervisor workflow
Purpose:
Verify GUI-created config drives Supervisor behavior.

Preconditions:
- Config Editor available.

Steps:
1. Create or edit a config through GUI.
2. Save.
3. Launch Supervisor from GUI.
4. Verify console behavior matches GUI settings.

Expected result:
- [ ] Saved config is used by launched Supervisor.
- [ ] Startup mode, paths, processes, routes, apps, detectors, and timings match GUI settings.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Human QA required.

### E2E-003 — SteamVR overlay workflow
Purpose:
Verify SteamVR-started full workflow.

Preconditions:
- SteamVR installed/running.
- Manifest/helper installed.

Steps:
1. Start SteamVR.
2. Confirm host starts automatically.
3. Open dashboard overlay.
4. Use overlay buttons.
5. Stop SteamVR.

Expected result:
- [ ] Overlay is visible and interactive.
- [ ] Elevated Supervisor starts hidden with `--steamvr-start`.
- [ ] Overlay commands affect Supervisor.
- [ ] Supervisor exits when SteamVR stops.

Actual result:
- [x] Pass
- [ ] Fail
Notes:
- [ ] Hardware-dependent.
- [ ] Human QA required.
