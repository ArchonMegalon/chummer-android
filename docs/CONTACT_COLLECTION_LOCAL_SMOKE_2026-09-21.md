# Contact collection — local work in progress, 21 September 2026

Not a release or Play publication receipt. Ordinary pending Contacts pass managed
integration tests and the real Priority Add/save/process-restart/reopen and
Remove routes. Karma-funded/group-contact contribution remains unfinished.

## Package integration after Preview 28 availability

UI #192 passed its protected package and release-control checks and merged to
`578f1658e32091944e62ef03a9c02dc252c1bc2d`, tree
`24e26aa77ecf6d5cedb6c19f77d99dbca3f8ac67`. Android now pins that published
commit, not the superseded review branch. The Core consumer runtime and Hub's
original producer input remain independently bound; no dirty-source exception
was added for the temporary Hub input.

The reviewed tree passed a local cold consumer run without an owner cache.
The published commit passed the existing local consumer verifier with the exact
unchanged owner artifacts and a fresh consumer cache: five builds with zero
warnings/errors,836 product tests and the existing focused owner/persistence
groups. Test compilation retained61 existing analyzer warnings.
`UI_PUBLISHED_CONTACTS_PACKAGE_CONSUMER.json` is79539 bytes with SHA-256
`65540da37d5af6cb4659d853e31cd00be17b84716eb8dd6279a3003eef378d26`.
This second receipt records authenticated local reuse, not another cold or
hosted result. Android's actual intake accepted all18 packages against it.

The Android app/native NuGet lock files are unchanged from the previous repin;
the owner artifact package bytes are identical. The two Career runtime identity
constants now bind the published UI commit; both affected managed harnesses
passed (SkillGroup8, Quality8), as did47 workflow/Career source tests and the
exact dependency-pin source check. These are not fresh device or Play results.

Preview28's original AAB, signing identity and availability observation remain
immutable. This later package integration does not retroactively change its
source graph and did not build, sign or upload a replacement AAB. Complete Life
Modules and live FirstBook AI book creation are still unfinished; the current
Origin adapter only completes its first nationality decision.

## Latest implementation and verification

Core now derives the Contact allowance from the confirmed Priority/Sum-to-Ten
Creation graph and source profile. A new auxiliary Contacts draft and its exact
append-only receipt are checkpointed together; the original character XML and
other Creation drafts remain unchanged. Finalization consumes the contacts into
Career XML and archives their original draft/receipts. Source-policy and quality
authority are rechecked. A rehashed draft without an admitted receipt is rejected.

The real-service native harness passes both Priority and Sum-to-Ten routes:
Add, checkpoint, fresh FileWorkspaceStore/service reopen, replay lookup without
another write, rejection of a forged draft and malformed receipt, Remove,
re-Add, finalization, archive and Career reopen. This is managed integration
coverage, **not** an Android process-restart result.

All twenty pre-existing local/linked owner, ABA, add/remove and ambiguous receipt
cases also pass after moving synchronous Contact Core work off the UI context.
The tests now assert that Load, Preview and Confirm do not execute on the
caller's UI synchronization context. Phone list/edit/review pages retain an
appearance-bound snapshot instead of calling Core from render/toggle handlers.

The Debug APK used for the Contact Add/Remove smoke built with zero warnings/errors:
`c24b1748d30c2fbf430f5cadb6b459be54dbb6dfeb189ccb41b5c91c87af4d93`.
It uses the separate `com.myexternalbrain.chummer.contactsdebug` package.
An additional early prerequisite guard was added **after** this APK build to
avoid source/catalog work while Contacts cannot yet be calculated. It passes
the affected managed build and actual Priority/Sum-to-Ten service tests; it is
not present in these APK bytes.

In the actual API-36 emulator, the existing Priority draft reopened at revision
3. Attributes were explicitly confirmed to 4/4, empty Qualities to 5/5, native
language Skills to 6/6 and one Trodes Gear purchase to 7/7. The Contact list then
offered the source-derived three-point pool. `Draft Fixer` (Connection 1,
Loyalty 1) was added through the real editor, preview and explicit-confirm controls:
revision 8/8, cost 2, remaining 1, raw character XML unchanged.

