# Preview 152 — scoped Contacts source reuse

Authenticated Chummer Play Console readback at `2026-10-09T00:54:38Z` showed
`152 (0.1.0-preview.152)` **Available to internal testers**, one version code,
Internal release 146, released 9 October at 02:54 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API evidence.
**Physical Play 152 installation/update remains unverified.** Play says changes
usually appear within one hour and can take longer. Version 152 is consumed:
do not rebuild or re-upload it. Previous artifacts and evidence stay unchanged.

## Change and focused verification

Core reuses source construction only during one synchronous Contacts read.
Fresh source admission, owner stamps, revision checks, detached results and
failure cleanup remain enforced. Ten new focused regression cases and 33 total
selected Contacts/nested-finalization cases passed. Four initial regression
cases failed against the baseline; this is not a claim that all ten did.
Paired managed Contacts reads allocated about 18–19% less temporary memory.
Overall restore still allocates about 1 GB; wall-clock improvement is not proven.

Core package production passed 800 selected managed cases, 18 owner-admission
cases and 15 inventory cases plus scoped persistence/reopen checks. Exact UI
consumption passed five builds, 879 product cases and focused groups (62 existing
test-build warnings, no errors). Android intake compiled actual MAUI/native and
interaction tests with zero warnings/errors. Affected Priority/Sum-to-Ten Contacts
admission, mutations, checkpoint, cold reopen, replay and finalization passed;
six startup/restore managed cases passed. Exact 18-package/13-authority-file
binding and 331 content files passed, as did the 51 package, 219 affected pin and
58 source/safety cases. These are separate suites, not unique combined coverage.

The final-source Release SDK-test x64 APK updated the retained API 36 app without
uninstalling or resetting it. Eleven-runner restoration, Contacts read and a
verified force-stop/new-process reopening passed. The same Sum-to-Ten draft,
revision 9/9 and exact route authority returned; Contacts displayed 3 total,
0 used and 3 remaining. All 33 workspace files (9,132,743 bytes) stayed identical.
This read/reopen smoke does not claim a fresh native save mutation or physical
Play-managed ARM64 installation.

The emulator had system/SystemUI ANRs before candidate installation. A transient
unusable hierarchy, an early screenshot taken during navigation and one stale-
coordinate tap opening Attributes without mutation are retained as limitations.
Stable post-restart Contacts screenshot and hierarchy independently agreed.
Restart shell initialization took 35.447 seconds and selected-runner restoration
74.886 seconds. **Startup remains slow; no native speedup is claimed.** The owned
emulator was stopped. Full chapters and chapter images remain in the reader and
EPUB; unchanged [chapter-image coverage](../../docs/ORIGIN_EPUB_AND_SCENES.md) is
reused, not represented as a new provider or physical-device test.

## Exact local artifact

- Android producer: `5d6801993903c55ec7a39fa9f006425989a5f977`;
  protected PR 589 merge: `b0479fbd198f446afd4087e556bf02632cf255f9`;
  identical tree: `9da63906e3b3ea42b87c1244458633e11d349fba`.
- Presentation seal: `959d210b2266b5a375768c421ac9c085033bcf89`;
  protected PR 371 merge: `c29f1f5f524028026f8f083313ac158652406c1d`;
  identical tree: `6102d9be00d4309ea2da071002b259dc2fb0d9b9`.
- Core runtime: `e19dc7be93688fc0d664197b9537110263321173`;
  recipe/content: `68dfe40b3bf542ca8b534ef1bb01d3aa871daf8d`;
  protected PR 135 merge: `656dd350cb2b772d64b096a4c4e86d4919806e5e`.
- Core public bundle: `7b23e34948b7e55e3c85b5ca788a55484756d752612ef4f23cc025ba9628f107`.
- UI verification: `c89637d087cb04ce5eaef2dca5a26f1f7e4e614e53d379ca113e2f1e6fbfb6a1`.
- SDK-test APK: `4a66ba9172b601422110cd9c96dc87188f99fe4fd19d6959a580221a460d30c6`.
- Native smoke record: `6bf18446ef4ad9a1c91e283ce5127142c0923bbbeedba20674c8210e0790231e`.
- Unsigned AAB, 34,960,835 bytes:
  `72bc29b93970e6aa2fe7b08ef17f0b00214a7369bca680821786beb114827398`.
- Signed AAB, 35,133,482 bytes:
  `2b84bc4e76b644cd5d8ef949b7252cfd203b272639dd2c0a6bb973de29734257`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `a505059134855ad4a8f3280f900c7e922b3953843584d35eb39d2acda7a79bc2`.
- Independent keyless verification receipt:
  `a87ed8b8dc82b0529f708ee88313863a01e7a7fe2add1951ef32c29889b639f7`.
- Private Console execution record:
  `dc3d0420974e44cc9ecaec0eff9f007f353027623ef49eb93f461c382c05c5eb`.
- Visually inspected availability screenshot:
  `29e83a78b76955ba1d3a676dc743d97d86aa3830b22a20d10d4dfed2e681b2dc`.

One offline local ARM64 build used .NET 10.0.112/JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exported source/content, package checks, bundle/version/
API/ARM64 inspection, proof exclusion and credential hygiene passed. Dependency
mode is locked packages with pinned Presentation source and Core content, not
source-free package-only assembly. A separate offline signer used the existing
key. Independent keyless strict JAR/certificate verification and all 1,783
unchanged unsigned payload entries passed. Raw packets and signing material
remain private; pre-upload stage receipts retain their original status.

One upload at 00:50:04 UTC and one final confirmation at 00:53:58 UTC used
standing owner Internal approval of 2026-10-05, separately from candidate checks.
Scoped browser/OODA checks verified the account/app/track and actual availability.
Play recognized 152, API 24+, target 36 and ARM64, with zero validation errors,
no supported-device delta and only missing mapping/native-symbol warnings.
No uncertain action was replayed. No audience, public-track, account, listing or
security change occurred. The owned browser session was closed.

Physical installation, complete Creation/Career coverage and general startup
reliability remain open. This is not finished-app, full beta, tablet, Full
Editing, Windows or new live-book-generation authority.
