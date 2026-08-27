using Chummer.Android.Platform;
using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

public sealed class ShadowArchivePage : ContentPage
{
    private readonly IShadowArchivePublicCatalogPort _catalog;
    private readonly ShadowArchivePresenter _presenter;
    private readonly IAndroidSystemService _system;
    private readonly AndroidSurfaceCopy _copy;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };
    private CancellationTokenSource? _appearanceLifetime;
    private ShadowArchiveCatalogFilterSelection _filterSelection = new(null, null);
    private bool _loaded;

    public ShadowArchivePage(
        IShadowArchivePublicCatalogPort catalog,
        ShadowArchivePresenter presenter,
        IAndroidSystemService system)
    {
        _catalog = catalog;
        _presenter = presenter;
        _system = system;
        _copy = AndroidSurfaceStrings.Resolve();
        Title = _copy["Stories.Title"];
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
        _body.Add(NativeTheme.Eyebrow(_copy["Stories.Title"]));
        _body.Add(NativeTheme.Title(_copy["Stories.LoadingCatalog"]));
        ActivityIndicator loading = new()
        {
            AutomationId = "archive-loading",
            IsRunning = true,
            Color = NativeTheme.Signal
        };
        SemanticProperties.SetDescription(loading, _copy["Stories.LoadingCatalog"]);
        _body.Add(loading);
    }

    private void RenderCatalog(ShadowArchivePublicCatalogViewModel catalog)
    {
        ShadowArchiveFilteredCatalogViewModel filtered;
        try
        {
            filtered = ShadowArchiveCatalogFilterPolicy.Project(catalog, _filterSelection);
        }
        catch
        {
            RenderFailure(
                ShadowArchivePresentationState.InvalidContract,
                new ShadowArchiveErrorViewModel(
                    "stories_catalog_metadata_invalid",
                    _copy["Stories.CatalogInvalid"],
                    null,
                    null,
                    null,
                    null,
                    null));
            return;
        }

        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(_copy["Stories.Title"]));
        _body.Add(NativeTheme.Title(_copy["Stories.Public"]));
        _body.Add(NativeTheme.Body(_copy["Stories.Intro"], NativeTheme.Muted));

        if (catalog.Stories.Count == 0)
        {
            VerticalStackLayout empty = new() { Spacing = 8 };
            empty.Add(NativeTheme.Title(_copy["Stories.EmptyTitle"], 21));
            empty.Add(NativeTheme.Body(_copy["Stories.EmptyDetail"], NativeTheme.Muted));
            Border card = NativeTheme.Card(empty);
            card.AutomationId = "archive-empty";
            _body.Add(card);
            return;
        }

        _body.Add(BuildFilterPicker(
            _copy["Stories.LanguageEdition"],
            _copy["Stories.AllLanguages"],
            "archive-filter-language",
            _copy["Stories.LanguageFilterSemantic"],
            filtered.LanguageEditions,
            filtered.Selection.LanguageEditionId,
            selected =>
            {
                _filterSelection = _filterSelection with { LanguageEditionId = selected };
                RenderCatalog(catalog);
            }));
        _body.Add(BuildFilterPicker(
            _copy["Stories.Archetype"],
            _copy["Stories.AllArchetypes"],
            "archive-filter-archetype",
            _copy["Stories.ArchetypeFilterSemantic"],
            filtered.Archetypes,
            filtered.Selection.ArchetypeId,
            selected =>
            {
                _filterSelection = _filterSelection with { ArchetypeId = selected };
                RenderCatalog(catalog);
            }));

        if (filtered.Stories.Count == 0)
        {
            VerticalStackLayout empty = new() { Spacing = 8 };
            empty.Add(NativeTheme.Title(_copy["Stories.FilterEmptyTitle"], 21));
            empty.Add(NativeTheme.Body(_copy["Stories.FilterEmptyDetail"], NativeTheme.Muted));
            Border card = NativeTheme.Card(empty);
            card.AutomationId = "archive-filter-empty";
            SemanticProperties.SetDescription(card, _copy["Stories.FilterEmptySemantic"]);
            _body.Add(card);
            return;
        }

        foreach (ShadowArchivePublicStoryCardViewModel story in filtered.Stories)
        {
            _body.Add(BuildStoryCard(catalog, story));
        }
    }

    private static View BuildFilterPicker(
        string title,
        string allLabel,
        string automationId,
        string accessibilityDescription,
        IReadOnlyList<ShadowArchiveCatalogFilterOption> options,
        string? selectedId,
        Action<string?> onSelected)
    {
        string[] labels = new[] { allLabel }
            .Concat(options.Select(static option => option.DisplayName))
            .ToArray();
        int selectedIndex = selectedId is null
            ? 0
            : 1 + options.ToList().FindIndex(option => string.Equals(
                option.Id,
                selectedId,
                StringComparison.Ordinal));
        Picker picker = new()
        {
            Title = title,
            AutomationId = automationId,
            ItemsSource = labels,
            SelectedIndex = selectedIndex,
            TextColor = NativeTheme.Ink,
            TitleColor = NativeTheme.Muted
        };
        SemanticProperties.SetDescription(picker, accessibilityDescription);
        picker.SelectedIndexChanged += (_, _) =>
        {
            int index = picker.SelectedIndex;
            onSelected(index <= 0 ? null : options[index - 1].Id);
        };
        return NativeTheme.Card(picker);
    }

    private Border BuildStoryCard(
        ShadowArchivePublicCatalogViewModel catalog,
        ShadowArchivePublicStoryCardViewModel story)
    {
        VerticalStackLayout content = new() { Spacing = 8 };
        content.Add(NativeTheme.Eyebrow(_copy["Stories.Runner"]));
        content.Add(NativeTheme.Title(story.Identity.RunnerHeading, 23));
        if (!string.IsNullOrWhiteSpace(story.Identity.RunnerHandle))
        {
            content.Add(NativeTheme.Body(story.Identity.RunnerHandle!, NativeTheme.Muted));
        }
        content.Add(NativeTheme.Title(story.Title, 20));
        content.Add(NativeTheme.Body(story.Summary));
        string archetypes = string.Join(", ", story.Metadata.Archetypes.Select(static value => value.DisplayName));
        Label metadata = NativeTheme.Body(
            $"{story.Metadata.PublicationLanguage.DisplayName} · {archetypes}",
            NativeTheme.Muted);
        metadata.AutomationId = $"archive-story-metadata-{story.Binding.PublicationId}";
        SemanticProperties.SetDescription(
            metadata,
            _copy.Format("Stories.MetadataSemantic", story.Metadata.PublicationLanguage.DisplayName, archetypes));
        content.Add(metadata);
        content.Add(NativeTheme.Body(_copy.Format("Stories.Owner", story.Identity.StoryOwnerLabel), NativeTheme.Muted));
        Label signals = NativeTheme.Body(_copy.Format("Stories.Signals", story.SignalCount.ToString("N0", _copy.DisplayCulture)), NativeTheme.Muted);
        signals.AutomationId = $"archive-signal-count-{story.Binding.PublicationId}";
        signals.FontAttributes = FontAttributes.Bold;
        content.Add(signals);

        Button read = NativeTheme.PrimaryButton(_copy["Stories.Read"]);
        read.AutomationId = $"archive-read-{story.Binding.PublicationId}";
        read.Clicked += async (_, _) => await Navigation.PushAsync(
            new ShadowArchiveReaderPage(_presenter, _system, story, catalog.Viewer, _copy));
        content.Add(read);

        Border card = NativeTheme.Card(content);
        card.AutomationId = $"archive-story-{story.Binding.PublicationId}";
        return card;
    }

    private void RenderFailure(
        ShadowArchivePresentationState state,
        ShadowArchiveErrorViewModel? error)
    {
        ShadowArchivePhoneStateCopy copy = ShadowArchivePageLocalization.StateCopy(
            _copy,
            state,
            ShadowArchivePhoneSurface.Catalog,
            error);
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(_copy["Stories.Title"]));
        _body.Add(NativeTheme.Title(copy.Title));
        _body.Add(NativeTheme.Body(copy.Detail, NativeTheme.Muted));
        if (copy.CanRetry)
        {
            Button retry = NativeTheme.SecondaryButton(_copy["Common.Retry"]);
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
                "Stories is unavailable. No public response was assumed.",
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
    private readonly AndroidSurfaceCopy _copy;
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
        ShadowArchiveViewerContext viewer,
        AndroidSurfaceCopy copy)
    {
        _presenter = presenter;
        _system = system;
        _story = story;
        _viewer = viewer;
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
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
        _body.Add(NativeTheme.Eyebrow(_copy["Stories.Title"]));
        _body.Add(NativeTheme.Title(_copy["Stories.LoadingStory"]));
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
        _body.Add(NativeTheme.Eyebrow(_copy["Stories.Runner"]));
        _body.Add(NativeTheme.Title(reader.Identity.RunnerHeading));
        if (!string.IsNullOrWhiteSpace(reader.Identity.RunnerHandle))
        {
            _body.Add(NativeTheme.Body(reader.Identity.RunnerHandle!, NativeTheme.Muted));
        }
        _body.Add(NativeTheme.Title(reader.Title, 22));
        _body.Add(NativeTheme.Body(reader.Summary, NativeTheme.Muted));
        _body.Add(NativeTheme.Body(_copy.Format("Stories.Owner", reader.Identity.StoryOwnerLabel), NativeTheme.Muted));
        int signalCount = _community?.IsReady == true && _community.Value is not null
            ? _community.Value.Signal.VoteCount
            : _story.SignalCount;
        Label signals = NativeTheme.Body(_copy.Format("Stories.Signals", signalCount.ToString("N0", _copy.DisplayCulture)), NativeTheme.Muted);
        signals.AutomationId = "archive-reader-signal-count";
        _body.Add(signals);

        VerticalStackLayout chapterCard = new() { Spacing = 12 };
        chapterCard.Add(NativeTheme.Eyebrow(_copy.Format("Stories.Chapter", chapter.Sequence, reader.Chapters.Count)));
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
        Button previous = NativeTheme.SecondaryButton(_copy["Common.Previous"]);
        previous.AutomationId = "archive-reader-previous";
        previous.IsEnabled = _chapterIndex > 0;
        previous.Clicked += (_, _) =>
        {
            _chapterIndex--;
            RenderReader();
        };
        navigation.Add(previous);
        Button next = NativeTheme.PrimaryButton(isAtFinalChapter
            ? _copy["Stories.LastChapter"]
            : _copy["Stories.NextChapter"]);
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
        downloads.Add(NativeTheme.Eyebrow(_copy["Stories.PublicDownloads"]));
        if (reader.Downloads.Count == 0)
        {
            downloads.Add(NativeTheme.Body(_copy["Stories.NoDownloads"], NativeTheme.Muted));
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
                        await DisplayAlertAsync(
                            _copy["Stories.DownloadUnavailable"],
                            _copy["Stories.DownloadOpenFailed"],
                            _copy["Common.Ok"]);
                    }
                };
                downloads.Add(open);
            }
        }
        downloads.Add(NativeTheme.Body(_copy["Stories.NoAccount"], NativeTheme.Muted));
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
                _body.Add(NativeTheme.Body(_copy["Stories.LoadingSignal"], NativeTheme.Muted));
                return;
            }

            ShadowArchivePhoneStateCopy failure = ShadowArchivePageLocalization.StateCopy(
                _copy,
                _community.State,
                ShadowArchivePhoneSurface.Signal,
                _community.Error);
            VerticalStackLayout unavailable = new() { Spacing = 7 };
            unavailable.Add(NativeTheme.Eyebrow(_copy["Stories.Signal"]));
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
        section.Add(NativeTheme.Eyebrow(_copy["Stories.Signal"]));
        section.Add(NativeTheme.Title(_copy["Stories.End"], 21));
        string? signalDetail = ShadowArchivePageLocalization.SignalDetail(_copy, signal);
        if (!string.IsNullOrWhiteSpace(signalDetail))
        {
            section.Add(NativeTheme.Body(signalDetail, NativeTheme.Muted));
        }

        if (signal.Kind is ShadowArchivePhoneSignalKind.Vote
            or ShadowArchivePhoneSignalKind.Retract
            or ShadowArchivePhoneSignalKind.SignInRequired)
        {
            Button action = NativeTheme.PrimaryButton(ShadowArchivePageLocalization.SignalLabel(_copy, signal.Kind));
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
                        _copy["Stories.SignIn"],
                        _copy["Stories.SignInDetail"],
                        _copy["Common.Ok"]);
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
        ShadowArchivePhoneStateCopy copy = ShadowArchivePageLocalization.StateCopy(
            _copy,
            state,
            ShadowArchivePhoneSurface.Reader,
            error);
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(_copy["Stories.Title"]));
        _body.Add(NativeTheme.Title(copy.Title));
        _body.Add(NativeTheme.Body(copy.Detail, NativeTheme.Muted));
        if (copy.CanRetry)
        {
            Button retry = NativeTheme.SecondaryButton(_copy["Stories.RetryStory"]);
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
                "Stories is unavailable. No public response or Signal change was assumed.",
                null,
                null,
                null,
                null,
                null));
}

