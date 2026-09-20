# Local Sum-to-Ten Creation smoke — 20 September 2026

This is local, affected-route evidence for the experimental SR5 phone wizard,
not seven-journey, all-method, ARM64, Play-install or performance qualification.
Preview 21 remains the last observed Internal artifact. Preview 22 is a new
candidate version; this document does not claim its signing or publication.

## Inputs and changes

- Android branch: `feat/android-sum-to-ten-completion-20260920`, based on
  `d956b146849567e863bc02392583d9eff1197a92`.
- Core semantic source: `034b5a656fd99e7a96eeb21593c6cafaa13b8ebc`; separate
  package recipe: `4dd649826581086dafae1a46791ae21863527a16` (Core PR #65).
- Presentation source: `c2f755acf584de513bcf042877471b4bbcc88543`.
- Existing local Docker toolchain:
  `sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.
- Debug x64 API 36, isolated synthetic package, no proof instrumentation, SDK
  debug key only. The Play upload key was not used for this smoke.

Core now admits the exact Sum-to-Ten method through Skills, Qualities,
Magic/Resonance and atomic whole-build finalization. Android schedules Skills,
admits method-bound Qualities and restores the exact persisted final receipt.
Cross-method, source, owner, revision and digest checks remain intact. DE/EN/ES
shared page copy no longer mislabels a Sum-to-Ten draft as Priority.

Attributes/Skills return and Qualities receipt acknowledgement use the attached
phone Shell instead of popping a child and then using its detached navigation
proxy. Existing acknowledgement CAS and single-action guards are preserved.

## Actual visible route

The same synthetic workspace was continued across the diagnostic APK updates;
this is **not** a complete fresh creation run on one APK.

1. Sum-to-Ten, repeated ranks E/E/A/A/C, Human, Mundane; confirm prerequisites.
2. Body 1 → 2 using one normal attribute point; save draft.
3. Select Arabic as native language; confirm Skills. No active skill points
   were spent in this device fixture (purchased skills are covered by Core tests).
4. Review and confirm an empty Qualities draft.
5. Select zero Karma conversion: rank C, 140,000 starting nuyen.
6. Add one source-backed Flashlight (SR5 p. 449), cost 25 nuyen; confirm once.
7. Core final review accepted revision 7. Confirm once to revision/saved
   revision 8/8, open Career, explicitly Save.
8. Verify the old process has ended after force-stop. Cold launch into Career
   in a different process; read back the identical saved workspace.

Final successful APK SHA-256:
`93ac8330fc66ba9e42000d93961e11073ec018fdd16dacc121edf6bf15944fda`.
It built with zero warnings/errors in 1m46.15s, before the metadata-only version
22 bump. It covers saved Resources reopen, Gear, finalization, Career and restart.

The before/after restart workspace bytes match:
`3f2f457ee319f789fb2b33b2d93412111cf322b0709f381476ed968c2151dd66`.
There is exactly one finalization receipt, digest
`sha256:fe19597eec78cfe079ba28c22891e89d33c84ea9f2edfc92646b676723c455db`.
Final XML retains `created=True`, `SumtoTen`, Body total 2, native Arabic,
one Flashlight and 139,975 nuyen. No confirmation was replayed to recover an
observation timeout or navigation failure.

## Runtime finding and mitigation

The earlier concurrent-GC APK
`726be08aba15e42147331c59fc22c968ff8cd0ab729cd0207747e8ddf3a14322`
aborted in Mono/SGen after Skills return, then crashed in libmonosgen after a
cold reopen. Both crash packets are retained; this earlier run is not a pass.

A diagnostic rebuild changed the generated runtime environment from
`major=marksweep-conc` to `major=marksweep`. That build saved Qualities and
Resources without another crash. The project now explicitly disables concurrent
SGen for Debug and Release. The final APK above completed Gear, finalization,
save and verified process restart without a new crash in the retained crash log.

The matching upstream report is [dotnet/runtime #100311](https://github.com/dotnet/runtime/issues/100311).
This is a scoped mitigation, not proof of the underlying runtime defect or
long-term/ARM64 stability. First loads remain slow; no responsiveness SLO is claimed.

## Focused verification and remaining limits

- Actual Core/native coordinator Sum-to-Ten creation/finalization/cold receipt:
  PASS; scheduler and real Qualities admission regressions reproduced before fix.
- Persisted receipt projection: 7 cases PASS, including cross-method rejection.
- Affected Python source/runtime configuration checks: 29 PASS.
- Core method/source tests: 10 PASS; finalization/owner/prerequisite: 152 PASS;
  domain/source cases: 90 PASS. These are managed tests, not device coverage.
- Local Core package consumer: eight packages, fourteen asset graphs, 609 managed
  tests PASS, plus existing owner/store/inventory checks. This is separate from
  Android's explicit source-assembly build, not a package-only APK claim.
- New Qualities acknowledgement return has source/build coverage but was not
  repeated visibly after its fix; the prior receipt was already acknowledged.
- Awake/Resonant Sum-to-Ten has Core coverage but not this phone walkthrough.
  The Presentation Magic/Resonance lane still needs method admission review.
- The minimal fixture leaves points unspent. Contacts, lifestyles, all Career
  actions, Life Modules and tablet/full-editor parity are not established here.
- A misleading legacy dashboard Karma-authority blocker remains visible for
  Sum-to-Ten while the separately Core-authorized final review is available.

Detailed logs, screenshots and synthetic state are retained in the local
`sum-to-ten-completion-20260920.tfYB5vmK` packet, outside served directories.
Release AAB verification, isolated existing-key signing, actual Play readback
and physical Play installation are separate steps, not asserted by this record.
