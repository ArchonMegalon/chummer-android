using System.Globalization;
using Chummer.Android.Platform;
using Chummer.Run.Contracts.Community;

namespace Chummer.Android.Native;

internal sealed class RetainedOriginBookPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new() { Padding = 20, Spacing = 14 };
    private readonly AndroidSurfaceCopy _copy = AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name);
    private RetainedOriginBook? _book;
    private string? _notice;
    private readonly Dictionary<string, AndroidOriginChapterResult> _chapterStatus = new(StringComparer.Ordinal);
    private CancellationTokenSource? _pollLifetime;
    private bool _watchPending;
    private int _readFailures;
    private CancellationTokenSource? _sceneLifetime;
    private readonly HashSet<string> _sceneChecked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AndroidOriginSceneResult> _sceneStatus = new(StringComparer.Ordinal);
    private bool _sceneObservationPaused;

    public RetainedOriginBookPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = _copy["Origin.ReadBook"];
        AutomationId = "origin-retained-book";
        Content = new ScrollView { Content = _body };
    }

    protected override void OnAppearing()
    {
        ShowLoading();
        base.OnAppearing();
    }

    private void ShowLoading()
    {
        _body.Clear();
        _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "origin-book-loading" });
        var message = NativeTheme.Body(_copy["Origin.BookLoading"], NativeTheme.Muted);
        message.AutomationId = "origin-book-loading-message";
        _body.Add(message);
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken ct)
    {
        long appearance = CaptureAppearanceGeneration();
        _book = null;
        _notice = null;
        _book = await Coordinator.LoadRetainedOriginBookAsync(ct, () => IsCurrentAppearanceGeneration(appearance));
        await ReadMissingChapterAsync(appearance, ct);
        if (_watchPending && IsCurrentAppearanceGeneration(appearance)) StartStatusWatch(appearance);
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
        if (book.UsesSr5Opening)
        {
            var opening = NativeTheme.Body(_copy[book.OpeningSetupComplete
                ? "Origin.OpeningSetupReady" : "Origin.OpeningSetupRequired"], NativeTheme.Muted);
            opening.AutomationId = "origin-book-opening-setup";
            _body.Add(opening);
        }
        long appearance = CaptureAppearanceGeneration();
        var epub = NativeTheme.ReadingButton(_copy["Origin.ExportEpub"]);
        epub.IsEnabled = book.HasExportableChapters;
        epub.AutomationId = "origin-book-export-epub";
        epub.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool Current() => IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
                && Coordinator.IsRetainedOriginBookCurrent(book);
            if (!Current()) return;
            bool saved = await Coordinator.ExportRetainedOriginBookAsync(book, _copy, Current, CancellationToken.None, epub: true);
            if (Current()) _notice = _copy[saved ? "Origin.BookExported" : "Origin.BookExportCancelled"];
        });
        _body.Add(epub);
        var export = NativeTheme.ReadingButton(_copy["Origin.ExportBook"]);
        export.IsEnabled = book.HasExportableChapters;
        export.AutomationId = "origin-book-export";
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
            var account = NativeTheme.ReadingButton(_copy["Origin.BookAccount"]);
            account.AutomationId = "origin-book-account";
            account.IsEnabled = !Coordinator.Account.IsLoading;
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
        if (Coordinator.CanRequestOriginChapter(book) && book.Chapters.Any(c => book.ReadableChapter(c) is null
            && book.Reading(c)?.AuthoringSource is not null))
        {
            var refresh = NativeTheme.ReadingButton(_copy["Origin.AuthoringRefresh"]);
            refresh.AutomationId = "origin-reader-refresh";
            refresh.Clicked += async (_, _) => await RunWithConditionalRefreshAsync(async () =>
            {
                if (!IsCurrentAppearanceGeneration(appearance) || !ReferenceEquals(_book, book)) return false;
                bool changed = await ReadMissingChapterAsync(appearance, default);
                if (_watchPending && IsCurrentAppearanceGeneration(appearance)) StartStatusWatch(appearance);
                return changed;
            });
            _body.Add(refresh);
        }
        if (book.ScenesUnavailable) _body.Add(NativeTheme.Body(_copy["Origin.ScenesUnavailable"]));
        foreach (var chapter in book.Chapters)
        {
            // Metatype/birth decisions form the opening brief, not a short
            // pretend chapter before the generated childhood narrative.
            if (book.IsOpeningSetup(chapter) && book.ReadableChapter(chapter) is null) continue;
            _body.Add(NativeTheme.Title(book.IsOpeningSetup(chapter)
                ? _copy["Origin.OpeningSetupTitle"] : chapter.Title, 21));
            if (book.Scene(chapter) is { } scene)
            {
                _body.Add(OriginBookScenePage.SceneImage(scene, $"origin-book-scene-{chapter.Sequence}",
                    () => IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
                        && Coordinator.IsRetainedOriginBookCurrent(book)));
                _body.Add(NativeTheme.Body(scene.Identity.AltText, NativeTheme.Muted));
            }
            if (book.ReadableChapter(chapter) is { } prose)
            {
                var text = NativeTheme.BookProse(prose.Text);
                text.AutomationId = $"origin-retained-chapter-{chapter.Sequence}";
                _body.Add(text);
                if (book.Reading(chapter)?.Selected?.DraftDigest != prose.DraftDigest)
                {
                    var read = NativeTheme.ReadingButton(_copy["Origin.ChapterRead"]);
                    read.AutomationId = $"origin-chapter-read-{chapter.Sequence}";
                    read.Clicked += async (_, _) => await RunAsync(async () =>
                    {
                        bool Current() => IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
                            && Coordinator.IsRetainedOriginBookCurrent(book);
                        if (!Current()) return;
                        var updated = await Coordinator.ReviewOriginBookProseDraftAsync(book, prose, true, true, Current, default);
                        if (updated is null || !IsCurrentAppearanceGeneration(appearance)
                            || !Coordinator.IsRetainedOriginBookCurrent(updated)) return;
                        _book = updated;
                        await Coordinator.RecordOriginBookReaderAcceptanceAsync(updated, prose,
                            () => IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, updated), default);
                    });
                    _body.Add(read);
                }
            }
            else
            {
                _chapterStatus.TryGetValue(chapter.ChapterId, out var result);
                string state = result?.Job?.State ?? "";
                double progress = state switch
                {
                    OriginChapterAuthoringStates.AwaitingAuthoring => 1d / 3,
                    OriginChapterAuthoringStates.ReconciliationRequired => 2d / 3,
                    _ => 0
                };
                var bar = new ProgressBar { Progress = progress, ProgressColor = NativeTheme.Ink,
                    AutomationId = $"origin-reader-progress-{chapter.Sequence}" };
                SemanticProperties.SetDescription(bar, _copy["Origin.ReaderProgressStages"]);
                _body.Add(bar);
                string message = result?.Outcome switch
                {
                    AndroidOriginChapterOutcome.NotFound => "Origin.AuthoringNotFound",
                    AndroidOriginChapterOutcome.Available => state switch
                    {
                        OriginChapterAuthoringStates.AwaitingAuthoring => "Origin.AuthoringQueued",
                        OriginChapterAuthoringStates.ReconciliationRequired => "Origin.AuthoringOutcomeUnconfirmed",
                        _ => "Origin.ReaderFullTextPending"
                    },
                    _ => "Origin.ReaderFullTextPending"
                };
                var waiting = NativeTheme.Body(_copy[message]);
                waiting.AutomationId = $"origin-reader-status-{chapter.Sequence}";
                _body.Add(waiting);
                var eta = NativeTheme.Body(_copy["Origin.AuthoringEtaUnknown"], NativeTheme.Muted);
                eta.AutomationId = $"origin-reader-eta-{chapter.Sequence}";
                _body.Add(eta);
            }
            if (book.Scene(chapter) is null && Coordinator.CanAutomaticallyIllustrateOriginBook(book)
                && Coordinator.CanRequestOriginBookScene(book, chapter))
            {
                _sceneStatus.TryGetValue(chapter.ChapterId, out var imageStatus);
                bool pending = !_sceneObservationPaused && (imageStatus is null || imageStatus.State == "dispatching");
                var status = NativeTheme.Body(_copy[pending ? "Origin.SceneAutomatic" : "Origin.SceneAutomaticPaused"], NativeTheme.Muted);
                status.AutomationId = $"origin-scene-status-{chapter.Sequence}";
                _body.Add(status);
                if (pending)
                {
                    _body.Add(new ProgressBar { Progress = imageStatus?.State == "dispatching" ? 0.5 : 0,
                        ProgressColor = NativeTheme.Ink, AutomationId = $"origin-scene-progress-{chapter.Sequence}" });
                    _body.Add(NativeTheme.Body(_copy["Origin.AuthoringEtaUnknown"], NativeTheme.Muted));
                }
            }
            if (Coordinator.PrepareOriginChapterSource(book, chapter) is not null)
            {
                var author = NativeTheme.ReadingButton(_copy["Origin.AuthorChapter"]);
                author.AutomationId = $"origin-author-chapter-{chapter.Sequence}";
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
                var review = NativeTheme.ReadingButton(_copy["Origin.ReviewProse"]);
                review.AutomationId = $"origin-review-prose-{chapter.Sequence}";
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
        StartSceneWatch(appearance);
    }

    private void StartSceneWatch(long appearance)
    {
        if (_sceneLifetime is not null || _sceneObservationPaused || _book is not { } book
            || !Coordinator.CanAutomaticallyIllustrateOriginBook(book)) return;
        var lifetime = new CancellationTokenSource(); _sceneLifetime = lifetime;
        _ = WatchScenesAsync(appearance, lifetime);
    }

    private async Task WatchScenesAsync(long appearance, CancellationTokenSource lifetime)
    {
        // Never hold the page action gate across provider I/O: the complete
        // chapter, navigation and export stay usable while an image is pending.
        await Task.Yield();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        int failures = 0;
        RetainedOriginBook? observedBook = null;
        try
        {
            while (!lifetime.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance)
                && elapsed.Elapsed < TimeSpan.FromMinutes(10) && _book is { } book
                && Coordinator.CanAutomaticallyIllustrateOriginBook(book))
            {
                observedBook = book;
                var chapter = book.Chapters.OrderBy(c => c.Sequence).FirstOrDefault(c => book.Scene(c) is null
                    && Coordinator.CanRequestOriginBookScene(book, c) && (!_sceneChecked.Contains(c.ChapterId)
                        || !_sceneStatus.TryGetValue(c.ChapterId, out var status) || status.State is "dispatching" or "uncertain"
                            || status.UnknownRemoteOutcome || status.RetryableReadFailure));
                if (chapter is null) break;
                bool create = _sceneChecked.Add(chapter.ChapterId);
                bool Current() => !lifetime.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance)
                    && ReferenceEquals(_book, book);
                var (result, updated) = await Coordinator.SyncAutomaticOriginBookSceneAsync(book, chapter,
                    create, Current, lifetime.Token);
                if (!Current() || updated is null || !Coordinator.IsRetainedOriginBookCurrent(updated)) break;
                _sceneStatus.TryGetValue(chapter.ChapterId, out var previous);
                _book = updated;
                _sceneStatus[chapter.ChapterId] = result;
                if (!ReferenceEquals(book, updated) || result != previous)
                    await RunWithConditionalRefreshAsync(() => Task.FromResult(!lifetime.IsCancellationRequested
                        && IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, updated)));
                if (result.RetryableReadFailure && ++failures >= 3) { _sceneObservationPaused = true; break; }
                if (!result.RetryableReadFailure) failures = 0;
                if (result.State is "dispatching" or "uncertain" || result.UnknownRemoteOutcome || result.RetryableReadFailure)
                    await Task.Delay(TimeSpan.FromSeconds(15), lifetime.Token);
                else if (updated.Scene(chapter) is null)
                {
                    // Do not charge later scenes when their original character
                    // reference is still absent, rejected or awaiting review.
                    _sceneObservationPaused = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
            // Do not retry a failed durable insertion or expose private provider
            // errors. The authenticated existing image is recoverable by read.
            if (IsCurrentAppearanceGeneration(appearance)) _sceneObservationPaused = true;
        }
        finally
        {
            if (elapsed.Elapsed >= TimeSpan.FromMinutes(10) && IsCurrentAppearanceGeneration(appearance))
                _sceneObservationPaused = true;
            if (ReferenceEquals(_sceneLifetime, lifetime)) _sceneLifetime = null;
            if (_sceneObservationPaused && !lifetime.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance))
                await RunWithConditionalRefreshAsync(() => Task.FromResult(!lifetime.IsCancellationRequested
                    && IsCurrentAppearanceGeneration(appearance)));
            // A simultaneous full-text read can issue a newer retained edition.
            // Reacquire that handle; _sceneChecked still forbids another write
            // for the image whose response belonged to the retired edition.
            if (!lifetime.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance)
                && observedBook is not null && !ReferenceEquals(_book, observedBook)) StartSceneWatch(appearance);
            lifetime.Dispose();
        }
    }

    private async Task<bool> ReadMissingChapterAsync(long appearance, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !IsCurrentAppearanceGeneration(appearance)) return false;
        if (_book is not { } book
            || !Coordinator.CanRequestOriginChapter(book)) { _watchPending = false; return false; }
        var chapter = book.Chapters.FirstOrDefault(c => book.ReadableChapter(c) is null
            && book.Reading(c)?.AuthoringSource is not null
            && Coordinator.PrepareOriginChapterSource(book, c) is not null);
        if (chapter is null) { _watchPending = false; return false; }
        var source = Coordinator.PrepareOriginChapterSource(book, chapter)!;
        bool Current() => !ct.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance)
            && ReferenceEquals(_book, book) && Coordinator.CanRequestOriginChapter(book);
        var (result, updated) = await Coordinator.SyncOriginChapterAsync(book, chapter, source,
            consentToCreate: false, Current, ct, reconcileReaderAcceptance: false);
        if (ct.IsCancellationRequested || !IsCurrentAppearanceGeneration(appearance)
            || !ReferenceEquals(_book, book)) return false;
        if (updated is null || !Coordinator.IsRetainedOriginBookCurrent(updated))
        { _watchPending = false; return false; }
        _book = updated;
        _chapterStatus.TryGetValue(chapter.ChapterId, out var previous);
        bool transient = result.Outcome == AndroidOriginChapterOutcome.Unavailable && result.RetryableReadFailure;
        if (transient)
        {
            _watchPending = ++_readFailures < 3;
            string notice = _copy[_watchPending ? "Origin.AuthoringStatusRetrying" : "Origin.AuthoringStatusPaused"];
            bool changed = _notice != notice; _notice = notice;
            return changed;
        }
        _readFailures = 0;
        _notice = null;
        _chapterStatus[chapter.ChapterId] = result;
        _watchPending = result.Job?.State is OriginChapterAuthoringStates.AwaitingAuthoring
            or OriginChapterAuthoringStates.ReconciliationRequired;
        return !ReferenceEquals(book, updated) || previous?.Outcome != result.Outcome
            || previous?.Job?.State != result.Job?.State;
    }

    private void StartStatusWatch(long appearance)
    {
        if (_pollLifetime is not null) return;
        var lifetime = new CancellationTokenSource(); _pollLifetime = lifetime;
        _ = WatchStatusAsync(appearance, lifetime);
    }

    private async Task WatchStatusAsync(long appearance, CancellationTokenSource lifetime)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (_watchPending && IsCurrentAppearanceGeneration(appearance) && elapsed.Elapsed < TimeSpan.FromMinutes(10))
            {
                await Task.Delay(TimeSpan.FromSeconds(15), lifetime.Token);
                if (elapsed.Elapsed >= TimeSpan.FromMinutes(10)) break;
                await PollChapterOnceAsync(appearance, lifetime.Token);
            }
            if (_watchPending && IsCurrentAppearanceGeneration(appearance))
                await RunWithConditionalRefreshAsync(() =>
                {
                    if (lifetime.IsCancellationRequested || !IsCurrentAppearanceGeneration(appearance)) return Task.FromResult(false);
                    _watchPending = false; _notice = _copy["Origin.AuthoringStatusPaused"];
                    return Task.FromResult(true);
                });
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_pollLifetime, lifetime)) _pollLifetime = null;
            lifetime.Dispose();
        }
    }

    internal Task PollChapterOnceAsync(long appearance, CancellationToken ct)
        => RunWithConditionalRefreshAsync(() => _watchPending
            ? ReadMissingChapterAsync(appearance, ct) : Task.FromResult(false));

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pollLifetime?.Cancel(); _pollLifetime = null;
        _sceneLifetime?.Cancel(); _sceneLifetime = null;
        _sceneChecked.Clear(); _sceneStatus.Clear();
        _sceneObservationPaused = false;
        _watchPending = false; _readFailures = 0; _chapterStatus.Clear();
        _book = null;
        _body.Clear();
    }
}
