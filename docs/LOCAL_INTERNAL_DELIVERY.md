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

## Standing Play Internal upload authorization

The owner's 2026-10-05 approval applies today and to future verified candidates
until revoked. Upload newly verified Chummer Android builds to the existing
Internal track and tester audience without another per-build question. There is
no operator-imposed daily or total upload-count limit; earlier one-build or daily
upload permissions within this scope are superseded. A new candidate, version or
day alone is not a reason to request approval again.

Record this standing authorization alongside, but separately from, the exact
candidate's source, checks, signing and Play results. It is not a passing test,
signer result or publication receipt. Preserve every safety check below, the
existing upload-key identity and unique higher version codes. Serialize uploads
within host/platform capacity, and reconcile uncertain provider results before a
retry rather than blindly repeating an upload or rollout.

This approval does not extend to public tracks, tester/audience changes, account
security, purchases or unrelated provider budgets. Stop for a revoked approval,
failed required check or changed release scope. Physical Play installation still
requires separate observed evidence; upload permission is not an install receipt.

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
   toolchain with `-p:ChummerDistributionChannel=internal`. This explicitly selects
   default-on, metadata-only technical reporting for Internal testing; saved
   opt-outs remain off. Record this build property and verify the Settings
   disclosure/toggle and absence of technical diagnostics from Home. Other build
   channels default off. Do not promote this same AAB to
   a public track; see `INTERNAL_TESTER_DIAGNOSTICS.md` for the channel boundary.
   Record its toolchain, source/content/package identities and unsigned
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
6. Under the standing upload authorization above, upload that exact bundle only
   to the Chummer Internal track and existing tester audience; no fresh per-build
   approval is needed. Stop on identity drift,
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

Preview 178 is the latest [observed Internal availability](../play/evidence/preview178-internal-observation.md),
confirmed on 10 October at 14:26 Europe/Vienna (Play displayed 14:24). Skills
removes duplicate feedback and gives unchanged selections a neutral "Already
saved" notice without a technical button. Genuine errors retain their warning
and disclosure; choices, budgets, rules and explicit saving remain. Focused
managed/source checks, affected native 130% German-font smoke, local ARM64 build,
original-key signing, independent keyless verification and one Internal upload/
readback passed. **Physical Play 178 installation, real tester-incident intake,
faster cold startup and complete Creation/Career remain unverified.** Version
178 is consumed; previous evidence and rollback artifacts are retained.

Preview 177's [observed Internal availability](../play/evidence/preview177-internal-observation.md),
confirmed on 10 October at 13:46 Europe/Vienna. Skills allocation/review combine
three budget cards into one wrapping summary, retaining bold remaining points,
usage/totals, controls, Core rules and explicit saving. Focused managed checks,
native normal/130% font and changed-draft review, local ARM64 build, original-key
signing, independent keyless verification and one Internal upload/readback passed.
**Physical Play 177 installation, real tester-incident intake, faster cold startup
and complete Creation/Career remain unverified.** Preexisting unchanged-review
wording remains a follow-up. Version 177 is consumed; previous evidence retained.

Preview 176's [observed Internal availability](../play/evidence/preview176-internal-observation.md),
confirmed on 10 October around 13:15 Europe/Vienna (Play displayed 13:14).
Skills rating adjustments retain controls, scroll and a pending specialization;
feedback stays on the pressed button. Required Core previews and explicit saving
remain. Focused managed checks, native specialization/save/new-process reopen,
local ARM64 build, original-key signing, independent keyless verification and
one Internal upload/readback passed. **Physical Play 176 installation, real tester
incident intake, faster cold startup and complete Creation/Career remain
unverified.** Version 176 is consumed; previous artifacts/evidence are retained.

Preview 175's [observed Internal availability](../play/evidence/preview175-internal-observation.md),
confirmed on 10 October at 12:38 Europe/Vienna. Creation now summarizes required
steps and removes duplicate completion warnings while keeping stage/budget links
and required guidance. The reviewed Core missing-draft optimization is consumed.
Focused managed checks and native display/navigation/new-process reopen passed;
four synthetic saved files and the diagnostic opt-out stayed unchanged. Local
ARM64 build, original-key signing, independent keyless verification and one
Internal upload/readback passed. **Physical Play 175 installation, real tester
incident intake, faster cold startup and complete Creation/Career remain
unverified.** Version 175 is consumed; previous artifacts/evidence are retained.

Preview 174's [observed Internal availability](../play/evidence/preview174-internal-observation.md),
confirmed on 10 October around 11:03 Europe/Vienna (Play displayed 11:03).
Inline attribute adjustments retain controls, expanded Karma options and scroll
position while updating values/budgets from a fresh Core preview. Focused managed
checks and native normal/Karma/Edge, explicit save and new-process reopen passed;
other runners and the diagnostic opt-out remained unchanged. Local ARM64 build,
original-key signing, independent keyless verification and one Internal
upload/readback passed. **Physical Play 174 installation, real tester-incident
capture, faster cold startup and complete Creation/Career remain unverified.**
Version 174 is consumed; previous evidence and rollback artifacts remain unchanged.

Preview 173's [observed Internal availability](../play/evidence/preview173-internal-observation.md),
confirmed on 10 October around 10:29 Europe/Vienna (Play displayed 10:28).
Priority/Sum-to-Ten hide unrelated inactive Foundation/Life Modules cards while
required, available and warned steps remain visible. Focused managed checks,
native Continue-to-Skills and new-process reopen passed with four workspace
files and saved settings unchanged. Local ARM64 build, original-key signing,
independent keyless verification and one Internal upload/readback passed.
**Physical Play 173 installation, real tester-incident capture, faster cold
startup and complete Creation/Career remain unverified.** Version173 is consumed;
previous evidence and rollback artifacts remain unchanged.

Preview 172's [observed Internal availability](../play/evidence/preview172-internal-observation.md),
confirmed on 10 October at 09:25 Europe/Vienna. Creation now offers one admitted
Continue action, compact Attribute budgets and inline Karma controls; technical
diagnostics live only in Settings as a native switch. Focused checks and native
points/Karma/save/new-process reopen passed, including retained opt-out. Exact
package intake includes Elf/Ork Priority/Sum-to-Ten finalization checks. Local
ARM64 build, original-key signing, independent keyless verification and one
Internal upload/readback passed. **Physical Play 172 installation, real tester
incident capture, faster cold startup and complete Creation/Career remain
unverified.** Slow restoration/System UI stalls remain recorded. Version172 is
consumed; previous evidence and rollback artifacts remain unchanged.

Preview 171's [observed Internal availability](../play/evidence/preview171-internal-observation.md),
confirmed on 10 October at 05:38 Europe/Vienna (Play displayed 05:38). Repeated
metatype catalog projections use fewer temporary allocations, with unchanged
rule results and saved bytes. Exact package/intake checks and native bounded
Human draft, Save and full saved-chapter/new-process smoke passed; four workspace
hashes and the diagnostic opt-out were unchanged. Local ARM64 build, original-key
signing, independent keyless verification and one Internal upload/readback passed.
**Physical Play 171 installation, real tester-incident capture, native startup
speedup and complete Creation/Career remain unverified.** Nonhuman admission/raw
blockers in the retained fixture and slow restoration remain open. Version171 is
consumed; prior evidence and rollback artifacts remain unchanged.

Preview 170's [observed Internal availability](../play/evidence/preview170-internal-observation.md),
confirmed on 10 October at 03:47 Europe/Vienna (Play displayed 03:47). Origin
digest calculation uses less temporary memory while preserving canonical and
saved bytes. Exact package/intake checks and native full saved-chapter/new-process
smoke passed; four workspace hashes and the saved diagnostic opt-out were
unchanged. Local ARM64 build, original-key signing, independent keyless
verification and one Internal upload/readback passed. **Physical Play 170
installation, real tester-incident capture, native startup-speed improvement and
complete Creation/Career remain unverified.** Emulator Launcher/System UI stalls
and slow startup are recorded limitations. Version170 is consumed; prior
evidence and rollback artifacts remain unchanged.

Preview 169's [observed Internal availability](../play/evidence/preview169-internal-observation.md),
confirmed on 10 October at 02:29 Europe/Vienna (Play displayed 02:29). Saved
Origin-history projections use fewer transient allocations with unchanged rules
and stored bytes. Exact package/intake tests and native x64 full saved-chapter/
new-process smoke passed; all four workspace and diagnostic preference hashes
were unchanged. Local ARM64 build, original-key signing, independent keyless
verification and one Internal upload/readback passed. Internal diagnostics retain
default-on behavior and saved opt-outs. **Physical Play 169 installation, real
tester-incident capture, native startup-speed improvement and complete Creation/
Career remain unverified.** Startup remains slow. Version169 is consumed;
prior evidence and rollback artifacts remain unchanged.

