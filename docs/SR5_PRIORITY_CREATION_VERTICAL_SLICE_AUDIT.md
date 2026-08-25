# SR5 Priority creation vertical-slice audit

Audit baseline: `chummer-android` `1c3b0a4d86a3d91c740c02645902cbb2bdb42f9d`.
Compatibility graph used for the audit and compile proof:

- Presentation: `d276f1d0ed8f76938d26b92389e62676f48acf7b`
- Core: `da43a509b1f41abe70f3426253acd7158352e589`
- Run services: `d29a880f624ec94aabedd0c2901ae8fed2f93ed4`
- Hub registry: `af9a7e19c3bf331e96411dfb8f9e7820a98cab29`
- UI kit: `d51ecd99cf72098d4adc8db0192bff7bf9fd8e61`
- Media factory: `415c8163d3d90b1211e4014fef332bdec6d75f73`
- Chummer5 reference source: `fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3`

Status vocabulary is deliberately strict:

- **Implemented**: a user-facing Android step has typed Core authority, stale-state protection,
  an explicit commit boundary, and focused proof.
- **Partial**: useful authority or UI exists, but the complete stage acceptance contract is not met.
- **Placeholder**: the phone renders generic projected state or an editor, without creation mechanics.
- **Missing**: no dedicated creation step exists.
- **Falsely proven**: a green contract proves only shape/presence while the user path remains absent.

## Acceptance matrix

| Stage | Acceptance for a complete SR5 Priority runner | Status at this commit | Exact evidence or blocker |
|---|---|---|---|
| New runner, settings, build method | Create an uncreated workspace, select source/settings and Standard Priority, then reopen the same revision safely | Partial | Native setup dialog and typed setup/foundation authority exist. The path is still spread across a generic dialog and Build page, and no current end-to-end device receipt proves it. |
| Priority table | Assign the exact A-E multiset once across Metatype, Attributes, Skills, Magic/Resonance, and Resources; reject duplicate/stale rows; show source anchors; explicitly confirm | Implemented for Standard Priority | Core-backed prerequisite state/preview/confirm and Android Priority rows are typed, digest-bound, and auxiliary-state-only. |
| Sum-to-Ten | Assign legal priority ranks whose points total ten and confirm the exact table | Partial | The method and UI entry exist, but this audit found no separate current interaction/device proof. It is not part of the claimed Standard Priority slice. |
| Metatype and metavariant | Choose an allowed metatype/metavariant from the selected Priority row, apply special-attribute points, qualities, and attribute bounds | Partial | The Foundation/prerequisite flow is typed, but downstream Attributes authority currently accepts only base Human. Metavariants and granted qualities are not carried into the supported slice. |
| Magic or Resonance | Resolve Mundane/Awakened/Emerged choice, MAG/RES/DEP, spells/forms/powers, and all Talent grants | Missing | The phone prerequisite deliberately rejects Talent options with active-skill or skill-group grants. There is no dedicated creation stage. |
| Attributes | Allocate normal/special points and optional Karma under metatype bounds and the maximum-at-maximum rule; preview the full ledger; persist a typed draft without changing character XML | Implemented for the currently supported Human/Mundane prerequisite | `CreationAttributesPage`, `CreationAttributeAllocationPage`, and `CreationAttributesPreviewPage` consume only `ICharacterCreationAttributesService` projections. Every adjustment gets a fresh Core preview; confirmation is preview-digest-bound; the receipt verifies `CharacterDocumentChanged == false` and effects pending finalization. Unsupported metatype/Talent inputs fail closed upstream. |
| Active skills and groups | Spend the exact skill/group budgets, enforce group break rules, specialization costs, grants, and maxima | Missing | No dedicated creation stage. Existing post-create editors are unavailable while `Created == false` and are not an acceptable fallback. |
| Knowledge skills and languages | Calculate the creation pool, apply free native language and language ratings/specializations, spend Karma legally | Missing | No dedicated creation stage. |
| Qualities | Add/remove creation qualities with limits, conflicts, grants, and exact Karma ledger | Missing | No dedicated creation stage. |
| Contacts | Create contacts, connection/loyalty, group/type flags, free/contact Karma budgets, and validation | Missing on this baseline | No creation page in the pinned Android source. Authority that may exist in newer adjacent repositories is not evidence for this baseline. |
| Resources and purchases | Select resource priority, calculate starting nuyen, and buy gear, weapons, armor, cyberware, bioware, vehicles, and lifestyles with availability, capacity, essence, and nuyen checks | Missing | Generic post-create editors are not reachable during creation and do not compose the required creation quote/ledger. |
| Identity and story | Capture name, alias, demographic/profile data, portrait and notes without bypassing revision authority | Missing as a creation stage | Some generic editors exist after creation; no composed creation step is wired. |
| Derived validation | Recompute attributes, limits, initiative, condition monitors, essence, nuyen, availability, Karma, carryover, and all cross-stage blockers from the exact draft set | Placeholder / falsely proven | Build renders projected budgets and generic blockers, but there is no whole-build typed validation ledger. Existing green source contracts do not prove these mechanics. |
| Review | Render one immutable review of every typed choice, all budget remainders, warnings, source authority, and the exact revision/digests to be committed | Missing | Attributes now has a stage-local immutable review only. There is no whole-character review. |
| Finalize and save | Explicitly confirm one composed transaction, apply all pending effects once, change `Created` from false to true, save, reload, and prove an atomic receipt | Missing | No composed finalization service/page or device receipt was found. Stage confirmations persist auxiliary drafts only. |
| Career wizards | After finalization, provide the shared add/edit flows independently of build method | Outside this creation slice; partial in product | These flows are intentionally common to all build methods. Several generic post-create editors exist, but their completeness needs its own cross-ruleset audit and must not be used to mask missing creation stages. |

## First coherent user-visible path

The first honest design-review path is now:

1. Create an uncreated runner and choose Standard Priority.
2. Confirm the exact A-E Priority assignment and supported Human/Mundane prerequisite.
3. Open Attributes from Build even while the older generic wizard projector is stale.
4. Allocate one Attribute at a time. Android requests a fresh full-ledger Core preview for every
   candidate adjustment and adopts only an exact, revision-bound projection.
5. Review exact normal, special, and Karma ledgers, then explicitly confirm the preview digest.
6. Reload the atomic auxiliary-draft receipt and return to Build. No canonical character effect has
   been applied yet.

This is a coherent stage and a useful native-phone design sample. It is not a finalized runner.
The shortest route to that claim is: Skills and languages, Qualities, Magic/Resonance, Resources,
Contacts/identity, whole-build validation, then one composed final review/finalize transaction.

## Proof boundary

The Attributes implementation is source-contract tested and compiled in the exact compatibility
graph above, including the native interaction-test executable. It has not received an API 36 device
receipt in this change. No raw XML is parsed or changed by the Android stage; Core remains the only
rules authority and confirmation stores only typed auxiliary state.
