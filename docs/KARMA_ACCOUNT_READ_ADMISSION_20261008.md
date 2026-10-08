# Karma opening during account hydration — 8 October 2026

Karma Open/Load dispatched synchronous Core reads to a worker, but did not use
the existing Android read-admission scope. The actual account service briefly
owns the same credential gate during local hydration. A valid, unchanged owner
can therefore be captured while Core's non-blocking lease is unavailable; the
dashboard then receives an unavailable result instead of the pending draft.

Both read-only entry points now use the existing scheduled Android read scope.
Waiting happens off the UI thread, is cancellable, and retains the exact owner
stamp. The normal post-read page, owner, profile, revision and digest checks are
unchanged. Confirmation and other mutations do not use this waiting scope.
There is no retry, timeout increase, account relink or automatic draft selection.

## Focused local checks

- A production-backed test holds the actual local account hydration inside its
  metadata read. Before the change, Karma Open returns unavailable immediately;
  the new regression fails at that exact assertion.
- After the change, Open and Load wait with a responsive UI context, then admit
  the unchanged Core draft. Cancellation of either read completes while account
  hydration remains held. All four cases pass, with zero managed build warnings
  or errors, no Hub requests, no confirmations and unchanged persisted bytes,
  content revision, saved revision and owner stamp.
- Existing dashboard Save/read-failure recovery, owner A→B→A during Open/Load,
  departed-page Open and canceled Open tests pass on the changed assembly.
- The 15 Creation wizard source contracts and private-key hygiene pass.
- The Release x64 native build passes with zero warnings/errors. Its distinct
  SDK-test-key app opens the existing pending Karma runner, keeps the dashboard
  ready after actual toolbar Save, and reopens the foundation editor after a
  verified force-stop/new process at revision 1 / saved 1. Metatype is enabled;
  no choice or finalization is implicitly applied. All four retained synthetic
  workspace file hashes remain identical before Save and after restart.

This reproduces a specific startup-contention defect. Earlier emulator packets
did not retain the failed Core Open result, so this is not conclusive attribution
of every historical cold-start warning or a general startup-performance claim.
Preview 140 stays immutable and available on Play Internal. Version 141 source
alone is not a signed bundle, Play availability or physical installation.
