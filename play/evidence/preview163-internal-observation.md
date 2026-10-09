# Preview 163 — localized runner status messages

The authenticated Chummer Console showed `163 (0.1.0-preview.163)` **Available
to internal testers**, one version code, release 157, following one upload and
one final confirmation on 9 October 2026 at 14:47 Europe/Vienna (12:47 UTC).
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API evidence. **Physical Play 163
installation/update is unverified.** Distribution usually appears within one
hour but may take longer, according to Play's confirmation dialog. Version 163
is consumed; do not rebuild or re-upload it. Previous evidence is unchanged.

## Change and focused verification

The phone display now localizes exact canonical runner-restoration, saved,
settings-saved, account-recovery-pending and workspace-verification notices in
English, German and Spanish. Restored dossier counts have singular/plural
resources. Unknown errors, partial-restoration warnings, custom text and malformed
counts remain unchanged; the adapter cannot turn an error into a success message.
Canonical shared notices, rules, ownership checks and saved runner data are
unchanged. Existing unsaved-workspace switch guidance retains its separate path.

Existing managed locale tests passed, including DE/EN/ES, zero/one/eleven
restored dossiers and exact-match negative cases. Managed and native x64 builds
reported zero warnings/errors. The final-source API 36 Release x64 smoke updated
the retained synthetic SDK-key package, started a new process and restored the
selected runner with German/Austria settings. Home visibly displayed
`11 Runner-Dossiers wiederhergestellt.` The final settled screenshot and
hierarchy agree. After force-stop all 33 retained runner files (9,134,113 bytes)
were byte-identical. The owned emulator was stopped before final ARM64 packaging.

Coverage is read-only restored-runner feedback, not a new native account recovery,
save mutation or complete wizard. Other notice translations have managed tests.
Startup remains slow (shell initialization 89.838s, selected restoration 65.593s).
An Android System UI ANR during emulator startup and earlier incomplete hierarchy
captures remain in the packet; they are not passing app screenshots. The owned
ARM64 build was paused, the owned emulator's 4 GiB memory cap was increased to
6 GiB after checking host capacity, and System UI's Wait action was selected.
There was no OOM, data reset, shared-service change or app-code retry workaround.
English synthetic runner names remain user data, not untranslated UI captions.
**Whole-app German localization, full Creation/Career and physical Play 163
installation remain open.** No seven-journey, speedup or book-generation claim.

## Exact local artifact

- Android producer `1089d2ed648c57da6c13b5400ca75b4aa7b7b823`;
  protected PR 611 merge `aca21a8e6187f0a5287ed9378035b934544743e6`;
  identical tree `3f27697cdf0797a74f475c1638efc3ed37c7dab5`.
- Presentation `b5653f408d8f4794ad278c334ea965379972c798`,
  seal `2a6e88e3276e2158edadf1e1b608e362c3a7f0d2`.
- Core runtime `1df475aa5c4c90da0f9570ca8ac18721a3c26cf0`,
  recipe/content `b9daf8e559edb8110dfc3ce5a003ae08c540a696`.
- UI verification `c5d4625265419f6763191b396c0487f0d560c8d864e8d21547774cd30d12032a`.
- SDK-test APK `a2c28d7af83634bfc7ed7ff056692681742bf1b783d7c9c9aa7e82e8ec5cdb83`.
- Native smoke receipt `359fb47e209d3d40139373e688810423dade00c146c9cdb6fa2c12a8602f46c6`.
- Unsigned AAB, 35,274,469 bytes:
  `1d5382c224ab389b50a53eaff84702cee138937d702ed0512a784b1627590a87`.
- Signed AAB, 35,447,095 bytes:
  `bbd93e967a6138986a56db52a2aa2137a8779756b98f56d0c5f1e135819b809a`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `7a3ebabf3089fa4fad09c878468be1d7e8ada4411fff9f55a47f97ac7fba90c8`.
- Independent keyless verification receipt:
  `9d61c58e333e9b50f7a161b3dea540c290f3bdcea4b1c961abe9ae8efc64f46e`.
- Visually inspected availability screenshot:
  `900ef74515ad8786c26b313d4694d08bbee534020952ef707659926917441631`.

Offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and builder
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, 18 packages, all 331 content files,
bundle/version/API checks, proof exclusion and private-key hygiene passed.
Assembly uses locked packages plus explicit pinned Presentation source and Core
content, not package-only. Separate offline original-key signing and independent
keyless strict JAR, certificate and all 1,783 unchanged payload-entry checks passed.

The standing owner approval of 2026-10-05 authorizes this Internal upload
separately from the candidate checks. Browser/OODA verified the live owner/app/
track. Play recognized 163, API 24+, target 36 and ARM64, with no blocking error
or supported-device delta. Existing mapping/native-symbol warnings remain.
No public track, audience, account, provider
credit, Windows or unrelated-service change is in scope. Prior release artifacts
and evidence remain unchanged; private packets are retained outside served paths.
