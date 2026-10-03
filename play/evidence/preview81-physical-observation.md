# Preview 81 — physical Play update and existing-state restart

On3October2026 at09:33:30UTC, a physical API36 ARM64 phone reported
`com.myexternalbrain.chummer`, version81/`0.1.0-preview.81`, installed by
`com.android.vending` after a normal Play80-to-81 update. Font scale1.3 was
unchanged. This is a bounded observation, not complete SR5 Creation authority.

The exact [Internal AAB/source observation](preview81-internal-observation.md)
remains immutable. No rebuild, re-sign or second upload occurred.

## Identity and observed behavior

- Installed base APK:21,024,530bytes;
  SHA256`b6a2474e19a79e636fa07b2bcadc762c90cf864cff34206c767c0908d66f482e`.
- Independent offline keyless verification passed APKv2/v3 and source stamp.
  Expected Play App Signing certificate:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
  This is deliberately distinct from the AAB upload certificate. Two unknown
  additional-attribute warnings remain retained in the verifier output.
- First launch reached the existing Create page after its initial loading state.
  Read-only navigation reached the disabled Review and finalize card, which
  correctly required equipment review/save for this unfinished draft.
- Force-stop at09:36:48UTC was verified by absence of the old process. Launch
  at09:36:49UTC produced a different process and reopened the existing Create
  state. Ready hierarchy before/after and before the80-to-81 update was identical:
  `aaf46e7892abd696b90ac3a6cf8ee0c580cf1809508c1ca4daddf687b7ac38ef`.

No character choice, Save, Confirm, data clearing, sideload or device-setting
change occurred. This does not compare all private store bytes or establish a
new saved mutation, restoration of the deep navigation stack, physical execution
of the changed cash/finalization/receipt route, full-book quality, all-method
coverage, tablet parity or general beta readiness. The changed route's native
confirm/save/new-process evidence remains the separate synthetic API36 x64
diagnostic smoke recorded in the Internal observation; it is not relabelled as
physical Play81 evidence.

Private packet `creation-release81-20261003.FomglhkO` retains the installed APK,
observations and actual execution record outside served directories.
Install-verification logSHA256:
`df53247ea8af041fff009cb3ff4c98a8bc3b8dbdd739165ad5fb437238dca277`.
Base-signature logSHA256:
`e49dd3cd77df1ada8faee981495357958bd0bd0c01dc814237b4cab6d0d0c36e`.
