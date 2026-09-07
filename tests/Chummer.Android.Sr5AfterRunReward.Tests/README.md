# Local reward host groundwork

This managed harness compiles the new Android reward model, journal and
coordinator with the **real** Core Contracts, Application and Infrastructure
projects. It has no ambient Core checkout fallback. Supply `ChummerCoreRoot`
explicitly; this developer-only source harness does not authorize a package pin
or replace package-only Android validation.

The reviewed source used for this slice is Core
`759ec86ca390bb56d253dd8a225a91dd0248472b`. Preview, NoAward, preview fingerprint,
date admission, receipt validation and workspace mutation are implemented by
that Core service, not by Android test contracts.

```sh
dotnet restore tests/Chummer.Android.Sr5AfterRunReward.Tests/Chummer.Android.Sr5AfterRunReward.Tests.csproj --disable-parallel -p:ChummerCoreRoot=/absolute/path/to/reviewed-core
dotnet build tests/Chummer.Android.Sr5AfterRunReward.Tests/Chummer.Android.Sr5AfterRunReward.Tests.csproj --no-restore -m:1 -p:ChummerCoreRoot=/absolute/path/to/reviewed-core -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet tests/Chummer.Android.Sr5AfterRunReward.Tests/bin/Debug/net10.0/Chummer.Android.Sr5AfterRunReward.Tests.dll
```

Use the repository's qualified SDK and configured package feeds/cache; coordinate
one compiler slot. The console harness exits nonzero on any failed case.

## Evidence boundaries

- `Program.cs` runs real `FileWorkspaceStore` and file-backed reward journal
  flows, including explicit NoAward, exact confirmed command persistence,
  selected-expense association, duplicate events, source/owner mismatches,
  acknowledgement failures, historical receipt recovery and retained mappings.
- `NewProcessRecoversApplyingWithoutOldProcessProof` starts a distinct OS process
  after a real Core commit whose response was lost. It reopens the existing
  workspace/journal/owner files, resolves by Core Lookup, verifies the original
  command digest and asserts zero Commit calls in the child. Other `Cold()` cases
  only reconstruct objects within the same process and are labelled accordingly.
- The shared owner backend in these file tests is an explicit test adapter, not
  Android Preferences. `JournalOwnershipTests.cs` uses memory-only fault storage
  to test CAS/ownership transitions. Neither establishes physical-phone or MAUI
  Preferences crash durability. The Preferences compile seam intentionally throws
  if accidentally used; no shadow Core DTO or service is compiled.
- `PhoneModelTests.cs` adds 17 model/recovery checks to the 49 coordinator and
  journal cases. It compiles the actual pending phone model and consequences
  handoff. A presenter-reload adapter is substituted; all currency previews,
  writes, lookups and receipt validation still execute real Core. This is not a
  rendered MAUI page, Android lifecycle or device qualification.
- A pre-canceled confirmation leaves the displayed review intact. Cancellation
  during the last read before any journal preparation returns a definite
  rejection rather than stranding an unstarted operation. An existing journal,
  cancellation after preparation, or a lost commit acknowledgement retains its
  original identity for lookup and explicit retry. The tests cover both sides of
  these boundaries and never equate cancellation with rollback.
- A recorded reward cannot enable continuation until the exact saved runner is
  reloaded and Core facts are read again. A refresh failure keeps the receipt;
  recovery does not credit again. Later edits use a fresh snapshot alongside the
  historical receipt, and owner/activation/revision changes invalidate handoff.
- `ColdEntryTests.cs` adds 14 entry/history cases. Journal and shared Career
  owner are inspected under one process gate,
  in the same lock order as transitions. An Applied receipt with an unreleased
  owner is selected for recovery, never as permission for a new reward. Foreign,
  corrupt or mismatched ownership blocks entry without releasing it or exposing
  another owner's history. A read racing a live execution lease fails closed.
