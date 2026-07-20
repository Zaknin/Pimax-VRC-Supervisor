# Phase 33A Secure GitHub Update Discovery and Notification

Status: the reviewed manifest/signature/state contracts and standalone bounded discovery/scheduling services are implemented. They are not wired into Supervisor startup, Configurator, TUI, overlay, bridge, or configuration yet. No package download, installer, version change, release-workflow change, GitHub setting change, or lifecycle/configuration write is implemented.

Baseline: `codex/phase-33a-secure-update-discovery` from Phase 32F commit `62e4ed9971934e1c767400ea4dbd0c76464cd660`.

## Scope and security invariant

Phase 33A may discover a newer published stable release and show a notification. It must never download a package, extract an archive, replace a file, run downloaded content, or change SteamVR/Supervisor lifecycle settings. A check failure must not delay startup, session actions, recovery, cleanup, or process exit.

The update checker is not an updater. The only user-directed transition is opening the canonical release page in the default browser after an explicit desktop action. The SteamVR overlay remains informational and cannot open, download, or install anything.

Phase 33A retrieves only fixed-repository GitHub release metadata, the bounded manifest bytes, and the bounded detached-signature envelope. It contains no package download, staging, extraction, installation, replacement, or execution path.

## Current repository discovery

### Version metadata

All shipped components currently agree on `1.3.1`:

| Component | Source | Current metadata |
| --- | --- | --- |
| Supervisor | `PimaxVrcSupervisor.csproj` | `Version`/`InformationalVersion` `1.3.1`; assembly/file `1.3.1.0` |
| Configurator | `PimaxVrcSupervisor.ConfigEditor.csproj` | `Version`/`InformationalVersion` `1.3.1`; assembly/file `1.3.1.0` |
| SteamVR overlay host | `PimaxVrcSupervisor.SteamVrHost.csproj` | `Version`/`InformationalVersion` `1.3.1`; assembly/file `1.3.1.0` |
| Rust Terminal UI | `PimaxVrcSupervisor.Tui/Cargo.toml` | package version `1.3.1` |
| Packaging script | `scripts/package-release.ps1` | default tag `v1.3.1` |

There is no shared version source today. Release validation must compare all four product version sources, the requested tag, package names, and the signed manifest version before a draft can be published. Phase 33A does not change them.

### Package variants

`scripts/package-release.ps1` produces exactly two flat Windows x64 ZIP variants:

| Variant | Package rule |
| --- | --- |
| `with-dotnet9` | Self-contained .NET 9 Windows Desktop package for users who do not know whether the runtime is installed. |
| `no-dotnet9` | Framework-dependent package requiring the .NET 9 Windows Desktop Runtime x64. |

Both include Supervisor, Configurator, SteamVR host, Rust TUI, the Startup Helper and Watcher copies, config template, README, release notes, and overlay PNG. The script strips PDB/backup/runtime-state clutter, satellite-language folders, and embedded icon sources before ZIP creation. It creates ZIPs before Authenticode signing; the release workflow correctly recreates the ZIPs after signing.

### Release and verification workflow

The current `.github/workflows/release.yml`:

1. accepts a strict `vX.Y.Z` tag and verifies checkout/tag identity;
2. builds both variants;
3. optionally Authenticode-signs app binaries, but currently skips rather than fails when signing secrets are absent;
4. recreates and inventories both ZIPs;
5. writes one SHA-256 file and one keyless Sigstore bundle per ZIP;
6. creates GitHub build-provenance attestations for the ZIPs;
7. creates or updates a draft release with six files: two ZIPs, two checksum files, and two Sigstore bundles.

It does not publish the draft, attach attestation bundles as release assets, create or sign an update manifest, or perform post-publication verification.

The separate `.github/workflows/sign-release-assets.yml` runs on `release.published`, downloads the two ZIPs, signs/checksums them, and uploads four companion assets after publication. That ordering is incompatible with immutable releases and duplicates the main release workflow. It must be retired, not adapted to mutate a published release.

