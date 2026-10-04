# Preview 92 — Origin recovery after a connection failure

Authenticated Chummer Play Console readback at `2026-10-04T09:12:22Z` showed
`92 (0.1.0-preview.92)` **Available to internal testers**, Internal release 86,
one version code, released at 11:12 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API or physical-device evidence.
**Physical Play installation/update of 92 is not yet verified.**
Version 92 is consumed; never rebuild or re-upload it. Preview 91 and its
evidence remain unchanged. No Production or tester-audience change occurred.

## Bounded change and verification

After three transient chapter-status failures before authoring admission,
Origin previously paused without a visible recovery action. The reader now
offers the normal status action for an eligible missing chapter, so the user
can recover after connectivity returns without reopening the book. Existing
read-first, ownership and uncertain-job no-replay guards remain unchanged.
No automatic reader acceptance, persistence migration or rules change was made.

The focused regression reproduced the missing action before the fix and passed
after it. Local actual-MAUI managed compilation reported zero warnings/errors.
The checks use real Core/file stores/page handlers with synthetic remote chapter
transport: bounded reads pause without dispatch; recovery dispatches one
successor; cold missing-job recovery does not replay a paid request; full saved
prose and both exports remain usable during pending observation; unread effects
stay hidden and workspace bytes unchanged. No provider credits were spent.

The unchanged reading/export routes retain Preview 91's explicitly scoped
synthetic API36 save/EPUB/new-process evidence. **The recovery-action delta has
not received a new Android-device smoke.** It has focused managed-route checks
and this candidate's own ARM64 Release build/inspection; neither is a physical
Play92 test or complete seven-journey qualification. All-method SR5, full Origin
manuscript, responsiveness and general-beta completion remain open.

## Exact local artifact

- Android producer: `f9925199bd7423c9a84539dd61f74468016e6d0a`;
  main merge: `2c75b797829fce743ac7b51670001c64592921e6`;
  identical tree: `148872034e1d4c056a0d0e77cd1f0e45bcbd7f4f`.
- Product PR 428 and version-only PR 429 merged normally with both required
  source/safety and GitGuardian checks. No protected check was bypassed.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`;
  tree: `557d0486b7fb4a1d7d10cb6f79c910bd8d0de895`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,663,717 bytes:
  `9934ecf29325ce9ef6c3eb32ae6f704d4fc1e53bc8eb0e15f3594c8b28ab4da9`.
- Signed AAB, 33,836,364 bytes:
  `9809eadedc47c0e15c4d1d280e33ccd0a464f618974d7df3295906c25b6ed5ce`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `38b7bdbfc4def79448fc446979432fa64bc948be489ca87d5e5b06d5578a0566`.
- Independent signed-verification receipt:
  `ab62f8e33353243dc074ffa4b498df88c5241443dc5591081159070dd9877ce6`.
- Private Console execution record:
  `d3c7b98a4b1e8af36723f7351cc99f4a14940e23dc2b46b2beee269136a55a51`.
- Availability screenshot:
  `81d2f8b8b24dfd3b47863bb4cfd29f8a7cd72b0ee594dd7d795f736a8f2e971f`.

Execution was local offline Docker with .NET 10.0.112, JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports, 18 packages, 331 embedded content files,
package/version/API24+/target36/ARM64, proof exclusion and credential hygiene
passed. Dependency mode is locked package closure with pinned Presentation
source and Core content, not package-only or hosted qualification.
Separate original-upload-key signing passed independent keyless strict JAR,
certificate and equality of all 1,783 non-signature payload entries.
An initial cache-path preparation error stopped before the compiler; the exact
existing cache path was corrected without discarding logs or changing inputs.

One upload and one confirmed Internal rollout occurred under standing approval.
Play reports unchanged device coverage and the existing two diagnostic warnings:
missing deobfuscation mapping and native debug symbols. These remain deferred;
no platform or signer control was weakened. ADB observed no connected device.
The FirstBook continuation worker remains separate service evidence, not a live
phone book test. Uncertain historical paid jobs remain fenced.

Private packets `origin-release92-20261004.VSv5PbRO` and
`origin-reading-flow-20261004.PIrxqdyu` retain build, signature and test evidence
outside served directories. No credentials or private character data are public.