internal static class ShadowArchivePageLocalization
{
    public static ShadowArchivePhoneStateCopy StateCopy(
        AndroidSurfaceCopy copy,
        ShadowArchivePresentationState state,
        ShadowArchivePhoneSurface surface,
        ShadowArchiveErrorViewModel? error)
    {
        ArgumentNullException.ThrowIfNull(copy);
        bool canRetry = ShadowArchivePhonePolicy.StateCopy(state, surface, error).CanRetry;
        string? englishDetail = copy.ResourceLanguage == "en" && !string.IsNullOrWhiteSpace(error?.Message)
            ? error.Message
            : null;

        if (state == ShadowArchivePresentationState.AuthenticationRequired
            && surface == ShadowArchivePhoneSurface.Signal)
        {
            return new(
                copy["Stories.SignIn"],
                copy["Stories.State.SignInDetail"],
                canRetry);
        }

        return state switch
        {
            ShadowArchivePresentationState.Offline => new(
                copy["Stories.State.OfflineTitle"],
                englishDetail ?? copy["Stories.State.OfflineDetail"],
                canRetry),
            ShadowArchivePresentationState.Stale or ShadowArchivePresentationState.RevisionConflict => new(
                copy["Stories.State.ChangedTitle"],
                englishDetail ?? copy["Stories.State.ChangedDetail"],
                canRetry),
            ShadowArchivePresentationState.ModerationHeld => new(
                copy["Stories.State.ModerationTitle"],
                englishDetail ?? copy["Stories.State.ModerationDetail"],
                canRetry),
            ShadowArchivePresentationState.Removed or ShadowArchivePresentationState.NotFound => new(
                copy["Stories.State.UnavailableTitle"],
                englishDetail ?? copy["Stories.State.UnavailableDetail"],
                canRetry),
            ShadowArchivePresentationState.RateLimited => new(
                copy["Stories.State.BusyTitle"],
                RetryDetail(copy, englishDetail, error?.RetryAfter),
                canRetry),
            ShadowArchivePresentationState.AuthenticationRequired => new(
                copy["Stories.State.PublicAccessTitle"],
                copy["Stories.State.LoginUnexpected"],
                canRetry),
            ShadowArchivePresentationState.Forbidden => new(
                copy[surface == ShadowArchivePhoneSurface.Signal
                    ? "Stories.State.SignalNotAllowed"
                    : "Stories.State.PublicAccessTitle"],
                englishDetail ?? copy["Stories.State.ActionNotAllowed"],
                canRetry),
            _ => new(
                copy[surface == ShadowArchivePhoneSurface.Signal
                    ? "Stories.State.SignalUnavailable"
                    : "Stories.State.StoriesUnavailable"],
                englishDetail ?? DefaultUnavailableDetail(copy, surface),
                canRetry)
        };
    }

