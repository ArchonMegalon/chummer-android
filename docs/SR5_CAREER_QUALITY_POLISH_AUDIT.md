# SR5 Career Quality hardcoded-vs-generic audit

The original structural audit below used Android `1d2bd7c`, Core `b7a5f29f5`
and Presentation `37b4f048`. It did not establish a working local runtime
capability or complete Career coverage.

## Local runtime integration — 13 October 2026

The native route now consumes the actual owner-bound Core quality capability
through the local Presentation client. It retains the original full owner stamp,
selected-profile rule sources and saved revision. Android's build identity is
separate from Core's exact loaded-runtime identity; a device preference GUID is
not a Core owner identity or an authorization token.

Core writes quality, effects, Karma, expense and receipt atomically. Explicit
correction restores the original retained graph. Recovery reads actual durable
state: an interrupted response never triggers an automatic mutation, including
when correction succeeded before the phone journal could be retired.

Actual Core/MAUI handler tests pass for local and linked owners: selection,
review, one purchase (100→84 Karma), complete saved effect/receipt, reconstructed
readback, explicit managed correction (84→100), interrupted-correction readback,
and owner A→B→A rejection. Eleven phone authority behavior groups also pass.
These are managed tests, not an Android device process-death or Play claim.

Ordinary screens show localized quality names and costs instead of machine
identities. Only implemented effect families and exact eligibility are admitted.
Unsupported prompted/anatomy/variant effects and discretionary free-cost choices
remain unavailable. Owner-managed local editing is not remote GM or campaign
approval. Neither this integration nor the historical audit completes all Career
actions or the entire quality catalogue.

## Kept reusable

The quality chooser displays the quoted **character Karma change**, not the
quality's rule value. A negative quality with no Career grant says “No Karma
change”; purchases say “Spend … Karma”, alongside the before/after balance.
This changes presentation only, not Core pricing or admission. Focused actual
Core/MAUI tests cover the no-grant negative quote and a 16-Karma purchase for
local and linked owners, with English, German and Spanish formatter cases.
The managed test assembly now embeds the Career resources too, so localization
tests exercise the real translations rather than English fallback.

- The phone hub route, `Sr5CareerActionPlan`, typed cost envelope, runner
  revision binding, checkpoint owner/backend seam, CAS token, and
  review/apply/receipt navigation follow the existing Career lane patterns.
- Presentation owns the reusable atomic workspace choreography: fresh project,
  review, one commit, receipt validation, reopen, recovery lookup, and one
  compensating correction.
- No UI path parses XML, recomputes Karma, matches a display label, or invents
  a fallback mutation.

## Kept explicitly SR5 Quality

- Core owns quality operation, positive/negative type, InternalId + SourceId,
  origin/removal legality, prerequisite order, enabled-source and GM gates,
  exact effect families, Karma purchase/refund/buyoff arithmetic, level caps,
  expense/undo semantics, and receipt/correction coherence.
- The Chummer5 serialized values `Karma`, `AddQuality`, `RemoveQuality`, and
  `AddCyberware`, the career double multiplier, and the default level limit are
  named constants inside `CharacterCareerQualityRules`; they are not generic UI
  strings or presentation arithmetic.
- Mentor Spirit Way free-cost eligibility is a typed definition projection.
  The display name `Mentor Spirit` cannot grant a discount.
- Quality recovery remains specialized because it must compare the complete
  quality definition, instance set, affected InternalIds, Karma delta, expense,
  receipt digest, and compensating inverse.

## Integration seam for main polish

This base predates the shared `Sr5CareerRunnerGuard` and cross-lane Career
mutation gate. Do not add another public guard or gate for this slice. When
cherry-picking onto the polished main graph:

1. replace the call to
   `Sr5CareerActiveSkillCoordinator.RequireCreatedSr5` in
   `Sr5CareerQualityCoordinator.RequireCleanSr5` with
   `Sr5CareerRunnerGuard.RequireCreatedSr5`;
2. route the Applying lease and authoritative resolution transition in
   `Sr5CareerQualityCheckpointStore` through the shared cross-lane mutation
   gate; and
3. retain all Quality-specific checkpoint, receipt, recovery-proof, and
   correction comparisons around that shared lease.

The isolated branch intentionally does not define duplicate guard or gate
types.
