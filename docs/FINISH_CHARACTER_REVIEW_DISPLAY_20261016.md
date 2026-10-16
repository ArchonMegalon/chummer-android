# Finish Character review display — scoped handoff

## Current verification — 16 October 2026

The controller compiled both affected managed projects and ran six focused
actual Core/MAUI completion scenarios successfully: `manual-cancel`,
`magic-choice-cancel`, `optional-karma`, `loss-decline`, `loss-owner-aba`, and
`apply`. The compile check had no warnings; the interaction harness retained
four existing nullable warnings. Exact comparisons confirmed that all five
changed code/resource/test files match the tested snapshot.

The scenarios include the new EN/DE/ES display assertions, unchanged input and
projection digests, expand/collapse behavior, inert detached/stale controls,
no save or extra review during disclosure, cancellation, optional Karma,
loss acknowledgement, owner transition rejection, and explicit Apply.
Punctuated names remain readable; distinct named homes retain their identity.

Two earlier failed attempts are retained: the first reproduced the disclosure
refresh defect described below; the second exposed a test adapter that required
an async-void callback from the intentionally synchronous disclosure. The latter
was corrected to dispatch the actual click and drain the UI context, without
weakening assertions or changing product behavior.

This is local managed evidence, not native layout, hosted CI, or Play evidence.
The next check is a newly built exact-source emulator candidate, including
translated/large-text layout and the retained-details interaction. APK09 is only
evidence for the unchanged base. No new release AAB, signing, or upload is claimed.

## Initial implementation handoff (historical)

Inspected Android HEAD: `840c00892dde9d02a2086cf794df8b27168d11c4` (clean before this work).
The `chummer_android_active` vexp mapping points at this exact worktree; the supplied
pipeline and the subsequent scoped discovery pipeline succeeded. No mappings changed.
Read-only context heads: Presentation `e1d348400265156db01473016cf440ba581ad978`,
Core `505bc12ec78b5d35d355b17529cfd77031c56ae0`, local Design
`a574a6522eb08765b7c6efdcb893612980e3306b`.
The supplied Design document has the same blob at that local head and canonical
`193fe9f6067719ae79e38d53fb5592e2ca7a97e0`; these are distinct commits.

Changed: `CreationCompletionPage.cs` review rendering and pure copy/projection helpers;
new review entries in `CreationFlowStrings.resx`, `.de.resx`, and `.es.resx`;
the existing `CreationCompletionPageRuntimeTests.cs`; this note.

The final review groups proposed changes by area, uses admitted names and existing
catalog localization, displays Priority ranks and Sum-to-Ten points, and compares
full graph assignments with original inputs. Retained choices and Career setup
remain inspectable behind a wrapping secondary button. Ratings, quantities,
reviewed total costs, balances, losses and remaining tasks remain visible or
inspectable. Unknown semantics receive explicit unavailable-detail copy, while
safe numeric values remain inspectable. No proposal or Core review is mutated.

Validation performed: the existing finalization/navigation Python source suite
passed all 19 tests. XML checks found all 47 new keys in EN/DE/ES with matching
format placeholders and no duplicate resource names. `git diff --check` passed.
An exact comparison against HEAD verified that BuildAsync, ReviewAsync, ApplyAsync,
all non-display page code, and the existing balances/losses/Apply rendering were
unchanged. Final genericity/accessibility review covered unsupported methods/kinds,
embedded identifiers, specialization values, retained lifestyle structural equality,
explicit before/after and retained copy, and long translated button text.

Managed compilation and runtime tests were **not run by the initial worker**: the
controller owns the heavy slot and retained device14. The existing harness checks the display in
EN/DE, Priority/Sum-to-Ten/unsupported method rendering, original attribute values,
unknown numeric values, embedded identifiers, preserved input/projection digests,
and expanding/collapsing retained rows without review/apply calls or file writes.

Controller next steps:

1. Build `tests/Chummer.Android.Native.InteractionTests/Chummer.Android.Native.InteractionTests.csproj`
   with the retained local toolchain/cache and existing build properties. Set
   `ChummerPresentationRoot` to this workspace's `presentation` sibling and
   `ChummerCoreEngineRoot` to `/docker/chummercomplete-active-worktrees/finish-character-loadout-20261015.Z1IY5hZd/core`.
   Its project reference includes the native compile check and these changed files.
2. Run the resulting `Chummer.Android.Native.InteractionTests.dll` with
   `--creation-manual-loadout-content-root <retained-Core-content-root> <scenario>`
   for `manual-cancel`, `magic-choice-cancel`, `optional-karma`, `loss-decline`, and
   `apply`. This existing switch accepts the completion scenario filter.
3. On a newly built candidate, inspect final review in EN and DE on device14,
   including large text, retained detail expansion, Priority/Sum-to-Ten labels,
   before/after values, all losses, remaining tasks, and explicit Apply/Cancel.
   Controller decides installation and subsequent Apply/cold-reopen verification.

No builds, restore, managed test execution, emulator/ADB/Docker actions, provider
calls, credentials, publication, commits, or pushes occurred in this worker.
APK09 exercised unchanged base code only; it does not validate these edits.
At that initial handoff, managed compilation and native layout/runtime verification
were outstanding; the current checkpoint above supersedes that status. This is a scoped final-review display change, not a playable-loadout or
complete-character delivery claim.

## Retained-details click correction

The controller's subsequent focused build passed with four preexisting nullable
warnings, but `manual-cancel` failed at the real disclosure click: visible-tree
lookup could not find `creation-completion-retained-details`. The supplied receipt
is `../review-managed.kgWCVN6s/build-and-test.log`. That build predates this correction.

Source diagnosis: the disclosure used `AddAction`, which awaits inherited
`NativePageBase.RunAsync`. Its post-action `Refresh()` replaces the just-expanded
group with a new group initialized to `IsVisible = false`. The visible-tree
assertion correctly caught the collapsed replacement; this was not a test setup
failure. The same refresh also retired the previously captured Apply control.

The disclosure now uses a synchronous display-only click handler. It checks the
current page, appearance, render and inputs, control attachment/enabled state,
and busy/applying/leaving guards before changing visibility and localized text.
It invokes no load, save, review, apply or async action wrapper. Wrapping copy,
minimum touch height and existing styling remain; balances, losses, remaining
tasks and actual Apply/Cancel handlers are unchanged.

The focused harness keeps the original expanded-visible-details assertion and
adds actual retained-label visibility, collapse visibility, stable live controls,
detached/stale clicks and post-Cancel click inertness. The existing completion
read probe also covers this interaction; input reads, coordinator state,
Review/Apply counts and saved files must remain unchanged. A deliberate test
refresh retires old controls before the stale-click assertion; only then does
the harness obtain the new Apply control.

This correction worker did not run managed/native tests or build/device commands.
The controller must rebuild and rerun `manual-cancel`, `cancel`, `optional-karma`,
`loss-decline`, `loss-departure`, `loss-owner-aba` and `apply` via the existing
completion scenario switch, then verify expanded/collapsed retained rows with
EN/DE/ES and large text on the native candidate. No changed-route pass is claimed.
