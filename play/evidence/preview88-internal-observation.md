# Preview 88 — saved Origin reader

Authenticated Chummer Play Console readback on 4 October 2026 at approximately
00:58 UTC showed `88 (0.1.0-preview.88)` **Available to internal testers**,
Internal release 83, one version code, released at 02:58 Europe/Vienna.
This is browser readback, not Publisher API evidence.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
Physical Play88 installation is not yet verified. Version88 is consumed: never
rebuild or re-upload it. Preview87 and its physical evidence remain unchanged.

## Change and exact artifact

The Origin reader now renders admitted saved full text before waiting for a
later unfinished chapter's status. Status reads are serialized per appearance
without holding the page action gate across network I/O, keeping EPUB export
usable. Old appearance callbacks cannot release a newer read. Initial text-read
and illustration-watch ordering, owner/source/digest checks, consent, no-replay
and persistence contracts remain unchanged. Product PR415 and version-only
PR416 merged normally with required checks. No rules or dependency change.

- Android producer: `4589a5707ecffd6b5f1374ea6d62f05caac1a4eb`;
  main merge: `b9c6005d0df2e36c7c3e6a5ca31fe532626a777b`;
  identical tree: `6a357df2a0b9d14892ad8bbbcf65909a04b4f707`.
- Presentation seal: `6cae599a89301a290d13e0d7b0c46fc6f29c302c`.
- Core runtime: `9fb784271f3f0e0cd926565b148ff31bd7ca6565`;
  recipe: `34b631541d3f0b7be5a5c99385164a26d535aed1`.
- UI verification: `9de71432b1e7d16c189054ed38e9e67a783697bd50862e4b84d6330386036183`.
- Unsigned AAB, 33,626,333 bytes:
  `95e74d108d1b1236e3127c6a0dc52e4b777ce20ca41336d342fe2642b8bde8af`.
- Signed AAB, 33,798,983 bytes:
  `44ac86f9cd226680878771be21b8d847e7db1d3075c32779a0a2345e5067e7dd`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `18a7ff54e529c78931794f852b9584dff484d36929505e871728dbe2075d395d`.
- Independent signed verification:
  `f76455d795fdda07e06df4ebd0d89d941cc21fae310a9a34a1379df31219fdaa`.
- Private Console execution record:
  `01f19fc929891f6f17374116c1a0159f23f699881ae19a7f7a702109988d540b`.
- Availability screenshot:
  `a19a56568de25d76ce26a083958d475ee23f337fce574fb54bcce64a8fc4b50f`.

The offline local keyless ARM64 Release build used .NET10.0.112, JDK17.0.20.1
and image `sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports, package authority,331 embedded content files,
manifest/API24+/target36/ARM64, proof exclusion and credential hygiene passed.
This is locked package closure with explicit pinned Presentation source and
Core content, not package-only or hosted runtime qualification. Separate signing
passed independent keyless strict JAR, certificate and all1,783 payload checks.

## Focused verification and limits

The actual-MAUI baseline reproduced saved prose hidden behind pending HTTP.
Regressions now verify visible complete saved text and successful EPUB while a
status read is held, serialization, cancellation/departure, owner ABA, no extra
generation/acceptance and no runner mutation. Existing Life Modules page tests
also pass, including Career, retained book, HTML/EPUB/images and Save boundaries.

The isolated API36 x64 Release diagnostic APK, SDK test-key signed, is
`370fd6fe7b2470250060af2404da561260f2822f49dbc39959fb91a02e65f419`.
At font scale1.3 the exact saved synthetic prose was readable, exported once
through native DocumentsUI, and reopened after verified process death in a new
process. Runner and reading JSON were byte-identical. EPUB
`98af25423665948caf5f31c0f686d66bdb9096a45552794b8ecc8699a06ea259`
contains the full acknowledged synthetic chapter and no pending placeholder.
This short fixture does not prove a newly generated FirstBook book. Network
concurrency is managed-test evidence; native offline proof is not live-provider
or physical Play coverage. Results are reused only for unchanged runtime inputs;
the candidate adds version metadata and receives its own ARM64 artifact checks.

One exact upload and Internal rollout occurred under standing approval. No
Production, audience, listing, billing or security change. Missing mapping/native
symbol warnings remain. Emulator System UI startup ANR, slow transitions and one
null DocumentsUI observation are retained limitations. No full SR5/Origin,
all-method, seven-journey, tablet, performance or general-beta completion claim.
The full original goal remains active.

Private packets `creation-release88-20261004.wK69Rbku` and
`origin-reader-local-text-20261004.Y7sggTcG` retain artifacts and observations
outside served directories. No private runner/device data or credentials are
published with this record.
