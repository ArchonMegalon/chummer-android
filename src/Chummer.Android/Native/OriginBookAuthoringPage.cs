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
    private string? _jobState;
    private bool _watchPending;
    private int _consecutiveReadFailures;
    private CancellationTokenSource? _pollLifetime;
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
        int? stage = _jobState switch
        {
            OriginChapterAuthoringStates.AwaitingAuthoring => 1,
            OriginChapterAuthoringStates.ReconciliationRequired => 2,
            OriginChapterAuthoringStates.ReviewRequired => 3,
            _ => null
        };
        if (stage is not null)
        {
            var progress = new ProgressBar { Progress = stage.Value / 3d,
                ProgressColor = NativeTheme.Ink, AutomationId = "origin-authoring-progress" };
            SemanticProperties.SetDescription(progress, _copy.Format("Origin.AuthoringStage", stage.Value));
            _body.Add(progress);
            _body.Add(NativeTheme.Body(_copy.Format("Origin.AuthoringStage", stage.Value), NativeTheme.Muted));
            if (stage < 3)
            {
                var eta = NativeTheme.Body(_copy["Origin.AuthoringEtaUnknown"], NativeTheme.Muted);
                eta.AutomationId = "origin-authoring-eta";
                _body.Add(eta);
            }
        }
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

    private async Task<bool> SyncAsync(bool create, Func<bool> current,
        bool automatic = false, CancellationToken ct = default)
    {
        if (_busy || !current() || _book is not { } book || _source is not { } source || create && !_consent) return false;
        var priorNotice = _notice;
        var priorState = _jobState;
        bool wasWatching = _watchPending;
        _watchPending = false;
        _busy = true;
        // A status read must not repeatedly rebuild the fact/consent controls
        // or move the reader's scroll position while they wait.
        if (!automatic) Refresh();
        (AndroidOriginChapterResult result, RetainedOriginBook? updated) response;
        try { response = await Coordinator.SyncOriginChapterAsync(book, _chapter, source, create, current, ct,
            reconcileReaderAcceptance: !automatic); }
        finally { _busy = false; }
        var (result, updated) = response;
        // Staging may retire book, so test the issued updated edition rather than
        // accepting an old export/review handle after a durable store change.
        if (!ReferenceEquals(_book, book) || updated is null || ct.IsCancellationRequested
            || !Coordinator.IsRetainedOriginBookCurrent(updated)) return false;
        _book = updated;
        // Request/resume first performs a read. A failure of that read is still
        // read-only: preserve the stage and never infer absence or create a job.
        if (result.Outcome == AndroidOriginChapterOutcome.Unavailable
            && result.RetryableReadFailure)
        {
            _watchPending = wasWatching && ++_consecutiveReadFailures < 3;
            // Keep the last confirmed stage, not an invented percentage or ETA.
            // A failed manual read of an idle/terminal job does not start a
            // watcher or turn a transport interruption into an account failure.
            _notice = _copy[!wasWatching ? "Origin.AuthoringStatusInterrupted"
                : _watchPending ? "Origin.AuthoringStatusRetrying" : "Origin.AuthoringStatusPaused"];
            if (_watchPending && !automatic) StartPendingStatusWatch();
            return !ReferenceEquals(book, updated) || priorNotice != _notice;
        }
        _consecutiveReadFailures = 0;
        _jobState = result.Outcome == AndroidOriginChapterOutcome.Available ? result.Job?.State : null;
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
        _watchPending = _jobState is OriginChapterAuthoringStates.AwaitingAuthoring
            or OriginChapterAuthoringStates.ReconciliationRequired
            || create && result.UnknownRemoteOutcome;
        if (_watchPending && !automatic) StartPendingStatusWatch();
        return !ReferenceEquals(book, updated) || priorState != _jobState || priorNotice != _notice;
    }

    private void StartPendingStatusWatch()
    {
        if (_pollLifetime is not null) return;
        var lifetime = new CancellationTokenSource();
        _pollLifetime = lifetime;
        long appearance = CaptureAppearanceGeneration();
        _ = WatchPendingStatusAsync(appearance, lifetime);
    }

    private async Task WatchPendingStatusAsync(long appearance, CancellationTokenSource lifetime)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Bounded foreground observation, never a generation retry. Only
            // classified transient reads have a three-failure reserve; all
            // authentication, integrity and unclassified failures stop at once.
            while (_watchPending && IsCurrentAppearanceGeneration(appearance)
                && elapsed.Elapsed < TimeSpan.FromMinutes(10))
            {
                await Task.Delay(TimeSpan.FromSeconds(15), lifetime.Token);
                if (elapsed.Elapsed >= TimeSpan.FromMinutes(10)) break;
                await PollPendingChapterOnceAsync(appearance, lifetime.Token);
            }
            if (elapsed.Elapsed >= TimeSpan.FromMinutes(10))
                await PausePendingStatusObservationAsync(appearance, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_pollLifetime, lifetime)) _pollLifetime = null;
            lifetime.Dispose();
        }
    }

    internal Task PausePendingStatusObservationAsync(long appearance, CancellationToken ct)
        => RunWithConditionalRefreshAsync(() =>
        {
            if (ct.IsCancellationRequested || !_watchPending || _busy
                || !IsCurrentAppearanceGeneration(appearance) || _book is not { } book
                || !Coordinator.CanRequestOriginChapter(book)) return Task.FromResult(false);
            _watchPending = false;
            _notice = _copy["Origin.AuthoringStatusPaused"];
            return Task.FromResult(true);
        });

    internal Task PollPendingChapterOnceAsync(long appearance, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !_watchPending || _busy
            || !IsCurrentAppearanceGeneration(appearance)) return Task.CompletedTask;
        return RunWithConditionalRefreshAsync(async () =>
        {
            if (ct.IsCancellationRequested || !_watchPending || _busy
                || !IsCurrentAppearanceGeneration(appearance) || _book is not { } book) return false;
            bool Current() => !ct.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance)
                && ReferenceEquals(_book, book) && Coordinator.CanRequestOriginChapter(book);
            return await SyncAsync(create: false, Current, automatic: true, ct);
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _watchPending = false;
        _consecutiveReadFailures = 0;
        _pollLifetime?.Cancel();
        _pollLifetime = null;
        _book = null;
        _source = null;
        _consent = false;
        _jobState = null;
        _notice = null;
        _body.Clear();
    }
}
