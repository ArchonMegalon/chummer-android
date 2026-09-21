# SR6 native foundation increment — 21 September 2026

This is an experimental pending-draft wizard, not complete SR6 creation or a
Play delivery. The native New runner dialog shows Priority, Sum-to-Ten, Point
Buy, Life Path and optional Karma (SR6). All five have Core-owned bootstrap
identities; Priority, Sum-to-Ten and Point Buy have the foundation editor below.
Life Path and optional Karma must not enter an SR5 editor or priority service.

## Implemented

- Native foundation route for pending SR6 Priority/Sum-to-Ten/Point Buy runners.
- Explicit five-category priorities for the two priority methods; metatype and
  talent are explicit choices for all three methods.
- Point Buy has its own four pool-purchase selectors, with Core-provided limits,
  costs and remaining CP. No priority ranks are invented. The separate 50 Karma
  is displayed but not spent here. No free spells/forms/power points are granted.
- Core load/preview/confirm off the UI synchronization context. Android does
  not calculate rules or directly modify character XML.
- Core-generated budget/source anchors followed by explicit confirmation.
- Exact owner stamp, workspace, revision, displayed state and page lifetime
  checks. Editing a selection invalidates the old confirmation. Departed
  controls cannot dispatch writes; uncertain outcomes are reopened, not retried.
- Saved draft choices return after cold reopen. Historical previews are not
  reused as current confirmation permission.
- Core-owned attribute allocation with separate normal/adjustment pools,
  metatype ranges (including reduced maxima), active Magic/Resonance and Edge,
  and the one-physical/mental-attribute-at-maximum restriction.
- Attribute review shows the Core calculation and remaining budgets. Partial
  allocations can be saved as drafts. Changing foundation choices explicitly
  resets the next confirmed allocation; old decisions remain in saved history.
- Native skill allocation with Core-owned availability, rating limits,
  specialization costs and aspect selection. Unavailable skills are explained
  without input controls. Player-entered specialization names require GM review;
  exotic weapon specialties share one rating and the first is free.
- Knowledge/language page after saved attributes: one free native language,
  unrated topics, and additional Basic/Specialist/Expert languages. Core derives
  the separate pool from Logic, validates cumulative costs, and prevents buying
  another native language. Stable entry IDs survive editing and cold reopen.
  Attribute changes preserve the choices and reject any resulting overspend.
- DE/EN/ES labels, validation feedback and explicit incomplete-feature scope.
- Talent planning after saved attributes: Core-derived free spell/form budgets,
  aspect selection, mystic-adept priority split and whole-power-point CP purchase
  for Point Buy. Saved budgets are distinguished from unconfirmed edits. This
  does not select or learn individual spells, rituals, forms or adept powers.
- Returning from a saved child refreshes a clean parent from current Core state.
  Unsaved inputs are retained only for the same binding; a conflicting revision
  or lost dirty baseline blocks further editing. Old confirmation/navigation
  callbacks and owner A→B→A transitions remain rejected.

Karma/Aptitude/Bilingual, individual talent abilities/effects, equipment, finalization and
Career entry are still missing. Point Buy pool purchases are a partial draft,
not a completed creation method, even when all CP are spent. Life Path and
optional Karma still need their own rule implementations and native flows.
This does not enable SR6 Origin generation or audiobook conversion.

## Current parent-return correction — 22 September 2026

- Android functional commit: `46a836a59a3c95b1d1cc346b8716326899c49624`.
- Core unchanged: `bb128f8268581d08806c4670dbe953a2457d78b3`.
- Presentation unchanged: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Local keyless Docker build against explicit source roots; no package reseal.

`sr6-native-parent-return-2.log`: **55 native managed scenarios pass**, including
actual parent/child navigation, clean refresh after both attribute and talent
saves, stale callbacks, preserved same-revision edits, dirty revision conflicts,
failed-load fencing and full owner A→B→A rejection. The first run passed 54
before the failed-load regression was added. Unchanged Core results below are
reused, not reported as a new run.

`sr6-parent-debug-build-1.log`: Debug x64 APK, zero warnings/errors, 1m29s.
Retained `sr6-parent-debug.apk`, SHA-256:
`7a9770354d8370a47763fa80d0b9c2c5a8df76d6813fa0a7362509629627d959`.

Actual API36 smoke began with a fresh debug install and New runner → SR6 →
Point Buy → Human/mystic adept. Foundation saved revision 2/2; the attribute
page explicitly saved its default allocation at revision 3/3. Back returned to
the same usable foundation, showing revision 3/3. Its talent button opened the
current budget directly, without the former runner/reopen detour. One selected
whole power point cost 8 CP; review showed Magic 1, total 18 CP spent / 82
remaining, separate 50 Karma and no remaining spell slots. Explicit confirmation
saved revision 4/4. Back again returned to a usable foundation at revision 4/4.

