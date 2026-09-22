using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.OriginBooks;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed class RetainedOriginBook(OriginStoryArcSeed projection)
{
    public string RunnerName { get; } = projection.CurrentTurn.RunnerDisplayName;
    public string Locale { get; } = projection.CurrentTurn.Locale;
    public string Digest { get; } = projection.SeedDigest;
    public IReadOnlyList<OriginNarrativeChapterProjection> Chapters { get; } =
        Array.AsReadOnly(projection.VisibleChapters.ToArray());

    // No executable HTML, remote resources or private workspace metadata. The
    // exact accepted prose is escaped, not regenerated or sent to a provider.
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
                .Append(E(chapter.VisibleMarkdown)).Append("</div></section>");
        return html.Append("<footer>").Append(E(copy.Format("Origin.BookMetadata", OriginTechnicalPublicationMetadata.ChummerRunId)))
            .Append("</footer></body></html>").ToString();
    }
}

public sealed partial class RunnerSessionCoordinator
{
    private readonly IOwnerBoundLifeModuleBookService? _lifeModuleBookService;
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
            var book = new RetainedOriginBook(projection);
            _retainedBooks.Add(book, original);
            return book;
        }, ct);
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
