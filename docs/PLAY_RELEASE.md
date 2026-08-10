# Play release contract

## Identity

- package id: `com.myexternalbrain.chummer`
- target API: 36
- minimum API: 24
- artifact: Android App Bundle (`.aab`)
- signing: Play App Signing with a Chummer-specific upload key

The package id must be confirmed available in the Chummer-scoped Play Console
before the first app record is created. After that point it is immutable product
identity and may not be silently changed.

API 36 is intentional: Google Play requires new phone/tablet apps and updates to
target Android 16/API 36 starting 31 August 2026. The current requirement is
documented at https://support.google.com/googleplay/android-developer/answer/11926878.

## Secret boundary

Upload keys and passwords are never committed. Release automation supplies:

- `AndroidSigningKeyStore`
- `ChummerAndroidSigningStorePass`
- `ChummerAndroidSigningKeyAlias`
- `ChummerAndroidSigningKeyPass`

Logs must not print these values. A release receipt records only the AAB digest,
certificate digest, version code, package id, track, rollout state, and Play
operation id.

Provision a dedicated Chummer upload identity into an explicit directory outside
this repository. The command creates a mode-`0600` PKCS#12 keystore, public PEM
certificate, and `android-release.env`, and fails closed rather than replacing
existing material:

```sh
CHUMMER_ANDROID_SIGNING_DIR=/absolute/private/path \
  scripts/provision-upload-key.sh
```

Source the generated environment only in the release shell. The environment file
uses the four MSBuild property names above, while passwords stay out of process
arguments and logs. It also binds the public upload certificate so validation can
prove that the AAB signer is the intended Chummer upload identity:

```sh
set -a
. /absolute/private/path/android-release.env
set +a
CHUMMER_BUNDLETOOL_JAR=/secure/tools/bundletool-all-1.18.3.jar \
  scripts/build-release.sh
```

Run `scripts/build-release.sh` with `CHUMMER_BUNDLETOOL_JAR` and, when signing,
the four signing properties above supplied through the secure environment. The
script does not interpolate passwords into process arguments.

## Required gates

1. parity and privacy contract tests pass;
2. Release AAB builds from a clean, pinned source graph;
3. bundle inspection confirms package id, version, SDK bounds, app links, and
   absence of broad-storage and cleartext permissions;
4. phone and tablet journeys pass for clean install, upgrade, offline work,
   rotation, process death, import/save-as, PDF/print/share, account linking,
   conflict handling, deep links, and live play;
5. Data safety and privacy answers are derived from the inspected artifact and
   runtime routes;
6. screenshots come from the tested app, not a fabricated mockup;
7. internal-test upload processes successfully and a tester installs it;
8. production widening remains staged and reversible through Play controls.

Google Play requires an in-app deletion path and a functional public deletion
web resource when an app enables account creation. The final Data safety form
must point to `https://chummer.run/account/delete`; see
https://support.google.com/googleplay/android-developer/answer/13327111.

## Publication boundary

A locally signed AAB is not publication. Publication requires a Chummer-scoped
Play Console session or service account. Memorial or PropertyQuarry browser
sessions and app identities must never be reused for Chummer.

## Historical preview.1 release evidence (2026-08-08)

- `android-arm64` Release publish completed with zero warnings and zero errors.
- The unsigned, upload-key-ready AAB is written to
  `artifacts/chummer-android-0.1.0-preview.1-unsigned.aab` and is intentionally
  ignored by Git. SHA-256:
  `2c1724e9feee4999778a90839bde21a32cbbc03187faa72f4fbae74ee9862086`.
- bundletool 1.18.3 validation passed. Bundle inspection confirmed package
  `com.myexternalbrain.chummer`, version code 1, version
  `0.1.0-preview.1`, min API 24, target/compile API 36, arm64 native output,
  modern Back integration, verified `https://chummer.run/app` links, no
  cleartext traffic, and no broad storage permission.
- All 18 Android parity, privacy, lifecycle, release-automation, listing, and
  store-asset contract tests passed. Five phone and four 9:16 tablet captures
  meet the upload dimension contract.
- The SDK-generated debug-signed AAB is QA-only. Its Android Debug certificate
  must never be supplied to Play or configured as the production app-link
  fingerprint.
- API 36 emulator journeys passed for standalone install, cold launch, direct
  Home → New runner command dispatch, Home → Build, campaign command, native
  tools, New runner → build method → metatype priority, Documents UI import of
  `Glessner.chum5`, persisted workspace restart, native Back navigation, and
  phone/tablet rendering.
- At the time of this receipt, publication was still closed. The Chummer Play
  app record and Play App Signing identity were created afterward.

## Current preview.2 release evidence (2026-08-10)

- Version code 1 (`0.1.0-preview.1`) remains active on the internal testing
  track. The next candidate is version code 2 (`0.1.0-preview.2`).
- The exact signed upload candidate is
  `artifacts/chummer-android-0.1.0-preview.2-upload.aab`. SHA-256:
  `2f1daa9329d7f88efd35ebe000b1b3d3e65fa2392157d3f0c600127e604b5762`.
- bundletool validation and structural inspection passed for package identity,
  version, API 24/36 bounds, permissions, privacy posture, predictive Back,
  verified app links, and the arm64 payload.
- The AAB signature matches the registered Chummer upload certificate:
  `CB:C5:DF:FF:A0:10:88:A0:55:51:7E:5C:42:0B:EB:25:41:2A:4F:72:53:9B:20:18:D0:4F:F4:EC:DE:A4:03:2F`.
- All 20 Android parity, privacy, navigation, account-linking, lifecycle,
  release-automation, listing, and store-asset contract tests pass. Debug x64
  and signed Release arm64 builds complete with zero warnings and zero errors.
- This candidate adds compact app-owned navigation and protected account
  linking through browser approval plus signed installation proof. Local runner
  files remain local.
- Upload is intentionally pending exact-artifact approval. Rebuilding or
  changing app source invalidates the SHA-256 above and requires a new approval.
