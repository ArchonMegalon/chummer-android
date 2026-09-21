# Preview 27 — observed Play Internal availability

At `2026-09-21T02:14:32Z`, the owner-authenticated Chummer Console showed
`27 (0.1.0-preview.27)` as **Available to internal testers**, Internal release23.
The displayed release time was `21 Sept 04:13`.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
Physical Play installation or update remains unverified.

## Exact artifact and verification

- Android producer `b2381943b5c17b7efa06ab66977e5cecde8b72c5`, tree
  `1bfa750cb24ef0cf2022e8b80547cceb09cd59e0`.
- Core runtime `81e94d200cfda7f4a2d369268cdda43f94070a72`, recipe candidate
  `4a836c703d229094f20b448a5c5005e6ca723045`.
- Core PR68 merged as `0cfcd5792caca4f49c189b53b96e6a8d31abd6c4` at
  `2026-09-21T02:09:51Z`; merged and qualified candidate trees are identical.
- Presentation `eab90a6f9c0113e81c04f24bc4625d6c644d652a`.
- Local Design policy `27bed9e67a639859123a0b2eee66b7ea02060812`.
- Source-input SHA-256
  `b6faafe67b7ae8743b30ad0f547a14ba9d32f36ecf9e1bf4bb83972302f57d5d`.
- Unsigned AAB SHA-256
  `8e2b35ed8a2244d97e9515fd48e685f4434972d476bfc325eec396c2b590a02b`.
- Signed AAB SHA-256
  `4a0a3d87ebe8613a06cea4291770fa4373b8149bbef9787c47faa34131b76fe4`.
- Signed size31,388,852 bytes.
- Existing upload certificate SHA-256
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.

The offline keyless local Docker ARM64 Release build passed in2m30.84s with zero
warnings/errors. Unsigned inspection verified package/version, minimum API24,
target36, ARM64, privacy/permissions,330 exact content files, private-key hygiene
and binary proof exclusion. A separate offline signer used the existing upload
key. An independent keyless verifier passed strict JAR signature, exact upload
certificate and unchanged non-signature ZIP payload.

One exact signed AAB was uploaded. Play accepted its version27, then the Internal
release confirmation completed. The only displayed warnings concerned missing
deobfuscation mapping and native debug symbols. Release notes were accepted in
English, German and Spanish. No Production, tester, billing or security settings
were changed. Version27 is consumed; next upload requires28 or a higher unused
code. Preview26 and earlier evidence remain immutable.

Android PR103 was still open at the availability observation; its exact producer
head had passed the required source/safety and secret checks. Core PR68 was
already merged after its protected checks passed. Local availability is not
proof of a shared package-only APK or hosted runtime qualification.

## Affected route and limitations

The [local Magician smoke](../../docs/LOCAL_KARMA_MAGIC_SMOKE_20260921.md)
completed Human/Magician Karma creation with AGI2, MAG2, English native,
Hermetic tradition and Manabolt. Core quoted5 spell Karma and55 total. Pending
save/new-process reopen retained revision2/2 and exact bytes. Explicit Street
cash roll3, reviewed7 Career Karma and60 nuyen were applied once. Another
force-stop/new process reopened the Career wizard at3/3, byte-identical with
one finalization receipt. The smoke records the copy-only debug APK upgrade;
it does not claim a complete fresh route on one APK.

The underlying Android product source after that smoke changed only in version
metadata and documentation. Release ARM64 differs from Debug x64 and was not
run on a physical Play-installed phone. Core access/selection/completion34/34,
package-authority100/100, Android native phone-magic,321 localization keys and
6 release-intent tests passed. Source-bound tradition/stream, power, spell,
complex-form and Mystic Adept choices are implemented, but only the described
Magician route has this local device proof.

Slow catalog preparation and long finalization review remain rough edges.
System-service boot ANRs were retained; no Chummer crash/ANR was observed in
the retained event log. No general responsiveness claim follows. All-method
coverage, all Awakened choices, all Career actions, physical Play installation,
Play App Signing identity, Full Editing, tablets and Rook remain incomplete or
unverified. This is an experimental Internal increment, not phone-beta completion.

The private packet `android-preview27-local-20260921.zfKo1Hqo` retains source
inputs, build/sign/verification logs and `PLAY_PUBLICATION.json`. Its availability
screenshot SHA-256 is
`5ff702b75e398ad97e237f14703defd3fb3db18470b7b49a7834ba682af747ba`.
The smoke packet is `karma-awakened-phone-20260921.D4hOBO6w`. Owned local
build/sign/verify containers and the test emulator are stopped. Saved data,
keys and prior release artifacts were preserved; only the signed AAB was uploaded.