Preview 168's [observed Internal availability](../play/evidence/preview168-internal-observation.md),
confirmed on 10 October at 00:55 Europe/Vienna (Play displayed 00:54). Foundation
and related SR5 Creation overview reads share one owner-bound composition;
mutation and saved-state checks remain. Exact Core/UI package tests and Android
managed page checks passed. A pre-repin private x64 API 36 smoke preserved four
saved runners and diagnostic preference across a new process; it is not final
ARM64 execution or proof of faster startup. Local ARM64 build, original-key
signing, keyless verification and one Internal upload/readback passed.
Diagnostics retain default-on Internal behavior and existing opt-outs.
**Physical Play 168 installation, real tester-incident capture, startup-speed
improvement and complete Creation/Career remain unverified.** Version168 is
consumed; prior evidence and rollback artifacts are unchanged.

Preview 167's [observed Internal availability](../play/evidence/preview167-internal-observation.md),
confirmed on 9 October at 21:45 Europe/Vienna (Play displayed 21:44). Home now
offers localized same-page retry after a failed initial restore, with visible
loading feedback. Baseline/fixed managed page tests, native Release compile and
existing-runner/new-process API 36 smoke passed; all saved workspace hashes were
unchanged. Local ARM64 build, isolated original-key signing, independent keyless
verification and one Internal upload/readback passed. Diagnostics retain their
Internal default-on policy and existing opt-outs. **Injected startup failure is
managed-page evidence; native remote diagnostic delivery, physical Play 167
installation and complete Creation/Career remain unverified.** This is not a
startup-speed improvement or conclusive attribution of the original incident.
Prior evidence and rollback artifacts remain unchanged.

Preview 166's [observed Internal availability](../play/evidence/preview166-internal-observation.md),
confirmed on 9 October at 21:15 Europe/Vienna. A reproduced reader background
refresh lost during export cancellation is fixed without replaying provider work.
Baseline/fixed managed checks and a native full-chapter, picker-cancel and
new-process reopen smoke passed. Stale lock-authority metadata was reconciled
without runtime/lock changes; 35 focused contract tests passed. Local ARM64 build,
isolated original-key signing, keyless verification and one Internal upload/
readback passed. Internal diagnostics still default on; saved opt-outs stay off.
**The original tester incident is not conclusively attributed; live-linked native
polling, native remote diagnostic delivery, physical Play 166 installation and
complete Creation/Career remain unverified.** Startup remains slow. Prior
evidence and rollback artifacts are preserved.

Preview 165's [observed Internal availability](../play/evidence/preview165-internal-observation.md),
confirmed on 9 October at 20:23 Europe/Vienna. Internal technical diagnostics
default on only when no prior preference exists; saved opt-outs remain off.
The private Hub intake and bounded Telegram alert worker are deployed. Focused
managed checks, native preference/opt-out/restart checks, separate synthetic
HTTPS intake and a clearly labelled synthetic operator alert passed. Local
ARM64 build, isolated original-key signing, independent keyless verification and
one Internal upload/readback passed. **The reported reload defect is unresolved;
native remote report delivery, physical Play 165 installation and whole-app
Creation/Career coverage remain unverified.** Existing UI guidance/readability
changes are included without claiming exhaustive device coverage. Prior evidence
and rollback artifacts remain unchanged.

Preview 164's [observed Internal availability](../play/evidence/preview164-internal-observation.md),
confirmed on 9 October at 15:39 Europe/Vienna. The quality catalog has 803 German
display names and German/original-name search, with localized help/configuration
headings and unchanged rules. Twenty focused Python tests, managed locale checks
and a native German catalog/search/help/configure smoke passed; all 33 original
runner files remained unchanged. Local ARM64 build, original-key isolated signing,
keyless verification and one Internal upload/readback passed. **Whole-app German
coverage, complete Creation/Career and physical Play 164 installation remain
open.** Remaining New runner/prerequisite English labels and slow startup/System
UI emulator boot ANR are recorded honestly. Prior evidence remains unchanged.

Preview 163's [observed Internal availability](../play/evidence/preview163-internal-observation.md),
confirmed on 9 October at 14:47 Europe/Vienna. Exact canonical restoration,
save, settings-save and account-recovery status notices now use display-only
EN/DE/ES resources; unknown errors/custom notices remain intact. Managed locale
checks and native German restored-runner feedback smoke passed; all 33 retained
runner files were unchanged. Local build, isolated original-key signing,
independent keyless checks and one Internal upload/readback passed. **Whole-app
German coverage, physical Play 163 installation and complete Creation/Career
remain open; startup remains slow.** A retained System UI emulator boot ANR is
explicitly distinguished from the final successful app display. Prior evidence
remains unchanged.

Preview 162's [observed Internal availability](../play/evidence/preview162-internal-observation.md),
confirmed on 9 October at 14:22 Europe/Vienna. German Magic/Resonance names,
talent/metatype/category captions and historical re-review display are localized;
search accepts German and original names. Twenty source tests, focused managed
checks and final-source native read-only smoke passed; all 33 fixture files
stayed unchanged. Local build, isolated original-key signing, independent
keyless verification and one Internal upload/readback passed. **Whole-app German
coverage, remaining status/system/custom labels, physical Play 162 installation
and complete Creation/Career remain open; startup remains slow.** The earlier
unsigned diagnostic was excluded from signing. Previous evidence is unchanged.

Preview 161's [observed Internal availability](../play/evidence/preview161-internal-observation.md),
confirmed on 9 October at 13:43 Europe/Vienna. German skill/language names,
groups, categories and exactly matched specializations are now localized in
Creation Skills and historical review. Nineteen source tests, affected managed
checks and final-source native read-only smoke passed; all 33 fixture files were
unchanged. Local ARM64 build, isolated original-key signing, independent keyless
verification and one Internal upload/readback passed. **Whole-app localization,
some system/custom/fallback labels, physical Play161 installation and complete
Creation/Career remain open; startup remains slow.** Prior evidence is unchanged.

Preview 160's [observed Internal availability](../play/evidence/preview160-internal-observation.md),
confirmed on 9 October at 13:21 Europe/Vienna. Skills blocker and historical
re-review guidance is localized; exact reason codes, IDs, source anchors and
receipt/binding details are behind optional collapsed diagnostics. Fourteen
locale source tests, affected managed runtime checks and final-source native
German disclosure/re-review smoke passed; all 33 fixture files were unchanged.
The local ARM64 build, isolated existing-key signature, independent keyless
verification and one Internal upload/readback passed. **English catalog names,
physical Play 160 installation and complete Creation/Career remain open;
startup remains slow.** No whole-app localization or speedup claim is made.
Previous artifacts and evidence are unchanged.

Preview 159's [observed Internal availability](../play/evidence/preview159-internal-observation.md),
confirmed on 9 October at 12:52 Europe/Vienna. Skills editor, confirmation and
historical re-review budget headings and linked attributes use localized display
labels; numeric formatting follows the independent region. Thirteen source
tests, managed locale checks and final-source native German historical re-review
passed; all 33 fixture runner files remained byte-identical. One final-source
local ARM64 build, isolated existing-key signature, independent keyless checks
and one Internal upload/readback passed. **Remaining English catalog labels,
technical source references, physical Play 159 installation and complete
Creation/Career remain open; startup remains slow.** No whole-app localization
or speedup claim is made. Previous artifacts and evidence are unchanged.

Preview 158's [observed Internal availability](../play/evidence/preview158-internal-observation.md),
confirmed on 9 October at 12:26 Europe/Vienna. Creation stages, budgets, status
and route copy and the Attribute editor/review headings and units now use
localized display resources. Focused source/managed checks and final-source
German native display/navigation smoke passed; all 33 fixture runner files
remained byte-identical. One local ARM64 build, isolated existing-key signature,
independent keyless checks and one Internal upload/readback passed.
**Complete German app/catalog localization, physical Play 158 installation and
complete Creation/Career remain open; startup remains slow.** No full-app or
speedup claim is made. Previous artifacts and evidence are unchanged.

Preview 157's [observed Internal availability](../play/evidence/preview157-internal-observation.md),
confirmed on 9 October at 11:20 Europe/Vienna. Settings now offers independent
app-language and regional-format choices with a live preview. Save persists the
choices; restart applies them. Back without saving discards them. Book language,
rules and runner data are unchanged. Focused managed checks and final-source
native cancel/save/German-Austria/new-process restore passed; all 33 fixture
runner files stayed byte-identical. One local ARM64 build, isolated existing-key
signature, independent keyless checks and one Internal upload/readback passed.
**Physical Play 157 installation and complete Creation/Career remain open;
startup remains slow.** No finished-app or native speedup claim is made.
Previous artifacts and evidence are unchanged.

Preview 156's [observed Internal availability](../play/evidence/preview156-internal-observation.md),
confirmed on 9 October at 10:21 Europe/Vienna. Ordinary saved-runner reads reuse
successful exact-byte historical validation while ownership, revisions, leases
and mutation checks remain fresh. Exact package/managed checks and final-source
native normal-point navigation, Body 3 to 4, explicit save and new-process restore
passed. Revision 12/12 became 13/13; only the selected JSON changed, and all 33
saved/reopened files matched. The smoke used a separate SDK-key package with
copied synthetic fixtures, not an in-place upgrade. One local ARM64 build,
isolated existing-key signature, independent keyless checks and one Internal
upload/readback passed. Existing full-chapter and inline-image coverage is reused.
**Startup remains slow; physical Play 156 installation and complete Creation/
Career coverage remain open.** No native speedup or finished-app claim is made.
Previous artifacts and evidence are unchanged.

