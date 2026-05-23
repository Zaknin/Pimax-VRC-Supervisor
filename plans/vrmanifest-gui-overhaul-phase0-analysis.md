# Phase 0 Analysis & Implementation Plan — vrmanifest-gui-overhaul

## 1. Relevant Files/Components Found

| # | Component | File | Lines |
|---|-----------|------|-------|
| 1 | **Console Supervisor App** | `PimaxVrcSupervisor/Program.cs` | 5,979 lines (single file, top-level statements) |
| 2 | **Config Editor App** | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 5,905 lines (single file, top-level statements) |
| 3 | **Base Station Support** | `PimaxVrcSupervisor/BaseStationSupport.cs` | 684 lines (shared via linked compile include in .csproj) |
| 4 | **SteamVR Dashboard Host** | `PimaxVrcSupervisor.SteamVrHost/Program.cs` | 1,438 lines |

Key types in `PimaxVrcSupervisor/Program.cs`:
- `AppSupervisor` (line 350) — main supervisor loop
- `ConsoleHotkeys` struct (line 343) — current hotkey state
- `ConsumeConsoleHotkeys()` (line 2590) — reads console keys
- `HandleConsoleHotkeysAsync()` (line 2524) — dispatches hotkey actions
- `SupervisorCommandServer` (line 3316) — named pipe + TCP command server
- `SupervisorConfig` (line 5557) — config model with `OscGoesBrrrHotkeyEnabled` property
- `OscRouter` (line 5237) — in-process OSC router
- `AutoLaunchWatcher` (line 4076) — scheduled task watcher

Key types in `PimaxVrcSupervisor.ConfigEditor/Program.cs`:
- `ConfigEditorForm` (line 28) — main form
- `ThemedActionButton` (line 5323) — custom dark-themed button
- `ThemedTabHost` (line 5485) — custom tab control
- `ThemedTabButton` (line 5433) — custom tab button
- `AppTheme` (line 5274) — theme definition (Light/Dark)
- `ApplyThemeTo()` (line 5079) — recursive theme application
- `ApplyWindowsTheme()` (line 5068) — detects and applies Windows dark/light theme

---

## 2. Current Implementation of Each Relevant Routine

### 2.1 Console Hotkeys (Supervisor)

**Location:** `PimaxVrcSupervisor/Program.cs`
- `ConsumeConsoleHotkeys()` (line 2590): Polls `Console.KeyAvailable` / `Console.ReadKey()`. Currently recognizes:
  - `L` → `hotkeys.LaunchOscGoesBrrr = true`
  - `Spacebar` → `hotkeys.RetryOscRouter = true`
  - `R` → `hotkeys.RestartCoreApps = true`
- `HandleConsoleHotkeysAsync()` (line 2524): Dispatches to `RestartCoreAppsAsync`, `RetryOscRouterAsync`, `HandleOscGoesBrrrHotkeyAsync`
- Called every poll cycle at line 484: `await HandleConsoleHotkeysAsync(cancellationToken);`
- **Critical limitation**: Only works when `Console.IsInputRedirected` is false AND `Console.KeyAvailable` is true. This means it only works when the console window is focused and has keyboard input — it does NOT work when the supervisor is launched as a hidden scheduled task (input is redirected).

### 2.2 Broken Eye + VRCFaceTracking Routine

**Location:** `PimaxVrcSupervisor/Program.cs`
- `StartCoreAppsAsync()` (line 1484): Calls `StartBrokenEyeWithRetriesAsync()` then `VerifyRunningAsync("Broken Eye", ...)`, waits `DelayBeforeVrcFaceTrackingSeconds`, then starts VRCFaceTracking.
- `RestartCoreAppsAsync()` (line 1584): Stops VRCFaceTracking + Broken Eye, then calls `StartCoreAppsAsync()`. Uses `_coreAppRestartLock` semaphore.
- Triggered by: startup sequence (line 480), Pimax reconnect (line 601-602), console hotkey R (line 2527-2529).

