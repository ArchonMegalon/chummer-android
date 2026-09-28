# Preview 51 — Play Internal and physical recovery check

Authenticated Chummer Play Console readback at `2026-09-28T12:49:28Z` showed
`51 (0.1.0-preview.51)` **Available to internal testers**, Internal release 47,
track Active, one version code. This is browser evidence, not Publisher API
evidence. [Install/update](https://play.google.com/apps/internaltest/4700678198570024687).

## Delivered increment

Linked-runner recovery now uses explicit readable native colors and accounts
for the root account alias. Account loading no longer briefly offers a new link
before the stored link is resolved. The brighter Troll launcher/splash remains;
the store listing is unchanged.

Focused managed checks and the affected local Android build passed. A physical
API-36 ARM64 phone updated normally from Preview 50 through Play (`com.android.vending`),
retaining the existing runner at revision 1. The recovery page was readable at
the unchanged 1.3 font scale, correctly reported empty history and passed a
verified force-stop/reopen check. No application data reset or debug sideload
was used on the phone.

The installed APK signature was verified offline; its certificate matched the
installed Preview 50 certificate. It was **not** independently compared with
the certificate displayed in Play Console. This is not a full-book reader pass:
the physical fresh runner had no confirmed opening decisions or complete chapter.
The separate reader-entry fix and real-book test are later work, not Preview 51
evidence. No seven-journey, full beta, public, tablet or desktop claim.

## Exact artifact identities

- Android producer `fdcf862633dad4e9cba26bc3834db898cd5ce142`, PR 186 merge
  `b6a1b37c00b7329b2bac9e50990b3596c3ec0f86`, identical tree
  `6386a8004bea1d9ef75701a362c4217f096cdfc5`.
- Presentation seal `fde950c3a093d2d384180aa2a4baa5db4eeac934`;
  Core runtime `b19fc03123c43885c39859cdad0c4554ad46b7ff`, recipe/content
  `b865101d02eb24fb89f9ca4eb5b397e1acffae83`;
  Hub package producer `c77395de9f733427ef952c851f4a95b063cb5573`.
- Source-input receipt SHA-256:
  `3274b04ceb39b93cedbf71670d5e1641413640b80eb8500bed0ba8f262594442`.
- Unsigned AAB: 32,913,462 bytes, SHA-256
  `111d1ef6e8b657ae9af2445aa8466452c03b610ba2e08fb6c8330ca604ebcc64`.
- Signed AAB: 33,085,919 bytes, SHA-256
  `01f142f49cbe8a2c40cc8374699696b5d2ef21ee0b40ade6993476bbc651d2fb`.
- Existing upload certificate SHA-256:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Independent signed-verification receipt SHA-256:
  `63daa77c0e46c149472754378ca6638f2781bdc3e69364e7e437d36815cc4dc0`.
- Physical installed base APK SHA-256:
  `e951fadd374380e08ce7fd9c14a5a25ae7e3202f7c1d38c63cf7ccb3b9bf3c9e`.
- Physical installed signing certificate SHA-256:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.

The local keyless ARM64 Release build completed in 145.95 seconds, zero warnings
or errors, .NET 10.0.112, builder image
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Exact package/content, bundle/API/ABI, privacy, key hygiene and proof-exclusion
checks passed. Separate old-key signing and independent strict signature,
certificate and unchanged-payload checks passed. Dependency mode remains locked
package closure with explicit pinned Presentation source and Core content,
not package-only native assembly or hosted qualification.

## Retention and limits

Private packet `origin-release51-20260928.HdnvtVw4` retains the bytes and logs.
`PLAY_PUBLICATION.json` SHA-256
`2fb8807e7faff07f46757002cd09aceea06a8601b6e2f20ccede8f4f98e8dd97`
records the earlier availability stage, when physical installation was pending.
The later `PHONE_INSTALL.json` SHA-256
`c4bb0024bda82e52463d6230ba5d892abf512fae1e1821e7bfc009d8ed7ac0cb`
records the physical update and recovery check. Earlier receipts are unchanged.

Play reported the existing missing mapping/native-symbol warnings and no lost
supported devices. Production, audience, billing, account security, listing and
the old interrupted FirstBook job were not changed. Version 51 is consumed;
do not rebuild or re-upload it. [Preview 50](preview50-internal-observation.md)
and earlier observations retain their original time-bounded claims.