The process was force-stopped (PID 4048 absent) and relaunched with new PID 7194.
The app restored the selected runner at revision 8. Its Contact list showed the
same stable contact ID, name, ratings and cost. The saved workspace JSON before
and after restart is byte-identical:
`c20667d364098b00bcc7acf520f704e9574175ec8db48676785ffea5f217a031`.
Its one persisted receipt matches the one displayed before restart:
`sha256:26ca83825589423b4a7df85bb4a0e415d11bc8f50436c6458965473c3631347d`.
Screenshots and the two diagnostic workspace copies are retained in the local
`contact-collection-tests-20260921.kBEQ7Qw9` packet, not public release assets.

The same Contact was then removed through the real editor, review and explicit
confirmation: revision 9/9, zero Contacts, the three-point allowance restored,
receipt `sha256:13dc97c611a5c0cd6b549f8f3296b2de6fa8b76a6a1eb8ddb2577fa167dae8a5`.
Its empty draft survived the subsequent Debug APK update byte-for-byte.
The old APK's dashboard rejected finalization with
`creation-wizard-lifestyles-authority-unavailable`. The corrected APK's visible
finalization and process-restart result is recorded separately below.
Android System UI had an ANR during emulator boot, before Chummer started;
it was dismissed once. Some transition-time UIAutomator reads returned empty;
screenshots showed the expected pages and no character action was replayed.

Open implementation work before claiming the full contact workflow:

- Integrate paid group contacts/overflow with the existing shared Karma and
  positive-quality cost rules. The new table-method draft lane currently rejects
  paid group choices explicitly rather than treating them as free; normal pool
  overspend also remains blocked. Karma's existing separate contact path remains.
- Exercise dependency/source-change recovery for already-confirmed Contacts.
- Optional non-empty pending Lifestyle purchases are not proven here. The
  empty-Lifestyle Priority route now passes visible finalization and restart.
- The original local APK builds use explicit source roots. The later package
  integration is recorded separately above and does not reclassify those APKs.
- No release AAB was signed or uploaded during this initial smoke. The subsequent
  Preview28 availability is recorded in `play/evidence/preview28-internal-observation.md`.

## Adjacent concrete UI fix from the smoke

The Gear page incorrectly treated an absent draft as an already confirmed empty
basket and disabled its first review. Core accepts an explicit empty Gear draft
(the actual Contacts integration fixture uses it). `DiffersFromPersisted` now
permits the initial review without forcing an arbitrary purchase. Three focused
Gear tests pass; the actual service fixture also checks that a confirmed empty
basket does not enable another unchanged review.

This fix and the early Contact prerequisite guard were built in Debug APK
`7c9542611a0d37b57a1f05d76a75fccb88f1da6bb35fc950a7176f926970e511`
with zero warnings/errors. A second runner was created through the actual
Priority/Human/Mundane picker and confirmed zero-Karma Resources. Gear then
offered its first empty review, confirmed zero lines/cost, and saved revision
4/4 with receipt
`sha256:34f8e8618ba475bb43d9dc07acf771ccbdac61c9669d0c538b3c8b4194b56016`.
After the next Debug APK update and process launch, its saved workspace remained
byte-identical, SHA-256
`fd43952da35c919cfbf6e30c3f00abe6d8ca9b20531806e426775e81eae6996a`.

## Lifestyle overview wiring correction

Android registered the separate Lifestyle editor but omitted Lifestyle reads
from `WorkspaceOverviewStateFactory`. Core now supplies a read-only
`IOwnerBoundCharacterCreationLifestylesReader`, using its existing synchronous
owner lease and scoped workspace store. Presentation uses the exact displayed
owner stamp and never falls back to unscoped reads for a bound overview.
Android and its real-runtime fixture both inject this reader.

Focused tests cover local and linked reads, same-ID foreign workspace denial,
stale/ABA stamps, an untrusted local-owner name, no writes, no leaked leases,
and the Presentation missing-reader/stale-display no-fallback behavior.
Both real Priority and Sum-to-Ten fixtures now assert the production dashboard
has `CanFinalize=true` before running Contact-consuming Career finalization.
The latest focused run (`native-lifestyles-overview-4.log`) and affected managed
build pass with zero warnings/errors.

