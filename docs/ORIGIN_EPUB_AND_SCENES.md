# Private Origin reading and EPUB export

The native reader displays the complete returned chapter, not its rules summary
or decision draft. While it is unavailable, the reading status describes the
pending work; uncertain outcomes do not authorize another paid request. A
completed chapter can be read before acknowledgement. **Save book as EPUB**
and HTML export use the selected reading edition, not an unaccepted rewrite.
EPUB 3 includes ordered navigation, full paragraphs, Unicode text and offline
illustrations. It uses the reading application's colors and font settings.

## Scene illustrations

For a consented illustrated book, the current route requests an illustration
automatically through the private Hub/Media bridge. The user does not have to
download an image, select a local file or approve each insertion separately.
The existing book's external-processing consent, accepted text, current account
authority, allowance and provider receipt remain required. Uncertain paid work
is reconciled rather than blindly repeated. Automatic rendering is not public
provider publication.

The book keeps an app-private copy, displays it in the native reader and embeds
the exact bytes in EPUB and HTML, without remote image URLs. Limits are one
image per selected chapter, 128 chapters, 4 MiB/4096 pixels per image and 16 MiB
of images per book. The total byte limit still applies: this is not capacity for
128 maximum-size images. Export uses Android's Storage Access Framework; broad
storage permission is not required.

Images bind to the private owner/workspace namespace, canonical chapter digest,
and exact currently displayed text. A changed chapter or selected rewrite does
not silently inherit an old illustration. Cold read checks byte identity; writes
are atomic and reject stale editions, canceled admission and owner transitions.
Invalid optional artwork leaves the text readable, with an explicit text-only
export warning; the unreadable archive is not overwritten.

## Observed scope and remaining work

On 29 September 2026, a diagnostic API-36 x64 app displayed a real accepted
2,641-word FirstBook chapter. Opening its reader triggered one successful
1min.ai scene; the image was automatically inserted. Real SAF EPUB export
contained all 149 paragraphs and the exact 1536x1024 PNG. Force-stop/reopen and
a second export preserved chapter, image and book identity without a new
provider job. Only the exporter-generated modification time and ZIP timestamps
changed. Hub PR279 removed duplicate accepted-chapter composition that had
exhausted the native request deadline; fresh authorization checks remain.

This was one editorial-assisted chapter. It does not establish unattended prose
quality, consistent likeness across multiple chapters, Phygital+ execution or
complete book delivery. The same-character continuity requirements still need
multi-chapter product verification. A server-side correction is not a new AAB.

Focused managed tests cover full EPUB contents, long Unicode prose, exact image
bytes, 8/9/128-chapter capacity, malformed/oversized images, cold reopen, stale
text and owner isolation. Synthetic capacity tests are separate from the real
one-chapter smoke above. Neither proves a physical Play installation. Actual
release and Play availability remain in the publication evidence, not here.
