# Preview 58 — Creation allocation reduction and readable save guidance

Authenticated Chummer Play Console readback at `2026-09-30T18:14:01Z` showed
`58 (0.1.0-preview.58)` **Available to internal testers**, Internal release 54,
track Active, one version code. This is browser readback, not Publisher API proof.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).

**Available on Play Internal; physical Play installation not yet verified.**
Version 58 is consumed; do not rebuild or re-upload it.

## Delivered change and affected checks

Core reduces temporary allocations while hashing canonical Creation state,
preserving the exact bytes and validation boundaries. The measured managed
Qualities/Magic allocation reduction was about 29.6%; it is not a claim that the
whole app is 29.6% faster. New runner's exact active-workspace save/conflict
notices now use readable localized guidance without GUIDs. Presenter diagnostics,
typed ownership, mutation admission and unknown/stale notices remain unchanged.
Preview 57's quality explanations, readable Attributes and upgrade-safe all-source
defaults remain included.

Focused Core allocation/authority tests and the actual eight-package producer
passed, including 706 managed cases and owner/storage checks. The exact local UI
consumer passed 862 product cases plus existing scoped filters. Android's affected
managed checks covered four SR5 defaults/selectors, minimal UI, Mystic Adept cold
recovery and the no-duplicate unsaved-runner guard. The final notice delta passed
its managed build/tests and seven localization cases. Package/content/verifier
checks passed (301 cases plus 43 final Core-tree provenance cases). Three separately
run legacy desktop fixture assertions remain failed and classified as stale
expectations; they are not included in the green claim.

Final API-36 x64 Debug APK SHA-256:
`89e0e534fb70fb71ac316e3450943ce9d79f8f322bd072c2aacc96b36d4329d3`.
All tracked Android `src` bytes match the tested intake. In-place upgrade preserved
22 existing synthetic workspace files. Exactly one new Priority runner was
created, saved at revision 1/1 and reopened on Creation after a verified process
replacement; all 23 saved files were unchanged across that restart. The preceding
unsaved-runner rejection was a legitimate guard, not a successful creation or a
hang; the old runner was explicitly saved before the 22-file baseline.

One constrained-emulator creation measured 62.194 seconds, versus an earlier
73.325-second sample. Creation remains slow. These are single observations, not a
repeatable or physical performance qualification. The smoke does not establish
Creation-to-Career finalization, four complete native methods or ARM64 Release
execution. The owned emulator was stopped after verification.

## Exact local release

- Android producer `21e3a6d465c874c3ff1be1cf0ecc96e4578ce857`, PR 232 merge
  `fdf5d9f9b00506a66988b6fa1b12683b69ed19d5`, identical tested tree
  `39e352f196c5d93c289f6c1b928b66cd1dc1ded3`.
- Presentation seal `c256e3eb44089a513383cc6024ff003cd8d429ef`, UI PR 283 merge
  `7dc5b3cdb5f0e4d91f261a0f1477238ad40a86ac`, identical tree.
- Core runtime `4e98a67122d00592f89234a1686352083c48f9e7`, recipe/content
  `2c76068056cfa74d2496e0686cb181fac7c7a7a4`, Core PR 104 merge
  `c56e6e5a8290965012c1af67268d55441adb40bc`.
- Hub package producer `c77395de9f733427ef952c851f4a95b063cb5573`.
- Source-input receipt SHA-256:
  `a5912ee494f749e7eb0d2395b2c316a4f9d24622785c0a4a753cd94a48dbbb4c`.
- Unsigned AAB: 33,097,741 bytes, SHA-256
  `e269f4593589a73b3937280ba92e8ff7f850c1bc146b387576ef17b78ce04743`.
- Signed AAB: 33,270,391 bytes, SHA-256
  `fa72122fc0e969d4c7418bd3f1f8690157d57c644c2de2d779729242f27a00eb`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Independent signed-verification receipt SHA-256:
  `6006fdee90ad0279acc7fcf2654b854a334915f6ae082777c7515db60a55dfc8`.
- Console-observation receipt SHA-256:
  `59dd8ca92bc7ce36f4b2c868517da8c6173222a24963d8a70c3864438a817517`.

One local keyless ARM64 Release build took 147.88 seconds, zero warnings/errors,
.NET 10.0.112, existing toolchain image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
All 331 packaged content files, bundle/API/ABI/privacy, credential hygiene and
proof-exclusion checks passed. Separate original-key signing and independent
keyless strict JAR signature/certificate/unchanged-payload checks passed. Builder
and verifiers had no signing keys or network. Dependency mode is locked package
closure with explicit pinned Presentation source and Core content, not package-only
native assembly, a cold owner rebuild or hosted runtime qualification.

Required protected checks passed before the normal source merges. No protection
changed. Private packet `creation-release58-20260930.3EBtt4` retains both AABs,
exact inputs, logs, signature verification and Console evidence. One upload and
one final publication confirmation occurred. Play reported only missing mapping
and native-symbol warnings, with no lost supported devices. Production, tester
membership, listing, billing and account security were unchanged.

## Remaining limits

This does not prove all build methods/Career actions, all-screen polish, unattended
whole-book quality, cross-chapter likeness, hosted seven-journey qualification,
tablet/Full Editing/desktop parity or public-beta readiness. No paid-provider
action occurred; uncertain FirstBook jobs remain fenced against replay. The upload
certificate is not the Play App Signing certificate. Physical version 58 is
unverified; [Preview 52](preview52-internal-observation.md) remains the latest
physical installation evidence. [Preview 57](preview57-internal-observation.md)
and earlier artifacts/receipts remain immutable.
