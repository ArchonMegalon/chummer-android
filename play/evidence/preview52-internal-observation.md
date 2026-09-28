# Preview 52 — account-recovery and Origin navigation hotfix

Authenticated Chummer Play Console readback at `2026-09-28T15:42:05Z` showed
`52 (0.1.0-preview.52)` **Available to internal testers**, Internal release 48,
track Active, one version code. This is browser evidence, not Publisher API
evidence. [Install/update](https://play.google.com/apps/internaltest/4700678198570024687).

## Delivered increment and verification

Home no longer admits account linking while stored-account recovery is loading.
It shows progress and retires the transient waiting notice when actual recovery
settles, including a late notice assignment. Authentication, owner checks and
runner data are not reset. Origin now puts selected follow-up questions and
confirmation first; valid pending decisions reopen through scheduled owner-read
admission after cold restart. An empty reader offers the next real setup step,
not a fabricated story.

The old account UI defect was reproduced in a focused regression test. The
corrected real account/Core/startup tests passed, including linked/unlinked
completion, stale callbacks, owner ABA, corruption and cancellation. Local
Debug API-36 x64 upgrade and verified process restart retained the exact saved
Life Modules revision and linked account without a stale waiting notice.
Earlier unchanged-input Origin tests verified first-reopen answers and one
confirmation. The release delta after those checks is version metadata only.

Physical Preview 52 updated normally from Preview 51 through Play at
`2026-09-28T16:00:36Z` on an API-36 ARM64 phone. Package/version and installer
`com.android.vending` were read back. First launch and a verified force-stop/new
process retained the existing runner's displayed revision, snapshot prefix and
module budget. Home, More and Account & privacy settled without the waiting
notice; the pre-existing unlinked state and enabled Link action remained intact.
No account link, credential reset, character mutation or debug sideload was used.
This verifies the observed unlinked route, not a new end-to-end account login or
a reproduction of the original permanent wait on the phone. Linked recovery and
late-callback behavior retain the separate focused/local Debug evidence above.

The empty-origin Read entry now explains the missing opening decisions and its
Back to your runner action works. It does not present a setup summary as prose.
Linked-runner recovery remains readable and displays the correct empty-history
state after process restart. The physical font scale remained 1.3; no phone
security setting was changed.

The pulled installed base APK SHA-256 is
`5c82582b897438d9b046999fe9b9f46cb02d16e49d8f87d2338416962bf552f3`.
Offline verification passed APK v2/v3 and Source Stamp verification. Its single
signer certificate SHA-256 is
`035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`,
matching the installed Preview 50/51 certificate. This Play signing certificate
is distinct from the upload certificate below and was not independently compared
with Play Console. The verifier reported two unknown additional v3 attributes;
signature verification still succeeded. No claim about every installed split is
made from the base-APK check alone.

There is no full-book reading, EPUB, hosted seven-journey, full beta, public,
tablet or desktop completion claim. The paid FirstBook operations remain
separate, unresolved provider work; this hotfix did not retry them.

## Exact artifact identities

- Android producer `42662db644789a4fa7c24cd6ba42ff71dae2d658`, PR 190 merge
  `fed5c5bb01b52003d2766bcf79194ea5fb4e24bc`, identical tree
  `b5b0eef453422302540da601e99a26df91f17edb`.
- Presentation seal `fde950c3a093d2d384180aa2a4baa5db4eeac934`;
  Core runtime `b19fc03123c43885c39859cdad0c4554ad46b7ff`, recipe/content
  `b865101d02eb24fb89f9ca4eb5b397e1acffae83`;
  Hub package producer `c77395de9f733427ef952c851f4a95b063cb5573`.
- Source-input receipt SHA-256:
  `d66026601525ce6733ab539cc6d7aee59a4712d689c03a9bdac46b8d775d6728`.
- Unsigned AAB: 32,920,826 bytes, SHA-256
  `c0d3b2e53ec53c06c41d5cc80bab3b796c2dbee8780116280951b444dc87d0e0`.
- Signed AAB: 33,093,317 bytes, SHA-256
  `7892723367437feca69223dec26326aa17d926272e7ce7a5b80985b12e11d08d`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Independent signed-verification receipt SHA-256:
  `fc932a86ba2b760bb246695a1a3ba9cf78b2528719cfa8fbe270877b1d08f438`.

The local keyless ARM64 Release build completed in 150.95 seconds, zero warnings
or errors, .NET 10.0.112, builder image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Exact package/content, bundle/API/ABI, privacy, key hygiene and proof-exclusion
checks passed. Separate old-key signing and independent strict signature,
certificate and unchanged-payload checks passed. Dependency mode remains locked
package closure with explicit pinned Presentation source and Core content,
not package-only native assembly or hosted qualification.

## Retention and limits

Private packet `origin-release52-20260928.XmbDot5m` retains the bytes and logs.
`PLAY_PUBLICATION.json` SHA-256
`3eb7328f49a3ad91ade195ab1a7d2e4f108d769cba693b85486d7ea5c7d78547`
records availability with physical installation pending at that earlier time.
The later private `PHONE_INSTALL.json` and phone screenshots/hierarchies retain
the physical update and affected-route result; earlier pending observations
remain unchanged rather than being rewritten as successful installs.

Play reported the existing missing mapping/native-symbol warnings and no lost
supported devices. Production, audience, billing, account security and listing
were unchanged. Version 52 is consumed; do not rebuild or re-upload it.
[Preview 51](preview51-internal-observation.md) and older observations retain
their original time-bounded claims.