### 2.3 OSCGoesBrrr + Intiface Routine

**Location:** `PimaxVrcSupervisor/Program.cs`
- `InitializeOscGoesBrrrWorkflowAsync()` (line 2333): If `OscGoesBrrrHotkeyEnabled`, prints "Press L to launch OSCGoesBrrr." and returns.
- `HandleOscGoesBrrrHotkeyAsync()` (line 2543): Checks `OscGoesBrrrEnabled && OscGoesBrrrHotkeyEnabled`, calls `StartLovenseOscAsync()`.
- `StartLovenseOscAsync()` (line 2649): Starts Intiface → waits `DelayBeforeOscGoesBrrrSeconds` → starts OscGoesBrrr. Uses `_oscGoesBrrrLaunchLock`.
- `StartLovenseIntifaceAsync()` (line 2625): Starts Intiface process.
- Also has BLE scanner path: `StartOscGoesBrrrBleScanner()` (line 2385), `RunOscGoesBrrrBleScannerAsync()` (line 2398).
- Auto-detection path (Lovense device appears): lines 635-644 in the main poll loop.

### 2.4 Base Station Power On/Off Routines

**Location:** `PimaxVrcSupervisor/Program.cs`
- `TryPowerOnBaseStationsForSessionAsync()` (line 1696): Multi-pass power-on with SteamVR tracking confirmation or BLE fallback. Called at startup (lines 450, 472) and during poll loop (line 487).
- `ManualPowerOnBaseStationsAsync()` (line 1656): Resets state and calls `TryPowerOnBaseStationsForSessionAsync`. Used by command server.
- `TryPowerDownBaseStationsForSessionAsync()` (line 1898): Sends power-down command via `BaseStationPowerDownRoutine.RunAsync()`.
- `ManualPowerDownBaseStationsAsync()` (line 1675): Sets state and calls power-down.
- Command server exposes `base-stations-on` (line 1623) and `base-stations-off` (line 1626).

### 2.5 OSC Router Launch/Restart Logic

**Location:** `PimaxVrcSupervisor/Program.cs`
- `TryStartOscRouterAsync()` (line 2251): Creates `OscRouter` instance if enabled and not running. Handles `AddressAlreadyInUse`.
- `RetryOscRouterAsync()` (line 2300): Retries if not already running.
- `RestartOscRouterAsync()` (line 1689): Stops + restarts. Used by command server (`restart-osc-router` at line 1629).
- `ShowOscRouterRetryPromptIfNeeded()` (line 2319): Prints "Press Space to retry to restart OSC routing."
- `StopOscRouter()` (line 3277): Disposes and nulls the router.

### 2.6 After-Launch Applications Logic

**Location:** `PimaxVrcSupervisor/Program.cs`
- `StartManagedAppsAsync()` (line 1476): Calls `StartCoreAppsAsync()` then `StartAutoLaunchAppsAsync()`.
- `StartAutoLaunchAppsAsync()` (line 2716): Iterates `GetEnabledAutoLaunchApps()`, starts each one.
- `GetEnabledAutoLaunchApps()` (line 2756): Filters config `AutoLaunchApps` by enabled + non-empty path.
- `CreateManagedAutoLaunchApp()` (line 2766): Maps config to runtime record with process name inference.
- On Pimax reconnect (line 601-602): `StopManagedAppsAsync` then `StartManagedAppsAsync`.
- **No existing "restart only not-running apps" logic** — this needs to be created.

### 2.7 Config Editor Keyboard Shortcuts

