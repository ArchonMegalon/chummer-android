using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// SR5 Standard priority-table phone chooser. The page renders Core/Presentation projections and
/// delegates every proposed OptionId set back to Core before retaining it.
/// </summary>
public sealed class CreationQualitiesPage : NativePageBase
{
    private const int CatalogPageSize = 20;
    private int _catalogOffset;
    private string _filter = string.Empty;
    private CreationQualitiesPhoneDraft _draft = new();
    private readonly CharacterCreationQualitiesCheckpointStore _store;
    private readonly VerticalStackLayout _technicalDetails = new() { Spacing = 8 };
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _localBlockers = [];
    private CharacterOverviewState? _loadedDisplay;
    private CharacterCreationFoundationResult<CharacterCreationQualitiesState>? _loaded;
    private CharacterCreationQualitiesEditorState? _editor;
    private IReadOnlyList<CharacterCreationQualitiesDesktopOption> _availableOptions = [];
    private bool _canReview;
    private string? _reviewCheckpointDigest;
    private bool _loading = true;

    public CreationQualitiesPage(RunnerSessionCoordinator coordinator)
        : this(coordinator, CharacterCreationQualitiesCheckpointStore.CreateDefault(
            coordinator.State.DisplayOwnerContext, coordinator.IsCreationQualitiesOwnerCurrent))
    {
    }

