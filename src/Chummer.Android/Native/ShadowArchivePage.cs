using Chummer.Android.Platform;
using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

public sealed class ShadowArchivePage : ContentPage
{
    private readonly IShadowArchivePublicCatalogPort _catalog;
    private readonly ShadowArchivePresenter _presenter;
    private readonly IAndroidSystemService _system;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };
    private CancellationTokenSource? _appearanceLifetime;
    private bool _loaded;

    public ShadowArchivePage(
        IShadowArchivePublicCatalogPort catalog,
        ShadowArchivePresenter presenter,
        IAndroidSystemService system)
    {
        _catalog = catalog;
        _presenter = presenter;
        _system = system;
        Title = "Archive";
        AutomationId = "phone-archive";
        BackgroundColor = NativeTheme.Paper;
        Content = new ScrollView { Content = _body };
        RenderLoading();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        _appearanceLifetime?.Cancel();
        _appearanceLifetime?.Dispose();
        CancellationTokenSource lifetime = new();
        _appearanceLifetime = lifetime;
        try
        {
            await LoadAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Leaving the public catalog is a normal cancellation boundary.
            _loaded = false;
        }
    }

    protected override void OnDisappearing()
    {
        _appearanceLifetime?.Cancel();
        _appearanceLifetime?.Dispose();
        _appearanceLifetime = null;
        base.OnDisappearing();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        _loaded = true;
        RenderLoading();
        ShadowArchivePresentationResult<ShadowArchivePublicCatalogViewModel> result;
        try
        {
            result = await _catalog.LoadPublicStoriesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = Unavailable<ShadowArchivePublicCatalogViewModel>();
        }

        if (!result.IsReady || result.Value is null)
        {
            RenderFailure(result.State, result.Error);
            return;
        }

        RenderCatalog(result.Value);
    }

    private void RenderLoading()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Shadow Archive"));
        _body.Add(NativeTheme.Title("Loading public stories"));
        ActivityIndicator loading = new()
        {
            AutomationId = "archive-loading",
            IsRunning = true,
            Color = NativeTheme.Signal
        };
        SemanticProperties.SetDescription(loading, "Loading public stories");
        _body.Add(loading);
    }

    private void RenderCatalog(ShadowArchivePublicCatalogViewModel catalog)
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Shadow Archive"));
        _body.Add(NativeTheme.Title("Public runner stories"));
        _body.Add(NativeTheme.Body(
            "Read and download without an account. Signals are shown here, but voting appears only after the final chapter.",
            NativeTheme.Muted));

        if (catalog.Stories.Count == 0)
        {
            VerticalStackLayout empty = new() { Spacing = 8 };
            empty.Add(NativeTheme.Title("No public stories yet", 21));
            empty.Add(NativeTheme.Body(
                "Published Origin Stories will appear here when the Archive returns them.",
                NativeTheme.Muted));
            Border card = NativeTheme.Card(empty);
            card.AutomationId = "archive-empty";
            _body.Add(card);
            return;
        }

        foreach (ShadowArchivePublicStoryCardViewModel story in catalog.Stories)
        {
            _body.Add(BuildStoryCard(catalog, story));
        }
    }

    private Border BuildStoryCard(
        ShadowArchivePublicCatalogViewModel catalog,
        ShadowArchivePublicStoryCardViewModel story)
    {
        VerticalStackLayout content = new() { Spacing = 8 };
        content.Add(NativeTheme.Eyebrow("Runner"));
        content.Add(NativeTheme.Title(story.Identity.RunnerHeading, 23));
        if (!string.IsNullOrWhiteSpace(story.Identity.RunnerHandle))
        {
            content.Add(NativeTheme.Body(story.Identity.RunnerHandle!, NativeTheme.Muted));
        }
        content.Add(NativeTheme.Title(story.Title, 20));
        content.Add(NativeTheme.Body(story.Summary));
        content.Add(NativeTheme.Body($"Story owner: {story.Identity.StoryOwnerLabel}", NativeTheme.Muted));
        Label signals = NativeTheme.Body($"Signals: {story.SignalCount:N0}", NativeTheme.Muted);
        signals.AutomationId = $"archive-signal-count-{story.Binding.PublicationId}";
        signals.FontAttributes = FontAttributes.Bold;
        content.Add(signals);

        Button read = NativeTheme.PrimaryButton("Read story");
        read.AutomationId = $"archive-read-{story.Binding.PublicationId}";
        read.Clicked += async (_, _) => await Navigation.PushAsync(
            new ShadowArchiveReaderPage(_presenter, _system, story, catalog.Viewer));
        content.Add(read);

        Border card = NativeTheme.Card(content);
        card.AutomationId = $"archive-story-{story.Binding.PublicationId}";
        return card;
    }

    private void RenderFailure(
        ShadowArchivePresentationState state,
        ShadowArchiveErrorViewModel? error)
    {
        ShadowArchivePhoneStateCopy copy = ShadowArchivePhonePolicy.StateCopy(
            state,
            ShadowArchivePhoneSurface.Catalog,
            error);
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Shadow Archive"));
        _body.Add(NativeTheme.Title(copy.Title));
        _body.Add(NativeTheme.Body(copy.Detail, NativeTheme.Muted));
        if (copy.CanRetry)
        {
            Button retry = NativeTheme.SecondaryButton("Retry");
            retry.AutomationId = "archive-retry";
            retry.Clicked += async (_, _) =>
            {
                _loaded = false;
                await LoadAsync(_appearanceLifetime?.Token ?? CancellationToken.None);
            };
            _body.Add(retry);
        }
    }

    private static ShadowArchivePresentationResult<T> Unavailable<T>()
        => new(
            ShadowArchivePresentationState.Unavailable,
            default,
            new ShadowArchiveErrorViewModel(
                "shadow_archive_android_client_failure",
                "Shadow Archive is unavailable. No public response was assumed.",
                null,
                null,
                null,
                null,
                null));
}