Force-stop removed PID 4744; launch created PID 5876. Reopened foundation and
talent controls retained revision 4/4 and power-point selection 1, with no
historical Confirm button. Exactly three decisions and the entire workspace
bytes were unchanged across restart, SHA-256:
`580e033908d5b25d716d48f84d616563797450cf0bbbe4e0656fcdac981cc983`.
The local packet retains `sr6-parent-*` APK, logs, screenshots, fresh hierarchies
and before/after workspace copies. Startup system-process ANRs preceded app
installation; closing the System UI dialog allowed interaction. Initial observer
exit 137/null hierarchy were rejected. No mutation was replayed. Retained fault
events contain no crash or ANR for the debug app. The owned emulator was stopped.

This resolves the parent-return limitation recorded in older sections below.
It does not complete any SR6 creation method: individual abilities, further
creation domains and finalization remain open. Life Path and optional Karma
remain bootstrap-only. No user Play package, upload key, AAB, main merge or Play
upload was involved.

## Historical talent-budget increment: exact local source assembly

- Android functional commit: `4c3d6ac5`.
- Core integration: `bb128f8268581d08806c4670dbe953a2457d78b3`.
- Presentation unchanged: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Same local keyless Docker toolchain and explicit source roots, not a new seal.

`core-sr6-talents-2.log`: **155 Core tests pass**. The first Core run passed
151 before four explicit Sum-to-Ten entitlement cases were added.
`sr6-native-talents-2.log`: **50 native managed scenarios pass**. New coverage
tests Priority mystic splits and Point Buy purchases in DE/EN/ES, stale and
departed controls, no duplicate confirmation, saved budgets and cold reopen.
The first native run passed all 44 earlier scenarios, then correctly rejected
the new test's invalid Human/heritage-B seed; the seed was corrected to C.
That failed run is not counted as a pass. The final run includes saved-budget
copy that distinguishes stored results from the editable controls below it.

`sr6-talent-debug-build-1.log`: Debug x64 APK, zero warnings/errors, 1m35s.
Retained `sr6-talent-debug.apk`, SHA-256:
`447d0a258648e57e9da305c50d0b0deacccc71226e548935226f2ff5806e42f2`.

Actual API36 smoke used a fresh installation and New runner → SR6 → Point Buy,
not an injected draft. Human/mystic adept with two extra adjustment points
saved revision 2/2 (18 CP). The attribute page assigned two adjustment points
to Magic, saved revision 3/3 and exposed the talent step. Talent review showed
Magic 3, two selected power points at 8 CP each, total 34 CP spent / 66 remaining,
spell/ritual ceiling 2 and no free spells. Explicit confirmation saved revision
4/4 with exactly three decisions. No individual abilities or finalization were
claimed. Force-stop removed PID 4671; launch created PID 5892. The restored
talent page showed the exact selection and saved budget, with no historical
Confirm button. Workspace bytes before and after restart were identical:
`57932cb1825304f7e28e9ff8fe64ee1607eca5fdd1d15502768c7d1641988cf4`.

The packet retains `sr6-talent-*` APK, logs, screenshots, hierarchies and workspace
copies. Emulator startup had system-process ANRs before installation; closing
the System UI dialog restored interaction. Early observer failures and one
null app hierarchy were rejected; no mutation was replayed. The known parent
navigation issue remains: after saving a child page, return to the runner and
reopen the foundation. This is a usability follow-up, not a completed wizard
claim. The owned emulator was stopped. No Play package/upload key, package seal,
AAB, main merge or Play upload was involved.

## Historical Point Buy increment: exact local source assembly

- Android functional commit: `29e6d179` (pool editor `ca0b8e5a`, then incomplete
  selection feedback corrected at the coordinator boundary).
- Core integration: `6ead4535c8c672be5b5c6ce7902e7182a86d1a6f`.
- Presentation unchanged: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Same local keyless Docker toolchain and explicit source roots, not a new seal.

`core-sr6-point-buy-2.log`: 138 Core tests pass. `sr6-native-point-buy-3.log`:
44 native managed scenarios pass, including all three supported draft methods
and all four pages in DE/EN/ES. Point Buy coverage rejects CP overspend without
confirmation, verifies that changing one pool retains all other selections,
checks no fabricated priority ranks, and saves/reopens purchased budgets plus
independent attribute/skill/knowledge allocations. Stale/departed controls and
existing ownership/recovery cases remain covered.
`sr6-origin-after-point-buy-1.log`: 12 existing Origin book regressions pass.
The first native Point Buy run also passed, before the final Core source-anchor
aggregation and method-specific malformed-selection message; run 2 uses both.
Run 3 additionally covers the coordinator rejecting missing metatype/talent
without asking a Point Buy user to select five nonexistent priority ranks.

