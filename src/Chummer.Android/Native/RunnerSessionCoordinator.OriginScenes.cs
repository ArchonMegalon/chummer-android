using System.Security.Cryptography;
using System.Text;
using Chummer.Android.Platform;
using Chummer.Contracts.LifeModules;
using Chummer.Run.Contracts.Community;
using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    internal bool CanRequestOriginBookScene(RetainedOriginBook book, OriginNarrativeChapterProjection chapter)
    {
        if (!CanRetainOriginBookScene(book) || !CanRequestOriginChapter(book)
            || _account is not IAndroidOriginSceneTransport || !book.Chapters.Contains(chapter)
            || book.Reading(chapter)?.Selected is not { } selected) return false;
        try { return selected.JobId == OriginChapterSourceIdentity.RequestId(book.AuthoringSource(chapter)); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    internal static bool ValidOriginSceneExcerpt(string text, string excerpt, string altText)
        => !string.IsNullOrWhiteSpace(excerpt) && !excerpt.Contains('\0') && Encoding.UTF8.GetByteCount(excerpt) <= 3072
            && text.Contains(excerpt, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(altText)
            && !altText.Contains('\0') && Encoding.UTF8.GetByteCount(altText) <= 1024;

    internal async Task<(AndroidOriginSceneResult Result, OriginBookScene? Scene)> SyncOriginBookSceneAsync(
        RetainedOriginBook book, OriginNarrativeChapterProjection chapter, string excerpt, string altText,
        bool consentToCreate, Func<bool> isCurrentPage, CancellationToken ct, bool automatic = false,
        Action? onCreationFenced = null)
    {
        bool Current() => isCurrentPage() && CanRequestOriginBookScene(book, chapter);
        if (!Current() || !_retainedBooks.TryGetValue(book, out var original)
            || original.DisplayOwnerContext is not { } owner || _account is not IAndroidOriginSceneTransport transport)
            return (new(AndroidOriginSceneOutcome.Unauthorized), null);
        var source = book.AuthoringSource(chapter);
        string text = book.ChapterText(chapter);
        if (automatic && book.Readings?.IllustrationPolicy != OriginBookReadingState.AutomaticIllustrations
            || consentToCreate && !automatic && !ValidOriginSceneExcerpt(text, excerpt, altText))
            return (new(AndroidOriginSceneOutcome.Conflict), null);
        // Local reading commits before its best-effort Hub acknowledgement.
        // Media requires that same server acceptance even for a status read.
        // Reconcile the saved selection first, including after process death;
        // never turn this race into a terminal scene conflict or paid replay.
        if (_account is not IAndroidOriginChapterTransport chapters
            || book.Reading(chapter)?.Selected is not { } selected)
            return (new(AndroidOriginSceneOutcome.Unauthorized), null);
        var accepted = new OriginChapterPredecessor(selected.JobId, OriginChapterSourceIdentity.Digest(source),
            selected.ProviderReceiptDigest, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        var acceptance = await ReconcileOriginPredecessorAsync(book, accepted, owner, chapters, Current, ct);
        if (!Current() || ct.IsCancellationRequested) return (new(AndroidOriginSceneOutcome.Unauthorized), null);
        if (acceptance is not null)
            return (new(acceptance.Outcome == AndroidOriginChapterOutcome.Unauthorized ? AndroidOriginSceneOutcome.Unauthorized
                : acceptance.Outcome == AndroidOriginChapterOutcome.Conflict ? AndroidOriginSceneOutcome.Conflict
                : AndroidOriginSceneOutcome.Unavailable, RetryableReadFailure: acceptance.RetryableReadFailure), null);
        AndroidOriginSceneResult? result = null;
        try
        {
            result = await transport.ReadSceneAsync(owner, source, text, ct);
            // A known remote admission is also read-only from now on. A later
            // missing response is not permission to replace an existing job.
            if (result.Outcome == AndroidOriginSceneOutcome.Available || result.UnknownRemoteOutcome)
                onCreationFenced?.Invoke();
            if (!Current() || ct.IsCancellationRequested) return (new(AndroidOriginSceneOutcome.Unauthorized), null);
            // A read cannot spend credits. Only confirmed absence plus this
            // explicit excerpt consent may enter the governed render lane.
            if (result.Outcome == AndroidOriginSceneOutcome.NotFound && consentToCreate)
            {
                // Fence the actual write before dispatch, including a lost result
                // or a retired reader edition. A failed read above is not a write
                // attempt and must leave a later confirmed-absence request possible.
                onCreationFenced?.Invoke();
                result = automatic
                    ? await transport.RequestAutomaticSceneAsync(owner, source, text, true, ct)
                    : await transport.RequestSceneAsync(owner, source, text, excerpt, altText, true, ct);
                if (!Current() || ct.IsCancellationRequested) return (new(AndroidOriginSceneOutcome.Unauthorized), null);
                if (result.Outcome == AndroidOriginSceneOutcome.Available && result.State is "review" or "persisted")
                    result = await transport.ReadSceneAsync(owner, source, text, ct);
            }
            if (!Current() || ct.IsCancellationRequested) return (new(AndroidOriginSceneOutcome.Unauthorized), null);
            OriginBookScene? scene = null;
            if (result.Outcome == AndroidOriginSceneOutcome.Available && result.Image is { } image)
            {
                // A historical/manual preview never becomes an automatic
                // adoption merely because the reader was opened after update.
                if (automatic && (result.State != "persisted"
                    || image.InsertionPolicy != OriginBookReadingState.AutomaticIllustrations))
                    return (new(AndroidOriginSceneOutcome.Conflict), null);
                // Decode/copy/hash the bounded raster off the Android UI thread.
                scene = await Task.Run(() =>
                {
                    if (OriginBookScene.Hash(image.Bytes) != image.ImageHash) throw new InvalidDataException();
                    return OriginBookScene.ForChapter(book, chapter, image.AltText, image.Bytes);
                }, ct);
            }
            return Current() && !ct.IsCancellationRequested
                ? (result with { Image = null }, scene) : (new(AndroidOriginSceneOutcome.Unauthorized), null);
        }
        finally { if (result?.Image is { } image) CryptographicOperations.ZeroMemory(image.Bytes); }
    }

    internal bool CanAutomaticallyIllustrateOriginBook(RetainedOriginBook book)
        => book.Readings?.IllustrationPolicy == OriginBookReadingState.AutomaticIllustrations
            && CanRetainOriginBookScene(book) && CanRequestOriginChapter(book)
            && _account is IAndroidOriginSceneTransport;

    internal async Task<(AndroidOriginSceneResult Result, RetainedOriginBook? Book)> SyncAutomaticOriginBookSceneAsync(
        RetainedOriginBook book, OriginNarrativeChapterProjection chapter, bool allowCreate,
        Func<bool> isCurrentPage, CancellationToken ct, Action? onCreationFenced = null)
    {
        if (!CanAutomaticallyIllustrateOriginBook(book) || !isCurrentPage())
            return (new(AndroidOriginSceneOutcome.Unauthorized), null);
        if (book.Scene(chapter) is not null) return (new(AndroidOriginSceneOutcome.Available, "persisted"), book);
        var (result, scene) = await SyncOriginBookSceneAsync(book, chapter, "", "", allowCreate,
            isCurrentPage, ct, automatic: true, onCreationFenced: onCreationFenced);
        if (!isCurrentPage() || ct.IsCancellationRequested || !IsRetainedOriginBookCurrent(book))
            return (new(AndroidOriginSceneOutcome.Unauthorized), null);
        // No local picker, pretend human approval or separate image decision.
        // Only authenticated exact private-auto retention is inserted offline.
        if (scene is null) return (result, book);
        var updated = await PersistOriginBookSceneAsync(book, chapter, scene, isCurrentPage, ct);
        return (result, updated);
    }

    internal async Task<AndroidOriginSceneResult> DecideOriginBookSceneAsync(RetainedOriginBook book,
        OriginNarrativeChapterProjection chapter, OriginBookScene scene, bool approve, bool explicitlyConfirmed,
        Func<bool> isCurrentPage, CancellationToken ct)
    {
        bool Current() => isCurrentPage() && CanRequestOriginBookScene(book, chapter);
        if (!explicitlyConfirmed || !Current() || !scene.Matches(book, chapter)
            || !_retainedBooks.TryGetValue(book, out var original) || original.DisplayOwnerContext is not { } owner
            || _account is not IAndroidOriginSceneTransport transport) return new(AndroidOriginSceneOutcome.Unauthorized);
        var result = await transport.DecideSceneAsync(owner, book.AuthoringSource(chapter),
            book.ChapterText(chapter), scene.Identity.ImageDigest, approve, true, ct);
        if (result.Image is { } unexpected) CryptographicOperations.ZeroMemory(unexpected.Bytes);
        return Current() && !ct.IsCancellationRequested ? result with { Image = null } : new(AndroidOriginSceneOutcome.Unauthorized);
    }
}
