# Preview 36 — observed Play Internal availability

At `2026-09-25T16:48:52Z`, the owner-authenticated Chummer Play Console showed
`36 (0.1.0-preview.36)` as **Available to internal testers**, Internal release 32,
track Active. Displayed release time: `25 Sept 18:48` (Europe/Vienna).
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is rendered Console readback, not Publisher API evidence. Physical Play
installation/update remains unverified and explicitly deferred by the owner.

## Delivered increment and affected-route verification

Career no longer performs a synchronous Core finalization-receipt load while
rendering its route marker. Receipt preparation runs off the UI synchronization
context through the existing workspace gate, retaining the exact displayed
snapshot, appearance generation, owner and revision/digest checks. Rendering
uses only that prepared receipt; stale results are rejected. The regression
first reproduced synchronous Core I/O during rendering, then passed with
off-context loading, pre-cancellation and post-read owner A→B→A rejection.

Core also reduces duplicate finalization allocations. The warmed managed sample
fell from 2,581,968,592 to 2,337,076,488 cumulative allocated bytes (9.5%). This is
not a claim about peak memory, Android latency or elimination of every ANR.
Core producer checks passed 687 managed tests, 17 owner and 15 inventory cases;
the exact UI consumer passed 858 main tests and focused checks. Android's
affected dashboard, Origin continuity, reader/adoption/HTML, Life Modules page
and Save cancellation/owner/ABA cases passed, along with 9 source regressions,
49 package-authority, 33 build-contract and 3 refresh-coalescing tests.

The corrected Debug APK built with zero warnings/errors; SHA-256:
`413adf53a9619439001f9998743ed61fe169a26a7377864638f7fc7b712cef9f`.
The actual local API36 x64 route exercised Career → readable adopted book/prose
→ Navigate Up → Career → Save, then verified process termination and a new
process restoring Career/book/prose/Up. The same synthetic runner remained at
saved/content revisions 7/7 with one identical finalization receipt. Its ID was
`sha256:f195f51ac2968926e86538685d19e254d81efc5b8e08442932057cb26e691187`;
the three-chapter reading-store SHA-256 remained
`dd5221806af7563b0fe7c615eeb5031897b655726d28ce1180fb5d95256da84d`.
All 330 catalog files in the corrected APK matched the admitted manifest.

No new app ANR was observed during this bounded smoke. Separate emulator-boot
SystemUI/input-method/Google-service ANRs were retained as infrastructure
observations. One initial book hierarchy was null; a screenshot showed the
book and a later settled hierarchy exposed its prose. The earlier app ANR was
input dispatch after Navigate Up; its late native trace did not prove a sole
cause. This smoke does not retest full finalization timing or repeat the older
native HTML-export observation. It is Debug emulator evidence, not a physical
test of the signed Release bundle.

## Exact source, artifact and signing

- Android producer `b5422310576ce31b4d77fe47d5f9ba363bb2b3b6`, tree
  `9a9b3ac053bdb7319d35dbc9accb858e8d363940`.
- PR121 merged normally at `2026-09-25T16:36:54Z` as
  `36830984290c0549e39f057bf4ddeb422d359388`, with the identical tree.
  Required hosted source/safety checks passed; they are not device tests.
- Presentation seal `fc614e38aa1ec7dfab5be7c8bac3eae542f7cfd4`, tree
  `253ba382f3dbe84b073223b06203d05c15f85c4a`; protected UI PR220 merged.
- Core runtime `3004181b467a0f77653ce97aef8cd16d4bc4a0ff`; recipe/content
  `b194b5eabb4691c7307a7c794177abdb8b282614`; protected Core PR80 merged.
- Hub producer `42d0bfbb117ab6250e8b0512dd92585916c6469f`, unchanged.
- Exact UI consumer receipt SHA-256
  `86c4e47114c0e2cb7b98081b3f728f6b09ba0fe8b61ae2364ce7f18dc25cca44`.
- Frozen source-input record SHA-256
  `05304a10ef5e47988c07384477b452b0c94c6ce7b307bb36c9d30465e21dbe8c`.
- Unsigned AAB: 32,016,579 bytes, SHA-256
  `d40ea9701b1bfd5d4653bf1b0d35016f31be9a7aa867ec965b04042530afdcb0`.
- Signed AAB: 32,189,033 bytes, SHA-256
  `c9a5981ca2ea71582ed58bb6e124a5c41c7464a6cfaaae9533e7399b0d865120`.
- Existing upload certificate SHA-256
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
  This is not the unobserved Play App Signing certificate.

The offline keyless Docker ARM64 Release build passed in 2:25.49 with zero
warnings/errors, SDK10.0.111 and existing toolchain image
`sha256:9795253a6f2218f9a757cefaea59ebd8a9d3e056fb6e4d9011b5179b42862be5`.
Assembly uses locked packages, explicitly pinned Presentation source and Core
content, not package-only APK assembly or ambient sibling discovery. Unsigned
inspection passed package/version/API24+/target36/ARM64, privacy, permissions,
proof exclusion across 165 managed assemblies, key hygiene and exact content.
Isolated existing-key signing completed at 16:42:17 UTC; independent keyless
verification at 16:42:37 verified strict JAR signing, the upload certificate and
unchanged non-signature payload. Verification receipt SHA-256:
`b7f3d156b926595a92efcb209ce7a9279413a7e78c0d56e1be9d8670eeacb207`.

Only those signed bytes were uploaded, once. Play accepted en-GB/de-DE/es-ES
notes, reported the existing missing mapping/native-symbol warnings and no
lost supported devices. No Production, tester, billing, account/security or
unrelated-app settings changed. Version36 is consumed; future uploads require
37 or a higher unused code. Preview35 and earlier artifacts/evidence remain
unchanged. The owned build/sign/verification containers and emulator are stopped.

## Limits and custody

The opening prose remains an operator-edited derivative; the next two chapters
are actual provider drafts. The prior provider observer needed manual status
recovery; unattended full generation and editorial prompt/length compliance are
not proven. Native finalization latency remains unmeasured for this increment.
No physical Play installation, production App Links, general beta, full method/
Career parity, SR6, audiobook, tablet, Full Editing, Windows or Rook completion
is claimed. No additional provider budget was used for this release.

Private packet `life-release36-20260925.VAX4UiZy` retains exact source/build/sign/
verification inputs and the Console screenshot; the focused native smoke is in
`life-finalization-allocation-20260925.rM0erOgB`. Actual `PLAY_PUBLICATION.json`
SHA-256: `3c72ee88ff95721d1fac54b68b189db579c1e0558fae70bc97f29938c6f3d8ff`.
