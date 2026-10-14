# Preview 231 — simpler Calendar review and save

Available on Play Internal; physical Play installation not yet verified.
Console displayed **14 October 2026, 12:00 Europe/Vienna** for release 224.
The [exact observation](preview231-internal-observation.json) binds the source,
affected checks, signed bundle and retained private readback packet.

Android [#773](https://github.com/ArchonMegalon/chummer-android/pull/773) merged
with the tested tree. Calendar now leads through input, review and a plain saved
receipt. Inputs are hidden during review. **Change preview** restores them and
requires a fresh review; **Save change** asks for native confirmation and checks
that the page, session, preview, journal and current runner binding still match.
Readable week labels replace machine IDs in the ordinary ready-state UI. Exact
typed identities, atomic persistence and uncertain-outcome guards remain intact.
Core and Presentation are unchanged.

Focused managed checks compiled 324 actual Core/native sources with zero
warnings/errors and exercised the real buttons/dialogs, cancellation, review
replacement, add/edit/delete, confirmation after page departure, and stale-review
recovery. Calendar driver cases, appearance cancellation and 901-key EN/DE/ES
resource parity also passed.

The Release/Internal API 36 x64 native route used ordinary controls and a retained
synthetic runner. Canceling and revising a review preserved all 37 saved files.
A verified new process restored the revised preview and required fresh
confirmation. One save advanced revision 21 to 22, changing only the chosen
week's notes; other parsed character values and all 36 unrelated files were
unchanged. A second new process restored the saved receipt, with all 37 files
byte-identical to the saved snapshot. No saved-state injection was used. This
was a separate SDK-test application/key, not a physical or Play-installed app.

The ARM64 bundle passed local source/content, manifest, proof-exclusion and
credential-hygiene checks, isolated original-key signing and independent keyless
signature/certificate/payload verification. Its signed SHA-256 is
`7894ce9327d737f752e539326b2363a0d4fdfe125abead12e311360459ae8bfe`
(36,462,480 bytes). Exactly one upload and final confirmation reached Internal
availability under the standing 2026-10-05 approval. The existing tester audience
and public tracks were unchanged. Play reported 8,738 supported phones and no
newly unsupported phones, with mapping/native-symbol warnings retained.
Version 231 is consumed; do not rebuild/re-upload it.

This is a locked package closure with pinned Presentation source and Core
content, not a source-free
package-only build. The diagnostics contract still requires its exact retained
local package. SystemUI boot trouble, startup/reload reliability, physical
Preview 231, full Career and the complete live Origin journey remain open.
No complete phone-beta or whole-UI minimalism claim is made.
