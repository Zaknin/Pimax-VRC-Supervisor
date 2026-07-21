# Immutable Release Operations

This runbook is for future updater-capable stable releases. It does not authorize publishing, tag creation, repository-setting changes, version changes, or production-key creation during development.

The first updater-capable release must be installed manually. Phase 33A discovers and reports a verified release; it does not download, stage, extract, install, replace, or execute packages.

## One-time repository preflight

Before the next release is published, a repository administrator must enable **Settings -> Releases -> Enable release immutability**. Do this outside the release workflow, then run the publisher preflight. The publisher refuses to create a draft while immutability is disabled.

Do not edit, recreate, add assets to, or otherwise retrofit the historical mutable `v1.3.1` release. The release tooling rejects that tag.

## Key custody and rotation

The offline ECDSA P-256 private key must never enter the repository, GitHub Actions secrets, GitHub environments, workflow artifacts, CI logs, or release assets. Keep it offline or hardware-backed under the project's approved access, backup, and dual-control procedure. The explicit operator-only [Production Update Trust-Key Ceremony](production-update-key-ceremony.md) creates it outside the repository; no build, test, package, workflow, or application path invokes that tool.

The approved public key used by the local publisher also comes from the separately reviewed trust-root ceremony. The publisher refuses a public-key file inside the repository or candidate so candidate-supplied metadata cannot select the trust decision.

Rotation uses an embedded current/next-key overlap, never trust on first use:

1. commit the reviewed next public key and key ID in an application release while the current key remains accepted;
2. dual-sign during the overlap after supported clients contain the next key;
3. promote/remove keys only in a later reviewed application release after the support window;
4. never accept a public key supplied by a manifest, candidate, release, or network response.

## 1. Build the unsigned candidate

From the GitHub Actions **Release Candidate** workflow, dispatch the reviewed `main` workflow with:

- normalized stable version such as `1.4.0`;
- exact tag `v1.4.0`;
- exact 40-character lowercase source commit;
- channel `stable`.

The remote tag must already exist at that exact commit; this runbook does not create or push it. The workflow validates version parity across all managed and Rust components, runs the complete validation suite, builds both Windows variants, and uploads one artifact named like `PimaxVrcSupervisor-v1.4.0-unsigned-release-candidate`.

The unsigned candidate contains exactly 15 files: the two ZIP variants and their checksum/Sigstore/attestation companions; manifest and its checksum/Sigstore/attestation companions; `SHA256SUMS.txt`; CI verification report; and candidate inventory. It must contain no `.json.sig`, updater signature envelope, offline-signing report, or private key.

## 2. Offline signing ceremony

Move the downloaded candidate to the approved offline signing system. Review the machine-readable inventory, workflow identity, version, tag, commit, sizes, and SHA-256 values. Keep the private-key path outside both the repository checkout and candidate directory.

Run:

```powershell
.\scripts\Invoke-OfflineManifestSigning.ps1 `
  -CandidateDirectory 'D:\release-candidate\v1.4.0' `
  -PrivateKeyPath 'X:\offline-keys\pimax-update-current.private.pk8' `
  -KeyId 'pimax-update-primary-2026' `
  -RepositoryRoot 'C:\reviewed\Pimax-VRC-Supervisor'
```

The tool revalidates every inventoried byte, signs the exact manifest bytes with ECDSA P-256/SHA-256 using DER signature encoding, immediately verifies with the corresponding public key, and prints only the key ID and public-key fingerprint. It creates three bounded outputs:

- `PimaxVrcSupervisor-vX.Y.Z-update-manifest-v1.json.sig` containing one Base64 DER value;
- `PimaxVrcSupervisor-vX.Y.Z-update-manifest-v1.signatures.json` for the application verifier;
- `offline-signing-report.json` containing public signing evidence only.

