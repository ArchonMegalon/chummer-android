# Preview 109 — account loading and device unlink

Authenticated Chummer Play Console readback on 5 October 2026 at 09:16 UTC
showed `109 (0.1.0-preview.109)` **Available to internal testers**, Internal
release 103, one version code, released at 11:16 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser readback, not Publisher API or physical-device evidence.
**Physical Play109 installation/update remains unverified.** Version109 is
consumed; never rebuild/re-upload it. Preview108/107 artifacts and evidence stay
retained. Production, tester audience, listing, billing and account settings
were not changed.

## Change and focused verification

Home's online-runner load now reads the owner-bound workspace catalog without
waiting for unrelated groups or chronicles. Campaign refresh still reads its
full catalog. A successfully loaded empty list has an explicit localized message
and Refresh action; an account transition retires that result. Existing signed
requests, one total deadline, single-flight admission, cancellation and exact
owner stamp/lease checks remain enforced.

Device unlink now uses the existing background scheduler for the entire
credential/HTTP operation, including native response disposal, then resumes the
UI continuation. It performs no additional revoke or blind retry.

Three focused regressions failed before their respective fixes: runner-route
isolation, completed-empty state and unlink running on the UI context. Final
local managed/native builds passed with zero warnings/errors. Focused native/
Core cases cover owner ABA, cancellation, deadline handling, runner/full-account
sequencing, empty-state retirement, exactly one signed revoke/disposal,
off-UI execution, catalog cleanup and UI continuation. Seven phone-localization
checks and the required source/security checks passed.

Actual API36 emulator checks used the real account with a separate empty
diagnostic installation, SDK-test certificate and font scale1.3. A normal link
and same-certificate app update preserved linkage. Online loading completed
with the explicit empty workspace message. The web account's dossier entries
are not evidence of complete recoverable native workspaces.

Normal unlink of the earlier diagnostic candidate exposed a Java runtime alert
after the server removed the installation. No uncertain revoke was replayed.
The scheduling regression reproduces the UI-context defect; the exact Java
stack was not captured, so its precise attribution remains an inference.
With the corrected candidate, one normal unlink completed without the error.
Authenticated server readback confirmed only the temporary installation absent
and the original six linked copies retained. Verified process death followed by
a new process retained the unlinked state and removed the stale catalog.
The old diagnostic runner app and real user installations were preserved.

Final diagnostic APK SHA256:
`5f114345617db54f36e4ebb7476b24d32ff53aacf3fc6fb7e470b510c420b4db`.
This is SDK-test signing and a distinct diagnostic package, not Play execution.
The version-only release delta reuses unchanged behavior checks. Nonempty native
workspace recovery, full native Origin reading/EPUB and physical Play installation
are not established by this bounded smoke. No new paid generation was performed.

## Exact local artifact

- Android producer: `70aabc4f5b0e6142984ac18f47701905d1449418`;
  main merge: `ee68458a1a8cdbe5c92cc78c45e36d4b1eeb9bd9`;
  identical tree: `cf9b3f874bd7988763219ba3549da0cce91aa6f8`.
- Account fix PR481 and version PR482 merged normally. Required source/safety
  and GitGuardian passed; version run37288222521/job111692159354.
  No protection bypass or hosted runtime claim.
- Presentation: `2b2d63a3887e1d2813843bef958be448c7837c1a`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,730,888 bytes:
  `105d6dae1d5e77a949f11c971626b9f94c095b880f8524ea4cfb5ef6079b322f`.
- Signed AAB, 33,903,513 bytes:
  `7de00dbcb8a7a74e5833574516dff419a7b38df12e3eb50c58ed63137a400ea5`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `e2aef7d9748ea06ee79bd463b9c7cae7f39f8ea31d0e7e4f44fde44d8829efb8`.
- Independent signed-verification receipt:
  `216240f3ea8452c9bd6ae63f44cfc83a72da14cb48561211251f7f8c8be3115f`.
- Private Console execution record:
  `2b4595391615344aaab4d5ec1bceea4ebdfd6e8e8ee242f42c52ecfe2aef7cce`.
- Availability screenshot:
  `53abfb9c14a9de06b32e09f41e63df1f01b90097313231d71614a84b2e06566e`.

One offline local ARM64 build used .NET10.0.112, JDK17.0.20.1 and image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact source exports, 18 packages, 331 embedded content files,
package/version/API24+/target36/ARM64, privacy flags, proof exclusion and
credential hygiene passed. Dependency mode remains locked package closure with
pinned Presentation source and Core content and explicit warm-cache reuse;
not package-only or hosted qualification. Separate existing-key signing passed
independent keyless strict JAR/certificate checks and comparison of all1,783
non-signature payload entries. Build/sign/verification containers exited.

One exact upload and one Internal rollout confirmation were executed under
standing approval. Device catalogue counts did not change. Missing deobfuscation
and native-symbol warnings remain; catalogue presence is not tablet authority.
The upload certificate is not proof of Play App Signing identity. Physical
installation, nonempty native recovery, full native Origin, all-method SR5
Creation and general-beta readiness remain open. Private packets
`origin-release109-20261005.iDhddX8e` and
`runner-only-account-load-20261005.VpSAqPcp` retain exact artifacts and bounded
evidence outside served directories. No credentials or private user facts appear here.
