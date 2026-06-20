# Pimax Process-Group Launch Readiness

Phase 28D2-B2 adds a read-only model for Pimax Play/runtime startup orchestration. It does not execute the recipe.

## Command

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-launch-recipe-json
dotnet .\PimaxVrcSupervisor.dll pimax-startup-sources-json
dotnet .\PimaxVrcSupervisor.dll pimax-startup-observe-json --fake
```

Schema:

```text
pimax-launch-recipe-v1
pimax-startup-sources-v1
pimax-startup-observation-v1
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
- `activationMechanismIdentified`;
- `readyForActivationValidation`;
- `validated`;
- `incomplete`;
- `rejected`;
- `conflicting`;
- `unknown`.

The current state after BV2/B2A evidence is `shellActivationObserved`: direct `PimaxClient.exe` process creation is rejected, while normal Start Menu activation is the candidate path. `executable` remains `false`. A safe programmatic equivalent requires separate validation and cannot be inferred from manual Start Menu success.

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

`pimax-startup-observe-json` is a bounded process lifecycle observer. Fake mode emits deterministic non-live validation data. Live mode observes Pimax-related process starts and stops by bounded process snapshots, tokenizes raw PIDs, and avoids command lines, environment blocks, USB, SetupAPI, MMDEVAPI, DisplayPort, named pipes, localhost probes, and GUI automation.

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

Creator-chain evidence is partial. A post-launch parent snapshot showed `PiPlayService` parented by `DeviceSetting`, `PiService` parented by `DeviceSetting`, and `pi_server` parented by `PiService`. `DeviceSetting` itself had an external or already-exited parent, so the root activation creator remains unresolved. Mechanism classification is therefore `manualShellLaunchWorksMechanismStillUnresolved`, not `shellActivationMechanismIdentified`.

## Execution Boundary

Automatic restart remains disabled because these blockers are unresolved:

- the formal observer-backed Start Menu comparison has not yet proven the creator chain;
- the root creator of `DeviceSetting` remains unknown because its parent was external or already exited in the post-launch snapshot;
- a safe programmatic equivalent to normal Start Menu activation has not been validated;
- post-launch readiness and registration still require component-health proof;
- no retry, shutdown, or rollback behavior is approved.

Recommended next phase:

```text
Phase 28D2-B2B - Implement a Safe Windows Shell Activation Adapter for Pimax Play
```
