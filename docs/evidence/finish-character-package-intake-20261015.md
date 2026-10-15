# Finish-character package intake — 15 October 2026

## Exact local inputs

- Android pre-intake source: `a9ec2ce1727198cf7b2c42e380fe5ae9aa452551`.
- Presentation source: `e1d348400265156db01473016cf440ba581ad978`, tree
  `7fd17204c5add213bbd9af02e3331238930fcc1f`.
- Core recipe: `505bc12ec78b5d35d355b17529cfd77031c56ae0`; runtime source:
  `fa951b99cc4ac232a8b483fbece72028a067c7e1`.
- Passed UI consumer receipt SHA-256:
  `6923132a3aecd90ff2fe9a885b3ea38d6f53dd31a48195daa3406eb499234d17`.
- Passed local Android intake receipt SHA-256:
  `6c5f90f6112a57797e4f6214d19339a96796833105dffc6da96f0c3d7adf2721`.

Retained private packet: `finish-character-loadout-20261015.Z1IY5hZd`;
UI consumer `ui-verify-2`, producer cache `ui-attempt-4/package-cache`, and
Android compile/handler execution `android-intake-1`. These receipts retain their
actual producer identities; this later pin commit is not substituted into them.
Presentation's protected-publication seal uses a different commit with identical
tree and lock bytes. The Android pin deliberately names the actual local consumer.

## Checks completed

The exact UI consumer passed 1,057 full Product tests and its focused owner
subsets. The local Android intake completed Native.CompileCheck, native Android
Compile, and the actual InteractionTests `creation-manual-loadout-content-root`
`manual-both` route. Native compilation had no warnings or errors; interaction
test compilation retained four nullable warnings. This was a managed handler
test, not a device lifecycle proof.

The applied pins passed the existing package-authority verifier with the actual
UI checkout, passed receipt and all retained package/authority bytes. The
validator now binds the reviewed UI producer's configured test floor of 932,
not the earlier 834 or the observed total of 1,057. Focused negative tests reject
stale, partial and invented floors. Its 58 tests and 646 subtests passed.

The affected package/build/provenance/source-contract subset passed 224 tests
and 645 subtests. The exact dependency-graph assertion from
`test_android_contract.py` passed separately. Whitespace checks passed.

An exploratory run of the entire `test_android_contract.py` did not pass:
18 assertions outside the changed dependency-graph assertion failed, including
old navigation/icon expectations and legacy edit-source contracts. This is not
recorded as a green broad suite or exhaustive Android parity. No historical
hosted/device result was relabelled as current evidence.

## Delivery boundary

This intake changes package/content identities, locks and corresponding guards;
the 331 Core rule-data files are unchanged. New native Release/API-36 smoke,
fresh runner save/finalize/reopen, Preview239 AAB, isolated upload-key signing,
Play processing/readback and physical installation are still pending.
Preview238 remains the recorded Play Internal version. No public-release,
all-build-method, tablet, Windows or whole-product completion claim is made.
