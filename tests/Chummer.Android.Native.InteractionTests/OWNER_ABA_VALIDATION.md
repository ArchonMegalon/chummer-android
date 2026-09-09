# Linked-runner owner-transition regression

This source slice starts at Android draft PR56 commit
`7b529dc135eff015770583fc634cfe8f2221f3c2`. It requires the new canonical Core
`IOwnerContextLeaseAccessor` and UI `IOwnerBoundWorkspaceMutationPresenter`
capabilities. Package pins and release manifests are intentionally unchanged;
this is not a claim that the draft's old published dependency graph is fixed.

An additional review finding requires the new Presentation-owned
`CharacterOverviewState.DisplayOwnerContext` provenance. Android takes that
immutable stamp before the first await, then uses the background owner capture
only to validate it. It never adopts B's freshly captured stamp for a stale
A-owned view. Missing display authority fails closed. The actual Presentation
load/projection producer and same-ID/same-revision cross-owner regression must
be validated together before this slice is considered complete; a nullable
property scaffold alone does not enable the workflow.

That earlier boundary now has an executed negative receipt at
`_completion/chummer-next-wave/android-linked-stale-display-red-20260909.BmayWs/owner-stale-display-red-receipt.json`.
The exact pre-display consumer patch compiled with zero warnings/errors against
the frozen real Core lease, UI writer/dispatch/roaming graph plus a null display
property scaffold. The unchanged regression failed at runtime: A's stale screen
adopted the first B capture, B advanced from revision 1 to 2, and one B intent and
staged file remained. A stayed unchanged.

Final local source GREEN is retained at
`_completion/chummer-next-wave/android-linked-owner-aba-green-20260909.sVMuWt/owner-aba-green-receipt.json`.
The complete frozen Core lease + UI writer/roaming/Session/display-producer graph
compiled with zero warnings/errors. All nine focused owner cases passed, including
the real unchanged-owner control and contact/pet attach/remove ABA cases. The
full linked-runtime entry point also passed its normal mutation, reader lease,
joined cancellation and four historical/concurrent recovery groups. The stale A
view / first B capture left both actual workspace files byte-identical, with no
picker, staged file or journal intent.

The unfiltered native interaction harness passed all 62 top-level tests with
`CHUMMER_CORE_ENGINE_ROOT` set to the exact Core source root. Its initial run
without that required variable stopped at `ResolveCoreRoot`; the preserved v2
run corrected only the environment and reused the same verified binaries. The
14 managed After Run authority and 13 page/dialog cases are nested coverage, not
additional top-level tests. No assertions, source guards or package locks were
weakened.

The preserved negative baseline is the local diagnostic receipt at
`_completion/chummer-next-wave/android-linked-owner-aba-red-20260909.i0DPh9/owner-aba-red-receipt.json`
in the integration workspace. During actual durable intent publication, the old
scope-only check accepted A→B→A and advanced the real stored document from
revision 1 to 2. The unchanged-owner and persistent-owner-change controls passed.

The regression uses actual native, Presentation, runtime, codec and
`FileWorkspaceStore` assemblies. Its controlled test owner implements the real
Core lease interface, with one transition counter and one exclusion gate shared
by that test authority's writer and leases. It is not a production adapter over
`Current`, nor evidence for the Desktop installation writer or physical Android.

The pending action retains its original transient owner stamp across selection,
staging, durable intent publication and shared presenter dispatch. The reader
holds the same live authority's lease only during synchronous store reads and
projection; no lease crosses an await. Original owner stamps are never written
to the intent journal or used as owner paths. Historical observation captures
currently authorized scope and can recover after a later A→B→A transition.

Only a joined typed `NotDispatched` result permits the host to mark an existing
intent abandoned and reclaim that invocation's staged attachment. A dispatched
result or escaping exception retains custody. Cancellation is not rollback.
Successful readback remains an exact current-effect observation, not an atomic
Core operation/idempotency receipt, and does not authorize mutation replay.

Focused executable entry points:

- `--linked-owner-aba-publication-content-root <exact-Core>/Chummer`
- `--linked-stale-display-owner-content-root <exact-Core>/Chummer`
- `--linked-character-runtime-content-root <exact-Core>/Chummer`
- `--tablet-inspector-binding`

Run them only after compiling this checkout against the explicit reviewed
combined Core/UI source graph. The source inventory check and Python build-guard
tests do not substitute for those executable runs. No Android device, process
kill, power-cut durability, package seal or release qualification is asserted.
