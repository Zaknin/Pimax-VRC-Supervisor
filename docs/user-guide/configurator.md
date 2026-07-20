# Configurator

Configurator is the main setup tool. Use it instead of editing JSON by hand whenever possible.

## Config Selector And Display Name

The top area lets you choose a config file. **Display name** is a friendly label shown in the selector and startup output.

- Empty display names fall back to the filename.
- The file path is still the real identity.
- Duplicate display names are shown with the filename so you can tell them apart.
- Very long display names are shortened when saved.

## Tabs

### General

Choose **Terminal UI only**, **SteamVR Overlay only**, **Terminal UI + SteamVR Overlay**, or Off. The one interface-mode control prevents invalid combinations; saving applies the matching watcher, helper task, and SteamVR registration plan.

### Face Tracking

Configure Broken Eye, VRCFaceTracking, Vive Face Tracker options, and reconnect detection.

### Base Stations

Scan for base stations, choose which ones are controlled, and test power actions.

### Auto Startup

Add extra applications to start after the core tools.

### Detectors

Configure device and process detection rules used by reconnect handling.

### Processes

Set process names used to detect and close managed tools.

### OSC Router

Enable the local OSC Router and configure routes.

### OSCGoesBrrr

Configure OscGoesBrrr, Intiface, manual launch behavior, and optional device detection.

### Timers

Tune delays, timeouts, polling, reconnect grace periods, and shutdown timing.

### Raw JSON

Advanced editor for the full config. Use it only when a setting is not exposed in the normal tabs.
