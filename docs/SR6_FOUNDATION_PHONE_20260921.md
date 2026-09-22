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
  is spent in its own customization step, not the pool selectors. The initial
  Point Buy pools grant no free spells/forms/power points.
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
- Contacts after saved attributes: Core derives a separate final-Charisma × 6
  pool, Connection + Loyalty costs and per-rating creation caps. Names and
  optional roles are GM-reviewed player text, not approved NPCs. Stable IDs
  survive edits/removal/reopen. Charisma changes cannot silently delete contacts
  or retain illegal ratings. No Karma or CP are charged by this editor.
- Talent planning after saved attributes: Core-derived free spell/form budgets,
  aspect selection, mystic-adept priority split and whole-power-point CP purchase
  for Point Buy. Saved budgets are distinguished from unconfirmed edits. This
  budget page does not select individual abilities.
- Separate Technomancer complex-form editor after a saved talent budget. Core
  supplies 71 core-book form/program variants, source anchors, slot limits and
  Point Buy costs. Add/remove and explicit review/confirmation preserve the
  exact saved selection. Required autosoft model subjects remain GM-reviewed
  player text, not verified equipment. UI text is DE/EN/ES; catalog names follow
  the owned German 2024 source and are labelled as such.
- Separate spell/ritual editor after a saved talent budget. Core supplies 73
  spells and eight rituals, aspect access, a shared slot budget and Point Buy
  costs. Enchanting aspects can select spells but not rituals; alchemy does not
  duplicate known-spell purchases. Names retain the labelled German source;
  controls and validation are DE/EN/ES. Saved formulas do not execute effects.
- Separate adept-power editor with Core-owned level ceilings, exact fractional
  costs and saved power-point budget. The 22 core families expand to 51 options
  for senses, physical attributes and improved skills. Mixed-use skills expose
  explicit full-use/noncombat scope. Saved Astral Perception unlocks Astral.
  Removing prerequisites or lowering budgets cannot silently drop saved choices.
- Separate customization-Karma editor after saved attribute/skill allocation.
  Core supplies available targets and ceilings, cumulative per-rank costs,
  the independent 50-Karma base pool (adjusted by admitted qualities), Nuyen conversion and carry-over warning.
  Base pool previews remain intact; Karma's final ratings drive dependent
  knowledge, magic/resonance and power budgets. The first exotic-weapon subject
  remains explicit GM-reviewed text. No extra CP are charged by this step.
- Karma specialties share that budget and explicit review/save. Core enforces
  the combined pool/Karma creation limit, positive final skill rating and the
  multiple-weapon-type exception without a dice bonus. Free-text subjects carry
  a GM-review warning. Removing earlier ranks cannot leave an orphan specialty.
- Karma knowledge/languages use a saved free-pool baseline. New unrated topics
  and new languages are separate purchases; existing languages use their exact
  IDs and charge only additional levels. Core checks duplicates/native-language
  conflicts and the shared budget. All controls/reviews are DE/EN/ES.
- A separate quality editor offers a partial German-core catalogue: fixed-price
  advantages/disadvantages, physical/mental Exceptional Attribute variants and
  Aptitude skill targets. Core owns costs, six-choice/net-bonus limits, metatype
  restrictions and changed attribute/skill caps. Removing a prerequisite or
  Karma bonus cannot silently erase existing allocations or purchases. Innate
  metatype qualities are not charged; dwarf Toxin Resistance cannot be rebought.
  Names follow the labelled German source; controls and review are DE/EN/ES.
- Five rated quality families extend the partial catalogue. Core supplies
  total/innate/purchased levels, exact per-level costs and the final-Willpower
  floor for Glass Jaw. Built Tough upgrades charge only above innate ork/troll
  levels. Choosing another level explicitly replaces the same family, even
  when all six choices are filled, and invalidates the old confirmation.
- Separate equipment basket with Core-owned catalogue, exact Nuyen prices,
  metatype surcharge, availability and remaining/carry-over budget. Search by
  source name or ID, add quantities, edit/remove stable-ID rows and explicitly
  preview/confirm. Controls/validation are DE/EN/ES; item names are explicitly
  the German source catalogue. Licensed/illegal gear is flagged for GM review;
  purchase does not grant a license or activate in-play equipment effects.
- Returning from a saved child refreshes a clean parent from current Core state.
  Unsaved inputs are retained only for the same binding; a conflicting revision
  or lost dirty baseline blocks further editing. Old confirmation/navigation
  callbacks and owner A→B→A transitions remain rejected.
