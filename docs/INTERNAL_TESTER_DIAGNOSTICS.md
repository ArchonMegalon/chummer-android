# Internal tester diagnostics

Current source has **default-on automatic reporting for Internal test builds**
(owner decision, 9 October 2026), with an immediately available opt-out. The private
Hub intake is deployed; [Preview 165 is available on Play Internal](../play/evidence/preview165-internal-observation.md).
Physical Play installation and a real native report reaching the operator remain unverified.
Native page appearance/actions/refreshes and Life Modules story-readiness checks
record allowlisted technical events in app-private `diagnostics/technical-diagnostics.json`.
The settings page explains this and offers an explicit Android share action.
Build Internal candidates with `-p:ChummerDistributionChannel=internal`.
The default channel is `development`; `public` and `development` default off.
The app cannot discover its Google Play track. Do not promote the Internal AAB to
a public track unchanged: rebuild with the public channel and its own release checks.
The first-run Internal default is persisted only when no prior state exists.
An existing disabled state (including the earlier off-by-default implementation)
is preserved conservatively, even when its origin cannot be distinguished.
Corrupt/inaccessible state never counts as a fresh install. An Internal default
does not authorize sending after switching to a non-Internal build; an explicit
saved enable remains a separate choice. The Home screen links directly to the
diagnostic setting and discloses the default without claiming everyone has it on.
The separate settings action takes effect
immediately (not through the language/settings Save button). DE/EN/ES disclosures
explain the first-party destination, metadata, two-day private inbox and withdrawal.
On 9 October a synthetic HTTPS report was accepted, deduplicated and read back
through the private reader. This proves server intake, not native tester delivery.
A separate bounded operator worker is deployed. A clearly labelled synthetic
integration notification reached the existing verified private Telegram operator
chat. This is not a real tester report or proof of native end-to-end delivery.

Records contain app version, UTC time, coarse page category, operation kind,
process-local operation counter, elapsed time, outcome and coarse error category.
They never contain exception messages/stacks, account/runner/device identifiers,
tokens, character data, prose, screenshots, URLs or control text. A 30-second
operation observation is **not** an ANR classification: it can include legitimate
work or user interaction. Canceled actions are distinct from failures.

The journal retains at most 128 entries, prunes entries older than two days on
write/export, bounds its JSON file at 64 KiB and atomically replaces it using a
bounded temporary file. Writes are asynchronous and coalesced. Storage failure
does not block an app action; writes back off for one minute. This is best-effort
logging, not guaranteed crash capture: abrupt process death can lose pending writes.

`NativeProblemOutbox` enforces the delivery boundary:
the build-scoped default and saved preference, a maximum of eight metadata-only reports per
two-day window, category suppression across restart, a stable random submission
ID persisted before delivery, at least five-minute persisted backoff, and one send per
wake-up. Revocation clears pending reports. Unknown outcomes keep the same ID;
the adapter validates an exact receipt and uses idempotent intake. Failed actions,
rejected dispatches and observations lasting at least 30 seconds are eligible.
Slow remains a separate observation, never a crash claim. Busy taps, cancellations
and ordinary story-not-ready observations are not sent. This app-private state is
not a copied Hub wire DTO. Persisted enablement has a start timestamp; old preparatory
states without it fail closed. `NativeProblemReporter` additionally holds an exact
in-memory journal sequence boundary, excluding earlier events even if they
share the enablement timestamp. It never reimports the on-disk local journal.

The singleton reporter starts off-thread, checks every 30 seconds while the app
process can run, and emits at most one eligible queued report per five minutes.
It is not an Android WorkManager/foreground service and does not guarantee work
while Android suspends/kills the process. Durable pending reports resume on the
next app launch with their original IDs and backoff. Opt-out cancels an in-flight
request and clears the queue; a receiver may already have accepted that request,
so this is not a remote deletion promise. A failed consent save stops sending in
the current process and explicitly asks the tester to retry before restarting.
It must not claim that an unsuccessful disk write changed durable consent.

`NativeProblemHttpTransport` is registered for the preference-controlled reporter and locally tested.
It uses the canonical `Chummer.Control.Contracts` package identified in
`eng/android-diagnostics-contract.json`. That exact 38,598-byte package was built
locally from Hub `fef48022f`, not copied from DTO source, published as an asset or
represented as a protected package-plane seal. Retain the exact local feed when
building this preparation; the existing public feed does not supply it yet.

The adapter uses only the fixed first-party HTTPS diagnostic endpoint, no account
client, cookies, credentials or automatic redirects. It caps receipt bodies at
2048 bytes before deserialization, requires 202 plus matching report ID and a
valid receipt time, and persists server Retry-After across process restart.
Unknown outcomes keep the same ID. Permanent rejections are terminal, not success.
No production request was made by the synthetic managed tests.

Deployment and remaining verification:

- Preview 165's locked local build consumes the exact canonical contract above;
  the missing lock entry was regenerated without changing other dependencies.
  This is still a local package, not a protected published package-plane seal.
- The separate private Hub diagnostic reader is deployed from PR303 producer
  `b88e8dfd0b59292349cf8b76139b9b4d376d41bb`, with a mounted reader credential
  and a bounded, private two-day inbox. The inbox is temporary storage, excluded
  from backups; a host reboot can discard pending diagnostic reports.
  Account/Origin storage remains unchanged. Intake does not create Teable support
  cases or copy reports into incident history.
- Hub PR303 is merged as `033fb07adefcb102ec7fdb49e8be2c963eafd338`.
  The separate alert worker polls the private reader once per minute, with at
  most one attempt per 15 minutes, four per hour and twelve per day. It sends
  only allowlisted version/category/outcome counts, not IDs or raw reports;
  counts do not identify distinct users. Slow observations are not called ANRs.
  The first poll baselines existing reports without sending historical alerts.
  Two-day dedupe state is bounded and persisted before sending; uncertain sends
  are never blindly replayed after restart. Its private reader and scoped
  Telegram credentials exist only in private runtime mounts, never Android.
  Eleven focused worker tests and one synthetic live notification passed.
- Native remote delivery remains unverified. The native opt-out/restart smoke
  blocked HTTPS intentionally; the live server and Telegram probes are separate.
- The generated privacy disclosure is publicly visible at `https://chummer.run/privacy`
  and canonical Design PR33 is merged. Verify the actual Play scope/disclosures
  before delivery; the old preview.7 worksheet is not current authority.

This metadata-only request body does not imply that the HTTPS ingress has no
connection metadata. Review ingress/access-log retention and actual published
privacy/Play disclosures before enabling collection in a distributed build.

The reported reload recovery after leaving/re-entering a page is still not
reproduced. Instrumentation is not evidence of its cause or a claimed fix.
