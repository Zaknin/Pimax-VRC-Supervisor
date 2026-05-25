using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using PimaxVrcSupervisor.BaseStations;

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
    private static readonly string BaseWindowTitle = $"Pimax VRC Supervisor Config Editor {AppVersion.Current}";
    private const int TemporaryStatusMilliseconds = 15000;
    private const int MinimumGridVisibleRows = 10;
    private const int CompactNumberInputWidth = 104;
    private const int PathStatusColumnWidth = 230;
    private const int MinimumConfigSelectorWidth = 180;
    private const int MaximumConfigSelectorWidth = 620;
    private const string DefaultConfigFileName = "supervisor.config.json";
    private const string ActiveConfigSelectionFileName = "supervisor.active-config.txt";
    private const string DefaultConfigResourceName = "PimaxVrcSupervisor.ConfigEditor.Defaults.supervisor.config.json";
    private const string DefaultVrcFaceTrackingDirectory = @"C:\Program Files (x86)\Steam\steamapps\common\VRCFaceTracking";
    private static readonly string DefaultIntifacePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IntifaceCentral",
        "intiface_central.exe");
    private static readonly string DefaultOscGoesBrrrPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "OscGoesBrrr",
        "OscGoesBrrr.exe");
    private static readonly string[][] DefaultLovenseDetectors = [["Lovense"], ["LVS-"]];
    private static readonly string[] ConsoleShortcutLines =
    [
        "1 = Broken Eye + VRCFaceTracking routine",
        "2 = OSCGoesBrrr + Intiface routine",
        "3 = Turn on all controlled base stations",
        "4 = Turn off all controlled base stations",
        "5 = OSC Router launch/restart",
        "6 = Reload Autostart apps",
        "F1 = Show console shortcuts"
    ];
    private static readonly string[] EditorShortcutLines =
    [
        "Ctrl+S = Save",
        "Ctrl+Shift+S = Save As",
        "Ctrl+O = Browse config",
        "F5 = Reload",
        "Ctrl+R = Reload",
        "Ctrl+L = Launch Supervisor",
        "Ctrl+Shift+V = Validate",
        "Delete = Delete selected row in Auto Launch, Base Stations, or OSC Routes grids"
    ];

    private readonly ComboBox _configSelectorComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _configPathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _displayNameTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _brokenEyePathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _vrcFaceTrackingPathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _intifacePathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _oscGoesBrrrPathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _brokenEyeStartMinimizedCheckBox = new() { Text = "Start Broken Eye minimized", AutoSize = true };
    private readonly CheckBox _vrcFaceTrackingStartMinimizedCheckBox = new() { Text = "Start VRCFaceTracking minimized", AutoSize = true };
    private readonly CheckBox _intifaceStartMinimizedCheckBox = new() { Text = "Start Intiface minimized", AutoSize = true };
    private readonly CheckBox _oscGoesBrrrStartMinimizedCheckBox = new() { Text = "Start OscGoesBrrr minimized", AutoSize = true };
    private readonly CheckBox _oscGoesBrrrEnabledCheckBox = new() { Text = "Enabled", AutoSize = true };
    private readonly CheckBox _oscGoesBrrrHotkeyCheckBox = new() { Text = "Use manual console launch mode", AutoSize = true };
    private readonly CheckBox _oscGoesBrrrBleScannerCheckBox = new() { Text = "Enable BLE scanner", AutoSize = true };
    private readonly CheckBox _oscRouterEnabledCheckBox = new() { Text = "Enable OSC routing", AutoSize = true };
    private readonly NumericUpDown _oscRouterReceivePortInput = new()
    {
        Minimum = 1,
        Maximum = 65535,
        Value = 9001,
        Anchor = AnchorStyles.Left,
        Width = 110
    };
    private readonly CheckBox _baseStationsEnabledCheckBox = new() { Text = "Enable base station power automation", AutoSize = true };
    private readonly ComboBox _baseStationPowerDownModeComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _mouthTrackerCheckBox = CreateOptionalConfigCheckBox("Use Vive mouth tracker");
    private readonly CheckBox _turnOffMonitorsCheckBox = CreateOptionalConfigCheckBox("Turn off secondary monitors during headset sessions");
    private readonly CheckBox _autoLaunchTaskCheckBox = new() { Text = "Create/evaluate VRChat auto-launch Scheduled Task", AutoSize = true };
    private readonly CheckBox _startWithSteamVrCheckBox = new() { Text = "Start with SteamVR", AutoSize = true };
    private readonly CheckBox _usePimaxLogCheckBox = new() { Text = "Watch Pimax PiService logs for fast reconnects", AutoSize = true };
    private readonly CheckBox _useMouthTrackerPnPCheckBox = new() { Text = "Watch Windows PnP events for fast mouth tracker reconnects", AutoSize = true };
    private readonly TextBox _pimaxServiceLogDirectoryTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly DataGridView _autoLaunchAppsGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly DataGridView _baseStationsGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly DataGridView _oscRoutesGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly TextBox _brokenEyeProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _vrcFaceTrackingProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _intifaceProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _oscGoesBrrrProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _watchedShutdownProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _steamVrServerProcessesTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _pimaxDetectorsTextBox = CreateMultilineTextBox();
    private readonly TextBox _mouthTrackerDetectorsTextBox = CreateMultilineTextBox();
    private readonly TextBox _lovenseDetectorsTextBox = CreateMultilineTextBox();
    private readonly TextBox _rawJsonTextBox = CreateMultilineTextBox(readOnly: false);
    private readonly Label _rawJsonValidationLabel = new() { AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
    private readonly Label _statusLabel = new() { AutoSize = true };
    private readonly ThemedTabHost _tabs = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = TemporaryStatusMilliseconds };
    private readonly ToolTip _toolTips = new()
    {
        AutoPopDelay = 12000,
        InitialDelay = 400,
        ReshowDelay = 100,
        ShowAlways = true
    };
    private readonly Dictionary<string, NumericUpDown> _numberInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _numberLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _numberUnits = new(StringComparer.Ordinal);
    private readonly List<Action> _pathIndicatorRefreshers = [];
    private readonly EditorState _editorState = EditorState.Load();
    private AppTheme _theme = AppTheme.Light;
    private string _loadedJson = "{\r\n}\r\n";
    private string _appliedRawJson = "{\r\n}\r\n";
    private string _persistentStatus = "Ready";
    private string _persistentStatusToolTip = "Ready";
    private string _rawJsonStatus = "";
    private string? _temporaryStatus;
    private string? _temporaryStatusToolTip;
    private DateTime _temporaryStatusExpiresAt;
    private int _temporaryStatusTabIndex = -1;
    private bool _temporaryStatusIsActiveOperation;
    private bool _hasUnsavedChanges;
    private bool _rawJsonHasUnappliedChanges;
    private bool _suppressDirtyTracking;
    private bool _suppressConfigSelectorChange;
    private bool _saveInProgress;
    private bool _startupIntegrationApplyInProgress;
    private bool _mouthTrackerPreferenceTouched;
    private bool _turnOffMonitorsPreferenceTouched;
    private bool _startupIntegrationPreferenceTouched;

    public ConfigEditorForm(string? requestedConfigPath)
    {
        Text = BaseWindowTitle;
        SetWindowIconFromExecutable();
        MinimumSize = new Size(1040, 720);
        Size = new Size(1260, 820);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        RestoreWindowState();

        ConfigureToolTips();
        BuildLayout();
        ConfigureStartupOptionInteractions();
        RegisterDirtyTracking(this);
        ApplyWindowsTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _statusTimer.Tick += (_, _) => ExpireTemporaryStatus();

        var configPath = ResolveInitialConfigPath(requestedConfigPath);
        if (configPath is not null)
        {
            _configPathTextBox.Text = configPath;
            LoadConfig(configPath);
        }
        else
        {
            var defaultConfigPath = GetDefaultConfigPath();
            EnsureDefaultConfigFile(defaultConfigPath);
            _configPathTextBox.Text = defaultConfigPath;
            LoadConfig(defaultConfigPath);
        }

        Shown += async (_, _) => await PromptForExistingStartupTaskMigrationAsync();
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
            _statusTimer.Dispose();
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
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 112,
            Padding = new Padding(0, 6, 0, 12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var configSelectorLabel = new Label { Text = "Config", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) };
        _configSelectorComboBox.Dock = DockStyle.None;
        _configSelectorComboBox.Anchor = AnchorStyles.Left;
        _configSelectorComboBox.Width = MinimumConfigSelectorWidth;
        _configSelectorComboBox.Margin = new Padding(0, 2, 10, 4);
        _configSelectorComboBox.SelectedIndexChanged += (_, _) => LoadSelectedConfigFromDropdown();
        _toolTips.SetToolTip(configSelectorLabel, "Named configs in the app folder.");
        _toolTips.SetToolTip(_configSelectorComboBox, "Select a named *.config.json file from the app folder.");

        var restoreBackupButton = CreateButton("Restore Backup...", autoSize: false, width: 126, height: 28);
        restoreBackupButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        restoreBackupButton.Margin = new Padding(0, 0, 6, 4);
        restoreBackupButton.Click += (_, _) => RestoreBackup();
        _toolTips.SetToolTip(restoreBackupButton, "Restore a selected .bak file into the default supervisor.config.json.");

        var deleteButton = CreateButton("Delete", autoSize: false, width: 86, height: 28);
        deleteButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        deleteButton.Margin = new Padding(0, 0, 0, 4);
        deleteButton.Click += (_, _) => DeleteSelectedConfig();
        _toolTips.SetToolTip(deleteButton, "Delete the selected named config file from the app folder.");

        var browseButton = CreateButton("Browse...", autoSize: false, width: 96, height: 28);
        browseButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        browseButton.Click += (_, _) => BrowseConfig();
        _toolTips.SetToolTip(browseButton, "Open config (Ctrl+O). Choose the supervisor.config.json file you want this editor to load.");

        var reloadButton = CreateButton("Reload", autoSize: false, width: 86, height: 28);
        reloadButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        reloadButton.Click += (_, _) => ReloadConfig();
        _toolTips.SetToolTip(reloadButton, "Reload (F5 or Ctrl+R). Reload values from disk and discard unsaved edits in the editor.");

        var configPathLabel = new Label { Text = "Config file", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) };
        _configPathTextBox.Dock = DockStyle.Fill;
        _configPathTextBox.Margin = new Padding(0, 2, 10, 4);
        browseButton.Margin = new Padding(0, 0, 6, 4);
        reloadButton.Margin = new Padding(0, 0, 0, 4);
        _toolTips.SetToolTip(configPathLabel, "The JSON file that will be loaded and saved.");
        _toolTips.SetToolTip(_configPathTextBox, "Usually this is supervisor.config.json next to the supervisor exe.");

        var displayNameLabel = new Label { Text = "Display name", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) };
        _displayNameTextBox.Dock = DockStyle.Fill;
        _displayNameTextBox.Margin = new Padding(0, 2, 10, 0);
        _toolTips.SetToolTip(displayNameLabel, "Friendly name saved as DisplayName in the config JSON.");
        _toolTips.SetToolTip(_displayNameTextBox, "Friendly name shown in the editor config list and console startup.");

        layout.Controls.Add(configSelectorLabel, 0, 0);
        layout.Controls.Add(_configSelectorComboBox, 1, 0);
        layout.Controls.Add(restoreBackupButton, 2, 0);
        layout.Controls.Add(deleteButton, 3, 0);
        layout.Controls.Add(configPathLabel, 0, 1);
        layout.Controls.Add(_configPathTextBox, 1, 1);
        layout.Controls.Add(browseButton, 2, 1);
        layout.Controls.Add(reloadButton, 3, 1);
        layout.Controls.Add(displayNameLabel, 0, 2);
        layout.Controls.Add(_displayNameTextBox, 1, 2);
        layout.SetColumnSpan(_displayNameTextBox, 3);
        panel.Controls.Add(layout);
        return panel;
    }

    private static Button CreateButton(
        string text,
        bool autoSize = true,
        int width = 0,
        int height = 0,
        DialogResult dialogResult = DialogResult.None)
    {
        var button = new ThemedActionButton
        {
            Text = text,
            AutoSize = autoSize,
            DialogResult = dialogResult
        };
        if (width > 0)
        {
            button.Width = width;
        }

        if (height > 0)
        {
            button.Height = height;
        }

        return button;
    }

    private Control BuildTabs()
    {
        var selectedTab = _editorState.LastSelectedTab;
        _tabs.AddTab("General", BuildBasicsTab());
        _tabs.AddTab("Auto Launch", BuildAutoLaunchTab());
        _tabs.AddTab("Base Stations", BuildBaseStationsTab());
        _tabs.AddTab("Detectors", BuildDetectorsTab());
        _tabs.AddTab("Processes", BuildProcessesTab());
        _tabs.AddTab("OSC Router", BuildOscRouterTab());
        _tabs.AddTab("OSCGoesBrrr", BuildLovenseTab());
        _tabs.AddTab("Timing", BuildTimingTab());
        _tabs.AddTab("Raw JSON", BuildRawJsonTab());
        _tabs.SelectTab(Math.Clamp(selectedTab, 0, _tabs.TabCount - 1));
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            _editorState.LastSelectedTab = _tabs.SelectedIndex;
            RefreshVisibleStatus();
        };
        return _tabs;
    }

    private Control BuildBasicsTab()
    {
        var layout = CreateFormLayout(3);
        layout.Dock = DockStyle.Top;
        layout.AutoSize = true;
        layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;

        AddPathRow(layout, "Broken Eye executable", _brokenEyePathTextBox, "Full path to Broken Eye.exe. The supervisor starts this app first.", configKey: "BrokenEyePath", defaultFileName: "Broken Eye.exe");
        AddFullWidth(layout, _brokenEyeStartMinimizedCheckBox, "Checked means the supervisor starts Broken Eye minimized and tries to minimize its main window after launch.");
        AddPathRow(
            layout,
            "VRCFaceTracking executable",
            _vrcFaceTrackingPathTextBox,
            "Full path to VRCFaceTracking.exe. The supervisor starts this after Broken Eye settles.",
            DefaultVrcFaceTrackingDirectory,
            "VrcFaceTrackingPath",
            "VRCFaceTracking.exe");
        AddFullWidth(layout, _vrcFaceTrackingStartMinimizedCheckBox, "Checked means the supervisor starts VRCFaceTracking minimized and tries to minimize its main window after launch.");
        AddFullWidth(layout, _mouthTrackerCheckBox, "Checked means you use a Vive mouth tracker. Unchecked disables mouth-tracker monitoring.");
        AddFullWidth(layout, _turnOffMonitorsCheckBox, "Checked saves the current monitor layout and disables secondary monitors during the VR session. The layout is restored after VRChat and SteamVR close.");
        AddSectionHeader(layout, "Startup");
        AddFullWidth(layout, _autoLaunchTaskCheckBox, "Checked lets the app create or repair the elevated auto-launch Scheduled Task.");
        AddFullWidth(layout, _startWithSteamVrCheckBox, "Checked registers the SteamVR dashboard host manifest and starts the supervisor when SteamVR starts.");
        AddFolderValidationRow(layout, "PiService log folder", _pimaxServiceLogDirectoryTextBox, ToolTipWithConfigKey("Folder containing PiService__*.log files. Environment variables such as %LOCALAPPDATA% are expanded by the supervisor.", "PimaxServiceLogDirectory"));

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var restoreButton = CreateButton("Restore Defaults");
        restoreButton.Click += (_, _) => RestoreDefaults();
        _toolTips.SetToolTip(restoreButton, "Replace current editor values with default configuration values. The config file is not overwritten until Save.");
        buttons.Controls.Add(restoreButton);
        var aboutButton = CreateButton("About");
        aboutButton.Click += (_, _) => ShowAboutDialog();
        _toolTips.SetToolTip(aboutButton, "Show editor version and supervisor executable path.");
        buttons.Controls.Add(aboutButton);
        AddFullWidth(layout, buttons, "Editor utilities.");

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(layout, 0, 0);
        content.Controls.Add(BuildHotkeysPanel(), 1, 0);

        return BuildTabWithDescription(
            "General supervisor options",
            "Configure core behavior used by the console supervisor and choose whether helper features are enabled.",
            content);
    }

    private Control BuildHotkeysPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(18, 8, 8, 8),
            Margin = new Padding(12, 0, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(CreateSectionLabel("Hotkeys"), 0, panel.RowCount++);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var consoleHeader = CreateMutedLabel("Console hotkeys");
        consoleHeader.Font = new Font(FontFamily.GenericSansSerif, 9F, FontStyle.Bold);
        consoleHeader.Padding = new Padding(0, 6, 0, 3);
        panel.Controls.Add(consoleHeader, 0, panel.RowCount++);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(CreateShortcutListLabel(ConsoleShortcutLines), 0, panel.RowCount++);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var editorHeader = CreateMutedLabel("Config Editor shortcuts");
        editorHeader.Font = new Font(FontFamily.GenericSansSerif, 9F, FontStyle.Bold);
        editorHeader.Padding = new Padding(0, 14, 0, 3);
        panel.Controls.Add(editorHeader, 0, panel.RowCount++);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(CreateShortcutListLabel(EditorShortcutLines), 0, panel.RowCount++);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return panel;
    }

    private static Label CreateShortcutListLabel(string[] lines) => new()
    {
        Text = string.Join(Environment.NewLine, lines),
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        Padding = new Padding(0, 0, 0, 4)
    };

    private void ConfigureStartupOptionInteractions()
    {
        _mouthTrackerCheckBox.CheckStateChanged += (_, _) =>
        {
            if (!_suppressDirtyTracking)
            {
                _mouthTrackerPreferenceTouched = true;
            }
        };

        _turnOffMonitorsCheckBox.CheckStateChanged += (_, _) =>
        {
            if (!_suppressDirtyTracking)
            {
                _turnOffMonitorsPreferenceTouched = true;
            }
        };

        _autoLaunchTaskCheckBox.CheckStateChanged += (_, _) =>
        {
            if (_suppressDirtyTracking)
            {
                return;
            }

            _startupIntegrationPreferenceTouched = true;
            if (_autoLaunchTaskCheckBox.CheckState == CheckState.Checked && _startWithSteamVrCheckBox.Checked)
            {
                _startWithSteamVrCheckBox.Checked = false;
            }
        };

        _startWithSteamVrCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressDirtyTracking)
            {
                _startupIntegrationPreferenceTouched = true;
            }

            if (_startWithSteamVrCheckBox.Checked && _autoLaunchTaskCheckBox.CheckState == CheckState.Checked)
            {
                _autoLaunchTaskCheckBox.CheckState = CheckState.Unchecked;
            }
        };
    }

    private Control BuildBaseStationsTab()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _baseStationPowerDownModeComboBox.Items.Clear();
        _baseStationPowerDownModeComboBox.Items.Add(BaseStationPowerDownMode.Sleep.ToString());
        _baseStationPowerDownModeComboBox.Items.Add(BaseStationPowerDownMode.Standby.ToString());
        _baseStationPowerDownModeComboBox.SelectedIndex = 0;
        _baseStationPowerDownModeComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Top;

        var settings = CreateFormLayout(2);
        settings.Dock = DockStyle.Top;
        settings.AutoSize = true;
        AddFullWidth(settings, _baseStationsEnabledCheckBox, "Checked means the supervisor powers on enabled base stations after SteamVR and the Pimax headset are present, then powers them down after VRChat and SteamVR close.");
        AddLabeledRow(settings, "Power-down mode", _baseStationPowerDownModeComboBox, "Sleep works for Base Station 1.0 and 2.0. Standby applies to Base Station 2.0; Base Station 1.0 falls back to sleep.");

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8)
        };
        var scanButton = CreateButton("Scan");
        scanButton.Click += async (_, _) => await ScanBaseStationsAsync();
        _toolTips.SetToolTip(scanButton, "Scan nearby Bluetooth LE devices for SteamVR base stations and merge them into the list.");
        buttons.Controls.Add(scanButton);
        var refreshStateButton = CreateButton("Refresh State");
        refreshStateButton.Click += async (_, _) => await RefreshBaseStationStatesAsync();
        _toolTips.SetToolTip(refreshStateButton, "Read live power state from enabled Base Station 2.0 devices when firmware supports it.");
        buttons.Controls.Add(refreshStateButton);
        var turnOnButton = CreateButton("Turn On");
        turnOnButton.Click += async (_, _) => await SendBaseStationPowerOnToEnabledRowsAsync();
        _toolTips.SetToolTip(turnOnButton, "Power on every enabled base station in the list.");
        buttons.Controls.Add(turnOnButton);
        var turnOffButton = CreateButton("Turn Off");
        turnOffButton.Click += async (_, _) => await SendBaseStationCommandToEnabledRowsAsync(
            "Turn Off",
            (client, baseStation, token) => client.PowerDownAsync(baseStation, SelectedBaseStationPowerDownMode(), token));
        _toolTips.SetToolTip(turnOffButton, "Power down every enabled base station using the selected power-down mode.");
        buttons.Controls.Add(turnOffButton);
        var addManualButton = CreateButton("Add Manual");
        addManualButton.Click += (_, _) =>
        {
            var rowIndex = AddBaseStationGridRow(new BaseStationDevice
            {
                FriendlyName = "Base station",
                Name = "",
                BluetoothAddress = "",
                Version = BaseStationVersion.V2,
                Enabled = true
            });
            SelectGridRow(_baseStationsGrid, rowIndex);
            UpdateBaseStationRowWarnings();
            MarkDirty("Unsaved changes");
        };
        _toolTips.SetToolTip(addManualButton, "Add a base station row manually if Windows discovery does not expose it.");
        buttons.Controls.Add(addManualButton);
        var deleteButton = CreateButton("Delete");
        deleteButton.Click += (_, _) => DeleteSelectedGridRow(_baseStationsGrid, "Deleted selected base station row.");
        _toolTips.SetToolTip(deleteButton, "Delete the selected base station row. Changes are written only when you save the config.");
        buttons.Controls.Add(deleteButton);

        ConfigureBaseStationsGrid();
        ConfigureDynamicGridSizing(_baseStationsGrid);
        layout.Controls.Add(settings, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(_baseStationsGrid, 0, 2);
        UpdateDynamicGridHeight(_baseStationsGrid);
        return BuildTabWithDescription(
            "Base station power and identification",
            "Scan for SteamVR base stations, test power states, and use Identify to confirm each device before saving.",
            layout);
    }

    private void ConfigureBaseStationsGrid()
    {
        if (_baseStationsGrid.Columns.Count > 0)
        {
            return;
        }

        _baseStationsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "Enabled",
            FillWeight = 6,
            MinimumWidth = 64
        });
        _baseStationsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FriendlyName",
            HeaderText = "Friendly name",
            FillWeight = 16,
            MinimumWidth = 130
        });
        _baseStationsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "BLE name",
            FillWeight = 14,
            MinimumWidth = 120
        });
        _baseStationsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "BluetoothAddress",
            HeaderText = "Bluetooth address",
            FillWeight = 15,
            MinimumWidth = 130
        });
        _baseStationsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Version",
            HeaderText = "Version",
            DataSource = Enum.GetNames<BaseStationVersion>(),
            FillWeight = 7,
            MinimumWidth = 72
        });
        _baseStationsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Id",
            HeaderText = "V1 ID",
            FillWeight = 7,
            MinimumWidth = 70
        });
        _baseStationsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "State",
            HeaderText = "State",
            ReadOnly = true,
            FillWeight = 7,
            MinimumWidth = 70
        });
        _baseStationsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "PowerStateReadUnsupported",
            HeaderText = "State read unsupported",
            Visible = false
        });
        _baseStationsGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "PowerOn",
            HeaderText = "",
            Text = "Power On",
            UseColumnTextForButtonValue = true,
            FillWeight = 7,
            MinimumWidth = 76
        });
        _baseStationsGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Sleep",
            HeaderText = "",
            Text = "Sleep",
            UseColumnTextForButtonValue = true,
            FillWeight = 6,
            MinimumWidth = 66
        });
        _baseStationsGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Standby",
            HeaderText = "",
            Text = "Standby",
            UseColumnTextForButtonValue = true,
            FillWeight = 7,
            MinimumWidth = 76
        });
        _baseStationsGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Identify",
            HeaderText = "",
            Text = "Identify",
            UseColumnTextForButtonValue = true,
            FillWeight = 7,
            MinimumWidth = 76
        });
        _baseStationsGrid.CellContentClick += OnBaseStationsGridCellContentClick;
        _baseStationsGrid.CellValueChanged += OnBaseStationsGridCellValueChanged;
        _baseStationsGrid.CellFormatting += OnWarningGridCellFormatting;
        _baseStationsGrid.DefaultValuesNeeded += OnBaseStationsGridDefaultValuesNeeded;
        _baseStationsGrid.KeyDown += OnManagedGridKeyDown;
        _baseStationsGrid.Paint += (_, e) => DrawEmptyGridPlaceholder(_baseStationsGrid, e, "No base stations configured");
    }

    private void OnBaseStationsGridDefaultValuesNeeded(object? sender, DataGridViewRowEventArgs e)
    {
        e.Row.Cells["Enabled"].Value = true;
        e.Row.Cells["Version"].Value = BaseStationVersion.V2.ToString();
        RefreshBaseStationIdCellState(e.Row, clearInvalidId: true);
    }

    private void OnBaseStationsGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _baseStationsGrid.Rows.Count)
        {
            return;
        }

        var row = _baseStationsGrid.Rows[e.RowIndex];
        if (_baseStationsGrid.Columns[e.ColumnIndex].Name == "Version")
        {
            RefreshBaseStationIdCellState(row, clearInvalidId: true);
        }

        UpdateBaseStationRowWarnings();
    }

    private async Task ScanBaseStationsAsync()
    {
        try
        {
            SetActiveOperationStatus("Scanning for base stations...");
            var discovered = await BaseStationDiscovery.ScanAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            var existingAddresses = ReadBaseStationsGrid()
                .Select(station => station.BluetoothAddress)
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newCount = 0;
            foreach (var baseStation in discovered)
            {
                if (!existingAddresses.Contains(baseStation.BluetoothAddress))
                {
                    newCount++;
                }

                UpsertBaseStationGridRow(baseStation.WithDefaults());
            }

            UpdateBaseStationRowWarnings();
            var enabledCount = ReadBaseStationsGrid().Count(station => station.Enabled);
            SetStatus($"Scan complete: {discovered.Count} base stations found, {enabledCount} enabled, {newCount} new.");
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex.Message, "Base station scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Base station scan failed.");
        }
    }

    private async void OnBaseStationsGridCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        var columnName = _baseStationsGrid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("PowerOn" or "Sleep" or "Standby" or "Identify"))
        {
            return;
        }

        CommitBaseStationsGridEdits();
        var row = _baseStationsGrid.Rows[e.RowIndex];
        var baseStation = ReadBaseStationGridRow(row);
        if (baseStation is null)
        {
            return;
        }

        if (columnName == "Standby" && !baseStation.SupportsStandby)
        {
            ShowThemedMessageBox("Standby is only supported for Base Station 2.0. Use Sleep for Base Station 1.0.", "Standby unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (columnName == "Identify" && !baseStation.SupportsStandby)
        {
            ShowThemedMessageBox("Identify is only supported for Base Station 2.0.", "Identify unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetActiveOperationStatus($"{columnName} test for {baseStation.DisplayName}...");
            var client = new BaseStationGattClient();
            switch (columnName)
            {
                case "PowerOn":
                    await client.PowerOnAsync(baseStation, CancellationToken.None);
                    break;
                case "Sleep":
                    await client.SleepAsync(baseStation, CancellationToken.None);
                    break;
                case "Standby":
                    await client.StandbyAsync(baseStation, CancellationToken.None);
                    break;
                case "Identify":
                    await client.IdentifyAsync(baseStation, CancellationToken.None);
                    break;
            }

            SetStatus(columnName == "Identify"
                ? $"Identify command sent to {baseStation.Name}."
                : $"{columnName} test succeeded for {baseStation.DisplayName}.");
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex.Message, "Base station command failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus($"{columnName} test failed.");
        }
    }

    private async Task SendBaseStationCommandToEnabledRowsAsync(
        string action,
        Func<BaseStationGattClient, BaseStationDevice, CancellationToken, Task> commandAsync,
        int attemptsPerStation = 1)
    {
        CommitBaseStationsGridEdits();
        var baseStations = ReadBaseStationsGrid()
            .Where(baseStation => baseStation.Enabled)
            .ToArray();
        if (baseStations.Length == 0)
        {
            SetStatus("No enabled base stations to control.");
            return;
        }

        var failures = new List<string>();
        var client = new BaseStationGattClient();
        SetActiveOperationStatus($"{action}: controlling {baseStations.Length} base station(s)...");
        for (var index = 0; index < baseStations.Length; index++)
        {
            var baseStation = baseStations[index];
            SetActiveOperationStatus($"{action}: {index + 1}/{baseStations.Length} {baseStation.DisplayName}...");
            Exception? lastException = null;
            try
            {
                for (var attempt = 1; attempt <= Math.Max(1, attemptsPerStation); attempt++)
                {
                    try
                    {
                        await commandAsync(client, baseStation, CancellationToken.None);
                        lastException = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (attempt < attemptsPerStation)
                        {
                            await Task.Delay(BaseStationCommandTiming.InterStationDelay);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            if (lastException is not null)
            {
                failures.Add($"{baseStation.DisplayName}: {lastException.Message}");
            }

            if (index < baseStations.Length - 1)
            {
                await Task.Delay(BaseStationCommandTiming.InterStationDelay);
            }
        }

        if (failures.Count == 0)
        {
            SetStatus($"{action} succeeded for {baseStations.Length} base station(s).");
            return;
        }

        ShowThemedMessageBox(string.Join(Environment.NewLine, failures), action + " failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        SetStatus($"{action} completed with {failures.Count} failure(s).");
    }

    private async Task SendBaseStationPowerOnToEnabledRowsAsync()
    {
        CommitBaseStationsGridEdits();
        var baseStations = ReadBaseStationsGrid()
            .Where(baseStation => baseStation.Enabled)
            .ToArray();
        if (baseStations.Length == 0)
        {
            SetStatus("No enabled base stations to control.");
            return;
        }

        var stationSucceeded = new bool[baseStations.Length];
        var client = new BaseStationGattClient();
        for (var pass = 1; pass <= BaseStationCommandTiming.PowerOnPasses; pass++)
        {
            var passBaseStations = pass >= 3
                ? baseStations.Where(baseStation => baseStation.RequiresExtendedPowerOnPasses).ToArray()
                : baseStations;
            if (passBaseStations.Length == 0)
            {
                continue;
            }

            if (pass == 3)
            {
                SetActiveOperationStatus($"Turn On: waiting before pass {pass}/{BaseStationCommandTiming.PowerOnPasses}...");
                await Task.Delay(BaseStationCommandTiming.PowerOnRetryPassDelay);
            }

            await SendBaseStationCommandToRowsAsync(
                passBaseStations,
                pass == 1 ? "Turn On" : $"Turn On pass {pass}",
                client,
                (baseStation, token) => client.PowerOnAsync(baseStation, token),
                BaseStationCommandTiming.PowerOnAttempts,
                index =>
                {
                    var originalIndex = Array.FindIndex(
                        baseStations,
                        candidate => string.Equals(candidate.BluetoothAddress, passBaseStations[index].BluetoothAddress, StringComparison.OrdinalIgnoreCase));
                    if (originalIndex >= 0)
                    {
                        stationSucceeded[originalIndex] = true;
                    }
                });
        }

        var succeeded = stationSucceeded.Count(value => value);
        if (succeeded != baseStations.Length)
        {
            var missing = baseStations
                .Where((_, index) => !stationSucceeded[index])
                .Select(baseStation => baseStation.DisplayName);
            ShowThemedMessageBox(string.Join(Environment.NewLine, missing), "Turn On failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        SetStatus(succeeded == baseStations.Length
            ? $"Turn On succeeded for {baseStations.Length} base station(s)."
            : $"Turn On completed. {succeeded}/{baseStations.Length} base station(s) accepted at least one command.");
    }

    private async Task<List<string>> SendBaseStationCommandToRowsAsync(
        BaseStationDevice[] baseStations,
        string action,
        BaseStationGattClient client,
        Func<BaseStationDevice, CancellationToken, Task> commandAsync,
        int attemptsPerStation,
        Action<int>? onSuccess = null)
    {
        var failures = new List<string>();
        SetActiveOperationStatus($"{action}: controlling {baseStations.Length} base station(s)...");
        for (var index = 0; index < baseStations.Length; index++)
        {
            var baseStation = baseStations[index];
            SetActiveOperationStatus($"{action}: {index + 1}/{baseStations.Length} {baseStation.DisplayName}...");
            Exception? lastException = null;
            try
            {
                for (var attempt = 1; attempt <= Math.Max(1, attemptsPerStation); attempt++)
                {
                    try
                    {
                        await commandAsync(baseStation, CancellationToken.None);
                        onSuccess?.Invoke(index);
                        lastException = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (attempt < attemptsPerStation)
                        {
                            await Task.Delay(BaseStationCommandTiming.InterStationDelay);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            if (lastException is not null)
            {
                failures.Add($"{baseStation.DisplayName}: {lastException.Message}");
            }

            if (index < baseStations.Length - 1)
            {
                await Task.Delay(BaseStationCommandTiming.InterStationDelay);
            }
        }

        return failures;
    }

    private BaseStationPowerDownMode SelectedBaseStationPowerDownMode()
        => Enum.TryParse(Convert.ToString(_baseStationPowerDownModeComboBox.SelectedItem), ignoreCase: true, out BaseStationPowerDownMode mode)
            ? mode
            : BaseStationPowerDownMode.Sleep;

    private Control BuildOscRouterTab()
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

        var settings = CreateFormLayout(2);
        settings.Dock = DockStyle.Top;
        settings.AutoSize = true;
        AddFullWidth(settings, _oscRouterEnabledCheckBox, ToolTipWithConfigKey("Checked means the supervisor starts the in-process OSC router before Broken Eye and VRCFaceTracking.", "OscRouterEnabled"));
        AddLabeledRow(settings, "Supervisor receive port", _oscRouterReceivePortInput, ToolTipWithConfigKey("Local UDP port the OSC router listens on at 127.0.0.1. Default is 9001.", "OscRouterReceivePort"));

        ConfigureOscRoutesGrid();
        ConfigureDynamicGridSizing(_oscRoutesGrid);
        var routesPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 8, 0, 0)
        };
        routesPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        routesPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        routesPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var routesLabel = new Label
        {
            Text = "OSC routes",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4)
        };
        var routeButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8)
        };
        var addRouteButton = CreateButton("Add Route");
        addRouteButton.Click += (_, _) => SelectGridRow(_oscRoutesGrid, AddOscRouteGridRow("", "", enabled: true));
        _toolTips.SetToolTip(addRouteButton, "Add a new OSC route row.");
        routeButtons.Controls.Add(addRouteButton);
        var deleteRouteButton = CreateButton("Delete");
        deleteRouteButton.Click += (_, _) => DeleteSelectedGridRow(_oscRoutesGrid, "Deleted selected OSC route.");
        _toolTips.SetToolTip(deleteRouteButton, "Delete the selected OSC route row. Changes are written only when you save the config.");
        routeButtons.Controls.Add(deleteRouteButton);
        const string routesTooltip = "Each enabled route receives every OSC datagram unchanged. No OSC address filtering is applied.";
        _toolTips.SetToolTip(routesLabel, routesTooltip);
        _toolTips.SetToolTip(_oscRoutesGrid, routesTooltip);
        routesPanel.Controls.Add(routesLabel, 0, 0);
        routesPanel.Controls.Add(routeButtons, 0, 1);
        routesPanel.Controls.Add(_oscRoutesGrid, 0, 2);
        UpdateDynamicGridHeight(_oscRoutesGrid);

        layout.Controls.Add(settings, 0, 0);
        layout.Controls.Add(routesPanel, 0, 1);
        return BuildTabWithDescription(
            "OSC routing",
            "Configure local OSC forwarding from the supervisor to apps such as VRCFT and OSCB00P.",
            layout);
    }

    private void ConfigureOscRoutesGrid()
    {
        if (_oscRoutesGrid.Columns.Count > 0)
        {
            return;
        }

        _oscRoutesGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "Enabled",
            FillWeight = 8,
            MinimumWidth = 76
        });
        _oscRoutesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "Name",
            FillWeight = 24,
            MinimumWidth = 150
        });
        _oscRoutesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "AppReceivePort",
            HeaderText = "Target app receive port",
            FillWeight = 14,
            MinimumWidth = 140
        });
        _oscRoutesGrid.DefaultValuesNeeded += OnOscRoutesGridDefaultValuesNeeded;
        _oscRoutesGrid.CellFormatting += OnWarningGridCellFormatting;
        _oscRoutesGrid.KeyDown += OnManagedGridKeyDown;
        _oscRoutesGrid.Paint += (_, e) => DrawEmptyGridPlaceholder(_oscRoutesGrid, e, "No OSC routes configured. Click Add Route to create one.");
    }

    private void OnOscRoutesGridDefaultValuesNeeded(object? sender, DataGridViewRowEventArgs e)
    {
        e.Row.Cells["Enabled"].Value = true;
    }

    private Control BuildLovenseTab()
    {
        var layout = CreateFormLayout(3, compact: true);

        AddSectionHeader(layout, "Feature enablement");
        _oscGoesBrrrEnabledCheckBox.Margin = new Padding(0, 2, 0, 2);
        _oscGoesBrrrHotkeyCheckBox.Margin = new Padding(0, 2, 0, 2);
        _oscGoesBrrrBleScannerCheckBox.Margin = new Padding(0, 2, 0, 2);
        _intifaceStartMinimizedCheckBox.Margin = new Padding(0, 2, 0, 2);
        _oscGoesBrrrStartMinimizedCheckBox.Margin = new Padding(0, 2, 0, 2);
        AddFullWidth(layout, _oscGoesBrrrEnabledCheckBox, ToolTipWithConfigKey("Checked means the OscGoesBrrr workflow is available during headset sessions.", "OscGoesBrrrEnabled"));
        AddFullWidth(layout, _oscGoesBrrrHotkeyCheckBox, ToolTipWithConfigKey("Checked keeps the OSCGoesBrrr workflow in manual console launch mode instead of prestarting Intiface and watching Windows Lovense device detection. Console hotkey 2 is available whenever the OSCGoesBrrr feature is enabled.", "OscGoesBrrrHotkeyEnabled"));
        AddFullWidth(layout, _oscGoesBrrrBleScannerCheckBox, ToolTipWithConfigKey("Checked means the supervisor scans nearby BLE advertisements for Lovense names such as LVS- and auto-launches the workflow when one matches.", "OscGoesBrrrBleScannerEnabled"));

        AddSectionHeader(layout, "Executables");
        AddPathRow(layout, "Intiface executable", _intifacePathTextBox, "Full path to intiface_central.exe. This starts first when a Lovense detector rule matches.", Path.GetDirectoryName(DefaultIntifacePath), "IntifacePath");
        AddPathRow(layout, "OscGoesBrrr executable", _oscGoesBrrrPathTextBox, "Full path to OscGoesBrrr.exe. This starts after Intiface is running.", Path.GetDirectoryName(DefaultOscGoesBrrrPath), "OscGoesBrrrPath");

        AddSectionHeader(layout, "Startup behavior");
        AddFullWidth(layout, _intifaceStartMinimizedCheckBox, ToolTipWithConfigKey("Checked means the supervisor starts Intiface minimized and tries to minimize its main window after launch.", "IntifaceStartMinimized"));
        AddFullWidth(layout, _oscGoesBrrrStartMinimizedCheckBox, ToolTipWithConfigKey("Checked means the supervisor starts OscGoesBrrr minimized and tries to minimize its main window after launch.", "OscGoesBrrrStartMinimized"));
        AddNumber(layout, "DelayBeforeOscGoesBrrrSeconds", "Delay before OscGoesBrrr", 0, 3600, "Seconds to wait after Intiface is running before starting OscGoesBrrr.", "seconds");

        AddSectionHeader(layout, "Process detection");
        AddLabeledRow(layout, "Intiface process name", _intifaceProcessesTextBox, ToolTipWithConfigKey("Process names used to detect, attach to, and close Intiface. .exe is optional.", "IntifaceProcessNames"));
        AddLabeledRow(layout, "OscGoesBrrr process name", _oscGoesBrrrProcessesTextBox, ToolTipWithConfigKey("Process names used to detect, attach to, and close OscGoesBrrr. .exe is optional.", "OscGoesBrrrProcessNames"));

        AddSectionHeader(layout, "BLE scanning");
        AddNumber(layout, "OscGoesBrrrBleScanSeconds", "BLE scan duration", 1, 3600, "Seconds each BLE scan burst runs.", "seconds");
        AddNumber(layout, "OscGoesBrrrBleScanIntervalSeconds", "BLE scan interval", 1, 3600, "Seconds to wait after each unsuccessful BLE scan before trying again.", "seconds");

        AddSectionHeader(layout, "Lovense detection");
        AddFullWidth(layout, BuildLovenseDetectorPanel(), ToolTipWithConfigKey("Each line is one possible Lovense match rule. Defaults include Lovense and LVS- for common Bluetooth/WebBluetooth names.", "LovenseDetectors"));

        return BuildTabWithDescription(
            "OSCGoesBrrr and Intiface workflow",
            "Configure optional Lovense/Intiface startup, process detection, BLE scanning, and launch timing.",
            layout,
            limitWidth: true);
    }

    private Control BuildLovenseDetectorPanel()
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = 148,
            Margin = new Padding(0, 0, 0, 0),
            Padding = new Padding(8),
            BorderStyle = BorderStyle.FixedSingle
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = "Lovense detector rules",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4)
        };
        _lovenseDetectorsTextBox.Dock = DockStyle.Fill;
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(_lovenseDetectorsTextBox, 0, 1);
        host.Controls.Add(panel);
        return host;
    }

    private Control BuildAutoLaunchTab()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        var appButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8)
        };
        var addAppButton = CreateButton("Add App");
        addAppButton.Click += (_, _) => SelectGridRow(
            _autoLaunchAppsGrid,
            AddAutoLaunchAppGridRow("", "", enabled: true, restartOnPimaxReconnect: true, runAsAdmin: false, startMinimized: false));
        _toolTips.SetToolTip(addAppButton, "Add a new auto-launch app row.");
        appButtons.Controls.Add(addAppButton);
        var deleteAppButton = CreateButton("Delete");
        deleteAppButton.Click += (_, _) => DeleteSelectedGridRow(_autoLaunchAppsGrid, "Deleted selected auto-launch app.");
        _toolTips.SetToolTip(deleteAppButton, "Delete the selected auto-launch app row. Changes are written only when you save the config.");
        appButtons.Controls.Add(deleteAppButton);

        _toolTips.SetToolTip(label, tooltip);
        ConfigureDynamicGridSizing(_autoLaunchAppsGrid);
        _toolTips.SetToolTip(_autoLaunchAppsGrid, tooltip);
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(appButtons, 0, 1);
        layout.Controls.Add(_autoLaunchAppsGrid, 0, 2);
        UpdateDynamicGridHeight(_autoLaunchAppsGrid);
        return BuildTabWithDescription(
            "Auto-launch apps",
            "Configure optional tools that should start with the supervisor or restart after a Pimax reconnect.",
            layout);
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
            HeaderText = "Executable path",
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
            HeaderText = "Restart after Pimax reconnect",
            FillWeight = 22,
            MinimumWidth = 190
        });
        _autoLaunchAppsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "RunAsAdmin",
            HeaderText = "Run as administrator",
            FillWeight = 14,
            MinimumWidth = 120
        });
        _autoLaunchAppsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "StartMinimized",
            HeaderText = "Start minimized",
            FillWeight = 16,
            MinimumWidth = 135
        });
        _autoLaunchAppsGrid.CellContentClick += OnAutoLaunchAppsGridCellContentClick;
        _autoLaunchAppsGrid.CellFormatting += OnAutoLaunchAppsGridCellFormatting;
        _autoLaunchAppsGrid.DefaultValuesNeeded += OnAutoLaunchAppsGridDefaultValuesNeeded;
        _autoLaunchAppsGrid.KeyDown += OnManagedGridKeyDown;
        _autoLaunchAppsGrid.Paint += (_, e) => DrawEmptyGridPlaceholder(_autoLaunchAppsGrid, e, "No auto-launch apps configured. Click Add App to create one.");
    }

    private void OnAutoLaunchAppsGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex >= 0 && _autoLaunchAppsGrid.Columns[e.ColumnIndex].Name == "Browse")
        {
            e.Value = "Browse...";
            e.FormattingApplied = true;
        }

        OnWarningGridCellFormatting(sender, e);
    }

    private void OnWarningGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(grid.Rows[e.RowIndex].ErrorText))
        {
            e.CellStyle.BackColor = _theme.IsDark ? Color.FromArgb(72, 52, 34) : Color.FromArgb(255, 244, 214);
            e.CellStyle.SelectionBackColor = _theme.IsDark ? Color.FromArgb(92, 68, 42) : Color.FromArgb(255, 231, 166);
        }
    }

    private void OnAutoLaunchAppsGridDefaultValuesNeeded(object? sender, DataGridViewRowEventArgs e)
    {
        e.Row.Cells["Enabled"].Value = true;
        e.Row.Cells["RestartOnPimaxReconnect"].Value = true;
        e.Row.Cells["RunAsAdmin"].Value = false;
        e.Row.Cells["StartMinimized"].Value = false;
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

        selectedRow.Cells["Path"].Value = dialog.FileName;
        if (string.IsNullOrWhiteSpace(GetGridString(selectedRow, "Name")))
        {
            selectedRow.Cells["Name"].Value = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private Control BuildProcessesTab()
    {
        var layout = CreateFormLayout(2);
        AddSectionHeader(layout, "Tool processes");
        AddLabeledRow(layout, "Broken Eye process name", _brokenEyeProcessesTextBox, ToolTipWithConfigKey("Process names used to detect, attach to, and close Broken Eye. Do not include .exe.", "BrokenEyeProcessNames"));
        AddLabeledRow(layout, "VRCFaceTracking process name", _vrcFaceTrackingProcessesTextBox, ToolTipWithConfigKey("Process names used to detect, attach to, and close VRCFaceTracking. Do not include .exe.", "VrcFaceTrackingProcessNames"));
        AddSectionHeader(layout, "Session process");
        AddLabeledRow(layout, "Apps that trigger cleanup when closed", _watchedShutdownProcessesTextBox, ToolTipWithConfigKey("When one of these processes has run and then exits, the supervisor closes the managed apps.", "WatchedShutdownProcessNames"));
        AddSectionHeader(layout, "SteamVR process");
        AddLabeledRow(layout, "SteamVR server process name", _steamVrServerProcessesTextBox, ToolTipWithConfigKey("When monitor handling is enabled, the supervisor waits for these processes to exit before restoring monitors.", "SteamVrServerProcessNames"));
        AddSectionHeader(layout, "Fast reconnect detection");
        AddFullWidth(layout, _usePimaxLogCheckBox, "Also scan PiService logs for quick HID remove/add reconnects that normal USB polling can miss.");
        AddFullWidth(layout, _useMouthTrackerPnPCheckBox, "Also scan Windows Kernel-PnP events for quick mouth tracker reconnects that normal USB polling can miss.");
        return BuildTabWithDescription(
            "Watched process names",
            "Configure the process names used to detect VRChat, Broken Eye, VRCFaceTracking, SteamVR, and shutdown conditions.",
            layout,
            limitWidth: true);
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
                "Pimax headset detection",
                "One rule per line. Use comma-separated keywords when all keywords must match.\r\nExample: Pimax, Crystal",
                "Each line is one possible Pimax headset match rule. Put multiple required keywords on the same line separated by commas.",
                "Test Pimax Rules",
                () => TestDetectorRulesAsync("Pimax", ParseStringMatrix(_pimaxDetectorsTextBox.Text))),
            0,
            0);
        layout.Controls.Add(
            BuildDetectorPanel(
                "Mouth tracker detector rules",
                _mouthTrackerDetectorsTextBox,
                "Mouth tracker detection",
                "One rule per line. Use comma-separated keywords when all keywords must match.\r\nExample: HTC Multimedia Camera",
                "Each line is one possible mouth tracker match rule. Put multiple required keywords on the same line separated by commas.",
                "Test Mouth Tracker Rules",
                () => TestDetectorRulesAsync("Mouth tracker", ParseStringMatrix(_mouthTrackerDetectorsTextBox.Text))),
            0,
            1);
        return BuildTabWithDescription(
            "Device detector rules",
            "Configure keyword rules used to detect Pimax devices and mouth trackers from USB/Bluetooth device names.",
            layout);
    }

    private Control BuildDetectorPanel(
        string title,
        TextBox textBox,
        string sectionTitle,
        string helperText,
        string tooltip,
        string buttonText,
        Func<Task> testAsync)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var sectionLabel = CreateSectionLabel(sectionTitle);
        var label = new Label
        {
            Text = title,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4)
        };
        var helperLabel = CreateMutedLabel(helperText);
        helperLabel.Padding = new Padding(0, 0, 0, 4);
        textBox.Dock = DockStyle.Fill;
        var testButton = CreateButton(buttonText);
        testButton.Margin = new Padding(0, 6, 0, 0);
        testButton.Click += async (_, _) => await testAsync();
        _toolTips.SetToolTip(label, tooltip);
        _toolTips.SetToolTip(textBox, tooltip);
        _toolTips.SetToolTip(testButton, "Scan currently connected devices and report which rows match the current rules.");
        panel.Controls.Add(sectionLabel, 0, 0);
        panel.Controls.Add(label, 0, 1);
        panel.Controls.Add(helperLabel, 0, 2);
        panel.Controls.Add(textBox, 0, 3);
        panel.Controls.Add(testButton, 0, 4);
        return panel;
    }

    private async Task TestDetectorRulesAsync(string detectorName, string[][] rules)
    {
        try
        {
            if (rules.Length == 0)
            {
                SetStatus($"{detectorName} detector rules are empty.");
                ShowThemedMessageBox($"No devices matched the current {detectorName} detector rules because the rule list is empty.", $"{detectorName} detector test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetActiveOperationStatus($"Testing {detectorName} detector rules...");
            var timeoutSeconds = _numberInputs.TryGetValue("DeviceProbeTimeoutSeconds", out var timeoutInput)
                ? (int)timeoutInput.Value
                : 10;
            var output = await RunProcessForOutputAsync(
                "pnputil.exe",
                "/enum-devices /connected",
                TimeSpan.FromSeconds(timeoutSeconds),
                CancellationToken.None);
            var matches = SplitDeviceBlocks(output)
                .Where(block => rules.Any(rule => DetectorRuleMatchesBlock(rule, block)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (matches.Length == 0)
            {
                SetStatus($"No devices matched the current {detectorName} detector rules.");
                ShowThemedMessageBox($"No devices matched the current {detectorName} detector rules.", $"{detectorName} detector test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetStatus($"{detectorName} detector test matched {matches.Length} device block(s).");
            ShowDetectorTestDialog(detectorName, matches);
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex.Message, $"{detectorName} detector test failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus($"{detectorName} detector test failed.");
        }
    }

    private static string[] SplitDeviceBlocks(string pnputilOutput)
        => Regex
            .Split(pnputilOutput.Trim(), @"(?:\r?\n){2,}")
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Select(block => block.Trim())
            .ToArray();

    private static bool DetectorRuleMatchesBlock(string[] rule, string deviceBlock)
    {
        var keywords = rule.Where(keyword => !string.IsNullOrWhiteSpace(keyword)).ToArray();
        var normalizedDeviceBlock = deviceBlock.ToLowerInvariant();
        return keywords.Length > 0 && keywords.All(keyword => normalizedDeviceBlock.Contains(keyword.ToLowerInvariant(), StringComparison.Ordinal));
    }

    private void ShowDetectorTestDialog(string detectorName, string[] matches)
    {
        using var dialog = new Form
        {
            Text = $"{detectorName} detector test",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(760, 520),
            KeyPreview = true
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 16, 18, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var summary = new Label
        {
            Text = $"{detectorName} detector test matched {matches.Length} device block(s).",
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 9.5F, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 10)
        };
        var detailsText = FormatDetectorMatches(matches);
        var details = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9F),
            Text = detailsText,
            HideSelection = true
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 20, 0, 0)
        };
        var closeButton = CreateButton("Close", autoSize: false, width: 86, dialogResult: DialogResult.OK);
        var copyButton = CreateButton("Copy", autoSize: false, width: 86);
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(detailsText);
                SetStatus($"{detectorName} detector test details copied.");
            }
            catch (Exception ex)
            {
                ShowThemedMessageBox(ex.Message, "Could not copy detector details", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(copyButton);
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(details, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(root);
        dialog.AcceptButton = closeButton;
        dialog.CancelButton = closeButton;
        dialog.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            }
        };
        ApplyThemeTo(dialog);
        WindowsTitleBar.ApplyTheme(dialog.Handle, _theme.IsDark);
        dialog.Shown += (_, _) =>
        {
            details.SelectionLength = 0;
            closeButton.Focus();
        };
        dialog.ShowDialog(this);
    }

    private static string FormatDetectorMatches(string[] matches)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < matches.Length; index++)
        {
            if (index > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine($"Match {index + 1}");
            builder.AppendLine("Device: " + GetDeviceField(matches[index], "Device Description", "Description", "Device"));
            builder.AppendLine("Class: " + GetDeviceField(matches[index], "Class Name", "Class"));
            builder.AppendLine("Manufacturer: " + GetDeviceField(matches[index], "Manufacturer Name", "Manufacturer"));
            builder.AppendLine("Status: " + GetDeviceField(matches[index], "Status"));
            builder.AppendLine("Instance ID:");
            builder.AppendLine(GetDeviceField(matches[index], "Instance ID", "InstanceId"));
        }

        return builder.ToString();
    }

    private static string GetDeviceField(string block, params string[] names)
    {
        foreach (var line in block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var label = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (names.Any(name => string.Equals(label, name, StringComparison.OrdinalIgnoreCase)))
            {
                return string.IsNullOrWhiteSpace(value) ? "-" : value;
            }
        }

        return "-";
    }

    private static async Task<string> RunProcessForOutputAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        await process.WaitForExitAsync(timeoutSource.Token);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"{fileName} exited with code {process.ExitCode}." : error.Trim());
        }

        return output;
    }

    private Control BuildTimingTab()
    {
        var layout = CreateFormLayout(2);
        AddSectionHeader(layout, "Reconnect detection");
        AddNumber(layout, "PimaxServiceLogReconnectLookbackLines", "Pimax log scan depth", 1, 100000, "Number of recent PiService log lines scanned each polling cycle.", "lines");
        AddNumber(layout, "PollIntervalSeconds", "Supervisor check interval", 1, 3600, "How often the supervisor checks device and watched-process state.", "seconds");
        AddNumber(layout, "RestartDelayAfterReconnectSeconds", "Restart delay after reconnect", 0, 3600, "Seconds to wait after Pimax reconnects before restarting managed apps.", "seconds");

        AddSectionHeader(layout, "Startup verification");
        AddNumber(layout, "StartupTimeoutSeconds", "Startup timeout", 1, 3600, "Maximum number of seconds to wait for launched apps to appear before startup is considered failed.", "seconds");
        AddNumber(layout, "StartupStableSeconds", "Required stable time", 0, 3600, "Seconds an app must remain running before startup verification succeeds.", "seconds");
        AddNumber(layout, "DelayBeforeVrcFaceTrackingSeconds", "Delay before VRCFaceTracking", 0, 3600, "Seconds to wait after starting Broken Eye before starting VRCFaceTracking.", "seconds");

        AddSectionHeader(layout, "Crash and shutdown behavior");
        AddNumber(layout, "WatchedProcessCrashRelaunchGraceSeconds", "Crash relaunch grace period", 0, 86400, "If VRChat exits with a likely crash code, seconds to wait for it to relaunch before cleanup.", "seconds");
        AddNumber(layout, "ShutdownGraceSeconds", "Shutdown grace period", 0, 3600, "Seconds to wait for graceful app shutdown before force-closing process trees.", "seconds");

        AddSectionHeader(layout, "Device probing");
        AddNumber(layout, "DeviceProbeTimeoutSeconds", "Device probe timeout", 1, 3600, "Maximum seconds to wait for the Windows device query command.", "seconds");
        return BuildTabWithDescription(
            "Timing and recovery behavior",
            "Controls startup waits, reconnect delays, crash recovery, shutdown grace periods, and device probing.",
            layout,
            limitWidth: true);
    }

    private Control BuildRawJsonTab()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var warning = CreateMutedLabel("Advanced: edits here directly change the full configuration used by all tabs.");
        warning.Padding = new Padding(0, 0, 0, 8);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8)
        };
        var applyButton = CreateButton("Apply JSON to editor");
        applyButton.Click += (_, _) => ApplyRawJsonToEditor();
        _toolTips.SetToolTip(applyButton, "Parse the Raw JSON text and update all other tabs with those values.");
        buttons.Controls.Add(applyButton);

        var revertButton = CreateButton("Revert JSON changes");
        revertButton.Click += (_, _) => RevertRawJsonChanges();
        _toolTips.SetToolTip(revertButton, "Discard unapplied Raw JSON edits and restore JSON from the current editor values.");
        buttons.Controls.Add(revertButton);

        var formatButton = CreateButton("Format JSON");
        formatButton.Click += (_, _) => FormatRawJson();
        _toolTips.SetToolTip(formatButton, "Pretty-print the Raw JSON text after validating it.");
        buttons.Controls.Add(formatButton);

        _rawJsonTextBox.Dock = DockStyle.Fill;
        layout.Controls.Add(warning, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(_rawJsonTextBox, 0, 2);
        layout.Controls.Add(_rawJsonValidationLabel, 0, 3);

        return BuildTabWithDescription(
            "Raw configuration",
            "Advanced editor for the full JSON config. Changes here affect all tabs.",
            layout);
    }

    private Control BuildFooter()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 5,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var validateButton = CreateButton("Validate");
        validateButton.Click += (_, _) => ValidateConfigFromButton();
        _toolTips.SetToolTip(validateButton, "Validate (Ctrl+Shift+V). Validate the currently loaded editor values without saving.");

        var launchButton = CreateButton("Launch Supervisor");
        launchButton.Click += (_, _) => LaunchSupervisor();
        launchButton.FlatStyle = FlatStyle.Flat;
        _toolTips.SetToolTip(launchButton, "Launch Supervisor (Ctrl+L). Starts the console supervisor using the currently selected config file. Save changes first if you want the launched supervisor to use them.");

        var saveButton = CreateButton("Save");
        saveButton.Tag = "Primary";
        saveButton.Click += (_, _) => SaveConfig();
        _toolTips.SetToolTip(saveButton, "Save (Ctrl+S). Save the current editor values into the selected config file.");

        var saveAsButton = CreateButton("Save As...");
        saveAsButton.Click += (_, _) => SaveConfigAs();
        _toolTips.SetToolTip(saveAsButton, "Save As (Ctrl+Shift+S). Choose a different JSON file path, then save the current editor values.");

        layout.Controls.Add(_statusLabel, 0, 0);
        layout.Controls.Add(validateButton, 1, 0);
        layout.Controls.Add(launchButton, 2, 0);
        layout.Controls.Add(saveAsButton, 3, 0);
        layout.Controls.Add(saveButton, 4, 0);
        return layout;
    }

    private Control BuildTabWithDescription(string title, string description, Control content, bool limitWidth = false)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var descriptionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 0, 10)
        };
        descriptionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        descriptionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        descriptionPanel.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 9F, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 2)
        }, 0, 0);
        descriptionPanel.Controls.Add(CreateMutedLabel(description), 0, 1);

        if (limitWidth)
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            content.Dock = DockStyle.Top;
            content.AutoSize = true;
            content.MaximumSize = new Size(960, 0);
            if (content is TableLayoutPanel tableLayout)
            {
                tableLayout.AutoSize = true;
                tableLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            }

            host.Controls.Add(content);
            content = host;
        }

        root.Controls.Add(descriptionPanel, 0, 0);
        root.Controls.Add(content, 0, 1);
        return root;
    }

    private static TableLayoutPanel CreateFormLayout(int columns, bool compact = false)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = columns,
            Padding = compact ? new Padding(6) : new Padding(8),
            Tag = compact ? "CompactForm" : null
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, compact ? 220 : 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (columns > 2)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, compact ? PathStatusColumnWidth - 8 : PathStatusColumnWidth));
        }

        return layout;
    }

    private void AddPathRow(
        TableLayoutPanel layout,
        string label,
        TextBox textBox,
        string tooltip,
        string? suggestedDirectory = null,
        string? configKey = null,
        string? defaultFileName = null)
    {
        var browseButton = CreateButton("Browse...", autoSize: false, width: 104, height: 27);
        browseButton.Click += (_, _) =>
        {
            var browseFileName = string.IsNullOrWhiteSpace(Path.GetFileName(textBox.Text))
                ? defaultFileName ?? label
                : Path.GetFileName(textBox.Text);
            using var dialog = new OpenFileDialog
            {
                Title = "Select " + label,
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                FileName = browseFileName
            };

            var expandedTextPath = ExpandPath(textBox.Text);
            if (File.Exists(expandedTextPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(expandedTextPath);
            }
            else if (!string.IsNullOrWhiteSpace(suggestedDirectory) && Directory.Exists(suggestedDirectory))
            {
                dialog.InitialDirectory = suggestedDirectory;
            }

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                textBox.Text = dialog.FileName;
                RefreshPathIndicators();
            }
        };

        var labelControl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left };
        var statusLabel = CreatePathStatusLabel();
        var extraPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        extraPanel.Controls.Add(browseButton);
        extraPanel.Controls.Add(statusLabel);

        void UpdatePathStatus()
        {
            var currentTooltip = BuildPathTooltip(tooltip, textBox.Text, configKey);
            _toolTips.SetToolTip(labelControl, currentTooltip);
            _toolTips.SetToolTip(textBox, currentTooltip);
            _toolTips.SetToolTip(browseButton, currentTooltip);
            _toolTips.SetToolTip(statusLabel, currentTooltip);
            UpdatePathIndicator(statusLabel, textBox.Text, File.Exists);
        }

        AddControlRow(layout, labelControl, textBox, extraPanel, BuildPathTooltip(tooltip, textBox.Text, configKey));
        textBox.TextChanged += (_, _) =>
        {
            UpdatePathStatus();
        };
        _pathIndicatorRefreshers.Add(UpdatePathStatus);
        UpdatePathStatus();
    }

    private void AddFolderValidationRow(TableLayoutPanel layout, string label, TextBox textBox, string tooltip)
    {
        var browseButton = CreateButton("Browse...", autoSize: false, width: 104, height: 27);
        browseButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select " + label,
                UseDescriptionForTitle = true
            };

            var expandedTextPath = ExpandPath(textBox.Text);
            if (Directory.Exists(expandedTextPath))
            {
                dialog.SelectedPath = expandedTextPath;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                textBox.Text = dialog.SelectedPath;
                RefreshPathIndicators();
            }
        };

        var labelControl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left };
        var statusLabel = CreatePathStatusLabel();
        var extraPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        extraPanel.Controls.Add(browseButton);
        extraPanel.Controls.Add(statusLabel);

        void UpdatePathStatus()
        {
            var currentTooltip = BuildPathTooltip(tooltip, textBox.Text, null);
            _toolTips.SetToolTip(labelControl, currentTooltip);
            _toolTips.SetToolTip(textBox, currentTooltip);
            _toolTips.SetToolTip(browseButton, currentTooltip);
            _toolTips.SetToolTip(statusLabel, currentTooltip);
            UpdatePathIndicator(statusLabel, textBox.Text, Directory.Exists);
        }

        AddControlRow(layout, labelControl, textBox, extraPanel, tooltip);
        textBox.TextChanged += (_, _) => UpdatePathStatus();
        _pathIndicatorRefreshers.Add(UpdatePathStatus);
        UpdatePathStatus();
    }

    private static Label CreatePathStatusLabel() => new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(8, 5, 0, 0),
        MinimumSize = new Size(92, 0),
        Tag = "PathStatus"
    };

    private void UpdatePathIndicator(Label statusLabel, string pathText, Func<string, bool> exists)
    {
        var path = ExpandPath(pathText);
        if (string.IsNullOrWhiteSpace(path))
        {
            statusLabel.Text = "";
            statusLabel.ForeColor = _theme.Text;
            return;
        }

        var found = exists(path);
        statusLabel.Text = found ? "✓ Found" : "⚠ Not found";
        statusLabel.ForeColor = found
            ? (_theme.IsDark ? Color.FromArgb(147, 197, 153) : Color.FromArgb(35, 118, 61))
            : (_theme.IsDark ? Color.FromArgb(231, 170, 117) : Color.FromArgb(158, 82, 28));
    }

    private void RefreshPathIndicators()
    {
        foreach (var refresh in _pathIndicatorRefreshers)
        {
            refresh();
        }
    }

    private void ConfigureDynamicGridSizing(DataGridView grid)
    {
        grid.Dock = DockStyle.Top;
        grid.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        grid.ScrollBars = ScrollBars.Both;
        grid.MinimumSize = new Size(0, CalculateGridHeight(grid, MinimumGridVisibleRows));
        grid.Height = grid.MinimumSize.Height;

        grid.RowsAdded += (_, _) => UpdateDynamicGridHeight(grid);
        grid.RowsRemoved += (_, _) => UpdateDynamicGridHeight(grid);
        grid.RowHeightChanged += (_, _) => UpdateDynamicGridHeight(grid);
        grid.ColumnHeadersHeightChanged += (_, _) => UpdateDynamicGridHeight(grid);
        grid.HandleCreated += (_, _) => UpdateDynamicGridHeight(grid);
        grid.ParentChanged += (_, _) =>
        {
            if (grid.Parent is Control parent)
            {
                parent.SizeChanged += (_, _) => UpdateDynamicGridHeight(grid);
                UpdateDynamicGridHeight(grid);
            }
        };
    }

    private void UpdateDynamicGridHeight(DataGridView grid)
    {
        if (grid.IsDisposed)
        {
            return;
        }

        var minimumHeight = CalculateGridHeight(grid, MinimumGridVisibleRows);
        var rowCount = grid.Rows
            .Cast<DataGridViewRow>()
            .Count(row => !row.IsNewRow);
        var desiredHeight = CalculateGridHeight(grid, Math.Max(MinimumGridVisibleRows, rowCount));
        if (grid.Parent is Control parent)
        {
            var availableHeight = parent.ClientSize.Height - grid.Top - parent.Padding.Bottom;
            if (availableHeight > 0)
            {
                desiredHeight = Math.Min(desiredHeight, Math.Max(minimumHeight, availableHeight));
            }
        }

        grid.MinimumSize = new Size(0, minimumHeight);
        if (grid.Height != desiredHeight)
        {
            grid.Height = desiredHeight;
        }
    }

    private static int CalculateGridHeight(DataGridView grid, int visibleRows)
    {
        var rowHeight = grid.RowTemplate.Height > 0
            ? grid.RowTemplate.Height
            : grid.Font.Height + 8;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.IsNewRow)
            {
                rowHeight = Math.Max(rowHeight, row.Height);
                break;
            }
        }

        var headerHeight = grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0;
        var borderHeight = grid.BorderStyle == BorderStyle.None ? 0 : SystemInformation.BorderSize.Height * 2;
        return headerHeight
            + (rowHeight * visibleRows)
            + SystemInformation.HorizontalScrollBarHeight
            + borderHeight
            + 4;
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

    private void AddSectionHeader(TableLayoutPanel layout, string text)
    {
        AddFullWidth(layout, CreateSectionLabel(text, Equals(layout.Tag, "CompactForm")), text);
    }

    private static Label CreateSectionLabel(string text, bool compact = false) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(FontFamily.GenericSansSerif, 9.5F, FontStyle.Bold),
        Padding = compact ? new Padding(0, 10, 0, 3) : new Padding(0, 16, 0, 5),
        Tag = "Section"
    };

    private static Label CreateMutedLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        MaximumSize = new Size(900, 0),
        Tag = "Muted"
    };

    private void AddNumber(
        TableLayoutPanel layout,
        string propertyName,
        string label,
        int minimum,
        int maximum,
        string tooltip,
        string unit)
    {
        var input = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Anchor = AnchorStyles.Left,
            Width = CompactNumberInputWidth
        };
        _numberInputs[propertyName] = input;
        _numberLabels[propertyName] = label;
        _numberUnits[propertyName] = unit;
        var inputPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        inputPanel.Controls.Add(input);
        inputPanel.Controls.Add(new Label
        {
            Text = unit,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(6, 5, 0, 0)
        });
        AddLabeledRow(layout, label, inputPanel, ToolTipWithConfigKey(tooltip, propertyName));
    }

    private static string ToolTipWithConfigKey(string tooltip, string configKey)
        => $"{tooltip}\r\n\r\nConfig key: {configKey}";

    private static string BuildPathTooltip(string tooltip, string pathText, string? configKey)
    {
        var builder = new StringBuilder(tooltip);
        var rawPath = pathText.Trim();
        if (!string.IsNullOrWhiteSpace(rawPath))
        {
            var expandedPath = ExpandPath(rawPath);
            if (!string.Equals(rawPath, expandedPath, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append("\r\n\r\nExpanded path:\r\n");
                builder.Append(expandedPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(configKey))
        {
            builder.Append("\r\n\r\nConfig key: ");
            builder.Append(configKey);
        }

        return builder.ToString();
    }

    private static string ExpandPath(string path)
    {
        try
        {
            return Environment.ExpandEnvironmentVariables(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string GetDefaultConfigPath()
        => Path.Combine(AppContext.BaseDirectory, DefaultConfigFileName);

    private static string GetDefaultConfigTemplateJson()
    {
        using var stream = typeof(ConfigEditorForm).Assembly.GetManifestResourceStream(DefaultConfigResourceName)
            ?? throw new InvalidOperationException("The default supervisor config template is not embedded in the config editor build.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();
        return string.IsNullOrWhiteSpace(json) ? "{\r\n}\r\n" : json;
    }

    private static void EnsureDefaultConfigFile(string path)
    {
        if (File.Exists(path))
        {
            return;
        }

        WriteDefaultConfigFile(path);
    }

    private static void WriteDefaultConfigFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, GetDefaultConfigTemplateJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void SaveActiveConfigSelection(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var appDirectory = Path.GetFullPath(AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var configDirectory = Path.GetDirectoryName(fullPath)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var value = string.Equals(appDirectory, configDirectory, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(fullPath)
                : fullPath;
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, ActiveConfigSelectionFileName),
                value,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Direct launch falls back to supervisor.config.json if this convenience marker cannot be written.
        }
    }

    private void RefreshConfigSelector()
    {
        _suppressConfigSelectorChange = true;
        var beganUpdate = false;
        try
        {
            var currentPath = TryGetFullPath(_configPathTextBox.Text.Trim());
            var items = GetConfigProfileItems().ToArray();

            _configSelectorComboBox.BeginUpdate();
            beganUpdate = true;
            _configSelectorComboBox.Items.Clear();
            foreach (var item in items)
            {
                _configSelectorComboBox.Items.Add(item);
            }

            var selectedIndex = currentPath is null
                ? -1
                : Array.FindIndex(items, item => PathsEqual(item.Path, currentPath));
            UpdateConfigSelectorWidth(items);
            _configSelectorComboBox.SelectedIndex = selectedIndex;
            _configSelectorComboBox.Enabled = items.Length > 0;
            _configSelectorComboBox.EndUpdate();
            beganUpdate = false;
            UpdateConfigSelectorTooltip();
        }
        catch
        {
            if (beganUpdate)
            {
                _configSelectorComboBox.EndUpdate();
            }
            _configSelectorComboBox.Items.Clear();
            _configSelectorComboBox.Enabled = false;
            _configSelectorComboBox.Width = MinimumConfigSelectorWidth;
            _toolTips.SetToolTip(_configSelectorComboBox, "Could not read named configs from the app folder.");
        }
        finally
        {
            _suppressConfigSelectorChange = false;
        }
    }

    private void UpdateConfigSelectorWidth(IReadOnlyCollection<ConfigProfileItem> items)
    {
        var widestText = items.Count == 0
            ? MinimumConfigSelectorWidth
            : items.Max(item => TextRenderer.MeasureText(item.DisplayName, _configSelectorComboBox.Font).Width);
        var desiredWidth = widestText + SystemInformation.VerticalScrollBarWidth + 36;
        var availableWidth = Math.Max(MinimumConfigSelectorWidth, ClientSize.Width - 430);
        _configSelectorComboBox.Width = Math.Clamp(
            desiredWidth,
            MinimumConfigSelectorWidth,
            Math.Min(MaximumConfigSelectorWidth, availableWidth));
        _configSelectorComboBox.DropDownWidth = Math.Max(_configSelectorComboBox.Width, desiredWidth);
    }

    private IEnumerable<ConfigProfileItem> GetConfigProfileItems()
    {
        var directory = AppContext.BaseDirectory;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, "*.config.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var displayName = ReadConfigDisplayName(path);
                return new ConfigProfileItem(
                    string.IsNullOrWhiteSpace(displayName) ? fileName : displayName.Trim(),
                    fileName,
                    Path.GetFullPath(path));
            })
            .ToArray();
    }

    private static string ReadConfigDisplayName(string path)
    {
        try
        {
            return GetString(ParseJson(File.ReadAllText(path)), "DisplayName");
        }
        catch
        {
            return "";
        }
    }

    private void LoadSelectedConfigFromDropdown()
    {
        if (_suppressConfigSelectorChange || _configSelectorComboBox.SelectedItem is not ConfigProfileItem item)
        {
            return;
        }

        var currentPath = TryGetFullPath(_configPathTextBox.Text.Trim());
        if (currentPath is not null && PathsEqual(currentPath, item.Path))
        {
            UpdateConfigSelectorTooltip();
            return;
        }

        if (!ConfirmUnsavedChangesBefore("loading another config"))
        {
            RefreshConfigSelector();
            return;
        }

        _configPathTextBox.Text = item.Path;
        LoadConfig(item.Path);
    }

    private void DeleteSelectedConfig()
    {
        if (_configSelectorComboBox.SelectedItem is not ConfigProfileItem item)
        {
            ShowThemedMessageBox("Select a config to delete.", "Delete config", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus("No config selected.");
            return;
        }

        var result = ShowThemedMessageBox(
            "Do you really wish to delete the config and its name?",
            "Delete config",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var deletedCurrentConfig = PathsEqual(_configPathTextBox.Text.Trim(), item.Path);
            File.Delete(item.Path);

            if (deletedCurrentConfig)
            {
                var nextConfig = GetConfigProfileItems().FirstOrDefault(config => !PathsEqual(config.Path, item.Path));
                if (nextConfig is not null)
                {
                    _configPathTextBox.Text = nextConfig.Path;
                    LoadConfig(nextConfig.Path);
                }
                else
                {
                    var defaultConfigPath = GetDefaultConfigPath();
                    WriteDefaultConfigFile(defaultConfigPath);
                    _configPathTextBox.Text = defaultConfigPath;
                    LoadConfig(defaultConfigPath);
                }
            }
            else
            {
                RefreshConfigSelector();
            }

            SetTemporaryStatus($"Deleted config {item.FileName}.");
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex.Message, "Could not delete config", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Delete config failed.");
        }
    }

    private void UpdateConfigSelectorTooltip()
    {
        var tooltip = _configSelectorComboBox.SelectedItem is ConfigProfileItem item
            ? $"Config: {item.DisplayName}\r\nFile: {item.FileName}\r\nPath:\r\n{item.Path}"
            : "Select a named *.config.json file from the app folder.";
        _toolTips.SetToolTip(_configSelectorComboBox, tooltip);
    }

    private static string GetFallbackDisplayName(string path)
    {
        var fileName = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? "supervisor.config.json" : fileName;
    }

    private static bool PathsEqual(string first, string second)
        => string.Equals(
            TryGetFullPath(first) ?? first,
            TryGetFullPath(second) ?? second,
            StringComparison.OrdinalIgnoreCase);

    private void BrowseConfig()
    {
        if (!ConfirmUnsavedChangesBefore("loading another config"))
        {
            return;
        }

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

    private void ReloadConfig()
    {
        if (ConfirmUnsavedChangesBefore("reloading"))
        {
            LoadConfig(_configPathTextBox.Text);
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

    private void RestoreBackup()
    {
        var backups = GetBackupFileItems().ToArray();
        if (backups.Length == 0)
        {
            ShowThemedMessageBox("No .bak backups were found in the app folder.", "Restore backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus("No backups found.");
            return;
        }

        if (!ConfirmUnsavedChangesBefore("restoring a backup"))
        {
            return;
        }

        var selectedBackup = ShowBackupSelectionDialog(backups);
        if (selectedBackup is null)
        {
            return;
        }

        var defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "supervisor.config.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(defaultConfigPath)!);
            var preRestoreBackupPath = CreateConfigBackup(defaultConfigPath);
            File.Copy(selectedBackup.Path, defaultConfigPath, overwrite: true);
            _configPathTextBox.Text = defaultConfigPath;
            LoadConfig(defaultConfigPath);
            var tooltip = preRestoreBackupPath is null
                ? "Restored from:\r\n" + selectedBackup.Path
                : "Restored from:\r\n" + selectedBackup.Path + "\r\n\r\nPrevious default config backup:\r\n" + preRestoreBackupPath;
            SetTemporaryStatus("Restored backup to supervisor.config.json.", tooltip);
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex.Message, "Could not restore backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Backup restore failed.");
        }
    }

    private IEnumerable<BackupFileItem> GetBackupFileItems()
    {
        var directory = AppContext.BaseDirectory;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, "*.bak", SearchOption.TopDirectoryOnly)
            .Select(path => new BackupFileItem(
                Path.GetFileName(path),
                Path.GetFullPath(path),
                File.GetLastWriteTime(path)))
            .OrderByDescending(item => item.LastWriteTime)
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private BackupFileItem? ShowBackupSelectionDialog(IReadOnlyList<BackupFileItem> backups)
    {
        using var dialog = new Form
        {
            Text = "Restore backup",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(560, 152),
            ShowInTaskbar = false
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = CreateMutedLabel("Choose a backup to restore into the app folder's supervisor.config.json.");
        label.Margin = new Padding(0, 0, 0, 8);

        var comboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 12)
        };
        foreach (var backup in backups)
        {
            comboBox.Items.Add(backup);
        }
        comboBox.SelectedIndex = 0;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            WrapContents = false
        };
        var restoreButton = CreateButton("Restore", autoSize: false, width: 92, dialogResult: DialogResult.OK);
        restoreButton.Tag = "Primary";
        var cancelButton = CreateButton("Cancel", autoSize: false, width: 86, dialogResult: DialogResult.Cancel);
        buttons.Controls.Add(restoreButton);
        buttons.Controls.Add(cancelButton);

        root.Controls.Add(label, 0, 0);
        root.Controls.Add(comboBox, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(root);
        dialog.AcceptButton = restoreButton;
        dialog.CancelButton = cancelButton;
        ApplyThemeTo(dialog);
        WindowsTitleBar.ApplyTheme(dialog.Handle, _theme.IsDark);

        return dialog.ShowDialog(this) == DialogResult.OK
            ? comboBox.SelectedItem as BackupFileItem
            : null;
    }

    private void LaunchSupervisor()
    {
        if (!ValidateForLaunch())
        {
            return;
        }

        if (_hasUnsavedChanges)
        {
            var result = ShowLaunchUnsavedChangesDialog();
            if (result == LaunchUnsavedChoice.Cancel)
            {
                return;
            }

            if (result == LaunchUnsavedChoice.SaveAndLaunch && !SaveConfig())
            {
                return;
            }
        }

        var supervisorPath = Path.Combine(AppContext.BaseDirectory, "PimaxVrcSupervisor.exe");
        if (!File.Exists(supervisorPath))
        {
            ShowThemedMessageBox($"Could not find {supervisorPath}.", "Could not launch supervisor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var configPath = Path.GetFullPath(_configPathTextBox.Text.Trim());
            var startInfo = new ProcessStartInfo
            {
                FileName = supervisorPath,
                WorkingDirectory = Path.GetDirectoryName(supervisorPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
                ErrorDialog = true,
                ErrorDialogParentHandle = Handle
            };
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(configPath);
            if (!IsAdministrator())
            {
                startInfo.Verb = "runas";
            }

            Process.Start(startInfo);
            SetStatus("Launched Supervisor using " + Path.GetFileName(configPath) + ".");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            ShowThemedMessageBox(
                "Windows cancelled the administrator approval prompt for PimaxVrcSupervisor.exe. Click Launch again and approve the UAC prompt.",
                "Supervisor launch cancelled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            SetStatus("Launch cancelled.");
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex.Message, "Could not launch supervisor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Launch failed.");
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void LoadConfig(string path)
    {
        try
        {
            _suppressDirtyTracking = true;
            if (!File.Exists(path))
            {
                _loadedJson = GetDefaultConfigTemplateJson();
                _appliedRawJson = _loadedJson;
                _rawJsonTextBox.Text = _loadedJson;
                PopulateControls(ParseJson(_loadedJson));
                _loadedJson = BuildCurrentJson();
                _appliedRawJson = _loadedJson;
                _rawJsonTextBox.Text = _loadedJson;
                _rawJsonHasUnappliedChanges = false;
                _editorState.LastConfigPath = Path.GetFullPath(path);
                SetCleanStatus("Config file does not exist yet. Fill values, then Save.");
                return;
            }

            var loadedJson = File.ReadAllText(path);
            _rawJsonTextBox.Text = loadedJson;
            var node = ParseJson(loadedJson);
            PopulateControls(node);
            _loadedJson = BuildCurrentJson();
            _appliedRawJson = _loadedJson;
            _rawJsonTextBox.Text = _loadedJson;
            _rawJsonHasUnappliedChanges = false;
            _editorState.LastConfigPath = Path.GetFullPath(path);
            SaveActiveConfigSelection(path);
            SetCleanStatus("Loaded " + path);
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex.Message, "Could not load config", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Load failed.");
        }
        finally
        {
            ResetFirstRunPreferenceTouchTracking();
            _suppressDirtyTracking = false;
            ValidateRawJsonText(showStatus: false);
            RefreshConfigSelector();
            UpdateWindowTitle();
        }
    }

    private void PopulateControls(JsonNode? node)
    {
        _displayNameTextBox.Text = GetStringOrDefault(node, "DisplayName", GetFallbackDisplayName(_configPathTextBox.Text));
        _brokenEyePathTextBox.Text = GetString(node, "BrokenEyePath");
        _vrcFaceTrackingPathTextBox.Text = GetString(node, "VrcFaceTrackingPath");
        _intifacePathTextBox.Text = GetStringOrDefault(node, "IntifacePath", DefaultIntifacePath);
        _oscGoesBrrrPathTextBox.Text = GetStringOrFallbackOrDefault(node, "OscGoesBrrrPath", "OscGoesBrrrrPath", DefaultOscGoesBrrrPath);
        _brokenEyeStartMinimizedCheckBox.Checked = GetBool(node, "BrokenEyeStartMinimized", defaultValue: false);
        _vrcFaceTrackingStartMinimizedCheckBox.Checked = GetBool(node, "VrcFaceTrackingStartMinimized", defaultValue: false);
        _intifaceStartMinimizedCheckBox.Checked = GetBool(node, "IntifaceStartMinimized", defaultValue: false);
        _oscGoesBrrrStartMinimizedCheckBox.Checked = GetBool(node, "OscGoesBrrrStartMinimized", defaultValue: false);
        _oscGoesBrrrEnabledCheckBox.Checked = GetBoolOrFallback(node, "OscGoesBrrrEnabled", "LovenseAutoLaunchEnabled", defaultValue: false);
        _oscGoesBrrrHotkeyCheckBox.Checked = GetBool(node, "OscGoesBrrrHotkeyEnabled", defaultValue: true);
        _oscGoesBrrrBleScannerCheckBox.Checked = GetBool(node, "OscGoesBrrrBleScannerEnabled", defaultValue: false);
        _oscRouterEnabledCheckBox.Checked = GetBool(node, "OscRouterEnabled", defaultValue: false);
        _oscRouterReceivePortInput.Value = Math.Clamp(GetInt(node, "OscRouterReceivePort", 9001), (int)_oscRouterReceivePortInput.Minimum, (int)_oscRouterReceivePortInput.Maximum);
        _mouthTrackerCheckBox.Checked = GetBoolCheckState(node, "MouthTrackerUser") == CheckState.Checked;
        _turnOffMonitorsCheckBox.Checked = GetBoolCheckState(node, "TurnOffSecondaryMonitors") == CheckState.Checked;
        var startupLaunchMode = GetStartupLaunchMode(node);
        if (startupLaunchMode == "ScheduledTask")
        {
            _autoLaunchTaskCheckBox.CheckState = CheckState.Checked;
            _startWithSteamVrCheckBox.Checked = false;
        }
        else if (startupLaunchMode == "SteamVrManifest")
        {
            _autoLaunchTaskCheckBox.CheckState = CheckState.Unchecked;
            _startWithSteamVrCheckBox.Checked = true;
        }
        else if (startupLaunchMode == "None")
        {
            _autoLaunchTaskCheckBox.CheckState = CheckState.Unchecked;
            _startWithSteamVrCheckBox.Checked = false;
        }
        else
        {
            _autoLaunchTaskCheckBox.Checked = GetBoolCheckState(node, "AutoLaunchScheduledTask") == CheckState.Checked;
            _startWithSteamVrCheckBox.Checked = false;
        }
        _usePimaxLogCheckBox.Checked = GetBool(node, "UsePimaxServiceLogReconnectDetector", defaultValue: true);
        _useMouthTrackerPnPCheckBox.Checked = GetBool(node, "UseMouthTrackerPnPReconnectDetector", defaultValue: true);
        _pimaxServiceLogDirectoryTextBox.Text = GetString(node, "PimaxServiceLogDirectory");
        _baseStationsEnabledCheckBox.Checked = GetBool(node, "BaseStationsEnabled", defaultValue: false);
        _baseStationPowerDownModeComboBox.SelectedItem = GetStringOrDefault(node, "BaseStationPowerDownMode", BaseStationPowerDownMode.Sleep.ToString());
        if (_baseStationPowerDownModeComboBox.SelectedIndex < 0)
        {
            _baseStationPowerDownModeComboBox.SelectedItem = BaseStationPowerDownMode.Sleep.ToString();
        }

        PopulateBaseStationsGrid(GetBaseStations(node));
        PopulateAutoLaunchAppsGrid(GetAutoLaunchApps(node));
        PopulateOscRoutesGrid(GetOscRoutes(node));
        _brokenEyeProcessesTextBox.Text = string.Join(", ", GetStringArray(node, "BrokenEyeProcessNames"));
        _vrcFaceTrackingProcessesTextBox.Text = string.Join(", ", GetStringArray(node, "VrcFaceTrackingProcessNames"));
        _intifaceProcessesTextBox.Text = string.Join(", ", GetStringArrayOrDefault(node, "IntifaceProcessNames", ["intiface_central.exe"]));
        _oscGoesBrrrProcessesTextBox.Text = string.Join(", ", GetStringArrayOrDefault(node, "OscGoesBrrrProcessNames", ["OscGoesBrrr.exe"], "OscGoesBrrrrProcessNames"));
        _watchedShutdownProcessesTextBox.Text = string.Join(", ", GetStringArray(node, "WatchedShutdownProcessNames"));
        _steamVrServerProcessesTextBox.Text = string.Join(", ", GetStringArray(node, "SteamVrServerProcessNames"));
        _pimaxDetectorsTextBox.Text = FormatStringMatrix(GetStringMatrix(node, "PimaxDetectors"));
        _mouthTrackerDetectorsTextBox.Text = FormatStringMatrix(GetStringMatrix(node, "MouthTrackerDetectors"));
        _lovenseDetectorsTextBox.Text = FormatStringMatrix(GetStringMatrixOrDefault(node, "LovenseDetectors", DefaultLovenseDetectors));

        foreach (var (propertyName, input) in _numberInputs)
        {
            var defaultValue = propertyName switch
            {
                "DelayBeforeOscGoesBrrrSeconds" => 5,
                "OscGoesBrrrBleScanSeconds" => 30,
                "OscGoesBrrrBleScanIntervalSeconds" => 60,
                _ => decimal.ToInt32(input.Minimum)
            };
            var value = propertyName == "DelayBeforeOscGoesBrrrSeconds"
                ? GetIntOrFallback(node, propertyName, "DelayBeforeOscGoesBrrrrSeconds", defaultValue)
                : GetInt(node, propertyName, defaultValue);
            input.Value = Math.Clamp(value, input.Minimum, input.Maximum);
        }
    }

    private bool SaveConfig(bool forceApplyStartupIntegration = false)
    {
        if (_saveInProgress)
        {
            SetStatus("Save is already running.");
            return false;
        }

        try
        {
            _saveInProgress = true;
            var path = _configPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Choose a config file path before saving.");
            }

            CommitAutoLaunchAppsGridEdits();
            CommitBaseStationsGridEdits();
            CommitOscRoutesGridEdits();
            var json = BuildCurrentJson();
            ParseJson(json);
            var shouldApplyStartupIntegration = forceApplyStartupIntegration || StartupIntegrationSelectionChanged(_loadedJson, json);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var backupPath = CreateConfigBackup(path);
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SaveActiveConfigSelection(path);
            _loadedJson = json;
            _appliedRawJson = json;
            _rawJsonHasUnappliedChanges = false;
            _suppressDirtyTracking = true;
            _rawJsonTextBox.Text = json;
            _suppressDirtyTracking = false;
            _editorState.LastConfigPath = Path.GetFullPath(path);
            RefreshConfigSelector();
            var savedStatus = backupPath is null
                ? $"Saved config at {DateTime.Now:HH:mm}."
                : $"Saved config and created backup at {DateTime.Now:HH:mm}.";
            SetCleanStatus(savedStatus, backupPath is null ? null : "Backup path:\r\n" + backupPath);
            ResetFirstRunPreferenceTouchTracking();
            if (shouldApplyStartupIntegration)
            {
                ApplyStartupIntegration(path);
            }
            return true;
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex.Message, "Could not save config", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Save failed.");
            return false;
        }
        finally
        {
            _saveInProgress = false;
        }
    }

    private string BuildCurrentJson()
    {
        CommitAutoLaunchAppsGridEdits();
        CommitBaseStationsGridEdits();
        CommitOscRoutesGridEdits();
        return ApplyControlValues(_appliedRawJson);
    }

    private static bool StartupIntegrationSelectionChanged(string previousJson, string currentJson)
    {
        try
        {
            var previous = ParseJson(previousJson);
            var current = ParseJson(currentJson);
            var currentMode = GetEffectiveStartupLaunchMode(current);
            if (!string.Equals(GetEffectiveStartupLaunchMode(previous), currentMode, StringComparison.Ordinal))
            {
                return true;
            }

            return currentMode == "SteamVrManifest"
                && !File.Exists(Path.Combine(AppContext.BaseDirectory, "PimaxVrcSupervisor.vrmanifest"));
        }
        catch
        {
            return true;
        }
    }

    private static string GetEffectiveStartupLaunchMode(JsonNode? node)
    {
        var startupLaunchMode = GetStartupLaunchMode(node);
        if (!string.IsNullOrWhiteSpace(startupLaunchMode))
        {
            return startupLaunchMode;
        }

        var autoLaunchTask = GetBoolCheckState(node, "AutoLaunchScheduledTask");
        return autoLaunchTask switch
        {
            CheckState.Checked => "ScheduledTask",
            CheckState.Unchecked => "None",
            _ => "Unspecified"
        };
    }

    private void ApplyStartupIntegration(string configPath)
    {
        if (_startupIntegrationApplyInProgress)
        {
            SetStatus("Startup integration apply is already running.");
            return;
        }

        var startupLaunchMode = GetSelectedStartupLaunchMode();
        if (startupLaunchMode == "Unspecified")
        {
            return;
        }

        var supervisorPath = Path.Combine(AppContext.BaseDirectory, "PimaxVrcSupervisor.exe");
        if (!File.Exists(supervisorPath))
        {
            SetStatus("Startup integration was saved but not applied because PimaxVrcSupervisor.exe was not found.");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = supervisorPath,
                WorkingDirectory = Path.GetDirectoryName(supervisorPath) ?? AppContext.BaseDirectory,
                UseShellExecute = !IsAdministrator(),
                ErrorDialog = true,
                ErrorDialogParentHandle = Handle
            };
            startInfo.ArgumentList.Add("--apply-startup-integration");
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(Path.GetFullPath(configPath));

            if (!IsAdministrator())
            {
                startInfo.Verb = "runas";
            }
            else
            {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.CreateNoWindow = true;
            }

            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PimaxVrcSupervisor.exe.");
            if (!IsAdministrator())
            {
                process.Dispose();
                SetStatus(startupLaunchMode switch
                {
                    "ScheduledTask" => "Saved config and requested Scheduled Task startup repair.",
                    "SteamVrManifest" => "Saved config and requested SteamVR startup repair.",
                    "None" => "Saved config and requested removal of managed startup tasks.",
                    _ => "Saved config and requested startup integration update."
                });
                return;
            }

            _startupIntegrationApplyInProgress = true;
            SetActiveOperationStatus("Applying startup integration...");
            _ = MonitorStartupIntegrationApplyAsync(process, startupLaunchMode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            SetStatus("Startup integration was saved but Windows cancelled the administrator approval prompt.");
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex.Message, "Could not apply startup integration", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Startup integration apply failed.");
        }
    }

    private async Task MonitorStartupIntegrationApplyAsync(Process process, string startupLaunchMode)
    {
        try
        {
            using (process)
            {
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var exited = await Task.Run(() => process.WaitForExit(30000));
                if (!exited)
                {
                    TryKillProcessTree(process);
                    await TryWaitForProcessOutputAsync(outputTask, errorTask);
                    RunOnUiThread(() =>
                    {
                        ShowThemedMessageBox(
                            "The configuration was saved, but startup integration did not finish within 30 seconds.\r\n\r\nPlease try Save again, or use Validate to check the scheduled task state.",
                            "Startup integration timed out",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        SetStatus("Startup integration timed out.");
                    });
                    return;
                }

                var output = await outputTask;
                var error = await errorTask;
                if (process.ExitCode != 0)
                {
                    var message = (error + output).Trim();
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                        ? $"Startup integration exited with code {process.ExitCode}."
                        : message);
                }
            }

            RunOnUiThread(() => SetStatus(startupLaunchMode switch
            {
                "ScheduledTask" => "Saved and applied Scheduled Task startup.",
                "SteamVrManifest" => "Saved and applied SteamVR startup manifest.",
                "None" => "Saved and disabled startup integrations.",
                _ => "Saved startup integration."
            }));
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                ShowThemedMessageBox(ex.Message, "Could not apply startup integration", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Startup integration apply failed.");
            });
        }
        finally
        {
            RunOnUiThread(() => _startupIntegrationApplyInProgress = false);
        }
    }

    private static async Task TryWaitForProcessOutputAsync(params Task<string>[] outputTasks)
    {
        try
        {
            await Task.WhenAll(outputTasks).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private async Task PromptForExistingStartupTaskMigrationAsync()
    {
        var currentReleaseFolder = GetCurrentConfigEditorDirectory();
        if ((_editorState.StartupTaskMigrationPromptedReleaseFolders ?? [])
            .Any(path => string.Equals(
                global::ScheduledTaskPathValidator.NormalizeDirectory(path),
                currentReleaseFolder,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        IReadOnlyList<ScheduledTaskExecutableValidationResult> staleTasks;
        try
        {
            staleTasks = await Task.Run(() => GetExistingStartupTasksOutsideCurrentRelease(currentReleaseFolder));
        }
        catch
        {
            return;
        }

        if (staleTasks.Count == 0)
        {
            return;
        }

        var choice = ShowStartupTaskMigrationDialog(staleTasks, currentReleaseFolder);
        _editorState.MarkStartupTaskMigrationPrompted(currentReleaseFolder);
        SaveEditorState();

        switch (choice)
        {
            case StartupTaskMigrationChoice.Rebind:
                SetStartupLaunchMode(SelectStartupLaunchModeForTasks(staleTasks));
                SaveConfig(forceApplyStartupIntegration: true);
                break;
            case StartupTaskMigrationChoice.TurnOffAutostart:
                SetStartupLaunchMode("None");
                SaveConfig(forceApplyStartupIntegration: true);
                break;
            case StartupTaskMigrationChoice.KeepExisting:
            default:
                SetStatus("Kept existing startup task configuration.");
                break;
        }
    }

    private static IReadOnlyList<ScheduledTaskExecutableValidationResult> GetExistingStartupTasksOutsideCurrentRelease(string currentReleaseFolder)
    {
        return global::ScheduledTaskPathValidator.ManagedTaskNames
            .Select(taskName => global::ScheduledTaskPathValidator.ValidateExistingTaskExecutable(taskName, currentReleaseFolder))
            .Where(result => result.Exists && !result.PointsToCurrentDirectory)
            .ToArray();
    }

    private static string SelectStartupLaunchModeForTasks(IReadOnlyList<ScheduledTaskExecutableValidationResult> tasks)
    {
        return tasks.Any(task => string.Equals(task.TaskName, global::ScheduledTaskPathValidator.SteamVrStartTaskName, StringComparison.OrdinalIgnoreCase))
            ? "SteamVrManifest"
            : "ScheduledTask";
    }

    private void SetStartupLaunchMode(string startupLaunchMode)
    {
        switch (startupLaunchMode)
        {
            case "ScheduledTask":
                _autoLaunchTaskCheckBox.CheckState = CheckState.Checked;
                _startWithSteamVrCheckBox.Checked = false;
                break;
            case "SteamVrManifest":
                _autoLaunchTaskCheckBox.CheckState = CheckState.Unchecked;
                _startWithSteamVrCheckBox.Checked = true;
                break;
            default:
                _autoLaunchTaskCheckBox.CheckState = CheckState.Unchecked;
                _startWithSteamVrCheckBox.Checked = false;
                break;
        }
    }

    private StartupTaskMigrationChoice ShowStartupTaskMigrationDialog(
        IReadOnlyList<ScheduledTaskExecutableValidationResult> staleTasks,
        string currentReleaseFolder)
    {
        var choice = StartupTaskMigrationChoice.KeepExisting;
        using var dialog = new Form
        {
            Text = "Startup option found",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(720, 390)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label
        {
            Text = "Autostart from another release was found",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };
        var body = new Label
        {
            Text = "A PimaxVrcSupervisor startup task points to another release folder.\r\n\r\n" +
                "Choose whether to rebind autostart to this release, keep the existing task, or turn off autostart and remove the managed startup tasks.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 76,
            Margin = new Padding(0, 0, 0, 12)
        };
        var details = new TextBox
        {
            Text = FormatStartupTaskMigrationDetails(staleTasks, currentReleaseFolder),
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, Font.Size),
            Margin = new Padding(0, 0, 0, 12)
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        var rebindButton = CreateButton("Yes, rebind", autoSize: false, width: 112, height: 28);
        var keepButton = CreateButton("No, keep as is", autoSize: false, width: 124, height: 28);
        var turnOffButton = CreateButton("Turn off autostart", autoSize: false, width: 148, height: 28);

        rebindButton.Click += (_, _) =>
        {
            choice = StartupTaskMigrationChoice.Rebind;
            dialog.DialogResult = DialogResult.Yes;
            dialog.Close();
        };
        keepButton.Click += (_, _) =>
        {
            choice = StartupTaskMigrationChoice.KeepExisting;
            dialog.DialogResult = DialogResult.No;
            dialog.Close();
        };
        turnOffButton.Click += (_, _) =>
        {
            choice = StartupTaskMigrationChoice.TurnOffAutostart;
            dialog.DialogResult = DialogResult.Ignore;
            dialog.Close();
        };

        buttons.Controls.Add(rebindButton);
        buttons.Controls.Add(keepButton);
        buttons.Controls.Add(turnOffButton);
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(details, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        dialog.Controls.Add(root);
        dialog.AcceptButton = rebindButton;
        dialog.CancelButton = keepButton;

        ApplyThemeTo(dialog);
        WindowsTitleBar.ApplyTheme(dialog.Handle, _theme.IsDark);
        dialog.ShowDialog(this);
        return choice;
    }

    private static string FormatStartupTaskMigrationDetails(
        IReadOnlyList<ScheduledTaskExecutableValidationResult> staleTasks,
        string currentReleaseFolder)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Current Config Editor folder:");
        builder.AppendLine(currentReleaseFolder);
        foreach (var task in staleTasks)
        {
            builder.AppendLine();
            builder.AppendLine("Scheduled task:");
            builder.AppendLine(task.TaskName);
            if (task.Issue is not null)
            {
                if (!string.IsNullOrWhiteSpace(task.Issue.TaskExecutableDirectory))
                {
                    builder.AppendLine("Task executable folder:");
                    builder.AppendLine(task.Issue.TaskExecutableDirectory);
                }

                if (!string.IsNullOrWhiteSpace(task.Issue.TaskExecutablePath))
                {
                    builder.AppendLine("Task executable:");
                    builder.AppendLine(task.Issue.TaskExecutablePath);
                }

                builder.AppendLine("Issue:");
                builder.AppendLine(task.Issue.Message);
            }
        }

        return builder.ToString();
    }

    private bool HasUnsavedChanges()
    {
        return _hasUnsavedChanges;
    }

    private bool ConfirmUnsavedChangesBefore(string action)
    {
        if (!HasUnsavedChanges())
        {
            return true;
        }

        var result = ShowThemedMessageBox(
            $"Save changes before {action}?",
            "Unsaved changes",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        return result switch
        {
            DialogResult.Yes => SaveConfig(),
            DialogResult.No => true,
            _ => false
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmUnsavedChangesBefore("exiting"))
        {
            e.Cancel = true;
            return;
        }

        SaveEditorState();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                SaveConfig();
                return true;
            case Keys.Control | Keys.Shift | Keys.S:
                SaveConfigAs();
                return true;
            case Keys.Control | Keys.O:
                BrowseConfig();
                return true;
            case Keys.F5:
            case Keys.Control | Keys.R:
                ReloadConfig();
                return true;
            case Keys.Control | Keys.L:
                LaunchSupervisor();
                return true;
            case Keys.Control | Keys.Shift | Keys.V:
                ValidateConfigFromButton();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RestoreWindowState()
    {
        if (_editorState.WindowBounds is not { Width: > 0, Height: > 0 } bounds)
        {
            return;
        }

        var visible = Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds));
        if (!visible)
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        WindowState = _editorState.WindowState is FormWindowState.Maximized
            ? FormWindowState.Maximized
            : FormWindowState.Normal;
    }

    private void SaveEditorState()
    {
        _editorState.LastConfigPath = TryGetFullPath(_configPathTextBox.Text.Trim()) ?? _editorState.LastConfigPath;
        _editorState.LastSelectedTab = _tabs.SelectedIndex;
        _editorState.WindowState = WindowState;
        _editorState.WindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _editorState.Save();
    }

    private static string? TryGetFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private string ApplyControlValues(string baseJson)
    {
        var json = string.IsNullOrWhiteSpace(baseJson) ? "{\r\n}\r\n" : baseJson;
        json = JsonPropertyEditor.Replace(json, "DisplayName", Serialize(_displayNameTextBox.Text.Trim()));
        json = JsonPropertyEditor.Replace(json, "BrokenEyePath", Serialize(_brokenEyePathTextBox.Text.Trim()));
        json = JsonPropertyEditor.Replace(json, "VrcFaceTrackingPath", Serialize(_vrcFaceTrackingPathTextBox.Text.Trim()));
        json = JsonPropertyEditor.Replace(json, "IntifacePath", Serialize(_intifacePathTextBox.Text.Trim()));
        json = JsonPropertyEditor.Replace(json, "OscGoesBrrrPath", Serialize(_oscGoesBrrrPathTextBox.Text.Trim()));
        json = JsonPropertyEditor.Replace(json, "BrokenEyeStartMinimized", _brokenEyeStartMinimizedCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "VrcFaceTrackingStartMinimized", _vrcFaceTrackingStartMinimizedCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "IntifaceStartMinimized", _intifaceStartMinimizedCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "OscGoesBrrrStartMinimized", _oscGoesBrrrStartMinimizedCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "OscGoesBrrrEnabled", _oscGoesBrrrEnabledCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "OscGoesBrrrHotkeyEnabled", _oscGoesBrrrHotkeyCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "OscGoesBrrrBleScannerEnabled", _oscGoesBrrrBleScannerCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "OscRouterEnabled", _oscRouterEnabledCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Remove(json, "OscRouterReceiveAddress");
        json = JsonPropertyEditor.Replace(json, "OscRouterReceivePort", ((int)_oscRouterReceivePortInput.Value).ToString());
        json = JsonPropertyEditor.Replace(json, "OscRoutes", Serialize(ReadOscRoutesGrid()));
        json = JsonPropertyEditor.Replace(json, "BaseStationsEnabled", _baseStationsEnabledCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "BaseStationPowerDownMode", Serialize(Convert.ToString(_baseStationPowerDownModeComboBox.SelectedItem) ?? BaseStationPowerDownMode.Sleep.ToString()));
        json = JsonPropertyEditor.Replace(json, "BaseStations", Serialize(ReadBaseStationsGrid()));
        json = JsonPropertyEditor.Replace(json, "AutoLaunchApps", Serialize(ReadAutoLaunchAppsGrid()));
        json = JsonPropertyEditor.Replace(json, "BrokenEyeProcessNames", Serialize(ParseStringList(_brokenEyeProcessesTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "VrcFaceTrackingProcessNames", Serialize(ParseStringList(_vrcFaceTrackingProcessesTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "IntifaceProcessNames", Serialize(ParseStringList(_intifaceProcessesTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "OscGoesBrrrProcessNames", Serialize(ParseStringList(_oscGoesBrrrProcessesTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "WatchedShutdownProcessNames", Serialize(ParseStringList(_watchedShutdownProcessesTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "SteamVrServerProcessNames", Serialize(ParseStringList(_steamVrServerProcessesTextBox.Text)));
        var baseNode = ParseJson(json);
        var firstRunBinaryPreferenceTouched = _mouthTrackerPreferenceTouched || _turnOffMonitorsPreferenceTouched;
        json = JsonPropertyEditor.Replace(
            json,
            "MouthTrackerUser",
            SerializeFirstRunPreferenceBool(
                baseNode,
                "MouthTrackerUser",
                _mouthTrackerCheckBox.Checked,
                _mouthTrackerPreferenceTouched || firstRunBinaryPreferenceTouched));
        json = JsonPropertyEditor.Replace(
            json,
            "TurnOffSecondaryMonitors",
            SerializeFirstRunPreferenceBool(
                baseNode,
                "TurnOffSecondaryMonitors",
                _turnOffMonitorsCheckBox.Checked,
                _turnOffMonitorsPreferenceTouched || firstRunBinaryPreferenceTouched));
        var startupLaunchMode = GetSelectedStartupLaunchModeForSave(baseNode);
        json = JsonPropertyEditor.Replace(json, "StartupLaunchMode", Serialize(startupLaunchMode));
        json = JsonPropertyEditor.Replace(json, "StopWithSteamVr", startupLaunchMode == "SteamVrManifest" ? "true" : "false");
        json = JsonPropertyEditor.Replace(
            json,
            "AutoLaunchScheduledTask",
            startupLaunchMode == "Unspecified"
                ? SerializeFirstRunPreferenceBool(baseNode, "AutoLaunchScheduledTask", currentValue: false, _startupIntegrationPreferenceTouched)
                : startupLaunchMode == "ScheduledTask" ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "PimaxDetectors", Serialize(ParseStringMatrix(_pimaxDetectorsTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "MouthTrackerDetectors", Serialize(ParseStringMatrix(_mouthTrackerDetectorsTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "LovenseDetectors", Serialize(ParseStringMatrix(_lovenseDetectorsTextBox.Text)));
        json = JsonPropertyEditor.Replace(json, "UsePimaxServiceLogReconnectDetector", _usePimaxLogCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "UseMouthTrackerPnPReconnectDetector", _useMouthTrackerPnPCheckBox.Checked ? "true" : "false");
        json = JsonPropertyEditor.Replace(json, "PimaxServiceLogDirectory", Serialize(_pimaxServiceLogDirectoryTextBox.Text.Trim()));

        foreach (var (propertyName, input) in _numberInputs)
        {
            json = JsonPropertyEditor.Replace(json, propertyName, ((int)input.Value).ToString());
        }

        return json;
    }

    private static string SerializeFirstRunPreferenceBool(JsonNode? baseNode, string propertyName, bool currentValue, bool touched)
    {
        if (currentValue || touched || GetBoolCheckState(baseNode, propertyName) != CheckState.Indeterminate)
        {
            return currentValue ? "true" : "false";
        }

        return "\"\"";
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
        return JsonSerializer.Serialize(value, JsonOptions());
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

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

    private static string GetStringOrDefault(JsonNode? node, string propertyName, string defaultValue)
    {
        var value = GetString(node, propertyName);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string GetStringOrFallback(JsonNode? node, string propertyName, string fallbackPropertyName)
    {
        var value = GetString(node, propertyName);
        return string.IsNullOrWhiteSpace(value) ? GetString(node, fallbackPropertyName) : value;
    }

    private static string GetStringOrFallbackOrDefault(JsonNode? node, string propertyName, string fallbackPropertyName, string defaultValue)
    {
        var value = GetStringOrFallback(node, propertyName, fallbackPropertyName);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string GetStartupLaunchMode(JsonNode? node)
    {
        var value = GetString(node, "StartupLaunchMode");
        return value is "None" or "ScheduledTask" or "SteamVrManifest" ? value : "";
    }

    private string GetSelectedStartupLaunchMode()
    {
        if (_startWithSteamVrCheckBox.Checked)
        {
            return "SteamVrManifest";
        }

        return _autoLaunchTaskCheckBox.CheckState switch
        {
            CheckState.Checked => "ScheduledTask",
            _ => "None"
        };
    }

    private string GetSelectedStartupLaunchModeForSave(JsonNode? baseNode)
    {
        if (_startWithSteamVrCheckBox.Checked)
        {
            return "SteamVrManifest";
        }

        if (_autoLaunchTaskCheckBox.CheckState == CheckState.Checked)
        {
            return "ScheduledTask";
        }

        return _startupIntegrationPreferenceTouched || StartupIntegrationConfigured(baseNode)
            ? "None"
            : "Unspecified";
    }

    private static bool StartupIntegrationConfigured(JsonNode? node)
        => !string.IsNullOrWhiteSpace(GetStartupLaunchMode(node))
            || GetBoolCheckState(node, "AutoLaunchScheduledTask") != CheckState.Indeterminate;

    private void ResetFirstRunPreferenceTouchTracking()
    {
        _mouthTrackerPreferenceTouched = false;
        _turnOffMonitorsPreferenceTouched = false;
        _startupIntegrationPreferenceTouched = false;
    }

    private static bool GetBool(JsonNode? node, string propertyName, bool defaultValue)
    {
        return node?[propertyName] is JsonValue value && value.TryGetValue<bool>(out var result)
            ? result
            : defaultValue;
    }

    private static bool GetBoolOrFallback(JsonNode? node, string propertyName, string fallbackPropertyName, bool defaultValue)
    {
        if (node?[propertyName] is JsonValue value && value.TryGetValue<bool>(out var result))
        {
            return result;
        }

        return GetBool(node, fallbackPropertyName, defaultValue);
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

    private static int GetIntOrFallback(JsonNode? node, string propertyName, string fallbackPropertyName, int defaultValue)
    {
        var value = GetInt(node, propertyName, int.MinValue);
        return value != int.MinValue ? value : GetInt(node, fallbackPropertyName, defaultValue);
    }

    private static string[] GetStringArray(JsonNode? node, string propertyName)
    {
        return node?[propertyName] is JsonArray array
            ? array.Select(item => item?.GetValue<string>() ?? "").Where(value => value.Length > 0).ToArray()
            : [];
    }

    private static string[] GetStringArrayOrDefault(JsonNode? node, string propertyName, string[] defaultValue)
    {
        var values = GetStringArray(node, propertyName);
        return values.Length > 0 ? values : defaultValue;
    }

    private static string[] GetStringArrayOrDefault(JsonNode? node, string propertyName, string[] defaultValue, string fallbackPropertyName)
    {
        var values = GetStringArray(node, propertyName);
        if (values.Length > 0)
        {
            return values;
        }

        var fallbackValues = GetStringArray(node, fallbackPropertyName);
        return fallbackValues.Length > 0 ? fallbackValues : defaultValue;
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

    private static string[][] GetStringMatrixOrDefault(JsonNode? node, string propertyName, string[][] defaultValue)
    {
        var values = GetStringMatrix(node, propertyName);
        return values.Length > 0 ? values : defaultValue;
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
                    apps.Add(new AutoLaunchAppEditorRow("", path.Trim(), Enabled: true, RestartOnPimaxReconnect: true, RunAsAdmin: false, StartMinimized: false));
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
                        GetBool(obj, "RunAsAdmin", defaultValue: false),
                        GetBool(obj, "StartMinimized", defaultValue: false)));
                    break;
            }
        }

        return apps.ToArray();
    }

    private static BaseStationDevice[] GetBaseStations(JsonNode? node)
    {
        if (node?["BaseStations"] is not JsonArray array)
        {
            return [];
        }

        var baseStations = new List<BaseStationDevice>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var name = GetString(item, "Name").Trim();
            var address = GetString(item, "BluetoothAddress").Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            baseStations.Add(new BaseStationDevice
            {
                FriendlyName = GetString(item, "FriendlyName").Trim(),
                Name = name,
                BluetoothAddress = address,
                Version = GetBaseStationVersion(item, "Version", BaseStationDevice.InferVersion(name)),
                Enabled = GetBool(item, "Enabled", defaultValue: true),
                Id = GetString(item, "Id").Trim(),
                PowerStateReadUnsupported = GetBool(item, "PowerStateReadUnsupported", defaultValue: false)
            }.WithDefaults());
        }

        return baseStations.ToArray();
    }

    private static OscRouteEditorRow[] GetOscRoutes(JsonNode? node)
    {
        if (node?["OscRoutes"] is not JsonArray array)
        {
            return [];
        }

        var routes = new List<OscRouteEditorRow>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var appReceivePort = GetInt(item, "AppReceivePort", GetInt(item, "OutputPort", 0));
            if (appReceivePort is < 1 or > 65535)
            {
                continue;
            }

            routes.Add(new OscRouteEditorRow(
                GetString(item, "Name").Trim(),
                appReceivePort,
                GetBool(item, "Enabled", defaultValue: true)));
        }

        return routes.ToArray();
    }

    private static BaseStationVersion GetBaseStationVersion(JsonNode? node, string propertyName, BaseStationVersion defaultValue)
    {
        if (node?[propertyName] is not JsonValue value)
        {
            return defaultValue;
        }

        if (value.TryGetValue<string>(out var text) && Enum.TryParse(text, ignoreCase: true, out BaseStationVersion parsedText))
        {
            return parsedText;
        }

        return value.TryGetValue<int>(out var number) && Enum.IsDefined(typeof(BaseStationVersion), number)
            ? (BaseStationVersion)number
            : defaultValue;
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
            AddAutoLaunchAppGridRow(app.Name, app.Path, app.Enabled, app.RestartOnPimaxReconnect, app.RunAsAdmin, app.StartMinimized);
        }
    }

    private int AddAutoLaunchAppGridRow(string name, string path, bool enabled, bool restartOnPimaxReconnect, bool runAsAdmin, bool startMinimized)
    {
        return _autoLaunchAppsGrid.Rows.Add(
            name,
            path,
            "",
            enabled,
            restartOnPimaxReconnect,
            runAsAdmin,
            startMinimized);
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
                GetGridBool(row, "RunAsAdmin", defaultValue: false),
                GetGridBool(row, "StartMinimized", defaultValue: false)));
        }

        return apps.ToArray();
    }

    private void PopulateOscRoutesGrid(OscRouteEditorRow[] routes)
    {
        ConfigureOscRoutesGrid();
        _oscRoutesGrid.Rows.Clear();
        foreach (var route in routes)
        {
            AddOscRouteGridRow(route.Name, route.AppReceivePort, route.Enabled);
        }
    }

    private int AddOscRouteGridRow(string name, object appReceivePort, bool enabled)
    {
        return _oscRoutesGrid.Rows.Add(enabled, name, appReceivePort);
    }

    private void CommitOscRoutesGridEdits()
    {
        if (_oscRoutesGrid.IsCurrentCellDirty)
        {
            _oscRoutesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        _oscRoutesGrid.EndEdit();
        ValidateChildren();
    }

    private OscRouteEditorRow[] ReadOscRoutesGrid()
    {
        CommitOscRoutesGridEdits();
        var routes = new List<OscRouteEditorRow>();
        foreach (DataGridViewRow row in _oscRoutesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var appReceivePort = GetGridInt(row, "AppReceivePort", defaultValue: 0);
            if (appReceivePort is < 1 or > 65535)
            {
                continue;
            }

            routes.Add(new OscRouteEditorRow(
                GetGridString(row, "Name"),
                appReceivePort,
                GetGridBool(row, "Enabled", defaultValue: true)));
        }

        return routes.ToArray();
    }

    private void PopulateBaseStationsGrid(BaseStationDevice[] baseStations)
    {
        ConfigureBaseStationsGrid();
        _baseStationsGrid.Rows.Clear();
        foreach (var baseStation in baseStations)
        {
            AddBaseStationGridRow(baseStation.WithDefaults());
        }
    }

    private int AddBaseStationGridRow(BaseStationDevice baseStation)
    {
        var rowIndex = _baseStationsGrid.Rows.Add(
            baseStation.Enabled,
            baseStation.FriendlyName,
            baseStation.Name,
            baseStation.BluetoothAddress,
            baseStation.EffectiveVersion.ToString(),
            baseStation.Id,
            "",
            baseStation.PowerStateReadUnsupported,
            "Power On",
            "Sleep",
            "Standby",
            "Identify");
        RefreshBaseStationIdCellState(_baseStationsGrid.Rows[rowIndex], clearInvalidId: true);
        return rowIndex;
    }

    private void UpsertBaseStationGridRow(BaseStationDevice baseStation)
    {
        foreach (DataGridViewRow row in _baseStationsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            if (!string.Equals(GetGridString(row, "BluetoothAddress"), baseStation.BluetoothAddress, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            row.Cells["Name"].Value = baseStation.Name;
            row.Cells["Version"].Value = baseStation.EffectiveVersion.ToString();
            RefreshBaseStationIdCellState(row, clearInvalidId: true);
            if (string.IsNullOrWhiteSpace(GetGridString(row, "FriendlyName")))
            {
                row.Cells["FriendlyName"].Value = baseStation.FriendlyName;
            }

            return;
        }

        AddBaseStationGridRow(baseStation);
    }

    private void CommitBaseStationsGridEdits()
    {
        if (_baseStationsGrid.IsCurrentCellDirty)
        {
            _baseStationsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        _baseStationsGrid.EndEdit();
        ValidateChildren();
    }

    private BaseStationDevice[] ReadBaseStationsGrid()
    {
        CommitBaseStationsGridEdits();
        var baseStations = new List<BaseStationDevice>();
        foreach (DataGridViewRow row in _baseStationsGrid.Rows)
        {
            var baseStation = ReadBaseStationGridRow(row);
            if (baseStation is not null)
            {
                baseStations.Add(baseStation);
            }
        }

        return baseStations.ToArray();
    }

    private BaseStationDevice? ReadBaseStationGridRow(DataGridViewRow row)
    {
        if (row.IsNewRow)
        {
            return null;
        }

        var address = GetGridString(row, "BluetoothAddress");
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var name = GetGridString(row, "Name");
        var friendlyName = GetGridString(row, "FriendlyName");
        if (!Enum.TryParse(GetGridString(row, "Version"), ignoreCase: true, out BaseStationVersion version))
        {
            version = BaseStationDevice.InferVersion(name);
        }

        return new BaseStationDevice
        {
            Enabled = GetGridBool(row, "Enabled", defaultValue: true),
            FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? name : friendlyName,
            Name = name,
            BluetoothAddress = address,
            Version = version,
            Id = version == BaseStationVersion.V1 ? GetGridString(row, "Id") : "",
            PowerStateReadUnsupported = GetGridBool(row, "PowerStateReadUnsupported", defaultValue: false)
        }.WithDefaults();
    }

    private void RefreshBaseStationIdCellState(DataGridViewRow row, bool clearInvalidId)
    {
        if (row.IsNewRow || !_baseStationsGrid.Columns.Contains("Id"))
        {
            return;
        }

        if (!Enum.TryParse(GetGridString(row, "Version"), ignoreCase: true, out BaseStationVersion version))
        {
            version = BaseStationDevice.InferVersion(GetGridString(row, "Name"));
        }

        var idCell = row.Cells["Id"];
        var idAllowed = version == BaseStationVersion.V1;
        idCell.ReadOnly = !idAllowed;
        if (!idAllowed && clearInvalidId && !string.IsNullOrWhiteSpace(GetGridString(row, "Id")))
        {
            idCell.Value = "";
        }

        idCell.ToolTipText = idAllowed
            ? "Base Station 1.0 requires the 8-character ID printed on the back label."
            : "Base Station 2.0 does not use a V1 ID.";
        idCell.Style.BackColor = idAllowed
            ? _theme.InputBack
            : (_theme.IsDark ? Color.FromArgb(45, 45, 45) : SystemColors.Control);
        idCell.Style.ForeColor = idAllowed ? _theme.Text : SystemColors.GrayText;
    }

    private async Task RefreshBaseStationStatesAsync()
    {
        CommitBaseStationsGridEdits();
        var client = new BaseStationGattClient();
        var updated = 0;

        foreach (DataGridViewRow row in _baseStationsGrid.Rows)
        {
            var baseStation = ReadBaseStationGridRow(row);
            if (baseStation is null)
            {
                continue;
            }

            try
            {
                SetActiveOperationStatus($"Reading state for {baseStation.DisplayName}...");
                var state = await client.ReadPowerStateAsync(baseStation, CancellationToken.None);
                row.Cells["State"].Value = state;
                row.Cells["PowerStateReadUnsupported"].Value = state == BaseStationPowerState.Unsupported;
                updated++;
            }
            catch (Exception ex)
            {
                row.Cells["State"].Value = "Unknown";
                SetActiveOperationStatus($"Could not read state for {baseStation.DisplayName}: {ex.Message}");
            }

            await Task.Delay(BaseStationCommandTiming.InterStationDelay);
        }

        SetStatus($"Refreshed state for {updated} base station(s).");
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

    private static int GetGridInt(DataGridViewRow row, string columnName, int defaultValue)
    {
        var value = row.Cells[columnName].Value;
        return value switch
        {
            int intValue => intValue,
            decimal decimalValue => decimal.ToInt32(decimalValue),
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private void OnManagedGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete || sender is not DataGridView grid)
        {
            return;
        }

        if (grid.IsCurrentCellInEditMode || grid.EditingControl is not null)
        {
            return;
        }

        DeleteSelectedGridRow(grid, "Deleted selected row.");
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void DeleteSelectedGridRow(DataGridView grid, string statusMessage)
    {
        var row = GetSelectedGridRow(grid);
        if (row is null)
        {
            SetStatus("No row selected to delete.");
            return;
        }

        var removedIndex = row.Index;
        grid.Rows.RemoveAt(removedIndex);
        if (grid.Rows.Count > 0)
        {
            SelectGridRow(grid, Math.Min(removedIndex, grid.Rows.Count - 1));
        }

        SetStatus(statusMessage);
    }

    private static DataGridViewRow? GetSelectedGridRow(DataGridView grid)
    {
        if (grid.SelectedRows.Count > 0 && !grid.SelectedRows[0].IsNewRow)
        {
            return grid.SelectedRows[0];
        }

        var currentRow = grid.CurrentRow;
        return currentRow is not null && !currentRow.IsNewRow ? currentRow : null;
    }

    private static void SelectGridRow(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
        {
            return;
        }

        grid.ClearSelection();
        var row = grid.Rows[rowIndex];
        row.Selected = true;
        var firstVisibleCell = row.Cells
            .Cast<DataGridViewCell>()
            .FirstOrDefault(cell => cell.Visible);
        if (firstVisibleCell is not null)
        {
            grid.CurrentCell = firstVisibleCell;
        }
    }

    private static CheckBox CreateOptionalConfigCheckBox(string text)
    {
        return new CheckBox
        {
            Text = text,
            AutoSize = true,
            ThreeState = false,
            Checked = false
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

    private void SetStatus(string message) => SetTemporaryStatus(message);

    private void SetTemporaryStatus(string message, string? tooltip = null)
    {
        _temporaryStatus = message;
        _temporaryStatusToolTip = tooltip ?? message;
        _temporaryStatusTabIndex = _tabs.SelectedIndex;
        _temporaryStatusIsActiveOperation = false;
        _temporaryStatusExpiresAt = DateTime.UtcNow.AddMilliseconds(TemporaryStatusMilliseconds);
        _statusTimer.Stop();
        _statusTimer.Start();
        RefreshVisibleStatus();
    }

    private void SetActiveOperationStatus(string message, string? tooltip = null)
    {
        _temporaryStatus = message;
        _temporaryStatusToolTip = tooltip ?? message;
        _temporaryStatusTabIndex = _tabs.SelectedIndex;
        _temporaryStatusIsActiveOperation = true;
        _temporaryStatusExpiresAt = DateTime.MaxValue;
        _statusTimer.Stop();
        RefreshVisibleStatus();
    }

    private void SetPersistentStatus(string message, string? tooltip = null)
    {
        _persistentStatus = string.IsNullOrWhiteSpace(message) ? "Ready" : message;
        _persistentStatusToolTip = tooltip ?? _persistentStatus;
        RefreshVisibleStatus();
    }

    private void ExpireTemporaryStatus()
    {
        if (_temporaryStatusIsActiveOperation)
        {
            return;
        }

        if (_temporaryStatus is null || DateTime.UtcNow < _temporaryStatusExpiresAt)
        {
            return;
        }

        _statusTimer.Stop();
        ClearTemporaryStatus();
        RefreshVisibleStatus();
    }

    private void ClearTemporaryStatus()
    {
        _temporaryStatus = null;
        _temporaryStatusToolTip = null;
        _temporaryStatusExpiresAt = default;
        _temporaryStatusTabIndex = -1;
        _temporaryStatusIsActiveOperation = false;
    }

    private void RefreshVisibleStatus()
    {
        if (_temporaryStatus is not null)
        {
            if (!_temporaryStatusIsActiveOperation && DateTime.UtcNow >= _temporaryStatusExpiresAt)
            {
                ClearTemporaryStatus();
            }
            else
            {
                var visibleTemporaryStatus = _temporaryStatus;
                if (!_temporaryStatusIsActiveOperation
                    && _temporaryStatusTabIndex >= 0
                    && _temporaryStatusTabIndex != _tabs.SelectedIndex
                    && !visibleTemporaryStatus.StartsWith("Last action:", StringComparison.OrdinalIgnoreCase))
                {
                    visibleTemporaryStatus = "Last action: " + visibleTemporaryStatus;
                }

                _statusLabel.Text = visibleTemporaryStatus;
                _toolTips.SetToolTip(_statusLabel, _temporaryStatusToolTip ?? visibleTemporaryStatus);
                return;
            }
        }

        var (status, tooltip) = ResolvePersistentStatus();
        _statusLabel.Text = status;
        _toolTips.SetToolTip(_statusLabel, tooltip);
    }

    private (string Status, string ToolTip) ResolvePersistentStatus()
    {
        if (_hasUnsavedChanges)
        {
            return ("Unsaved changes", "Unsaved changes");
        }

        if (_tabs.SelectedIndex == _tabs.TabCount - 1 && !string.IsNullOrWhiteSpace(_rawJsonStatus))
        {
            return (_rawJsonStatus, _rawJsonStatus);
        }

        if (!string.IsNullOrWhiteSpace(_persistentStatus))
        {
            return (_persistentStatus, _persistentStatusToolTip);
        }

        return ("Ready", "Ready");
    }

    private void SetLastActionStatus(string message)
    {
        SetTemporaryStatus(message.StartsWith("Last action:", StringComparison.OrdinalIgnoreCase)
            ? message
            : "Last action: " + message);
    }

    private void SetCleanStatus(string message, string? tooltip = null)
    {
        _hasUnsavedChanges = false;
        ClearTemporaryStatus();
        SetPersistentStatus(message, tooltip);
        UpdateWindowTitle();
    }

    private void MarkDirty(string status = "Unsaved changes", bool syncRawJson = true)
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        _hasUnsavedChanges = true;
        UpdateWindowTitle();
        SetPersistentStatus(status);
        UpdateGridWarnings();
        if (syncRawJson && !_rawJsonHasUnappliedChanges)
        {
            RefreshRawJsonFromEditor();
        }
    }

    private void UpdateWindowTitle()
    {
        Text = _hasUnsavedChanges ? BaseWindowTitle + " *" : BaseWindowTitle;
    }

    private void RegisterDirtyTracking(Control root)
    {
        foreach (Control control in root.Controls)
        {
            RegisterDirtyTracking(control);
        }

        switch (root)
        {
            case TextBox textBox when ReferenceEquals(textBox, _rawJsonTextBox):
                textBox.TextChanged += (_, _) => OnRawJsonEdited();
                break;
            case TextBox textBox when !ReferenceEquals(textBox, _configPathTextBox):
                textBox.TextChanged += (_, _) => MarkDirty();
                break;
            case CheckBox checkBox:
                checkBox.CheckStateChanged += (_, _) => MarkDirty();
                break;
            case NumericUpDown input:
                input.ValueChanged += (_, _) => MarkDirty();
                break;
            case ComboBox comboBox when !ReferenceEquals(comboBox, _configSelectorComboBox):
                comboBox.SelectedIndexChanged += (_, _) => MarkDirty();
                break;
            case DataGridView grid:
                grid.CurrentCellDirtyStateChanged += (_, _) =>
                {
                    if (grid.IsCurrentCellDirty)
                    {
                        grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    }
                };
                grid.CellValueChanged += (_, _) => MarkDirty();
                grid.CellEndEdit += (_, _) => MarkDirty();
                grid.RowsAdded += (_, _) => MarkDirty();
                grid.RowsRemoved += (_, _) => MarkDirty();
                grid.UserDeletedRow += (_, _) => MarkDirty();
                grid.EditingControlShowing += (_, e) =>
                {
                    if (e.Control is TextBox textBox)
                    {
                        textBox.TextChanged -= OnGridEditingControlTextChanged;
                        textBox.TextChanged += OnGridEditingControlTextChanged;
                    }
                };
                break;
        }
    }

    private void OnGridEditingControlTextChanged(object? sender, EventArgs e)
        => MarkDirty();

    private void RefreshRawJsonFromEditor()
    {
        try
        {
            _suppressDirtyTracking = true;
            _rawJsonTextBox.Text = BuildCurrentJson();
            _appliedRawJson = _rawJsonTextBox.Text;
            ValidateRawJsonText(showStatus: false);
        }
        catch
        {
            // Keep the current Raw JSON text if a transient grid edit cannot be serialized yet.
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    private void OnRawJsonEdited()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        _rawJsonHasUnappliedChanges = true;
        ValidateRawJsonText(showStatus: true);
        MarkDirty("Unsaved Raw JSON changes", syncRawJson: false);
    }

    private bool ValidateRawJsonText(bool showStatus)
    {
        try
        {
            ParseJson(_rawJsonTextBox.Text);
            _rawJsonValidationLabel.Text = _rawJsonHasUnappliedChanges
                ? "Raw JSON is valid but has not been applied to the editor."
                : "Raw JSON is valid.";
            _rawJsonStatus = _rawJsonValidationLabel.Text;
            if (showStatus)
            {
                RefreshVisibleStatus();
            }

            return true;
        }
        catch (JsonException ex)
        {
            var message = FormatJsonError(ex);
            _rawJsonValidationLabel.Text = message;
            _rawJsonStatus = message;
            if (showStatus)
            {
                RefreshVisibleStatus();
            }

            return false;
        }
        catch (Exception ex)
        {
            var message = "Invalid JSON: " + ex.Message;
            _rawJsonValidationLabel.Text = message;
            _rawJsonStatus = message;
            if (showStatus)
            {
                RefreshVisibleStatus();
            }

            return false;
        }
    }

    private static string FormatJsonError(JsonException ex)
    {
        if (ex.LineNumber is long lineNumber && ex.BytePositionInLine is long bytePosition)
        {
            return $"Invalid JSON: {ex.Message} near line {lineNumber + 1}, position {bytePosition + 1}";
        }

        return "Invalid JSON: " + ex.Message;
    }

    private void ApplyRawJsonToEditor()
    {
        try
        {
            var node = ParseJson(_rawJsonTextBox.Text);
            _suppressDirtyTracking = true;
            PopulateControls(node);
            _appliedRawJson = _rawJsonTextBox.Text;
            _rawJsonHasUnappliedChanges = false;
            ResetFirstRunPreferenceTouchTracking();
            ValidateRawJsonText(showStatus: false);
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex is JsonException jsonEx ? FormatJsonError(jsonEx) : ex.Message, "Could not apply Raw JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Raw JSON apply failed.");
            return;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        MarkDirty("Unsaved changes");
        SetTemporaryStatus("Raw JSON applied to editor.");
    }

    private void RevertRawJsonChanges()
    {
        try
        {
            _suppressDirtyTracking = true;
            _rawJsonTextBox.Text = BuildCurrentJson();
            _appliedRawJson = _rawJsonTextBox.Text;
            _rawJsonHasUnappliedChanges = false;
            ValidateRawJsonText(showStatus: false);
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        try
        {
            _hasUnsavedChanges = !string.Equals(BuildCurrentJson(), _loadedJson, StringComparison.Ordinal);
        }
        catch
        {
            _hasUnsavedChanges = true;
        }

        UpdateWindowTitle();
        SetStatus(_hasUnsavedChanges ? "Raw JSON changes reverted. Unsaved changes remain." : "Raw JSON changes reverted.");
    }

    private void FormatRawJson()
    {
        try
        {
            var node = ParseJson(_rawJsonTextBox.Text);
            _suppressDirtyTracking = true;
            _rawJsonTextBox.Text = node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{\r\n}";
            _rawJsonHasUnappliedChanges = true;
            ValidateRawJsonText(showStatus: false);
        }
        catch (Exception ex)
        {
            ShowThemedMessageBox(ex is JsonException jsonEx ? FormatJsonError(jsonEx) : ex.Message, "Could not format JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Format JSON failed.");
            return;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        MarkDirty("Unsaved Raw JSON changes", syncRawJson: false);
        SetTemporaryStatus("Raw JSON formatted.");
    }

    private void RestoreDefaults()
    {
        var result = ShowThemedMessageBox(
            "Restore default configuration values?\r\n\r\nThis will replace current editor values but will not overwrite the config file until you click Save.",
            "Restore defaults",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.OK)
        {
            return;
        }

        try
        {
            _suppressDirtyTracking = true;
            _appliedRawJson = GetDefaultConfigTemplateJson();
            _rawJsonTextBox.Text = _appliedRawJson;
            PopulateControls(ParseJson(_appliedRawJson));
            _rawJsonHasUnappliedChanges = false;
            ResetFirstRunPreferenceTouchTracking();
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        MarkDirty("Unsaved changes");
        SetTemporaryStatus("Default values restored. Save to write them to disk.");
    }

    private void ShowAboutDialog()
    {
        var supervisorPath = Path.Combine(AppContext.BaseDirectory, "PimaxVrcSupervisor.exe");
        using var dialog = new Form
        {
            Text = "About",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(560, 240)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18, 16, 18, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconBox = new PictureBox
        {
            Width = 42,
            Height = 42,
            Margin = new Padding(0, 4, 14, 0),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = SystemIcons.Information.ToBitmap()
        };

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            Text = "Pimax VRC Supervisor Config Editor",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };
        var versionLabel = new Label
        {
            Text = $"Version {AppVersion.Current}",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        var integrationLabel = new Label
        {
            Text = "Uses optional SteamVR/OpenVR runtime integration for base-station checks and SteamVR startup.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        var supervisorPathLabel = new Label
        {
            Text = "Supervisor executable:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3)
        };
        var supervisorPathTextBox = new TextBox
        {
            Text = supervisorPath,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0),
            Width = 450
        };

        details.Controls.Add(titleLabel, 0, 0);
        details.Controls.Add(versionLabel, 0, 1);
        details.Controls.Add(integrationLabel, 0, 2);
        details.Controls.Add(supervisorPathLabel, 0, 3);
        details.Controls.Add(supervisorPathTextBox, 0, 4);

        content.Controls.Add(iconBox, 0, 0);
        content.Controls.Add(details, 1, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };

        var okButton = CreateButton("OK", autoSize: false, width: 88, height: 28, dialogResult: DialogResult.OK);
        okButton.Margin = new Padding(6, 0, 0, 0);
        var moreButton = CreateButton("More", autoSize: false, width: 88, height: 28);
        moreButton.Margin = new Padding(6, 0, 0, 0);
        moreButton.Click += (_, _) => ShowAboutDetailsDialog(dialog);
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(moreButton);

        root.Controls.Add(content, 0, 0);
        root.Controls.Add(buttonPanel, 0, 1);
        dialog.Controls.Add(root);

        dialog.AcceptButton = okButton;
        dialog.CancelButton = okButton;

        ApplyThemeTo(dialog);
        WindowsTitleBar.ApplyTheme(dialog.Handle, _theme.IsDark);
        dialog.ShowDialog(this);
    }

    private void ShowAboutDetailsDialog(IWin32Window owner)
    {
        using var dialog = new Form
        {
            Text = "Third-party notices",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            MaximizeBox = true,
            ShowInTaskbar = false,
            MinimumSize = new Size(560, 420),
            ClientSize = new Size(760, 600)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14, 14, 14, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var noticesTextBox = new TextBox
        {
            Text = BuildAboutDetailsText(),
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = true,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, Font.Size)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var closeButton = CreateButton("Close", autoSize: false, width: 88, height: 28, dialogResult: DialogResult.OK);
        closeButton.Margin = new Padding(6, 0, 0, 0);
        buttonPanel.Controls.Add(closeButton);

        root.Controls.Add(noticesTextBox, 0, 0);
        root.Controls.Add(buttonPanel, 0, 1);
        dialog.Controls.Add(root);

        dialog.AcceptButton = closeButton;
        dialog.CancelButton = closeButton;

        ApplyThemeTo(dialog);
        WindowsTitleBar.ApplyTheme(dialog.Handle, _theme.IsDark);
        dialog.ShowDialog(owner);
    }

    private static string BuildAboutDetailsText()
        => """
           Pimax VRC Supervisor - Third-party notices

           SteamVR/OpenVR runtime integration
           Pimax VRC Supervisor can optionally query the user's installed SteamVR/OpenVR runtime to confirm active tracking references for SteamVR base-station startup and to register the SteamVR dashboard host manifest. The app does not bundle SteamVR, OpenVR, or openvr_api.dll. When this feature is available, it loads openvr_api.dll from the user's configured SteamVR/OpenVR runtime.

           OpenVR SDK attribution
           OpenVR SDK
           Copyright (c) 2015, Valve Corporation
           License: BSD-3-Clause
           Source: https://github.com/ValveSoftware/openvr
           License source: https://raw.githubusercontent.com/ValveSoftware/openvr/master/LICENSE

           OpenVR SDK license text
           Copyright (c) 2015, Valve Corporation
           All rights reserved.

           Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

           1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
           2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
           3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

           THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
           IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

           Named integrations and devices
           This app contains compatibility logic, default paths, process names, or device detection labels for Pimax, SteamVR/OpenVR, SteamVR Base Station, HTC Vive/Vive mouth tracker, VRChat, VRCFaceTracking, Broken Eye, Intiface, Lovense, and OscGoesBrrr.

           Trademark notice
           Product names, company names, trademarks, and registered trademarks are property of their respective owners. Their mention here describes compatibility or user-configurable integration points and does not imply endorsement, sponsorship, or affiliation.
           """;

    private async void ValidateConfigFromButton()
    {
        var result = await ValidateCurrentEditorValuesAsync(includeScheduledTaskValidation: true);

        if (result.HasErrors)
        {
            ShowValidationResultDialog(result, hasErrors: true);
            SetStatus($"Validation failed with {result.Errors.Count} error(s).");
            return;
        }

        if (result.HasWarnings)
        {
            ShowValidationResultDialog(result, hasErrors: false);
            SetStatus($"Validation completed with {result.Warnings.Count} warning(s).");
            return;
        }

        SetStatus("Validation passed.");
    }

    private async Task<ValidationResult> ValidateCurrentEditorValuesAsync(bool includeScheduledTaskValidation)
    {
        SetStatus("Validating configuration...");
        var result = ValidateCurrentConfig(ValidationMode.Save);
        UpdateGridWarnings();
        if (includeScheduledTaskValidation)
        {
            await AddScheduledTaskValidationAsync(result);
        }

        return result;
    }

    private async Task AddScheduledTaskValidationAsync(ValidationResult result)
    {
        SetStatus("Validating scheduled tasks...");
        try
        {
            var currentEditorDirectory = GetCurrentConfigEditorDirectory();
            var issues = await Task.Run(() => global::ScheduledTaskPathValidator.ValidateExistingManagedTasks(currentEditorDirectory))
                .WaitAsync(TimeSpan.FromSeconds(5));
            foreach (var issue in issues)
            {
                result.Warnings.Add(global::ScheduledTaskPathValidator.FormatIssue(issue));
            }
        }
        catch (TimeoutException)
        {
            result.Warnings.Add("Scheduled task validation timed out. Config values were validated, but Windows Task Scheduler did not respond in time.");
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Scheduled task validation could not complete: {ex.Message}");
        }
    }

    private bool ValidateForLaunch()
    {
        var result = ValidateCurrentConfig(ValidationMode.Launch);
        UpdateGridWarnings();
        if (result.HasErrors)
        {
            ShowThemedMessageBox(FormatValidationSummary(result, includePrompt: false), "Cannot launch supervisor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus($"Launch validation failed with {result.Errors.Count} error(s).");
            return false;
        }

        if (result.HasWarnings)
        {
            var answer = ShowThemedMessageBox(FormatValidationSummary(result, includePrompt: true).Replace("Save anyway?", "Launch anyway?"), "Launch warnings", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                SetStatus("Launch cancelled after validation warnings.");
                return false;
            }
        }

        return true;
    }

    private ValidationResult ValidateCurrentConfig(ValidationMode mode)
    {
        var result = new ValidationResult();
        CommitAutoLaunchAppsGridEdits();
        CommitBaseStationsGridEdits();
        CommitOscRoutesGridEdits();
        RefreshPathIndicators();

        if (!ValidateRawJsonText(showStatus: false))
        {
            result.Errors.Add(_rawJsonValidationLabel.Text);
        }
        else if (_rawJsonHasUnappliedChanges)
        {
            result.Warnings.Add("Raw JSON changes are valid but have not been applied to the editor.");
        }

        ValidatePath(result, "Broken Eye executable path", _brokenEyePathTextBox.Text, required: false);
        ValidatePath(result, "VRCFaceTracking executable path", _vrcFaceTrackingPathTextBox.Text, required: false);
        if (_oscGoesBrrrEnabledCheckBox.Checked)
        {
            ValidatePath(result, "Intiface executable path", _intifacePathTextBox.Text, required: true);
            ValidatePath(result, "OscGoesBrrr executable path", _oscGoesBrrrPathTextBox.Text, required: true);
        }
        else
        {
            ValidatePath(result, "Intiface executable path", _intifacePathTextBox.Text, required: false);
            ValidatePath(result, "OscGoesBrrr executable path", _oscGoesBrrrPathTextBox.Text, required: false);
        }

        ValidateAutoLaunchApps(result);
        ValidateProcessList(result, "Broken Eye process name", _brokenEyeProcessesTextBox.Text);
        ValidateProcessList(result, "VRCFaceTracking process name", _vrcFaceTrackingProcessesTextBox.Text);
        ValidateProcessList(result, "Apps that trigger cleanup when closed", _watchedShutdownProcessesTextBox.Text);
        ValidateProcessList(result, "SteamVR server process name", _steamVrServerProcessesTextBox.Text);
        ValidateProcessList(result, "Intiface process name", _intifaceProcessesTextBox.Text);
        ValidateProcessList(result, "OscGoesBrrr process name", _oscGoesBrrrProcessesTextBox.Text);

        if (ParseStringMatrix(_pimaxDetectorsTextBox.Text).Length == 0)
        {
            result.Warnings.Add("Pimax detector rules are empty.");
        }
        if (ParseStringMatrix(_mouthTrackerDetectorsTextBox.Text).Length == 0)
        {
            result.Warnings.Add("Mouth tracker detector rules are empty.");
        }
        if (_oscGoesBrrrEnabledCheckBox.Checked && ParseStringMatrix(_lovenseDetectorsTextBox.Text).Length == 0)
        {
            result.Warnings.Add("Lovense detector rules are empty.");
        }

        ValidateOscRoutes(result);
        ValidateBaseStations(result);
        ValidateTimingValues(result);
        ValidateManagedScheduledTasks(result, mode);
        return result;
    }

    private static void ValidateManagedScheduledTasks(ValidationResult result, ValidationMode mode)
    {
        // Existing scheduled tasks are queried asynchronously by the Validate button and are
        // rewritten/deleted during Save when startup integration is applied.
    }

    private static string GetCurrentConfigEditorDirectory()
        => global::ScheduledTaskPathValidator.GetCurrentExecutableDirectory();

    private static void ValidatePath(ValidationResult result, string label, string path, bool required)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (required)
            {
                result.Errors.Add($"{label} is required.");
            }
            return;
        }

        var expanded = ExpandPath(path);
        if (!File.Exists(expanded))
        {
            result.Warnings.Add($"{label} does not exist.");
            return;
        }

        if (!string.Equals(Path.GetExtension(expanded), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add($"{label} should point to an .exe file.");
        }
    }

    private void ValidateAutoLaunchApps(ValidationResult result)
    {
        foreach (DataGridViewRow row in _autoLaunchAppsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var enabled = GetGridBool(row, "Enabled", defaultValue: true);
            var name = GetGridString(row, "Name");
            var path = GetGridString(row, "Path");
            var label = string.IsNullOrWhiteSpace(name)
                ? $"Auto-launch app row {row.Index + 1} executable path"
                : $"Auto-launch app '{name}' executable path";
            if (string.IsNullOrWhiteSpace(path))
            {
                if (enabled)
                {
                    result.Errors.Add($"{label} is required when the row is enabled.");
                }

                continue;
            }

            ValidatePath(result, label, path, required: enabled);
        }
    }

    private static void ValidateProcessList(ValidationResult result, string label, string text)
    {
        if (ParseStringList(text).Length == 0)
        {
            result.Warnings.Add($"{label} is empty.");
        }
    }

    private void ValidateOscRoutes(ValidationResult result)
    {
        var enabledPorts = new HashSet<int>();
        foreach (DataGridViewRow row in _oscRoutesGrid.Rows)
        {
            if (row.IsNewRow || !GetGridBool(row, "Enabled", defaultValue: true))
            {
                continue;
            }

            var name = GetGridString(row, "Name");
            var port = GetGridInt(row, "AppReceivePort", defaultValue: 0);
            if (string.IsNullOrWhiteSpace(name) || port is < 1 or > 65535)
            {
                result.Errors.Add("Enabled OSC route requires a name and target app receive port.");
            }
            else if (!enabledPorts.Add(port))
            {
                result.Errors.Add($"Duplicate enabled OSC route target app receive port: {port}.");
            }
        }
    }

    private void ValidateBaseStations(ValidationResult result)
    {
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in _baseStationsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var enabled = GetGridBool(row, "Enabled", defaultValue: true);
            var name = GetGridString(row, "Name");
            var friendlyName = GetGridString(row, "FriendlyName");
            var address = GetGridString(row, "BluetoothAddress");
            var version = Enum.TryParse(GetGridString(row, "Version"), ignoreCase: true, out BaseStationVersion parsedVersion)
                ? parsedVersion
                : BaseStationDevice.InferVersion(name);
            if (!string.IsNullOrWhiteSpace(address) && !addresses.Add(address))
            {
                result.Errors.Add($"Duplicate base station Bluetooth address: {address}.");
            }

            if (version == BaseStationVersion.V2 && !string.IsNullOrWhiteSpace(GetGridString(row, "Id")))
            {
                result.Errors.Add("Base Station 2.0 rows cannot have a V1 ID.");
            }

            if (enabled && (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(friendlyName)))
            {
                result.Errors.Add("Base station enabled without required identifying data.");
            }
        }
    }

    private void ValidateTimingValues(ValidationResult result)
    {
        foreach (var (key, input) in _numberInputs)
        {
            var value = (int)input.Value;
            if (value < input.Minimum || value > input.Maximum)
            {
                result.Errors.Add($"{_numberLabels.GetValueOrDefault(key, key)} is outside the allowed range.");
            }
        }

        WarnTiming(result, "PollIntervalSeconds", low: 1, high: 120);
        WarnTiming(result, "StartupTimeoutSeconds", low: 3, high: 300);
        WarnTiming(result, "StartupStableSeconds", low: 1, high: 120);
        WarnTiming(result, "RestartDelayAfterReconnectSeconds", low: 1, high: 300);
        WarnTiming(result, "ShutdownGraceSeconds", low: 1, high: 120);
        WarnTiming(result, "DeviceProbeTimeoutSeconds", low: 2, high: 120);
    }

    private void WarnTiming(ValidationResult result, string key, int low, int high)
    {
        if (!_numberInputs.TryGetValue(key, out var input))
        {
            return;
        }

        var value = (int)input.Value;
        var label = _numberLabels.GetValueOrDefault(key, key);
        if (value < low)
        {
            result.Warnings.Add($"{label} is very low and may cause unstable startup.");
        }
        else if (value > high)
        {
            result.Warnings.Add($"{label} is very high and may slow reconnect behavior.");
        }
    }

    private static string FormatValidationSummary(ValidationResult result, bool includePrompt)
    {
        var builder = new StringBuilder();
        if (result.HasErrors)
        {
            builder.AppendLine($"Configuration has {result.Errors.Count} error(s).");
            builder.AppendLine();
            builder.AppendLine("Errors:");
            foreach (var error in result.Errors.Distinct())
            {
                builder.AppendLine("- " + error);
            }
        }

        if (result.HasWarnings)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine($"Configuration has {result.Warnings.Count} warning(s).");
            builder.AppendLine();
            builder.AppendLine("Warnings:");
            foreach (var warning in result.Warnings.Distinct())
            {
                builder.AppendLine("- " + warning);
            }
        }

        if (includePrompt)
        {
            builder.AppendLine();
            builder.Append("Save anyway?");
        }

        return builder.ToString();
    }

    private void ShowValidationResultDialog(ValidationResult result, bool hasErrors)
    {
        var technicalText = FormatValidationSummary(result, includePrompt: false);
        var friendly = BuildFriendlyValidationSummary(result, hasErrors);
        using var dialog = new Form
        {
            Text = hasErrors ? "Configuration validation failed" : "Configuration validation warnings",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(620, 250)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 16, 18, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var summaryPanel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1
        };
        summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconBox = new PictureBox
        {
            Width = 42,
            Height = 42,
            Margin = new Padding(0, 4, 16, 0),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = IconForMessageBox(hasErrors ? MessageBoxIcon.Error : MessageBoxIcon.Warning).ToBitmap()
        };

        var textPanel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2
        };
        textPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headingLabel = new Label
        {
            Text = friendly.Heading,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };
        var bodyLabel = new Label
        {
            Text = friendly.Body,
            AutoSize = false,
            Width = 510,
            Height = 94,
            Margin = new Padding(0),
            TextAlign = ContentAlignment.TopLeft
        };
        textPanel.Controls.Add(headingLabel, 0, 0);
        textPanel.Controls.Add(bodyLabel, 0, 1);

        summaryPanel.Controls.Add(iconBox, 0, 0);
        summaryPanel.Controls.Add(textPanel, 1, 0);

        var detailsTextBox = new TextBox
        {
            Text = technicalText,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, Font.Size),
            Visible = false
        };

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var okButton = CreateButton("OK", autoSize: false, width: 88, height: 28, dialogResult: DialogResult.OK);
        okButton.Margin = new Padding(6, 0, 0, 0);
        var detailsButton = CreateButton("Show details", autoSize: false, width: 112, height: 28);
        detailsButton.Margin = new Padding(6, 0, 0, 0);
        detailsButton.Click += (_, _) =>
        {
            var showing = !detailsTextBox.Visible;
            detailsTextBox.Visible = showing;
            detailsButton.Text = showing ? "Hide details" : "Show details";
            dialog.ClientSize = showing ? new Size(720, 520) : new Size(620, 250);
        };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(detailsButton);

        root.Controls.Add(summaryPanel, 0, 0);
        root.Controls.Add(detailsTextBox, 0, 1);
        root.Controls.Add(buttonPanel, 0, 2);
        dialog.Controls.Add(root);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = okButton;

        ApplyThemeTo(dialog);
        WindowsTitleBar.ApplyTheme(dialog.Handle, _theme.IsDark);
        dialog.ShowDialog(this);
    }

    private static FriendlyValidationSummary BuildFriendlyValidationSummary(ValidationResult result, bool hasErrors)
    {
        var scheduledTaskIssue = result.Errors.Concat(result.Warnings)
            .FirstOrDefault(text => text.Contains("Scheduled task:", StringComparison.OrdinalIgnoreCase));
        if (scheduledTaskIssue is not null)
        {
            var taskName = ExtractValidationDetail(scheduledTaskIssue, "Scheduled task");
            if (string.IsNullOrWhiteSpace(taskName))
            {
                taskName = "the scheduled task";
            }

            return new FriendlyValidationSummary(
                "Scheduled task needs updating",
                $"The scheduled task \"{taskName}\" points to an older release of PimaxVrcSupervisor.\r\n\r\nClick OK, then update or recreate the scheduled task so it points to the current release.");
        }

        var count = hasErrors ? result.Errors.Count : result.Warnings.Count;
        var kind = hasErrors ? "error" : "warning";
        return new FriendlyValidationSummary(
            hasErrors ? "Configuration needs attention" : "Configuration has warnings",
            $"Validation found {count} {kind}{(count == 1 ? "" : "s")}.\r\n\r\nClick Show details to review the full validation output.");
    }

    private static string ExtractValidationDetail(string text, string label)
    {
        var prefix = label + ":";
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return "";
    }

    private string? CreateConfigBackup(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(path);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(directory, $"{fileName}.{timestamp}.bak");
        File.Copy(path, backupPath, overwrite: true);
        return backupPath;
    }

    private LaunchUnsavedChoice ShowLaunchUnsavedChangesDialog()
    {
        using var dialog = new Form
        {
            Text = "Unsaved changes",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(420, 140)
        };

        var label = new Label
        {
            Text = "You have unsaved changes. Save before launching the supervisor?",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(12)
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
            WrapContents = false
        };
        var cancelButton = CreateButton("Cancel", autoSize: false, width: 86, dialogResult: DialogResult.Cancel);
        var launchButton = CreateButton("Launch Without Saving", autoSize: false, width: 148, dialogResult: DialogResult.No);
        var saveButton = CreateButton("Save and Launch", autoSize: false, width: 124, dialogResult: DialogResult.Yes);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(launchButton);
        buttons.Controls.Add(saveButton);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = saveButton;
        dialog.CancelButton = cancelButton;
        ApplyThemeTo(dialog);

        return dialog.ShowDialog(this) switch
        {
            DialogResult.Yes => LaunchUnsavedChoice.SaveAndLaunch,
            DialogResult.No => LaunchUnsavedChoice.LaunchWithoutSaving,
            _ => LaunchUnsavedChoice.Cancel
        };
    }

    private void UpdateGridWarnings()
    {
        UpdateAutoLaunchRowWarnings();
        UpdateOscRouteRowWarnings();
        UpdateBaseStationRowWarnings();
    }

    private void UpdateAutoLaunchRowWarnings()
    {
        foreach (DataGridViewRow row in _autoLaunchAppsGrid.Rows)
        {
            row.ErrorText = "";
            row.Cells["Path"].ErrorText = "";
            if (row.IsNewRow)
            {
                continue;
            }

            var enabled = GetGridBool(row, "Enabled", defaultValue: true);
            var path = GetGridString(row, "Path");
            if (enabled && string.IsNullOrWhiteSpace(path))
            {
                row.ErrorText = "Enabled auto-launch row has no executable path.";
                row.Cells["Path"].ErrorText = row.ErrorText;
            }
            else if (!string.IsNullOrWhiteSpace(path) && !File.Exists(ExpandPath(path)))
            {
                row.ErrorText = "Executable path does not exist.";
                row.Cells["Path"].ErrorText = row.ErrorText;
            }
        }
    }

    private void UpdateOscRouteRowWarnings()
    {
        var seenPorts = new HashSet<int>();
        foreach (DataGridViewRow row in _oscRoutesGrid.Rows)
        {
            row.ErrorText = "";
            if (row.IsNewRow || !GetGridBool(row, "Enabled", defaultValue: true))
            {
                continue;
            }

            var port = GetGridInt(row, "AppReceivePort", defaultValue: 0);
            if (string.IsNullOrWhiteSpace(GetGridString(row, "Name")) || port is < 1 or > 65535)
            {
                row.ErrorText = "Enabled OSC route requires a name and target app receive port.";
            }
            else if (!seenPorts.Add(port))
            {
                row.ErrorText = "Duplicate enabled target app receive port.";
            }
        }
    }

    private void UpdateBaseStationRowWarnings()
    {
        var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in _baseStationsGrid.Rows)
        {
            row.ErrorText = "";
            if (_baseStationsGrid.Columns.Contains("Id"))
            {
                row.Cells["Id"].ErrorText = "";
            }

            if (row.IsNewRow)
            {
                continue;
            }

            var address = GetGridString(row, "BluetoothAddress");
            var enabled = GetGridBool(row, "Enabled", defaultValue: true);
            var version = Enum.TryParse(GetGridString(row, "Version"), ignoreCase: true, out BaseStationVersion parsedVersion)
                ? parsedVersion
                : BaseStationDevice.InferVersion(GetGridString(row, "Name"));
            if (!string.IsNullOrWhiteSpace(address) && !seenAddresses.Add(address))
            {
                row.ErrorText = "Duplicate Bluetooth address.";
            }
            else if (version == BaseStationVersion.V2 && !string.IsNullOrWhiteSpace(GetGridString(row, "Id")))
            {
                row.ErrorText = "Base Station 2.0 does not use a V1 ID.";
                row.Cells["Id"].ErrorText = row.ErrorText;
            }
            else if (enabled && string.IsNullOrWhiteSpace(address))
            {
                row.ErrorText = "Bluetooth address required.";
            }
            else if (enabled && (string.IsNullOrWhiteSpace(GetGridString(row, "FriendlyName")) || string.IsNullOrWhiteSpace(GetGridString(row, "Name"))))
            {
                row.ErrorText = "Friendly name and BLE name are required.";
            }
        }
    }

    private void DrawEmptyGridPlaceholder(DataGridView grid, PaintEventArgs e, string message)
    {
        if (grid.Rows.Count > 0)
        {
            return;
        }

        var bounds = new Rectangle(12, grid.ColumnHeadersHeight + 18, grid.Width - 24, 28);
        var color = _theme.IsDark ? Color.FromArgb(170, 170, 170) : SystemColors.GrayText;
        TextRenderer.DrawText(e.Graphics, message, grid.Font, bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private DialogResult ShowThemedMessageBox(
        string message,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        using var dialog = new Form
        {
            Text = caption,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(520, 240)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18, 16, 18, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconBox = new PictureBox
        {
            Width = 42,
            Height = 42,
            Margin = new Padding(0, 4, 14, 0),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = IconForMessageBox(icon).ToBitmap()
        };

        var lineCount = message.Count(ch => ch == '\n') + 1;
        Control messageControl;
        if (message.Length > 520 || lineCount > 9)
        {
            messageControl = new TextBox
            {
                Text = message,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = Font
            };
            dialog.ClientSize = new Size(560, 460);
        }
        else
        {
            messageControl = new Label
            {
                Text = message,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var measured = TextRenderer.MeasureText(message, Font, new Size(430, 0), TextFormatFlags.WordBreak);
            dialog.ClientSize = new Size(Math.Max(420, Math.Min(620, measured.Width + 118)), Math.Max(150, Math.Min(320, measured.Height + 100)));
        }

        content.Controls.Add(iconBox, 0, 0);
        content.Controls.Add(messageControl, 1, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };

        var dialogButtons = CreateDialogButtons(buttons);
        foreach (var button in dialogButtons)
        {
            buttonPanel.Controls.Add(button);
        }

        var defaultDialogButton = DefaultDialogButton(dialogButtons, buttons, defaultButton);
        dialog.AcceptButton = defaultDialogButton;
        dialog.CancelButton = dialogButtons.FirstOrDefault(button => button.DialogResult is DialogResult.Cancel or DialogResult.No);

        root.Controls.Add(content, 0, 0);
        root.Controls.Add(buttonPanel, 0, 1);
        dialog.Controls.Add(root);

        ApplyThemeTo(dialog);
        WindowsTitleBar.ApplyTheme(dialog.Handle, _theme.IsDark);
        return dialog.ShowDialog(this);
    }

    private static Icon IconForMessageBox(MessageBoxIcon icon)
        => icon switch
        {
            MessageBoxIcon.Error => SystemIcons.Error,
            MessageBoxIcon.Warning => SystemIcons.Warning,
            MessageBoxIcon.Question => SystemIcons.Question,
            _ => SystemIcons.Information
        };

    private static Button[] CreateDialogButtons(MessageBoxButtons buttons)
    {
        static Button Create(string text, DialogResult result)
        {
            var button = CreateButton(text, autoSize: false, width: 88, height: 28, dialogResult: result);
            button.Margin = new Padding(6, 0, 0, 0);
            return button;
        }

        return buttons switch
        {
            MessageBoxButtons.OKCancel => [Create("Cancel", DialogResult.Cancel), Create("OK", DialogResult.OK)],
            MessageBoxButtons.YesNo => [Create("No", DialogResult.No), Create("Yes", DialogResult.Yes)],
            MessageBoxButtons.YesNoCancel => [Create("Cancel", DialogResult.Cancel), Create("No", DialogResult.No), Create("Yes", DialogResult.Yes)],
            _ => [Create("OK", DialogResult.OK)]
        };
    }

    private static Button DefaultDialogButton(Button[] buttons, MessageBoxButtons buttonSet, MessageBoxDefaultButton defaultButton)
    {
        var targetResult = (buttonSet, defaultButton) switch
        {
            (MessageBoxButtons.OKCancel, MessageBoxDefaultButton.Button2) => DialogResult.Cancel,
            (MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button2) => DialogResult.No,
            (MessageBoxButtons.YesNoCancel, MessageBoxDefaultButton.Button2) => DialogResult.No,
            (MessageBoxButtons.YesNoCancel, MessageBoxDefaultButton.Button3) => DialogResult.Cancel,
            (MessageBoxButtons.YesNo, _) => DialogResult.Yes,
            (MessageBoxButtons.YesNoCancel, _) => DialogResult.Yes,
            _ => DialogResult.OK
        };

        return buttons.FirstOrDefault(button => button.DialogResult == targetResult) ?? buttons[0];
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
        RefreshPathIndicators();
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
                ApplyNativeThemeHint(textBox);
                break;
            case NumericUpDown input:
                input.BackColor = _theme.InputBack;
                input.ForeColor = _theme.Text;
                ApplyNativeThemeHint(input);
                break;
            case ComboBox comboBox:
                ApplyThemeToComboBox(comboBox);
                break;
            case DataGridView grid:
                ApplyThemeToGrid(grid);
                break;
            case Button button:
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Equals(button.Tag, "Primary") ? _theme.StrongBorder : _theme.Border;
                button.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
                button.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
                button.BackColor = Equals(button.Tag, "Primary") ? _theme.PrimaryButtonBack : _theme.ButtonBack;
                button.ForeColor = _theme.Text;
                button.Invalidate();
                break;
            case CheckBox checkBox:
                checkBox.UseVisualStyleBackColor = false;
                checkBox.BackColor = _theme.WindowBack;
                checkBox.ForeColor = _theme.Text;
                break;
            case Label label:
                label.BackColor = _theme.WindowBack;
                if (!Equals(label.Tag, "PathStatus"))
                {
                    label.ForeColor = Equals(label.Tag, "Muted")
                        ? (_theme.IsDark ? Color.FromArgb(178, 178, 178) : SystemColors.GrayText)
                        : _theme.Text;
                }
                break;
            case Panel or TableLayoutPanel:
                control.BackColor = _theme.WindowBack;
                control.ForeColor = _theme.Text;
                ApplyNativeThemeHint(control);
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
        grid.GridColor = _theme.IsDark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(214, 214, 214);
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
        grid.RowTemplate.DefaultCellStyle.BackColor = _theme.InputBack;
        grid.RowTemplate.DefaultCellStyle.ForeColor = _theme.Text;

        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.DefaultCellStyle.BackColor = _theme.InputBack;
            column.DefaultCellStyle.ForeColor = _theme.Text;
            column.DefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
            column.DefaultCellStyle.SelectionForeColor = _theme.Text;
            if (column is DataGridViewButtonColumn buttonColumn)
            {
                buttonColumn.FlatStyle = FlatStyle.Flat;
                buttonColumn.DefaultCellStyle.BackColor = _theme.ButtonBack;
                buttonColumn.DefaultCellStyle.ForeColor = _theme.Text;
                buttonColumn.DefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
                buttonColumn.DefaultCellStyle.SelectionForeColor = _theme.Text;
            }
            else if (column is DataGridViewComboBoxColumn comboColumn)
            {
                comboColumn.FlatStyle = FlatStyle.Flat;
                comboColumn.DefaultCellStyle.BackColor = _theme.InputBack;
                comboColumn.DefaultCellStyle.ForeColor = _theme.Text;
                comboColumn.DefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
                comboColumn.DefaultCellStyle.SelectionForeColor = _theme.Text;
            }
        }

        grid.CellPainting -= OnThemedGridCellPainting;
        grid.CellPainting += OnThemedGridCellPainting;
        grid.EditingControlShowing -= OnThemedGridEditingControlShowing;
        grid.EditingControlShowing += OnThemedGridEditingControlShowing;
        ApplyNativeThemeHint(grid);
        grid.Invalidate();
    }

    private void ApplyThemeToComboBox(ComboBox comboBox)
    {
        comboBox.BackColor = _theme.InputBack;
        comboBox.ForeColor = _theme.Text;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.DrawMode = DrawMode.OwnerDrawFixed;
        comboBox.DrawItem -= OnThemedComboBoxDrawItem;
        comboBox.DrawItem += OnThemedComboBoxDrawItem;
        ApplyNativeThemeHint(comboBox);
        comboBox.Invalidate();
    }

    private void OnThemedComboBoxDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var backColor = selected ? _theme.ButtonHover : _theme.InputBack;
        var textColor = comboBox.Enabled ? _theme.Text : SystemColors.GrayText;
        using var background = new SolidBrush(backColor);
        e.Graphics.FillRectangle(background, e.Bounds);

        var text = e.Index >= 0 && e.Index < comboBox.Items.Count
            ? Convert.ToString(comboBox.Items[e.Index], CultureInfo.CurrentCulture)
            : comboBox.Text;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            comboBox.Font,
            e.Bounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
        {
            e.DrawFocusRectangle();
        }
    }

    private void OnThemedGridEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is ComboBox comboBox)
        {
            ApplyThemeToComboBox(comboBox);
        }
    }

    private void OnThemedGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || e.Graphics is null
            || grid.Columns[e.ColumnIndex] is not DataGridViewButtonColumn)
        {
            return;
        }

        e.Handled = true;
        var selected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;
        using var cellBack = new SolidBrush(selected ? _theme.ButtonHover : _theme.InputBack);
        e.Graphics.FillRectangle(cellBack, e.CellBounds);

        var buttonBounds = Rectangle.Inflate(e.CellBounds, -4, -4);
        if (buttonBounds.Width <= 0 || buttonBounds.Height <= 0)
        {
            return;
        }

        using var buttonBack = new SolidBrush(selected ? _theme.ButtonHover : _theme.ButtonBack);
        using var border = new Pen(_theme.Border);
        e.Graphics.FillRectangle(buttonBack, buttonBounds);
        e.Graphics.DrawRectangle(border, buttonBounds);

        var text = Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture) ?? "";
        var font = e.CellStyle?.Font ?? grid.Font;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            font,
            buttonBounds,
            _theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void ApplyNativeThemeHint(Control control)
    {
        if (control is not (TextBox or NumericUpDown or ComboBox or DataGridView or ScrollableControl { AutoScroll: true }))
        {
            return;
        }

        if (control.IsHandleCreated)
        {
            WindowsControlTheme.ApplyTheme(control.Handle, _theme.IsDark);
        }

        control.HandleCreated -= OnThemedControlHandleCreated;
        control.HandleCreated += OnThemedControlHandleCreated;
    }

    private void OnThemedControlHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            WindowsControlTheme.ApplyTheme(control.Handle, _theme.IsDark);
        }
    }

    private string? ResolveInitialConfigPath(string? requestedConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedConfigPath))
        {
            return FindConfigPath(requestedConfigPath, null);
        }

        var lastConfigPath = TryGetFullPath(_editorState.LastConfigPath ?? "");
        if (lastConfigPath is not null && File.Exists(lastConfigPath))
        {
            if (!IsPathInCurrentReleaseFolder(lastConfigPath))
            {
                return ResolveExternalLastConfigPath(lastConfigPath);
            }

            return lastConfigPath;
        }

        return FindConfigPath(null, null);
    }

    private string? ResolveExternalLastConfigPath(string lastConfigPath)
    {
        var choice = ShowExternalLastConfigPathDialog(lastConfigPath);
        if (choice == ExternalConfigChoice.CopyLastUsedConfig)
        {
            try
            {
                var copiedPath = CopyExternalConfigToCurrentRelease(lastConfigPath);
                SetTemporaryStatus(
                    "Copied and loaded last-used config.",
                    "Copied from:\r\n" + lastConfigPath + "\r\n\r\nNow using:\r\n" + copiedPath);
                return copiedPath;
            }
            catch (Exception ex)
            {
                ShowThemedMessageBox(
                    "Could not copy the last-used config into the current release folder.\r\n\r\n" + ex.Message,
                    "Could not copy config",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        return FindConfigPath(null, null);
    }

    private static string CopyExternalConfigToCurrentRelease(string lastConfigPath)
    {
        var currentReleaseFolder = GetCurrentConfigEditorDirectory();
        var fileName = Path.GetFileName(lastConfigPath);
        if (string.Equals(fileName, DefaultConfigFileName, StringComparison.OrdinalIgnoreCase))
        {
            fileName = "supervisor_moved.config.json";
        }

        var destinationPath = Path.Combine(currentReleaseFolder, fileName);
        if (File.Exists(destinationPath))
        {
            destinationPath = Path.Combine(
                currentReleaseFolder,
                Path.GetFileNameWithoutExtension(fileName) + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Path.GetExtension(fileName));
        }

        File.Copy(lastConfigPath, destinationPath, overwrite: false);
        return destinationPath;
    }

    private ExternalConfigChoice ShowExternalLastConfigPathDialog(string lastConfigPath)
    {
        var currentReleaseFolder = GetCurrentConfigEditorDirectory();
        var lastConfigFolder = global::ScheduledTaskPathValidator.NormalizeDirectory(Path.GetDirectoryName(lastConfigPath) ?? "");
        var choice = ExternalConfigChoice.UseCurrentFolderConfig;
        using var dialog = new Form
        {
            Text = "Config from another release",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(760, 360)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label
        {
            Text = "Last-used config is from another release",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };
        var body = new Label
        {
            Text = "Choose which config this release should use.\r\n\r\n" +
                "Copying the last-used config creates a local copy in the current release folder and loads that copy.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 72,
            Margin = new Padding(0, 0, 0, 12)
        };
        var details = new TextBox
        {
            Text =
                "Current Config Editor folder:\r\n" +
                currentReleaseFolder +
                "\r\n\r\nRemembered config folder:\r\n" +
                lastConfigFolder +
                "\r\n\r\nRemembered config file:\r\n" +
                lastConfigPath,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, Font.Size),
            Margin = new Padding(0, 0, 0, 12)
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var copyButton = CreateButton("Use last-used config", autoSize: false, width: 156, height: 28);
        var currentButton = CreateButton("Use current folder config", autoSize: false, width: 176, height: 28);

        copyButton.Click += (_, _) =>
        {
            choice = ExternalConfigChoice.CopyLastUsedConfig;
            dialog.DialogResult = DialogResult.Yes;
            dialog.Close();
        };
        currentButton.Click += (_, _) =>
        {
            choice = ExternalConfigChoice.UseCurrentFolderConfig;
            dialog.DialogResult = DialogResult.No;
            dialog.Close();
        };

        buttons.Controls.Add(copyButton);
        buttons.Controls.Add(currentButton);
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(details, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        dialog.Controls.Add(root);
        dialog.AcceptButton = copyButton;
        dialog.CancelButton = currentButton;

        ApplyThemeTo(dialog);
        WindowsTitleBar.ApplyTheme(dialog.Handle, _theme.IsDark);
        dialog.ShowDialog(this);
        return choice;
    }

    private static bool IsPathInCurrentReleaseFolder(string path)
    {
        var currentReleaseFolder = GetCurrentConfigEditorDirectory();
        var pathFolder = global::ScheduledTaskPathValidator.NormalizeDirectory(Path.GetDirectoryName(path) ?? "");
        return string.Equals(pathFolder, currentReleaseFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindConfigPath(string? requestedConfigPath, string? lastConfigPath)
    {
        var candidates = new[]
        {
            requestedConfigPath,
            lastConfigPath,
            Path.Combine(AppContext.BaseDirectory, "supervisor.config.json"),
            Path.Combine(Environment.CurrentDirectory, "supervisor.config.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "supervisor.config.json")
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => TryGetFullPath(path!))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .FirstOrDefault(File.Exists);
    }
}

internal static class AppVersion
{
    public static string Current =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}

internal sealed record AutoLaunchAppEditorRow(string Name, string Path, bool Enabled, bool RestartOnPimaxReconnect, bool RunAsAdmin, bool StartMinimized);

internal sealed record OscRouteEditorRow(string Name, int AppReceivePort, bool Enabled);

internal enum ValidationMode
{
    Validate,
    Save,
    Launch
}

internal enum LaunchUnsavedChoice
{
    SaveAndLaunch,
    LaunchWithoutSaving,
    Cancel
}

internal enum StartupTaskMigrationChoice
{
    Rebind,
    KeepExisting,
    TurnOffAutostart
}

internal enum ExternalConfigChoice
{
    CopyLastUsedConfig,
    UseCurrentFolderConfig
}

internal sealed record ConfigProfileItem(string DisplayName, string FileName, string Path)
{
    public override string ToString() => DisplayName;
}

internal sealed record BackupFileItem(string FileName, string Path, DateTime LastWriteTime)
{
    public override string ToString() => $"{FileName} ({LastWriteTime:yyyy-MM-dd HH:mm})";
}

internal sealed class ValidationResult
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool HasErrors => Errors.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
}

internal sealed record FriendlyValidationSummary(string Heading, string Body);

internal sealed class EditorState
{
    private static readonly string StatePath = Path.Combine(Application.UserAppDataPath, "config-editor-state.json");

    public string? LastConfigPath { get; set; }
    public Rectangle? WindowBounds { get; set; }
    public FormWindowState WindowState { get; set; } = FormWindowState.Normal;
    public int LastSelectedTab { get; set; }
    public List<string> StartupTaskMigrationPromptedReleaseFolders { get; set; } = [];

    public static EditorState Load()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return new EditorState();
            }

            return JsonSerializer.Deserialize<EditorState>(File.ReadAllText(StatePath)) ?? new EditorState();
        }
        catch
        {
            return new EditorState();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StatePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Editor state is only a convenience; ignore persistence failures.
        }
    }

    public void MarkStartupTaskMigrationPrompted(string releaseFolder)
    {
        var normalized = ScheduledTaskPathValidator.NormalizeDirectory(releaseFolder);
        StartupTaskMigrationPromptedReleaseFolders ??= [];
        if (!StartupTaskMigrationPromptedReleaseFolders.Any(path => string.Equals(
            ScheduledTaskPathValidator.NormalizeDirectory(path),
            normalized,
            StringComparison.OrdinalIgnoreCase)))
        {
            StartupTaskMigrationPromptedReleaseFolders.Add(normalized);
        }
    }
}

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
    Color Border,
    Color StrongBorder,
    Color PrimaryButtonBack)
{
    public static readonly AppTheme Light = new(
        IsDark: false,
        WindowBack: SystemColors.Control,
        InputBack: SystemColors.Window,
        ButtonBack: SystemColors.ControlLight,
        ButtonHover: Color.FromArgb(229, 241, 251),
        ButtonPressed: Color.FromArgb(204, 228, 247),
        TabBack: SystemColors.Control,
        TabSelectedBack: Color.FromArgb(250, 250, 250),
        ToolTipBack: SystemColors.Info,
        ToolTipFore: SystemColors.InfoText,
        Text: SystemColors.ControlText,
        Border: SystemColors.ControlDark,
        StrongBorder: Color.FromArgb(76, 128, 176),
        PrimaryButtonBack: Color.FromArgb(240, 247, 252));

    public static readonly AppTheme Dark = new(
        IsDark: true,
        WindowBack: Color.FromArgb(32, 32, 32),
        InputBack: Color.FromArgb(45, 45, 45),
        ButtonBack: Color.FromArgb(58, 58, 58),
        ButtonHover: Color.FromArgb(74, 74, 74),
        ButtonPressed: Color.FromArgb(88, 88, 88),
        TabBack: Color.FromArgb(40, 40, 40),
        TabSelectedBack: Color.FromArgb(62, 62, 62),
        ToolTipBack: Color.FromArgb(45, 45, 45),
        ToolTipFore: Color.WhiteSmoke,
        Text: Color.WhiteSmoke,
        Border: Color.FromArgb(86, 86, 86),
        StrongBorder: Color.FromArgb(160, 160, 160),
        PrimaryButtonBack: Color.FromArgb(76, 76, 76));
}

internal sealed class ThemedActionButton : Button
{
    private const int Radius = 4;
    private bool _hovered;
    private bool _pressed;

    public ThemedActionButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        UseVisualStyleBackColor = false;
        Padding = new Padding(8, 2, 8, 2);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var backColor = BackColor;
        if (!Enabled)
        {
            backColor = SystemColors.Control;
        }
        else if (_pressed)
        {
            backColor = ColorOrFallback(FlatAppearance.MouseDownBackColor, BackColor);
        }
        else if (_hovered)
        {
            backColor = ColorOrFallback(FlatAppearance.MouseOverBackColor, BackColor);
        }

        using var path = CreateRoundedRectanglePath(bounds, Radius);
        using var background = new SolidBrush(backColor);
        using var border = new Pen(Enabled ? ColorOrFallback(FlatAppearance.BorderColor, SystemColors.ControlDark) : SystemColors.ControlDark);
        pevent.Graphics.FillPath(background, path);
        pevent.Graphics.DrawPath(border, path);

        var textColor = Enabled ? ForeColor : SystemColors.GrayText;
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            bounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(pevent.Graphics, Rectangle.Inflate(bounds, -4, -4), textColor, backColor);
        }
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ColorOrFallback(Color color, Color fallback)
        => color.IsEmpty ? fallback : color;
}

internal sealed class ThemedTabButton : Button
{
    private const int Radius = 3;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppTheme Theme { get; set; } = AppTheme.Light;

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.Clear(Parent?.BackColor ?? Theme.WindowBack);
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectanglePath(bounds, Radius);
        using var background = new SolidBrush(Selected ? Theme.TabSelectedBack : Theme.TabBack);
        using var border = new Pen(Selected ? Theme.StrongBorder : Theme.Border);
        pevent.Graphics.FillPath(background, path);
        pevent.Graphics.DrawPath(border, path);

        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            bounds,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(bounds, -4, -4);
            ControlPaint.DrawFocusRectangle(pevent.Graphics, focusBounds, ForeColor, Selected ? Theme.TabSelectedBack : Theme.TabBack);
        }
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
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
    private readonly List<(ThemedTabButton Button, Control Content)> _tabs = [];
    private int _selectedIndex = -1;
    private AppTheme _theme = AppTheme.Light;
    public event EventHandler? SelectedIndexChanged;
    public int SelectedIndex => _selectedIndex;
    public int TabCount => _tabs.Count;

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
        var button = new ThemedTabButton
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 30,
            MinimumSize = new Size(76, 30),
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
            button.Selected = selected;
            button.Theme = theme;
            button.BackColor = selected ? theme.TabSelectedBack : theme.TabBack;
            button.ForeColor = theme.Text;
            button.FlatAppearance.BorderColor = selected ? theme.StrongBorder : theme.Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
            button.Invalidate();
            ApplyThemeToContent(_tabs[index].Content);
        }
    }

    public void SelectTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            return;
        }

        if (_selectedIndex == index && _contentPanel.Controls.Count > 0)
        {
            return;
        }

        _selectedIndex = index;
        _contentPanel.SuspendLayout();
        _contentPanel.Controls.Clear();
        _contentPanel.Controls.Add(_tabs[index].Content);
        _contentPanel.ResumeLayout();
        ApplyTheme(_theme);
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyThemeToContent(Control control)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.BackColor = _theme.InputBack;
                textBox.ForeColor = _theme.Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                ApplyNativeThemeHint(textBox);
                break;
            case NumericUpDown input:
                input.BackColor = _theme.InputBack;
                input.ForeColor = _theme.Text;
                ApplyNativeThemeHint(input);
                break;
            case ComboBox comboBox:
                ApplyThemeToComboBox(comboBox);
                break;
            case DataGridView grid:
                ApplyThemeToGrid(grid);
                break;
            case Button button:
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Equals(button.Tag, "Primary") ? _theme.StrongBorder : _theme.Border;
                button.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
                button.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
                button.BackColor = Equals(button.Tag, "Primary") ? _theme.PrimaryButtonBack : _theme.ButtonBack;
                button.ForeColor = _theme.Text;
                button.Invalidate();
                break;
            case CheckBox checkBox:
                checkBox.UseVisualStyleBackColor = false;
                checkBox.BackColor = _theme.WindowBack;
                checkBox.ForeColor = _theme.Text;
                break;
            case Label label:
                label.BackColor = _theme.WindowBack;
                if (!Equals(label.Tag, "PathStatus"))
                {
                    label.ForeColor = Equals(label.Tag, "Muted")
                        ? (_theme.IsDark ? Color.FromArgb(178, 178, 178) : SystemColors.GrayText)
                        : _theme.Text;
                }
                break;
            default:
                control.BackColor = _theme.WindowBack;
                control.ForeColor = _theme.Text;
                ApplyNativeThemeHint(control);
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
        grid.GridColor = _theme.IsDark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(214, 214, 214);
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
        grid.RowTemplate.DefaultCellStyle.BackColor = _theme.InputBack;
        grid.RowTemplate.DefaultCellStyle.ForeColor = _theme.Text;

        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.DefaultCellStyle.BackColor = _theme.InputBack;
            column.DefaultCellStyle.ForeColor = _theme.Text;
            column.DefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
            column.DefaultCellStyle.SelectionForeColor = _theme.Text;
            if (column is DataGridViewButtonColumn buttonColumn)
            {
                buttonColumn.FlatStyle = FlatStyle.Flat;
                buttonColumn.DefaultCellStyle.BackColor = _theme.ButtonBack;
                buttonColumn.DefaultCellStyle.ForeColor = _theme.Text;
                buttonColumn.DefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
                buttonColumn.DefaultCellStyle.SelectionForeColor = _theme.Text;
            }
            else if (column is DataGridViewComboBoxColumn comboColumn)
            {
                comboColumn.FlatStyle = FlatStyle.Flat;
                comboColumn.DefaultCellStyle.BackColor = _theme.InputBack;
                comboColumn.DefaultCellStyle.ForeColor = _theme.Text;
                comboColumn.DefaultCellStyle.SelectionBackColor = _theme.ButtonHover;
                comboColumn.DefaultCellStyle.SelectionForeColor = _theme.Text;
            }
        }

        grid.CellPainting -= OnThemedGridCellPainting;
        grid.CellPainting += OnThemedGridCellPainting;
        grid.EditingControlShowing -= OnThemedGridEditingControlShowing;
        grid.EditingControlShowing += OnThemedGridEditingControlShowing;
        ApplyNativeThemeHint(grid);
        grid.Invalidate();
    }

    private void ApplyThemeToComboBox(ComboBox comboBox)
    {
        comboBox.BackColor = _theme.InputBack;
        comboBox.ForeColor = _theme.Text;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.DrawMode = DrawMode.OwnerDrawFixed;
        comboBox.DrawItem -= OnThemedComboBoxDrawItem;
        comboBox.DrawItem += OnThemedComboBoxDrawItem;
        ApplyNativeThemeHint(comboBox);
        comboBox.Invalidate();
    }

    private void OnThemedComboBoxDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var backColor = selected ? _theme.ButtonHover : _theme.InputBack;
        var textColor = comboBox.Enabled ? _theme.Text : SystemColors.GrayText;
        using var background = new SolidBrush(backColor);
        e.Graphics.FillRectangle(background, e.Bounds);

        var text = e.Index >= 0 && e.Index < comboBox.Items.Count
            ? Convert.ToString(comboBox.Items[e.Index], CultureInfo.CurrentCulture)
            : comboBox.Text;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            comboBox.Font,
            e.Bounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
        {
            e.DrawFocusRectangle();
        }
    }

    private void OnThemedGridEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is ComboBox comboBox)
        {
            ApplyThemeToComboBox(comboBox);
        }
    }

    private void OnThemedGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || e.Graphics is null
            || grid.Columns[e.ColumnIndex] is not DataGridViewButtonColumn)
        {
            return;
        }

        e.Handled = true;
        var selected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;
        using var cellBack = new SolidBrush(selected ? _theme.ButtonHover : _theme.InputBack);
        e.Graphics.FillRectangle(cellBack, e.CellBounds);

        var buttonBounds = Rectangle.Inflate(e.CellBounds, -4, -4);
        if (buttonBounds.Width <= 0 || buttonBounds.Height <= 0)
        {
            return;
        }

        using var buttonBack = new SolidBrush(selected ? _theme.ButtonHover : _theme.ButtonBack);
        using var border = new Pen(_theme.Border);
        e.Graphics.FillRectangle(buttonBack, buttonBounds);
        e.Graphics.DrawRectangle(border, buttonBounds);

        var text = Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture) ?? "";
        var font = e.CellStyle?.Font ?? grid.Font;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            font,
            buttonBounds,
            _theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void ApplyNativeThemeHint(Control control)
    {
        if (control is not (TextBox or NumericUpDown or ComboBox or DataGridView or ScrollableControl { AutoScroll: true }))
        {
            return;
        }

        if (control.IsHandleCreated)
        {
            WindowsControlTheme.ApplyTheme(control.Handle, _theme.IsDark);
        }

        control.HandleCreated -= OnThemedControlHandleCreated;
        control.HandleCreated += OnThemedControlHandleCreated;
    }

    private void OnThemedControlHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            WindowsControlTheme.ApplyTheme(control.Handle, _theme.IsDark);
        }
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

internal static class WindowsControlTheme
{
    public static void ApplyTheme(IntPtr handle, bool dark)
    {
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        try
        {
            _ = SetWindowTheme(handle, dark ? "DarkMode_Explorer" : null, null);
        }
        catch
        {
            // Native scrollbar theming support varies across Windows builds.
        }
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);
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

    public static string Remove(string json, string propertyName)
    {
        var propertyMatch = Regex.Match(json, $"\"{Regex.Escape(propertyName)}\"\\s*:");
        if (!propertyMatch.Success)
        {
            return json;
        }

        var valueStart = propertyMatch.Index + propertyMatch.Length;
        while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
        {
            valueStart++;
        }

        var valueEnd = FindValueEnd(json, valueStart);
        var removeStart = propertyMatch.Index;
        while (removeStart > 0 && json[removeStart - 1] is not '\r' and not '\n')
        {
            removeStart--;
        }

        var removeEnd = valueEnd;
        while (removeEnd < json.Length && json[removeEnd] is ' ' or '\t')
        {
            removeEnd++;
        }

        if (removeEnd < json.Length && json[removeEnd] == ',')
        {
            removeEnd++;
            if (removeEnd < json.Length && json[removeEnd] == '\r')
            {
                removeEnd++;
            }

            if (removeEnd < json.Length && json[removeEnd] == '\n')
            {
                removeEnd++;
            }
        }
        else
        {
            var commaIndex = removeStart - 1;
            while (commaIndex >= 0 && char.IsWhiteSpace(json[commaIndex]))
            {
                commaIndex--;
            }

            if (commaIndex >= 0 && json[commaIndex] == ',')
            {
                removeStart = commaIndex;
            }
        }

        return json[..removeStart] + json[removeEnd..];
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
