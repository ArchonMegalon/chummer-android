# Visible blocked runner-switch feedback

Local scoped result, 8 October 2026. Preview144 reproduced a real usability
failure: Core correctly rejected switching away from an unsaved runner, but
Home displayed the notice below a long roster and included the workspace ID.
The switch appeared unresponsive.

Preview145 keeps action feedback above the roster scroller. The exact known
unsaved-switch notice is translated into English, German and Spanish without
the workspace ID. A guarded **Open current runner** action returns to the
current editor, where the same readable notice is used. It does not save,
discard, retry the switch, or change Core ownership/admission/persistence rules.
Unknown notices are preserved rather than silently suppressed.

## Focused checks

- Production-backed managed regression demonstrates the original missing-notice
  failure, then passes actual switch refusal, dirty-runner retention, unchanged
  stored bytes, EN/DE/ES Home and return-page copy, and retired callback rejection.
- Existing Home initial-frame/loading/departure/reappearance regression passes.
- Managed and native diagnostic builds report zero warnings/errors; ten source
  scope tests, resource checks, private-key hygiene and whitespace checks pass.
- Final API36 x64 smoke selects another saved runner, observes the readable
  refusal, scrolls the roster while feedback remains pinned, and opens the same
  current runner on Create with Save visible and no GUID in that notice.
- Both settled screenshots were visually inspected. All seven saved workspace
  file hashes remain identical. No Save, discard, delete, finalization or provider
  generation operation was needed for this UI-only route.

Producer `90b6ae8006db9e7bf16a9a71d0bc9b3a64caa0e1`; tree
`a858ecaaf3b5187795a306229f5d97528b27c839`. Protected PR571 merged normally as
`3d8e6033298848e9bbd522a851f6f76bab15df6e` with the identical tree.
Final installed diagnostic APK SHA-256:
`1d22c0203e3ef73eff82d82925c546f8beeae2f4208472b62e789e536a82173d`.
This is the separate SDK-test-signed x64 package, not the Play ARM64 artifact.
Native packet result SHA-256:
`738c90615f1eb7c933045df723cf527348c65ba99f73215dc9e81ae6dbe075c1`.

The emulator needed the observed System UI Wait control during boot, and app
startup was slow. This is not a clean-cold-start/performance pass. The initial
145 smoke exposed the raw notice on the return page; only the final changed-input
smoke above covers that correction. The first managed setup attempt failed in
its fixture and is not counted as the original-product negative.

The intended combined Karma Contacts/Lifestyle/Career smoke was not completed;
this narrower concrete blocker was fixed first. No all-method, full Career,
physical-device or finished-app claim. The owned emulator was stopped afterward.
Per-chapter in-app/EPUB illustrations are unchanged; existing scoped reader
evidence is reused only for those unchanged inputs.

Later on 8 October, the combined Contacts/Lifestyle/Career route passed using
the same final diagnostic APK, including process restart and exact saved-byte
comparison. See the separate [combined route result](KARMA_CONTACTS_LIFESTYLE_CAREER_20261008.md);
the narrower feedback-only observations above remain unchanged.