`sr6-point-buy-debug-build-2.log`: Debug x64 APK builds with zero warnings/errors
in 1m39s. Build 1 also passed, before the coordinator message correction.
Retained APK `sr6-point-buy-debug.apk`, SHA-256:
`d5fd6e8e61b3a903c75bba9e7d68e688c270e574d59238bb5b3e1d11416c5de6`.

### Actual Point Buy New runner/save/restart smoke

The API36 emulator used a fresh installation of the separate debug package and
the actual New runner dialog: SR6 → Point Buy → Create runner. No stored draft
was injected. The foundation page accepted extra pools 6 attributes, 6 skills,
2 adjustment and 3 resource units, with Human and Technomancer. Core review
showed 10 attribute points, 18 skill points, 3 adjustment points, 45,000 nuyen,
base Resonance 1, and 45 CP spent / 55 remaining (10+12+12+8+3). The separate
50 Karma and missing spell/form/power/finalization scope remained explicit.

One explicit confirmation saved revision 2/2 with exactly one decision, empty
priority assignments and a null priority rank. Force-stop removed PID 4975;
launch created PID 5737. The runner restored automatically. Reopening the
foundation restored all six input selections, without a historical Confirm
button. Workspace bytes before/after restart were identical, SHA-256:
`122b07a0b003f439bbfe231c413c76f2c36fe34bfdcd6e7c155ded32823dfff9`.
The reopened attribute page also displayed the purchased pools and correct
base Resonance. Attribute/skill/knowledge writes for Point Buy have managed
coverage here, not additional device mutations in this smoke.

The packet below retains `sr6-point-buy-*` screenshots, fresh hierarchies,
before/after workspace copies, the APK, build log and Android fault events.
System UI had an ANR before app installation; closing System UI cleared it.
Null accessibility roots during the ruleset transition and process restart
were rejected, followed by fresh reads without repeating any mutation. The
app route passed, and the owned read-only emulator was stopped. No Play package,
upload key, AAB, package seal or main merge was involved.

## Historical knowledge/language increment: exact local source assembly

- Android functional commit: `256dd7246a2e76e40289d4f136b79badf6e560eb`.
- Core integration: `bd22577336f423a35dc469acb03a11e3260aa7c3`.
- Presentation unchanged: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Same local keyless Docker toolchain and explicit source roots, not a new seal.

`core-sr6-knowledge-1.log`: 124 Core tests pass. `sr6-native-knowledge-3.log`:
32 native managed scenarios pass, including both methods and all four pages in
DE/EN/ES. The new cases cover the attribute prerequisite, empty/deleted rows,
stable IDs, invalidated/departed confirmation, overspend without writes, and
preserved independent attribute/skill allocations through cold reopen.
`sr6-origin-knowledge-regression-1.log`: 12 existing Origin book regressions pass.
The first two native runs stopped at test-harness errors (a synchronous row
callback passed to an async-only observer; a disabled detached test button).
Those setup errors were corrected, not counted as successful runs.

`sr6-knowledge-debug-build-1.log`: Debug x64 APK builds with zero warnings/errors
in 1m46s. Retained APK `sr6-knowledge-priority-sum-debug.apk`, SHA-256:
`080266a2cf029f77099310a06669ac4a8be680ee3aebd47d36537eeb365e329f`.

### Actual knowledge/language save/restart smoke

The API36 emulator used the authentic earlier synthetic Priority attribute
draft as setup, copied into the separate debug package; this was not another
fresh New runner/bootstrap test. Through the actual attribute UI, Logic was
raised to 3 and explicitly saved (revision 4/4). The old foundation page rejected
its stale revision on Back, so the route was reopened through the runner as
instructed. This conservative navigation remains a usability limitation.

The knowledge page then accepted native `German`, topic `Seattle gangs`, and
`Sperethiel` at Specialist level. Core review showed 3 picks spent, 0 remaining,
Logic 3, free native language, comprehension +2, GM scope review and pp70/100
source binding. One confirmation saved revision 5/5, four total decisions
(two historical setup decisions, one attribute edit and one knowledge save).

