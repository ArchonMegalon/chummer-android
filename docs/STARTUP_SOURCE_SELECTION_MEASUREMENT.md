# Selected-runner startup source measurement

The opt-in managed saved-roster harness now persists the requested selection
through the real owner-bound Shell session API before initializing Android.
The Android preference remains a fallback hint; it does not override a restored
canonical Shell selection. This corrects the measurement fixture, not production
navigation or persisted runner data.

The harness retains its existing 20-second initialization and 10-second account
startup budgets. It requires the exact selected owner-bound runner, all expected
open workspaces, usable initial-route state, one shared Core overview, and no
changes to copied or original synthetic workspace files. It records numeric
source counts, content basenames, full-overview SHA-256 and bounded cold-store
read observations, without logging character contents or absolute source paths.
The roster read count is the additional read observed after bootstrap, not a
claim that the entire application startup makes only one store call.

## Controlled local diagnostic

On 12 October 2026, the same nine-runner synthetic fixture, selected Adept,
Android/Presentation harness and content were compared with the old and new
source-built Core resolver assemblies. Both were built in the same Debug
configuration. This private assembly-substitution diagnostic is **not** a sealed
package consumer, a native Android execution, or a distributable artifact.

The Core change skips installed custom-directory discovery only for a profile
with no enabled custom key. Effective source bytes and settings remain validated.
Its source commit is `f4bf073a50636ae4fe00b32b0141019509f64eb9`.

| Last shared source context | Before | After |
| --- | ---: | ---: |
| Initial file reads / XML parses | 68 / 68 | 13 / 13 |
| Validation reads | 4,293 | 718 |
| Validation bytes | 189,323,915 | 183,642,655 |
| Directory validations | 64 | 0 |
| Prerequisite projections | 1 | 1 |

All nine original/copied runner records remained unchanged. Both full shared
overviews had SHA-256
`8d28f9dc66dab4987460ec61f95e42e647c6d81c7a189d0fbb2498a777f4303a`.
The overall times were 2,950 ms before and 2,978 ms after. This demonstrates
fewer unnecessary file validations, **not** an end-to-end startup speedup.

## Verified package intake

The 12 October intake now binds Core runtime `f4bf073a50636ae4fe00b32b0141019509f64eb9`,
Core recipe `e06e2124df4a3c02c64939bba45d0b48cb3e3c1a`, and Presentation seal
`f6321a324f7b590e162856033be1e45d5f16e9af`. The fresh local UI consumer passed
882 runtime tests and its existing focused checks. Its receipt SHA-256 is
`5d716d8157debe01df5757ab90c3be81b68192a92cc9df45ecd6a933844b4b21`.
This is local verification, not a hosted check or source-free Android build.

The Android intake compiled the native compile-check project, MAUI Compile
target and interaction-test project with zero warnings or errors. The existing
cold-restore and failed-read checks passed. The same nine-runner saved-roster
fixture restored the canonical selected runner in 2,731 ms, with all nine
workspace files unchanged and the same complete overview hash above. This is
a managed harness measurement, not a native startup benchmark. All 18 actual
package bytes and the 331 unchanged content files passed the Android verifiers.

## Native saved-runner update and cold reopen

The local API-36 x64 Release/Internal SDK-test build consumed that exact graph
and completed with zero warnings or errors. APK SHA-256:
`ed74569a881ddc25061822693ab8e7bb67dea80ff1322b980c24ec0863d5959d`.
All 331 embedded content files passed verification. The separate SDK-test app
was updated once without clearing its existing nine synthetic runner records;
its temporary UID-only IPv4/IPv6 egress block prevented live provider work.

The selected saved Adept restored into Creation, with the enabled read-only
Continue action leading to Resources and the expected 6,000 starting/remaining
nuyen, zero converted karma and zero known purchases. A verified force-stop
and different process restored the same selection and repeated that navigation
without saving again. All nine files were byte-identical before the update,
after the update and after cold navigation. The state-inventory SHA-256 is
`c02b79c641e4ccb21709d546767f1a336806191e57c93938ea8cc8308dd5a14f`;
the final Resources hierarchy SHA-256 is
`70c3dd79a997aa76b6465be6b3b1dcec7151cf74ea095955189471a179404326`.

The old baseline encountered a SystemUI ANR, recovered after Wait, and competed
with host transcoding. Its shell/selected-restore stages were 36,919/84,118 ms;
the updated app observed 18,598/32,695 ms and the later cold process observed
16,024/40,685 ms. These are confounded observations, **not a controlled speedup**.
Activity launch completion was not treated as runner readiness. Background
projections still delayed the usable next action. Initial hierarchy reads during
navigation returned null; later observations established the real Resources page
without a repeated tap. The original failed observations are retained.

The tested APK has version metadata 198 and a separate app ID, x64 architecture
and SDK test certificate. Preview 199 changes the release version metadata;
its ARM64 AAB, original-key signature and Play result require separate checks.
Slow native startup/reload, physical installation, complete Creation/Career/Origin
acceptance and broader parity remain open. This is not a live provider or
physical Play test, a new save-operation test, or a full wizard qualification.
