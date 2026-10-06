# Preview 113 — Life Modules refinement keyboard

Authenticated Chummer Play Console readback on 6 October 2026 at 00:18:54 UTC
showed `113 (0.1.0-preview.113)` **Available to internal testers**, Internal
release 107, one version code, released at 02:18 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API or physical-device evidence.
**Physical Play113 installation/update remains unverified.** Version113 is
consumed; never rebuild/re-upload it. Preview112 and its evidence remain intact.
Production, tester audience, listing, billing and account settings were unchanged.

## Change and affected verification

The Life Modules decision page now dismisses the soft keyboard when the user
taps outside a text field. Optional chapter refinements can also be collapsed
without losing their entered Custom answer or leaving the keyboard over the
decision controls. This does not change rules, budgets or provider admission.

The existing focused page regression and DE/EN/ES story/refinement checks passed;
the affected native Release x64 SDK-test APK built with zero warnings/errors.
Actual API36 emulator smoke restored the saved runner, prepared an unconfirmed
Further Education preview, expanded optional questions and typed a Custom
motivation. Both outside-text tap and refinement-collapse changed the observed
Android IME state from shown to hidden. Re-expansion retained the exact text;
back navigation retained the original 105/750 budget. Confirm was never clicked:
no new module mutation, paid book or image generation was used by this smoke.

The same app process remained alive throughout this keyboard test; it is not a
new-process persistence test. Existing illustrated-book persistence evidence
has its own scope below. Initial emulator-system/keyboard boot ANRs and null-root
observations were retained; the same emulator recovered without a restart,
resource increase, app reset or larger timeout. The owned emulator then exited
cleanly, preserving saved data. The physical phone was not touched.

The validated APK used a separate SDK-test package/signature, not the final
Play-managed ARM64 installation. Behavior checks are reused only for the subsequent
version-only delta. Full Creation and general beta are not claimed complete.

## Related illustrated-book evidence, separately scoped

Before this keyboard change, the native reader exported two actual complete
FirstBook chapters through Android's Save book as EPUB picker: 2,494 words /
78 prose paragraphs and 2,506 words / 62 paragraphs, with both embedded PNGs.
The second automatic 1min.ai scene used the first image as its actual reference;
visual inspection showed the same protagonist somewhat older. After verified
force-stop and a new process, the reader and a second real EPUB export preserved
all chapter/image bytes and package identity; only the export timestamp changed.

The supporting Hub bounded-read change is normally merged as
`aa8ecae30c1ae8d111c94b6b478cf224ff1da771`, tree
`5e69f26f1a1d1b5c2ccacc27b58283e882943471`, identical to the tested/deployed tree.
This two-stage sample is not proof of every future module or age, a complete
book, or unattended all-user operation. The current continuous FirstBook worker
still serves only its existing finitely approved selected test book. General
permanent book-service admission remains incomplete; unknown earlier jobs are
not replayed. Decision-save latency and generic progress copy remain follow-ups.

## Exact local artifact

- Tested behavioral Android head: `d423cdd96b1c4686fd4e0fc553d86058a6072a7b`;
  PR498 merge: `5cad56215a16965499221cb5faad471790e00b03`;
  identical tree: `1f6089c034ac67afc9947ceee99e6710a8b8c872`.
- Version-only producer: `3ca8299822286422f85d16246e5e823acafe3063`;
  PR499 main merge: `886daf493eb2564ef10aea30cdff39ff9349a5f5`;
  identical tree: `05e0ed41565a8f40238019a66967244949edc0c0`.
- Both PRs merged normally after required source/safety and security checks.
- Presentation seal: `5859961a8d0eef814a2bf86fe702b0013512ae58`.
- Core runtime: `5f4e6350791c2c4fd7959da1f07cc1a85ce5446e`;
  recipe/content: `51c6a6bbdb1a498352f95512b7d47f11e6fa42b7`.
- UI consumer receipt:
  `a21c7dfe547e6d219b6b5333a8cabd4907f34599604f173dd858e0542e459584`.
- Unsigned AAB, 33,795,669 bytes:
  `09d4d4cd50db01af65852887cded31406b2478ad88addb1878f9f9431e9a7d6a`.
- Signed AAB, 33,968,305 bytes:
  `4c854ab852202e25eeefe7a405bff66e87274389b165957a2d31db69c9711d19`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `58c9ce80f839ff3d43f5f6826d501fe551ca336e36d468f2e6320e93e787cf68`.
- Independent signed-verification receipt:
  `dcf279859d29ce8b8f02f1cede0c5ff1f043b0c44d4b365fb5b7c0a469920727`.
- Private Console execution record:
  `2f3741e4a5c9297bd83c6e864ea9aa0a04938e7e82d07740063c29a87044525b`.
- Availability screenshot:
  `43c3f52c693629b10b8ece780c9e55fff5bdc0ea713f52d9adc336348bb48d69`.
- Actual two-chapter illustrated EPUB:
  `3a5531664e3529ceeb157cb31efaec1ad409e3f61af1a0f2e429fcb0829567f4`.
- Post-restart export (new export timestamp, same content):
  `aba056cffe6b9ab4c9452b32eeefa8dd81a057414b879542abc7218fae772c99`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, all 331 embedded content files, bundle
identity, API24+/target36/ARM64, privacy flags, proof exclusion and credential
hygiene passed. Dependency mode is locked package closure with explicit pinned
Presentation source and Core content; no ambient sibling discovery, **not
package-only, hosted qualification or a two-green receipt**. Separate existing-key
signing passed independent keyless strict JAR/certificate verification and
comparison of all 1,783 non-signature payload entries. Release containers exited.

One exact upload and one final Internal rollout confirmation used the standing
2026-10-05 approval; authorization is separate from candidate verification.
Missing deobfuscation mapping and native symbols remain non-blocking Console
warnings. Device catalogue presence is not tablet authority. The upload
certificate does not establish the Play App Signing identity. Private packets
`origin-release113-20261006.vuU43i95`, `origin-keyboard-smoke-20261006.fJP0xK6b`
and `origin-authority-read-20261006.Ks850m5R` retain artifacts and actual route
evidence outside served directories. No credentials or private character facts
are included here.
