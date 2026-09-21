# Contact collection — local work in progress, 21 September 2026

Not a release or Play publication receipt. Ordinary pending Contacts pass managed
integration tests and the real Priority Add/save/process-restart/reopen route.
Karma-funded/group-contact contribution and the visible Remove route remain unfinished.

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

The latest installed Debug APK built with zero warnings/errors:
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

The visible Remove route has not yet been exercised; managed Add/Remove/re-add
and Career finalization are green. The actual dashboard still rejects finalization
with `creation-wizard-lifestyles-authority-unavailable`. Do not conflate the
Core finalization integration tests with a completed visible full Creation route.
Android System UI had an ANR during emulator boot, before Chummer started;
it was dismissed once. Some transition-time UIAutomator reads returned empty;
screenshots showed the expected pages and no character action was replayed.

Open implementation work before claiming the full contact workflow:

- Integrate paid group contacts/overflow with the existing shared Karma and
  positive-quality cost rules. The new table-method draft lane currently rejects
  paid group choices explicitly rather than treating them as free; normal pool
  overspend also remains blocked. Karma's existing separate contact path remains.
- Exercise dependency/source-change recovery for already-confirmed Contacts.
- Finish the visible Remove route. Add/save/process-restart/reopen is now proven
  for the APK identified above.
- Connect the pending Lifestyle draft/budget to the actual Priority dashboard;
  do not remove its readiness blocker merely to enable finalization.
- Refresh package seals only when functional changes are stable. Current local
  builds use explicit source roots and are not new sealed-package authority.
- No new release AAB has been signed or uploaded.

## Adjacent concrete UI fix from the smoke

The Gear page incorrectly treated an absent draft as an already confirmed empty
basket and disabled its first review. Core accepts an explicit empty Gear draft
(the actual Contacts integration fixture uses it). `DiffersFromPersisted` now
permits the initial review without forcing an arbitrary purchase. Three focused
Gear tests pass; the actual service fixture also checks that a confirmed empty
basket does not enable another unchanged review. This later fix is not in the
APK used for the Contact smoke and still needs its affected Android build/smoke.

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
