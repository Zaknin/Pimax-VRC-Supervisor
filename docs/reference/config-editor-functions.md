# Config Editor Functions Reference

Major behavior-defining functions and classes in `PimaxVrcSupervisorConfigEditor.exe`.

## ConfigEditorForm

The main editor form class.

### Key Methods

| Method | Description |
| --- | --- |
| `LoadConfig(string)` | Loads a config file and populates all tabs. |
| `SaveConfig()` | Validates and saves the current editor values to the config file. |
| `SaveConfigAs()` | Saves to a new file path. |
| `BuildCurrentJson()` | Serializes the current editor state to JSON. |
| `PopulateControls(JsonNode)` | Populates all UI controls from a JSON node. |
| `ApplyControlValues(string)` | Applies current control values to a JSON string. |
| `ValidateCurrentConfig(ValidationMode)` | Validates the current configuration. |

### Grid Management

| Method | Description |
| --- | --- |
| `ConfigureBaseStationsGrid()` | Sets up the base stations DataGridView columns. |
| `ConfigureAutoLaunchAppsGrid()` | Sets up the auto-launch apps DataGridView columns. |
| `ConfigureOscRoutesGrid()` | Sets up the OSC routes DataGridView columns. |
| `ReadBaseStationsGrid()` | Reads base station rows from the grid. |
| `ReadAutoLaunchAppsGrid()` | Reads auto-launch app rows from the grid. |
| `ReadOscRoutesGrid()` | Reads OSC route rows from the grid. |
| `UpsertBaseStationGridRow()` | Adds or updates a base station row (merge by Bluetooth address). |

### Base Station Operations

| Method | Description |
| --- | --- |
| `ScanBaseStationsAsync()` | Scans for BLE base stations and merges into the grid. |
| `RefreshBaseStationStatesAsync()` | Reads live power state from V2 stations. |
| `SendBaseStationPowerOnToEnabledRowsAsync()` | Sends power-on to all enabled stations. |
| `SendBaseStationCommandToEnabledRowsAsync()` | Sends a custom command to enabled stations. |

### Validation

| Method | Description |
| --- | --- |
| `ValidateCurrentConfig(ValidationMode)` | Full validation with errors and warnings. |
| `ValidatePath()` | Validates an executable or folder path. |
| `ValidateAutoLaunchApps()` | Validates auto-launch app rows. |
| `ValidateProcessList()` | Validates process name lists. |
| `ValidateOscRoutes()` | Validates OSC route configuration. |
| `ValidateBaseStations()` | Validates base station configuration. |
| `ValidateTimingValues()` | Validates timing values against recommended ranges. |
| `ValidateRawJsonText()` | Validates the Raw JSON text. |

### Detector Testing

| Method | Description |
| --- | --- |
| `TestDetectorRulesAsync()` | Tests detector rules against connected devices. |
| `ShowDetectorTestDialog()` | Shows a dialog with matched device details. |

### UI Helpers

| Method | Description |
| --- | --- |
| `ApplyWindowsTheme()` | Detects Windows dark/light mode and applies theme. |
| `ApplyThemeTo(Control)` | Recursively applies theme to a control tree. |
| `ShowThemedMessageBox()` | Shows a themed message box dialog. |
| `DrawEmptyGridPlaceholder()` | Draws placeholder text on empty grids. |

## ValidationResult

| Property | Description |
| --- | --- |
| `Errors` | List of validation error messages. |
| `Warnings` | List of validation warning messages. |
| `HasErrors` | `true` if any errors exist. |
| `HasWarnings` | `true` if any warnings exist. |

## EditorState

Persists editor window state between sessions.

| Property | Description |
| --- | --- |
| `LastConfigPath` | Last opened config file path. |
| `LastSelectedTab` | Last selected tab index. |
| `WindowBounds` | Window position and size. |
| `WindowState` | Normal or Maximized. |

## JsonPropertyEditor

Utility for editing JSON properties by key.

| Method | Description |
| --- | --- |
| `Replace(string, string, string)` | Replaces a property value in JSON. |
| `Remove(string, string)` | Removes a property from JSON. |

See also: [Reference Overview](index.md) · [Configuration Fields](configuration-fields.md) · [Supervisor Functions](supervisor-functions.md)
