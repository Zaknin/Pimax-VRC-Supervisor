# Phase 33B Secure Update Transaction

Status: design and threat model. This document defines the implementation contract for Phase 33B. It does not authorize a release, tag, publication, GitHub setting change, version change, private-key access, package download, extraction, installation, replacement, or lifecycle mutation.

Baseline: `d2550614a150e8dd1b96f22b786d979fcf4f32f4` (`Improve update status wording`). All product version metadata remains `1.3.1` during Phase 33B development acceptance. The first updater-capable release is installed manually; the first self-update acceptance targets a later immutable stable release.

## Scope and non-negotiable invariants

Phase 33B extends the existing Phase 33A verified discovery chain with an explicitly requested download and installation transaction. The trust decision remains:

```text
embedded production public key
  -> exact detached ECDSA signature over manifest bytes
  -> strict manifest v1 parsing
  -> fixed repository identity
  -> immutable published Stable release
  -> exact tag/version/release identity
  -> exact release asset identity
  -> streamed package size and SHA-256 verification
  -> strict payload inventory verification
  -> offline transaction re-verification
```

The following statements are invariants rather than best-effort behavior:

- A package is never downloaded from configuration, a release title, a browser URL, a user path, TLS alone, a checksum alone, or an unsigned manifest.
- The selected package is the manifest's exact asset for the installed `win-x64-no-dotnet9` or `win-x64-with-dotnet9` variant. A ZIP cannot select a variant.
- Check and download remain unelevated. UAC is considered only after an explicit installation request and all unelevated preflight validation succeeds.
- The installer has no network client, accepts only a bounded transaction ID, and revalidates every trusted input before target mutation.
- Downloaded bytes are data. No downloaded file, ZIP entry, script, or executable is executed before complete verification and a later explicit transaction stage.
- TUI and overlay remain passive cached-status clients. The overlay has no Install action.
- A failed or interrupted transaction favors restoration of the last verified installation. Ambiguous installation state blocks normal lifecycle start.
- Phase 33A repository/signature/immutability/admission/ACL/state protections remain intact and are not weakened.
- No defense against a malicious local administrator is claimed.

## Existing integration surfaces

The Phase 33A verifier already selects one validated package asset in `ValidatedUpdateManifest.SelectedPackage`. Manifest schema v1 has one exact package entry per supported variant, including name, size, SHA-256, RID, and runtime classification. `GitHubUpdateDiscoveryClient` already rejects draft, prerelease, mutable, rollback, repository, signature, manifest, and asset-set failures before returning an update candidate.

`UpdateStateStore` owns `%LOCALAPPDATA%\PimaxVrcSupervisor\Update\update-state-v1.json`, keeps a previous copy, and uses durable temp-write plus atomic replacement. Phase 33B retains that location and expands its bounded, strictly parsed state only through additive versioned records. It does not use the release directory, `%TEMP%`, registry, `Application.UserAppDataPath`, or a scheduled task as a trust store.

The installed variant is currently determined from the running component runtime configuration by `InstalledPackageVariantDetector`. Phase 33B will preserve this trusted running-installation derivation and add package-generated installation metadata only if inspection proves it cannot survive the first manually installed updater-capable release. No ZIP availability heuristic is allowed.

The current watcher can start a Supervisor when SteamVR is active. Its existing local watcher mutex and session logic do not constitute update maintenance. Phase 33B introduces a separate versioned user-scoped maintenance marker and makes watcher behavior fail closed when a valid marker or incomplete transaction requires recovery assessment.

Configuration can reside in the installation directory or externally through `supervisor.active-config.txt` and migration helpers. The transaction must preserve only an explicit allowlist of supported JSON configuration/selection data. `%LOCALAPPDATA%\PimaxVrcSupervisor\Update` remains outside the installation root and is never copied by replacement.

## Trust boundaries and attacker model

### Trust boundaries

