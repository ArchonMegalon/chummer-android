# Preview 94 — stable Origin exports during illustration completion

Authenticated Chummer Play Console readback at `2026-10-04T11:33:06Z` showed
`94 (0.1.0-preview.94)` **Available to internal testers**, Internal release 88,
one version code, released at 13:33 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API or physical-device evidence.
**Physical Play installation/update of 94 is not yet verified.**
Version 94 is consumed; never rebuild or re-upload it. Preview 93 and its
evidence remain unchanged. No Production or tester-audience change occurred.

## Bounded change and verification

Origin now admits an immutable book snapshot when EPUB or HTML export starts.
If automatic illustration completion refreshes the selected book while Android
Save As is open, the admitted export completes. A later export includes the new
artwork. Page-lifetime, full owner-stamp, workspace/revision and old-edition
guards remain; this does not accept a chapter or generate another paid book.

The focused managed regression reproduced the race before the fix. The corrected
actual-MAUI test passes the in-flight export, later illustrated EPUB, cold scene
reopen and stale-owner/edition/Save guards. Related existing checks passed;
managed and native builds reported zero warnings/errors. Initial test-setup
failures were retained and are not described as product regressions or passes.

An isolated synthetic/offline API36 smoke used real DocumentsUI Save As for both
EPUB and HTML. Both files exactly contained all 13,769 selected prose characters.
Verified force-stop and a new process restored the same full reader and export
controls; workspace and selected-reading hashes remained unchanged. The image
completion race is covered by the managed test, not a paid native-provider run.
The diagnostic APK used an SDK test key and isolated app ID, not the release
upload identity. Emulator System UI startup ANRs and a transient empty picker
hierarchy were retained: this is functional, not performance evidence. The same
picker was observed again without replaying the export action.

These behavior results bind PR 435's unchanged code. Preview 94 adds only its
version name/code and has its own ARM64 Release build and artifact inspection.
Neither is a physical Play94 test or complete seven-journey qualification.
Complete live Origin, all-method SR5 and general-beta completion remain open.

## Exact local artifact

- Android producer: `f57da54344d9dca836ae13a8b8dc5d96e2454a24`;
  main merge: `1f67709e8c2f5a7b62ea5a5e2a9acacb057c6364`;
  identical tree: `577725cd50e57a106679ee4f2136565d5c4d3f44`.
- Product PR 435 and version-only PR 436 merged normally with source/safety and
  GitGuardian checks passing. No protected check was bypassed.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`;
  tree: `557d0486b7fb4a1d7d10cb6f79c910bd8d0de895`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,666,113 bytes:
  `1cbf3b00625efc3b842b347b14089f3bd6a82621826719365cda61538927885d`.
- Signed AAB, 33,838,759 bytes:
  `0afbdb51f1d2eb81f73e2420fbfa94d43241f1933d20ef9c52c33aae2d61a1c8`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `54c2bd95ce1a86044bae4baf708335b041070425a81435dacbf3b0a293561e03`.
- Independent signed-verification receipt:
  `1ad088fa38d8f242fb0ab6c915aa1fd0d158e53abb2337fae161b994e4ba3b17`.
- Private Console execution record:
  `040235b57f851ec31e5ec3ac50bbd4d2694dc2bddf40f0ad24517fd1cbdf8c43`.
- Availability screenshot:
  `1d3ad69e3125b9335ba9b051450fb35cc037982f308866394c291695f24b4784`.

Execution was local offline Docker with .NET 10.0.112, JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports, 18 packages, 331 embedded content files,
package/version/API24+/target36/ARM64, proof exclusion and credential hygiene
passed. Dependency mode is locked package closure with pinned Presentation
source and Core content, not package-only or hosted qualification.
Separate original-upload-key signing passed independent keyless strict JAR,
certificate and equality of all 1,783 non-signature payload entries.

One upload and one confirmed Internal rollout occurred under standing approval.
Play reports unchanged device coverage and the existing two diagnostic warnings:
missing deobfuscation mapping and native debug symbols. These remain deferred;
no platform or signer control was weakened. Existing ADB reported no connected
device. No provider credits were spent; historical uncertain FirstBook jobs
remain fenced.

Private packets `origin-release94-20261004.AjS3j9Cb` and
`origin-reading-flow-20261004.PIrxqdyu` retain build, signature and test evidence
outside served directories. No credentials or private character data are public.
