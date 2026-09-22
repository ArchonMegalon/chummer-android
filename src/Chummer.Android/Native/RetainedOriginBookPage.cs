using System.Globalization;

namespace Chummer.Android.Native;

internal sealed class RetainedOriginBookPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new() { Padding = 20, Spacing = 14 };
    private readonly AndroidSurfaceCopy _copy = AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name);
    private RetainedOriginBook? _book;
    private string? _notice;

    public RetainedOriginBookPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = _copy["Origin.ReadBook"];
        AutomationId = "origin-retained-book";
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken ct)
    {
        long appearance = CaptureAppearanceGeneration();
        _book = null;
        _notice = null;
        _body.Clear();
        _book = await Coordinator.LoadRetainedOriginBookAsync(ct, () => IsCurrentAppearanceGeneration(appearance));
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (_book is not { } book || !Coordinator.IsRetainedOriginBookCurrent(book))
        {
            _book = null;
            _body.Add(NativeTheme.Body(_copy["Origin.BookUnavailable"]));
            return;
        }
        _body.Add(NativeTheme.Title(book.RunnerName));
        _body.Add(NativeTheme.Body(_copy.Format("Origin.BookLanguage", book.Locale), NativeTheme.Muted));
        _body.Add(NativeTheme.Body(_copy["Origin.BookSavedChapters"], NativeTheme.Muted));
        long appearance = CaptureAppearanceGeneration();
        var export = new Button { Text = _copy["Origin.ExportBook"], AutomationId = "origin-book-export" };
        export.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool Current() => IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
                && Coordinator.IsRetainedOriginBookCurrent(book);
            if (!Current()) return;
            bool saved = await Coordinator.ExportRetainedOriginBookAsync(book, _copy, Current, CancellationToken.None);
            if (Current()) _notice = _copy[saved ? "Origin.BookExported" : "Origin.BookExportCancelled"];
        });
        _body.Add(export);
        if (!Coordinator.Account.IsLinked)
        {
            var explanation = NativeTheme.Body(_copy["Origin.BookAccountExplanation"], NativeTheme.Muted);
            explanation.AutomationId = "origin-book-account-explanation";
            _body.Add(explanation);
            var account = new Button { Text = _copy["Origin.BookAccount"], AutomationId = "origin-book-account",
                IsEnabled = !Coordinator.Account.IsLoading };
            account.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
                    && Coordinator.IsRetainedOriginBookCurrent(book)
                    && !Coordinator.Account.IsLinked && !Coordinator.Account.IsLoading)
                    await Navigation.PushAsync(new AccountPrivacyPage(Coordinator));
            });
            _body.Add(account);
        }
        if (_notice is not null) _body.Add(NativeTheme.Body(_notice));
        foreach (var chapter in book.Chapters)
        {
            _body.Add(NativeTheme.Title(chapter.Title, 21));
            var text = NativeTheme.Body(book.ChapterText(chapter));
            text.AutomationId = $"origin-retained-chapter-{chapter.Sequence}";
            _body.Add(text);
            if (Coordinator.PrepareOriginChapterSource(book, chapter) is not null)
            {
                var author = new Button { Text = _copy["Origin.AuthorChapter"], AutomationId = $"origin-author-chapter-{chapter.Sequence}" };
                author.Clicked += async (_, _) => await RunAsync(async () =>
                {
                    if (IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
                        && Coordinator.CanRequestOriginChapter(book))
                        await Navigation.PushAsync(new OriginBookAuthoringPage(Coordinator, book, chapter));
                });
                _body.Add(author);
            }
            if (book.Pending(chapter) is { } draft)
            {
                var review = new Button { Text = _copy["Origin.ReviewProse"], AutomationId = $"origin-review-prose-{chapter.Sequence}" };
                review.Clicked += async (_, _) => await RunAsync(async () =>
                {
                    if (IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
                        && Coordinator.IsRetainedOriginBookCurrent(book))
                        await Navigation.PushAsync(new OriginBookProseReviewPage(Coordinator, book, chapter, draft));
                });
                _body.Add(review);
            }
        }
        _body.Add(NativeTheme.Body(_copy.Format("Origin.BookMetadata", "chummer.run"), NativeTheme.Muted));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _book = null;
        _body.Clear();
    }
}
