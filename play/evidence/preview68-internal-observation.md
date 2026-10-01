# Preview 68 — single-pass Magic draft admission on return

Authenticated Chummer Play Console readback at `2026-10-01T04:11:51Z` showed
`68 (0.1.0-preview.68)` **Available to internal testers**, Internal release 64,
track Active, one version code. The Console displayed `Released on 1 Oct 06:11`
(Europe/Vienna). This is browser readback, not Publisher API proof.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).

**Available on Play Internal; physical Play installation not yet verified.**
Version 68 is consumed; do not rebuild or re-upload it.

## Change and affected checks

Magic/Resonance now binds a freshly loaded draft using one full canonical
projection instead of repeating that projection while binding and comparing the
same state. Equal owner/editor state retains unsaved selections and review;
changed owner epochs reset them. Invalid, corrupt or stale authority is rejected
with no actionable editor. Fresh Core loads, exact owner/display/digest checks,
post-await revalidation, draft admission and mutation rules remain intact. No
cross-read cache or rules/dependency-package change was introduced.

Actual-Core managed checks passed with zero warnings/errors, including retained
selection/review, corrupt or stale authority, owner A-to-B-to-A transitions,
cancellation, the 363-option catalog, preview/confirmation, Mystic Adept powers
and spells, saved-state cold reopen and replay rejection. Eleven Magic source
contracts, version validation, whitespace and private-key hygiene passed.

One paired instrumented API-36 x64 emulator observation measured warm return
preparation at 8,984 ms before and 6,016 ms after. The changed validation segment
was approximately 2,907 ms versus 603 ms. Fresh Core loading was still 5,413 ms
in the fixed run; final rendering was separately measured at 392 ms. These are
single synthetic observations, not a statistical or physical-phone benchmark.

The fixed Release x64 diagnostic APK built in 76.32 seconds with zero warnings/
errors, SHA-256 `10e8800e7a81cb9dfbf6bc5645747da9005c8d9485ea453a6cd2e2809700bf37`.
Its separate application ID and version 67 are diagnostic only, not the Play 67
artifact. Temporary duration-only Console probes and the subsequent 67-to-68
version metadata are the only differences from the clean production sources.
Those probes were not tracked or included in the production source export.

Native smoke reopened the stored Mystic Adept fixture, opened Magic and its
363-option spell catalog, selected Acid Stream once in memory and returned
through the catalog to the main page. Selection and the 1/5 used, 4 left budget
survived. All 30 existing workspace files stayed byte-identical. Both diagnostic
startups had a System UI ANR before the affected route; Wait was selected once
per startup. No ANR-free startup claim is made. The owned emulator was stopped.
This is not a complete native Magic save/process-restart journey; actual-Core
managed cold-reopen coverage is separate.

## Exact local release

- Android producer `d2e72a2c4fbf2a266b868bff44e84759c94d152c`, PR 252 merge
  `492070736a3fd42d8c0e00a87d288f7b297019a1`, identical tested tree
  `1aa49cbe38062ef1c4f91207b28ff2a6660c76b4`.
- Required source/safety run `36813038477`, job `110212058529`, and GitGuardian
  passed before normal protected merge; this is not hosted runtime proof.
- Unchanged Presentation seal `779bf2e83ec2cc422e14ff499346e9a4c5050d6c`, tree
  `8d35e645d3ec5a6c68eab4d6c9ac06b70285a80d`.
- Unchanged Core runtime `d311b117606be87cc0ea6e6237a51ac792bd1507`, recipe/content
  `7247a7f3051f3b9412bae4877bbfebec788b589a`.
- Reused UI consumer receipt SHA-256:
  `2632acf913ec635c7ce27f056335dea8ccf938e42967e38be0716b1d99c581f7`.
- Source-input receipt SHA-256:
  `8ea22f6edd3ba2b177bb497082a14d7d8b4c3d56f7f2a27f7487fe5607911a8f`.
- Native smoke receipt SHA-256:
  `74f807c7562f9b87f27f806a344453fe412524658cb73a186bddc8823cb5db02`.
- Unsigned AAB: 33,112,840 bytes, SHA-256
  `de63f46ea754eb8b69903a6fb2874cc20538418bcb654771da205d728808337e`.
- Signed AAB: 33,285,478 bytes, SHA-256
  `fe044beb0be5011b03b6d00d646472515cb542e4c9604104a44a99a275f83902`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Independent verification receipt SHA-256:
  `6c0a06380e64a4b273d03b307b931449f224155bac3d945a3447082907d0e422`.
- Console observation receipt SHA-256:
  `1e5dbbb6899e4e57511e853c98fc23420f4b972ef0eaa2fa026ab4424abfd924`.

The local keyless ARM64 Release build took 156.04 seconds with zero warnings/errors,
.NET 10.0.112 and toolchain image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
All 331 content files, bundle/API/ABI/privacy, key-hygiene and proof-exclusion
checks passed. Separate existing-key signing and independent keyless strict JAR,
certificate and unchanged-payload verification passed. Dependency mode remains
locked package closure with pinned Presentation source and Core content, not
package-only native assembly. Builder and verifiers had no network or private key.

Private packet `creation-release68-20261001.2ho21eKV` retains artifacts and logs.
One upload and one final confirmation occurred. Play reported two existing
diagnostic warnings (deobfuscation and native symbols), 8,702 supported phones
and no lost devices. One English release note describes the narrow change and
remaining delay. Other listed form factors are not qualified. Production, tester
membership, store listing, billing and account security were unchanged.

## Limits

Physical 68 is unverified; Preview 52 remains the latest physical evidence.
The upload certificate is not a new Play App Signing certificate observation.
Fresh Magic loading and some implementation-oriented Magic labels remain open.
No all-screen polish, general physical-performance, full-method/Career, unattended
whole-book quality, cross-chapter likeness, hosted seven-journey, tablet/Full Editing/
desktop/audiobook or public-beta completion claim. No provider calls or uncertain-job
replays occurred. Preview 67 and older evidence remain immutable.
