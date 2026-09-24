# Preview 29 — observed Play Internal availability

At `2026-09-24T16:07:06Z`, the owner-authenticated Chummer Play Console showed
`29 (0.1.0-preview.29)` as **Available to internal testers**, Internal release25.
The displayed release time was `24 Sept 18:06`.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
Physical Play installation or update remains unverified. This is browser
readback, not Publisher API evidence or phone-beta completion.

## Exact artifact and inputs

- Android producer `234aeb94a40b984e62aae801be1edf698497f8f4`, tree
  `a921af097167b283cfeaa4007c5ba9130df6a421`.
- Android PR106 merge `0822121f33ab6644acc847d5afa1c43e94dc2a6f` has that exact
  same tree. Core PR71 and UI PR202 also merged normally before availability.
- Presentation seal `12236cdfe828f147bf3b8623f19e6cbd3fbaca3f`.
- Core runtime `5160e78a60bcefd952e8720aae6032a127c3a755`; recipe/content
  `1e477c0f5e036eed241f4fe723a0e2eda30c51dd`.
- Hub package producer `42d0bfbb117ab6250e8b0512dd92585916c6469f`. Hub PR267
  remained draft at upload; this is not a claim about its merge or deployment.
- UI consumer receipt SHA-256
  `63299515adc56d5e93901d4e33951a256ea970ff2955eeb0bf7521dc2ad325c8`.
- Android package-authority SHA-256
  `2612c0a17a6b9f394a21579b2677eb1f7b1e22daa09250a5a8d08a3197872132`.
- Source-input record SHA-256
  `a5702e1b484844e9f0d5c01c8e7b711ac1b27f566d7055edd299b4d5f20e19fe`.
- Unsigned AAB: 31,907,850 bytes, SHA-256
  `0eeda22019c77db872bb53a145901e58ef0c81091063fcd0b4cc17e18d8d623f`.
- Signed AAB: 32,080,325 bytes, SHA-256
  `09728474a634d8797fe97ae33d8699cc98782cbe59475e802b55f2e025764a81`.
- Existing upload certificate SHA-256
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
  This is not the Play App Signing certificate, which remains unobserved.

## Local verification and actual upload

The offline keyless Docker ARM64 Release build passed in 2:24.71 with zero
warnings/errors, using SDK 10.0.111 and toolchain image
`sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.
Dependency mode was locked package closure with pinned Presentation source and
Core content, not a package-only APK or ambient sibling build. The actual UI
owner-cache reuse was authenticated, not described as a fresh owner rebuild.

Unsigned verification checked package `com.myexternalbrain.chummer`, version29,
minimum API24, target API36, ARM64, privacy/permissions, 330 exact content files,
private-key hygiene and binary proof exclusion. Twenty bundled icon PNGs matched
the user-supplied troll SVG's rendered references pixel-for-pixel. A separate
offline signer used the existing upload key. Independent keyless verification
passed strict JAR signature, exact certificate and unchanged non-signature ZIP
payload. No keys were mounted in the builder or verifier.

The sealed-package managed intake passed native compilation, linked-owner and
ABA rejection, chapter HTTP/consent/hostile-response cases, book continuity,
Life Modules completion/Career and save/reopen tests. Generated locks and content
provenance were synchronized afterwards; all 330 content entries remained
byte-identical, focused metadata checks passed, and the final Release build
compiled those changes. These are local results, not seven hosted journeys.

Only that exact signed AAB was uploaded. Play processed version29 and confirmed
Internal availability. English, German and Spanish release notes were accepted.
The two nonblocking warnings concerned missing deobfuscation mapping and native
debug symbols. No Production, tester, billing or security settings changed.
Version29 is consumed; the next upload requires an unused version code30 or
higher. Preview28 and earlier evidence remain immutable.

## User-path limits and remaining operation

The [linked FirstBook native smoke](../../docs/LINKED_LIFE_FIRSTBOOK_SMOKE_20260924.md)
demonstrated one synthetic linked-owner Life Modules decision, one genuine
523-word FirstBook chapter, explicit review/adoption, HTML export, and identical
prose after force-stop/cold reopen. It used the recorded Debug x64 development
packages, not this ARM64 Release AAB. Whole Life Modules completion/Career tests
passed separately; these results do not prove one complete linked-user lifetime
with multiple generated chapters on a Play-installed phone.

A continuously operating, consent- and quota-bound book worker still needs
operational verification. The bounded provider run is not proof that every new
tester request will be processed unattended. Physical Play installation/update,
retention of existing characters and account links during that update, actual
Play signing identity and production App Links remain unverified. Full SR6,
audiobook conversion, Full Editing, tablets, Windows and Rook are not claimed.

Private packet `life-release29-linked-20260924.2ayPs3Li` retains exact source
inputs, build/sign/verification logs and `PLAY_PUBLICATION.json`. Availability
screenshot SHA-256:
`cf4e1ef64b80ad4dd4c9767e1a271ad7d9853b046e0f958eb5997873d9c6ab3d`.
The owned Play browser and temporary build/sign/verification containers are
closed. Release/rollback artifacts, source, signing material and user data remain
preserved.
