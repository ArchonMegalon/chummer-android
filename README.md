# Chummer for Android

The Play-distributed, local-first Android host for the complete Chummer
workbench. It uses .NET MAUI Blazor Hybrid to reuse the production Blazor
workbench and the same deterministic local engine as the desktop client.

## Workflow

- **Home** starts a new runner in one tap, resumes local work, or imports a file.
- **Build** opens the complete runner/critter workbench.
- **Campaign** is an explicit command deck for the canonical signed-in campaign,
  GM, organizer, and rules-environment surfaces.
- **Account** links this Android installation through authenticated browser
  approval and a signed device-key handoff; local runner files stay local.
- **More** groups native file/output, sharing, and Google Play update tools.

Live play remains one tap from Home and Campaign and hands sessions to the
canonical Chummer play shell.

## Account linking

Open **Account** and choose **Link account**. Sign in on `chummer.run`, approve
the device, then choose **Return to Chummer**. The app proves possession of its
installation key over HTTPS and stores the resulting 30-day grant in Android
secure storage. It validates the grant on launch, refreshes it near expiry, and
supports server-side revocation from **Unlink this device**. Account linking is
optional and never moves or uploads local runner files by itself.

Desktop window operations become document/task tabs. Desktop file dialogs use
Android's document picker. Printing uses Android Print Framework. Updates are
owned by Google Play.

## Local build

Use a .NET 10 SDK with the `maui-android` workload, Android SDK API 36, and JDK
17. The repository never stores signing material.

```sh
dotnet restore Chummer.Android.slnx
dotnet build Chummer.Android.slnx -c Debug
python3 -m unittest discover -s tests -v
```

The repeatable arm64 release lane requires the pinned official bundletool JAR:

```sh
CHUMMER_BUNDLETOOL_JAR=/secure/tools/bundletool-all-1.18.3.jar \
  scripts/build-release.sh
```

The script runs contract tests, publishes the AAB, validates it with bundletool,
inspects identity/SDK/permissions/privacy/app-links/ABI, and prints its SHA-256.
Release signing is supplied only through secure environment properties documented
in `docs/PLAY_RELEASE.md`; secrets are never command-line arguments or repo files.
Provision a Chummer-specific upload key with `scripts/provision-upload-key.sh`;
the script requires an absolute signing directory outside this repository and
refuses to overwrite an existing identity.
