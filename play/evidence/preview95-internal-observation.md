# Preview 95 — recovered Origin chapter continuation

Authenticated Chummer Play Console readback on 4 October 2026 at approximately
12:16 UTC showed `95 (0.1.0-preview.95)` **Available to internal testers**,
Internal release 89, one version code, released at 14:16 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API or physical-device evidence.
**Physical Play installation/update of 95 is not yet verified.**
Version 95 is consumed; never rebuild or re-upload it. Preview 94 and its
evidence remain unchanged. No Production or tester-audience change occurred.

## Bounded change and verification

After explicit reading of a recovered full chapter, Origin now resumes the next
already-confirmed eligible chapter through the existing predecessor
acknowledgement/outbox flow and starts its bounded status watch. Previously this
reader path acknowledged the recovered text but could leave its successor idle.
No future module choice, automatic reading acceptance or uncertain paid-job
replay is introduced. Reading and export stay available during slow remote work.

The focused actual-MAUI regression reproduced missing continuation before the
fix. Corrected tests passed delayed acknowledgement, visible progress, a
concurrent EPUB export, exactly one acknowledgement/request, cold selected-book
state, unchanged runner state and no replay. Existing chapter HTTP/hostile-body,
owner A→B→A, cancellation, predecessor recovery, read-to-next and automatic
reader/export tests passed. An intermediate overlapping-async-void test-setup
failure was retained and corrected, not described as a product failure or pass.

An isolated synthetic/offline API36 smoke opened the complete 13,769-character
chapter before effects. Explicit reading enabled the next choices. Verified
force-stop/new-process reopening preserved those choices, the exact full prose
and EPUB/HTML controls; workspace and selected-reading hashes were unchanged.
The automatic remote successor is managed-test evidence, not a paid native
FirstBook run. The diagnostic APK used an isolated app ID and SDK test key.
An emulator System UI startup ANR was retained; this is not performance evidence.

These behavior results bind PR 438's unchanged code. Preview 95 adds only its
version name/code and has its own ARM64 Release build and artifact inspection.
Neither is physical Play95 or complete seven-journey qualification. Full live
Origin, all-method SR5 and general-beta completion remain open.

## Exact local artifact

- Android producer: `79654147c7eda21c51866e83f6da2774bd0bd05b`;
  main merge: `7bc475260d72f337bdeefe94455c751056fb7673`;
  identical tree: `69627c52635fabc6030fd271b3a2b9ef22fdb251`.
- Product PR 438 and version-only PR 439 merged normally with source/safety and
  GitGuardian checks passing. No protected check was bypassed.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`;
  tree: `557d0486b7fb4a1d7d10cb6f79c910bd8d0de895`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,667,097 bytes:
  `ff24e216e60a675b313f382dc99fd59e6c5d53c4444304343d6aa5266bd94f0c`.
- Signed AAB, 33,839,696 bytes:
  `5ca487cec4958b6b7cd3e6839d931a7891c2df9dd398d2725f07a91173369232`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `f39869dc46edcadd13537b72b269842ae40b7c1e3d7fdfcf80b02b3dc6852d4d`.
- Independent signed-verification receipt:
  `1e13782e9d7151691d69e2e74f2de8b630797132287f4face59da5be2f4d5c2c`.
- Private Console execution record:
  `955049f2c3fef62c8b95b5c077e1b4586aff1c2a2daeec2f5bc022075cea5241`.
- Availability screenshot:
  `32fd6caca6216b2cf700c1f1f197c5689da46e52ec6736bb2ffa6fff9fe7ff51`.

Execution was local offline Docker with .NET 10.0.112, JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
The build completed successfully without compiler warning/error diagnostics.
Locked restore, exact exports, 18 packages, 331 embedded content files,
package/version/API24+/target36/ARM64, proof exclusion and credential hygiene
passed. Dependency mode is locked package closure with pinned Presentation
source and Core content, not package-only or hosted qualification.
Separate original-upload-key signing passed independent keyless strict JAR,
certificate and equality of all 1,783 non-signature payload entries.

One upload and one confirmed Internal rollout occurred under standing approval.
Play reports unchanged device coverage and the existing two diagnostic warnings:
missing deobfuscation mapping and native debug symbols. These remain deferred;
no platform or signer control was weakened. Existing ADB reported no connected
device. No provider credits were spent; historical uncertain FirstBook jobs
remain fenced.

Private packets `origin-release95-20261004.XWZHXhGV` and
`origin-reading-flow-20261004.PIrxqdyu` retain build, signature and test evidence
outside served directories. No credentials or private character data are public.