Read-only GitHub discovery on 2026-07-20 found:

- repository release immutability is disabled (`enabled=false`, `enforced_by_owner=false`);
- v1.3.1 is published and mutable (`isImmutable=false`);
- v1.3.1 has six assets, with checksum/Sigstore companions created after the release publication time;
- `gh release verify v1.3.1` exits unsuccessfully with `no attestations for tag v1.3.1`.

No release asset was downloaded during this discovery. `v1.3.1` remains untouched as a historical mutable release: Phase 33A does not edit its release, tag, or assets and applies the immutable-release contract only to future releases.

### Application and schema surfaces

- `SupervisorConfig` deserializes the commented `supervisor.config.json` beside the selected installation/config path. Configurator mirrors fields through its WinForms controls and `JsonPropertyEditor` helpers.
- Configurator is a tabbed WinForms application. It already persists window/editor convenience state through `Application.UserAppDataPath`, which is version/product scoped and is not the recommended cross-release update-state location.
- Supervisor exposes `line-oriented-tcp-v1` on loopback port `37957`. Structured `query-json` resources currently cover status, commands, logs, and Pimax connectivity. The additive status snapshot is consumed by the Rust TUI.
- The Rust TUI uses serde defaults for missing response fields, so a new optional update object can remain backward compatible.
- The SteamVR overlay currently reads the local bridge and renders a fixed status strip, buttons, console panel, and footer. It must receive only a verified, already-cached update projection from Supervisor; it must not become another network client.
- General diagnostics default to `%TEMP%\PimaxVrcSupervisorDiagnostics`. Bounded Base Station and XSOverlay operational records already use `%LOCALAPPDATA%\PimaxVrcSupervisor\Diagnostics`.

### Persistent-state location

Use `%LOCALAPPDATA%\PimaxVrcSupervisor\Update\update-state-v1.json` for update scheduling and notification state. This fixed per-user path survives side-by-side release folders and stays outside the installation directory. Do not use the release folder, `Application.UserAppDataPath`, the config file, `%TEMP%`, the registry, or a scheduled task.

Writes must use a per-user cross-process lock plus create/write/flush/atomic-replace in the same directory. State contains no credential, package, executable content, or machine identifier sent to GitHub.

## Threat model

### Assets to protect

- accuracy of the claimed latest version and canonical release URL;
- authenticity and integrity of manifest/package metadata;
- the embedded update trust root;
- the 24-hour schedule and dismissal state;
- user privacy and bounded GitHub traffic;
- existing Supervisor lifecycle, recovery, and cleanup behavior.

### Adversaries and failures

| Threat | Required control |
| --- | --- |
| Network/CDN interception or corrupt response | HTTPS plus a detached signature over the exact manifest bytes; never trust transport alone. |
| Malicious redirect or phishing URL in a valid-looking document | Fixed repository identity and exact canonical release-URL pattern; do not follow arbitrary manifest URLs. |
| GitHub API/CDN outage, rate limit, timeout, or malformed body | Bounded request/response sizes and timeouts; fail closed to no notification; never affect lifecycle. |
| Replay or rollback of an older valid manifest | Require version not below the running version, persist highest accepted sequence/version/digest, and require agreement with GitHub's published latest stable release. |
| Compromised GitHub account, workflow, or mutable action tag | Offline manifest-signing key, immutable releases, protected publishing environment, full-SHA action pinning, least privilege, and independent Sigstore/attestation checks. |
| Incomplete draft or post-publish asset mutation | Exact draft asset inventory before publication and repository release immutability enabled as a hard preflight. |
| Compromised manifest signing key | Embedded current/next-key overlap, release incident procedure, and no arbitrary executable/download behavior even for a valid signature. |
| Oversized, duplicate-key, ambiguous, or type-confused JSON | Strict UTF-8, size caps, duplicate-property rejection, exact schema/type validation, and no coercion. |
| Local same-user state tampering or corruption | Treat state as scheduling/UI cache only; never as a trust root. Re-verify every new manifest and recover atomically. |
| Concurrent Supervisor/Configurator checks | One named per-user lock and one writer; TUI/overlay never check independently. |
| Clock rollback/forward or repeated restarts | Persist the automatic-attempt timestamp before network I/O; suppress automatic checks until the stored 24-hour boundary. |
| Notification spam or lifecycle coupling | One non-modal notification per verified version; update work is cancellable, background-only, and excluded from all session/recovery coordinators. |
| Replaced local application binary or embedded key | Out of scope: an attacker who can replace the trusted running binary can replace its verifier and UI. Authenticode remains a separate defense. |

