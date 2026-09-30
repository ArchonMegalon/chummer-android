# Preview 59 — lower Magic source-row allocation during Creation

Authenticated Chummer Play Console readback at `2026-09-30T19:46:18Z` showed
`59 (0.1.0-preview.59)` **Available to internal testers**, Internal release 55,
track Active, one version code. This is browser readback, not Publisher API proof.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).

**Available on Play Internal; physical Play installation not yet verified.**
Version 59 is consumed; do not rebuild or re-upload it.

## Delivered change and affected checks

Core serializes each Magic source XML row once and writes its fixed canonical
digest directly, preserving the existing raw-source versus normalized-payload
semantics. In a local managed repeated-projection measurement, temporary
allocations fell from 62,665,184 to 52,848,016 bytes (about 15.7%). The same
67 reads, 828 validations and 29,400,547 validated bytes remained. This is not
a whole-app speedup, reduced validation, or a measured Android speed improvement.
Previous quality explanations, readable/clickable Attributes, GUID cleanup and
upgrade-safe all-source defaults remain included.

The 49 focused Core digest/source/admission cases and actual package producer
passed, including 707 managed cases. The actual UI consumer passed 862 product
cases plus existing scoped filters. Android's affected checks cover four SR5
defaults/selectors, minimal UI, Mystic Adept cold recovery and the no-duplicate
dirty guard. Native build completed with zero warnings/errors. The 35 focused
content/runtime/version and 128 provenance Python cases passed; 15 source-contract
cases passed against exact exported inputs. One unchanged icon-scale assertion
expects <=0.60 while the existing value is 0.78; it remains failed and is not
included in the green claim. Existing UI test-project warnings remain separate
from the zero-warning Release build.

Installed API-36 x64 Debug APK SHA-256:
`f99bbfa86be4d7fe4cb7184df8d0a576afab9a6b474e7e6486079ee18c988827`.
All tracked Android `src` bytes match the tested intake. The first upgrade safely
refused a different Debug signing certificate; the replacement used the existing
emulator Debug key, with unchanged non-signature ZIP payload independently
verified. No uninstall or data wipe occurred; this key is not the Play upload key.

In-place upgrade preserved 23 existing synthetic workspaces. Exactly one new
Priority runner was created and saved at revision 1/1. Force-stop, old-PID absence
and a new process were verified; the exact selected runner returned to Creation.
All 24 saved files were byte-identical across restart. The smoke packet binds
hierarchies, screenshot, selected workspace and file digests. The owned emulator
was stopped after testing.

No new Creation timing was retained in logcat, so no native speedup is claimed.
Preview 58's single constrained-emulator measurement of 62.194 seconds remains
slow. This smoke does not prove complete Creation-to-Career, all four native
methods, general responsiveness or ARM64 Release execution.

## Exact local release

- Android producer `62e39b7c3b8454dcec46578566ab0395d47d23bb`, PR 234 merge
  `8b8908ffceff29381da0f8b9d165ff1135265361`, same tested tree
  `443290b37b169fc0c9348285fba9e2d6b8bc694c`.
- Presentation seal `e8c4f49c6d0bc02459c4f3b4d8006d819d4605f9`, UI PR 285 merge
  `aab471246cb9a8cf09af93fcd615b00a9fdeb28f`, identical tree.
- Core runtime `e086c6794095bf6a4a9856e8a600486e241469c7`, recipe/content
  `08fd0b960ba999e98e05cf95c8d79ec07183152e`, Core PR 105 merge
  `375a696a6bf492204aa365e64668d0402cefe49d`.
- Hub package producer `c77395de9f733427ef952c851f4a95b063cb5573`.
- Source-input receipt SHA-256:
  `6e5a1337a7d9b95696b92294ff91f1c5aafe9f0dac8d4fd56846c046cdb359ca`.
- Unsigned AAB: 33,096,791 bytes, SHA-256
  `3735283b71c5576a4dbcf670ea0a7109979de32c59a3a5f9f48c18dc4a4f7479`.
- Signed AAB: 33,269,458 bytes, SHA-256
  `132594c272f0cdcb5742c4aa5da43297d760919bc55cd55a418eaee910965ebf`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Independent signed-verification receipt SHA-256:
  `779a2b6480e27945797b95a7a462a6250beca25526511cc60c0d5833414d008c`.
- Console-observation receipt SHA-256:
  `7003ab7dd3e0ccf2c1960e7ceab1e9d44fe0de9e2cd134174ad61c4af595015f`.

One local keyless ARM64 Release build took 151.97 seconds, zero warnings/errors,
.NET 10.0.112, existing toolchain image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
All 331 packaged content files, bundle/API/ABI/privacy, credential hygiene and
proof-exclusion checks passed. Separate original-key signing and independent
keyless strict JAR signature/certificate/unchanged-payload checks passed.
Dependency mode is locked package closure with explicit pinned Presentation
source and Core content; not package-only native assembly or hosted qualification.
Builder and verifiers had no network or signing keys.

Required protected checks passed before normal source merges. No protection
changed. Private packet `creation-release59-20260930.F4sa8E` retains both AABs,
exact inputs, logs, signature verification and Console evidence. One upload and
one final publication confirmation occurred. Play reported only missing mapping
and native-symbol warnings and no lost supported devices. Production, tester
membership, listing, billing and account security were unchanged.

## Remaining limits

This does not prove all build methods/Career actions, all-screen polish,
unattended whole-book quality, cross-chapter likeness, hosted seven-journey
qualification, tablet/Full Editing/desktop parity or public-beta readiness.
No paid-provider action occurred; uncertain FirstBook jobs remain fenced.
The upload certificate is not the Play App Signing certificate. Physical 59 is
unverified; [Preview 52](preview52-internal-observation.md) remains the latest
physical evidence. [Preview 58](preview58-internal-observation.md) and earlier
artifacts and receipts remain immutable.
