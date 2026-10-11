# Compact attribute review — bounded local acceptance

11 October 2026, Europe/Vienna. This is a local SDK-signed test APK observation,
not a production AAB, Play release, physical-phone acceptance of this change or
complete Creation/Career qualification.

## Change and inputs

Product commit `f22c30e15caac2921af2ba08648ee9b05c4b9e30`, tree
`d834989d1228a4f807b8f06d14978ed7f5f23e3c`. The later physical-Preview186
documentation commit does not change these product inputs.

Review budgets share one card; all thirteen projected attributes share another,
with large bold values and wrapping nonzero costs. Zero-cost detail rows are
omitted. Disabled attributes and exact budget/accessibility information remain.
Core calculations, immutable preview binding, confirmation, owner/ABA, stale
revision, persistence and no-replay guards are unchanged.

The current local intake10/cache14 graph and pinned Presentation
`692cce2d69f3382a907f81092807b4df813498ae` were reused. No shared Core or
Presentation source changes were made.

## Verification

- The new actual MAUI/Core display regression fails on the original product
  source and passes with the change: EN-GB, DE-AT and ES-MX, with and without
  special-point/Karma costs. Rendering does not mutate the saved document.
- Existing attribute-save and special-attribute tests pass, including uncertain
  outcomes, duplicate work, departure, owner ABA, separate pools, caps,
  Mystic Adept/Adept/Technomancer, Priority/Sum-to-Ten and cold-store reopening.
- Eight attribute source-contract checks and private-key hygiene pass.
- Managed and native builds complete with zero warnings/errors.
- The exact x64 Release/Internal APK was independently certificate-checked;
  the installed base APK matches its SHA-256:
  `05ed6b00c0843fa5a752e2b7edcb1f92fd78b94e2ee254f8c8ee9bace57dae25`
  (37,657,192 bytes). This uses the SDK diagnostic key, not the upload key.

## Actual API36 emulator route

The isolated test app used German and 130% font scale. One new, byte-verified
Core-created synthetic Priority workspace was added without overwriting the
four retained workspaces. Its actual pools were normal 20, special 9 and Karma
25; one special point was already spent on Edge 3.

Normal UI actions selected that runner, opened Attributes, jumped to special
attributes, increased Edge once to 4, opened Review and saved once. Screenshots
show readable compact budgets, every projected value and the nonzero two-point
Edge expenditure. The UI first showed disabled saving feedback, then the
explicit saved confirmation. The workspace advanced exactly from revision 3/3
to 4/4; the other four files remained byte-identical.

After Back and a verified force-stop, process 2578 was absent before the one
cold launch of process 5279. The same selected runner and five dossiers returned.
Reopening Attributes showed Edge 4 and remaining pools 20/7/25. All five saved
files matched the post-save bytes; no mutation was replayed on reopen. The
runner remains in Creation, with finalization effects unapplied.

The retained private packet is `attribute-review-20261011.rkVTYmmn`.
`NATIVE_STATE_RESULT.json` SHA-256 is
`cea2dbc89ae7af1d2865d570515ba5b5cb68197e43a07309b75ab9d3ec315faa`.
Budget screenshot SHA-256:
`02a46660e13e225cf8b15e49d1c7d085a0cdc273f8957368a0a393905b1c21fd`.
Attribute screenshot SHA-256:
`da33dd26fdf9b31d7f0247db32436e6c75c18d3bb1daf14e2d1c398b57cbccfa`.

## Limits

The initial emulator boot had a SystemUI ANR and host cgroup memory pressure;
only the owned emulator's limit was raised from 8 to 10 GiB. Slow initial
restoration and transient null-root hierarchy observations are retained as
failures, not passing evidence. No startup speed improvement is claimed.

The isolated test UID had IPv4/IPv6 egress blocked. The app was stopped and those
exact temporary rules and byte-verified staging files were removed afterward;
saved runners and private evidence remain. No provider generation, production
signing or upload occurred. The physical phone stays on Play Preview186.
