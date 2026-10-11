# Preview 191 — physical Play update and restoration

11 October 2026, 13:28–13:30 Europe/Vienna. Scoped physical ARM64/API 36 phone,
ordinary `com.myexternalbrain.chummer`, not the separate SDK-test package.

The Internal listing offered Update while 189 was installed. One update tap
started a Play-managed download; the listing then offered Open. Package metadata
reported version code 191 / `0.1.0-preview.191`, ARM64, installer
`com.android.vending`, updated at 13:28:40. No sideload, uninstall or data clear.

Independent offline keyless `apksigner` verification passed v2 and v3, with the
expected Play App Signing certificate SHA-256:
`035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
Pulled base APK: 21,024,530 bytes,
SHA-256 `d7ea35aa8e5863f6fe7ce3c43251d960818636ae61e81c183b54c158c2b89490`.
This authenticates the installed base APK, not equality of every delivered split
with locally generated splits.

Open restored the retained runner sheet and three dossiers. One force-stop
verified the old process absent; a different process launched cold. Its hierarchy
again showed the same retained runner and Career sheet. The first cold screenshot
still showed loading: screenshot and hierarchy are successive observations,
not simultaneous frames. The later `cold-ready` screenshot and hierarchy both
show the restored sheet. No character mutation, save or provider request was
dispatched by the test. Private character-file equality is not asserted.

Private packet: `wizard-forward-delivery-20261011.y44ZpY8o/physical`.

- Installed verification: `ba1ea81e765beb76ab4674f890776975734eac11c8d517ed500e613ea869d386`.
- Process restart receipt: `b7c771f03d246ca14ea8ac804aa1d5a24b2a37d4e0e45aa810bb5909ef138933`.
- Update offer hierarchy: `e54a9bfcf1dd6ed8e1fb7b358b3f2464d831b17d5b3c174825c139fc0d92d327`.
- Open-after-update hierarchy: `0599ff66d237c22294555592503eee706a40db5844488532220b2f21a51a7cc0`.
- Cold-ready hierarchy: `639624dcd7a98f64f455d18afa0b4beda307111a29a052cbdf6ec78ae8cb39a9`.
- Cold-ready screenshot: `8a9fd16480cca34a5d6f7eff66da8cea55c3d5ee046cd361f3b958ea0048573f`.

The changed Attribute→Skills and Skills→Qualities mutation routes retain their
separate physical SDK-test acceptance from PRs 691/692; they were not replayed
on this Play-installed binary. This is update/restoration evidence, not complete
Creation/Career/Origin acceptance, a startup benchmark, an exhaustive account
recovery test or fresh incident-to-operator diagnostics evidence. Existing
FirstBook uncertainty remains fenced. Version 191 is consumed; no rebuild or
additional upload is needed to retain these observations.
