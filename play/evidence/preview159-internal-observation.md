# Preview 159 — German Skills display and independent regional formatting

Authenticated Chummer Play Console readback in the `2026-10-09T10:52Z` minute
showed `159 (0.1.0-preview.159)` **Available to internal testers**, one version
code, Internal release 153, released 9 October at 12:52 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API evidence.
**Physical Play 159 installation/update remains unverified.** Play says changes
usually appear within one hour and can take longer. Version 159 is consumed:
do not rebuild or re-upload it. Previous artifacts and evidence are unchanged.

## Change and affected verification

Skills editor, confirmation and historical re-review now resolve active-skill,
skill-group and knowledge-skill budget headings through canonical display
resources. Linked attributes use localized names. Numeric displays follow the
selected regional format independently of app language, including the shared
Creation allocation formatter. Explicit culture overrides and custom attribute
labels remain supported. Original typed budget IDs, automation/admission values,
rules, persisted runner formats and Core/UI pins are unchanged.

Thirteen focused locale source tests and final managed locale checks passed.
Managed and native builds completed with zero warnings/errors. Coverage includes
DE/US, EN/AT and ES/DE formatting, explicit culture overrides, canonical German
attribute labels and custom fallbacks. Existing unchanged Skills source checks
also passed. An initial native diagnostic found English headings on historical
Skills re-review; that gap was corrected before the final build. The earlier
unsigned diagnostic artifact was marked not for release and never signed or
uploaded.

The final-source Release/API 36 x64 smoke upgraded the retained SDK-key package
`com.myexternalbrain.chummer.readvalidation156` to version 159. A new process
restored German/Austria settings and the synthetic saved runner, then displayed
`Aktive Fertigkeiten`, `Fertigkeitsgruppen` and `Wissensfertigkeiten`, with the
expected unchanged point totals on historical re-review. After force-stop, all
33 fixture files (9,134,113 bytes) were byte-identical. No allocation, save,
confirmation or mutation replay was submitted. Editor/confirmation and linked
attribute changes have source/managed coverage, not a separate native mutation
flow. The owned emulator was stopped. The boot-time System UI Wait dialog is
disclosed; no app ANR or startup speedup is claimed. This is not a physical
Play-signing upgrade test.

Remaining English catalog names and technical source references are not claimed
fixed. This increment does **not** establish full German app/book/catalog
localization, finished Creation/Career or seven-journey qualification. Startup
remains slow. Existing full-chapter and per-chapter-image
[coverage](../../docs/ORIGIN_EPUB_AND_SCENES.md) is unchanged, not fresh provider
generation. Windows and provider-credit budgets were untouched.

## Exact local artifact

- Android producer: `c15f56c052ddca35bc6cd7f38ddb23dc23d7ae15`;
  protected PR 603 merge: `4f679fbd85ad375dc2416a44eca0c9f39a95595b`;
  identical tree: `c5490d86a5272411c165ef21f3d3a3f8b9b20b34`.
- Presentation consumer: `b5653f408d8f4794ad278c334ea965379972c798`;
  seal: `2a6e88e3276e2158edadf1e1b608e362c3a7f0d2`;
  protected PR 385 merge: `56704c3334c0a3b43efb32a00fe08cb2ee0f74f4`.
- Core runtime: `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`;
  recipe/content: `b9daf8e559edb8110dfc3ce5a003ae08c540a696`;
  protected PR 139 merge: `2522a896f5c4fb35f60fc7f5eb19000f475374eb`.
- UI verification: `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.
- SDK-test APK: `e000dd755635ddc34861c9d2bb7eb10009eddb0cbb0c8d3075d12c622d96f9f0`.
- Native smoke receipt: `19ecb5983efda6c88cccdf551d74fc63ef4ab03441c27bab2ff7fbad91ec3636`.
- Unsigned AAB, 34,982,326 bytes:
  `b9c1d25d7af047f624731b2d5d65200c6a7177611a107235140657b599eac357`.
- Signed AAB, 35,154,971 bytes:
  `84522adb121a4c6ca4abbef9bcf1a632ad5a3eeae0ccedf5e822433000042c86`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `c5a28dedcc37c88e2c67d9665082bcb4fea1777e487b9b8755982f2f9fcd3282`.
- Independent keyless verification receipt:
  `1aae01e7d6d738be6d02e38970463a14cd1a02e2c7e47be5c9d9b8f5d74b5dee`.
- Private Console readback record:
  `c36464efbee5bb3638e340a59508f30a2408ae4ec5d51a424c194b9a10b302e6`.
- Visually inspected availability screenshot:
  `1220c44eece2a6d5a23aa36c7addeff4e578693cce2a044ca06a03c82e107035`.

The final-source offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and
image `sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports, all 331 content files, bundle/version/API/ARM64
inspection, proof exclusion and private-key hygiene passed. Dependency mode is
locked packages with pinned Presentation source and Core content, not source-free
package-only assembly. The isolated offline signer used the existing upload key.
Independent keyless strict JAR/certificate verification and all 1,783 unchanged
unsigned payload entries passed. Raw packets and keys remain private; pre-upload
stage receipts retain their original status.

One upload was observed processing in the 10:49 UTC minute, with final Internal
confirmation and availability in the 10:52 UTC minute under standing owner
approval of 2026-10-05. Candidate checks are separate from approval. Browser/OODA
checks verified live account/app/track identity and availability. Play recognized
version 159, API 24+, target 36 and ARM64, without blocking validation errors or
supported-device delta. Missing mapping/native-symbol warnings remain. No
uncertain action was replayed. No audience, public-track, listing, account or
security change was made. The owned browser session was closed. No broad suite
or rebuild was added for unchanged dependencies or this documentation update.
