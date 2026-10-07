# Magic catalog package intake — 7 October 2026

This change consumes Core's scoped XML writer-buffer reuse for Creation Magic
catalogs. Rules, option identities and catalog digests remain unchanged. Buffers
belong to one synchronous projection; fresh source and owner checks are retained.

## Exact local inputs

- Core runtime: `abee6939e92e1de29403e0665bfab42664ee18af`.
- Core package recipe: `e2b93e59ed68689b621a83be8a8b4cf29a00fb09`,
  normally merged through Core PR127 with an identical tree.
- Core ZIP SHA-256: `031a2214ef4bdd9737798ecb9fe9f2d9316e463405f09290d69632e8aa0a7fab`.
- Presentation seal: `ef4828df8933252a8eaf17d70ae15373ff10549d`.
- Presentation lock SHA-256: `5be9df92d2f416eb28b1f1ddab37fee054483e9eddd9ce12640a13a7a579902f`.
- Local UI consumer receipt SHA-256: `d1a5ce4e7f5e017db5c4b8d4c7d090adca1a963d1b5f5d7a2bc5330618e282f7`.
- Owner-cache manifest SHA-256: `baa0efbb608eb8732259a1c8a4d77c09437f809405d5625c8e440a0e1e23ed54`.

Actual local production emitted both UI locks. The exact local consumer passed
five builds, 869 product tests and all selected owner/continuation regressions.
The regular main-branch update for UI PR347 has the identical source tree to the
tested seal; the receipt continues to name the original exact consumer commit.
Android independently verifies all 18 packages and their authority files.
All 331 rule-data files are unchanged; the content manifest records the new
recipe provenance. APK assembly still uses pinned Presentation source and an
explicit Core content export; it is not a source-free APK or hosted proof.

## Affected checks

The Core regression failed before the fix. Managed allocation for 128 catalog
rows decreased from 5,622,856 to 1,980,040 bytes; all 585 rows across five actual
catalogs decreased from 27,740,352 to 10,607,976 bytes with identical digests.
Focused XML boundary, ownership, parallel-isolation and reopen checks passed,
followed by 775 tests in local package production. This is not native latency
or peak-memory evidence.

The local Android intake compiled the native and MAUI targets and interaction
tests with zero warnings/errors. Dashboard entry, short spell help in three
locales, actual Mystic Adept and Adept profiles, power/spell purchase, Skills to
Magic revisit, single-save/cold-reopen, owner ABA, cancellation, recovery and
uncertain-reply no-replay tests passed. These are managed native-source tests.
The restore log retains a transient NuGet connection reset; restore completed.

An earlier separate SDK-test API36 x64 APK with this Core bundle and the prior
Presentation graph saved Aeronautics Mechanic 2-to-3 once and reopened the same
Sum-to-Ten workspace after a verified new process. Revision 8/8, four receipts,
40 remaining active points and exact saved bytes were retained. The final
sealed-graph upgrade/read/reopen check remains pending; no mutation is replayed
merely to refresh evidence.

## Delivery boundary

No new AAB, signing, Play upload or physical installation is asserted here.
Preview127 remains the last confirmed Internal artifact. Earlier emulator
SystemUI ANR and null accessibility observations remain recorded. Restored
workspace readiness still took about 18 seconds; native responsiveness and
complete SR5 Creation remain open. Existing releases and evidence are immutable.
