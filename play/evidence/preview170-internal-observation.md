# Preview 170 — Origin digest scratch allocation reduction

Authenticated Chummer Console showed `170 (0.1.0-preview.170)` **Available to
internal testers**, one version code, release 164, on 10 October 2026 at 03:47
Europe/Vienna (01:47 UTC). Play displayed release time 03:47. One exact AAB
upload and one final confirmation used the standing 2026-10-05 Internal approval.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API evidence. **Physical Play 170
installation/update is unverified.** Play says changes usually appear within
one hour, occasionally longer. Version 170 is consumed; never rebuild/re-upload
it. Previous evidence and rollback artifacts remain unchanged.

## Change and actual checks

Core reuses cleared, operation-local scratch buffers while calculating Origin
history digests, with a fresh hash instance and unchanged canonical bytes.
There is no shared owner-state cache. Focused synthetic host measurements showed
about 25% fewer allocations for historical/full-shape projections. This is
**not proof of faster native startup**.

- Core focused tests: 23 passed with zero build warnings/errors; package lock
  checks: 55 passed. Exact local package producer: 826 managed, 18 owner-admission
  and 15 inventory tests passed. Legacy compile retained 17 obsolete warnings.
- Exact UI seal: 880 product tests and focused checks passed, including 60
  finalization-owner cases. Production builds were warning-free; the test
  assembly retained 62 existing analyzer/nullable warnings.
- Android intake compiled with zero warnings/errors and passed focused startup,
  Life Modules, retained full-reader/export, owner-transition, cancellation and
  save checks. Exact package/content bindings passed; 59 focused Python tests
  plus 95 subtests passed.
- Final producer native x64 Release build: zero warnings/errors. API 36 smoke
  reopened the complete retained short synthetic chapter after verified process
  termination in a new process. All four saved workspace hashes and the saved
  diagnostic opt-out hash were unchanged. EPUB/HTML controls were enabled;
  this native smoke did not perform another export or provider generation.
  The SDK-test-signed private package is not physical execution of the signed
  ARM64 Play artifact.

The constrained emulator showed Launcher and System UI ANR dialogs; explicit
Wait actions and slow startup remain recorded, not hidden as successful speed
proof. The new process took about 21 seconds for selected-runner restoration.
No controlled native speedup or resolution of the original tester incident is
claimed. No provider credits were used. Internal diagnostics default on only
without a saved preference; opt-outs, disclosure and disable/clear remain.
Prior synthetic intake proof does not establish capture of real tester incidents.
Do not promote this Internal binary unchanged to public distribution. Complete
SR5 Creation/Career, seven-journey, SR6, tablet and physical-device qualification
remain open; this is a bounded experimental Internal increment.

## Exact local artifact

- Android producer `92805e7e61074333333283b45630654a63154300`;
  protected PR627 merge `bf0bd5a4327667f48ae2db377c590b8b81b0b41b`;
  identical tree `79e6d1c5a9b46961c37cef23526d38aba33c7584`.
- Presentation seal `8e711b8ebcfefd6cc9307fc865cc0c24a8e489e8`;
  protected PR406 merge `1ec0d2f6d164b6615ae8c7658b8ac0382f7f3faf`;
  identical tree `18840b9c907a8654a584bd7268d3a17e5d9707ce`.
- Core recipe/content `9af8368e31943cec1d93ffea6342e721cf65ff80`;
  runtime `4297b07e57397d678c201f5fcc20f62dfeb57afa`;
  protected PR142 merge `8914ea3d7103a401032353c8a7dd7d29c4532ac5`.
- UI verification receipt:
  `4fdea9db1777524d54acc92e3222746ec698aa607a78d623e86f7db5711c23b7`.
- Package binding:
  `5fc1e3353e79e858d9c0646fda34ff0fa5f4c29414d2e00c624e9e250fc81e0a`.
- Native private SDK-test APK:
  `18fcf6347c5e1ecae2e45711c1d83c0e292a90b42d1a54cad2f14f5f75da82b4`.
- Native restart report:
  `f4838e672ee1c9f8a510e5ac317d55c07547cb156bd93c8ed437bbffc16cfec8`.
- Unsigned AAB, 35,862,773 bytes:
  `cb32665ba78dce151fac088fba4206f728f59f3f3599e19d08eb092c82c8081f`.
- Signed AAB, 36,035,808 bytes:
  `945404c279755eb3e31e6d5f7d5f2e953bb33b376e192dca8b3fb356f9cfc6f2`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content verification:
  `f248cdb4f5acc5128ad4e28d3ca215743d7d35f6177d3fb5f5023f9455e334c9`.
- Independent keyless signature/payload verification:
  `828d50c379ed4c5373107c4888a3a01de2ffaa923b2363090994512bbc3258bb`.
- Visually inspected availability screenshot:
  `fb2ffec4b222e316c55e713c1343f770e90f31c923dfaaeae705c4c8222ecf0f`.
- Private browser readback receipt:
  `b9e8d930ff6cd2f62f75c58b6e0b45fd160c3fd7fb955f0ac6eac215e5bdd234`.

Offline local Docker ARM64 build used .NET 10.0.112, JDK 17.0.20.1, builder
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`
and explicit `ChummerDistributionChannel=internal`. Exact source exports,
18 packages, all 331 Core content files, version/API/privacy checks, credential
hygiene and proof exclusion passed. Assembly uses locked packages plus explicit
pinned Presentation source and Core content, not package-only or ambient siblings.
The separate offline original-key signer and independent keyless verifier passed
strict JAR signature, certificate and all 1,787 unchanged payload entries.

Play recognized API 24+, target 36, ARM64 and no supported-device delta. The two
missing mapping/native-symbol warnings remain. Catalog compatibility is not
tested device parity. No public-track, audience, account or unrelated-service
changes occurred. Owned emulator and temporary build containers are stopped;
private packets remain outside served directories.
