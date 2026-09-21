# Contact collection — local work in progress, 21 September 2026

Not a release or Play publication receipt. New-runner Contact creation remains
blocked and must not be advertised as complete.

## Verified locally

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

## Reproduced product blocker

The shared Contacts/Lifestyles card originally rejected a separately ready
Contacts editor solely because the Lifestyle projection was unavailable. The
local patch admits that editor only with exact Contact authority, an available
stage, and that single sibling blocker. It does not mark the stage complete.

The real new-runner route remains blocked for a separate reason: the Core
Contact evaluator reads `contactpoints` from raw XML, while new-runner choices
are persisted as typed Creation drafts. The raw bootstrap document has no such
value. Even confirming all required drafts does not make the budget available.

`--creation-contacts-bootstrap-content-root <Core/Chummer>` now reproduces this
with the real Bootstrap, Priority, Attributes, Skills, Qualities, Resources and
Gear services. Its Priority case fails with
`creation-contacts-budget-authority-required` and `CanEdit=false`. Sum-to-Ten
is included in the test but was not reached after the Priority assertion failed.

## Next implementation step

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

The temporary emulator was stopped after collecting the failure. Its saved
draft and local diagnostic packet are retained. No new release AAB was signed
or uploaded during this test.