Preview 155's [observed Internal availability](../play/evidence/preview155-internal-observation.md),
confirmed on 9 October at 08:23 Europe/Vienna. Creation overview reads reuse one
owner-bound observation and operation-local source context; mutation admission
stays fresh. Exact package/managed checks and final-source native normal-point
navigation, Body 2 to 3, explicit save and new-process restore passed. The draft
advanced from revision 11/11 to 12/12; XML and other 32 files stayed unchanged,
and all 33 saved/reopened files matched. One local ARM64 build, isolated existing-key
signature, independent keyless checks and one Internal upload/readback passed.
Full chapters and per-chapter images remain in the app reader and EPUB.
**Startup remains slow; physical Play 155 installation and complete Creation/
Career coverage remain open.** No native speedup or finished-app claim is made.
Previous artifacts and evidence are unchanged.

Preview 154's [observed Internal availability](../play/evidence/preview154-internal-observation.md),
confirmed on 9 October at 06:07 Europe/Vienna. Canonical Magic catalog checks
use bounded temporary sorting allocations without changing rule bytes, saved
formats or admission. Exact package/managed checks and final-source native
Magic rereview, explicit save and new-process restore passed. Saved choices,
the earlier receipt and all 33 saved/reopened files were preserved; the selected
draft advanced to revision 11/11. One local ARM64 build, isolated existing-key
signature, independent keyless checks and one Internal upload/readback passed.
Full chapters and per-chapter images remain in the app reader and EPUB.
**Startup remains slow; physical Play 154 installation and complete Creation/
Career coverage remain open.** Emulator resource limits changed during the smoke,
so no native speedup claim is made. Previous artifacts/evidence are unchanged.

Preview 153's [observed Internal availability](../play/evidence/preview153-internal-observation.md),
confirmed on 9 October at 04:42 Europe/Vienna. Creation heritage projection
reuses parsed source fields within one operation, retaining canonical values,
detached results and owner/source checks. Exact package/managed checks and the
final-source native normal-attribute jump, Body +1, explicit save and new-process
reopen passed. The selected draft persisted at revision 10/10; its XML and the
other 32 workspace files stayed unchanged. One local ARM64 build, isolated
existing-key signature, independent keyless checks and one Internal upload/
readback passed. Full chapters and chapter images remain in the reader and EPUB.
**Startup remains slow; physical Play 153 installation and complete Creation/
Career coverage remain open.** No native speedup or finished-app claim is made.
Previous artifacts and evidence are unchanged.

Preview 152's [observed Internal availability](../play/evidence/preview152-internal-observation.md),
confirmed on 9 October at 02:54 Europe/Vienna. Synchronous Contacts reads reuse
source construction while retaining fresh source admission, detached results and
owner/revision checks. Exact package/managed checks and the final-source native
update/Contacts read/new-process smoke passed; all 33 saved files stayed unchanged.
One local ARM64 build, isolated existing-key signature, independent keyless checks
and one Internal upload/readback passed. Full chapters and chapter images remain
in the reader and EPUB. **Startup remains slow; physical Play 152 installation
and complete Creation/Career coverage remain open.** No native speedup or
finished-app claim is made. Previous artifacts and evidence are unchanged.

Preview 151's [observed Internal availability](../play/evidence/preview151-internal-observation.md),
confirmed on 9 October at 01:12 Europe/Vienna. Repeated Creation prerequisite
reads avoid full XML copies while retaining fresh source validation, detached
values and owner/revision checks. Exact package/managed checks and the final-source
11-runner native update/read/new-process smoke passed; all 33 files stayed
unchanged. One local ARM64 build, isolated existing-key signature, independent
keyless checks and one Internal upload/readback passed. Full chapters and chapter
images remain in the reader and EPUB. **Startup remains slow; physical Play 151
installation and complete Creation/Career coverage remain open.** No native
speedup or finished-app claim is made. Earlier artifacts and evidence are unchanged.

Preview 150's [observed Internal availability](../play/evidence/preview150-internal-observation.md),
confirmed on 8 October at 23:23 Europe/Vienna. Saved lifestyle integrity checks
allocate less temporary memory without changing rules, canonical digests or
owner/revision validation. Core/UI package checks, affected managed checks and
the retained 11-runner native update/read/restart smoke passed; all 33 files stayed
unchanged. The native APK predates the final metadata repin; exact final ARM64
build, isolated existing-key signing and independent keyless verification are
separate evidence. One Internal upload/readback passed. Full chapters and
per-chapter images remain in the reader and EPUB. **Startup is still slow;
physical Play 150 installation and complete Creation/Career remain open.**
This is not a general performance or finished-app claim. Earlier artifacts and
evidence are unchanged.

Preview 149's [observed Internal availability](../play/evidence/preview149-internal-observation.md),
confirmed on 8 October at 21:28 Europe/Vienna. Creation Review and Continue
cards open the existing guarded finalization route when ready and explain
blocked/loading states correctly. Focused actual-MAUI/Core checks and native
update/Review-to-cash smoke passed; all33 saved files stayed unchanged. One
local ARM64 build, isolated existing-key signing, independent keyless checks
and one Internal upload/readback passed. Per-chapter images and full text remain
in the app reader and EPUB. **Physical Play149 installation, complete
Creation/Career and general startup reliability remain open.** Earlier artifacts
and evidence are unchanged.

Preview 148's [observed Internal availability](../play/evidence/preview148-internal-observation.md),
confirmed on 8 October at 19:57 Europe/Vienna. Home distinguishes Open runner
for Career from Continue building for Creation. Focused managed EN/DE/ES checks
and native update/Home-to-Career smoke passed; all27 saved files/nine synthetic
runners stayed unchanged. One local ARM64 build, isolated existing-key signing,
independent keyless verification and one Internal upload/readback passed.
Per-chapter images and full text remain in the app reader and EPUB. **Physical
Play148 installation, complete Creation/Career and general startup reliability
remain open.** Earlier artifacts and evidence are unchanged.

Preview 147's [observed Internal availability](../play/evidence/preview147-internal-observation.md),
confirmed on 8 October at 19:28 Europe/Vienna. Home/picker preserve runner names;
Career hides opaque profile IDs; Life Modules has readable selection/dice/budget
guidance. Focused managed controls and native update/runner-switch smoke passed;
all27 saved files/nine synthetic runners remained unchanged. One successful local
ARM64 build, isolated existing-key signing, independent keyless verification and
one Internal upload/readback passed. Two pre-compilation script invocation
failures are retained, not counted as successful builds. Per-chapter images and
full text remain in the app reader and EPUB. **Physical Play147 installation,
complete Creation/Career and general startup reliability remain open.**

Preview 146's [observed Internal availability](../play/evidence/preview146-internal-observation.md),
confirmed on 8 October at 18:02 Europe/Vienna. Saved-character integrity checks
use fewer temporary allocations without changing canonical digests or admission.
Focused package/managed checks and actual API36 update/new-process Career reopen
passed; all24saved files/eight synthetic runners stayed byte-identical. One local
ARM64 build, isolated existing-key signature, independent keyless verification
and Internal upload/readback passed. Per-chapter app/EPUB illustrations remain.
**Physical Play146 installation, complete Creation/Career and general startup
reliability remain open.** Earlier artifacts and evidence are unchanged.

Preview 145's [observed Internal availability](../play/evidence/preview145-internal-observation.md),
confirmed on 8 October at 14:44 Europe/Vienna. Unsaved-switch feedback stays
above the roster scroller, with readable EN/DE/ES copy and a guarded return to
the current runner; the editor no longer repeats that notice with a workspace ID.
Focused managed regression and final API36 feedback/return smoke passed; all
seven saved runner files remained unchanged. One local ARM64 build, isolated
existing-key signature, independent keyless checks and Internal upload/readback
passed. Per-chapter app/EPUB illustrations remain included. **Physical Play145
installation, complete Creation/Career and general startup reliability remain
open.** Preview144 and earlier artifacts/evidence are unchanged.

Preview 144's [observed Internal availability](../play/evidence/preview144-internal-observation.md),
confirmed on 8 October at 13:41 Europe/Vienna. Karma's unsupported magic options
have readable explanations; Career review names Mystic Adept power points.
Focused managed regression and native Adept-catalog copy smoke passed; all six
previous runner files stayed byte-identical. The native144 smoke did not repeat
Career finalization. Local ARM64 build, isolated existing-key signing,
independent keyless checks and one Internal upload/readback passed. Per-chapter
app/EPUB images remain included. **Physical Play144 installation, complete
Creation/Career and general startup reliability remain open.** Prior evidence
and artifacts are unchanged.

Preview 143's [observed Internal availability](../play/evidence/preview143-internal-observation.md),
confirmed on 8 October at 12:33 Europe/Vienna. Karma Resources no longer shows a
raw funding expression, and unsupported equipment has a readable explanation.
The focused managed regression and actual API36 Resources/Equipment smoke
passed; all five saved runners stayed byte-identical. Local ARM64 build,
separate existing-key signing, independent keyless verification and one Internal
upload/readback passed. Per-chapter app/EPUB illustrations remain included.
**Physical Play 143 installation, complete Creation/Career and general startup
reliability remain open.** Earlier artifacts and evidence are unchanged.