- `ResumeRecordedRewardAsync` re-reads entry state before honoring a history
  selection. A newly pending operation takes priority. A recorded selection then
  uses actual Core Lookup and fresh saved facts, never Commit or a new operation.
  Appearance alone performs neither Lookup nor owner release. This observation
  is not a lease authorizing a later mutation; existing CAS/ownership checks still
  govern every confirm, recovery and handoff.
- `RunnerHostTests.cs` adds 13 checks (93 total) over the actual new
  `RunnerSessionCoordinator.AfterRunRewards.cs` partial and
  `RunnerSessionSr5AfterRunRewardHost.cs` adapter. Only the existing presenter,
  owner and surrounding session scaffolding are substituted. These tests use
  real Core commits and saved revisions, but do not execute the actual MAUI
  presenter or the main coordinator's existing navigation methods. They cover
  explicit reactivation, A-to-B-to-A selection, activation-gate races, owner
  changes, cancellation, failed reload and disposal.
- A failed presenter reload does not authorize another Commit or discard the
  receipt. Read-only recovery may clear that error on the same saved selection;
  dirty, busy, closed, wrong-edition and changed-selection frames still block
  reload. Recovery then uses Core Lookup and fresh facts. Post-save shell sync
  must also preserve selection before continuation is exposed.

## Native compile check

The ordinary `Chummer.Android.Native.CompileCheck` project was also compiled
locally with its unmodified source/generated-assets verifier: 237 owned sources,
three project references, zero warnings/errors. That check includes the actual
main coordinator, new partial/adapter and `MauiProgram` composition, not the host
test seams. It caught a missing Infrastructure namespace import which has been
corrected. It is still a neutral dependency compile check, not a full MAUI APK,
Android lifecycle execution or the governed internal-beta build script.

This local next-wave check used UI source `80f72ba18e5587679ec2e8838906b97fc99cda63`
and hosted Core package version `0.0.0-packageplane.candidate.shb7297a346fe81`
via explicit command-line overrides, with existing compiled UI references.
Transient lock files were placed under each project's `obj`; checked-in package
locks, source pins and the qualified Preview 12 graph were not changed. This is
not a new sealed cross-repository authority or a substitute for its locked build.

## Intentionally unfinished integration

No existing page, entry point, package pin or sealed proof graph is changed in
this slice. The reward service is registered against the runtime's existing
workspace store, and the real runner host now captures one immutable state with
a guarded selection generation. Actual selection/import/close intents fence old
reviews; ordinary same-runner presenter reloads preserve the generation. The
synchronous Core calls and journal work run off the caller thread. Convert local date and
time picker values to whole-second `DateTimeKind.Unspecified` **before Preview**;
change only the returned command's confirmation flag before journaling.

Ordinary stale source/auxiliary/revision previews reject before a pending command
or owner is created. Confirmed pre-Begin failure is not evidence of a Core award;
the confirmed intent remains recoverable. An uncertain Applying result, including
a late workspace Conflict, remains visibly unresolved with its original command
and shared owner. Lookup NotFound does not prove rollback, and this slice does not
invent an abandon/rebase authority. The native UI must expose that limitation,
not label it complete or allocate replacement identities. A safe explicit
abandon/review path for such terminal conflicts is follow-up authority work.

Future wiring must recover outstanding reward ownership before other participating
Career routes, re-read the current clean runner after a receipt, then separately
quote/confirm consequences without inferring GM/run authority. Release consumption
still requires Core owner packaging, UI/runtime resealing, authorized Android
repinning, native compilation and fresh device proof. Local managed green tests
do not advance the current published graph.

The model now supports recovery-first selection for Applied/unreleased entries
and explicit recorded-history resumption. The real presenter/selection adapter
is composed; the rendered native page and entry routing are still not wired.
Every new ordinary entry point
must use this observation, not infer clear ownership from non-Applied history
alone. A receipt-backed handoff still does not supply the independent run/GM
approval, policy or Core consequences quote needed for the second transaction.
