# Preview 84 — Resources save feedback

Authenticated Chummer Play Console readback on 3 October 2026 at 18:05:25 UTC
showed `84 (0.1.0-preview.84)` **Available to internal testers**, Internal release
79, one version code, released at 20:05 Europe/Vienna. This is browser readback,
not Publisher API evidence. [Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
The later [physical Play update](preview84-physical-observation.md) verified
83→84, installed identity and read-only Resources navigation after restart.
Version 84 is consumed: never
rebuild or re-upload it. Preview 83 and its separate physical evidence are retained.

## Change and exact artifact

While saving a Resources budget, the page retains the issued controls, disables
duplicate submission and displays neutral localized progress instead of a false
outdated-state warning. Owner changes still hide private content. Admission,
mutation, replay and recovery checks remain unchanged. PR400 and version-only
PR401 merged normally with required source/safety and GitGuardian checks.
No branch protection, rules, dependency graph or persistence format changed.

- Android producer: `6c9ebefe4a568c79f38fcab15391212698015f01`;
  main merge: `3432c20a99cfa9b84caf25c40985aa6ee116a6c2`;
  identical tree: `dfbe5086e9cd1c343bbe67878c20a7b4305cdac6`.
- Presentation: `2704ce4339838706d00b035e6baae2446631b0ed`.
- Core runtime: `9552a390b5091c5c2bd294436b160b1e0655024b`;
  recipe/content: `4e34e01fd945d1cafbef5cc71e51bcf093df4c0c`.
- UI receipt: `d2a792231054fd588c364fbca2850e8c78b3914bd50ed1f92066ee59e92d4ee5`.
- Unsigned AAB, 33,621,896 bytes:
  `53fd746fb6e60f5cbf912a7d772fe2ab9ae9708b345c8b309d20153fecbed5f9`.
- Signed AAB, 33,794,558 bytes:
  `0dac0e5d1c9df72248df68f63f3d7d7aaa684d270edbdc12fc53da9950e0c0fa`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `27e781928666f90cce7a0c4082346f4f2941fa3cd8560e3a0b39bf3284ced103`.
- Independent signed verification:
  `93f3cd2752c0c79e91f10d6c3674001643d3a04b07fbb26023c19a3352122245`.
- Private Console execution record:
  `c0f97f2c3f0f969ad02897183660e5835aa1262e06dd3e39384594c6d37b615d`.
- Console availability screenshot:
  `7807d57fb51559a83c2758f8467c32381f7b826bbff9842cb315f4e68d67baa5`.

The local offline keyless ARM64 Release build used .NET10.0.112, JDK17.0.20.1
and image `sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Exact source exports, package binding, 331 embedded content files, manifest,
API24+/target36/ARM64, proof exclusion and credential hygiene passed. This is
locked package closure with explicit pinned Presentation source and Core content,
not package-only or hosted runtime qualification. A separate existing-key signer
ran once. Independent keyless verification passed strict JAR signing, expected
certificate and byte-identical 1,783 non-signature payload entries.

## Focused tests and limits

Six actual Core/MAUI cases cover Priority, Sum-to-Ten, linked ownership,
owner ABA, lost reply after commit and failed refresh. The regression failed
before the fix. Twenty focused Python checks and the managed compile passed.
The runtime source is identical to the native-tested tree; 84 changes only version.

The separate SDK-test-signed Release x64 API36 fixture used APK
`4330320badac5726ef2194cf0b3b73ddb6d40d52548069f15fdfa42fdfc2c3ea`.
At font scale 1.3, Priority preview wrote nothing; one explicit zero-Karma save
advanced 5/5→6/6 with one Resources receipt and a 50,000 budget. Raw character
and unrelated drafts stayed unchanged. The pending screenshot shows a disabled
confirmation, saving text and spinner. Verified process death/new process and
reopened Resources showed the same saved choice. Persisted snapshot SHA256:
`d6b11417af833db191ed2616e0fd1ab3afd08ec7128dbd09a0105abb7014996f`.

Slow transitions, a System UI startup dialog and null-root observations remain
documented. This is not a performance fix or native Sum-to-Ten/physical mutation
proof. No full seven-journey, all-method, SR5/Origin/tablet/ANR-free or general-beta
claim. The complete thread goal remains incomplete.

One exact upload/publication occurred under standing Internal approval. Existing
audience and Production/listing/billing/security settings were unchanged. Play's
missing mapping/native-symbol warnings remain. Private packets
`creation-release84-20261003.gIgdr4dg` and
`resources-save-feedback-20261003.Rvq3lvbe` retain artifacts and observations
outside served directories; no private runner/device data is published here.
