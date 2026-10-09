# Preview 156 — exact-byte saved-runner read validation

Authenticated Chummer Play Console readback in the `2026-10-09T08:21Z` minute
showed `156 (0.1.0-preview.156)` **Available to internal testers**, one version
code, Internal release 150, released 9 October at 10:21 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API evidence.
**Physical Play 156 installation/update remains unverified.** Play says changes
usually appear within one hour and can take longer. Version 156 is consumed:
do not rebuild or re-upload it. Previous artifacts and evidence are unchanged.

## Change and affected verification

Ordinary Core workspace reads reuse a successful historical shape check only
for the same owner, workspace ID and full-record SHA-256 computed from the same
stream used for decoding. The process-local cache retains at most 128 keys,
not mutable character objects. Each read still constructs fresh state, obtains
its lease and validates revisions, ownership and history. Writes, strict export,
adoption and current-rule/source admission retain their checks. A new process
starts with an empty cache. Rules and persisted formats are unchanged.

Exact packages, managed checks and unchanged-input reuse are in the
[package-intake record](../../docs/evidence/runner-read-validation-package-intake-20261009.md).
Core, UI and Android merged normally after their required checks; no protection
was changed. Host-only timing improvements are not native startup evidence.

The final-source API 36 x64 Release diagnostic APK built with zero warnings and
errors, without proof instrumentation. Its temporary predecessor signing key
was absent; a different retained SDK key was used for a **new diagnostic package**,
not substituted into the existing package. The prior app and its data were kept.
The new diagnostic app used 33 copied synthetic workspace files. This is a
save/reopen test, **not an in-place app upgrade or Play App Signing test**.

The normal-points control opened Attributes and its heading scrolled to normal
allocation. One Body increment changed 3 to 4, with 21 normal points remaining.
No durable change occurred before explicit save. Confirmation saved revision
13/13 from 12/12; only the selected workspace revisions and Attributes draft
changed. XML, special points, Karma, the older Magic draft and the other 32
files stayed unchanged. That older Magic draft still requires re-review.

Force-stop was verified to terminate the old process. A different process
reopened the authoritative Creation overview at 13/13; all 33 saved/reopened
files matched. All 33 files in the prior diagnostic app also remained unchanged.
An initial emulator System UI Wait dialog and a host memory-cap increase from
6 to 8 GiB are disclosed; no OOM was observed. Startup remained slow. Reopened
shell/selected-restore observations were about 14.6/14.8 seconds, not a controlled
before/after comparison. The owned emulator was stopped after the smoke.

Full chapters and per-chapter images retain their existing app-reader/EPUB
[coverage](../../docs/ORIGIN_EPUB_AND_SCENES.md), not a fresh provider generation
or multi-age likeness test. No complete Creation/Career or native speedup claim
is made.

## Exact local artifact

- Android producer: `b4716a9d1d1a7741c1141b7ae2d7cb683656af06`;
  protected PR 597 merge: `943ee9144c8c5e58fd34a7c4ccaffba3c8a06782`;
  identical tree: `4248583937600e4d7bfdec7c9c20c93bf3c549c1`.
- Actual Presentation consumer: `b5653f408d8f4794ad278c334ea965379972c798`;
  published seal: `2a6e88e3276e2158edadf1e1b608e362c3a7f0d2`;
  protected PR 385 merge: `56704c3334c0a3b43efb32a00fe08cb2ee0f74f4`;
  identical tree: `1c6fd28b794e29d443317d2317d2578686f196b9`.
- Core runtime: `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`;
  recipe/content: `b9daf8e559edb8110dfc3ce5a003ae08c540a696`;
  protected PR 139 merge: `2522a896f5c4fb35f60fc7f5eb19000f475374eb`.
- UI verification: `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.
- SDK-test APK: `19322d0b3a0735f47607fe81cb0e7cec7194326943db46ea06129d3d8d226a20`.
- Native smoke receipt: `e1db75095fd003d1717addb8cfd0ba3ac36f7e2eb9aa79a18bfac525d19ca522`.
- Unsigned AAB, 34,964,195 bytes:
  `4b6f430a9a985b7194d9c79731252cef6f9c7bb014def1f72e7eccccfb8b92ae`.
- Signed AAB, 35,136,827 bytes:
  `42bdedbddebe5b4761b5fcac1eb1ae76d5084782b50f824fb0af0d6fdbdb4dc7`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `9a3994a2e43a0a8516fd9d81a57677b28c6806e3539a094a60e6c20b8bec55ba`.
- Independent keyless verification receipt:
  `816a3150294c2441d5b0e02487a2adcb75fb8ebd3eed48c8e76bd6a3fd8004b4`.
- Private Console readback record:
  `b42fbf9ba25ceb070e1b47eb4b1cc557fe58d64debca018a37c5c2b86511a406`.
- Visually inspected availability screenshot:
  `ea134835db182e5a483ae1f6f84e83bfe44e3453ba00c9fbb19b5cc0f1eb251e`.

One offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, all 331 content files, bundle/version/
API/ARM64 inspection, proof exclusion and private-key hygiene passed. Dependency
mode is locked packages with pinned Presentation source and Core content, not
source-free package-only assembly. The separate offline signer used the existing
upload key. Independent keyless strict JAR/certificate verification and all
1,783 unchanged unsigned payload entries passed. Raw packets and keys remain
private; pre-upload stage receipts retain their original status.

One upload was observed processing at 08:19:54 UTC, and final confirmation at
08:21:13 UTC, under standing owner Internal approval of 2026-10-05. Candidate
verification is separate from approval. Browser/OODA checks verified live
account/app/track identity and availability. Play recognized version 156,
API 24+, target 36 and ARM64, with no blocking validation errors or supported-
device delta. Missing mapping/native-symbol warnings remain. No uncertain action
was replayed. No audience, public-track, listing, account or security change was
made. The owned browser session was closed.

Physical installation, complete Creation/Career and startup reliability remain
open. This is not finished-app, seven-journey, full beta, tablet, Full Editing,
Windows or new live-book-generation authority.