Preview 142's [observed Internal availability](../play/evidence/preview142-internal-observation.md),
confirmed on 8 October at 11:33 Europe/Vienna. Karma creation has one heading,
shorter guidance and an explicit Metatype-first instruction. Focused managed
prerequisite/Save/reopen checks and an actual API36 dashboard/overview/catalog
smoke passed; all four saved runners stayed byte-identical. Local ARM64 build,
separate existing-key signing, independent keyless verification and one Internal
upload/readback passed. Per-chapter app/EPUB illustrations remain included.
**Physical Play 142 installation, complete Creation/Career and general startup
reliability remain open.** Earlier artifacts and evidence are unchanged.

Preview 141's [observed Internal availability](../play/evidence/preview141-internal-observation.md),
confirmed on 8 October at 09:49 Europe/Vienna. Karma Open/Load use cancellable
background read admission while local account hydration owns the read gate.
Focused actual-account contention/cancellation and owner tests passed; actual
API36 Save/force-stop/new-process reopen retained all four saved runner files.
Local ARM64 build, isolated existing-key signature, independent keyless checks
and one Internal upload/readback passed. Per-chapter app/EPUB illustrations
remain included. **Physical Play 141 installation, complete Creation/Career and
general startup reliability/performance remain open.** Earlier evidence is unchanged.

Preview 140's [observed Internal availability](../play/evidence/preview140-internal-observation.md),
confirmed on 8 October at 09:11 Europe/Vienna. Karma Creation refreshes its exact
saved-revision dashboard binding after toolbar Save. Focused managed failure/
recovery checks and actual API36 Save/force-stop/new-process reopen passed. Local
ARM64 build, separate existing-key signature, independent keyless checks and one
Internal upload/readback passed. Existing per-chapter app/EPUB illustrations remain
included. **Physical Play 140 installation, complete Creation/Career and startup
reliability/performance remain open.** Preview 139 and earlier evidence are unchanged.

Preview 139's [observed Internal availability](../play/evidence/preview139-internal-observation.md),
confirmed on 8 October at 08:12 Europe/Vienna. The sealed UI graph skips unrelated
Priority preparation during pending Karma/Life Modules restoration. The existing
app reader's matching chapter illustrations, EPUB image bytes and offline reopen
passed explicit four-chapter coverage on the final graph. Focused managed/native
checks and the unchanged product's actual API 36 update/new-process restoration
passed with four saved runner files unchanged. Local ARM64 build, separate
existing-key signature, independent keyless checks and one Internal upload/readback
passed. **Physical Play 139 installation, complete Creation/Career and responsive
cold startup remain open.** Preview 138 and all earlier evidence are unchanged.

Preview 138's [observed Internal availability](../play/evidence/preview138-internal-observation.md),
confirmed on 8 October at 06:36 Europe/Vienna. The new sealed Core/UI graph avoids
duplicate reads while loading the saved-runner roster and adds generated JSON
metadata. Focused managed/native checks and an actual API 36 in-place update/
new-process restore passed; all four saved runner files stayed byte-identical.
Local ARM64 build, isolated existing-key signing, independent keyless inspection
and one Internal upload/readback passed. **Physical Play 138 installation,
complete Creation/Career and responsive cold startup remain open.** The emulator
still exhibited slow startup; no broad speed or finished-app claim is made.
Earlier artifacts and evidence remain unchanged.

Preview 137's [observed Internal availability](../play/evidence/preview137-internal-observation.md),
confirmed on 8 October at 03:51 Europe/Vienna. Karma Attributes adds a top
Refresh preview and compact read-only rating/cost cards without losing the
ability to remove invalid purchases. Focused managed/native checks and the
actual API 36 Human/Mundane attribute route passed; three existing saved runner
files stayed unchanged. The unchanged Core/UI graph was built locally for ARM64,
signed with the existing isolated upload key and independently verified before
one Internal upload. **Physical Play 137 installation, full Creation/Career
coverage and native startup responsiveness remain open.** Earlier evidence is
unchanged; the prior complete Karma restart smoke is not a rerun of this delta.

Preview 136's [observed Internal availability](../play/evidence/preview136-internal-observation.md),
confirmed on 8 October at 03:08 Europe/Vienna. Career entry uses shorter player
guidance and collapses exact technical IDs/revisions/digests by default. All six
destinations and Experimental warnings remain. Focused managed/native checks and
the actual API 36 navigation/disclosure smoke passed with unchanged saved runner
bytes. The unchanged Core/UI graph was built locally for ARM64, signed with the
existing isolated upload key and independently verified before one Internal
upload. **Physical Play 136 installation, complete Creation/Career coverage and
native responsiveness remain open.** Earlier artifacts/evidence are unchanged.

Preview 135's [observed Internal availability](../play/evidence/preview135-internal-observation.md),
confirmed on 8 October at 02:45 Europe/Vienna. Creation Skills puts the first
native-language picker and Review above the budget/catalog content. Focused
managed/native checks and the actual API 36 minimal Priority route passed through
one Skills save, one Career finalization and new-process reopening with identical
saved bytes. The unchanged Core/UI graph was built locally for ARM64, signed with
the existing isolated upload key and independently verified before one Internal
upload. **Physical Play 135 installation, complete Creation coverage and native
responsiveness remain open.** Earlier artifacts and evidence remain unchanged.

Preview 134's [observed Internal availability](../play/evidence/preview134-internal-observation.md),
confirmed on 8 October at 02:16 Europe/Vienna. Creation Gear shows only
Core-eligible equipment with exact supported pricing/availability. Focused
managed/native checks and the actual API 36 catalog/search/save/new-process
reopen route passed, with identical saved workspace bytes. The unchanged Core/UI
graph was built locally for ARM64, signed with the existing isolated upload key
and independently verified before one Internal upload. **Physical Play 134
installation, complete Creation and native responsiveness remain open.**
Earlier artifacts and evidence remain unchanged.

Preview 133's [observed Internal availability](../play/evidence/preview133-internal-observation.md),
confirmed on 8 October at 01:48 Europe/Vienna. Creation opens Gear directly after
saved Resources, with current-runner and stale-action checks. Focused managed,
native and actual API 36 navigation/save/restart checks passed with identical
saved workspace bytes. The unchanged Core/UI graph was built locally for ARM64,
signed with the existing isolated upload key and independently verified before
one Internal upload. **Physical Play 133 installation, complete Creation and
native startup responsiveness remain open.** Earlier artifacts are unchanged.

Preview 132's [observed Internal availability](../play/evidence/preview132-internal-observation.md),
confirmed on 8 October at 01:26 Europe/Vienna. Irrelevant exact-zero Mundane
magic budgets are hidden; Qualities saving immediately shows readable progress
and prevents duplicate taps. Focused managed/native checks and a real API 36
Priority draft save/restart/receipt reopen passed with identical workspace bytes.
The unchanged Core/UI graph was built locally for ARM64, signed with the
existing isolated upload key and independently verified before one upload.
**Physical Play 132 installation, complete Creation and native startup
responsiveness remain open.** Preview 131 and earlier artifacts are unchanged.

Preview 131's [observed Internal availability](../play/evidence/preview131-internal-observation.md),
confirmed on 8 October at 00:28 Europe/Vienna (released at 00:27). Creation
readiness avoids loading qualities, magic and gear catalogs for absent drafts;
saved choices still reload fresh authority and required missing steps still
block finalization. Exact sealed-graph native update/new-process reopening
retained identical saved runner bytes. Local ARM64 inspection, isolated
existing-key signing and independent signature/certificate/payload checks passed
before one Internal upload. **Physical Play131 installation, native startup
responsiveness and complete SR5 Creation remain open.** Restoration13.523s is
still slow; no controlled native speedup is claimed. Preview130 and earlier
artifacts/evidence remain unchanged.

Preview 130's [observed Internal availability](../play/evidence/preview130-internal-observation.md),
confirmed on 7 October at 22:10 Europe/Vienna. Core reuses private XML buffers
inside a quality catalog projection, with unchanged rule results and owner
admission. Exact sealed-graph native upgrade/new-process reopening retained
saved data. Local ARM64 inspection, isolated existing-key signing and independent
signature/certificate/payload verification passed before one Internal upload.
**Physical Play130 installation, native startup responsiveness and complete SR5
Creation remain open.** Final process restoration still took12.521s; the40.6%
managed allocation improvement is not a controlled native speedup. Boot SystemUI
and null-root observations are retained. Previous129 artifacts remain unchanged.

Preview 129's [observed Internal availability](../play/evidence/preview129-internal-observation.md),
confirmed on 7 October at 19:56 Europe/Vienna. Core reuses temporary XML buffers
inside one Creation prerequisite projection, with unchanged rule results and
fresh owner admission. The exact package graph passed focused managed checks and
API 36 in-place upgrade/new-process reopening with unchanged saved workspace bytes.
The local ARM64 AAB passed unsigned inspection, isolated existing-key signing
and independent signature/certificate/payload verification before one upload.
**Physical Play 129 installation, controlled native speedup and complete SR5
Creation remain open.** The separate SDK-test APK predates only version metadata
128-to-129 and evidence. Slow restoration and the boot SystemUI ANR remain
recorded. Preview 128 and earlier artifacts/evidence are unchanged.

