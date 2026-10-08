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

## Follow-up: contextual paid quality and cold recovery

The original minimal routes used empty Qualities drafts. The follow-up fixes
Ambidextrous' `{arm} - 1` limit for the exact original Priority/Sum-to-Ten
Human/Mundane prerequisite with ordinary two-arm anatomy. Unknown variants,
grants and active limb modifiers remain rejected; Karma anatomy support is not
claimed. The source node and its four-Karma price are preserved, not replaced
with a hard-coded generic rating limit.

Package production exposed a second concrete defect: continuation recovery had
captured the legacy catalog without the prerequisite-dependent quality context.
The corrected frozen capture binds the entire prerequisite record and both
contextual/legacy catalogs. Restore preview and confirmation recheck the full
workspace. Substituted prerequisite choices, uncaptured live-source fallbacks,
owner ABA and stale/replayed operations remain rejected.

Verification of this correction:

- 60 focused Core continuation, source-capture, restore, quality, owner/CAS and
  historical-draft regressions passed; 106 package-control tests passed.
- The corrected package producer passed 703 managed cases, owner-admission
  checks and 15 strict inventory cases. The eight published package payloads
  match the local development feed byte-for-byte.
- Native Debug APK `3f78ac68dfc60d500565e83d5993c4ddc8e189f328bdd85d0b112256f8b92d9d`
  selected Ambidextrous at rating 1, previewed 25 → 21 Karma and saved once,
  revision 4 → 5. After verified process death/relaunch, Create reopened at
  revision 5 with 21 Karma, and the saved quality receipt reopened unchanged.
  Receipt digest:
  `9613a337bcbeb2f13ccc68a407a7ea05f6542a6ad8e12671d3d1a6769c5c2992`.
  Workspace SHA-256 before/after restart:
  `13c4047ee027031b8e01f33ce8fb56b5cdd7be6b1faaa63b22248652c4f3df13`.
- The Android package intake passed managed/native Priority/Sum-to-Ten ×
  local/linked-owner allocation and purchase cases, including actual catalog
  purchases, save/reopen, cancellation, ABA rejection and finalization.

Core runtime: `633f81abaf5f18be10235d28765e9245760a8483`;
recipe: `fb86e9c9d6ffdc0ed2ca5e1dda87b87d624fa5c7`.
The new immutable local-build bundle has SHA-256
`1c6540ac3fc46d57589cd91948cce29d50a15744509e9efe67c39d54c1b5671a`;
anonymous public readback matched. This is package provenance, not Play evidence.

Core PR #97 merged as `46acff4f83b3790c1e802aa13cd564dde9198f07`;
UI PR #260 merged as `394e934c4cde3ecbf266a1ca0ec3d4bb6f3a0ac7`, with
reviewed seal `b6fe493e0ebd07b5eed1e91e76fd56dd7f9d67ff`. Both merges preserve
their reviewed source trees and passed the existing protected checks. Android
now consumes that exact seal and the corrected Core recipe; workflow edits are
identity repins only.

The final local Debug APK has SHA-256
`40c0c017dbf6b922f7ab5a22fb56f6c8f727406c39ed3579d646f5eae311ed57`.
Its build passed with zero warnings/errors, and all 330 content payloads and the
manifest passed verification. After the preceding native route also saved zero
Karma conversion in Resources (revision 6), updating to this final APK and cold
launching reopened Create at revision 6 with byte-identical workspace state:
`d30910710817b92a14879648dd4ccd11d1c8cb3a40f1bf8eeffa85e07e021090`.
The follow-up paid-quality runner has not been finalized natively; the earlier
minimal-route Career results and focused paid-quality finalization tests remain
separately scoped evidence.

The final UI seal's local consumer replay passed five consumer builds and the
selected managed test groups. Its product-test compile retained 61 existing
analyzer warnings (zero errors). Android's final package verifier, selected
package/workflow/content/security contracts and both eight-group Career Quality
and Skill Group runners passed. The broad cross-repository Android source-contract
suite is not claimed green: stale ambient sibling sources caused lookup failures,
and the existing icon-scale assertion disagreed with the checked-in icon scale.
The changed dependency-pin contract was rerun separately and passed; no assertion
was weakened to obtain a pass.

### Emulator observation boundary

