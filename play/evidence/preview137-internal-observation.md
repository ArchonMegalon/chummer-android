# Preview 137 — compact Karma attributes

Authenticated Play Console readback on 8 October 2026, approximately 01:51:40 UTC,
showed `137 (0.1.0-preview.137)` **Available to internal testers**, Internal
release 131, one version code, released 03:51 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API or physical-device evidence.
**Physical Play 137 installation/update remains unverified.** Play states that
propagation usually takes up to an hour and can take longer. Version 137 is
consumed: do not rebuild or re-upload it. Prior artifacts/evidence are unchanged.
No public-track, tester-audience, listing or account changes occurred.

## Change and affected checks

Karma Creation now has Refresh preview above the attribute list as well as below
it. Attributes which Core marks read-only use compact rating/cost cards instead
of inactive purchase controls when there are no purchases to remove. Existing
invalid purchases remain removable. All current Core ratings, ranges and costs
remain visible; final ratings stay large and bold. Quote invalidation, ownership,
stale-action checks, durable mutation and finalization rules are unchanged.

The focused managed regression and affected Release/x64 native build passed
with zero warnings/errors. Managed coverage includes Human, Troll and Magician,
both preview positions, current rating/cost invalidation, changed-talent invalid
purchases remaining removable, stale/departed callbacks and unchanged saved data.
An initial regression test incorrectly expected an asynchronous callback after
a departed-page rejection; that test expectation was corrected, not the guard.
No source-pytest pass is claimed for a pattern which matched no tests.

Actual API 36 emulator execution used a separate SDK-test package and APK
`b0cc27f5884687bd6ed794dc3e7faf2372c871f4b64d21b3b6207c0b3aa68ec4`.
Its product commit `613a0b6e82cdbf2fd8aa573a70e91ef5e96c5693` predates only
release-version metadata. Installation preserved existing data. A new synthetic
Human/Mundane Karma runner was created through the UI. One Body purchase first
invalidated the quote; the new top preview then showed Body 2, 10 Karma and 790
remaining. Settled screenshots confirmed readable bold values, compact Magic,
Resonance, Essence and Depth cards, and retained editable Edge/bottom preview.
The three previously completed Karma, Priority and Sum-to-Ten runner files were
byte-identical after this test; no save/finalization was issued for the new draft.

The earlier minimal Karma Creation-to-Career/process-restart smoke passed on the
previous product build. It is retained separately, not relabelled as a fresh
full-route test of this cosmetic change. Emulator boot produced a System UI ANR
dialog; one Wait action is retained in the record. This increment does not claim
generally responsive startup, all magical paths, full Creation/Career coverage,
hosted qualification or finished-app status. Windows work remains stopped.

## Exact local artifact

- Android producer: `4e9e12bc0377c953ea763a6aff476dc63c843471`;
  normal PR 554 merge: `4e0331c7b6d7bf56ef8832c12a9d65418c5df7b8`;
  identical tree: `a56cf12284764ba2a2f495861b94e3ee32b0951c`.
- Presentation seal: `0a9c2ad615386aba8e1f56d68ffa514a3216daf5`;
  Core runtime: `66576d96153f0888da88959c2f3f527f112761c3`;
  Core recipe: `81ce9602465504e429eab8166ef4964a7592166c`.
- UI verification receipt:
  `aaf301cb0bf4e0a6ca4e2831a901985f7e8b8420d8bf10406a6a43375c0fd129`.
- Unsigned AAB, 33,820,984 bytes:
  `19506224a3fe9aa260a992eccf516ec8f51611c7c3a398bc7415349bf2bc9a90`.
- Signed AAB, 33,993,625 bytes:
  `75df6fec1ee2d11fcca6d56a89d7adaabc73d11daa52e8454db5ad2bff727f34`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `ea93a4d45e84056ab65fdb52e38e6a03d70d9c7b4fd1e1024fcad53c5d9c8b16`.
- Independent keyless verification receipt:
  `74d0f916b0c564d49270cac46f0e2adf1ecde287d47b1bc7bd702485516817f`.
- Private affected native smoke record:
  `276c455c3aea4d7a795b1b9d2b61308948af348f4a9f1e936b8d0605cdcbbf84`.
- Private Console execution record:
  `8fa01adad83f4730b293fae2753f28c3ba75bf30447714a04a63a4db02a06524`.
- Visually inspected availability screenshot:
  `1f3deba10e5a68ba34cfff0ae0d5a09974e5f99aa542c808b458175d3bcfa007`.

One offline local ARM64 build used .NET 10.0.112, JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, 18 packages/13 authority files, 331 embedded
content files, identity/version/API/ARM64, proof exclusion and credential checks
passed. Mode is locked package closure with explicit pinned Presentation source
and Core content, not package-only or ambient siblings. Separate existing-key
signing and independent offline keyless verification passed strict JAR signature,
certificate identity and all 1,783 unchanged payload entries.

Required checks passed before normal merge. One upload/final confirmation used
standing Internal approval of 2026-10-05, separately from candidate verification.
Expanded Play validation showed only deobfuscation/native-symbol warnings, with
unchanged device counts. Signing inputs and the underlying evidence packet remain
private. Browser automation followed the existing scoped browser-act workflow.
