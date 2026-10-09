# Preview 157 — selectable app language and regional formats

Authenticated Chummer Play Console readback in the `2026-10-09T09:20Z` minute
showed `157 (0.1.0-preview.157)` **Available to internal testers**, one version
code, Internal release 151, released 9 October at 11:20 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API evidence.
**Physical Play 157 installation/update remains unverified.** Play says changes
usually appear within one hour and can take longer. Version 157 is consumed:
do not rebuild or re-upload it. Previous artifacts and evidence are unchanged.

## Change and affected verification

More → Settings → Language and region now has real selection controls instead
of explanatory text. App language (English, German, Spanish or the phone setting)
and regional date/number formats can be selected independently. A live format
preview shows the selected region. Save persists both phone-local choices in
one bounded preference value; a new app process applies them before pages are
created. Back without saving discards the selection. The app explicitly asks
the user to restart after saving. Book language, rule choices and runner data
are unchanged; existing settings revision checks remain in place.

Focused managed checks passed for separate UI/format cultures, real localized
resources, background culture defaults, system reset, unsupported UI-language
fallback, malformed preferences and invalid saves without writes. An initial
test expected the wrong Austrian grouping separator; the test oracle was
corrected against CultureInfo (nonbreaking space), and the focused rerun passed.
The managed check and final-source native build completed with zero warnings
and errors. No Core/UI source or package reseal was needed.

The API 36 x64 smoke upgraded the retained SDK-key diagnostic package from 156
to 157, preserving its 33 synthetic workspace files. Selecting German/Austria,
then leaving without saving, reopened with the original phone defaults. Selecting
them again showed `21.02.2026 · 1 234,56`. After explicit save, force-stop verified
the old process was gone; a new process reopened German UI and retained both
selections and the regional preview. All 33 runner files were byte-identical.
Native English/Spanish/reset paths were not separately replayed; they have the
focused managed coverage above. This is not a production-package or Play-signing
upgrade test. A System UI Wait dialog at emulator boot and one transient
null-root accessibility observation are disclosed. Startup remained slow; no
speedup claim is made. The owned emulator was stopped after the smoke.

Existing full-chapter and per-chapter-image [coverage](../../docs/ORIGIN_EPUB_AND_SCENES.md)
is unchanged, not a fresh provider generation. Complete Creation/Career and
physical installation remain open.

## Exact local artifact

- Android producer: `7f92e9395892a664ac1b15cb790ba26221892b98`;
  protected PR 599 merge: `deed66a1b3ae06f4244ec0a6bad84b693e606dfa`;
  identical tree: `525d3d510a829d5a338b02d340bad2b9e3d1f42a`.
- Actual Presentation consumer: `b5653f408d8f4794ad278c334ea965379972c798`;
  published seal: `2a6e88e3276e2158edadf1e1b608e362c3a7f0d2`;
  protected PR 385 merge: `56704c3334c0a3b43efb32a00fe08cb2ee0f74f4`;
  identical tree: `1c6fd28b794e29d443317d2317d2578686f196b9`.
- Core runtime: `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`;
  recipe/content: `b9daf8e559edb8110dfc3ce5a003ae08c540a696`;
  protected PR 139 merge: `2522a896f5c4fb35f60fc7f5eb19000f475374eb`.
- UI verification: `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.
- SDK-test APK: `0028817eddce822a5ba0d40a3fa4c5990115e3377573a8b7acb05d8f084bd9da`.
- Native smoke receipt: `7cab6db0be3810ec81df159e10ff6154e2f47b35cb3833c7f5fb68d470702723`.
- Unsigned AAB, 34,974,613 bytes:
  `e329880e7578e4bb5903ba3380ef929225371b9694e59dbbbf064a0a3d2b2a07`.
- Signed AAB, 35,147,243 bytes:
  `905247e11096f43a5c4688c6fa771d876e7ff6562ae4db06e7eea389f1f7a16c`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `80a3d9b5c4094b176c4adf5ff019e9a36d527989200ddaf6e1b49004bd2e83e5`.
- Independent keyless verification receipt:
  `f06f5a98fdea487da585149a88efe8dc07ee605d3dd059b75710dffe8eb6c4a3`.
- Private Console readback record:
  `feef48ff4362776dc9e6b1f23ae7a304e63d85f6d498f8654e05336d26b92b67`.
- Visually inspected availability screenshot:
  `14b250e52a57038f4d288d99f36d032158ee2314ae625ec8d99ae8bc2d9fbe9f`.

One offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, all 331 content files, bundle/version/
API/ARM64 inspection, proof exclusion and private-key hygiene passed. Dependency
mode is locked packages with pinned Presentation source and Core content, not
source-free package-only assembly. The separate offline signer used the existing
upload key. Independent keyless strict JAR/certificate verification and all
1,783 unchanged unsigned payload entries passed. Raw packets and keys remain
private; pre-upload stage receipts retain their original status.

One upload was observed processing at 09:18:36 UTC, and final confirmation at
09:20:16 UTC, under standing owner Internal approval of 2026-10-05. Candidate
verification is separate from approval. Browser/OODA checks verified live
account/app/track identity and availability. Play recognized version 157,
API 24+, target 36 and ARM64, with no blocking validation errors or supported-
device delta. Missing mapping/native-symbol warnings remain. No uncertain action
was replayed. No audience, public-track, listing, account or security change was
made. The owned browser session was closed.

This is not finished-app, seven-journey, full beta, tablet, Full Editing, Windows
or new live-book-generation authority. No extra full suite or rebuild was added
for this settings-only increment; unchanged Core/UI evidence was reused.
