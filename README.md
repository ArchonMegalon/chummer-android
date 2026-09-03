# Chummer for Android

The Play-distributed, local-first Chummer client. Its UI is built from native
.NET MAUI controls and Android Shell navigation. It does not embed the Blazor
PWA, a `BlazorWebView`, or a campaign/play `WebView`.

The app still uses the same deterministic engine and presentation contracts as
the desktop client. Shared presenters supply runner state, commands, tabs,
workspace actions, dialogs, imports, exports, and print payloads. Android turns
those contracts into native pages, pickers, entries, switches, editors, Android
document intents, Android Print Framework jobs, and Google Play update flows.

## Workflow

- **Home** opens local files, starts a runner, resumes recent runners, and opens
  linked online runners.
- **Build** starts with the loaded runner and a short native list of build areas.
  Each area opens a focused action page, then drills into small value groups.
  The searchable, grouped action catalog still contains every desktop command.
- **Play** is a native table view for the loaded runner and selected group, with
  dice, condition tracking, and session notes.
- **Campaign** is a native group view. A GM can create or edit a group, inspect
  its roster, copy or share a browser-safe invite link, and manage Chronicle
  Studio drafts and approvals without leaving the app.
- **More** contains account linking, import/save/export/print, all actions, app
  version, Google Play updates, and a focused Account & privacy page.

Complex desktop workflows stay complete without becoming walls of text. Build
uses Android-style list/detail navigation instead of horizontal web-like tabs;
the command catalog first shows groups and only expands the chosen group. A
command opens its shared workflow as a native modal. Choice and radio-style
fields render as Android pickers; booleans use switches; long text uses editors.
The same coordinator and page-level contracts compile against plain `net10.0`,
which keeps the navigation/state layer reusable for a later iPhone shell while
letting each platform keep its own visual conventions.

## Account linking

Open **More** and choose **Link account**. Sign in on `chummer.run`, approve
the device, then choose **Return to Chummer**. The app proves possession of its
installation key over HTTPS and stores the resulting 30-day grant in Android
secure storage. It validates the grant on launch, refreshes it near expiry, and
supports server-side revocation from **Unlink this device**. Account linking is
optional and never moves or uploads local runner files by itself.

After linking, **Home** can load the account's online runners directly into the
native Build and Play pages. **Campaign** uses the same grant to list groups and
perform GM-authorized group mutations. Raw access tokens stay in Android secure
storage, responses are `no-store`, and the group API does not return member user
IDs. Invite links remain ordinary `https://chummer.run/groups/join/...` URLs so
players can open them in any browser.

Account deletion is native. Open **More → Account & privacy → Delete account**,
choose whether runners stored on the device should also be removed, enter the
exact confirmation phrase, and confirm once more. Chummer deletes server data
first; only a receipt covering every required first-party data plane clears the
device grant and local account cache. The app shows a copyable content-free
receipt digest, while backup retention and whole-account policy claims remain
review-gated until the public policy says they are production-proven. The
public explanation remains available at
`https://chummer.run/account/delete`.

Chronicle Studio is native too. It creates versioned drafts, saves a reviewed
source packet, records the external AIWriteBook project and finished export,
and keeps source, upload, generation, outline, publication, and external-sharing
approvals separate. Consent, spoiler review, redaction review, and source rights
are independent checks. A machine-readable operator handoff carries only the
source digest, approvals, and exact credit ceiling; source text and runner names
remain outside it. The app never spends provider credits, uploads source
material, publishes, or sends anything on a button's behalf.

Desktop window operations become document/task tabs. Desktop file dialogs use
Android's document picker. Printing uses Android Print Framework. Play-installed
copies check for flexible in-app updates on launch and resume; sideloaded copies
stay inside Chummer and explain that updates arrive through Google Play.

## Local build

Use a .NET 10 SDK with the `maui-android` workload, Android SDK API 36, and the
Java SDK selected by that workload. The repository never stores signing
material.

```sh
scripts/build-debug.sh
python3 -m unittest discover -s tests -v
```