- Basic lifestyle and starting-cash page: six localized choices and whole-month
  prepayment, with Core-calculated joint equipment/lifestyle cash. The projected
  starting balance is capped at 5,000 Nuyen, never rolled or silently discarded.
  Changing choice/months invalidates review. Gear pages show the combined balance
  when a lifestyle is saved, not money already spent on prepaid months.

Further qualities (including other parameterized families and Bilingual),
runtime quality/talent effects, the rest of the equipment catalogue (including
ammunition, accessories, bodytech, SIN/license assignments and vehicles), custom
lifestyles, SIN-linked lifestyle taxes, finalization and Career entry are still missing. Point Buy pool purchases are a partial draft,
not a completed creation method, even when all CP are spent. Life Path and
optional Karma still need their own rule implementations and native flows.
This does not enable SR6 Origin generation or audiobook conversion.

## Current lifestyle increment — 22 September 2026

Core `eee964d79`; Presentation `1eae38aa1` unchanged. Six basic monthly
lifestyles come from the owned German 2024 core p59; starting cash comes from
p70. There is no SR5 cash roll, permanent/custom lifestyle, SIN-linked tax or
recurring Career payment in this editor. Gear and lifestyle share Core's exact
resource/Karma-cash budget; changes cannot keep an overdrawn saved lifestyle.
The 5,000-Nuyen starting-cash ceiling is a projection, not silent disposal.
Nullable new fields preserve previous decision and gear-quote bytes. When a
lifestyle exists, both native pages display its combined remaining balance.

`core-sr6-lifestyle-1.log`: **261 focused Core tests PASS**.
`sr6-native-lifestyle-1.log`: **166 managed native scenarios PASS**, including
nine new three-method × DE/EN/ES cases for selection/months, malformed input,
shared budget, stale confirmation, departed edits and unchanged saved reopen.
That run emitted one test-only nullable warning; a subsequent null-forgiving
annotation corrects it without changing executable behavior. The existing
allocation localization test now recognizes the SR6 prefixed helper (including
Build-page captions) rather than reporting its entire catalogue as unused;
**612 keys pass** parity, formatting, fallback and source checks.

Keyless local Debug x64 build: **zero warnings/errors**, 1m44.85s, in
`sr6-lifestyle-debug-build-1.log`. `sr6-lifestyle-debug.apk` SHA256:
`1d33f363aeccfed5694b40994640c6929103daec694b7776b9bbc1708e7ad449`.

Actual API36 UI smoke reused the old equipment draft at14/14. Selected Low,
entered2 prepaid months, reviewed4,000¥ lifestyle +1,800¥ equipment against
10,000¥ resources, and confirmed once: **15/15, fourteen decisions**.
Remaining/projected starting cash is4,200¥ with no excess above carry-over.
The prior thirteen decisions, all other choices, gear quote and document
envelope compare unchanged. Force-stop removedPID4854; newPID5433 reopened
the actual lifestyle editor with saved Low/2 and no historical Confirm.
Save and post-restart page copies are byte-identical, SHA256:
`40b1a4a5264ac55b7756c0e0f6b8adc6d6c7dc2a05c7e6320e620cec81c22cbb`.

Packet `sr6-lifestyle-*` retains APK, PNG/XML, JSON and logs. Emulator cold-boot
system ANRs/Bluetooth crash occurred before app testing; no Chummer ANR/crash
was observed. The initial immediate post-launch hierarchy read was empty;
the resumed Activity and a fresh rendered hierarchy were then verified.
Owned emulator and temporary containers are stopped. No release AAB, upload
key, main merge, package seal or Play operation is part of this increment.

## Historical equipment increment — 22 September 2026

Core `ec568da7b`; Presentation `1eae38aa1` unchanged. The new basket supplies
148 source-backed item/rating choices for Priority, Sum-to-Ten and Point Buy.
Core prices quantity and size surcharges using decimal Nuyen, includes saved
Karma conversion, enforces availability ≤7 and refuses overspending. Licensed/
illegal flags require review, not automatic licensing. The catalogue remains
partial and has no equipment runtime/finalization authority.

Native add/edit/remove/search uses stable IDs and Core quotes. Malformed
quantities return equipment-specific feedback, not the old generic build-method
error. Obsolete confirmations and departed controls cannot write. A changed
foundation explicitly resets the basket with other dependent allocations.

