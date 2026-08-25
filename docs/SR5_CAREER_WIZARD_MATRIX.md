# SR5 Career Wizard authority matrix

This matrix describes the native Android phone wizard sources based on
`da4f242652c1192256aed3a6bb2d262b65176a8d`. Active Skill is integrated in that
base; the Attribute slice is pinned to combined Core
`8e2c53bf9c5ac85f675e738bf6e8ecd2ade4bb2a` and combined Presentation
`37b4f048fa50911db7cd493217e1b64005c37770`, which carry the exact Core authority
and tree-identical Presentation authority. This document still claims source
implementation and lightweight static tests only—not a compiled or API-36-proven
Attribute artifact. A visible route or an individual emulator receipt is not
evidence of full Chummer5 Career parity.

| Workflow | Exact typed mutation authority already present at the base | Status after this slice | Remaining blocker |
| --- | --- | --- | --- |
| Karma / Nuyen adjustments | Manual Career Karma and Nuyen plus four typed expense-create operations | **Partial:** exact leaves are linked from After Run | No shared quote/review/atomic closeout result |
| Attribute advancement | Exact typed abbreviation/kind identity; Core quote/plan/receipt with logical, source, rule and receipt digests; Presentation atomic XML mutation plus recoverable receipt ledger | **Implemented source vertical slice:** created-SR5-only `Choose -> exact quote and blockers -> preview diff -> checkpointed apply once -> fresh recoverable receipt -> acknowledgement` | Integrate the exact Core and Presentation authority heads into the combined graph, compile, then capture digest-bound API-36 save/reopen/process-restart proof |
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
| Downtime | Calendar add/edit/delete; Active Skill and Attribute advancement | Intent lane exposes Calendar and both separately reviewed advancement slices | No training duration, healing, crafting, acquisition delivery or other planned-work execution contract |
| Undo / correction / recovery | Karma/Nuyen expense field edits; every typed leaf rejects a stale revision | Active Skill and Attribute use separate workspace/owner/action/version/phase CAS journals with exact write read-back. `Reviewed -> Applying` happens before mutation; restart performs fresh typed outcome lookup. Verified-not-applied returns to `Reviewed`; verified-applied becomes `Applied`; unknown remains a replay-blocking lock. Attribute recovery additionally requires the Presentation receipt ledger to reproduce one coherent Core receipt for the exact transaction | Attribute correction exists in Presentation but is intentionally not exposed by this advancement slice; shared recovery for every other action and API-36 restart proof remain missing |
| Final review / apply | Active Skill and Attribute Core quote, mutation and persistence authorities | Both adapt into typed `Sr5CareerCostQuote` and `Sr5CareerActionPlan` boundaries. Active Skill verifies fresh skill and expense projections; Attribute verifies the fresh Presentation `RecoverableReceipts` projection against every reviewed quote/plan value and Core receipt digest | Android owns separate single-action wrappers because Core has no common Career transaction presenter; neither slice claims a Core-declared atomic multi-action bundle |

## False-proof audit

- The existing API-36 receipts for Active Skill, reputation, weapon fire and
  other leaves prove those individual journeys only.
- A flat list of Build actions is not a Career wizard.
- An alert immediately before a mutation is not a staged review/diff boundary.
- A successful in-memory mutation, a save boolean or a client `Atomic` flag is
  not a receipt. Active Skill builds its receipt from fresh typed skill and
  expense projections. Attribute accepts only one coherent recoverable Core
  receipt whose transaction, typed attribute identity, before/after values,
  expense amount and all reviewed digests match the checkpointed plan.
- `Applying` is neither success nor permission to retry. Only authoritative
  outcome lookup may transition it to `Applied` or back to `Reviewed`; its
  process-bound proof and live owner/workspace/revision binding are checked
  again at the store CAS, and a partial/mismatched outcome remains locked.
- This source slice has no digest-bound API-36 edit/save/reopen/process-restart
  receipt. Release parity remains unproven until that separate device journey
  is captured against integrated bytes.

## Integration boundary

`BuildPage` and the SR5 Career Advancement/Downtime journeys enter the Attribute
choose page. The public coordinator rechecks created + SR5 on prepare, apply and
restart resolution. Phone navigation is deliberately deep: choose, review and
receipt are separate pushed pages; only the review page can move the durable
checkpoint to Applying. The existing direct attribute editor remains a separate
generic leaf and is not proof for this wizard.

The Android integration points at combined Core
`8e2c53bf9c5ac85f675e738bf6e8ecd2ade4bb2a` and combined Presentation
`37b4f048fa50911db7cd493217e1b64005c37770`. Those heads contain the exact
Career Attribute Core authority and tree-identical Presentation authority,
respectively. The Android compile and API 36 device proof against these exact
heads remain explicit release gates. Substituting handwritten Android
calculations or the generic Attribute editor for either authority is outside
this proof.

The next advancement checkpoint is blocked specifically on shared Android
orchestration: expose the existing Presentation `Prepare/ApplyCareerSkillGroup`
(or Specialization) methods through `RunnerSessionCoordinator` with the same
durable successor check as the two implemented slices, then adapt its exact Core
quote and plan into the shared action boundary.
