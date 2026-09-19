# Local SR5 Creation smoke — 19 September 2026

This is local Debug emulator evidence, not a Play publication or hosted CI result.

## Changes exercised

- Explicit New runner/build-method confirmation and pending-name preservation.
- Correct raw auxiliary-digest validation for Qualities and Gear.
- Entry to an available Qualities editor before its first required draft exists.
- Background, owner-bound catalog preparation; bounded Qualities paging/search.
- Gear browsing without repeated Core catalog loading on the UI thread.
- Structural Gear budget comparison after persistence/deserialization.
- Account-resume probing without unnecessarily excluding unchanged local ownership.

## Verified route

The retained synthetic Priority/Human/Mundane runner passed the native route
through prerequisite selection, Attributes, Skills, empty Qualities, Resources,
Gear review/confirmation, final review and entry into Career. This deliberately
minimal runner has unspent creation budgets; it is not a recommended build.

The final Gear edit was confirmed once: two Flashlights, 50 nuyen basket cost,
49,950 remaining; workspace/saved revision 8 and Gear draft revision 2.
Force-stop and a new app process reopened the identical saved draft digest.

Core's finalization review admitted revision 8. One explicit confirmation
produced revision/saved revision 9 with `CharacterCreated=true`. After force-stop,
a new process reopened the Career sheet with the same finalization receipt digest
and opened the SR5 Career wizard bound to revision 9/9.

- Debug x64 APK SHA-256: `1f0e83036a8c55c898d8ef9245fc2b5d3bc3c5ebabcf4497aac2550f28803dcc`
- Core source: `e66adccf06fb8bee96264b9a9112f970508e6e97`
- Presentation source: `ce487f3b5eb21129b7ffbac5dcd17ad8b2b64255`
- Finalization receipt digest: `sha256:300382761a7067acc3eeb2eea17f121aa1d4e9dfd24c2dd10e6bb68ce11b306d`

Focused managed/source regressions passed, including actual-Core Gear persistence,
budget-field tampering, catalog cancellation, owner ABA and staged-account recovery.
The affected test build and APK build completed with zero warnings/errors.
No exhaustive seven-journey or hosted qualification was repeated for this slice.

## Limits and next delivery step

Catalog loading and some synchronous preview/confirmation work still take visible
time in this constrained Debug emulator. Android system components showed cold-boot
ANRs; the final Gear/finalization boot's event log contained no Chummer ANR.
This is not a general performance or ANR-free claim.

This proves the observed Priority route, not full completion of Karma, Sum-to-Ten,
Life Modules, all Career actions, tablet or generic Full Editing. Core/UI feature
branches still need their changed package inputs integrated; their existing
package-plane checks are not green. No release key was used, no release AAB was
built and no new Play upload was performed for these changes.

Local screenshots, hierarchies and logs are retained with the operator's
`android-new-runner-20260919.V3tHjA2p` walkthrough packet. The owned emulator was
shut down normally; its saved runner remains available for subsequent checks.
