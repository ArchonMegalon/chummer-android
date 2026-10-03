# Preview 80 — physical Play update and Magic budget navigation

On 3 October 2026 the existing physical API 36 ARM64 phone updated from Preview
79 to `80 (0.1.0-preview.80)` through the normal Google Play Update action.
Package-manager readback at 07:44:02 UTC confirms `com.myexternalbrain.chummer`
and installer `com.android.vending`. Font scale remains 1.3. There was no
sideload, uninstall, data clearing or device-setting change.

The original producer and signed AAB remain those in the
[Internal availability observation](preview80-internal-observation.md).
This documentation does not change the producer or require another upload.

## Installed identity

- Installed base APK: 21,024,530 bytes, SHA256
  `a165c32b3529fa10b53c0ee05161a53d79ad4e17c12d9bab5d84d6994bbda51a`.
- Keyless offline `apksigner` verified APK v2/v3 and the source stamp.
- Expected Play signing certificate SHA256:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
  This is the Play identity, not the separate upload certificate.
- Installed-readback log SHA256:
  `f12d1cbfb54270d328cddeda3da4762cb1065fb881618b9bdd49e232393ad877`.
- Signature log SHA256:
  `05f84136957fbf9aa7e4ed897438c8d577bc312999a44598cf87450bb99c98ff`.

The verifier retained its two existing unknown-v3-attribute warnings while
successfully verifying the signature. No certificate was substituted.

## Bounded physical route

After loading, the existing Priority/Mystic Adept runner reopened in Create.
One tap on its combined Spells/complex-forms budget opened Magic/Resonance with
the Spells section fully visible, without a swipe on the Magic page. After
returning and locating the Adept-powers budget, one tap opened Magic/Resonance
with the Mystic Adept power-point summary and both purchase controls visible,
again without a Magic-page swipe. Screenshots confirm readable content at 1.3.
No spell, power point, confirmation or Save was selected.

At 07:53:04 UTC force-stop removed the old app process. Launch created a
different process. After the initial loading state, the same runner, Priority
method and visible budgets returned. The first-ready and post-restart-ready
hierarchies are byte-identical, SHA256
`aaf46e7892abd696b90ac3a6cf8ee0c580cf1809508c1ca4daddf687b7ac38ef`.
Restart log SHA256:
`1efc5b92402dcaaab9c9951e9bd4ec2d30012974ee1eb71b8f76cc6219f675a9`.

## Custody and limits

Private packet `creation-release80-20261003.YgDXeTDV` retains the installed APK,
logs, fresh hierarchies and screenshots outside served directories. No device
identifiers or private screenshots are checked in. This is navigation and
existing-state restart evidence, not a new saved mutation, comparison of all
private stored bytes, full Creation/Career/Origin coverage, tablet parity,
hosted seven-journey qualification or public-beta approval. Earlier evidence
is unchanged. The separate Starting Cash rejection fix is not in Preview 80.
