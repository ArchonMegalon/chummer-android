# Preview 130 — quality-loading allocation improvement

Authenticated Chummer Play Console readback on `2026-10-07` at20:10UTC showed
`130 (0.1.0-preview.130)` **Available to internal testers**, Internal release124,
one version code, released7October22:10Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API or physical-device evidence.
**Physical Play130 installation/update remains unverified.** Play states that
propagation usually takes up to an hour and can take longer. Version130 is
consumed; do not rebuild/re-upload it. Preview129 and earlier artifacts remain
unchanged. No public-track, audience, listing or account changes were made.

## Change and affected verification

Core reuses private XML writer buffers within a quality catalog projection.
Rules, exact serialization/digests, owner isolation and fresh admission remain
unchanged; there is no shared catalog cache. The [package intake and native smoke](../../docs/evidence/quality-buffer-package-intake-20261007.md)
record focused XML equivalence, ownership and allocation checks. The512-row
managed projection allocates10,697,504bytes instead of18,003,200 (40.6% less).
This is not native startup-time or peak-memory evidence. UI consumer verification
passed five builds,869 product cases and selected regressions. Android affected
native/MAUI/interaction builds passed with zero warnings/errors; managed cold
restore cases covered Priority, Sum-to-Ten, Karma, Life Modules and Career,
including failed-presenter fail-closed recovery. Package/content/source/hygiene
checks passed; an initially incorrect relative hygiene path was corrected without
changing the verifier. Old unpublished UI oracle failures remain recorded.

Final exact-graph API36 x64 SDK-test APK
`f28ac766453613922272d666cf83a1ffd82bf334585d5290d1f278107679c0d7`
passed certificate and331embedded-content checks. In-place upgrade and verified
new-process reopening retained identical synthetic workspace bytes and showed
the same Creation budgets. No Save, import or mutation was replayed. New-process
Shell initialization took6.254s and selected-workspace restoration12.521s.
Boot SystemUI ANR and an initial null-root observation were retained separately;
subsequent actual Create screenshot/hierarchy passed. **Native loading remains
slow; complete SR5 Creation and a finished application are not claimed.**
No book/image provider requests or credits were used by this increment.

## Exact local artifact

- Android producer: `a389d388b45790e43ebc2c47aed77275d5531a96`;
  normal PR539 merge: `6cac906c5383d855c7e1bec700dd4e7b3ea7ff5b`;
  identical tree: `ae60853687c013b1e4a3d1bdbd4c16a4bf35148f`.
- Presentation seal: `e3cc3fea12676f8483dfdd475e8863e19cc9e560`;
  Core runtime: `34baa87a3b0a53d97571ef396f3df15e6e24c494`;
  Core recipe: `8c039ce6e612175a17a5757cb09cf31bfa9dcb47`.
- UI consumer receipt SHA256:
  `5d1618cadf708081827a55dc958c7a009785d6606a48e6db081f612c0578c7cc`.
- Core package ZIP SHA256:
  `267fa383639fa5bcb4f3e8304dacefc9ad7c5e66c92a25b973a768336d49cc07`.
- Unsigned AAB,33,811,473bytes:
  `b1c54a791dc2597c1f9485617c98277bec2ce51740ba7e7842e8d51bd767d09e`.
- Signed AAB,33,984,125bytes:
  `ffe1a0f653103f2026483bc45f6ef8556c18856cfe43883de990636904d3b8f5`.
- Existing upload certificate SHA256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt SHA256:
  `b9bf9f1d092a52111eb2451f8a74d964077b039ce02fc39d3d5fc59c991cb215`.
- Independent keyless verification receipt SHA256:
  `ddf764a40ce63a335360ce555096075934b407726af6a00b75f959c5ac1d8c85`.
- Private Console execution record SHA256:
  `9457ff008a75b804eeac7e5473660f1cce1f60b5cb34a6c5f2030602926fa53c`.
- Visually inspected availability screenshot SHA256:
  `d974cc766110ea1aef1a1ff87d285fe2f0e96706607ad3d9c92a8eb13dfe8b88`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, canonical exports,18packages/13authority files,331embedded
content files, application/version/API24+/target36/ARM64, privacy, credential
hygiene and proof exclusion passed. Mode: locked package closure with explicit
pinned Presentation source and Core content; not package-only/ambient siblings.
Separate existing-key signing and independent offline keyless verification
passed strict JAR signature, pinned certificate and equality of all1783payload
entries. Private signer inputs were never served or uploaded.

Required source/safety checks passed before the normal protected merge. One
upload and one final confirmation used standing2026-10-05 Internal approval,
recorded separately from the checks. Browser/OODA account, app, artifact and
track checks preceded actual availability readback. No hosted qualification,
physical installation, tablet authority or public readiness is claimed. Missing
deobfuscation/native-symbol warnings remain. Owned build/sign containers and
emulator are stopped; saved data, private build packet and rollback artifacts
are retained. Windows implementation remains stopped.
