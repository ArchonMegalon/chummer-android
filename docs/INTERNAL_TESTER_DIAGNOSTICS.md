# Internal tester diagnostics

Current implementation is **local only**, not automatic central reporting.
Native page appearance/actions/refreshes and Life Modules story-readiness checks
record allowlisted technical events in app-private `diagnostics/technical-diagnostics.json`.
The settings page explains this and offers an explicit Android share action.
No background upload or operator notification is enabled.

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

Remaining central-delivery work:

- Admit the canonical Hub support contract through the exact dependency graph;
  do not duplicate a Hub DTO or reuse raw desktop diagnostics/ambient credentials.
- Complete the private Hub diagnostic-reader configuration. The read-only live
  probe on 9 October returned 503: internal crash automation auth not configured.
- Add bounded consent/disclosure-aware submission and verify actual private
  intake/readback. Never embed the private reader credential in Android.
- Reconcile current privacy/Play disclosures before remote collection; the old
  preview.7 worksheet is not current disclosure authority.

The reported reload recovery after leaving/re-entering a page is still not
reproduced. Instrumentation is not evidence of its cause or a claimed fix.
