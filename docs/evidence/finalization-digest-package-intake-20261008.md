# Finalization digest allocation intake — 8 October 2026

The next local candidate consumes Core's allocation reduction in
`CharacterCreationFinalizationDigest.Compute`. Canonical JSON bytes, duplicate
property ordering, number spelling, invalid-Unicode rejection, fresh mutable
input reads and concurrent-call isolation remain checked. No owner, persistence,
replay or integrity check is removed. All 331 packaged rule-data files are
unchanged; their source-provenance envelope advances with the Core recipe.

## Exact local inputs

- Core runtime: `899be24cff0525eedd90c2299ba19dbef7f63cc1`.
- Core recipe: `76e77b13b9ce375a3183da9abb6cdf58ecec21af`.
- Core bundle SHA-256: `60e52b1df2c6861ad4ce5b02d56d3d6c2a3a7a4a83625e2ebea87628f98c0792`.
- Presentation seal: `66b3185dd97a1e9da8002a38d2abbc364fda6dcd`.
- Presentation lock SHA-256: `8029cee77a18d84f813361c0f8fe5a4a3bfa10d49ee2448e94e8d32511529d46`.
- Local UI consumer receipt SHA-256: `be76cc97cd5c6de6daeeb108267397396d9508d47490b8757d03d89b04a5adef`.
- Owner-cache manifest SHA-256: `82e592a715251e1d53f18f47877f5ca6a8c513faa182ec93d03f3b30e0917b84`.

Core PR132 and UI PR363/364 merged normally with their required checks. The
UI merge tree equals the receipt's exact seal tree. The new public Core asset
was authorized by standing approval, and its anonymous download matched the
locally verified bytes. No existing asset was replaced.

## Completed checks and their limits

Core's 34 focused digest regressions pass, including a new allocation regression
which failed against the old implementation. In that managed fixture, allocated
bytes fell from 2,619,632 to 418,912. A separate production-store fixture preserved
all 24 files belonging to eight synthetic runners byte-for-byte. Neither result
is an Android latency measurement.

The local Core package producer passed 783 product cases plus focused owner and
inventory checks. UI's exact-seal consumer passed five builds, 879 product tests
and its existing focused groups. The test build retains 62 MSTest analyzer
warnings; no build error occurred. This reused an authenticated owner-package
cache with a fresh consumer NuGet cache, not a cold owner rebuild or hosted run.

Android intake passed the native-source compile, MAUI compile and interaction
test build with zero warnings/errors. The production-backed startup subset
passes restoration for Priority, Sum-to-Ten, Karma, Life Modules and an existing
Career runner, reusing one owner-bound roster synchronization without mutation.
An actual failed presenter read still takes the fail-closed recovery path.
The exact package and content validators pass; 51 package-authority and 114
affected build/runtime-pin contract tests pass. Synthetic verifier receipts
emitted by those tests are not device or publication evidence.

APK assembly still requires explicitly pinned Presentation source and Core
content alongside the sealed packages. It must not be described as a
source-free package-only APK.

## Delivery boundary

Subsequent delivery: [Preview146 is available on Play Internal](../../play/evidence/preview146-internal-observation.md).
The exact candidate passed the bounded SDK-test x64 update/new-process Career
reopen smoke, local ARM64 build, isolated existing-key signing, independent
keyless inspection and actual Console readback. All24saved files/eight synthetic
runners remained unchanged. Physical Play installation and broad performance
remain unverified. The paragraphs below preserve the pre-build checkpoint;
they are not the current release status.

Preview 146 is a source candidate only at this checkpoint. Its APK/emulator
startup and saved-runner process-restart smoke, ARM64 AAB, isolated signing and
Play readback remain pending. A prior Preview145 cold-emulator attempt suffered
SystemUI/system-server failure under host pressure; it remains inconclusive,
not a passing performance result or a proven application defect.

Preview145 remains the latest observed Play Internal version and already
includes per-chapter illustrations in the app reader and EPUB. Its evidence
and artifacts are unchanged. Physical Play installation, general startup
reliability and complete Creation/Career coverage remain open. Windows is not
part of this increment.
