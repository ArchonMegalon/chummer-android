# Data safety source of truth

This worksheet describes the exact preview.7 AAB with SHA-256
`34b6b206b422e439e19e675e9f6ec849ed6b3c64b7db66852fdf3463ee4b509f`.
It is not a claim that Play review has accepted the answers. Reconcile the
deletion and privacy routes against the live service immediately before
submission.

## Artifact observations

- The app requests only `INTERNET` and `ACCESS_NETWORK_STATE` plus framework
  signature permissions.
- It does not request location, camera, microphone, contacts, advertising id,
  notification, or broad external-storage permissions.
- Android backup is disabled and cleartext traffic is disabled.
- Character files selected through Storage Access Framework are read only after
  an explicit user choice. Save destinations are likewise user-selected.
- Local app state is stored in app-private storage.
- No advertising or analytics SDK is included by the Android product head.
- The app does not submit crash logs, diagnostics, usage analytics, advertising
  identifiers, contacts, messages, photos, video, or audio.

## Data that can cross the device boundary

| Data | Trigger | Purpose | Handling posture |
| --- | --- | --- | --- |
| Linked account user ID | User chooses **Link account** and approves the installation in the browser | Account management and continuity | Chummer service; encrypted in transit; optional |
| Installation and device-link metadata | User starts or refreshes account linking | App functionality, account security, and continuity | Random installation ID, generated public key, device label, platform, architecture, app version, and release channel; Chummer service; encrypted in transit; optional |
| Group and runner-roster metadata | Linked user creates or edits a group, invite, or runner identity | Native campaign and group collaboration | Group name, visibility, role, runner handle, and membership metadata; Chummer service; encrypted in transit; optional |
| Chronicle Studio user content | Linked user creates or advances a Chronicle project | Produce the user-requested Chronicle artifact | Title, source summary, roster choice, output options, rights/consent/review flags, and handoff metadata; Chummer service; encrypted in transit; optional |

Preview.7 can download online runner/workspace payloads and Chronicle packets
that already belong to the linked account. It does not upload a runner file
opened from Android's document picker. Its native UI has no assistant-prompt,
support-report, crash-report, or diagnostic-submission flow.

Chronicle Studio is the only preview.7 lane that may disclose user-generated
content beyond Chummer. That happens only after the user opts into external
processing and the separate consent, rights, review, generation, external-send,
and upload approvals are satisfied. Declare this as sharing of **Other
user-generated content** for app functionality; do not imply that account or
device identifiers are shared with the provider.

Public help and policy links can open a browser. Account deletion itself stays
inside the native app; `https://chummer.run/account/delete` is the matching
public explanation and browser entry required by Play.

## Play Console answer posture

- Answer **Yes** to data collection because linked-account features transmit
  data even though the offline runner tools do not.
- Declare these data types:

  | Play data type | Collected | Shared | Required or optional | Purpose |
  | --- | --- | --- | --- | --- |
  | Personal info → User IDs | Yes | No | Optional | App functionality and account management |
  | App activity → Other user-generated content | Yes | Yes, only for the explicitly approved Chronicle external-processing lane | Optional | App functionality |
  | Device or other IDs | Yes | No | Optional | App functionality, account management, and security/fraud prevention |

- Do not select email address: sign-in occurs on the Chummer web account
  surface, while the AAB receives an opaque installation grant and never reads
  or submits the account email.
- Do not select Files and docs for local runner files. Preview.7 reads and
  writes them only through user-selected Android document-provider URIs and
  does not transmit those local files.
- Do not select location, financial information, health information, messages,
  photos/videos, audio, contacts, calendar, web browsing, app interactions,
  search history, installed apps, crash logs, diagnostics, or advertising ID.
- Data is encrypted in transit for network flows.
- **More → Account & privacy → Delete account** submits the linked-device grant
  and exact confirmation to Chummer. Credentials and cached account data are
  cleared only after the server returns an authenticated deletion receipt.
- The user can also remove app-private runners stored on that device. Public
  deletion information is available at `https://chummer.run/account/delete`.
- Account linking, groups, and Chronicle Studio are optional; the offline
  runner builder remains usable without them.
- Do not promise a fixed backup or tombstone retention window while the live
  privacy policy still marks that policy as review-required.
