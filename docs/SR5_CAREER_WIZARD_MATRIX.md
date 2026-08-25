# SR5 Career Wizard authority matrix

This matrix describes the native Android state at base
`1c3b0a4d86a3d91c740c02645902cbb2bdb42f9d`, plus the first wizard
foundation in this branch. It is deliberately narrower than Chummer5 Career
parity: a visible route or an individual emulator receipt is not evidence that
an end-to-end Career wizard exists.

| Workflow | Exact typed mutation authority already present at the base | Status after this slice | Remaining blocker |
| --- | --- | --- | --- |
| Karma / Nuyen adjustments | Manual Career Karma and Nuyen plus four typed expense-create operations | **Partial:** exact leaves are linked from After Run | No shared quote/review/atomic closeout result |
| Attribute advancement | Career Attribute `improve` leaf with projected cost and balance | **Partial:** visible blocker, deliberately not presented as a completed wizard | No stable rule digest, expense identity or typed durable ApplyResult |
| Active Skill advancement | Exact identity/source/rule/logical revisions, Core quote and plan, revision-bound request, expense undo metadata and durable Coordinator save | **Implemented source vertical slice:** created-SR5-only Build route and `Choose -> Quote -> Review diff -> Apply once -> fresh typed skill + expense reload -> receipt` | API-36 save/reopen/process-restart proof remains unavailable in this environment |
| Skill Group / Specialization | The coherent compatibility graph contains exact Core and Presentation quote/plan authorities | **Missing Android route:** called out as the intended second path | Shared Android Coordinator has no prepare/apply/durable-result methods on this base |
| Knowledge Skill | No complete Android Career acquisition/advance route found | **Missing** | Exact source/rule quote, expense plan, mutation and receipt |
| Qualities | Generic collection editing exists | **Missing; generic edit is false proof** | Career add/remove/refund prerequisites, cost and expense/undo authority |
| Contacts | Generic contact editing exists | **Missing; generic edit is false proof** | Career add/change costs, loyalty/connection constraints and After Run award transaction |
| Lifestyles | Generic lifestyle editing exists | **Missing; generic edit is false proof** | Career acquisition/change quote, recurring cost and prerequisite authority |
| Gear / weapons / armor | Generic item editors; weapon fire is a separate stable-context live mutation | **Missing acquisition wizard; weapon fire is false proof for acquisition** | Exact availability, legality, capacity, prerequisites, Nuyen cost, expense and stable item identity plan |
| Cyberware / bioware | Exact Cyberware upgrade/sale quote exists with Nuyen and Essence delta | **Partial:** not composed into the hub; acquisition and bioware parity are unproven | Common cost/review/apply boundary, prerequisites/availability, capacity, grades and expense receipt |
| Vehicles / drones | Generic collection surfaces exist | **Missing; generic edit is false proof** | Exact availability, modification slots, prerequisites, Nuyen/expense and stable identity authority |
| Initiation / submersion | No complete Android Career wizard found | **Missing** | Grade, ordeal/echo/metamagic prerequisites, Karma plan and receipt |
| Before Run | Career Edge spend/regain; scattered stable-identity item leaves | Intent lane exposes only exact Edge authority | No typed loadout, healing, ammunition, contact, identity, license or acquisition checklist |
| Live / Playtime | Career Edge; weapon fire from a stable weapon context; condition/damage leaves | Intent lane exposes Edge and refuses to guess a weapon identity | No atomic live-action session transaction; Play dice/notes are local table state rather than Career receipts |
| After Run | Manual Karma, manual Nuyen, reputation and Burn Street Cred | Intent lane exposes the three independent typed editors | No atomic closeout bundle, no typed contact award/change and no typed Heat field in the current authority |
| Downtime | Calendar add/edit/delete; Active Skill advancement | Intent lane exposes Calendar and the reviewed Active Skill slice | No training duration, healing, crafting, acquisition delivery or other planned-work execution contract |
| Undo / correction / recovery | Karma/Nuyen expense field edits; every typed leaf rejects a stale revision | Active Skill uses workspace/owner/action/version/phase CAS with exact write read-back. A globally loaded `Reviewed` checkpoint is hidden and cannot be resumed, abandoned or deleted until the durable local owner, current workspace, created SR5 edition, revision, action, idempotency key, schema and route all match. `Reviewed -> Applying` happens before mutation; restart performs authoritative typed outcome lookup. Verified-not-applied returns to `Reviewed`; verified-applied becomes `Applied`; unknown remains locked and cannot be cleared or replayed | Chummer5 Undo Expense, `Correct this transaction`, shared recovery for every other action and API-36 restart proof are missing |
| Final review / apply | Active Skill Core quote, mutation and persistence authority | Shared typed `Sr5CareerCostQuote` -> `Sr5CareerActionPlan` -> `Sr5CareerApplyResult`; receipt values come from fresh revision-bound skill and Karma-expense projections and verify skill/source/rating plus expense GUID/date/amount/reason, exact `Karma` type, false refund/force-visible flags and presence-aware undo karmatype/nuyentype/objectid/qty/extra | Android owns this single-action wrapper because Core has no common Career transaction presenter; the slice does not claim a Core-declared atomic multi-action bundle |

## False-proof audit

- The existing API-36 receipts for Active Skill, reputation, weapon fire and
  other leaves prove those individual journeys only.
- A flat list of Build actions is not a Career wizard.
- An alert immediately before a mutation is not a staged review/diff boundary.
- A successful in-memory mutation, a save boolean or a client `Atomic` flag is
  not a receipt. The Active Skill receipt is built only from fresh typed
  post-save projections that match the exact saved successor revision, skill
  instance/source/rating and the entire typed Karma expense and undo record.
- `Applying` is neither success nor permission to retry. Only authoritative
  outcome lookup may transition it to `Applied` or back to `Reviewed`; a
  partial/mismatched outcome remains locked.
- This source slice has no digest-bound API-36 edit/save/reopen/process-restart
  receipt. Release parity remains unproven until that separate device journey
  is captured against integrated bytes.

## Integration boundary

`BuildPage` now owns a user-visible created-SR5-only navigation row that pushes
`Sr5CareerWizardPage`; the public Active Skill coordinator rechecks created +
SR5 on prepare, apply and restart resolution. The shared API-36 driver is still
untouched and must capture a dedicated digest-bound device journey without
weakening existing gates.

The isolated authority graph used for this slice is pinned exactly to Core
`5537e99df72e1a8a347269ad3b02b5a2cc2f9da1` (base
`cc3997d5279e9ac7beda595940095f66cfc5366b`) and Presentation
`bd01091df3cdeb889c8336f9b2bf5e07af1c3c82` (base
`d276f1d0ed8f76938d26b92389e62676f48acf7b`). The Android hardening applies
on `40c8ee2995ed88d764be7e15607fbce54c36e53c`; substituting another projection
or contract revision is outside this proof.

The second advancement checkpoint is blocked specifically on shared Android
orchestration: expose the existing Presentation `Prepare/ApplyCareerSkillGroup`
(or Specialization) methods through `RunnerSessionCoordinator` with the same
durable successor check as Active Skill, then adapt its exact Core quote and
plan into the shared action boundary. The existing generic Attribute edit is
not sufficient proof because it has no stable rule digest, expense identity or
typed ApplyResult.