The first follow-up boot reported an app ANR after BOD increase/immediate Back;
its retained trace sampled the UI thread waiting in HWUI drawing. SystemUI and
other guest processes also had ANRs. The emulator inherited a heavily throttled
two-CPU agent cgroup. Moving only the owned emulator into a bounded independent
four-CPU/6-GB transient scope let the same APK pass that interaction and the
paid-quality save/restart sequence with no ANR events observed in that boot.
This supports a scheduling contribution, not a claim that all product latency
is resolved or physical-device behavior is proven. Failure evidence is retained.

## Follow-up: quality catalog readability

The catalog no longer puts full authority/runtime hashes ahead of its budgets.
Exact revision and digest values retain their automation IDs behind a collapsed
technical-details disclosure. Detached buttons cannot toggle a replacement view.
The catalog and configure page explain a disabled source in ordinary language;
other unavailable reasons use a conservative generic message. Admission, costs,
owner checks and persistence are unchanged. Intro, budget and save-boundary copy
is shorter in English, German and Spanish.

Local Debug APK SHA-256:
`3790728e3df2b036944bd69d4447174310328708fad031cf1b610b11ca5dbaf7`.
The build passed with zero warnings/errors; twelve focused source/localization
regressions and the private-key hygiene check passed. On the owned API 36 x64
emulator at 150% font scale, update/cold launch restored revision 6 with one
selected quality, four Karma spent and 21 remaining. The disclosure opened and
closed, exposing the exact digest anchors only when opened. The disabled
360-degree Eyesight entry showed the source explanation and its Add button
remained disabled. Workspace bytes stayed unchanged at SHA-256
`d30910710817b92a14879648dd4ccd11d1c8cb3a40f1bf8eeffa85e07e021090`.
This is a display-only follow-up, not a rerun of every method or a Play release.

## Follow-up: Skills before choosing a native language

A native Human/Magician Priority route exposed another interaction defect:
Sorcery's plus button did not retain a legal increment before the native
language was chosen. Core returned a valid but incomplete projection; Android
incorrectly required save eligibility merely to stage that local selection.

Local staging now accepts an exact Core projection whose **only** blocker is
the missing native language. Binding, snapshot, digest, projection, access and
budget checks remain enforced. Review and persistence still require a complete
confirmable preview. This does not persist incomplete edits or relax Core's
rules. The actionable language hint is translated into English, German and
Spanish and appears before the catalogs.

Focused verification:

- Six source-contract tests passed. Managed compile and Android Debug build
  both passed with zero warnings/errors.
- Actual Core/native-draft tests cover Priority Sorcery and Sum-to-Ten Adept,
  Technomancer, Mystic Adept and Sorcery, including pre-language increments,
  native-language removal/reselection, blocked incomplete confirmation,
  stale/tampered packets, budget/access rejection, cold save/reopen and replay.
  These are managed tests, not device coverage for every talent.
- Debug APK SHA-256:
  `c167413ad3d515784dfd73f5d5d1c1900ccc1889fa2bdeb9591a7bba48b40f7e`.
  On the owned API 36 x64 emulator at 150% font scale, the Priority Magician
  selected Alchemy 1 and Sorcery 1 **before** selecting Arabic as native.
  Both increments remained visible. One explicit confirmation saved revision
  3 → 4, spending one active point and one group point, with no knowledge cost.
  Verified process death/cold launch reopened the saved Skills draft and
  displayed Alchemy 1. Workspace bytes remained identical:
  `9e2ec1f1bdcb06042b0bba020eb9eb205999a59b83f979fe3620a3d95cffb625`.
  Receipt: `137cce158ac55287e26e72e36cea721dc9bc1b12d832b71e5921bcf17adede67`.

The preceding unchanged APK had a separate first-launch no-focused-window ANR
on emulator boot 14; its failure packet is retained. The corrected APK's boot
15 route/restart produced no ANR event, but this Skills fix does not establish
the cause or resolution of that earlier startup failure. This Magician route
has reached Skills, not yet a new native Career/finalization proof.

## Follow-up: owner-bound Magic / Resonance

The native Human/Magician route exposed two additional blockers after Skills:
the shared overview and native service read the legacy workspace partition,
and the dashboard treated the missing Magic draft as a prerequisite for entering
the editor that creates that draft. An owner lease alone did not redirect the
legacy store. Core now supplies an explicit original-owner companion; UI and
Android use it for load, preview and confirmation. Only the exact step's own
missing-draft blocker is admitted with a matching, ready typed projection.
Foreign blockers, stale revisions and invalid projections remain rejected.

