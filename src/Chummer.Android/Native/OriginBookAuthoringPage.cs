using System.Globalization;
using Chummer.Android.Platform;
using Chummer.Contracts.LifeModules;
using Chummer.Run.Contracts.Community;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Consent, recoverable job readback and explicit draft-review entry.</summary>
internal sealed class OriginBookAuthoringPage : NativePageBase
{
    private readonly AndroidSurfaceCopy _copy = AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name);
    private readonly VerticalStackLayout _body = new() { Padding = 20, Spacing = 14 };
    private RetainedOriginBook? _book;
    private readonly OriginNarrativeChapterProjection _chapter;
    private OriginChapterSource? _source;
    private bool _consent;
    private bool _busy;
    private string? _notice;
    private readonly CharacterOverviewState _original;

    internal OriginBookAuthoringPage(RunnerSessionCoordinator coordinator, RetainedOriginBook book,
        OriginNarrativeChapterProjection chapter) : base(coordinator)
    {
        _book = book;
        _original = coordinator.State;
        _chapter = chapter;
        _source = coordinator.PrepareOriginChapterSource(book, chapter);
        Title = _copy["Origin.AuthorChapter"];
        AutomationId = "origin-chapter-authoring";
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken ct)
    {
        if (_book is not null) return;
        long appearance = CaptureAppearanceGeneration();
        bool Current() => IsCurrentAppearanceGeneration(appearance)
            && Coordinator.State.WorkspaceId == _original.WorkspaceId
            && Coordinator.State.DisplayOwnerContext == _original.DisplayOwnerContext;
        if (!Current()) return;
        var book = await Coordinator.LoadRetainedOriginBookAsync(ct, Current);
        if (!Current() || book is null || !book.Chapters.Contains(_chapter)) return;
        _source = Coordinator.PrepareOriginChapterSource(book, _chapter);
        _book = book;
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (_book is not { } book || _source is not { } source || !Coordinator.CanRequestOriginChapter(book))
        {
            _body.Add(NativeTheme.Body(_copy["Origin.BookUnavailable"]));
            return;
        }
        long appearance = CaptureAppearanceGeneration();
        bool Current() => IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
            && Coordinator.CanRequestOriginChapter(book);
        _body.Add(NativeTheme.Title(_chapter.Title));
        _body.Add(NativeTheme.Body(_copy["Origin.AuthoringExplanation"]));
        if (_busy)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "origin-authoring-busy" });
            _body.Add(NativeTheme.Body(_copy["Origin.AuthoringBusy"]));
        }
        _body.Add(NativeTheme.Body(_copy.Format("Origin.BookLanguage", source.Locale)));
        _body.Add(NativeTheme.Title(source.RunnerName, 21));
        foreach (var fact in source.Facts) _body.Add(NativeTheme.Body(fact.Text));
        bool canCreate = book.TryGetAuthoringPredecessor(_chapter, out _);
        if (canCreate) _body.Add(NativeTheme.Body(_copy["Origin.AuthoringConsent"]));
        var consent = new Switch { IsToggled = _consent, IsEnabled = !_busy && canCreate,
            IsVisible = canCreate, AutomationId = "origin-authoring-consent" };
        SemanticProperties.SetDescription(consent, _copy["Origin.AuthoringConsent"]);
        var submit = NativeTheme.ReadingButton(_copy["Origin.AuthoringRequest"]);
        submit.IsEnabled = _consent && !_busy && canCreate; submit.IsVisible = canCreate;
        submit.AutomationId = "origin-authoring-request";
        consent.Toggled += (_, args) => { if (Current() && !_busy && canCreate) { _consent = args.Value; submit.IsEnabled = _consent; } };
        submit.Clicked += async (_, _) => await RunAsync(() => SyncAsync(create: true, Current));
        var refresh = NativeTheme.ReadingButton(_copy["Origin.AuthoringRefresh"]);
        refresh.IsEnabled = !_busy; refresh.AutomationId = "origin-authoring-refresh";
        refresh.Clicked += async (_, _) => await RunAsync(() => SyncAsync(create: false, Current));
        _body.Add(consent);
        _body.Add(submit);
        _body.Add(refresh);
        if (_notice is not null) _body.Add(NativeTheme.Body(_notice));
        if (book.Pending(_chapter) is { } draft)
        {
            var review = NativeTheme.ReadingButton(_copy["Origin.ReviewProse"]);
            review.AutomationId = "origin-authoring-review";
            review.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (Current()) await Navigation.PushAsync(new OriginBookProseReviewPage(Coordinator, book, _chapter, draft));
            });
            _body.Add(review);
        }
    }

    private async Task SyncAsync(bool create, Func<bool> current)
    {
        if (_busy || !current() || _book is not { } book || _source is not { } source || create && !_consent) return;
        _busy = true;
        Refresh();
        (AndroidOriginChapterResult result, RetainedOriginBook? updated) response;
        try { response = await Coordinator.SyncOriginChapterAsync(book, _chapter, source, create, current, CancellationToken.None); }
        finally { _busy = false; }
        var (result, updated) = response;
        // Staging may retire book, so test the issued updated edition rather than
        // accepting an old export/review handle after a durable store change.
        if (!ReferenceEquals(_book, book) || updated is null || !Coordinator.IsRetainedOriginBookCurrent(updated)) return;
        _book = updated;
        _notice = _copy[result.Outcome switch
        {
            AndroidOriginChapterOutcome.NotFound => "Origin.AuthoringNotFound",
            AndroidOriginChapterOutcome.Available => result.Job?.State switch
            {
                OriginChapterAuthoringStates.AwaitingAuthoring => "Origin.AuthoringQueued",
                OriginChapterAuthoringStates.ReconciliationRequired => "Origin.AuthoringReconciling",
                OriginChapterAuthoringStates.ReviewRequired => "Origin.AuthoringReady",
                _ => "Origin.AuthoringUnavailable"
            },
            _ => result.UnknownRemoteOutcome ? "Origin.AuthoringUnknown" : "Origin.AuthoringUnavailable"
        }];
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _book = null;
        _source = null;
        _consent = false;
        _body.Clear();
    }
}
