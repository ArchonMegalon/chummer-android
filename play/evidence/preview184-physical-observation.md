# Preview 184 — physical Play update and retained Creation/reader restoration

On 10 October 2026, 23:03–23:09 UTC, the existing Internal-test phone was
updated from Preview 183 to 184 through the actual Play Store Update control.
The listing changed from Update to Open; installed package metadata records
`184`, `0.1.0-preview.184`, installer `com.android.vending`, and update time
11 October 01:03:56 Europe/Vienna. No sideload, reinstall or data clear occurred.

The physical device is API 36 ARM64 with font scale 1.3. The installed base APK
was pulled and independently checked by the existing offline, read-only,
keyless Docker verifier:

- Base APK: 21,024,530 bytes, SHA-256
  `57fd5fa1cb374451694acdd252e049182377a72e93b506811fe7c11743291dfd`.
- Established Play certificate SHA-256:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
- Signature log SHA-256:
  `54dfeebad1db78f626c8e477a85312d86a054b5171a185843b27e1a7226ae98c`.
- Installed-verification receipt SHA-256:
  `3dd0a8483c126b340ecd2a7c2cafc556acd2a7da7583a65f4364917163a22195`.
- Physical route result SHA-256:
  `4ed0f3a521c256241afa2f8ca786e43ede48c2b6305927915ac6e091101fe3ff`.

APK v2/v3 signatures and SourceStamp passed. Two unknown optional v3-attribute
warnings remain recorded. This checks the base APK, not equality of every split
with the release bundle. The Play certificate is distinct from the upload key.

## Observed route

The two retained Life Modules dossiers restored after the update. The selected
runner's Read your story control opened the existing complete chapter and its
automatic illustration. Switching between the two existing runners and back
restored the original selection. Creation's Continue your story tile entered
Life Modules and automatically opened the reader; Back returned to Life Modules,
then a further Back returned to Create. The normal Save control was invoked
without changing rules or story decisions. No separate save confirmation or
durable commit receipt was observed, so a new save transaction is not claimed.

The app was force-stopped. Both process lookup and `/proc` confirmed the old
process was absent; relaunch created a different process. The same selected
runner and both dossiers restored. The complete Creation-dashboard accessibility
hierarchy matched the pre-restart dashboard. Normal reader entry again displayed
the illustration and exactly the accepted 17,196-character, 2,447-word chapter:

`6f37c5aaf49c1b9dd6e5a87f829c9572a92fc61080856228cc40e4ea4c30316a`

The text was compared against the retained accepted provider result before and
after restart. Illustration restoration is visual/accessibility evidence, not
on-device image-byte comparison. Private saved-file equality is not claimed.
The original uncertain successor fence remained byte-identical; its reader
status returned to outcome unknown. No generation, reading acknowledgement,
new decision, export or Career finalization was requested.

## Limits and retention

The abandoned-navigation-press fault remains actual managed-handler evidence;
it was not injected into this physical app. Priority's affected native smoke is
the separately recorded emulator test. This phone currently retains Life Modules
runners, so it supplies no new physical Priority allocation result.

Initial and restart Activity timings were 822 ms and 696 ms, not time-to-usable.
The first restart hierarchy still showed loading; a later observation confirmed
restoration. No controlled startup improvement, general reload reliability,
complete Creation/Career or live multi-chapter manuscript claim is made.

The private `physical-preview184-20261011.PcwML19P` packet retains the exact
base APK, signature result, screenshots, hierarchies and bounded verification
scripts. Screenshots precede hierarchy captures and are not atomic with them.
Raw character prose, account details and device identifiers are not published.
Owned temporary device captures were removed after byte comparison with private
local copies. No new build, signature or upload was needed; version 184 remains
unchanged. This supplements the observation-time limits in the earlier
[Internal availability record](preview184-internal-observation.json).