Preview 128's [observed Internal availability](../play/evidence/preview128-internal-observation.md),
confirmed on 7 October at 17:30 Europe/Vienna. Core reuses temporary XML buffers
inside one Creation Magic catalog projection, with unchanged rules and catalog
digests. The corrected package graph passed focused managed checks and actual
API 36 saved Skills upgrade/new-process reopening; Magic Mundane entry rendered.
The local ARM64 AAB passed unsigned inspection, isolated existing-key signing
and independent signature/certificate/payload verification before one upload.
**Physical Play 128 installation, native speedup and complete SR5 Creation
remain open.** The separate SDK-test APK predates only version metadata
127-to-128 and evidence. Slow restoration, SystemUI ANR and failed null-root
observations remain recorded. Preview 127 and earlier artifacts are unchanged.

Preview 127's [observed Internal availability](../play/evidence/preview127-internal-observation.md),
confirmed on 7 October at 13:58 Europe/Vienna. Creation Skills review now shows
readable selections, costs and remaining points; technical digests/revisions are
collapsed by default, and save feedback distinguishes the draft from Career.
Focused managed checks and the actual API 36 review/disclosure/save/new-process
route passed, with unchanged saved bytes after restart. The local ARM64 AAB
passed unsigned inspection, isolated existing-key signing and independent
signature/certificate/payload verification before one upload. Core/UI inputs
are unchanged. **Physical Play 127 installation, native speedup and complete
SR5 Creation remain open.** The separate SDK-test APK predates only version
metadata 126-to-127 and evidence. SystemUI ANR and null/stale observations remain
recorded. Preview 126 and earlier artifacts/evidence are unchanged.

Preview 126's [observed Internal availability](../play/evidence/preview126-internal-observation.md),
confirmed on 7 October at 13:09 Europe/Vienna. The complete Skills specialization
picker replaces the former six-option limit and requires explicit preview.
Focused managed checks and actual API 36 selection, one confirmation and verified
new-process reopen passed; saved workspace bytes remained unchanged after restart.
The local ARM64 AAB passed unsigned inspection, isolated existing-key signing
and independent signature/certificate/payload verification before one upload.
Core/UI inputs are unchanged. **Physical Play 126 installation, native speedup
and complete SR5 Creation remain open.** The separate SDK-test APK predates only
version metadata 125-to-126 and evidence. Boot SystemUI ANR, null/stale observers
and the unchanged Priority markup test failure are recorded, not hidden.
Preview 125 and earlier artifacts/evidence remain unchanged.

Preview 125's [observed Internal availability](../play/evidence/preview125-internal-observation.md),
confirmed on 7 October at 12:22 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Core reuses detached Creation Skills snapshots and per-projection XML buffers
without weakening fresh validation or owner admission. Focused managed checks
and final sealed-graph SDK-test upgrade/read/reopen in a verified new process
passed; the saved Skills workspace stayed byte-identical at revision5/5.
The once-only mutation was checked earlier on the same Core runtime with an
earlier Presentation graph, not replayed in this final smoke. **Physical Play125,
native speedup and complete SR5 Creation remain open.** Restored workspace took
16.104s; boot SystemUI ANR and null hierarchy observations remain recorded.
Preview124 and earlier artifacts/evidence are unchanged.

Preview 124's [observed Internal availability](../play/evidence/preview124-internal-observation.md),
confirmed on 7 October at 09:58 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Core's detached Priority/Sum-to-Ten qualities/magic projections reuse work inside
one admitted context without weakening fresh validation. Focused managed tests
and actual API36 Sum-to-Ten Body2-to3, one Save and new-process reopen passed;
the exact saved workspace remained unchanged after restart. Qualities loaded.
The separate SDK-test APK predates only version metadata123-to124 and evidence;
ARM64 artifact checks cover that delta. **Physical Play124, native responsiveness
and complete SR5 Creation remain open.** Cold restore29.803s then14.566s is not
controlled speedup evidence. System ANRs and null observers remain recorded.
Preview123 and earlier artifacts/evidence are unchanged.

Preview 123's [observed Internal availability](../play/evidence/preview123-internal-observation.md),
confirmed on 7 October at 07:31 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
The converged Core/UI graph reduces redundant owner-bound Creation reads while
retaining fresh mutation admission. Managed ownership/startup checks and actual
saved Sum-to-Ten upgrade, VM reboot/new-process restoration and interactive
Priorities passed with unchanged workspace bytes. The separate SDK-test APK
predates only version metadata122->123; exact ARM64 artifact checks cover that
delta. **Physical Play123, native responsiveness and complete SR5 Creation
remain open.** Cold restore still took96.634s; system ANRs, observer failures and
the non-green broader source-test selection are recorded, not hidden.
Preview122 and earlier artifacts and evidence remain unchanged.

Preview 122's [observed Internal availability](../play/evidence/preview122-internal-observation.md),
confirmed on 7 October at 03:48 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Individual Priority/Sum-to-Ten ranks can be cleared without resetting every
category. Focused managed admission tests passed for both methods; actual
API36 Sum-to-Ten correction, one confirmation and verified new-process reopen
preserved the exact workspace bytes and unrelated selections. The separate
SDK-test APK predates only version metadata121->122; exact ARM64 artifact checks
cover that delta. **Physical Play122, slow native loading and complete SR5
Creation remain open.** Initial SystemUI ANR, observer failures and an unchanged
source-markup test failure are recorded; no exhaustive/native-speed claim.
Preview121 and earlier artifacts and evidence remain unchanged.

Preview 121's [observed Internal availability](../play/evidence/preview121-internal-observation.md),
confirmed on 7 October at 03:00 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Lightweight Life Modules story readiness no longer loads next-module narrative
opportunities; full-reader/authoring and owner/revision/prose checks remain.
Affected nonterminal dashboard, full synthetic chapter and verified new-process
reopening passed with all six original saved JSON files byte-identical.
The separate SDK-test APK predates only the two version metadata changes120→121;
the exact ARM64 build/artifact checks cover that delta. **Physical Play121,
slow native loading and all-method SR5 Creation remain open.** Initial system
crashes/ANRs and observer failures are retained; no new provider-book, native-speed
or exhaustive qualification claim. Preview120 and earlier evidence stays intact.

Preview 120's [observed Internal availability](../play/evidence/preview120-internal-observation.md),
confirmed on 7 October at 02:12 Europe/Vienna; Console release time is 02:11.
Its local ARM64 AAB was separately signed with the existing upload key and
independently verified. Core reuses the detached Life Modules method-rejection
projection without weakening Priority-editor rejection or live-source checks.
Focused saved completion/Qualities/Talent and verified new-process reopening
passed with unchanged runner/input/draft/reading/settings bytes. The native
SDK-test APK predates only the two version metadata changes from119 to120;
the exact ARM64 release build and artifact checks cover that delta.
**Physical Play120 installation, slow loading and all-method SR5 Creation remain
open.** Managed allocation reduction is not native latency/peak-memory proof.
Boot SystemUI ANR and loading-state observer failures remain documented;
this is not a fresh mutation, full Career or new generated-book test.
Preview119 and earlier artifacts/evidence remain unchanged.

Preview 119's [observed Internal availability](../play/evidence/preview119-internal-observation.md),
confirmed on 7 October at 00:28 Europe/Vienna. One exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Core avoids redundant hashing of freshly owned Origin projections while keeping
persisted/external and caller-owned state fully validated. Focused saved Life
Modules completion/Qualities/Talent navigation and verified new-process reopen
passed with unchanged runner/input/draft/reading/settings bytes. This is a narrow
read-path smoke, not a fresh mutation, full Career or generated-book proof.
**Physical Play119 installation, slow loading and all-method SR5 Creation remain
open.** Managed allocation reduction is not native latency or peak-memory proof;
initial system ANRs and one loading-state observer failure remain documented.
Preview118 and earlier artifacts/evidence remain unchanged.

Preview 118's [observed Internal availability](../play/evidence/preview118-internal-observation.md),
confirmed on 6 October at 22:47 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Core avoids repeated workspace reads inside each synchronous Qualities/Magic
load while preserving fresh later calls, owner admission and mutation invalidation.
Focused native saved Life Modules Qualities/Talent navigation and new-process
completion reopen passed with unchanged runner/input/reading/settings bytes.
This is a narrow read-path smoke, not fresh mutation, full Career or book proof.
**Physical Play118 installation, slow cold loading/rule checks and all-method
SR5 Creation remain open.** Managed allocation reduction is not a native latency
or peak-memory result. Previous artifacts and real illustrated-book evidence
remain intact; no new paid provider job was needed.

