using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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

    private readonly TextBox _configPathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _brokenEyePathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _vrcFaceTrackingPathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _intifacePathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _oscGoesBrrrPathTextBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _brokenEyeStartMinimizedCheckBox = new() { Text = "Start Broken Eye minimized", AutoSize = true };
    private readonly CheckBox _vrcFaceTrackingStartMinimizedCheckBox = new() { Text = "Start VRCFaceTracking minimized", AutoSize = true };
    private readonly CheckBox _intifaceStartMinimizedCheckBox = new() { Text = "Start Intiface minimized", AutoSize = true };
    private readonly CheckBox _oscGoesBrrrStartMinimizedCheckBox = new() { Text = "Start OscGoesBrrr minimized", AutoSize = true };
    private readonly CheckBox _oscGoesBrrrEnabledCheckBox = new() { Text = "Enabled", AutoSize = true };
    private readonly CheckBox _oscGoesBrrrHotkeyCheckBox = new() { Text = "Enable L hotkey launch", AutoSize = true };
    private readonly CheckBox _oscGoesBrrrBleScannerCheckBox = new() { Text = "Enable BLE scanner", AutoSize = true };
    private readonly CheckBox _baseStationsEnabledCheckBox = new() { Text = "Enable base station power automation", AutoSize = true };
    private readonly ComboBox _baseStationPowerDownModeComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _mouthTrackerCheckBox = CreateOptionalConfigCheckBox("Use Vive mouth tracker");
    private readonly CheckBox _turnOffMonitorsCheckBox = CreateOptionalConfigCheckBox("Turn off secondary monitors during headset sessions");
    private readonly CheckBox _autoLaunchTaskCheckBox = CreateOptionalConfigCheckBox("Create/evaluate VRChat auto-launch Scheduled Task");
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
    private readonly DataGridView _baseStationsGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        RowHeadersVisible = false
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
        Text = $"Pimax VRC Supervisor Config Editor {AppVersion.Current}";
        SetWindowIconFromExecutable();
        MinimumSize = new Size(900, 660);
        Size = new Size(1180, 760);
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
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(0, 0, 0, 10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var browseButton = new Button { Text = "Browse...", Width = 96, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        browseButton.Click += (_, _) => BrowseConfig();
        _toolTips.SetToolTip(browseButton, "Choose the supervisor.config.json file you want this editor to load.");

        var reloadButton = new Button { Text = "Reload", Width = 86, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        reloadButton.Click += (_, _) => LoadConfig(_configPathTextBox.Text);
        _toolTips.SetToolTip(reloadButton, "Reload values from disk and discard unsaved edits in the editor.");

        var configPathLabel = new Label { Text = "Config file", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
        _configPathTextBox.Dock = DockStyle.Fill;
        _configPathTextBox.Margin = new Padding(0, 3, 6, 0);
        browseButton.Margin = new Padding(0, 0, 6, 0);
        reloadButton.Margin = new Padding(0);
        _toolTips.SetToolTip(configPathLabel, "The JSON file that will be loaded and saved.");
        _toolTips.SetToolTip(_configPathTextBox, "Usually this is supervisor.config.json next to the supervisor exe.");
        layout.Controls.Add(configPathLabel, 0, 0);
        layout.Controls.Add(_configPathTextBox, 1, 0);
        layout.Controls.Add(browseButton, 2, 0);
        layout.Controls.Add(reloadButton, 3, 0);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildTabs()
    {
        var tabs = new ThemedTabHost { Dock = DockStyle.Fill };
        tabs.AddTab("Basics", BuildBasicsTab());
        tabs.AddTab("Base Stations", BuildBaseStationsTab());
        tabs.AddTab("Auto Launch", BuildAutoLaunchTab());
        tabs.AddTab("OSCGoesBrrr", BuildLovenseTab());
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
        AddFullWidth(layout, _brokenEyeStartMinimizedCheckBox, "Checked means the supervisor starts Broken Eye minimized and tries to minimize its main window after launch.");
        AddPathRow(
            layout,
            "VRCFaceTracking.exe",
            _vrcFaceTrackingPathTextBox,
            "Full path to VRCFaceTracking.exe. The supervisor starts this after Broken Eye settles.",
            DefaultVrcFaceTrackingDirectory);
        AddFullWidth(layout, _vrcFaceTrackingStartMinimizedCheckBox, "Checked means the supervisor starts VRCFaceTracking minimized and tries to minimize its main window after launch.");
        AddFullWidth(layout, _mouthTrackerCheckBox, "Checked means you use a Vive mouth tracker. Unchecked disables mouth-tracker monitoring. Filled square is shown only when the config leaves the first-run question enabled.");
        AddFullWidth(layout, _turnOffMonitorsCheckBox, "Checked saves the current monitor layout and disables secondary monitors during the VR session. The layout is restored after VRChat and SteamVR close.");
        AddFullWidth(layout, _autoLaunchTaskCheckBox, "Checked lets the app create or repair the elevated auto-launch Scheduled Task. Filled square is shown only when the config asks on first setup.");
        AddFullWidth(layout, _usePimaxLogCheckBox, "Also scan PiService logs for quick HID remove/add reconnects that normal USB polling can miss.");
        AddFullWidth(layout, _useMouthTrackerPnPCheckBox, "Also scan Windows Kernel-PnP events for quick mouth tracker reconnects that normal USB polling can miss.");
        AddLabeledRow(layout, "PiService log folder", _pimaxServiceLogDirectoryTextBox, "Folder containing PiService__*.log files. Environment variables such as %LOCALAPPDATA% are expanded by the supervisor.");

        return layout;
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
        var scanButton = new Button { Text = "Scan", AutoSize = true };
        scanButton.Click += async (_, _) => await ScanBaseStationsAsync();
        _toolTips.SetToolTip(scanButton, "Scan nearby Bluetooth LE devices for SteamVR base stations and merge them into the list.");
        buttons.Controls.Add(scanButton);
        var refreshStateButton = new Button { Text = "Refresh State", AutoSize = true };
        refreshStateButton.Click += async (_, _) => await RefreshBaseStationStatesAsync();
        _toolTips.SetToolTip(refreshStateButton, "Read live power state from enabled Base Station 2.0 devices when firmware supports it.");
        buttons.Controls.Add(refreshStateButton);
        var turnOnButton = new Button { Text = "Turn On", AutoSize = true };
        turnOnButton.Click += async (_, _) => await SendBaseStationPowerOnToEnabledRowsAsync();
        _toolTips.SetToolTip(turnOnButton, "Power on every enabled base station in the list.");
        buttons.Controls.Add(turnOnButton);
        var turnOffButton = new Button { Text = "Turn Off", AutoSize = true };
        turnOffButton.Click += async (_, _) => await SendBaseStationCommandToEnabledRowsAsync(
            "Turn Off",
            (client, baseStation, token) => client.PowerDownAsync(baseStation, SelectedBaseStationPowerDownMode(), token));
        _toolTips.SetToolTip(turnOffButton, "Power down every enabled base station using the selected power-down mode.");
        buttons.Controls.Add(turnOffButton);
        var addManualButton = new Button { Text = "Add Manual", AutoSize = true };
        addManualButton.Click += (_, _) => AddBaseStationGridRow(new BaseStationDevice
        {
            FriendlyName = "Base station",
            Name = "",
            BluetoothAddress = "",
            Version = BaseStationVersion.V2,
            Enabled = true
        });
        _toolTips.SetToolTip(addManualButton, "Add a base station row manually if Windows discovery does not expose it.");
        buttons.Controls.Add(addManualButton);

        ConfigureBaseStationsGrid();
        layout.Controls.Add(settings, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(_baseStationsGrid, 0, 2);
        return layout;
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
        _baseStationsGrid.DefaultValuesNeeded += OnBaseStationsGridDefaultValuesNeeded;
    }

    private void OnBaseStationsGridDefaultValuesNeeded(object? sender, DataGridViewRowEventArgs e)
    {
        e.Row.Cells["Enabled"].Value = true;
        e.Row.Cells["Version"].Value = BaseStationVersion.V2.ToString();
    }

    private async Task ScanBaseStationsAsync()
    {
        try
        {
            SetStatus("Scanning for base stations...");
            var discovered = await BaseStationDiscovery.ScanAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            foreach (var baseStation in discovered)
            {
                UpsertBaseStationGridRow(baseStation.WithDefaults());
            }

            SetStatus($"Base station scan complete. Found {discovered.Count} station(s).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Base station scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show(this, "Standby is only supported for Base Station 2.0. Use Sleep for Base Station 1.0.", "Standby unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (columnName == "Identify" && !baseStation.SupportsStandby)
        {
            MessageBox.Show(this, "Identify is only supported for Base Station 2.0.", "Identify unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetStatus($"{columnName} test for {baseStation.DisplayName}...");
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

            SetStatus($"{columnName} test succeeded for {baseStation.DisplayName}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Base station command failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        SetStatus($"{action}: controlling {baseStations.Length} base station(s)...");
        for (var index = 0; index < baseStations.Length; index++)
        {
            var baseStation = baseStations[index];
            SetStatus($"{action}: {index + 1}/{baseStations.Length} {baseStation.DisplayName}...");
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

        MessageBox.Show(this, string.Join(Environment.NewLine, failures), action + " failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SetStatus($"Turn On: waiting before pass {pass}/{BaseStationCommandTiming.PowerOnPasses}...");
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
            MessageBox.Show(this, string.Join(Environment.NewLine, missing), "Turn On failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        SetStatus($"{action}: controlling {baseStations.Length} base station(s)...");
        for (var index = 0; index < baseStations.Length; index++)
        {
            var baseStation = baseStations[index];
            SetStatus($"{action}: {index + 1}/{baseStations.Length} {baseStation.DisplayName}...");
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

    private Control BuildLovenseTab()
    {
        var layout = CreateFormLayout(3);

        AddFullWidth(layout, _oscGoesBrrrEnabledCheckBox, "Checked means the OscGoesBrrr workflow is available during headset sessions.");
        AddFullWidth(layout, _oscGoesBrrrHotkeyCheckBox, "Checked means the supervisor shows Press L to launch OSCGoesBrrr and starts Intiface plus OscGoesBrrr when L is pressed.");
        AddFullWidth(layout, _oscGoesBrrrBleScannerCheckBox, "Checked means the supervisor scans nearby BLE advertisements for Lovense names such as LVS- and auto-launches the workflow when one matches.");
        AddPathRow(layout, "intiface_central.exe", _intifacePathTextBox, "Full path to intiface_central.exe. This starts first when a Lovense detector rule matches.", Path.GetDirectoryName(DefaultIntifacePath));
        AddFullWidth(layout, _intifaceStartMinimizedCheckBox, "Checked means the supervisor starts Intiface minimized and tries to minimize its main window after launch.");
        AddPathRow(layout, "OscGoesBrrr.exe", _oscGoesBrrrPathTextBox, "Full path to OscGoesBrrr.exe. This starts after Intiface is running.", Path.GetDirectoryName(DefaultOscGoesBrrrPath));
        AddFullWidth(layout, _oscGoesBrrrStartMinimizedCheckBox, "Checked means the supervisor starts OscGoesBrrr minimized and tries to minimize its main window after launch.");
        AddLabeledRow(layout, "Intiface process names", _intifaceProcessesTextBox, "Process names used to detect, attach to, and close Intiface. .exe is optional.");
        AddLabeledRow(layout, "OscGoesBrrr process names", _oscGoesBrrrProcessesTextBox, "Process names used to detect, attach to, and close OscGoesBrrr. .exe is optional.");
        AddNumber(layout, "DelayBeforeOscGoesBrrrSeconds", 0, 3600, "Seconds to wait after Intiface is running before starting OscGoesBrrr.");
        AddNumber(layout, "OscGoesBrrrBleScanSeconds", 1, 3600, "Seconds each BLE scan burst runs.");
        AddNumber(layout, "OscGoesBrrrBleScanIntervalSeconds", 1, 3600, "Seconds to wait after each unsuccessful BLE scan before trying again.");
        AddFullWidth(layout, BuildLovenseDetectorPanel(), "Each line is one possible Lovense match rule. Defaults include Lovense and LVS- for common Bluetooth/WebBluetooth names.");

        return layout;
    }

    private Control BuildLovenseDetectorPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            Height = 180,
            Margin = new Padding(0, 8, 0, 0)
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
        return panel;
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

        var row = selectedRow.IsNewRow
            ? _autoLaunchAppsGrid.Rows[AddAutoLaunchAppGridRow("", "", enabled: true, restartOnPimaxReconnect: true, runAsAdmin: false, startMinimized: false)]
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
            ColumnCount = 4,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var launchButton = new Button { Text = "Launch", AutoSize = true };
        launchButton.Click += (_, _) => LaunchSupervisor();
        _toolTips.SetToolTip(launchButton, "Launch PimaxVrcSupervisor.exe from the same folder as this config editor.");

        var saveButton = new Button { Text = "Save", AutoSize = true };
        saveButton.Click += (_, _) => SaveConfig();
        _toolTips.SetToolTip(saveButton, "Save the current editor values into the selected config file.");

        var saveAsButton = new Button { Text = "Save As...", AutoSize = true };
        saveAsButton.Click += (_, _) => SaveConfigAs();
        _toolTips.SetToolTip(saveAsButton, "Choose a different JSON file path, then save the current editor values.");

        layout.Controls.Add(_statusLabel, 0, 0);
        layout.Controls.Add(launchButton, 1, 0);
        layout.Controls.Add(saveAsButton, 2, 0);
        layout.Controls.Add(saveButton, 3, 0);
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
            var defaultFileName = string.IsNullOrWhiteSpace(Path.GetFileName(textBox.Text))
                ? label
                : Path.GetFileName(textBox.Text);
            using var dialog = new OpenFileDialog
            {
                Title = "Select " + label,
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                FileName = defaultFileName
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

    private void LaunchSupervisor()
    {
        if (!ConfirmUnsavedChangesBefore("launching the supervisor"))
        {
            return;
        }

        var supervisorPath = Path.Combine(AppContext.BaseDirectory, "PimaxVrcSupervisor.exe");
        if (!File.Exists(supervisorPath))
        {
            MessageBox.Show(this, $"Could not find {supervisorPath}.", "Could not launch supervisor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = supervisorPath,
                WorkingDirectory = Path.GetDirectoryName(supervisorPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            SetStatus("Launched " + supervisorPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not launch supervisor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Launch failed.");
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
                PopulateControls(null);
                _loadedJson = BuildCurrentJson();
                SetStatus("Config file does not exist yet. Fill values, then Save.");
                return;
            }

            var loadedJson = File.ReadAllText(path);
            _rawJsonTextBox.Text = loadedJson;
            var node = ParseJson(loadedJson);
            PopulateControls(node);
            _loadedJson = BuildCurrentJson();
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
        _intifacePathTextBox.Text = GetStringOrDefault(node, "IntifacePath", DefaultIntifacePath);
        _oscGoesBrrrPathTextBox.Text = GetStringOrFallbackOrDefault(node, "OscGoesBrrrPath", "OscGoesBrrrrPath", DefaultOscGoesBrrrPath);
        _brokenEyeStartMinimizedCheckBox.Checked = GetBool(node, "BrokenEyeStartMinimized", defaultValue: false);
        _vrcFaceTrackingStartMinimizedCheckBox.Checked = GetBool(node, "VrcFaceTrackingStartMinimized", defaultValue: false);
        _intifaceStartMinimizedCheckBox.Checked = GetBool(node, "IntifaceStartMinimized", defaultValue: false);
        _oscGoesBrrrStartMinimizedCheckBox.Checked = GetBool(node, "OscGoesBrrrStartMinimized", defaultValue: false);
        _oscGoesBrrrEnabledCheckBox.Checked = GetBoolOrFallback(node, "OscGoesBrrrEnabled", "LovenseAutoLaunchEnabled", defaultValue: false);
        _oscGoesBrrrHotkeyCheckBox.Checked = GetBool(node, "OscGoesBrrrHotkeyEnabled", defaultValue: true);
        _oscGoesBrrrBleScannerCheckBox.Checked = GetBool(node, "OscGoesBrrrBleScannerEnabled", defaultValue: false);
        _mouthTrackerCheckBox.CheckState = GetBoolCheckState(node, "MouthTrackerUser");
        _turnOffMonitorsCheckBox.CheckState = GetBoolCheckState(node, "TurnOffSecondaryMonitors");
        _autoLaunchTaskCheckBox.CheckState = GetBoolCheckState(node, "AutoLaunchScheduledTask");
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

    private bool SaveConfig()
    {
        try
        {
            var path = _configPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Choose a config file path before saving.");
            }

            CommitAutoLaunchAppsGridEdits();
            CommitBaseStationsGridEdits();
            var json = ApplyControlValues(_rawJsonTextBox.Text);
            ParseJson(json);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _loadedJson = json;
            _rawJsonTextBox.Text = json;
            SetStatus("Saved " + path);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save config", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Save failed.");
            return false;
        }
    }

    private string BuildCurrentJson()
    {
        CommitAutoLaunchAppsGridEdits();
        CommitBaseStationsGridEdits();
        return ApplyControlValues(_rawJsonTextBox.Text);
    }

    private bool HasUnsavedChanges()
    {
        try
        {
            return !string.Equals(BuildCurrentJson(), _loadedJson, StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    private bool ConfirmUnsavedChangesBefore(string action)
    {
        if (!HasUnsavedChanges())
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
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

        base.OnFormClosing(e);
    }

    private string ApplyControlValues(string baseJson)
    {
        var json = string.IsNullOrWhiteSpace(baseJson) ? "{\r\n}\r\n" : baseJson;
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
        json = JsonPropertyEditor.Replace(json, "MouthTrackerUser", SerializeTriState(_mouthTrackerCheckBox.CheckState));
        json = JsonPropertyEditor.Replace(json, "TurnOffSecondaryMonitors", SerializeTriState(_turnOffMonitorsCheckBox.CheckState));
        json = JsonPropertyEditor.Replace(json, "AutoLaunchScheduledTask", SerializeTriState(_autoLaunchTaskCheckBox.CheckState));
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
        return _baseStationsGrid.Rows.Add(
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
            Id = GetGridString(row, "Id"),
            PowerStateReadUnsupported = GetGridBool(row, "PowerStateReadUnsupported", defaultValue: false)
        }.WithDefaults();
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
                SetStatus($"Reading state for {baseStation.DisplayName}...");
                var state = await client.ReadPowerStateAsync(baseStation, CancellationToken.None);
                row.Cells["State"].Value = state;
                row.Cells["PowerStateReadUnsupported"].Value = state == BaseStationPowerState.Unsupported;
                updated++;
            }
            catch (Exception ex)
            {
                row.Cells["State"].Value = "Unknown";
                SetStatus($"Could not read state for {baseStation.DisplayName}: {ex.Message}");
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

    private static CheckBox CreateOptionalConfigCheckBox(string text)
    {
        return new CheckBox
        {
            Text = text + " (filled square = set by config)",
            AutoSize = true,
            ThreeState = false,
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
            case ComboBox comboBox:
                comboBox.BackColor = _theme.InputBack;
                comboBox.ForeColor = _theme.Text;
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

internal static class AppVersion
{
    public static string Current =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}

internal sealed record AutoLaunchAppEditorRow(string Name, string Path, bool Enabled, bool RestartOnPimaxReconnect, bool RunAsAdmin, bool StartMinimized);

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
            case ComboBox comboBox:
                comboBox.BackColor = _theme.InputBack;
                comboBox.ForeColor = _theme.Text;
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
