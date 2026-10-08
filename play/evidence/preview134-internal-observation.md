# Preview 134 — available Creation equipment

Authenticated Play Console readback at 2026-10-08T00:16Z showed
`134 (0.1.0-preview.134)` **Available to internal testers**, Internal release 128,
one version code, released 8 October 02:16 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is Console-observed availability, not Publisher API or physical-device
evidence. **Physical Play 134 installation/update remains unverified.** Play
states propagation usually takes up to an hour and can take longer.
Version 134 is consumed: do not rebuild or re-upload it. Earlier artifacts and
evidence are unchanged. No public, audience, listing or account changes occurred.

## Change and affected checks

Creation Gear filters Core-issued ineligible, inexact-price, inexact-availability
and blocked options before search and paging. It does not recalculate rules or
promise that every displayed item fits the remaining basket budget. Quantity
and line limits, saved baskets, current-owner checks and final cost review remain.
EN/DE/ES guidance describes the supported catalog and cost review.

Affected managed and native builds passed with zero warnings/errors. Focused
runtime tests use the real Core catalog and verify exact eligible membership and
ordering, every rejected eligibility flag, no visible internal rejection codes,
search reset/empty results, stable controls, quantity bounds, no browsing writes
and stale-owner rejection. Three applicable source-contract tests passed; this
is not a claim that the entire historical source-contract module ran.

The API 36 x64 SDK-test APK
`abb482af272515e36dd466aee8571e46560a553f4959164dfb9237eccec9fb9d`
showed 549 eligible entries and three Flashlight search results. A synthetic
Priority/Human/Mundane runner selected one Flashlight, reviewed 25 nuyen with
449,975 remaining, confirmed once, and displayed the saved receipt at revision
6/6. After verified process death, a new process reopened the same quantity,
costs and saved state, with no-change confirmation disabled. Saved bytes match:
`20c09219d336647f9c98d086039f33eec5f080a4725bdcdc2ecae047c47ed8c2`.
The retained Career runner was unchanged. No real user data or provider credits
were used. The tested product tree was
`9648e779824f8d3f46200a094a248f181de21dc2`; only version metadata differs.

The first emulator host terminated at its 256-task limit. The second used the
same preserved AVD and 512-task limit and completed the route. This is not an
application-crash or performance-pass claim. Saving and cold restoration remain
slow. This smoke does not finalize the unfinished Priority runner or establish
all-method, physical Play, whole-app or startup-responsiveness coverage.

## Exact local artifact

- Android producer: `c0f56eb5dc67e5120de9687cb5a757071ac609db`;
  normal PR 548 merge: `69dfcd0f5879f545903a8f28fce7a6192bd6da51`;
  identical tree: `297231fa32ff9638cb87628a94da7e9a2b9df97e`.
- Presentation seal: `0a9c2ad615386aba8e1f56d68ffa514a3216daf5`;
  Core runtime: `66576d96153f0888da88959c2f3f527f112761c3`;
  Core recipe: `81ce9602465504e429eab8166ef4964a7592166c`.
- Unchanged UI verification receipt:
  `aaf301cb0bf4e0a6ca4e2831a901985f7e8b8420d8bf10406a6a43375c0fd129`.
- Unsigned AAB, 33,814,392 bytes:
  `f0eff021ce982e72e9f74256a10baacc21a0183a5bf5f9e1cffb21751ca6d2a8`.
- Signed AAB, 33,986,989 bytes:
  `4856a7b41146c26762887b534d5ba06f2e50f6ba1a17142841709792fd3e185e`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `f9351f3884af7a34014b9a07aeb502314f6a786208b0ad20ac80641a9cf0e833`.
- Independent keyless verification receipt:
  `b56a3f409b9ba7e4f5820d76b462625522a259dd6857a1d20532bab2ba70fc13`.
- Private Console execution record:
  `98597892388e8b9f53c98899b2eccf80fa49f38b997f641d11cbffb02852d2a6`.
- Visually inspected availability screenshot:
  `f2825cfbec6b6fc2e87d69ceddf1d7057af32ba13ef9e91266085ee9fbe1ad3c`.

One offline local ARM64 build used .NET 10.0.112, JDK 17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, source exports, 18 packages/13 authority files, 331 embedded
content files, identity/version/API/ARM64, proof exclusion and credential checks
passed. Mode is locked package closure with explicit pinned Presentation source
and Core content, not package-only or ambient siblings. Separate existing-key
signing and independent offline keyless verification passed strict JAR signature,
certificate identity and all 1,783 unchanged payload entries.

Required checks passed before normal merge. One upload and final confirmation
used standing Internal approval of 2026-10-05, separate from candidate checks.
Expanded Play validation showed only existing deobfuscation/native-symbol
warnings, with unchanged supported-device counts. Signing inputs stayed private.
Build/sign containers and the owned release browser are stopped. An additional
Priority completion smoke is separate ongoing work, not release evidence here.
Windows implementation remains stopped; finished-app status is not claimed.