internal sealed class ShadowArchiveReaderPage : ContentPage
{
    private readonly ShadowArchivePresenter _presenter;
    private readonly IAndroidSystemService _system;
    private readonly ShadowArchivePublicStoryCardViewModel _story;
    private readonly ShadowArchiveViewerContext _viewer;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 48),
        Spacing = 16
    };
    private CancellationTokenSource? _appearanceLifetime;
    private ShadowArchivePublicReaderViewModel? _reader;
    private ShadowArchivePresentationResult<ShadowArchiveCommunityViewModel>? _community;
    private int _chapterIndex;
    private int _loaded;
    private bool _signalSubmitting;
    private string? _signalIdempotencyKey;

    public ShadowArchiveReaderPage(
        ShadowArchivePresenter presenter,
        IAndroidSystemService system,
        ShadowArchivePublicStoryCardViewModel story,
        ShadowArchiveViewerContext viewer)
    {
        _presenter = presenter;
        _system = system;
        _story = story;
        _viewer = viewer;
        Title = story.Title;
        AutomationId = $"archive-reader-{story.Binding.PublicationId}";
        BackgroundColor = NativeTheme.Paper;
        Content = new ScrollView { Content = _body };
        RenderLoading();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Interlocked.Exchange(ref _loaded, 1) != 0)
        {
            return;
        }

        CancellationTokenSource lifetime = new();
        _appearanceLifetime = lifetime;
        try
        {
            await LoadReaderAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Back navigation cancels public reader work without changing any server state.
            Interlocked.Exchange(ref _loaded, 0);
        }
    }

    protected override void OnDisappearing()
    {
        _appearanceLifetime?.Cancel();
        _appearanceLifetime?.Dispose();
        _appearanceLifetime = null;
        base.OnDisappearing();
    }

    private async Task LoadReaderAsync(CancellationToken cancellationToken)
    {
        RenderLoading();
        ShadowArchivePublicReaderQuery readerQuery = new(
            _story.Binding.PublicationId,
            _story.Binding.PublicationRevision,
            _story.Binding.ContentDigest);
        ShadowArchivePresentationResult<ShadowArchivePublicReaderViewModel> readerResult;
        try
        {
            readerResult = await _presenter.LoadPublicReaderAsync(readerQuery, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            readerResult = Unavailable<ShadowArchivePublicReaderViewModel>();
        }

        if (!readerResult.IsReady || readerResult.Value is null)
        {
            RenderReaderFailure(readerResult.State, readerResult.Error);
            return;
        }

        _reader = readerResult.Value;
        _chapterIndex = Math.Clamp(_chapterIndex, 0, _reader.Chapters.Count - 1);
        RenderReader();

        ShadowArchiveCommunityQuery communityQuery = new(
            _story.Binding.PublicationId,
            _story.Binding.PublicationRevision,
            _story.Binding.ContentDigest);
        try
        {
            _community = await _presenter.LoadCommunityAsync(communityQuery, _viewer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _community = Unavailable<ShadowArchiveCommunityViewModel>();
        }
        RenderReader();
    }

    private void RenderLoading()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Shadow Archive"));
        _body.Add(NativeTheme.Title("Loading story"));
        ActivityIndicator loading = new()
        {
            AutomationId = "archive-reader-loading",
            IsRunning = true,
            Color = NativeTheme.Signal
        };
        _body.Add(loading);
    }

    private void RenderReader()
    {
        if (_reader is null)
        {
            return;
        }

        ShadowArchivePublicReaderViewModel reader = _reader;
        ShadowArchiveReaderChapterViewModel chapter = reader.Chapters[_chapterIndex];
        bool isAtFinalChapter = _chapterIndex == reader.Chapters.Count - 1;

        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Runner"));
        _body.Add(NativeTheme.Title(reader.Identity.RunnerHeading));
        if (!string.IsNullOrWhiteSpace(reader.Identity.RunnerHandle))
        {
            _body.Add(NativeTheme.Body(reader.Identity.RunnerHandle!, NativeTheme.Muted));
        }
        _body.Add(NativeTheme.Title(reader.Title, 22));
        _body.Add(NativeTheme.Body(reader.Summary, NativeTheme.Muted));
        _body.Add(NativeTheme.Body($"Story owner: {reader.Identity.StoryOwnerLabel}", NativeTheme.Muted));
        int signalCount = _community?.IsReady == true && _community.Value is not null
            ? _community.Value.Signal.VoteCount
            : _story.SignalCount;
        Label signals = NativeTheme.Body($"Signals: {signalCount:N0}", NativeTheme.Muted);
        signals.AutomationId = "archive-reader-signal-count";
        _body.Add(signals);

        VerticalStackLayout chapterCard = new() { Spacing = 12 };
        chapterCard.Add(NativeTheme.Eyebrow($"Chapter {chapter.Sequence} of {reader.Chapters.Count}"));
        chapterCard.Add(NativeTheme.Title(chapter.Title, 23));
        Label chapterBody = NativeTheme.Body(chapter.BodyMarkdown);
        chapterBody.AutomationId = $"archive-chapter-{chapter.ChapterId}";
        chapterBody.LineHeight = 1.3;
        chapterCard.Add(chapterBody);
        _body.Add(NativeTheme.Card(chapterCard));

        Grid navigation = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        Button previous = NativeTheme.SecondaryButton("Previous");
        previous.AutomationId = "archive-reader-previous";
        previous.IsEnabled = _chapterIndex > 0;
        previous.Clicked += (_, _) =>
        {
            _chapterIndex--;
            RenderReader();
        };
        navigation.Add(previous);
        Button next = NativeTheme.PrimaryButton(isAtFinalChapter ? "Last chapter" : "Next chapter");
        next.AutomationId = "archive-reader-next";
        next.IsEnabled = !isAtFinalChapter;
        next.Clicked += (_, _) =>
        {
            _chapterIndex++;
            RenderReader();
        };
        navigation.Add(next, 1);
        _body.Add(navigation);

        AddDownloads(reader);
        AddFinalChapterSignal(reader, isAtFinalChapter);
    }

    private void AddDownloads(ShadowArchivePublicReaderViewModel reader)
    {
        VerticalStackLayout downloads = new() { Spacing = 10 };
        downloads.Add(NativeTheme.Eyebrow("Public downloads"));
        if (reader.Downloads.Count == 0)
        {
            downloads.Add(NativeTheme.Body("No downloads are available for this revision.", NativeTheme.Muted));
        }
        else
        {
            foreach (ShadowArchiveDownloadViewModel download in reader.Downloads)
            {
                Button open = NativeTheme.SecondaryButton($"{download.DisplayName} ({download.Format.ToUpperInvariant()})");
                open.AutomationId = $"archive-download-{download.ArtifactId}";
                open.Clicked += async (_, _) =>
                {
                    if (!await _system.OpenUriAsync(download.DownloadUri))
                    {
                        await DisplayAlertAsync("Download unavailable", "Android could not open this public download.", "OK");
                    }
                };
                downloads.Add(open);
            }
        }
        downloads.Add(NativeTheme.Body("No account is required to read or download.", NativeTheme.Muted));
        _body.Add(NativeTheme.Card(downloads));
    }

    private void AddFinalChapterSignal(
        ShadowArchivePublicReaderViewModel reader,
        bool isAtFinalChapter)
    {
        if (!isAtFinalChapter)
        {
            return;
        }

        if (_community is not { IsReady: true, Value: not null })
        {
            if (_community is null)
            {
                _body.Add(NativeTheme.Body("Loading Signal status…", NativeTheme.Muted));
                return;
            }

            ShadowArchivePhoneStateCopy failure = ShadowArchivePhonePolicy.StateCopy(
                _community.State,
                ShadowArchivePhoneSurface.Signal,
                _community.Error);
            VerticalStackLayout unavailable = new() { Spacing = 7 };
            unavailable.Add(NativeTheme.Eyebrow("Signal"));
            unavailable.Add(NativeTheme.Title(failure.Title, 20));
            unavailable.Add(NativeTheme.Body(failure.Detail, NativeTheme.Muted));
            _body.Add(NativeTheme.Card(unavailable));
            return;
        }

        ShadowArchiveCommunityViewModel community = _community.Value;
        ShadowArchivePhoneSignalProjection signal = ShadowArchivePhonePolicy.ProjectSignal(
            _chapterIndex,
            reader.Chapters.Count,
            _story.ViewerIsOwner,
            community.Signal);
        if (signal.Kind == ShadowArchivePhoneSignalKind.Hidden)
        {
            return;
        }

        VerticalStackLayout section = new() { Spacing = 9 };
        section.Add(NativeTheme.Eyebrow("Signal"));
        section.Add(NativeTheme.Title("You reached the end", 21));
        if (!string.IsNullOrWhiteSpace(signal.Detail))
        {
            section.Add(NativeTheme.Body(signal.Detail!, NativeTheme.Muted));
        }

        if (signal.Kind is ShadowArchivePhoneSignalKind.Vote
            or ShadowArchivePhoneSignalKind.Retract
            or ShadowArchivePhoneSignalKind.SignInRequired)
        {
            Button action = NativeTheme.PrimaryButton(signal.Label!);
            action.AutomationId = signal.Kind switch
            {
                ShadowArchivePhoneSignalKind.Vote => "archive-signal-vote",
                ShadowArchivePhoneSignalKind.Retract => "archive-signal-retract",
                _ => "archive-signal-sign-in"
            };
            action.IsEnabled = !_signalSubmitting;
            action.Clicked += async (_, _) =>
            {
                if (signal.Kind == ShadowArchivePhoneSignalKind.SignInRequired)
                {
                    await DisplayAlertAsync(
                        "Sign in to Signal",
                        "Link your account from More. This story and its downloads remain public.",
                        "OK");
                    return;
                }

                await SubmitSignalAsync(signal.Kind, community);
            };
            section.Add(action);
        }
        _body.Add(NativeTheme.Card(section));
    }

    private async Task SubmitSignalAsync(
        ShadowArchivePhoneSignalKind kind,
        ShadowArchiveCommunityViewModel current)
    {
        if (_signalSubmitting || _story.ViewerIsOwner)
        {
            return;
        }

        _signalSubmitting = true;
        _signalIdempotencyKey ??= $"android-phone-{Guid.NewGuid():N}";
        RenderReader();
        try
        {
            string intent = kind == ShadowArchivePhoneSignalKind.Retract
                ? ShadowArchiveSignalIntents.Retract
                : ShadowArchiveSignalIntents.Vote;
            ShadowArchiveSignalCommandResult command = _presenter.CreateSignalCommand(
                current,
                _viewer,
                intent,
                _signalIdempotencyKey);
            if (!command.CanSubmit || command.Mutation is null)
            {
                _community = new(command.State, null, command.Error);
                return;
            }

            CancellationToken cancellationToken = _appearanceLifetime?.Token ?? CancellationToken.None;
            _community = await _presenter.SubmitSignalAsync(command.Mutation, _viewer, cancellationToken);
            if (_community.IsReady)
            {
                _signalIdempotencyKey = null;
            }
        }
        catch (OperationCanceledException) when (_appearanceLifetime?.IsCancellationRequested == true)
        {
            // Preserve the idempotency key so an explicit retry cannot double-cast.
        }
        catch
        {
            _community = Unavailable<ShadowArchiveCommunityViewModel>();
        }
        finally
        {
            _signalSubmitting = false;
            RenderReader();
        }
    }

    private void RenderReaderFailure(
        ShadowArchivePresentationState state,
        ShadowArchiveErrorViewModel? error)
    {
        ShadowArchivePhoneStateCopy copy = ShadowArchivePhonePolicy.StateCopy(
            state,
            ShadowArchivePhoneSurface.Reader,
            error);
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Shadow Archive"));
        _body.Add(NativeTheme.Title(copy.Title));
        _body.Add(NativeTheme.Body(copy.Detail, NativeTheme.Muted));
        if (copy.CanRetry)
        {
            Button retry = NativeTheme.SecondaryButton("Retry story");
            retry.AutomationId = "archive-reader-retry";
            retry.Clicked += async (_, _) => await LoadReaderAsync(
                _appearanceLifetime?.Token ?? CancellationToken.None);
            _body.Add(retry);
        }
    }

    private static ShadowArchivePresentationResult<T> Unavailable<T>()
        => new(
            ShadowArchivePresentationState.Unavailable,
            default,
            new ShadowArchiveErrorViewModel(
                "shadow_archive_android_client_failure",
                "Shadow Archive is unavailable. No public response or Signal change was assumed.",
                null,
                null,
                null,
                null,
                null));
}
