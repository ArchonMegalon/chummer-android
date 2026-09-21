# Life Modules: retained book chapters (21 September 2026)

User priority: finish SR5 Life Modules **including its growing book before
Windows**. Design PR 32 records First Book AI as the default Origin authoring
provider. This change is a bounded persistence/readability increment, not that
complete feature or a Play release.

## Implemented here

- Keep Core-confirmed chapters in the existing timeline store, including terminal
  turns. Finishing the current nationality slice must not delete the story.
- Once Core commits a decision, finish saving the resulting checkpoint even if
  the caller cancels. A failed local write retains the pending preview so the
  same idempotent command can recover without another mechanics mutation.
- Reject corrupt pending checkpoints; only a validated conflict may enter that
  recovery path.
- Reopen a terminal checkpoint in a read-only chapter reader. Live decision
  pages can also open their already accepted chapters.
- Localize reader controls in DE/EN/ES, retain the saved story language, display
  the runner prominently and `chummer.run` as technical author. Draft copy
  explicitly disclaims all-stage completion and actual provider generation.
- Update the existing physical-route observer to require retained chapters
  instead of checkpoint deletion. Its fixture now uses Core's canonical
  `LifeModule` method and Life Modules settings profile, not the Priority profile.
- Admit a newly bootstrapped runner through explicit metatype and nationality
  selection. These are filtered Core-generated composite choices, not an
  implicit Human default or a separate Android mechanics write.
- Convert only the documented Foundation `sha256:` digest envelope to Origin's
  raw lowercase SHA-256 boundary. Reject malformed or unknown digest semantics.
- Move synchronous Origin preparation off the UI thread; reject controls from
  a departed or superseded page and overlapping prepare/confirm actions.

## Local verification

- Six managed scenarios passed using the real Core interaction/projection and
  file-backed timeline store, with a deterministic decision-authority double:
  terminal reopen, two chapters, cancellation after commit, storage failure,
  corrupt pending state and stale source binding. These are not full rules E2E.
- Additional managed tests cover strict digest conversion, explicit metatype
  filtering, hidden previews, stale controls, page departure and reappearance.
- Core's actual Foundation/FileWorkspaceStore/Origin regression passed for both
  an imported Human and a new BootstrapService-created runner selecting Elf;
  the focused Core subset passed 16 tests. It verifies the 15/55 karma deltas,
  explicit confirmation, saved chapter, unchanged character XML, disk reopen
  and byte-identical idempotent replay.
- Localization: 222 keys in three catalogs and five locale cases passed.
- Focused Python runtime/physical-driver tests: 45 passed.
- Debug x64 APK: zero warnings/errors, local keyless Docker build. Separate test
  package `com.myexternalbrain.chummer.originbookdebug`, debug key only.
- APK SHA-256:
  `780f524b0222a8d54d2feb76936e216b90e921b7dd79dc8cd0dd2699c6bb2339`.
- Explicit source assembly: UI `578f1658e32091944e62ef03a9c02dc252c1bc2d`,
  local Core feature `4b72764c2` (on `d0cd52befb5b512de4321a512d6e31eee5b85d6c`), Android base
  `8fe4357c543bf319194c2b0b173dde16c8adaa95` plus this source patch. This is
  **not** package-only verification. A package-only native managed compile was
  attempted and failed on missing existing Karma/continuation/After Run types;
  it is not counted as passing evidence.

Local logs/artifact directory:
`/docker/chummercomplete-active-worktrees/life-module-book-tests-20260921.L0D7pON5`.

## Native API 36 smoke

The isolated debug app now completes this actual user route:

1. New runner, Life Modules, Create runner.
2. Open the Life Modules scene; explicitly select Elf.
3. Preview Native American Nations / Trans-Polar Aleut Nation, total 55 karma.
4. Confirm once; workspace advances from revision 1 to 2.
5. Open the retained chapter reader.
6. Force-stop the app, launch a new process, reopen the same runner and reader.

The process changed from PID 4055 to 5143. The saved checkpoint stayed at
revision 2 with digest
`29b88a9bcb35de7c7a4296aa2a85a4a4a095498bfbedfc66f16af091a71f346b`;
the single chapter digest remained
`41883c20cddd2a942b9d188952d40a47e696ff5c01af1e3ea4a90949c1f33ea7`.
No confirmation was replayed. `origin-book-after-restart.png` and `.xml` in the
local packet record the visible reader. The owned read-only emulator was stopped.

This closes the fresh-runner entry and retained-first-chapter restart blockers,
not all Life Modules, physical-device qualification or provider integration.

## Concrete next blockers

The older import fixture also failed to activate a runner. Updating its method
and settings tuple removed obsolete test inputs but did **not** establish a
passing import: the workspace was stored while the previously selected runner
remained active, with `Workspace verification unavailable`. Do not count either
historical import attempt as a passing import. The successful route above uses
the native New runner bootstrap instead. No user app/data was cleared.

The historical APK above stopped at `nationality-accepted`. Core feature commit
`6513b144f` now connects subsequent source-projected modules to the same atomic
Origin acceptance ledger. Its 68 passing focused tests include real-catalog
multi-chapter decisions, disk reopen and replay without additional writes.
That Core branch is not yet package-sealed or on main.

The Android continuation increment refreshes the workspace and exact budget
after every accepted turn, renders the next scene in the same page, retains the
book action, and invalidates old confirmation controls. Confirmation itself
runs off the UI synchronization context. The managed regression covers two
successive page decisions and rejects another runner's continuation, alongside
the existing cancellation, failed-storage, stale and corrupt-checkpoint cases.
`managed-continuation-3.log` and five focused Python source checks passed;
localization remains 222 keys in three catalogs with five locale cases.

Required follow-up controls, an explicit finish choice, cumulative effect
application and honest finalization into Career remain open. These managed
tests do not establish the current native route. The reader still exposes raw Markdown
and a legacy `$real` story placeholder; these still need presentation/template
handling. The overview budget remains unavailable despite the exact decision
budget. This APK has no live First Book AI connection.

## Separate live First Book canary

With explicit user authorization to spend test credits, the dedicated browser
completed a private synthetic German outline, first chapter and one rewrite.
Tier 5 account balance moved from 25 to 24; no purchase or public publication.
The chapter leaves the next player decision open. The first draft invented
unconfirmed metatype abilities; the rewrite removed those examples but still
ignored the short-length request and retained unconfirmed biographical detail.
Neither draft was adopted into Core or the Android book.

This proves live private chapter generation, not unattended provider integration,
all-stage completion, manuscript export or canonical fact fidelity. Provider
generation took minutes, so the implementation needs a background job and
reviewed narrative result, never a blocking Android call. Existing deterministic
chapters remain readable if enrichment is unavailable. Local readbacks and the
bounded canary notes are in the same packet; no credentials are in those files.

No upload key was mounted, no release AAB was signed, and no Play upload occurred.
Existing Preview 28 publication evidence remains unchanged.
