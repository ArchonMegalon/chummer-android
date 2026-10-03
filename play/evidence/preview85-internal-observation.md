# Preview 85 — Qualities catalog digest overhead

Authenticated Chummer Play Console readback on 3 October 2026 at approximately
20:39 UTC showed `85 (0.1.0-preview.85)` **Available to internal testers**,
Internal release 80, one version code, released at 22:39 Europe/Vienna.
This is browser readback, not Publisher API evidence.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
Physical Preview 85 installation and affected-route restart are not yet verified.
Version 85 is consumed: never rebuild or re-upload it. Preview 84 and its
physical evidence remain unchanged.

## Change and exact artifact

Core now streams the canonical Qualities authority/state JSON directly into its
digest. Exact historical bytes, fields, ordering, null handling and freshness
checks remain unchanged. No validity cache, relaxed owner admission or replay
change is introduced. Core PR114, UI PR304, Android intake PR404 and version-only
PR405 merged normally with their required checks; no protection was bypassed.

- Android producer: `4d410603d8215863937d85a26d936615c23e19c4`;
  main merge: `f6fcc2f84656c99e136f33b3259a1142004db9e3`;
  identical tree: `3f3a59d02b626446cf2629736465be442c9af250`.
- Presentation seal: `6cae599a89301a290d13e0d7b0c46fc6f29c302c`.
- Core runtime: `9fb784271f3f0e0cd926565b148ff31bd7ca6565`;
  recipe: `34b631541d3f0b7be5a5c99385164a26d535aed1`.
- UI receipt: `9de71432b1e7d16c189054ed38e9e67a783697bd50862e4b84d6330386036183`.
- Unsigned AAB, 33,622,112 bytes:
  `24c928e064c507ab210578f1c8aaa004fe5f94025c198cc3baa78058fd502abf`.
- Signed AAB, 33,794,791 bytes:
  `07d6200bc0cf91c0e6d49e731d4abd569ff0aef6e31d2bcb5303cf0ba9bb4836`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `0c3f963ee0dce625663b44e1992bf543b58e1758453a1197d4c9c8fd3259c9d6`.
- Independent signed verification:
  `c46fba212889372e1baa667d43ae7f276ea9e93b10906876a16fa8a9ecb52fb1`.
- Private Console execution record:
  `a96ab2e1f3e77fe63f8ef57c6a4031e99ccb7904dd1f5ca3c5f3914917244c79`.
- Availability screenshot:
  `12c2ec9338a734f3192d3b3b5b1fa755bf0168b1be2d3d9ff5b0f2378134f60d`.

The offline local keyless ARM64 Release build used .NET10.0.112, JDK17.0.20.1
and image `sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Exact source exports, locked packages, 331 embedded content files, manifest,
API24+/target36/ARM64, proof exclusion and credential hygiene passed. This is
locked package closure with explicit pinned Presentation source and Core content,
not package-only or hosted runtime qualification. One isolated existing-key
signing operation was independently verified keylessly: strict JAR signature,
expected certificate and 1,783 unchanged non-signature payload entries.

Two initial build attempts failed closed on a stale candidate-local copied
Campaign package cache and consequent untracked restore locks. Only those
candidate-local files were quarantined recoverably. Admitted package hashes,
tracked locks, sources and guards were not weakened; the corrected third build
completed. No prior release artifact or shared feed was changed.

## Focused tests and limits

139 focused Core tests preserve historical digest equivalence, nested mutation,
ordering and allocation behavior, plus owner-bound save/reopen/finalization.
729 package-producer tests, five UI consumer builds, 862 UI tests and focused
checks passed. Android's affected managed checks, 135 lightweight Python tests
and the native compile passed. Unchanged inputs were reused honestly.

The isolated SDK-test-signed x64 API36 APK has SHA256
`89c34a45410458538b25e4aab283c3c49fef40f4e7acce3fba910404df063460`.
The synthetic Priority route selected one available quality, displayed its short
original explanation and cost, then saved once: revisions4/4 to5/5, one receipt,
positive cost4/25 and remaining21. Raw character and unrelated drafts were
unchanged. Verified process death, a different new process and native saved-receipt
reopen retained byte-identical persisted state, SHA256
`c16c8e4c790d078a4eb8cea3ff4fa8c4b163d854f03ab5f4ea451be97f219576`.
No user character was mutated. Version85 changes only release identity from
the native-tested runtime tree.

The same-process diagnostic lowered warmed digest hashing from21–23ms to6.2ms
and allocation from2.86MB to30KB; this is not a whole-phone speedup measurement.
Slow native transitions, initial null accessibility roots and a System UI startup
wait remain documented. No all-method, seven-journey, full SR5/Origin, tablet,
ANR-free, copyright-clearance or general-beta claim. The full goal remains open.

One exact upload and Internal publication occurred under standing approval.
Existing audience, Production, listing, billing and security were unchanged.
Play's missing mapping/native-symbol warnings remain. Private packets
`creation-release85-20261003.bNe8dOll` and
`quality-catalog-digest-20261003.BF51DVQf` retain artifacts and observations outside
served directories. No private runner/device data or credentials are published.
