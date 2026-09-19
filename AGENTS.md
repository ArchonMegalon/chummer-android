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

## Local execution is the default

User decision, 2026-09-19: build locally and host locally. Use the existing
local Android toolchain and Docker services for builds, focused tests, packaging
and hosting. Reuse passing results for unchanged inputs; do not wait for a
GitHub-hosted run to build or smoke-test a local candidate.

GitHub remains source synchronization/review. Do not add hosted builder or
signer jobs, GHCR helper-image publication or remote build storage as routine
delivery dependencies. Existing protected-merge checks still apply; never
represent local results as hosted checks or bypass protection silently.

Keep the existing upload key isolated from the build and verification stages.
Use a separate local signer. Never put credentials in images or served files.
Host services in local Docker and reuse existing ingress; new development
listeners are loopback-only unless wider exposure is explicitly in scope.
Clearly distinguish Debug APKs, signed release bundles and actual Play delivery.

## Release truth

Never claim Play publication from a local AAB. Publication requires a
Chummer-scoped Play Console receipt and a successful internal-test install.
Never check in signing secrets, upload keys, service-account JSON, tokens, or
device identifiers.
