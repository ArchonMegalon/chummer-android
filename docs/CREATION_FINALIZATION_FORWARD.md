# Final confirmation → Career

Successful Priority and Sum-to-Ten final confirmation now returns directly to
the Career runner root. The consumed wizard pages are removed from Back
navigation. This happens only after the coordinator has accepted the exact
durable receipt and restored the current owner's Career revision.

Post-commit restoration failures keep the existing saved-receipt screen.
Leaving or changing the navigation stack while saving cannot steal navigation.
Returning to a committed review offers a read-only continuation, never another
confirmation. Account transitions, including A→B→A, still invalidate the old
display authority. Core rules, persistence and receipt-owner admission are
unchanged.

## Local checks

The affected managed compile passed with zero warnings/errors. The existing
workload-inventory warning remains; the installed toolchain was not changed.
26 actual Core/MAUI cases passed:

- Ten new forward-navigation cases: Priority, Sum-to-Ten, linked owner,
  post-commit shell failure, departure, return, changed navigation stack,
  owner B, owner A→B→A and lost confirmation result. Each verifies one durable
  commit, current Career state or retained fallback, and no replay.
- Four existing starting-cash cases, covering both methods with and without
  purchased lifestyles, explicit dice input, invalid-input feedback, receipt
  disclosure and cold stored state.
- Twelve existing finalization owner/commit recovery cases: local baseline,
  eight linked/stale/queued cases and three post-commit faults.

Eight focused source/localization checks, the version/identity check,
private-key hygiene and whitespace validation passed. The version test's first
invocation failed because its explicit dependency roots were omitted; rerunning
with the existing workspace roots resolved setup without weakening the test.

Retained earlier failures distinguish the actual before-fix navigation
regression from two test-adapter corrections: native scroll completion was not
acknowledged in the headless host, and the cash test passed a freshly loaded,
unissued receipt to a screen requiring original-owner issuance. The latter now
also asserts that a matching stored projection is not a display capability.
Timeouts and production owner checks were not relaxed.

Native API-36 smoke, a signed ARM64 AAB and Play delivery are still pending for
this change. This is not a complete Creation/Career/Origin, physical-installation,
startup-performance, tablet or general-release claim.
