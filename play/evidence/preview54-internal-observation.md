# Preview 54 — illustrated Origin EPUB capacity correction

Authenticated Chummer Play Console readback at `2026-09-29T12:52:44Z` showed
`54 (0.1.0-preview.54)` **Available to internal testers**, Internal release 50,
track Active, one version code. This is browser readback, not Publisher API proof.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).

**Available on Play Internal; physical Play installation not yet verified.**
Version 54 is consumed; do not rebuild or re-upload it.

## Delivered change and actual verification

PR 204 fixes the ninth-illustration storage/export rejection: scenes and EPUB
now share the existing 128-chapter text count. The existing 4 MiB per-image and
16 MiB total image limits remain; 128 maximum-size images will not fit. Escaped
metadata is bounded. Duplicate images and images for unselected prose are
rejected. Owner, workspace, source-text/image binding, atomic CAS and v1 storage
schemas remain unchanged.

Actual-Core/Presentation/MAUI tests passed for 8, 9 and 128 full illustrated
exports, cold storage reopen, separate-process 128-chapter export, long escaped
descriptions and owner/cancellation/stale/CAS/corrupt/image-budget failures.
These are explicitly synthetic books, not provider-quality approval.

Diagnostic API-36 x64 testing displayed the complete selected synthetic passage
and image and exported through Android's Storage Access Framework. Verified
force-stop and a new process restored the same book and exported identical
non-timestamp EPUB payload. Workspace, checkpoint, prose and scene-store hashes
were unchanged; the crash buffer was empty. This native fixture had nine stored
scene entries but only one selected chapter; orphan images were excluded. It
does not prove 128 native visible chapters. Diagnostic APK SHA-256:
`0a362767c7a63c11231158a552d81f879360c049b60c40d4f2e17ecc457a186b`.

One unrelated unchanged Python source assertion still expects an old inline
Task.Run expression. It was recorded as failed/stale, not reported as passed.
Required source/safety and GitGuardian checks passed. The only app delta after
the focused PR 204 tests is version metadata in PR 205.

## Exact local release

- Android producer `29795d3587cbcfcd7b1b340316ea81188559b388`, PR 205 merge
  `e0450b93e5f54524dd828bb325e845498b2bf063`, identical tree
  `a0222971a054132766defaefd13a156186468183`.
- Presentation seal `aa75dd1fac06fb014df3d23af6c20dd7e979e995`;
  Core runtime `9a49d4fa348f41b9d462d706603b85f462ccb905`, recipe/content
  `55b848ab27cfd2637d2c8ee69ab631d40a2b109a`;
  Hub package producer `c77395de9f733427ef952c851f4a95b063cb5573`.
- Source-input receipt SHA-256:
  `630b9c136a0377f5f2318d6cbaeb2e1f7727a6e4464f8642d95a9b4ba1b81fb4`.
- Unsigned AAB: 32,986,503 bytes, SHA-256
  `daf2a6d7c685135368f8472378328d25360e68e3d62cb172645f416187b4921a`.
- Signed AAB: 33,158,936 bytes, SHA-256
  `3fccce8134f2bb81acf025184c866e76f7b18671cb11ef04560e0938eefecab1`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Independent signed-verification receipt SHA-256:
  `2182564e80d34c5234803d96885a78542f3ba516ab1842f2ddba5665b96b4c35`.

The keyless local ARM64 Release build took 152.12 seconds, zero warnings/errors,
.NET 10.0.112, toolchain image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
All 330 content files, bundle/API/ABI/privacy checks, private-key hygiene and
proof exclusion passed. Separate old-key signing and independent keyless strict
JAR signature, certificate and unchanged non-signature payload checks passed.
Builder and verifier had no keys or network. The unchanged 18-package graph was
checked; dependency mode remains locked package closure with explicit pinned
Presentation source and Core content, not package-only native assembly.

Private packet `origin-release54-20260929.dIvtbbmR` retains exact inputs, both
AABs, build/sign/verification logs and Console evidence. Its publication receipt
SHA-256 is `90d28e6826ea177781fc92fcbb46e34794485de0627d8c31cae55f6a625b9b8b`.
Play reported only the existing missing mapping/native-symbol warnings and no
lost supported devices. No Production, tester, listing, security or billing
change occurred; no paid-provider request was replayed.

## Remaining limits

This fixes illustrated-book storage/export capacity; it does not approve the
real AI-generated story's quality or assert complete Origin delivery. Native
testing was Debug x64, not physical Release ARM64. Hosted seven-journey,
exhaustive method/Career, tablet, Full Editing, desktop and public-beta claims
remain outside this evidence. The upload certificate is not the Play App Signing
certificate. [Preview 52](preview52-internal-observation.md) retains the latest
physical installation evidence; [Preview 53](preview53-internal-observation.md)
and all older receipts remain immutable.
