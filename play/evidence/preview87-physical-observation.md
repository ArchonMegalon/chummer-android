# Preview 87 — physical Play update and saved-state reopen

On 4 October 2026, approximately 01:46–01:54 Europe/Vienna, the authorized
physical API36 ARM64 phone updated from Preview86 to Preview87 through Google
Play. Package `com.myexternalbrain.chummer`, version `87` / `0.1.0-preview.87`
and installer `com.android.vending` were checked. No sideload, reinstall, app-data
clear or change to the user's character was performed.

The pulled installed base APK (not the AAB or complete split set), 21,024,530
bytes, has SHA256
`6d65a73a7cfbba7b390fdc20ef1fa4a04f3c3d8371586ebfb7106bd5085240fb`.
Independent offline verification passed APK v2/v3 and SourceStamp signatures.
The Play signing certificate SHA256 is
`035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`,
distinct from the existing upload certificate in the
[signed artifact and Console record](preview87-internal-observation.md).

## Observed route

First launch restored the existing Priority runner. At font scale1.3, opening
Qualities through the Creation Karma budget showed zero selected qualities,
positive and negative allowances25 each, and remaining Karma25. The page was
readable. No quality was selected and no Save was pressed.

Force-stop was verified to remove the old process; a different process then
launched successfully. Create and Qualities reopened with the same visible
state. The complete settled hierarchies before update, after update and after
restart were byte-identical for each page:

- Create: `4acd868881532bd5ed30db47caeef1977d6d62f54ecb2e901b4dfeebd21b48c1`.
- Qualities: `aca4bca10d18c77bf224c85b7503a3cfbae09636c92cec7174d08424a330ec03`.

Private XML, screenshots, install/signature output and verified restart logs
are retained in `creation-release87-20261004.YrdANReB`, outside served paths.
Only the owned temporary phone observation file was removed after matching its
retained copy. User data and release/rollback artifacts remain intact.

## Limits

This proves the bounded Play update, installed signing identity, read-only
changed page and existing visible-state reopen. It is not a private-store digest,
physical mutation, measured performance improvement or all-method SR5/Origin
completion. Early loading placeholders and slow transitions remain limitations.
The separate synthetic API36 test covers the one-save/exact-receipt mutation.
Version87 remains consumed; this evidence-only update requires no new AAB.
