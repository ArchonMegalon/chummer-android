# Prerequisite XML package intake — 7 October 2026

This update consumes Core's per-projection XML writer reuse for Creation
prerequisites. It preserves rule results, typed identities, source digests and
fresh owner admission. There is no new shared mutable catalog cache.

## Exact local inputs

- Core runtime: `d9f29b059d44954dcaf2a0012ca7851e56b97be9`.
- Core recipe: `70f68382fe45daa0c29a7c4dd27cf1a8cfea505a`.
- Core ZIP SHA-256: `1afbb897447cd9dc26e9df554ac0e6508df3eec21c81ec0836077181f12e36e2`.
- Presentation seal: `7f1e1f7299265571448d449aca30ebc3c549c5f7`.
- Presentation lock SHA-256: `0d1433691df160ad29bfeaaac18507857ec95a176a09de66c80a9570f4762db7`.
- Local UI consumer receipt SHA-256: `c324cc2690d95b12ec6bd9979865342ce994a291b7b3bb9542f2f92f24a49eb7`.
- Owner-cache manifest SHA-256: `ce75882f181f8e8c1db7a87aa882d119fc15fb7c3f73ca26089717548bdd10ad`.

The UI recipe was normally merged in PR350 before the generated two-lock seal
was committed. Local consumer verification of that exact seal completed at
16:54:10 UTC: five builds, 869 product cases and all selected focused groups
passed. This is local verification, not a hosted-check or merge assertion.
Android independently accepted the exact receipt, all 18 packages and their
authority inventory. All 331 rule-data files remain byte-identical; the content
manifest changes only their recipe provenance and resulting bundle digest.
APK assembly still uses pinned Presentation source and explicit Core content.

## Affected verification

Core's focused XML-equivalence, ownership, isolation and allocation checks pass.
Managed allocation for the full prerequisite projection decreased from
43,210,496 to 22,003,768 bytes, with identical authority digest. This is not a
native startup-time or peak-memory benchmark.

The local Android intake compiled the native-source check, MAUI target and
interaction tests with zero warnings/errors. Startup tests restored Priority,
Sum-to-Ten, Karma, Life Modules and an existing Career runner without mutation.
The failed-presenter case retained fail-closed recovery and full synchronization.
Focused package, content, source graph, physical-contract and historical
verifier tests passed; simulated receipts are not actual hosted/device evidence.
Repository private-key hygiene and exact content validation passed.

Initial test invocations lacked the explicit workspace or Python import roots;
the affected tests passed after correcting invocation, not changing guards.
An unnecessarily broad legacy Android-contract selection also reported three
failures and fifteen errors (stale markup expectations and incompatible ambient
Presentation files). That selection is not claimed green or whole-app coverage.
The changed dependency-pin assertion passed separately. Existing navigation,
startup markup and launcher-scale assertions were not weakened for this intake.

Earlier diagnostic API-36 testing used this exact Core bundle with the previous
UI graph, not the final seal above. Its separate SDK-test APK
`b4de26b2cb7f1b5386abf12ebcac3425dd02e4717835131b14b8ec6a7e4b5413`
retained the same saved runner across an in-place upgrade and verified new
process. Workspace SHA-256 stayed
`2ff4347b6641900db048c44c0634fb8d3e02e08ceb2844f031d5835dcdf410ad`.
Observed restoration times were 25.484 and 14.521 seconds, not a controlled
speedup. A SystemUI ANR remains recorded. No Save was replayed.

## Final sealed-graph native check

The exact Android tree `6fa3590f30bcbd31e223c46a3625119bb4050181`
(commit `ff0b7756697376ea82b0acb4ce3be8c9a3ffeb21`) built locally in
2m27s with zero warnings/errors, using the final seal above and no Core override.
SDK-test APK SHA-256:
`dcd510f066f414b7aabe9e32fb7516580b6a162643f812c0205cf49fc5793884`.
Its test certificate and all 331 embedded content files were verified.

An in-place update preserved the retained synthetic runner. Creation rendered
before and after a verified force-stop/new process (PID 3573 to 3757). The
workspace SHA-256 remained `2ff4347b6641900db048c44c0634fb8d3e02e08ceb2844f031d5835dcdf410ad`
before upgrade, after upgrade and after restart. No Save was replayed and no
data was wiped. Screenshots, complete non-null hierarchy observations and
process-bound startup logs are retained in the private local packet.

Workspace restoration took 25.613s and 14.440s; shell initialization took
9.147s and 6.117s. These are observations, not a controlled native speedup.
A boot SystemUI ANR was recorded and its visible Wait action selected once.
The owned emulator is stopped; its saved data remains available.

## Delivery boundary

Preview129 is the next local candidate. The native SDK-test smoke above predates
only version metadata (128 to 129) and this evidence text; exact ARM64 Release
bundle inspection must cover the version/package/architecture delta.
No new AAB, signature, Play upload or physical installation is asserted here.
Preview128 remains the latest observed Internal artifact; its evidence and
earlier release artifacts remain unchanged. Native responsiveness and complete
SR5 Creation are still open.
