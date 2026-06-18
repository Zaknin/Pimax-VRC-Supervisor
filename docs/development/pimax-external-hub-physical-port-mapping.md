# Pimax External-Hub Physical Port Mapping

`pimax-usb-physical-port-map-json` is a CLI-only, read-only diagnostic for mapping Windows USB 2 and SuperSpeed logical hub paths to physical downstream connectors. It emits exactly one JSON document with schema `pimax-usb-physical-port-map-v1`.

## Physical Setup and Motivation

The headset uses one USB 3 cable connected to one port of an external seven-connector USB 3 hub. The Vive face tracker uses another physical port, and DisplayPort is separate. One USB 3 connector can expose a USB 2 logical path and a SuperSpeed logical path, so multiple PnP branches do not imply multiple headset cables.

Phase 28C3 found no safe common PnP ancestor for every Pimax branch. The first common scopes were a root hub and xHCI controller, both of which contained unrelated devices. Exact-device re-enumeration was therefore not implemented. PnP ancestry describes devnode ownership; hub connector topology describes the physical socket. They are related but not interchangeable.

## Command

Snapshot mode:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-usb-physical-port-map-json
```

Bounded observation mode:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-usb-physical-port-map-json `
  --scenario physical-disconnect-reconnect `
  --duration-seconds 300 `
  --sample-interval-ms 500 `
  --assessment-interval-ms 2000 `
  --output-dir "C:\path\outside\the\repository" `
  --marker-file "C:\path\outside\the\repository\markers.jsonl"
```

The output directory receives bounded baseline, marker checkpoint, final, and observer-status documents. Machine-specific paths and identifiers belong only in external evidence and must not be committed.

## Read-Only Windows Queries

The mapper enumerates `GUID_DEVINTERFACE_USB_HUB`, opens each hub interface with zero desired access, and uses only these query operations:

- `IOCTL_USB_GET_NODE_INFORMATION`
- `IOCTL_USB_GET_HUB_INFORMATION_EX`
- `IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX`
- `IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2`
- `IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES`
- `IOCTL_USB_GET_NODE_CONNECTION_NAME`
- `IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME`

Connector-property and variable-name buffers are length-checked before decoding. Unsupported connector properties are reported as limitations. No query failure is converted into an assumed relationship.

## Connector Grouping

Connection indices are one-based logical port numbers on a hub interface. Windows connector properties may identify a companion port number and companion-hub symbolic link. Reciprocal metadata between the USB 2 and SuperSpeed sides creates an API-confirmed physical connector group. One-sided metadata is retained with reduced confidence and contrary evidence.

Observation-inferred pairing requires repeated synchronized occupancy transitions plus consistent ancestry. Timing alone is insufficient. Stable connection indices, matching hub enclosure evidence, location paths, and reciprocal metadata increase confidence; contradictions remain explicit.

The mapper classifies descendants from exact ancestry and observed hardware identities. Known Pimax evidence includes Crystal `34A4:0012`, the `05E3:0608` Valve/HID/composite branch, and EyeChip `2104:0220`. A Vive face tracker must be independently identified from its actual current devnode and ancestry. Friendly name alone is insufficient.

## Current Pre-Capture Topology

The initial read-only snapshot identifies the external enclosure's USB 2 side as `05E3:0610` and its SuperSpeed companion as `05E3:0626`. Reciprocal connector metadata maps the Pimax cable to connection index 4 on both logical sides. The downstream `0424:2137` / `0424:5537` pair is Pimax-internal and must not be mistaken for the external enclosure merely because it exposes seven ports.

The official disconnect/reconnect scenario must not begin until the Vive face tracker has a separately identified connector group. A missing or ambiguous Vive mapping is a stop condition, not evidence that it shares the Pimax connector.

## Observer-Before-Action Protocol

Every user action follows this sequence:

1. Start the bounded observer and verify `observer-status.json` is advancing.
2. Capture the complete hub, port, PnP, connectivity, registration, process, and service baseline.
3. Record a readiness marker before giving an action instruction.
4. Record the immediate action-completed marker while observation continues.
5. Wait for a stable topology and record a result-observed marker.

The unplug and reconnect use one uninterrupted observer. The user disconnects only the Pimax USB cable and reconnects it to the exact same physical port. The Vive face tracker and DisplayPort remain connected. An action performed before observer start invalidates the scenario.

## No-Mutation Boundary

This diagnostic does not cycle or power a port, reset a hub or controller, disable/enable/remove/eject a device, request a machine-wide rescan, restart Pimax services or processes, automate Pimax Play, start SteamVR, or expose a bridge, lifecycle, TUI, overlay, or Configurator action. Root-hub and xHCI mutation remain prohibited.

## Outcome Requirements

An exact future target requires the external hub pair, both connection indices, reciprocal companion evidence, a Pimax-only occupant signature, explicit Vive-port exclusion, and an unchanged unrelated-port inventory. Without all of those conditions, no exact-port cycle experiment is justified.
