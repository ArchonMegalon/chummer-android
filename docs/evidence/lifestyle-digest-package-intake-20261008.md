# Lifestyle digest allocation intake — 8 October 2026

Preview 150 is a local candidate, not yet a signed or uploaded release. It
consumes Core's bounded allocation reduction for saved lifestyle integrity
checks. The internal digest now reuses the existing canonical streaming helper;
canonical bytes, fresh mutable-input reads, rule replay, owner/revision checks,
constant-time comparisons and persistence semantics remain unchanged.

## Exact inputs

- Core runtime: `d884b1f17025847704c64d4957f87253f9bca31b`.
- Core recipe: `9f44361649bd109f522a6610b41cb9f1def75905`.
- Core local bundle SHA-256: `2b6772b72b4715f29504a09bededad1e0e732943bb65937c3d98e76ea43b7a02`.
- UI seal: `d5a1e0ee16e7cc18e12475a7c7e70e06aa0c9db2`.
- UI tree: `53687a61e89b7842da99e31f58b9cf0d03d5536f`.
- UI lock SHA-256: `cea6ea2692c7a6ff31a15b4f6559091c5e0f0c10815f1bc529f8b527c96f2f84`.
- UI consumer receipt SHA-256: `1ced83afde88cc08c9c93ecc117ea1cda284d6a19943ab8509b7e60414c65227`.
- Owner-cache manifest SHA-256: `ae2f922f16c6e8c2fdc93e9bd950becfad5a7d64c5c692ce9308f236ead888c7`.

The new Core bundle was published as a new, explicitly local-build prerelease
under the standing 25 September approval. Provider metadata and the anonymous
download matched its exact bytes; no existing asset was replaced.

## Verified locally

Core's 50 focused canonical-digest/lifestyle tests pass. The new allocation
regression fails on the previous implementation. An offline read of the eleven
retained synthetic runners preserved their JSON bytes and reduced warm-roster
allocations from 95,204,480 to 66,874,384 bytes (29.8%). Wall-clock measurements
were noisy; this is **not an Android startup-speed claim**.

The exact Core package producer passed 783 product tests and its owner/inventory
checks. UI's exact-seal consumer passed five builds, 879 product tests and all
existing focused groups. The test-project build retains 62 analyzer warnings,
not compiler errors. Verification used a fresh consumer cache and authenticated
reuse of the 18 owner packages and 13 authority files, not a cold owner rebuild
or hosted test result.

Android intake passed the native-source compile, actual MAUI compile and
interaction-test build with zero warnings/errors. The affected startup subset
restored Priority, Sum-to-Ten, Karma, Life Modules and an existing Career runner
without mutation or repeated owner-bound roster synchronization. A failed
presenter read still follows the fail-closed recovery path. Both restored
consumer locks come from that real build; the app version metadata is advanced
after intake for the native candidate.

The exact package-byte and content validators pass. All 331 XML/content files
are unchanged; their provenance envelope advances to the new Core recipe.
The 51 package-authority regressions pass. An initial validator invocation
passed the cache parent instead of its `packages` directory and was correctly
rejected; the corrected invocation passed without changing the validator.

Another 124 affected build, runtime-pin and provenance source checks pass.
Initial test invocations had an absent explicit workspace root and a Python
module-path error; corrected invocations passed without changing assertions.
Repository private-key hygiene and whitespace checks also pass. Synthetic
receipts produced by verifier tests are not runtime or publication evidence.

APK assembly still uses explicit pinned Presentation source and Core content
alongside sealed packages. It is not a source-free package-only APK.

## Still pending for this candidate

Protected dependency/source merges, native API-36 update/start/open/reopen and
process-restart checks against the retained eleven-runner fixture, the local
ARM64 AAB, separate existing-upload-key signing, independent keyless inspection
and actual Play Internal readback remain separate steps. No runtime authority
is inferred from source checks or these managed results.

Preview 149 remains the latest observed Internal build and is unchanged. Full
chapters and per-chapter illustrations remain in the app reader and EPUB.
Physical Play installation, complete Creation/Career coverage and general
startup reliability are not yet established. Windows is outside this increment.
