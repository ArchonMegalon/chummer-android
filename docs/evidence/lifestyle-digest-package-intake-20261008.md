# Lifestyle digest allocation intake — 8 October 2026

Preview 150 is now [available on Play Internal](../../play/evidence/preview150-internal-observation.md),
with physical installation unverified. It consumes Core's bounded allocation
reduction for saved lifestyle integrity checks. The internal digest now reuses
the existing canonical streaming helper;
canonical bytes, fresh mutable-input reads, rule replay, owner/revision checks,
constant-time comparisons and persistence semantics remain unchanged.

## Exact inputs

- Core runtime: `d884b1f17025847704c64d4957f87253f9bca31b`.
- Core recipe: `9f44361649bd109f522a6610b41cb9f1def75905`.
- Core local bundle SHA-256: `2b6772b72b4715f29504a09bededad1e0e732943bb65937c3d98e76ea43b7a02`.
- UI seal: `a9cbac136ece444ac74d1a24c40f5d366fa673a7`.
- UI tree: `53687a61e89b7842da99e31f58b9cf0d03d5536f`.
- UI lock SHA-256: `cea6ea2692c7a6ff31a15b4f6559091c5e0f0c10815f1bc529f8b527c96f2f84`.
- UI consumer receipt SHA-256: `09ed78fa086d579cf1579e42e52eb5e4f33c40923e9bb19b2cbc6f151364d48a`.
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

The final seal repin rechecked the exact package receipt/bytes and affected
pin contracts. An additionally selected legacy `test_android_contract.py` full
suite is not green: 88 cases include three failures and fifteen errors, with
the identical failing case set on the retained pre-repin Android commit. These
include stale UI substring/icon-scale expectations and missing files in the
legacy ambient Presentation root. Its affected dependency-pin case passes;
the full-suite nonpass is retained, not relabelled or bypassed. A physical
provenance test invocation from `tests/` also failed to import `scripts`;
running it from the repository root is the supported invocation.

APK assembly still uses explicit pinned Presentation source and Core content
alongside sealed packages. It is not a source-free package-only APK.

## Native update and retained runner smoke

The Release SDK-test x64 APK from Android `95ed55a9f73b086317a64b0078d2c95d6a560468`
has SHA-256 `c914e8b0203fc9395a75bb0f558e491ec6fc3e65a634d6adfeaa10fa88584cb9`
and size 36,690,148 bytes. The local build passed with zero warnings/errors.
It updated the retained API-36 test installation from 149 to 150 without an
uninstall or fixture reset. The eleven-runner restore, Home display, initial
Creation restoration and force-stop/new-process restoration of the same Adept
draft passed. All 33 retained workspace files (9,132,743 bytes) stayed identical
after update, read and restart. No new mutation or finalization is claimed.

That APK used the original UI seal `d5a1e0ee16e7cc18e12475a7c7e70e06aa0c9db2`.
UI PR 367 moves its identical full tree onto the published preseal main commit;
the new receipt is a real verification of `a9cbac13`, not relabelled evidence.
The final Android repin changes provenance and derived Career authority digests,
not UI/package bytes. The earlier native smoke is not an exact-final-repin APK
test; final ARM64 compilation and artifact checks remain separate.

Startup is still slow: first-run shell/restore stages measured 37.282/59.193
seconds and the new-process stages 27.475/72.917 seconds. The local emulator
was under load; baseline 149 already encountered a SystemUI ANR. These are not
controlled performance results and do not prove the startup problem fixed.
The temporary emulator was stopped after the smoke, preserving the fixture.

## Delivery result and remaining scope

Core PR 133, UI preseal PR 365, final UI seal PR 367 and Android PR 585 merged
normally under their unchanged required checks. One local ARM64 AAB, separate
existing-upload-key signing, independent keyless inspection and actual Play
Internal readback passed. Exact source/artifact/receipt identities are in the
linked release record. No runtime authority is inferred from source checks or
managed results.

Preview 149 remains unchanged as historical evidence. Full
chapters and per-chapter illustrations remain in the app reader and EPUB.
Physical Play installation, complete Creation/Career coverage and general
startup reliability are not yet established. Windows is outside this increment.