The most important containment control is that Phase 33A performs notification only. Even a compromised signing key cannot make the application download, extract, or execute a package.

## Signed update-manifest v1

### Files

Each release must attach these manifest files while still a draft:

```text
PimaxVrcSupervisor-vX.Y.Z-update-manifest-v1.json
PimaxVrcSupervisor-vX.Y.Z-update-manifest-v1.signatures.json
PimaxVrcSupervisor-vX.Y.Z-update-manifest-v1.json.sha256
PimaxVrcSupervisor-vX.Y.Z-update-manifest-v1.json.sigstore.json
PimaxVrcSupervisor-vX.Y.Z-update-manifest-v1.json.attestation.json
```

The application's primary trust decision is the detached signature verified by an embedded offline ECDSA trust root. GitHub release attestations, artifact attestations, Sigstore, checksums, and immutable-release verification are independent defense-in-depth and operator-verification layers; none can substitute for, remotely replace, or be collapsed into the embedded-key decision.

### Manifest schema

The checker first treats the manifest as opaque, bounded bytes. The detached signature covers those exact bytes and must be verified before the bytes are parsed as JSON. The signed bytes must be UTF-8 without a byte-order mark; after signature verification, the v1 parser also rejects comments, trailing commas, duplicate property names, unknown properties, floating-point numbers, and non-UTC timestamps.

```json
{
  "schemaVersion": 1,
  "repository": "Zaknin/Pimax-VRC-Supervisor",
  "channel": "stable",
  "releaseSequence": 1,
  "generatedAtUtc": "2026-07-20T19:00:00Z",
  "release": {
    "version": "1.4.0",
    "tag": "v1.4.0",
    "commitSha": "0123456789abcdef0123456789abcdef01234567",
    "releaseUrl": "https://github.com/Zaknin/Pimax-VRC-Supervisor/releases/tag/v1.4.0"
  },
  "assets": [
    {
      "variant": "with-dotnet9",
      "rid": "win-x64",
      "runtimeMode": "self-contained",
      "requiredWindowsDesktopRuntime": null,
      "fileName": "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip",
      "sizeBytes": 55000000,
      "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      "checksumFile": {
        "fileName": "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip.sha256",
        "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
      },
      "sigstoreBundleFile": {
        "fileName": "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip.sigstore.json",
        "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
      },
      "attestationBundleFile": {
        "fileName": "PimaxVrcSupervisor-v1.4.0-win-x64-with-dotnet9.zip.attestation.json",
        "sha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
      }
    },
    {
      "variant": "no-dotnet9",
      "rid": "win-x64",
      "runtimeMode": "framework-dependent",
      "requiredWindowsDesktopRuntime": "9.0.x-windowsdesktop-x64",
      "fileName": "PimaxVrcSupervisor-v1.4.0-win-x64-no-dotnet9.zip",
      "sizeBytes": 9000000,
      "sha256": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
      "checksumFile": {
        "fileName": "PimaxVrcSupervisor-v1.4.0-win-x64-no-dotnet9.zip.sha256",
        "sha256": "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"
      },
      "sigstoreBundleFile": {
        "fileName": "PimaxVrcSupervisor-v1.4.0-win-x64-no-dotnet9.zip.sigstore.json",
        "sha256": "1111111111111111111111111111111111111111111111111111111111111111"
      },
      "attestationBundleFile": {
        "fileName": "PimaxVrcSupervisor-v1.4.0-win-x64-no-dotnet9.zip.attestation.json",
        "sha256": "2222222222222222222222222222222222222222222222222222222222222222"
      }
    }
  ]
}
```

