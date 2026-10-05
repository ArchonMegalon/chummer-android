# Preview 106 — settle canceled initial account checks

Authenticated Chummer Play Console readback on 5 October 2026 at 00:33:27 UTC
showed `106 (0.1.0-preview.106)` **Available to internal testers**, Internal
release 100, one version code, released at 02:33 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is scoped browser readback, not Publisher API or physical-device evidence.
**Physical Play 106 installation/update remains unverified.** Version 106 is
consumed; never rebuild/re-upload it. Preview 105/104 artifacts and evidence are
retained. No Production, tester-audience, listing, billing or account changes
occurred.

## Change and focused verification

An admitted initial account check now settles its Loading snapshot on caller
cancellation and rethrows cancellation, allowing an explicit retry. Canceling a
queued initializer cannot settle the active caller. Existing linked/pending
snapshots, stored grants, owner identity and admitted credential writes remain
protected. Cancellation messages are localized in English, German and Spanish;
three previously missing Spanish loading messages are also restored.

The original regression was reproduced before the fix. The focused account HTTP/
security suite, two localization contracts and five actual-MAUI/Core cases passed.
Managed cases cover initial cancellation, explicit retry, exact owner and fresh
account-service metadata restoration. The managed test explicitly refreshes the
account-page controls: it is not native dispatcher or OS-restart evidence. Affected
managed and private Release x64 builds reported zero warnings/errors.

The synthetic API36 x64/font1.3 smoke exercised existing-linked account loading
and cancellation. The status read canceled after 6058ms with no catalog request;
controls returned to idle and linked state remained. Force-stop, verified process
absence and a new process reopened linked account state with identical stored
preferences. SDK-test APK SHA-256:
`0241e15ab7398d7a7206d7f0cc174d22cc3f88b6342eaecd7348fc142e9ce457`.
Initial-loading fault injection is managed coverage only. A boot System UI ANR
remains recorded, not performance clearance. The diagnostic endpoint/browser
fixture and MauiProgram override were not admitted into the Release export.

The version-only candidate reuses these unchanged behavior/dependency checks;
it is not a fresh hosted runtime qualification. This narrow cancellation fix does
not prove the cause of the historical live-account hang or interrupt a
noncooperative OS secure-storage read. Live Hub/Teable account success remains
unverified. No provider jobs, credits or real user-runner data were used.

## Exact local artifact

- Android producer: `e84984563fec2d8593e24b8a17815bd00da811c6`;
  main merge: `f150c2f44935d6c82e2ca2609349fc8abc81a9c3`;
  identical tree: `07b2b89da6a2742a6594c7f0f743e484144e5a14`.
- Behavior PR471 and version-only PR472 merged normally. Required source and
  GitGuardian checks passed; version run37247167609/job111567215124.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,686,382 bytes:
  `a0c6ae9ec8be4d5c87299f80feb55b3e21861eaa58d9e9a919156382e8888a58`.
- Signed AAB, 33,859,043 bytes:
  `861adb645361c08de8c5b0bdbd918d6072cdd2931684547daad8d9aa981e087c`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `05eeab39e55c115ed569d9a4ff070d29de1edd5eca190ecaf9983b7253b82754`.
- Independent signed-verification receipt:
  `18f368cba34243d8d41a0588051f79b90a5901c87eb723e1f79540cb0aafbb6b`.
- Private Console execution record:
  `a9c274a9ae28e6a8f69b96565e7c80efff4eb76a9c1e01a1e7e9322bef77ec47`.
- Availability screenshot:
  `5c603e7b6c72fcab0862d6689c939dd9eb63f4530c0853ace6ee29382a8d94fc`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact clean source exports, 18 packages, 331 embedded content
files, package/version/API24+/target36/ARM64, privacy flags, proof exclusion and
credential hygiene passed. Dependency mode is locked package closure with pinned
Presentation source and Core content; warm cache reuse is explicit, not package-
only, a cold rebuild or hosted qualification. Separate original-key signing
passed independent keyless strict JAR/certificate/all1,783 non-signature payload
comparisons. No compiler warnings/errors were observed in the candidate log.

One upload and one confirmed Internal rollout occurred under standing approval.
The already completed upload was resumed by observation, never replayed.
Device catalogue is unchanged; missing mapping/native-symbol warnings remain.
The upload certificate is not evidence of the Play App Signing certificate.
Physical installation, successful live-account/provider behavior, full SR5/Origin,
tablets, exhaustive native parity and private Rook remain incomplete. No general-
beta or whole-goal completion is claimed. Private packets
`origin-release106-20261005.7YefnHvX` and
`account-initialization-cancel-20261005.yfycPHOS` retain exact artifacts and bounded
evidence outside served directories. No credentials/private runner data are here.
