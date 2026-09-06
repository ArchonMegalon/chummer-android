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
dotnet run --project tests/Chummer.Android.Sr5AfterRunReward.Tests/Chummer.Android.Sr5AfterRunReward.Tests.csproj --no-restore -p:ChummerCoreRoot=/absolute/path/to/reviewed-core -p:BuildInParallel=false -p:UseSharedCompilation=false
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

## Intentionally unfinished integration

No existing page, entry point, DI registration, package pin or sealed proof graph
is changed in this slice. The synchronous Core calls and journal work are sent
off the caller thread by the coordinator; a future native adapter must supply a
thread-safe immutable owner/workspace/activation snapshot. Convert local date and
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
