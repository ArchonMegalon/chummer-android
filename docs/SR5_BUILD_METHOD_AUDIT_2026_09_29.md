# SR5 build-method audit — 29 September 2026

## Result and scope

Minimal native SR5 Priority, Sum-to-Ten and Karma routes reached Career and
survived process restart on the local API 36 x64 emulator. This is bounded local
Debug evidence, not all-options parity, hosted seven-journey qualification,
release signing, a Play upload or a physical-device test.

The current increment corrects the allocation and purchase blockers found while
walking those routes. It consumes protected, merged Core/UI changes; the Android
package authority records their exact source and package identities.

## Defects corrected

- Owner-scoped runners could become unavailable at Attributes, Skills, Qualities,
  Resources or Gear because those services read the legacy unscoped workspace.
  The services now use explicit original-owner reads and mutation leases. No
  runner is copied to the legacy namespace to make admission succeed.
- Concurrent dashboard reads competed for Android's credential gate, leaving a
  required step unavailable. Background read admission now serializes access
  while retaining cancellation and owner-transition rejection.
- Allocation/purchase previews and confirms now retain the displayed owner,
  workspace and snapshot. Cloned, stale and A→B→A previews fail closed; mutation
  leases do not span asynchronous work.
- A refresh error or cancellation after a durable purchase no longer discards
  the successful operation receipt and suggests an ambiguous retry.
- Linked Qualities checkpoints are owner-partitioned; legacy local checkpoint
  keys remain compatible.
- Gear search dismisses the keyboard on submission. Starting-cash entry uses
  explicit readable text/background colors, inspected at 150% font scaling.

Earlier audit fixes already on Android main address Karma purchased-level
captions and unavailable-source hints (PR #193), Priority/Sum-to-Ten rank
explanations (#194), and stale parent readiness after prerequisite save (#195).

## Observed native routes

| Method | Actual bounded route | Persistence result |
| --- | --- | --- |
| Priority | Human/Mundane, D/E/A/B/C, BOD 2, native language, empty Qualities, 1 Karma → 2,000 Nuyen, Flashlight purchase, starting-cash roll 3, finalize once | Career revision 8/8; cold restart and update to final Debug APK retained identical workspace bytes and finalization receipt |
| Sum-to-Ten | Human/Mundane, E/E/A/A/C, BOD 2, native language, empty Qualities, 1 Karma → 2,000 Nuyen, Flashlight purchase, starting-cash roll 3, finalize once | Career revision 8/8; cold restart retained identical workspace bytes and finalization receipt |
| Karma | Earlier minimal native creation/finalization route after the Karma display fixes | Career revision 3/3 and process restart/reopen passed on the earlier audit APK; not rerun end-to-end on the final APK |

Priority and Sum-to-Ten each purchased one Flashlight for 25 from 142,000 Nuyen,
retaining 141,975 before finalization. Street starting-cash roll 3 produced 60;
Career contained 5,060 after carryover. Intermediate allocation and purchase
routes also received focused save/reopen checks.

### Exact local artifacts and persistence identities

- Karma route APK SHA-256:
  `77bdf6311947d91ac69a3749cc7fa4cf6385b00937dc365518327f6bbca32ddb`.
- Sum-to-Ten route/restart APK SHA-256:
  `d13610579e83a3140f7aeccd9b15de751c713ca2502d8eab86152e8f1cc55361`.
- Priority route APK SHA-256:
  `10d9995c4f7228754cb7cc7bbab101cfd6657096acb9b4f63c43745186014763`.
- Final Debug APK used for Priority update/cold reopen SHA-256:
  `e25ecd0531fbde056c9d844ccad8dad7fbe3528392ad14c54b6c2197eb36fbd5`.
- Priority finalization receipt:
  `32badc8278acb45a94e9c8c663129d7dcc520bb9db95b9513ceab6302c8d359d`;
  unchanged workspace SHA-256:
  `8c926798cc7d92519db436ded99eeb86cc7a249f1f294cbc04924e27d784495a`.
- Sum-to-Ten finalization receipt:
  `03e9d82e74bcf523085ee2dccd4312a8cd305a86dd77576ab01e2bd7635a4de1`;
  unchanged workspace SHA-256:
  `79096ab0a26ef2dd0ba7d64b140ea96077eac12779c3501251fc3e39936ce405`.

These are separately identified Debug builds, not one artifact retrospectively
credited with every route. The final APK has all 330 bundled content files
verified against the manifest. Their payload bytes are unchanged; the content
binding now names the admitted Core recipe commit.

## Focused verification

- Core package producer: 696 managed, 17 admission and 15 inventory cases;
  eight package/no-siblings checks. Exported package bytes match the local feed.
- UI producer and fresh consumer: five builds passed; product and focused
  managed groups passed, including 27 original-owner finalization/projection
  cases. Existing test-analyzer warnings are not reported as a clean test build.
- Android managed/native integration covers Priority/Sum-to-Ten × local/linked
  storage, real catalog purchases, cold reopen, cancellation, forged previews,
  owner ABA and postcommit refresh failures. Affected compile checks passed.
- Selected Python source/package/physical-contract checks passed after correcting
  the exact content commit/tree binding; no whole-repository suite is claimed.
- Career Quality and Skill Group runtime identity tests passed after rebinding
  their content-dependent digests. Final Debug APK build: zero warnings/errors.

Core runtime source: `e8d12598b4897d006f58965389a9ecac0bd5f9d2`;
package recipe: `3710828effe1200517de3a618794d1674ba9579f`;
UI seal: `4db8d445fdd13c8c99dd50eb6d59fe2d243a6062`.
Core PR #96 and UI PR #257 merged through their existing required checks.
Workflow changes here repin inputs; they do not change triggers or protections.

## Remaining limitations

- Ambidextrous is still unavailable: its `{arm} - 1` rating limit requires an
  exact anatomy-dependent projection the current quality source adapter lacks.
  It remains visibly unavailable/fail-closed, not silently treated as rating 1.
  Empty Qualities success is not proof of every paid quality.
- Complex magic/metatype options and exhaustive catalogs were not covered by
  these minimal routes. Method availability does not imply all-options parity.
- The finalization detail remains verbose at large font sizes; only the concrete
  starting-cash contrast and search-keyboard defects were corrected here.
- This increment creates no new Release AAB or Play publication. Preview 52's
  existing release evidence remains unchanged. Origin/provider work is separate.
