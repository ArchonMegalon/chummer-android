# Private Origin reading and EPUB export

The native retained-book reader offers **Save book as EPUB**, alongside HTML.
EPUB 3 includes the selected reading edition, an ordered table of contents,
paragraphs and Unicode text. It uses the reading application's colors and font
settings. An unreviewed proposed rewrite does not replace the exported story.

## Scene illustrations

The **Chapter illustration** action imports an already downloaded PNG or JPEG,
for example a scene rendered with Phygital+ or OneMinAI. Enter a description,
choose the file, inspect the preview, then explicitly use it in the private book.
Selection alone does not save it. This action does not generate art, call a
provider, spend credits, or establish provider attribution.

The book keeps an app-private copy. It displays that image in the native reader
and embeds the exact bytes in EPUB and HTML, without remote image URLs. Removing
the book's copy does not delete the original download. Limits are one image per
chapter, eight per book, 4 MiB/4096 pixels per image and 16 MiB of images per book.
The existing Android Storage Access Framework picker validates and decodes the
selected image; broad storage permissions are not required.

Images bind to the private owner/workspace namespace, canonical chapter digest,
and exact currently displayed text. A changed chapter or selected rewrite does
not silently inherit an old illustration. Cold read checks byte identity; writes
are atomic and reject stale editions, canceled admission and owner transitions.
Invalid optional artwork leaves the text readable, with an explicit text-only
export warning; the unreadable archive is not overwritten.

## Scope and remaining work

This is a local-file illustration increment, **not automatic provider rendering**.
The existing `origin-dossier-media` governed lane still needs an approved-source
bridge, private retained-byte delivery and actual worker execution for automatic
Phygital+/OneMinAI scenes. A successful synthetic OneMinAI preview is not that
product integration. No new FirstBook chapter or paid render is triggered by
opening, illustrating or exporting the book.

Focused managed tests cover EPUB contents, long Unicode prose, exact offline
image bytes, malformed/oversized images, save/cold reopen, stale text, owner
isolation and the real MAUI preview/confirm/reader/export controls. These are not
an emulator screenshot or a physical Play installation. Release and Play truth
remain in the publication evidence, not this feature document.
