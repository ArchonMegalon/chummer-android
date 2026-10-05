# Clear a recovered reader's paused notice

5 October 2026. A real API-36 Life Modules reader resumed its existing chapter
request but still displayed the inline paused message. The pinned status had
already resumed. No new generation was requested.

`ReadMissingChapterCoreAsync` cleared the notice without reporting a UI change
when the retained edition, outcome and remote job state were unchanged. The
fix includes clearing a notice in that existing refresh decision. It changes
neither the read budget nor paid-admission, ownership or acceptance behavior.

The focused real-page regression first fails on unchanged product code with
the stale paused/retrying notice assertion. It passes with the fix. It covers
three bounded transient reads, the actual recovery button, both inline and
pinned status, unchanged paid-request/acceptance counts, and retained prose and
EPUB export. An initial test-helper reflection error was corrected separately;
that failed invocation is not evidence of the product bug.

The same compiled managed output also passes automatic pre-admission recovery
and editorial refresh tests, including cold state, cancellation, stale actions,
owner transitions, accepted-text immutability and no paid replay. The managed
build has zero warnings or errors. These are focused local checks, not a hosted
runtime qualification or a Play-installation claim.

The independent live synthetic book produced a complete 2,494-word FirstBook
chapter. Its native reader text matches the provider capture exactly. The real
read acknowledgement reveals the rule effects only afterwards; one automatic
1min.ai illustration is visibly inserted without a picker or local upload.
This proves one chapter's scene, not consistent appearance at multiple ages.
Native build, cold reopen and EPUB checks for the new pause fix remain separate
from this preceding scene/reader observation. Release availability is recorded
only in the Play evidence, not inferred from this document.
