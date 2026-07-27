# XSOverlay Brokered Restart

Phase31A stopped XSOverlay before the secondary-monitor transition, waited for the display topology to settle, and restarted it afterward. The original restart implementation started the captured `XSOverlay.exe` directly. Live comparison showed that this direct Supervisor-owned process restored the VR overlay but created a small blank desktop window, unlike the original SteamVR-brokered process.

Phase31C therefore does not use a bare `XSOverlay.exe` restart. It discovers a validated launch target before stopping the original process and uses this priority order:

1. An installed XSOverlay Steam application manifest in a validated Steam library, launched through `steam://rungameid/<app-id>` using Shell execution.
2. A registered OpenVR application manifest that identifies XSOverlay, has a validated binary identity, and is launched through the registered OpenVR application key.
3. No automatic restart. The coordinator refuses the known-bad direct executable route and records a user-facing failure.

Steam discovery reads only the Steam installation location, `libraryfolders.vdf`, and candidate `appmanifest_*.acf` files. An app ID is accepted only when its manifest identifies XSOverlay and its installed executable exactly matches the stopped process. The usual `1173510` value is therefore evidence-backed rather than an unconditional constant.

The coordinator owns only the one XSOverlay process that existed before the monitor transition. It checks for an independently reappeared instance before making one brokered request, verifies that exactly one process appears afterward, and never retries in a loop. If monitor shutdown or settle fails after a Supervisor-owned stop, it still makes that single best-effort brokered restart. SteamVR and VRChat are not restarted.

Phase31C emitted structured `xsOverlaySafeMonitorShutdown` events through the optional Supervisor diagnostics path only. With the default `DiagnosticsLogSupervisor=false` and `DiagnosticsDebugSupervisor=false`, those events were not durable unless the broader Supervisor diagnostics were explicitly enabled.

Phase31D adds a dedicated XSOverlay operational event journal. It writes one compact JSON object per emitted transition event to `%LOCALAPPDATA%\PimaxVrcSupervisor\Diagnostics\XSOverlay\xs-overlay-supervisor.jsonl`. The journal is always available for actual XSOverlay-safe monitor-transition evaluations, but it does not enable general Supervisor diagnostics, verbose debug diagnostics, continuous Steam tracing, process-inventory dumps, or polling outside the transition. `DiagnosticsLogSupervisor` still controls broader Supervisor diagnostic logging, and `DiagnosticsDebugSupervisor` still controls verbose debug logging.

The active file rotates before it would exceed 1 MiB. Two rotated generations are retained as `xs-overlay-supervisor.jsonl.1` and `xs-overlay-supervisor.jsonl.2`; `.1` becomes `.2`, active becomes `.1`, and the new event is written to a fresh active file. Write, serialization, directory, append, rotation, flush, and disposal failures are best-effort and never block XSOverlay detection, stop, monitor shutdown, topology settling, brokered restart, process verification, SteamVR, VRChat, or Supervisor shutdown. BaseStations diagnostics remain independent, and the broader project-wide diagnostics redesign is deferred to a later phase.

Each record includes schema version `xs-overlay-transition-diagnostics-v1`, UTC timestamp, operation `xsOverlaySafeMonitorShutdown`, event type, correlation ID, Supervisor process ID, and the existing coordinator payload fields available for that event. The correlation ID is generated once per transition invocation, shared by all events from that invocation, and differs across separate invocations. The selected route, validated app ID where available, safely reduced registration source, request method, process result, and window-observation result are included without logging unrelated Steam data.

Desktop-window verification is observational. A visible XSOverlay top-level desktop window is reported as a regression; an unavailable observer is recorded as unavailable rather than guessed. Window absence is not the only hard process-restart gate, but a process alone is not reported as a clean window-regression resolution.
