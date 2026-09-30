# Preview 56 — simpler Creation screens and saved-choice recovery

Authenticated Chummer Play Console readback at `2026-09-30T11:35:13Z` showed
`56 (0.1.0-preview.56)` **Available to internal testers**, Internal release 52,
track Active, one version code. This is browser readback, not Publisher API proof.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).

**Available on Play Internal; physical Play installation not yet verified.**
Version 56 is consumed; do not rebuild or re-upload it.

## Delivered change and affected checks

Creation overview, Priority choices, Gear and Resources show fewer repeated
headings and technical details. GUIDs, hashes, revisions and exact diagnostic
codes are collapsed behind explicit technical details on these surfaces.
The dashboard uses one runner heading/picker and plain English/German/Spanish
blocker guidance. Normal diagnostic overlays no longer enter TalkBack output;
proof configurations retain them. Actual budgets, warnings, touch targets and
owner/persistence checks remain. This is not an all-app GUID cleanup.

Gear confirmation now shows pending-save feedback rather than a stale warning.
Explicit Skills and Magic re-review preserves saved choices after Attributes
changes while checking current owner, revision and effective rules. It does not
silently mark stale data exact or replay an uncertain save.

Focused managed tests covered local/linked ownership, owner transitions,
single submission, UI responsiveness, failed refresh, cold recovery and uncertain
reply/no-replay behavior. The final minimalist-dashboard checks covered localized
guidance, dependent-Attributes locks, picker destination, Priority/Sum-to-Ten
Continue routes and blocked/stale-owner rejection; 15 Creation source-contract
tests passed. The unrelated obsolete Origin source assertion was not reported
as passing. Required source/safety and GitGuardian checks passed before normal
protected merges; no branch protections or hosted gates were changed.

Native API-36 x64 smoke observed a Priority Magician's explicit Magic re-review,
Gear confirmation, single finalization and Career entry, with saved-state restart
checks. The later Gear feedback smoke saved one Flashlight once (revision 7 to 8),
then reopened it in a new process. Final presentation-only smoke verified the
dashboard, real budgets and explicit Show/Hide technical details with large text;
the synthetic saved workspace remained unchanged and the crash buffer was empty.
The dashboard fixture was seeded from Core, not a new all-native Creation claim.

Final diagnostic APK SHA-256:
`3b1751f15fa5c983e18fa0d30a76eff2e4a51606a6fe0c0ce09359e02ae71a58`.
The release producer changes only version metadata after that tested Android
implementation. Native smoke was Debug x64, not physical ARM64 Release execution.

## Exact local release

- Android producer `6548fb13579d9f9f9ffac5dc976928b4590d01f4`, PR 226 merge
  `3918eb5ce2de446095849a5e41d82cda91161e54`, identical tree
  `d4e9f278798336cbd2a6daa3d479a2fdd940acd3`.
- Presentation seal `c9391946ccd84e9978cfdd4cbb6671c1c6836ffd`;
  Core runtime `a5a03a8bd529f3dc9ed6658b55cc6d37daa6da77`, recipe/content
  `0d4887f0c6ca3ca2afd883577e682f053dbad6d2`;
  Hub package producer `c77395de9f733427ef952c851f4a95b063cb5573`.
- Source-input receipt SHA-256:
  `8e13b92a1bc1aeec99706ffcf7fb543a6c8dfffdffda5b556337424ee5b82a25`.
- Unsigned AAB: 33,069,312 bytes, SHA-256
  `a7b6a1052db719753d18d0bbf5906e57f057819ae37bb1a1983fd0bf4891e05c`.
- Signed AAB: 33,241,784 bytes, SHA-256
  `de3a90cb8c4a1a5f438ad02e5c56e497e51e154744f4c2030005b774b34627da`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Independent signed-verification receipt SHA-256:
  `fc39296e660926aa7d05b5b5aafeac3880c87888d0a6e0aec703a7a12e2453d9`.

The local keyless ARM64 Release build took 145.63 seconds, zero warnings/errors,
.NET 10.0.112, toolchain image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
All 330 content files, bundle/API/ABI/privacy checks, private-key hygiene and
proof exclusion passed. Separate original-key signing and independent keyless
strict JAR signature, certificate and unchanged non-signature payload checks
passed. Builder and verifier had no keys or network. The admitted 18-package
graph was checked against the actual local consumer receipt; dependency mode
remains locked package closure with explicit pinned Presentation source and
Core content, not package-only native assembly or hosted qualification.

Private packet `creation-release56-20260930.c7Md4I` retains inputs, both AABs,
build/sign/verification logs and Console evidence. Internal readback receipt
SHA-256: `b8fa83d285cabae621a581be1fce30f9acd2792c94f6559900bc6e20b365ebb4`.
Exactly one upload and one final publication confirmation were performed. Play
reported missing mapping/native-symbol warnings and no lost supported devices.
No Production, tester, listing, security or billing change occurred.

## Remaining limits

This does not establish all-screen polish, all build methods/Career actions,
unattended full-book quality, cross-chapter likeness, hosted seven-journey
qualification, tablet/Full Editing/desktop parity or public-beta readiness.
No paid-provider action was performed for this release. Uncertain FirstBook jobs
remain fenced against replay. The upload certificate is not the Play App Signing
certificate. Physical version 56 remains unverified;
[Preview 52](preview52-internal-observation.md) retains the latest physical
installation evidence. [Preview 55](preview55-internal-observation.md) and all
older artifacts/receipts remain immutable.
