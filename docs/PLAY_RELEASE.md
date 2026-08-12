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

When owner-approved signing custody is restored from EA, import the recovered
JSON into a new external directory instead of sourcing or unpacking it by hand.
The importer requires the Play Console fingerprint, validates the custody
hashes, proves that the PKCS#12 entry contains that certificate, rewrites only
the local path fields, and never prints the passwords or private key:

```sh
scripts/import-signing-recovery.py \
  --bundle /private/recovery/chummer_android_signing.local.json \
  --target-dir /absolute/private/new-signing-directory \
  --expected-certificate-sha256 AA:BB:... \
  --keytool /absolute/java/bin/keytool
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

If SDK API 36 and Java are absent, the guarded
`scripts/bootstrap-build-environment.sh` uses .NET's official
`InstallAndroidDependencies` target. It cannot accept Android licenses unless
the caller supplies the exact documented approval token, requires an absolute
toolchain directory outside this repository, and proves the current Debug
worktree compiles before writing its path-only environment file. Merely running
`scripts/build-release.sh` never installs dependencies or accepts licenses.

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

## Last signed preview.2 release evidence (2026-08-10)

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

## Current preview.3 release evidence (2026-08-12)

- The app source now declares version code 3 (`0.1.0-preview.3`).
- Release artifact naming and structural validation read that version directly
  from `Chummer.Android.csproj`; they no longer carry a separate hard-coded
  preview version that can drift.
- A fresh signed preview.3 bundle must be built with the registered upload key,
  validated, hashed, and approved by exact SHA-256 before Play upload. The
  preview.2 approval does not authorize a rebuilt preview.3 artifact.
- The UI is now a native MAUI Shell with native Home, Build, Play, Campaign,
  and More pages. The Blazor/PWA project reference, `BlazorWebView`, and Play
  `WebView` have been removed from the built app.
- Shared presenter contracts still expose every desktop command and workflow;
  Android renders their tabs, actions, dialogs, inputs, choices, exports, and
  print payloads with platform controls. A platform-neutral compile gate builds
  this entire native surface with zero warnings.
- The guarded bootstrap installed Android SDK API 36 and its accepted licenses,
  build-tools 36.0.0, platform-tools, and a private Microsoft JDK into the
  explicitly authorized external toolchain directory. The current
  `net10.0-android36.0` arm64 Debug app then built with zero warnings and zero
  errors; the platform-neutral native compile gate also remained clean.
- The last unsigned structural candidate is
  `artifacts/chummer-android-0.1.0-preview.3-unsigned.aab`. SHA-256:
  `ddb91078c07e342ff84d667897b2ca1f61e4bb1b2cee0305640c1e6c47370fce`.
  Pinned bundletool 1.18.3 validation passed. Structural inspection confirmed
  package `com.myexternalbrain.chummer`, version code 3, version
  `0.1.0-preview.3`, API 24/36 bounds, the arm64 payload, modern Back support,
  verified Chummer app links, no cleartext traffic, and no broad-storage
  permission.
- A separate x64 Debug build and a linked x64 Release test APK build with zero
  warnings and zero errors. KVM access is now persistent for the host user, the
  Android emulator acceleration check passes, and an accelerated API 36 AOSP
  emulator completed the native Home, New runner, creation-method, metatype,
  Build, Play, Campaign, and More journeys. Dice rolling worked; condition changes
  survived tab navigation; Campaign remained app-native; and repeated clean runs
  produced no crash-buffer errors. One harness-driven ANR occurred during rapid
  stale-coordinate UI automation and did not reproduce through the proper native
  journey, so it is retained as diagnostic evidence rather than classified as an
  app failure.
- Update checks use Play Core's flexible in-app update flow when the install is
  Google Play managed. A sideloaded build now stays inside Chummer and reports
  `Updates come through Google Play`; it does not open a browser, the Play Store,
  or another app. The final x64 device check confirmed Chummer remained the top
  resumed activity and emitted no external activity launch.
- Secure linked-install endpoints now supply online runners plus native campaign
  group list/create/edit/invite operations and the governed Chronicle Studio
  lifecycle. Server build and targeted controller tests pass; group responses
  are private and omit raw user IDs. Consent, spoiler, redaction, source upload,
  generation, outline, artifact import, publication, and external sharing are
  separate, explicit actions; none invokes the provider or sends content.
- The unsigned AAB above predates the latest native UI and update changes and is
  superseded. It must not be uploaded or described as the current candidate.
- A fresh arm64 Release AAB from the current native source is now staged at
  `artifacts/chummer-android-0.1.0-preview.3-upload.aab`. SHA-256:
  `4e73ebb8678b8d11b63e6a5f6a02b2981ab6003403daece60b127c511eaa659c`.
  All 26 Android contracts, pinned bundletool validation, structural inspection,
  and JAR-signature verification pass. The package/version, API 24/36 bounds,
  privacy permissions, app link, modern Back support, and arm64 payload are
  valid. Its signer is the replacement certificate below. The owner approved
  this exact SHA-256 for upload to internal testing on 12 August 2026. The
  approval does not authorize production rollout, a public announcement, a
  tester-roster change, or a differently hashed artifact.
  The candidate also removes the obsolete external Play-listing launcher: Play
  installs update through the in-app update API, while sideloaded installs keep
  the explanation inside Chummer. The replacement signer was restored from the
  hash-verified EA recovery bundle through the fail-closed local importer; no
  private key or password entered repository history or command output.
- The Play screenshot set is current-source native UI, not a mockup or retained
  WebView surface. Five phone captures are 1080×2400 and cover Home, Build,
  New runner, Play, and Campaign. Four tablet captures are 1440×2560 and cover
  Home, Build, New runner, and More/native tools. Both sets were captured on
  accelerated API 36 x64 emulator profiles, visually inspected, and pass the
  store-asset dimension contract.
- The historical preview.2 upload certificate was
  `CB:C5:DF:FF:A0:10:88:A0:55:51:7E:5C:42:0B:EB:25:41:2A:4F:72:53:9B:20:18:D0:4F:F4:EC:DE:A4:03:2F`,
  and is no longer accepted for new uploads. It must not be used for release
  builds. A dedicated replacement upload key was provisioned outside the
  repository with public SHA-256
  `D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15`.
  Its owner-only recovery bundle is backed up in EA; the recovery table and full
  restore drill pass with 616 logical entries and 9 referenced files. The Play
  Console upload-key reset request was submitted with this replacement
  certificate after explicit approval. A read-only Console check on 2026-08-12
  confirmed the reset was accepted and the replacement certificate is active;
  no pending-reset notice remains. The exact approved AAB was then transferred
  to the internal-release form, but Play rejected ingestion because a recently
  reset upload certificate is not valid for bundle uploads until
  `2026-08-14T03:29:49Z`. The rejected bundle was removed and the empty draft
  discarded. Read-back showed version code 1 still active, version code 3 not
  present in the bundle library, and the one-member `Chummer internal` tester
  list unchanged. Retry the same frozen bytes after that timestamp; do not
  rebuild merely to retry. The existing tester invite remains
  `https://play.google.com/apps/internaltest/4700678198570024687`, but it still
  serves preview.1 until Play accepts and activates preview.3.