Confirmation runs off the UI thread. Account checkpoints are owner-partitioned,
retain their issuing stamp and reject owner ABA; trusted-local keys remain
compatible. A known durable receipt survives postcommit refresh failure rather
than being reported as an unapplied operation. Unknown outcomes retain the
identical command for original-owner recovery.

Rendered recovery callbacks retain their original display instead of capturing
the current account when tapped. Local selections also retain the exact owner
stamp, so opening an old selection after A→B→A cannot rebind it to fresh A even
when the workspace id and revisions happen to match. These callback changes
follow the native APK evidence below; that APK is not evidence of the later
callback patch. The focused managed regression passed with zero build warnings
and errors, including detached selection/recovery callbacks and retained-draft
owner ABA rejection. The final package smoke is recorded separately below.

Focused local verification:

- Core linked/local partition isolation, default/foreign/ABA stamp rejection,
  unchanged character XML, exactly-once confirmation and cold receipt replay.
- Actual Core/native dashboard, catalog, preview, confirmation, UI heartbeat,
  cancellation, journal isolation and Skills/Magic save/revisit/replay tests.
  The managed slice also covers Sum-to-Ten Adept, Technomancer, Mystic Adept and
  Aspected Sorcery; this is not physical-device coverage of each talent.
- Eleven Magic source-contract tests passed. Affected managed and native Debug
  builds passed with zero warnings/errors.
- Native Debug APK SHA-256
  `72fe4a04fe1bb1afaf550b77e478c4afc33b954becadda47122e6a665b96e2d6`
  used a local development feed, not a release package seal. At 150% font scale,
  the real UI selected Hermetic plus Acid Stream, Agony, Analyze Device, Analyze
  Magic and Analyze Truth. One confirmation saved revision 4/4 to 5/5, with five
  spells and zero remaining spell choices. No character XML changed.
- Verified force-stop (PID 3908 gone) and relaunch (PID 5461) reopened Create,
  the saved Magician draft and its exact receipt
  `6fa5d6972eb2175f8c61389d267bee7e1d177342cf4eb7cdc30ed4dd9adfdc17`.
  Workspace bytes were identical before/after restart, SHA-256
  `60e7e6d3162fc0e03e05f408622b07f14d52d5eb35517ecffabfcee7d4036d31`.
  No Chummer ANR was observed in this boot; an unrelated Dialer ANR was present.

This awakened route has reached persisted Magic, not a new native Career proof.
The earlier minimal-method Career results remain separately scoped. Exact Core
and UI package convergence follows the successful local route; no Play release
or exhaustive catalog claim is made by this Debug evidence.

### Exact package follow-up

The final local package producer uses Core runtime source
`fac18e875a8b8d118136b4c3b806f365ed805dd2` and recipe
`655677478a44d9cd8e18f21723d0792d9caf4c4a`. It passed 703 managed,
18 owner-admission and 15 strict inventory cases, plus scoped-storage and
no-siblings checks. Its new immutable public bundle has SHA-256
`1393762eaf58504961e8b9c868753b171172cc2ebef4224e17b79ece4774dd8b`;
anonymous readback matched. This is local package production, not hosted
runtime or Play evidence.

UI seal `b930829109919f9444e7207f893c8f9840f3e587` passed the exact local
consumer replay and merged normally in PR #262 as
`e6df90948ec5a8c53cbabae8ae5069b12413f2a3`. Five consumer builds and selected
managed groups passed; the product-test project retains 61 existing analyzer
warnings, with zero errors. Android's package verifier accepts the exact feed,
receipt and both actual NuGet lock files. The content manifest and runtime
materializer now name the same Core recipe; all 330 content payloads are unchanged.

The final Android intake passed the managed/native dashboard and Sum-to-Ten
Magic slice, including actual Core save/reopen, original-owner and ABA rejection,
detached callbacks, cancellation, journal recovery and Skills/Magic revisits.
Affected builds had zero warnings/errors. A further 193 selected Python
source/package/workflow/provenance regressions and the repository private-key
hygiene check passed. The broad ambient-sibling suite was not rerun.
Both affected Career Quality and Skill Group runners also passed their eight
behavior groups with the new exact runtime/content identities.

Final separate Debug APK SHA-256:
`9b9b0a1d1e547f4e775a7a57709019aa045e2b0d162fed3b91fe5e4d27dfa063`.
Its local build passed with zero warnings/errors; all 330 APK content files and
the embedded manifest passed verification. Tracked app source and consumer
locks match the actual build snapshot. The update opened the saved Magician at
revision 5 and its identical receipt, without changing workspace bytes.

