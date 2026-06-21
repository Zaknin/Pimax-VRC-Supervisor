# Pimax Process-Group Launch Readiness

Phase 28D2-B2 adds a read-only model for Pimax Play/runtime startup orchestration. It does not execute the recipe.

## Command

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-launch-recipe-json
dotnet .\PimaxVrcSupervisor.dll pimax-startup-sources-json
dotnet .\PimaxVrcSupervisor.dll pimax-startup-observe-json --fake
dotnet .\PimaxVrcSupervisor.dll pimax-startup-observe-elevated-json --preflight-only
dotnet .\PimaxVrcSupervisor.dll pimax-startup-creator-chain-json --input .\startup-observation.json
```

Schema:

```text
pimax-launch-recipe-v1
pimax-startup-sources-v1
pimax-startup-observation-v1
pimax-startup-observation-elevated-v1
pimax-startup-creator-chain-v1
```

The commands report launcher candidates, selected candidate, static validation evidence, startup activation sources, expected group members, lifecycle-root confidence, readiness criteria, failure criteria, prohibited side effects, blockers, and a human-readable summary.

It must not start or stop Pimax, restart services, invoke Pimax Play Connect, automate GUI input, cycle USB, touch DisplayPort, restart SteamVR, restart VRChat, restart VRCFT, restart the Supervisor, restart the watcher, change scheduled tasks, or access the network.

## Confirmed Candidate And Rejection

Candidate path:

```text
C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe
```

Static discovery found:

- the official `PimaxPlay.lnk` Start Menu shortcut points to this executable;
- shortcut arguments are empty;
- shortcut working directory is `C:\Program Files\Pimax\PimaxClient\pimaxui`;
- installed-application registry evidence identifies `PimaxPlay version 1.43.9.272`;
- no Pimax App Paths launcher override was observed;
- executable metadata identifies `PimaxClient` by `Pimax`;
- requested execution level is `asInvoker`.

Phase 28D2-BV2 proved that direct process creation of this executable is not a complete startup recipe. Direct launch started `PimaxClient`, but `DeviceSetting`, `PiPlayService`, and `pi_server` did not return, readiness stayed `groupPartial`, Pimax Play showed a blank window, and registration remained `conflictingEvidence / contradicted`.

After the first Phase 28D2-B2A health gate stopped on a partial group, the operator closed Pimax Play and launched it once from the normal Windows Start Menu `PimaxPlay` entry. That unobserved launch opened normally and automatically restored headset registration. This is supporting baseline-restoration evidence only; it is not the formal observer-backed comparison and does not identify the creator chain by itself.

Private discovery evidence retains local hashes, raw process inventories, raw registry data, and raw certificate details. Public docs and JSON output redact raw PIDs, command lines, user profile paths, machine names, certificate serial numbers, and raw PnP IDs.

## Recipe States

Recipe state values:

- `candidate`;
- `verifiedReadOnly`;
- `readyForControlledValidation`;
- `directLaunchRejected`;
- `shellActivationObserved`;
- `activationRootIdentified`;
- `readyForShellActivationValidation`;
- `activationMechanismIdentified`;
- `readyForActivationValidation`;
- `validated`;
- `incomplete`;
- `rejected`;
- `conflicting`;
- `unknown`.

The current state after BV2/B2A evidence is `shellActivationObserved`: direct `PimaxClient.exe` process creation is rejected, while normal Start Menu activation is the candidate path. `executable` remains `false`. A safe programmatic equivalent requires separate validation and cannot be inferred from manual Start Menu success. Phase 28D2-B2B adds `activationRootIdentified` and `readyForShellActivationValidation` for later use, but the recipe must not advance to either state until live creator-chain evidence proves the activation root.

## Readiness States

Readiness values:

- `groupReadyAndRegistered`;
- `groupReadyAwaitingRegistration`;
- `groupCompleteRegistrationUnknown`;
- `groupStarting`;
- `groupPartial`;
- `groupUnavailable`;
- `groupConflicting`;
- `groupLaunchFailed`;
- `timedOut`;
- `unknown`.

`groupReadyAndRegistered` requires required members present, valid path/signer evidence, no unexpected conflicting group member, and current `registeredReady / confirmed` registration owned by the active group session.

`groupReadyAwaitingRegistration` means the required group is formed but registration is not ready:

```text
Pimax Play started successfully, but the headset is still awaiting
registration.

