# Local full-reader and protagonist-reference checks — 28 September 2026

This is focused local engineering evidence, not hosted qualification, a signed
release, a Play upload or a physical-device installation.

## Native missing-chapter reader

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
progress/ETA availability. The manual chapter-illustration control remains;
automatic illustration generation and insertion are not complete.

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
No provider credits were used. Real multi-age visual consistency, deployment
and the automatic-artwork user route still require completion.
