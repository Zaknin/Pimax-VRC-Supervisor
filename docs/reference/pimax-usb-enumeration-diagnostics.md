# Pimax USB Enumeration Diagnostics

`pimax-usb-enumeration-json` is an advanced read-only diagnostic command for comparing Windows USB and PnP evidence across headset states.

Use it when Terminal UI or the Configurator troubleshooting flow is not enough to explain whether Windows can see a Pimax Crystal device, interface, parent node, or related USB topology.

## What It Does

The command collects a broad Windows USB/PnP inventory and prints JSON to standard output.

It records:

- present and non-present device records when Windows exposes them;
- USB, HID, audio, camera, sensor, vendor-specific, and related PnP records;
- sanitized device, parent, container, and location identities;
- VID, PID, class, status, problem code, service, and topology hints;
- candidate-device reasons explaining why a record may be relevant.

The schema is versioned separately as `pimax-usb-enumeration-v1`.

## Safety

The command is diagnostic only. It does not:

- reset USB devices;
- rescan hardware;
- enable, disable, remove, eject, or restart devices;
- restart Pimax Play or Pimax services;
- start or stop SteamVR;
- perform recovery.

Physical LED state is not detected by this command. If a capture is labeled green, white, or blue, that label comes from the user observing the headset.

## Example

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-usb-enumeration-json > pimax-usb-enumeration.json
```

The output can be large. Keep local captures out of Git unless they are explicitly sanitized and requested for a bug report.
