# Preview 12: local signing and Play Internal observation

On September 19, 2026 the owner explicitly approved an isolated local Docker
build/signing path using the existing upload key, replacing the three hosted
signing jobs and shared remote storage for this release. Existing source,
credential-isolation, certificate, artifact and Play-readback checks remained.
This record describes that local transaction, not successful execution of the
hosted signing protocol or a fabricated detached hosted attestation.

## What happened

1. The unchanged unsigned AAB from Android
   `0d5c8f0cacfdfbb89288eb0f3906d2fdbab36d0a`, tree
   `7d2585c46236f975a7a4d73a591688fd84b4228f`, was reused. Its prior producer
   completed bundle generation but failed its later external-signer handoff.
   That historical failure is not relabelled as a successful build transaction.
2. An isolated local Docker signer used the existing upload key and checked its
   certificate. No key was generated, rotated or exported. The container had
   no network, a read-only root filesystem, read-only key inputs, no capabilities,
   and temporary password files confined to tmpfs. It exited successfully.
3. A separate keyless container verified the signature and expected certificate,
   unchanged ZIP payload, bundle structure, version/API/ABI and proof exclusion.
   It also exited successfully. No extra broad runtime suite was run for these
   unchanged application bytes.
4. The exact signed AAB was uploaded through the Chummer-scoped Play Console.
   Both Internal publication confirmations were completed. Fresh readback
   showed the track active and Preview 12 available to internal testers.
5. The existing tester link was reread and sent to the operator. No tester-list
   mutation, Production promotion or unrelated account operation occurred.

## Artifact identity

| Field | Value |
| --- | --- |
| Package | `com.myexternalbrain.chummer` |
| Version | `0.1.0-preview.12`, code `12` |
| Signed AAB | `chummer-android-0.1.0-preview.12-upload.aab` |
| Signed size | `30445092` bytes |
| Signed SHA-256 | `ae003be3df43d8885b40e42ecba74410e20354ab17e29e9b9e0b7ef789e2f398` |
| Unsigned SHA-256 | `e26f3d9d2701d21b98a264731cba2567e355f3ebf2754f7287dea727d064b252` |
| Source graph SHA-256 | `60adae1fc108ced9aefe3ff101f1c31e17442c95ad7ed78cd1be0e634b47cc87` |
| Upload certificate SHA-256 | `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15` |
| Docker image | `sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5` |

The upload-certificate identity is not a claim about the separate Play App
Signing certificate used for Play-delivered APKs.

## Evidence and limits

- [Signing output](preview12-local-signing.json) records the signing step only.
  Its verification/upload flags remain false because neither later action had
  happened when that output was emitted.
- [Keyless verification output](preview12-local-verification.json) records the
  subsequent verification, still before upload. Its upload flag remains false.
- [Play browser readback](preview12-internal-browser-readback.json) uses the
  existing `chummer.android.play-internal-browser-readback/v1` contract. It is
  an observation, not the v4 hosted-signing publication receipt. No validator
  or authorization field was changed to make it appear to be one.
- Displayed release time was `19 Sept 06:31`; observation time was
  `2026-09-19T04:35:27Z`. No provider-authoritative UTC release instant is inferred.
- Play review reported two non-blocking warnings: native debug symbols and a
  deobfuscation file were not uploaded. Reported device availability (13,517)
  does not prove tablet or foldable behavior.
- Upload-to-artifact linkage is the operator's exact-file upload action plus
  local digest verification. The Console did not expose a downloadable AAB
  digest, and no Play artifact was downloaded for byte comparison.
- Physical Play installation/update, verified production HTTPS App Links,
  whole phone-beta completion, exhaustive edit parity, tablet readiness,
  SR4/SR6 creation, Rook and public/Production release remain unclaimed.

The private retained packet is
`preview12-local-release-20260919.03qAGdV2`; it holds the AAB, transaction record
and screenshot. The screenshot SHA-256 is
`e62ad9f4f8bbad9f87450fe6119f64357ce95be9c9ea054018cd800e09a8dad5`.
No private key, password, browser session or operator chat identity is included
in this repository record. Preview 10 and earlier evidence remain unchanged.

Internal testers can [join or update through Play](https://play.google.com/apps/internaltest/4700678198570024687).
The next release must use a higher unused version code; do not repeat this upload.
