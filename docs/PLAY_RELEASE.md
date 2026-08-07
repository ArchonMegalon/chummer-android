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

## Local release evidence (2026-08-07)

- `android-arm64` Release publish completed with zero warnings and zero errors.
- The unsigned, upload-key-ready AAB is written to
  `artifacts/chummer-android-0.1.0-preview.1-unsigned.aab` and is intentionally
  ignored by Git. SHA-256:
  `a6b72be80d0003c69e79339627ef2752724eb852f9ad860f7efd8b9fb702b6a5`.
- bundletool 1.18.3 validation passed. Bundle inspection confirmed package
  `com.myexternalbrain.chummer`, version code 1, version
  `0.1.0-preview.1`, min API 24, target/compile API 36, arm64 native output,
  modern Back integration, verified `https://chummer.run/app` links, no
  cleartext traffic, and no broad storage permission.
- All 15 Android parity, privacy, lifecycle, release-automation, listing, and
  store-asset contract tests passed. Four phone and four 9:16 tablet captures
  meet the upload dimension contract.
- The SDK-generated debug-signed AAB is QA-only. Its Android Debug certificate
  must never be supplied to Play or configured as the production app-link
  fingerprint.
- API 36 emulator journeys passed for standalone install, cold launch, Home →
  Build, New runner → build method → metatype priority, Documents UI import of
  `Glessner.chum5`, persisted workspace restart, native Back navigation, and
  phone/tablet rendering.
- Publication is still closed until the Chummer Play app record, Play App
  Signing/upload key, real Play certificate fingerprint, and production
  privacy-retention decision are supplied.
