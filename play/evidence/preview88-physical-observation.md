# Preview 88 — physical Play update

On 4 October 2026 at approximately 01:11–01:14 UTC, the existing Internal-test
phone updated from Preview 87 to 88 through the normal Google Play Update
action. No sideload, reinstall, cache clearing or runner choice was made.

- Package: `com.myexternalbrain.chummer`, `88`, `0.1.0-preview.88`.
- Installer: `com.android.vending`; API 36, ARM64, font scale 1.3.
- Installed base APK: 21,024,530 bytes,
  `cd9363cd081605c0229b36c01b761cc6c49fdc6522e9fe63474992812d425863`.
- Expected Play signing certificate:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.

Offline `apksigner` verified APK v2/v3 signatures and SourceStamp. The exact
certificate matched the existing Play identity, distinct from the upload key.
Two unknown optional v3-attribute warnings remain in the verification log.

First launch restored the existing Creation runner. After verified process
death, a different process reopened the same visible Creation overview. Settled
before/after-restart accessibility XML is byte-identical:
`4acd868881532bd5ed30db47caeef1977d6d62f54ecb2e901b4dfeebd21b48c1`.
This is bounded visible-state evidence, not equality of inaccessible private
files or proof of a new saved creation mutation.

Initial blank Home and intermediate projection-loading observations are retained;
startup speed is not qualified. The public Stories tab reported unavailable.
That tab is not the retained Origin reader: this phone check does not establish
full prose/export on a real account or new FirstBook generation. The changed
reader has the separately scoped managed and synthetic API-36 evidence in the
[artifact record](preview88-internal-observation.md).

Private packet `creation-release88-20261004.wK69Rbku` retains the APK, signature
log, screenshots, hierarchies and process record. The owned loopback relay was
stopped; only its temporary device observation XML was removed after verifying
a retained copy. User services, accounts, runner data and signing keys remain.
No version-88 rebuild or re-upload occurred. Full SR5/Origin and beta completion
remain unclaimed.
