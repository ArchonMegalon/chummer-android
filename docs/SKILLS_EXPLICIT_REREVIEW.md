# Explicit Skills re-review

The Creation dashboard offers a separate **Check older Skills choices for
re-review** route when ordinary Skills authority is unavailable. Opening that
route performs a read-only Core capability check. It does not unlock the ordinary
editor or silently migrate a runner.

Core owns recognition of the saved pre-TalentAccess draft, the current source
policy, all costs and budgets, the historical/current comparison and the atomic
confirmation. Unknown history, changed source or upstream authority stays blocked.
This is not a general migration for every historical draft format.

The native page displays historical and proposed ratings, specialization/native
language choices, costs, removed rows, source anchors and blockers. Users may
remove or restore individual rows, adjust ratings, select specializations or
languages, and propose choices from the Core-permitted catalog. Blocked
intermediate corrections stay visible, but cannot be saved. DE/EN/ES resources
cover the phone copy; rule labels and source identities remain Core-provided.

The final confirmation dialog applies only the displayed comparison. The
coordinator independently reprojects it, then calls the separate Core re-review
transaction using a deterministic preview-bound key. The prior receipt chain
and unrelated wizard/character data remain unchanged. Once a save has a receipt,
a failed read or host refresh cannot offer another Apply: reopening reloads the
runner, while exact Core retry resolves the existing receipt without a new write.

Read/projection/commit work runs off the UI thread. Workspace revisions, page
departure generations, the existing dashboard navigation lease and the ordinary
page action gate fence stale responses and overlapping actions.

## Verification scope

`SkillsReReviewNativeRuntimeTests` uses the actual native/Core/Presentation
assemblies and a real file store. Its controlled serialized pre-policy fixtures
are derived from the canonical source catalog, not historical Play exports.
The cases cover unchanged legal choices, multiple incremental repairs, declined
review, forged comparison reprojection, detached/departed actions, a post-commit
read failure, presenter/shell refresh, cold reopen and idempotent replay.

Managed page/control tests do not establish Android handler behavior, native
alerts, process-death upgrade behavior, APK authority or Play publication. Those
remain separate gates, as do package sealing and cross-repository integration.
