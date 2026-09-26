# Current Internal store copy

The version-neutral short/full descriptions describe selected experimental SR5
phone workflows and the accepted Origin reader/EPUB surface. They are not runtime
qualification or a declaration that a source version has reached Play.

At the 2026-09-26 Console inspection, the only configured store language was
**en-GB (default)**. Use the English files in `listing/en-US/` for that existing
en-GB entry. This is an explicit copy mapping, not a claim that an en-US entry
exists. `de-DE` and `es-ES` are prepared source translations; do not call them
published or silently add store languages. The app's UI locale policy is separate.

Scope is grounded in [Preview18's minimal Priority route](evidence/preview18-internal-observation.md)
and [Preview41's accepted story, illustration and EPUB](evidence/preview41-internal-observation.md).
Full Life Modules-to-Career with a whole accepted book and physical Play-managed
installation remain incomplete/unverified. No3D; no audiobook, SR6, tablet,
Full Editing or Rook completion claim.

Historical `release-notes-*.txt` files retain their original version-specific
meaning and bytes. Do not upload Preview12 notes with a later bundle or make
the old seven-journey policy a claim of current runtime qualification.

Validate the copy without building or publishing:

```sh
python3 scripts/verify_play_listing_localizations.py
python3 -m unittest discover -s tests -p test_play_listing_localizations.py
```

The validator checks format, limits, supported source languages, required scope
and consent disclosures, known overclaims and preserved historical evidence.
It cannot replace review of new feature claims against actual delivery evidence.
Keep source version identity separate from actual release and listing-review
receipts. Saving or submitting a listing is not Google approval or live visibility.
