# Preview 38 — observed Play Internal availability

At `2026-09-25T18:22:51Z`, the owner-authenticated Chummer Play Console showed
`38 (0.1.0-preview.38)` as **Available to internal testers**, Internal release34,
track Active. Displayed release time: `25 Sept 20:22` (Europe/Vienna).
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is rendered Console readback, not Publisher API evidence. Physical Play
installation/update remains unverified and explicitly deferred by the owner.

## Delivered correction and focused checks

Opening a saved Origin book immediately after cold start could falsely report
“The book context changed.” Native diagnostics established a short local
credential-hydration exclusion while the page and display were still current.
The actual account/Core/native-coordinator regression reproduced that contention
before the fix. Temporary diagnostic instrumentation was removed from the fix.

Only deliberately scheduled synchronous background book reads may now wait for
the credential writer. Normal UI/mutation admission stays non-blocking; the
exact owner identity is rechecked after waiting. Cancellation, owner ABA and
non-reentrant thread-bound lease protections remain enforced. No retry, timeout
increase, provider generation, consent, reader acceptance or mutation replay
was introduced.

The red regression became green. Focused tests cover real writer contention,
cancellation while excluded, queued owner ABA rejection, nested admission,
unchanged runner revisions and no remote call; actual account startup, expiry,
unlink/relink, persistence and initial-route boundaries; and existing signed
HTTP/Life Modules/Career/book/export/Save routes. Managed build passed with zero
warnings/errors. Passing log SHA-256:
`0288735ad4d2383ee3d1d1689ac772254e98fbfaedb13b8fdffd41bbaf92cbfe`.
Actual owner-boundary log SHA-256:
`058906baa210d9cf3682a9105d9448a6bcc034c53e13f360719543be3254d32a`.

The API36 x64 Debug build passed in 1:54.23, zero warnings/errors. APK SHA-256:
`44d65bdefe266a30b43dc2754ad784379afcef105fdf50b4f87c5880c0998d4b`.
All330 canonical content files matched. A data-preserving emulator upgrade
opened the saved Career/book, saved the runner, verified force-stop/new process,
then reopened the book and scrolled the retained prose without the false dialog.
The reading edition and completion inputs remained byte-identical:
`dd5221806af7563b0fe7c615eeb5031897b655726d28ce1180fb5d95256da84d` and
`de7a6801edc473fe1206fea260f93f6404c8cf20cdd1b001c4f6447364a42267`.
Bounded new-process error inspection found no app fatal exception or ANR marker.
An emulator System UI boot ANR and null-root hierarchy observations are retained
separately, not relabelled as successful observations. This is an affected Debug
route smoke, not a physical test of the signed ARM64 Release AAB.

## Exact artifact and isolated local signing

- Android producer `e3eb72cd04e949cd0e7bee58dc44b947788a133f`, tree
  `fc44c8a20edb6eefc59b149d2ede7f0fee57905a`.
- PR125 merged normally at `2026-09-25T18:17:27Z` as
  `08dc10e2fd6e3f34f9692a9308a3cbc415508f62`, with the identical tree.
  Required PR source/safety and secret checks and main source/safety passed.
  These checks are not managed builds or device tests.
- Unchanged Presentation seal `fc614e38aa1ec7dfab5be7c8bac3eae542f7cfd4`, tree
  `253ba382f3dbe84b073223b06203d05c15f85c4a`.
- Unchanged Core runtime `3004181b467a0f77653ce97aef8cd16d4bc4a0ff` and
  recipe/content `b194b5eabb4691c7307a7c794177abdb8b282614`.
- Unchanged Hub producer `42d0bfbb117ab6250e8b0512dd92585916c6469f`.
- Exact reused UI consumer receipt SHA-256
  `86c4e47114c0e2cb7b98081b3f728f6b09ba0fe8b61ae2364ce7f18dc25cca44`.
  All18 admitted packages were rechecked; no new upstream rebuild is claimed.
- Source-input record SHA-256
  `48402b56745de46ca046810484b3c1d8053db54891bbcd26a44aba16363a5885`.
- Unsigned AAB:32,023,901bytes, SHA-256
  `abb0e1938e1d922cc88c23889649aa17a1e1bb0f8e14c1795077b1705523cd3b`.
- Signed AAB:32,196,375bytes, SHA-256
  `c84edb519862bcf056f2e75cc957808e3f4f0be853dfb85f3558e123eae61973`.
- Existing upload certificate SHA-256
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
  This is not the unobserved Play App Signing certificate.

The offline keyless Docker ARM64 Release build passed in 2:24.51, zero
warnings/errors, with SDK10.0.111 and existing toolchain image
`sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.
It uses locked packages, explicitly pinned Presentation source and Core content,
not package-only APK assembly or ambient siblings. Unsigned inspection passed
package/version/API24+/target36/ARM64, privacy, permissions, key hygiene,
proof exclusion across165 managed assemblies and330 exact content files.
Separate existing-key signing and independent keyless verification passed
strict JAR signature, certificate and unchanged non-signature payload.
Signed-verification receipt SHA-256:
`b23f6f689372d5ae7791f27348af6c436aa4f9c4201fb16f9e5abc6fa108e1a9`.

Only those signed bytes were uploaded, once. Play accepted en-GB/de-DE/es-ES
notes, reported the existing missing mapping/native-symbol warnings and no
lost supported devices. No Production, tester, billing, security or unrelated
app settings changed. Version38 is consumed; future uploads require39 or a
higher unused code. Preview37 and older artifacts/evidence remain immutable.
Owned temporary build/sign/verification containers and emulator are stopped.

## Limits and custody

The opening prose remains operator-edited; later chapters remain provider
drafts. Full unattended generation and editorial prompt/length compliance are
unproven. The earlier live Rich Kid status-read cancellation remains a separate
issue; this correction establishes local cold-book admission, not live provider
outage recovery. No physical Play installation, production App Links, general
beta, exhaustive creation/Career, SR6, audiobook, tablet, Full Editing, Windows
or Rook completion is claimed. No provider generation budget was used.

Private packet `life-release38-20260925.opEmBH14` retains exact build/sign/
verification inputs and the Console screenshot. Native evidence is in
`origin-cold-book-20260925.BBXGqa/COLD_BOOK_NATIVE_SMOKE.md`.
Actual `PLAY_PUBLICATION.json` SHA-256:
`f9152be1992f0c81502279a42f00f0ef79afa3b79a60988b78dbe203b679b01d`.
