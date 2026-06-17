# Pimax Runtime Service Restart Experiment

This page documents the controlled `restart-runtime-service` experiment for Pimax headset registration evidence collection. It is not automatic recovery and is not exposed through the Supervisor bridge, Terminal UI, overlay, or Configurator.

## Purpose

Phase 28C showed that restarting only the Pimax Play UI/client did not recover the captured blue/unregistered state. The runtime-service experiment isolates the next question: whether restarting one verified Pimax runtime service can make already-present headset USB/PnP evidence register with Pimax Play.

## Command

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-recovery-experiment-json --experiment restart-runtime-service
```

The command keeps the existing `pimax-recovery-experiment-v1` schema. New service-related fields are optional additions for this experiment.

## Safety Model

The dry run performs no mutation. It collects the current registration assessment, verifies SteamVR is closed, discovers the exact runtime service, checks service identity, checks dependency state, and returns a short-lived confirmation token.

Execution requires:

- `--confirm`
- the matching `--confirmation-token`
- the current state still being `likelyPoweredOnAwaitingRegistration`
- SteamVR still closed
- the same exact service name, executable path, executable hash, service PID, and metadata
- no active dependent-service conflict

The experiment does not restart Pimax Play, manipulate USB/PnP devices, automate Connect, start SteamVR, run bridge actions, or retry the service restart.

## UAC Helper

The normal CLI process may remain non-elevated. The privileged operation is brokered through a one-shot PowerShell helper generated under the evidence folder. Windows UAC is shown only for the single service stop/start operation.

The helper:

- verifies it is elevated
- verifies the request hash and confirmation binding
- rejects expired or modified requests
- independently revalidates the exact service
- stops that one service once
- starts that same service once
- writes one atomic JSON result file

If the normal start fails, the helper may perform one safety-restoration start for the same service. That restoration attempt is recorded separately and is not a second recovery experiment.

## Stabilized Checkpoints

The `stabilized-assessment` experiment repeatedly collects read-only registration assessments until a small number of equivalent samples is observed or a bounded timeout is reached.

```powershell
dotnet .\PimaxVrcSupervisor.dll pimax-recovery-experiment-json --experiment stabilized-assessment --duration-seconds 20
```

This helper does not change the assessor rules. It records whether a physical checkpoint is stable, transitional, conflicting, or timed out.

## Success Criteria

The service-only experiment succeeds only if:

- the initial state was `likelyPoweredOnAwaitingRegistration`
- one exact verified runtime service was restarted
- the service returned to a valid running state
- final assessment became `registeredReady`
- Pimax Play was not restarted
- USB/PnP devices were not manipulated
- SteamVR remained closed

A successful service restart without registration recovery is a failed recovery result.
