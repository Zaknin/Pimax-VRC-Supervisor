# Phase 33A Update Notification Acceptance

Phase 33A is notification only. It retrieves bounded release metadata, a manifest, and a detached signature; it has no application package download, staging, extraction, installation, replacement, or execution API.

## Dedicated fixture boundary

The acceptance harness lives only in `PimaxVrcSupervisor.Tests`. It injects an in-memory HTTP transport into the discovery client and cannot configure the production UI, normal configuration, or production endpoint. The positive fixture consists of static UTF-8-without-BOM manifest bytes and a static detached signature envelope. Its exact manifest SHA-256 is `8bedf3676d17427bb1cf1bcc9b4b2097b3a161ed08208ee2d92cddded4b10472`.

The approved production private key is not in the repository, test output, deployment, CI, logs, or release assets. `Sign-UpdateAcceptanceFixture.ps1` is an explicit operator-only tool: it refuses a private key beneath the repository or deployment roots, derives and checks the approved public fingerprint, signs exact bytes once, verifies immediately, and writes only the public signature envelope. Builds and tests never invoke it and never sign with the production key.

The harness covers these bounded states:

1. current version is latest;
2. newer verified Stable release is available;
3. current candidate is dismissed;
4. a newer candidate supersedes an older dismissal;
5. manifest bytes are altered;
6. a DER signature is altered;
7. the signature references an unknown key ID;
8. a valid test-key signature carries the wrong repository;
9. the candidate is draft or prerelease;
10. the server times out or is unavailable;
11. GitHub metadata returns HTTP 304 with an ETag;
12. a valid test-key signature carries an unsupported manifest schema.

Only state 2 uses the static operator-signed production fixture. Repository/schema negative fixtures use ephemeral keys confined to the test process so the production key never signs deliberately invalid content. Every network-state assertion also proves that no `.zip` request occurs.

## Automated acceptance

Run both configurations:

```powershell
dotnet test .\PimaxVrcSupervisor.Tests\PimaxVrcSupervisor.Tests.csproj -c Debug --filter 'FullyQualifiedName~Phase33AProductionAcceptanceFixtureTests|FullyQualifiedName~SecureUpdateContractTests|FullyQualifiedName~GitHubUpdateDiscoveryTests|FullyQualifiedName~SupervisorUpdateCoordinatorTests'
dotnet test .\PimaxVrcSupervisor.Tests\PimaxVrcSupervisor.Tests.csproj -c Release --filter 'FullyQualifiedName~Phase33AProductionAcceptanceFixtureTests|FullyQualifiedName~SecureUpdateContractTests|FullyQualifiedName~GitHubUpdateDiscoveryTests|FullyQualifiedName~SupervisorUpdateCoordinatorTests'
dotnet test .\PimaxVrcSupervisor.Tests\PimaxVrcSupervisor.Tests.csproj -c Debug --filter 'FullyQualifiedName~StandaloneUpdateCheckWorkerTests|FullyQualifiedName~ConfiguratorUpdateFallbackTests|FullyQualifiedName~ConfiguratorStandaloneUpdateCheckLauncherTests|FullyQualifiedName~UpdateDiscoverySchedulerTests|FullyQualifiedName~UpdateCheckAdmissionSecurityTests|FullyQualifiedName~UpdateCheckCrossProcessTests'
dotnet test .\PimaxVrcSupervisor.Tests\PimaxVrcSupervisor.Tests.csproj -c Release --filter 'FullyQualifiedName~StandaloneUpdateCheckWorkerTests|FullyQualifiedName~ConfiguratorUpdateFallbackTests|FullyQualifiedName~ConfiguratorStandaloneUpdateCheckLauncherTests|FullyQualifiedName~UpdateDiscoverySchedulerTests|FullyQualifiedName~UpdateCheckAdmissionSecurityTests|FullyQualifiedName~UpdateCheckCrossProcessTests'
```

The tests prove production-key load/fingerprint, exact-byte verification, one-byte rejection, unknown-key and repository rejection, verified cache creation, invalid-data non-availability, structured nonblocking Configurator manual results under Disabled, bridge-first standalone fallback only when no operation was accepted, exact sibling/no-shell worker launch, bounded worker output, pre-acceptance shared admission, explicit current-user mutex ownership, an exact protected bounded SID-only `Global\\` DACL, Owner/Access inspection from the returned handle, fail-closed exact-DACL foreign-owner rejection without hostile-object repair, actual separate-process contention/release, retained-handle genuine abandoned-owner recovery, dismissal/supersession, zero bridge-client network ownership, passive TUI/overlay projections, and absence of download/install/execute surfaces. The predictable mutex name can still be squatted for fail-closed denial of service by another local user, and no protection against a local administrator is claimed.

## External manual acceptance plan

Use a fresh `no-dotnet9` package extracted below `C:\Users\operator\Documents\PimaxVrcSupervisor-TestDeployments`; never use repository-local release output. Record the directory inventory and SHA-256 of every file before launching anything. Preserve the operator's current update-state directory and Supervisor configuration before the exercise; restore them afterward.

