# Preview 150 — saved lifestyle digest allocation

Authenticated Chummer Play Console readback at `2026-10-08T21:23:06Z` showed
`150 (0.1.0-preview.150)` **Available to internal testers**, one version code,
Internal release 144, released 8 October at 23:22 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API evidence.
**Physical Play 150 installation/update remains unverified.** Play says changes
usually appear within one hour and can take longer. Version 150 is consumed:
do not rebuild or re-upload it. Preview 149 and earlier evidence stay unchanged.

## Change and focused verification

Core lifestyle integrity hashing reuses the existing canonical streaming digest
instead of retaining full serialized/canonical buffers and scalar/sorting copies.
Canonical output, fresh mutable-input reads, rule replay, owner/revision checks,
constant-time comparison and persistence semantics remain unchanged. The
[package intake record](../../docs/evidence/lifestyle-digest-package-intake-20261008.md)
retains exact inputs, checks and nonpasses.

All 50 focused digest/lifestyle tests pass; only the new allocation regression
fails on the baseline. An offline read of eleven retained synthetic runners
preserves their JSON bytes and reduces warm-roster allocated bytes from
95,204,480 to 66,874,384 (29.8%). This is not an Android startup-speed claim.
The Core producer passed 783 product tests and owner/inventory checks. Exact UI
consumer verification passed five builds, 879 product tests and focused groups;
the test-project build retains 62 analyzer warnings, no compiler errors.

Android intake compiled native-source, actual MAUI and interaction tests with
zero warnings/errors. Six focused restore/recovery paths and exact 18-package,
13-authority-file and 331-content-file checks passed. Final repin verification
passed 51 package, 124 affected pin/source/provenance and 57 bounded safety cases.
An over-selected legacy 88-case contract suite is not green: three failures and
fifteen errors have the identical failing case set before/after repin. Those
nonpasses and corrected invocation errors are retained, not relabelled.

The local Release SDK-test x64 APK updated the retained API 36 installation
149→150 without uninstall/reset. Eleven-runner restoration, Home, Creation and
force-stop/new-process reopening of the same Adept draft passed. All 33 workspace
files (9,132,743 bytes) remained identical. No new mutation/finalization is claimed.
This APK used Android 95ed55a9 and the earlier UI seal d5a1e0ee, whose full tree
is identical to final a9cbac13. The final Android repin changes provenance and
derived Career authority digests; the prior native smoke is not represented as
an exact-final-repin APK test. Final ARM64 compilation is separate evidence.

Startup remains slow: initial shell/restore 37.282/59.193 seconds; new-process
27.475/72.917 seconds. The emulator was under load and the 149 baseline had a
SystemUI ANR. These are not controlled speed measurements or proof that startup
is fixed. The owned emulator stopped, preserving the fixture. Full chapters and
per-chapter illustrations remain in the native reader and EPUB; unchanged
[chapter-image coverage](../../docs/ORIGIN_EPUB_AND_SCENES.md) is reused, not
claimed as a new provider/physical rendering test.

## Exact local artifact

- Android producer: `007083f4196f66e0922b5d36187e67fad7490b91`;
  protected PR 585 merge: `0cc4f85429c939af327cd528b3dc2744efe7331a`;
  identical tree: `4b4c9754756a0eeebb476ab6ff7565d70b67ea08`.
- Presentation seal: `a9cbac136ece444ac74d1a24c40f5d366fa673a7`;
  protected PR 367 merge: `0a14f42e9ed4ccac7bbeedfadefe7c8c6d98b6e8`;
  identical tree: `53687a61e89b7842da99e31f58b9cf0d03d5536f`.
- Core runtime: `d884b1f17025847704c64d4957f87253f9bca31b`;
  recipe/content: `9f44361649bd109f522a6610b41cb9f1def75905`;
  protected PR 133 merge: `76deda288efd8d9ae8046d6060c887495bb1ca19`.
- UI verification: `09ed78fa086d579cf1579e42e52eb5e4f33c40923e9bb19b2cbc6f151364d48a`.
- SDK-test APK: `c914e8b0203fc9395a75bb0f558e491ec6fc3e65a634d6adfeaa10fa88584cb9`.
- Unsigned AAB, 34,960,844 bytes:
  `dc82f214f60d985b5818fb27b8018c052407dd78111d4e638f2d55706e847ddb`.
- Signed AAB, 35,133,490 bytes:
  `bdc3ce50727c3ad6eb49cac7387ca24d741cc1d114517593c2c9c26e7202f8db`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `662268447424a2c57ef7fa66950a8c977d88b94cb60572b87c98891aad2aa822`.
- Independent keyless verification receipt:
  `d76ec293758167d508505effb1609214aad82ba70ec17fdd2eeaf53a5e126358`.
- Private Console execution record:
  `63681f469a945234a7994f88192731730b3d24a4e1978906b00c94e88688faa3`.
- Visually inspected availability screenshot:
  `2764e25fcfa5114e78b6ff995bb0fcf46f2f5ffc85be281d1f8349f32cc19870`.

One offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exported source/content, package checks, bundle/version/
API/ARM64 inspection, proof exclusion and credential hygiene passed. Dependency
mode is locked packages with explicitly pinned Presentation source and Core
content, not source-free package-only assembly. A separate offline signer used
the existing key. Independent keyless strict JAR/certificate verification and
all 1,783 unchanged unsigned payload entries passed. Stage receipts retain their
original pre-upload status; the separate Console record proves later availability.
Raw packets and signing material stay private.

One upload and one final confirmation used standing owner Internal approval of
2026-10-05, separately from candidate verification. Scoped browser/OODA checks
verified account/app/track and actual availability. Play recognized 150, API 24+,
target 36 and ARM64; only missing mapping/native-symbol warnings, no errors or
supported-device delta. No audience, public-track, account, listing or security
change occurred. No uncertain upload/signing operation was replayed.

Physical installation, complete Creation/Career coverage and general startup
reliability remain open. This is not finished-app, full beta, tablet, Full
Editing, Windows or new live-book-generation authority.
