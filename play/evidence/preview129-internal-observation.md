# Preview 129 — Creation prerequisite allocation improvement

Authenticated Chummer Play Console readback on `2026-10-07` at 17:56 UTC showed
`129 (0.1.0-preview.129)` **Available to internal testers**, Internal release
123, one version code, last updated 7 October at 19:56 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API or physical-device evidence.
**Physical Play 129 installation/update remains unverified.** Play states that
propagation usually takes up to an hour and can take longer. Version 129 is
consumed; do not rebuild or re-upload it. Preview 128 and earlier artifacts and
evidence remain unchanged. No Production, audience, listing or account changes.

## Change and affected verification

Core reuses XML writer buffers within a Creation prerequisite projection.
Rules, source digests and fresh owner admission remain unchanged; there is no
shared mutable catalog cache. The [package intake and native smoke](../../docs/evidence/prerequisite-package-intake-20261007.md)
record focused XML equivalence, ownership, isolation and allocation checks.
Managed allocation for the full projection decreased from 43,210,496 to
22,003,768 bytes with identical authority digest. This is not native startup-time
or peak-memory evidence. The exact UI consumer passed five builds, 869 product
cases and its selected regressions. Android native, MAUI and interaction-test
compilation passed with zero warnings/errors. Startup shell tests, focused
package/source/content checks and private-key hygiene passed. An unrelated broad
legacy contract selection remained red and is documented, not claimed green.

The final separate API 36 x64 SDK-test APK has SHA-256
`dcd510f066f414b7aabe9e32fb7516580b6a162643f812c0205cf49fc5793884`, built from
Android `ff0b7756697376ea82b0acb4ce3be8c9a3ffeb21` and the exact seal below,
with no Core override. Its certificate and 331 embedded content files matched.
In-place upgrade and verified force-stop/new-process reopening rendered the
retained synthetic runner's Creation dashboard. Saved workspace SHA-256 remained
`2ff4347b6641900db048c44c0634fb8d3e02e08ceb2844f031d5835dcdf410ad`
before upgrade, after upgrade and after restart. No Save was replayed or data
wiped. Actual screenshots, non-null hierarchies and process-bound logs are retained.

Workspace restoration took 25.613s and 14.440s; shell initialization took 9.147s
and 6.117s. A boot SystemUI ANR remains recorded. **Native loading is still slow;
no controlled native speedup or complete SR5 Creation is claimed.** The SDK-test
APK predates only version metadata 128-to-129 and evidence; exact ARM64 bundle
checks cover that delta. No book/image provider requests or credits were used.

## Exact local artifact

- Android producer: `9d44dd08b2aa436020683f2e190c1b4e7471a6dd`;
  PR 537 merge: `f98d1ccf48c61cfe91504a5a6d554467e898974c`;
  identical tree: `1adc218b6a8a3fb9534522d3bfe1efd9157dbf1a`.
- Presentation: `7f1e1f7299265571448d449aca30ebc3c549c5f7`;
  Core runtime: `d9f29b059d44954dcaf2a0012ca7851e56b97be9`;
  Core recipe: `70f68382fe45daa0c29a7c4dd27cf1a8cfea505a`.
- UI consumer receipt SHA-256:
  `c324cc2690d95b12ec6bd9979865342ce994a291b7b3bb9542f2f92f24a49eb7`.
- Core package ZIP SHA-256:
  `1afbb897447cd9dc26e9df554ac0e6508df3eec21c81ec0836077181f12e36e2`.
- Unsigned AAB, 33,811,411 bytes:
  `c972af1634e564c12d32c0d70c438e78dbcadac422120dd2ac2c1f6ced397686`.
- Signed AAB, 33,984,074 bytes:
  `3b1b5d469b823e93ac40ad64706e0ebc39c44665f98fd36f3131dbfbd9f480e5`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt SHA-256:
  `86a1f7f6dbd684d21dab1c0f501baac889197035419dcd24c6d68d715b1de014`.
- Independent signed-verification receipt SHA-256:
  `737638431071a385deb8f7501712dd762c9696478d2ee63b3cffbeff6eb91e45`.
- Private Console execution record SHA-256:
  `1ce21396cec8f183850da4206702406bd7783c5f2d37b006eddc26e575ab2cdc`.
- Visually inspected availability screenshot SHA-256:
  `0597b4ba62c7498d4a41674a7a7dcd067595698d2811273dda2bb1f92d04e7d2`.

One offline local ARM64 build used .NET 10.0.112, JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, canonical source exports, 18 packages/13 authority files, 331
embedded content files, package identity/API 24+/target 36/ARM64, privacy,
credential hygiene and Release proof exclusion passed. Mode: locked package
closure with explicit pinned Presentation source and Core content; no ambient
siblings, not package-only. Separate existing-key signing and independent
offline keyless verification passed strict JAR signature, pinned certificate
and exact byte equality of all 1,783 non-signature payload entries.

Required source/safety checks passed before the normal protected merge. One
upload and one final confirmation used standing 2026-10-05 Internal approval,
separately from candidate checks. Browser/OODA account, app, artifact and track
checks preceded actual availability readback. No hosted qualification or bypass
is claimed. Play's missing deobfuscation/native-symbol warnings remain. Catalogue
support is not tablet authority, and the upload certificate is not an observed
Play App Signing certificate. Owned emulator, release containers and browser
are stopped; recovery artifacts and saved data are retained.
