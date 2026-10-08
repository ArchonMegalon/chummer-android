# Karma dashboard after Save — 8 October 2026

The Creation toolbar saved the pending Karma runner, but retained a dashboard
session bound to the previous saved revision. Its next refresh could therefore
show the stale-draft warning for the document it had just successfully saved.

After toolbar Save, the dashboard now reloads the persisted Core authority only
while the same page appearance, workspace and displayed owner remain current.
It does not rebase an editable child draft, choose a metatype, confirm a purchase,
finalize a runner, replay Save, or relax Core's admission checks. A failed reload
retires the previous dashboard state and retains the guarded fresh-editor route.

## Focused local verification

- Production-backed managed native-control regression reproduced the old warning
  after actual toolbar Save (Core Open called once). With the fix it reloads once
  and remains ready; document content, auxiliary digest and content revision do
  not change, while saved revision advances to the current content revision.
- The same test injects a failed post-save read, verifies stale authority is not
  reused, then leaves/reopens the page and recovers without choosing or confirming.
- Managed and Release x64 native builds completed with zero warnings/errors.
- Fifteen creation-wizard source-contract tests and private-key hygiene passed.
- The retained synthetic API36 runner was updated in place with the SDK-test-key
  APK. Actual toolbar Save kept the dashboard ready; opening Karma foundation
  showed revision 1 / saved 1 and the enabled Metatype control. The other three
  synthetic runner files were unchanged.
- Force-stop verified the old process absent; a new process restored all four
  dossiers and reopened Karma foundation at revision 1 / saved 1. The saved
  runner hash and the other three file hashes remained unchanged across restart.

The private local packet retains the exact test logs and native screenshots.
The first emulator boot displayed a System UI ANR; its observed Wait control was
used once before the app route check. This is not a general startup performance
claim, a physical Play installation, a hosted suite result or full Karma parity.
The earlier intermittent cold-start readiness warning has not been attributed to
this Save defect and is not claimed fixed by it. Preview 139 remains immutable;
version 140 is the new candidate, not yet Play availability evidence.
