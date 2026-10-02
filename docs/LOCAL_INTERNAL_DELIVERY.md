# Local Play Internal delivery

The owner's 2026-09-19 decision is the default for small SR5 phone-wizard updates:
build and host locally, run risk-based affected checks, sign separately with the
existing upload key, then verify the real Play result. This is not a shortcut
through the historical hosted signer protocol and creates no two-green receipt.

## One current policy, separately named evidence

Canonical Design policy is `RELEASE_PIPELINE.md` and `internalDeliveryPolicy` in
`ANDROID_PHONE_BETA_SUPPORT_MATRIX.yaml`. Android pins its exact commit, tree,
matrix and validator in `eng/local-internal-design-policy-authority.json`, using
the existing policy-binding schema. Given that exact clean Design checkout:

```sh
python3 -I -B scripts/android_design_policy_authority.py \
  --local-internal --design-root /absolute/path/to/exact/chummer6-design
```

Android checks those committed bytes independently, including local testing,
key isolation, certificate and Play boundaries, and checks that extended E2E is
manual-only. A passing policy check proves agreement, not a build, owner approval,
signing, upload, physical installation or beta readiness. Do not use an arbitrary
current sibling checkout or silently repin during a transaction.

The older `eng/design-policy-authority.json`, v3 two-green policy, ordered replay
workflow, `prepare-release-inputs.sh`, `build-release.sh` and hosted signer/verifier
contracts are retained as the **historical hosted protocol**, not the local
Internal route. Their guards stay intact. Historical replay still needs actual
PR and main-push events, exact artifact provenance and its original policy.
Manual dispatch and local smoke cannot satisfy those event identities. There is
no need to manufacture new historical receipts for a local update.

The optional seven-journey aggregate remains unchanged: it requires all seven
journeys on one APK if that particular coverage is claimed. A short route smoke
is never called a seven-journey pass. No branch protection is changed by this
document or by selecting the local policy.

## Delivery checklist

The current package intake may reuse the exact UI owner-artifact cache under
the local-first policy. Its receipt remains pinned by SHA-256 and byte size,
alongside the exact consumer commit/tree, package locks and producer authority.
The validator rechecks all 18 packages and 13 authority files and compares their
actual bytes with the receipt's copied-file inventory. The consumer NuGet cache
must still have started fresh; source fallbacks and stub packages remain forbidden.
This is authenticated local reuse, **not** a cold owner rebuild or hosted CI pass.
Historical cold receipts retain their separate strict non-use predicate. Nothing
here changes protected merge checks, signing or Play authorization.

1. Freeze the exact change and advertised scope. Keep an unused higher version
   code, source commits/trees and input hashes. Record dependency mode honestly:
   sealed package graph, or exact source assembly with explicit absolute roots,
   clean exports and retained content/dependency inputs. Never use an ambient
   sibling or call a source assembly package-only. Existing package seals remain
   separate proof; a source-only merge check is not a managed compile.
2. Run the affected local build and existing managed tests. Smoke the changed
   wizard. For persistence/lifecycle changes include save, reopen and a verified
   new process. Keep focused negative tests for ownership, credentials, signing
   and destructive behavior. Reuse results only for unchanged inputs and state
   any untested delta. Docs-only policy changes need lightweight checks, not an
   AAB or emulator run. Broad device suites are manual/impact-driven, not the
   default double-run prerequisite.
3. Build the exact ARM64 Release AAB in the existing local keyless Docker
   toolchain. Record its toolchain, source/content/package identities and unsigned
   digest; retain logs and inputs outside served directories. No signing key,
   cloud credentials, Docker socket or private credential store in the builder.
4. Inspect the unsigned bundle using the existing bundle, content, private-key
   hygiene and proof-exclusion verifiers. Require the intended application ID,
   unused version, target/minimum API, ARM64 payload and current privacy/network
   posture. Record any debug-versus-Release smoke limitation explicitly.
5. Hand only the exact admitted artifact to a separate local key-bearing signer.
   Require the existing upload certificate; never regenerate or substitute the
   key as recovery. Independently verify the signed hash, strict JAR signature,
   certificate and unchanged non-signature ZIP payload in a keyless verifier.
   Preserve unsigned/signed hashes, verification results and recovery inputs.
6. Under the owner's upload authorization, upload that exact bundle only to the
   Chummer Internal track and existing tester audience. Stop on identity drift,
   conflicting draft/version use, revoked approval or failed verification. Do
   not change Production, tester membership, billing or account security.
7. Observe actual Play processing/availability with the scoped Console or
   Publisher API. Record which transport was used; browser readback is not API
   evidence. Report **Available on Play Internal; physical Play installation not
   yet verified** until the physical step succeeds. Do not replay a consumed
   version code or rebuild the same uploaded version to change producer metadata.