Pimax Play Connect and a physical USB reconnection may still be required.
```

## Expected Group

Required members:

- `PimaxClient`;
- `DeviceSetting`;
- `PiPlayService`;
- `pi_server`;
- `PiServiceLauncher`;
- `Tobii VR4PIMAXP3B Platform Runtime`.

Optional members:

- `PiService`;
- `PiPlatformService_64`;
- `PVRHome`;
- `pi_overlay`.

Lifecycle root confidence is not yet confirmed. The shortcut launches `PimaxClient.exe`, but the BV2 direct-launch failure proves the visible UI executable is not sufficient by itself. The formal observer-backed comparison must determine whether shell activation, a service broker, a transient helper, or startup state restoration creates the required runtime members.

## Startup Sources

`pimax-startup-sources-json` is read-only. It inspects Pimax-specific Start Menu shortcuts, installed application metadata, App Paths, protocol and COM registrations, Run entries, Startup folders, services, scheduled tasks where readable, and signed helper executables under the Pimax installation root. It does not launch or stop anything.

The command currently reports the normal `PimaxPlay.lnk` Start Menu entry as the visible user activation source. It also reports Pimax service and helper candidates that require correlation during the formal observer-backed launch. Backend execution remains disabled because the creator chain and a safe programmatic equivalent are not proven.

`pimax-startup-observe-json` is a bounded process lifecycle observer. Fake mode emits deterministic non-live validation data. Live mode takes a pre-roll baseline of Pimax processes and likely Windows activation brokers, then uses Windows process start/stop trace events where allowed to preserve an immutable observation-local identity for each relevant process. If process trace subscription is denied, it falls back to bounded WMI process snapshots that still preserve start tokens and stop state, but may miss already-exited parent ownership. Public JSON emits observation-local tokens such as `baseline:0001` and `process:0007`; raw PIDs, command lines, environment blocks, handles, user names, machine names, USB, SetupAPI, MMDEVAPI, DisplayPort, named pipes, localhost probes, and GUI automation are excluded.

`pimax-startup-creator-chain-json` analyzes a captured startup-observation result and emits the `pimax-startup-creator-chain-v1` assessment. It reports preserved creator edges, root candidates, `DeviceSetting`, `PiPlayService`, `PiService`, and `pi_server` creators, unresolved gaps, and a sanitized summary. It does not launch Pimax, stop Pimax, access hardware, automate the GUI, mutate services, run tasks, or access the network.

`pimax-startup-observe-elevated-json` is the Phase 28D2-B2C elevated observer boundary. It requires an existing administrator token, refuses to self-elevate, does not weaken UAC, does not create a service, driver, scheduled task, or persistent helper, and terminates at the configured deadline. The formal elevated mode disables the WMI snapshot fallback; if process-creator trace subscription is unavailable, it reports the provider/session failure and stops before a Start Menu launch is requested.

The elevated observer contract is scoped to process-creator evidence only. It preserves observation-local process tokens, parent tokens captured at process start, start/stop timestamps, sanitized image names and paths, session labels, and event source. Public output excludes raw PIDs, raw parent PIDs, command lines, environment blocks, handles, user SIDs, user names, machine names, certificate serial numbers, and raw event payloads.

## Formal Start Menu Comparison

Phase 28D2-B2A ran one formal observer-backed normal Windows Start Menu launch comparison. The user exited Pimax Play through the official tray Exit command, then launched the normal Start Menu `PimaxPlay` entry once. The launch was not retried, and no missing runtime executable was manually started.

Machine result:

- process-group result: `groupReadyAndRegistered`;
- registration: `registeredReady / confirmed`;
- freshness: `current`;
- required members formed: `PimaxClient`, `DeviceSetting`, `PiPlayService`, `pi_server`, `PiServiceLauncher`, and `Tobii VR4PIMAXP3B Platform Runtime`;
- direct process creation remains rejected because it previously produced `groupPartial`.

Operator-visible result:

- headset LED was green;
- Pimax Play opened normally, not as a blank window;
- image, audio, microphone, and eye tracking were present;
- Vive face tracking remained detected;
- no unrelated application restarted;
- no freeze, restart, blue-screen, or abnormal PC behavior was reported.

Creator-chain evidence remains partial after Phase 28D2-B2B. The B2B live run used the bounded snapshot fallback because process trace subscription returned access denied. It preserved the child chain and post-launch timing, but `DeviceSetting` still arrived with an `external-parent` token. The creator-chain result is `unknownExternalCreator` with `insufficient` confidence. Preserved child-process evidence showed `PiPlayService` created by `DeviceSetting`, `PiService` created by `DeviceSetting`, and the final `pi_server` created by `PiService`.

The access-denied boundary occurred when the non-elevated observer attempted to start the Windows process start/stop trace subscription. ETW-style process events were modeled correctly enough for sanitized fixtures and non-live analysis, but the non-elevated live run could not enable the event stream. The WMI snapshot fallback preserved process presence and later parent observations, but it could not prove short-lived or already-exited parent identity with the same fidelity as event-time process creation evidence.

Phase 28D2-B2C adds the elevated no-fallback observer to answer the remaining narrow question: what creates `DeviceSetting` during a successful normal Start Menu launch. The supported root classifications are `windowsExplorer`, `startMenuExperienceHost`, `windowsShellBroker`, `pimaxBootstrapHelper`, `pimaxServiceBroker`, `piServiceLauncher`, `serviceControlManager`, `scheduledTask`, `comDelegateActivation`, `existingPimaxProcess`, `unknownExternalCreator`, `multipleCandidateRoots`, `conflictingEvidence`, and `insufficientEvidence`. A confirmed result requires direct event-time parent evidence, not a later snapshot.

B2B post-launch health was `healthy`; the software group was complete; registration was `registeredReady / confirmed` with current freshness. Operator-visible state was green LED, normal Pimax Play window, automatic headset recognition, image/audio/microphone/eye tracking present, Vive face tracker detected, no unrelated application restart, and no PC freeze, restart, blue screen, or abnormal behavior. Mechanism classification therefore remains `manualShellLaunchWorksMechanismStillUnresolved`, not `shellActivationMechanismIdentified`.

## Execution Boundary

Automatic restart remains disabled because these blockers are unresolved:

- the formal observer-backed Start Menu comparison formed the group but did not prove the `DeviceSetting` root;
- the root creator of `DeviceSetting` remains `unknownExternalCreator` because the live fallback observer saw an external parent token;
- a safe programmatic equivalent to normal Start Menu activation has not been validated;
- post-launch readiness and registration still require component-health proof;
- no retry, shutdown, or rollback behavior is approved.

The next phase depends on the one B2C elevated result. If the root is confirmed or a safe Windows Shell activation path is clear, the follow-up is a Shell activation adapter validation phase. If the root remains unresolved after elevated observation, the follow-up should treat the official Start Menu shortcut as a black-box Windows Shell activation route rather than repeating creator-chain launch experiments.
