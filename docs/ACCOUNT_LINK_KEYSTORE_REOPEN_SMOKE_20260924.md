# Android Keystore link-resume correction — 24 September 2026

## Defect and correction

The API-36 synthetic Debug app reproduced **Fresh link required** immediately
after returning from its browser handoff. Redacted status markers proved that
the key existed, but `key is IPrivateKey` was false for the managed `IKey`
wrapper. The app classified that as an invalidated key before signing or HTTP.

Both public-key lookup and signing now use Android's runtime-checked
`JavaCast<IPrivateKey>()`, retaining null/invalid-type rejection, non-exportable
private keys, SHA-256/RSA signing, exact public-key binding and local signature
verification. An invalid Java cast remains a relink-required failure. See the
[Microsoft API contract](https://learn.microsoft.com/en-us/dotnet/api/android.runtime.extensions.javacast?view=net-android-36.0).

## Focused verification

- Two focused source-contract checks passed, including the regression guard.
  Initial invocation lacked the required explicit reviewed-workspace root;
  corrected invocation used the existing local reviewed roots, not stubs.
- Locked local restore and all 29 key-authority tests passed.
- Locked local restore and all 48 HTTP/ownership hardening tests passed.
- Local offline Docker Debug build passed: zero warnings/errors, 1m27.92s.
- Native link initiation, browser return and the public Hub poll passed: the
  original managed-wrapper test remained false, but Java conversion succeeded,
  the stored public key matched, signature verification passed, and HTTP 202
  correctly left the unapproved link **Finish linking** rather than discarding it.
- Force-stop verified the old process absent; cold launch produced a new PID.
  The existing pending key was reopened without a new Link action, signing and
  HTTP 202 passed again, and More still showed **Finish linking**.
- The retained synthetic Life Modules runner was byte-identical before/after.
  No runner was reseeded, no app data was cleared, and no key was exported.

## Scope and artifact boundary

The exercised APK is an isolated x64 Debug package, not a Play artifact:
`com.myexternalbrain.chummer.lifepackagedebug`, diagnostic version 28. SHA-256:
`0c673b37829ee423306a335b02102272c5eb4acfe631b8277cc7ff764de7d3fb`.
Its source exports contain Debug-only status markers; those markers are **not**
part of the committed app code. The production Keystore correction is identical
after removing those marker blocks. Existing account/HTTP behavior is unchanged
apart from that correction. The device update used the same ordinary Debug
certificate; the Play upload key was not accessed.

The emulator initially rejected an install while booting and later restarted
system_server during installation. That attempt failed. A subsequent install
was made only after verifying the replacement service, unlocked storage,
PackageManager and unchanged runner data. System/System UI ANR prompts were
observed separately; this report does not claim emulator performance readiness.

Chrome's first-run terms were not accepted, no browser login or account approval
was performed, and no access grant, provider generation, chapter adoption or
book export is proved here. The separate user-controlled Google browser was
untouched. This closes the native pending-key resume defect, not end-to-end
account/book qualification.

The earlier unsigned Preview 29 AAB does **not** contain this correction and
must not be signed as if it did. It remains an immutable unpublished artifact.
No new Release AAB, upload-key signature, Play upload or physical installation
is asserted by this smoke.
