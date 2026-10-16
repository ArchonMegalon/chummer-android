# Completion initial-read recovery — local verification

The Preview 239 completion entry now performs at most one fresh initial-input
read when a same-saved-state hydration replaces its profile while the original
read is awaiting activation. It discards the retired authority rather than
rebinding old inputs. The retry requires the same owner transition stamp,
workspace, saved/content revisions, navigation, build method and visible page;
cancellation, departure, busy/error/conflict state and legitimate rule blockers
remain fail-closed. No Review, Apply or purchase is retried.

Verification on 16 October 2026 used the existing offline local Docker toolchain,
the retained UI/Core package graph and real Core/MAUI managed handlers. This is
not a native-device, hosted-CI or Play qualification result.

- `initial-load-refresh-preview239`: failed before the product fix in retained
  `native-regression-08`, passed after it in `native-regression-09`.
- Departure, owner A→B→A and a second profile replacement passed using the same
  `09` compiled harness; cancellation passed in `native-regression-11` after
  correcting test callback admission and comparing the original Shell stack
  rather than assuming its root entry is the displayed page.
- The hash-bound, synthetic original-device checkpoint passed full managed
  choices → proposal → Cancel → reread in `11`. Original bytes, saved revision,
  manual weapon/armor instances and receipts remained unchanged. No Review,
  Apply or additional purchase was dispatched.
- Both affected compile runs had zero errors and four existing warnings.
  The 19 existing finalization/navigation source-contract tests passed.

The captured-file replay used two CPU cores and 4 GiB without swap, measured
15.034 s to proposal return and another 2.663 s to starting-cash return;
the whole callback finished at 17.708 s. Cancel joined at 1.753 s. These are
managed local measurements, not phone latency. Earlier one-core captured runs
were inconsistent, including a bounded join failure and a post-Cancel compound
assertion failure. Their logs remain retained; this later pass does not explain
every earlier failure or prove host pressure was their cause.

The `11` input patch SHA-256 is
`414055debc8e792e233f6212610d3cf6bb35084cc2648d80b55551191b142d10`.
Its successful captured-replay log SHA-256 is
`aa96c60eefb4f4b530dfa4ee0032dfbb71c184b71def8e76bc5b137ce25b8770`.
The private checkpoint remains outside the repository.

Release boundary: Preview 238 remains the recorded Play Internal distribution.
The changed APK still needs actual Review/Apply, Career entry and process-restart
reopen verification. The original APK08 incident is not closed solely by these
managed checks. No Preview 239 AAB, signing, upload or physical installation is
claimed by this document.
