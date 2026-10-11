# Saved attribute allocation: read-only review

11 October 2026. This fixes the unchanged-review defect observed on physical
Play Internal 187. That version's historical failure record remains unchanged.
The correction is now [available on Play Internal 188](../play/evidence/preview188-internal-observation.json)
and [physically verified](../play/evidence/preview188-physical-observation.md).

## Behavior and safety

Review now accepts an issued, current, exact saved projection through the
existing strict `CreationAttributesPhoneAuthority.CanAdoptPreview` checks.
Core still reports the duplicate-save blocker. Android retains that reason in
collapsed technical details and offers a read-only view with an already-saved
message and **Back to attributes**, not another Save action. Direct duplicate
confirmation is rejected before Core IO. Changed allocations still use the
existing confirmation, owner, revision and persistence checks. Core and
Presentation inputs are unchanged.

## Focused local verification

- New actual Core/MAUI-handler regression failed on the old implementation and
  passed after the fix. EN/DE/ES saved views, copied/unissued previews, stale
  revisions, cold-store reload and owner A-to-B-to-A rejection were checked.
  Read-only rendering and duplicate confirmation performed no Core IO; a real
  changed allocation still saved exactly one revision.
- Existing compact-review, attribute-save feedback and special-attribute
  checks passed, including save/reopen, cap, budget and owner boundaries.
  Eight existing attribute source-contract tests passed. Managed and native
  builds completed with zero warnings and zero errors.
- API36 x64 Release/Internal, separate SDK-signed test package, German and
  font scale 1.3: the retained synthetic Priority runner opened its unchanged
  saved review, displayed all thirteen values and the already-saved message,
  and returned through the actual Back button. No Save control was offered.
- Process 2710 was stopped and verified absent; new cold process 4967 restored
  the same runner. Opening saved review again displayed the same values and
  remaining normal/special/Karma pools of 20/7/25. Edge stayed 4 and saved
  revision stayed 4/4. All five saved workspace files were byte-identical
  before review, after returning, and after cold reopen. No mutation occurred.

Initial Pixel Launcher and SystemUI ANRs, two transition-time null hierarchy
observations and slow startup were retained. One out-of-viewport selector was
rejected without tapping; scrolling exposed it normally. These are not passing
startup-performance results. The smoke does not establish full Creation/Career,
live Origin, physical installation of this fix or wider beta readiness.
Test-app IPv4/IPv6 egress was blocked; no provider operation was requested.

## Retained identities

- Product commit: `f185b78f709c08d30093c6a66218d5a2e20e0ea2`.
- Product tree: `a00c979504f6224bef8fa7a40e376e4da3100fdb`.
- Test APK SHA256: `d2203a46f8b2f19320d5fc1a2eb2162d207d81caee40e0b40d5d3d4fbec00e47`.
- SDK test certificate: `a793dcceb2eb624e0d3e0cc6156668afac889e1ee248d50d7551bab808d6b2bf`;
  APK v2/v3 verification passed. This is not the production upload key or Play certificate.
- State-comparison result SHA256: `1cdeccb7c51688d63f409a4f6aaf9604e9a0b50777cbb0dfd028a8745729742e`.
- Passing managed log SHA256: `c06bd0b0e383201b3854e400156743436d74cb69055f800d8abb758949f7b4d1`.
- Safety log SHA256: `d4134a362c33778d2fae2712c0d73bb49f7c7227ffb2ecb0d5c9292e41a33fb6`.
- Native build log SHA256: `4101afe8b33c73a4dd0b8a2b95ac3ccb02c5c14b73f3c7882df78929b905b2c5`.

Exact artifacts, source exports, failed and passing logs, screenshots and state
comparisons remain in the private local packet. The build uses the current
locked package closure with pinned Presentation source and Core content, not
a source-free build. The later production 188 ARM64 AAB was locally built,
original-key signed, independently verified and uploaded once. Play187→188
update, Play certificate, read-only saved review, actual Back and cold re-review
passed on the authorized physical phone. All 13 displayed values and budgets
were unchanged, without another save. See the separate 188 records above for
exact artifact/physical identities and limitations. Both 187 and 188 are consumed;
neither may be re-uploaded to relabel evidence.