    internal CreationQualitiesPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationQualitiesCheckpointStore store) : base(coordinator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Title = CreationFlowStrings.Get("Qualities.PageTitle", "Qualities");
        AutomationId = "creation-qualities-page";
        Content = new ScrollView { Content = _body };
        Refresh();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        _loaded = null;
        _loadedDisplay = null;
        _editor = null;
        _availableOptions = [];
        _canReview = false;
        _reviewCheckpointDigest = null;
        _loading = true;
        Refresh();
        CharacterOverviewState original = Coordinator.State;
        CreationQualitiesPhoneDraft draft = _draft.Copy();
        try
        {
            var loaded = await Coordinator.LoadCreationQualitiesForDisplayAsync(original, cancellationToken);
            // Digest checks and Presentation projection are substantial too
            // (especially Android's crypto interop). Keep them off the UI,
            // not merely the initial Core store read.
            var prepared = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (loaded.Value is not { } state || !CreationQualitiesPhoneAuthority.IsReady(state, original))
                    return (Editor: (CharacterCreationQualitiesEditorState?)null, CanReview: false, CheckpointDigest: (string?)null, Options: (IReadOnlyList<CharacterCreationQualitiesDesktopOption>)[]);
                draft.Bind(state, original);
                if (!draft.Matches(state, original))
                    return (Editor: (CharacterCreationQualitiesEditorState?)null, CanReview: false, CheckpointDigest: (string?)null, Options: (IReadOnlyList<CharacterCreationQualitiesDesktopOption>)[]);
                var editor = CreationQualitiesPhoneAuthority.ProjectEditor(state, original);
                bool canReview = CreationQualitiesPhoneAuthority.CanConfirmPreview(
                    state, original, draft.Preview ?? state.Preview, draft.SelectedOptionIds);
                string? checkpointDigest = _store.TryRead(out var checkpoint, out _)
                    && checkpoint.OwnsExactReview(state, original) ? checkpoint.CheckpointDigest : null;
                cancellationToken.ThrowIfCancellationRequested();
                var available = draft.AvailableOptions(state, original, editor, cancellationToken);
                return (Editor: editor, CanReview: canReview, CheckpointDigest: checkpointDigest, Options: available);
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _loadedDisplay = original;
            _loaded = loaded;
            _editor = prepared.Editor;
            _availableOptions = prepared.Options;
            _canReview = prepared.CanReview;
            _reviewCheckpointDigest = prepared.CheckpointDigest;
            _draft = draft;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                _loading = false;
        }
    }

    protected override void Refresh()
    {
        _body.Clear();
        _technicalDetails.Clear();
        _technicalDetails.IsVisible = false;
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.Step1", "SR5 · 1 of 4")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Qualities.Choose", "Choose qualities")));
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Get(
                "Qualities.Intro",
                "Only available qualities within your budget are shown. Tap ! to learn about their effects. Selected qualities remain available to remove."),
            NativeTheme.Muted));

        if (_loading)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-qualities-loading" });
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get(
                "Qualities.Loading", "Loading qualities…"), NativeTheme.Muted));
            return;
        }
        if (_loaded is not { } load || _loadedDisplay is not { } original
            || !Coordinator.IsCreationCatalogDisplayCurrent(original))
        {
            AddBlockers([CharacterCreationQualitiesBlockers.RevisionConflict]);
            return;
        }

        // Search, paging and coordinator renders consume only this appearance's
        // accepted snapshot. Preview/Confirm still revalidate through Core.
        if (load.Value is not { } state || _editor is not { } editor
            || !CreationQualitiesPhoneAuthority.MatchesOverview(state, Coordinator.State))
        {
            AddBlockers(load.Blockers.Count == 0
                ? [CharacterCreationQualitiesBlockers.AuthorityUnavailable]
                : load.Blockers);
            return;
        }
        AddBinding(state);
        AddBudgets(_draft.Preview ?? state.Preview);
        CharacterCreationQualitiesCheckpoint? checkpoint = AddRecovery(state);
        bool checkpointOwnsLane = checkpoint is not null || HasMalformedCheckpoint();
        AddReview(state, checkpointOwnsLane);
        AddBlockers(_localBlockers);
        AddTechnicalDetailsDisclosure();
        AddGranted(state);
        AddOptions(state, editor, checkpointOwnsLane);
    }

    private void AddBinding(CharacterCreationQualitiesState state)
    {
        Label binding = NativeTheme.Body(
            CreationFlowStrings.Format(
                "Qualities.Binding",
                "Revision {0} · prerequisite {1} · attributes {2}",
                state.Binding.ContentRevision.ToString(CultureInfo.InvariantCulture),
                state.Binding.PrerequisiteDraftRevision.ToString(CultureInfo.InvariantCulture),
                state.Binding.AttributesDraftRevision.ToString(CultureInfo.InvariantCulture)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-qualities-binding";
        _technicalDetails.Add(binding);
        AddDigest("creation-qualities-authority-digest", state.Binding.AuthorityDigest);
        AddDigest("creation-qualities-runtime-digest", state.Binding.RuntimeDigest);
    }

    private void AddBudgets(CharacterCreationQualitiesPreview preview)
    {
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.CoreLedgers", "Quality budgets")));
        FlexLayout ribbon = new()
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row
        };
        ribbon.Add(BudgetCard(
            CreationFlowStrings.Get("Qualities.Positive", "Positive qualities"),
            preview.PositiveQualityBudget,
            "creation-qualities-budget-positive"));
        ribbon.Add(BudgetCard(
            CreationFlowStrings.Get("Qualities.Negative", "Negative qualities"),
            preview.NegativeQualityBudget,
            "creation-qualities-budget-negative"));
        VerticalStackLayout karma = new() { Spacing = 5, MinimumWidthRequest = 155 };
        karma.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.CreationKarma", "Creation Karma")));
        karma.Add(NativeTheme.Title(
            CreationFlowStrings.Format(
                "Qualities.KarmaRemainingInline",
                "{0} remaining",
                Signed(preview.KarmaRemaining)),
            20));
        Border karmaCard = NativeTheme.Card(karma, new Thickness(12));
        karmaCard.Margin = new Thickness(0, 0, 8, 8);
        karmaCard.AutomationId = "creation-qualities-budget-karma";
        ribbon.Add(karmaCard);
        _body.Add(ribbon);
        if (preview.MetagenicPositiveKarma != 0 || preview.MetagenicNegativeKarma != 0)
        {
            Label metagenic = NativeTheme.Body(
                CreationFlowStrings.Format(
                    "Qualities.Metagenic",
                    "Metagenic: +{0} / -{1}",
                    preview.MetagenicPositiveKarma.ToString(CultureInfo.InvariantCulture),
                    preview.MetagenicNegativeKarma.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Muted);
            metagenic.AutomationId = "creation-qualities-budget-metagenic";
            _body.Add(metagenic);
        }
    }

    private static Border BudgetCard(
        string label,
        CharacterCreationQualitiesBudget budget,
        string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 5, MinimumWidthRequest = 155 };
        card.Add(NativeTheme.Eyebrow(label));
        card.Add(NativeTheme.Title(
            CreationFlowStrings.Format(
                "Qualities.Left",
                "{0} left",
                budget.Remaining.ToString(CultureInfo.InvariantCulture)),
            20));
        card.Add(NativeTheme.Body(
            CreationFlowStrings.Format(
                "Qualities.BudgetKarma",
                "{0} / {1} Karma",
                budget.Used.ToString(CultureInfo.InvariantCulture),
                budget.Total.ToString(CultureInfo.InvariantCulture)),
            budget.Blockers.Count == 0 ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card, new Thickness(12));
        border.Margin = new Thickness(0, 0, 8, 8);
        border.AutomationId = automationId;
        return border;
    }

    private CharacterCreationQualitiesCheckpoint? AddRecovery(
        CharacterCreationQualitiesState state)
    {
        if (!_store.TryRead(
                out CharacterCreationQualitiesCheckpoint checkpoint,
                out string blocker))
        {
            if (!string.IsNullOrWhiteSpace(blocker))
            {
                Label malformed = NativeTheme.Body(blocker, NativeTheme.Danger);
                malformed.AutomationId = "creation-qualities-checkpoint-blocker";
                _body.Add(NativeTheme.Card(malformed));
            }
            return null;
        }

        VerticalStackLayout recovery = new() { Spacing = 8 };
        recovery.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.DurableRecovery", "Durable review recovery")));
        recovery.Add(NativeTheme.Body(
            checkpoint.Phase switch
            {
                CharacterCreationQualitiesCheckpointPhase.Reviewed =>
                    CreationFlowStrings.Get(
                        "Qualities.Recovery.Reviewed",
                        "A reviewed quality draft can be resumed without reconstructing rules data."),
                CharacterCreationQualitiesCheckpointPhase.Applying =>
                    CreationFlowStrings.Get(
                        "Common.Recovery.Applying",
                        "An interrupted atomic commit is locked. Resolve its exact idempotent command before continuing."),
                CharacterCreationQualitiesCheckpointPhase.Applied =>
                    CreationFlowStrings.Get(
                        "Qualities.Recovery.Applied",
                        "A verified quality-draft receipt is waiting for acknowledgement."),
                _ => CreationFlowStrings.Get("Qualities.Recovery.Locked", "The quality lane is locked.")
            },
            NativeTheme.Muted));

        if (checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Reviewed
            && _reviewCheckpointDigest is not null
            && checkpoint.CheckpointDigest == _reviewCheckpointDigest)
        {
            Button resume = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Qualities.Recovery.Resume",
                "Resume reviewed qualities"));
            resume.AutomationId = "creation-qualities-resume-reviewed";
            resume.Clicked += async (_, _) => await RunAsync(() => ResumeReviewAsync(state, checkpoint));
            recovery.Add(resume);
            Button abandon = NativeTheme.SecondaryButton(CreationFlowStrings.Get(
                "Common.AbandonReviewedDraft",
                "Abandon reviewed draft"));
            abandon.AutomationId = "creation-qualities-abandon-reviewed";
            abandon.Clicked += async (_, _) => await RunAsync(() => AbandonReviewedAsync(checkpoint));
            recovery.Add(abandon);
        }
        else if (checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Applying
                 && checkpoint.OwnsRecoveryRevision(Coordinator.State))
        {
            Button resolve = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Common.ResolveInterruptedCommit",
                "Resolve interrupted commit"));
            resolve.AutomationId = "creation-qualities-resolve-applying";
            resolve.Clicked += async (_, _) => await RunAsync(() => ResolveApplyingAsync(checkpoint));
            recovery.Add(resolve);
        }
        else if (checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Applied
                 && checkpoint.OwnsRecoveryRevision(Coordinator.State)
                 && checkpoint.Receipt is { } receipt)
        {
            Button receiptButton = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Common.OpenSavedReceipt",
                "Open saved receipt"));
            receiptButton.AutomationId = "creation-qualities-open-receipt";
            receiptButton.Clicked += async (_, _) => await Navigation.PushAsync(
                new CreationQualitiesReceiptPage(Coordinator, checkpoint, receipt, _store));
            recovery.Add(receiptButton);
        }
        else
        {
            Label stale = NativeTheme.Body(
                CreationFlowStrings.Get(
                    "Qualities.Recovery.Stale",
                    "The checkpoint belongs to another revision or its authority changed. The lane remains fail-closed; reopen the exact runner or use support recovery."),
                NativeTheme.Danger);
            stale.AutomationId = "creation-qualities-stale-checkpoint";
            recovery.Add(stale);
        }

        Border card = NativeTheme.Card(recovery);
        card.AutomationId = "creation-qualities-recovery-card";
        _body.Add(card);
        return checkpoint;
    }

    private bool HasMalformedCheckpoint()
        => !_store.TryRead(out _, out string blocker) && !string.IsNullOrWhiteSpace(blocker);

    private void AddGranted(CharacterCreationQualitiesState state)
    {
        if (state.Authority.GrantedQualities.Count == 0)
            return;
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get(
            "Qualities.Granted",
            "Granted by earlier choices")));
        foreach (CharacterCreationGrantedQuality grant in state.Authority.GrantedQualities)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(grant.Name, 18));
            card.Add(NativeTheme.Body(
                CreationFlowStrings.Format(
                    "Qualities.GrantedDetail",
                    "{0} · rating {1} · Karma {2}",
                    grant.Origin,
                    grant.Rating.ToString(CultureInfo.InvariantCulture),
                    Signed(grant.KarmaCost)),
                NativeTheme.Muted));
            _technicalDetails.Add(NativeTheme.Body(
                string.Join(" · ", grant.SourceAnchorIds),
                NativeTheme.Muted));
            Grid content = new() { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 16 };
            content.Add(card, 0);
            Border border = NativeTheme.Card(content);
            border.AutomationId = $"creation-quality-granted-{Token(grant.GrantId)}";
            Button info = NativeTheme.SecondaryButton("!");
            info.AutomationId = $"creation-quality-granted-info-{Token(grant.GrantId)}";
            info.WidthRequest = 50;
            info.FontSize = 20;
            info.Padding = 0;
            info.VerticalOptions = LayoutOptions.Center;
            SemanticProperties.SetDescription(info, CreationFlowStrings.Format(
                "Qualities.Info.Accessible", "Explain {0}", grant.Name));
            info.Clicked += async (_, _) =>
            {
                if (!ReferenceEquals(border.Parent, _body) || _loadedDisplay is not { } original
                    || !Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
                await Navigation.PushAsync(new CreationQualityInfoPage(Coordinator, original, grant,
                    CreationQualityInfo.SourceForGrant(grant, state.Authority.Options)));
            };
            content.Add(info, 1);
            _body.Add(border);
        }
    }

    private void AddOptions(
        CharacterCreationQualitiesState state,
        CharacterCreationQualitiesEditorState editor,
        bool checkpointOwnsLane)
    {
        SearchBar search = new()
        {
            AutomationId = "creation-qualities-search",
            Placeholder = CreationFlowStrings.Get("Qualities.Search", "Search qualities"),
            Text = _filter,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            PlaceholderColor = NativeTheme.Muted
        };
        search.SearchButtonPressed += (_, _) => ApplyFilter(search.Text);
        search.TextChanged += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.NewTextValue) && !string.IsNullOrWhiteSpace(_filter))
                ApplyFilter(string.Empty);
        };
        _body.Add(search);

        CharacterCreationQualitiesDesktopOption[] matches = _availableOptions
            .Where(option => string.IsNullOrWhiteSpace(_filter)
                             || option.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
                             || (option.FollowUpChoiceLabel?.Contains(_filter, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .OrderBy(option => option.Type)
            .ThenBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(option => option.OptionId, StringComparer.Ordinal)
            .ToArray();
        _catalogOffset = Math.Min(_catalogOffset, Math.Max(0, matches.Length - 1) / CatalogPageSize * CatalogPageSize);
        int end = Math.Min(matches.Length, _catalogOffset + CatalogPageSize);
        Label range = NativeTheme.Body(matches.Length == 0
            ? CreationFlowStrings.Get("Qualities.NoMatches", "No matching qualities")
            : CreationFlowStrings.Format("Qualities.Showing", "Showing {0}–{1} of {2}", _catalogOffset + 1, end, matches.Length),
            NativeTheme.Muted);
        range.AutomationId = "creation-qualities-catalog-range";
        _body.Add(range);

        // Keep navigation ahead of the bounded rows; neither review nor paging should
        // require scrolling through the entire Core catalog. Paging never edits the draft.
        HorizontalStackLayout pager = new() { Spacing = 10 };
        Button previous = NativeTheme.SecondaryButton(CreationFlowStrings.Get("Qualities.Previous", "Previous"));
        previous.AutomationId = "creation-qualities-catalog-previous";
        previous.IsEnabled = _catalogOffset > 0;
        previous.Clicked += (_, _) =>
        {
            _catalogOffset = Math.Max(0, _catalogOffset - CatalogPageSize);
            Refresh();
        };
        pager.Add(previous);
        Button next = NativeTheme.SecondaryButton(CreationFlowStrings.Get("Qualities.Next", "Next"));
        next.AutomationId = "creation-qualities-catalog-next";
        next.IsEnabled = end < matches.Length;
        next.Clicked += (_, _) =>
        {
            _catalogOffset += CatalogPageSize;
            Refresh();
        };
        pager.Add(next);
        _body.Add(pager);

        CharacterCreationQualitiesDesktopOption[] visible = matches.Skip(_catalogOffset).Take(CatalogPageSize).ToArray();
        foreach (CharacterCreationQualityType type in Enum.GetValues<CharacterCreationQualityType>())
        {
            if (!visible.Any(option => option.Type == type))
                continue;
            _body.Add(NativeTheme.Eyebrow(type == CharacterCreationQualityType.Positive
                ? CreationFlowStrings.Get("Qualities.Positive", "Positive qualities")
                : CreationFlowStrings.Get("Qualities.Negative", "Negative qualities")));
            foreach (CharacterCreationQualitiesDesktopOption option in visible
                         .Where(candidate => candidate.Type == type))
            {
                bool selected = _draft.IsSelected(option.OptionId);
                bool exact = CreationQualitiesPhoneAuthority.IsOptionConfigurable(option);
                string followUp = string.IsNullOrWhiteSpace(option.FollowUpChoiceLabel)
                    ? string.Empty
                    : CreationFlowStrings.Format(
                        "Common.DotValue",
                        " · {0}",
                        option.FollowUpChoiceLabel);
                string detail = CreationFlowStrings.Format(
                    "Qualities.OptionDetail",
                    "{0}rating {1} · Karma {2}{3}",
                    selected ? CreationFlowStrings.Get("Common.SelectedPrefix", "Selected · ") : string.Empty,
                    option.Rating.ToString(CultureInfo.InvariantCulture),
                    Signed(option.KarmaCost),
                    followUp);
                if (!exact)
                    detail += $" · {UnavailableReason(option.DisableReasonKey)}";
                Border row = NativeTheme.NavigationRow(
                    option.Name,
                    detail,
                    () => Navigation.PushAsync(new CreationQualityConfigurePage(
                        Coordinator,
                        state,
                        editor,
                        option,
                        _draft,
                        original: _loadedDisplay!)),
                    enabled: !checkpointOwnsLane,
                    automationId: $"creation-quality-option-{Token(option.OptionId)}");
                var rowGrid = (Grid)row.Content!;
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                Button info = NativeTheme.SecondaryButton("!");
                info.AutomationId = $"creation-quality-info-{Token(option.OptionId)}";
                info.WidthRequest = 50;
                info.FontSize = 20;
                info.Padding = 0;
                info.VerticalOptions = LayoutOptions.Center;
                SemanticProperties.SetDescription(info, CreationFlowStrings.Format(
                    "Qualities.Info.Accessible", "Explain {0}", option.Name));
                info.Clicked += async (_, _) =>
                {
                    if (!ReferenceEquals(row.Parent, _body) || _loadedDisplay is not { } original
                        || !Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
                    var source = state.Authority.Options.Single(item => item.OptionId == option.OptionId);
                    await Navigation.PushAsync(new CreationQualityInfoPage(Coordinator, original, source));
                };
                rowGrid.Add(info, 2);
                _body.Add(row);
            }
        }
    }

    private void ApplyFilter(string? value)
    {
        _filter = value?.Trim() ?? string.Empty;
        _catalogOffset = 0;
        Refresh();
    }

    private void AddReview(
        CharacterCreationQualitiesState state,
        bool checkpointOwnsLane)
    {
        Button review = NativeTheme.PrimaryButton(
            CreationFlowStrings.Format(
                "Qualities.ReviewSelected",
                "Review {0} selected qualities",
                _draft.SelectedOptionIds.Count.ToString(CultureInfo.InvariantCulture)));
        review.AutomationId = "creation-qualities-open-review";
        review.IsEnabled = !checkpointOwnsLane && _canReview;
        review.Clicked += async (_, _) => await RunAsync(() => OpenReviewAsync(state));
        _body.Add(review);
        Label finalization = NativeTheme.Body(
            CreationFlowStrings.Get(
                "Qualities.FinalizationBoundary",
                "Confirm your choices to save them. Their effects are applied when you finish creating your runner."),
            NativeTheme.Muted);
        finalization.AutomationId = "creation-qualities-finalization-boundary";
        _body.Add(finalization);
    }

    private async Task OpenReviewAsync(CharacterCreationQualitiesState state)
    {
        if (_loadedDisplay is not { } original || !Coordinator.IsCreationCatalogDisplayCurrent(original))
            return;
        CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> result =
            await Task.Run(() => Coordinator.ReadCreationAuthority(original,
                () => Coordinator.PreviewCreationQualities(state.Binding, _draft.SelectedOptionIds, original), CancellationToken.None));
        if (!Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
        if (!_draft.TryAdopt(state, Coordinator.State, result, _draft.SelectedOptionIds)
            || result.Value is not { } preview
            || !CreationQualitiesPhoneAuthority.CanConfirmPreview(
                state,
                Coordinator.State,
                preview,
                _draft.SelectedOptionIds))
        {
            _localBlockers = result.Blockers;
            _canReview = false;
            Refresh();
            return;
        }

        CharacterCreationQualitiesCheckpoint candidate =
            CharacterCreationQualitiesCheckpoint.CreateReviewed(
                preview,
                _draft.SelectedOptionIds,
                Guid.NewGuid());
        if (!_store.TryCreate(candidate, out CharacterCreationQualitiesCheckpoint stored, out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReviewNotCheckpointed", "Review not checkpointed"),
                blocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
            Refresh();
            return;
        }
        await Navigation.PushAsync(new CreationQualitiesReviewPage(Coordinator, stored, _store, original));
    }

    private async Task ResumeReviewAsync(
        CharacterCreationQualitiesState state,
        CharacterCreationQualitiesCheckpoint checkpoint)
    {
        if (_loadedDisplay is not { } original || !Coordinator.IsCreationCatalogDisplayCurrent(original))
            return;
        CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> result =
            await Task.Run(() => Coordinator.ReadCreationAuthority(original, () => Coordinator.PreviewCreationQualities(
                checkpoint.Preview.Binding,
                checkpoint.SelectedOptionIds, original), CancellationToken.None));
        if (!Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
        if (!checkpoint.OwnsExactReview(state, Coordinator.State)
            || !CreationQualitiesPhoneAuthority.CanDisplayPreview(
                state,
                Coordinator.State,
                result,
                checkpoint.SelectedOptionIds)
            || result.Value is not { } refreshed
            || !CreationQualitiesPhoneAuthority.CanonicallyEquals(
                checkpoint.Preview,
                refreshed))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReviewCannotResume", "Review cannot resume"),
                CreationFlowStrings.Get(
                    "Qualities.ReviewChanged",
                    "The Core preview, source authority or workspace revision changed."),
                CreationFlowStrings.Get("Common.OK", "OK"));
            return;
        }
        await Navigation.PushAsync(new CreationQualitiesReviewPage(Coordinator, checkpoint, _store, original));
    }

    private async Task AbandonReviewedAsync(CharacterCreationQualitiesCheckpoint checkpoint)
    {
        bool confirmed = await DisplayAlertAsync(
            CreationFlowStrings.Get("Qualities.Abandon.Title", "Abandon reviewed qualities?"),
            CreationFlowStrings.Get(
                "Qualities.Abandon.Message",
                "This removes only the durable phone review. It does not change the Creation draft or character."),
            CreationFlowStrings.Get("Common.Abandon", "Abandon"),
            CreationFlowStrings.Get("Common.Keep", "Keep"));
        if (!confirmed)
            return;
        if (!_store.TryDeleteReviewed(
                CharacterCreationQualitiesCheckpointCas.From(checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.CheckpointNotRemoved", "Checkpoint not removed"),
                blocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
        }
        Refresh();
    }

    private async Task ResolveApplyingAsync(
        CharacterCreationQualitiesCheckpoint checkpoint)
    {
        if (_loadedDisplay is not { } original || !Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
        CreationQualitiesPhoneConfirmResult result =
            await Coordinator.ConfirmCreationQualitiesAsync(checkpoint, display: original);
        if (result.MutationOutcomeKnown
            && string.Equals(result.Outcome, CreationQualitiesPhoneOutcomes.Applied, StringComparison.Ordinal)
            && result.Receipt is { } receipt)
        {
            if (_store.TryRecordApplied(
                    CharacterCreationQualitiesCheckpointCas.From(checkpoint),
                    receipt,
                    out CharacterCreationQualitiesCheckpoint applied,
                    out string appliedBlocker))
            {
                await Navigation.PushAsync(new CreationQualitiesReceiptPage(
                    Coordinator,
                    applied,
                    receipt,
                    _store));
                return;
            }
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReceiptLocked", "Receipt remains locked"),
                appliedBlocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
            Refresh();
            return;
        }
        if (result.MutationOutcomeKnown
            && string.Equals(
                result.Outcome,
                CreationQualitiesPhoneOutcomes.RejectedBeforeMutation,
                StringComparison.Ordinal))
        {
            if (_store.TryReturnToReviewed(
                    CharacterCreationQualitiesCheckpointCas.From(checkpoint),
                    out _,
                    out string returnBlocker))
            {
                await DisplayAlertAsync(
                    CreationFlowStrings.Get("Common.CommitNotSaved", "Commit was not saved"),
                    result.Blockers.Count == 0
                        ? CreationFlowStrings.Get(
                            "Qualities.NoMutation",
                            "Fresh authority proved that no mutation occurred; the reviewed draft can be resumed.")
                        : string.Join("\n", result.Blockers),
                    CreationFlowStrings.Get("Common.OK", "OK"));
            }
            else
            {
                await DisplayAlertAsync(
                    CreationFlowStrings.Get("Common.RecoveryLocked", "Recovery remains locked"),
                    returnBlocker,
                    CreationFlowStrings.Get("Common.OK", "OK"));
            }
            Refresh();
            return;
        }
        await DisplayAlertAsync(
            CreationFlowStrings.Get("Common.CommitLocked", "Commit remains locked"),
            string.Join("\n", result.Blockers),
            CreationFlowStrings.Get("Common.OK", "OK"));
        Refresh();
    }

    private void AddBlockers(IEnumerable<string> blockers)
    {
        string[] normalized = blockers
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static blocker => blocker, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.CoreBlockers", "Core blockers")));
        foreach (string blocker in normalized)
            card.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
        _body.Add(NativeTheme.Card(card));
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _technicalDetails.Add(label);
    }

    private void AddTechnicalDetailsDisclosure()
    {
        Button toggle = NativeTheme.SecondaryButton(CreationFlowStrings.Get(
            "Qualities.ShowDetails", "Show technical details"));
        toggle.AutomationId = "creation-qualities-technical-details-toggle";
        toggle.Clicked += (_, _) =>
        {
            // A queued click from a replaced page must not toggle its successor.
            if (!ReferenceEquals(toggle.Parent, _body))
                return;
            _technicalDetails.IsVisible = !_technicalDetails.IsVisible;
            toggle.Text = _technicalDetails.IsVisible
                ? CreationFlowStrings.Get("Qualities.HideDetails", "Hide technical details")
                : CreationFlowStrings.Get("Qualities.ShowDetails", "Show technical details");
        };
        _body.Add(toggle);
        _body.Add(_technicalDetails);
    }

    internal static string UnavailableReason(string? reason)
        => reason == "creation-qualities-source-disabled"
            ? CreationFlowStrings.Get("Qualities.SourceDisabled", "The required source is not enabled for this runner.")
            : CreationFlowStrings.Get("Qualities.Unavailable", "This quality is currently unavailable for this runner.");

    internal static string Signed(int value)
        => value > 0
            ? $"+{value.ToString(CultureInfo.InvariantCulture)}"
            : value.ToString(CultureInfo.InvariantCulture);

    internal static string Token(string value)
        => new(value.Trim().ToLowerInvariant()
            .Select(static character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
}

/// <summary>Read-only source-backed help; never a selection or a rules calculation.</summary>
public sealed class CreationQualityInfoPage : NativePageBase
{
    private readonly CharacterOverviewState _original;
    private readonly string _name;
    private readonly string _detail;
    private readonly string? _followUp;
    private readonly string? _sourceXml;
    private readonly VerticalStackLayout _body = new() { Padding = 20, Spacing = 14 };

    internal CreationQualityInfoPage(RunnerSessionCoordinator coordinator, CharacterOverviewState original,
        CharacterCreationQualityCatalogOption option) : this(coordinator, original, option.Name,
            CreationFlowStrings.Format("Qualities.Info.Cost", "Rating {0} · Karma {1}",
                option.Rating, CreationQualitiesPage.Signed(option.KarmaCost)), option.FollowUpChoiceLabel, option.SourceNodeXml)
    {
    }

    internal CreationQualityInfoPage(RunnerSessionCoordinator coordinator, CharacterOverviewState original,
        CharacterCreationGrantedQuality grant, string? sourceXml) : this(coordinator, original, grant.Name,
            CreationFlowStrings.Format("Qualities.GrantedDetail", "{0} · rating {1} · Karma {2}",
                grant.Origin, grant.Rating, CreationQualitiesPage.Signed(grant.KarmaCost)), null, sourceXml)
    {
    }

    private CreationQualityInfoPage(RunnerSessionCoordinator coordinator, CharacterOverviewState original,
        string name, string detail, string? followUp, string? sourceXml) : base(coordinator)
    {
        _original = original;
        _name = name;
        _detail = detail;
        _followUp = followUp;
        _sourceXml = sourceXml;
        Title = name;
        AutomationId = "creation-quality-info-page";
        Content = new ScrollView { Content = _body };
        Refresh();
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (!Coordinator.IsCreationCatalogDisplayCurrent(_original))
        {
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Qualities.Info.Stale",
                "Reopen Qualities for the current runner.")));
            return;
        }
        _body.Add(NativeTheme.Title(_name));
        _body.Add(NativeTheme.Body(_detail));
        if (!string.IsNullOrWhiteSpace(_followUp))
            _body.Add(NativeTheme.Body(_followUp));
        if (_sourceXml is not null)
            foreach (string effect in CreationQualityInfo.Effects(_sourceXml))
                _body.Add(NativeTheme.Body(effect));
        else
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Qualities.Info.MissingSource",
                "The description for this granted quality is not available yet.")));
        Button back = NativeTheme.ReadingButton(CreationFlowStrings.Get("Qualities.Configure.Back", "Back to qualities"));
        back.AutomationId = "creation-quality-info-back";
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        _body.Add(back);
    }
}

internal static class CreationQualityInfo
{
    // Resolve only within this accepted authority by source identity, never by
    // a display name or an ambient catalog. Conflicting source bytes are not help.
    internal static string? SourceForGrant(CharacterCreationGrantedQuality grant,
        IReadOnlyList<CharacterCreationQualityCatalogOption> options)
    {
        var sources = options.Where(option => option.SourceId == grant.SourceId)
            .Select(option => option.SourceNodeXml).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return sources.Length == 1 ? sources[0] : null;
    }

    private static XElement Read(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
        return XElement.Load(reader);
    }

    public static IReadOnlyList<string> Effects(string xml)
    {
        var source = Read(xml);
        var result = new List<string>();
        // Original, localized explanatory copy, keyed by the accepted source's
        // stable identity. It never quotes, calculates or admits a selection.
        string summary = Guid.TryParse(source.Element("id")?.Value, out var id)
            ? CreationFlowStrings.Get("Qualities.Summary." + id.ToString("D"), string.Empty) : string.Empty;
        if (!string.IsNullOrWhiteSpace(summary)) result.Add(summary);
        bool incomplete = false;
        foreach (var effect in source.Element("bonus")?.Elements() ?? [])
        {
            string? description = effect.Name.LocalName switch
            {
                "ambidextrous" => CreationFlowStrings.Get("Qualities.Info.Ambidextrous", "Use either hand without the off-hand penalty."),
                "skillattribute" => Fields(effect, "Attribute-based tests"),
                "skill" or "specificskill" => Fields(effect, "Skill"),
                "skillcategory" => Fields(effect, "Skill category"),
                "skillgroup" => Fields(effect, "Skill group"),
                "specificattribute" => Fields(effect, "Attribute changes"),
                "enableattribute" => Fields(effect, "Enables attribute"),
                "enabletab" => Fields(effect, "Enables capabilities"),
                "unlockskills" => Scalar(effect, "Unlocks skills"),
                "conditionmonitor" => Fields(effect, "Condition monitor"),
                "selectskill" => Fields(effect, "Chosen skill"),
                "selectattributes" => string.Join("\n", effect.Elements("selectattribute")
                    .Select(attribute => Fields(attribute, "Chosen attributes"))),
                "limitmodifier" => Fields(effect, "Limit"),
                "spelldicepool" => Fields(effect, "Spell tests"),
                "damageresistance" => Scalar(effect, "Damage resistance"),
                "physicalcmrecovery" => Scalar(effect, "Physical healing"),
                "stuncmrecovery" => Scalar(effect, "Stun recovery"),
                "nativelanguagelimit" => Scalar(effect, "Additional native languages"),
                "notoriety" => Scalar(effect, "Notoriety"),
                "publicawareness" => Scalar(effect, "Public awareness"),
                "surprise" => Scalar(effect, "Surprise tests"),
                "dodge" => Scalar(effect, "Defense tests"),
                "reach" => Scalar(effect, "Reach"),
                "fatigueresist" => Scalar(effect, "Fatigue resistance"),
                "toxiningestionresist" => Scalar(effect, "Ingested toxin resistance"),
                "toxininjectionresist" => Scalar(effect, "Injected toxin resistance"),
                "pathogencontactresist" => Scalar(effect, "Contact pathogen resistance"),
                "pathogeninhalationresist" => Scalar(effect, "Inhaled pathogen resistance"),
                "pathogeningestionresist" => Scalar(effect, "Ingested pathogen resistance"),
                "pathogeninjectionresist" => Scalar(effect, "Injected pathogen resistance"),
                "initiative" => Scalar(effect, "Initiative"),
                "initiativedice" or "initiativepass" => Scalar(effect, "Initiative dice"),
                "armor" => Scalar(effect, "Armor"),
                "physicalcm" => Scalar(effect, "Physical condition monitor"),
                "stuncm" => Scalar(effect, "Stun condition monitor"),
                "painresistance" => Scalar(effect, "Wound penalty resistance"),
                "composure" => Scalar(effect, "Composure"),
                "judgeintentions" => Scalar(effect, "Judge Intentions"),
                "memory" => Scalar(effect, "Memory"),
                "drainresist" => Scalar(effect, "Drain resistance"),
                "fadingresist" => Scalar(effect, "Fading resistance"),
                "spellresistance" => Scalar(effect, "Spell resistance"),
                "toxincontactresist" => Scalar(effect, "Contact toxin resistance"),
                "toxininhalationresist" => Scalar(effect, "Inhaled toxin resistance"),
                "diseaseresist" => Scalar(effect, "Disease resistance"),
                "sociallimit" => Scalar(effect, "Social limit"),
                "mentallimit" => Scalar(effect, "Mental limit"),
                "physicallimit" => Scalar(effect, "Physical limit"),
                // A prompt is not itself a modifier. Authored copy explains
                // the choice; the accepted option retains its follow-up label.
                "selecttext" when summary.Length > 0 => string.Empty,
                _ => AdditionalEffect(effect)
            };
            if (description is null)
                incomplete = true;
            else
            {
                // Unknown nested fields must not silently turn a partial
                // modifier into an apparently complete explanation, including
                // when its known text duplicates an earlier effect.
                incomplete |= HasUndescribedDetail(effect);
                if (!string.IsNullOrWhiteSpace(description) && !result.Contains(description, StringComparer.Ordinal))
                    result.Add(description);
            }
        }
        if (result.Count == 0)
            result.Add(CreationFlowStrings.Get("Qualities.Info.Manual", "A description of this quality is not available yet."));
        else if (incomplete)
            result.Add(CreationFlowStrings.Get("Qualities.Info.Additional", "Some additional effects are not yet described here."));
        return result;
    }

    private static string Label(string value) => CreationFlowStrings.Get("Qualities.Effect." + value, value);

    private static string Scalar(XElement effect, string label, bool hundredths = false)
        => effect.HasElements ? Fields(effect, label)
            : string.Join(" · ", new[] { string.IsNullOrWhiteSpace(effect.Value)
                ? Label(label) : $"{Label(label)}: {(hundredths ? DisplayHundredths(effect.Value) : DisplayValue(effect.Value))}" }.Concat(Attributes(effect)));

    // Formatting source units is not evaluating an improvement. Keep symbolic
    // expressions symbolic; in particular never substitute the current rating.
    private static string DisplayHundredths(string value)
        => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture, out var number)
            ? (number / 100m).ToString("0.############################", CultureInfo.CurrentUICulture)
            : Guid.TryParse(value, out _) ? string.Empty : $"({DisplayValue(value)}) / 100";

    private static string? ReplacementMovement(XElement effect)
    {
        string speed = (effect.Element("speed")?.Value ?? "walk").ToLowerInvariant();
        string? label = speed switch
        {
            "walk" => "Replacement walking multiplier", "run" => "Replacement running multiplier",
            "sprint" => "Replacement sprint distance", _ => null
        };
        if (label is null) return null;
        string text = Fields(effect, label, speed == "sprint" ? "Meters per hit" : "Multiplier", speed == "sprint");
        return string.IsNullOrWhiteSpace(effect.Element("category")?.Value)
            ? $"{text} · {Label("All movement types")}" : text;
    }

    // These are display mappings of the accepted source XML, not another rules
    // evaluator. In particular percentages remain percentages, not dice bonuses.
    private static string? AdditionalEffect(XElement effect) => effect.Name.LocalName switch
    {
        "coldarmor" => Scalar(effect, "Cold resistance armor"),
        "firearmor" => Scalar(effect, "Fire resistance armor"),
        "defensetest" => Scalar(effect, "Defense tests"),
        "astralreputation" => Scalar(effect, "Astral reputation"),
        "judgeintentionsdefense" => Scalar(effect, "Resisting Judge Intentions"),
        "judgeintentionsoffense" => Scalar(effect, "Reading another person's intentions"),
        "detectionspellresist" => Scalar(effect, "Detection spell resistance"),
        "decreaseintresist" => Scalar(effect, "Decrease Intuition resistance"),
        "decreaselogresist" => Scalar(effect, "Decrease Logic resistance"),
        "manaillusionresist" => Scalar(effect, "Mana illusion resistance"),
        "physicalillusionresist" => Scalar(effect, "Physical illusion resistance"),
        "mentalmanipulationresist" => Scalar(effect, "Mental manipulation resistance"),
        "physiologicaladdictionfirsttime" => Scalar(effect, "Resisting a new physical addiction"),
        "physiologicaladdictionalreadyaddicted" => Scalar(effect, "Resisting an existing physical addiction"),
        "psychologicaladdictionfirsttime" => Scalar(effect, "Resisting a new psychological addiction"),
        "psychologicaladdictionalreadyaddicted" => Scalar(effect, "Resisting an existing psychological addiction"),
        "drainvalue" => Scalar(effect, "Drain value change"),
        "fadingvalue" => Scalar(effect, "Fading value change"),
        "adeptpowerpoints" => Scalar(effect, "Adept Power Points"),
        "unarmeddv" => Scalar(effect, "Unarmed damage change"),
        "unarmeddvphysical" => Scalar(effect, "Unarmed attacks deal Physical damage"),
        "addesstophysicalcmrecovery" => Scalar(effect, "Add Essence to Physical healing tests"),
        "addesstostuncmrecovery" => Scalar(effect, "Add Essence to Stun recovery tests"),
        "disablebioware" => Scalar(effect, "Cannot use bioware"),
        "disablebiowaregrade" => Scalar(effect, "Unavailable bioware grade"),
        "disablecyberwaregrade" => Scalar(effect, "Unavailable cyberware grade"),
        "cyberwareessmultiplier" or "cyberwaretotalessmultiplier" => Scalar(effect, "Cyberware Essence cost (% of normal)"),
        "biowareessmultiplier" => Scalar(effect, "Bioware Essence cost (% of normal)"),
        "lifestylecost" => Scalar(effect, "Lifestyle cost change (%)"),
        "availability" => Scalar(effect, "Availability change"),
        "essencemax" => Scalar(effect, "Maximum Essence change"),
        "essencepenalty" => Scalar(effect, "Essence change"),
        "essencepenaltyt100" => Scalar(effect, "Essence change", hundredths: true),
        "essencepenaltymagonlyt100" => Scalar(effect, "Essence adjustment for Magic loss only", hundredths: true),
        "specialattburnmultiplier" => Scalar(effect, "Essence-related special-attribute loss (% of normal)"),
        "streetcredmultiplier" => Scalar(effect, "Extra earned Karma needed per Street Cred"),
        "walkmultiplier" => Fields(effect, "Walking multiplier change"),
        "runmultiplier" => Fields(effect, "Running multiplier change"),
        "sprintbonus" => Fields(effect, "Sprint distance change", "Meters per hit", hundredths: true),
        "movementreplace" => ReplacementMovement(effect),
        "skilldisable" => Scalar(effect, "Unavailable skill"),
        "skillgroupdisable" => Scalar(effect, "Unavailable skill group"),
        "skillgroupdisablechoice" => Scalar(effect, "Unavailable chosen skill group"),
        "skillgroupcategorydisable" => Scalar(effect, "Unavailable skill-group category"),
        "blockskillcategorydefaulting" => Scalar(effect, "Cannot default in this skill category"),
        "skillcategorykarmacost" => Fields(effect, "Skill-category Karma cost change"),
        "activeskillkarmacost" => Fields(effect, "Active-skill Karma cost change"),
        "knowledgeskillkarmacost" => Fields(effect, "Knowledge-skill Karma cost change"),
        "knowledgeskillkarmacostmin" => Fields(effect, "Minimum Knowledge-skill Karma cost"),
        "skillcategorykarmacostmultiplier" => Fields(effect, "Skill-category Karma cost", "Percent of normal cost"),
        "skillcategorypointcostmultiplier" => Fields(effect, "Skill-category creation-point cost", "Percent of normal cost"),
        "skillcategoryspecializationkarmacostmultiplier" => Fields(effect, "Specialization Karma cost", "Percent of normal cost"),
        "skillgroupcategorykarmacostmultiplier" => Fields(effect, "Skill-group-category Karma cost", "Percent of normal cost"),
        "knowledgeskillpoints" => Fields(effect, "Knowledge skill points"),
        "newspellkarmacost" => Scalar(effect, "New spell Karma cost change"),
        "contactkarma" => Scalar(effect, "Contact Karma cost change"),
        "contactkarmaminimum" => Scalar(effect, "Minimum contact Karma cost change"),
        "nuyenamt" => Scalar(effect, "Starting nuyen change"),
        "nuyenmaxbp" => Scalar(effect, "Karma-to-nuyen purchase limit change"),
        "restrictedgear" => Fields(effect, "Restricted gear allowance"),
        "specialmodificationlimit" => Scalar(effect, "Special modification allowance"),
        "spellcategorydamage" => Fields(effect, "Spell damage change"),
        "spellcategorydrain" => Fields(effect, "Spell Drain change"),
        "spelldescriptordamage" => Fields(effect, "Spell damage change"),
        "spelldescriptordrain" => Fields(effect, "Spell Drain change"),
        "focusbindingkarmacost" => Fields(effect, "Focus-binding Karma cost change"),
        "swapskillattribute" or "swapskillspecattribute" => Fields(effect, "Use a different test attribute"),
        "livingpersona" => Fields(effect, "Living persona attribute changes"),
        "addlimb" => Fields(effect, "Additional limbs"),
        "replaceattributes" => string.Join("\n", effect.Elements("replaceattribute")
            .Select(attribute => Fields(attribute, "Replacement attribute limits"))),
        "addqualities" => string.Join("\n", effect.Elements("addquality").Select(quality => Scalar(quality, "Grants quality"))),
        "addmetamagic" => Scalar(effect, "Grants metamagic"),
        "addecho" => Scalar(effect, "Grants echo"),
        "addspell" => Scalar(effect, "Grants spell"),
        "critterpowers" => PowerReferences(effect, optional: false),
        "optionalpowers" => PowerReferences(effect, optional: true),
        "limitcritterpowercategory" => Scalar(effect, "Power choices restricted to category"),
        "addgear" => Equipment(effect),
        "addskillspecializationoption" => SpecializationOptions(effect),
        "selectexpertise" => Scalar(effect, "Choose a free expertise specialization"),
        "allowspellrange" => Scalar(effect, "Permitted spell range"),
        "freespells" => Scalar(effect, "Free-spell allowance"),
        "addware" => Fields(effect, "Grants augmentation"),
        // Entries whose rules are not encoded (including enum-like flags such
        // as trustfund) need authored copy. Never turn their raw value into a
        // made-up benefit, or a selection prompt into a rule explanation.
        _ => null
    };

    private static string PowerReferences(XElement effect, bool optional)
    {
        var lines = new List<string>();
        if (optional)
        {
            lines.Add(Label("Choose from these powers, not all of them"));
            lines.Add($"{Label("Number of choices")}: {DisplayValue(effect.Attribute("count")?.Value ?? "1")}");
        }
        foreach (var power in effect.Elements(optional ? "optionalpower" : "power"))
        {
            // Names/selected parameters are source references, not an expanded
            // power definition. Never borrow rules from an ambient catalog.
            if (power.HasElements || string.IsNullOrWhiteSpace(power.Value) || Guid.TryParse(power.Value, out _)) continue;
            var parts = new List<string> { $"{Label(optional ? "Power option" : "Granted power")}: {DisplayValue(power.Value)}" };
            if (power.Attribute("rating") is { } rating)
                parts.Add($"{Label("Rating")}: {DisplayValue(rating.Value)}");
            if (power.Attribute("select") is { } selection)
                parts.Add($"{Label("Fixed detail")}: {DisplayValue(selection.Value)}");
            lines.Add(string.Join(" · ", parts));
        }
        return string.Join("\n", lines);
    }

    private static string Equipment(XElement effect)
    {
        static string Item(XElement item, string label) => Fields(item, label)
            + (item.Element("fullcost") is null ? $" · {Label("No nuyen cost for this granted item")}" : string.Empty);
        var lines = new List<string> { Item(effect, "Granted equipment") };
        // Core addgear admits immediate children. Preserve multiplicity: four
        // identical included licenses must not become a single displayed item.
        lines.AddRange(effect.Elements("children").Elements("child")
            .Select(child => Item(child, "Included equipment")));
        return string.Join("\n", lines);
    }

    private static string SpecializationOptions(XElement effect)
    {
        var lines = new List<string> { Fields(effect, "Additional specialization option, not automatically learned") };
        lines.AddRange(effect.Elements("skills").Elements("skill")
            .Where(skill => !skill.HasElements && !string.IsNullOrWhiteSpace(skill.Value) && !Guid.TryParse(skill.Value, out _))
            .Select(skill => Scalar(skill, "Skill")));
        return string.Join("\n", lines);
    }

    private static string? AttributeLabel(string name) => name switch
    {
        "condition" => "When", "specific" => "Only for", "lifestyle" => "Lifestyle",
        "type" => "Type", "limittoskill" => "Choose from", "excludecategory" => "Except",
        "minimumrating" => "Minimum skill rating", "select" => "Choice", "rating" => "Rating",
        "alchemical" => "Alchemical", "forced" => "Fixed choice",
        "attribute" => "Based on attribute", "skill" => "Based on skill", "limit" => "Restriction",
        "limittospecialization" => "Choose specialization from", _ => null
    };

    private static IEnumerable<string> Attributes(XElement effect)
        => effect.Attributes().Where(attribute => AttributeLabel(attribute.Name.LocalName) is not null)
            .Select(attribute => $"{Label(AttributeLabel(attribute.Name.LocalName)!)}: {DisplayValue(attribute.Value)}");

    private static string? FieldLabel(XElement field, string valueLabel) => field.Name.LocalName switch
    {
        "name" => "", "val" or "value" => valueLabel, "bonus" => "Bonus",
        "min" when field.Parent?.Name.LocalName is "specificattribute" or "selectattribute" => "Minimum change",
        "max" when field.Parent?.Name.LocalName is "specificattribute" or "selectattribute" or "selectskill" => "Maximum change",
        "aug" when field.Parent?.Name.LocalName is "specificattribute" or "selectattribute" => "Augmented maximum change",
        "min" => "Minimum", "max" => "Maximum", "aug" => "Augmented maximum",
        "condition" => "When", "applytorating" => "Applies to rating",
        "thresholdoffset" => "Wound-penalty threshold offset", "threshold" => "Wound-penalty interval change",
        "overflow" => "Additional overflow boxes", "physical" => "Physical boxes", "stun" => "Stun boxes",
        "exclude" or "excludeattribute" => "Except", "attribute" => "Attribute",
        "limit" => "Limit", "category" => "Category", "descriptor" => "Spell traits",
        "limittoskill" => "Skill", "spec" => "Specialization", "extracontains" => "Only for",
        "availability" => "Maximum Availability", "amount" => "Number of items",
        "disablespecializationeffects" => "Specialization bonuses do not apply",
        "attack" => "Attack", "sleaze" => "Sleaze", "dataprocessing" => "Data Processing", "firewall" => "Firewall",
        "limbslot" => "Limb", "grade" => "Grade", "type" => "Type",
        "percent" => "Percentage change", "speed" => "Pace", "skillgroup" => "Choose from",
        "rating" => "Rating", "quantity" => "Quantity", "skill" => "Skill",
        "fullcost" when field.Parent?.Name.LocalName is "addgear" or "child" => "Pay full price", _ => null
    };

    private static bool HasUndescribedDetail(XElement effect)
    {
        // Listing a granted/optional power does not explain that power's own
        // rules. Keep these entries partial until those definitions are bound.
        if (effect.Name.LocalName is "critterpowers" or "optionalpowers") return true;
        if (!effect.HasElements && Guid.TryParse(effect.Value, out _)) return true;
        if (effect.Attributes().Any(attribute => AttributeLabel(attribute.Name.LocalName) is null)) return true;
        foreach (var field in effect.Elements())
        {
            if (effect.Name.LocalName == "addgear" && field.Name.LocalName == "children")
            {
                if (field.HasAttributes || field.Elements().Any(child => child.Name.LocalName != "child" || HasUndescribedDetail(child))) return true;
                continue;
            }
            if (effect.Name.LocalName == "addskillspecializationoption" && field.Name.LocalName == "skills")
            {
                if (field.HasAttributes || field.Elements().Any(skill => skill.Name.LocalName != "skill"
                    || skill.HasElements || skill.HasAttributes || Guid.TryParse(skill.Value, out _))) return true;
                continue;
            }
            if (field.Name.LocalName is "selectattribute" or "replaceattribute" or "addquality")
            {
                if (HasUndescribedDetail(field)) return true;
                continue;
            }
            if (FieldLabel(field, "Modifier") is null || field.HasElements || field.HasAttributes || Guid.TryParse(field.Value, out _)) return true;
        }
        return false;
    }

    private static string DisplayValue(string value)
        => Guid.TryParse(value, out _) ? string.Empty
            : CreationFlowStrings.Get("Qualities.Value." + (value switch
                { "Magician" => "magician", "Adept" => "adept", "Technomancer" => "technomancer", _ => value }), value);

    // Preserve source expressions and conditions; only convert known display
    // units. Do not evaluate Rating or mistake a cap increase for dice.
    private static string Fields(XElement effect, string label, string valueLabel = "Modifier", bool hundredths = false)
    {
        var parts = new List<string> { Label(label) };
        parts.AddRange(Attributes(effect));
        foreach (var field in effect.Elements())
        {
            string? fieldLabel = FieldLabel(field, valueLabel);
            if (fieldLabel is null || field.HasElements || Guid.TryParse(field.Value, out _)) continue;
            if (field.Name.LocalName is "disablespecializationeffects" or "fullcost") parts.Add(Label(fieldLabel));
            else if (!string.IsNullOrWhiteSpace(field.Value))
            {
                string value = hundredths && field.Name.LocalName is "val" or "value"
                    ? DisplayHundredths(field.Value) : DisplayValue(field.Value);
                parts.Add(fieldLabel.Length == 0 ? value : $"{Label(fieldLabel)}: {value}");
            }
        }
        return string.Join(" · ", parts);
    }
}

/// <summary>Phone-deep details for one immutable Core option; no label-based identity.</summary>
public sealed class CreationQualityConfigurePage : NativePageBase
{
    private readonly CharacterOverviewState _original;
    private readonly CharacterCreationQualitiesState _state;
    private readonly CharacterCreationQualitiesEditorState _editor;
    private readonly CharacterCreationQualitiesDesktopOption _option;
    private readonly CreationQualitiesPhoneDraft _draft;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _blockers = [];

    internal CreationQualityConfigurePage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationQualitiesState state,
        CharacterCreationQualitiesEditorState editor,
        CharacterCreationQualitiesDesktopOption option,
        CreationQualitiesPhoneDraft draft,
        CharacterOverviewState? original = null) : base(coordinator)
    {
        _original = original ?? coordinator.State;
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _option = option ?? throw new ArgumentNullException(nameof(option));
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Title = CreationFlowStrings.Get("Qualities.Configure.PageTitle", "Configure quality");
        AutomationId = "creation-quality-configure-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.Step2", "SR5 · 2 of 4")));
        if (!Coordinator.IsCreationCatalogDisplayCurrent(_original))
        {
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Qualities.Info.Stale",
                "Reopen Qualities for the current runner."), NativeTheme.Muted));
            return;
        }
        _body.Add(NativeTheme.Title(_option.Name));
        VerticalStackLayout details = new() { Spacing = 6 };
        VerticalStackLayout technical = new() { Spacing = 6 };
        technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.StableOption", "Stable option"), _option.OptionId));
        technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceId", "Source id"), _option.SourceId.ToString("D")));
        details.Add(NativeTheme.Eyebrow(_option.Type == CharacterCreationQualityType.Positive
            ? CreationFlowStrings.Get("Qualities.Positive", "Positive qualities")
            : CreationFlowStrings.Get("Qualities.Negative", "Negative qualities")));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Rating", "Rating"), _option.Rating.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SignedKarma", "Signed Karma"), CreationQualitiesPage.Signed(_option.KarmaCost)));
        if (_option.IsMetagenic)
            details.Add(NativeTheme.Body(CreationFlowStrings.Get("Qualities.MetagenicLabel", "Metagenic")));
        if (!string.IsNullOrWhiteSpace(_option.FollowUpChoiceLabel))
        {
            details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.FollowUp", "Follow-up"), _option.FollowUpChoiceLabel));
            technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.FollowUpId", "Follow-up id"), _option.FollowUpChoiceId!));
        }
        technical.Add(NativeTheme.Body(
            string.Join("\n", _option.SourceAnchorIds),
            NativeTheme.Muted));
        Border detailCard = NativeTheme.Card(details);
        detailCard.AutomationId = "creation-quality-configure-authority";
        _body.Add(detailCard);

        CharacterCreationQualitiesPreview preview = _draft.Preview ?? _state.Preview;
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Format(
                "Qualities.Configure.Preview",
                "Selected: +{0} positive · -{1} negative · {2} Karma remaining",
                preview.PositiveQualityBudget.Used.ToString(CultureInfo.InvariantCulture),
                preview.NegativeQualityBudget.Used.ToString(CultureInfo.InvariantCulture),
                CreationQualitiesPage.Signed(preview.KarmaRemaining)),
            preview.Blockers.Count == 0 ? NativeTheme.Muted : NativeTheme.Danger));

        foreach (string blocker in preview.Blockers.Concat(_blockers).Distinct(StringComparer.Ordinal))
            _body.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));

        bool exact = CreationQualitiesPhoneAuthority.IsOptionConfigurable(_option)
                     && Coordinator.IsCreationCatalogDisplayCurrent(_original)
                     && _draft.Matches(_state, Coordinator.State)
                     && _editor.Options.Count(candidate => string.Equals(
                         candidate.OptionId,
                         _option.OptionId,
                         StringComparison.Ordinal)) == 1;
        Button toggle = NativeTheme.PrimaryButton(
            _draft.IsSelected(_option.OptionId)
                ? CreationFlowStrings.Get("Qualities.Configure.Remove", "Remove from draft")
                : CreationFlowStrings.Get("Qualities.Configure.Add", "Add to draft"));
        toggle.AutomationId = "creation-quality-configure-toggle";
        toggle.IsEnabled = exact;
        toggle.Clicked += async (_, _) => await RunAsync(ToggleAsync);
        _body.Add(toggle);
        if (!exact)
        {
            Label disabled = NativeTheme.Body(
                CreationQualitiesPage.UnavailableReason(_option.DisableReasonKey),
                NativeTheme.Danger);
            disabled.AutomationId = "creation-quality-configure-disabled-reason";
            _body.Add(disabled);
        }
        Button done = NativeTheme.SecondaryButton(CreationFlowStrings.Get(
            "Qualities.Configure.Back",
            "Back to qualities"));
        done.AutomationId = "creation-quality-configure-done";
        done.Clicked += async (_, _) => await Navigation.PopAsync();
        _body.Add(done);
        _body.Add(NativeTheme.TechnicalDetails(technical, "creation-quality-configure-technical-details"));
    }

    private async Task ToggleAsync()
    {
        IReadOnlyList<string> proposed = _draft.WithToggle(_option);
        CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> result =
            await Task.Run(() => Coordinator.ReadCreationAuthority(_original,
                () => Coordinator.PreviewCreationQualities(_state.Binding, proposed, _original), CancellationToken.None));
        if (!Coordinator.IsCreationCatalogDisplayCurrent(_original)) return;
        _blockers = result.Blockers;
        _draft.TryAdopt(_state, Coordinator.State, result, proposed);
        Refresh();
    }
}

