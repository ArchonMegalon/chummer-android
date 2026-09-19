# Preview 16: local build/signing and observed Internal availability

At `2026-09-19T13:39:31Z`, the authenticated Chummer Play Console showed the
active Internal track with latest **16 (0.1.0-preview.16)**, **Available to
internal testers**. Its localized last-updated display was `19 Sept 15:39`.
Physical Play installation/update remains unverified.

## Product increment and bounded verification

- Six Cyberware/custom-drug pages prepare catalogs in the background, while
  generation and cancellation checks prevent stale render after departure.
- Actual workspace adapters and preference drafts use owner-scoped admission;
  linked accounts cannot adopt another account's draft or a legacy local draft.
- All ten commerce gesture entrypoints bind issued snapshots to the original
  owner transition, workspace/revision, selection, phase and command identity.
  Owner A/B/A, forged snapshots, stale drafts and mutated selections are rejected.
- Focused native selector12, async20 and MAUI lifecycle6 tests passed, alongside
  production FileWorkspaceStore/Preferences owner isolation and action tests.
- Actual local Debug API36 custom-drug and Cyberware slices preserved exact
  drafts through save/reopen/new-process, with changed PIDs, unchanged runner
  revision/document/payload and visibly loaded restored pages. Neither slice
  committed a purchase or recipe. The first SystemUI ANR attempt is preserved
  separately; it is not relabelled as a passing app result.

The tested product head is `e3d2a3f498d1d519dc49f9c1fd4fef3667c1454b`, merged
through PR82 as `c2a9fc7d09a927019139d967e79c5f5cfe199657`, identical tree
`da8f8c195a5ae1de3bcf3ad80eff2d914ee7b6ab`. Release source is
`85b1c3eb8bfbf7b766578e31ca46a7eb898ce3cd`, tree
`2cf812685dac255dc5cbf8a6dcbb68e2fa893fd5`; only version16 and three inventory
hashes changed. PR83 merged that same tree as
`22436bf121c849fe73ee634fbb3e07f706dcbb3a`. PR source check35446027745 and
main source check35446081788 passed. Neither is hosted runtime qualification.

## Artifact and actual transaction

Fresh git-archive exports of the unchanged dependency graph formed a sealed
local source assembly, not a package-only consumer. Keyless offline Docker had
a read-only root/cache, UID/GID1000, dropped capabilities, 4CPU/8GiB limits and
no signing key or Docker socket. Build passed in **2m16.77s**, zero warnings or
errors and no OOM. The first unsigned-verifier start failed on a missing nested
read-only mount target, before execution; after supplying the actual input file
the same isolated verifier passed without weakening permissions or validation.

Bundle inspection checked package, version, API, ABI, privacy flags and absence
of proof instrumentation. All330 content files matched the canonical manifest.
A separate offline signer used the existing upload key read-only. A separate
keyless verifier checked strict signature, exact certificate and unchanged ZIP
payload. No private key was generated, rotated, exported or put in the build.

| Field | Value |
| --- | --- |
| Package | `com.myexternalbrain.chummer` |
| Version | `0.1.0-preview.16`, code16 |
| Signed bytes | `31002287` |
| Signed SHA-256 | `b36282dce73ba73994599f21967cec4370f65c6ce0fa5d0301d32e51249d245b` |
| Unsigned SHA-256 | `84a5454204174bde74846a93a67757597e07f608a7824a0a89bac860dcf83f69` |
| Source-input SHA-256 | `c9f88ad432d29f0b51820a29a23465cfbb2616133987c214d7fbfbe526d0dd4a` |
| Upload certificate SHA-256 | `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15` |
| Docker image | `sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5` |

The [signer output](preview16-local-signing.json) and
[keyless verifier output](preview16-local-verification.json) are unchanged phase
outputs. Later-stage flags remain false as originally emitted. This later
provider observation does not manufacture a hosted publication receipt.

The exact file was uploaded once to Chummer Internal release12. Play parsed
version16, API24+, target36 and arm64-v8a; DE/EN/ES notes describe the bounded
changes. Both Internal publication controls were confirmed, then availability
was read back. Supported device counts did not change. The two warnings were
missing deobfuscation data and missing native debug symbols; neither blocked.
No provider AAB was downloaded for byte comparison. The upload certificate is
not the distinct Play App Signing certificate used for installed APKs.

The retained packet `android-preview16-local-20260919.2sblo9Ae` holds source
exports, scripts, bundles, logs, phase outputs and PLAY_PUBLICATION.json.
Screenshot SHA-256: `f57fc2070e2dc5bd6faf5e964958b5db0145087089620353ac3ee3a1c6f1303c`.
The owned browser session was closed. Previous release evidence is unchanged.
No Production, tester roster, security or billing setting changed.

This does not establish all-wizard, exhaustive parity, physical Play install,
verified HTTPS App Links, tablet, Rook or public readiness. Some review/confirm
work remains synchronous; this is not an all-freezes-resolved claim.

Internal testers can [install or update through Play](https://play.google.com/apps/internaltest/4700678198570024687).
Code16 is consumed; use a higher unused code for the next upload.
