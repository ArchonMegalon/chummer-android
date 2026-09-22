using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Chummer.Application.LifeModules;
using Chummer.Android.Platform;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.OriginBooks;
using Chummer.Presentation.Overview;
using Chummer.Run.Contracts.Community;

namespace Chummer.Android.Native;

internal sealed class RetainedOriginBook(OriginStoryArcSeed projection, OriginBookReadingState? readings = null)
{
    internal OriginStoryArcSeed Projection { get; } = projection;
    internal OriginBookReadingState? Readings { get; } = readings;
    public string RunnerName { get; } = projection.CurrentTurn.RunnerDisplayName;
    public string Locale { get; } = projection.CurrentTurn.Locale;
    public string Digest { get; } = projection.SeedDigest;
    public IReadOnlyList<OriginNarrativeChapterProjection> Chapters { get; } =
        Array.AsReadOnly(projection.VisibleChapters.ToArray());
    private readonly IReadOnlyDictionary<string, string> _chapterText = projection.VisibleChapters.ToDictionary(
        chapter => chapter.ChapterId, chapter => OriginBookChapterText.Render(projection, chapter), StringComparer.Ordinal);

    public string ChapterText(OriginNarrativeChapterProjection chapter)
        => Reading(chapter)?.Selected is { } selected && selected.Matches(chapter, Locale)
            ? selected.Text : _chapterText[chapter.ChapterId];

    internal OriginBookReadingChapter? Reading(OriginNarrativeChapterProjection chapter)
        => Chapters.Contains(chapter) ? Readings?.Chapters.SingleOrDefault(c => c.ChapterId == chapter.ChapterId) : null;
    internal OriginBookProseDraft? Pending(OriginNarrativeChapterProjection chapter)
        => Reading(chapter)?.Pending;

    // No executable HTML, remote resources or private workspace metadata. The
    // Display text is escaped, not sent to a provider. Original chapter bytes
    // remain untouched even when a legacy template needs a confirmed-facts view.
    public string ToHtml(AndroidSurfaceCopy copy)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var html = new StringBuilder("<!doctype html><html lang=\"").Append(E(Locale))
            .Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<meta name=\"author\" content=\"chummer.run\"><title>").Append(E(RunnerName))
            .Append("</title><style>body{max-width:48rem;margin:2rem auto;padding:0 1rem;font:1.15rem/1.65 serif}h1,h2{line-height:1.2}.prose{white-space:pre-wrap;overflow-wrap:anywhere}section{break-before:page}footer{margin-top:3rem}</style></head><body><h1>")
            .Append(E(RunnerName)).Append("</h1><p>").Append(E(copy["Origin.BookSavedChapters"]))
            .Append("</p><p>").Append(E(copy.Format("Origin.BookLanguage", Locale))).Append("</p>");
        foreach (var chapter in Chapters)
            html.Append("<section><h2>").Append(E(chapter.Title)).Append("</h2><div class=\"prose\">")
                .Append(E(ChapterText(chapter))).Append("</div></section>");
        return html.Append("<footer>").Append(E(copy.Format("Origin.BookMetadata", OriginTechnicalPublicationMetadata.ChummerRunId)))
            .Append("</footer></body></html>").ToString();
    }
}

public sealed partial class RunnerSessionCoordinator
{
    private readonly IOwnerBoundLifeModuleBookService? _lifeModuleBookService;
    private readonly OriginBookReadingStore? _originBookReadings;
    private readonly ConditionalWeakTable<RetainedOriginBook, CharacterOverviewState> _retainedBooks = new();

    internal bool CanReadRetainedOriginBook(CharacterOverviewState? original = null)
    {
        original ??= State;
        return _lifeModuleBookService is not null
            && original.Profile?.BuildMethod == CharacterCreationBuildMethods.LifeModules
            && string.Equals(original.Rules?.GameEdition, "SR5", StringComparison.OrdinalIgnoreCase)
            && IsNativeEditDisplayCurrent(original);
    }

    internal bool IsRetainedOriginBookCurrent(RetainedOriginBook book)
        => _retainedBooks.TryGetValue(book, out var original) && IsNativeEditDisplayCurrent(original);

    internal bool CanRequestOriginChapter(RetainedOriginBook book)
        => _account is IAndroidOriginChapterTransport && _account.Snapshot.IsLinked
            && IsRetainedOriginBookCurrent(book);

    internal OriginChapterSource? PrepareOriginChapterSource(RetainedOriginBook book, OriginNarrativeChapterProjection chapter)
    {
        if (!CanRequestOriginChapter(book)) return null;
        try { return OriginBookAuthoringSource.Create(book.Projection, chapter); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return null; }
    }