Preview 117's [observed Internal availability](../play/evidence/preview117-internal-observation.md),
confirmed on 6 October at 20:51 Europe/Vienna. The exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Core avoids duplicate historical Origin-turn processing within workspace reads;
owner, revision, source and receipt validation remain intact. Focused native
saved Life Modules completion and verified new-process reopen passed with
unchanged runner/input/synthetic-reading bytes. This is a narrow read-path
smoke, not a fresh mutation, full Career or generated-book proof.
**Physical Play117 installation, slow cold startup/rule checks and all-method
SR5 Creation remain open.** Reduced managed allocations are not proven native
latency or peak-memory improvement. Previous artifacts and illustrated-book
evidence remain intact; no new paid provider job was needed.

Preview 116's [observed Internal availability](../play/evidence/preview116-internal-observation.md),
confirmed on 6 October at 18:52 Europe/Vienna. The exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Core's private talent-catalog snapshot reduces repeated managed allocations;
live source admission, detached returns and permanent drift rejection remain.
Focused native Talent/quality-answer save and a verified new-process reopen
passed with unchanged input/workspace/synthetic-reading bytes and final-allocation
readiness restored. This is a narrow synthetic route, not a fresh real-book or
full Career proof. **Physical Play116 installation, slow cold startup/rule checks
and all-method SR5 Creation remain open.** Managed allocation measurements are
not native latency/peak-memory claims. Preview115 and its illustrated-book
evidence remain intact; no new paid provider job was needed for this increment.

Preview 115's [observed Internal availability](../play/evidence/preview115-internal-observation.md),
confirmed on 6 October at 16:36 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
The affected corporate Life Modules route now reaches Career; one finalization
receipt and the exact saved workspace survive verified process restart. The
full illustrated reader reopens with unchanged four-chapter prose/image storage
and the exact 2,494-word first chapter. Existing illustrated EPUB evidence is
retained, not presented as a new export on this APK. Core/UI package intake and
focused native/managed checks passed; version115 adds only metadata to the
native-tested behavior. **Physical Play115 installation and all-method SR5
Creation/Career remain open.** Slow saves/startup/navigation and broad stale
contract-test expectations are not cleared; no general beta or full-suite claim.

Preview 114's [observed Internal availability](../play/evidence/preview114-internal-observation.md),
confirmed on 6 October at 05:30 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Life Modules language questions exclude the technical metadata answer `0`.
Actual API36 SDK-test smoke verified the genuine choices, reviewed-answer reopen,
and confirmed nationality-to-childhood continuation after a verified new process.
The post-restart check proves stage continuation, not every saved answer byte.
The separately deployed standing Origin service delivered a new complete German
chapter, automatic 1min.ai illustration, illustrated EPUB and restart reread.
Earlier two-chapter reference-image evidence remains separately scoped.
**Physical Play114 installation and all-stage SR5 Creation/Career remain open.**
Slow saves/loading and provider-progress copy are not cleared by this release;
general beta and every-module illustration continuity are not claimed.

Preview 113's [observed Internal availability](../play/evidence/preview113-internal-observation.md),
confirmed on 6 October at 02:18 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Life Modules optional chapter refinements now allow outside-tap keyboard
dismissal. Actual API36 smoke verified both outside tap and collapsing questions
hide the keyboard, retain the Custom answer and leave the runner budget unchanged.
No module confirmation or paid generation occurred in this keyboard smoke.
Unchanged behavior checks are reused for the version-only delta.
Separately, a real two-chapter illustrated-book test preserved both full chapters
and embedded images through native EPUB export and a new-process reopen. The
later image used the opening image as a protagonist reference. This is a two-stage
sample, not all-age/all-module coverage. **Physical Play113 installation, general
permanent book-service admission and full SR5 Creation remain open.** Decision-save
latency and provider-progress copy remain follow-ups; general beta is not claimed.

Preview 112's [observed Internal availability](../play/evidence/preview112-internal-observation.md),
confirmed on 6 October at 00:27 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Automatic illustration admission now survives an initial status-read failure,
successful observation clears stale paused-reader copy, and account runner
loading avoids a redundant grant preflight while preserving owner checks.
Focused regressions and the actual API36 diagnostic route passed: a new full
2,494-word FirstBook chapter, automatic scene insertion, embedded-image EPUB
through the real save picker, and exact saved-book reopen in a new process.
Unchanged behavior evidence is reused for the documentation/version-only delta.
**Physical Play112 installation, unattended repeatability, multi-age visual
continuity and all-method SR5 Creation completion remain open.** Initial decision
save latency and generic provider-progress copy also remain follow-up findings.
This is not a complete book across all stages, whole-app parity or general beta.

Preview 111's [observed Internal availability](../play/evidence/preview111-internal-observation.md),
confirmed on 5 October at 21:23 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
The reader reconciles saved reading acknowledgement before automatic illustration;
unread editorial corrections can refresh without replacing already-read prose.
Focused managed checks and an actual API36 diagnostic route passed for a complete
2,181-word chapter, one real illustration, illustrated EPUB and new-process reopen.
Unchanged behavior checks are reused for the version-only delta. An initial Media
read timed out and recovered by read-only reopening of the persisted image, without
another paid render. **Physical Play111 installation, unattended repeatability,
multi-age visual continuity and all-method SR5 Creation completion remain open.**
This is not a complete book across all stages, whole-app parity or general beta.

Preview 110's [observed Internal availability](../play/evidence/preview110-internal-observation.md),
confirmed on 5 October at 16:14 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Explicit on-device adoption moves a local runner into the linked account;
cancellation preserves the original and interrupted placement can resume.
Focused package/managed checks and the actual API36 adoption/new-process route
passed with their documented boundaries. Native full paid prose was not present
in that diagnostic fixture; chapter/image/EPUB preservation is managed coverage.
**Physical Play110 installation, the real native complete-book route and all-method
SR5 Creation completion remain unverified.** This is not cloud runner upload,
whole-app parity or general-beta completion.

Preview 109's [observed Internal availability](../play/evidence/preview109-internal-observation.md),
confirmed on 5 October at 11:16 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Home loads runner workspaces independently of campaigns, completed empty lists
are explicit, and device unlink runs off the UI context. Focused managed and
actual real-account API36 empty-roster/unlink/new-process checks passed; unchanged
behavior evidence is reused for the version-only delta. **Physical Play109
installation, nonempty native recovery and full native Origin reading/EPUB
remain unverified.** This is not full SR5 Creation/Origin or general-beta completion.

Preview 108's [observed Internal availability](../play/evidence/preview108-internal-observation.md),
confirmed on 5 October at 08:53 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
New-book language selection and optional next-module chapter wishes are included.
Focused managed and synthetic API36 UI/save/new-process checks passed; unchanged
results are reused for the version-only delta. Questions use local stage-aware
suggestions and Custom, without changing rules or existing paid source identities.
**Physical Play108 installation/update, newly generated prose quality and live
native account/provider checks remain unverified.** Full SR5 Creation/Origin,
general performance and beta completion are not established.

Preview 107's [observed Internal availability](../play/evidence/preview107-internal-observation.md),
confirmed on 5 October at 06:57 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Account-response headers and content now share one original deadline; buffered
responses cannot renew an expired budget. Focused HTTP/security checks and a
synthetic API36 timeout/linked-state/new-process smoke passed. Unchanged behavior
results are reused for the version-only delta. **Physical Play107 installation/
update and successful native live-account/provider checks remain unverified.**
This is not a diagnosis of every historical account hang, overall performance
clearance, complete SR5 Creation/Origin or general-beta readiness.

Preview 106's [observed Internal availability](../play/evidence/preview106-internal-observation.md),
confirmed on 5 October at 02:33 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
An admitted initial account check now leaves Loading on caller cancellation;
queued callers, stored account data, owner identity and admitted credential
writes remain protected. Focused account/security/localization checks, five
actual-MAUI/Core cases and synthetic API36 linked cancellation/new-process reopen
passed. Initial-loading fault injection is managed only. Unchanged behavior
checks are reused for the version-only delta. **Physical Play106 installation/
update and successful live account/provider checks remain unverified.** This
does not diagnose every historical account hang or establish full SR5/Origin,
performance clearance, tablet parity or general-beta completion.

Preview 105's [observed Internal availability](../play/evidence/preview105-internal-observation.md),
confirmed on 5 October at 00:19 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Life Modules completion now blocks allocations when retained story history is
unread or cannot be verified; explicitly verified legacy absence remains allowed.
Nine focused actual-MAUI/Core cases and a synthetic API36 unread/read/new-process
smoke passed. Unchanged behavior results are reused for the version-only delta;
native failure injection is not claimed. **Physical Play105 installation/update
and live account/provider checks remain unverified.** This is not full SR5/Origin,
performance clearance or general-beta completion. The historical Hub503 cause
and the read-first completion-card subtitle remain separate open work.

Preview 104's [observed Internal availability](../play/evidence/preview104-internal-observation.md),
confirmed on 4 October at 23:35 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
An unexpected Origin status-read failure now stops the pinned spinner and shows
the pause without rebuilding saved prose or export controls; explicit recovery
does not replay paid generation. Focused actual-MAUI/Core checks and synthetic
API36 complete-chapter, explicit-read, new-process and DocumentsUI EPUB/HTML
checks passed. Unchanged behavior results are reused for the version-only delta.
**Physical Play104 installation/update and live account/provider checks are
unverified.** This is not full SR5/Origin, performance clearance or general-beta
completion. The historical Hub503 cause remains unresolved.

