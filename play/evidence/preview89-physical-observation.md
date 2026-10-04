# Preview 89 — physical Play update and startup restoration

On 4 October 2026 at approximately 02:19–02:27 UTC, the existing Internal-test
phone updated from Preview 88 to 89 through the normal Google Play Update
action. No sideload, reinstall, cache clearing or runner choice was made.

- Package: `com.myexternalbrain.chummer`, `89`, `0.1.0-preview.89`.
- Installer: `com.android.vending`; physical API 36, ARM64, font scale 1.3.
- Installed base APK: 21,024,530 bytes,
  `f5f43ab742a5c065ed60f56807a4a0064e21559ba1266c4a0aad0e3050fee7fc`.
- Expected Play signing certificate:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
- Offline signature log:
  `9da10ddffe63ea8c188d9461115456b6edd007c0b3713d295db585e950559b0c`.

Offline keyless `apksigner` verified APK v2/v3 signatures and SourceStamp. The
certificate matched the established Play identity, distinct from the upload
key. Two unknown optional v3-attribute warnings remain in the retained log.

First launch showed readable loading feedback before restoring the existing
Priority Creation runner. After force-stop, absence of the old process was
verified through both the package process lookup and `/proc`; a different
process reopened the same visible Creation overview. Before the update, after
the update and after restart, settled accessibility XML is byte-identical:
`4acd868881532bd5ed30db47caeef1977d6d62f54ecb2e901b4dfeebd21b48c1`.
The restart record is
`bb3a998af1320e781a776928df30f6e1f07e094bc2359d9177809c9acd5d1119`.

This is bounded visible-state evidence, not equality of inaccessible private
files or a new saved creation mutation. Activity launch timings do not measure
runner readiness; startup speed is not qualified. Physical Origin reading,
export, new provider generation and all-method Creation remain unproven by
this observation. The separately scoped managed and synthetic-device results
remain in the [artifact record](preview89-internal-observation.md).

Private packet `creation-release89-20261004.mpnewcLx` retains the installed APK,
signature/process logs, screenshots and hierarchies. The owned relay and ADB
server were stopped; only the temporary device observation XML was removed
after verifying its retained copy. User services, other ADB sessions, accounts,
runner data, signing keys and rollback evidence were preserved. No version-89
rebuild or re-upload occurred. Full SR5/Origin and beta completion remain open.
