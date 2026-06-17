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
