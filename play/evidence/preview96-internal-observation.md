# Preview 96 — confirmed local runner deletion

Authenticated Chummer Play Console readback on 4 October 2026 at
13:12:26 UTC showed `96 (0.1.0-preview.96)` **Available to internal testers**,
Internal release 90, one version code, released at 15:11 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API or physical-device evidence.
**Physical Play installation/update of 96 is not yet verified.**
Version 96 is consumed; never rebuild or re-upload it. Preview 95 and its
evidence remain unchanged. No Production or tester-audience change occurred.

## Bounded change and verification

The runner list offers Delete runner for the current and other saved runners,
including unopened ones. A confirmation names the runner and explains the scope:
only the local copy is removed; online copies, books and exports remain.
All roster entries are displayed. This is not account deletion or secure erasure.

Deletion retains original-owner, transition, row identity and revision checks.
Unknown revisions are inspected before confirmation without activating the runner.
The existing owner-bound Core deletion path performs the mutation; committed
deletion is not replayed if a later roster refresh fails.

Ten coordinator cases and three actual-MAUI Home cases passed, including cancel,
inactive/linked/dirty runners, revision changes, owner changes and A→B→A,
queued cancellation and leaving the confirmation page. Managed and diagnostic
native builds completed with zero warnings/errors. Intermediate fixture,
compile and intake-ordering failures were corrected and retained, not called passes.

The final isolated synthetic API36 x64 smoke directly deleted an unopened
runner without activating it. Five other workspace files remained byte-identical.
Force-stop verified process absence, then a new process reopened the active
runner; deleted runners stayed absent. Earlier same-feature evidence covers
cancel and active-runner deletion with six other files unchanged. Only synthetic
test data was removed. A diagnostic app replacement preserved checksum-verified
test state because its SDK test certificate changed. An emulator System UI
startup ANR was retained; this is not performance clearance or physical coverage.

These results bind unchanged PR 441 runtime code. Preview 96 adds only its
version name/code and has its own ARM64 Release build and artifact inspection.
It is not complete seven-journey qualification, full SR5/Origin or beta readiness.

## Exact local artifact

- Android producer: `4ff576bed6c071269f05f9a9bccdee385bf2db9a`;
  main merge: `604a77983d551d21de679465e8f35b7809348217`;
  identical tree: `179d7517ecfb253c9547ec572e5276ce9189cde9`.
- Product PR 441 and version-only PR 442 merged normally with source/safety and
  GitGuardian checks passing. No protected check was bypassed.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`;
  tree: `557d0486b7fb4a1d7d10cb6f79c910bd8d0de895`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,676,516 bytes:
  `86ef560c0a591ac628ad45334ecb0b130d6eb159d411e61a3b160f4193460c98`.
- Signed AAB, 33,849,133 bytes:
  `126ea249de0076da83e7491eaed8f8c9d529ca8c43e01c2f4a6f8698192429fb`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `1b72a2d870c08c36bb7d5d3cf136ecced8de853a5ad0f1a1bc27f20ca3612296`.
- Independent signed-verification receipt:
  `ea55d4d02c81886a757aca3f87146414c8eda1efb727bea04fbb87523061c506`.
- Private Console execution record:
  `658cea7adc835dafb108791c30cf2c57f3fa4c0ff533467b9c08396ff2fdb41e`.
- Availability screenshot:
  `f00a0f1a39af6df44497ee45356c9de689560af466394264133b1baf8658b1a8`.

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
no platform or signer control was weakened. No physical-device action was taken
and no real runner was deleted in this release turn. No provider credits were
spent; historical uncertain FirstBook jobs remain fenced.

Private packets `origin-release96-20261004.QKXSmrJD` and
`runner-delete-20261004.8DTf5sCE` retain build, signature and test evidence outside
served directories. No credentials or private character data are public.