8. Install/update through Play on a physical ARM64 phone, verify package/version
   and the Play signing identity (distinct from the upload certificate), first
   launch, changed route and saved-state restart. Retain bounded installation
   evidence without device identifiers. Only then consider the separate declared
   phone-beta claim; unavailable optional capabilities do not become supported.

The retained Preview 18 local packet and committed
[artifact/readback record](../play/evidence/preview18-internal-observation.md)
are the concrete existing local recipe/verification example, not a reusable
Preview 19 approval. Its per-candidate hashes, versions and private paths must
not be copied as new evidence. No new generalized signer or receipt chain is
required by this policy.

Known credential, ownership, crash, data-loss or broken required-wizard defects
still block the affected candidate. Keep experimental Internal scope explicit;
do not imply all build methods, full Career coverage, Full Editing, tablets,
desktop or Rook. An upgrade recovery uses a higher version code and compatible
stored data, not an Android downgrade. Keep the previous artifact and evidence.

## Current evidence boundary

Preview 75 is the latest [observed Internal availability](../play/evidence/preview75-internal-observation.md),
confirmed on 2 October at 19:03 Europe/Vienna. Its exact locally built ARM64 AAB
was separately signed with the existing upload key and independently verified.
It adds 31 short original EN/DE/ES Quality summaries, shortens 48 existing ones
and folds supporting effects behind a disclosure while keeping warnings visible.
520/803 are authored; 283 Quality summaries and ritual/enchantment help remain open.
Ordinary-spell coverage remains 292. The normal physical Play update74→75,
installed base-APK signature, Gearhead help, Mystic Adept effect disclosure,
special-attribute jump and existing-state process-restart observation passed.
See the separate [Preview 75 physical observation](../play/evidence/preview75-physical-observation.md).
No new saved mutation or exhaustive physical catalog check is claimed. Existing
audience and other release surfaces were unchanged. No copyright clearance,
full-book, all-method or public-beta completion is claimed.

Preview 74's [Internal availability](../play/evidence/preview74-internal-observation.md)
and [physical observation](../play/evidence/preview74-physical-observation.md)
remain unchanged historical evidence.

