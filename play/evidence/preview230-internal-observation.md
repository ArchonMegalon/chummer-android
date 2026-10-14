# Preview 230 — stale Calendar preview recovery

Available on Play Internal; physical Play installation not yet verified.
Console displayed **14 October 2026, 10:57 Europe/Vienna** for release 223.
The [exact observation](preview230-internal-observation.json) binds the source,
affected checks, signed bundle and retained private readback packet.

Android [#771](https://github.com/ArchonMegalon/chummer-android/pull/771) merged
with the tested tree. An unsaved Calendar review could previously lock the page
after an unrelated saved change. It now offers explicit discard of that stale
preview, preserving the runner and requiring a fresh preview and confirmation.
Applying, Applied, corrupt, replaced and owner-unresolved journals stay protected.
Core and Presentation are unchanged.

Local checks compiled 324 native sources with zero warnings/errors and passed
the actual Core/native regression, nine authority cases, four source/driver cases
and 886-key EN/DE/ES parity. The Release/Internal API36 x64 native route exercised
the stale preview, unrelated Karma save, explicit discard, fresh confirmation,
single Calendar save and three process restarts. Discard preserved all 37 saved
files; the final restart reopened the same revision21 Applied receipt with all
37 files byte-identical. No saved-state injection was used.

The ARM64 bundle passed local build/content checks, isolated original-key signing
and independent keyless signature/certificate/payload verification. Exactly one
upload and final confirmation reached Internal availability under the standing
2026-10-05 approval. Audience and public tracks were unchanged. Version230 is
consumed; do not rebuild/re-upload it.

This is a locked package closure with pinned Presentation source and Core
content, not a source-free package-only build. The diagnostics contract still
requires its exact retained local package. SystemUI boot trouble, startup/reload
reliability, a more minimal Calendar layout, physical230, full Career and the
complete live Origin journey remain open. No complete phone-beta claim is made.
