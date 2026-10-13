# Life Modules finalization leads directly to Career

After explicitly confirming the reviewed Life Modules runner, the wizard now
returns to the root Career screen when the exact saved receipt has been reopened
successfully. The user no longer needs a second `Open Career` tap.

The continuation belongs to the original page appearance, render and navigation
stack. A departed page, changed stack/modal, changed owner, uncertain write or
failed Career restoration cannot redirect the current screen. The once-applied
receipt remains retained; a navigation failure never retries the mutation. The
existing read-only `Open Career` fallback remains for recovery.

## Focused verification

The actual-Core/MAUI regression
`LifeModuleCompletionForwardRuntimeTests.RunLifeModuleCompletionForwardAsync`
fails on the previous implementation because it waits for the extra tap. With
the correction, eight cases pass: current page, departure, departure/re-entry,
changed navigation stack, owner B, owner A→B→A, failed shell refresh and an
uncertain confirmation result. All preserve one finalization and no leaked owner
lease. The successful case exposes the Career screen; negative cases leave the
current navigation alone. Build: zero warnings/errors.

Existing Life Modules page/reader/export, Apprentice Career and exotic-skill
integration routes also pass with their existing state/budget checks retained.
These are managed handler tests, with synthetic story/provider data, not a new
live FirstBook manuscript or native-device acceptance.

The existing test executable accepts
`--life-module-forward-content-root /absolute/Core/Chummer`. An optional second
absolute, nonexistent directory exports the synthetic pre-finalization state
for an affected native smoke. Never replace user state with this fixture.

Native Android finalization and new-process restore, production signing and
Play Internal availability must be recorded separately for the candidate. This
document alone claims none of them and does not establish whole Creation or
Origin completion.
