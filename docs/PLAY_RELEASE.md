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

Run `scripts/build-release.sh` only from a complete coherent workspace whose
`chummer-android` sibling is accompanied by exact clean Presentation, Core, UI
Kit, Hub, Hub Registry, Media Factory, and Design checkouts. Set
`CHUMMER_COMPLETE_ROOT` to that workspace and supply all eight exact
`CHUMMER_*_REVISION` variables required by `verify_release_source_graph.py`.
The packager requires already-materialized arm64 assets and explicit canonical
`AndroidSdkDirectory` and `JavaSdkDirectory` paths; it never restores or installs
tooling. It also requires the pinned bundletool JAR and all five signing/certificate
environment values. Passwords remain in the environment and are never printed or
interpolated into process arguments.

The release outputs are versioned and immutable. The packager fails before the
build if the AAB, source graph, or checksum target already exists, validates the
source graph again after packaging, publishes only into a fresh unique private
staging directory, requires exactly one new package-ID-bound signed AAB there,
and seals each output with an exclusive no-clobber link. Persistent `bin/`
outputs are never accepted as release input. A partial failed release is not
overwritten automatically.

If SDK API 36 and Java are absent, the guarded
`scripts/bootstrap-build-environment.sh` uses .NET's official
`InstallAndroidDependencies` target. It cannot accept Android licenses unless
the caller supplies the exact documented approval token, requires an absolute
toolchain directory outside this repository, and proves the current Debug
worktree compiles before writing its path-only environment file. Merely running
`scripts/build-release.sh` never installs dependencies or accepts licenses.

## Required gates

The pull-request API 36 beta gate is currently phone-only. Tablet acceptance is
explicitly deferred: that lane does not start a tablet emulator, and a passing
phone receipt is not a tablet-readiness claim or a substitute for the tablet
journeys required before a general Play release.

The current phone-beta runtime authority is narrower than general Chummer5 edit
parity. It is the exact SR5 wizard-only contract in
`eng/api36-sr5-wizard-gate-authority.json`. The protected aggregate check
requires exactly these three digest-bound API-36 journeys against one APK:

1. Creation Prerequisite;
2. Career Active Skill Advance;
3. Career Weapon Fire.

General Full Editing is not required by this gate. Its product code, fixtures,
and standalone tests remain available, but no old Full Editing receipt may
count toward or coexist with the three required aggregate artifacts. This is a
scope decision, not a Full Editing pass claim. The exhaustive editability and
Character Settings inventories remain fail-closed parity backlogs and do not
become the phone-beta denominator.

The wizard aggregate uses schema
`chummer.android.api36-sr5-wizard-e2e-aggregate/v1` and always records
`publicationAuthorized: false`. Even a passing three-journey aggregate proves
only the stated phone wizard scope; it does not itself authorize a Play upload,
tablet support, broad Android parity, or public release.

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

## In-app review contract and testing

The automatic rating flow uses the official Google Play In-App Review library
(`com.google.android.play:review:2.0.2`, through Microsoft's matching .NET
binding). It becomes eligible after 60 cumulative minutes while Chummer is the
resumed foreground activity. Monotonic current-session deltas are checkpointed
locally; background, lock-screen, and process-dead time is not counted. The app
stores only cumulative use, the last attempt time and app version, and a random
no-backup install identity. An upgrade keeps that identity, while restored
backup state from another installation is reset.

Production requests require the release build of the canonical
`com.myexternalbrain.chummer` package installed by `com.android.vending`. They
run at most once per app version and no sooner than 30 days after an attempt by
another version. Eligibility alone never interrupts work: the request still
needs a successful runner/workspace activation, mutation, or durable save followed
by an idle Runners/Home or More root within a short two-minute in-memory window, no
modal/shared dialog, no nested editor, no action in flight, and a clean runner
revision. Creation and Career drafts, review/apply/conflict pages, and every
unsaved mutation remain ineligible. Merely opening or revisiting a root page is
not a success signal, and an old success cannot arm an unrelated later heartbeat.
Google Play quota and API failures are
silent, and completion is not evidence that the card appeared or that a review
was submitted. Support and product-feedback channels are separate and do not
influence eligibility.

