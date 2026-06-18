# Exact external-hub Pimax port-cycle experiment

Phase 28C3B is a CLI-only, operator-confirmed hypothesis test. It does not add automatic recovery to the supervisor, bridge, TUI, overlay, Configurator, or startup path.

## Proven mapping

Phase 28C3A-R physically isolated two reciprocal USB 2/SuperSpeed connector groups on the external Genesys Logic hub:

- Pimax: `connector:97c673a6423e4da4`, external `05E3:0610` index 4 and reciprocal `05E3:0626` index 4.
- Vive face tracker: `connector:be6df8ea5b2ce0d2`, USB 2 and SuperSpeed index 2.

The first experiment targets only the Pimax USB 2 logical side. Physical reconnect evidence showed that SuperSpeed arrived before USB 2, but that ordering does not prove which side controls power or registration. A successful API response also does not prove that registration recovered or that the request reproduced a physical unplug.

## Command and modes

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-usb-port-cycle-experiment-json --mode dry-run `
  --target-signature <outside-git-target.json> `
  --observer-status <observer-status.json> `
  --marker-file <markers.jsonl>
```

The command emits one `pimax-usb-port-cycle-experiment-v1` JSON document. Its modes are:

- `dry-run`: collect and validate current state, then issue a five-minute one-time token only when every guard passes.
- `prepare`: require the exact operator phrase and signed token, consume the token atomically, write the privileged request, and optionally launch the helper through UAC.
- `execute-elevated-helper`: reserved for `PimaxVrcSupervisor.PortCycleHelper.exe`.
- `observe-result`: perform bounded read-only post-request comparison.

The target signature and all request/result files contain machine-specific identifiers and must remain outside Git.

## Safety boundary

The dry run binds the token to the complete current hub identities, container and location data, reciprocal companion mapping, Pimax and Vive connector groups, descendant inventories, unrelated-port inventory, registration assessment, Pimax Play and SteamVR states, observer session, scenario, and immutable Connect marker identity. Semantically unordered inventories and identity arrays are normalized, deduplicated, and sorted before hashing. It also verifies that the active Phase 29B task, watcher hash, and persistent logs remain intact.

The marker order must be `observer-started`, `pimax-info-opened`, `pimax-crystal-model-selected`, `connect-ready-before-action`, then `connect-action-completed`. The final marker represents both the Connect press and immediate scan start; there is no later scan-active marker. The observer status must still be `running` and fresh. Marker age is diagnostic only and is excluded from stable token/request identity. Every safety boundary recalculates age from the immutable marker timestamp and rejects a marker older than 120 seconds. Preparation re-collects state, requires `CONFIRM EXACT PIMAX USB2 PORT CYCLE EXPERIMENT`, and rejects an expired, reused, or mismatched token.

The UAC helper verifies the request SHA-256, signed token, expiry, nonce, elevation, its own executable identity, and all current guards. Its native adapter opens only the exact USB 2 hub interface and makes one `IOCTL_USB_HUB_CYCLE_PORT` call with an eight-byte `USB_CYCLE_PORT_PARAMS` buffer for one-based connection index 4. The API is `FILE_ANY_ACCESS`, so the hub handle requests zero desired access. The helper writes its result atomically outside Git; stdout is not its result channel.

There is no SuperSpeed fallback, second request, retry, device disable/enable/remove/eject/uninstall, devnode rescan, hub/controller reset, service or process restart, or UI automation.

## Observation and restoration

Observation must begin before the Connect readiness marker and remain uninterrupted through the UAC request and final registration result. Post-request observation distinguishes no transition, USB 2 only, both companion sides, partial descendant return, full descendant return, unexpected Vive change, unrelated-port change, timeout, and registration-ready.

If registration remains unavailable, finish the official software evidence first. Do not send another request. Start the documented manual-restoration observation, press Connect only after readiness, and physically reseat only the Pimax USB cable while its scan is active. Manual restoration is never automated.

## Result categories

- Full reconnect and registration: consider a later manual operator-confirmed recovery phase.
- USB 2 only: design, but do not execute, a separate paired-companion experiment.
- Partial recovery: analyze the partial outcome.
- Accepted with no transition: investigate hub-driver support.
- Unsupported or rejected: retain operator-guided physical reseat.
- Vive or unrelated-port change: safety failure.
- Spontaneous pre-mutation recovery: repeat timing controls.
- Invalid evidence: repeat preparation without another mutation.

No result from this phase authorizes automatic recovery.

## Phase 28C3B-R result and paired boundary

The corrected USB 2-only request succeeded technically: one request was accepted for `05E3:0610` index 4 and returned Win32/status zero. The USB 2 side transitioned, the `05E3:0626` SuperSpeed side did not, and registration remained unavailable with Pimax Play error 10500. Manual physical Pimax USB restoration was required.

Paired behavior has not been tested. Phase 28C3C adds only a non-mutating design report and fake-adapter simulation. It does not add a paired UAC request, helper mode, native adapter, hub-handle opening path, or hardware execution path, and it does not claim that a future paired cycle will reproduce a physical reseat.
