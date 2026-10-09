# Preview 162 — German Magic catalog display

The authenticated Chummer Console showed `162 (0.1.0-preview.162)` **Available
to internal testers**, one version code, release 156, following one upload and
one final confirmation on 9 October 2026 at 14:22 Europe/Vienna (12:22 UTC).
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API evidence. **Physical Play 162
installation/update is unverified.** Play says distribution usually appears
within one hour but may take longer. Version 162 is consumed; do not rebuild or
re-upload it. Previous artifacts and evidence remain unchanged.

## Change and focused verification

German display resources cover 585 canonical Magic/Resonance options: 74
traditions, one stream, 109 adept powers, 363 spells and 38 complex forms.
Talent, metatype and category captions bring the resource set to 644 keys.
Resources are deterministically materialized from exact pinned Core data;
19 absent/renamed legacy labels have explicitly authored short-name fallbacks.
Matching requires kind, ID and canonical original name. Unknown/custom/renamed
rows keep their own label. Search accepts German names, original names and
source-book labels. Rules, costs, ordering, IDs and stored data are unchanged.

Twenty focused Python checks and deterministic resource verification passed.
Managed locale tests cover all five catalog kinds, localized/original/book
search, custom-identity isolation, fallback and independent region formatting.
The existing real-store Magic budget-focus tests passed, including one-shot
focus, owner ABA, confirmed save and cold reopen. Managed and native x64 builds
reported zero warnings/errors.

The final-source API 36 Release x64 smoke used the retained SDK-key synthetic
package `com.myexternalbrain.chummer.readvalidation156`. New process 4524
reopened the selected runner with German/Austria settings. Historical review
showed Spurloser Schritt, Adrenalinschub, Wandlaufen, Geschossparade and Leichter
Körper, with unchanged levels and `Adeptenkraftpunkte: 5,00 / 5`. Default
technical details stayed collapsed with no exposed GUIDs or source anchors.
Back was selected without confirmation/save. After force-stop, all 33 runner
files (9,134,113 bytes) were byte-identical. The owned emulator was stopped.

An earlier diagnostic APK exposed untranslated names in the separate historical
review page. The same increment fixed that display path and added its regression
check. The earlier unsigned diagnostic AAB is marked NOT_FOR_RELEASE and was
never signed or uploaded. Only the corrected final artifact below was released.

Limitations: native coverage is read-only historical review, not a fresh native
catalog-search/selection/confirmation flow. Startup remains slow: shell 41.017s,
selected-workspace restoration 43.076s; one null hierarchy during startup has a
retained screenshot. The system accessibility label follows the English system
locale, and the restored-runner status is still English. **Whole-app German
localization, full Creation/Career and physical Play installation remain open.**
No startup speedup, seven-journey or new chapter/provider-generation claim.
Windows, provider credits and unrelated user services were untouched.

## Exact local artifact

- Android producer `0ce5f305d37a4ce92d97727e6a760c66aa6ab0eb`;
  protected PR 609 merge `cf21fb14a8eb861b83b7d404d3ba4391293adeb5`;
  identical tree `65be9de7e901c3b30ed87b1c4bb74dcda1b80024`.
- Presentation `b5653f408d8f4794ad278c334ea965379972c798`,
  seal `2a6e88e3276e2158edadf1e1b608e362c3a7f0d2`.
- Core runtime `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`,
  recipe/content `b9daf8e559edb8110dfc3ce5a003ae08c540a696`.
- UI verification `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.
- SDK-test APK `94e051cc86066a6eb76b4c3a91c295047ed9392a6125f8a3f05ea0e667698add`.
- Native smoke receipt `7799df8d8f8b1b4ccb745ff758455f15671b4c1fcb8912ac0e54daad056e96db`.
- Unsigned AAB, 35,272,589 bytes:
  `ba13485781993200ae41b04b74a68e387a383f0e99d3c2cc5a2a1584fe21a9a6`.
- Signed AAB, 35,445,243 bytes:
  `536069ca9f07ef6f885ae41537e6108eee998a73c45b6865de7b8a1280519c0e`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `6dfa9b4dd1bf5fa54fce2e8499aa69262649a5bdb7e3cf42cde94fe2c20911d0`.
- Independent keyless verification receipt:
  `effd662aebc90c2e3acbd628044b2fb2cae19587cd8bdd925fb283a914c6e3f6`.
- Visually inspected availability screenshot:
  `b8ddcd55f7a782c6c69f3a3e06eff13fec5c5a21ceae3f2de58b611c09162430`.

Final offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and builder
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, all 331 content files, bundle/version/API,
proof exclusion and private-key hygiene passed. Assembly uses locked packages
plus explicit pinned Presentation source and Core content, not package-only.
Separate offline original-key signing and independent keyless strict JAR,
certificate and all 1,783 unchanged payload-entry checks passed.

The initial capacity guard incorrectly attributed a separately throttled user
service's global PSI to Docker. Candidate-local helpers now check the verified
systemd-v2 Docker slice with the same 10% full-PSI limit, a 6 GiB available-memory
floor and the existing disk/container limits. The retained diagnosis includes
actual cgroup and disk observations; no unrelated service was stopped or changed.

Standing owner approval 2026-10-05 authorized the Internal upload separately
from candidate checks. Browser/OODA verified the live owner/app/track. Play
recognized 162, API 24+, target 36 and ARM64 with no blocking error or device
delta. Missing mapping/native-symbol warnings remain. No action was replayed;
no public-track, audience, account or security change was made. Private packets
remain retained outside served directories.
