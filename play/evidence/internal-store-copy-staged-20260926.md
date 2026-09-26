# Current Internal store copy — saved, not submitted

At `2026-09-26T19:38:07Z`, the owner-authenticated Chummer Play Console showed
exactly two **Changes not yet submitted for review** on the default en-GB listing:
short description and full description. The separately submitted
[exact Troll icon](troll-store-icon-review-20260926.md) remained **in review**.

Only the short/full text was changed. App name, promotional-video field, all
seven assigned image URLs (icon, feature graphic, five phone screenshots) and
the existing AI declaration were preserved. The language menu contained only
en-GB; DE/ES source translations were not uploaded and no language was added.
The explicit English source mapping is documented in [LISTING.md](../LISTING.md).

The saved form matched `listing/en-US/` exactly after removing the source files'
single final newline. SHA-256 including that newline:

- short description: `4411a609700e6810bc170fede6897df3530b4d516a9d29cd3b0286d1d254852c`;
- full description: `4bc7825285cec3b64910b7a7ababae1f48b034eaaf4456e8ac3e51cf421de7fd`.

Selecting “Submit 2 changes for review” opened a warning that sending would
cancel and restart the existing icon review. **Cancel was selected**; the final
restart confirmation was not approved or executed. Subsequent rendered readback
still showed the two texts unsubmitted and the icon separately in review.
Submit the saved text after that review finishes, following fresh account and
exact-change checks. Do not describe the saved text as submitted, approved or live.

This removes obsolete tablet/desktop/campaign promises and Preview12/seven-flow
authority wording. Current copy covers experimental SR5 phone wizards and the
accepted Origin reader/illustrated EPUB, while retaining consent/provider
conditions and incomplete whole-book/Life Modules/physical-install disclosures.
No current seven-journey runtime qualification is inferred from text validation.

Local checks: 13 listing tests (including all disclosure-removal subcases),
the existing focused Android listing-contract test, standalone listing validator,
private-key hygiene and whitespace checks passed. The contract test's first
invocation failed because its implicit Hub sibling was absent; rerunning with
explicit existing workspace/Hub roots passed. No app build, emulator, signing,
new AAB, Play release, provider request, credential or account-setting change.
Historical release notes, Data safety and gate-authority bytes remain unchanged.
[Preview41](preview41-internal-observation.md) stays the last recorded Internal
artifact; its consumed version code must not be reused. Physical install remains
unverified. No3D.

The local packet `store-copy-20260926.Ctng84TV` retains exact Console screenshots
and the operation result. The pre-submit summary screenshot SHA-256 is
`b2eac4cee03fe97af295ecb7374a9a082a04a337b7ac7d93d3b9c73ddd3cc8ff`.
These records prove staging and the review boundary, not publication.
