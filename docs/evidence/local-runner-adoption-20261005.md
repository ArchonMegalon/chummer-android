# Explicit local runner-to-account transfer

5 October 2026. Local engineering evidence only; no new Play upload or
physical installation is established by this record.

## Scope

A linked user can review and explicitly move a runner created in unlinked mode
to the current account **on this device**. Linking alone does not move anything.
Cancel leaves the original data unchanged. Existing account runners are retained;
the transfer preserves identity, revisions, history, Origin chapters, illustrations,
story preferences, chapter wishes and unfinished creation inputs. It does not
upload the runner, generate paid prose, or create a second runner.

Owner-transition stamps reject stale review after an A-to-B-to-A account change.
A durable Core claim and private-file journal allow interrupted placement to
finish without replaying the rules mutation. Unlink does not reveal the claimed
runner in the unlinked namespace.

## Exact package intake

- Core runtime: `5f4e6350791c2c4fd7959da1f07cc1a85ce5446e`.
- Core package recipe: `51c6a6bbdb1a498352f95512b7d47f11e6fa42b7`.
- UI seal: `3f33ab8e46a4e795a683010d50a10afc5de97187`.
- UI merge: `bbb1ccaf62bc3db038f225834965a6711793a06a`; tree identical to the seal.
- UI local consumer receipt SHA-256:
  `022453dd36082005fc1b1e9a300158e09d721868349333b1047662604ab3da6c`.

The existing Android package verifier authenticates the actual consumer receipt,
both restored Android locks, all 18 retained packages and 13 authority files.
The rule-data manifest checks all 331 files; their content is unchanged.
The UI producer's configured test floors are now 781 overall and 27 bootstrap
cases. The intake checks those exact values and rejects the older 774/24 values;
configured minima are not observed passing totals.

## Executed checks

- Local UI consumer: all five affected builds and the complete Product test run
  passed, with 869 tests passing, plus the existing focused owner suites.
- Local Android intake: native compile check, MAUI Compile target and native
  interaction-test project built with zero warnings/errors. Core runtime comes
  from the exact packages; Presentation source and Core rule-data roots are
  explicit. This is not a package-only MAUI build or an AAB.
- Eleven focused Origin cases and both real-coordinator adoption cases passed:
  direct handoff and cold partial-placement recovery, including synthetic full
  prose, image, EPUB, timeline, owner isolation and retained creation inputs.
- 98 package/workflow/pin regression cases and 101 affected source, release and
  verifier cases passed. Private-key hygiene and actual package/content checks
  passed. The new floor regression failed before the verifier correction.

The earlier API-36 x64 diagnostic route exercised unchanged adoption behavior
using explicit source roots: normal account linking, no automatic adoption,
review cancellation, one confirmed transfer, exact save/reopen in a new process,
and unlink. Its separate SDK-test-signed APK SHA-256 was
`bb81f1a011dbee0bea0d3bec3a2496723239aa1feadf293095d09c485f078c41`.
This is reused source-behavior evidence, not a device run of the new package
assembly. The device fixture had no paid prose; full-book preservation above is
managed integration evidence, not a live native FirstBook-reader result.

## Still separate

A release bundle, isolated upload-key signing, Play processing/readback and
physical Play installation require their own actual results. Full live native
Origin reading/export, every SR5 creation method, seven-journey coverage, tablet
parity, and general-beta completion are not asserted. Preview 109 remains the
last recorded Internal availability until a newer observation is committed.
