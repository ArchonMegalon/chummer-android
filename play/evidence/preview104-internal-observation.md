# Preview 104 — pause a failed Origin status observer visibly

Authenticated Chummer Play Console readback on 4 October 2026 at 21:35:36 UTC
showed `104 (0.1.0-preview.104)` **Available to internal testers**, Internal
release 98, one version code, released at 23:35 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is scoped browser readback, not Publisher API or physical-device evidence.
**Physical Play 104 installation/update remains unverified.** Version 104 is
consumed; never rebuild/re-upload it. Preview 103 and its evidence are retained.
No Production, tester-audience, listing, billing or account changes occurred.

## Change and focused verification

An unexpected Origin chapter-status read failure now stops the pinned spinner
and explains the pause. It does not rebuild saved prose or export controls.
Explicit reading recovery clears the stale pause label without replaying paid
generation. No rules, persistence, ownership or package graph changed.

The original-code regression reproduced a stopped observer leaving its spinner
running. Final actual-MAUI/Core local-text and automatic-mode checks passed:
visible pause, unchanged prose/export controls, explicit recovery, no paid replay,
export availability only with saved complete prose, and unchanged runner data.
Five native-theme contracts and the Release x64 build passed. Managed and native
build logs report zero warnings/errors. The version-only candidate reuses these
unchanged behavior checks; they are not represented as fresh hosted qualification.

A synthetic offline API36 book showed its complete 13,769-character chapter
before and after force-stop, verified process absence and a new process.
Effects appeared only after explicit reading confirmation. Runner bytes remained
unchanged; reading state persisted. Actual Android DocumentsUI saves produced
EPUB and HTML whose decoded prose, including the closing paragraph, exactly
matched the complete chapter. The emulator route did not inject the unexpected
status exception; that case is covered by the managed regression.

- SDK-test diagnostic APK:
  `b53640dfbf78f801be57cdba3038b899c7d48476a4f0da5d60042e1ed141552e`.
- Complete chapter text:
  `7fa6e555d2dfe3242ae11ac6b5906543eb279a496284fc8b55e3d45c87eebf4e`.
- EPUB, 2,681 bytes:
  `e508e964ee22666a54b745216d0650b9ece6e7b67f17eef57badc2fa1f365a8e`.
- HTML, 14,868 bytes:
  `a15c415592ff09fcbf49dc19a611ad075858cdc0dd89e90b94b780fd58495d7c`.

Emulator 36.3.10/build14472402, API36 x64, font scale1.3. Startup system-service
ANRs remain recorded limitations, not performance clearance. This is synthetic
local-book evidence, not live FirstBook generation, linked-account recovery or
physical Play installation. No provider credits or user-runner data were used.

## Exact local artifact

- Android producer: `6c28a95e47c0a476fa9e3d83c0fb299f8d59c771`;
  main merge: `1e16afc0ac7deabd8417c0e5f0e7fbb326117477`;
  identical tree: `6fb2eecb92039b12f8bcd03bc603cf4e25f81b6c`.
- Behavior PR465 and version-only PR466 merged normally. Required source and
  GitGuardian checks passed; version run37235996686/job111535093471.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,685,749 bytes:
  `b664d3c31cd01ff27760026b18743154d9ee1bd73fb55e6cebb6c35af5f3ca83`.
- Signed AAB, 33,858,370 bytes:
  `2829aea813626e91b0322ad94f40b8a0c5eec4bb1fcb2ae1db3b18453cd11099`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `1660d38575f0ced448b5c3ea1fb572200adc3cbb0460f488f6b8696d6528869b`.
- Independent signed-verification receipt:
  `ad667cb827e492879c8e2837fd3e4802f751faf0c1c61f2c767ef1b1a60aa783`.
- Private Console execution record:
  `782006ce930e9a5ce93939849551b0b01514b56357fe723542ef1fbe9b527984`.
- Availability screenshot:
  `11fa4441dd6bbd1ec4668b7bedde8e9e4d3fa6e000deb29a0992cb5f703632ae`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, 18 packages, 331 embedded content files,
package/version/API24+/target36/ARM64, privacy flags, proof exclusion and credential
hygiene passed. Dependency mode is locked package closure with pinned Presentation
source and Core content, not package-only or hosted qualification. Separate
original-key signing passed independent keyless strict JAR/certificate/all1,783
non-signature payload comparisons. No compiler warnings/errors were observed in
the candidate build log.

One upload and one confirmed Internal rollout occurred under standing approval.
Device catalogue is unchanged; missing mapping/native-symbol warnings remain.
Live account refresh, the historical Hub503 cause, live-provider end-to-end Origin,
full SR5/Origin and general-beta completion remain unverified. Private packets
`origin-release104-20261004.brYbSwvg` and
`origin-reader-status-error-20261004.vFXBT657` retain exact artifacts and bounded
evidence outside served directories. No credentials/private runner data are here.
