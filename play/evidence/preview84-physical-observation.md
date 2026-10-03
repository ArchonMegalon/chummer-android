# Preview 84 — physical Play-managed update

On 3 October 2026 at 18:25 UTC, the existing physical API36 ARM64 phone updated
83→84 through Google Play. No sideload, reinstall, data clearing or repeat AAB
upload was used. The installed package is `com.myexternalbrain.chummer`,
versionCode84 / `0.1.0-preview.84`, installer `com.android.vending`.

Independent offline keyless verification passed APK v2/v3 and SourceStamp,
with the expected Play App Signing certificate SHA256
`035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
Installed base APK SHA256:
`3eadb214de455b3c8524df29dad4025adf917cc24cdf8b9694660bb0529ea9d1`.
The Play certificate is distinct from the upload certificate. Existing apksigner
unknown-additional-attribute warnings are retained in the private verifier log.

At font scale1.3, first launch restored the existing Priority/Mystic Adept runner.
Create→Resources displayed the current budget and conversion options. A verified
force-stop removed the old process; a different new process restored the same
runner and the same read-only Resources route. Complete before/after Resources
hierarchies were byte-identical, SHA256
`15cb34f62bb6c6da7864d5a8c554a2a567094f9f01e201909ecdada04b46a841`.
No character option, Save or Confirm was selected, and no user decision changed.

This is update/start/read-only route/restart evidence, not a new physical Resources
mutation or saving-feedback test. The separate synthetic native one-save/reopen
evidence is described in the [Internal artifact observation](preview84-internal-observation.md).
Initial blank loading and slow transitions remain; no all-method, Origin,
tablet, ANR-free or general-beta completion claim.

The private `creation-release84-20261003.gIgdr4dg` packet retains the exact APK,
signature log, screenshots, hierarchies and `PHYSICAL_EXECUTION.md`. Private
runner/device details are not published. The signed AAB and version84 remain
unchanged; this evidence update does not require another build or upload.
