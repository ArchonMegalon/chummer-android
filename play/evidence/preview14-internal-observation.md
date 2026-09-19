# Preview 14: local build/signing and observed Play Internal availability

At `2026-09-19T10:20:53Z`, the scoped authenticated Chummer Play Console showed
the active Internal track with latest **14 (0.1.0-preview.14)**, **Available to
internal testers**. The displayed release time was `19 Sept 12:19`; no exact UTC
publication instant is inferred from that localized display.

## Product change and bounded verification

- Wizard metric captions and values share bounded columns, retaining full
  identifiers and accessibility scaling with wrapping rather than truncation.
- Each Android window receives a new Shell; runner/session state remains shared.
  This fixes an actual disposed-IServiceProvider crash reproduced when the system
  font scale changed on the preceding metric-only build.
- Focused metric/menu interaction checks, native compilation, two window-lifetime
  guards and 11 page-refresh tests passed. The corrected Debug x64 APK was built
  without warnings/errors and its 110 canonical content files were verified.
- The real API36 affected route (saved runner -> Career -> Commerce -> Cyberware
  preparation -> saved runner) passed at default and 130% font, including the
  return font transition. Process identity and saved runner revision/document/
  payload digests were unchanged. No purchase was made. Failed pre-fix evidence
  was preserved rather than relabeled. This is not process-restart, all-wizard,
  physical-device or tablet qualification.

These runtime checks belong to application source `f309341e`; only the main
policy merge, version14 and generated hashes changed before the release source
`503b076d3085ee83e66826e5219650cc42d35833`, tree
`ce77980f4638c5981fbbd40b68a72de4df25613a`. PR77 merged the same exact tree to
`facb4c97f35178da165033b03417eaad7dcc7aa8`; the source-check jobs passed on PR
and main. Those short jobs did not build or qualify the app at runtime.

## Actual local transaction

The existing offline Docker image and exact upstream source commits from
[Preview13](preview13-internal-observation.md) were reused unchanged. Inputs were
fresh git-archive exports with explicit roots. The package cache was mounted
read-only, with a transaction-local AAR cache. This is a sealed source assembly,
not package-only consumption or the old hosted signing protocol.

The keyless build ran with no network or Docker socket, a read-only root,
UID/GID1000, dropped capabilities and 4CPU/8GiB limits. It passed in **2m14.88s**,
zero warnings/errors and no OOM. Unsigned bundle/version/API/ABI/privacy, proof
exclusion, hygiene and all110 exact content checks passed before signing.

A separate networkless container used the existing upload key, mounted only
read-only; passwords were restricted to temporary files. No key was generated,
rotated or exported. A separate keyless verifier then checked the expected
certificate, strict JAR signature, unchanged ZIP payload and bundle/proof checks.

The exact verified file was uploaded into Chummer Internal draft release10.
Play parsed version14, API24+, target36 and arm64-v8a. Both publication controls
were confirmed, followed by the successful availability readback above. This is
an actual provider observation, not availability inferred from a local build.

| Field | Value |
| --- | --- |
| Package | `com.myexternalbrain.chummer` |
| Version | `0.1.0-preview.14`, code14 |
| Signed bytes | `30614353` |
| Signed SHA-256 | `4c2ac471b12c57f69cfa4fc90eedece7f5080529c8c601e55fbc43907b20bd30` |
| Unsigned SHA-256 | `b8bbfa2c4ca100f1d05d5ec6247d90b7ab50064d103314c8b56d6dc6b3abffd9` |
| Source-input record SHA-256 | `8f4c3ecf9fd96a8af29f7b35aebac47ea55cc1959bbe67fe74a833abc22e1421` |
| Upload certificate SHA-256 | `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15` |
| Docker image | `sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5` |

The [signer output](preview14-local-signing.json) and
[keyless verifier output](preview14-local-verification.json) are unchanged phase
outputs. Their later-stage flags remain false as emitted; this observation,
not an edited earlier receipt, records the subsequent publication.

Play showed only the existing two nonblocking diagnostic warnings (no
deobfuscation file and no native debug symbols); device counts were unchanged.
No provider AAB was downloaded for a byte comparison. The upload certificate
is not the separate Play App Signing certificate for installed APKs.

Physical Play installation/update, verified HTTPS App Links, exhaustive wizard
coverage, full editing, tablet/Rook readiness and public/Production release are
unclaimed. No tester roster, security/billing setting or Production track changed.
Previews13 and earlier records remain immutable.

The retained packet `android-preview14-local-20260919.YbQIQ1uy` includes exact
sources, scripts, logs, bundles, phase outputs and PLAY_PUBLICATION.json.
Screenshot SHA-256: `d6dd4584504f0af4f771d0eb0ecf5b7611d86caac20cfedeee19ff32c47e1b41`.
All four temporary containers and the owned browser session were closed.

Internal testers can [install or update through Play](https://play.google.com/apps/internaltest/4700678198570024687).
Code14 is consumed; use a higher unused code for the next release.