The localized **Application settings → Google Play → Rate Chummer on Google
Play** action always remains manual. It opens
`market://details?id=com.myexternalbrain.chummer` with the HTTPS listing as a
fallback; using it neither reads nor changes automatic eligibility.

Real UI proof is internal-track-only. Upload an exact approved AAB to the
internal testing track, add the tester account, select it as the primary Play
Store account, and install Chummer from that track before testing. A sideload,
emulator, fake launcher, or successful API task is not proof that Play displayed
the review UI. In Debug only, launch with the typed Boolean extra below to bypass
the hour/version/install gates for an explicit test; normal safety gates still
apply and a non-Play environment may still return no UI:

```sh
adb shell am start -a android.intent.action.MAIN \
  -c android.intent.category.LAUNCHER -p com.myexternalbrain.chummer \
  --ez com.myexternalbrain.chummer.extra.DEBUG_PLAY_REVIEW true
```

Pure policy tests use an injected fake launcher. Android integration tests may
use the official `FakeReviewManager` (pinned by the binding compile check); it
simulates API completion only and never displays or submits review UI. Neither
path is real UI evidence.

Automatic review can be disabled at build time with
`-p:ChummerPlayReviewEnabled=false` or locally for a process with
`CHUMMER_DISABLE_PLAY_REVIEW=1`. The manual store-listing action remains
available under either kill switch.

## Publication boundary

A locally signed AAB is not publication. Publication requires a Chummer-scoped
Play Console session or service account. Memorial or PropertyQuarry browser
sessions and app identities must never be reused for Chummer.

## Current preview.7 release evidence (source 2026-08-12, Play 2026-08-14)

The current app is version code 7 (`0.1.0-preview.7`). It supersedes preview.6
because the older native deletion explanation presented target retention windows
as an unconditional promise while the live public privacy policy still marks
Hosted Build retention and whole-account erasure proof as review-required.

Preview.7 keeps the native, server-first deletion flow but validates that the
authenticated receipt covers Hosted Build workspaces, support, first-party
auxiliary stores, community data, and identity before it clears the device
grant. It exposes the content-free receipt digest for copying and directs people
to the public deletion page for the current retention posture.

The exact signed preview.7 candidate is
`artifacts/chummer-android-0.1.0-preview.7-upload.aab`. Its SHA-256 is
`34b6b206b422e439e19e675e9f6ec849ed6b3c64b7db66852fdf3463ee4b509f`
and its size is 21,734,060 bytes. Its signer matches the registered replacement
upload certificate:
`D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15`.

The clean input graph is recorded in
`artifacts/chummer-android-0.1.0-preview.7-source-graph.json`, SHA-256
`ab0c22f777523dc119b1b5debfcfbcf964dd0fdf28c97e81db81ca661c0317ad`:

- Android `f1fca38aa837cc307be5b7977c330ba978ae749a`;
- UI `8a383e3a8d81dbfd9cfaa3ab864bd5cc3da50664`;
- Core `108b898d55af52fe5af18f6ce40efa58ed0d659a`;
- UI Kit `d51ecd99cf72098d4adc8db0192bff7bf9fd8e61`;
- Hub `10e1b759896b260c90000381594773b0cf84adfd`;
- Registry `7b54afec574a9327616c4ad7566da3a7b6b906a5`;
- Media Factory `415c8163d3d90b1211e4014fef332bdec6d75f73`;
- Design `159529d8768fa58995db62f080b792bf720759fa`.

All 31 Android contracts pass. The platform-neutral Release gate and API 36 x64
Debug build completed with zero warnings and zero errors. The signed arm64 AAB
passed bundletool, package/version/API/permission/privacy/app-link inspection,
and upload-certificate verification. A clean API 36 emulator install from the
same source graph passed cold launch and native Home, Build, Play, Campaign,
More, Account & privacy, and How deletion works navigation. The deletion page
showed the review-gated Hosted Build limits and public deletion route without
showing unapproved retention windows. Chummer recorded no fatal exception,
ANR, or process exit. The emulator produced one System UI ANR under host load;
that system-process event is excluded from Chummer runtime evidence.

