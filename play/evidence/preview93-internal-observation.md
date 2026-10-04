# Preview 93 — responsive Origin reading acknowledgement

Authenticated Chummer Play Console readback at `2026-10-04T10:49:31Z` showed
`93 (0.1.0-preview.93)` **Available to internal testers**, Internal release 87,
one version code, released at 12:49 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API or physical-device evidence.
**Physical Play installation/update of 93 is not yet verified.**
Version 93 is consumed; never rebuild or re-upload it. Preview 92 and its
evidence remain unchanged. No Production or tester-audience change occurred.

## Bounded change and verification

After explicitly confirming a finished chapter, Origin now persists the local
reading decision and releases its page action gate before awaiting the Hub
acknowledgement. Effects, available exports and Return remain usable while that
remote acknowledgement is pending. Leaving the page cancels its observation;
late responses cannot restore stale page state. The selected-edition outbox,
owner checks and successor no-replay guard remain. No automatic acceptance,
rules change or new provider generation was introduced by this fix.

The focused regression reproduced hidden effects/export before the change.
Actual-MAUI checks passed with delayed acknowledgement, Return and next choices,
departure cancellation, late-response isolation and unchanged character bytes.
Related signed-HTTP, ownership, response-bound, successor recovery, automatic
reader and full-prose/export checks passed. Managed and diagnostic native
builds reported zero warnings/errors. No provider credits were spent.

An isolated synthetic/offline API36 smoke traversed Continue, all 13,769 prose
characters, explicit read confirmation, effects, Return and next-module choices.
After verified force-stop and a new process, the same choices, selected prose,
workspace and reading-state hashes persisted; EPUB/HTML actions remained
available. A new native file export was not exercised. Delayed remote transport
is covered by the managed test, not this offline device run. The diagnostic APK
used an SDK test key and isolated app ID, not the release upload identity.
Only that isolated app was replaced after a test-signature mismatch; previous
synthetic state was backed up and verified. Emulator System UI startup ANR was
retained as a limitation: this is functional, not performance evidence.

These behavior results bind PR 432's unchanged code. Preview 93 adds only its
version name/code and has its own ARM64 Release build and artifact inspection.
Neither is a physical Play93 test or complete seven-journey qualification.
All-method SR5, complete live Origin and general-beta completion remain open.

## Exact local artifact

- Android producer: `46c4026d5ee01e775403b43d86558756c479adfb`;
  main merge: `a0d5d6c86d4fecaab796aa52bd4ffe7a1e52e4ed`;
  identical tree: `abca34d2df0e68d80d78af6fe207918195f03bbe`.
- Product PR 432 and version-only PR 433 merged normally with source/safety and
  GitGuardian checks passing. No protected check was bypassed.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`;
  tree: `557d0486b7fb4a1d7d10cb6f79c910bd8d0de895`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,666,075 bytes:
  `86ae47fa24f364ad02be1a9ffde93d00e93e155b22762eab0a688a03fb58631d`.
- Signed AAB, 33,838,668 bytes:
  `5c346731fc5173e4d69f8d8485d3b84b240ac883dfbe5319b1339235bec0ffef`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `ef500fe7199927f4cb5a3a9a0be45b4a61a15071b2099804a39a6001e7ff2fcf`.
- Independent signed-verification receipt:
  `326ded4e801fdc0ac90a325336374722fc3b5c4fbfc15e7e1a9ff550b922e557`.
- Private Console execution record:
  `c24cdc7216d2af9e736fb4e7ddc3432e21bd38722e633cce08d66e52e78b94c1`.
- Availability screenshot:
  `06b3800d6f45bc181f6d52100063a6fd43179083709fd457e562c328b8bf64d9`.

Execution was local offline Docker with .NET 10.0.112, JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
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
device. Historical uncertain FirstBook jobs remain fenced.

Private packets `origin-release93-20261004.aLnJeJXC` and
`origin-reading-flow-20261004.PIrxqdyu` retain build, signature and test evidence
outside served directories. No credentials or private character data are public.
