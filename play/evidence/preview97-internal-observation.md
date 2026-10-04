# Preview 97 — responsive, cancelable account loading

Authenticated Chummer Play Console readback on 4 October 2026 at
14:02:25 UTC showed `97 (0.1.0-preview.97)` **Available to internal testers**,
Internal release 91, one version code, released at 16:02 Europe/Vienna.
[Install/update](https://play.google.com/apps/internaltest/4700678198570024687).
This is browser Console evidence, not Publisher API or physical-device evidence.
**Physical Play97 installation/update and live authenticated refresh remain
unverified.** Version97 is consumed; never rebuild or re-upload it. Preview96
and its evidence remain unchanged. No Production or tester-audience change.

## Change and focused verification

Manual account loading keeps an enabled **Loading… Cancel** action. A second
tap cancels rather than dispatching another refresh; leaving the page also
cancels. The entire credential/status/runner/group/chronicle read chain runs off
the UI synchronization context, with one 30-second cancellation budget and an
overlap guard. Previously only the continuation-list portion ran off the UI.
Admitted durable credential recovery still finishes under its existing safe
commit boundary; the read budget does not abandon an in-progress commit.

Canceled credential reads retain the linked owner. Failed/canceled refreshes
retain the last complete catalog and local runners. Original owner, revision,
generation and A-to-B-to-A checks remain. No account reset, automatic retry,
relink, local data deletion or credential migration was added.

The new regression failed against the original implementation because
credential/HTTP work ran on the UI context. The corrected managed build passed
with zero warnings/errors. Actual MAUI RunnersPage button cancellation,
departure and successful-completion cases passed, together with total-budget,
explicit retry, overlap, retained-catalog and credential wait/read cancellation
cases. Five existing catalog sequencing/owner cases, the existing actual
account/Core/Shell ownership and recovery suite, four responsiveness source
contracts, private-key hygiene and diff checks passed. Test transport was
synthetic; this is not Android device or live-provider execution. Tests preceded
only the project version96-to97 edit; the changed behavior bytes were unchanged.
The exact version97 source has its own ARM64 Release compilation below.

Hub logged one `grant-authority-unavailable` 503 at 13:34:00 UTC. Its underlying
cause and relation to the phone's exact blocked stack remain unresolved. This
release fixes the observed app loading design, not that server failure. No
server-side authentication checks were weakened or bypassed.

## Exact local artifact

- Android producer: `ad60c81dbe18ae42590cabd853bc11b21a06fd08`;
  main merge: `433605c3490e1739cd376fadc52da12d9b5c59ed`;
  identical tree: `e60c9a5808542514f96153ee225395e55c0f3ad4`.
- PR444 merged normally with source/safety and GitGuardian passing.
- Presentation seal: `2b2d63a3887e1d2813843bef958be448c7837c1a`.
- Core runtime: `5d1a1d74e9027895d68fa48162f310e663521d35`;
  recipe/content: `d498a45a4e18ad88109abc88789c271f752505e6`.
- UI receipt: `d09b305f3f540adb68b079f455bab8ebdfccc3617ee5f978a2c82bb5ba6e4588`.
- Unsigned AAB, 33,680,112 bytes:
  `b5c919ffc3304d81d263c8600de0c8ef49d0c4ccad6efa1f8da7d49537127ded`.
- Signed AAB, 33,852,710 bytes:
  `e6d12e4fcc4f11c2c41f015b8489474423e28775338058f1864c2fdda8f2b12d`.
- Existing upload certificate:
  `d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15`.
- Unsigned input/content receipt:
  `c8af9c0352b2deed07be1b3dcee8b8a6dc36f2d6bc363cf748d3b8bf43d4c96b`.
- Independent signed-verification receipt:
  `f4b89d9555e7f797ffd9a7861a4fd729e2a9047615559c7545d26a183ab4c539`.
- Private Console execution record:
  `f0a2dedbb3941f04d8a416321348e244821be7f2d20e17d4826a8e4d0f6b0f63`.
- Availability screenshot:
  `ab0f1c8f38103e68500f59106e02c8e91957f586db4d75c3e6b4104697fc4fea`.

Local offline Docker used .NET10.0.112, JDK17.0.20.1 and builder
`sha256:a174fae2f11575864da89e84bde682af7887e2e7213f54741764d8751e767eb5`.
Locked restore, exact exports, 18 packages, 331 embedded content files,
package/version/API24+/target36/ARM64, proof exclusion and credential hygiene
passed. Dependency mode is locked package closure with pinned Presentation
source and Core content, not package-only or hosted qualification. Separate
original-key signing passed independent keyless strict JAR, certificate and
equality of all1,783 non-signature payload entries.

One upload and one confirmed Internal rollout occurred under standing approval.
Supported-device coverage was unchanged. Existing missing mapping/native-symbol
warnings remain. No physical-device action, provider credit spend or uncertain
FirstBook replay occurred. Full SR5/Origin and general-beta completion stay open.

Private packets `origin-release97-20261004.ymJKBk1a` and
`account-loading-20261004.JTRV0Rf4` retain artifacts and evidence outside served
directories. No credentials or private runner data are public.
