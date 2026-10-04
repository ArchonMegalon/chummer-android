# Preview 103 — prevent late dialogs after page departure

Authenticated Chummer Play Console readback on 4 October 2026 by 20:31:47 UTC
showed `103 (0.1.0-preview.103)` **Available to internal testers**, Internal
release 97, one version code, released at 22:31 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is scoped browser evidence, not Publisher API or physical-device evidence.
**Physical Play 103 installation/update remains unverified.** Version 103 is
consumed; never rebuild/re-upload it. Preview 102 and its evidence are retained.
No Production, tester-audience, listing, billing or account changes occurred.

## Change and focused verification

Native action completion now checks that the originating page appearance is
still current before opening a dialog/error or requesting a Play review. Leaving
and returning creates a new appearance; the old response cannot present UI in
it. Admitted work still completes, overlap is rejected and the action gate is
released. Current-page errors remain visible. No rules, persistence, ownership,
package graph, retries or timeouts changed.

The original-code regression failed on stale modal admission. Fixed actual-MAUI
tests passed 18 cases across normal/conditional action entry, current/departed/
returned appearances and dialog/error outcomes. Adjacent account-loading,
cancellation, recovery, owner/ABA and dialog-busy checks passed. Existing Python
contracts passed 23 tests and 3 subtests. Native Release x64 and ARM64 AAB builds
had no observed compiler warnings/errors. Only version fields differ from the
tested behavior tree; unchanged results are reused, not relabelled as new tests.

A private synthetic API36 diagnostic used real NativePageBase/coordinator,
navigation and Android alerts: departed and away-and-back responses did not open
late alerts, while a current-page response did. Force-stop, process absence and
a new process showed Ready, a persisted diagnostic count of five and enabled
controls. This is **not runner-save, live account or Play signature authority**.
The private diagnostic root and synthetic action are outside Git/release exports.
Managed modal-admission tests use recording navigation, not native modal rendering.

Diagnostic APK SHA-256:
`97303129c9dee099fcd825a24503edd113f69806d238690c83e91645116c5893`.
Emulator 36.3.10/build14472402, API36, existing font scale1.3. Startup System UI
ANR and an initial null cold hierarchy remain recorded limitations; a later
settled observation passed. This is not a performance clearance.

## Exact local artifact

- Android producer: `a62bea0c28cdaadc630d7cc96fb5ca478d5594e6`;
  main merge: `ad9fe294f6ffbae3d2c0534e2b0902f774a9ed2f`;
  identical tree: `2b08c4f44bff7e6f9e96c4788edcbe4da428e183`.
- Behavior PR462 and version-only PR463 merged normally. Required source and
  GitGuardian checks passed; version run37231994201/job111523520023.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,685,629 bytes:
  `8117462bd30d5dfa5be558fe5b62278079217f2ed07864225a7648887a6f56b3`.
- Signed AAB, 33,858,260 bytes:
  `b3d2a90f0b5ade6b7f05681f66605599bc9487a63d7ccd32504f32a5f056371b`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `d27a82c67d61a4bf12681dca138b05a838820bfa1274c9cbe36881af1fd05afd`.
- Independent signed-verification receipt:
  `03f54504d2c154c4ed2328990950abfdf30522a49c0c6c91b2a42dc73e7a0b9d`.
- Private Console execution record:
  `f32b8be09b1506632f434760c1ffd471276e0e1a6f0dead44f89bf51ac78a5eb`.
- Availability screenshot:
  `ca92b29b58a7ad7ecf566dbd3cea7118fafecad3f81bb73f4a150ea8e4553575`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, 18 packages, 331 embedded content files,
package/version/API24+/target36/ARM64, privacy flags, proof exclusion and credential
hygiene passed. Dependency mode is locked package closure with pinned Presentation
source and Core content, not package-only or hosted qualification. Separate
original-key signing passed independent keyless strict JAR/certificate/all1,783
non-signature payload comparisons.

One upload and one confirmed Internal rollout occurred under standing approval.
Device catalogue is unchanged; missing mapping/native-symbol warnings remain.
Live account refresh, the historical Hub503 cause, full SR5/Origin and general-beta
completion remain unverified. No phone, user runner or provider mutation occurred.
Private packets `origin-release103-20261004.Y6r2krZc` and
`native-action-departure-20261004.OxuipT9x` retain exact artifacts and bounded
evidence outside served directories. No credentials/private runner data are here.
