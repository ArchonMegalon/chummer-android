# Exact-byte runner read validation — local Android intake

Ordinary Core workspace reads may reuse a successful historical shape check
only for the same owner, workspace ID and full-record SHA-256. The digest is
computed from the same stream bytes consumed by JSON decoding. The process-local
store retains at most 128 validation keys, not mutable character objects.

Every read still deserializes fresh state, secures its path, obtains the lease
and validates revisions, ownership and delegated/local history. Transactions,
strict continuation export and adoption reads retain full validation. Current
rule/source admission is unchanged. A new process starts with an empty cache.
No rules, persisted format, user data or rendering behavior is changed here.

## Exact inputs

- Core runtime: `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`.
- Core package recipe: `b9daf8e559edb8110dfc3ce5a003ae08c540a696`.
- Locally produced Core bundle SHA-256:
  `11cd148294c07a749143ef27222ebf28cc7d7b883953d91d7b838de22b1c8d6d`.
- Actual local UI consumer: `b5653f408d8f4794ad278c334ea965379972c798`.
- UI tree: `1c6fd28b794e29d443317d2317d2578686f196b9`.
- UI package lock SHA-256:
  `bc9b48b8e2484b1f0a75b59865e928aef6fba5cbfd9098dd7a66e93f598c842b`.
- Actual local UI verification receipt SHA-256:
  `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.

The credential-free Core bundle was published once under standing approval;
an independent public download matched its exact bytes. UI preparation PR384
merged normally. UI seal PR385 publishes the identical complete tree as the
frozen local consumer, directly on the preparation merge; its unchanged strict
topology check passes. Local evidence retains the actual consumer SHA and is
not represented as hosted evidence. Required protected-merge checks remain.

## Completed checks

The focused Core checks include warm/cold reads, bounded cache membership,
fresh returned object graphs, same-ID/different-owner separation, ABA/lease
guards, and same-size/same-time receipt tampering. All eight retained finalized
archives rejected corruption on warm reads, cold reads, strict export and write.
All 33 synthetic fixture files remained unchanged. Managed baseline/candidate
restored domain snapshots were byte-identical.

The local Core producer passed 822 managed cases, 18 owner-admission cases,
scoped storage checks and 15 inventory cases. UI's actual producer and consumer
passed: 18 packages, 889 product cases and every selected owner/wizard slice,
including the unchanged 47-case Creation admission slice. Test-project builds
retain existing warnings; no warning-free whole-suite claim is made.

Android's local package intake compiled the affected native source and MAUI
project with zero warnings/errors. Six managed startup cases passed: pending
Priority, Sum-to-Ten, Karma and Life Modules runners, an existing Career runner,
and a failed presenter read that preserves fail-closed startup. These tests
assert restoration without mutation, not complete creation-method coverage.
The final metadata/version repin is subsequent to that managed intake; the
native APK and ARM64 AAB must be built from the final frozen Android export.

All 18 package files and 13 authority artifacts passed exact receipt admission.
All 331 Core rule-content files match, with unchanged rule-data bytes.
The affected source/package/runtime-pin checks passed 132 tests and 799 subtests;
the two exact version/dependency-pin tests passed separately. An initial combined
test collection failed because the reviewed Hub root was not explicitly supplied;
the corrected invocation supplied that root and did not weaken the guard.

Host-only diagnostic selected restore improved from 2.957 to 2.304 seconds;
cold shell was essentially unchanged (1.873 to 1.821 seconds). These are not
native Android speed measurements or a claim that startup is now fast.

## Delivery boundary

Preview156 is the next local candidate, not an uploaded artifact at this commit.
Final-source native save/new-process smoke, isolated existing-key signing,
independent artifact checks and actual Internal readback remain required.
Preview155 remains the last observed Internal artifact and is not rebuilt.
The APK uses explicit pinned Presentation/Core-content roots plus locked runtime
packages; it is not a source-free/package-only APK or an ambient-sibling build.

Full chapters and per-chapter illustrations retain their earlier reader/EPUB
coverage. This change claims no new provider generation, image-continuity proof,
physical Play installation, Windows delivery, full Creation/Career completion,
seven-journey qualification or finished-app readiness.
