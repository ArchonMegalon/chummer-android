# Quality XML buffer reuse — 7 October 2026

This narrow update consumes Core's private per-load quality XML writer reuse.
Rule results, exact serialization, source digests, ownership and fresh admission
are unchanged. No shared catalog cache is introduced.

## Exact local inputs

- Core runtime: `34baa87a3b0a53d97571ef396f3df15e6e24c494`.
- Core recipe: `8c039ce6e612175a17a5757cb09cf31bfa9dcb47`.
- Core bundle SHA-256: `267fa383639fa5bcb4f3e8304dacefc9ad7c5e66c92a25b973a768336d49cc07`.
- Presentation seal: `e3cc3fea12676f8483dfdd475e8863e19cc9e560`.
- Presentation lock SHA-256: `5f0f18d27b7642778cc6227a915d603989412e3a9551f6f42cb763ef6fac1299`.
- Local UI consumer receipt SHA-256: `5d1618cadf708081827a55dc958c7a009785d6606a48e6db081f612c0578c7cc`.
- Owner-cache manifest SHA-256: `f59a3d918e79de47c7d97eead75f934a17433d8106e0c1794ff55e50556e4989`.

Core PR129 and UI PR352/353 merged normally. The local UI consumer completed
at19:50:50 UTC: five builds,869 product cases and selected focused groups passed.
Android independently verified all18 packages and13 authority artifacts.
All331 rule-data files remain byte-identical; their recipe provenance changes.
APK assembly still uses pinned Presentation source and explicit Core content;
it is not package-only or a hosted qualification.

## Affected checks and limits

Core source regression checks preserve the exact catalog XML and digest.
The512-row managed allocation measurement fell from18,003,200 to10,697,504
bytes (40.6%). This is not a native latency or peak-memory improvement claim.
Local Android native-source, MAUI and interaction builds completed with zero
warnings/errors. Startup tests reopened Priority, Sum-to-Ten, Karma, Life
Modules and an existing Career runner without mutation. A failed presenter
read retained full synchronization and fail-closed recovery.

Diagnostic native testing of this exact Core bundle with the previous UI seal
preserved saved-runner bytes before and after in-place upgrade/process restart.
Restoration took11.492/13.258 seconds versus13.860 in the earlier comparison;
that does not establish a stable speedup. Additional private stage tracing
measured12.614 seconds after verified process restart, including qualities,
magic and finalization. Temporary stage instrumentation is not release source.
Initial emulator SystemUI/keyboard ANRs were retained in the private packet;
the new app process rendered the saved Creation screen without observed ANR.
The saved workspace digest remained unchanged; no Save was replayed.

## Delivery boundary

Final sealed-graph native check on 7 October20:03UTC used SDK-test APK
`f28ac766453613922272d666cf83a1ffd82bf334585d5290d1f278107679c0d7`,
Android `a389d388b45790e43ebc2c47aed77275d5531a96` and the exact seal above.
Build completed with zero warnings/errors. Independent test-certificate and
all331 embedded content checks passed. In-place update and process2875→3932
reopening retained workspace SHA-256
`2ff4347b6641900db048c44c0634fb8d3e02e08ceb2844f031d5835dcdf410ad`.
The saved runner appeared in Runners; Continue building rendered Create with
unchanged Sum-to-Ten budgets/revision8/8. No mutation or Save was replayed.
Screenshots and the final non-null hierarchy are retained in the private packet.

The new process took6.254s for Shell initialization and12.521s for selected
workspace restoration. Boot SystemUI ANR contaminated the initial33.809s
observation; one post-restart null-root hierarchy also remains recorded as a
failed observation, not a pass. No timeout or acceptance rule was weakened.
No app FATAL/ANR was found in the captured new-process log; this is not an
exhaustive crash-free claim. Emulator stopped normally and data were retained.

ARM64 bundle/signature inspection and actual Play readback are separate release
evidence. This document does not assert Play upload or physical installation.
Native responsiveness and complete SR5 Creation remain open; Windows stopped.