/// <summary>Immutable Core preview followed by one explicit durable apply transition.</summary>
public sealed class CreationQualitiesReviewPage : NativePageBase
{
    private readonly CharacterOverviewState _original;
    private CharacterCreationQualitiesCheckpoint _checkpoint;
    private readonly CharacterCreationQualitiesCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _blockers = [];
    private int _applyStarted;

    internal CreationQualitiesReviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationQualitiesCheckpoint checkpoint,
        CharacterCreationQualitiesCheckpointStore store,
        CharacterOverviewState? original = null) : base(coordinator)
    {
        _original = original ?? coordinator.State;
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (!_checkpoint.IsStructurallyValid()
            || _checkpoint.Phase != CharacterCreationQualitiesCheckpointPhase.Reviewed)
            throw new InvalidOperationException("The review page requires one exact Reviewed checkpoint.");
        Title = CreationFlowStrings.Get("Qualities.Review.PageTitle", "Review qualities");
        AutomationId = "creation-qualities-review-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (!Coordinator.IsCreationCatalogDisplayCurrent(_original))
        {
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Qualities.Info.Stale",
                "Reopen Qualities for the current runner."), NativeTheme.Muted));
            return;
        }
        CharacterCreationQualitiesPreview preview = _checkpoint.Preview;
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.Step3", "SR5 · 3 of 4")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get(
            "Qualities.Review.Heading",
            "Review qualities")));
        VerticalStackLayout technical = new() { Spacing = 6 };
        technical.Add(NativeTheme.Body(
            CreationFlowStrings.Format(
                "Qualities.Review.Binding",
                "Revision {0} · transaction {1}",
                preview.Binding.ContentRevision.ToString(CultureInfo.InvariantCulture),
                _checkpoint.TransactionId.ToString("D")),
            NativeTheme.Muted));
        AddDigest(technical, "creation-qualities-review-preview-digest", preview.PreviewDigest);
        AddDigest(technical, "creation-qualities-review-authority-digest", preview.AuthorityDigest);
        AddDigest(technical, "creation-qualities-review-raw-digest", preview.Binding.RawCharacterXmlDigest);
        AddDigest(technical, "creation-qualities-review-auxiliary-digest", preview.Binding.AuxiliaryStateDigest);

        VerticalStackLayout budgets = new() { Spacing = 6 };
        budgets.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.CoreLedgers", "Quality budgets")));
        budgets.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Qualities.PositiveKarma", "Positive quality Karma"),
            $"{preview.PositiveQualityBudget.Used.ToString(CultureInfo.InvariantCulture)} / {preview.PositiveQualityBudget.Total.ToString(CultureInfo.InvariantCulture)}"));
        budgets.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Qualities.NegativeKarma", "Negative quality Karma"),
            $"-{preview.NegativeQualityBudget.Used.ToString(CultureInfo.InvariantCulture)} / -{preview.NegativeQualityBudget.Total.ToString(CultureInfo.InvariantCulture)}"));
        budgets.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.CreationKarmaRemaining", "Creation Karma remaining"),
            CreationQualitiesPage.Signed(preview.KarmaRemaining)));
        _body.Add(NativeTheme.Card(budgets));

        if (preview.Selections.Count == 0)
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Qualities.Review.Empty", "No additional qualities selected.")));
        foreach (CharacterCreationQualitySelection selection in preview.Selections)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(selection.Name, 18));
            technical.Add(NativeTheme.Title(selection.Name, 18));
            technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.OptionId", "Option id"), selection.OptionId));
            technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceId", "Source id"), selection.SourceId.ToString("D")));
            card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Rating", "Rating"), selection.Rating.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SignedKarma", "Signed Karma"), CreationQualitiesPage.Signed(selection.KarmaCost)));
            if (!string.IsNullOrWhiteSpace(selection.FollowUpChoiceLabel))
                card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.FollowUp", "Follow-up"), selection.FollowUpChoiceLabel));
            technical.Add(NativeTheme.Body(string.Join("\n", selection.SourceAnchorIds), NativeTheme.Muted));
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-qualities-review-selection-{CreationQualitiesPage.Token(selection.OptionId)}";
            _body.Add(border);
        }

        foreach (string blocker in preview.Blockers.Concat(_blockers).Distinct(StringComparer.Ordinal))
            _body.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));

        Button apply = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
            "Qualities.Review.Confirm",
            "Save qualities"));
        apply.AutomationId = "creation-qualities-confirm-draft";
        apply.IsEnabled = _checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Reviewed
                          && Coordinator.IsCreationCatalogDisplayCurrent(_original)
                          && preview.CanConfirm
                          && preview.RequiresExplicitConfirmation
                          && preview.Blockers.Count == 0
                          && Volatile.Read(ref _applyStarted) == 0;
        apply.Clicked += async (_, _) => await RunAsync(ApplyAsync);
        _body.Add(apply);
        Label boundary = NativeTheme.Body(
            CreationFlowStrings.Get(
                "Qualities.Review.Boundary",
                "Your choices are saved with this draft. Their effects are applied when you finish creating your runner."),
            NativeTheme.Muted);
        boundary.AutomationId = "creation-qualities-review-preview-only";
        _body.Add(boundary);
        _body.Add(NativeTheme.TechnicalDetails(technical, "creation-qualities-review-technical-details"));
    }

    private async Task ApplyAsync()
    {
        if (Interlocked.CompareExchange(ref _applyStarted, 1, 0) != 0)
            return;
        try
        {
            if (!Coordinator.IsCreationCatalogDisplayCurrent(_original))
            {
                _blockers = [CharacterCreationQualitiesBlockers.RevisionConflict];
                return;
            }
            CharacterCreationFoundationResult<CharacterCreationQualitiesState> live =
                await Coordinator.LoadCreationQualitiesForDisplayAsync(_original, CancellationToken.None);
            CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> reprojection =
                await Task.Run(() => Coordinator.ReadCreationAuthority(_original, () => Coordinator.PreviewCreationQualities(
                    _checkpoint.Preview.Binding,
                    _checkpoint.SelectedOptionIds, _original), CancellationToken.None));
            if (!Coordinator.IsCreationCatalogDisplayCurrent(_original) || live.Value is not { } state
                || !_checkpoint.OwnsExactReview(state, Coordinator.State)
                || !CreationQualitiesPhoneAuthority.CanDisplayPreview(
                    state,
                    Coordinator.State,
                    reprojection,
                    _checkpoint.SelectedOptionIds)
                || reprojection.Value is not { } canonical
                || !CreationQualitiesPhoneAuthority.CanonicallyEquals(
                    _checkpoint.Preview,
                    canonical))
            {
                _blockers = reprojection.Blockers
                    .Append(CharacterCreationQualitiesBlockers.PreviewChanged)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return;
            }
            if (!_store.TryBeginApply(
                    CharacterCreationQualitiesCheckpointCas.From(_checkpoint),
                    out CharacterCreationQualitiesCheckpoint applying,
                    out string beginBlocker))
            {
                _blockers = [beginBlocker];
                return;
            }
            _checkpoint = applying;
            CreationQualitiesPhoneConfirmResult result =
                await Coordinator.ConfirmCreationQualitiesAsync(applying, display: _original);
            _blockers = result.Blockers;
            if (result.MutationOutcomeKnown
                && string.Equals(result.Outcome, CreationQualitiesPhoneOutcomes.Applied, StringComparison.Ordinal)
                && result.Receipt is { } receipt)
            {
                if (!_store.TryRecordApplied(
                        CharacterCreationQualitiesCheckpointCas.From(applying),
                        receipt,
                        out CharacterCreationQualitiesCheckpoint applied,
                        out string appliedBlocker))
                {
                    _blockers = [appliedBlocker, CreationQualitiesPhoneBlockers.OutcomeUnknown];
                    return;
                }
                _checkpoint = applied;
                await Navigation.PushAsync(new CreationQualitiesReceiptPage(
                    Coordinator,
                    applied,
                    receipt,
                    _store));
                return;
            }
            if (result.MutationOutcomeKnown
                && string.Equals(
                    result.Outcome,
                    CreationQualitiesPhoneOutcomes.RejectedBeforeMutation,
                    StringComparison.Ordinal)
                && _store.TryReturnToReviewed(
                    CharacterCreationQualitiesCheckpointCas.From(applying),
                    out CharacterCreationQualitiesCheckpoint reviewed,
                    out string returnBlocker))
            {
                _checkpoint = reviewed;
                _blockers = result.Blockers;
                return;
            }
            _blockers = result.Blockers.Count == 0
                ? [CreationQualitiesPhoneBlockers.OutcomeUnknown]
                : result.Blockers;
        }
        finally
        {
            Interlocked.Exchange(ref _applyStarted, 0);
            Refresh();
        }
    }

    private static void AddDigest(VerticalStackLayout technical, string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        technical.Add(label);
    }
}