Google Play accepted and processed this exact-hash-approved preview.7 bundle on
2026-08-14. The Internal testing track reports version code 7 as available to
the two approved tester accounts. Saved Play setup, store listing, and app-content
changes remain in Google review; this is not public-store or production approval.
The internal tester invite remains
`https://play.google.com/apps/internaltest/4700678198570024687` and still requires
an authenticated Google account. No physical-device install from that Play flow
has been recorded, so the repository does not claim installed-from-Play proof.
The current console review state also requires re-authentication before it can be
reconfirmed. Preview.3, preview.6, emulator, and sideload receipts are preserved
as historical or local evidence and are not substitutes for the missing
preview.7 real-device receipt.

## Historical preview.6 release evidence (2026-08-12)

Preview.6 is version code 6 (`0.1.0-preview.6`). It contains the native
preview.5 feature set plus repeatable API 36 arm64/x64 build automation and
fail-closed account-link recovery: expired or future-dated link attempts are
discarded, failed browser launches clear pending state, missing grant expiry is
treated as invalid, expired server callbacks cannot be reused, and group invite
links must exactly match the server-issued Chummer code without a query or
fragment.

It superseded preview.5. The exact signed preview.6 candidate is
`artifacts/chummer-android-0.1.0-preview.6-upload.aab`. Its SHA-256 is
`847760c63a4b54a4bf11054de499924dc1a1d8cb10daf6f9adc1ecde83726f5d`
and its size is 21,273,927 bytes. Its signer matches the registered replacement
upload certificate:
`D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15`.

The clean input graph is recorded in
`artifacts/chummer-android-0.1.0-preview.6-source-graph.json`, SHA-256
`ca9182f426583a332b484e19fc7d951d5ddebc92f8ce4228d0bdce80a0e34c52`:

- Android `872100ec26c30c7ab60bbf59131da3f5089e23ae`;
- UI `8a383e3a8d81dbfd9cfaa3ab864bd5cc3da50664`;
- Core `108b898d55af52fe5af18f6ce40efa58ed0d659a`;
- UI Kit `d51ecd99cf72098d4adc8db0192bff7bf9fd8e61`;
- Hub `10e1b759896b260c90000381594773b0cf84adfd`;
- Registry `7b54afec574a9327616c4ad7566da3a7b6b906a5`;
- Media Factory `415c8163d3d90b1211e4014fef332bdec6d75f73`;
- Design `360c4b2716a9dca0f55e8e5c999a48e3dac64f3f`.

All 31 Android contracts pass. The arm64 Release AAB passed bundletool,
package/version/API/permission/privacy/app-link inspection, and upload-certificate
verification. The x64 Debug app built from the same merged graph with zero
warnings and zero errors, then passed a clean-install API 36 device journey
covering native Home, New Runner, Build, Play, Campaign, and More with no Chummer
fatal exception or ANR. That journey caught and closed an Android app-data
ancestor validation crash in Core before this candidate was rebuilt.

This evidence does not authorize upload, and preview.6 is now superseded by
preview.7 source. Play upload remains closed until the
Google-enforced `2026-08-14T03:29:49Z` upload-key cooldown ends and exact-artifact
approval is current. Approval for preview.3, preview.6, or any other artifact is
not transferable. The internal tester list contains two approved accounts; the
tester invite remains
`https://play.google.com/apps/internaltest/4700678198570024687`. Neither the
tester roster nor the invite proves that preview.6 has been uploaded, processed,
installed, or promoted.

## Historical preview.5 release evidence (2026-08-12)

**More → Account & privacy** is now available whether or not the device is
linked. Unlinked users get a native explanation and a direct link-account
action. Linked users can unlink or enter the guarded deletion journey. “How
deletion works” stays inside the app, while its copyable public address remains
`https://chummer.run/account/delete` for people who no longer have the app.

