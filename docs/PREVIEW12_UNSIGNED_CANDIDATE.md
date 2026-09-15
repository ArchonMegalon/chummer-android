# Preview.12 unsigned candidate lanes

Preview.12 has two separate, dormant GitHub Actions lanes. They are manual
`workflow_dispatch` workflows and never run automatically:

1. `Preview.12 exact-main unsigned candidate producer`
2. `Preview.12 independent candidate rebuild verifier`

They run only after the API-36 ordered review-to-main Two-Green workflow has
emitted an exact eligibility receipt for the current `main` tree. Neither lane
contains a GitHub Environment, upload key, signing key, Play credential,
deployment step, Play upload step, or publication action.

## Authority chain

```text
review API-36 green
  + later exact-main API-36 green
  -> Two-Green eligibility (not publication authority)
  -> unsigned Preview.12 producer
  -> independent source rebuild and exact AAB-byte comparison
  -> signer-eligibility/v1 (still not signing authority)
```

The closed policy is
`eng/preview12-unsigned-candidate-authority.json`. It fixes:

- package `com.myexternalbrain.chummer`;
- version `0.1.0-preview.12`, code `12`;
- compile/target API 36 and minimum API 24;
- Release ARM64 AAB output;
- exact non-Android dependency commits from the Two-Green graph;
- .NET SDK 10.0.110, Java 17, Microsoft Android SDK 36.1.69,
  MAUI 10.0.20, and pinned bundletool 1.18.3 bytes;
- canonical Core content from `c06f22c…`, embedded from the separate content
  checkout and verified byte-for-byte inside `base/assets/chummer-content`;
- binary exclusion of every API-36 proof-only source/type/contract marker.

Both workflows require explicit numeric run/artifact IDs and explicit SHA
pins. They authenticate the selected upstream Actions run and artifact via the
read-only repository token; a “latest successful run” lookup is never used.
The checked-out commit and tree must match the Two-Green receipt exactly.

## Producer output

The first workflow rebuilds the exact `main` source and dependencies, produces
an unsigned AAB, normalizes unsigned ZIP metadata deterministically, validates
it with the pinned public bundletool, checks package/version/SDK/privacy
structure, scans its managed assembly stores for proof instrumentation, and
emits:

```text
PREVIEW12_UNSIGNED_CANDIDATE.generated.json
PREVIEW12_TOOLCHAIN.generated.json
ANDROID_API36_TWO_GREEN_ELIGIBILITY.generated.json
chummer-android-0.1.0-preview.12-unsigned.aab
SHA256SUMS
```

The producer receipt deliberately says:

```text
signerEligible = false
signingAuthorized = false
googlePlayUploadAuthorized = false
publicationAuthorized = false
```

## Independent verifier output

The second workflow authenticates one exact producer run and artifact, checks
every producer sidecar, checks out the same exact source graph, restores the
toolchain independently, and builds a second normalized AAB. It fails unless
the producer and rebuild agree on:

- Android commit and tree;
- release identity;
- Two-Green eligibility digest;
- dependency-graph digest;
- toolchain-compatibility digest;
- normalized AAB SHA-256 and byte size;
- manifest identity;
- binary proof-exclusion result.

Only after that comparison does it write
`PREVIEW12_SIGNER_ELIGIBILITY.generated.json`. `signerEligible = true` means
only that a separately governed external signer may consider those exact
unsigned bytes as input. The receipt continues to set signing, Play upload,
and publication authority to `false`.

## Operator sequence

Dispatch the producer on `main` with the exact `main` SHA, Two-Green workflow
run ID, Two-Green artifact ID, and the SHA-256 of the extracted Two-Green JSON.
After it succeeds, dispatch the independent verifier with the same main SHA,
the exact producer run/artifact IDs, the GitHub artifact digest (including its
`sha256:` prefix), and the extracted producer-receipt SHA-256.

The resulting artifact is a temporary unsigned handoff. It is not a Play
release receipt and must not update release notes, public-guide truth, tester
availability, or current deployed-version records.
