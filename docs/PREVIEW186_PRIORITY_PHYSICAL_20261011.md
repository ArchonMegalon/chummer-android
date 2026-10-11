# Preview 186: bounded physical Priority acceptance

11 October 2026, Europe/Vienna. This is a normal UI test of the already
Play-installed Preview 186, not acceptance of subsequent source changes.
The installation/certificate boundary remains in the existing Preview 186
publication and physical-install records. No sideload or application-data reset.

A new explicitly synthetic runner, `Priority186Audit` / `P186Test`, was created
through New runner. The existing two dossiers were retained. The tested choices:

- SR5 Priority: Attributes A, Skills B, Resources C, Heritage D, Talent E.
- Elf / Mundane, assignments explicitly confirmed and saved.
- Three normal Priority points allocated to each of the eight normal attributes:
  Body 4, Agility 5, Reaction 4, Strength 4, Charisma 6, Intuition 4, Logic 4,
  Willpower 4. Edge 1; no special points spent and no Karma spent.
- The displayed budgets reached 24/24 normal points, 0/0 special points and
  0/25 Karma spent. Another normal-point increase was disabled.
- Explicit Review -> Save attribute choices, followed by the actual saved message.
- Force-stop was checked before one relaunch; the process changed from 32508 to
  18130. The same test runner returned, with three dossiers and Skills as the
  next step. Reopened attribute observations retained the above normal values
  and budgets; some values were read through accessibility descriptions while
  their rows were partly outside the screenshot viewport.

The snapshots are sequential screenshot/hierarchy observations, not atomic
first-frame or latency measurements. Physical private saved files were not read;
this establishes observed UI restoration, not byte equality of private storage.

## Retained private observations

| Observation | SHA-256 of retained JSON |
| --- | --- |
| Explicit save result | `434723cb143f1d6d8a9eb262dee8b6aa2c23b27424bc0d892a74ab6532c4bb56` |
| Restored runner | `95d32012f8aa18eab9b82b27f8aa22a01a406a61eccf743f0a5af0d39b560a71` |
| Reopened attributes/top | `18a408d686903088756b72743f720b1e4a609a5eab6c2e9d5a90e727d06ce5cd` |
| Reopened attributes/middle | `d4f68b64e0ca5d19832c8a37758560687bcad38337b391e0cb609389c75e48b1` |
| Reopened attributes/lower | `fc4da84a1c9e29c2826b0178d73016275927e159d32582d99cc862a5d171e20c` |

## Findings and limits

The attribute review needed several long scrolls through repeated zero costs
and large individual cards before Save became visible. That motivates the
separate compact-review fix; Preview 186 does not contain that fix.

Two very rapid Body taps produced one observed increment. Single admitted taps
produced correct values; no double-spend or incorrect rule result was observed.
The exact cause of the rapid-tap behavior was not diagnosed by this test.

This is **not complete Priority Creation**: Skills, Qualities, Resources/Gear and
Career finalization were not completed. No Life Modules decision, new chapter,
reading acknowledgement, book export or provider request occurred. The synthetic
runner is retained for continuing the remaining Creation route. The original
uncertain FirstBook successor remains separately fenced; no paid replay.
