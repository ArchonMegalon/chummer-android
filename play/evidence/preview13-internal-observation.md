# Preview 13: local build/signing and observed Play Internal availability

The owner requested local builds and local hosting on September 19, 2026, then
explicitly requested publication of the locally built app. This records the
actual local transaction and authenticated Play Console observation. It is not
a hosted signing attestation, a package-only consumer proof or an installation
receipt. Preview 12 and earlier evidence remain unchanged.

## Change and verification scope

Career and Commerce menus now use cheap saved-runner/service-availability
checks instead of loading cyberware, custom-drug and vehicle catalogs while
drawing navigation. Destination wizards retain their Core preparation, review,
confirmation and recovery checks. No rule or mutation implementation changed.

The app change passed the existing focused Python checks (6 contextual and 11
Career tests), the native compile checks and the managed menu-interaction test.
The local x64 Debug smoke exercised Career → Commerce → Cyberware preparation
→ back → reopen, with no purchase. The saved runner's identity, revision and
document/payload digests were unchanged. An initial System UI first-boot ANR
and a separate fixture rejected for missing Ex-Con state were retained as
failures; the successful route used a separate valid synthetic fixture. This
does not establish cold-start, all-wizard or physical ARM64 qualification.

Only AGENTS.md and version/inventory hashes changed after that tested app source
(`dd36bae5c1aa72a52989618980f0da67b6c050fb`). The released source is
`dd5e6addc00971009de0449a028e9e1c42d51847`, tree
`2e2f89f25add21baf9e841b8d541131447213822`, on the pushed branch
`fix/career-menu-lazy-catalogs-20260919`. It was not produced from a `main`
merge. Subsequent integration must preserve this actual producing identity.

## Actual local release transaction

1. Exact git-archive source exports were built in the existing local Docker
   toolchain, using a copied local NuGet cache and explicit source roots.
   The builder had no network, signing key, credentials or Docker socket.
   It used a read-only root, UID/GID 1000, dropped capabilities and resource
   limits. This was a pinned multi-repository source assembly, not package-only.
2. The first attempt failed because Xamarin selected a read-only default cache.
   Supplying the local original-AAR cache corrected that environment issue;
   all five locked AAR digests matched. The corrected build completed in
   1m35.30s, zero warnings/errors, without OOM. Both logs were preserved.
3. Before signing, bundletool validation, version/API/ABI/permissions checks,
   binary proof-instrumentation exclusion, artifact hygiene and all 110 exact
   canonical content-file checks passed.
4. A separate offline container used the existing upload key. No key was
   generated, rotated or exported. Key inputs were read-only, temporary password
   files lived only in tmpfs, and only the signed-output directory was writable.
5. A separate keyless container verified the expected certificate, strict JAR
   signature, unchanged ZIP payload, version/API/ABI, hygiene and proof exclusion.
6. The exact signed AAB was uploaded to the Chummer Internal track. The operator
   reviewed the release and completed both publication confirmations. At
   `2026-09-19T09:14:08Z`, authenticated readback showed **Active**, latest
   release **13 (0.1.0-preview.13)**, **Available to internal testers**.
   The displayed release time was `19 Sept 11:14`; no provider-authoritative
   UTC publication instant is inferred from that localised display.

## Exact artifact and input identity

| Field | Value |
| --- | --- |
| Package | `com.myexternalbrain.chummer` |
| Version | `0.1.0-preview.13`, code `13` |
| Signed AAB | `chummer-android-0.1.0-preview.13-upload.aab` |
| Signed size | `30612904` bytes |
| Signed SHA-256 | `0eaffd296f1a447fbd50ea8b45e6ceb0e53a9d9ac0966d705a6d2cdfe5e3e217` |
| Unsigned SHA-256 | `00d953f6bb0184f300a4e18781fff407a9f20e4a3e1a96c0f0be949f2aba95e0` |
| Local source-input record SHA-256 | `448bc6e862dc696c8ecd47bd3560db51fb3bbccdfb7e7666acad6b606986fa96` |
| Upload certificate SHA-256 | `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15` |
| Docker image | `sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5` |
| Toolchain | .NET `10.0.111`, Android SDK `36.1.69`, MAUI `10.0.20`, JDK 17 |
| Presentation source | `9a869420ecc335f9a54968debeff6723d4997ff7` |
| Core runtime source | `3bc5fe725fd2bbbad0333c5c7a3f849e53808c4f` |
| Hub source | `e35db6feca8f194161302064a9f77d4f8e60fe14` |
| Registry source | `af9a7e19c3bf331e96411dfb8f9e7820a98cab29` |
| UI Kit source | `d51ecd99cf72098d4adc8db0192bff7bf9fd8e61` |
| Media source | `f3c955488210c69abdf96689dd3b3d67afba80d2` |
| Content validation source | `1d8cf694d0412b3bd9f4a241fb95244fad341160` |
| Inventory Design source | `e408e11bdfabc34898cb0eca6e2409a98d021e4d` |

The local source-input record is not the hosted v3 release-source-graph contract.
The upload-certificate identity is not the separate Play App Signing certificate
used for installed APKs. Upload linkage is the exact-file action and local
digest check; no provider AAB was downloaded for byte comparison.

## Evidence and remaining limits

- [Signing output](preview13-local-signing.json) records signing only. Its later
  verification/upload flags remain false, as emitted before those actions.
- [Verification output](preview13-local-verification.json) records successful
  keyless verification; its upload flag likewise remains false.
- Play reported two nonblocking warnings: no deobfuscation file and no native
  debug symbols uploaded. Supported-device counts did not change from Preview12;
  device eligibility does not establish tablet or foldable behaviour.
- Physical Play installation/update, production HTTPS App Links, complete
  phone-beta qualification, full editing, SR4/SR6 creation, tablet readiness,
  Rook and public/Production release remain unclaimed.
- No tester list, billing, security setting or Production track was changed.
  No credentials, browser session or device identifier are recorded here.

The retained local packet `android-preview13-local-20260919.p0QhFopC` contains
the signed bundle, source-input record, actual publication record and screenshot.
Screenshot SHA-256:
`24f8ede6b1f99357fa5f6563336ce0d30940f4edfc489adbd9d92f6fe8f90cb6`.
Temporary build/signing containers and the owned browser session were closed.

Internal testers can [install or update through Play](https://play.google.com/apps/internaltest/4700678198570024687).
Code 13 is consumed. Do not repeat this upload; use a higher unused code next time.
