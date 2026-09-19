# Preview 17: New runner correction and observed Internal availability

At `2026-09-19T14:59:43Z`, the authenticated Chummer Play Console showed
**17 (0.1.0-preview.17)**, **Available to internal testers**, on the Active
Internal track. Its localized release display was `19 Sept 16:59`.
Physical Play installation/update remains unverified.

## Bounded product result

- Native Picker callbacks now unwind before dialog controls are replaced.
  The old callback failed the new regression and left a stuck Android input
  channel; the corrected selection and scrolling were exercised on API36.
- The shared UI maps its exact legacy `SumToTen` value to Core's `SumtoTen`.
  Other unknown/miscased aliases remain rejected.
- Core accepts valid incomplete Karma/LifeModule drafts through its normal
  workspace loader, without waiving finished-character/metatype validation.
- Save/discard guards now display their Notice in the native dialog.
- 72 Core bootstrap tests and 9 Presentation boundary tests passed. Actual
  native-control/Core tests created exactly one draft for each of four methods,
  rejected stale Picker events and displayed the unsaved-runner guard.
- Sum-to-Ten creation, save, restart/reopen and actual prerequisite-editor
  navigation passed in a local Debug x64 APK. The editor displayed the exact
  Sum-to-Ten profile and authoritative 25-Karma budget.
- Final Karma device smoke created/opened a draft, but its next build-method
  editor remained unavailable. **Karma wizard completion is NOT fixed.**
  Initial creation was slow on the emulator. No all-method or all-freeze claim.

## Exact local source assembly

Android `7276c7008087e55d08ef405d812f96a27a6b04a9`, tree
`3420bc3c7d4240ef6a01bbeaedc801ca4468cacb`, merged via PR85 as
`00144c29ba10b4ad8a6f60211fd3540c64c0ed86` with identical tree.
Exact-main source checks35450023194 passed, not hosted runtime qualification.

The bundle uses Core `e66adccf06fb8bee96264b9a9112f970508e6e97` and
Presentation `ce487f3b5eb21129b7ffbac5dcd17ad8b2b64255`. Core PR61 and UI
PR177 are source fixes; their package reseals remain separate and their hosted
package gates are not green. Existing package locks do NOT authorize these
new source bytes. This user-approved local source assembly is not a new sealed
package snapshot or no-siblings package proof. The
[source inputs](preview17-local-source-inputs.json) record every exact export.
The Debug smoke used identical functional sources with version16; only release
identity and generated inventory hashes changed for17.

## Artifact and transaction

Keyless, networkless Docker Release build passed in 2m17.26s with zero warnings
or errors. Inspection verified version17, API24+/target36, arm64-v8a, absence
of proof instrumentation and all330 canonical content files. A separate offline
signer used the unchanged old upload key; an independent keyless verifier
checked the certificate, strict signature and unchanged ZIP payload.

| Field | Value |
| --- | --- |
| Package | `com.myexternalbrain.chummer` |
| Signed bytes | `31034240` |
| Signed SHA-256 | `e48f39f4650321d2a12063bf49bb04060b1f37b9a00fcb6e0a11f233cdb0b7fb` |
| Unsigned SHA-256 | `1554c09d9f1b98d91fe7b070fdf2ec4d56c09f72e3e3202ea5ee215ca7620b9b` |
| Source-input SHA-256 | `0ba5119d54f3b005f750489ec6f07eaffcfd0cc7027a86f203d5a99749381e5f` |
| Upload certificate SHA-256 | `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15` |

The [signer](preview17-local-signing.json) and
[independent verifier](preview17-local-verification.json) retain their original
phase flags; later publication does not rewrite those earlier receipts.
The exact file was uploaded once into Chummer Internal release13. Both Internal
publication controls were confirmed and provider availability was read back.
DE/EN/ES notes explicitly disclose the remaining Karma limitation.
Supported device counts were unchanged. The two non-blocking warnings concerned
missing deobfuscation data and native debug symbols.

Retained packet: `android-preview17-local-20260919.8QZVRGxD`.
Availability screenshot SHA-256:
`c9ebfbb2a09ca2f377ac3d48718889007b7ff2b643027abdfab6b6b2fdc61d3b`.
No provider AAB was downloaded for byte comparison. No physical Play install,
exhaustive wizard, Full Editing, tablet, Rook or public readiness is asserted.
Previous release evidence remains unchanged. No Production, tester roster,
security or billing change occurred. The four owned temporary containers were
removed; the owned emulator and browser were closed. Artifacts and key retained.

Internal testers can [install/update through Play](https://play.google.com/apps/internaltest/4700678198570024687).
Code17 is consumed; use a higher unused code next.
