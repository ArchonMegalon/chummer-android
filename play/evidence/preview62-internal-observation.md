# Preview 62 — cumulative Creation Karma after saving qualities

Authenticated Chummer Play Console readback at `2026-09-30T23:20:25Z` showed
`62 (0.1.0-preview.62)` **Available to internal testers**, Internal release 58,
track Active, one version code. This is browser readback, not Publisher API proof.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).

**Available on Play Internal; physical Play installation not yet verified.**
Version 62 is consumed; do not rebuild or re-upload it.

## Delivered change and affected checks

Creation's Karma overview retains the cumulative Qualities budget once that
stage is admitted, instead of overwriting it with the earlier Attributes-only
budget. A saved 4-Karma quality correctly leaves 21 of 25. Before Qualities is
admitted, the existing Attributes fallback remains. Inexact cumulative budgets
are not replaced by an apparently exact earlier-stage budget. No rules,
mutation, persistence, ownership or dependency contract changed.

A real Core-backed regression failed before the fix and passed afterwards for
Priority and Sum-to-Ten: save a 4-Karma quality, reopen from the cold store, remove
it, reopen again, and verify 21 then 25 without changing the Attributes budget.
Checks cover admitted/inexact cumulative state and Attributes readiness. Existing
budget navigation, owner A-to-B-to-A rejection, unchanged saved bytes and both
methods' Continue checks passed, plus three source contracts. Managed, native
diagnostic and ARM64 release builds completed with zero warnings and errors.

Release x64 diagnostic APK SHA-256:
`bfa7b46bc334058deebd3771089e74655b205a1ed12b4e4a61e99e8b234b327a`.
Every tracked Android `src` byte matches its native intake. This APK uses a
separate diagnostic application ID and non-Play signing identity.

The API-36 emulator smoke reused the existing Core-produced synthetic Priority
workspace with its saved 4-Karma quality at revision 5/5. Creation visibly showed
21 left and 4/25 before and after a verified force-stop and new-process reopen.
The exact workspace bytes and quality receipt were unchanged; all 28 previously
stored synthetic workspace files remained byte-identical. Screenshots and
hierarchies were retained, and the owned emulator was stopped afterwards.
Earlier Creation steps and the quality save are fixture provenance, not a new
UI-driven end-to-end journey. One Android System UI unresponsive dialog appeared
after emulator boot; Wait was selected once and the app remained alive. Neither
this environment nor the update is a physical-performance qualification.

## Exact local release

- Android producer `61e3418251aecd9770879c380f12c6f358d94077`, PR 240 merge
  `1050f3c7e490aa040de222658379819ffccf3e03`, identical tested tree
  `a46c298c77b990c00a4584d72039a4f6d442ab81`.
- Presentation seal `e8c4f49c6d0bc02459c4f3b4d8006d819d4605f9`, tree
  `b9dd7763af0ab6f9b4bea9af6cbb1eb5b3adc6ee`.
- Core runtime `e086c6794095bf6a4a9856e8a600486e241469c7`, recipe/content
  `08fd0b960ba999e98e05cf95c8d79ec07183152e`.
- Hub package producer `c77395de9f733427ef952c851f4a95b063cb5573`.
- Reused exact 18-package UI consumer receipt SHA-256:
  `1da132f11a30bb69d13a3047c24657415a363fbbb59507b7ba4f58bd3e807249`.
- Source-input receipt SHA-256:
  `8471aad5f454d25ec61858887e77d01a436ad6eb0d1c859f99a1d735877c9026`.
- Native smoke receipt SHA-256:
  `17b17be20812e0cfdf8ebbd60d4decf860674473549e411ed2ac4a4701835a40`.
- Unsigned AAB: 33,097,259 bytes, SHA-256
  `966231f74ea2fb3fdd58b629d37754b1c08b315544a7e1a951ca36d020d4b402`.
- Signed AAB: 33,269,912 bytes, SHA-256
  `cfdf151ac74f28c212d64a01eecc9be647b3e7ef2169cbf6e86a5ec878ea66f3`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Independent signed-verification receipt SHA-256:
  `20df694da35cab76ab3acfb439c884931ea0e826df0d8f153458a5c37bc79ebd`.
- Console-observation receipt SHA-256:
  `f115de638a799f3f6827776f8536011b05a2a422ef25151470453800799c5ebd`.

One local keyless ARM64 Release build took 149.87 seconds with .NET 10.0.112,
existing toolchain image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
All 331 content files, bundle/API/ABI/privacy, credential hygiene and proof-exclusion
checks passed. Separate existing-key signing and independent keyless strict JAR
signature/certificate/unchanged-payload checks passed. Dependency mode remains
locked package closure with explicit pinned Presentation source and Core content;
not package-only native assembly or hosted runtime qualification. Unchanged
package/policy evidence was reused after exact identity and byte checks.
Builder and verifiers had no network or signing keys.

Required protected source checks passed before normal source merge. No protection
changed. Private packet `creation-release62-20261001.3AXZk3DZ` retains both AABs,
inputs, logs, signing verification and Console evidence. One exact upload and one
final confirmation occurred. Play reported missing deobfuscation/native-symbol
warnings, 8,702 supported phones and no lost supported devices. This compatibility
inventory does not qualify other form factors. Production, tester membership,
listing, billing and account security were unchanged.

## Remaining limits

Physical 62 is unverified; [Preview 52](preview52-internal-observation.md) remains
the latest physical evidence. The upload certificate is not the Play App Signing
certificate. Catalog latency remains open. No full-method/Career, general
responsiveness, all-screen polish, unattended whole-book quality, cross-chapter
likeness, hosted seven-journey, tablet/Full Editing/desktop/audiobook or public-beta
readiness claim is made. No provider action occurred; uncertain FirstBook jobs
remain fenced. [Preview 61](preview61-internal-observation.md) and all earlier
evidence and artifacts remain immutable.
