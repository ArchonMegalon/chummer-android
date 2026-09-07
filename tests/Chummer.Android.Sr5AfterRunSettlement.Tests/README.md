# Settlement checkpoint tests

`AfterRunAuthorityHarness.cs` is shared by two deliberately distinct executables:

- The original standalone `Chummer.Android.Sr5AfterRunSettlement.Tests` compiles
  selected Core rule sources and compatibility stubs. It is not package proof.
- `Chummer.Android.Native.InteractionTests` now also executes the same cases
  against the actual `Native.CompileCheck` assembly and its actual Core package
  dependencies. The standalone `AuthorityCompileStubs.cs` is **not included** in
  that executable. Assembly-identity assertions reject source substitution.

The twelve settlement cases use the actual Core quote/plan/receipt algorithms.
They substitute the presenter and checkpoint/owner storage with explicit memory
and fault adapters; no real workspace transaction, Android Preferences process
durability, rendered page, phone lifecycle or Play qualification is claimed.

The receipt-retirement regression was reproduced against the actual native/Core
assemblies: a failed shared-owner release left an Applied checkpoint, but
acknowledgment could delete the checkpoint and orphan that owner. Retirement
now holds the existing shared unowned gate before the domain gate. Unreleased,
foreign, corrupt and racing ownership prevent deletion, retaining both journals.

Historical receipt display uses a separate `OwnsRecordedReceipt` authority.
Later clean saved revisions of the same runner/owner can display and acknowledge
an already-Applied receipt without granting permission to resume or replay it.
The old exact-revision recovery checks are unchanged and explicitly remain
negative for later revisions. Tests also cover stale CAS, earlier/dirty/unsaved
states, wrong runner/owner/edition, presenter errors and non-Applied checkpoints.

Only the local recovery checkpoint is acknowledged; no settlement method or
workspace mutation is executed by receipt display or retirement. Runtime testing
of the actual entry factory, lifecycle and coexistence with another pending
Career workflow remains required.
