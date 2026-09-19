# Preview 15: local build/signing and observed Internal availability

At `2026-09-19T12:03:30Z`, the scoped authenticated Chummer Play Console showed
the active Internal track with latest **15 (0.1.0-preview.15)**, **Available to
internal testers**. The displayed release time was `19 Sept 14:03`; that localized
display is not asserted as an exact UTC publication instant. Physical Play
installation/update remains unverified.

## Product change and bounded checks

- Cyberware source/grade and custom-drug grade/component selectors serialize
  draft updates and awaited Back navigation, rejecting overlapping actions.
- Updates bind the displayed runner, revision, catalog/character/rules digests,
  selection and phase; stale selectors cannot overwrite a newer draft.
- Android now bundles the 220 canonical customdata files required by existing
  Core profiles. Full House previously failed closed because these directories
  were absent. Packs are available, not automatically enabled; Core still owns
  profile activation and rule interpretation. No upstream pin or rule changed.
- Focused native selector scenarios12, Cyberware service3, custom-drug service7,
  content/source tests23 and provenance tests43 passed. The provenance tests
  now explicitly version their historical synthetic fixture before sealing;
  production validators and hostile assertions remain unchanged.
- The corrected local Debug x64 APK passed separate API36 Standard Cyberware
  and Full House custom-drug draft/reopen/process-restart checks. Selected typed
  IDs matched persisted drafts. Different process PIDs were verified, restored
  pages were opened, and draft state plus runner revision/document/payload
  hashes remained unchanged. No purchase or recipe was confirmed.
- Earlier failing observer/fixture/content attempts remain preserved. A separate
  emulator System UI boot ANR is not relabelled as an app result. No timeout or
  product validation was relaxed, and no ANR-free cold-start claim is made.

Runtime evidence belongs to the app changes committed at
`29e9e4d33814c429e9b6775b348be77457eac6ea`, merged through PR79 as
`ad0f71c9e19b5addd5640aab6cb17653fb77fa45`. The producing release source is
`d05d2a40c1110924d7d2e3b35d4760a6b6d7207a`, tree
`c0334c0d476f96634c7793e9cb50f6551e9052a6`. Only version15 and three generated
inventory hashes changed after the product test. PR80 merged an identical tree
as `a005691bf4073eb1e99fefc28ef8a16785a3eddf`; short PR/main source checks passed.
Those checks are not hosted APK or runtime qualification.

## Actual local artifact transaction

The exact source inputs and existing offline Docker builder from Preview14 were
reused. Fresh git-archive exports and explicit roots form a source assembly,
not package-only consumption. The keyless build had no network or Docker socket,
a read-only root, UID/GID1000, dropped capabilities, 4CPU/8GiB limits and a
read-only package cache. It passed in **2m10.30s**, zero warnings/errors, no OOM.

Unsigned version/API/ABI/privacy, proof exclusion, hygiene and all330 exact
content files passed. A separate networkless signer used the existing upload
key read-only; no private key was generated, rotated or exported. A separate
keyless verifier checked strict signature, expected certificate, unchanged ZIP
payload and bundle/proof exclusion before upload.

| Field | Value |
| --- | --- |
| Package | `com.myexternalbrain.chummer` |
| Version | `0.1.0-preview.15`, code15 |
| Signed bytes | `30971935` |
| Signed SHA-256 | `42996fa327c85c31f3ea7b1c06c1fe926cca1b483340a01441dbb7d6f40e9d92` |
| Unsigned SHA-256 | `b3d8eab4b9a97412bda3d1fed7dbdab85f84de32bfabbea8ad220dc2cd82d7bc` |
| Source-input SHA-256 | `569a919bd12d4dd00e175f7e9bd4531584b8394a37b9f0fc09a558a10673a451` |
| Upload certificate SHA-256 | `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15` |
| Docker image | `sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5` |

The [signer output](preview15-local-signing.json) and
[keyless verifier output](preview15-local-verification.json) are unchanged phase
outputs. Their later-stage flags remain false as emitted; this later observation
does not rewrite them or manufacture a hosted signing/publication receipt.

The exact file was uploaded into Chummer Internal release11. Play parsed
version15, API24+, target36 and arm64-v8a. Both Internal publication controls
were confirmed and the availability status above was read back. Release notes
were provided in DE/EN/ES and limited to the tested product changes.

Play showed the same two nonblocking warnings: no deobfuscation file and no
native debug symbols. Supported-device counts were unchanged. No provider AAB
was downloaded for byte comparison. The upload certificate is not the separate
Play App Signing certificate used for installed APKs.

Physical installation/update, HTTPS App Links, exhaustive wizard/mutation
coverage, tablet, Rook and public/Production readiness remain unclaimed. No
tester roster, account/security/billing setting or Production track changed.
Preview14 and older evidence remain immutable.

The retained packet `android-preview15-local-20260919.LQNWVtsm` contains exact
sources, scripts, bundles, logs, phase outputs and PLAY_PUBLICATION.json.
Screenshot SHA-256: `64b4729d53113e3cea9cd65a8f9d49f9e6d576e542e6b0e2558e4d37c5899348`.
All four temporary containers and the owned browser session were closed.

Internal testers can [install or update through Play](https://play.google.com/apps/internaltest/4700678198570024687).
Code15 is consumed; use a higher unused code for the next upload.