`core-sr6-gear-1.log`: **254 focused Core tests PASS**.
`sr6-native-gear-2.log`: **157 native managed scenarios PASS**, including nine
new equipment scenarios (three methods × DE/EN/ES), exact troll decimals,
availability, malformed quantities, stale confirmation and saved reopen.
The first native run exposed only the wrong error message for rejected
quantities; the coordinator now forwards the Core gear shape blocker.
After that managed run's compilation, six punctuation-only localization edits
were compiled in the final keyless Debug APK; the logic/tests were unchanged.
Both local Debug builds had **zero warnings/errors** (1m43.08s, then 55.86s).
Final artifact `sr6-gear-debug-final.apk`, SHA256:
`ab17f3399fa02f2ba31cc7e4bc4eff0eb98047657e9911e1e0a1c83330c02b92`.

Actual API36 smoke used the existing mixed-domain Point Buy draft at12/12.
Through the Karma UI, converted5 Karma→10,000¥ and saved13/13. Through the
new basket, searched `lined-coat`, selected quantity2, reviewed1,800¥ spent /
8,200¥ remaining /3,200¥ above carry-over and confirmed once: **14/14, thirteen
decisions**. Prior history, other selections, Karma and CP compare unchanged
across the equipment save. Force-stop removedPID4817; newPID5784 restored the
same rowID, quantity, budget and editor, without a historical Confirm button.
The saved workspace and post-restart page-reacquisition copy are byte-identical, SHA256:
`dc58329e9d84f873837b57b7b98754a849a4e6a653ba375e93d24af8db0a17ab`.

Local packet `sr6-gear-*` retains APKs, screenshots, hierarchies, workspace
copies and logs. System cold-boot ANRs plus UIAutomator/Bluetooth failures
preceded app testing; no Chummer ANR/crash was observed. The owned emulator
and temporary containers were stopped. No release AAB, upload-key use, main
merge, package seal or Play operation is part of this increment.

## Historical contact increment — 22 September 2026

Core `80f803ca4`; Presentation `1eae38aa1` unchanged. The native Contacts page
provides stable-ID add/edit/remove, optional roles, Core-provided rating ranges,
separate budget/cost review and explicit confirmation. All controls and feedback
are DE/EN/ES. Names/roles still need GM review. Dirty inputs invalidate old
confirmation; departed controls cannot mutate. Foundation changes explicitly
reset contacts with the other allocations; attribute/Karma changes revalidate.

`core-sr6-contacts-1.log`: **242 focused Core tests PASS**.
`sr6-native-contacts-1.log`: **148 native managed scenarios PASS**, including
contact add/edit/remove, budget/CP separation, stale confirmation and saved
reopen for three methods × DE/EN/ES. The keyless local Debug x64 build passed
with **zero warnings/errors**, 1m37.99s (`sr6-contacts-debug-build-1.log`).
Retained `sr6-contacts-debug.apk` SHA256:
`1e86c414be4ce8042eb030a83bf491a0e00dc5e38e3c45cee0c0b4353bf09f8a`.

The API36 emulator smoke restored the previous mixed-domain draft at 11/11,
added Mira/Fixer with Connection1/Loyalty1, reviewed 2/6 contact points and
confirmed once: **12/12, eleven decisions**. Existing history and all earlier
selections/Karma/CP were unchanged. Force-stop removed PID4153; PID5021 restored
the same contact, ID, budget and editable page without a historical Confirm.
Save/restart workspace files compare byte-identical, SHA256:
`f01fc0d3eb09709e4083c73c3016e4942f814803500366a99e5caf63c40d5e50`.
The local packet retains `sr6-contacts-*` APK/PNG/XML/JSON/logs. Cold-boot Android
system ANRs occurred before app testing; no Chummer ANR/crash was observed.
The owned emulator and temporary containers were stopped. No release AAB,
package seal, upload key, main merge or Play operation is part of this increment.

## Historical rated-quality increment — 22 September 2026

Core `bb6b32995baea76e58361f6303e2cf9ef499fba3`; Presentation `1eae38aa1`
unchanged. Focused Concentration, Built Tough, Will to Live, Glass Jaw and
Dependents now have Core-owned levels/costs. Built Tough charges only above
innate ork/troll levels; the upgrade still counts as one of six choices. Glass
Jaw is checked against final Willpower and must leave two stun boxes. Other
parameterized qualities remain unavailable; these are still draft choices.
The native picker explicitly replaces the same family instead of appending
another level, including when all six choices are occupied. Review is renewed.

`core-sr6-rated-qualities-1.log`: **234 focused Core tests PASS**.
`sr6-native-rated-qualities-1.log`: **139 native managed scenarios PASS**,
including all three methods × DE/EN/ES for free-level pricing, replacement at
six choices, stale confirmation, exact save and reopen. The unchanged old
nonrated selections retain their serialized bytes and historical digests.
`sr6-rated-qualities-debug-build-1.log`: local keyless Docker Debug x64 build,
**zero warnings/errors**, 1m42.07s. Retained `sr6-rated-qualities-debug.apk`:
SHA256 `6415961c107ecf51693f9c1a25dd560ff8538ef8ce36c2995cda8f7ea24652ff`.

