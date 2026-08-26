# SR5 Career Quality hardcoded-vs-generic audit

Status: complete for the quality slice based on Android `1d2bd7c`, Core
`b7a5f29f5`, and Presentation `37b4f048`.

## Kept reusable

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
