# Preview 164 — German quality catalog names

The authenticated Chummer Console showed `164 (0.1.0-preview.164)` **Available
to internal testers**, one version code, release 158, after one upload and one
final confirmation on 9 October 2026 at 15:39 Europe/Vienna (13:39 UTC).
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API evidence. **Physical Play 164
installation/update is unverified.** Play says distribution usually appears
within one hour but may take longer. Version 164 is consumed; do not rebuild or
re-upload it. Previous evidence and rollback artifacts are unchanged.

## Change and focused verification

The native Creation quality catalog uses 803 German display names from the
exact Core content. Display and search are guarded by both typed ID and original
name: unknown/custom/mismatched entries remain unchanged. Search accepts German
and original English names; help, configuration and review headings use the same
display adapter. Costs, IDs, ordering, mutations and saved rules data are unchanged.
Generation is deterministic; duplicate Tattoo Magic and the renamed Shoot First
entry have explicit reviewed mappings. No rulebook description was added.

Twenty focused Python tests and the existing managed phone-locale tests passed,
including de-AT, original-name guards, search and fallback languages. Resource
regeneration matched checked-in bytes. Managed and native Release x64 builds
reported zero warnings/errors. A local API 36 emulator smoke used the SDK-test
package, not the Play artifact, with German language and Austria formatting.

A fresh synthetic Priority runner reached the catalog through normal prerequisite
selection and an empty saved attribute draft. The catalog visibly showed German
names; submitting `beidh` and `Ambidextrous` each returned `Beidhändigkeit`.
The `!` help showed its German heading and existing short summary. Configuration
showed the same name, rating 1 and +4 Karma. No quality was added. All 33 original
fixture files remained byte-identical; only the three new synthetic runner files
were created. The owned emulator was stopped before final packaging.

This is not full Creation-to-Career or seven-journey coverage. The synthetic
runner deliberately retained unspent attribute points. Older fixture drafts
were not repaired or admitted by bypassing guards. Cold emulator startup showed
an Android System UI ANR (Wait selected once), and some transitional hierarchy
reads had no root; these remain recorded separately from the successful final
display. No app-performance improvement is claimed. **Whole-app German
localization, full Creation/Career and physical Play installation remain open.**
Known remaining English surfaces include New runner fields and Core prerequisite
rank/metatype/talent descriptions. Book generation and illustrations are unchanged.

## Exact local artifact

- Android producer `ecff3c8bf594522445a0e1d2263206775a1d79cd`;
  protected PR 613 merge `4741de3908a4d7f41316cf6a7e52b120461d6739`;
  identical tree `2e2b7e2a4376a4346face7dc53c3103d2d00a1ce`.
- Presentation `b5653f408d8f4794ad278c334ea965379972c798`,
  seal `2a6e88e3276e2158edadf1e1b608e362c3a7f0d2`.
- Core runtime `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`,
  recipe/content `b9daf8e559edb8110dfc3ce5a003ae08c540a696`.
- UI verification `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.
- SDK-test APK `b906bcfda3eb5ef5bc42e9041b6cb44369e14ebe7171003ba6899c34baf5138d`.
- Native smoke receipt `ab0a8c3e4572d94a22d2a601a6e91e2015594c46a82efc3610883030593d1151`.
- Unsigned AAB, 35,354,407 bytes:
  `1e0994020d2baaae9ba663fffebfbe8cdb2f0bf9c3bffe8704073e41d98805b5`.
- Signed AAB, 35,527,054 bytes:
  `4157a338bfa9d4d8a777f4c0d1ab26f3708d17a957c90a2a290528bc3f1fd9ea`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `342f8fe340fdf332a132344031174e7dd8534ac23c07129555087c72820b6ba3`.
- Independent keyless verification receipt:
  `72cdecd76ec76ca34eb6dff828d760a27721a3ae5e469abf70bed98ea43d0e83`.
- Visually inspected availability screenshot:
  `54305295587a0dbdc3533b27387effbd34610f76257e573e6d8aa5f4d8b3f3c9`.

The offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and builder
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, 18 packages, all 331 content files,
bundle/version/API checks, proof exclusion and private-key hygiene passed.
Assembly uses locked packages plus explicit pinned Presentation source and Core
content, not package-only. Separate offline original-key signing and independent
keyless strict JAR, certificate and all 1,783 unchanged payload-entry checks passed.

Standing owner approval of 2026-10-05 authorizes this Internal upload separately
from those checks. Browser/OODA verified the live owner, app and track. Play
recognized 164, API 24+, target 36 and ARM64 without a blocking error or supported
device delta. Existing mapping/native-symbol warnings remain. No public-track,
tester/audience, account, provider-credit, Windows or unrelated-service changes.
Private build, signer and readback packets remain outside served directories.
