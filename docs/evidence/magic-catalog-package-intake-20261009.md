# Magic catalog allocation — local package intake

This candidate consumes Core's operation-local canonical JSON sorting change.
Property names are decoded once and sorting uses cleared pooled scratch storage;
canonical bytes, duplicate-property ordering and malformed-input rejection are
preserved. There is no persistent digest cache or change to rules, ownership,
revisions, mutation admission or saved runner formats.

## Exact inputs

- Core runtime: `aad6cb17d63f35f78c3e234292c25f2615d30763`.
- Core recipe: `d241787b94f1a0c3d5a42324a1b1176df07c956b`.
- Locally produced public Core bundle SHA-256:
  `f6c135d319e569366c9eab1f519f519854fd33739fd1b845976ca2fdbc791232`.
- Actual local UI consumer: `3036962a4d6a588bb1f4dbfb28c91bde5efe033c`.
- UI tree: `e5123a5afa7320639fe3cd025674c7468a2a2f61`.
- UI package lock SHA-256:
  `bedb5c699e69536776e614c4a23000fa125e96d8423b22e07b46588f3c034b4f`.
- Actual UI verification receipt SHA-256:
  `1ec7c07593edde8385aa6ccf4fe63d1ca15bd92a966da6ee417815caa306add3`.

The immutable Core bundle was inspected and published once under standing
approval. Independent public download matched its bytes. UI PR 375 admitted
the recipe; PR 376 seals the actual generated locks on main's current topology.
Its publication head `8a62ac5ada6b09f76b3296f6fae803e40ae4479f` has the exact
consumer tree above. The local receipt keeps its real producer/consumer identity.

## Completed local checks

Core's 42 canonical-digest and 46 Magic service/source tests passed. Four new
regressions cover canonical equivalence, mutable inputs, parallel use, malformed
input cleanup and allocation bounds. The allocation regression fails on the
previous Contracts assembly. Representative catalog allocation fell from
1,647,712 to 418,912 bytes. Full restored domain hashes match and all 33 retained
synthetic files stayed unchanged. Two managed restore comparisons were 5–9%
faster, but warm reopens were noisy. **No native startup speedup is established.**

The local Core producer passed 811 managed consumer cases, 18 owner-admission
cases, scoped-storage checks and 15 inventory cases. The local UI consumer
passed its builds, 879 product cases and selected focused groups. Test compilation
retained 62 warnings and zero errors. These separate suites are not a count of
unique combined coverage.

Android's native graph, actual MAUI compilation and interaction test compilation
passed with zero warnings/errors. The affected Magic tests passed: exact source
spell summaries, native picker/review/confirmed save, Mystic Adept power-point
purchase and powers/spells, Skills/Magic revisit, cold file-store reopen/replay,
pending-feedback cleanup, cancellation, owner ABA, journal isolation/recovery
and uncertain-reply no-replay. This is managed-control proof, not device proof.

The changed pin/verifier selection passed 219 tests and 645 subtests; package
regressions passed 51 tests and 614 subtests. The bounded source/security
selection passed 58 tests and 91 subtests. Private-key hygiene also passed.

All 18 packages and 13 authority files passed byte-bound admission. All 331 Core
content files match; rule-data bytes are unchanged. APK assembly uses explicitly
pinned Presentation source and Core content with locked runtime packages, not
ambient sibling discovery or a source-free package-only build.

## Remaining delivery boundary

Preview 154 is a candidate version only. Final-source native-device smoke,
signed ARM64 AAB and Play readback remain pending. Preview 153 stays immutable
and available. Full chapters and chapter images in the reader and EPUB are
unchanged. No new provider generation, physical Play installation, Windows work,
complete Creation/Career coverage or finished-app claim is made here.