Verified force-stop removed process 3727; cold launch created process 4130.
At 150% font scale, Create reopened at revision 5, then the saved Magician draft
and receipt `6fa5d6972eb2175f8c61389d267bee7e1d177342cf4eb7cdc30ed4dd9adfdc17`
opened again. Workspace SHA-256 remained
`60e7e6d3162fc0e03e05f408622b07f14d52d5eb35517ecffabfcee7d4036d31`
before update, after update and after restart. No ANR event was observed in this
emulator boot. This verifies the final package's update/cold-reopen route; it
does not claim a new native confirmation or awakened Career finalization on
that APK. The owned emulator was stopped after the bounded check.

### Native awakened finalization and Career closeout

Core PR #98 merged normally as `28684d772fa5088764f53756300bbc5d5c1eba21`
after the required package checks passed. Android PR #200 merged normally as
`b05ad8f88a8eda0837cc665dbccc3da962cbf2a4`; its review and main have identical
tree `498e0e822669d19fc011c2ce485a2788e03b99e9`. No protection was changed.

The unchanged final Debug APK `9b9b0a1d…` above then continued the saved
Priority Human/Magician through empty Qualities (revision 5 → 6), zero-Karma
Resources (6 → 7), and one Flashlight costing 25 Nuyen (7 → 8). Native review
showed 50,000 starting Nuyen and 49,975 remaining before carryover. Street
starting cash used an explicitly entered synthetic dice total of 3, yielding 60.
Core applied the configured carryover limits, leaving 7 Karma and 5,060 Nuyen.

One explicit confirmation finalized revision 8 → 9, with one durable receipt:
`5f0f5c6cbde8e3447a868286c326c6f1cd5c3cefa94f28d73c9f27ea121be336`.
Read-only inspection of the actual saved XML verified `created=True`, Human,
Priority, Magician enabled, Magic 3, Hermetic tradition, exactly the five selected
spells and exactly one Flashlight. The actual native Career wizard opened at 9/9.

Verified force-stop removed process 2557; cold launch created process 5675.
The sheet and Career wizard reopened at 9/9 with the identical receipt. Exact
workspace bytes remained SHA-256
`08f2fa93c969cdffa0eb1a7eb6138c5e0562934bde2cc5e3f381f0709fb84e00`.
No ANR event was observed in this emulator boot. The bounded observer first
exhausted its scroll allowance on the very long final review; an earlier
overlapping hierarchy read also failed. Neither observation was treated as a
pass or caused a confirmation retry. A subsequent serial observation located
the enabled confirmation and exactly one tap was dispatched.

This closes the specific native awakened Career route, not every talent,
spell, metatype, build-method combination or physical Play installation.

### Final-review readability and paid-quality closeout

The native final review exposed many screens of raw source identifiers at 150%
font scale. Those exact anchors now sit behind an initially collapsed technical
details toggle, using the existing English/German/Spanish disclosure copy.
Every ordered delta, before/after value and cost remains visible. The disclosure
does not mutate or refresh the review, rejects detached/stale/account-switched
callbacks, and resets on a fresh render. Confirmation and Core rules are unchanged.

Eighteen focused source-contract cases passed. The affected local Debug build
completed with zero warnings/errors, APK SHA-256
`1dfa20ae5c5b09ec7b6238cd50607d60efacb08ec3fb03856bb6cb9469f3500d`.
All 330 embedded content payloads and the exact manifest match the preceding
package authority. This uses the same sealed Core/UI graph, not a new package
producer or remote build.

On the owned API 36 x64 emulator at 150% font scale, the existing paid-quality
Priority runner continued from its saved zero-Karma Resources draft. One empty
Gear confirmation advanced revision 6 → 7. The final review showed the unchanged
plan after disclosure open/close and leaving/reentering the page; the new review
was collapsed again. Workspace bytes remained
`8a6f6aedffe99b86c1b96f419244156b87463f44ca02ea5562d43399ee362b70`
until explicit confirmation.

