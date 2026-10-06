# Life Modules language choices — local native verification

The canonical Caribbean League language prompt accidentally admitted the
`knowledgeskilllevel` metadata value `0` as an answer. Core now excludes only
that metadata element from the option list, preserving raw rules metadata and
the genuine languages. No canonical rules data was changed.

## Actual inputs and checks

- Android behavioral/package head: `5c53c169229654b925e7fa7eb2f6b04df1b2fbed`.
- Core runtime: `fe305d1b6873d4904e794eab7ce28042389688a3`; recipe
  `c04f6b0a1bdc21f3fa54df78dd9f799743642e44`, normally merged through PR117.
- Presentation consumer: `39db1d414a43da341c64d617c32a07fd4052f052`;
  actual receipt SHA-256
  `70cb577a4474fb966e9f658d01c66768eb9f7df69f78ff7eb002a280f663d4e5`.
  The protected PR320 merge `2716b2805ffdce9d1cada50f1841a4ae54df6d0a`
  has the same tree `1400ae8c5778d08d17bf39c31aa515beb73f0227`.
  The original consumer receipt was not relabelled to that different commit.
- All 18 actual packages passed intake verification. All 331 canonical content
  files remain unchanged. Affected native dependency and MAUI builds passed.
- Focused canonical picker regression covers the four actual languages,
  required explicit selection, reviewed-answer reopening and stale confirmation.
  Affected Python checks: 219 passed, with 645 passing subtests.
- Local Release x64 SDK-test APK: zero warnings/errors, SHA-256
  `a3c71e9b5ffb89a6454206eace9c63b936266e9bb5968e3a65139489d898dee0`.
  The installed APK bytes were independently checked against that digest.

## Observed Android route

On the retained API36 emulator, a separate synthetic SR5 Life Modules runner
selected Human and Caribbean League / Greater Antilles. The actual native
language dialog contained exactly French, Spanish, Dutch and English, without
`0`. The second language dialog offered Creole, Lucimi and Taino.

Haiti, English and Creole were entered. Review displayed those answers;
reopening the answer form retained all three. One explicit confirmation then
advanced to the childhood choices. After force-stop, verified process absence
and cold launch in a different process, the saved runner reopened at the same
childhood stage. The post-restart check proves stage continuation, not a new
inspection of every persisted answer byte. Childhood was not confirmed; the
reader correctly required the remaining opening choices before a first chapter.
No new paid chapter or image was requested by this route.

The private packet `life-language-package-20261006.nDNO2rfl` retains screenshots,
full hierarchies, exact artifacts and initial failures. SystemUI ANRs, a prior
emulator system-server crash and transient null-root observations were **not**
counted as passes. The saved AVD later ran with four virtual CPUs / 6 GiB, under
the unchanged host session CPU quota. Selection saving and cold roster loading
were slow; this is not a responsiveness clearance. No data reset or extended
observer timeout was used.

This is a focused SDK-test identity smoke, not Play-managed ARM64 execution,
seven-journey qualification, complete Creation or general beta readiness.
Release AAB/signing/upload and physical installation remain separate evidence.
