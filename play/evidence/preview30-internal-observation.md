# Preview 30 — observed Play Internal availability

At `2026-09-24T21:37:45Z`, the owner-authenticated Chummer Play Console showed
`30 (0.1.0-preview.30)` as **Available to internal testers**, Internal release26.
The displayed update time was `24 Sept 23:37`.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is Console browser readback, not Publisher API evidence. Physical Play
installation/update remains unverified and was explicitly deferred by the owner.

## Exact artifact and inputs

- Android producer `a79135c5bb7c5a9aaa190e895f344384a83bb66d`, tree
  `fbd30f123c8e6e4f3e2e1d9a79c2b7178755d6d4`.
- Android PR108 merge `b28ac12fc87f09ae1146badb00151e853a8db09c` has the same
  exact tree; no rebuild was needed merely to change producer metadata.
- Presentation seal `047cf904b1d42d872d3bc271de0c0112ddfe8b74`; UI PR205 merged
  as `bad474264d46c2a504563cd50c213458a0477531` with the same seal tree.
- Core runtime `5160e78a60bcefd952e8720aae6032a127c3a755`; recipe/content
  `1e477c0f5e036eed241f4fe723a0e2eda30c51dd`. Core PR73 is not consumed here.
- Hub package producer `42d0bfbb117ab6250e8b0512dd92585916c6469f`, unchanged.
- UI consumer receipt SHA-256
  `0a9b93bfed53cdc1c1514052f45ecaa72e7c2dd900b9266666e081af6e2795ed`.
- Android package-authority SHA-256
  `9199e13cc00a747cd81bad1aa8a41a5119c6ea19757ddcdb3dc1977695538030`.
- Source-input record SHA-256
  `31173fa1939c9e66640985bfbceb4d7c3447e34c7540d166a0bdbca7a4018254`.
- Unsigned AAB:31,908,057 bytes, SHA-256
  `4bfbd4808c8753236273a056e19d2d528e496a1b91af11fb56d2dcc3dfd3c7cb`.
- Signed AAB:32,080,527 bytes, SHA-256
  `a62c5309e1244708416cd0948de68807a685428b11f1d44449fbe1aa398c103a`.
- Existing upload certificate SHA-256
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
  This is not the Play App Signing certificate, which remains unobserved.

## Scope and verification

Life Modules finalization now defers the presenter's shell synchronization to
Android's single final refresh. Owner, generation, cancellation and mutation
replay checks remain. Normal presenter loads retain their existing behavior.
See the [changed-route checks and limitations](../../docs/LIFE_MODULES_SINGLE_SHELL_REFRESH_20260924.md).
No numerical speedup or complete responsiveness fix is asserted.

The actual local UI consumer verification passed five Release consumer builds,
858 product tests and focused ownership subsets. All18 incoming owner packages
and both Android NuGet locks are byte-identical to the prior passing intake;
that intake was reused only for unchanged inputs. Changed C# has separate
focused managed and Debug API36 confirmation/Career/save/new-process evidence.
135 focused package/source/version tests plus800 subtests passed after repinning.

Local offline keyless Docker ARM64 Release build passed in2:21.41, with zero
warnings/errors, SDK10.0.111 and toolchain image
`sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.
Dependency mode is locked package closure with pinned Presentation source and
Core content: not a package-only APK, ambient sibling build or hosted result.
Unsigned verification passed bundle identity/version/API24+/target36/ARM64,
privacy/permissions, proof exclusion across165 managed assemblies,330 exact
content files and private-key hygiene. A separate offline existing-key signer
and independent keyless verifier confirmed strict JAR signature, exact upload
certificate and unchanged non-signature ZIP payload. No key was mounted in the
builder or verifier.

Only the verified signed bytes were uploaded. Play accepted three release-note
languages and reported only the nonblocking missing deobfuscation mapping and
native-debug-symbol warnings. No Production, tester, billing or security changes
were made. Version30 is consumed; use31 or a higher unused code for a new upload.
Preview29 and earlier artifacts/evidence remain immutable.

## Limits and custody

Physical Play installation/update, account/character retention on that update,
Play signing identity and production App Links remain unverified. The seeded
Debug smoke is not a full native Creation journey or ARM64 Release performance
test. Full SR6, audiobook conversion, Full Editing, tablets, Windows, Rook and
complete phone-beta qualification are not claimed. Book-provider budget and
worker operation were not changed by this app update.

Private packet `life-release30-shell-sync-20260924.PVxSfGJM` retains source
inputs, build/sign/verification logs and `PLAY_PUBLICATION.json`. Availability
screenshot SHA-256:
`42d30d5829267c5f567db21bfaf3d7182b55fde12b9a736689f7519539447b87`.
The owned Play browser and temporary build/sign/verification containers are
closed; source, signing material, release/rollback artifacts and user data remain.
