# Configurator Issues

## Editor Won't Open

| Check | Action |
| --- | --- |
| .NET runtime | The configurator requires .NET 9. Use the self-contained release or install the runtime. |
| Corrupt config | If the editor crashes on load, delete the state file at `%APPDATA%\Pimax VRC Supervisor\Configurator\configurator-state.json`. |
| Window off-screen | Delete the state file to reset window position. |

## "Could not load config" Error

| Cause | Solution |
| --- | --- |
| Invalid JSON | Fix the JSON syntax. Use the **Raw JSON** tab or a JSON validator. |
| File in use | Close other programs that might have the file open. |
| Permission denied | Ensure the config file is in a writable location. |

## Validation Errors

### "Broken Eye executable path is required"

The path is empty or the file doesn't exist. Browse for the correct executable in the **General** tab.

### "Enabled auto-launch row has no executable path"

An auto-launch app row is enabled but has no path. Either disable the row or set a valid path.

### "Duplicate base station Bluetooth address"

Two base station rows have the same Bluetooth address. Remove or correct the duplicate.

### "Enabled OSC route requires a name and target app receive port"

An OSC route is enabled but missing a name or valid port (1â€“65535).

## Grid Rows Not Saving

| Symptom | Solution |
| --- | --- |
| Changes lost after reload | Click **Save** before closing or reloading. |
| Row appears empty | Ensure all required fields are filled. Empty rows are skipped on save. |
| Delete doesn't work | Select the row and press **Delete** or click the **Delete** button. |

## Raw JSON Tab Shows "Invalid JSON"

The JSON parser supports:
- Trailing commas
- Comments (`//` and `/* */`)
- Standard JSON types (strings, numbers, booleans, arrays, objects)

Common mistakes:
- Missing quotes around property names
- Missing commas between properties
- Unclosed brackets or braces
- Single quotes instead of double quotes

Click **Format JSON** to auto-format after fixing syntax errors.

## Theme Issues

The editor auto-detects Windows dark/light mode. If the theme looks wrong:

1. Check Windows Settings â†’ Personalization â†’ Colors â†’ "Choose your mode".
2. Restart the editor after changing the theme.

## Keyboard Shortcuts Not Working

Ensure the editor window has focus. Some shortcuts (like Ctrl+S) only work when a control within the form is focused, not when the text box is in edit mode.

See also: [Troubleshooting Overview](index.md) Â· [Install Issues](install-issues.md) Â· [Base Station Issues](base-station-issues.md)