    internal async Task<(AndroidOriginChapterResult Result, RetainedOriginBook? Book)> SyncOriginChapterAsync(
        RetainedOriginBook book, OriginNarrativeChapterProjection chapter, OriginChapterSource approvedSource,
        bool consentToCreate, Func<bool> isCurrentPage, CancellationToken ct)
    {
        bool Current() => isCurrentPage() && CanRequestOriginChapter(book);
        if (!Current() || !_retainedBooks.TryGetValue(book, out var original)
            || original.DisplayOwnerContext is not { } owner || _account is not IAndroidOriginChapterTransport transport)
            return (new(AndroidOriginChapterOutcome.Unauthorized), null);
        var source = OriginBookAuthoringSource.Create(book.Projection, chapter);
        if (OriginChapterSourceIdentity.Digest(source) != OriginChapterSourceIdentity.Digest(approvedSource))
            return (new(AndroidOriginChapterOutcome.Conflict), null);
        // Read first; neither opening this page nor refreshing a job can create
        // a new paid task. Explicit consent is only used for a confirmed absence.
        var result = await transport.ReadChapterAsync(owner, source, ct);
        if (!Current() || ct.IsCancellationRequested) return (new(AndroidOriginChapterOutcome.Unauthorized), null);
        if (result.Outcome == AndroidOriginChapterOutcome.NotFound && consentToCreate)
            result = await transport.RequestChapterAsync(owner, source, true, ct);
        if (!Current() || ct.IsCancellationRequested) return (result with { Job = null }, null);
        if (result.Job is not { State: OriginChapterAuthoringStates.ReviewRequired } job)
            return (result, book);
        var draft = OriginBookProseDraft.Create(chapter, book.Locale, job.RequestId,
            job.ProviderReceiptDigest!, job.DraftText!);
        if (book.Reading(chapter)?.Selected?.DraftDigest == draft.DraftDigest)
        {
            // The durable selected edition is the local acceptance outbox.
            // Recover a lost acknowledgement without regenerating or adopting
            // an unselected draft. A read alone never grants new acceptance.
            if (job.ReaderAcceptedTextDigest is null)
                await RecordOriginBookReaderAcceptanceAsync(book, draft, isCurrentPage, ct);
            return (result, Current() ? book : null);
        }
        var updated = await StageOriginBookProseDraftAsync(book, draft, isCurrentPage, ct);
        return (result, updated);
    }

