# Preview 173 — clearer Creation steps

Authenticated Chummer Console showed `173 (0.1.0-preview.173)` **Available to
internal testers**, one version code, release 167, on 10 October 2026 around
10:29 Europe/Vienna. Play displayed release time 10:28. One exact AAB upload
and one final confirmation used the standing 2026-10-05 Internal approval.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API evidence. **Physical Play 173
installation/update is unverified.** Play says changes usually appear within
one hour, occasionally longer. Version 173 is consumed; do not rebuild/re-upload
it. Previous evidence and rollback artifacts remain unchanged.

## Change and actual checks

SR5 Priority and Sum-to-Ten Creation no longer show unrelated inactive Foundation
and Life Modules cards. This is presentation filtering only: required, available,
complete, blocked or warned steps stay visible, and the Core snapshot is not
changed. Other methods/rulesets retain their existing behavior. The single
Continue action, inline attribute Karma controls and Settings-only diagnostics
from Preview 172 remain; explicit Review/Confirm and owner guards are preserved.

- 16 Creation source-contract tests and five current-phone scope tests passed.
  Stale source assertions were corrected to the already-existing route guard
  and localized finish label; the initial failed assertions are not a pass.
- Focused managed build passed with zero warnings/errors. Real Core fixtures
  cover Priority/Sum-to-Ten continuation/filtering, required/blocked/warned
  stage retention, unchanged snapshots, review routing and stale/owner-ABA
  rejection. Managed product inputs differ from the final native build only
  by the version bump; no broad-suite claim is made.
- Exact final-source x64 Release build passed with zero warnings/errors in
  2m21s. API 36 smoke showed the filtered Creation dashboard; one Continue tap
  opened the real Skills editor. A verified force-stop/new process restored
  the same dashboard. Four saved workspace files and both preference files
  remained byte-identical, including the saved diagnostic opt-out.
- The synthetic app's HTTPS egress was blocked. No account/provider operation
  or credit use occurred. The private SDK-test-signed x64 APK is not execution
  of the production ARM64 Play artifact or a live tester-diagnostics receipt.
- Startup remains slow. System UI ANR and transient null accessibility roots
  were recorded; no repeated navigation tap was used. A separate unchanged-APK
  CPU-quota comparison shows an environment confound, not a startup-speed fix.

Complete SR5 Creation/Career, seven-journey, SR6, tablet and physical-device
qualification remain open. Do not promote this Internal binary unchanged to a
public track.

## Exact local artifact

- Android producer `549b207dd90d3f5b998493c0eaa88f57a90a71f6`;
  protected PR634 merge `c2b7b1b403210c9198b36acf46bacd630d334e8b`;
  identical tree `d6c589b7f2ac87a0217f98357374f8b05471eabf`.
- Presentation seal `73185db34dc2160370d5c876962800b21b05eac9`.
- Core recipe/content `0e03f6affc030cffdabcde70bfe0a65b331ad1a7`;
  runtime `c8e7f795d8796ed3b52280fa7a9003e988cd5213`.
- UI verification: `50931344aa416f2ed44494143c8ff4a3ec25fb54634afdd09a353869cc4f3912`.
- Package binding: `04d0902c446733f077b3c401007ed1e1e5d2238eb76ddef1880408843a184bce`.
- Native private APK: `34c336eeeff9e3efe70138814ceacc9dba948be9ed140f82b6721ab3da97b79e`.
- Native smoke record: `d35923355ffcd69b0e409b4fb153ce7f96399a6aa384de973df66afff4c7b48b`.
- Unsigned AAB, 35,872,337 bytes:
  `22970fe46d2d4eaa2bacc48e1477ef810440c475af84c810b178d577b7ccaf02`.
- Signed AAB, 36,045,377 bytes:
  `6e03d2da882b5bf40ad03124cbf27a05d3e2cb5341514d7e6379bf3541689e41`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content verification:
  `5143c80794229b6b22ac09ddd9fbae3b1e01ad25926528172987aac896822fde`.
- Independent keyless signature/payload verification:
  `da533f07168a9d0f51367b4421190977d49c57d09c8f18a477b116ff64e14b37`.
- Visually inspected availability screenshot:
  `4252f9d52d28d3ee587aaa8e62fe2db26daf73ca40f57bc897c50c14a5fa926f`.
- Private browser readback receipt:
  `0b071e9fc52cd45199c13efff6745db614f4fc9e2ae64323ca2884fdc36af9bf`.

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
