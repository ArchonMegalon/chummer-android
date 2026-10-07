# Magic catalog package intake — 7 October 2026

This change consumes Core's scoped XML writer-buffer reuse for Creation Magic
catalogs. Rules, option identities and catalog digests remain unchanged. Buffers
belong to one synchronous projection; fresh source and owner checks are retained.

## Exact local inputs

- Core runtime: `abee6939e92e1de29403e0665bfab42664ee18af`.
- Core package recipe: `e2b93e59ed68689b621a83be8a8b4cf29a00fb09`,
  normally merged through Core PR127 with an identical tree.
- Core ZIP SHA-256: `031a2214ef4bdd9737798ecb9fe9f2d9316e463405f09290d69632e8aa0a7fab`.
- Presentation seal: `8e89c3f92f5ea1c6c95c8cab69128ae48aa45b6e`.
- Presentation lock SHA-256: `5be9df92d2f416eb28b1f1ddab37fee054483e9eddd9ce12640a13a7a579902f`.
- Local UI consumer receipt SHA-256: `74d930c296872ca796fcad6bc807b36d61565851e7fe1a2fee903c02ef1e3585`.
- Owner-cache manifest SHA-256: `baa0efbb608eb8732259a1c8a4d77c09437f809405d5625c8e440a0e1e23ed54`.

Actual local production emitted both UI locks. The exact local consumer passed
five builds, 869 product tests and all selected owner/continuation regressions.
The original UI PR347 was superseded by PR348 to preserve the required seal
topology after the protected preseal publication. PR348 merged normally as
`c42cdf3c4d728b6cf78bfba3dd7484e534eb4969`, with the same source tree and
unchanged package bytes. The exact corrected consumer passed locally at
14:53:03 UTC; the receipt above binds `8e89c3f`, not the earlier seal.
Two preceding download-preflight failures remain recorded; no TLS, timeout,
retry, test or authority check was weakened. The successful local run used
observational public-request logging outside the unchanged repository verifier.
Android independently verifies all 18 packages and their authority files.
All 331 rule-data files are unchanged; the content manifest records the new
recipe provenance. APK assembly still uses pinned Presentation source and an
explicit Core content export; it is not a source-free APK or hosted proof.

## Affected checks

The Core regression failed before the fix. Managed allocation for 128 catalog
rows decreased from 5,622,856 to 1,980,040 bytes; all 585 rows across five actual
catalogs decreased from 27,740,352 to 10,607,976 bytes with identical digests.
Focused XML boundary, ownership, parallel-isolation and reopen checks passed,
followed by 775 tests in local package production. This is not native latency
or peak-memory evidence.

The local Android intake compiled the native and MAUI targets and interaction
tests with zero warnings/errors. Dashboard entry, short spell help in three
locales, actual Mystic Adept and Adept profiles, power/spell purchase, Skills to
Magic revisit, single-save/cold-reopen, owner ABA, cancellation, recovery and
uncertain-reply no-replay tests passed. These are managed native-source tests.
The restore log retains a transient NuGet connection reset; restore completed.
On the corrected seal, 270 focused package/source/authority/workflow verifier
tests passed, and repository private-key hygiene passed. Initial local CLI
invocations used the cache parent or the wrong test working directory; those
failures were corrected at invocation only, without weakening any check. These
synthetic verifier tests do not constitute hosted or physical runtime evidence.

An earlier separate SDK-test API36 x64 APK with this Core bundle and the prior
Presentation graph saved Aeronautics Mechanic 2-to-3 once and reopened the same
Sum-to-Ten workspace after a verified new process. Revision 8/8, four receipts,
40 remaining active points and exact saved bytes were retained. The final
old-seal upgrade/read/reopen also passed with Android `f284da8fe06581bbe149548b6383170256b7d68b`
and the earlier `ef4828df8` Presentation seal. Its separate SDK-test
APK SHA-256 is `91d420d59f1508a2f639706c747f685ef587496aecb8014f789a815332a9d473`.
The installed APK matched those bytes. After verified force-stop and a new
process, the same Skills page showed revision 8/8, Aeronautics Mechanic 3 and
40 remaining points; saved workspace SHA-256 remained
`2ff4347b6641900db048c44c0634fb8d3e02e08ceb2844f031d5835dcdf410ad`.
Direct current screenshots were inspected; two null-root accessibility
observations remain failures. The owned emulator was stopped with its data
retained. This does not qualify the later corrected Presentation commit, and
no mutation was replayed merely to refresh evidence.

## Corrected-seal native upgrade and reopen

The final corrected-seal SDK-test APK was built from Android
`bb4da3322ea232d876d40433bb0e4ace6a5bf2db`, tree
`92fa376f3c296e82f5800f706dd5375f1443df0d`, and Presentation `8e89c3f`.
Its SHA-256 is `7baaf6ec828cafa88d32a6e7193cedfa76361c963aae661bf65da2cb5281f5e2`;
the local build completed with zero warnings/errors. Installed bytes matched.
An in-place upgrade retained the exact saved workspace hash above. After a
verified force-stop and new process, the visible Skills page again showed
revision 8, Aeronautics Mechanic 3 and 40 remaining active points. The saved
workspace bytes remained identical. No Save or mutation was replayed.

The Magic/Resonance page also opened with the existing Mundane talent and zero
magic/resonance budgets. This is a native entry/read check, not a magical talent
purchase or all-method coverage claim. Four null-root hierarchy observations
remain failures; separate current screenshots were visually inspected. The
initial emulator boot produced a SystemUI ANR. Workspace restoration took
72.111 seconds on the first launch and 43.310 seconds after process restart;
these are not controlled speedup results. The emulator was stopped and its
saved data retained. Native loading performance remains an open defect.

Only release version metadata 127-to-128 and this evidence change after that
SDK-test build; the exact ARM64 bundle must still be built and inspected.

## Delivery boundary

No new AAB, signing, Play upload or physical installation is asserted here.
Preview127 remains the last confirmed Internal artifact. Preview128 is only the
next local candidate. Native responsiveness and complete SR5 Creation remain
open. Existing releases and evidence are immutable.
