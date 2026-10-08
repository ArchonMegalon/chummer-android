# Creation prerequisite reuse — 9 October 2026

This candidate consumes Core's narrowly scoped reduction in repeated Creation
prerequisite allocations. A warm prerequisite read now admits the retained
projection before making full XML document copies. It still checks current
source bytes, identities, directory membership and catalog fingerprint on every
reuse, including calls nested in an active snapshot. Returned authority is a
detached copy. Cold and drifted reads retain their existing validation paths;
owner, revision, mutation and persistence checks are unchanged.

## Exact package inputs

- Core runtime: `c73e43fb144b50cc7f85a71f8aeee50e28f74dd2`.
- Core recipe: `953957152206c8981a2177ffec4923059971e6eb`.
- Local Core bundle SHA-256: `3b630f906fcc605facdd8cdea5cdba88eea7a1313f5f6023230791988f531a99`.
- UI seal: `f1e51904591ac9bc39f183e0d12520de804a6308`.
- UI tree: `5f893d4ac6c3a0b24db6cb57dd51a906b065fdb3`.
- UI canonical lock SHA-256: `104792306f59c307f07251e741497fd033557c7741709cf234aed865567c9151`.
- Local UI consumer receipt SHA-256: `51e3c4c3057c657e7a590cfcde9cc158f880e6e0564f0427ceb9b0c780f27b19` (81,071 bytes).

The exact Core bundle was published as a new local-build GitHub prerelease
under the standing 25 September approval. Its provider digest and anonymous
download matched the retained archive. No existing release was replaced.

## Verified before Android intake

Seven focused regressions failed against the previous Core implementation and
passed unchanged on the candidate: three detached-copy/reuse cases and four
nested source-drift cases. All 280 selected resolver/prerequisite cases passed;
the actual Infrastructure build had no warnings or errors. The private test
project retained analyzer warnings. The exact Core package producer passed
790 selected product cases plus its existing owner/admission/inventory checks.

Reads of the retained synthetic runner produced identical full serialized
contacts, qualities, magic and finalization results before and after the change.
All 33 workspace files stayed byte-identical. Warm prerequisite allocation fell
from approximately 4.13 MB to 0.40 MB; measured finalization allocation fell from
406,817,408 to 351,204,856 bytes. These are managed allocation observations,
**not proof that native Android startup is fixed**. Earlier native profiling
also encountered a SystemUI ANR, so it is not a clean performance baseline.

The exact UI seal passed five local consumer builds, 879 product tests and all
existing focused groups. The test build retained 62 warnings and no compiler
errors. All 18 reused owner packages and 13 authority files were checked against
their actual cache manifest. This is authenticated local package reuse with a
fresh consumer cache, not a hosted Android qualification or cold owner rebuild.

## Android intake

The local intake compiled the native source graph, actual MAUI project and
interaction-test executable with zero warnings or errors. Six focused startup
cases passed: uncreated Priority, Sum-to-Ten, Karma and Life Modules, an existing
Career runner, and failed presenter initialization retaining its fail-closed
path. These builds used the new dependencies before the final metadata repin;
they are not a native-device or final-APK receipt.

After the exact repin, all 18 packages and 13 authority files passed byte-bound
validation; all 331 canonical content files matched. The 51 package-authority,
167 affected pin/build/provenance and 58 source/safety cases passed. Generated
physical and hosted receipts in those verifier tests are fixtures, not actual
device or hosted qualification evidence. Core PR 134 and UI PRs 368/369 merged
normally with their required checks; the merged trees preserve these inputs.

## Pending delivery checks

The retained-runner native update/reopen/process-restart smoke, ARM64 AAB
assembly and isolated signing
remain pending for this candidate. Preview 150 is unchanged and remains the
latest observed Internal version. No new upload or physical installation is
claimed here. Full chapters and per-chapter app/EPUB images are unchanged.
Complete Creation/Career coverage and general startup reliability remain open;
Windows is outside this increment.