Required semantic checks:

1. schema, repository, and channel are exact constants;
2. `version` is exactly three non-negative decimal components with no leading `v`, build metadata, or prerelease suffix;
3. tag, canonical release URL, every asset name, and version all agree;
4. the GitHub latest-release API result is published, non-draft, non-prerelease, immutable, and has the same tag and exact asset names/digests;
5. `commitSha` is 40 lowercase hexadecimal characters and matches the release/tag commit reported by GitHub;
6. `releaseSequence` is positive and not below the embedded/current or highest accepted sequence;
7. assets contain exactly one of each defined variant and no other package variant;
8. sizes are positive integers and hashes are exactly 64 lowercase hexadecimal characters;
9. the release version must be greater than the running version to set `updateAvailable=true`; equal is current and lower is rejected as rollback;
10. the response limits are 256 KiB for the manifest and 64 KiB for its signature envelope.

The checker fetches metadata only. It never follows a package asset URL.

### Detached signature schema

```json
{
  "schemaVersion": 1,
  "manifestFile": "PimaxVrcSupervisor-v1.4.0-update-manifest-v1.json",
  "manifestSha256": "3333333333333333333333333333333333333333333333333333333333333333",
  "signatures": [
    {
      "keyId": "pimax-update-primary-2026",
      "algorithm": "ecdsa-p256-sha256-der",
      "signatureBase64": "BASE64_OF_DER_ENCODED_ECDSA_SIGNATURE"
    }
  ]
}
```

The detached-signature envelope is size bounded and strictly parsed only to select a signature and an already embedded `keyId`; it cannot supply a trusted public key. The verifier computes SHA-256 over the exact manifest bytes, compares `manifestSha256` in constant time, Base64-decodes the ASN.1 DER-encoded signature, and verifies ECDSA P-256 with SHA-256 using the embedded DER SubjectPublicKeyInfo for that `keyId`. Only after a trusted signature succeeds may the manifest bytes be parsed as JSON. Unknown algorithms or key IDs, duplicate signatures, malformed Base64 or DER, a manifest-supplied key, or any mismatch rejects the result.

## Trust root and key rotation

Recommended trust model:

- keep each ECDSA P-256 manifest private key offline or in a hardware-backed signing device; never store a private key in the repository or an ordinary Actions secret;
- embed an explicit current/next trust ring in application code, mapping fixed key IDs to DER SubjectPublicKeyInfo bytes;
- permit downloaded release metadata and signature envelopes to reference only a key ID already present in that embedded ring; manifest-supplied public keys are never trusted;
- use no trust on first use (TOFU), remote key discovery, or signature-authorized key injection;
- keep the key IDs and SubjectPublicKeyInfo bytes in one shared .NET verifier used by Supervisor and Configurator; TUI and overlay consume verified state and do not implement crypto;
- treat keyless Sigstore workflow identity and GitHub attestations as independent provenance evidence, not as a remotely replaceable application trust root;
- require protected-environment approval before the draft-publish job, even though the manifest was signed offline.

The production trust-root registry is isolated in `ProductionUpdateTrustRoots.cs` and intentionally remains empty until approved offline public-key bytes and key IDs are supplied for an auditable rotation commit. Tests generate ephemeral P-256 keys only in the test assembly. An empty production registry rejects every signature rather than falling back to a remote key or TOFU.

Routine rotation is an overlap:

1. release N is signed by the embedded current key and embeds the next public key and key ID in application code;
2. release N+1 carries valid signatures from both current and next keys;
3. releases continue dual-signing through an overlap in which supported update-aware clients already embed the next key;
4. a later application release may promote next to current, embed a new next key, and remove the retired key only after the support window permits it.