**Location:** `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `ProcessCmdKey()` (line 3019): Handles:
  - `Ctrl+S` → Save
  - `Ctrl+Shift+S` → Save As
  - `Ctrl+O` → Browse
  - `F5` or `Ctrl+R` → Reload
  - `Ctrl+L` → Launch Supervisor
  - `Ctrl+Shift+V` → Validate
- **No F1 help shortcut exists yet.**

### 2.8 Tab UI Code Locations

**Location:** `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- **Basics tab**: `BuildBasicsTab()` (line 386)
- **Processes tab**: `BuildProcessesTab()` (line 1335)
- **Base Stations tab**: `BuildBaseStationsTab()` (line 483)
- **Detectors tab**: `BuildDetectorsTab()` (line 1352)
- **Raw JSON tab**: `BuildRawJsonTab()` (line 1682)
- **Auto Launch tab**: `BuildAutoLaunchTab()` (line 1147)

### 2.9 Theming/Styling Code

**Location:** `PimaxVrcSupervisor.ConfigEditor/Program.cs`
- `AppTheme` record (line 5274): Defines Light and Dark themes with colors for WindowBack, InputBack, ButtonBack, ButtonHover, ButtonPressed, TabBack, TabSelectedBack, ToolTipBack, ToolTipFore, Text, Border, StrongBorder, PrimaryButtonBack.
- `ApplyWindowsTheme()` (line 5068): Detects Windows theme, calls `ApplyThemeTo(this)`.
- `ApplyThemeTo()` (line 5079): Recursive control theming. Handles Form, ThemedTabHost, TextBox, NumericUpDown, ComboBox, DataGridView, Button, CheckBox, Label, Panel, TableLayoutPanel.
- `ThemedActionButton` (line 5323): Custom button with rounded corners, hover/pressed states, dark theme support.
- `ThemedTabHost` (line 5485): Custom tab control with `ThemedTabButton`.
- `WindowsTitleBar` (line 5695): DWM dark mode title bar.
- `WindowsThemeDetector` (line 5684): Registry-based theme detection.

---

## 3. Proposed Implementation in Manageable Phases

### Phase 1: Console Hotkey Infrastructure Expansion
**Scope:** Add number key hotkeys 1-6 to the supervisor console, add F1 help, add "Press F1 for shortcuts." message.

**Changes to `PimaxVrcSupervisor/Program.cs`:**
1. Expand `ConsumeConsoleHotkeys()` to recognize keys `1`-`6` and `F1`.
2. Add new fields to `ConsoleHotkeys` struct: `LaunchBrokenEye`, `LaunchOscGoesBrrr`, `BaseStationsOn`, `BaseStationsOff`, `OscRouterAction`, `AfterLaunchAppsAction`.
3. Expand `HandleConsoleHotkeysAsync()` to dispatch new hotkeys.
4. Add `PrintShortcutHelp()` method that prints the same shortcut list shown in Config Editor.
5. Add `RunAfterLaunchAppsRoutineAsync()` method implementing the smart start/restart logic.
6. Print "Press F1 for shortcuts." after startup completes (after line 473).

### Phase 2: Config Editor Hotkeys Section in Basics Tab
**Scope:** Add non-clickable Hotkeys section to the right side of the Basics tab.

**Changes to `PimaxVrcSupervisor.ConfigEditor/Program.cs`:**
1. Modify `BuildBasicsTab()` to use a split layout: left side = existing controls, right side = Hotkeys section.
2. Add a read-only label/list showing console hotkeys (1-6, F1).
3. Add a read-only label/list showing Config Editor shortcuts (Ctrl+S, Ctrl+Shift+S, etc.).
4. Add F1 keyboard shortcut handler in `ProcessCmdKey()` to show the shortcut help.

### Phase 3: Processes Tab Reorganization
**Scope:** Move two checkboxes to bottom of Processes tab, evaluate StopWithSteamVR removal.

**Changes to `PimaxVrcSupervisor.ConfigEditor/Program.cs`:**
1. In `BuildProcessesTab()`, move `_usePimaxLogCheckBox` and `_useMouthTrackerPnPCheckBox` to the bottom.
2. Evaluate `StopWithSteamVR` redundancy (see risks below).

### Phase 4: Dark Theme Consistency Fixes
**Scope:** Fix white Browse buttons, dropdown theming, scrollbar theming.