Actual API36 emulator smoke loaded the prior quality draft at revision10/10
(nine decisions), added Built Tough3 then explicitly replaced it with4 before
confirmation. Review showed three qualities, 19 positive/10 negative Karma,
**41 Karma budget with unchanged18 spent**, 38CP, Magic3, 2PP and two formulas.
One confirmation saved **revision11/11**, ten decisions. Force-stop/relaunch
**PID4782→5619** restored level4 and the same budget, without an old Confirm.
Whole workspace JSON was byte-identical before/after restart, SHA256
`035af71eae1454d6ab882fa70efaddbd8b8a4c051bb6ba64439cc654b68a104c`.
Local packet `sr6-rated-qualities-*` retains screenshots, hierarchies, workspace
copies and event logs. No debug-app ANR/crash was recorded. Cold-boot system
ANRs occurred before app installation; those are not passing app evidence.
Owned emulator and test/build containers stopped. No main merge, package seal,
upload-key use, release AAB or Play update follows from this feature increment.

## Historical selected-quality increment — 22 September 2026

Core `b54d4b779072bfa4a55c7aaed4493b5e88625b23`; Presentation remains `1eae38aa1`.
The separate quality page is reachable before pool allocation from a saved
foundation. It renders Core catalogue costs, source anchors, eligibility and
review totals. Changing inputs invalidates confirmation; saving preserves
other allocations and Karma purchases or blocks the incompatible new selection.
The partial catalogue has 66 variants: 39 fixed choices, eight Exceptional
Attribute targets and 19 Aptitude targets. One Aptitude family is admitted.
This is not full quality coverage or activation of situational effects.

`core-sr6-qualities-2.log`: **216 focused Core tests PASS**; the three mixed-domain
cases in `core-sr6-qualities-combined.log` also pass, including eleven anchors.
`sr6-native-qualities-1.log`: **130 native managed scenarios PASS**, including
three methods × DE/EN/ES for this page, stale-confirm rejection, eligibility,
preserved purchases, overspend after removing a bonus and cold reopen.
The subsequent Point Buy label-only clarification distinguishes base Karma
from the quality-adjusted budget; resource XML/diff checks and the actual APK
below cover that wording. No broad suite or hosted run was started.

`sr6-qualities-debug-build-1.log`: local keyless Docker Debug x64 build,
zero warnings/errors, 1m39.41s. Retained `sr6-qualities-debug.apk` SHA-256:
`f7e658ba1f87098f3c7b9149518c1da252207c1269299f530ef0e18b8352c4ba`.
This is an explicit local source assembly and debug signing, not a package seal
or use of the Play upload key.

Actual API36 smoke reused historical `sr6-karma-knowledge-after-restart.json`,
revision 9/9 with eight decisions, not a fresh creation claim. Real UI added
Analytischer Geist (3 Karma) and AR-Desorientierung (+10 Karma). Review showed
two of six qualities, net bonus 7/20 and **57 Karma** before other purchases;
existing purchases stayed **18/57**, with 38 CP/Magic3/2PP/two formulas unchanged.
One confirmation saved **revision10/10**, nine decisions. Force-stop/relaunch
changed PID **4605→5183**. Reopening the quality page restored both selections,
budget and revision without a historical Confirm button. Entire JSON bytes
before/after restart share SHA-256:
`47417b8d147e152da096de664acf518d99e91b2a922723068853956152db1b19`.
The local packet retains `sr6-qualities-*` APK/logs/screenshots/hierarchies/JSON.
No debug-app ANR/crash was recorded; cold-boot system ANRs predated installation.
The owned emulator and build/test containers were stopped afterward.

Still missing: other quality families, equipment, finalization and Career;
full Life Path/optional Karma flows. No main merge, release AAB, Play upload,
physical-device proof, SR6 Origin generation or audiobook conversion is claimed.

## Historical Karma knowledge/language increment — 22 September 2026

Core `346f7fdcfae46a8b1a8ae80c7a0881f85686a955`; Android functional
`7f61e580ca11b11056fbd943b1f57c08375727ad`, tree
`01e03f34a2be53eca9b1e99bf96598f97ce15a68`; Presentation remains `1eae38aa1`.
The owned German core pp70/72/100 supplies three-Karma knowledge topics and
three Karma per added language level, up to Expert. Base pool/native choices
are not charged again. Core validates the exact existing language ID/name,
target level, cumulative cost, duplicate names and dependent free-pool budget.
Changing inputs invalidates review; departed controls cannot mutate the draft.

