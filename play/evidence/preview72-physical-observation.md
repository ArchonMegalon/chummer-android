# Preview 72 — physical Play update and Magic allocation observation

On 2 October 2026, the existing Internal tester's physical API 36 ARM64 phone
updated normally through Google Play from Preview 52 to Preview 72. Play showed
the Chummer Internal Early Access listing, its internal-tester notice, then
**Open** after the update. Package Manager independently reported:

- Package: `com.myexternalbrain.chummer`.
- Version: `72` / `0.1.0-preview.72`.
- Installer: `com.android.vending`.
- Device API/ABI: `36` / `arm64-v8a`.
- Existing font scale: `1.3`, unchanged.

No sideload, app-data reset, account change, new build, signing, or upload occurred.
Version 72 remains consumed. The earlier
[Internal availability and exact AAB record](preview72-internal-observation.md)
is unchanged; this is a later physical observation, not a replacement build or
retroactive expansion of its emulator coverage.

## Installed signature

The installed base APK was pulled for offline, keyless verification:

- Size: `21024530` bytes.
- SHA-256: `cfb83133b586a7fbc8d6e2afa66a4be6a6a0c368f22ad6787f6d5b3e998f37a8`.
- One signer; APK v2 and v3 signatures and SourceStamp verified.
- Signing-certificate SHA-256:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.

This matches the previously observed Play-installed Preview 50/51/52 certificate.
It is **not** the upload certificate. The verifier reported two unknown v3
additional-attribute warnings, as in the earlier physical checks. Only the base
APK was independently verified; no all-split payload comparison or new comparison
against the certificate displayed in Play Console is claimed.

## Affected route

First launch restored the existing displayed Priority runner. Its saved Magic
allocation remained 4 (natural range 3–6), with 1 special point spent, 8 special
points remaining, 20 normal points remaining and 25 Karma remaining.

1. The dashboard's Special Attribute Points budget opened Attributes.
2. The Special Attribute Points jump scrolled to the special-attribute group.
3. One enabled **Special point +** changed the unsaved Magic projection 4 → 5
   and special points remaining 8 → 7; normal points and Karma were unchanged.
4. One enabled **Special point −** restored Magic 4 and special points remaining
   8, without the duplicate-draft blocker observed immediately before the update
   on Preview 52. No Save, Review, or Confirm action was taken.
5. The app was force-stopped and process absence verified. A distinct new process
   reopened the same displayed runner. The budget remained 8 special / 20 normal
   / 25 Karma; reopening the special group showed Magic 4 and 1 special point spent.

Screenshots and accessibility snapshots were retained in the private local packet
`magic-phone-20261002.chJemIJP`. The owned loopback relay was disconnected and
stopped afterward; user services and app data were retained.

## Limits

This proves the observed Play update, first launch, affected navigation, temporary
allocation/undo, and reopening of the existing saved allocation after process
death. It does **not** prove a new physical save transaction or byte-for-byte
preservation of every stored workspace. The earlier native save/restart evidence
in the Preview 72 release record remains separate.

The user's earlier plus failure was not reproduced in the observed baseline;
this is not a universal root-cause diagnosis. The duplicate-draft undo failure
was observed on Preview 52 and absent in this Preview 72 route. Some preparation
states still temporarily disable the controls; duration was not measured.
Technical blocker text remains
visible on unsupported attributes in this version. No general responsiveness,
all-method/Career, full-book, seven-journey, tablet, or public-beta pass is claimed.
Later source-only changes intended for Preview 73 are not present in this build.
