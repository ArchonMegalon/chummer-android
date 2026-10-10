using System.Globalization;
using Chummer.Android.Platform;
using Chummer.Presentation.OriginBooks;
using Chummer.Run.Contracts.Community;

namespace Chummer.Android.Native;

internal sealed class RetainedOriginBookPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new() { Padding = 20, Spacing = 14 };
    private readonly VerticalStackLayout _progress = new() { Padding = new Thickness(20, 8), Spacing = 4,
        BackgroundColor = NativeTheme.Paper, IsVisible = false, AutomationId = "origin-reader-pinned-progress" };
    private readonly AndroidSurfaceCopy _copy = AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name);
    private RetainedOriginBook? _book;
    private OriginBookReaderLoad? _load;
    private string? _notice;
    private readonly Dictionary<string, AndroidOriginChapterResult> _chapterStatus = new(StringComparer.Ordinal);
    private CancellationTokenSource? _pollLifetime;
    private CancellationTokenSource? _acceptanceLifetime;
    private bool _watchPending;
    private int _readFailures;
    private long? _chapterReadAppearance;
    private long? _openingAppearance;
    private readonly HashSet<string> _unreadChecked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _registrationChecked = new(StringComparer.Ordinal);
    private CancellationTokenSource? _sceneLifetime;
    // Actual dispatches or known remote admissions, never a failed read probe.
    private readonly HashSet<string> _sceneChecked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AndroidOriginSceneResult> _sceneStatus = new(StringComparer.Ordinal);
    private bool _sceneObservationPaused;
    private readonly Func<Task>? _returnToRunner;

    public RetainedOriginBookPage(RunnerSessionCoordinator coordinator, Func<Task>? returnToRunner = null) : base(coordinator)
    {
        _returnToRunner = returnToRunner;
        Title = _copy["Origin.ReadBook"];
        AutomationId = "origin-retained-book";
        var layout = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        layout.Add(_progress, 0, 0);
        layout.Add(new ScrollView { Content = _body }, 0, 1);
        Content = layout;
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
        _openingAppearance = appearance;
        try
        {
            _book = null;
            _load = null;
            _notice = null;
            _unreadChecked.Clear();
            _registrationChecked.Clear();
            var loaded = await Coordinator.LoadOriginBookReaderAsync(ct, () => IsCurrentAppearanceGeneration(appearance));
            if (ct.IsCancellationRequested || !IsCurrentAppearanceGeneration(appearance)) return;
            _load = loaded;
            _book = loaded.Book;
            // The admitted local edition is already readable/exportable. A slow
            // status request for a later chapter must not hide those saved pages.
            Refresh();
            await ReadMissingChapterAsync(appearance, ct);
            if (_watchPending && IsCurrentAppearanceGeneration(appearance)) StartStatusWatch(appearance);
        }
        finally
        {
            if (_openingAppearance == appearance) _openingAppearance = null;
        }
    }

    protected override void Refresh()
    {
        _body.Clear();
        _progress.Clear();
        _progress.IsVisible = false;
        if (_book is not { } book || !Coordinator.IsRetainedOriginBookCurrent(book))
        {
            _book = null;
            if (_load is { OpeningNotStarted: true } load && Coordinator.CanReadRetainedOriginBook(load.Display))
            {
                _body.Add(NativeTheme.Title(_copy["Origin.BookNotStarted"]));
                var setup = NativeTheme.Body(_copy["Origin.OpeningSetupRequired"]);
                setup.AutomationId = "origin-book-opening-setup";
                _body.Add(setup);
                _body.Add(NativeTheme.Body(_copy["Origin.BookNotStartedDetail"], NativeTheme.Muted));
                AddReturnToRunner();
            }
            else _body.Add(NativeTheme.Body(_copy["Origin.BookUnavailable"]));
            return;
        }
        _body.Add(NativeTheme.Title(book.RunnerName));
        _body.Add(NativeTheme.Body(_copy.Format("Origin.BookLanguage", book.Locale), NativeTheme.Muted));
        bool hasReadableChapter = book.Chapters.Any(c => book.ReadableChapter(c) is not null);
        if (!hasReadableChapter) _body.Add(NativeTheme.Body(_copy["Origin.BookSavedChapters"], NativeTheme.Muted));
        if (book.UsesSr5Opening && !hasReadableChapter)
        {
            var opening = NativeTheme.Body(_copy[book.OpeningSetupComplete
                ? "Origin.OpeningSetupReady" : "Origin.OpeningSetupRequired"], NativeTheme.Muted);
            opening.AutomationId = "origin-book-opening-setup";
            _body.Add(opening);
            if (!book.OpeningSetupComplete) AddReturnToRunner();
        }
        long appearance = CaptureAppearanceGeneration();
        AddPinnedProgress(book);
        // Do not offer empty books or decision summaries as downloadable prose.
        // Completed chapters remain available while later chapters are pending.
        if (book.HasExportableChapters)
        {
            var epub = NativeTheme.ReadingButton(_copy["Origin.ExportEpub"]);
            epub.AutomationId = "origin-book-export-epub";
            epub.Clicked += async (_, _) => await RunAsync(async () =>
            {
                bool Current() => IsCurrentAppearanceGeneration(appearance);
                if (!Current() || !ReferenceEquals(_book, book) || !Coordinator.IsRetainedOriginBookCurrent(book)) return;
                bool saved = await Coordinator.ExportRetainedOriginBookAsync(book, _copy, Current, CancellationToken.None, epub: true);
                if (Current() && _book is { } current && Coordinator.IsRetainedOriginBookCurrent(current))
                    _notice = _copy[saved ? "Origin.BookExported" : "Origin.BookExportCancelled"];
            });
            _body.Add(epub);
            var export = NativeTheme.ReadingButton(_copy["Origin.ExportBook"]);
            export.AutomationId = "origin-book-export";
            export.Clicked += async (_, _) => await RunAsync(async () =>
            {
                bool Current() => IsCurrentAppearanceGeneration(appearance);
                if (!Current() || !ReferenceEquals(_book, book) || !Coordinator.IsRetainedOriginBookCurrent(book)) return;
                bool saved = await Coordinator.ExportRetainedOriginBookAsync(book, _copy, Current, CancellationToken.None);
                if (Current() && _book is { } current && Coordinator.IsRetainedOriginBookCurrent(current))
                    _notice = _copy[saved ? "Origin.BookExported" : "Origin.BookExportCancelled"];
            });
            _body.Add(export);
        }
        if (!hasReadableChapter) AddAccountRoute(book, appearance);
        if (_notice is not null) _body.Add(NativeTheme.Body(_notice));
        // A read can fail before any authoring source is frozen. Keep recovery
        // available after the bounded watch pauses, not only for admitted jobs.
        // The normal read-first path still fences every uncertain paid request.
        if (Coordinator.CanRequestOriginChapter(book) && book.Chapters.Any(c => (book.ReadableChapter(c) is null
                || book.Reading(c) is { Selected: null, Pending: not null, AuthoringSource: not null })
            && Coordinator.PrepareOriginChapterSource(book, c) is not null))
        {
            var refresh = NativeTheme.ReadingButton(_copy["Origin.AuthoringRefresh"]);
            refresh.AutomationId = "origin-reader-refresh";
            refresh.Clicked += async (_, _) =>
            {
                if (!IsCurrentAppearanceGeneration(appearance) || !ReferenceEquals(_book, book)
                    || _chapterReadAppearance == appearance) return;
                // Only a new explicit check resets the bounded read reserve.
                // Keep durable authoring fences and any active read untouched.
                _readFailures = 0;
                _unreadChecked.Clear();
                _registrationChecked.Clear();
                await RefreshChapterStatusAsync(appearance, default);
                if (_watchPending && IsCurrentAppearanceGeneration(appearance)) StartStatusWatch(appearance);
            };
            _body.Add(refresh);
        }
        if (book.ScenesUnavailable) _body.Add(NativeTheme.Body(_copy["Origin.ScenesUnavailable"]));
        // Unread full prose first, then saved prose. Missing chapters must not
        // bury text that already exists; their live status stays pinned above.
        foreach (var chapter in book.Chapters.OrderBy(c => book.ReadableChapter(c) is null ? 2
            : book.IsExportableChapter(c) ? 1 : 0))
        {
            // Metatype/birth decisions form the opening brief, not a short
            // pretend chapter before the generated childhood narrative.
            if ((book.IsOpeningSetup(chapter) || book.IsSelectionFinish(chapter)) && book.ReadableChapter(chapter) is null) continue;
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
                    read.Clicked += async (_, _) => await ConfirmReadAsync(book, prose, appearance);
                    _body.Add(read);
                }
                else
                {
                    var changes = book.MechanicsAfterReading(chapter);
                    if (changes.Count > 0)
                    {
                        var effects = new VerticalStackLayout { Spacing = 8,
                            AutomationId = $"origin-read-chapter-effects-{chapter.Sequence}" };
                        effects.Add(NativeTheme.Title(_copy["Origin.AfterReadingChanges"], 18));
                        foreach (string change in changes) effects.Add(NativeTheme.Body(change));
                        _body.Add(NativeTheme.Card(effects));
                    }
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
                    AndroidOriginChapterOutcome.NotFound => book.Reading(chapter)?.AuthoringSource is not null
                        ? "Origin.AuthoringOutcomeUnconfirmed" : "Origin.ReaderFullTextPending",
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
        }
        if (hasReadableChapter) AddAccountRoute(book, appearance);
        if (book.HasReadCurrentStory) AddReturnToRunner();
        _body.Add(NativeTheme.Body(_copy.Format("Origin.BookMetadata", "chummer.run"), NativeTheme.Muted));
        // Keep the existing initial text-read -> illustration ordering even
        // when an export refreshes the now-readable page during that first read.
        if (_openingAppearance != appearance) StartSceneWatch(appearance);
    }

    private void AddPinnedProgress(RetainedOriginBook book)
    {
        if (!Coordinator.Account.IsLinked || !book.OpeningSetupComplete) return;
        var chapter = book.Chapters.FirstOrDefault(c => !book.IsOpeningSetup(c) && !book.IsSelectionFinish(c)
            && book.ReadableChapter(c) is null);
        if (chapter is null) return;
        _chapterStatus.TryGetValue(chapter.ChapterId, out var result);
        bool active = _chapterReadAppearance is not null || _watchPending;
        _progress.IsVisible = true;
        _progress.Add(new ActivityIndicator { IsRunning = active, IsVisible = active,
            AutomationId = "origin-reader-writing-spinner" });
        string detailKey = !active && _notice == _copy["Origin.AuthoringStatusPaused"] ? "Origin.AuthoringStatusPaused"
            : result?.UnknownRemoteOutcome == true ? "Origin.AuthoringOutcomeUnconfirmed"
            : result?.Outcome == AndroidOriginChapterOutcome.NotFound && book.Reading(chapter)?.AuthoringSource is not null
                ? "Origin.AuthoringOutcomeUnconfirmed"
            : result?.Job?.State == OriginChapterAuthoringStates.AwaitingAuthoring ? "Origin.AuthoringQueued"
            : result?.Job?.State == OriginChapterAuthoringStates.ReconciliationRequired ? "Origin.AuthoringOutcomeUnconfirmed"
            : "Origin.ReaderFullTextPending";
        string compactKey = detailKey switch
        {
            "Origin.AuthoringStatusPaused" => "Origin.ReaderChecksPausedCompact",
            "Origin.AuthoringOutcomeUnconfirmed" => "Origin.ReaderOutcomeUnknownCompact",
            "Origin.AuthoringQueued" => "Origin.ReaderQueuedCompact",
            _ => "Origin.ReaderPendingCompact"
        };
        // This stays above the scrolling book. Keep full explanations in the
        // chapter body and accessibility description, not over the saved prose.
        // Preserve wrapping/font scaling rather than clipping enlarged text.
        var message = NativeTheme.Body(_copy[compactKey]);
        message.AutomationId = "origin-reader-writing-status";
        SemanticProperties.SetDescription(message, _copy[detailKey]);
        _progress.Add(message);
        var progress = new ProgressBar { Progress = result?.Job?.State switch
            {
                OriginChapterAuthoringStates.AwaitingAuthoring => 1d / 3,
                OriginChapterAuthoringStates.ReconciliationRequired => 2d / 3,
                _ => 0
            }, ProgressColor = NativeTheme.Ink, AutomationId = "origin-reader-writing-progress" };
        SemanticProperties.SetDescription(progress, _copy["Origin.ReaderProgressStages"]);
        _progress.Add(progress);
        var eta = NativeTheme.Body(_copy["Origin.ReaderEtaUnknownCompact"], NativeTheme.Muted);
        eta.AutomationId = "origin-reader-writing-eta";
        SemanticProperties.SetDescription(eta, _copy["Origin.AuthoringEtaUnknown"]);
        _progress.Add(eta);
    }

    private async Task ConfirmReadAsync(RetainedOriginBook book, OriginBookProseDraft prose, long appearance)
    {
        RetainedOriginBook? accepted = null;
        await RunAsync(async () =>
        {
            bool Current() => IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
                && Coordinator.IsRetainedOriginBookCurrent(book);
            if (!Current()) return;
            var updated = await Coordinator.ReviewOriginBookProseDraftAsync(book, prose, true, true, Current, default);
            if (updated is null || !IsCurrentAppearanceGeneration(appearance)
                || !Coordinator.IsRetainedOriginBookCurrent(updated)) return;
            _book = accepted = updated;
        });
        if (accepted is null || !IsCurrentAppearanceGeneration(appearance)
            || !ReferenceEquals(_book, accepted) || !Coordinator.IsRetainedOriginBookCurrent(accepted)) return;

        // Local explicit reading is durable before this best-effort delivery.
        // Keep effects, export and Return usable while Hub acknowledges it.
        // The existing selected-edition outbox reconciles a lost response before
        // any successor request; navigation must never retry a paid generation.
        _acceptanceLifetime?.Cancel();
        var lifetime = new CancellationTokenSource();
        _acceptanceLifetime = lifetime;
        bool StillHere() => !lifetime.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance)
            && ReferenceEquals(_book, accepted);
        try
        {
            if (accepted.Chapters.Any(c => accepted.ReadableChapter(c) is null
                && Coordinator.PrepareOriginChapterSource(accepted, c) is not null))
            {
                // Restored history can already contain the next confirmed
                // module. Reading its predecessor makes that chapter eligible
                // now, without another click/reopen. The existing read-first
                // path reconciles this exact saved acceptance before dispatch;
                // do not send a separate acknowledgement and then retry it.
                await RefreshChapterStatusAsync(appearance, lifetime.Token);
                if (_watchPending && !lifetime.IsCancellationRequested
                    && IsCurrentAppearanceGeneration(appearance)) StartStatusWatch(appearance);
            }
            else
                await Coordinator.RecordOriginBookReaderAcceptanceAsync(accepted, prose, StillHere, lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (StillHere())
                await RunWithConditionalRefreshAsync(() => StillHere()
                    ? Task.FromException<bool>(error) : Task.FromResult(false));
        }
        finally
        {
            if (ReferenceEquals(_acceptanceLifetime, lifetime)) _acceptanceLifetime = null;
            lifetime.Dispose();
        }
    }

    private void AddAccountRoute(RetainedOriginBook book, long appearance)
    {
        if (Coordinator.Account.IsLinked) return;
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

    private void AddReturnToRunner()
    {
        long appearance = CaptureAppearanceGeneration();
        var back = NativeTheme.ReadingButton(_copy["Origin.BookReturnToRunner"]);
        back.AutomationId = "origin-book-return-to-runner";
        back.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!IsCurrentAppearanceGeneration(appearance)) return;
            if (_returnToRunner is not null) await _returnToRunner();
            else await Navigation.PopAsync();
        });
        _body.Add(back);
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
                bool create = !_sceneChecked.Contains(chapter.ChapterId);
                bool Current() => !lifetime.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance)
                    && ReferenceEquals(_book, book);
                void FenceCreation()
                {
                    // Keep the fence across newer editions, but never let a
                    // retired appearance alter the next appearance's state.
                    if (!lifetime.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance))
                        _sceneChecked.Add(chapter.ChapterId);
                }
                var (result, updated) = await Coordinator.SyncAutomaticOriginBookSceneAsync(book, chapter,
                    create, Current, lifetime.Token, onCreationFenced: FenceCreation);
                if (!Current() || updated is null || !Coordinator.IsRetainedOriginBookCurrent(updated)) break;
                _sceneStatus.TryGetValue(chapter.ChapterId, out var previous);
                _book = updated;
                _sceneStatus[chapter.ChapterId] = result;
                if ((!ReferenceEquals(book, updated) || result != previous)
                    && !lifetime.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance)
                    && ReferenceEquals(_book, updated))
                    await RefreshAfterBackgroundReadAsync(appearance);
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
                await RefreshAfterBackgroundReadAsync(appearance);
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
        // Serialize reads within this appearance without occupying the action
        // gate while awaiting Hub. A retired appearance cannot clear a newer
        // read's ownership or stop its watch.
        if (ct.IsCancellationRequested || !IsCurrentAppearanceGeneration(appearance)
            || _chapterReadAppearance == appearance) return false;
        _chapterReadAppearance = appearance;
        if (_book is { } visible) { _progress.Clear(); AddPinnedProgress(visible); }
        try { return await ReadMissingChapterCoreAsync(appearance, ct); }
        finally
        {
            if (_chapterReadAppearance == appearance) _chapterReadAppearance = null;
            if (IsCurrentAppearanceGeneration(appearance) && _book is { } current)
            { _progress.Clear(); _progress.IsVisible = false; AddPinnedProgress(current); }
        }
    }

    private async Task<bool> ReadMissingChapterCoreAsync(long appearance, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !IsCurrentAppearanceGeneration(appearance)) return false;
        if (_book is not { } book
            || !Coordinator.CanRequestOriginChapter(book)) { _watchPending = false; return false; }
        // Check saved, unread prose once on entry (or explicit Refresh). Hub
        // may have corrected that exact draft since it was retained. Keep local
        // text visible throughout the read; do not poll/rewrite accepted prose.
        var chapter = book.Chapters.FirstOrDefault(c => !_unreadChecked.Contains(c.ChapterId)
            && book.Reading(c) is { Selected: null, Pending: not null, AuthoringSource: not null }
            && book.ReadableChapter(c) is not null && Coordinator.PrepareOriginChapterSource(book, c) is not null)
            ?? book.Chapters.FirstOrDefault(c => book.ReadableChapter(c) is null
            && Coordinator.PrepareOriginChapterSource(book, c) is not null);
        if (chapter is null) { _watchPending = false; return false; }
        var source = Coordinator.PrepareOriginChapterSource(book, chapter)!;
        // The confirmed Origin choices are the source approval. No separate
        // provider toggle/button. A frozen source remains immutable. Recovery
        // needs Hub's explicit same-request registration contract, not a bare
        // 404 or a provider reporting no result. Polls never repeat a request.
        bool newChapter = book.Reading(chapter)?.AuthoringSource is null;
        // The coordinator retires this reading edition when it durably freezes
        // a new request and validates the replacement against the same owner.
        // The page guard tracks navigation, not the superseded edition's lease.
        bool Current() => !ct.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance)
            && ReferenceEquals(_book, book);
        var (result, updated) = await Coordinator.SyncOriginChapterAsync(book, chapter, source,
            consentToCreate: newChapter, Current, ct, reconcileReaderAcceptance: false,
            consentToAutomaticIllustrations: newChapter,
            recoverMissingRegistration: !_registrationChecked.Contains(chapter.ChapterId),
            onRequesting: () =>
            {
                if (Current()) _registrationChecked.Add(chapter.ChapterId);
            });
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
        _unreadChecked.Add(chapter.ChapterId);
        bool noticeCleared = _notice is not null;
        _notice = null;
        _chapterStatus[chapter.ChapterId] = result;
        _watchPending = result.Job?.State is OriginChapterAuthoringStates.AwaitingAuthoring
            or OriginChapterAuthoringStates.ReconciliationRequired || result.UnknownRemoteOutcome;
        return noticeCleared || !ReferenceEquals(book, updated) || previous?.Outcome != result.Outcome
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
            if (_watchPending && !lifetime.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance))
            {
                _watchPending = false; _notice = _copy["Origin.AuthoringStatusPaused"];
                await RefreshAfterBackgroundReadAsync(appearance);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_pollLifetime, lifetime)) _pollLifetime = null;
            lifetime.Dispose();
        }
    }

    internal Task PollChapterOnceAsync(long appearance, CancellationToken ct)
        => _watchPending ? RefreshChapterStatusAsync(appearance, ct) : Task.CompletedTask;

    private async Task RefreshChapterStatusAsync(long appearance, CancellationToken ct)
    {
        bool Current() => !ct.IsCancellationRequested && IsCurrentAppearanceGeneration(appearance);
        if (!Current()) return;
        try
        {
            // Do not silently reject Read/Export actions for the duration of a
            // provider read. Only the short render uses the normal action gate.
            bool changed = await ReadMissingChapterAsync(appearance, ct);
            if (changed && Current()) await RefreshAfterBackgroundReadAsync(appearance);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!Current()) return;
            _watchPending = false;
            _notice = _copy["Origin.AuthoringStatusPaused"];
            // ReadMissingChapterAsync restored the ribbon before this catch
            // stopped the watch. Update only that pinned status: saved prose
            // and an in-flight export must not be rebuilt or interrupted.
            _progress.Clear(); _progress.IsVisible = false;
            if (_book is { } book && Coordinator.IsRetainedOriginBookCurrent(book)) AddPinnedProgress(book);
            // Preserve the existing page error handling; never leave an async
            // click/watch exception unobserved or retry an unclassified failure.
            await RunWithConditionalRefreshAsync(() => Task.FromException<bool>(error));
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _acceptanceLifetime?.Cancel(); _acceptanceLifetime = null;
        _pollLifetime?.Cancel(); _pollLifetime = null;
        _sceneLifetime?.Cancel(); _sceneLifetime = null;
        _sceneChecked.Clear(); _sceneStatus.Clear();
        _unreadChecked.Clear();
        _sceneObservationPaused = false;
        _watchPending = false; _readFailures = 0; _chapterStatus.Clear();
        _book = null;
        _load = null;
        _body.Clear();
        _progress.Clear(); _progress.IsVisible = false;
    }
}