`core-sr6-karma-knowledge-1.log`: **194 Core tests PASS**, plus both mixed-domain
anchor-cap regression cases in `core-sr6-karma-knowledge-combined.log`.
`sr6-native-karma-knowledge-1.log`: **121 native managed scenarios PASS**,
including Priority/Sum-to-Ten/Point Buy in DE/EN/ES, combined budget rejection,
duplicate/native-language conflicts, stable IDs, stale review and departed
callback rejection. The two extra Core cases exercise the mixed-domain anchor
limit with and without Karma knowledge; they are not additional full-suite runs.

`sr6-karma-knowledge-debug-build-1.log`: local keyless Docker Debug x64 build,
**0 warnings/errors**, 1m37.81s. Explicit source assembly, not a package seal.
Retained `sr6-karma-knowledge-debug.apk`, SHA-256:
`e3c5e0bfa62b285489180ee16e2a3064a0118796064e2c4cd81f471a5bb49b60`.

Actual API36 smoke reused `sr6-powers-after-restart.json`, revision 7/7 with six
decisions, workspace `1924cafdb6ab458cbc105902fe931131`; it is not a fresh
bootstrap or all-method completion test. The real Knowledge page saved English
native and Spanish Basic with its one free pick (revision 8/8). The Karma page
then added Seattle gangs (3), Spanish Basic to Expert (6), and new German Expert
(9). Review showed **18/50 Karma**, the separate one-pick baseline, and unchanged
38 CP, Magic 3, 2 PP and two formulas. One explicit confirmation saved revision
**9/9**, exactly eight decisions. The topics remain GM-reviewed player text.

Force-stop removed PID 4357; relaunch created PID 5598. Restored Foundation and
Karma showed revision 9/9, the 18-Karma saved budget and the same topic/language
IDs without a historical Confirm button. Entire workspace bytes were identical:
`d8e168ccf139c699d92920244cc438f8c6661dd058b28b2eb34ba3c58cdcb9bf`.
Retained `sr6-karma-knowledge-*` screenshots, fresh hierarchies, JSON and logs in
the existing local packet. The first post-launch null hierarchy was rejected;
subsequent observations established restored state. No debug-app crash/ANR
appeared in captured events; cold-boot system-process ANRs were separate.
The owned emulator and local build/test containers stopped. No main merge,
package reseal, upload-key use, release AAB or Play update occurred.
This remains a partial creation draft, not a finalized runner or Play delivery.

## Historical Karma-specialty increment — 22 September 2026

Core `a1e10ae05` adds five-Karma specialization purchases from the owned German
2024 core pp66/72/94/97. Normal skills allow one specialty across pool and Karma;
Exotic Weapons allows multiple distinct weapon types without the ordinary +2
bonus. Core requires a positive final rating, including a rating purchased in
this same preview. Earlier saved drafts without specialty purchases retain their
canonical bytes and reopen under the updated rules.

Rule correction: expertise and Karma-bought spells/rituals/complex forms are
prohibited during Creation, not merely unfinished features. The DE/EN/ES help
now says so. Qualities, Karma knowledge/languages, equipment and finalization
remain open. No source prose or equipment identity is invented from player text.

The first emulator confirm exposed the old eight-source-anchor ledger limit.
The existing mystic-adept draft needs nine anchors with this purchase, or ten
with knowledge. The write was rejected and all revision-8 bytes stayed intact.
Core `738b672d18d8ecc60cada66f0bb9a36844d5111d` fixes the bounded shape limit;
exact source/digest re-evaluation is unchanged. The case first failed in a
focused regression, then all **187 Core tests passed** in
`core-sr6-specializations-anchor-fix-green.log`.

Android functional `e30f394904f0c50df5e52d987ff333244fb12333`, tree
`dfb76bc790764b4a8401150f0d3dc9bdd65aeb56`; Presentation `1eae38aa1` unchanged.
`sr6-specializations-debug-build-2.log`: local keyless Docker Debug x64 build,
**0 warnings/errors**, 1m25.68s. Explicit source assembly, not a package seal.
Retained `sr6-specializations-fixed-debug.apk`, SHA-256:
`b4dcb0f1752a44b821ebca210aaab35b77e0bfa1ff16d1eff86faceb5883c4fa`.
`sr6-native-specializations-anchor-fix.log`: **112 native managed scenarios
PASS** against the fixed Core, including all three methods in DE/EN/ES,
combined specialty limits, zero-rank/duplicate rejection, budget overflow,
departed callbacks, stale confirmations and saved specialty reopen.
Actual API36 smoke used the unchanged historical Karma draft, revision 8/8,
seven decisions (`sr6-karma-after-restart.json`); not a new bootstrap proof.
The failed first candidate's rejected write left those bytes unchanged. After
installing the corrected debug APK, the real Karma page added AstralCombat,
reviewed **50/50 Karma**, and saved once to **revision 9/9**, eight decisions.
Body 2, Magic 4, Astral 2, 10,000 Nuyen, 3 PP, two formulas and 38 spent CP
remained intact. The specialty costs five Karma and displays +2 with GM review.

