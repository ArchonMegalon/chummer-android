# Preview 174 — stable inline attribute controls

Authenticated Chummer Console showed `174 (0.1.0-preview.174)` **Available to
internal testers**, one version code, release 168, on 10 October 2026 around
11:03 Europe/Vienna. Play displayed release time 11:03. One exact AAB upload
and one final confirmation used the standing 2026-10-05 Internal approval.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API evidence. **Physical Play 174
installation/update is unverified.** Play says changes usually appear within
one hour, occasionally longer. Version 174 is consumed; do not rebuild/re-upload
it. Previous evidence and rollback artifacts remain unchanged.

## Change and actual checks

Accepted inline attribute adjustments now update retained rows, buttons, values
and budget cards in place. Scroll position and expanded Karma controls survive
normal adjustments. One fresh owner-bound Core preview still runs per tap,
off the UI thread; explicit Review/Confirm and persistence remain unchanged.
Rejected previews, blockers or a changed row shape retain the guarded full
refresh path. This reduces layout reconstruction, not Core rule computation.

- Eight attribute and 16 Creation source-contract tests passed; whitespace
  checks passed. Two unrelated old legal-path textual assertions also fail on
  unchanged Preview 173 and are not represented as passing.
- Focused managed build passed with zero warnings/errors. Real Core fixtures
  cover Priority/Sum-to-Ten, Magic/Resonance/Edge, independent budgets and caps,
  repeated retained controls, UI heartbeat, duplicate/hidden/detached rejection,
  owner-ABA/departure guards, cancellation recovery and explicit save/cold reopen.
  The first run exposed a test assumption about an already-disabled capped
  control; the corrected exact-source run passed, not the initial run.
- Exact final-source x64 Release build passed with zero warnings/errors in
  2m21s. API 36 smoke exercised Body points and inline Karma increases/decreases,
  retained expanded controls, the special-budget shortcut and Edge increase
  without scroll movement. One explicit confirm persisted Edge 3; a verified
  force-stop/new process reopened the same value. Only the selected synthetic
  workspace changed at confirmation; other runner files and the saved
  diagnostic opt-out remained unchanged. Restart did not replay the save.
- Synthetic app HTTPS egress was blocked; no account/provider operation or
  credit use occurred. The private SDK-test-signed x64 APK is not execution of
  the production ARM64 Play artifact or a live tester-diagnostics receipt.
- Startup remains slow. An emulator System UI ANR and a transient null
  accessibility hierarchy were retained as environment observations, not
  converted into an application speed claim or hidden by repeated navigation.

Complete SR5 Creation/Career, seven-journey, SR6, tablet and physical-device
qualification remain open. Do not promote this Internal binary unchanged to a
public track.

## Exact local artifact

- Android producer `a1498b4cbd306055d356fc21db6a47e86c39d45d`;
  protected PR636 merge `7579b1a73194b3e096e0aecaed1557613e280762`;
  identical tree `72335cfdd6c21b52b5e440f00a225eb1f14dafea`.
- Presentation seal `73185db34dc2160370d5c876962800b21b05eac9`.
- Core recipe/content `0e03f6affc030cffdabcde70bfe0a65b331ad1a7`;
  runtime `c8e7f795d8796ed3b52280fa7a9003e988cd5213`.
- UI verification: `50931344aa416f2ed44494143c8ff4a3ec25fb54634afdd09a353869cc4f3912`.
- Package binding: `04d0902c446733f077b3c401007ed1e1e5d2238eb76ddef1880408843a184bce`.
- Native private APK: `0b3f7f9264b5d72b3d86ae739bcf7b6cb8d3ce133b6c96ff47943c609dbff83d`.
- Native smoke record: `61f0634ebb005d4a8cd18c82f97719d9aec9a1e735f90e7e6788ce0a8b6fd7d6`.
- Managed run log: `2aec56561543f14c0e4c22c6a597c462d2d7723b32bfa80fcd30bfb3ea783baa`.
- Unsigned AAB, 35,876,870 bytes:
  `cef561eb8603b91b799d57929f97b76e9b4d9e1b6784b970251ae9b8691d7717`.
- Signed AAB, 36,049,941 bytes:
  `b3dec0af35c57810e7ec91bee3564b15246d51c01c5a048b1197c5a5bb956dfe`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content verification:
  `71d68c7a880683585f55a0658c345b5eac943564f44d82ec1cafa8f21a5434b0`.
- Independent keyless signature/payload verification:
  `f1d9096b36d2f67f08e39e6f1dae9e280047f16ddb7ff52f1ff40ec12d84aeb8`.
- Visually inspected availability screenshot:
  `e227c9d94430e5ca005263303076799c18f00fbdea656b3c8320672083611b7b`.
- Private browser readback receipt:
  `447e76e1f5bf815b88719db2e25f960cb02e1d65821b570c1d20743023847971`.

Offline local Docker ARM64 build used .NET 10.0.112, JDK 17.0.20.1, builder
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`
and explicit `ChummerDistributionChannel=internal`. Exact source exports,
18 packages, all 331 Core content files, version/API/privacy checks, credential
hygiene and proof exclusion passed. Assembly uses locked packages plus explicit
pinned Presentation source and Core content, not package-only or ambient siblings.
The separate offline original-key signer and independent keyless verifier passed
strict JAR signature, certificate and all 1,787 unchanged payload entries.

Play recognized API 24+, target 36, ARM64 and no supported-device delta. Missing
mapping/native-symbol warnings remain. Catalog compatibility is not device
parity. No public-track, audience, account or unrelated-service changes occurred.
Owned emulator/build/sign/verify processes and Play browser are stopped; private
release packets remain outside served directories.
