# Pimax Process-Group Launch Readiness

Phase 28D2-B2 adds a read-only model for a future Pimax Play/runtime process-group launch. It does not execute the recipe.

## Command

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-launch-recipe-json
```

Schema:

```text
pimax-launch-recipe-v1
```

The command reports launcher candidates, selected candidate, static validation evidence, expected group members, lifecycle-root confidence, readiness criteria, failure criteria, prohibited side effects, blockers, and a human-readable summary.

It must not start or stop Pimax, restart services, invoke Pimax Play Connect, automate GUI input, cycle USB, touch DisplayPort, restart SteamVR, restart VRChat, restart VRCFT, restart the Supervisor, restart the watcher, change scheduled tasks, or access the network.

## Confirmed Candidate

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

Private discovery evidence retains local hashes, raw process inventories, raw registry data, and raw certificate details. Public docs and JSON output redact raw PIDs, command lines, user profile paths, machine names, certificate serial numbers, and raw PnP IDs.

## Recipe States

Recipe state values:

- `candidate`;
- `verifiedReadOnly`;
- `readyForControlledValidation`;
- `validated`;
- `incomplete`;
- `rejected`;
- `conflicting`;
- `unknown`.

The highest permitted state in Phase 28D2-B2 is `readyForControlledValidation`. Even in that state, `executable` remains `false`. Only a later stopped-state live validation may mark the recipe `validated`.

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

Lifecycle root confidence is `probable`: the shortcut launches `PimaxClient.exe`, the top-level client owns Electron child processes, and runtime members are coordinated through `DeviceSetting` and service-owned processes. The model does not infer that the visible UI executable is the whole lifecycle root.

## Execution Boundary

Automatic restart remains disabled because these blockers are unresolved:

- the recipe has not been validated from a fully absent Pimax process group;
- process-group formation after launching the candidate has not been observed;
- single-instance behavior was not tested by execution in this phase;
- post-launch readiness and registration still require component-health proof;
- no retry, shutdown, or rollback behavior is approved.

Recommended next phase:

```text
Phase 28D2-BV2 - One-Shot Validation of the Candidate Pimax Process-Group Launch Recipe
```
