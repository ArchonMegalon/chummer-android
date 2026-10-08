# Readable Mystic Adept guidance — 8 October 2026

Unsupported magic catalog options now use the existing translated explanation
instead of `creation-magic-resonance-option-semantics-unsupported`. Adept Spell
remains unavailable; the change does not add its rules implementation. Unknown
blockers remain visible. Career review labels the typed `magsplitadept` scalar
as Mystic Adept power points. Core identities, pricing, ownership, admission and
saved data are unchanged.

## Focused local checks

- The production-backed `phone-mystic` regression reproduced the old display
  failure and passed on the fix. It selects Human/Mystic Adept, English native
  language, Hermetic tradition, one purchased power point, Adept Accident and
  a supported spell. It verifies disabled-action rejection, exact costs,
  readable Career review, one finalization and managed cold presenter reopen.
- English, German and Spanish explanations are checked alongside preservation
  of unknown blocker reasons. Managed/native x64 builds completed with zero
  warnings/errors; ten source-scope tests, resource validation and key hygiene
  passed. The frozen ARM64 release export reran `phone-mystic` successfully.
- Actual API36 x64 Preview144 smoke created a separate synthetic pending Karma
  runner, selected Human/Mystic Adept and English native language, inspected
  Attributes and empty Qualities, and bought one power point in the draft.
  The UI showed 40/800 Karma, power-point cost5, used0/1 and disabled further
  purchase at MAG1. The settled Adept catalog screenshot was visually inspected:
  Adept Spell has the readable unsupported-rules explanation and no Add action.
- All six pre-existing runner files remained byte-identical. No draft Save or
  Career finalization was performed in this144 native copy smoke. The new
  pending workspace was retained; no character-file injection was used. Career
  review/finalization assertions above are managed tests, not native144 claims.
  The owned emulator was stopped with an inactive/dead/success result.

Test APK SHA-256:
`523690d795f4ec7bbb0c6d10db8d3a4e98def907e7e91cf6fb3a250b18e72229`.
It uses a separate application ID and SDK test key, not the Play identity.
Native copy-smoke result SHA-256:
`21e485252dc35d65edc8fb5946770f3205db79c35bca0eda5d746e6038d2445e`.
Visually inspected settled screenshot SHA-256:
`53fb5e6e5646c9c69f3d60809d64d36862a69f7e4a01aa524a90d3248e5fae41`.

Android producer `69006420d29a8fd581800816d3be08198056d13e`, tree
`7e63c44b013f0b994649c97b9b80aa7b8af85e42`; protected PR569 merged normally as
`93a4cbbbe6010d3082de61c697cba5fb27ea9ed7` with the identical tree.

The preceding143 native Mystic Adept Creation-to-Career/save/process-restart
smoke covers unchanged rules and persistence. Preview139's four-chapter
app-reader/EPUB/offline illustration coverage also remains separate unchanged
evidence. No new provider job was required. Page preparation remains slow in
the emulator; transient accessibility null roots and a boot SystemUI ANR were
observed. These checks do not establish general startup responsiveness, all
Creation methods, physical Play installation or a finished app.