Force-stop removed PID 4373; launch created PID 5801. Reopened native controls
restored the native language, topic, additional language, level and exact entry
IDs. No historical Confirm button was restored. Workspace bytes before/after
restart were identical, SHA-256:
`8a13c358e370c2721084f71569c235744594521e6dbe5319acc3182198a11b7f`.
The retained packet below contains `sr6-knowledge-*` input/review/reopen images,
fresh hierarchies, workspace copies, startup log and Android ANR events.

The emulator's System UI ANR began before app installation and recurred after
Wait; closing that System UI process cleared the obstruction. One startup
observer exited 137, and transient null hierarchies during system/page/process
transitions were rejected rather than accepted as fresh state. No character
mutation was replayed. The subsequent app route completed, and the owned
read-only emulator was stopped. Sum-to-Ten knowledge and all locales have
managed coverage, not separate device coverage in this increment. No user Play
package or upload key was touched; no AAB or publication was produced.

## Historical skill increment: exact local source assembly

- Android functional commit: `df1e3f77a4a22e780f73c84e28e95c12082782fe`.
- Core integration: `e82a1f8265796ff455ebdd01d1296d8f47aaf702`.
- Presentation unchanged: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Same local keyless Docker toolchain, explicit source assembly, no package seal.

`core-sr6-skills-2.log`: 115 Core tests pass. `sr6-native-skills-2.log`: 26 native
managed scenarios pass (ownership/recovery plus both methods, three pages and
three locales). New cases cover specialization cost, stale controls, preserving
attributes during skill save, reopened selections and unavailable mundane skill
controls. `sr6-origin-skills-regression-1.log`: 12 Origin book regressions pass.
These results do not establish live book generation or audiobook conversion.

`sr6-skills-debug-build-1.log`: Debug x64 APK builds with zero warnings/errors.
SHA-256: `2824bbbece1b865194d8598327d357c2d15de435fa0fbb68e423b882adef6f98`.

### Actual skill save/restart smoke

The API 36 emulator restored the authentic synthetic Sum-to-Ten foundation
draft saved by the historical smoke below (copied into the separate Debug app
as setup, not a fresh New runner route). The native page accepted Athletics 3
with `Climbing` and Exotic Weapons 2 with `Whip`. Core review displayed costs
4 and 2, total 6, remaining 18, source anchors and the GM-review notice. One
confirmation saved revision 3/3 with exactly two decisions. Force-stop removed
PID 4451; the next launch had PID 5106. Reopened controls showed both saved
ratings and specialization names. Before/after workspace JSON was byte-identical:
`53b6e33144daf10e60c4f5909c6b7f943e1cbe842d945189d2d8c21ea032580f`.

Artifacts are retained in the packet below as `sr6-skills-*`, including the
input/review/reopen screenshots, fresh hierarchies and synthetic workspace
copies. The source-bound APK is `sr6-skills-priority-sum-debug.apk`. The emulator
again showed a System UI startup ANR before app installation; Wait cleared it.
A pre-install null hierarchy was rejected. This is not an app ANR or successful
observation. The affected app route then completed and the owned emulator was
stopped. Priority skill behavior is managed-tested, not separately device-tested
in this increment. No Play package or signing credential was touched.

## Historical attribute increment: exact local source assembly

- Android functional commit: `1c5e0ce593fbc6cb8f0e053c9a216348a8579aac`.
- Core integration: `68a83b1fe13b3b02d1af506d81f1fed2e32505cd`.
- Presentation unchanged: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Same keyless local Docker toolchain as the historical increment below.
- No package reseal or main merge. Existing foundation-only decisions retain
  their exact serialized digests; the optional allocation is omitted when null.

Focused verification of these inputs:

- `core-sr6-attributes-1.log`: 98 Core tests pass, including legacy digest
  compatibility, deep-copy protection, invalid allocation, point-kind/rating
  and budget checks, stale/forged previews and cold-store replay.
- `sr6-native-attributes-1.log`: 20 native managed scenarios pass, including
  both methods and both pages in DE/EN/ES, stale confirmation rejection,
  exactly-once save and restored point selections.
- `sr6-origin-attributes-regression-1.log`: 12 existing Origin book scenarios
  pass against this build. No live provider/audiobook claim.
- `sr6-attributes-debug-build-1.log`: Debug x64 APK builds with zero warnings
  and errors. Separate debug package and debug key only.
- APK SHA-256:
  `9b589db4a747823eb26f1be0ca6b2f6fd6ec90294159f7831aa93985fe38246c`.

### Actual attribute save/restart smoke

The API 36 read-only emulator used the authentic synthetic Priority draft
saved during the earlier foundation smoke below, copied into the separate
debug app as test setup. This was NOT a fresh New runner route on the new APK.
The app restored revision 2/2; its native attribute page accepted Body +3,
Agility +2 and Edge +4 adjustment points. Core preview showed Body 4, Agility
3, Edge 5 and remaining pools 19/0. One explicit confirmation saved revision
3/3 with exactly two decisions (foundation plus allocation).

