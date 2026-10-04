# Preview 100 — cancellable account-recovery reads

Authenticated Chummer Play Console readback on 4 October 2026 at approximately
17:31 UTC showed `100 (0.1.0-preview.100)` **Available to internal testers**,
Internal release 94, one version code, released at 19:31 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is scoped browser evidence, not Publisher API or physical-device evidence.
**Physical Play 100 installation/update and live account refresh remain unverified.**
Version 100 is consumed; never rebuild/re-upload it. Preview 99 and its evidence
remain unchanged. No Production, tester-audience, listing or account changes.

## Change and focused verification

Early read-only staged-grant probes and pre-recovery credential-gate waits now
receive caller cancellation. This closes a demonstrated gap in the existing
Cancel action and whole-read deadline. Cancellation before admission preserves
the owner and stored grant. Once a staged credential transaction is admitted,
recovery completes non-cancellably; genuine storage failures remain fail-closed.
Campaign toolbar and page refresh also share the same visible Cancel action.
No rules, dependency, credential format, budget, retry or timeout widening.

The original cancellation regression failed; final actual-MAUI/Core and account
HTTP/security suites passed with zero compile warnings/errors. Tests cover four
probe entry points, credential-queue cancellation, genuine I/O failure, bootstrap
and refresh recovery after admitted-write cancellation, eleven page actions and
existing ownership/ABA/HTTP/erasure guards. The final behavior source is unchanged
by this version-only increment; results are reused, not timestamp-refreshed.

On API36, the private Release x64 diagnostic APK used synthetic endpoints and
an explicitly enabled cancellable delay around the real SecureStorage adapter.
OS Keystore, account service, coordinator and native pages remained real.
At font scale1.3, Cancel stopped the delayed probe, the configured30s budget
returned a readable timeout, navigating to More canceled a separate load, and
force-stop/verified process absence/new-process launch reopened linked and idle.
No catalog request followed canceled probes. Encrypted account preferences were
byte-identical before, after and after cold reopen. Campaign cross-control
cancellation/departure/restart was separately verified on its tablet-shell route;
this is not a full-tablet claim or the cause of the phone-side complaint.

Recovery diagnostic APK SHA-256:
`d83a478b1b664b3b9ca11fa3ba8aa5b7bdf557549136a6b34e58fc6d70c024f4`.
Diagnostic application identities, SDK test signatures and fixture registrations
were not exported into the production AAB. Emulator System UI startup ANR remains
a recorded limitation, not performance clearance. Cooperative-delay tests do not
prove forcibly interrupting a non-cooperative OS storage operation. No runner
mutation/save proof is asserted by this read-only account route.

## Exact local artifact

- Android producer: `7667af6f03f20a4a396a0bb74fe46ca2096488f0`;
  main merge: `ac88fe253620517770976be1591be29b308d7f8e`;
  identical tree: `9968cb4c0dc65e1f43fa2469136234c6b42e8ce7`.
- Behavior PR452/453 and version-only PR454 merged normally. Required source
  and GitGuardian checks passed; version run37220446349/job111489523748.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,685,704 bytes:
  `27b25bfcf3106d71a92e0eaabc5aaeb2d42e2e9ab27567817ebc65537e5308c4`.
- Signed AAB, 33,858,333 bytes:
  `a60036f4687a2f9514d9372af3e983f177988b162592cd5b5997a9a954457fcb`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `e18872dbc4d658ab24beb62c788b85cc0d713bfb68e90869eec88ba2df838586`.
- Independent signed-verification receipt:
  `b61645e45ac9977d918a67c7a9a0581e06e66eda83d78e269ea1943607487543`.
- Private Console execution record:
  `85adf497a9ba3d56fa05119f18f453a20fb922a79d7150b1f545d171c81933eb`.
- Availability screenshot:
  `7a8eaf11e134948bbca83f12c401debf5e6abcd9a3826a686acd6325e80e4e71`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports,18 packages,331 embedded content files,
package/version/API24+/target36/ARM64, privacy flags, proof exclusion and credential
hygiene passed. No compiler warnings/errors were observed. Dependency mode is
locked package closure with pinned Presentation source and Core content, not
package-only or hosted qualification. Separate original-key signing passed
independent keyless strict JAR/certificate/all1,783 payload comparisons.

One upload and one confirmed Internal rollout occurred under standing approval.
Device-catalog coverage is unchanged; missing mapping/native-symbol warnings
remain. Read-only live checks before this release showed account/keyring readiness
passing, but do not diagnose the historical Hub503 or prove the user's account
can now load. No phone, account or provider mutation was attempted. Full SR5/Origin
and general-beta completion remain open. The owned browser was closed and all
owned release containers removed automatically; no user services were stopped.

Private packets `origin-release100-20261004.FvtuzN0M`,
`account-loading-20261004.JTRV0Rf4` and
`account-loading-native-20261004.twDaQhk0` retain exact artifacts and bounded
evidence outside served directories. No credentials or private runner data are public.
