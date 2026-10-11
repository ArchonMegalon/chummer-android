# Resources → Gear

The Creation wizard advances directly to Gear after a verified Resources save.
It removes the consumed review page from Back navigation. Ordinary Gear entry
still loads fresh authority; only the exact first forwarded appearance can
reuse the coordinator's admitted read.

A failed next-page read keeps the saved receipt and offers **Continue to gear**.
That action only reloads and checks the committed state; it never repeats the
Resources confirmation. Unknown save outcomes, stale owners (including A→B→A),
changed revisions and departed page instances do not advance.

The exact Resources receipt remains available inside Gear's existing collapsed
technical details while its workspace, revisions, draft and raw-content digest
still match. It is not shown in the normal player surface. The optional API-36
driver reads those actual values on Gear, then uses Back to reopen Resources;
receipt/digest and mutation checks are unchanged.

## Local verification

- Nine actual Core/MAUI forward cases: Priority, Sum-to-Ten, Gear read failure,
  post-save refresh failure, owner B, owner A→B→A, departure, return and lost
  confirmation result. Includes disabled opening feedback, one save, read-only
  retry, no duplicate first Gear load, fresh later appearance, cold receipt and
  unchanged saved document checks.
- Six existing Resources save/feedback/recovery cases passed.
- Affected managed build and Android Compile passed with zero warnings/errors.
- 15 Resources, 12 Gear, 36 resource-driver, 3 Resources-priority, 5 localization
  and 8 version/identity/intent checks passed. Initial source-test setup omitted
  explicit dependency paths; those setup failures were resolved without relaxing
  the checks. Private-key hygiene and whitespace checks passed.

The German 130% API-36 x64 Release/Internal SDK-test route passed: one Resources
save opens Gear automatically, one Back skips the consumed review, and a new
process reopens the same saved Resources. One synthetic draft advanced from
5/5 to 6/6 with one Resources receipt, zero converted Karma and 50,000 remaining
Nuyen. Its raw character document and the other six workspaces were unchanged;
all seven cold-reopened files matched their post-save bytes. No Gear save was
issued. The APK SHA-256 is
`82fc16dc19395bf1f2c89e45f2321c3c45ee62c50c5848d4b5bbbd5e9fa7c59d`;
the private packet retains screenshots, hierarchy observations, source/test logs
and the synthetic state comparison.

One emulator SystemUI boot ANR, a transient null hierarchy after restart and
slow cold restoration/save remain recorded. This is not a startup-performance
closure. This document does not assert a signed AAB, Play upload, physical
installation, full Creation or complete live Origin book generation.
