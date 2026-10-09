# Preview 165 — Internal diagnostics and Creation guidance

The authenticated Chummer Console showed `165 (0.1.0-preview.165)` **Available
to internal testers**, one version code, release 159, after one upload and one
final confirmation on 9 October 2026 at 20:23 Europe/Vienna (18:23 UTC).
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console readback, not Publisher API evidence. **Physical Play
165 installation/update is unverified.** Play says distribution usually appears
within one hour but may take longer. Version 165 is consumed; do not rebuild or
re-upload it. Previous evidence and rollback artifacts are unchanged.

## Change and focused verification

Internal builds now default to automatic, bounded technical diagnostic reporting
when no prior preference exists. Saved opt-outs remain off. Home/settings explain
the default and offer an immediate opt-out. Public/development builds default off;
this Internal artifact must not be promoted unchanged to a public track.
The [diagnostic contract](../../docs/INTERNAL_TESTER_DIAGNOSTICS.md) describes
the metadata allowlist, exclusion of account/runner identifiers and story content,
durable bounded outbox, revocation and unknown-outcome handling. Slow operation
observations are not classified as ANRs; abrupt crash capture is not guaranteed.

Managed journal/outbox/HTTP negative checks and 18 action cases passed. The local
SDK-test native Release x64 build passed with zero warnings/errors. Its API 36
smoke verified an existing off preference stays off, a missing preference starts
on, opt-out cancels/clears pending work, and a new process preserves off. HTTPS
was intentionally blocked in that smoke: **native remote delivery is unverified**.

The first-party Hub intake and private reader are deployed; Hub PR303 is merged
as `033fb07adefcb102ec7fdb49e8be2c963eafd338`. Fifty-one focused and 168 adjacent
Hub tests passed. A separate synthetic HTTPS probe verified acceptance,
deduplication and private readback. The bounded operator worker passed eleven
focused tests and sent one clearly labelled synthetic integration notification
to the verified private Telegram operator chat. These separate probes are not a
real tester report or native end-to-end proof. Privacy documentation is deployed
at `https://chummer.run/privacy`; canonical Design PR33 is merged.

The increment also includes accumulated Life Modules interaction guidance,
Creation readability and inline quality-help changes. Affected managed checks
passed; a native synthetic Life Modules route displayed the Rich Kid choice and
two questions. Missing-answer focus and every attribute/quality route were not
fully verified on-device. An emulator System UI ANR and incomplete startup
observations remain distinct from app-crash evidence. **The reported reload
failure after which leaving/re-entering helped is not reproduced or fixed.**
No whole-app localization, complete Creation/Career, seven-journey, tablet or
physical installation claim is made. Book generation and images are unchanged.

## Exact local artifact

- Android producer `f2a9e8386b0fdde97e457dac808678fa7c1612b3`;
  protected PR615 merge `bc455ad33726618aebe525d55714bfffe7b77097`;
  identical tree `b978cc5c5d006f737d03f243f5776d7de5dfcf80`.
- Presentation `b5653f408d8f4794ad278c334ea965379972c798`,
  tree `1c6fd28b794e29d443317d2317d2578686f196b9`.
- Core recipe/content `b9daf8e559edb8110dfc3ce5a003ae08c540a696`,
  runtime `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`.
- UI verification `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.
- Additional local `Chummer.Control.Contracts` package, 38,598 bytes:
  `a0f59210e35abb9e88eda2b6b4f4ad497ea27577f77c294876120773e6aed7a2`.
  This canonical package is locally built, not a published protected package seal.
- SDK-test APK `38f0629b90a32853de161bcdea182ceafe440a54e712e846ddfa9f3c53e5e7eb`.
- Unsigned AAB, 35,857,291 bytes:
  `77a577a9bdc68c30864753a36d6cb4794d2083b4a9b30ed2febeaf93d0541da8`.
- Signed AAB, 36,030,381 bytes:
  `fcf59346e634ad9048f4a7f59d84b4abca6f7b92767ba33a6390ca116a4c8c7b`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `3467304b019e435d53157bbc2a1740c03ce4236aabe0713d9e087d72fe2b7b4d`.
- Independent keyless verification receipt:
  `a1e38e7b3960f62444e3c617881e825bce705816b3c3153b24a8e822f660696a`.
- Visually inspected availability screenshot:
  `1fa96018c470cd9c80acf587a8f7509739347f2c2da80c2aca470916eae7c076`.
- Private browser readback receipt:
  `d717d2aeb288f1648d92066245db04ff7d0eb39d2ad4a2d9ca77a5d2fe25c2a3`.

The offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and builder
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`,
with `ChummerDistributionChannel=internal`. Locked restore, exact exports,
331 Core content files, bundle/version/API checks, private-key hygiene and proof
exclusion passed. Assembly uses locked packages plus explicit pinned Presentation
source and Core content, not package-only or ambient siblings. Separate offline
original-key signing and independent keyless strict JAR, certificate and all
1,787 unchanged payload-entry checks passed.

Standing owner approval of 2026-10-05 authorizes this Internal upload separately
from these checks. Browser/OODA verified the owner, app and track. Play recognized
165, API 24+, target 36 and ARM64 without blocking errors or a supported-device
delta. Missing deobfuscation/native-symbol warnings remain. No public-track,
tester/audience, account, provider-credit, Windows or unrelated-service changes.
Private build, signer and readback packets remain outside served directories.
