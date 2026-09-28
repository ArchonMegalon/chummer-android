# Local full-reader and protagonist-reference checks — 28 September 2026

This is focused local engineering evidence, not hosted qualification, a signed
release, a Play upload or a physical-device installation.

## Earlier native missing-chapter reader

The isolated x64 Debug APK was built from Android
`3ddf5853b9ea605d86a4ca2c86001f42e8c6cf18`, using the current sealed package
inputs, SDK 10.0.112 and the existing local Docker builder. Build completed in
1m01.50s with zero warnings/errors. APK SHA-256:

`b3583cd0ae8f79ae7e6789f46dcb1f1817952313dbd451c4ac0a27b61bb25be3`

The separate package `com.myexternalbrain.chummer.copycostvalidation` received
only three retained synthetic workspace/input/story files, no linked account or
credentials. API-36 emulator 5586 opened Read from the restored Creation runner.
The reader displayed the missing-full-chapter state, its milestone progress bar
and explicit unavailable ETA; EPUB/HTML export was disabled. It did not display
the canonical decision summary as full prose. Screenshot and complete hierarchy
were retained before and after force-stop/relaunch.

Process 3803 was force-stopped and confirmed absent; the cold launch used process
4048. Read reopened with the same pending state. The exact workspace remained
at revision 7 and SHA-256:

`28b2e8e4b2a301257e903788c0cdfb59c197931f2e8b0428fd4093176c2a2d88`

Full selected/returned chapter reading, acknowledgement, export capacity,
observer cancellation and stale-owner handling have separate focused managed
coverage. This offline smoke does not prove live FirstBook generation or live
progress/ETA availability. In that earlier build the manual chapter-illustration
control remained; automatic insertion was not yet implemented. See the current
local implementation and its separately bounded checks below.

## Growing-protagonist transport

The subsequent transport change accepts complete owner/book-bound protagonist,
reference-scene and reference-image hashes from the compatible Media manifest.
Legacy retained scenes remain readable. The original reference must match its
own image hash; later references remain subject to Hub/Media's exact predecessor
and retained-image checks, not an Android assertion of visual similarity.

The focused `--origin-scene-http` suite built with zero warnings/errors and
passed original/continuation image readback, 29 hostile payload cases, bounded
chunked responses, off-UI I/O, uncertain-write no-replay and owner A→B→A checks.
This transport delta is NOT in the APK named above.

Local packet: `life-canonical-equality-20260928.pM6Symni`, under
`/docker/chummercomplete-active-worktrees/`. Retained results are
`reader-check-3`, `reader-check-6`, `reader-check-7` and `reader-device`.
No provider credits were used. Real multi-age visual consistency and deployment
remain unverified; the subsequent automatic-artwork implementation is below.

## Current automatic private artwork

Local source commits:

- Android `2bd062e87158220acfd27f3723ea3bcc4e6fc145`;
- Hub `29da3f48309a6ff2a0344f4ed2b6cae9063465e1`;
- Media `7ec676f18127dc9f5a0c8437836f46dff92588aa`.

Hub composes a private scene from exact accepted chapter prose under the new
illustrated-book consent. Media retains one original protagonist PNG and uses
that exact reference for later ages, persisting validated automatic-policy
images without claiming human review. Android adopts only authenticated
persisted images into the reader and EPUB; no image picker or per-image approval
is offered. Legacy FirstBook-only consent is not widened. Image I/O does not
hold the reading/export action gate. Missing or uncertain references stop the
sequence rather than paying to invent a replacement protagonist.

Focused local checks passed:

- `reader-check-13`: zero-warning/error managed build; reference and automatic
  transport, 32 hostile payloads, owner A→B→A, bounded streams, automatic image
  insertion, exact image EPUB export, usable export during pending image work,
  cold reader reopen without duplicate writes, and affected Life Modules/save/
  Career routes. Legacy manual-route checks remain present.
- `reader-check-11`: full Origin and 128-chapter Unicode/EPUB capacity checks;
  subsequent changes were confined to the image observer and test fixtures.
- `hub-check-10/results/continuity.trx`: 40 tests executed and passed. This is
  compilation against retained dependencies, with known MSB3106 path warnings,
  not a fresh package-plane seal.
- Media: 75 store, renderer, worker and asset-download checks passed.

The actual x64 Debug APK `ReaderValidation-reader-check-13.apk` built locally
with SDK 10.0.112 in 1m00.06s, zero warnings/errors. Tracked Android source bytes
were compared equal to the Android commit above. SHA-256:

`cf92bfe4cd75c3b65f01f74598c3c1d7922d577aaf22b00abfc115868a23767a`

It updated only the separate diagnostic package without clearing its retained
synthetic runner or adding account credentials. The API-36 native reader showed
the missing-full-chapter milestone bar and honest unavailable ETA, no draft or
summary fallback, disabled empty exports and no manual image picker. Process
3641 was force-stopped and verified absent; cold process 4023 reopened the same
reader state. Workspace revision 7 and the exact workspace hash above remained
unchanged. The current APK's screenshots and hierarchies are the
`reader-device/automatic-art-*` files in the same local packet.

The first observation contained a **System UI** not-responding dialog, not a
Chummer ANR. It was preserved and its observed Wait action selected once. Later
reader/restart observations passed. One ambiguous helper selector refused to
tap; navigation then used the uniquely observed tab bounds. Neither observation
was relabelled as product success. The owned emulator and isolated ADB server
were stopped normally; AVD state and recovery artifacts remain retained.

This native smoke is offline: managed fixtures establish automatic insertion
and reference identity, but not provider-rendered visual similarity. A real
original/later-age pair, live automatic workflow, deployment and release remain
open. No provider call, quota reset, upload-key signing, AAB or Play upload was
performed. Preview 49 and the uncertain FirstBook job 5435 fence are unchanged.

## Current-package native completion and cold Career reopen

The **same** `cf92bfe4…` APK above was reused on 28 September; no rebuild,
installation, account credentials or data reset was needed. Actual installed
APK bytes were checked equal before this route. In the separate synthetic
package, the restored revision-7 runner opened Complete Life Modules → Review
draft. Its retained dice total was 6, giving 120 ¥ for the Street lifestyle.

The confirmation checkbox was observed unchecked and the action disabled.
After explicit acknowledgement, one native button tap started finalization.
While it was saving, repeat confirmation was disabled and the first stored
workspace observation remained revision 7 with its original hash. The next
stored observation was revision 8/8, `created=True`, 120 ¥ and exactly one
finalization receipt. The UI subsequently confirmed saved/reopened Career;
Open Career runner then displayed `CAREER RUNNER`.

Process 2114 was force-stopped and verified absent. A successful cold launch
created process 4147; after the loading view, `CAREER RUNNER` appeared again.
Exact saved bytes matched after confirmation, after restart and after the cold
UI had loaded, SHA-256:
`9392ac58a5d5aeb8d3eb227afa6c3597d125022064fd0b3888621255958d60ab`.

The bounded local verifier passed all these observations. `updated-device/` in
the retained packet contains the complete screenshots/hierarchies, input/tap
journal, exact workspace observations and restart record. Early blank/loading
views and the saving view after the durable commit are preserved, not treated
as completed UI states. A duplicate-label helper selector refused without a tap.
No Android speedup is claimed from this non-benchmark route. The owned emulator
and isolated ADB were stopped normally; the AVD and recovery artifacts remain.

This closes the native completion/restart check for the current package inputs,
not real multi-age image continuity, full live-book delivery, physical ARM64 or
Play publication. No AAB or upload-key signing occurred.
