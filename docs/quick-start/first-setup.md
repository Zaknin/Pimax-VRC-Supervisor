# First Setup

## 1. Open Configurator

Run `PimaxVrcSupervisorConfigurator.exe`.

The top of the window shows the selected config file and friendly display name. The display name is only a label; the file path is the real identity.

## 2. Choose Autostart

Open **General** and choose **Autostart mode**:

- **Off**: no managed autostart.
- **Terminal Mode**: starts during SteamVR sessions.
- **SteamVR Overlay**: starts through the SteamVR dashboard overlay path.

For most users, choose **Terminal Mode** and leave **Use Terminal UI as default interface** enabled.

## 3. Set Tool Paths

Fill in the tabs for features you use:

- **Face Tracking**: Broken Eye and VRCFaceTracking.
- **OSCGoesBrrr**: OscGoesBrrr and Intiface.
- **OSC Router**: local OSC routes.
- **Base Stations**: base-station scan and power options.

You can leave unused features disabled.

## 4. Validate

Click **Validate**. Fix missing paths or invalid entries before saving.

## 5. Save And Launch

Click **Save**, then **Launch Supervisor**.

If Terminal UI is the default interface, Launch Supervisor starts the Supervisor and opens Terminal UI. If it is unchecked, Launch Supervisor starts the classic visible console.