After force-stop, the old PID 4108 disappeared and launch created PID 4978.
Reopening foundation and attributes displayed revision 3/3 and the saved
3/2/4 selections. Before/after workspace JSON was byte-identical:
`f04540b10308df5ab253fc42176c7c797299c9cdf8f6403c38826b419f7997f3`.
Partial allocation is intentionally still a draft, not final character XML.

The retained packet contains `sr6-attributes-*.log`, review/reopen screenshots
and hierarchy files, and the before/after synthetic workspace copies. Startup
again encountered an emulator System UI ANR, cleared with Wait. An immediate
route transition returned a null hierarchy; it was rejected, followed by a
fresh successful observation without replaying a mutation. The owned emulator
was stopped after the successful route. Sum-to-Ten attributes were covered by
the managed native tests, not a second device route in this increment.

## Historical foundation-only source assembly

- Android functional commit: `d12ab8a735d1be4a406081a2a1080e6a9698462f`.
- Core integration: `a86ed70bfc659cd69f8432a7bd06422b2726ac5c`, merging the
  SR6 foundation `e8efffedd` with Life Modules `83ea93b5b`.
- Presentation: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Reused local Docker toolchain:
  `sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.

These are explicit source roots, NOT a new package seal or a package-only
consumer claim. No main merge or protected-check result is implied.

## Historical foundation-only verification

- `sr6-native-foundation-3.log`: 14 passing managed native scenarios using the
  real Core service/file store: eight ownership/recovery cases and both methods
  in EN/DE/ES. Covers owner A→B→A, stale/departed controls, post-commit
  cancellation, lost return, forged preview and one-write-only confirmation.
- `sr6-origin-regression-1.log`: 12 existing Origin book scenarios pass against
  the merged build. This is not live First Book AI or audiobook verification.
- Native compile graph: 279 owned sources, no issues.
- `sr6-debug-build-1.log`: local Debug x64 APK, zero warnings/errors.
- Separate debug package: `com.myexternalbrain.chummer.sr6foundationdebug`.
- APK SHA-256:
  `7942978f2dc6e1d31b2bc842494066ac4e9a7db0ae4c5c2ecfef39136a967493`.

The first managed run failed because the separate SR6 tree lacked newer Life
Modules contracts; the integration merge resolved that mismatch. The second
run reached a test-harness issue: a deliberately stale disabled button could
not dispatch a simulated event. The corrected test explicitly enables that
detached button and verifies that the production callback still refuses it.
Neither earlier run is counted as passing.

## Historical foundation-only API 36 emulator routes

Both used New runner → SR6 → method → foundation → explicit selections →
review → confirm once → force-stop → new process → reopen same foundation.

| Method | Heritage / talent / attributes / skills / resources | Preview | Restart |
| --- | --- | --- | --- |
| Priority | D / E / A / B / C, Human, Mundane | 24 attributes, 24 skills, 150,000 ¥, 4 adjustment | PID 4635 → 5590 |
| Sum-to-Ten | D / E / B / B / B, Human, Mundane | 16 attributes, 24 skills, 275,000 ¥, 4 adjustment | PID 5590 → 6385 |

Each draft retained revision 2 / saved revision 2 and exactly one foundation
decision. Before/after workspace JSON was byte-identical:

- Priority: `6358b82f1ce88c301b816fbc0ead7a1021844564f2d33140e11552fdc3484561`.
- Sum-to-Ten: `f41defad3ff6c88f500e00c68562c5e87099073ca66ee931aad414f74e4e68d2`.

The reopened native controls displayed the saved priorities and metatype.
Screenshots/hierarchies, synthetic workspace copies and logs are retained in
`/docker/chummercomplete-active-worktrees/life-module-book-tests-20260921.L0D7pON5`
with `sr6-` filenames. The owned read-only emulator was stopped afterward.

A System UI ANR appeared at initial emulator startup and cleared with Wait;
it was not an app ANR. Some immediate transition hierarchy reads returned null;
those reads were rejected, not treated as current-screen evidence or grounds
to replay a mutation. The completed routes above used fresh visible state.

## Delivery boundary

Debug key only; no upload key, AAB, Play upload, physical installation, new
package authority or hosted qualification. The installed user/Play package was
not touched. Next work is SR6 talent grants and remaining creation/finalization, then method-specific
Point Buy/Life Path/Karma flows. SR5 Life Modules and its book remain an open
priority before Windows; this increment does not declare them finished.
