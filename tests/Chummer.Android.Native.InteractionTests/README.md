# Native interaction regression executable

This executable references the actual `Native.CompileCheck` assembly and its
Core/Presentation dependencies. It does not link substitute native page or
coordinator implementations. Compile it with the same explicit dependency
roots, package feeds and versions as `Native.CompileCheck`, then execute its
`net10.0` DLL. Passing this development harness does not seal those dependencies.

The current 60 top-level cases contain 58 existing interaction cases and two
wrappers: 14 settlement authority cases and 9 native After Run page/entry cases.
Do not add the wrapper and nested counts as separate independent tests.

`AfterRunPageInteractionTests.cs` instantiates the actual settlement page,
reward page/view, coordinator, entry factory, action gate and checkpoint stores.
It exercises delayed discard confirmation, overlapping actions, departure,
selection/revision changes, replaced/applying checkpoints, and factory routing
for missing/available/corrupt catalogs and mixed reward/settlement recovery.
The pending reward command is prepared by the real Core file-store service;
entering its page must not call the injected runtime service or commit it.

Boundaries remain explicit:

- Presenter state/event interfaces are strict test adapters; unexpected calls
  throw. Unused coordinator dependencies are not initialized in these tests.
- Journal storage is an in-memory/fault adapter, not Android Preferences or a
  process-crash persistence proof.
- The platform alert is replaced by a controlled asynchronous answer. The same
  page action used by the button is executed and native controls are inspected.
- The actual disappearance hook is invoked through reflection; the tests do not
  run full coordinator startup, MAUI Android handlers, Activity lifecycle,
  accessibility, pixels, or physical-device navigation.
- The default production constructors still use platform preferences and
  `DisplayAlertAsync`. Test seams are internal, not a public bypass API.

Full API-36 and device qualification, governed package receipts and Play
publication remain separate gates.

## Local runtime/file-store integration

Pass `--after-run-runtime-content-root /absolute/core/Chummer` to the compiled
DLL to run four additional integration cases after the 60 default cases. The
path must contain `data/`; there is no implicit sibling lookup. Without that
argument the executable explicitly reports that these cases were not run.

`AfterRunNativeRuntimeTests.cs` uses the actual in-process client, canonical
import/save/validation, `CharacterOverviewPresenter`, `ShellPresenter`, native
coordinator, Core reward service and `FileWorkspaceStore`. It verifies a real
SR5 codec-produced document through reward preview/commit, saved-state reload,
explicit recorded-history resume, cancellation on presenter reload, and a
selected-workspace preference failure during native shell synchronization.
Recovery must preserve the original operation, exact character bytes, auxiliary
digest and revision; no second reward is allowed.

The fourth case verifies the explicit saved-reward continuation into the existing
governed consequences page using a real managed MAUI `NavigationPage`. With no
proposal provider composed, it must display an unavailable catalog, not create a
run, approve consequences, reopen a new reward form or credit currency again.
Nested hostile cases reject an unconfirmed reward, foreign/stale/malformed
editors, cancellation, replacement of the verified handoff and A-to-B-to-A
selection changes while a proposal read is pending. Rejected responses must not
push a page. Actual view button events also reject duplicate and departed-page
clicks; their action/query callbacks are explicit controlled test adapters.
These nested checks are not additional independent top-level case counts.

This is the local, non-account-linked runtime composition. Environment settings
and an in-memory platform Preferences adapter are scoped to each temporary
runtime and restored afterwards. The reward journal is file-backed; the shared
Career mutation-owner store still uses a memory adapter. The real presenters
are initialized through import/save/load, but unrelated coordinator startup
dependencies are not initialized. These tests are not Android process-death,
Keystore, account-linking, rendered-page, or full `MauiProgram` startup proof.

Run against a coherent Core build containing the canonical SR5 import-envelope
reward fix. The earlier `b7297a346fe81` package rejects the real imported payload
kind (`sr5/chum5-xml`); do not change the fixture to the legacy `workspace` kind
to make it pass. Any temporary application-assembly overlay used for development
must be explicitly recorded and must not overwrite a sealed feed or claim
package/APK qualification. Hosted qualification needs a newly sealed Core graph.

## Changing the package graph

Changing only the Core version properties is insufficient: both
`Chummer.Campaign.Contracts` and `Chummer.Run.Contracts` carry their own Core
dependencies. Rebuild the affected owner packages from their exact source
revisions against the chosen Core packages. Use distinct development versions
and isolated feeds/caches; never overwrite the bytes of a sealed package or
suppress a NuGet downgrade to make a mixed graph restore.

Check restore success for every referenced project, not only the outer test
project's assets file. After a package-graph change, clean and rebuild the actual
project references, then compare every copied Core runtime DLL with its entry
in the chosen nupkg before running this executable. Normalized package timestamps
can otherwise leave a previous DLL in an incremental output directory even
when restore selects the intended version. Passing tests against that stale
output do not prove the new package graph.

Locally rebuilt owner packages and source-built Presentation still require
their normal package seals and hosted verification. This executable is neither
an APK build nor an API-36/process-death or Play-publication proof.