Preview 73's [observed Internal availability](../play/evidence/preview73-internal-observation.md)
remains historical evidence.
Its exact local ARM64 AAB was independently inspected, separately signed with the
existing upload key, verified and accepted on the unchanged Internal track on
2 October. It includes short original quality/ordinary-spell explanations,
attribute/Karma-quality improvements and the sealed Tradition correction.
Descriptions remain incomplete; editorial brevity is not copyright clearance.
The normal physical Play update from 72 to 73, installed base-APK signature,
first launch, Mystic Adept/Acid Stream help, bold attribute values and reopening
the existing Magic allocation after verified process death passed on 2 October.
See the separate [Preview 73 physical observation](../play/evidence/preview73-physical-observation.md).
This was read-only navigation, not a new physical save transaction or full catalog
qualification. Existing saved source settings and font scale 1.3 were preserved.
Preview 72 retains the separate allocation/undo observation:
The normal physical Play update from 52 to 72, base-APK signature, first launch,
special-attribute navigation, unsaved Magic 4 → 5 → 4 adjustment/undo and reopening
the existing allocation after verified process death passed on 2 October.
See the separate [physical observation](../play/evidence/preview72-physical-observation.md).
This is not a new physical save transaction, all-workspace byte comparison,
all-method qualification or full-book pass. Special Attribute Points now jumps
to its allocation group with explicit
EN/DE/ES special-point controls. Actual-Core Priority/Sum-to-Ten checks and native
Priority allocation/save/new-process reopen passed; the other 31 workspace files stayed
unchanged. Pending Magic-choice feedback and guarded early-Back behavior from
PR 258 are included. Preview 71 was uploaded but held, then removed only from the
unpublished draft; it remains in the artifact library and was never released.
Existing technical attribute-review text, broader physical coverage and full polish remain
open. Prior Preview 70 evidence remains immutable: static Magic saving feedback
replaces the animated spinner, with short
EN/DE/ES actions and unchanged owner/review/mutation guards. Focused managed checks
and native save/new-process receipt reopen passed; the other 29 workspace files
stayed unchanged. One normal-animation emulator comparison measured 58,250 to
14,088 ms; this is not a general or physical speedup claim. Leaving a spell option
before its asynchronous selected-state update can still discard that uncommitted
selection. Pending feedback assertions are managed, not a native pending-frame
observation. Private x64/test-ID/version68 timing probes are absent from the clean
production ARM64/version70 artifact. Full polish and physical testing remain open.
Preview 69's Magic review, save and recovery are readable and owner-bound. The new
Core/UI packages and affected managed checks passed. Native selection, one save,
verified new-process receipt reopen and acknowledgement passed; the other 29
workspace files stayed unchanged. Confirmation still took about 85–90 seconds;
no general responsiveness or ANR-free startup claim. Diagnostic x64/test identity
and version 68 are separate from the production ARM64/version 69 artifact.
Preview 68's fresh Magic draft admission validates one canonical projection,
retaining unsaved choices on an unchanged owner/editor and rejecting stale or
invalid authority. Actual-Core negative/ownership/cold-reopen checks and the
affected native return/selection smoke passed; all 30 saved files were unchanged.
One instrumented emulator pair measured return preparation at 8,984 to 6,016 ms;
fresh Core loading remains about five seconds. This is not a general or physical
speedup claim, nor a complete native Magic save/restart journey. Both diagnostic
startups encountered a System UI ANR before the route; no ANR-free startup claim.
Preview 67's Magic page/overview/review preparation reuses one canonical projection
per fresh admission, retaining exact Core/owner/display/digest and mutation checks.
Actual-Core managed negative/ownership/save-cold-reopen checks and native upgrade,
stored-runner reopen, catalog selection and return-navigation smoke passed; all
30 saved workspace files stayed unchanged. Main-page return is still noticeably
delayed; no full native Magic save/restart or general speedup claim.
Preview 66's duplicate Magic guidance is removed while retaining all exact diagnostic
codes, and disabled primary/secondary buttons have readable colors. Actual-Core
managed checks and affected native disclosure/disabled-control/draft-selection
smoke passed; all 30 existing workspace files were unchanged. No full native
Magic save/restart or general performance claim. Transition latency and some
implementation-oriented Magic labels remain follow-ups.
Preview 65's Magic blockers have readable EN/DE/ES guidance, with exact codes
behind diagnostic disclosure and unsupported selections still disabled. Actual-Core
managed checks and the affected native guidance/disclosure/draft-selection smoke
passed; all 30 existing workspace files were unchanged. No full native Magic
save/restart or general performance claim. Its repeated guidance and disabled-button
visual styling are addressed by Preview 66; historical evidence remains unchanged.
Preview 64's Magic catalogs show at most 20 rows per page with name/source-book
search, retaining all Core options and unchanged rules. Managed catalog/owner/
save-cold-reopen checks and native paging/search/draft-selection smoke passed;
all 29 prior workspace files were unchanged. Native full Magic save/restart is
not claimed. Its raw Magic blocker messages are addressed by Preview 65; historical
evidence remains unchanged. This is not all-screen or physical-performance approval.
Preview 63's Core operation-scoped quality batches remove repeated catalog
preparation while preserving eligibility, option order and costs. Actual-Core
equivalence/ownership checks and native selection/help/save/new-process receipt
reopen passed; all 28 prior workspace files remained unchanged. Managed timing
improved, but general Android/physical responsiveness is not qualified.
Preview 62's cumulative saved Qualities Karma correction remains included:
a 4-Karma quality leaves 21 of 25, including after verified new-process reopen.
Core-backed Priority/Sum-to-Ten save, cold reopen and removal regressions passed;
the native affected-route smoke preserved all 28 stored workspace files.
Remaining native catalog/transition latency is an explicit follow-up; no physical-performance claim.
Preview 61's quality configuration, review and saved receipts use readable names,
costs and actions, with technical IDs/digests behind disclosure. Core-backed
EN/DE/ES checks and native quality save/new-process receipt reopen passed; 27 old
workspace files were unchanged. Its pending-Karma dashboard follow-up is corrected
by Preview 62; the earlier evidence remains an immutable record of that version.
Preview 60's Creation budgets retain one shared readiness hint, shorter actions and
unchanged exact values/editor routes. Skills review guidance is translated.
Affected managed/localization checks and a Release diagnostic native upgrade/reopen
and prerequisite-navigation smoke passed; 27 saved workspace files were unchanged.
This is not all-screen or physical performance approval. Preview 59's managed-only
Magic allocation improvement remains included, without an Android speedup claim.
Preview 58's readable save/conflict guidance and Preview 57's
quality explanations/readability and all SR5 sources
as defaults for new runners remain included, without changing old saved settings.
Focused managed checks cover all four defaults. Preview 56's simpler Creation,
Gear/Resources screens and explicit Skills/Magic re-review remain included.
This is not all-screen polish or complete build-method/Career coverage.
Preview 61 and earlier evidence stay immutable. Preview 55's
editorial-assisted illustrated chapter does not establish unattended
full-book quality or cross-chapter likeness approval.

[Preview 75](../play/evidence/preview75-physical-observation.md) is now the latest
physically observed Play version, with the bounded affected-route coverage above.
[Preview 52](../play/evidence/preview52-internal-observation.md) remains historical:
its normal Play update from 51, first launch and saved-runner process
restart passed. The observed unlinked account route settled without a stale
waiting notice; this is not a new account-login or full-book reader pass. The
installed base-APK certificate matched Preview 50/51 but was not independently
compared with Play Console. Preview 51 and earlier
evidence remain immutable. Neither this policy nor availability seals a new Android
package graph or retroactively claims hosted qualification. Protected source
integration and each candidate's actual build/test/sign/Play work remain separate.