Deletion still requires the linked-device grant, exact typed confirmation, and
a second native confirmation before calling Chummer's whole-account deletion
transaction. Device grants, linked data, the Play cache, and optionally local
runners are cleared only after an authenticated server receipt. An unlinked
device fails closed before confirmation controls are shown.

That source is version code 5 (`0.1.0-preview.5`). The exact signed internal-test
candidate was built from merged source commit
`26fee7af890a91dc67d158be64cc9ecc846c8170` and is sealed at
`artifacts/chummer-android-0.1.0-preview.5-upload.aab`. Its SHA-256 is
`a2cf53038b299ea8ad391201a9a3ded2be70650602d1abede5c1e24e2f83d63e`
and its size is 21,616,129 bytes. The signer matches the registered replacement
upload certificate:
`D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15`.

All 31 Android contracts, the platform-neutral Release compile gate, arm64
Release publish, bundletool validation, structural inspection, and signature
verification pass. A separately signed x64 Release APK from the same source was
installed on an accelerated API 36 emulator. The clean journey covered Home →
New runner, Android picker controls, native Play, native Campaign, More →
Account & privacy, and the native deletion explanation. Chummer remained the
top resumed activity, and no Chummer crash, ANR, or process exit was recorded.

Preview.5 superseded preview.4 and the approved-but-rejected preview.3 bundle,
but is now itself superseded by preview.6 source. It remains immutable evidence
and must not be uploaded as the current app. Production rollout and public
announcement remain outside this historical candidate's authority.

Chronicle Studio also saves a machine-readable operator handoff next to the
reviewed Markdown source packet. The handoff binds the source digest, provider,
current approvals, and exact credit ceiling. It contains no source text or
runner roster, never authorizes unattended automation, publication, or an
external send, and remains Chummer-owned evidence rather than provider truth.

## Historical preview.4 release evidence (2026-08-12)

The signed preview.4 candidate was built from source commit
`288f020693964abb02205fc1985c1eb87d082787` and is sealed at
`artifacts/chummer-android-0.1.0-preview.4-upload.aab`. Its SHA-256 is
`1388b3d16103be8370360f85bf9833b3cdc5fea7af506413a389929ff02bf5c8`.
All 31 Android contracts, bundletool validation, structural inspection, and
signature verification pass. The signer matches the registered replacement
upload certificate. It is retained as immutable historical evidence and is
superseded by preview.5; it does not contain preview.5's always-available native
privacy navigation.

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

## Frozen preview.3 release evidence (2026-08-12)

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
  present in the bundle library, and the then one-member `Chummer internal`
  tester list unchanged. That preview.3 retry instruction is now superseded:
  preview.3 must not be retried or uploaded. The existing tester invite remains
  `https://play.google.com/apps/internaltest/4700678198570024687`; it serves
  preview.1 until a newer exact candidate is accepted and activated.

## Play account readiness (read-only audit, 2026-08-12)

The historical preview.3 retry was independent of the wider store setup, but a
public release is not. The Play dashboard audit at that time reported:

- app state `Draft app` and production `Inactive`;
- temporary tester-facing name
  `com.myexternalbrain.chummer (unreviewed)`;
- 0 of 11 app-information and Store Listing tasks complete;
- closed testing locked until those setup tasks are complete;
- 0 testers opted in to closed testing;
- production access requires a published closed-testing release with at least
  12 opted-in testers continuously participating for at least 14 days.

The 11 setup tasks are privacy policy, sign-in details, ads, content rating,
target audience, Data safety, government-app declaration, financial-features
declaration, health declaration, app category/contact details, and Store
Listing. Repository listing copy, graphics, screenshots, and the Data safety
worksheet are inputs, not evidence that any Console declaration has been
submitted or accepted.

The public `https://chummer.run/privacy` and
`https://chummer.run/account/delete` routes resolve, but both currently disclose
that Hosted Build backup retention, lineage/tombstone retention, deletion replay,
and whole-account erasure remain under review. They are not production-policy
evidence until that owner policy is approved and the live routes are refreshed.

Do not widen the existing exact-artifact approval to complete these declarations,
change tester rosters, begin closed testing, or request production access. Each
requires current production-route evidence and an explicit account-side action.
