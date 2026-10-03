# Preview 86 — physical Play-managed update

On 3 October 2026 at 22:23:59 UTC, the existing physical API36 ARM64 phone
was verified updated85→86 through Google Play. Installed package
`com.myexternalbrain.chummer`, versionCode86 / `0.1.0-preview.86`, installer
`com.android.vending` were verified. No sideload, reinstall, data clearing or
repeat AAB upload was used.

Independent offline keyless verification passed APK v2/v3 and SourceStamp with
the expected Play App Signing certificate SHA256:
`035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
Installed base APK, 21,024,530 bytes, SHA256:
`0e6e64699d0a8e7556496b40bd389d8726cae97b4e5b275614757a42928f16a1`.
This certificate is distinct from the upload certificate. Existing apksigner
unknown-additional-attribute warnings remain in the private verification log.

At font scale1.3, first launch restored the existing Priority/Mystic Adept runner.
A verified force-stop removed the old process; a different new process restored
the same runner. Settled Create hierarchies before update, after update and after
restart are byte-identical, SHA256:
`4acd868881532bd5ed30db47caeef1977d6d62f54ecb2e901b4dfeebd21b48c1`.
Visible budgets and saved-Attribute-dependent method lock were unchanged.
No user choice was selected or saved.

Read-only navigation to Qualities after restart showed zero selections,
positive/negative25-point budgets and25 creation Karma remaining. Its settled
hierarchy matches the before-update85 observation byte-for-byte, SHA256:
`aca4bca10d18c77bf224c85b7503a3cfbae09636c92cec7174d08424a330ec03`.

This is normal update/start/existing-state/restart evidence. The existing runner
uses Priority, so this does not claim physical execution of the changed Karma
chooser or a new saved mutation. The separate synthetic native Karma rating,
single-save and process-restart scope is recorded in the
[Internal artifact observation](preview86-internal-observation.md).
Initial loading after start/restart remained slow and early hierarchies were
incomplete; only separately captured settled hierarchies were compared.
No all-private-store comparison, exhaustive options, full SR5/Origin, tablet,
ANR-free or general-beta completion claim.

The private `creation-release86-20261003.uGsyFx5B` packet retains the APK,
signature log, screenshots, hierarchies and `PHYSICAL_EXECUTION.md`.
No private runner/device identifiers are published. The signed AAB and consumed
version86 remain unchanged; this evidence update requires no build or upload.
