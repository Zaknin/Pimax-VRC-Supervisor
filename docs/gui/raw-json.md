# Raw JSON Tab

The **Raw JSON** tab provides direct editing of the full `supervisor.config.json` file.

!!! warning
    Edits here directly change the full configuration used by all tabs. Only use this tab if you understand the configuration schema. Invalid JSON will prevent the configuration from being saved or applied.

## Controls

| Button | Description |
| --- | --- |
| **Apply JSON to editor** | Parses the Raw JSON text and updates all other tabs with those values. |
| **Revert JSON changes** | Discards unapplied Raw JSON edits and restores JSON from the current editor values. |
| **Format JSON** | Pretty-prints the Raw JSON text after validating it. |

## Validation Status

Below the editor, a validation label shows the current state:

- **"Raw JSON is valid."** — The JSON is syntactically correct and matches the current editor state.
- **"Raw JSON is valid but has not been applied to the editor."** — The JSON is valid but differs from what the other tabs show. Click **Apply JSON to editor** to sync.
- **"Invalid JSON: ..."** — The JSON has syntax errors. Fix the errors before saving.

## JSON Format

The config file uses standard JSON with the following features supported by the parser:

- Trailing commas
- Comments (`//` and `/* */`)
- Indented formatting

## Config File Location

The config file is typically `supervisor.config.json` next to the supervisor executables. The editor can open any JSON file via **Browse** in the path bar.

## Keyboard Shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save As |
| Ctrl+Z | Undo (in text box) |
| Ctrl+Y | Redo (in text box) |

See also: [GUI Manual Overview](index.md) · [Timing](timing.md) · [Reference](../reference/index.md)