The signed manifest cannot authorize an unknown trust root. If all private keys corresponding to an already shipped client's embedded ring are compromised, that client cannot be made safe by remote metadata or unsigned revocation; recovery requires a separately distributed application release with a new embedded trust ring.

## Release assets and variant rules

For every stable tag, the draft must contain exactly:

- two ZIP packages;
- one `.sha256`, one `.sigstore.json`, and one attached `.attestation.json` bundle per ZIP;
- the manifest JSON plus its detached signatures, checksum, Sigstore bundle, and attached attestation bundle.

That is 13 named assets in the v1 contract. GitHub's release-level attestation is created/associated at immutable publication and is verified after publication; it is separate from the attached artifact-attestation bundles.

Variant rules:

- only `win-x64-with-dotnet9` and `win-x64-no-dotnet9` are accepted;
- both packages use the same tag/version/commit and contain all required app-owned files;
- the self-contained package must contain the .NET Windows Desktop runtime; the framework-dependent package must not;
- package selection is informational in Phase 33A; the application never chooses or fetches a ZIP;
- asset names are immutable and versioned; no `latest.zip`, aliases, clobber, or same-tag replacement after publication;
- draft replacement remains allowed only before publication, with an operator reason and a complete regeneration/reverification of manifest and companions;
- a failed published release is never repaired in place. Publish a new version/tag; use the documented incident procedure if removal is necessary.

## Update policy and 24-hour scheduling contract

Add one configuration enum in a later implementation:

| Value | Contract |
| --- | --- |
| `Disabled` | No automatic network request. Cached state may still be displayed. An explicit Configurator **Check now** is allowed. Missing/invalid values fail closed to this value. |
| `NotifyStable` | Supervisor may check the published stable channel automatically and show notifications. It still cannot download or install anything. |

The packaged default config may select `NotifyStable` for new installations, but an upgraded config with no field resolves to `Disabled` until the user chooses and saves a policy. This avoids silently adding network traffic for existing installations.

Automatic scheduling:

1. Supervisor is the only automatic-check owner. TUI and overlay make no Internet requests.
2. Persist `lastAttemptUtc` before starting network I/O, but determine eligibility only from `lastSuccessfulCheckUtc`.
3. An automatic attempt is due when no successful completion exists or when at least 24 hours have elapsed since the last successful completion; the exact 24-hour boundary is due.
4. A long-running session waits until that boundary and performs one check when it becomes due.
5. Run only after normal Supervisor startup is stable, on a cancellable background task. Never await it from startup readiness, command handling, action 7, replacement/recovery, reconnect, cleanup, or exit.
6. Permit at most one automatic attempt per Supervisor session. Failure does not advance the successful-completion timestamp, and the same session never rapidly retries.
7. A manual Configurator check bypasses policy and the automatic window under every policy, but only one check runs at a time; a successful manual check advances `lastSuccessfulCheckUtc`.
8. HTTP uses no GitHub credential, a fixed User-Agent, explicit connect/request timeouts, bounded redirects to the exact required GitHub-owned HTTPS hosts, and the response caps above.
9. Conditional requests may use a persisted ETag, but HTTP `304` can reuse only a previously signature-verified cached result.
10. Clock rollback suppresses the automatic request conservatively. Invalid or corrupt state recovers to the disabled default instead of causing a request burst.
11. One dismissal applies only to one verified version. A later verified version may notify again.

## Persistent update-state v1

Proposed `%LOCALAPPDATA%\PimaxVrcSupervisor\Update\update-state-v1.json`:

```json
{
  "schemaVersion": 1,
  "policy": "notifyStable",
  "channel": "stable",
  "installedVariant": "with-dotnet9",
  "lastAttemptUtc": "2026-07-20T19:04:50.0000000Z",
  "lastSuccessfulCheckUtc": "2026-07-20T19:05:00.0000000Z",
  "etag": "W/\"example\"",
  "lastManifestSha256": "3333333333333333333333333333333333333333333333333333333333333333",
  "highestAcceptedReleaseSequence": 1,
  "highestAcceptedVersion": "1.4.0",
  "latestVerifiedVersion": "1.4.0",
  "latestVerifiedTag": "v1.4.0",
  "latestVerifiedReleaseUrl": "https://github.com/Zaknin/Pimax-VRC-Supervisor/releases/tag/v1.4.0",
  "dismissedVersion": null,
  "dismissedAtUtc": null,
  "lastError": null,
  "packageDownload": null,
  "packageStaging": null,
  "packageInstallation": null
}
```

`lastError` contains only a bounded category, bounded code, and UTC timestamp. Allowed categories include `offline`, `timeout`, `http`, `schema`, `signature`, `rollback`, `releaseMismatch`, `state`, and `cancelled`; do not persist raw response bodies, stack traces, tokens, headers other than ETag, or package URLs. The three package fields are reserved schema slots that must remain `null` in Phase 33A.

State is a cache, not authority. `updateAvailable` may be shown only when the current process has successfully revalidated state integrity/semantics or has accepted a fresh verified result under the current embedded trust root.

## UX, bridge, diagnostics, and configuration plan

### Configurator

- Add an **Updates** tab containing the policy selector, installed version, last/next check, verification status, latest verified version, and failure category.
- Add **Check now**, **Open release page**, and **Dismiss this version**. Only the explicit open action launches the fixed canonical HTTPS URL.
- Do not add Download, Install, Replace, Restart, or Apply Update controls.
- Saving the policy changes only `UpdatePolicy`; update-state writes stay in LocalAppData and are not mixed into `supervisor.config.json`.

### Supervisor and bridge

- Add a standalone `UpdateDiscoveryService` with no references to lifecycle, USB, recovery, process-launch, SteamVR, or settings components.
- Extend the structured status snapshot with an optional `update` object: policy, check state, latest version, update-available flag, dismissed flag, and canonical release URL.
- Add read-only `query-json {"resource":"updates"}`. If manual checking through a running Supervisor is later desired, expose a separate low-risk, non-lifecycle `check-updates` request with one-operation deduplication; it must not be a session action card.
- Preserve `line-oriented-tcp-v1` and all existing fields. Older clients ignore the additive field; newer clients tolerate an older Supervisor with no update object.

### Terminal UI

- Render a non-modal banner only for a verified, non-dismissed version newer than the running version.
- Show `Update vX.Y.Z available - open Configurator or the GitHub release page`; do not auto-open a browser and do not add a download/install action.
- A failed or offline check belongs in the System/details view, not the operator-warning channel used for session failures.

### SteamVR overlay

- Render one compact informational line from Supervisor's verified bridge projection.
- Never perform network or signature work in the overlay host.
- Do not add an overlay button that downloads, installs, launches a browser, or changes update policy. Overlay disappearance/reconnect simply re-reads cached status.

### Diagnostics

- Emit bounded events for scheduled/manual start, HTTP outcome, verification outcome, update available/current, state recovery, and dismissal.
- Record version/tag, elapsed time, response size, and bounded failure category only. Never log manifest/signature bodies, GitHub response headers beyond an ETag hash, credentials, or local package paths.
- Update diagnostics are passive and cannot set session `OperatorWarning`, trigger an action result, or alter existing diagnostics enablement.

## Required release-workflow changes