**Changes to `PimaxVrcSupervisor.ConfigEditor/Program.cs`:**
1. Fix `ThemedActionButton` / `CreateButton()` to ensure Browse buttons get dark theme.
2. Ensure `ComboBox` dropdown lists are dark-themed (may require owner-draw or `FlatStyle` changes).
3. Address scrollbar theming limitations (see risks below).

### Phase 5: Config Cleanup
**Scope:** Remove `LHotkeyLaunch` toggle if present, update config.

**Changes to `PimaxVrcSupervisor/Program.cs` and `PimaxVrcSupervisor/supervisor.config.json`:**
1. Evaluate whether `OscGoesBrrrHotkeyEnabled` should be removed or repurposed.

---

## 4. Risks and Ambiguous Points

### 4.1 Removing StopWithSteamVR
**Risk: MEDIUM.** The `StopWithSteamVr` checkbox (`_stopWithSteamVrCheckBox`, line 80) is used in the supervisor config at `SupervisorConfig.StopWithSteamVr` (line 5624). In `Program.cs`, the `_steamVrStart` flag (set by `--steamvr-start` command line arg) controls whether the supervisor watches for SteamVR shutdown (line 490). `StopWithSteamVr` is a config property that is forced true when `StartWithSteamVR` is checked (line 469), but the actual shutdown behavior is driven by `_steamVrStart` (the command-line flag), not the config property. The config property `StopWithSteamVr` is saved/loaded but **is not directly checked in the supervisor's main loop** — the supervisor checks `_steamVrStart` which comes from the command line. However, the SteamVR dashboard host launches the supervisor via the scheduled task, not with `--steamvr-start`. **Recommendation: Do NOT remove StopWithSteamVr yet.** It is used in config save/load round-tripping and may be referenced by future SteamVR manifest startup flows. Instead, document that it is currently only forced-on when StartWithSteamVR is enabled, and the actual behavior depends on the launch mode.

### 4.2 Removing OscGoesBrrrHotkeyEnabled (LHotkeyLaunch)
**Risk: LOW.** The `OscGoesBrrrHotkeyEnabled` property (config: `"OscGoesBrrrHotkeyEnabled"`, default `true`) controls whether the L key prints "Press L to launch OSCGoesBrrr." and triggers the workflow. The task says to "replace the old L-based OSCGoesBrrr + Intiface routine trigger" with hotkey 2. The L key would no longer be needed, but the feature toggle itself (`OscGoesBrrrHotkeyEnabled`) should likely be **kept** because:
- It is still a valid config option (users may want to disable the hotkey entirely).
- The BLE scanner path and auto-detect path still work independently.
- Removing it would break config backward compatibility.
**Recommendation: Keep the config property but ignore the L key in `ConsumeConsoleHotkeys()`. Hotkey 2 replaces the L key behavior.**

### 4.3 Where to Source the Config Editor Shortcut List
**Risk: LOW.** The shortcut list should be defined as a shared constant/string resource to ensure the console output and Config Editor display stay in sync. **Recommendation: Define a static `ShortcutHelp` class with a formatted string constant in the Config Editor project, and duplicate the same string in the supervisor's `PrintShortcutHelp()` method.** Since these are in different projects and the supervisor doesn't reference the Config Editor, true sharing isn't practical. Accept the duplication and add a comment in both locations to keep them in sync.

### 4.4 Dark-Theming WinForms Dropdowns and Scrollbars
**Risk: HIGH.** WinForms has fundamental limitations:
- **ComboBox dropdown lists**: The dropdown portion of a `ComboBox` is a native Windows control that does not respect WinForms `BackColor`/`ForeColor` when `DropDownStyle = DropDownList`. To truly dark-theme it, you need to set `FlatStyle = FlatStyle.Flat` and handle custom drawing, or use `OwnerDrawFixed` mode with custom `DrawItem` handling. The current code sets `comboBox.BackColor` and `comboBox.ForeColor` in `ApplyThemeTo()` but this only affects the text box portion, not the dropdown list.
- **Scrollbars**: WinForms `DataGridView` scrollbars and `TextBox` scrollbars are native Windows scrollbars. They cannot be re-colored through standard WinForms properties. Options include:
  1. Using a custom scrollbar library (e.g., replacing with custom-drawn scrollbars).
  2. Using P/Invoke to set scrollbar colors (undocumented, fragile).
  3. Accepting native scrollbars in dark mode (least effort, common in many dark-themed WinForms apps).
  4. For `DataGridView`, setting `ScrollBars = ScrollBars.Both` and relying on the grid's own rendering (already themed).