The tool does not copy or retain the private key. Existing signing outputs cause failure. Use `-Overwrite` only after reviewing why a complete repeat ceremony is necessary; the candidate and key remain subject to every normal validation.

## 3. Draft, verify, and publish

Return the signed 18-file candidate to the publishing workstation. Use a clean checkout whose `HEAD` exactly matches the candidate/source/tag commit. Authenticate GitHub CLI as `Zaknin`, install `cosign`, keep the approved public key outside the checkout and candidate, and choose a report directory outside the candidate.

Run:

```powershell
.\scripts\Publish-ImmutableRelease.ps1 `
  -CandidateDirectory 'D:\release-candidate\v1.4.0' `
  -ApprovedPublicKeyPath 'D:\approved-trust\pimax-update-current.public.spki.der' `
  -ExpectedKeyId 'pimax-update-primary-2026' `
  -ReportDirectory 'D:\release-reports\v1.4.0' `
  -RepositoryRoot 'C:\reviewed\Pimax-VRC-Supervisor' `
  -Confirm
```

The publisher fails closed before remote mutation unless authentication, fixed repository identity, release immutability, remote tag commit, clean local source, inventory, package variants, manifest, signature, key ID, hashes, sizes, and exact asset set all agree.

It then creates or reuses only an exact matching draft, uploads only missing approved assets without clobbering, re-reads the draft, verifies exact names/sizes, and downloads each draft asset into a temporary directory to compare its SHA-256 with the local approved file. Before publishing, it prints repository, tag, version, commit, key ID, draft URL/status, and the complete asset inventory. Publication requires typing exactly `PUBLISH vX.Y.Z`.

The sole publication mutation is changing that existing draft to published once. No upload, replacement, deletion, recreation, or repair mutation is permitted afterward.

## 4. Post-publication verification

The publisher automatically requires all of the following before writing `post-publish-vX.Y.Z-verified.json`:

- published metadata is immutable, not draft, not prerelease, and has the exact tag, commit, canonical URL, title/body, and 18 assets;
- `gh release verify <tag>` succeeds;
- `gh release verify-asset <tag> <local-file>` succeeds for every one of the 18 local assets;
- every `SHA256SUMS.txt` entry matches;
- the exact-byte detached ECDSA signature verifies with the external approved public key and expected embedded key ID;
- each package and the manifest pass Sigstore verification for the exact `main` release-workflow certificate identity and GitHub Actions OIDC issuer;
- each package and the manifest pass `gh attestation verify` for the fixed repository and release workflow.

Keep the success report with the release ceremony evidence. It is deliberately outside the immutable asset set.

## Verification failure and recovery

If a check fails before publication, stop. Leave any draft unpublished, diagnose the mismatch, and regenerate/review the complete candidate if necessary. Never bypass a hash, identity, signature, asset, or immutability failure.

If a check fails after publication, the publisher writes `post-publish-vX.Y.Z-incident.json`, reports the release as verification-failed, and performs no further GitHub mutation. Preserve local assets, reports, workflow run identity, command output, and the published URL for investigation. Do not upload a replacement, delete/recreate the release, reuse the tag, or attempt an automated repair. Correct the release process and publish a new version/tag only after a separate review.

## Key-loss or compromise recovery

If the current private key is lost or suspected compromised, stop signing and publishing. Preserve custody, ceremony, and signing evidence; open an incident; and determine whether a previously embedded next key remains trustworthy.

- If a trusted next key is already embedded in supported clients, use its separately approved custody procedure and publish only a new version/tag after incident review. Do not remotely alter trust metadata.
- If no trusted next key is embedded, existing clients cannot authenticate a new trust root. Build and distribute a separately reviewed application release for manual installation with the replacement public root embedded.
- Never use TOFU, a manifest-supplied key, a downloaded key, a test bypass, a replacement asset on an immutable release, or reuse of the compromised key ID.

The first updater-capable release also requires manual installation because v1.3.1 does not contain the enrolled production trust root.
