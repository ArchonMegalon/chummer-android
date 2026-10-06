# Preview 117 — saved Origin history allocation reduction

Authenticated Chummer Play Console readback on 6 October 2026 at 18:51:16 UTC
showed `117 (0.1.0-preview.117)` **Available to internal testers**, Internal
release 111, one version code, released at 20:51 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API or physical-device evidence.
**Physical Play117 installation/update remains unverified.** Version117 is
consumed; do not rebuild/re-upload it. Preview116 and its evidence are retained.
Production, tester audience, listing, billing and account settings were unchanged.

## Change and affected verification

Core avoids duplicate historical Origin-turn projection during workspace reads:
it reuses the freshly validated previous-turn digest within that call and a
cleared canonical-writer buffer, not an across-call cache. Owner, revision,
source and receipt bindings remain enforced. The four-chapter regression reduces
history enumeration from four to two and still rejects historical tampering.
For the same synthetic six-entry/400-choice fixture, acceptance allocated
25,346,136 -> 12,461,816 bytes; warm workspace-store Get allocated
29,357,168 -> 17,223,848 bytes. These are local managed cumulative allocations,
**not** native elapsed time, peak RAM or general responsiveness claims.
Cold startup and rule checking remain slow.

Focused API36 Release x64 smoke used a separate SDK-test application ID and an
existing synthetic saved Life Modules draft. Completion reopened at revision7/
saved7 with book, Qualities, Talent, Attributes, Skills and Resources enabled.
Force-stop and process absence were verified, then a new process restored the
same draft and completion readiness. Workspace, input and synthetic-reading
bytes were unchanged; only review-policy state changed. No new mutation,
Career finalization or chapter request was submitted. Initial System UI boot
ANR and a transient null accessibility hierarchy were retained; later visible
readiness does not turn the failed observation into a pass or establish a clean
startup benchmark. The owned emulator stopped with data preserved. No physical
phone or existing real book was changed.

Core passed 736 managed regressions, 18 ownership cases, scoped-store probes,
15 inventory cases, 11 focused Origin/reopen/negative cases and 46 canonical/
decision cases. UI passed 869 product tests and focused subsets; five product
builds had zero warnings/errors, while the test project retained 62 existing
warnings. Android's native/MAUI/interaction compiles, focused managed Life
Modules/reader/EPUB/ownership checks, 219 affected Python cases, two identity
cases and actual 18-package/331-content-file bindings passed. Native Release
x64 built in 2m28.04s with zero warnings/errors; SDK-test APK SHA-256:
`708f1ace401619308a73b645b072c96489df39403be3dfaaab99bfcfb6e28ed4`.
That signature is not the production upload key or Play signature. No new paid
chapter/illustration request was made. Earlier full illustrated-book/Career
evidence remains separately scoped in the [Preview115 record](preview115-internal-observation.md).
This narrow smoke is not fresh full Career or seven-journey authority.

## Exact local artifact

- Android producer: `94e62b981ad7deccaf7fb7751f0c5c1b640802d3`;
  PR509 merge: `a19e534287a0c12a468ec6102e58a33b43090fe1`;
  identical tree: `4763cc0656d17288db8949f7da2cd626da903c82`.
- Presentation consumer: `28a35fc450cc5d6b057cecdc79845e736b26b1b0`;
  PR327 merge: `a2b38bb8babc5ca419d44fc341a78f4eea75a124`;
  identical tree: `f9bb12c7320021ca022aef2111994cb6ff678def`.
- Core runtime: `1da79c7d24bcdc8be77672d19835fa1cd65fd5cb`;
  recipe: `fd6d926bfe661beb67b0ec7f5660680cfd4d0f23`;
  PR120 merge: `6dfb7ea771240cbdc8be57001bbe265ac9dc2725`.
- UI consumer receipt SHA-256:
  `498082f009eeda5442fe0a1945dbcf23f8997e4f3689dbdd148e899b3c4296dc`.
- Unsigned AAB, 33,804,734 bytes:
  `ae2073e1625541f67b6fff384b498c90c63e3801663f965e2f1c670fb3e17a04`.
- Signed AAB, 33,977,341 bytes:
  `abffd32930b5518f6ac23024aee49c58754110ebac86633e10554fd88a83fc14`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt SHA-256:
  `8672543afbffe14c1047e9eb55096b442f3034f71b6ed9808023432c715193cb`.
- Independent signed-verification receipt SHA-256:
  `f93f4319f456f5c5b5009da9b496a1944658d8b0d7a96f5810487abe9d2d8910`.
- Private Console execution record SHA-256:
  `c66314b2e1814a2fe7df0ad551d943a1128cf6bc44ee24ea718d5e811a63aa53`.
- Availability screenshot SHA-256:
  `e1cf83b2b1f219b6067fb279c21ca3afa5ee0f35e075c418a9404fc9f5bed094`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports, all 331 embedded content files, bundle identity,
API24+/target36/ARM64, privacy flags, proof exclusion and credential hygiene
passed. Dependency mode is locked package closure with explicit pinned
Presentation source and Core content, no ambient siblings: **not package-only,
hosted runtime qualification or two-green**. Separate existing-key signing
passed independent keyless strict JAR/certificate checks and byte comparison of
all 1,783 non-signature payload entries. Release containers exited.

One exact upload and one final Internal rollout confirmation used the standing
2026-10-05 approval, separately from candidate verification. Required protected
merge checks were preserved. Missing deobfuscation mapping and native symbols
remain non-blocking warnings. Catalogue support does not establish tablet
authority; the upload certificate does not establish Play App Signing identity.
Private packets `origin-release117-20261006.eugJEyFx`,
`origin-local-read-perf-20261006.KtmdCrOJ`, `startup-profile-20261006.PpLciaKL`
and `origin-third-reconcile-20261006.AtFI8LFg` retain evidence outside served
directories. No credentials, device identifiers or private character facts are
included here. Physical Play117 and all-method SR5 Creation remain open.
