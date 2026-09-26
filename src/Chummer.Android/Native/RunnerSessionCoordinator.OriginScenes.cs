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
        if (!CanSelectOriginBookScene(book) || !CanRequestOriginChapter(book)
            || _account is not IAndroidOriginSceneTransport || !book.Chapters.Contains(chapter)
            || book.Reading(chapter)?.Selected is not { } selected) return false;
        try { return selected.JobId == OriginChapterSourceIdentity.RequestId(OriginBookAuthoringSource.Create(book.Projection, chapter)); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    internal static bool ValidOriginSceneExcerpt(string text, string excerpt, string altText)
        => !string.IsNullOrWhiteSpace(excerpt) && !excerpt.Contains('\0') && Encoding.UTF8.GetByteCount(excerpt) <= 3072
            && text.Contains(excerpt, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(altText)
            && !altText.Contains('\0') && Encoding.UTF8.GetByteCount(altText) <= 1024;

    internal async Task<(AndroidOriginSceneResult Result, OriginBookScene? Scene)> SyncOriginBookSceneAsync(
        RetainedOriginBook book, OriginNarrativeChapterProjection chapter, string excerpt, string altText,
        bool consentToCreate, Func<bool> isCurrentPage, CancellationToken ct)
    {
        bool Current() => isCurrentPage() && CanRequestOriginBookScene(book, chapter);
        if (!Current() || !_retainedBooks.TryGetValue(book, out var original)
            || original.DisplayOwnerContext is not { } owner || _account is not IAndroidOriginSceneTransport transport)
            return (new(AndroidOriginSceneOutcome.Unauthorized), null);
        var source = OriginBookAuthoringSource.Create(book.Projection, chapter);
        string text = book.ChapterText(chapter);
        if (consentToCreate && !ValidOriginSceneExcerpt(text, excerpt, altText))
            return (new(AndroidOriginSceneOutcome.Conflict), null);
        AndroidOriginSceneResult? result = null;
        try
        {
            result = await transport.ReadSceneAsync(owner, source, text, ct);
            if (!Current() || ct.IsCancellationRequested) return (new(AndroidOriginSceneOutcome.Unauthorized), null);
            // A read cannot spend credits. Only confirmed absence plus this
            // explicit excerpt consent may enter the governed render lane.
            if (result.Outcome == AndroidOriginSceneOutcome.NotFound && consentToCreate)
            {
                result = await transport.RequestSceneAsync(owner, source, text, excerpt, altText, true, ct);
                if (!Current() || ct.IsCancellationRequested) return (new(AndroidOriginSceneOutcome.Unauthorized), null);
                if (result.Outcome == AndroidOriginSceneOutcome.Available && result.State is "review" or "persisted")
                    result = await transport.ReadSceneAsync(owner, source, text, ct);
            }
            if (!Current() || ct.IsCancellationRequested) return (new(AndroidOriginSceneOutcome.Unauthorized), null);
            OriginBookScene? scene = null;
            if (result.Outcome == AndroidOriginSceneOutcome.Available && result.Image is { } image)
            {
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

    internal async Task<AndroidOriginSceneResult> DecideOriginBookSceneAsync(RetainedOriginBook book,
        OriginNarrativeChapterProjection chapter, OriginBookScene scene, bool approve, bool explicitlyConfirmed,
        Func<bool> isCurrentPage, CancellationToken ct)
    {
        bool Current() => isCurrentPage() && CanRequestOriginBookScene(book, chapter);
        if (!explicitlyConfirmed || !Current() || !scene.Matches(book, chapter)
            || !_retainedBooks.TryGetValue(book, out var original) || original.DisplayOwnerContext is not { } owner
            || _account is not IAndroidOriginSceneTransport transport) return new(AndroidOriginSceneOutcome.Unauthorized);
        var result = await transport.DecideSceneAsync(owner, OriginBookAuthoringSource.Create(book.Projection, chapter),
            book.ChapterText(chapter), scene.Identity.ImageDigest, approve, true, ct);
        if (result.Image is { } unexpected) CryptographicOperations.ZeroMemory(unexpected.Bytes);
        return Current() && !ct.IsCancellationRequested ? result with { Image = null } : new(AndroidOriginSceneOutcome.Unauthorized);
    }
}