| Boundary | Trusted only after | Never trusted as authority |
| --- | --- | --- |
| GitHub release metadata | fixed API endpoint, strict parse, immutable published Stable identity, exact tag/assets | release title/body, arbitrary links, browser redirect destination |
| Manifest/signature | exact-byte ECDSA P-256 verification using embedded production key ID | downloaded/public key material, checksum files, TLS, Sigstore alone |
| Package | selected manifest asset identity, streamed exact size/hash | local filename, extension, cached UI text, partial file |
| Payload manifest | outer ZIP hash first, then strict payload-manifest and extracted-inventory verification | a ZIP entry without matching inventory record |
| Transaction record | fixed LocalAppData root, strict schema, canonical subordinate paths, durable state transitions | command-line paths, environment overrides, arbitrary LocalAppData files |
| Target installation | initiating installed product identity, metadata, canonical handle path, expected current payload | arbitrary path, sibling directory name, executable process name alone |
| Elevated installer | same transaction-only contract independently revalidated | Configurator intent, prior unelevated validation, copied helper path alone |

### Adversary capabilities

The design assumes a network attacker, malicious CDN redirect, compromised same-user writable state, corrupted download, malformed or hostile ZIP, stale transaction, user cancellation, locked files, process death, and power loss. It also assumes a local unprivileged attacker can try path substitution, reparse points, hard links, mutex squatting, and command-line replay.

A malicious local administrator can alter the running binaries, embedded verifier, target filesystem, ACLs, process state, or OS APIs. Phase 33B does not claim to protect against that actor. It instead ensures the updater is not turned into a general privileged copy primitive for lower-privileged attackers.

### Required threat controls