    internal async Task<bool> RecordOriginBookReaderAcceptanceAsync(RetainedOriginBook book,
        OriginBookProseDraft draft, Func<bool> isCurrentPage, CancellationToken ct)
    {
        bool Current() => isCurrentPage() && CanRequestOriginChapter(book);
        if (!Current() || !_retainedBooks.TryGetValue(book, out var original)
            || original.DisplayOwnerContext is not { } owner || _account is not IAndroidOriginChapterTransport transport)
            return false;
        var chapter = book.Chapters.SingleOrDefault(c => c.ChapterId == draft.ChapterId);
        if (chapter is null || !draft.Matches(chapter, book.Locale)
            || book.Reading(chapter)?.Selected?.DraftDigest != draft.DraftDigest) return false;
        var source = OriginBookAuthoringSource.Create(book.Projection, chapter);
        if (draft.JobId != OriginChapterSourceIdentity.RequestId(source)) return false;
        var result = await transport.AcceptChapterAsync(owner, source, draft.ProviderReceiptDigest,
            draft.Text, explicitlyConfirmed: true, ct);
        return Current() && result.Outcome == AndroidOriginChapterOutcome.Available
            && result.Job?.ReaderAcceptedTextDigest == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(draft.Text))).ToLowerInvariant();
    }

    internal Task<RetainedOriginBook?> LoadRetainedOriginBookAsync(CancellationToken ct, Func<bool> isCurrentPage)
    {
        var original = State;
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (!isCurrentPage() || !CanReadRetainedOriginBook() || !IsNativeEditDisplayCurrent(original)
                || original.DisplayOwnerContext is not { } owner || original.WorkspaceId is not { } id
                || _lifeModuleBookService is not { } service) return null;
            var result = await Task.Run(() => service.Load(owner, id, original.ContentRevision, original.SavedRevision), ct);
            ct.ThrowIfCancellationRequested();
            if (!isCurrentPage() || !IsNativeEditDisplayCurrent(original)
                || result.Outcome != LifeModuleOriginDossierOutcomes.Success || result.Value is not { } projection
                || projection.CurrentTurn.WorkspaceId != id.Value || projection.VisibleChapters.Count == 0) return null;
            OriginBookReadingState? readings = null;
            if (_originBookReadings is { } readingStore)
                readings = await Task.Run(() =>
                {
                    if (!isCurrentPage() || !IsNativeEditDisplayCurrent(original)
                        || !TryAcquireDamageJournalOwner(owner, id, out var lease))
                        throw new OperationCanceledException("The book context changed.");
                    using (lease) return readingStore.Load(owner.Owner.Value, id.Value);
                }, ct);
            if (!isCurrentPage() || !IsNativeEditDisplayCurrent(original)) return null;
            RetireDifferentBookEditions(original, readings?.Digest);
            var book = new RetainedOriginBook(projection, readings);
            _retainedBooks.Add(book, original);
            return book;
        }, ct);
    }

    // The authenticated Hub job adapter will call this only after validating its
    // response provenance. It stages prose, never invokes a provider or adopts it.
    internal Task<RetainedOriginBook?> StageOriginBookProseDraftAsync(RetainedOriginBook book,
        OriginBookProseDraft draft, Func<bool> isCurrentPage, CancellationToken ct)
        => ChangeBookReadingAsync(book, draft, stage: true, useForReading: false, isCurrentPage, ct);

    internal Task<RetainedOriginBook?> ReviewOriginBookProseDraftAsync(RetainedOriginBook book,
        OriginBookProseDraft draft, bool useForReading, bool explicitlyConfirmed,
        Func<bool> isCurrentPage, CancellationToken ct)
        => useForReading && !explicitlyConfirmed ? Task.FromResult<RetainedOriginBook?>(null)
            : ChangeBookReadingAsync(book, draft, stage: false, useForReading, isCurrentPage, ct);

    private Task<RetainedOriginBook?> ChangeBookReadingAsync(RetainedOriginBook book,
        OriginBookProseDraft draft, bool stage, bool useForReading, Func<bool> isCurrentPage, CancellationToken ct)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            if (_originBookReadings is not { } store || book.Readings is not { } expected
                || !_retainedBooks.TryGetValue(book, out var original)
                || original.DisplayOwnerContext is not { } owner || original.WorkspaceId is not { } id
                || !isCurrentPage() || !IsRetainedOriginBookCurrent(book)) return null;
            var chapter = book.Chapters.SingleOrDefault(c => c.ChapterId == draft.ChapterId);
            // Stale prose can be discarded, never selected for a changed chapter.
            if (chapter is null || !draft.IsValid() || (stage || useForReading) && !draft.Matches(chapter, book.Locale)) return null;
            var existing = book.Reading(chapter) ?? new(chapter.ChapterId, null, null);
            if (stage && existing.Selected?.DraftDigest == draft.DraftDigest) return book;
            if (stage && existing.Pending is not null && existing.Pending.DraftDigest != draft.DraftDigest
                || !stage && existing.Pending?.DraftDigest != draft.DraftDigest) return null;
            var nextChapter = stage ? existing with { Pending = draft }
                : existing with { Pending = null, Selected = useForReading ? draft : existing.Selected };
            var next = expected with { Chapters = expected.Chapters.Where(c => c.ChapterId != chapter.ChapterId)
                .Append(nextChapter).OrderBy(c => c.ChapterId, StringComparer.Ordinal).ToArray() };
            var saved = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (!isCurrentPage() || !IsRetainedOriginBookCurrent(book)
                    || !TryAcquireDamageJournalOwner(owner, id, out var lease))
                    throw new OperationCanceledException("The book context changed.");
                using (lease) return store.Save(expected, next,
                    () => isCurrentPage() && IsRetainedOriginBookCurrent(book), ct);
            }, ct);
            // The durable reading edition changed, even if navigation changed
            // immediately after commit. Old open readers/export callbacks must
            // not keep treating their previous edition as current.
            bool current = isCurrentPage() && IsRetainedOriginBookCurrent(book);
            RetireDifferentBookEditions(original, saved.Digest);
            if (!current) return null;
            var updated = new RetainedOriginBook(book.Projection, saved);
            _retainedBooks.Add(updated, original);
            return updated;
        }, ct);

    private void RetireDifferentBookEditions(CharacterOverviewState original, string? readingDigest)
    {
        foreach (var entry in _retainedBooks)
            if (entry.Value.WorkspaceId == original.WorkspaceId
                && entry.Value.DisplayOwnerContext == original.DisplayOwnerContext
                && entry.Key.Readings?.Digest != readingDigest)
                _retainedBooks.Remove(entry.Key);
    }

    internal async Task<bool> ExportRetainedOriginBookAsync(RetainedOriginBook book, AndroidSurfaceCopy copy,
        Func<bool> isCurrentPage, CancellationToken ct)
    {
        bool Current() => isCurrentPage() && IsRetainedOriginBookCurrent(book);
        if (!Current()) throw new OperationCanceledException("The book context changed.");
        byte[] bytes = await Task.Run(() => Encoding.UTF8.GetBytes(book.ToHtml(copy)), ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!Current()) throw new OperationCanceledException("The book context changed.");
            await using var stream = new MemoryStream(bytes, writable: false);
            return await _documents.SaveAsAsync("origin-dossier.html", "text/html", stream, Current, ct);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