Preview 103's [observed Internal availability](../play/evidence/preview103-internal-observation.md),
confirmed on 4 October at 22:31 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Late native action results no longer open dialogs/errors after leaving a page,
including away-and-back. Admitted work still finishes; current-page errors remain.
Focused actual-MAUI tests and a synthetic API36 lifecycle/new-process diagnostic
passed; unchanged behavior results are reused for the version-only delta.
**Physical Play103 installation/update and live account refresh are unverified.**
This is not real runner-save authority, historical Hub503 diagnosis, performance
clearance, full SR5/Origin or general-beta completion.

Preview 102's [observed Internal availability](../play/evidence/preview102-internal-observation.md),
confirmed on 4 October at 21:36 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Life Modules distinguishes specialization removal from removing all added skill
choices, with wrapping English/German actions and explicit module-grant retention
help. Focused actual-MAUI/Core and synthetic API36 removal/save/new-process
cold-reopen checks passed; unchanged behavior results are reused for the
version-only delta. **Physical Play102 installation/update and live account
refresh are unverified.** This is not new native Career finalization, full
SR5/Origin, performance clearance or general-beta completion. The historical
Hub503 cause remains unresolved.

Preview 101's [observed Internal availability](../play/evidence/preview101-internal-observation.md),
confirmed on 4 October at 20:44 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Life Modules active-skill specializations no longer offer invalid knowledge-point
payment; knowledge-skill payment and active specialization removal remain.
Focused actual-MAUI/Core and synthetic API36 payment/save/new-process cold-reopen
checks passed; unchanged behavior results are reused for the version-only delta.
**Physical Play101 installation/update and live account refresh are unverified.**
This is not new native Career finalization, full SR5/Origin, performance clearance
or general-beta completion. The historical Hub503 cause remains unresolved.

Preview 100's [observed Internal availability](../play/evidence/preview100-internal-observation.md),
confirmed on 4 October at approximately 19:31 Europe/Vienna. Its exact local
ARM64 AAB was separately signed with the existing upload key and independently
verified. Early account-recovery reads and credential-queue waits now honor
cancellation; admitted credential commits still finish and real storage errors
remain fail-closed. Campaign uses the same visible Cancel action. Focused managed
MAUI/Core/security and synthetic API36 cancel/deadline/departure/new-process
checks passed; unchanged behavior results are reused for the version-only delta.
**Physical Play100 installation/update and live account refresh are unverified.**
This does not establish the cause of the historical Hub503, interruptibility of
a non-cooperative OS read, full SR5/Origin or general-beta completion.

Preview 99's [observed Internal availability](../play/evidence/preview99-internal-observation.md),
confirmed on 4 October at approximately 18:01 Europe/Vienna (release time 18:00).
Its exact local ARM64 AAB was separately signed with the existing upload key and
independently verified. Life Modules exotic skill variants now retain independent
allocations and readable multiline labels. Focused actual-MAUI/Core and synthetic
API36 saved-draft/cold-reopen/once-only-Career/process-restart checks passed;
unchanged runtime results are reused for the version-only delta. The existing
cancelable account-data loading protection remains included.
**Physical Play99 installation/update and live account refresh are unverified.**
Full SR5/Origin and general-beta completion remain open.

Preview 98's [observed Internal availability](../play/evidence/preview98-internal-observation.md),
confirmed on 4 October at approximately17:06 Europe/Vienna (release time17:05).
Its exact local ARM64 AAB was separately signed with the existing upload key and
independently verified. Karma completion displays exact readable names, translated
deltas and dice help; raw technical identities remain behind optional diagnostics.
Focused actual-MAUI/Core and synthetic API36 confirmation/Career/new-process
checks passed; unchanged runtime results are reused for the version-only delta.
**Physical Play98 installation/update and live account refresh are unverified.**
Full SR5/Origin and general-beta completion remain open.

Preview 97's [observed Internal availability](../play/evidence/preview97-internal-observation.md),
confirmed on 4 October at 16:02 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Account loading now remains cancelable, runs the whole read chain off the UI
context and uses one total read budget. Local runners and the last complete
catalog are retained on failed/canceled reads. Focused actual-MAUI synthetic
button/timeout/ownership/recovery tests passed; no Android-device smoke or live
authenticated refresh is claimed. **Physical Play97 installation/update is
unverified.** The observed Hub grant-authority503 cause remains unresolved.
Full SR5/Origin and general-beta completion are not established.

Preview 96's [observed Internal availability](../play/evidence/preview96-internal-observation.md),
confirmed on 4 October at 15:12 Europe/Vienna (release time 15:11). Its exact
local ARM64 AAB was separately signed with the existing upload key and independently
verified. Runner-list deletion now requires a named, local-only confirmation;
online copies, books and exports are kept. Focused managed ownership/cancellation
checks and synthetic API36 deletion/unchanged-other-runners/new-process checks
passed; unchanged behavior evidence is reused for the version-only increment.
**Physical Play96 installation/update is unverified.** This is not account deletion,
secure erasure, complete SR5/Origin qualification or general-beta readiness.

Preview 95's [observed Internal availability](../play/evidence/preview95-internal-observation.md),
confirmed on 4 October at 14:16 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
After explicit reading of a recovered Origin chapter, its already-confirmed
eligible successor now resumes automatically through the existing recovery flow.
Focused actual-MAUI delayed-acknowledgement/export/ownership checks and synthetic
API36 full-reader/read-to-next/new-process checks passed; unchanged behavior
results are reused for the version-only increment. **Physical Play95
installation/update is unverified.** No automatic reading acceptance or uncertain
paid-job replay was added. Full live Origin, all-method SR5 and general-beta
completion remain open.

Preview 94's [observed Internal availability](../play/evidence/preview94-internal-observation.md),
confirmed on 4 October at 13:33 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Origin EPUB/HTML exports retain their admitted snapshot if an illustration
finishes while Android Save As is open. Focused actual-MAUI race/ownership
checks and synthetic API36 real EPUB/HTML export/new-process checks passed;
unchanged behavior results are reused for the version-only increment.
**Physical Play94 installation/update is unverified.** No provider credits or
request replay were involved. Complete live Origin, all-method SR5 and
general-beta completion remain open.

Preview 93's [observed Internal availability](../play/evidence/preview93-internal-observation.md),
confirmed on 4 October at 12:49 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Explicitly confirming a finished Origin chapter no longer holds the reading,
export or Return controls behind the remote acknowledgement. Focused
actual-MAUI delayed-response and synthetic API36 read/next/new-process checks
passed; unchanged behavior results are reused for the version-only increment.
**Physical Play93 installation/update is unverified.** No automatic reader
acceptance or paid request replay was added. Complete live Origin, all-method
SR5 and general-beta completion remain open.

Preview 92's [observed Internal availability](../play/evidence/preview92-internal-observation.md),
confirmed on 4 October at 11:12 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Origin now retains a visible status action after bounded connection failures
before chapter admission. Focused actual-MAUI recovery/no-replay checks passed;
the changed action has not had a new device smoke. Existing full-text/export
and read-before-next guards remain. **Physical Play92 installation/update is
unverified.** Full Origin, all-method SR5 and general-beta completion remain open.

Preview 91's [observed Internal availability](../play/evidence/preview91-internal-observation.md),
confirmed on 4 October at 10:41 Europe/Vienna. The exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Origin opens available full prose first; EPUB/HTML require complete chapters.
Life Modules adds automatic chapter requests, visible progress, reading before
successor/rule effects, editable city reuse and contextual story presets.
Focused actual-MAUI and synthetic API36 reading/export/new-process checks passed.
**Physical Play91 installation/update remains unverified.** Version 90 remains
a separately uploaded inactive library artifact, not a completed rollout.
Slow transitions, all-method Creation and full Origin completion remain open.

Preview 89's [observed Internal availability](../play/evidence/preview89-internal-observation.md),
confirmed on 4 October at approximately 04:07 Europe/Vienna. Its exact local
ARM64 AAB was separately signed and independently verified. Startup now shows
loading feedback and reuses the already restored runner list, preserving owner
and stale-state checks. Focused actual-MAUI and synthetic API36 new-process
restoration passed. The [physical Play88→89 update](../play/evidence/preview89-physical-observation.md)
verified installed identity/signature, loading feedback and unchanged visible
Creation after a verified new-process restart. Slow transitions, all-method
Creation and full Origin completion remain open.

Preview 88's [observed Internal availability](../play/evidence/preview88-internal-observation.md),
confirmed on 4 October at approximately02:58 Europe/Vienna. Its exact local
ARM64 AAB was separately signed and independently verified. Saved Origin text
appears before pending chapter-status reads; EPUB stays usable during them.
Focused actual-MAUI and synthetic API36 reader/export/new-process reopen passed.
The [physical Play87→88 update](../play/evidence/preview88-physical-observation.md)
verified installed identity/signature and unchanged visible Creation after a
new-process restart. Physical Origin reader/export is not proven by that check.
Slow transitions, all-method Creation and full Origin completion remain open.

