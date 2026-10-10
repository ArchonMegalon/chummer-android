# Preview 182 — physical Play installation and bounded restoration

On 10 October 2026, 17:41–17:49 UTC, the existing Internal-test phone was
observed running the Play-installed Preview 182. This check did not initiate
or observe an in-place update transaction, sideload, reinstall or data clear.

- Package: `com.myexternalbrain.chummer`, version `182`, `0.1.0-preview.182`.
- Installer and initiating package: `com.android.vending`.
- Physical API 36, ARM64, font scale 1.3; observed routes were in English.
- Installed base APK: 21,024,530 bytes, SHA-256
  `efd17fdcebbea8c5aa58969d7f4362851d159c5fda91dc0ebdb7504f7bc945a9`.
- Expected Play signing certificate:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
- Offline signature log SHA-256:
  `c771726821bb8e862b096b66f0592b94ec862baa7c68ce199afa27f27e61e361`.
- Private observation receipt SHA-256:
  `220cbb9108a6ea1bdeb61a51bd6a0012f9bb54034b3384af1b862f5ad38edc89`.

The existing keyless Docker image, with networking disabled and the base APK
mounted read-only, verified APK v2/v3 signatures and SourceStamp. The signer
matches the established Play certificate, not the upload key. Two unknown
optional v3-attribute warnings remain in the retained log. Only the installed
base APK was independently signature-checked; this is not a claim of complete
split-payload equality with the retained release bundle.

## Observed route

The first cold launch displayed loading feedback and subsequently restored two
existing runner dossiers. The retained Priority runner opened its Skills page
without changing allocations. Selecting the retained Life Modules runner opened
its starting-decision page. Leaving and re-entering that page returned the same
visible state.

The application was then force-stopped. Absence of the old process was checked
through both package process lookup and `/proc`; launch created a different
process. The same selected runner returned to the Creation overview. Opening
Life Modules again restored the same visible starting choices and language
labels. The settled accessibility hierarchies match byte-for-byte:

- Creation overview before/after restart:
  `24bf2bc7c689f1cff292eee3deea7427a4ffe2f8766e01dc6fbc824b1eb05998`.
- Life Modules before re-entry, after re-entry and after restart:
  `1b52637005ab366cfc94b30e7eeb76013365e34445063583531b075ed3073bdd`.

This establishes bounded visible-state restoration, not equality of private
saved files. No decision confirmation, allocation, save, reading acknowledgement,
generation request or Career finalization was performed. The phone account was
unlinked; this does not test account login or linked-runner recovery.

## Limits and retention

Activity launch reported 1,222 ms on the initial cold launch and 734 ms after
force-stop. These are **not time-to-usable measurements**. The first observation
showed loading; a later observation, beginning roughly 18 seconds after launch,
showed the restored screen. Observation intervals and cached runtime inputs were
not controlled. No startup improvement, reload-defect closure or general
performance result is claimed.

The private `origin-worker-recovery-20261010.7Im4LZQX/physical-preview182` packet
retains the installed base APK, signature log, process observations, screenshots
and hierarchies. Screenshots precede hierarchy capture and are not atomic with
it. Raw character details, device identifiers and private account data are not
published. Owned temporary device XML files were removed only after checking
their retained copies. No new Android build, signing or Play upload occurred.

Complete live multi-chapter Origin reading/export, Creation-to-Career coverage,
fresh incident-to-operator diagnostics and broader beta readiness remain open.
The earlier [Internal availability and artifact record](preview182-internal-observation.json)
retains its original observation-time scope; this is a later, separate supplement.