- **Browse buttons on Auto Launch tab**: These are `DataGridViewButtonColumn` buttons inside the `_autoLaunchAppsGrid`. They are rendered by the DataGridView's default button column, not by `ThemedActionButton`. The `ApplyThemeToGrid()` method styles the grid but `DataGridViewButtonColumn` buttons use the system default appearance. **Fix: Handle `CellPainting` event for button columns to draw themed buttons.**

### 4.5 Console Hotkey Availability in Scheduled Task Mode
**Risk: MEDIUM.** When the supervisor is launched by the auto-launch scheduled task, `Console.IsInputRedirected` is likely true (the process is hidden/launched via PowerShell). The current `ConsumeConsoleHotkeys()` returns `default` when input is redirected. The task says hotkeys should work "whether the supervisor is launched manually, launched by clicking Launch Supervisor in the Config Editor, or launched by the auto-launch scheduled task." **This is a fundamental conflict**: if the console is not visible/focused, there is no console input to read. **Recommendation: Clarify that hotkeys only work when the console window is visible and focused (manual launch or Config Editor launch with visible console). For the scheduled task launch, the command server (named pipe + TCP) remains the control path.** The task description says "When the supervisor is running in visible console mode" — this qualifier should be emphasized.

---

## 5. Recommended Exact Next Implementation Prompt for Phase 1

```
Phase 1: Console Hotkey Infrastructure Expansion

Work on branch vrmanifest-gui-overhaul. Do not modify vrmanifest-gui.

Modify only PimaxVrcSupervisor/Program.cs:

1. Expand the ConsoleHotkeys struct (line 343) to add boolean fields:
   - LaunchBrokenEyeVrcFaceTracking (key 1)
   - LaunchOscGoesBrrr (key 2)
   - BaseStationsOn (key 3)
   - BaseStationsOff (key 4)
   - OscRouterAction (key 5)
   - AfterLaunchAppsAction (key 6)

2. Modify ConsumeConsoleHotkeys() (line 2590) to recognize:
   - Digit '1' through '6' on the top QWERTY row (ConsoleKey.D1 through D6, ignoring modifiers)
   - F1 key (print shortcut help)
   - Keep existing L, Spacebar, R handling for backward compatibility

3. Modify HandleConsoleHotkeysAsync() (line 2524) to dispatch:
   - Key 1: await RestartCoreAppsAsync(cancellationToken)
   - Key 2: await StartLovenseOscAsync(cancellationToken) (same as current L key behavior)
   - Key 3: await ManualPowerOnBaseStationsAsync(cancellationToken)
   - Key 4: await ManualPowerDownBaseStationsAsync(cancellationToken)
   - Key 5: if OSC router enabled and not running → launch it; if running → restart it
     (combine TryStartOscRouterAsync + StopOscRouter logic)
   - Key 6: new method RunAfterLaunchAppsRoutineAsync():
     a. Get list of configured after-launch apps (GetEnabledAutoLaunchApps())
     b. For each app, check if its processes are running (IsAnyProcessRunning)
     c. If all running → stop all (StopProcessesAsync), then start all (StartOrAttach + VerifyRunningAsync)
     d. If some running, some not → only start the ones not running
     e. If none running → start them all
   - F1: call PrintShortcutHelp()

4. Add PrintShortcutHelp() method that prints:
   === Console Hotkeys ===
   1 = Restart Broken Eye + VRCFaceTracking
   2 = Launch OSCGoesBrrr + Intiface
   3 = Turn on all base stations
   4 = Turn off all base stations
   5 = Launch/restart OSC Router
   6 = Start/restart after-launch apps
   F1 = Show this help
   (Keep existing L, Space, R for backward compatibility)

5. After startup completes (after line 473, after ShowOscRouterRetryPromptIfNeeded()),
   add: Console.WriteLine("Press F1 for shortcuts.");

6. Do NOT remove or modify OscGoesBrrrHotkeyEnabled config property.
   Do NOT remove L, Spacebar, or R key handling.
```

