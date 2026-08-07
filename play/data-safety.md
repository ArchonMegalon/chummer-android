# Data safety source of truth

This document is a disclosure worksheet, not a claim that Play review has
accepted the answers. Reconcile it against the final signed AAB and production
service routes before submission.

## Artifact observations

- The app requests only `INTERNET` and `ACCESS_NETWORK_STATE` plus framework
  signature permissions.
- It does not request location, camera, microphone, contacts, advertising id,
  notification, or broad external-storage permissions.
- Android backup is disabled and cleartext traffic is disabled.
- Character files selected through Storage Access Framework are read only after
  an explicit user choice. Save destinations are likewise user-selected.
- Local workbench state is stored in app-private storage.
- No advertising or analytics SDK is included by the Android product head.

## Data that can cross the device boundary

| Data | Trigger | Purpose | Handling posture |
| --- | --- | --- | --- |
| Account identifiers and device-link state | User opens account/device linking and signs in | Account management and continuity | Chummer service; encrypted in transit |
| Character or campaign content | User enables sync or invokes a connected workbench operation | Sync, continuity, or requested processing | Chummer service; encrypted in transit |
| Assistant prompt/context and generated response | User invokes an optional assistant feature | Provide the requested assistant result | Chummer service/provider lane; encrypted in transit |
| Support report and user-selected diagnostics | User explicitly submits a support case | Support and crash investigation | Chummer support service; encrypted in transit |

Public help, community, account, support, and live-play links can open a browser
or Chummer web surface. Their web disclosures must be included in the final
Play answer when the app initiates those flows.

## Play Console answer posture

- Data is encrypted in transit for network flows.
- Local deletion is available through workspace/document controls and Android
  app-data removal. Account deletion must link to the production Chummer
  account-deletion route before production review.
- Do not answer “no data collected” merely because there is no analytics SDK;
  optional account, sync, assistant, and support flows can transmit user data.
- Complete the exact Play data-type, purpose, required/optional, sharing, and
  retention answers only after the production routes and privacy policy are
  verified.
