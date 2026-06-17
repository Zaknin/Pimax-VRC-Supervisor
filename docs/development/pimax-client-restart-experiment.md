# Pimax Play Client Restart Experiment

This page documents the controlled CLI-only experiment for testing whether restarting the Pimax Play UI/client can recover a powered-on but unregistered Pimax Crystal headset.

The experiment is not automatic recovery. It is a manually run diagnostic tool that produces structured JSON and requires a dry run before any mutation.

## Scope

The command is:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-recovery-experiment-json --experiment wait-control
dotnet .\PimaxVrcSupervisor.dll pimax-recovery-experiment-json --experiment restart-play-client
```

It emits schema `pimax-recovery-experiment-v1`.

The command is not exposed through the bridge, Terminal UI, SteamVR overlay, Configurator UI, or scheduled startup flow.

## Phase 28C Trial Result

The first controlled hardware trial found that a client-only Pimax Play restart was not an effective recovery action for the captured failure mode:

- the wait-only control did not recover registration;
- the verified Pimax Play UI/client closed and relaunched once;
- no Pimax service, USB/PnP device, SteamVR process, or Connect UI automation was intentionally changed;
- registration remained `likelyPoweredOnAwaitingRegistration` through the bounded observation window;
- the user-observed Pimax Play UI remained disconnected or in the setup guide.

This does not prove that a client-only restart can never help. It means the captured blue/unregistered failure did not recover through this action.

## Phase 28C1 Corrections

The first trial exposed two transport defects in the experimental framework:

- the relaunched Electron client inherited the CLI command's stdout/stderr handles, so Pimax Play log output contaminated the JSON result stream;
- the ad hoc timeline helper used fragile nested quoting and failed when the repository path contained a space.

The corrected implementation detaches the relaunched GUI process from the command JSON channel. `pimax-recovery-experiment-json` is expected to write exactly one JSON document to stdout and return after the bounded experiment completes, even when the relaunched Pimax Play process remains open.

The reusable timeline helper now uses explicit PowerShell parameters and structured argument passing. Diagnostic stderr is written separately from JSON sample files.

## Wait-Control Experiment

`wait-control` repeats the existing Pimax registration assessment for a bounded duration and performs no mutation. Use it first to see whether the headset registers naturally.

Example:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-recovery-experiment-json --experiment wait-control --duration-seconds 30
```

## Restart-Client Experiment

`restart-play-client` has two steps.

First, run it without confirmation. This is a dry run:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-recovery-experiment-json --experiment restart-play-client
```

The dry run collects the current registration assessment, verifies the Pimax Play UI/client process target, verifies that SteamVR is not running, and returns a short-lived confirmation token only when execution is permitted.

Second, execute with the token:

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-recovery-experiment-json --experiment restart-play-client --confirm --confirmation-token <token>
```

The token expires quickly and is tied to the verified target and initial assessment state.

## Required State

The restart experiment is permitted only when the assessment state is `likelyPoweredOnAwaitingRegistration`. It refuses to run when the headset is already registered, likely powered off, conflicting, or inconclusive.

SteamVR must be closed.

## Process Targeting

The process controller targets only the verified top-level Pimax Play UI/client process. It verifies executable path, product metadata, process role, and a relaunch target from the installed shortcut or current verified executable.

It does not kill by broad process name. It does not stop Pimax services. It does not manipulate USB or PnP devices.

## Execution Boundary

When confirmed, the command:

1. Captures the pre-operation assessment.
2. Requests graceful close of the verified client window.
3. Waits for a bounded close timeout.
4. If needed, terminates only the previously verified exact client PID.
5. Relaunches the verified client target once.
6. Observes registration state for a bounded timeout.
7. Stops as soon as `registeredReady` is observed or the timeout expires.

There is no automatic retry and no escalation to service restart, Connect automation, USB re-enumeration, or SteamVR launch.

## Success And Failure

Success means:

- the initial state was `likelyPoweredOnAwaitingRegistration`;
- the single client restart completed safely;
- the final assessment became `registeredReady`.

A relaunched Pimax Play client alone is not a successful recovery.

Structured failures include target not found, target ambiguous, safety guard rejected, confirmation rejected, graceful close timeout, forced stop failed, relaunch failed, client started but registration unchanged, cancellation, and unexpected process state.

## Evidence

Use `--evidence-dir <path>` to annotate the structured result with the local evidence package location. The command itself does not write unrelated environment dumps.

Keep evidence outside the repository.

## Limitations

This experiment does not prove that client restart is safe for automatic recovery. A successful controlled run would justify a later manual operator-confirmed action design, not background automation.

`registeredReady` in `pimax-registration-assessment-v1` describes runtime/device evidence. It does not guarantee that the Pimax Play Electron UI has visually refreshed to the same state.