    public static string SignalLabel(AndroidSurfaceCopy copy, ShadowArchivePhoneSignalKind kind) => kind switch
    {
        ShadowArchivePhoneSignalKind.SignInRequired => copy["Stories.SignIn"],
        ShadowArchivePhoneSignalKind.Retract => copy["Stories.Retract"],
        ShadowArchivePhoneSignalKind.Vote => copy["Stories.SignalStory"],
        _ => throw new InvalidOperationException("A non-actionable Signal state cannot render an action label.")
    };

    public static string? SignalDetail(
        AndroidSurfaceCopy copy,
        ShadowArchivePhoneSignalProjection signal) => signal.Kind switch
    {
        ShadowArchivePhoneSignalKind.Hidden => null,
        ShadowArchivePhoneSignalKind.SignInRequired => copy["Stories.AccountToVote"],
        ShadowArchivePhoneSignalKind.OwnerBlocked => copy["Stories.OwnerCannotSignal"],
        ShadowArchivePhoneSignalKind.Retract => copy["Stories.RetractDetail"],
        ShadowArchivePhoneSignalKind.Vote => copy["Stories.SignalStoryDetail"],
        ShadowArchivePhoneSignalKind.Unavailable when copy.ResourceLanguage == "en"
            && !string.IsNullOrWhiteSpace(signal.Detail) => signal.Detail,
        ShadowArchivePhoneSignalKind.Unavailable => copy["Stories.SignalUnavailable"],
        _ => copy["Stories.SignalUnavailable"]
    };

    private static string DefaultUnavailableDetail(
        AndroidSurfaceCopy copy,
        ShadowArchivePhoneSurface surface) => surface switch
    {
        ShadowArchivePhoneSurface.Catalog => copy["Stories.UnavailableCatalog"],
        ShadowArchivePhoneSurface.Reader => copy["Stories.UnavailableReader"],
        _ => copy["Stories.State.NoResponse"]
    };

    private static string RetryDetail(
        AndroidSurfaceCopy copy,
        string? englishDetail,
        TimeSpan? retryAfter)
    {
        string detail = englishDetail ?? copy["Stories.State.BusyDetail"];
        return retryAfter is { } delay && delay > TimeSpan.Zero
            ? copy.Format("Stories.State.RetryAfter", detail, Math.Ceiling(delay.TotalSeconds))
            : detail;
    }
}
