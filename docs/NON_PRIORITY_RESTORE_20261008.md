# Bounded non-Priority restore improvement — 8 October 2026

The next Internal candidate consumes Presentation
`8dff197bf87958293f419bf25de8444b7c82848f`. When a pending SR5 runner's document
and profile both identify Karma or Life Modules, overview restoration no longer
prepares the unrelated Priority Qualities and Magic projections. Priority,
Sum-to-Ten, unknown/mismatched methods and other rulesets retain their existing
preparation; finalization is unchanged. Core runtime/content remains the same
as Preview 138. No stored runner data is migrated by this change.

## Observed checks

68 focused Presentation cases passed, including the ten new exact-method
regressions. Production-backed Android startup tests passed for Priority,
Sum-to-Ten, Karma, Life Modules, created runners and fail-closed Shell recovery.
The affected native build completed with zero warnings and errors.

An in-place update on the same retained API 36 x64 emulator restored four saved
synthetic runners. After force-stop and verified new-process launch, the selected
pending Karma runner reopened correctly; all four saved workspace JSON hashes
remained identical. The selected-workspace restoration stage fell from 16,018ms
to 4,203ms on the second launch. Shell initialization still took 22,308ms.
System UI/Pixel Launcher first-boot ANRs were separately observed and retained;
these are not omitted or represented as app successes. The final restored screen
and hierarchy showed no modal.

This is a narrow emulator observation, not a general cold-start benchmark or
physical-phone performance claim. The native smoke used the isolated SDK-test
package/certificate, not a Play-installed build. The later test-count correction
and package seal do not change product source. Reused results apply only to those
unchanged inputs; exact package/source checks remain part of the local release.

## Chapter images

The existing native book reader already inserts each retained illustration before
its matching full chapter. Android PR 558 adds explicit four-chapter coverage of
the deferred image streams, exact EPUB image bytes, offline reader reopen and
retirement of old controls on departure. See
[Origin reading and export](ORIGIN_EPUB_AND_SCENES.md). This test does not assert
a new provider job, multi-chapter likeness quality or physical installation.

## Delivery boundary

This document records a candidate, not an upload. Preview 138 and its signed
artifact/evidence remain immutable. A future Preview 139 needs its own local
ARM64 build, isolated existing-key signature, independent payload verification
and actual Play Internal readback. Complete Creation/Career coverage, physical
Play installation and full cold-start responsiveness remain open. Windows,
Full Editing, tablet parity, Rook and public-track promotion are not claimed.
