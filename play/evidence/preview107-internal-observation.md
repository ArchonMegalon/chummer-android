# Preview 107 — one account-response deadline

Authenticated Chummer Play Console readback on 5 October 2026 at 04:57:56 UTC
showed `107 (0.1.0-preview.107)` **Available to internal testers**, Internal
release 101, one version code, released at 06:57 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API or physical-device evidence.
**Physical Play 107 installation/update remains unverified.** Version 107 is
consumed; never rebuild/re-upload it. Preview 106/105 artifacts and evidence are
retained. Production, tester audience, listing, billing and account settings were
not changed.

## Change and focused verification

Account-response headers and content share the original monotonic request
deadline. Buffered/queued JSON cannot renew an expired budget. Explicit caller
cancellation, independent responses and infinite-timeout behavior are preserved,
alongside credential, ownership, capped-response and no-replay protections.
No timeout increase, automatic retry or credential reset was introduced.

The expired buffered-response regression failed before the production change.
The focused HTTP/security executable then passed, including three new cases.
A fresh local private API36 x64 build completed with zero warnings/errors.
The synthetic native route at font scale 1.3 showed Loading and Cancel; headers
took 12001ms and the body stopped at 20006ms total. Exactly one subsequent empty
catalog read completed, without retry. Controls returned to idle and the account
remained Linked. Force-stop, verified process absence and a new process reopened
the same linked state with an unchanged SecureStorage-preference hash.

SDK-test APK SHA-256:
`f0f57576e59e0673afd4c5f6f2b0e7477385daa0fcf83af3ae8df06acdd16cc7`.
This used a private synthetic endpoint/identity and diagnostic DI; neither the
fixture nor that APK is a release input. The release export matches clean source.
A boot System UI ANR remains recorded. An earlier incremental diagnostic APK
failed in JNI before account code; fresh intermediates resolved startup. Neither
failure is hidden or claimed as overall performance clearance.

The version-only candidate reuses unchanged behavior/dependency results. This
is not hosted runtime qualification or proof of the sole cause of historical
live-account hangs. Successful native live-account recovery and live-provider
Origin behavior remain unverified. No real runner data or provider credits were
used for this transport smoke.

## Exact local artifact

- Android producer: `dd0f6743d3441eabbdc4f21677d70b1c15199454`;
  main merge: `1d009d6377239c9ad7f9330478f3d4feace8efa6`;
  identical tree: `a85374cf8b5c7e043bd643b7e6a0b8a3203bac3a`.
- Behavior PR474 and version-only PR475 merged normally. Required source and
  GitGuardian checks passed; version run37265026270/job111619898041.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,688,004 bytes:
  `5e8ce4736937cd1cb3c3d27a5d88483495b3fd4c167d4ed02c5857cfbadc5b66`.
- Signed AAB, 33,860,629 bytes:
  `b5d3b5f636c9994d8c33c488332b03b02a29dd0c5b01d46cdf6d54d59ef0d8f7`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `d9f4ee050efb946a7bc87fbdf3e3a921d281dfbb293fcf9616771b2963f935f8`.
- Independent signed-verification receipt:
  `2dbcc619261d5d4545470421903107b0ebf80b7cd59b20261a60e16afc5a5609`.
- Private Console execution record:
  `db9b3c38b4fc5bd853ccb857253c6c2b436dbe66c920e33385f1d794b566e8e7`.
- Availability screenshot:
  `6b4e7a4c34cb03f95454b893a98af21a82ef5721f7b9d9673794788161faaa6c`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, 18 packages, 331 embedded content files,
package/version/API24+/target36/ARM64, privacy flags, proof exclusion and
credential hygiene passed. Dependency mode is locked package closure with pinned
Presentation source and Core content, with explicit warm-cache reuse; not package-
only or a hosted build. Separate existing-key signing passed independent keyless
strict JAR/certificate checks and comparison of all 1,783 non-signature payload
entries. The candidate did not contain the diagnostic fixture.

The one existing upload was resumed by observation, never replayed. One Internal
rollout was confirmed under standing approval. Device catalogue counts did not
change; missing deobfuscation/native-symbol warnings remain. Catalogue presence
does not establish tablet or other form-factor functionality. The upload
certificate does not prove the Play App Signing certificate.

Physical installation, successful native live-account/provider behavior, complete
SR5 Creation/Origin, exhaustive native/tablet parity and private Rook remain open.
No general-beta or whole-goal completion is claimed. Private packets
`origin-release107-20261005.TuFHVeex` and
`account-response-deadline-20261005.85wQSi8j` retain the exact artifacts, failures
and bounded evidence outside served directories. No private runner data or
credentials are included here.
