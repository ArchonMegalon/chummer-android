# Creation Skills — complete specialization picker

7 October 2026. Product source `667a857f47a0a291c94c7f9b0d5fb4e1d02098da`,
tree `3a26ad2af65edb62ea0ac6cf7678fe78ad7af6ce`. The subsequent version-only
125-to-126 change and this record do not change the tested behavior. Exact ARM64
release inspection must cover that metadata delta separately.

## Change and focused checks

Creation Skills previously exposed only the first six specializations. A native
picker now retains every admitted typed option, uses readable theme colors, and
requires explicit preview. Opening/selecting does not persist or spend points.
An unchanged choice cannot toggle itself off; removal is explicit. Retained
controls are rejected after render, appearance or owner changes. Core still
computes costs and admits the exact preview before confirmation.

The existing managed interaction executable's focused
`--creation-specialization-picker-content-root` entry passed against real Core:
Archery has 30 options; its last option can preview, save and reopen. No implicit
mutation, explicit removal and stale render/appearance/owner rejection passed.
Compilation had zero warnings/errors. The first run failed in the test helper
because a disabled button correctly invoked no handler; the helper was corrected
without weakening the product. Seven Skills source tests and five current-phone
scope tests passed. An additional Priority source suite had five passes and one
failure in unchanged, obsolete inline final-review markup expectations. That
suite is not reported green; no unrelated test was removed or relaxed.

## Actual API 36 native smoke

Local Release x64 SDK-test APK SHA256:
`329303bacd6099c9aa27992c6eb44f1553a92b5f3723ed65635957eed2a418e6`.
Build: 2m17s, zero warnings/errors. Installed bytes matched; all 331 embedded
content files matched the exact Core authority. The first content invocation
rejected a non-Git export; the retained second receipt verified the same APK
against the actual clean Core checkout. No candidate rebuild was needed.

The retained synthetic Sum-to-Ten runner was upgraded without clearing data.
Its existing Aeronautics Mechanic/Fixed Wing and Arabic native language stayed
intact. Actual native actions added Archery rating 1, opened the complete picker,
selected Bow (Rating 7), explicitly previewed it, reviewed and confirmed once.
Bow (Rating 7) is beyond the old six-option cutoff. Selection and preview left
the saved bytes unchanged until confirmation. Confirmation advanced content and
saved revision from 5 to 6, Skills draft from 1 to 2 and receipt count from 1 to 2.
Active points used became 4 of 46, leaving 42.

After force-stop, the old process was verified absent before launching a new
process. Reopened native Skills displayed revision 6, 42 active points left,
Archery rating 1 and Bow (Rating 7) in the picker. Unchanged preview was disabled.
Saved workspace SHA256 remained byte-identical after restart and final readback:
`8f5ffe6b6f96c7de65b8390c3aef0d551dbd8c6d541535f3239b90af155735c5`.
Visually inspected reopened-picker screenshot SHA256:
`1e9756c644363ab9331b73d5794172d404351dc7c81b6cb547e2facb2f194764`.

Initial SystemUI ANR and null/stale accessibility observations are retained, not
turned into passes or used to replay mutations. Later screenshots/hierarchies
and independent saved-state readback established the result. The owned emulator
was stopped, its scope became inactive and the private ADB device list empty;
saved AVD data remains intact.

## Scope and retained inputs

Presentation `3f78ff99b6c4988990769627129493195d197dc7`, Core recipe
`1035450779dcfcbb9117418fc3a98bce87c6d7a8`, runtime
`6c3b541fb5c8235109ce9e673dba0c271c49a74d`; no package/pin change.
Private managed/build/state evidence: `origin-local-read-perf-20261006.KtmdCrOJ`.
Native observation packet: `origin-illustration-smoke-20261005.9riU4hxr`,
`skills-picker-*` records. Device/workspace identifiers are not published here.

This proves the affected picker route, not exhaustive Creation, native startup
performance, all methods, physical Play installation, signing or publication.
The SDK-test app/certificate is separate from the ARM64 Play artifact. No provider
book/image credits or user-phone actions were needed. Preview125 and its evidence
remain unchanged. Technical review-page digests remain a separate UI cleanup item.
