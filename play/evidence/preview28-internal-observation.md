# Preview 28 — observed Play Internal availability

At `2026-09-21T09:14:18Z`, the owner-authenticated Chummer Console showed
`28 (0.1.0-preview.28)` as **Available to internal testers**, Internal release24.
The displayed release time was `21 Sept 11:13`.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
Physical Play installation or update remains unverified.

## Exact artifact and verification

- Android producer `5fd86c72fd0f2e909c3e054a0be72a700cc265b6`, tree
  `9f88554f69729ee8b9a9c2b8a100559a0fa50af2`.
- Core runtime `d1c6e3d22360ce61fd32ed58cb571ac2b50b070d`, recipe candidate
  `67ea1136f739256ceedb4b89eb3a5df2a1d4b3ea`.
- Presentation source `ea9b6839b208f79b11bd4ccc0c58f930f0368b06`.
- Local Design policy `27bed9e67a639859123a0b2eee66b7ea02060812`.
- Source-input SHA-256
  `d0b32711ddb42da03ec478f707f7e67e8562985b2b7a88657c4df631da7ddc91`.
- Unsigned AAB SHA-256
  `b0ede637c2aefb6da43872568d702cb111ae56a4f33f0191e858c5717564288c`.
- Signed AAB SHA-256
  `9b9657e66f7730122b7e20b9d014a95e859a20effcb2398572a2e0ccb78cd40f`.
- Signed size31,414,781 bytes.
- Existing upload certificate SHA-256
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.

The offline keyless local Docker ARM64 Release build passed in2m32.90s with
zero warnings/errors. Unsigned inspection verified package/version, minimum
API24, target36, ARM64, privacy/permissions,330 exact content files, private-key
hygiene and binary proof exclusion. The separate offline signer used the existing
upload key. The independent keyless verifier passed strict JAR signature, exact
upload certificate and unchanged non-signature ZIP payload. Android independently
validated its exact local Design policy. These are local results, not hosted
qualification or a package-only APK build.

One exact signed AAB was uploaded. Play accepted version28 and confirmed its
Internal availability. The only displayed warnings concerned missing
deobfuscation mapping and native debug symbols. Release notes were accepted in
English, German and Spanish. No Production, tester, billing or security settings
were changed. Version28 is consumed; the next upload needs29 or a higher unused
code. Preview27 and earlier evidence remain immutable.

At availability observation, the source feature branches were not merged and
the separate shared package integration was still in progress. Internal
availability does not prove those later integration or protected-merge results.

## Affected route and limitations

The [local Contacts smoke](../../docs/CONTACT_COLLECTION_LOCAL_SMOKE_2026-09-21.md)
records Priority Add/save/new-process reopen and Remove, empty Gear initial
confirmation, owner-bound Lifestyle overview and one finalization to Career.
The final force-stop/new-process reopen retained revision10/10 and identical
workspace SHA-256
`f69d652936cceacf151c11aafefeb466a2fc05656cd828f83d9f33eeef8547b7`, with
finalization receipt
`sha256:764787ca0c8de4ebbe4f8096900ab0c8d89ef9a00e1b412a5e647f368b78a3b5`.
The smoke spans several Debug APK updates; it is not a complete fresh route on
one APK. Release ARM64 differs from Debug x64 and was not physically installed
through Play.

The final runtime source bodies match that smoke. Subsequent Android changes
were version metadata/documentation; Core changed the imported-history test
fixture and package metadata, not the runtime budget guard. Focused Contacts and
history tests passed24/24, affected Core authority checks5/5, and Android release
intent checks6/6. The existing local package-consumer regression phase later
passed631/631; its source uses the same runtime and corrected test fixture.

Paid-group/overflow-Karma contact integration, non-empty pending Lifestyle
purchases, all build methods, full Career coverage, general responsiveness,
physical Play installation and Play App Signing identity remain incomplete or
unverified. Full Editing, tablets and Rook are not claimed. This is an
experimental Internal increment, not phone-beta completion.

The private packet `android-preview28-local-20260921.aVIkGOUQ` retains exact
source inputs, build/sign/verification logs and `PLAY_PUBLICATION.json`.
Availability screenshot SHA-256:
`20b98d86352b6032b319ae7d20e1fea6ffd2f6227a58dd7c56dd40bb937e3aa5`.
The local Debug smoke packet is `contact-collection-tests-20260921.kBEQ7Qw9`.
Owned build/sign/verification containers exited. The separate package producer
continues; saved data, signing material and prior artifacts remain preserved.
