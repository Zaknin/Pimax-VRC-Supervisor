# XSOverlay Brokered Restart

Phase31A stopped XSOverlay before the secondary-monitor transition, waited for the display topology to settle, and restarted it afterward. The original restart implementation started the captured `XSOverlay.exe` directly. Live comparison showed that this direct Supervisor-owned process restored the VR overlay but created a small blank desktop window, unlike the original SteamVR-brokered process.

Phase31C therefore does not use a bare `XSOverlay.exe` restart. It discovers a validated launch target before stopping the original process and uses this priority order:

1. An installed XSOverlay Steam application manifest in a validated Steam library, launched through `steam://rungameid/<app-id>` using Shell execution.
2. A registered OpenVR application manifest that identifies XSOverlay, has a validated binary identity, and is launched through the registered OpenVR application key.
3. No automatic restart. The coordinator refuses the known-bad direct executable route and records a user-facing failure.

Steam discovery reads only the Steam installation location, `libraryfolders.vdf`, and candidate `appmanifest_*.acf` files. An app ID is accepted only when its manifest identifies XSOverlay and its installed executable exactly matches the stopped process. The usual `1173510` value is therefore evidence-backed rather than an unconditional constant.

The coordinator owns only the one XSOverlay process that existed before the monitor transition. It checks for an independently reappeared instance before making one brokered request, verifies that exactly one process appears afterward, and never retries in a loop. If monitor shutdown or settle fails after a Supervisor-owned stop, it still makes that single best-effort brokered restart. SteamVR and VRChat are not restarted.

Diagnostics retain `xsOverlaySafeMonitorShutdown` and add launch-target discovery, brokered-request, direct-restart-refusal, duplicate-prevention, process-verification, and desktop-window-verification events. The selected route, validated app ID where available, safely reduced registration source, request method, process result, and window-observation result are included without logging unrelated Steam data.

Desktop-window verification is observational. A visible XSOverlay top-level desktop window is reported as a regression; an unavailable observer is recorded as unavailable rather than guessed. Window absence is not the only hard process-restart gate, but a process alone is not reported as a clean window-regression resolution.
