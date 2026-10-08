# Preview 141 — Karma reads during account hydration

Authenticated Chummer Play Console readback at `2026-10-08T07:49:18Z` showed
`141 (0.1.0-preview.141)` **Available to internal testers**, one version code,
Internal release 135, released 09:49 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API evidence. **Physical Play 141
installation/update remains unverified.** Play says propagation usually takes
up to an hour and occasionally longer. Version 141 is consumed: do not rebuild
or re-upload it. Preview 140 and earlier artifacts/evidence remain unchanged.

## Change and focused checks

Karma Open and Load now use the existing cancellable background read-admission
scope while local account hydration owns the credential gate. An unchanged owner
no longer gets an immediate unavailable result merely because that read gate is
busy. Exact owner, page, workspace, profile, revision and digest checks remain.
Mutation admission is unchanged; there is no retry, timeout increase, automatic
choice, account relink or replay. See the
[focused result](../../docs/KARMA_ACCOUNT_READ_ADMISSION_20261008.md).

The actual account-service regression failed before the change and passed after
it for Open, Load and cancellation of both while hydration remains held. It
checks responsive UI callbacks, zero Hub requests/confirmations, unchanged owner
and exact persisted bytes/content/saved revisions. Existing Save/read-failure,
owner A→B→A Open/Load, departed-page and cancellation cases also passed. The
frozen ARM64 source graph reran the new admission regression successfully.
Managed and native x64 builds had zero warnings/errors; 15 wizard source-contract
cases and private-key hygiene passed. Protected PR 563 passed source/safety and
GitGuardian checks and merged normally. No hosted runtime/build/signing claim.

Actual API36 x64 smoke updated the retained synthetic application in place using
distinct SDK-test-key APK
`f05bd92be32adde01f4d8c0ce93ac6288df96bd0b04cf1614e4d96182037a188`.
The pending Karma dashboard and foundation editor opened at revision 1 / saved 1,
with Metatype enabled. Actual toolbar Save retained readiness; verified
force-stop/new-process reopening restored the same editor. All four workspace
file hashes were identical before Save and after restart. No choice/finalization
was applied. No ANR overlay appeared in these captured hierarchies/screenshots;
this is not a general performance or clean-boot guarantee. Earlier intermittent
warnings lacked failed Core Open packets, so not all are conclusively attributed
to this separately reproduced contention defect.

The full-book reader's per-chapter illustrations remain included. Preview 139's
four-chapter native-control/deferred-stream/EPUB/offline-reopen coverage is reused
for unchanged reader inputs, not relabelled as a new provider or physical test.

## Exact local artifact

- Android producer: `1f8ec3efaa5837322414b9b0127dce35160a5ffb`;
  normal PR 563 merge: `74adc0ed1db092a860deaa9a1c4a19e213a0c977`;
  identical tree: `c36bd08c63b157b0b954d9ec73a10f8d1ac943d3`.
- Unchanged Presentation seal: `8dff197bf87958293f419bf25de8444b7c82848f`;
  Core runtime: `5f649b936ece09fbe3f56b5a86f48597df8180b0`;
  Core recipe/content: `b5d7420087c0aed502b5d7a80d1896836a5c9e7c`.
- UI verification: `9e5223a9a457e14d46c58bab62ed3dfe8d5b0192693730088c9ec64fcf823181`.
- Unsigned AAB, 34,954,375 bytes:
  `27c22876106786d6d28c9348a33b87f01402d6bc80b2b37dcdfa9c8d73180e69`.
- Signed AAB, 35,127,030 bytes:
  `e724627e96f6742543695e1034841b2a00871b86f98bb7bae1d658a61ee7950a`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `c79f8e41d6e0f1e869267a2be866cb164066d363f42311f6cba73d559e93f056`.
- Independent keyless verification receipt:
  `9e78866ea931d41dafa4c37ff696ee312d2163e9ee9b161894a0cdc692a0edfe`.
- Private Console execution record:
  `87d63813ba6c0c41bc2d49b52ba797b2d7c7088e329bcb816830c8db922bcb56`.
- Visually inspected availability screenshot:
  `8f57cb721453472cc0b97df7dec8a258f15aa6f36ba19793dfac59feb1039268`.

One offline local ARM64 build used .NET 10.0.112, JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports, all 18 package bindings and 331 content files,
package/version/API/ABI inspection, proof exclusion and artifact hygiene passed.
This is locked package closure with explicit pinned Presentation source and Core
content, not package-only or ambient siblings. The separate signer and keyless
verifier passed strict JAR verification, existing certificate identity and byte
comparison of all 1,783 non-signature payload entries.

Standing Internal approval of 2026-10-05 was used separately from these checks.
The scoped browser-act/EA browser procedure uploaded exactly that AAB once and
verified actual availability after one final confirmation. Play recognized
version 141, API 24+, target 36 and ARM64; only mapping/native-symbol warnings,
no errors, and no added/lost devices. No public-track, audience, listing, account
or security changes. Keys and local build/browser packets remain private.

Physical installation, complete Creation/Career coverage and general startup
reliability/performance remain open. This is not a finished-app, full phone-beta,
tablet, Full Editing, Windows or public-release claim. No new FirstBook or
illustration-provider job was required.
