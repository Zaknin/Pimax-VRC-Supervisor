# Installation Issues

## The Supervisor Exits Immediately

**Cause:** Another normal supervisor instance is already running.

The supervisor uses a mutex (`Local\PimaxVrcSupervisor`) to prevent duplicate instances. If you see "Pimax VRC Supervisor is already running. Exiting this duplicate instance.", check for a running instance in Task Manager.

## Broken Eye or VRCFaceTracking Does Not Launch

| Check | Action |
| --- | --- |
| Executable path | Open the Configurator and verify the path in the **General** tab. |
| Process name | Check the **Processes** tab. The process name must match the running process (without `.exe`). |
| File exists | Ensure the executable exists at the configured path. |
| Path variables | If using `%APPDATA%` or `%LOCALAPPDATA%`, verify the expanded path is correct. |

## .NET Runtime Errors

| Symptom | Solution |
| --- | --- |
| "You must install .NET to run this application" | Download the self-contained release (`PimaxVrcSupervisor-v1.2.0.zip`) or install the .NET 9 Windows Desktop Runtime. |
| Wrong architecture | Ensure you're running the `win-x64` build on a 64-bit Windows system. |

## UAC Prompt Not Appearing

The supervisor requests administrator privileges through its manifest (`app.manifest` with `requireAdministrator`). If the UAC prompt doesn't appear:

1. Check if UAC is disabled in Windows settings.
2. Run the executable directly (not through a script or shortcut that might suppress the prompt).
3. Right-click the executable and select "Run as administrator".

## Release Verification Fails

### Checksum Mismatch

```powershell
Get-FileHash .\PimaxVrcSupervisor-v1.2.0.zip -Algorithm SHA256
Get-Content .\PimaxVrcSupervisor-v1.2.0.zip.sha256
```

If the hashes don't match, the download may be corrupted. Re-download the release.

### Sigstore Verification Fails

```powershell
cosign verify-blob .\PimaxVrcSupervisor-v1.2.0.zip `
  --bundle .\PimaxVrcSupervisor-v1.2.0.zip.sigstore.json `
  --certificate-identity-regexp "^https://github.com/.+/.+/.github/workflows/release.yml@refs/(heads|tags)/.+$" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

Ensure `cosign` is installed and the bundle file matches the zip.

## Config File Not Found

If the supervisor reports that `supervisor.config.json` doesn't exist:

1. The editor creates a default config on first run.
2. Place a valid `supervisor.config.json` next to the supervisor executables.
3. Use the `--config <path>` argument to specify a custom path.

See also: [Troubleshooting Overview](index.md) Â· [Configurator Issues](config-editor-issues.md) Â· [OSC Issues](osc-issues.md)
