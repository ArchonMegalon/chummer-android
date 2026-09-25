# Preview 33 — observed Play Internal availability

At `2026-09-25T03:49:20Z`, the owner-authenticated Chummer Play Console showed
`33 (0.1.0-preview.33)` as **Available to internal testers**, Internal release29,
track Active. Displayed release time: `25 Sept 05:49` (Europe/Vienna).
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is rendered Console readback, not Publisher API evidence. Physical Play
installation/update remains unverified and explicitly deferred by the owner.

## Fix and focused verification

Life Modules and the Origin book inherited a black page background in OS dark
mode while their labels retained fixed dark text. Follow-up answer controls
also inherited incompatible system colors. PR113 explicitly sets the existing
NativeTheme page background and answer text, placeholder, title and surface colors.
No global theme, game rule, persistence, Core/UI dependency or provider change.

The regression failed on the original source and passed after the fix in the
existing Origin book-continuity suite. Managed and Debug APK builds passed with
zero warnings/errors. Actual API36 Debug x64 before/after screenshots confirmed
readable decision headings, free-text placeholder and saved draft book in OS
dark mode; light-mode book readback also passed. Existing synthetic stage3,
55Karma and two saved chapters reopened through the diagnostic APK update.
Picker colors have managed test coverage, not a separate physical-device claim.
No choices were confirmed and no provider credits were consumed in this smoke.

The release candidate adds only version33 to that tested product tree.
The unchanged Core/UI package verification from Preview32 is reused, not claimed
as a new test of changed Android source. No broad hosted suite or physical
Play-managed runtime qualification was performed for this small contrast fix.

## Exact artifact and source

- Android producer `4c270481efdc75abd25491dcc1180f00b126db96`, tree
  `042edc1df7bff90aac32f2dcb53c67374d25d48a`.
- PR114 merged normally at03:44:59UTC as
  `f650bdc97a2f7bc3b53e474fed6ccf3d7764df9c`, identical producer tree.
  Required source/safety and GitGuardian checks passed; these are not device tests.
- Presentation seal `e7da5ba5b55f5d81f6d8726ac7a55d73c7e62cff`, tree
  `f62295edd0111287770dfb8ec52733b49375043d`; PR208 already merged with that tree.
- Core runtime `dbc363651731d89c9d81c2f6fb6d0ee89a7b884b`; recipe/content
  `9ada34fab2160db86fe11bdb73249a3b4f415fe9`.
- Hub producer `42d0bfbb117ab6250e8b0512dd92585916c6469f`, unchanged.
- UI consumer receipt SHA-256
  `0f5e9bb3969387a87e94fda035f07cbba05aa21e5c6444009f7c52397cd63c42`.
- Source-input record SHA-256
  `d482989c75723368d854b44abceb3dad731852aaeebf029db9489455f5dc6b97`.
- Unsigned AAB:31,922,723 bytes, SHA-256
  `02c3c784a6d309c8364038e39c2effbc5cb7f00ab570f52b706c96ff2b613a95`.
- Signed AAB:32,095,184 bytes, SHA-256
  `288efd8860522dc60e9bab634f18453ea0b9341da537a26892e02638d2806895`.
- Existing upload certificate SHA-256
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
  This is not the unobserved Play App Signing certificate.

The local offline keyless ARM64 Release build passed in2:19.87 with zero
warnings/errors using SDK10.0.111 and toolchain image
`sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.
Dependency mode remains locked package closure with pinned Presentation source
and Core content, not package-only APK assembly or ambient sibling discovery.
Unsigned checks passed version/API24+/target36/ARM64, privacy and permission
checks, proof exclusion across165 managed assemblies,330 exact content files
and private-key hygiene. Isolated signing completed at03:47:33UTC; independent
keyless verification at03:47:47UTC confirmed strict JAR signature, existing
certificate identity and unchanged non-signature payload.

Only those exact signed bytes were uploaded, once, to the existing Internal
track. Play accepted three release-note languages and reported only the existing
missing-deobfuscation-mapping and native-symbol warnings. Supported-device
readback showed no lost devices. No Production, tester, billing or security
changes. Code33 is consumed; a future upload needs34 or a higher unused code.
Preview32 and earlier artifacts/evidence remain unchanged.

## Limits and custody

No physical Play installation/update, Play signing identity, production App Links,
general beta, complete method/Career parity, SR6, audiobook, tablet, Full Editing,
Windows or Rook completion is claimed. The book worker and its budget are unchanged.

Private packet `life-release33-contrast-20260925.RPAzrxPt` retains inputs,
build/sign/verification logs, screenshot and actual `PLAY_PUBLICATION.json`
SHA-256 `9d12677eb799c4ebadeb6f17a2e48161e346076a1d0ea2e076f763f340653d5e`.
Diagnostic smoke is retained separately in `life-readability-20260925.tGLquLKS`.
Owned emulator, build/sign containers and Play session are stopped.
