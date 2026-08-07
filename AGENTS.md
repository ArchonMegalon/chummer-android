# AGENTS

## Product boundary

This repo owns the Android host, Android-specific adapters, package recipe,
device tests, and Play delivery evidence for the full Chummer workbench.

It must reuse `Chummer.Blazor`, `Chummer.Presentation`, the canonical engine,
`Chummer.Ui.Kit`, and the `chummer-play` live-session shell. Do not copy rules,
DTOs, workbench components, live-session semantics, or hosted service truth.

Canonical product truth is in sibling repo `chummer-design`, especially:

- `products/chummer/ANDROID_APP_PRODUCT_SPEC.md`
- `products/chummer/ANDROID_WINDOWS_FEATURE_PARITY.yaml`
- `products/chummer/OWNERSHIP_MATRIX.md`

For codebase discovery, run the workspace vexp pipeline first. If this new repo
is not indexed yet, use targeted reads and commands only until indexing catches
up.

## Release truth

Never claim Play publication from a local AAB. Publication requires a
Chummer-scoped Play Console receipt and a successful internal-test install.
Never check in signing secrets, upload keys, service-account JSON, tokens, or
device identifiers.
