# SR6 native foundation increment — 21 September 2026

This is an experimental pending-draft wizard, not complete SR6 creation or a
Play delivery. The native New runner dialog shows Priority, Sum-to-Ten, Point
Buy, Life Path and optional Karma (SR6). All five have Core-owned bootstrap
identities; only Priority and Sum-to-Ten have the foundation editor below.
The other methods must not enter an SR5 editor or this priority service.

## Implemented

- Native foundation route for pending SR6 Priority/Sum-to-Ten runners only.
- Explicit five-category priorities, metatype and talent; no default choice.
- Core load/preview/confirm off the UI synchronization context. Android does
  not calculate rules or directly modify character XML.
- Core-generated budget/source anchors followed by explicit confirmation.
- Exact owner stamp, workspace, revision, displayed state and page lifetime
  checks. Editing a selection invalidates the old confirmation. Departed
  controls cannot dispatch writes; uncertain outcomes are reopened, not retried.
- Saved draft choices return after cold reopen. Historical previews are not
  reused as current confirmation permission.
- DE/EN/ES labels, validation feedback and explicit incomplete-feature scope.

Attribute/skill allocation, talent effects, equipment, finalization and Career
entry are still missing. Point Buy, Life Path and optional Karma still need
their own rule implementations and native flows. This does not enable SR6
Origin generation or audiobook conversion.

## Exact local source assembly

- Android functional commit: `d12ab8a735d1be4a406081a2a1080e6a9698462f`.
- Core integration: `a86ed70bfc659cd69f8432a7bd06422b2726ac5c`, merging the
  SR6 foundation `e8efffedd` with Life Modules `83ea93b5b`.
- Presentation: `1eae38aa1af78c935875f81d6faf7d7a884ba669`.
- Reused local Docker toolchain:
  `sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.

These are explicit source roots, NOT a new package seal or a package-only
consumer claim. No main merge or protected-check result is implied.

## Focused verification

- `sr6-native-foundation-3.log`: 14 passing managed native scenarios using the
  real Core service/file store: eight ownership/recovery cases and both methods
  in EN/DE/ES. Covers owner A→B→A, stale/departed controls, post-commit
  cancellation, lost return, forged preview and one-write-only confirmation.
- `sr6-origin-regression-1.log`: 12 existing Origin book scenarios pass against
  the merged build. This is not live First Book AI or audiobook verification.
- Native compile graph: 279 owned sources, no issues.
- `sr6-debug-build-1.log`: local Debug x64 APK, zero warnings/errors.
- Separate debug package: `com.myexternalbrain.chummer.sr6foundationdebug`.
- APK SHA-256:
  `7942978f2dc6e1d31b2bc842494066ac4e9a7db0ae4c5c2ecfef39136a967493`.

The first managed run failed because the separate SR6 tree lacked newer Life
Modules contracts; the integration merge resolved that mismatch. The second
run reached a test-harness issue: a deliberately stale disabled button could
not dispatch a simulated event. The corrected test explicitly enables that
detached button and verifies that the production callback still refuses it.
Neither earlier run is counted as passing.

## Actual API 36 emulator routes

Both used New runner → SR6 → method → foundation → explicit selections →
review → confirm once → force-stop → new process → reopen same foundation.

| Method | Heritage / talent / attributes / skills / resources | Preview | Restart |
| --- | --- | --- | --- |
| Priority | D / E / A / B / C, Human, Mundane | 24 attributes, 24 skills, 150,000 ¥, 4 adjustment | PID 4635 → 5590 |
| Sum-to-Ten | D / E / B / B / B, Human, Mundane | 16 attributes, 24 skills, 275,000 ¥, 4 adjustment | PID 5590 → 6385 |

Each draft retained revision 2 / saved revision 2 and exactly one foundation
decision. Before/after workspace JSON was byte-identical:

- Priority: `6358b82f1ce88c301b816fbc0ead7a1021844564f2d33140e11552fdc3484561`.
- Sum-to-Ten: `f41defad3ff6c88f500e00c68562c5e87099073ca66ee931aad414f74e4e68d2`.

The reopened native controls displayed the saved priorities and metatype.
Screenshots/hierarchies, synthetic workspace copies and logs are retained in
`/docker/chummercomplete-active-worktrees/life-module-book-tests-20260921.L0D7pON5`
with `sr6-` filenames. The owned read-only emulator was stopped afterward.

A System UI ANR appeared at initial emulator startup and cleared with Wait;
it was not an app ANR. Some immediate transition hierarchy reads returned null;
those reads were rejected, not treated as current-screen evidence or grounds
to replay a mutation. The completed routes above used fresh visible state.

## Delivery boundary

Debug key only; no upload key, AAB, Play upload, physical installation, new
package authority or hosted qualification. The installed user/Play package was
not touched. Next work is SR6 allocation and finalization, then method-specific
Point Buy/Life Path/Karma flows. SR5 Life Modules and its book remain an open
priority before Windows; this increment does not declare them finished.
