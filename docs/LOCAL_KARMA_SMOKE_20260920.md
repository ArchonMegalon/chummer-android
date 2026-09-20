# Local Karma draft and runtime check — 20 September 2026

This is local Debug/API-36 evidence, not a Play publication or completed Karma
creation. It supplements the Priority walkthrough; it does not supersede it.

## Current source and narrow change

Core `a3bd3b878dce4d436d9920a56a047071ad922f25` freezes and validates a complete
quality catalog once per public operation. Saved-selection and preview evaluation
reuse only that immutable, locally issued admission. Source-effect, cost, budget,
source-drift, revision and history checks remain. New calls validate fresh input.
The canonical digest format and persisted history are unchanged.

Android source was `b30c436f87b32d7fd482113f7bb3abd3a9c6fff7`, with Presentation
`ce487f3b5eb21129b7ffbac5dcd17ad8b2b64255`. These are explicit local source roots,
not a newly sealed package graph or hosted-main qualification.

## Verified behavior

- 34 focused Core tests passed, including catalog immutability, forged source
  effects, unselected tampering, stale source inputs and existing Open behavior.
- Real 915-option catalog/profile tests and temporary-fixture save/reopen passed.
- The local offline Docker Debug APK built without warnings/errors.
- APK replacement preserved the retained Karma draft at revision 10/10.
- Removing Unsteady Hands changed the pending budget but did not write storage.
- One explicit confirmation produced revision 11/11 and one new decision.
  All nine previous decisions and the original character Envelope were unchanged.
- A verified force-stop and new process reopened revision 11/11 and budget
  86.5/800, remaining 713.5. Saved and reopened workspace bytes matched exactly.

Interpreter APK SHA-256:
`c4e0b00dda9dca85dfecb5556e74293574d0f39fc9493e7064af7e31bf50d081`.

## Debug timing is not Release timing

The installed SDK defaults Debug builds to `UseInterpreter=true`. An otherwise
unchanged local build with **`-p:UseInterpreter=false`** was also installed over
the same revision-11 fixture. Generated runtime configuration confirms JIT rather
than interpreter execution; no proof instrumentation or upload key was used.

Read-only observation times decreased from about 40 to 15 seconds for the Karma
overview and 38 to 15 seconds for Qualities. Another warm Qualities visit was also
about 15 seconds. These clocks include hierarchy-read overhead and start after
input-helper completion; they are not precise tap-to-frame measurements.

This identifies a material Debug configuration effect, but **does not establish
acceptable responsiveness**. Release ARM64 with its normal AOT/trim configuration
still needs an affected-route smoke. Do not disable validations, change production
runtime settings, or silently change ordinary Debug Hot Reload to improve a metric.
Use the explicit JIT opt-out when profiling instead of assuming interpreter timings
represent a distributed build.

JIT diagnostic APK SHA-256:
`27c29d227a46f4f531d53bcd28a7d594cb07f528143e64dde7587fdebbad1e3d`.
Its install and all read-only navigation preserved the saved workspace bytes.
Raw local packets are retained under `android-karma-admission-20260920.GQ0xSIfd`
and `android-karma-jit-20260920.koKP2Fd2` in the operator's active-worktrees area.

## Still incomplete

Karma saves a pending draft; it does not yet finalize a runner into Career.
Remaining creation domains and unsupported quality effects/requirements remain
open (43 of 915 options selectable in this profile). No exhaustive-parity claim,
upload-key signing, release AAB or new Play upload is supported by these checks.
Core/UI branch integration and the next real delivery blocker remain separate work.