Preview 87's [observed Internal availability](../play/evidence/preview87-internal-observation.md),
confirmed on 4 October at approximately 01:32 Europe/Vienna. Its exact local
ARM64 AAB was separately signed and independently verified. Creation Qualities
removes repeated validation and retains unsaved selections during temporary
unavailability without relaxing fresh admission. Focused managed checks and a
synthetic API36 single-save/new-process receipt reopen passed. The
[physical Play86→87 update](../play/evidence/preview87-physical-observation.md)
verified installed identity/signature and unchanged read-only Create/Qualities
after process restart. Slow transitions, all-method Creation and full Origin
completion remain open.

Preview 86's [observed Internal availability](../play/evidence/preview86-internal-observation.md),
confirmed on 4 October at approximately 00:18 Europe/Vienna. Its exact local
ARM64 AAB was separately signed and independently verified. Karma creation now
shows bold actual Core attribute ratings and quoted ranges/costs; edits require
a fresh preview. Review hides reader-facing XML/GUID anchors. Focused managed
checks and a synthetic API36 save/new-process reopen passed within the explicitly
recorded per-APK scope. The [physical Play-managed85→86 update](../play/evidence/preview86-physical-observation.md)
verified installed identity/signature and unchanged existing Priority state
after process restart, without changing user choices. The changed Karma route
is synthetic-device evidence, not physical coverage of that existing runner.
Slow transitions, all-method Creation and full Origin completion remain open.

Preview 85's [observed Internal availability](../play/evidence/preview85-internal-observation.md),
confirmed on 3 October at approximately22:39 Europe/Vienna. Its exact local
ARM64 AAB was separately signed and independently verified. Core Qualities digest
processing now allocates less while preserving canonical bytes and all admission
guards. Focused managed checks and a synthetic API36 single-save/new-process
receipt reopen passed. The [physical Play-managed84→85 update](../play/evidence/preview85-physical-observation.md)
verified installed identity/signature, readable existing Quality help and
read-only Qualities after a verified process restart, without changing user
choices. Slow transitions, all-method Creation and full Origin completion remain open.

Preview 84's [observed Internal availability](../play/evidence/preview84-internal-observation.md),
confirmed on 3 October at 20:05 Europe/Vienna. The exact locally built ARM64 AAB
was separately signed with the existing upload key and independently verified.
Resources budget saves now retain their controls and show neutral progress;
ownership, admission and no-replay guards remain unchanged. Six actual Core/MAUI
cases, 20 focused Python checks and the affected synthetic API36 save/new-process
reopen passed. The [physical Play-managed83→84 update](../play/evidence/preview84-physical-observation.md)
also passed installed identity/signature, first launch and read-only Resources
navigation after a verified process restart, without changing user choices.
This is not a new physical Resources mutation test. Slow transitions
remain; no full SR5/Origin/tablet/general-beta completion claim.

Preview 83's [observed Internal availability](../play/evidence/preview83-internal-observation.md),
confirmed on 3 October at approximately19:06 Europe/Vienna (release time19:05).
Its exact locally built ARM64 AAB was separately signed and independently verified.
The [physical Play-managed82→83 update](../play/evidence/preview83-physical-observation.md)
verified the Play certificate, first launch and Contacts prerequisite navigation
after process restart without changing the user's choices. Contacts no longer
opens Gear before Resources is saved. This does not complete all SR5 methods,
Origin or phone beta; slow transitions remain. The later saving-feedback source
change is not included in83.

Preview 82's [observed Internal availability](../play/evidence/preview82-internal-observation.md),
confirmed on 3 October at approximately 17:59 Europe/Vienna (release time 17:58).
Its exact local ARM64 AAB was separately signed with the existing upload key and
independently verified. It includes Creation Contacts routing/readability, Gear
catalog stability and reduced repeated Qualities source-context construction.
Focused managed checks and affected synthetic API36 save/new-process receipt
readback passed; slow transitions remain. Its later physical Play update verified
installation/signature but exposed the premature Contacts→Gear route, corrected
and physically rechecked in83. Neither observation proves full SR5, Origin or
phone-beta completion.

Preview 81's [observed Internal availability](../play/evidence/preview81-internal-observation.md),
confirmed on 3 October at 11:28 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Invalid starting-cash rolls now retain editable input and show a readable error;
finalization review/receipt hide technical identifiers behind details while
preserving visible changes and costs. Focused managed checks and API36 x64
synthetic finalization/save/new-process receipt readback passed. Rules and
dependencies are unchanged. The normal physical Play80-to-81 update, expected
Play signing identity and existing-state process restart passed at font scale1.3;
see the separate [physical observation](../play/evidence/preview81-physical-observation.md).
The existing draft still requires Gear review/save, so changed finalization-route
execution remains diagnostic-only, not physical Play evidence. No draft was
mutated merely to enter that route.
No full SR5 Creation, general beta, tablet or Origin completion is claimed.

Preview 80's [observed Internal availability](../play/evidence/preview80-internal-observation.md) was
confirmed on 3 October at approximately 09:28 Europe/Vienna. Its exact local
ARM64 AAB was separately signed with the existing upload key and independently
verified. Creation Magic budget links now focus the corresponding editor section
once, including Spells and Mystic Adept power-point controls. Focused managed
checks and an API-36 x64 diagnostic smoke passed; rules and dependencies are
unchanged. Host restarts and System UI startup ANRs in that smoke are retained
limitations, not a general performance claim. The normal physical Play79-to-80
update, expected Play signature, Spells/Mystic power-point section focus at font
scale 1.3 and existing-state process restart passed. See the separate
[physical observation](../play/evidence/preview80-physical-observation.md).
No saved mutation, all-private-store comparison or full Creation claim is made.

Preview 79's [observed Internal availability](../play/evidence/preview79-internal-observation.md) was
confirmed on 3 October at 07:24 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
It adds 34 short original ritual/enchantment explanations, bringing EN/DE/ES
authored coverage to 803/803 Qualities and 361/363 spell/ritual/enchantment
entries. Two specialist explanations remain unverified. Rules and dependencies
are unchanged. The normal physical Play 78-to-79 update, expected Play signing
certificate, readable existing Quality/spell help at font scale 1.3 and
existing-state process restart passed. See the separate
[physical observation](../play/evidence/preview79-physical-observation.md).
This sample does not physically cover all 34 new explanations or a new saved
mutation. No legal clearance, complete effect execution, full-book or
public-beta readiness is claimed.

Preview 78's [observed Internal availability](../play/evidence/preview78-internal-observation.md) was
confirmed on 3 October at 05:41 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
Authored EN/DE/ES help now covers 803/803 Qualities and 327/363 spell/ritual
entries; 36 specialist explanations remain open. Rules and dependencies are
unchanged. The normal physical Play77-to-78 update, expected Play signing
certificate, readable existing help examples at font scale1.3 and existing-state
process restart passed. See the separate
[physical observation](../play/evidence/preview78-physical-observation.md).
The sample does not physically cover every new explanation or a new save
transaction. This is not legal clearance, complete effect execution, full-book
or public-beta readiness.

Preview 77's [observed Internal availability](../play/evidence/preview77-internal-observation.md) was
confirmed on 2 October at 22:12 Europe/Vienna. Its exact local ARM64 AAB was
separately signed with the existing upload key and independently verified.
It shortens 32 existing EN/DE/ES Quality explanations, preserving important
benefits, drawbacks and scopes without changing rules. The normal physical
Play76-to-77 update, installed base-APK signature, shortened Simsense Vertigo
help at font scale 1.3 and existing-state process restart passed.
See the separate [physical observation](../play/evidence/preview77-physical-observation.md);
no new saved mutation or exhaustive physical catalog check is claimed.
Authored coverage remains 563/803 Qualities
and 292 ordinary spells; 240 Quality and 71 ritual/enchantment prose gaps remain.
No legal clearance, full-book, all-method or public-beta completion is claimed.

Preview 76's [observed Internal availability](../play/evidence/preview76-internal-observation.md),
confirmed on 2 October at approximately 20:55 Europe/Vienna. Its exact locally built ARM64 AAB
was separately signed with the existing upload key and independently verified.
It adds 43 short original EN/DE/ES Quality summaries and shortens 57 existing ones.
Supporting effects remain folded while important warnings stay visible.
563/803 are authored; 240 Quality summaries and 71 ritual/enchantment summaries remain open.
Ordinary-spell coverage remains 292. The normal physical Play update75→76,
installed base-APK signature, shortened Juryrigger help and existing-state
process-restart observation passed.
See the separate [Preview 76 physical observation](../play/evidence/preview76-physical-observation.md).
No new saved mutation or exhaustive physical catalog check is claimed. Existing
audience and other release surfaces were unchanged. No copyright clearance,
full-book, all-method or public-beta completion is claimed.

Preview 75's [Internal availability](../play/evidence/preview75-internal-observation.md)
and [physical observation](../play/evidence/preview75-physical-observation.md)
remain unchanged historical evidence, including its effect-disclosure and special-attribute checks.

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

[Preview 89](../play/evidence/preview89-physical-observation.md) is the latest
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
