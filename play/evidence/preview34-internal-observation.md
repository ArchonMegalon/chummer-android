# Preview 34 — observed Play Internal availability

At `2026-09-25T06:08:30Z`, the owner-authenticated Chummer Play Console showed
`34 (0.1.0-preview.34)` as **Available to internal testers**, Internal release 30,
track Active. Displayed release time: `25 Sept 08:07` (Europe/Vienna).
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is rendered Console readback, not Publisher API evidence. Physical Play
installation/update remains unverified and explicitly deferred by the owner.

## Change and focused verification

This update consumes the current sealed Core/UI graph with reduced redundant
source validation while retaining ownership, source identity and save checks.
The Life Modules/Origin contrast fix from Preview 33 remains included.
Unconsumed Core PR77's separate canonical-property-buffer optimization is not
included in this candidate.

The underlying Core package verification passed 678 tests plus focused owner
and storage checks. The exact UI consumer passed 858 main-subset tests and
selected follow-ups. Android's affected managed owner, Life Modules, book,
save-boundary and cold-reopen tests passed, along with 29 pin/content/build
checks and the affected native Debug build with zero warnings/errors.

An API36 Debug x64 smoke used an isolated synthetic Life Modules fixture.
Finalization advanced revision 6/6 to 7/7 exactly once; verified process death
and restart reopened Career and the saved book. The 8,390,696-byte workspace
remained identical across restart/book reopen, SHA-256
`7af85c652918b94cf1c01f6a29bff6ba0d47a340d0ec9b376eda0b6f322a7394`.
This was a seeded completion slice, not a fresh whole-Creation, real-provider or
physical Play test. Finalization remained slow; responsiveness is not declared
fixed. Complete AI book narration is not verified.

The release candidate adds only version 34 to that tested Android product tree.
Unchanged package and route results are reused, not represented as new hosted
tests or a Release-AAB device run. Six version-intent tests, exact local Design
policy and all 18 package-authority checks also passed.

## Exact artifact and source

- Android producer `c8bf97b6fc716017ae64af6e8280294abe463af6`, tree
  `5a83f211cb78f9048ba33bd7138daad2529a9925`.
- PR117 merged normally at `2026-09-25T06:00:50Z` as
  `79dd1d75591b64db83116219710e1ba69c10b710`, identical producer tree.
  Required source/safety and GitGuardian checks passed; these are not device tests.
- Consumed Presentation seal `98801e837c6d6b5d9bdd1a6b44ab68088c56ef56`, tree
  `b4e469879cbba496fb6bb2c42d7da72554c93db3`. UI PR211 merged as
  `03389d4de1b6d119cbdbebad4594d3b623fac9da` with that same tree.
- Core runtime `f3ca90e11c838c83cd296902ce981c5b2f011f52`; recipe/content
  `69f6fb96fd237976c48a3671d01bf7d01a6e0cad`.
- Hub producer `42d0bfbb117ab6250e8b0512dd92585916c6469f`, unchanged.
- UI consumer receipt SHA-256
  `6f44e20386dc2e25008b5f04595209db95ec4d148c25a506423b3c715a7be67b`.
- Source-input record SHA-256
  `59a51db8aad7457707ef6e3cb8ede9c75460c9370db0b3a5f8c79db4f2645794`.
- Unsigned AAB: 31,922,425 bytes, SHA-256
  `0b3043c3885e3b3507af091c40143d30e436ec0b87458a1efbaa420f61e4c58a`.
- Signed AAB: 32,094,889 bytes, SHA-256
  `adbbbcf03dfef6dd37a769af7e542b9da073b01882d36c4f64550198cb2f23e3`.
- Existing upload certificate SHA-256
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
  This is not the unobserved Play App Signing certificate.

The local offline keyless ARM64 Release build passed in 2:29.83 with zero
warnings/errors using SDK10.0.111 and toolchain image
`sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.
Dependency mode remains locked package closure with pinned Presentation source
and Core content, not package-only APK assembly or ambient sibling discovery.
Unsigned checks passed version/API24+/target36/ARM64, privacy and permissions,
proof exclusion across 165 managed assemblies, 330 exact content files and
private-key hygiene. Isolated signing completed at 06:02:46UTC; independent
keyless verification at 06:03:05UTC confirmed strict JAR signature, existing
certificate identity and unchanged non-signature payload.

Only those exact signed bytes were uploaded, once, to the existing Internal
track. Play accepted three release-note languages and reported only the existing
missing-deobfuscation-mapping and native-symbol warnings. Supported-device
readback showed no lost devices. No Production, tester, billing or security
changes. Code 34 is consumed; a future upload needs 35 or a higher unused code.
Preview 33 and earlier artifacts/evidence remain unchanged.

## Limits and custody

No physical Play installation/update, Play signing identity, production App Links,
general beta, complete method/Career parity, SR6, audiobook, tablet, Full Editing,
Windows or Rook completion is claimed. The book worker and its budget are unchanged.

Private packet `life-release34-validation-20260925.X91P3PhG` retains inputs,
build/sign/verification logs, screenshot and actual `PLAY_PUBLICATION.json`,
SHA-256 `6a1e191406cb1666ab347dff512b855c7ebf9e92b51ccd0e3e075db888e258c4`.
Affected smoke is retained separately in
`life-source-byte-validation-20260925.Q7WbD5UG`.
Owned release containers and Play session are stopped; the prior diagnostic
emulator was already stopped before this release transaction.