---

## Summary of Key Source Locations

| Item | File | Key Lines |
|------|------|-----------|
| ConsoleHotkeys struct | `PimaxVrcSupervisor/Program.cs` | 343-348 |
| ConsumeConsoleHotkeys() | `PimaxVrcSupervisor/Program.cs` | 2590-2623 |
| HandleConsoleHotkeysAsync() | `PimaxVrcSupervisor/Program.cs` | 2524-2541 |
| Main poll loop | `PimaxVrcSupervisor/Program.cs` | 480-656 |
| Startup sequence | `PimaxVrcSupervisor/Program.cs` | 440-478 |
| StartCoreAppsAsync / RestartCoreAppsAsync | `PimaxVrcSupervisor/Program.cs` | 1484-1604 |
| StartLovenseOscAsync / StartLovenseIntifaceAsync | `PimaxVrcSupervisor/Program.cs` | 2625-2691 |
| HandleOscGoesBrrrHotkeyAsync | `PimaxVrcSupervisor/Program.cs` | 2543-2563 |
| TryPowerOnBaseStationsForSessionAsync | `PimaxVrcSupervisor/Program.cs` | 1696-1841 |
| ManualPowerOnBaseStationsAsync | `PimaxVrcSupervisor/Program.cs` | 1656-1673 |
| ManualPowerDownBaseStationsAsync | `PimaxVrcSupervisor/Program.cs` | 1675-1687 |
| TryStartOscRouterAsync | `PimaxVrcSupervisor/Program.cs` | 2251-2298 |
| RestartOscRouterAsync | `PimaxVrcSupervisor/Program.cs` | 1689-1694 |
| StartAutoLaunchAppsAsync | `PimaxVrcSupervisor/Program.cs` | 2716-2754 |
| GetEnabledAutoLaunchApps | `PimaxVrcSupervisor/Program.cs` | 2756-2764 |
| ExecuteSupervisorCommandAsync | `PimaxVrcSupervisor/Program.cs` | 1606-1640 |
| OscGoesBrrrHotkeyEnabled config | `PimaxVrcSupervisor/Program.cs` | 5613 |
| StopWithSteamVr config | `PimaxVrcSupervisor/Program.cs` | 5624 |
| BuildBasicsTab() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 386-433 |
| BuildProcessesTab() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 1335-1350 |
| BuildBaseStationsTab() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 483-565 |
| BuildDetectorsTab() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 1352-1390 |
| BuildRawJsonTab() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 1682-1731 |
| BuildAutoLaunchTab() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 1147-1198 |
| ProcessCmdKey() shortcuts | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 3019-3045 |
| ApplyThemeTo() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 5079-5149 |
| ApplyThemeToGrid() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 5151-5169 |
| ThemedActionButton | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 5323-5431 |
| AppTheme (Light/Dark) | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 5274-5321 |
| CreateButton() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 339-363 |
| AutoLaunchAppsGrid Browse column | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 1221-1230 |
| _stopWithSteamVrCheckBox | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 80 |
| _oscGoesBrrrHotkeyCheckBox | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 63 |
| _usePimaxLogCheckBox | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 81 |
| _useMouthTrackerPnPCheckBox | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 82 |
| RefreshStartupOptionStates() | `PimaxVrcSupervisor.ConfigEditor/Program.cs` | 465-481 |
