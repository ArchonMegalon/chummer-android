# Preview 167 — retry Home loading without leaving the page

Authenticated Chummer Console showed `167 (0.1.0-preview.167)` **Available to
internal testers**, one version code, release 161, on 9 October 2026 at 21:45
Europe/Vienna (19:45 UTC). Play displayed release time 21:44. There was one exact
AAB upload and one final confirmation under the standing 2026-10-05 Internal
approval. [Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console readback, not Publisher API evidence. **Physical Play
167 installation/update is unverified.** Play says changes usually appear within
one hour but can take longer. Version 167 is consumed; do not rebuild/re-upload it.
Previous evidence and rollback artifacts remain unchanged.

## Change and focused verification

A failed initial Home restore now leaves a localized, inline explanation and a
direct retry button instead of presenting ordinary runner actions without a
working same-page retry. Loading feedback is visible during the retry. Existing
appearance, action-overlap and owner guards remain in force. No runner state,
provider generation or mutation is replayed by this display change.

The controlled failure regression failed on the old runtime. The corrected
managed actual-page test passes in German, English and Spanish: same-page retry,
unchanged saved runner, exact owner, immediate progress, UI heartbeat and inert
duplicate/stale/departed taps. Existing first-frame checks and 18 action/refresh/
departure cases also passed. An earlier test-harness overlap error is retained as
a failed attempt, not counted as a product pass. Managed and final native x64
Release/Internal builds completed with zero warnings or errors.

An API 36 emulator upgrade smoke retained four synthetic workspaces, opened the
saved runner and settled Home controls, force-stopped the app, verified the old
process absent and reopened the same runner in a new process. All four workspace
hashes were unchanged. No app-data reset or user-account/provider work occurred.
The emulator was stopped after the check. **Injected startup failure/retry is
managed-page evidence, not a native fault-injection claim.** The native smoke
also encountered a separate System UI ANR; this is not evidence of an app crash
or a startup-speed improvement. No seven-journey, full Creation/Career or tablet
qualification is claimed. The original tester incident remains unattributed.

Internal diagnostics retain the default-on policy only when no preference
exists; saved opt-outs remain off. Disclosure, private intake and bounded alerts
are unchanged. Native remote diagnostic delivery remains unverified; prior
synthetic intake/alert checks are separate evidence. See the
[diagnostic contract](../../docs/INTERNAL_TESTER_DIAGNOSTICS.md).
Do not promote this Internal-channel artifact unchanged to a public track.

## Exact local artifact

- Android producer `d5f8476ed386859c56e0aefbb13503aa7a10caa9`;
  protected PR620 merge `ed53e0bae70b56749d20dac9db4b8c86518c539f`;
  identical tree `2e9117c36bc593630e84f096d612cb2ba050bf21`.
- Managed test tree `53b68b55ff8bb336d54f6985309d88f5f4304f39` differs only
  by the subsequent version bump; native build/smoke use the final 167 tree.
- Presentation `b5653f408d8f4794ad278c334ea965379972c798`,
  tree `1c6fd28b794e29d443317d2317d2578686f196b9`.
- Core recipe/content `b9daf8e559edb8110dfc3ce5a003ae08c540a696`,
  runtime `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`.
- UI verification `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.
- Local canonical `Chummer.Control.Contracts` package:
  `a0f59210e35abb9e88eda2b6b4f4ad497ea27577f77c294876120773e6aed7a2`.
  This is not a published protected package seal.
- Package authority binding:
  `f7a793c1ed76c62e683612481dc82e8203a04f959ac99d8d2d84c7193117c9b0`.
- SDK-test APK `41b7966c1fcb8701c841208c2bbb0bbdf152406b7a7f59fc578cbfca88daac4b`.
- Unsigned AAB, 35,860,201 bytes:
  `3e7145e0f8a59bd4966e3534a046c44ce08f72fd78d5cb1725e5e926cfc08916`.
- Signed AAB, 36,033,208 bytes:
  `14cc71cdcd0d7e9e64bc2c172c69ebaf4b7482fe4ad37050e3c5b2864d531139`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `4ed97c2943d13f1f773f18516bd5dde5ea2b992a0bdc5ee0c7e5548336410b60`.
- Independent keyless verification receipt:
  `8e4d045c1977dd2bac65fa7017029ab83808a3f794f82c7ca2048b2d7480334d`.
- Visually inspected availability screenshot:
  `20fdc45d690a3d1e42756971203c92892ca75d925fbef0b78a22efd42d7af8f8`.
- Private browser readback receipt:
  `2ac0335b29de507a67b0dfaa45674158be3ed851bac6d1d7b597871ff02e07c4`.

The offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and builder
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`,
with `ChummerDistributionChannel=internal`. Exact source exports, 18 packages,
331 canonical Core content files, bundle/version/API checks, key hygiene and
proof exclusion passed. Assembly uses locked packages plus explicit pinned
Presentation source and Core content, not package-only or ambient siblings.
Separate offline original-key signing and keyless strict JAR, certificate and
all 1,787 unchanged payload-entry checks passed. No hosted runtime is claimed.

Browser/OODA checked the owner, app and Internal track. Play recognized 167,
API 24+, target 36 and ARM64, with no blocking errors or supported-device delta.
Missing deobfuscation/native-symbol warnings remain. No public-track, tester,
account, provider-credit, Windows or unrelated-service changes were made. Private
build/signer/readback packets remain outside served directories.
