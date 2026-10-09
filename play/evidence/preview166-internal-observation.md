# Preview 166 — preserve reader refresh after a canceled export

Authenticated Chummer Console showed `166 (0.1.0-preview.166)` **Available to
internal testers**, one version code, release 160, on 9 October 2026 at 21:15
Europe/Vienna (19:15 UTC), after one upload and one final confirmation.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console readback, not Publisher API evidence. **Physical Play
166 installation/update is unverified.** Play says changes usually appear within
one hour but can take longer. Version 166 is consumed; do not rebuild/re-upload it.
Previous evidence and rollback artifacts remain unchanged.

## Change and focused verification

The Origin reader no longer loses a completed background status display update
when an export picker holds the page action guard. The pending refresh coalesces
and drains after the action finishes, including cancellation. Stale appearances
are still rejected. No provider generation or user mutation is replayed.

A deterministic managed regression failed on the baseline and passed on the fix:
hold the EPUB picker, finish the background read, cancel the picker, and observe
the updated display without leaving the page. Existing full-prose/export,
explicit recovery, bounded pending status, cold persistence and 18 action/refresh/
departure cases passed. The native Release x64 build passed with zero warnings
or errors. An API 36 emulator smoke read a saved full chapter, opened the real
DocumentsUI EPUB picker, canceled it, retained full prose, then force-stopped and
reopened the same runner/book in a verified new process. Workspace and reading
file hashes were unchanged. The owned emulator was stopped afterwards.

The native fixture was synthetic and unlinked. **Live-linked native polling and
remote diagnostic delivery are unverified**; the overlap/retry behavior above
was tested by the managed harness, not claimed as live provider evidence. Startup
remained slow, and a separate emulator System UI ANR was observed. **This fixes
one reproduced failure class; the original tester's exact reload incident is
not conclusively attributed.** No complete Creation/Career, seven-journey,
whole-app responsiveness, tablet or physical-installation claim is made.

Internal technical diagnostics retain the Preview 165 default-on behavior only
when no preference exists; a saved opt-out remains off. The existing disclosure,
private Hub intake and bounded operator alert worker remain unchanged. Prior
synthetic intake/alert tests are separate from real tester or native end-to-end
delivery. See the [diagnostic contract](../../docs/INTERNAL_TESTER_DIAGNOSTICS.md).
Do not promote this Internal-channel artifact unchanged to a public track.

The release preparation also corrected stale diagnostic consumer-lock hashes
and the derived package binding. The lock itself and all runtime bytes stayed
unchanged by that metadata correction. The baseline real-lock regression failed;
35 corrected contract tests and the exact package verifier passed. No package
version, guard or protected merge requirement was weakened.

## Exact local artifact

- Android producer `4d50b1b76229a945e00980edd58222782cb13cea`;
  protected PR618 merge `f52d904bc32f40fa3ded2708e8d77b9990eba891`;
  identical tree `5a4f52cde1b6009a3ef8b5915e54d7f5f4334e2a`.
- Native runtime smoke producer `0129409d907e666d4af8e8c3bacf58a6bc9ee10f`,
  merged by PR617; `src` is byte-identical through the release producer.
- Presentation `b5653f408d8f4794ad278c334ea965379972c798`,
  tree `1c6fd28b794e29d443317d2317d2578686f196b9`.
- Core recipe/content `b9daf8e559edb8110dfc3ce5a003ae08c540a696`,
  runtime `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`.
- UI verification `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.
- Additional local `Chummer.Control.Contracts` package, 38,598 bytes:
  `a0f59210e35abb9e88eda2b6b4f4ad497ea27577f77c294876120773e6aed7a2`.
  This is a locally built canonical package, not a published protected seal.
- Package authority binding:
  `f7a793c1ed76c62e683612481dc82e8203a04f959ac99d8d2d84c7193117c9b0`.
- SDK-test APK `d3b5568892819a818bf4cadeef13ade41e2d0ec37c5bd1fd90a75976c99187ee`.
- Unsigned AAB, 35,857,223 bytes:
  `15d2d72de11cc0ea5e30bbc242c84888940d75fc576d4c643431f589aead4b7a`.
- Signed AAB, 36,030,265 bytes:
  `abb7c088008fe1c3d723ab0bd5c67e55ad4d01990fc03997ff7cc68792bce5c1`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `7ea0ee57129b7f13bb8ba03680634c12f46f1e668c0ef7f94a3081367dbd4593`.
- Independent keyless verification receipt:
  `f592d169953ecfb418ce7d11f41c6a956b67b3f844439976023db2d74cc685c7`.
- Visually inspected availability screenshot:
  `f49d57e3d95e73c56e53e8b290597a4f30a611f4aef1b2a72576c9e97dcb40a2`.
- Private browser readback receipt:
  `a7a51476f5a16ad12c8e4d6a9dad49292b6dd024f95e2a4a45f35a12a68bd881`.

The offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and builder
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`,
with `ChummerDistributionChannel=internal`. Locked restore, exact source exports,
331 canonical Core content files, bundle/version/API checks, key hygiene and
proof exclusion passed. Assembly uses locked packages plus explicit pinned
Presentation source and Core content, not package-only or ambient siblings.
Separate offline original-key signing and independent keyless strict JAR,
certificate and all 1,787 unchanged payload-entry checks passed.

Standing owner approval of 2026-10-05 authorizes the Internal upload separately
from these checks. Browser/OODA verified owner, app and track. Play recognized
166, API 24+, target 36 and ARM64 with no blocking errors or supported-device
delta. Missing deobfuscation/native-symbol warnings remain. No public-track,
tester/audience, account, provider-credit, Windows or unrelated-service changes.
Private build/signer/readback packets remain outside served directories.