One confirmation finalized revision 7 → 8. Saved XML contains exactly one
Ambidextrous quality, its four-Karma source cost and one enabled, quality-bound
Ambidextrous improvement. The durable receipt is
`7e75bb3423eccb9b04c91cc1fddb8acba23ab5a74f265d7c209f272ce97d4568`.
Force-stop removed process 6041; new process 7249 reopened the Career sheet with
that same receipt. Revision stayed 8/8, receipt count stayed one and workspace
bytes stayed SHA-256
`2b5481ccb5d4364814256f296e996383ef1cee300f2753089ed102280675ffa6`.
No ANR event was observed in this boot. The owned emulator was then stopped.
The Magician proof above belongs to its separately identified preceding APK;
the display-only build is not presented as a rerun of all methods/talents.

## Remaining limitations

- Ambidextrous was unavailable in the earlier minimal-route APKs. The bounded
  follow-up above is not proof of arbitrary anatomy, Karma-method support or
  every paid quality.
- Complex magic/metatype options and exhaustive catalogs were not covered by
  these minimal routes. Method availability does not imply all-options parity.
- Finalization still exposes technical target IDs in individual delta headings;
  the long source-anchor paragraphs are now collapsed by default. Full friendly
  naming/localization of the Core delta catalog is not claimed by this change.
- This increment creates no new Release AAB or Play publication. Preview 52's
  existing release evidence remains unchanged. Origin/provider work is separate.

## 8 October follow-up: Life Modules Apprentice into Career

The existing Apprentice category test now has a separate
`--life-module-apprentice-career-content-root` route. It exercises actual MAUI
page handlers with production Core/Presentation, not simulated rules: explicit
Combat/Air restrictions, two purchased Magic levels, native English, zero-Karma
Resources, empty Equipment/Lifestyles/Contacts, Hermetic tradition and Manabolt.
It checks a fresh private-draft session, unchanged runner bytes before
confirmation, one finalization, saved Career reread and rejection of a stale
confirmation. The original Apprentice picker/retired-control/owner-ABA route
also passes independently. The local interaction build had zero warnings/errors.
These managed runs are not device-rendering evidence.

The matching bounded route also passed on an API36 x64 emulator with the existing
Preview146 SDK-test-signed Release APK:
`8cbf9309225cd99a3b1e39eac8386d5441407c33805b1ba31b171dcb4fe52433`.
Runtime producer is `6ca715723a7f1260cdc779372b6630e20af2e24f`;
there is no app-source delta between that producer and base `7b099728…`.
Core recipe `76e77b13…` / runtime `899be24…` and Presentation seal `66b3185…`
remain unchanged. No new APK, AAB, signing operation or Play upload was needed.

One synthetic Core-seeded Elf/module sequence was added beside the eight
existing synthetic runners. The real native controls completed its remaining
allocations from saved revision7. A synthetic dice total6 produced Street
starting cash120. The review explicitly showed 455 Karma before carryover,
7 retained and 448 discarded. Exactly one confirmation saved Career at8/8,
including one Hermetic tradition, one Manabolt and one finalization receipt.

After a verified force-stop, a new app process reopened the sheet and actual
Career wizard. Opt-in technical details confirmed the same workspace at8/8;
details were hidden again afterward. All27 workspace files were byte-identical
across the restart and opening Career. All24 pre-existing files remained
unchanged throughout, and the new runner's bytes were unchanged until final
confirmation. Saved workspace SHA-256:
`21bd442e9fbe38f172708c074f3b15a05765ff06e248367ee7e77849b623abda`.
Finalization receipt digest:
`sha256:76a75c7215c8570a5c77684e164e59b8bea617498ff482388fbcc90d489863f2`.
Private read-only state verifier SHA-256:
`a34bd505255306c1cd88dd8ec72f98ecee6166a99c727298ff5892e5e0d500be`.

The owned emulator was stopped after the route. Its8GiB host limit had zero
memory-limit/OOM events and zero swap; guest SystemUI, keyboard and service
startup ANRs before the app launch remain recorded. One observed SystemUI Wait
was needed. A transient null hierarchy on the later app restart was not counted
as readiness; subsequent visible Career and exact saved-state checks passed.
Catalog/finalization loading was slow. This is not a clean-boot or performance
qualification, and no app ANR/crash was observed in the retained event log.

Concrete UI follow-ups observed here remain open: duplicate default `Runner`
aliases conceal saved names in the roster/picker; some Life Modules blockers
still display technical codes; the Career sheet exposes a settings GUID.
This route does not prove every talent or subsequent Career action. Its seeded
sequence has no Origin chapters, so it does not requalify book generation or
claim physical Play installation. Existing per-chapter app/EPUB image evidence
remains separately recorded in [Origin EPUB and scenes](ORIGIN_EPUB_AND_SCENES.md).
