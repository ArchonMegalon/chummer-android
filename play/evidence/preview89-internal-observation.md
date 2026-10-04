# Preview 89 — startup feedback and restored runner reuse

Authenticated Chummer Play Console readback on 4 October 2026 at approximately
02:07 UTC showed `89 (0.1.0-preview.89)` **Available to internal testers**,
Internal release 84, one version code, released at 04:07 Europe/Vienna.
Readback was confirmed again at 02:10 UTC. This is browser readback, not
Publisher API evidence.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
The later [physical Play88→89 update](preview89-physical-observation.md)
verified installed identity, the Play signature, loading feedback and unchanged
visible Creation after a verified new-process restart. Its scope is bounded;
it does not establish physical Origin reading/export or all-method Creation.
Version 89 is consumed; never rebuild or re-upload it. Prior evidence is retained.

## Change and exact artifact

Home now displays localized loading feedback before awaiting saved-runner
restoration, without exposing actions before owner/lifecycle admission.
Restoration reuses the exact completed Shell roster state instead of reading
it twice. Failure, incomplete state, owner transition and stale-state cases
retain the full-sync fallback and final selection admission checks.
Product PR418/419 and version-only PR420 merged normally with required checks.
No gameplay rules or dependency change.

- Android producer: `64983f4914bf695dc291db58098109c890d9d8d2`;
  main merge: `df8dd8fd4bad6f270d444e007e2d822b2b954396`;
  identical tree: `88092e89cba654fb154407f159cd2169e8aa9be6`.
- Presentation seal: `6cae599a89301a290d13e0d7b0c46fc6f29c302c`.
- Core runtime: `9fb784271f3f0e0cd926565b148ff31bd7ca6565`;
  recipe: `34b631541d3f0b7be5a5c99385164a26d535aed1`.
- UI verification: `9de71432b1e7d16c189054ed38e9e67a783697bd50862e4b84d6330386036183`.
- Unsigned AAB, 33,628,827 bytes:
  `87f2c634db32372c8f43e018f77e6a814df4d77aa497782542e6babbea2bea79`.
- Signed AAB, 33,801,440 bytes:
  `dfe928aa27b96700604a74230d93004ac68d131fde8acadf1f6db58c604fd3c0`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `f85147bcffc7d492e47fcd5a55b8d5d7eaa59fc260336ea2154111f40848241c`.
- Independent signed verification:
  `f9dee841061516f116cbc7f5fad7114795f474878e4904eeaf3186408aacb9db`.
- Private Console execution record:
  `94ed56496b6368f7844110c8124196c194928f9b3354e29aa4480c7d06cd9db8`.
- Availability screenshot:
  `73abb4de3c2558fc736336f990082d0dabc3329dfab55c68741a535f077bcfc6`.

The offline local keyless ARM64 Release build used .NET 10.0.112, JDK 17.0.20.1
and image `sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports, package authority, 331 embedded content files,
manifest/API24+/target36/ARM64, proof exclusion and credential hygiene passed.
This is locked package closure with explicit pinned Presentation source and
Core content, not package-only or hosted runtime qualification. Separate signing
passed independent keyless strict JAR, certificate and all 1,783 payload checks.

## Focused verification and limits

Actual-MAUI loading/departure/return tests pass. Cold restoration demonstrated
two roster reads before the fix, one after for both Creation and Career,
unchanged stored bytes and no repeated initialization. Owner ABA, stale retained
selection, incomplete state and persistent failure cases retain fail-closed
behavior. Full passing results are distinguished from earlier incomplete runs.

The isolated API36 x64 Release diagnostic APK, SDK test-key signed, is
`39202b85542aa131354872a05a7fb1bea3f68337b24b4a549406954be0687943`.
At font scale 1.3 a synthetic Life Modules Create view restored, then reopened
after verified process death in a new process. The settled hierarchy and runner/
reading JSON remained byte-identical. These unchanged runtime-input results are
reused; version 89 received its own ARM64 build and artifact checks.

Startup remains slow. The emulator had substantial host/guest memory pressure
and Android service ANRs; no general speedup, ANR-free device or performance
claim follows from these tests. Missing mapping/native-symbol Play warnings
remain. One exact upload and confirmed Internal rollout occurred under standing
approval, with no Production, audience, listing, billing or security changes.
No full SR5/Origin, all-method, seven-journey, tablet or general-beta completion
claim. The full original objective remains active.

Private packets `creation-release89-20261004.mpnewcLx`,
`home-startup-feedback-20261004.6nRj79Ps` and
`startup-shell-reuse-20261004.gSQVhzlv` retain artifacts and observations outside
served directories. No private runner/device data or credentials are published.
