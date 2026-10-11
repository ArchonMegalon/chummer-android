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

## Native route

The local API-36 x64 Release/Internal SDK-test APK built with zero warnings/errors:
`ca4b9d791b59025b41b73058c65fae37e74b1aec7cf773d6191a1cfb64ead238`.
Its separate application ID and SDK debug certificate are not a production
upload signature. Product source, resources and build inputs match reviewed
commit `bf39f070aac94facb3b2f4bca23e8197340c85bc`; Core/UI inputs are unchanged.

The German, 130%-font native route used an actual Core-created synthetic ready
Priority draft. Explicit starting-cash input 1 for Street produced the displayed
20 cash, 5,020 total nuyen and 7 karma. One confirmation opened the Career root
automatically, without the receipt page. Android Back returned to the launcher,
not a consumed wizard. Force-stop and a verified new process restored the same
selected Career runner.

The runner advanced from revision 7/7 to 8/8 with exactly one finalization
receipt. Stored values remain 7 karma and 5,020 nuyen; consumed creation state
is retained in its finalization archive. The other seven synthetic runners were
unchanged, and all eight workspace files after cold reopen match their exact
post-finalization bytes. No second confirmation was issued. The owned emulator
was stopped and only its scoped temporary egress rules were removed.

The retained packet is `finalization-forward-20261012.3i2W4tBd`, including
screenshots, hierarchies and before/after/cold state. A pre-unlock capture failure,
SystemUI boot ANR, slow initial restoration and an early test-driver Back before
the keyboard appeared remain recorded. They are not startup-performance passes.
One selector mismatch caused no tap. No user phone or provider was used.

## Internal delivery

Android706 merged normally as `3d651f1d69f5e96d73fb328987594a9318adb7fc`,
tree `0179f2ff4ea72069273369dbc101a7eb55cc1781`, identical to the reviewed tree.
The local ARM64/Internal AAB passed source/content/proof/hygiene checks,
isolated original-key signing and independent keyless signature/payload checks.
Signed SHA256: `28bbfaeecf4b586daca0afaa03e3da7705183a7784a397e84524de622a24f87a`.
One authorized upload and final confirmation produced Preview197, observed
Available to internal testers at12 October00:50 Europe/Vienna. See the
[exact release record](../play/evidence/preview197-internal-observation.json).
Physical Play197 installation is not yet verified. This is not a complete
Creation/Career/Origin, startup-performance, tablet or general-release claim.
