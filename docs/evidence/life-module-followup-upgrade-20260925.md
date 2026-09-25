# Life Modules contextual questions — local upgrade smoke

This is a narrow local Debug x64/API-36 observation, not a Release AAB,
hosted seven-journey result, physical-device test or Play publication.

## Exact tested inputs

- Android `089b6eaa5b62b29bc454d9aa2b59d922253488bc`.
- Core runtime `dbc363651731d89c9d81c2f6fb6d0ee89a7b884b`.
- Presentation `3b7a828d93ea76c00c1bc07349537698238c685b`.
- Local keyless Docker image
  `sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.
- APK SHA-256
  `901abf1cbd5436151d4f577c91b55e05a5e12562224dfb430e1b57483fb33c1f`.
- Explicit source roots with unpublished development Core packages
  `0.0.0-zzlocal.followuplabels.20260925.2`; not the tracked sealed package graph.

## Observed route

1. Upgrade the existing diagnostic application without clearing its data.
2. Reopen the existing synthetic pending Rich Kid draft. The two previously
   ambiguous `Any` questions now show `Language · Any` and `Interest · Any`.
   Old answers remain present; workspace and checkpoint bytes are unchanged.
3. Edit only the language answer to Sperethiel; retain Art History as interest.
   Review both answers and source-derived contributions. Merely reviewing does
   not change the saved character revision.
4. Confirm once. The next screen is stage 3 / turn 3, with 55 of 750 Karma used
   and 695 remaining. The character is saved at revision 3/3. Its ledger contains
   exactly the prior nationality acceptance plus one Rich Kid acceptance.
5. Force-stop, verify the old process is gone and launch a different process.
   Continue the story: stage 3, budget and exact saved workspace/checkpoint
   bytes remain unchanged. No decision is replayed or duplicated.
6. Open the retained book. Both saved chapters are readable and the synthetic
   book is honestly labelled a draft with narrative enrichment pending.
   Reading it does not mutate either saved file or request a provider book.

The Rich Kid acceptance digest is
`a1dbc940bfc9ffe15c7c83a7b4f6500801b03e954531dcaf2987a17c7f380ebf`.
Post-confirmation and post-restart workspace SHA-256:
`6cc657e42c4c2673ede2b059e19a6dcbdfb912da0d91b1bd8a390f54a7941c14`.
Matching checkpoint SHA-256:
`609777f0a04119d1d964ba1c1dc3a3ea3bf77198f793aedd12963f5cf5568b24`.

## Checks and limits

Native interaction build and APK build completed with zero warnings/errors.
All 15 Origin-book continuity scenarios passed, including contextual hints and
canonical-label fallback. Five Origin source checks and five localization
checks passed. Core focused checks cover JSON stability, refreshed hints,
tamper rejection and real-source pending-answer/accept/replay; a separate
package-metadata reseal passed 106 local verifier tests.

`DisplayLabel` is transient and ignored by JSON serialization. Canonical labels,
prompt IDs, stored answers and accepted receipt digests remain authoritative;
existing exact state checks are not weakened to admit the new display hints.
Historical book text therefore retains its canonical labels.

The bounded observations do not establish complete Life Modules/Career or
all-method parity, Release performance, provider narration or physical Play
installation. The host was also building packages during part of this smoke;
elapsed UI times are not a phone responsiveness qualification. The earlier
SystemUI cold-boot ANR observation remains retained separately.

The private local packet `life-followup-labels-20260925.O4gVGHYx` retains the
actual build logs, screenshots, hierarchies and before/after synthetic state.
The owned emulator was stopped after the test without wiping its state.
Preview 31 and its release evidence remain unchanged; this change has not yet
been signed or uploaded to Play.