Force-stop removed PID 5756; relaunch created PID 6249. Reopened Karma showed
the saved budget, specialty and revision, with no historical Confirm button.
The entire workspace stayed byte-identical before/after restart:
`d33a3731e294574dab538f896ddc50a9f46cc1790f4b4a29d58ba384008b5598`.
Retained `sr6-specializations-fixed-*` screenshots, hierarchies, JSON and logs.
No debug-app crash/ANR appeared in captured events. Initial loading screens
were not accepted as restored-state proof. The owned emulator and local
build/test containers stopped. No main merge, package reseal, upload-key use,
release AAB or Play change follows from this increment.

## Historical customization-Karma increment — 22 September 2026

Core `fdbc460a08a16685addb3c3391ddd3257430dbdf` supplies rules from the owned
German 2024 core pp69/71–72/158 and Companion pp30–31. Attribute and active-skill
improvements cost five times every new rank, cumulatively, and still observe
creation maxima. Cash costs one Karma per 2,000 Nuyen. Unspent Karma is retained
in drafts; amounts above the five-Karma carry-over cap are visible. The step
does not implement qualities, further specialties/expertise, purchased knowledge/
languages or Karma-bought formulas. This is not the optional Karma build method.

UI preserves the exact rendered binding, clears stale confirmation on edits,
and requires explicit review/save. It never computes rule costs. Magic increases
can grant adept/mystic power points without rebuying CP; they do not enlarge the
earlier free or CP formula entitlements. Removing a prerequisite or increasing
an earlier base allocation recalculates dependent choices instead of dropping
them or silently reusing the old quote. Partial creation and finalization remain
separate; no release or completed SR6 creation claim follows from this step.