1. Enable **Settings -> Releases -> Enable release immutability** once with repository administration, preferably enforced by owner policy. Do not give the release job an administration token merely to toggle the setting.
2. Add a hard preflight using the immutable-releases API. Fail before build/publish unless `enabled=true` or `enforced_by_owner=true`.
3. Pin every action, including Rust toolchain and Cosign installer, to reviewed full commit SHAs and use Dependabot for controlled updates.
4. Make missing Authenticode credentials a release failure, or explicitly remove Authenticode from the claimed release contract. Do not silently publish a partially signed release.
5. Build/sign/package once, then create checksums and Sigstore bundles from the final ZIP bytes.
6. Use current `actions/attest` with least-privilege `contents: read`, `id-token: write`, `attestations: write`, and `artifact-metadata: write`; copy each `bundle-path` into the final asset set.
7. Generate the manifest from final file names, sizes, SHA-256 digests, tag, and commit. Obtain its offline detached signature, then independently checksum, Sigstore-sign, and attest the exact manifest bytes.
8. Create the GitHub release as a draft. Upload all 13 final assets while it remains a draft.
9. Re-query the draft and compare the exact asset-name/count/size/digest set. Verify checksums, offline manifest signature, Sigstore identities, attestation subjects/signer workflow, package inventory, version parity, and tag/commit parity before publication.
10. Gate publication behind a protected GitHub Environment/manual approval. Publish the existing draft with `gh release edit TAG --draft=false`; never create a second release.
11. After publication, require `isDraft=false`, `isImmutable=true`, the same tag/commit, and the same asset digests. Run:

    ```powershell
    gh release verify $tag --repo Zaknin/Pimax-VRC-Supervisor
    gh release verify-asset $tag $localAsset --repo Zaknin/Pimax-VRC-Supervisor
    Get-FileHash $localZip -Algorithm SHA256
    cosign verify-blob $localZip --bundle $sigstoreBundle --certificate-identity "https://github.com/Zaknin/Pimax-VRC-Supervisor/.github/workflows/release.yml@refs/tags/$tag" --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
    gh attestation verify $localAsset --repo Zaknin/Pimax-VRC-Supervisor --signer-workflow Zaknin/Pimax-VRC-Supervisor/.github/workflows/release.yml
    ```

    Repeat asset-level verification for every final asset covered by the release/attestation contract. Use the still-present runner files; no post-publication release download is required.
12. Delete or disable `sign-release-assets.yml`. No workflow may upload, replace, resign, or clobber assets after publication.
13. If post-publication verification fails, fail and alert without attempting mutation. Investigate and publish a new version/tag; do not reuse the immutable tag.
14. Leave the historical mutable `v1.3.1` release unchanged. Do not retrofit, replace, or republish its assets when enabling this contract for future tags.

## Phase 33A test strategy

### Automated tests

1. Version parity across all .NET projects, Cargo, tag, package names, manifest, and release notes.
2. Golden exact-byte manifest/signature vectors: valid embedded current/next keys, DER SubjectPublicKeyInfo import, Base64 DER signatures, one-byte mutation, wrong digest, unknown key/algorithm, malformed/truncated/non-DER signature, duplicate signature, and rotation overlap.
3. Trust-boundary tests prove signature verification precedes manifest JSON parsing and reject a manifest/envelope-supplied public key, an unembedded `keyId`, TOFU, remote key discovery, BOM, comments, trailing comma, duplicate/unknown properties, oversized body, invalid UTF-8, wrong types, fractional/negative numbers, and timestamp offsets.
4. Semantic cases: repository/channel mismatch, bad tag/URL/commit, asset name/version mismatch, missing/duplicate/extra variant, hash/size mismatch, draft/prerelease/mutable release, lower/equal/newer versions, stale sequence, and GitHub asset mismatch.
5. HTTP cases with a fake handler: HTTPS enforcement, redirects, host allowlist, timeouts, cancellation, response caps, rate limit, ETag/304 with and without verified cache, and no credentials.
6. Scheduler cases: missing/invalid state, exactly-before/at/after 24 hours, last-success eligibility, one automatic attempt per session, long-running due transition, persisted-before-I/O, manual bypass under every policy, clock rollback, cancellation, and serialized checks.
7. Atomic state cases: partial temp write, replace failure, stale revision, corrupt JSON, and recovery without losing highest accepted sequence.
8. Config cases: exact enum values, missing/invalid fail closed, packaged new-install default, Configurator load/save/raw-JSON preservation, and no lifecycle-field mutation.
9. Bridge compatibility: old/new Supervisor and TUI combinations, optional update snapshot, read-only updates resource, overlay reconnect, and no command replay.
10. UX tests: only verified newer versions notify; current/lower/dismissed/failed states do not; no update result becomes `OperatorWarning` or session action state.
11. Source guards allow only release-metadata, manifest, and detached-signature retrieval, and reject package download/staging/extraction/installation/process execution, install-directory writes, arbitrary URLs, update references from lifecycle/recovery/USB modules, and post-publish `gh release upload --clobber`.
12. Release-workflow fixtures prove exact 13-asset inventory, draft-first ordering, immutability preflight, signing/attestation before publish, protected publish gate, and all post-publish verification commands.
13. Preserve the full accepted .NET and Rust regression suites, strict MkDocs build, package-inventory checks, and Phase 32D/32E lifecycle tests.

