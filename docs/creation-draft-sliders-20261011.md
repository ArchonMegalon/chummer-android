# Creation allocation sliders — bounded implementation and acceptance

Ordinary/special attributes, active/knowledge skills and skill groups use
integer sliders without separate +/- controls on the current allocation pages.
The expandable attribute Karma allocation uses a separate slider for that pool.
Accepted values and
remaining budgets update from one detached Core rules snapshot. A refused value
returns to the last accepted rating with an adjacent explanation. Ordinary,
special, skill-group, knowledge and Karma budgets remain separate.

The full rules-projected range stays visible, even when its upper ratings cannot
currently be selected. Unavailable ticks are grey and struck through, with an
accessible unavailable description; the current number is bold. Affordances
use the same Core draft evaluator without adopting speculative values. A changed
allocation invalidates the scales, including shared budgets and another
attribute already reaching its cap. Unchanged projections reuse cached ticks.
Fixed grants and native-language rows cannot be reduced below their rules floor.

The snapshot is a read-only local draft, not authorization to save. Leaving the
page retires it. Changed owner (including A→B→A), workspace or revision makes old
controls unusable. Explicit Review and Confirm still perform fresh Core checks.
No store/source read or implicit save is performed by a rating adjustment.

MAUI snaps fractional values after its Value setter exits, preventing recursive
change callbacks. Adding a specialization selector at skill rating 1 is deferred
until a drag finishes, so the first point does not interrupt the gesture.

## Focused checks

- Core: detached input/output isolation, parity with the existing evaluator,
  budget refusal, invalid snapshot and historical Skills re-review regressions.
- Actual Core/MAUI handlers: rounding/refusal without replay; active skill
  0→1→2 in one drag; group-budget denial; normal and special attribute pools for
  Priority/Sum-to-Ten; no per-adjustment Core I/O; no cached confirmation
  authority; fresh save, cold store and stale controls after save/owner change.
- Existing affected special-attribute, specialization and review/save cases,
  plus localized source contracts, were retained and adjusted for local drafts.

## Earlier physical-device scope (before slider-only refinement)

A separate local SDK-test Release/Internal APK was checked on an API 36 ARM64
phone. One synthetic Priority/Human/Mundane runner reached Body 4 and Edge 3;
an over-budget Edge input was refused. Aeronautics Mechanic was dragged from 0
to 3, then reduced to 2 with minus. City Speak remained the native language.
One Skills save opened Qualities. A verified new process restored those values,
revision 4, remaining 21 normal/2 special/25 Karma, and 26 active/2 group/4
knowledge points. No second save or provider generation was performed.

This was a private SDK-test graph, not a signed Play candidate. Group-budget and
Magic/Sum-to-Ten checks above are managed tests, not additional physical claims.
The user's original Play 191 installation and saved data were untouched. The
debug connection went offline before removing the separate synthetic test app;
cleanup remains pending. Retained private observations are not a startup timing
benchmark, private-file equality proof or complete Creation/Career acceptance.

Package integration and Play delivery are recorded separately when completed;
this document alone grants neither package nor publication authority.

## Exact package intake

Core runtime c3dcf15e / package recipe c2fa9c64 and Presentation seal21a4a070
passed the local producer, fresh consumer, native compile and allocation-handler
checks. Android consumes those actual packages and retained receipt, not the
private SDK-test package identities. The unchanged rule-data files were checked.
Preview192 is the intended candidate; this is not a Play availability claim.

The first unsigned Preview192 output was marked NOT_FOR_RELEASE before signing
or upload when the user requested slider-only controls. It must not be reused
for this refinement. Version192 has not been consumed; Play191 remains the last
observed Internal version. The new slider-only rendering requires its own native
smoke; earlier phone observations above are not acceptance of that rendering.

The initial fresh restore fetched the public NuGet variant of ILLink10.0.12.
Its bytes differ from the SDK-shipped variant used for Preview191, even though
both report the same upstream source commit. That incidental tool change was
excluded: the existing exact SDK package was retained and both project restores
passed offline with their resulting locks. The Release build must use that same
retained cache; neither variant was relabeled under the other checksum.
