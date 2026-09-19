# Preview 18 — observed Play Internal availability

At `2026-09-19T20:58:28Z`, the owner-authenticated Chummer-only Play Console
showed `18 (0.1.0-preview.18)` as **Available to internal testers** on the active
Internal track. The UI displayed `19 Sept 22:58`; this is not asserted as a UTC
publication timestamp. [Install/update](https://play.google.com/apps/internaltest/4700678198570024687).

## Exact local artifact

- Android source: `99d2cb823e1c3cc1137cd14bffc9447694ae2243`.
- Android tree: `fad19923f7f339293b06ba0ad48a4f35c4da37a7`.
- Presentation source: `0a474a1f53614cc182fceadb2461335b0b9647db`.
- Core runtime source: `e66adccf06fb8bee96264b9a9112f970508e6e97`.
- Signed AAB SHA-256: `962412dd503ef028afbd0e395b42a1a326fd9c4c8e299a58ca991abc93185bb6`.
- Signed size: `31,053,793` bytes.
- Unsigned AAB SHA-256: `908ea3080e52395ac8029f636f8f6046a654d61fa062c037fa9c5bc41c062403`.
- Existing upload certificate SHA-256: `D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15`.

The networkless/keyless local Docker build completed in 2m14.68s with zero
warnings/errors. Independent unsigned inspection verified version, API bounds,
ARM64, proof exclusion and all 330 canonical content files. A separate offline
signer used the existing upload key. Independent keyless verification proved
the exact certificate, strict JAR signature and unchanged payload.

Play accepted this artifact once into Internal release 14, parsed API24+,
target36 and arm64-v8a, and reported no lost supported devices. The two review
warnings concern missing R8 mapping and native debug symbols. Both publication
controls were confirmed. Production, tester lists, billing and security were
not changed. Previous publication records remain unchanged; code18 is consumed.

## Scope and limitations

This fixes explicit New runner confirmation, Qualities/Gear admission and saved
Gear receipt validation, and reduces repeated catalog work on the UI thread.
The [local Creation smoke](../../docs/LOCAL_CREATION_SMOKE_20260919.md) completed
a minimal Priority runner to Career, then reopened revision 9/9 and its identical
finalization receipt in a new process. The Release delta is version/inventory
metadata; Presentation production sources are byte-identical to the smoked
Presentation commit. Nine packaged method-boundary regressions also passed.

This is a local source assembly, not a package-only, shared-reseal or hosted
qualification claim. Source integration remains separate from delivery; a later
main merge must not be relabelled as this AAB's producer. Physical Play install
or update has not been verified. Some first loads remain slow. Karma's next
Creation step remains unavailable; other methods and all Career actions are not
fully verified. No Full Editing, tablet, Rook or general public-ready claim.

The retained local `android-preview18-local-20260919.q5cF3KZz` packet contains
source inputs, build and verification logs, signing result and provider readback.
