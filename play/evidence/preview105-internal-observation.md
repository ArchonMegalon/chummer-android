# Preview 105 — verify retained story before Life Modules completion

Authenticated Chummer Play Console readback on 4 October 2026 at 22:19:55 UTC
showed `105 (0.1.0-preview.105)` **Available to internal testers**, Internal
release 99, one version code, released on 5 October at 00:19 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is scoped browser readback, not Publisher API or physical-device evidence.
**Physical Play 105 installation/update remains unverified.** Version 105 is
consumed; never rebuild/re-upload it. Preview 104 and its evidence are retained.
No Production, tester-audience, listing, billing or account changes occurred.

## Change and focused verification

Life Modules completion now distinguishes unreadable or invalid retained story
history from verified absence. Unread or unverified story state cannot expose
completion allocations. Explicitly verified legacy no-book completion remains
supported. No rules, persistence format or dependency graph changed.

The original-code regression reproduced an unreadable ledger exposing completion.
Nine focused actual-MAUI/Core cases passed: verified absence, unreadable ledger,
ambiguous missing history, invalid history, read exception, recovered absence,
real Core unread story, disk-backed reading acknowledgement/reopen and owner
A-to-B-to-A. The local Release x64 diagnostic build passed with zero warnings/errors.
The version-only candidate reuses these unchanged behavior checks; they are not
represented as fresh hosted runtime qualification.

Synthetic offline API36/font-scale1.3 product pages blocked completion for an
unread story, then admitted a persisted read story. Force-stop, verified process
absence and a new process reopened completion at revision7/saved7 without fixture
changes. Both runner documents and the reading-edition hash stayed identical.
SDK-test APK SHA-256:
`9a2ce7c8f50f91b6fc5f1c2e58f4306100becaa146ef625ef8fe0f9cfbbb8960`.
Emulator36.3.10/build14472402, API36 x64. Failure injection is managed-test
coverage only; the native smoke covers unread/read/restart states. A boot System
UI ANR remains recorded, not performance clearance. The existing completion-card
subtitle still asks to read first after acknowledgement; separate wording work
remains. No live provider, user-runner data or provider credits were used.

## Exact local artifact

- Android producer: `99df4529eee92b04607d972bacf73b9111a2e0e8`;
  main merge: `20222f54d7ddd738ba5212d7ff7e4319004f24d9`;
  identical tree: `3b6cb19e6e70c05da8a5a681b56385025d9a4699`.
- Behavior PR468 and version-only PR469 merged normally. Required source and
  GitGuardian checks passed; version run37238965658/job111543630176.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,685,457 bytes:
  `e041817a7fdd624c98ac433b21123b3e19fb95de32c705cf993ed5c8ff4a6c43`.
- Signed AAB, 33,858,125 bytes:
  `e3e4a87ba05bae48f86e953f0610812e7dfd60f8f9f4eec8d8123e27ba1baf9d`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `a19be7b63e72aa038890d061fa2177ee1433424ef4b2d4810c63d9ed368fb94f`.
- Independent signed-verification receipt:
  `c702b25fc677d252a152b0d7c92806951cb4408b7d9e7b2d9ecf7bf99121d419`.
- Private Console execution record:
  `ff2d0c8acb3d58471eab1100d35b3bb7cd9b707f9185b56bf71b0a2f79270dc9`.
- Availability screenshot:
  `de94d34ade1685c62f58a22f0818f8bc2bfc9133800f3fdc743adce4f4c8f370`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact clean source exports, 18 packages, 331 embedded content
files, package/version/API24+/target36/ARM64, privacy flags, proof exclusion and
credential hygiene passed. Dependency mode is locked package closure with pinned
Presentation source and Core content, not package-only or hosted qualification.
Separate original-key signing passed independent keyless strict JAR/certificate/
all1,783 non-signature payload comparisons. No compiler warnings/errors were
observed in the candidate build log.

One upload and one confirmed Internal rollout occurred under standing approval.
Device catalogue is unchanged; missing mapping/native-symbol warnings remain.
Live account refresh, the historical Hub503 cause, live-provider Origin, full
SR5/Origin and general-beta completion remain unverified. Private packets
`origin-release105-20261005.kpBdHxYX` and
`life-completion-story-gate-20261004.kmW22yud` retain exact artifacts and bounded
evidence outside served directories. No credentials/private runner data are here.
