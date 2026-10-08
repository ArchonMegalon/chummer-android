# Preview 148 — Home action for Creation and Career

Authenticated Chummer Play Console readback at `2026-10-08T17:57:08Z` showed
`148 (0.1.0-preview.148)` **Available to internal testers**, one version code,
Internal release 142, released 8 October at 19:56 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API evidence.
**Physical Play148 installation/update remains unverified.** Play says changes
usually appear within one hour and can take longer. Version148 is consumed:
do not rebuild or re-upload it. Preview147 and earlier evidence remain unchanged.

## Change and focused verification

Home now says **Open runner** for a finalized Career character and retains
**Continue building** for a Creation draft, using the existing Created state.
English, German and Spanish are covered. Navigation, rules, owner admission,
mutations and persistence are unchanged.

The managed actual-MAUI test reproduced the old Career label before the fix.
Creation→Career→Creation rendering in EN/de-AT/es-MX, enabled controls and
unchanged persisted bytes passed after the fix. Existing Home startup/held
initialization/heartbeat and blocked-switch/no-write tests also passed. Two
earlier test attempts failed in fixture setup; neither is product regression
evidence. Managed and native x64 APK builds had zero warnings/errors.

The local API36 in-place147→148 SDK-test APK update retained nine synthetic
runners. Settled PNG/XML showed Apprentice category choices with Open runner;
tapping it opened that same Career runner and its story entry. All27 saved
workspace files remained byte-identical after update and navigation. Draft
labels/localizations were managed-control tests, not another native draft journey.
No character mutation or paid provider request was made.

Guest boot encountered SystemUI/Launcher/Google-service ANRs. Severe owned
emulator CPU throttling was observed while host CPUs remained available; raising
only its CPU quota from200% to400% preceded the settled route. This correlation
is not a general startup/performance qualification. No Chummer ANR/crash was
found in retained logcat; memory-limit/OOM counters and swap stayed0. The owned
emulator stopped successfully. Transient pre-settlement screenshots are not
used as visible proof. No app timeout or user service was changed.

Full chapters and matching illustrations remain in the native reader and EPUB.
Unchanged [chapter-image coverage](../../docs/ORIGIN_EPUB_AND_SCENES.md) is reused,
not represented as a new provider or physical rendering test.

## Exact local artifact

- Android producer: `18ea236872f10c3f5fde95d632d798eaa6605696`;
  protected PR579 merge: `4e47e3e141808b6484a5440a3d71c454e7a232b5`;
  identical tree: `91dad3dca101960db62a8776e4f29de6607975f2`.
- Presentation seal: `66b3185dd97a1e9da8002a38d2abbc364fda6dcd`.
- Core runtime: `899be24cff0525eedd90c2299ba19dbef7f63cc1`;
  recipe/content: `76e77b13b9ce375a3183da9abb6cdf58ecec21af`.
- UI verification: `be76cc97cd5c6de6daeeb108267397396d9508d47490b8757d03d89b04a5adef`.
- SDK-test APK: `e1ba5c35e4348dbc51c28aed70445cb27dea497d618ffee44861155047fffd61`.
- Unsigned AAB,34,960,912bytes:
  `ace2fdfa312abf190dff18bb985573383871aed6d86b94106d9ba45edcc8acd3`.
- Signed AAB,35,133,555bytes:
  `55fef107005b2f5b1cba7441ae5f7b3ec6f8ea085bd8c0b001578cc3edcde9c5`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned source/content receipt:
  `efa75933f818ee89d853cc97a50e2d240f8201b0318c219b3be71d16c17a9ad6`.
- Independent keyless verification receipt:
  `aec7835a9ffb17c1a2f46ae662bbc9a58393fb93fc0bb3cafd979d5f2e484c72`.
- Private Console execution record:
  `6ac316debc2f96f2069f180ac5f399dc7dfe8415ca279bbd7236d00fb0f8473b`.
- Visually inspected availability screenshot:
  `95f17111aa354674b14d91a92f45af127bb0f1f2a3604fa590b7e6b873894f6f`.

One offline local ARM64 build used .NET10.0.112/JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports,18packages/13authority files,331 embedded content
files, bundle/version/API/ARM64 inspection, proof exclusion and credential
hygiene passed. Dependency mode remains locked packages with explicitly pinned
Presentation source and Core content, not source-free package-only assembly.
The separate offline signer used the existing key. Independent keyless strict
JAR/certificate verification and all1,783 unchanged payload entries passed.
The self-signed upload-certificate notice is checked against the exact existing
certificate, not an arbitrary signer. Raw packets and signing material stay private.

One upload and one final confirmation used standing owner Internal approval
of2026-10-05, separate from candidate verification. The scoped browser procedure
verified account/app/track and actual availability. Play recognized148, API24+,
target36 and ARM64, with only missing mapping/native-symbol warnings, no errors
and no supported-device delta. No audience, public-track, account, listing or
security change occurred; uncertain operations were not replayed.

Physical installation, complete Creation/Career coverage and general startup
reliability remain open. This is not finished-app, full beta, tablet, Full
Editing, Windows or new live-book-generation authority.