### Manual tests after design review

1. With a local fake HTTPS endpoint/test harness, verify notification for a valid signed newer manifest without downloading a package.
2. Repeat offline, timed out, malformed, invalid-signature, rollback, mutable-release, draft, and prerelease cases; Supervisor/session behavior must remain unchanged.
3. Keep Supervisor running longer than the eligibility boundary and prove one automatic attempt per 24 hours across restarts; prove manual checking is explicit and deduplicated.
4. Confirm Configurator, TUI, and overlay show the same verified result; dismiss one version and confirm a later version notifies again.
5. Run action 7, runtime replacement, Pimax scoped recovery, client reconnect, dongle remove/reinsert, normal cleanup, emergency cleanup, and exit while checks are pending; prove no lifecycle delay, replay, setting write, or extra process action.
6. Inspect `%LOCALAPPDATA%\PimaxVrcSupervisor\Update` permissions/content and prove no update state appears in either package/install folder.
7. In a controlled release rehearsal, enable immutability first, create a draft, attach the exact complete set, approve and publish once, then run release, asset, checksum, Sigstore, and attestation verification. Never use a production tag for a rehearsal.
8. Confirm both ZIP variants remain usable under their documented runtime prerequisites and contain no update state, generated site, signing key, or private workflow material.

## Proposed Phase 33A implementation commit sequence

1. `Document Phase 33A secure update discovery contract` - this document and MkDocs navigation only.
2. `Add Phase 33A manifest and state contracts` - strict models, signature verifier, version/asset validation, state store, and unit vectors; no network/UI.
3. `Add bounded GitHub update discovery scheduling` - fixed-origin metadata fetch, 24-hour owner, diagnostics, and service tests; no package retrieval.
4. `Expose verified update status to Configurator and bridge` - policy field, Configurator Updates tab, additive read-only status/query contract.
5. `Show verified update notifications in TUI and overlay` - informational projections only, with compatibility tests.
6. `Harden Phase 33A immutable release workflow` - draft-first complete assets, signed manifest, attached attestations, protected publication, post-publication verification, and retirement of the post-publish signing workflow.
7. `Document Phase 33A operator verification and acceptance` - user/release verification instructions and recorded manual evidence.

The embedded current/next trust root, exact-byte DER signature format, no-TOFU rule, retrieval-only Phase 33A boundary, and historical treatment of `v1.3.1` are fixed v1 decisions. This discovery commit authorizes documentation only; implementation remains separately gated and must follow the proposed sequence.

## Primary references

- [GitHub immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)
- [Preventing changes to releases](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/establish-provenance-and-integrity/prevent-release-changes)
- [GitHub CLI release verification](https://cli.github.com/manual/gh_release_verify)
- [GitHub CLI release-asset verification](https://cli.github.com/manual/gh_release_verify-asset)
- [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
- [GitHub offline attestation verification](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/verify-attestations-offline)
- [Sigstore threat model and trust root](https://docs.sigstore.dev/about/threat-model/)
- [Cosign blob verification](https://docs.sigstore.dev/quickstart/quickstart-cosign/)