| Threat | Required control |
| --- | --- |
| Forged/altered manifest or wrong key | exact-byte detached signature verification with embedded production key; no key fetched from network |
| Wrong repository, tag, release, mutable/draft/prerelease release | fixed repository endpoint; strict release identity; immutable published Stable requirement; exact tag/version/commit agreement |
| Wrong package variant/name/duplicate asset | selected `ValidatedUpdateManifest` asset only; exact filename, variant, size, asset name, and duplicate rejection |
| Downgrade/replay | installed/highest accepted version and release sequence checks; transaction freshness; completed-transaction replay rejection |
| Truncated, oversized, or altered download | streaming hard maximum and manifest maximum, exact byte count, rolling SHA-256, durable finalization only after match |
| Malicious redirect | no auto-redirect; bounded manual redirect policy limited to approved GitHub release-asset hosts and HTTPS |
| ZIP traversal/absolute/drive/UNC/ADS path | strict segment parser rejecting rooted paths, `..`, colon/ADS, malformed segments, trailing dot/space aliases, and path-length overflow |
| ZIP duplicate, case, Unicode collision | canonical ordinal/case-insensitive/NFC entry-key uniqueness before extraction |
| Reserved Windows names | reject device-name components (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`) including extensions/aliases |
| ZIP links, reparse metadata, encryption, unsupported compression | inspect metadata before extraction; accept only ordinary files/directories using the explicitly supported method; reject all links/reparse/encrypted/unknown entries |
| ZIP bomb | bounded entry count, individual size, total expansion, compression ratio, and staging volume checks before extraction |
| Extra/missing/unexpected executable | payload inventory exactness, per-file hash/size, expected executable version resources, explicit allowed roles |
| Target substitution/root reparse race | canonical target handle/final-path checks; reject roots, prohibited system/user roots, reparse parents, non-product identities, and final-path mismatch immediately before mutation |
| Hard-link confused deputy | reject unsafe link counts for mutable/source/target controlled files; create transaction directories directly beneath validated parent and re-open/validate them |
| Arbitrary elevated replacement | installer accepts only `--transaction <GUID>` and derives all paths beneath fixed LocalAppData transaction root; no target/package/URL/key arguments |
| Watcher respawn/session race | durable maintenance marker validated against transaction; watcher suppresses launch/recovery; refuse active managed VR session; stop only exact product components from target installation |
| Locked files | preflight stop/co-ordination; directory-swap failure rolls back or fails closed; never broad-force-kill SteamVR, VRChat, Pimax Play, or unrelated processes |
| UAC cancellation | privilege decision occurs before staging/target mutation; cancellation records a bounded cancellation result and leaves transaction target untouched |
| Crash/power loss | durable, validated state transitions before destructive actions; deterministic idempotent recovery prefers last verified root |
| Health-check failure | automatic rollback from verified backup, critical-file verification, human-readable restored result |
| Same-user marker/state tampering | strict schema, current-user ACL where required, path containment, ID agreement, revalidation of every trusted package input, and target-parent topology assessment before watcher launch; deletion of all same-user state and topology evidence remains an explicit residual limitation |

## Download and staging contract

### Admission and operation ordering

Phase 33B defines one current-user **update operation admission** contract. It preserves the existing SID-derived Global mutex identity, owner, DACL, and fail-closed behavior, while replacing the check-only process-local wrapper with an operation-wide wrapper that has the same protected cross-process lease:

1. A request obtains the process-local operation slot and existing secure cross-process lease before allocating an operation ID, creating a directory, touching state, or starting network I/O.
2. Network check and download share the network-operation lease. Requests fail fast; they never queue.
3. Install admission is distinct but mutually exclusive with the network-operation lease. Installation is rejected while check/download is active; check/download is rejected while an install transaction is active.
4. The fixed lock order is: process operation slot → current-user secure cross-process admission → operation-owned record writer. `UpdateStateStore`'s internal per-process writer is not a composable cross-process lock and is never acquired independently as an ordering primitive.
5. State persistence occurs only while the admitted operation owns the protected lease. Each record has one owner and an atomic durable writer; no network request is made while waiting to retry a state write.
6. Every lease is released in `finally`; operation ID allocation follows successful admission only.

The resulting states are `Idle`, `Checking`, `DownloadPreparing`, `Downloading`, `DownloadedAndVerified`, `InstallPreparing`, `Installing`, `Recovering`, and terminal bounded failure/cancellation states. Cached status is descriptive; it is never an authorization to download or install.

### Storage layout and ACL

All package and transaction material is beneath the fixed current-user root:

```text
%LOCALAPPDATA%\PimaxVrcSupervisor\Update\
  update-state-v1.json
  Metadata\
  Packages\
    <version>\
      <variant>\
        <filename>.partial
        <filename>
        verified-package.v1.json
  Transactions\
    <operation-guid>\
      transaction.v1.json
      maintenance.v1.json
      extract\
      new-root\
      failed-root\
```

Every path is constructed from validated fixed identifiers; no manifest string becomes a directory component except after strict comparison to the expected safe filename. The update directory and newly created child directories receive an owner/current-user-only ACL where Windows requires it. Existing directories, files, parents, and final handles are checked for reparse behavior before use.

The downloader writes only `<filename>.partial`, uses `FileMode.CreateNew`, streams response bytes directly to disk, computes SHA-256 while writing, enforces both the manifest-declared size and independent hard maximum, flushes to disk, verifies exact final size/digest, then atomically renames to `<filename>`. A partial file is never installable. Failed/cancelled files are deleted or quarantined under the fixed root with a bounded diagnostic record; interrupted downloads restart at zero and have no resume path.

No full ZIP is buffered in memory. The downloader uses explicit connect, request, and idle timeouts, cancellation, a fixed product User-Agent, no GitHub token, response-header limits, and manual HTTPS redirect validation for only necessary GitHub-owned release-asset endpoints. Diagnostics store only bounded category/code/host information: never full transient URLs, response bodies, package bytes, signatures, credentials, or private keys.

Before download, the application refreshes/revalidates the same signed immutable release and resolves the exact release asset through the fixed GitHub release identity. It requires exact asset name and metadata size agreement; it never derives a URL from an untrusted manifest string.

`verified-package.v1.json` is evidence, not a new trust root. It records schema, operation ID, version/tag/repository/release identity, variant/filename, expected/actual size and hash, manifest digest, key ID, verified timestamp, canonical local package-relative path, and `DownloadedAndVerified`. It is bounded, atomically written, strictly parsed, and independently rechecked by the installer.

### State compatibility and ownership

`update-state-v1.json` remains a Phase 33A-compatible cached-status projection during Commit 2: its reserved `packageDownload`, `packageStaging`, and `packageInstallation` fields remain `null`, and an older UpdateWorker therefore fails closed rather than accepting new package state. Download evidence lives in the separate per-package `verified-package.v1.json`; installation/recovery evidence lives in a separate per-operation `transaction.v1.json`. Both are strict, versioned records with atomic replace/flush and previous-copy recovery, never extensions silently inserted into the current v1 status schema.

The operation coordinator is the only component allowed to create an operation ID, select a record path, or write its record after it obtains the shared protected admission lease. A later bridge projection reads bounded summaries only. This removes cross-process writer races without treating the existing `UpdateStateStore` semaphore as a global lock, and gives old workers a deterministic null-slot compatibility behavior.

## Package payload contract

Each package will contain exactly one `install-payload.v1.json` at the ZIP root, generated by packaging before ZIP creation. The outer signed ZIP hash protects it; to avoid circular hashing, it inventories every shipped regular payload file except this one fixed manifest entry.

The manifest must contain:

- schema version and product identity;
- application version, source commit, platform, architecture, and package variant;
- exact normalized relative file list;
- per-file role, size, and lowercase SHA-256;
- expected executable file-version resources;
- an explicit mutable-configuration policy.

The ZIP contains no explicit directory entries; allowed parent directories are derived only from validated payload-file paths. Thus the only regular ZIP entry omitted from the payload inventory is the single fixed root `install-payload.v1.json`; it is authenticated by the verified outer ZIP hash. All other ZIP entries must appear exactly once in the payload inventory, and every inventory item must appear exactly once in the ZIP.

The package script generates payload inventory from the final publish directory after stale-output rejection and before ZIP creation; it includes `PimaxVrcSupervisor.UpdateInstaller.exe` exactly once for both package variants. The release-candidate workflow validates payload manifest generation, ZIP inventory, executable version parity, and both variants before candidate creation. `ReleasePipeline` expected-package validation and publisher inventory are expanded in the same slice so that an updater-capable ZIP has the new helper and payload manifest exactly once, while package asset names/manifests remain the existing exact two-variant release contract. A release without valid updater-capable installation metadata cannot start an update transaction: the first such release is installed manually and is the only transition into self-update capability. No private/test key, fixture, source, PDB, candidate material, or local update state is shipped.

## Installer and elevation contract

`PimaxVrcSupervisor.UpdateInstaller.exe` is a dedicated production executable with:

```xml
<requestedExecutionLevel level="asInvoker" uiAccess="false"/>
```

It has no `HttpClient`, socket, GitHub API, URL, downloader, or package-acquisition dependency. Static and runtime tests prove that it performs no network operation.

The only accepted initial command is:

```text
PimaxVrcSupervisor.UpdateInstaller.exe --transaction <operation-guid>
```

The installer rejects unknown switches, missing/malformed GUIDs, duplicate arguments, relative/absolute paths, URLs, target/package/key/repository arguments, and environment-variable path overrides. It resolves the transaction beneath the fixed LocalAppData transaction root, canonicalizes it, and rejects path containment failure.

Configurator initiates installation only after explicit confirmation. It creates a prepared transaction but **does not copy or elevate a transaction-local executable**. Instead, it opens the already-installed `PimaxVrcSupervisor.UpdateInstaller.exe` from the validated initiating root with a no-write/no-delete file-sharing lease, verifies its bytes and final handle path against the installed payload inventory, and holds that lease until the child has opened the same pinned image lease. The as-invoker child repeats the handle/path/hash validation before any elevation decision. This is the required deviation from a transaction-directory bootstrap: a current-user-writable transaction directory cannot securely bind the image that Windows loads through `runas`.

The helper validates the transaction unelevated first. It determines whether canonical target-parent writes are possible. If they are, it continues unelevated. If not, it retains the verified no-write/no-delete image lease and relaunches the same installed, pinned image through the Windows `runas` verb with the same transaction-only argument. The elevated instance opens and validates its own pinned lease before the unelevated parent releases its lease or Configurator exits. An adversarial swap-before-launch and swap-before-`runas` test is mandatory. UAC cancellation is a terminal no-target-mutation cancellation result. Transaction-record preparation is permitted before the decision; extraction and target-root mutation are not.

The implementation must prove that Windows `runas` can load the pinned image while the no-write/no-delete lease is held and that the elevated child acquires its lease before release. If that proof or the adversarial swap tests fail on supported Windows versions, Phase 33B stops: it must not substitute a mutable transaction-local bootstrap or weaken the elevation boundary.

Both initial and elevated executions independently verify production trust-root/key ID, exact manifest/signature, repository/channel/immutable release/tag/version, anti-downgrade state, variant/name/size/hash, manifest digest, package path containment, payload inventory, executable versions, transaction freshness, operation ID, target root identity, and current installed version. An equal/older target version, completed replay, altered transaction, missing evidence, package outside update root, or non-product target fails closed.

### Target identity and mutation boundary

The target is not supplied by the user. The transaction records the canonical directory from which the initiating installed product ran, plus expected product/version/variant metadata. Before every target mutation, the installer verifies that this root:

- is not a filesystem root, Windows, ProgramData, System32, user-profile root, or other prohibited system root;
- has the expected Pimax VRC Supervisor identity and expected current-version executable set;
- agrees with package-generated installation metadata and transaction record;
- has canonical handle-resolved paths matching the recorded root;
- is not a symbolic link, junction, reparse point, or redirected parent;
- has no unsafe hard-linked shipped/mutable file relevant to replacement.

The installer creates new and backup siblings directly under the validated target parent, then validates their final paths and reparse/link state. It never accepts an arbitrary privileged destination and does not silently fall back to per-file copying when safe same-volume directory replacement is unavailable.

## ZIP validation and extraction contract

Before extraction, the installer parses the central directory without invoking or executing content and enforces bounded limits. It rejects all of the following:

- empty, rooted, drive-qualified, UNC, malformed, `.`/`..`, colon/ADS, or trailing-dot/space path segments;
- reserved Windows names, path length overflow, duplicate names, ordinal case-insensitive collisions, or Unicode-normalization collisions;
- encrypted entries, unsupported compression, symbolic links, reparse-point metadata, non-ordinary entry types, and extra directory/file entries;
- excessive entry count, individual file size, total expanded bytes, or compression ratio;
- every entry missing from `install-payload.v1.json`, every listed payload file missing from ZIP, and the payload manifest's self-hash circularity.

Extraction occurs only into a fresh transaction-controlled directory. After extraction, the installer walks without following reparse points, verifies ordinary single-link files where applicable, checks every parent/final path, hashes every file, compares exact size/digest/inventory against the payload manifest, and validates executable version resources. No extracted content runs before all checks pass.

## Durable transaction and rollback

The durable transaction record is versioned, bounded, atomically persisted, flushed before each destructive step, and validated on every read. Legal transitions are explicit; terminal states cannot be replayed. The first implementation uses:

```text
Prepared
  -> ManifestVerified
  -> PackageVerified
  -> Extracted
  -> ExtractedVerified
  -> MaintenanceEntered
  -> ProcessesStopped
  -> NewRootPrepared
  -> CurrentRootBackedUp
  -> NewRootActivated
  -> HealthCheckPending
  -> Committed

any pre-commit failure after CurrentRootBackedUp
  -> RollbackStarted
  -> RollbackCompleted

validation/preflight failure before target mutation
  -> Failed
```

The transaction algorithm is whole-directory replacement on the same filesystem volume:

1. Validate the target and prepare a verified new sibling root.
2. Transfer only explicit safe mutable configuration allowlist items. The initial compatibility allowlist is the supported JSON configuration files `supervisor.config.json` and `supervisor_moved.config.json`, plus the text-only `supervisor.active-config.txt` selection marker. Existing external selected configuration remains external and is not copied by the installer. Each allowed source must be an ordinary non-reparse, expected-link-count file within a bounded size, and JSON configuration is parsed before transfer. `install-payload.v1.json` must mark these names as mutable configuration policy entries: the extracted package is inventory-exact before transfer, then post-transfer health checks parse and constrain these approved mutable entries instead of requiring their shipped digest. Any collision that is not explicitly marked mutable, any optional/malformed selection marker, all unknown binaries, and all arbitrary files are rejected from transfer. Unknown old-root files remain only in the rollback backup.
3. Enter maintenance and persist it before stopping eligible product components.
4. Refuse an active managed VR session. Do not kill SteamVR, VRChat, Pimax Play, base stations, or unrelated processes. Stop only exact product components whose canonical installation path matches the target after confirmation.
5. Rename current root to a validated transaction backup sibling.
6. Rename verified new root to the original target path.
7. Run the non-VR health check.
8. Persist `Committed` only after health success, then clear maintenance and retain/clean backup according to bounded retention policy.

If directory replacement cannot be proven safe (including cross-volume layouts), installation reports unsupported layout and mutates nothing.

On a replacement or health failure, the installer persists `RollbackStarted`, moves failed new root aside, restores backup to the original path, verifies restored critical payload files/version, clears maintenance only after restoration validity, persists `RollbackCompleted`, and reports:

> The update could not be installed. The previous version was restored.

The old shipped payload is restored byte-for-byte. UAC cancellation never enters rollback because it precedes mutation.

## Health check, maintenance, and recovery

A dedicated non-VR health-check command will be hosted in the UpdateWorker surface. It returns one bounded versioned JSON result and validates installed product identity, version metadata, package payload inventory, production trust registry, configuration parse policy, update-state load, and safe bridge-contract initialization. It must not initialize SteamVR, Pimax Play, VRChat, base stations, monitors, managed applications, watcher deployment, TUI, overlay, normal Supervisor lifecycle, or networking.

A versioned atomic maintenance marker under the fixed update root records schema, installation ID, operation ID, target version, transaction state, installer PID/start identity when available, timestamps, and bounded expiry/recovery data. It is trusted only when its strict contents agree with the durable transaction. The watcher must not respawn a Supervisor during valid maintenance. Supervisor refuses managed session start during maintenance. TUI and overlay remain passive/exit as appropriate and do not mutate state.

Watcher and product startup always assess both marker/transaction records and target-parent topology before launch: missing, mismatched, expired, partially deleted, or malformed records are recovery states, not a normal-start authorization. A missing target root, a backup/new/failed sibling using the transaction naming contract, a target identity mismatch, or a reparse/final-path failure suppresses watcher respawn and requires recovery assessment. Only a canonical single product root with no transaction siblings and no recovery evidence may start normally. Stale or incomplete markers do not cause normal startup. Recovery validates transaction/marker/root topology and deterministically continues only a safe commit boundary or restores the last verified working root when uncertain. Recovery is idempotent and requests elevation only when it needs protected mutation. It records bounded evidence without signatures, URLs, package bytes, or secrets.

If scheduled-task state requires temporary change, the exact pre-existing task state is persisted after preparation, restored after commit/rollback, and verified. The updater never recreates, overwrites, or changes unrelated task settings and does not rely solely on disabling the task.

## User experience contract

Configurator owns active actions. It displays human-readable, bounded status only and keeps dark enabled/disabled behavior:

- `Download update`, only after a verified eligible newer release;
- `Cancel download`, only while downloading;
- `Remove downloaded update`, only for local verified/failed evidence;
- `Install update`, only after verified current-variant download, healthy transaction state, no network operation, no install operation, supported target, and no active managed VR session.

The confirmation displays current/target version, current variant, verified package status, possible UAC, product-component closure, non-termination of SteamVR/VRChat, and rollback availability. Primary text uses messages such as `Downloading update 1.4.1…`, `Update 1.4.1 was downloaded and verified.`, `End the current VR session before installing the update.`, `Administrator approval is required to update this installation.`, `Installing update 1.4.1…`, `Update 1.4.1 was installed successfully.`, and `Installation was cancelled. Nothing was changed.` Internal codes remain bounded diagnostics only.

TUI and overlay read cached bridge state only. They may show update availability, progress, downloaded/verified, ready-to-install, or Configurator-required state. They perform no HTTP, local package read, verification, state mutation, helper launch, or installation action.

## Delivery slices and acceptance gates

The implementation is split into these reviewable commits:

1. `Document Phase 33B secure update transaction` — this design/threat model.
2. `Add verified package download and staging` — downloader, records, admission extension, tests, focused security review; no extraction/execution/UAC.
3. `Expose verified download state in update clients` — Configurator states/actions, bridge projection, passive TUI/overlay status, minimum-layout tests; no installer.
4. `Add offline update installer` — as-invoker transaction-only helper, full offline re-verification, ZIP/payload validation, security audit; no live-root replacement.
5. `Add transactional replacement and rollback` — durable state machine, same-volume swaps, health check, preserve allowlist, rollback tests.
6. `Coordinate watcher update maintenance and recovery` — marker, lifecycle coordination, crash recovery, task restoration, focused lifecycle audit.
7. `Finalize Phase 33B acceptance` — final tests, independent audit, external disposable deployment, production-signature operator gate, manual acceptance documentation.

Every code slice starts with focused Debug and Release failing tests and ends with the corresponding focused verification. The final matrix includes managed Debug/Release tests and builds with zero warnings, format verification, Rust formatting/clippy/tests/builds, PowerShell/YAML/release-pipeline validation, strict MkDocs with generated `site` removed, secret scan, package inventory/manifest/version checks, and `git diff --check`.

Automated tests use disposable directories, injected transports/fake processes, and ephemeral keys only. They never mutate an active installation or user-owned deployment and never launch or disturb Pimax, SteamVR, VRChat, Supervisor, watcher, TUI, overlay, or Configurator sessions.

### Production-trust manual gate

Final production-trust acceptance is an explicit operator stop gate. The development agent prepares and reports the exact local package and manifest paths, their SHA-256 values, target version, variant, and expected embedded key ID. It does not access, locate, copy, print, log, or sign with the production private key. The operator signs the exact manifest externally; only then may the detached signature be supplied and verified by the embedded production public key in a disposable external acceptance environment. No production signature fixture is shipped, committed, or treated as complete before this gate.

## Residual limitations

- A malicious local administrator is out of scope.
- A user who can replace trusted running binaries can replace their verifier; Authenticode and OS policy remain independent defenses.
- Same-user mutex squatting can cause fail-closed denial of service; it cannot cause an admitted update operation.
- An attacker running as the same user can delete both user-scoped update records and all transaction topology evidence, or directly destroy a user-writable installation. The updater cannot distinguish that total erasure from ordinary external deletion; it fails closed on every remaining ambiguous marker/topology state but does not claim recovery from evidence that no longer exists. Current-user ACLs protect unrelated users, not malicious code already running as that user.
- Hardware/lifecycle acceptance requiring an active VR environment remains manually scheduled. Automated updater tests use fakes and must not disturb user hardware.
- The updater deliberately refuses unsupported installation layouts rather than implementing unsafe fallback copying.
