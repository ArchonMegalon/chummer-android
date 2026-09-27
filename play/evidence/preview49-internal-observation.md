# Preview 49 — observed Play Internal availability

Authenticated Chummer Play Console readback at `2026-09-27T18:47:44Z` showed
`49 (0.1.0-preview.49)` as **Available to internal testers**, track Active,
one version code. First availability was observed at `18:43:24Z`; displayed
release time was `27 Sept 20:43` (Europe/Vienna), Internal release 45.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API evidence. Physical Play
installation/update and the Play App Signing certificate remain unverified.

## Delivered change and verification boundary

Returning from Life Modules completion now immediately removes stale Creation
controls and disables Save while the existing owner-bound Career load completes.
The same-page regression failed against the old behavior and passed with the
correction. The affected isolated API36 x64 Debug route finalized once, advanced
revision 7/7 to 8/8, retained starting cash 120 and exactly one finalization
receipt, reached Career without restarting, then reopened Career after verified
force-stop and a new process. Saved bytes remained identical throughout:
`9392ac58a5d5aeb8d3eb227afa6c3597d125022064fd0b3888621255958d60ab`.

The intermediate failed hierarchy observation is retained. The subsequent warm
Career observation occurred in the same process before the deliberate restart.
The first warm screenshot and hierarchy were captured at different instants;
the screenshot is not evidence of the hierarchy's loading state. Finalization
still took roughly 100 seconds. The Core allocation improvement does **not**
establish a native latency improvement.

Affected managed completion, ownership, Save, Origin/EPUB/scene and verifier
checks passed. Nine theme/localization checks passed; one pre-existing unchanged
Origin source-string assertion failed and was not weakened. This is not a
full-suite pass. The native APK was isolated Debug, not Release ARM64 execution,
a physical Play install, seven-journey qualification or a complete live linked
FirstBook test. The release producer differs from the tested behavior only in
its application version. The brighter Troll launcher/splash from Preview 48 is
unchanged; store-listing artwork was not changed by this transaction.

## Exact local build and signing

- Tested Android source `6de1e5b35b45b3a9b76219924f05aa0e21d8e346`, PR 174 merge
  `1d97db4a6daac0112f1d33084be0464c6263fc9c`, identical tree
  `8594af1d37883166567de7d42c1183d2d9828f59`.
- Version-only producer `8c35591b999af50be462d1059f4a226a5c0d78bb`, PR 175 merge
  `775e74cd30e0aa7abd25ecd8242f1bc4d85a8587`, identical tree
  `1636dfb14a09a5315b2bfd1e48e7b3b424923734`.
- Presentation seal `e585cf2ec9198010e2af5df95b67bdd179d38e1d`, tree
  `e9e643437c9286655bb56b83b87bbb4689a00f29`.
- Core runtime `231dd13afa82dde38e8a08ee7652f3eee8187bfe`; recipe/content
  `2a52c4bfb34694131101577e340098d950b1571b`.
- Hub producer `c77395de9f733427ef952c851f4a95b063cb5573`.
- UI consumer receipt SHA-256:
  `9c0903b62e0ffb3b784f717230ee29e0ce86902954996a6576772e5d18b87b01`.
- Source-input receipt SHA-256:
  `f1e94d48a5d2faa83de2e17867a7b3df878f1ef31da69b0527ef734d8d9189f9`.
- Unsigned AAB: 32,877,691 bytes, SHA-256
  `9f2a3bb3f6230b07f8512ef7030fb9dfe015e0a5e40bc0c68d1ca78f8ffdc4af`.
- Signed AAB: 33,050,165 bytes, SHA-256
  `1986f8987b544358b61462250d462e6627139eec177bc8f7b84a5050d23383d6`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
  This is not the unobserved Play App Signing certificate.
- Independent signed verification receipt SHA-256:
  `ef3a52f20005b36886137bf5089d7c1c268e503a63ea13090488bd75d702792f`.

The local network-disabled, keyless ARM64 Release build completed in 142.21
seconds, zero warnings/errors, SDK 10.0.112, toolchain image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact 18 packages/13 authority files, all 330 catalog files,
bundle identity/API24+/target36/ARM64, privacy/permissions, 165-assembly proof
exclusion and key hygiene passed. Separate old-key signing and independent
keyless strict JAR/certificate/unchanged-payload verification passed. Dependency
mode is locked package closure with explicit pinned Presentation source and Core
content, not package-only assembly or hosted qualification.

## Play result and retained evidence

Exactly the admitted signed AAB was uploaded once to the existing Internal
audience. Play review reported only missing deobfuscation mapping/native debug
symbols and zero lost supported devices. Eligibility is not form-factor parity.
Production, testers, billing, security, store listing and earlier releases were
not changed. Version 49 is consumed: do not rebuild or upload it again.

Private packet `origin-release49-20260927.1kfovVDZ` retains artifacts and logs.
`PLAY_PUBLICATION.json` SHA-256:
`499acb1689e5ab8bc1d51497ebcf313d054c91addec02875277dd835710c6b8f`.
Visually inspected `play-internal-available.png` SHA-256:
`bb5c77bdb5dd5f782a6562914fd4288319976f4e64dfd117b54d3f9ed8c7d303`.
Earlier pending-stage receipts describe their stage, not current availability.
[Preview 48](preview48-internal-observation.md) remains immutable.

Physical phone work remains deferred. Full live linked FirstBook/book verification
and finalization latency remain open. The interrupted FirstBook operation is
still fenced; no additional test books are started pending cleanup. No public
release, phone-beta completion, exhaustive parity, tablet, Full Editing, Rook,
Windows, SR6, audiobook or 3D completion claim.
