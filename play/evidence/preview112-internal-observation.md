# Preview 112 — automatic Life Modules illustration recovery

Authenticated Chummer Play Console readback on 5 October 2026 at 22:27:28 UTC
showed `112 (0.1.0-preview.112)` **Available to internal testers**, Internal
release 106, one version code, released at 00:27 on 6 October Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API or physical-device evidence.
**Physical Play112 installation/update remains unverified.** Version112 is
consumed; never rebuild/re-upload it. Preview111 and its evidence remain retained.
Production, tester audience, listing, billing and account settings were unchanged.

## Changes and bounded verification

An initial illustration-status read failure no longer consumes the first
automatic dispatch opportunity. Existing uncertain jobs remain non-replayable.
A successful observation of the same pending job clears stale paused-reader
copy and refreshes the view. A ready owner-bound account catalog avoids a
redundant grant preflight before its authenticated roster read; cold startup,
expiry, recovery and owner-generation checks remain enforced.

Focused regressions reproduced the stale paused notice before the fix and
passed afterwards. Automatic illustration admission/recovery, unread editorial
refresh, account ownership, cancellation and HTTP rejection checks passed.
The affected native Release x64 SDK-test APK built with zero warnings/errors.

The actual API36 emulator route created a new synthetic Life Modules opening,
received a complete 2,494-word FirstBook chapter (78 prose paragraphs), displayed
one automatically generated 1min.ai scene, and exported through Android's actual
Save book as EPUB picker. The EPUB contains the exact full text and embedded
1536x1024 PNG; it does not depend on a remote image URL. Reading acknowledgement
revealed rule effects only afterwards. A verified force-stop and new-process
launch retained the same runner, full chapter, image and export controls without
a duplicate generation or reading acknowledgement.

The native behavior evidence is reused for the subsequent documentation/version
delta. It uses a separate SDK-test identity/signature, not a Play-managed
installation of the final ARM64 bundle. Initial decision saves took minutes,
and provider progress copy was generic during some active phases. Those latency
and progress-visibility findings remain open. One first-chapter illustration
does **not** establish unattended repeatability, multi-age protagonist continuity,
a complete book through every life stage, or all-method SR5 Creation completion.

## Exact local artifact

- Tested behavioral Android head: `b12207f3a1b747d8b31a284c02f4a81d7229e956`.
- Version-only producer: `ecb596eff685e899847705240fe7ee7e5968e36f`;
  main merge: `9c5336b876927f3f3d52fa784e771bfd19f7f1a6`;
  identical tree: `4f6706184b850a9df8a449db7859b27d792d2b78`.
- PR493/494/495 and version PR496 merged normally after their required checks.
- Presentation seal: `5859961a8d0eef814a2bf86fe702b0013512ae58`.
- Core runtime: `5f4e6350791c2c4fd7959da1f07cc1a85ce5446e`;
  recipe/content: `51c6a6bbdb1a498352f95512b7d47f11e6fa42b7`.
- UI consumer receipt:
  `a21c7dfe547e6d219b6b5333a8cabd4907f34599604f173dd858e0542e459584`.
- Unsigned AAB, 33,795,677 bytes:
  `1a89637c5ae4129a26470fa67fafe9324b10746bca79b392fa6a859d4f7c1d8e`.
- Signed AAB, 33,968,344 bytes:
  `fe4aa138c03d7d3b2ae719c077e1eca600552de1b16ff2e2f9419b98f31077f3`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `5ddc78f2e3f7ba7a7d3c2fd2a6f4e063f30706067a93bdee0cab74fe2985fef3`.
- Independent signed-verification receipt:
  `566577e77d90ce3f6db9e669aa633069d4e609e4bd39620d98ffa1640bbf83e0`.
- Private Console execution record:
  `63b8fc9aad8c14f8911c34b44055780d622a75165c5e1ebd4aba9e0a1eb753be`.
- Availability screenshot:
  `f87007f8b7aeb689b03ea765d4cf83e6a1c48b23b9deace1669559cd505b1baa`.
- Actual native illustrated EPUB:
  `7e991d666c50a7677c98452deeebb3edc72792234577bc139b97b7bb05b395d2`.

The offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, all 331 embedded content files, bundle
identity, API24+/target36/ARM64, privacy flags, proof exclusion and credential
hygiene passed. Dependency mode is locked package closure with explicit pinned
Presentation source and Core content; no ambient sibling discovery, **not
package-only, hosted qualification or a two-green receipt**. Separate existing-key
signing passed independent keyless strict JAR/certificate verification and
comparison of all 1,783 non-signature payload entries. Release containers exited.

One exact upload and one final Internal rollout confirmation used the standing
2026-10-05 approval; authorization is separate from candidate verification.
Missing deobfuscation mapping and native symbols remain non-blocking Console
warnings. Device catalogue presence is not tablet authority. The upload
certificate does not establish the Play App Signing identity. No provider
generation was replayed during this release transaction. Private packets
`origin-release112-20261006.KRnf8gP5` and
`origin-next-chapter-20261005.obXatyPd` retain the artifacts and route evidence
outside served directories. No credentials or private character facts are
included here. Full Creation and general beta are not claimed complete.