The debug build defaults to an arm64 device package. For an x64 emulator, run
`CHUMMER_ANDROID_RUNTIME_ID=android-x64 scripts/build-debug.sh`. The wrapper
keeps the selected Android runtime aligned across the MAUI app and the shared
`net10.0` engine graph; a bare multi-runtime solution build cannot preserve that
restore boundary.

The `sealed_multi_repo_source_assembly` boundary applies to the full
`Chummer.Android` project whenever `Configuration=Release` or
`ChummerUseLocalCompatibilityTree=false`. Those governed invocations must pass
both `ChummerPresentationRoot` and `ChummerCoreEngineRoot` as explicit absolute
paths. The former supplies the two Presentation project references; the latter
supplies the `Chummer/data` and `Chummer/lang` content assembled into the app.
The project fails before `PrepareForBuild` rather than discovering either root
through its legacy relative sibling defaults. Those defaults remain available
to ordinary local compatibility builds outside this governed boundary.

This boundary does not reclassify the internal phone-beta Native.CompileCheck:
that dependency-only proof uses pinned Presentation source plus locked Core
packages, does not assemble Core source or content, and still authorizes neither
publication nor any additional API 36 journey.

When Android SDK 36 is not available, the platform-neutral native compile gate
still checks all Shell/pages and shared-presenter calls without an Android SDK.
Its checked-in input manifest explicitly owns every source outside the native
page directory, including platform-neutral service stubs, and rejects generated
assets that point at deleted or out-of-workspace worktrees:

```sh
dotnet build tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj
```

For an already provisioned SDK 36 host, the read-only Release compile wrapper
performs the pinned .NET/Android/JDK preflight, validates the no-restore asset
graph, and runs only the native `Compile` target:

```sh
CHUMMER_PRESENTATION_ROOT=/absolute/chummer-presentation \
CHUMMER_CORE_ENGINE_ROOT=/absolute/chummer-core-engine \
  scripts/compile-native-release-no-package.sh
```

The preflight exits `78` when the SDK/workload/JDK is unavailable and `64` for
an invalid repository pin. A later nonzero exit is a C# compile failure after
the toolchain passed. The wrapper does not restore, download, package, publish,
or start an emulator.

On a clean Linux host, `scripts/bootstrap-build-environment.sh` can use the
official .NET `InstallAndroidDependencies` target to install the exact Android
SDK and Java dependencies required by the project into an explicit directory
outside the repository, then compile the current arm64 Debug worktree and the
platform-neutral native gate. It fails before
any download or license acceptance unless both the exact approval token and an
absolute destination are supplied:

```sh
CHUMMER_ANDROID_TOOLCHAIN_APPROVAL=install-android-sdk36-jdk-and-accept-licenses \
CHUMMER_ANDROID_TOOLCHAIN_DIR=/absolute/operator-approved/chummer-android-toolchain \
  scripts/bootstrap-build-environment.sh
```

Do not set that token by inference. It represents explicit acceptance of the
Android development licenses. The resulting mode-`0600` environment file can be
sourced for later builds; it contains paths only, not credentials.

The repeatable arm64 release lane requires the pinned official bundletool JAR:

```sh
CHUMMER_BUNDLETOOL_JAR=/secure/tools/bundletool-all-1.18.3.jar \
CHUMMER_ANDROID_EXPECTED_VERSION_NAME=0.1.0-preview.11 \
CHUMMER_ANDROID_EXPECTED_VERSION_CODE=11 \
  scripts/build-release.sh
```

The release version pair is explicit and fail-closed: both values must exactly
match the project and the version code must be greater than the immutable
Preview.10 code `10`. The build creates only local versioned artifacts and a
version-bound source graph; it never uploads or changes Google Play.

The script runs contract tests, publishes the AAB, validates it with bundletool,
inspects identity/SDK/permissions/privacy/app-links/ABI, and prints its SHA-256.
Release signing is supplied only through secure environment properties documented
in `docs/PLAY_RELEASE.md`; secrets are never command-line arguments or repo files.
Provision a Chummer-specific upload key with `scripts/provision-upload-key.sh`;
the script requires an absolute signing directory outside this repository and
refuses to overwrite an existing identity.
