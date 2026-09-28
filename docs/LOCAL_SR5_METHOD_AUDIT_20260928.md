# SR5 method audit — 28 September 2026

This is a focused local audit of Priority, Sum-to-Ten and Karma, following the
Origin network correction. It is not exhaustive build-method parity, hosted
API-36 qualification, a new signed release or a Play upload. Preview 52 is unchanged.

## Findings fixed

- Karma attribute steppers showed values such as `Body: 0`, although they edit
  purchased levels rather than final ratings. The caption now explicitly names
  purchased levels and retains that distinction after a change.
- Disabled Karma talents did not explain their unavailability. They now show
  the actual source-disabled/unsupported-rule reason. Metatype choices also
  display their existing blockers. No disabled choice is admitted by this change.
- A missing native language now produces an actionable instruction instead of
  the raw `creation-skills-native-language-required` code.
- Copy is supplied in English, German and Spanish. No rule, budget, owner,
  persistence or finalization policy changed.

## Focused verification

Base: Android `2a00d00d46f4cbe0d771cdcfc1d3cdd9327bf443`.
Existing local toolchain:
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Core package: `0.0.0-packageplane.candidate.v20260928.2.shb19fc03123c43`.
The APK uses explicit Presentation/content sources; it is not a package-only
or no-source-checkouts assembly claim.

Before editing, the current tracked app/test inputs were compared with the
retained managed build. Only version metadata differed. Its existing executable
was reused for these freshly executed suites:

- Karma `phone`, `phone-magic`, `phone-revalidation`, `completion-admission`:
  passed; includes actual Core/native pages, persistence, cold store reopen,
  stale view/owner rejection and postcommit failure handling.
- Sum-to-Ten creation/finalization and magic: passed; covers native/Core
  finalization, Adept, Technomancer, Mystic Adept and Skills/Magic revisits with
  persisted receipts and cold reopen/replay.
- Priority finalization/ownership: passed; includes the minimal finalization
  baseline, stale/ABA owner rejection and postcommit faults. The magic slice
  also exercises Priority D Aspected/Sorcery and cold reopen/replay.

These are managed tests, **not Android Activity/process-death evidence** for
every variant. No new rules/persistence defect was reproduced in these suites.

After the caption/hint changes, a new managed build and both Karma `phone` and
`phone-magic` suites passed, including the new caption, disabled-callback and
DE/ES checks. An initial test incorrectly awaited an event from a disabled
button; correcting the test to assert its inert callback resolved that test
failure. Both logs are retained rather than presenting the failed attempt as green.

The affected Android Debug x64 APK built offline in 1m48.20s with zero warnings
and errors. SHA-256:
`77bdf6311947d91ac69a3749cc7fa4cf6385b00937dc365518327f6bbca32ddb`.
It uses an isolated synthetic application ID and the SDK debug key, not the
Play upload key. No proof instrumentation is enabled.

## Actual emulator observations

API 36 x64, 720×1600, 150% font. Before the hint update, the retained APK (same
app/test source as the base, version 51 metadata) completed:

Karma → Human → Mundane → Agility +1 purchased level → empty Qualities → native
English → zero resource conversion → empty Equipment/Contacts → Street lifestyle
selected for starting cash → save pending draft → dice total 4 → explicit
Career review/confirmation → Career → Save → force-stop and cold launch.

The final runner retained Agility 2, 7 Career Karma and 80 nuyen, revision/saved
revision 3/3 and exactly one finalization receipt. Old PID 3722 ended; cold PID
5970 reopened Career. Workspace SHA-256 before and after restart was identical:
`c7186e4a29c20eb7a480d81fbf3c4c302566d37b71d6460cbd3b9c593f526cc7`.
This is a minimal route with unused creation budget, not a recommended build.

The corrected APK then installed in place without clearing application data.
A new synthetic Karma runner retained the chosen build method. Explorer showed
the readable sourcebook-disabled explanation and tapping it remained inert.
The Agility editor showed purchased levels 0 → 1 without clipping at 150% font.
XML snapshots and screenshots are retained in the local audit packet.
The full Career walkthrough above preceded that update; it is not relabelled
as a second full run on the changed APK.

## Remaining limits

- Priority and Sum-to-Ten complete fresh device walkthroughs are separate from
  the passing managed/cold-store tests; do not claim current physical coverage.
- Technical source-anchor blocks make Karma review/confirmation unnecessarily
  long. Loading after returning to an already-scrolled page can be off-screen.
  These are observed follow-up usability issues, not resolved by this patch.
- The emulator initially showed system-service ANR dialogs; no Chummer ANR was
  established from those dialogs. One later hierarchy dump returned no fresh
  root. It was not accepted as a fresh app observation or retried as a mutation.
- Full FirstBook chapters/native reading/EPUB remain a separate unresolved
  provider-generation issue. This audit used no credits or provider writes.

Private logs/build inputs/screenshots are retained under the local
`sr5-method-audit-20260928.CtL6gdP6` packet, outside served directories. Do not
publish that packet or reuse these results for changed inputs.
