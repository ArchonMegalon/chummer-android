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

Preview 29 is the latest [observed Internal availability](../play/evidence/preview29-internal-observation.md).
Its physical Play installation is unverified. Preview28 and earlier evidence
remain immutable. Neither this policy nor availability seals a new Android
package graph or retroactively claims hosted qualification. Protected source
integration and each candidate's actual build/test/sign/Play work remain separate.
