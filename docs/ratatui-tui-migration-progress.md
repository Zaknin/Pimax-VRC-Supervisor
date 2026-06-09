# Ratatui TUI Migration Progress

Repository: Zaknin/Pimax-VRC-Supervisor  
Branch: vrmanifest-gui-overhaul-ver2

## Goal

Prepare the existing C# Pimax VRC Supervisor backend for a future separate Rust Ratatui TUI frontend.

Target architecture:

- C# supervisor remains backend and owns VR/SteamVR/VRChat/cleanup/monitor/OSC/base-station logic.
- Rust Ratatui app becomes a separate dashboard/controller.
- Communication should eventually happen through local IPC, preferably Windows named pipes.

## Hard Rules

- Preserve current behavior unless a phase explicitly changes it.
- Preserve all current CLI modes.
- Preserve Ctrl+C and console-close emergency cleanup.
- Do not add Ratatui/Rust until backend is ready.
- Do not remove the old console interface.
- Work in small buildable phases.
- Update this file at the end of every phase.

## Current Status

Phase 0 not started.

## Phase Roadmap

0. Inspect repo and document current architecture.
1. Add console/event abstraction.
2. Add structured status snapshot model.
3. Add command model.
4. Add local IPC server.
5. Add minimal Rust Ratatui frontend.
6. Add full TUI dashboard screens.
7. Add packaging/integration.

## Phase Log

_No phases completed yet._

## Next Codex Prompt

Start Phase 0.