/// <summary>Receipt acknowledgement; no direct character-apply action exists on this page.</summary>
public sealed class CreationQualitiesReceiptPage : NativePageBase
{
    private readonly Chummer.Application.Owners.OwnerContextStamp? _originalOwner;
    private readonly CharacterCreationQualitiesCheckpoint _checkpoint;
    private readonly CharacterCreationQualitiesDraftReceipt _receipt;
    private readonly CharacterCreationQualitiesCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationQualitiesReceiptPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationQualitiesCheckpoint checkpoint,
        CharacterCreationQualitiesDraftReceipt receipt,
        CharacterCreationQualitiesCheckpointStore store) : base(coordinator)
    {
        _originalOwner = coordinator.State.DisplayOwnerContext;
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (!_checkpoint.IsStructurallyValid()
            || _checkpoint.Phase != CharacterCreationQualitiesCheckpointPhase.Applied
            || _checkpoint.Receipt != _receipt)
            throw new InvalidOperationException("The receipt page requires one exact durable Applied checkpoint.");
        Title = CreationFlowStrings.Get("Qualities.Receipt.PageTitle", "Qualities saved");
        AutomationId = "creation-qualities-receipt-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.Step4", "SR5 · 4 of 4")));
        if (!Coordinator.IsCreationQualitiesOwnerCurrent(_originalOwner))
        {
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Qualities.Info.Stale",
                "Reopen Qualities for the current runner."), NativeTheme.Muted));
            return;
        }
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Common.DraftSaved", "Creation draft saved")));
        VerticalStackLayout card = new() { Spacing = 6 };
        VerticalStackLayout technical = new() { Spacing = 6 };
        technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Transaction", "Transaction"), _receipt.TransactionId.ToString("D")));
        technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.PreviousRevision", "Previous revision"), _receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.ContentRevision", "Content revision"), _receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SavedRevision", "Saved revision"), _receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.PositiveKarmaUsed", "Positive Karma used"), _checkpoint.Preview.PositiveQualityBudget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.NegativeKarmaUsed", "Negative Karma used"), $"-{_checkpoint.Preview.NegativeQualityBudget.Used.ToString(CultureInfo.InvariantCulture)}"));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.KarmaRemaining", "Karma remaining"), CreationQualitiesPage.Signed(_checkpoint.Preview.KarmaRemaining)));
        technical.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.DocumentChanged", "Character document changed"), _receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        AddDigest(technical, "creation-qualities-receipt-digest", _receipt.ReceiptDigest);
        AddDigest(technical, "creation-qualities-receipt-draft-digest", _receipt.DraftDigest);
        AddDigest(technical, "creation-qualities-receipt-plan-digest", _receipt.PlanDigest);
        AddDigest(technical, "creation-qualities-receipt-command-digest", _receipt.CommandDigest);
        Border receiptCard = NativeTheme.Card(card);
        receiptCard.AutomationId = "creation-qualities-confirm-receipt";
        _body.Add(receiptCard);
        Label boundary = NativeTheme.Body(
            !_receipt.CharacterDocumentChanged
                ? CreationFlowStrings.Get(
                    "Qualities.Receipt.Safe",
                    "Your choices are saved. Their effects are applied when you finish creating your runner.")
                : CreationFlowStrings.Get(
                    "Qualities.Receipt.Unsafe",
                    "Unsafe receipt: the character document changed before finalization."),
            !_receipt.CharacterDocumentChanged ? NativeTheme.Muted : NativeTheme.Danger);
        boundary.AutomationId = "creation-qualities-receipt-finalization-state";
        _body.Add(boundary);
        Button acknowledge = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
            "Qualities.Receipt.Continue",
            "Continue"));
        acknowledge.AutomationId = "creation-qualities-receipt-acknowledge";
        acknowledge.IsEnabled = !_receipt.CharacterDocumentChanged
                                && _checkpoint.OwnsRecoveryRevision(Coordinator.State);
        acknowledge.Clicked += async (_, _) => await RunAsync(AcknowledgeAsync);
        _body.Add(acknowledge);
        _body.Add(NativeTheme.TechnicalDetails(technical, "creation-qualities-receipt-technical-details"));
    }

    private async Task AcknowledgeAsync()
    {
        if (!_store.TryAcknowledgeApplied(
                CharacterCreationQualitiesCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReceiptNotAcknowledged", "Receipt not acknowledged"),
                blocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
            return;
        }
        // Receipt acknowledgement has completed; return through the attached
        // Shell, not this page's navigation proxy after popping its own page.
        if (Shell.Current is not MainShell { UsesTabletComposition: false } shell)
            throw new InvalidOperationException("Creation returns through the phone Runner route.");
        await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute, animate: false);
    }

    private static void AddDigest(
        VerticalStackLayout card,
        string automationId,
        string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        card.Add(label);
    }
}
