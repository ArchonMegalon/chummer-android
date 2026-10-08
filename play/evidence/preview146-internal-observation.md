# Preview 146 — lower-allocation saved-character verification

Authenticated Chummer Play Console readback at `2026-10-08T16:02:20Z` showed
`146 (0.1.0-preview.146)` **Available to internal testers**, one version code,
Internal release 140, last updated 8 October at 18:02 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API or physical-device evidence.
**Physical Play146 installation/update remains unverified.** Console says changes
usually appear within one hour and can take longer. Version146 is consumed:
do not rebuild or re-upload it. Preview145 and earlier artifacts remain unchanged.

## Change and affected checks

Core streams canonical finalization hashes through cleared pooled buffers to
reduce temporary allocations when validating saved character data. Canonical
digest bytes, integrity checks, owner admission and persistence semantics remain
unchanged. All 331 packaged rule-data files retain their original bytes. The
exact Core and UI package intake is described in the
[focused intake record](../../docs/evidence/finalization-digest-package-intake-20261008.md).

The 34 digest regressions pass, including an allocation regression that fails
on the old implementation. The local Core producer passed 783 product cases
and focused checks; the exact UI consumer passed five builds, 879 product cases
and its focused groups. Its test project retains 62 MSTest analyzer warnings,
not build errors. Android's native-source, MAUI and interaction builds passed;
the startup subset preserves owner-bound roster reuse and fail-closed recovery.
Actual package/content checks and 165 focused Python cases passed. Protected
Core132, UI363/364 and Android574 merged normally, without changing protection.

An actual API36 x64 SDK-test APK was built from this candidate. In-place update
from145 to146 and a verified force-stop/new-process restart both restored the
same saved Career runner at revision3/3 with its Career actions. All 24 saved
workspace files belonging to eight synthetic runners remained byte-identical.
No save, deletion, finalization or paid provider operation was replayed.
The installed APK hash was independently read back from the emulator:
`8cbf9309225cd99a3b1e39eac8386d5441407c33805b1ba31b171dcb4fe52433`.
Its SDK-test package/certificate are not the production package/upload key.

The emulator had a SystemUI ANR before app launch; the observed Wait action was
used once, then the launcher settled. No app ANR/crash was observed during the
two launches, and system_server did not restart. The owned emulator's 8GiB host
limit had zero memory-limit/OOM events and no swap; it was stopped afterwards.
This is bounded update/reopen evidence, not a clean-boot or controlled speed
benchmark. The prior failed emulator observation remains inconclusive.

Full chapter text and matching per-chapter illustrations remain in the app
reader and EPUB. Existing unchanged-reader coverage is retained; this release
does not claim a new FirstBook/image generation or physical rendering test.

## Exact local artifact

- Android producer: `6ca715723a7f1260cdc779372b6630e20af2e24f`;
  protected PR574 merge: `4f1f2b82b432f6b2694b23a431e5a80c3e5e46e2`;
  identical tree: `f30eb77d792fd5d9d795c4683bbe0bdf5002cba7`.
- Presentation seal: `66b3185dd97a1e9da8002a38d2abbc364fda6dcd`;
  protected UI364 merge: `e192b892d715c3605c971c33bc2a060936ffd72f`;
  identical tree: `a66c9de9747209e5eea2f8e23bd739cdb5118693`.
- Core runtime: `899be24cff0525eedd90c2299ba19dbef7f63cc1`;
  Core recipe/content: `76e77b13b9ce375a3183da9abb6cdf58ecec21af`.
- UI verification receipt:
  `be76cc97cd5c6de6daeeb108267397396d9508d47490b8757d03d89b04a5adef`.
- Unsigned AAB, 34,959,349 bytes:
  `989c6441d235a9c56bc376c2a41ad3957967b5976455a33f601a3674647f4429`.
- Signed AAB, 35,131,979 bytes:
  `c04c34b3b1935e00dd4f6c5a55855b6359f7353257ee45744e3f8fb90ab63312`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `53657cc567039fa088d4a0d41efeeadf77a1a282baf6613ba0deffc5145484c4`.
- Independent keyless verification receipt:
  `f8a9395df753a43931a96cb4bac90d85e71d4249066d93c6f641fe6053975764`.
- Private Console execution record:
  `0aa35b8358316bb3a655534eeb68af3c270636af81f4fa0aa955f82fce2a8716`.
- Visually inspected availability screenshot:
  `bced9d6e3d1c7503e0e9c4a24a973d68984029be61bf12ad9631a26a65b516a5`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports, package binding, all331 embedded content files,
package/version/API/ARM64 inspection, proof exclusion and credential hygiene
passed. Dependency mode remains locked packages with explicitly pinned
Presentation source and Core content, not source-free package-only assembly.
The separate offline existing-key signer and independent keyless verifier passed
strict JAR signature, exact certificate and all1,783 unchanged payload entries.
The self-signed upload-certificate notice is retained; the verifier explicitly
trusts that exact public certificate rather than accepting an arbitrary signer.

One upload, one Save and publish action and one final confirmation used standing
owner Internal approval of2026-10-05, separate from the candidate's checks.
The scoped browser procedure verified the intended account/app/track and real
availability. Play recognized146, minimumAPI24, target36 and ARM64. Expanded
validation showed only missing deobfuscation/native-symbol warnings, no errors
and no newly supported or lost devices. No public-track, tester, account,
listing or security changes occurred. Signing material and raw packets remain
private; no uncertain upload was replayed.

Physical installation, complete Creation/Career coverage and general startup
reliability remain open. This is not a finished-app, full phone-beta, tablet,
Full Editing, Windows or new live-book-generation claim.
