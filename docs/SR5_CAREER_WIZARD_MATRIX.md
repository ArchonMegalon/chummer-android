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
| Active Skill advancement | Exact identity/source/rule/logical revisions, Core quote and plan, revision-bound request, expense undo metadata and durable Coordinator save | **Implemented vertical slice:** `Choose -> Quote -> Review diff -> Apply once -> durable receipt` | API-36 save/reopen/process-restart proof and shell integration remain outside this branch |
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
| Undo / correction / recovery | Karma/Nuyen expense field edits; every typed leaf rejects a stale revision | Intent lane exposes exact expense editors. Active Skill persists its reviewed draft locally, resumes only the same exact revision and marks `Applying` before mutation so process restart cannot replay an uncertain attempt | Chummer5 Undo Expense, `Correct this transaction`, shared rebase/discard and recovery for every other action are missing; local checkpoint behavior still needs API-36 process-restart proof |
| Final review / apply | Active Skill Core quote and atomic mutation/persistence authority | Shared typed `Sr5CareerCostQuote` -> `Sr5CareerActionPlan` -> `Sr5CareerApplyResult`, stable route/action/idempotency identity, one-shot policy and clean saved-successor receipt for one Active Skill action | The wrapper is Android-owned because Core has no common Career transaction presenter; no Core-declared atomic multi-action bundle |

## False-proof audit

- The existing API-36 receipts for Active Skill, reputation, weapon fire and
  other leaves prove those individual journeys only.
- A flat list of Build actions is not a Career wizard.
- An alert immediately before a mutation is not a staged review/diff boundary.
- A successful in-memory mutation is not a receipt. The new Active Skill
  receipt is produced only after the Coordinator reports its exact durable
  successor revision and the saved Karma balance matches the reviewed Core
  plan.
- A local `Reviewed` checkpoint can be resumed, but an `Applying` checkpoint is
  deliberately non-resumable. It represents an unknown outcome, not proof of
  success or permission to replay the same expense identity.
- This source slice has no digest-bound API-36 edit/save/reopen/process-restart
  receipt. Release parity remains unproven until that separate device journey
  is captured against integrated bytes.

## Integration hook

The new pages intentionally do not modify `MainShell.cs`, `BuildPage.cs` or a
shared API-36 driver. The integration owner should add one created-SR5-only
navigation row that pushes `new Sr5CareerWizardPage(Coordinator)` from the
career surface, then add a dedicated digest-bound device driver without
weakening existing shared journey gates.

The second advancement checkpoint is blocked specifically on shared Android
orchestration: expose the existing Presentation `Prepare/ApplyCareerSkillGroup`
(or Specialization) methods through `RunnerSessionCoordinator` with the same
durable successor check as Active Skill, then adapt its exact Core quote and
plan into the shared action boundary. The existing generic Attribute edit is
not sufficient proof because it has no stable rule digest, expense identity or
typed ApplyResult.