The corrected Debug APK is
`9aab1ce4e45957ff0b3b715a44ecce9f9c5c9a2b34d8048946fb9443c7984ea2`
(zero warnings/errors, local Docker). It is installed in the same isolated
Debug package. Core source commit
`edb6ab441`, UI `ea9b6839b`, Android `5f54f3d7` contain the correction.
These are local source commits, not new package seals or Play delivery evidence.

On that APK the original revision-9 runner exposed Core's ready whole-build
review. The user route entered the explicit starting-cash test value 3, reviewed
the complete delta (7 Karma carryover, 60 starting cash, 5,060 total nuyen), and
confirmed once. Core saved Career at 10/10 with receipt
`sha256:764787ca0c8de4ebbe4f8096900ab0c8d89ef9a00e1b412a5e647f368b78a3b5`.
The consumed empty Contact draft and both original Add/Remove receipts were
archived, and no active pending Contact draft remained.

After opening Career, PID 9751 was force-stopped and verified absent. A normal
launcher start created PID 10420 and restored the Career screen with the same
creation receipt. Workspace bytes were identical before restart and after the
visible UI restoration, SHA-256
`f69d652936cceacf151c11aafefeb466a2fc05656cd828f83d9f33eeef8547b7`.
The revision stayed 10/10 and exactly one finalization receipt remained.
The owned temporary emulator was stopped after collecting screenshots and
workspace comparisons; its AVD/drafts remain available for later work.

## Historical earlier verification

- The separate `com.myexternalbrain.chummer.contactsdebug` x64 Debug APK built
  with zero warnings/errors and installed in the API-36 emulator.
- Tested APK SHA-256:
  `260c6e673d71f53b70584094e4af1e5c459108317a761e184f3ba80d095c0470`.
- The actual UI created a Priority runner, selected Human/Mundane and the five
  priority ranks, confirmed the prerequisite draft and a zero-Karma Resources
  draft, and reopened the saved revision 3 after updating the Debug APK.
- Twenty managed Contact owner/add/remove/recovery cases passed. Their fixture
  is an imported uncreated character with an explicit `contactpoints` value;
  those tests do **not** prove fresh-runner Creation.
- Dashboard entry tests passed, including rejecting foreign/missing Contact
  authority and preserving the shared stage's finalization blockers.
- Six Contact source-contract tests passed.

## Original reproduced product blocker (now covered by the draft integration)

The shared Contacts/Lifestyles card originally rejected a separately ready
Contacts editor solely because the Lifestyle projection was unavailable. The
local patch admits that editor only with exact Contact authority, an available
stage, and that single sibling blocker. It does not mark the stage complete.

The original real new-runner route was blocked for a separate reason: the Core
Contact evaluator reads `contactpoints` from raw XML, while new-runner choices
are persisted as typed Creation drafts. The raw bootstrap document has no such
value. Even confirming all required drafts does not make the budget available.

`--creation-contacts-bootstrap-content-root <Core/Chummer>` originally reproduced
this with the real Bootstrap, Priority, Attributes, Skills, Qualities, Resources
and Gear services. Its original Priority case failed with
`creation-contacts-budget-authority-required` and `CanEdit=false`. Sum-to-Ten
is included in the test but was not reached after the Priority assertion failed.

## Original implementation plan

Use the profile-bound contact allowance with the confirmed Creation drafts.
Core now exposes `TryResolveCreationContactsPolicy` for Priority, Sum-to-Ten and
Karma; seventeen focused source-policy/legacy Karma Contact tests passed. The
Karma-specific resolver still rejects the table methods.

This source-policy change alone is not the missing workflow. Contacts must be
saved without invalidating the other drafts' raw-document bindings, survive
reopen, and be included in finalization. Do not insert a guessed XML budget or
relax the existing draft-integrity checks to make the test pass. After that
integration, rerun the bootstrap regression and the actual Add/Remove route
with save/reopen and process restart. Refresh the Core/UI package seals only
after the functional changes are complete.

The previous temporary emulator was stopped after collecting that failure. Its saved
draft and local diagnostic packet are retained. No new release AAB was signed
or uploaded during this test.
