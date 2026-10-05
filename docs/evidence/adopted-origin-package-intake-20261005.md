# Continue an adopted Life Modules story

## Product correction

An explicitly adopted local runner keeps its original narrative owner and story
digests. Rendering that freshly Core-admitted workspace must use the current
custody owner without rewriting the narrative. The Android bridge now uses the
shared admitted projection. Unadmitted projection remains strict; owner changes,
stale revisions and corrupt checkpoints still reject the action.

## Exact inputs and local verification

- Android behavior commit: `0b205749deebb22ba7d719321f4d10701808ca59`.
- Presentation seal: `5859961a8d0eef814a2bf86fe702b0013512ae58`.
  UI PR317 merged as `c29c68b4a08297641e355e5f81f12e29ba75d726`; both trees are
  `37dca9c9e9ede012f3a70ae8b569fba78bac77c7`.
- Core runtime remains `5f4e6350791c2c4fd7959da1f07cc1a85ce5446e`;
  package recipe remains `51c6a6bbdb1a498352f95512b7d47f11e6fa42b7`.
- Actual local UI producer attempt7 contains 18 packages. Its cache and complete
  consumer receipt are bound by `eng/internal-phone-beta-package-authority.json`.
  Consumer verify4 passed five builds, 869 Product tests and focused suites.
- Actual Android intake2 compiled the native check, MAUI target and managed
  interaction tests with zero warnings/errors using SDK10.0.112. Eleven Origin
  story-flow checks and three real-Core adoption checks passed, including opening
  prepare/confirm, cold reopen, retained prose/image/EPUB, partial recovery,
  wrong-owner/corrupt rejection and A→B→A rejection.
- This Android compile uses pinned Presentation source and locked owner packages;
  it is not a package-only APK build. Its inputs were captured before this
  metadata repin; the repin updates authority constants, not behavior or packages.
- Actual package-authority verification, 141 focused Python cases with 856
  subtests, the changed workflow-pin assertion, 23 workflow cases with 85
  subtests, canonical content verification and private-key hygiene pass.

The broader historical Android source-contract invocation was not green:
18 assertions failed and 70 passed. It used older ambient dependency checkouts
and includes stale Stories/navigation/icon assertions. These were not weakened
or reported as current behavior regressions. The changed pin assertion was
verified separately. The first content check used an archive without Git
provenance and correctly blocked; the exact clean Core checkout passed.

## Device scope and remaining work

A prior local API36 diagnostic source-overlay APK exercised the actual adopted
runner: opening decisions, first chapter request, reader navigation and
force-stop/reopen retained the same request. That is separate from the new
package-consumer compile evidence and is not a Play-installed test.

Current genuine chapter/image/EPUB verification is still blocked by a live Hub
install-linking persistence failure. Do not claim a completed automatic book,
multi-chapter likeness/aging proof, new signed AAB, upload, or physical Play
installation from this intake. Existing uncertain provider jobs remain fenced.
