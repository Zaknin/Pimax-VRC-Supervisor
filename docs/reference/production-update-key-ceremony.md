# Production Update Trust-Key Ceremony

This procedure creates the offline ECDSA P-256 key pair used as the application's primary update-manifest trust root. It is an explicit operator action and is never invoked by builds, tests, packaging, GitHub Actions, or the application.

## Approval boundary

The ceremony has two stages:

1. generate the key pair and a bounded pending-approval record outside the repository and deployment directories;
2. stop and obtain operator approval of the exact `keyId`, SHA-256 public-key fingerprint, and DER SubjectPublicKeyInfo file before embedding any bytes in `ProductionUpdateTrustRoots.cs`.

Do not continue to trust enrollment when any value or path is unexpected.

## Select operator-controlled paths

Use an offline or hardware-controlled Windows system. Create the output directories yourself before running the tool. The private key path must remain operator controlled and must not be beneath:

- the repository;
- an installation or acceptance-deployment directory;
- a GitHub Actions workspace, artifact, environment, or secrets store;
- a synchronized/shared directory that is not approved for private-key custody.

The tool requires three distinct absolute paths and refuses existing files. Recommended extensions are enforced:

- private key: `.pk8` containing PKCS#8 DER;
- public key: `.der` containing DER SubjectPublicKeyInfo;
- ceremony record: `.json` containing public evidence only.

Choose one stable identifier such as `pimax-update-primary-2026`. A later overlap key receives a different stable ID; identifiers are never recycled.

## Generate once

From a reviewed clean checkout, invoke the tool explicitly:

```powershell
.\scripts\New-ProductionUpdateTrustKey.ps1 `
  -KeyId 'pimax-update-primary-2026' `
  -PrivateKeyPath 'X:\PimaxUpdateTrust\pimax-update-primary-2026.private.pk8' `
  -PublicKeyPath 'D:\PimaxTrustApproval\pimax-update-primary-2026.public.spki.der' `
  -RecordPath 'D:\PimaxTrustApproval\pimax-update-primary-2026.ceremony.json' `
  -RepositoryRoot 'C:\reviewed\Pimax-VRC-Supervisor' `
  -DeploymentRoot 'C:\Users\operator\Documents\PimaxVrcSupervisor-TestDeployments'
```

The tool:

- generates one ECDSA P-256 key pair;
- exports the private key only to the explicit PKCS#8 path and restricts that file to the current Windows identity;
- exports only the public key as DER SubjectPublicKeyInfo;
- imports the public DER again and rejects anything other than exact P-256 SPKI;
- computes SHA-256 over the exact public DER bytes;
- writes a UTF-8-without-BOM record smaller than 16 KiB;
- records no private-key material or private-key path;
- prints the key ID, fingerprint, public DER path, private custody path, and ceremony-record path, never private material;
- removes only outputs created by the failed invocation if any step cannot complete.

There is no overwrite switch. If any final path already exists, stop and review rather than replacing it.

## Approval evidence

Independently calculate the public fingerprint:

```powershell
(Get-FileHash -LiteralPath 'D:\PimaxTrustApproval\pimax-update-primary-2026.public.spki.der' -Algorithm SHA256).Hash.ToLowerInvariant()
```

Compare it with `publicKeySha256` in the ceremony record and the console evidence. Record approval of exactly:

- `keyId`;
- 64-character lowercase public-key SHA-256 fingerprint;
- absolute public DER file path;
- ceremony-record path and digest;
- custodian confirmation that the private key remains only at the approved operator-controlled path.

Only the public DER bytes, approved key ID, and expected fingerprint may enter the repository after approval. The private key and its path never enter source, tests, build output, packages, logs, or release assets.

## Enrolled current key

The Phase 33A enrollment review approved this exact production descriptor on 2026-07-21:

| Field | Approved value |
| --- | --- |
| `keyId` | `pimax-update-primary-2026` |
| Algorithm | ECDSA P-256 with SHA-256 and DER signatures |
| Public-key format | DER SubjectPublicKeyInfo |
| Public-key size | 91 bytes |
| Public-key SHA-256 | `929fa8e2a3a8d46064202a415f6c62e3e731f334be3de4c1d6d7045267371933` |
| Ceremony-record SHA-256 | `7b0a4900a98ddaad26d2b1936b5bd25f3a64ed17ccb23cf5959edc86d48bd584` |

`ProductionUpdateTrustRoots.cs` embeds only the approved public DER bytes. The project file independently records the approved key ID, algorithm, size, and fingerprint; every managed build runs `Test-ProductionUpdateTrustDescriptor.ps1` before compilation. Runtime initialization repeats the canonical Base64, exact size, SHA-256, SPKI import, P-256 key-size, and curve-OID checks. Any mismatch fails closed. The production registry has no test/development bypass and never accepts downloaded keys.

The operator deliberately signed the exact positive notification acceptance fixture offline. Its manifest SHA-256 is `8bedf3676d17427bb1cf1bcc9b4b2097b3a161ed08208ee2d92cddded4b10472`. Only the manifest and detached public signature envelope are test fixtures; the private key is never available to builds or tests.

## Custody record template

Keep the completed record outside the repository, deployment folders, CI, and release assets:

```text
keyId:
public-key SHA-256:
public DER file name and offline media identifier:
ceremony-record SHA-256:
primary custodian:
secondary custodian:
private-key storage control:
offline backup storage control:
last recovery test date and result:
authorized signing operators:
approval date and approvers:
next review date:
loss or compromise incident reference:
```

Do not record private-key bytes, passphrases, or an online/shared private-key path.

## Custody, loss, and suspected compromise

Maintain an offline backup under the same or stronger access controls, with documented custodians and recovery testing. Never test recovery by exposing the private key to CI or a normal deployment.

If the private key is lost before a next key is embedded, existing clients cannot authenticate future manifests; distribute a separately reviewed application build manually with a new embedded trust root. If compromise is suspected, stop signing and publishing immediately, preserve ceremony/signing evidence, and do not attempt remote key injection or TOFU. Use an already embedded next key when safe; otherwise recover through a separately distributed application release.

Rotation uses current/next overlap. Embed and ship the next public key before it is required, dual-sign manifests through the support window, then retire the old public root only in a later reviewed release.
