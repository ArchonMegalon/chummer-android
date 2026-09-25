# Preview 39 — observed Play Internal availability

At `2026-09-25T20:43:40Z`, the owner-authenticated Chummer Play Console showed
`39 (0.1.0-preview.39)` as **Available to internal testers**, Internal release35,
track Active. Displayed release time: `25 Sept 22:42` (Europe/Vienna).
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is rendered Console readback, not Publisher API evidence. Physical Play
installation/update remains unverified and explicitly deferred by the owner.

## Delivered correction and focused checks

Android's native HTTP handler reports a DNS failure as `HttpRequestError.Unknown`
with a typed `Java.Net.UnknownHostException`. A controlled emulator disconnect
reproduced its incorrect non-retryable classification; the narrow Android-only
correction accepts that typed cause only for an already-dispatched read, with
proof admission released, the exact owner unchanged and caller not canceled.
Misleading exception text, arbitrary Unknown failures, TLS and invalid responses
do not gain retry permission. Existing budgets/timeouts remain unchanged.

An interrupted manual chapter-status check now retains the last confirmed stage
and explains how to check the existing request again. It does not start polling
an idle/terminal job, generate another chapter, accept prose or replay a mutation.
The notice is translated in English, German and Spanish.

The actual MAUI regression first failed because confirmed progress disappeared,
then passed after the correction. Focused signed-HTTP, consent, ownership/ABA,
cancellation, malformed/oversized response, bounded recovery/no-write-retry,
saved-book/export and Save tests passed, with zero build warnings/errors.
Final managed log SHA-256:
`981856ef27ce87696ef235bfa53f9c0fe1778ca90e8e6741c32ccfceab717b37`.

Clean, non-diagnostic API36 x64 Debug39 APK SHA-256:
`768394ac06bb58d7b85d60af7b68e432dec7edc51313f8c934180d85159039e4`.
The build passed in1:48.80, zero warnings/errors. A data-preserving upgrade and
cold launch reopened the retained synthetic runner. Its existing chapter read
returned stage3/3. With emulator networking disabled, one manual read showed the
new connection-interrupted notice while retaining stage3/3 and the review action.
Restoring networking and explicitly checking again returned the same ready draft.
Current and older saved decision/reading/completion files remained byte-identical.
Generation consent stayed off; the new narrative was not accepted. An earlier
null-root observation is retained separately, not counted as a successful read.
Native smoke record SHA-256:
`13bf68d2dbf9b49d09912ff5f4c0c4ca081bed3d5b8b4677ddbd19ab54dcdf50`.
This is a local affected Debug route, not Release ARM64 execution or a physical
Play installation. The original19:59 edge cancellation is not conclusively
attributed to DNS, and no claim is made to explain every prior interruption.

## Exact artifact and isolated local signing

- Android producer `e161c0cd55434b9445874b9713b99cb67c259794`, tree
  `24a71f9c08805c5c26cca8fd39f8e73209119a0d`.
- PR127 merged normally at `2026-09-25T20:35:20Z` as
  `cda3674b69cc0491476f0e2b4827344e1b1ef491`, with the identical tree.
  Required PR source/safety and secret checks, and main source/safety, passed.
  These are not hosted managed-build or device-test results.
- Unchanged Presentation seal `fc614e38aa1ec7dfab5be7c8bac3eae542f7cfd4`, tree
  `253ba382f3dbe84b073223b06203d05c15f85c4a`.
- Unchanged Core runtime `3004181b467a0f77653ce97aef8cd16d4bc4a0ff` and
  recipe/content `b194b5eabb4691c7307a7c794177abdb8b282614`.
- Unchanged Hub producer `42d0bfbb117ab6250e8b0512dd92585916c6469f`.
- Exact reused UI consumer receipt SHA-256
  `86c4e47114c0e2cb7b98081b3f728f6b09ba0fe8b61ae2364ce7f18dc25cca44`.
  All18 admitted packages were rechecked; no upstream rebuild is claimed.
- Source-input record SHA-256
  `a68cc195ec9669c9bdcbb0619ea0f9d744c353b57fc4618c2939730576150f5f`.
- Unsigned AAB:32,025,016bytes, SHA-256
  `35de4e25d73035bf9eb3466a49bfae65e67a86bef7b6685266193b68c151be0c`.
- Signed AAB:32,197,545bytes, SHA-256
  `34e4c08116c63172ff643d6d7d2df748126252555ab2d73ffbcd0d971a7592ed`.
- Existing upload certificate SHA-256
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
  This is not the unobserved Play App Signing certificate.

The offline keyless Docker ARM64 Release build passed in2:25.92, zero warnings/
errors, SDK10.0.111 and existing toolchain image
`sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.
It uses locked packages, explicitly pinned Presentation source and Core content,
not package-only APK assembly or ambient siblings. Unsigned inspection passed
package/version/API24+/target36/ARM64, privacy, permissions, key hygiene,
proof exclusion across165 managed assemblies and330 exact content files.
Separate existing-key signing and independent keyless verification passed
strict JAR signature, certificate and unchanged non-signature payload.
Signed-verification receipt SHA-256:
`cfe7cdcb51e879b2bf99553944c0c2c220e72e68e3dc76efe4b18eb662e79b89`.

Only those signed bytes were uploaded, once. Play accepted en-GB/de-DE/es-ES
notes, reported the existing missing mapping/native-symbol warnings and no
lost supported devices. No Production, tester, billing, security or unrelated
app settings changed. Version39 is consumed; future uploads need40 or a higher
unused code. Preview38 and older artifacts/evidence remain immutable.
Owned temporary build/sign/verification containers and emulator are stopped.

## Limits and custody

The opening prose remains operator-edited. A new provider continuation invents
prior biography and unspecified pronouns and remains an unaccepted draft. This
release does not resolve automatic narrative quality or unattended whole-book
completion. No physical Play installation, production App Links, general beta,
exhaustive creation/Career, SR6, audiobook, tablet, Full Editing, Windows or Rook
completion is claimed. No provider generation budget was used for this fix.

Private packet `life-release39-20260925.LqXaDpUm` retains build/sign/verification
inputs and Console evidence. Native observations are retained in
`origin-read-diagnosis-20260925.0lkROCzs/NATIVE_SMOKE.md` and the referenced
`origin-fresh-book-smoke-20260925.FUeiXtGE` packet. Actual `PLAY_PUBLICATION.json`
SHA-256: `4c7b69f35b1fa0fbd17f5bf4b039f369fd8fc19f32f44c2e9fb2168995a3cb73`.
