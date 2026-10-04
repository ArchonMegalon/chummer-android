# Preview 102 — clear Life Modules skill-removal actions

Authenticated Chummer Play Console readback on 4 October 2026 at 19:36:17 UTC
showed `102 (0.1.0-preview.102)` **Available to internal testers**, Internal
release 96, one version code, released at 21:36 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is scoped browser evidence, not Publisher API or physical-device evidence.
**Physical Play 102 installation/update remains unverified.** Version 102 is
consumed; never rebuild/re-upload it. Preview 101 and its evidence are retained.
No Production, tester-audience, listing, billing or account changes occurred.

## Change and focused verification

Life Modules now distinguishes removing a specialization from removing all added
choices for that skill. Both actions wrap and have distinct English/German labels.
Help explicitly says that module-granted benefits remain. No handler, rules,
ownership, persistence, dependencies, retries or timeout changed in this patch.

The original ambiguous-label regression failed. Final actual-MAUI/Core tests and
Release x64 compilation passed with zero warnings/errors. Cases cover both
cultures, active/knowledge skills, specialization removal retaining the rating,
whole allocation removal retaining other choices and module-runner bytes, cold
draft-session reopen/re-add, exotic identities and once-only Career finalization.
Only version fields differ from the tested tree; unchanged behavior results are
reused, not represented as new tests of changed logic.

The synthetic API36 phone route, emulator 36.3.10/build 14472402 at font scale 1.3,
showed readable English actions/help. Removing a specialization retained Karma
level 1; removing the whole skill allocation preserved four other choices and
knowledge budget 2/29. Pending module-runner bytes and revision 7/7 stayed intact.
Verified force-stop, process absence and a new process reopened completion/Skills
with the removed allocation absent and identical persisted input bytes.
German is managed-tested, not newly device-tested. Native Career finalization
was not repeated in this slice; focused managed coverage includes it.

Release x64 diagnostic APK SHA-256:
`fe11e6c3e8537a04d5707a1e0c49e7714726effe29f07bc52c7eb5f5d57a0579`.
It used a separate application identity and SDK test key, not the Play ARM64
signature. System UI startup ANRs, transient null accessibility hierarchies and
slow initial rule checking remain recorded limitations, not performance clearance.

## Exact local artifact

- Android producer: `669d8d337f14fb41a5a2681cb528f8bf0f0ab30d`;
  main merge: `1740674fae55293012386efaef33d957331a3e11`;
  identical tree: `889e4fec500065a3ff9a8e4d09245bb1fc22eb8d`.
- Behavior PR459 and version-only PR460 merged normally. Required source and
  GitGuardian checks passed; version run37228520429/job111513091897.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,685,717 bytes:
  `d6c8b3bd9d6c9e479155ab05a9ffef5745a252d8ecab1f2b15bd1ae5895356f7`.
- Signed AAB, 33,858,354 bytes:
  `670975322199db9b2cee2940291bc58861becdab7b90c6c7ebd6f517a9ff31dc`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `6eed5a59d7916726d21b6ad29723cefda3d922916a0b16fc17ae10a5e76520d6`.
- Independent signed-verification receipt:
  `655ecb3cd8d4b1e329dd0cb40b211130204298994bfc242eb4952c834bb87442`.
- Private Console execution record:
  `c6dd5afe7d2e36e8f5037b99149fc6e80796893142fc4d52fbb91957de6c2339`.
- Availability screenshot:
  `c0026cdc76d8294ecc8fe55eed7c4c9caa53cb8faabf0a2617a828f5c5160514`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, 18 packages, 331 embedded content files,
package/version/API24+/target36/ARM64, privacy flags, proof exclusion and credential
hygiene passed. No compiler warnings/errors were observed. Dependency mode is
locked package closure with pinned Presentation source and Core content, not
package-only or hosted qualification. Separate existing-key signing passed
independent keyless strict JAR/certificate/all 1,783 payload comparisons.

One upload and one confirmed Internal rollout occurred under standing approval.
Device-catalog coverage is unchanged; missing mapping/native-symbol warnings
remain. Live account refresh, the historical Hub503 cause, full SR5/Origin and
general-beta completion remain unverified. No phone, user runner or provider
mutation was attempted. The owned browser was closed; temporary release
containers were removed automatically and user services were preserved.

Private packets `origin-release102-20261004.pbZUVkeu` and
`life-removal-clarity-20261004.Lrcb1If0` retain exact artifacts and bounded
evidence outside served directories. No credentials or private runner data are
included in this record.
