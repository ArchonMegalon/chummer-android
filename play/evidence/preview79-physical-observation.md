# Preview 79 — physical Play update and bounded help observation

On 3 October 2026 the existing physical API 36 ARM64 phone updated from Preview 78
to `79 (0.1.0-preview.79)` using Google Play's Update action. Package-manager
readback at 05:41:53 UTC identifies `com.myexternalbrain.chummer` with installer
`com.android.vending`. There was no sideload, uninstall, data clearing or change
to the device's font scale 1.3. Internal availability was recorded separately in
the [artifact and Console observation](preview79-internal-observation.md).

## Installed identity

- Installed base APK: 21,024,530 bytes;
  SHA256 `00cedba470152fd6b34f5199c985239648e82a8d99f8f1030bdb9da7b8448102`.
- Keyless offline `apksigner` verified APK v2/v3 and the source stamp.
- Play certificate SHA256:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
  This matches the expected Play identity, not the separate upload certificate.
- Installed-readback log SHA256:
  `47847f5f42d85db4028394b37a60b52a1dbc00fa84f7925db84e545ebe40a34a`.
- APK signature log SHA256:
  `bd3e261cd6ea01fdfb0ca1d3ae8dc5ab3713810f8de5bdd21ec4c4a538a19e7b`.

The verifier reported its two existing unknown-v3-attribute warnings while
successfully verifying the signature. No signing identity was substituted.
Producer and AAB identity remain those of the original Preview 79 transaction;
this evidence does not call a later documentation commit the APK producer.

## Observed route

After initial loading the app reopened the existing Priority creation runner.
The Qualities budget link opened the chooser. Mystic Adept's `!` help showed
the complete readable explanation with effect details folded and no visible
GUIDs or instruction to read a rulebook. Return navigation worked.

The Spells budget link opened Magic/Resonance; scrolling to Spells opened its
catalog. Acid Stream and Agony summaries rendered within their cards. Opening
Acid Stream showed the readable summary and separate Core-derived rule fields.
No selection, confirmation or Save action was performed. Existing saved source
settings remained in effect: this runner displayed six eligible Qualities and
86 spell choices, not the entire authored-description inventory.

At 05:48:20 UTC force-stop removed the existing app process; a different process
started at 05:48:21 UTC. After loading, Create again showed the same runner,
Priority method and visible budgets. First-launch-ready and post-restart-ready
hierarchies are byte-identical, SHA256:
`aaf46e7892abd696b90ac3a6cf8ee0c580cf1809508c1ca4daddf687b7ac38ef`.
Restart log SHA256:
`45ccaf0114dd989ca3b89d12380b744a98ca7a6a73907a2b701dec5db68ff2d7`.

The tested examples already existed before 79. This is an exact-installed-build
help/navigation and existing-state restart smoke, not physical coverage of all
34 newly added explanations, every locale or a newly saved mutation. Returning
from Quality help reloaded the chooser at its top; scroll-position preservation
remains a usability follow-up, not a claimed passing property.

## Custody and limits

Private packet `creation-release79-20261003.2WcQHk1A` retains the installed APK,
readback/signature/restart logs, screenshots and hierarchies outside served
directories. No device identifiers or private screenshots are checked in here.
The observation does not compare all private stored bytes or prove exhaustive
rule execution, all creation methods, Career, the full Origin book, tablet,
desktop, copyright clearance, hosted seven-journey qualification or public beta.
The two remaining authored-effect gaps are still open. Preview 78 and older
publication/physical evidence are unchanged; no new AAB or upload was needed.
