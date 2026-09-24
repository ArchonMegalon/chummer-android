# Life Modules / Origin book package intake

Local integration only; no signing, upload, physical install or hosted runtime
qualification is asserted by this change.

The Android consumer now uses UI `2a79298d9a14c1ce0f75819d46a6adb29a1eacac`,
Core recipe/content `bd955ad8e5ef4344ec6f87180b872d563f081ac8`, Core runtime
`d9051e0d214a56b515e14b164500f9e70f09edcd`, and Hub
`026943cd5d3413f390452dfbec06852ef10b0d55`. These are exact feature/seal
commits, not claims that each change has merged to main.

The actual UI consumer receipt has SHA-256
`b3e562579dd41e2606fc8e275afd111115a2520c24e731544d1848a44a326c7c`.
Android independently authenticated its 18 packages and 13 authority files,
including honest owner-cache reuse and fresh UI consumer restore. Both Android
NuGet locks were regenerated from those package bytes with SDK 10.0.111.
Core content contains 330 files. Career quality/skill-group runtime bindings
were recomputed; stale source/content generations still fail closed.

Local checks completed:

- Native.CompileCheck (294 owned sources), Android Debug Compile target and
  managed interaction-test build: zero warnings and errors.
- Origin chapter HTTP/admission and book continuity tests, including hostile
  readbacks, owner transitions, retained prose, offline recovery and export.
- Life Modules completion and actual phone-page managed harness: cumulative
  choices, Career, save/reopen, retained book/HTML export and stale-owner rejection.
- Toolbar save/cancellation/owner-transition boundary cases.
- 49 package-intake, 33 build-contract, 4 package-materializer, 14 Career-quality,
  10 skill-group, 23 manual-workflow and 3 content/pin regression tests.

The initial full Android compile stopped because Xamarin's default download
cache was in the read-only Docker image. An explicit writable, owned download
cache fixed the build environment; no source, credential or verification guard
was relaxed. The final builds reused the successfully restored exact packages.

This is a locked package closure with pinned Presentation source, not a
package-only APK. The full Android app still includes explicit Core content.
The tests above are managed/local checks, not emulator taps or live FirstBook
account-generation evidence. Native read/adopt/export and the final distributable
build remain delivery steps. Preview 28 publication evidence remains unchanged.