1. Confirm the Configurator Updates tab shows installed `1.3.1`, channel Stable, and policy Disabled.
2. With the Supervisor bridge available, press **Check now** under Disabled and confirm the bridge remains authoritative, the UI stays responsive, and one structured terminal result appears without a worker process.
3. With no Supervisor bridge available, press **Check now** and confirm the exact sibling `PimaxVrcSupervisor.UpdateWorker.exe` one-shot worker runs without a UAC prompt and without starting Supervisor lifecycle, SteamVR, Pimax Play, base-station, monitor, managed-application, watcher, TUI, overlay, normal bridge, startup integration, session ownership, or cleanup behavior. Confirm one bounded result appears and no helper process remains after completion.
4. Select Notify, save, reopen, and confirm the exact policy persists. Restore the prior configuration after the observation.
5. Feed the dedicated harness's verified cached projection and confirm only `1.4.0` is shown as verified. Feed altered/incorrectly signed cases and confirm no unverified version is displayed.
6. Confirm the TUI shows one compact passive indicator without overflow and the overlay shows one passive non-modal badge outside action controls.
7. Dismiss `1.4.0`; confirm the prominent indicators disappear. Present a verified newer state and confirm it reappears.
8. Disconnect/reconnect the Configurator, TUI, and overlay bridge clients. Confirm cached status returns with zero discovery requests and no action replay.
9. Exercise the existing SteamVR restart and final-normal-shutdown paths only in the established lifecycle acceptance environment. Confirm behavior is unchanged and no update operation delays or participates in cleanup.
10. Search the deployment, update-state directory, and network evidence for ZIP output. There must be no downloaded package file.

Do not run lifecycle steps merely to validate update UI when an active Pimax/SteamVR session would be disturbed. In that case record those manual rows as deferred and rely only on the preserved Phase 32D/32E regression gates until the normal hardware acceptance window.

## Mixed-integrity update-admission evidence

The secure admission remains one SID-derived `Global\\` mutex with the explicit current-user owner and exact protected SID-only DACL. It is intentionally not relaxed for standard-user checking.

Automated subprocess coverage runs at one integrity level and proves contention, no operation allocation/network/state mutation on rejection, release recovery, identity stability, and fail-closed `gate_unavailable` handling when descriptor security cannot be established. CI must not attempt UAC elevation.

For local Windows evidence after building both configurations, run the following from a standard-user PowerShell session. It deliberately prompts twice for UAC and performs no package or installation operation:

```powershell
.\scripts\Test-UpdateWorkerMixedIntegrity.ps1 -Configuration Release
```

The harness proves an unelevated gate owner rejects the elevated legacy Supervisor one-shot compatibility path, an elevated gate owner rejects the unelevated shipped UpdateWorker, both rejections return `already_running` with no operation status, and the gate is usable after each release. Run `UpdateCheckAdmissionSecurityTests` alongside it to prove descriptor security errors remain `gate_unavailable`; never reinterpret them as contention or repair the owner/DACL.

## Future installation contract (documented only)

Checking and any future package download/verification remain unelevated. UAC may be requested only after an explicit user installation action, by a separate minimal elevated installer executable. That installer performs no network access and independently revalidates the manifest, package hash, target path, and version before any mutation. If the user cancels UAC, the current installation remains unchanged. Phase 33A implements none of those installation capabilities.

## 2026-07-21 acceptance evidence

The reviewed slice created a fresh external deployment at `%USERPROFILE%\Documents\PimaxVrcSupervisor-TestDeployments\Phase33A-UpdateNotificationAcceptance-20260721`.

| Artifact | Evidence |
| --- | --- |
| `no-dotnet9` extracted directory | 28 files; 31,676,966 bytes |
| `PimaxVrcSupervisor-v1.3.1-win-x64-no-dotnet9.zip` | 9,347,155 bytes; SHA-256 `8721145e05edf5132b07aa20986ac2927ca003cba52d5ff6bbd0977dc31169bb` |
| `PimaxVrcSupervisor-v1.3.1-win-x64-with-dotnet9.zip` | 57,002,690 bytes; SHA-256 `30bdbef049f79d1e44f8002f0b54e8efa04ed10e460cdc3de2b14eba71885d08` |
| `no-dotnet9` ZIP inventory | exactly 28 entries, matching the extracted package contract |
| forbidden acceptance/private content | zero `.pk8`, `.pem`, `.key`, ceremony, update-manifest, signature-fixture, test-assembly, or update-state files |

The dedicated 12-state harness passed 16 production-trust/acceptance tests and the focused trust/update set passed 81 tests in both Debug and Release. The complete managed suite passed 618 tests in each configuration. The Rust TUI passed 69 tests in each configuration, including compact indicator, dismissal, reconnect-cache, zero-network bridge ownership, and action-7/final-owner separation cases.

The external Configurator launch was attempted, but a user-owned Phase 32E Configurator window and active Pimax watcher/session were already present. The acceptance runner did not close them, start another Supervisor, alter configuration, restart SteamVR, or perform final shutdown. Consequently the Configurator live fixture presentation, TUI/overlay live layout, policy-persistence write, reconnect, and lifecycle rows remain explicitly deferred to the next normal hardware acceptance window. Automated gates cover those contracts, but this record does not mislabel them as live manual observations.

## Acceptance record template

```text
commit:
approved keyId:
approved public-key SHA-256:
operator-signed manifest SHA-256:
external no-dotnet9 deployment:
package directory SHA-256 inventory:
package ZIP SHA-256:
Configurator current/channel:
Disabled manual check:
Notify persistence:
verified fixture presentation:
altered/invalid rejection:
TUI layout:
overlay layout:
dismissal and supersession:
bridge reconnect and request count:
lifecycle regression evidence:
package download search:
deferred rows and reason:
```
