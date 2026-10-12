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

The next native measurement must consume this actual sealed dependency graph.
Slow native startup/reload, physical Preview 198 installation, complete
Creation/Career/Origin acceptance and broader parity remain open. No release
artifact, signed bundle or Play upload is produced by this harness change.
