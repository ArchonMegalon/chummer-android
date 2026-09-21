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

## Local verification

- Six managed scenarios passed using the real Core interaction/projection and
  file-backed timeline store, with a deterministic decision-authority double:
  terminal reopen, two chapters, cancellation after commit, storage failure,
  corrupt pending state and stale source binding. These are not full rules E2E.
- Localization: 219 keys in three catalogs and five locale cases passed.
- Focused Python runtime/physical-driver tests: 45 passed.
- Debug x64 APK: zero warnings/errors, local keyless Docker build. Separate test
  package `com.myexternalbrain.chummer.originbookdebug`, debug key only.
- APK SHA-256:
  `9cac8735d902def40b04836ac81835e7bfac9965e06804b1ca1728f3a4f5b1af`.
- Explicit source assembly: UI `578f1658e32091944e62ef03a9c02dc252c1bc2d`,
  Core runtime `d1c6e3d22360ce61fd32ed58cb571ac2b50b070d`, Android base
  `8fe4357c543bf319194c2b0b173dde16c8adaa95` plus this source patch. This is
  **not** package-only verification. A package-only native managed compile was
  attempted and failed on missing existing Karma/continuation/After Run types;
  it is not counted as passing evidence.

Local logs/artifact directory:
`/docker/chummercomplete-active-worktrees/life-module-book-tests-20260921.L0D7pON5`.

## Concrete next blockers

The API 36 emulator can create a fresh canonical Life Modules bootstrap and
reach its dashboard. Opening Origin then returns
`origin-dossier-life-module-authority-invalid`: the production adapter requires
an existing metatype, but a newly created runner intentionally has none. This
is a real missing entry transition, not permission to silently choose Human.

Core's production adapter currently stops at `nationality-accepted`. It still
needs the remaining stages/follow-ups, cumulative budget/effect application and
honest finalization into Career. The generic multi-chapter interaction tests do
not prove those production mechanics. First Book AI has not been called by this
increment; no full manuscript or export is claimed.

No upload key was mounted, no release AAB was signed, and no Play upload occurred.
Existing Preview 28 publication evidence remains unchanged.