Android functional commit: `3928e07b1e4017b278413435a1169fe4ce8b4b27`, tree
`b4fc2a5f09281f4a75eb530dc08d526c84faa86f`.
Presentation remains `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
`core-sr6-karma-1.log`: **179 Core tests pass**.
`sr6-native-karma-1.log`: **103 native managed scenarios pass**. Nine new page
scenarios cover all three methods in DE/EN/ES, duplicate targets, unavailable
ratings, first exotic subject, overspend, stale confirms and cold reopen.

`sr6-karma-debug-build-1.log`: local keyless Docker Debug x64 build,
**0 warnings/errors**, 1m46.84s. Explicit source assembly, not a package seal.
Retained `sr6-karma-debug.apk`, SHA-256:
`0799095611896675106397f013b1ce519ab29480e763a685dcb927e8c0d4617a`.

Actual API36 smoke seeded unchanged `sr6-powers-after-restart.json` into the
isolated debug app before first launch. Workspace
`1924cafdb6ab458cbc105902fe931131` began at revision 7/7 with six decisions;
this is not a fresh New-runner proof. Through the real Karma page, Magic +1,
Body +1, Astral +1 and five Karma for cash reviewed as **45/50 Karma**,
**5 remaining**, **10,000 Nuyen**, Magic 4 and one additional power point.
One confirmation saved revision **8/8**, exactly seven decisions. Returning
to the power page showed **1.5/3 PP**, with existing powers and formulas intact
and unchanged 38/62 CP and 16-CP purchased-power cost.

Force-stop removed PID 4742; relaunch created PID 5793. Reopened foundation and
Karma pages retained revision, choices, cash and budget without a historical
Confirm button. Entire workspace bytes were identical before/after restart:
`17598fdd7f69b6d82c71c44f779ff47ff062cdf01c15157fd14a924c0e91b60e`.
The existing local packet retains `sr6-karma-*` logs, screenshots, fresh
hierarchies and workspace copies. Cold-boot system ANRs predated installation;
Launcher/System UI dialogs were closed. Two post-restart null-root observer
results were rejected; screenshots and resumed Activity showed the rendered
page, and fresh reads then succeeded. No mutation was replayed, and captured
events contained no debug-app crash/ANR. Owned emulator and build/test containers
were stopped. No main merge, package reseal, release signing or Play upload.

## Historical adept-power selection increment — 22 September 2026

- Android functional commit: `92f18881c5608416f2c5bd6e941c5d27de5d57e6`.
- Core: `f1d46fa86`.
- Presentation unchanged: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Local keyless Docker and explicit source roots, not a sealed package release.

`core-sr6-powers-1.log`: **167 Core tests pass**, including all 51 options,
both adept types, three build methods, exact quarters, natural-rating and Magic
limits, combat scope, Astral prerequisites, forged projections and cold reopen.
`sr6-native-powers-1.log`: **94 native managed scenarios pass**, including
18 new power-page scenarios across both adept types, all three methods and
DE/EN/ES. Actual parent/child navigation, unavailable skill levels, duplicate
rejection, stale confirms, fractional costs, no duplicate CP charge, departed
controls, saved choices and Astral availability are checked.

Rules follow the owned German 2024 core pp95/158–160. The
[official FAQ](https://shadowrunsixthworld.com/shadowrun-sixth-world-faq/)
clarifies mixed-use Improved Ability pricing; the scope is visible and bound
by Core, not chosen implicitly by Android. German-profile Magic-linked skill
restrictions remain intact. A power selection is not an active bonus or qi
focus, and Improved Reflexes carries the non-stacking warning. Qualities/Karma,
equipment, finalization and full Life Path/optional Karma flows remain open.

`sr6-powers-debug-build-1.log`: Debug x64 APK, **0 warnings/errors**, 1m43.41s.
Retained `sr6-powers-debug.apk`, SHA-256:
`63c9a8a7673a40a77e3384b602d416847c705d89b1d26be40abafb86f5272570`.

Actual API36 smoke seeded the unchanged saved Point Buy mystic-adept fixture
`sr6-spells-after-restart.json` into the isolated debug app before first launch.
Workspace `1924cafdb6ab458cbc105902fe931131` began at revision 5/5 with four
decisions, Magic 3, two purchased power points and Heilen/Hüter. This is not a
fresh New-runner proof. Through the actual power editor, Astral Perception 1
and Mystic Armor 2 reviewed as 1.5/2 PP, 0.5 remaining and unchanged 38/62 CP.
One confirmation saved revision 6/6. Returning to Skills exposed Astral;
assigning rank 1 cost one skill point, left 11 and saved revision 7/7 with
exactly six decisions. Both powers, both formulas and CP costs remained intact.

Force-stop removed PID 4572; relaunch created PID 5826. The immediate PID query
preceded process startup and was not accepted as readiness. Reopened foundation
and power pages retained revision 7/7, both levels and the fractional budget,
without a historical Confirm button. Entire workspace bytes were identical
before/after restart, SHA-256:
`d104c26ac9069cd7cf6b747648efa27a0870538e78e0aef235a002fd27aaa752`.
The existing local packet retains `sr6-powers-*` APK, logs, screenshots, fresh
hierarchies and workspace copies. Emulator system-process ANRs predated app
installation; its System UI dialog was closed. A null-root skill-page read was
rejected and checked against the screenshot and resumed Activity before a fresh
read. No write was replayed; captured events contain no debug-app crash/ANR.
The owned emulator and temporary keyless build/test containers were stopped.
No main merge, package seal, upload key, release AAB or Play upload was involved.

## Historical spell/ritual selection increment — 22 September 2026

- Android functional commit: `6f83c55b`.
- Core: `a8deec4a3`, functional `4ac8a21b1`.
- Presentation unchanged: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Local keyless Docker build against explicit source roots, not sealed packages.

`core-sr6-spells-1.log`: **156 Core tests pass**, filter `Sr6Creation`, including
all 81 catalog entries, shared budgets, aspects, mystic splits, malformed input,
overspend, cold reopen/replay and forged projections. `sr6-native-spells-1.log`:
**76 native managed scenarios pass**. Twelve new scenarios cover all three
methods and Enchanting in DE/EN/ES, actual parent/child navigation, add/remove,
duplicate rejection, stale confirmation, departed callbacks, CP costs and cold
reopen. The subsequent English ritual-help clarification changed no behavior;
it is included in the APK below.

`sr6-spells-debug-build-1.log`: Debug x64 APK, **0 warnings/errors**, 1m36.93s.
Retained `sr6-spells-debug.apk`, SHA-256:
`a0cdcc0c992ca20fe32a88a6f26c684e30e0d0a5ccdce31d807c4da0dc28a440`.

Actual API36 smoke used the unchanged saved Point Buy mystic-adept fixture
`sr6-talent-after-restart.json`, workspace `1924cafdb6ab458cbc105902fe931131`,
revision 4/4. This was an isolated debug-app fixture copy before first launch,
not a new New-runner proof. The existing Magic 3 / power-point 2 budget allowed
two formulas. The visible spell page selected Heilen and the ritual Hüter.
Core review showed two of two slots, no free slots, formula cost 4 CP, total
38 CP spent / 62 remaining and separate 50 Karma. Explicit confirmation saved
revision 5/5 with exactly four decisions.

Force-stop removed PID 4574; launch created PID 5298. Reopened foundation and
spell page retained both formulas, budget and revision without a historical
Confirm button. Entire workspace bytes were identical across restart, SHA-256:
`32dc884e24446005762c1a2acf6d3cf2bdafe8f5dc4929f04587154bac006312`.
The existing local packet retains `sr6-spells-*` APK, logs, screenshots, fresh
hierarchies and workspace copies. Emulator system-process ANRs predated app
installation; closing its System UI dialog cleared the obstruction. Initial
null-root launch/reopen observations were rejected and checked against separate
screenshots and resumed Activity state. No write was replayed. Captured fault
events contain no debug-app crash/ANR. The owned emulator was stopped.

These are draft formula choices, not active spell effects, prepared alchemical
objects, runtime ritual-prerequisite validation or final characters. Adept
powers, qualities/Karma, equipment, finalization and the complete Life Path /
optional Karma flows remain open. No main merge, package seal, upload key,
release AAB or Play upload was involved.

## Historical complex-form selection increment — 22 September 2026

- Android functional commit: `6c26992c`; review-copy correction: `8aa8b365`.
- Core: `b2242d1fe`, functional `18b4e317f`.
- Presentation unchanged: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Local keyless Docker build against explicit source roots, not sealed packages.

`core-sr6-forms-1.log`: **145 Core tests pass**, filter `Sr6Creation`, including
all offered variants, three methods, malformed/duplicate input, subject rules,
budget exhaustion, reduced-Resonance conflicts and forged saved projections.
This is a different subset from the historical 155-test run, not a regression
in its pass count. `sr6-native-forms-3.log`: **64 native managed scenarios pass**,
including nine form flows across Priority/Sum-to-Ten/Point Buy and DE/EN/ES.
Those exercise invalid subjects, duplicate forms, stale confirms, departed
callbacks, correct costs and cold reopen. Run 1 stopped at a test helper that
incorrectly awaited a synchronous Add callback; it is not counted as passing.

`sr6-forms-debug-build-1.log`: Debug x64 APK, **0 warnings/errors**, 1m40.94s.
Retained `sr6-forms-debug.apk`, SHA-256:
`9d119fdd35b5fed1a7c5fcc447a8e8105702f5ad25c2750f91992b3c78d7c970`.

Actual API36 smoke used the unchanged saved Point Buy Technomancer fixture
`sr6-point-buy-after-restart.json`, revision 2/2, workspace
`eb614706663e496c8f34b208bfd9f3a9`. This was a private debug-app fixture import,
not a new New-runner proof. Default attributes saved revision 3/3; the talent
budget saved 4/4. The now-enabled complex-form page selected Editor and Reiniger.
Core review showed Resonance 1, two of two slots, no free slots, 4 CP for forms,
49 total CP spent / 51 remaining and separate 50 Karma. Explicit confirmation
saved revision 5/5 with exactly four decisions.

Force-stop removed PID 4709; launch created PID 5480. Reopened forms retained
both selections, cost, revision and no historical Confirm button. The entire
workspace was byte-identical across restart, SHA-256:
`b7eded78a9151439b1f69b986bfe874d7a7bb93999367ae2caf5ebe28a00a2b2`.
The existing local packet retains `sr6-forms-*` screenshots, fresh hierarchies,
workspace copies, APK and logs. Emulator system-process ANRs/crashes preceded
app installation. Closing its System UI dialog enabled interaction. Transient
null-root reads during initial launch/reopen were rejected; screenshots and
resumed-Activity state distinguished them from product failures. No write was
replayed, and captured fault events contain no debug-app crash/ANR. The owned
emulator was stopped.

The smoke exposed a misleading budget-page-only hint in the shared form
review. A subsequent narrow UI condition shows that hint only on the talent
budget page; `sr6-native-forms-4.log` passes all **64 scenarios**, including the
new hint regression in all nine form flows. The APK evidence above remains
bound to `6c26992c`, not that later copy-only change. The saved
selection, calculation and persistence paths are unchanged.

These are stored draft choices, not active Matrix effects or final characters.
No main merge, package seal, upload key, release AAB or Play upload was involved.

## Historical parent-return correction — 22 September 2026

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
