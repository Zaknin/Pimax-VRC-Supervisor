using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PimaxVrcSupervisor.ConfigEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ConfigEditorForm(args.FirstOrDefault()));
    }
}

internal sealed class ConfigEditorForm : Form
{
    private const string DefaultVrcFaceTrackingDirectory = @"C:\Program Files (x86)\Steam\steamapps\common\VRCFaceTracking";

    private readonly TextBox _configPathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _brokenEyePathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _vrcFaceTrackingPathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _mouthTrackerCheckBox = CreateTriStateCheckBox("Use Vive mouth tracker");
    private readonly CheckBox _turnOffMonitorsCheckBox = CreateTriStateCheckBox("Turn off secondary monitors during headset sessions");
    private readonly CheckBox _autoLaunchTaskCheckBox = CreateTriStateCheckBox("Create/evaluate VRChat auto-launch Scheduled Task");
    private readonly CheckBox _usePimaxLogCheckBox = new() { Text = "Watch Pimax PiService logs for fast reconnects", AutoSize = true };
    private readonly CheckBox _useMouthTrackerPnPCheckBox = new() { Text = "Watch Windows PnP events for fast mouth tracker reconnects", AutoSize = true };
    private readonly TextBox _pimaxServiceLogDirectoryTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly DataGridView _autoLaunchAppsGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        RowHeadersVisible = false
    };
    private readonly TextBox _brokenEyeProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _vrcFaceTrackingProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _watchedShutdownProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _steamVrServerProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _pimaxDetectorsTextBox = CreateMultilineTextBox();
    private readonly TextBox _mouthTrackerDetectorsTextBox = CreateMultilineTextBox();
    private readonly TextBox _rawJsonTextBox = CreateMultilineTextBox(readOnly: false);
    private readonly Label _statusLabel = new() { AutoSize = true };
    private readonly ToolTip _toolTips = new()
    {
        AutoPopDelay = 12000,
        InitialDelay = 400,
        ReshowDelay = 100,
        ShowAlways = true
    };
    private readonly Dictionary<string, NumericUpDown> _numberInputs = new(StringComparer.Ordinal);
    private AppTheme _theme = AppTheme.Light;
    private string _loadedJson = "{\r\n}\r\n";

    public ConfigEditorForm(string? requestedConfigPath)
    {
        Text = "Pimax VRC Supervisor Config Editor";
        SetWindowIconFromExecutable();
        MinimumSize = new Size(780, 620);
        Size = new Size(940, 740);
        StartPosition = FormStartPosition.CenterScreen;

        ConfigureToolTips();
        BuildLayout();
        ApplyWindowsTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        var configPath = FindConfigPath(requestedConfigPath);
        if (configPath is not null)
        {
            _configPathTextBox.Text = configPath;
            LoadConfig(configPath);
        }
        else
        {
            _configPathTextBox.Text = Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
            SetStatus("Choose or save a config file to begin.");
        }
    }

    private void SetWindowIconFromExecutable()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
            {
                Icon = icon;
            }
        }
        catch
        {
            // Keep the default form icon if Windows cannot extract the executable icon.
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowsTheme();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _toolTips.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            BeginInvoke(ApplyWindowsTheme);
        }
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildPathBar(), 0, 0);
        root.Controls.Add(BuildTabs(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildPathBar()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var browseButton = new Button { Text = "Browse...", AutoSize = true };
        browseButton.Click += (_, _) => BrowseConfig();
        _toolTips.SetToolTip(browseButton, "Choose the supervisor.config.json file you want this editor to load.");

        var reloadButton = new Button { Text = "Reload", AutoSize = true };
        reloadButton.Click += (_, _) => LoadConfig(_configPathTextBox.Text);
        _toolTips.SetToolTip(reloadButton, "Reload values from disk and discard unsaved edits in the editor.");

        var configPathLabel = new Label { Text = "Config file", AutoSize = true, Anchor = AnchorStyles.Left };
        _toolTips.SetToolTip(configPathLabel, "The JSON file that will be loaded and saved.");
        _toolTips.SetToolTip(_configPathTextBox, "Usually this is supervisor.config.json next to the supervisor exe.");
        layout.Controls.Add(configPathLabel, 0, 0);
        layout.Controls.Add(_configPathTextBox, 1, 0);
        layout.Controls.Add(browseButton, 2, 0);
        layout.Controls.Add(reloadButton, 3, 0);
        return layout;
    }

    private Control BuildTabs()
    {
        var tabs = new ThemedTabHost { Dock = DockStyle.Fill };
        tabs.AddTab("Basics", BuildBasicsTab());
        tabs.AddTab("Auto Launch", BuildAutoLaunchTab());
        tabs.AddTab("Processes", BuildProcessesTab());
        tabs.AddTab("Detectors", BuildDetectorsTab());
        tabs.AddTab("Timing", BuildTimingTab());
        tabs.AddTab("Raw JSON", _rawJsonTextBox);
        return tabs;
    }

    private Control BuildBasicsTab()
    {
        var layout = CreateFormLayout(3);

        AddPathRow(layout, "Broken Eye.exe", _brokenEyePathTextBox, "Full path to Broken Eye.exe. The supervisor starts this app first.");
        AddPathRow(
            layout,
            "VRCFaceTracking.exe",
            _vrcFaceTrackingPathTextBox,
            "Full path to VRCFaceTracking.exe. The supervisor starts this after Broken Eye settles.",
            DefaultVrcFaceTrackingDirectory);
        AddFullWidth(layout, _mouthTrackerCheckBox, "Checked means you use a Vive mouth tracker. Unchecked disables mouth-tracker monitoring. Filled square leaves the first-run question enabled.");
        AddFullWidth(layout, _turnOffMonitorsCheckBox, "Checked saves the current monitor layout and disables secondary monitors during the VR session. The layout is restored after VRChat and SteamVR close.");
        AddFullWidth(layout, _autoLaunchTaskCheckBox, "Checked lets the app create or repair the elevated auto-launch Scheduled Task. Filled square asks on first setup.");
        AddFullWidth(layout, _usePimaxLogCheckBox, "Also scan PiService logs for quick HID remove/add reconnects that normal USB polling can miss.");
        AddFullWidth(layout, _useMouthTrackerPnPCheckBox, "Also scan Windows Kernel-PnP events for quick mouth tracker reconnects that normal USB polling can miss.");
        AddLabeledRow(layout, "PiService log folder", _pimaxServiceLogDirectoryTextBox, "Folder containing PiService__*.log files. Environment variables such as %LOCALAPPDATA% are expanded by the supervisor.");

        return layout;
    }

    private Control BuildAutoLaunchTab()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ConfigureAutoLaunchAppsGrid();

        const string tooltip = "Optional apps to launch after Broken Eye and VRCFaceTracking are running. The supervisor infers the process name from the selected exe.";
        var label = new Label
        {
            Text = "Auto-launch apps",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4)
        };

        _toolTips.SetToolTip(label, tooltip);
        _toolTips.SetToolTip(_autoLaunchAppsGrid, tooltip);
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(_autoLaunchAppsGrid, 0, 1);
        return layout;
    }

    private void ConfigureAutoLaunchAppsGrid()
    {
        if (_autoLaunchAppsGrid.Columns.Count > 0)
        {
            return;
        }

        _autoLaunchAppsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "Name",
            FillWeight = 20,
            MinimumWidth = 150
        });
        _autoLaunchAppsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Path",
            HeaderText = "Exe path",
            FillWeight = 42,
            MinimumWidth = 240
        });
        _autoLaunchAppsGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Browse",
            HeaderText = "",
            Text = "Browse...",
            UseColumnTextForButtonValue = true,
            DefaultCellStyle = { NullValue = "Browse..." },
            FillWeight = 10,
            MinimumWidth = 110
        });
        _autoLaunchAppsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "Enabled",
            FillWeight = 10,
            MinimumWidth = 88
        });
        _autoLaunchAppsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "RestartOnPimaxReconnect",
            HeaderText = "Restart on Pimax reconnect",
            FillWeight = 22,
            MinimumWidth = 190
        });
        _autoLaunchAppsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "RunAsAdmin",
            HeaderText = "Run as admin",
            FillWeight = 14,
            MinimumWidth = 120
        });
        _autoLaunchAppsGrid.CellContentClick += OnAutoLaunchAppsGridCellContentClick;
        _autoLaunchAppsGrid.CellFormatting += OnAutoLaunchAppsGridCellFormatting;
        _autoLaunchAppsGrid.DefaultValuesNeeded += OnAutoLaunchAppsGridDefaultValuesNeeded;
    }

    private void OnAutoLaunchAppsGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex >= 0 && _autoLaunchAppsGrid.Columns[e.ColumnIndex].Name == "Browse")
        {
            e.Value = "Browse...";
            e.FormattingApplied = true;
        }
    }

    private void OnAutoLaunchAppsGridDefaultValuesNeeded(object? sender, DataGridViewRowEventArgs e)
    {
        e.Row.Cells["Enabled"].Value = true;
        e.Row.Cells["RestartOnPimaxReconnect"].Value = true;
        e.Row.Cells["RunAsAdmin"].Value = false;
    }

    private void OnAutoLaunchAppsGridCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _autoLaunchAppsGrid.Columns[e.ColumnIndex].Name != "Browse")
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Select auto-launch app",
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        var selectedRow = _autoLaunchAppsGrid.Rows[e.RowIndex];
        var currentPath = selectedRow.IsNewRow ? "" : GetGridString(selectedRow, "Path");
        if (File.Exists(currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            dialog.FileName = Path.GetFileName(currentPath);
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var row = selectedRow.IsNewRow
            ? _autoLaunchAppsGrid.Rows[AddAutoLaunchAppGridRow("", "", enabled: true, restartOnPimaxReconnect: true, runAsAdmin: false)]
            : selectedRow;
        row.Cells["Path"].Value = dialog.FileName;
        if (string.IsNullOrWhiteSpace(GetGridString(row, "Name")))
        {
            row.Cells["Name"].Value = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private Control BuildProcessesTab()
    {
        var layout = CreateFormLayout(2);
        AddLabeledRow(layout, "Broken Eye process names", _brokenEyeProcessesTextBox, "Process names used to detect, attach to, and close Broken Eye. Do not include .exe.");
        AddLabeledRow(layout, "VRCFaceTracking process names", _vrcFaceTrackingProcessesTextBox, "Process names used to detect, attach to, and close VRCFaceTracking. Do not include .exe.");
        AddLabeledRow(layout, "Shutdown watch process names", _watchedShutdownProcessesTextBox, "When one of these processes has run and then exits, the supervisor closes the managed apps.");
        AddLabeledRow(layout, "SteamVR server process names", _steamVrServerProcessesTextBox, "When monitor handling is enabled, the supervisor waits for these processes to exit before restoring monitors.");
        return layout;
    }

    private Control BuildDetectorsTab()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        layout.Controls.Add(
            BuildDetectorPanel(
                "Pimax detector rules",
                _pimaxDetectorsTextBox,
                "Each line is one possible Pimax headset match rule. Put multiple required keywords on the same line separated by commas."),
            0,
            0);
        layout.Controls.Add(
            BuildDetectorPanel(
                "Mouth tracker detector rules",
                _mouthTrackerDetectorsTextBox,
                "Each line is one possible mouth tracker match rule. Put multiple required keywords on the same line separated by commas."),
            0,
            1);
        return layout;
    }

    private Control BuildDetectorPanel(string title, TextBox textBox, string tooltip)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label
        {
            Text = title + " (one rule per line, comma-separated keywords)",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4)
        };
        textBox.Dock = DockStyle.Fill;
        _toolTips.SetToolTip(label, tooltip);
        _toolTips.SetToolTip(textBox, tooltip);
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(textBox, 0, 1);
        return panel;
    }

    private Control BuildTimingTab()
    {
        var layout = CreateFormLayout(2);
        AddNumber(layout, "PimaxServiceLogReconnectLookbackLines", 1, 100000, "Number of recent PiService log lines scanned each polling cycle.");
        AddNumber(layout, "PollIntervalSeconds", 1, 3600, "How often the supervisor checks device and watched-process state.");
        AddNumber(layout, "StartupTimeoutSeconds", 1, 3600, "Maximum seconds to wait for a started app to appear as a running process.");
        AddNumber(layout, "StartupStableSeconds", 0, 3600, "Seconds an app must remain running before startup verification succeeds.");
        AddNumber(layout, "DelayBeforeVrcFaceTrackingSeconds", 0, 3600, "Seconds to wait after starting Broken Eye before starting VRCFaceTracking.");
        AddNumber(layout, "RestartDelayAfterReconnectSeconds", 0, 3600, "Seconds to wait after Pimax reconnects before restarting managed apps.");
        AddNumber(layout, "WatchedProcessCrashRelaunchGraceSeconds", 0, 86400, "If VRChat exits with a likely crash code, seconds to wait for it to relaunch before cleanup.");
        AddNumber(layout, "ShutdownGraceSeconds", 0, 3600, "Seconds to wait for graceful app shutdown before force-closing process trees.");
        AddNumber(layout, "DeviceProbeTimeoutSeconds", 1, 3600, "Maximum seconds to wait for the Windows device query command.");
        return layout;
    }

    private Control BuildFooter()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var saveButton = new Button { Text = "Save", AutoSize = true };
        saveButton.Click += (_, _) => SaveConfig();
        _toolTips.SetToolTip(saveButton, "Save the current editor values into the selected config file.");

        var saveAsButton = new Button { Text = "Save As...", AutoSize = true };
        saveAsButton.Click += (_, _) => SaveConfigAs();
        _toolTips.SetToolTip(saveAsButton, "Choose a different JSON file path, then save the current editor values.");

        layout.Controls.Add(_statusLabel, 0, 0);
        layout.Controls.Add(saveAsButton, 1, 0);
        layout.Controls.Add(saveButton, 2, 0);
        return layout;
    }

    private static TableLayoutPanel CreateFormLayout(int columns)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = columns,
            Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (columns > 2)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        return layout;
    }

    private void AddPathRow(
        TableLayoutPanel layout,
        string label,
        TextBox textBox,
        string tooltip,
        string? suggestedDirectory = null)
    {
        var browseButton = new Button { Text = "Browse...", AutoSize = true };
        browseButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select " + label,
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                FileName = Path.GetFileName(textBox.Text)
            };

            if (File.Exists(textBox.Text))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(textBox.Text);
            }
            else if (!string.IsNullOrWhiteSpace(suggestedDirectory) && Directory.Exists(suggestedDirectory))
            {
                dialog.InitialDirectory = suggestedDirectory;
            }

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                textBox.Text = dialog.FileName;
            }
        };

        AddControlRow(layout, new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, textBox, browseButton, tooltip);
    }

    private void AddLabeledRow(TableLayoutPanel layout, string label, Control control, string tooltip)
    {
        AddControlRow(layout, new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, control, null, tooltip);
    }

    private void AddControlRow(TableLayoutPanel layout, Control label, Control control, Control? extra, string tooltip)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _toolTips.SetToolTip(label, tooltip);
        _toolTips.SetToolTip(control, tooltip);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
        if (layout.ColumnCount > 2 && extra is not null)
        {
            _toolTips.SetToolTip(extra, tooltip);
            layout.Controls.Add(extra, 2, row);
        }
        else if (layout.ColumnCount > 2)
        {
            layout.Controls.Add(new Label(), 2, row);
        }
    }

    private void AddFullWidth(TableLayoutPanel layout, Control control, string tooltip)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _toolTips.SetToolTip(control, tooltip);
        layout.Controls.Add(control, 0, row);
        layout.SetColumnSpan(control, layout.ColumnCount);
    }

    private void AddNumber(TableLayoutPanel layout, string propertyName, int minimum, int maximum, string tooltip)
    {
        var input = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Anchor = AnchorStyles.Left,
            Width = 110
        };
        _numberInputs[propertyName] = input;
        AddLabeledRow(layout, propertyName, input, tooltip);
    }

    private void BrowseConfig()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select supervisor.config.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "supervisor.config.json"
        };

        if (File.Exists(_configPathTextBox.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_configPathTextBox.Text);
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _configPathTextBox.Text = dialog.FileName;
            LoadConfig(dialog.FileName);
        }
    }

    private void SaveConfigAs()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save supervisor.config.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "supervisor.config.json"
        };

        if (File.Exists(_configPathTextBox.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_configPathTextBox.Text);
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _configPathTextBox.Text = dialog.FileName;
            SaveConfig();
        }
    }

    private void LoadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _loadedJson = "{\r\n}\r\n";
                _rawJsonTextBox.Text = _loadedJson;
                SetStatus("Config file does not exist yet. Fill values, then Save.");
                return;
            }

            _loadedJson = File.ReadAllText(path);
            _rawJsonTextBox.Text = _loadedJson;
            var node = ParseJson(_loadedJson);
            PopulateControls(node);
            SetStatus("Loaded " + path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not load config", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Load failed.");
        }
    }

    private void PopulateControls(JsonNode? node)
    {
        _brokenEyePathTextBox.Text = GetString(node, "BrokenEyePath");
        _vrcFaceTrackingPathTextBox.Text = GetString(node, "VrcFaceTrackingPath");
        _mouthTrackerCheckBox.CheckState = GetBoolCheckState(node, "MouthTrackerUser");
        _turnOffMonitorsCheckBox.CheckState = GetBoolCheckState(node, "TurnOffSecondaryMonitors");
        _autoLaunchTaskCheckBox.CheckState = GetBoolCheckState(node, "AutoLaunchScheduledTask");
        _usePimaxLogCheckBox.Checked = GetBool(node, "UsePimaxServiceLogReconnectDetector", defaultValue: true);
        _useMouthTrackerPnPCheckBox.Checked = GetBool(node, "UseMouthTrackerPnPReconnectDetector", defaultValue: true);
        _pimaxServiceLogDirectoryTextBox.Text = GetString(node, "PimaxServiceLogDirectory");
        PopulateAutoLaunchAppsGrid(GetAutoLaunchApps(node));
        _brokenEyeProcessesTextBox.Text = string.Join(", ", GetStringArray(node, "BrokenEyeProcessNames"));
        _vrcFaceTrackingProcessesTextBox.Text = string.Join(", ", GetStringArray(node, "VrcFaceTrackingProcessNames"));
        _watchedShutdownProcessesTextBox.Text = string.Join(", ", GetStringArray(node, "WatchedShutdownProcessNames"));
        _steamVrServerProcessesTextBox.Text = string.Join(", ", GetStringArray(node, "SteamVrServerProcessNames"));
        _pimaxDetectorsTextBox.Text = FormatStringMatrix(GetStringMatrix(node, "PimaxDetectors"));
        _mouthTrackerDetectorsTextBox.Text = FormatStringMatrix(GetStringMatrix(node, "MouthTrackerDetectors"));

        foreach (var (propertyName, input) in _numberInputs)
        {
            input.Value = Math.Clamp(GetInt(node, propertyName, decimal.ToInt32(input.Minimum)), input.Minimum, input.Maximum);
        }
    }

    private void SaveConfig()
    {
        try
        {
            var path = _configPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Choose a config file path before saving.");
            }

            CommitAutoLaunchAppsGridEdits();
            var json = ApplyControlValues(_rawJsonTextBox.Text);
            ParseJson(json);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _loadedJson = json;
            _rawJsonTextBox.Text = json;
            SetStatus("Saved " + path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save config", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Save failed.");
        }
    }

    private string ApplyControlValues(string baseJson)
    {
        var json = string.IsNullOrWhiteSpace(baseJson) ? "{\r\n}\r\n" : baseJson;
        json = JsonPropertyEditor.Replace(json, "BrokenEyePath", Serialize(_brokenEyePathTextBox.Text.Trim()));
        json = JsonPropertyEditor.Replace(json, "VrcFaceTrackingPath", Serialize(_vrcFaceTrackingPathTextBox.Text.Trim()));
        json = JsonPropertyEditor.Replace(json, "AutoLaunchApps", Serialize(ReadAutoLaunchAppsGrid()));
        json = JsonPropertyEditor.Replace(json, "BrokenEyeProcessNames", Serialize(ParseStringList(_brokenEyeProcessesTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "VrcFaceTrackingProcessNames", Serialize(ParseStringList(_vrcFaceTrackingProcessesTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "WatchedShutdownProcessNames", Serialize(ParseStringList(_watchedShutdownProcessesTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "SteamVrServerProcessNames", Serialize(ParseStringList(_steamVrServerProcessesTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "MouthTrackerUser", SerializeTriState(_mouthTrackerCheckBox.CheckState));
        json = JsonPropertyEditor.Replace(json, "TurnOffSecondaryMonitors", SerializeTriState(_turnOffMonitorsCheckBox.CheckState));
        json = JsonPropertyEditor.Replace(json, "AutoLaunchScheduledTask", SerializeTriState(_autoLaunchTaskCheckBox.CheckState));
        json = JsonPropertyEditor.Replace(json, "PimaxDetectors", Serialize(ParseStringMatrix(_pimaxDetectorsTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "MouthTrackerDetectors", Serialize(ParseStringMatrix(_mouthTrackerDetectorsTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "UsePimaxServiceLogReconnectDetector", _usePimaxLogCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "UseMouthTrackerPnPReconnectDetector", _useMouthTrackerPnPCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "PimaxServiceLogDirectory", Serialize(_pimaxServiceLogDirectoryTextBox.Text.Trim()));

        foreach (var (propertyName, input) in _numberInputs)
        {
            json = JsonPropertyEditor.Replace(json, propertyName, ((int)input.Value).ToString());
        }

        return json;
    }

    private static JsonNode? ParseJson(string json)
    {
        return JsonNode.Parse(
            json,
            nodeOptions: null,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SerializeTriState(CheckState checkState)
    {
        return checkState switch
        {
            CheckState.Checked => "true",
            CheckState.Unchecked => "false",
            _ => "\"\""
        };
    }

    private static string GetString(JsonNode? node, string propertyName)
    {
        return node?[propertyName]?.GetValue<string>() ?? "";
    }

    private static bool GetBool(JsonNode? node, string propertyName, bool defaultValue)
    {
        return node?[propertyName] is JsonValue value && value.TryGetValue<bool>(out var result)
            ? result
            : defaultValue;
    }

    private static bool? GetOptionalBool(JsonNode? node, string propertyName)
    {
        return node?[propertyName] is JsonValue value && value.TryGetValue<bool>(out var result)
            ? result
            : null;
    }

    private static CheckState GetBoolCheckState(JsonNode? node, string propertyName)
    {
        if (node?[propertyName] is not JsonValue value)
        {
            return CheckState.Indeterminate;
        }

        if (value.TryGetValue<bool>(out var result))
        {
            return result ? CheckState.Checked : CheckState.Unchecked;
        }

        return CheckState.Indeterminate;
    }

    private static int GetInt(JsonNode? node, string propertyName, int defaultValue)
    {
        return node?[propertyName] is JsonValue value && value.TryGetValue<int>(out var result)
            ? result
            : defaultValue;
    }

    private static string[] GetStringArray(JsonNode? node, string propertyName)
    {
        return node?[propertyName] is JsonArray array
            ? array.Select(item => item?.GetValue<string>() ?? "").Where(value => value.Length > 0).ToArray()
            : [];
    }

    private static string[][] GetStringMatrix(JsonNode? node, string propertyName)
    {
        if (node?[propertyName] is not JsonArray array)
        {
            return [];
        }

        return array
            .OfType<JsonArray>()
            .Select(inner => inner.Select(item => item?.GetValue<string>() ?? "").Where(value => value.Length > 0).ToArray())
            .Where(rule => rule.Length > 0)
            .ToArray();
    }

    private static AutoLaunchAppEditorRow[] GetAutoLaunchApps(JsonNode? node)
    {
        if (node?["AutoLaunchApps"] is not JsonArray array)
        {
            return [];
        }

        var apps = new List<AutoLaunchAppEditorRow>();
        foreach (var item in array)
        {
            switch (item)
            {
                case JsonValue value when value.TryGetValue<string>(out var path) && !string.IsNullOrWhiteSpace(path):
                    apps.Add(new AutoLaunchAppEditorRow("", path.Trim(), Enabled: true, RestartOnPimaxReconnect: true, RunAsAdmin: false));
                    break;
                case JsonObject obj:
                    var appPath = GetString(obj, "Path").Trim();
                    if (string.IsNullOrWhiteSpace(appPath))
                    {
                        continue;
                    }

                    apps.Add(new AutoLaunchAppEditorRow(
                        GetString(obj, "Name").Trim(),
                        appPath,
                        GetBool(obj, "Enabled", defaultValue: true),
                        GetOptionalBool(obj, "RestartOnPimaxReconnect")
                            ?? GetOptionalBool(obj, "CloseOnPimaxDisconnect")
                            ?? true,
                        GetBool(obj, "RunAsAdmin", defaultValue: false)));
                    break;
            }
        }

        return apps.ToArray();
    }

    private static string[] ParseStringList(string text)
    {
        return text.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length > 0)
            .ToArray();
    }

    private void PopulateAutoLaunchAppsGrid(AutoLaunchAppEditorRow[] apps)
    {
        _autoLaunchAppsGrid.Rows.Clear();
        foreach (var app in apps)
        {
            AddAutoLaunchAppGridRow(app.Name, app.Path, app.Enabled, app.RestartOnPimaxReconnect, app.RunAsAdmin);
        }
    }

    private int AddAutoLaunchAppGridRow(string name, string path, bool enabled, bool restartOnPimaxReconnect, bool runAsAdmin)
    {
        return _autoLaunchAppsGrid.Rows.Add(
            name,
            path,
            "",
            enabled,
            restartOnPimaxReconnect,
            runAsAdmin);
    }

    private void CommitAutoLaunchAppsGridEdits()
    {
        if (_autoLaunchAppsGrid.IsCurrentCellDirty)
        {
            _autoLaunchAppsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        _autoLaunchAppsGrid.EndEdit();
        ValidateChildren();
    }

    private AutoLaunchAppEditorRow[] ReadAutoLaunchAppsGrid()
    {
        CommitAutoLaunchAppsGridEdits();
        var apps = new List<AutoLaunchAppEditorRow>();
        foreach (DataGridViewRow row in _autoLaunchAppsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var path = GetGridString(row, "Path");
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            apps.Add(new AutoLaunchAppEditorRow(
                GetGridString(row, "Name"),
                path,
                GetGridBool(row, "Enabled", defaultValue: true),
                GetGridBool(row, "RestartOnPimaxReconnect", defaultValue: true),
                GetGridBool(row, "RunAsAdmin", defaultValue: false)));
        }

        return apps.ToArray();
    }

    private static string[][] ParseStringMatrix(string text)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(rule => rule.Length > 0)
            .ToArray();
    }

    private static string FormatStringMatrix(string[][] matrix)
    {
        return string.Join(Environment.NewLine, matrix.Select(rule => string.Join(", ", rule)));
    }

    private static string GetGridString(DataGridViewRow row, string columnName)
    {
        return Convert.ToString(row.Cells[columnName].Value)?.Trim() ?? "";
    }

    private static bool GetGridBool(DataGridViewRow row, string columnName, bool defaultValue)
    {
        var value = row.Cells[columnName].Value;
        return value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static CheckBox CreateTriStateCheckBox(string text)
    {
        return new CheckBox
        {
            Text = text + " (filled square = ask on first run)",
            AutoSize = true,
            ThreeState = true,
            CheckState = CheckState.Indeterminate
        };
    }

    private static TextBox CreateMultilineTextBox(bool readOnly = false)
    {
        return new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            Multiline = true,
            ReadOnly = readOnly,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private void ConfigureToolTips()
    {
        _toolTips.OwnerDraw = true;
        _toolTips.Draw += (_, eventArgs) =>
        {
            using var backBrush = new SolidBrush(_theme.ToolTipBack);
            using var borderPen = new Pen(_theme.Border);
            eventArgs.Graphics.FillRectangle(backBrush, eventArgs.Bounds);
            eventArgs.Graphics.DrawRectangle(
                borderPen,
                eventArgs.Bounds.X,
                eventArgs.Bounds.Y,
                eventArgs.Bounds.Width - 1,
                eventArgs.Bounds.Height - 1);

            var textBounds = Rectangle.Inflate(eventArgs.Bounds, -6, -3);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                eventArgs.ToolTipText,
                eventArgs.Font ?? Font,
                textBounds,
                _theme.ToolTipFore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
    }

    private void ApplyWindowsTheme()
    {
        _theme = WindowsThemeDetector.AppsUseLightTheme() ? AppTheme.Light : AppTheme.Dark;
        ApplyThemeTo(this);
        _toolTips.BackColor = _theme.ToolTipBack;
        _toolTips.ForeColor = _theme.ToolTipFore;
        WindowsTitleBar.ApplyTheme(Handle, _theme.IsDark);
        Invalidate(invalidateChildren: true);
    }

    private void ApplyThemeTo(Control control)
    {
        switch (control)
        {
            case Form form:
                form.BackColor = _theme.WindowBack;
                form.ForeColor = _theme.Text;
                break;
            case ThemedTabHost tabHost:
                tabHost.ApplyTheme(_theme);
                break;
            case TextBox textBox:
                textBox.BackColor = _theme.InputBack;
                textBox.ForeColor = _theme.Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case NumericUpDown input:
                input.BackColor = _theme.InputBack;
                input.ForeColor = _theme.Text;
                break;
            case DataGridView grid:
                ApplyThemeToGrid(grid);
                break;
            case Button button:
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = _theme.Border;
                button.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
                button.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
                button.BackColor = _theme.ButtonBack;
                button.ForeColor = _theme.Text;
                break;
            case CheckBox checkBox:
                checkBox.UseVisualStyleBackColor = false;
                checkBox.BackColor = _theme.WindowBack;
                checkBox.ForeColor = _theme.Text;
                break;
            case Label label:
                label.BackColor = _theme.WindowBack;
                label.ForeColor = _theme.Text;
                break;
            case Panel or TableLayoutPanel:
                control.BackColor = _theme.WindowBack;
                control.ForeColor = _theme.Text;
                break;
            default:
                control.BackColor = _theme.WindowBack;
                control.ForeColor = _theme.Text;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyThemeTo(child);
        }

        if (control is ThemedTabHost themedTabs)
        {
            themedTabs.ApplyTheme(_theme);
        }
    }

    private void ApplyThemeToGrid(DataGridView grid)
    {
        grid.BackgroundColor = _theme.WindowBack;
        grid.GridColor = _theme.Border;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = _theme.ButtonBack;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = _theme.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = _theme.Text;
        grid.DefaultCellStyle.BackColor = _theme.InputBack;
        grid.DefaultCellStyle.ForeColor = _theme.Text;
        grid.DefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
        grid.DefaultCellStyle.SelectionForeColor = _theme.Text;
        grid.RowHeadersDefaultCellStyle.BackColor = _theme.WindowBack;
        grid.RowHeadersDefaultCellStyle.ForeColor = _theme.Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = _theme.WindowBack;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = _theme.Text;
    }

    private static string? FindConfigPath(string? requestedConfigPath)
    {
        var candidates = new[]
        {
            requestedConfigPath,
            Path.Combine(AppContext.BaseDirectory, "supervisor.config.json"),
            Path.Combine(Environment.CurrentDirectory, "supervisor.config.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "supervisor.config.json")
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .FirstOrDefault(File.Exists);
    }
}

internal sealed record AutoLaunchAppEditorRow(string Name, string Path, bool Enabled, bool RestartOnPimaxReconnect, bool RunAsAdmin);

internal sealed record AppTheme(
    bool IsDark,
    Color WindowBack,
    Color InputBack,
    Color ButtonBack,
    Color ButtonHover,
    Color ButtonPressed,
    Color TabBack,
    Color TabSelectedBack,
    Color ToolTipBack,
    Color ToolTipFore,
    Color Text,
    Color Border)
{
    public static readonly AppTheme Light = new(
        IsDark: false,
        WindowBack: SystemColors.Control,
        InputBack: SystemColors.Window,
        ButtonBack: SystemColors.ControlLight,
        ButtonHover: Color.FromArgb(229, 241, 251),
        ButtonPressed: Color.FromArgb(204, 228, 247),
        TabBack: SystemColors.Control,
        TabSelectedBack: SystemColors.Window,
        ToolTipBack: SystemColors.Info,
        ToolTipFore: SystemColors.InfoText,
        Text: SystemColors.ControlText,
        Border: SystemColors.ControlDark);

    public static readonly AppTheme Dark = new(
        IsDark: true,
        WindowBack: Color.FromArgb(32, 32, 32),
        InputBack: Color.FromArgb(45, 45, 45),
        ButtonBack: Color.FromArgb(58, 58, 58),
        ButtonHover: Color.FromArgb(74, 74, 74),
        ButtonPressed: Color.FromArgb(88, 88, 88),
        TabBack: Color.FromArgb(40, 40, 40),
        TabSelectedBack: Color.FromArgb(54, 54, 54),
        ToolTipBack: Color.FromArgb(45, 45, 45),
        ToolTipFore: Color.WhiteSmoke,
        Text: Color.WhiteSmoke,
        Border: Color.FromArgb(92, 92, 92));
}

internal sealed class ThemedTabHost : UserControl
{
    private readonly FlowLayoutPanel _tabStrip = new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        Padding = new Padding(0),
        WrapContents = false
    };
    private readonly Panel _contentPanel = new()
    {
        BorderStyle = BorderStyle.None,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };
    private readonly List<(Button Button, Control Content)> _tabs = [];
    private int _selectedIndex = -1;
    private AppTheme _theme = AppTheme.Light;

    public ThemedTabHost()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_tabStrip, 0, 0);
        layout.Controls.Add(_contentPanel, 0, 1);
        Controls.Add(layout);
    }

    public void AddTab(string title, Control content)
    {
        var tabIndex = _tabs.Count;
        var button = new Button
        {
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 3, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = title,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 1;
        button.Click += (_, _) => SelectTab(tabIndex);

        content.Dock = DockStyle.Fill;
        _tabs.Add((button, content));
        _tabStrip.Controls.Add(button);

        if (_selectedIndex < 0)
        {
            SelectTab(tabIndex);
        }
        else
        {
            ApplyTheme(_theme);
        }
    }

    public void ApplyTheme(AppTheme theme)
    {
        _theme = theme;
        BackColor = theme.WindowBack;
        ForeColor = theme.Text;
        _tabStrip.BackColor = theme.WindowBack;
        _tabStrip.ForeColor = theme.Text;
        _contentPanel.BackColor = theme.WindowBack;
        _contentPanel.ForeColor = theme.Text;

        for (var index = 0; index < _tabs.Count; index++)
        {
            var selected = index == _selectedIndex;
            var button = _tabs[index].Button;
            button.BackColor = selected ? theme.TabSelectedBack : theme.TabBack;
            button.ForeColor = theme.Text;
            button.FlatAppearance.BorderColor = selected ? theme.Text : theme.Border;
            button.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
            ApplyThemeToContent(_tabs[index].Content);
        }
    }

    private void SelectTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            return;
        }

        _selectedIndex = index;
        _contentPanel.SuspendLayout();
        _contentPanel.Controls.Clear();
        _contentPanel.Controls.Add(_tabs[index].Content);
        _contentPanel.ResumeLayout();
        ApplyTheme(_theme);
    }

    private void ApplyThemeToContent(Control control)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.BackColor = _theme.InputBack;
                textBox.ForeColor = _theme.Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case NumericUpDown input:
                input.BackColor = _theme.InputBack;
                input.ForeColor = _theme.Text;
                break;
            case DataGridView grid:
                ApplyThemeToGrid(grid);
                break;
            case Button button:
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = _theme.Border;
                button.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
                button.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
                button.BackColor = _theme.ButtonBack;
                button.ForeColor = _theme.Text;
                break;
            case CheckBox checkBox:
                checkBox.UseVisualStyleBackColor = false;
                checkBox.BackColor = _theme.WindowBack;
                checkBox.ForeColor = _theme.Text;
                break;
            case Label label:
                label.BackColor = _theme.WindowBack;
                label.ForeColor = _theme.Text;
                break;
            default:
                control.BackColor = _theme.WindowBack;
                control.ForeColor = _theme.Text;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyThemeToContent(child);
        }
    }

    private void ApplyThemeToGrid(DataGridView grid)
    {
        grid.BackgroundColor = _theme.WindowBack;
        grid.GridColor = _theme.Border;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = _theme.ButtonBack;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = _theme.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = _theme.Text;
        grid.DefaultCellStyle.BackColor = _theme.InputBack;
        grid.DefaultCellStyle.ForeColor = _theme.Text;
        grid.DefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
        grid.DefaultCellStyle.SelectionForeColor = _theme.Text;
        grid.RowHeadersDefaultCellStyle.BackColor = _theme.WindowBack;
        grid.RowHeadersDefaultCellStyle.ForeColor = _theme.Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = _theme.WindowBack;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = _theme.Text;
    }
}

internal static class WindowsThemeDetector
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool AppsUseLightTheme()
    {
        var value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
        return value is not int intValue || intValue != 0;
    }
}

internal static class WindowsTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void ApplyTheme(IntPtr handle, bool dark)
    {
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}

internal static class JsonPropertyEditor
{
    public static string Replace(string json, string propertyName, string serializedValue)
    {
        var propertyMatch = Regex.Match(json, $"\"{Regex.Escape(propertyName)}\"\\s*:");
        if (!propertyMatch.Success)
        {
            return InsertProperty(json, propertyName, serializedValue);
        }

        var valueStart = propertyMatch.Index + propertyMatch.Length;
        while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
        {
            valueStart++;
        }

        var valueEnd = FindValueEnd(json, valueStart);
        return json[..valueStart] + serializedValue + json[valueEnd..];
    }

    private static string InsertProperty(string json, string propertyName, string serializedValue)
    {
        var openBrace = json.IndexOf('{');
        if (openBrace < 0)
        {
            return "{\r\n  " + JsonSerializer.Serialize(propertyName) + ": " + serializedValue + "\r\n}\r\n";
        }

        var lineEnding = json.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertion = lineEnding + "  " + JsonSerializer.Serialize(propertyName) + ": " + serializedValue + ",";
        return json.Insert(openBrace + 1, insertion);
    }

    private static int FindValueEnd(string json, int valueStart)
    {
        if (valueStart >= json.Length)
        {
            return valueStart;
        }

        return json[valueStart] switch
        {
            '"' => FindStringEnd(json, valueStart),
            '[' => FindBalancedEnd(json, valueStart, '[', ']'),
            '{' => FindBalancedEnd(json, valueStart, '{', '}'),
            _ => FindPrimitiveEnd(json, valueStart)
        };
    }

    private static int FindStringEnd(string json, int start)
    {
        var escaped = false;
        for (var index = start + 1; index < json.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (json[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (json[index] == '"')
            {
                return index + 1;
            }
        }

        return json.Length;
    }

    private static int FindBalancedEnd(string json, int start, char open, char close)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < json.Length; index++)
        {
            var ch = json[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
            }
            else if (ch == open)
            {
                depth++;
            }
            else if (ch == close)
            {
                depth--;
                if (depth == 0)
                {
                    return index + 1;
                }
            }
        }

        return json.Length;
    }

    private static int FindPrimitiveEnd(string json, int start)
    {
        var index = start;
        while (index < json.Length && json[index] is not ',' and not '\r' and not '\n' and not '}')
        {
            index++;
        }

        return index;
    }
}
