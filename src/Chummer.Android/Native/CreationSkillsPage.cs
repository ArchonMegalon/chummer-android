using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>Core-owned local draft calculation; fresh store/source review before saving.</summary>
public sealed class CreationSkillsPage : CreationAutosavePage
{
    private const int CatalogPageSize = 20;
    private string _searchText = string.Empty;
    private CancellationTokenSource? _searchCancellation;
    internal Task SearchRefresh { get; private set; } = Task.CompletedTask;
    private readonly CreationSkillsPhoneDraft _draft = new();
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _blockers = [];
    private CharacterCreationSkillsState? _authority;
    private CharacterCreationSkillsState? _revalidationAuthority;
    private string? _catalogSnapshotDigest;
    private int _activeCatalogOffset;
    private int _knowledgeCatalogOffset;
    private long _renderGeneration;
    private readonly List<Action> _projectionUpdates = [];
    private readonly List<CreationIntegerSlider> _sliders = [];
    private readonly List<Func<bool>> _ratingShapeChecks = [];
    private readonly bool _afterSavedGear;

    public CreationSkillsPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationSkillsState? authority = null, bool afterSavedGear = false) : base(coordinator)
    {
        _authority = authority;
        _revalidationAuthority = authority;
        _afterSavedGear = afterSavedGear;
        Title = CreationAllocationStrings.Get("Skills.PageTitle", "Skills");
        AutomationId = "creation-skills-page";
        var search = new SearchBar
        {
            Placeholder = PhoneStrings.Get("CreationSkillsSearch", "Search skills and groups"),
            TextColor = NativeTheme.Text, PlaceholderColor = NativeTheme.Muted,
            BackgroundColor = NativeTheme.Paper, AutomationId = "creation-skills-search"
        };
        search.TextChanged += (_, args) =>
        {
            if (!CanEditAllocation) return;
            _searchCancellation?.Cancel();
            // Typing must not rebuild native cards or enqueue a complete set
            // of speculative Core scale checks for every key. Retire obsolete
            // checks immediately and render only the settled query.
            foreach (var slider in _sliders) slider.RetireScale();
            var cancellation = new CancellationTokenSource();
            _searchCancellation = cancellation;
            SearchRefresh = RefreshSearchAsync(args.NewTextValue?.Trim() ?? string.Empty,
                CaptureAppearanceGeneration(), cancellation);
        };
        var editor = new Grid { RowDefinitions = new() { new(GridLength.Auto), new(GridLength.Star) } };
        editor.Add(search);
        editor.Add(new ScrollView { Content = _body }, 0, 1);
        SetAutosaveContent(editor, "creation-skills");
    }

    private async Task RefreshSearchAsync(string query, long appearance, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(300, cancellation.Token);
            if (cancellation.IsCancellationRequested || !IsCurrentAppearanceGeneration(appearance)
                || !CanEditAllocation) return;
            _searchText = query;
            ShowAllocationFeedback(null);
            _activeCatalogOffset = _knowledgeCatalogOffset = 0;
            // A settled query is a read, not a mutation to reject while
            // another action owns the gate. Drain its render on release.
            await RefreshAfterBackgroundReadAsync(appearance);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation)) _searchCancellation = null;
            cancellation.Dispose();
        }
    }

    protected override void Refresh()
    {
        if (IsSaving) return;
        foreach (var slider in _sliders) slider.RetireScale();
        _sliders.Clear();
        _renderGeneration++;
        _projectionUpdates.Clear();
        _ratingShapeChecks.Clear();
        _body.Clear();
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? load = null;
        CharacterCreationSkillsState? state = CreationPageAuthorityCache.Resolve(
            _authority,
            candidate => Coordinator.IsCreationSkillsStateCurrent(candidate)
                         && CreationSkillsPhoneAuthority.IsReady(candidate, Coordinator.State),
            () =>
            {
                load = _revalidationAuthority is { } original
                    ? Coordinator.RevalidateCreationSkills(original)
                    : Coordinator.LoadCreationSkills();
                return load.Value;
            });
        _authority = state;
        _revalidationAuthority ??= state;
        if (state is null)
        {
            AddBlockers(load is { Blockers.Count: > 0 }
                ? load.Blockers
                : [CharacterCreationSkillsBlockers.AuthorityUnavailable]);
            return;
        }
        _draft.Bind(state, Coordinator.State);
        ResetCatalogPagingIfAuthorityChanged(state);
        if (CreationSkillsPhoneAuthority.IsReady(state, Coordinator.State) && _draft.Matches(state, Coordinator.State))
        {
            if (_afterSavedGear)
            {
                var notice = NativeTheme.Body(CreationFlowStrings.Get("Gear.SkillsRequired",
                    "Your equipment is saved. Save your skills to calculate contact points."), NativeTheme.Muted);
                notice.AutomationId = "creation-gear-skills-required";
                _body.Insert(0, notice);
            }
            AddNativeLanguage(state);
            if (_blockers.Count > 0) AddBlockers(_blockers, "creation-skills-review-top-blockers");
        }
        AddBinding(state);
        VerticalStackLayout budgets = new() { Spacing = 12 };
        AddBudget(budgets, () => _draft.Preview?.ActiveSkillPointBudget ?? state.ActiveSkillPointBudget, "active", CharacterCreationBudgetIds.ActiveSkills);
        AddBudget(budgets, () => _draft.Preview?.SkillGroupPointBudget ?? state.SkillGroupPointBudget, "groups", CharacterCreationBudgetIds.SkillGroups);
        AddBudget(budgets, () => _draft.Preview?.KnowledgeSkillPointBudget ?? state.KnowledgeSkillPointBudget, "knowledge", CharacterCreationBudgetIds.KnowledgeSkills);
        Border summary = NativeTheme.Card(budgets);
        summary.AutomationId = "creation-skills-budgets";
        _body.Add(summary);
        if (!CreationSkillsPhoneAuthority.IsReady(state, Coordinator.State) || !_draft.Matches(state, Coordinator.State))
        {
            AddBlockers(state.Blockers);
            return;
        }
        AddCatalog(
            state,
            CreationSkillsPhoneAuthority.AvailableActiveSkills(state).Where(MatchesSearch).ToArray(),
            CreationAllocationStrings.Get("Skills.ActiveSkills", "Active skills"),
            "active");
        AddGroups(state);
        AddCatalog(
            state,
            state.Authority.KnowledgeSkills.Where(MatchesSearch).ToArray(),
            CreationAllocationStrings.Get("Skills.KnowledgeLanguages", "Knowledge & languages"),
            "knowledge");
    }

    protected override void OnDisappearing()
    {
        _searchCancellation?.Cancel();
        _draft.RetireSession();
        foreach (var slider in _sliders) slider.RetireScale();
        base.OnDisappearing();
    }

    private void AddNativeLanguage(CharacterCreationSkillsState state)
    {
        // The required first language must not be buried inside many pages of
        // knowledge skills. This is only another entrance to the same Core preview.
        CharacterCreationSkillCatalogEntry[] options = state.Authority.KnowledgeSkills
            .Where(item => item.CanBeNativeLanguage).ToArray();
        if (options.Length == 0) return;
        VerticalStackLayout card = new() { Spacing = 8 };
        Label heading = NativeTheme.Eyebrow(CreationAllocationStrings.Get("Skills.NativeLanguage", "Native language"));
        card.Add(heading);
        string[] chosen = options.Where(option => _draft.Skills.Any(item =>
                item.Kind == option.Kind && item.SourceSkillId == option.SourceSkillId && item.IsNativeLanguage))
            .Select(option => SkillCatalogStrings.SkillName(option.Kind, option.SourceSkillId, option.Name)).ToArray();
        if (chosen.Length > 0)
        {
            card.Add(NativeTheme.Title(string.Join(", ", chosen), 18));
        }
        else
        {
            heading.TextColor = NativeTheme.Danger;
            card.Add(NativeTheme.RequiredChoiceNotice("creation-skills-native-language-required"));
            Picker picker = new()
            {
                Title = CreationAllocationStrings.Get("Skills.ChooseNativeLanguage", "Choose native language"),
                ItemsSource = options.Select(option => SkillCatalogStrings.SkillName(option.Kind, option.SourceSkillId, option.Name)).ToArray(),
                SelectedIndex = -1,
                TextColor = NativeTheme.Text,
                TitleColor = NativeTheme.Danger,
                BackgroundColor = NativeTheme.Surface,
                FontSize = 16,
                AutomationId = "creation-skills-native-language-picker"
            };
            Button choose = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
                "Skills.UseNativeLanguage", "Use as native language"));
            choose.AutomationId = "creation-skills-native-language-preview";
            choose.IsEnabled = false;
            picker.SelectedIndexChanged += (_, _) => choose.IsEnabled = picker.SelectedIndex >= 0
                && picker.SelectedIndex < options.Length;
            long renderGeneration = _renderGeneration;
            long appearanceGeneration = CaptureAppearanceGeneration();
            choose.Clicked += async (_, _) =>
            {
                int index = picker.SelectedIndex;
                if (index < 0 || index >= options.Length || renderGeneration != _renderGeneration
                    || !IsCurrentAppearanceGeneration(appearanceGeneration)
                    || !_draft.Matches(state, Coordinator.State)) return;
                await PreviewAsync(state, _draft.WithSkill(options[index], 0, native: true), _draft.Groups);
            };
            card.Add(picker);
            card.Add(choose);
        }
        Border border = NativeTheme.Card(card);
        if (chosen.Length == 0) border.Stroke = new SolidColorBrush(NativeTheme.Danger);
        border.AutomationId = "creation-skills-native-language";
        _body.Add(border);
    }

    private void AddBinding(CharacterCreationSkillsState state)
    {
        Label binding = NativeTheme.Body(
            CreationAllocationStrings.Format(
                "Skills.Binding",
                "Revision {0} · prerequisite {1} · attributes {2}",
                state.Binding.ContentRevision,
                state.Binding.PrerequisiteDraftRevision,
                state.Binding.AttributesDraftRevision),
            NativeTheme.Muted);
        binding.AutomationId = "creation-skills-binding";
        _body.Add(binding);
    }

    private void AddBudget(VerticalStackLayout budgets, Func<CharacterCreationBudgetState> readBudget, string token, string canonicalBudgetId)
    {
        budgets.Add(CreationSkillsBudgetRow.Create(readBudget, canonicalBudgetId,
            $"creation-skills-budget-{token}", out Action update));
        _projectionUpdates.Add(update);
    }

    private void AddCatalog(
        CharacterCreationSkillsState state,
        IReadOnlyList<CharacterCreationSkillCatalogEntry> catalog,
        string title,
        string catalogToken)
    {
        _body.Add(NativeTheme.Eyebrow(title));
        int currentOffset = string.Equals(catalogToken, "knowledge", StringComparison.Ordinal)
            ? _knowledgeCatalogOffset
            : _activeCatalogOffset;
        int offset = CreationSkillsCatalogPaging.NormalizeOffset(
            currentOffset,
            catalog.Count,
            CatalogPageSize);
        SetCatalogOffset(catalogToken, offset);
        int end = Math.Min(catalog.Count, offset + CatalogPageSize);
        Label range = NativeTheme.Body(
            catalog.Count == 0
                ? CreationAllocationStrings.Get("Skills.NoCatalogEntries", "No available entries")
                : CreationAllocationStrings.Format(
                    "Skills.CatalogRange",
                    "Showing {0}–{1} of {2}",
                    offset + 1,
                    end,
                    catalog.Count),
            NativeTheme.Muted);
        range.AutomationId = $"creation-skills-{catalogToken}-catalog-range";
        _body.Add(range);

        foreach (CharacterCreationSkillCatalogEntry source in catalog
                     .Skip(offset)
                     .Take(CatalogPageSize))
        {
            CharacterCreationSkillAllocation? selected = _draft.Skills.SingleOrDefault(item =>
                item.Kind == source.Kind && item.SourceSkillId == source.SourceSkillId);
            CharacterCreationSkillAllocation? CurrentSelection() => _draft.Skills.SingleOrDefault(item =>
                item.Kind == source.Kind && item.SourceSkillId == source.SourceSkillId);
            bool hasSpecialization = source.Specializations.Count > 0
                && selected is { Rating: > 0, IsNativeLanguage: false };
            bool isNative = selected?.IsNativeLanguage == true;
            _ratingShapeChecks.Add(() => isNative == (CurrentSelection()?.IsNativeLanguage == true)
                && hasSpecialization == (source.Specializations.Count > 0
                    && CurrentSelection() is { Rating: > 0, IsNativeLanguage: false }));
            VerticalStackLayout card = new() { Spacing = 7 };
            card.Add(NativeTheme.Title(SkillCatalogStrings.SkillName(source.Kind, source.SourceSkillId, source.Name), 18));
            Label detail = NativeTheme.Body(string.Empty, NativeTheme.Muted);
            void UpdateDetail()
            {
                selected = CurrentSelection();
                detail.Text = CreationAllocationStrings.Format(
                    "Skills.SkillDetail",
                    "{0} · {1} · rating {2}",
                    SkillCatalogStrings.CategoryName(source.Category),
                    CreationAllocationStrings.AttributeName(source.DefaultAttribute),
                    selected?.IsNativeLanguage == true
                        ? CreationAllocationStrings.Get("Skills.NativeValue", "native")
                        : (selected?.Rating ?? 0).ToString(CultureInfo.CurrentCulture));
            }
            UpdateDetail();
            _projectionUpdates.Add(UpdateDetail);
            detail.FontAttributes = FontAttributes.Bold;
            card.Add(detail);
            AddTalentGrant(card, _draft.MinimumRating(source));
            string feedbackKey = $"{source.Kind}-{Token(source.SourceSkillId)}";
            if (source.CanBeNativeLanguage)
            {
                Button native = NativeTheme.SecondaryButton(selected?.IsNativeLanguage == true
                    ? CreationAllocationStrings.Get("Skills.RemoveNative", "Remove native")
                    : CreationAllocationStrings.Get("Skills.NativeAction", "Native"));
                native.Clicked += async (_, _) => await PreviewAsync(state,
                    selected?.IsNativeLanguage == true
                        ? _draft.Skills.Where(item => item.SourceSkillId != source.SourceSkillId).ToArray()
                        : _draft.WithSkill(source, 0, native: true),
                    _draft.Groups);
                card.Add(native);
            }
            AddRatingSlider(card, state, feedbackKey,
                SkillCatalogStrings.SkillName(source.Kind, source.SourceSkillId, source.Name),
                () => CurrentSelection()?.Rating ?? 0,
                () => (_draft.MinimumRating(source), source.Kind == CharacterCreationSkillKinds.Active
                    ? state.Authority.MaxActiveSkillRatingCreate : state.Authority.MaxKnowledgeSkillRatingCreate,
                    CurrentSelection()?.IsNativeLanguage != true),
                target => _draft.WithSkill(source, target - (CurrentSelection()?.Rating ?? 0)), () => _draft.Groups);
            if (source.Specializations.Count > 0 && selected is { Rating: > 0, IsNativeLanguage: false })
                AddSpecializationPicker(card, state, source, selected);
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-skill-{Token(source.SourceSkillId)}";
            _body.Add(border);
        }

        HorizontalStackLayout pager = new() { Spacing = 10 };
        Button previous = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
            "Common.Previous",
            "Previous"));
        previous.AutomationId = $"creation-skills-{catalogToken}-catalog-previous";
        previous.IsEnabled = offset > 0;
        previous.Clicked += (_, _) =>
        {
            SetCatalogOffset(
                catalogToken,
                CreationSkillsCatalogPaging.PreviousOffset(offset, CatalogPageSize));
            Refresh();
        };
        pager.Add(previous);
        Button next = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
            "Common.Next",
            "Next"));
        next.AutomationId = $"creation-skills-{catalogToken}-catalog-next";
        next.IsEnabled = end < catalog.Count;
        next.Clicked += (_, _) =>
        {
            SetCatalogOffset(
                catalogToken,
                CreationSkillsCatalogPaging.NextOffset(offset, catalog.Count, CatalogPageSize));
            Refresh();
        };
        pager.Add(next);
        _body.Add(pager);
    }

    private void ResetCatalogPagingIfAuthorityChanged(CharacterCreationSkillsState state)
    {
        if (string.Equals(_catalogSnapshotDigest, state.SnapshotDigest, StringComparison.Ordinal))
            return;

        _catalogSnapshotDigest = state.SnapshotDigest;
        _activeCatalogOffset = 0;
        _knowledgeCatalogOffset = 0;
    }

    private void SetCatalogOffset(string catalogToken, int offset)
    {
        if (string.Equals(catalogToken, "knowledge", StringComparison.Ordinal))
            _knowledgeCatalogOffset = offset;
        else
            _activeCatalogOffset = offset;
    }

    private void AddGroups(CharacterCreationSkillsState state)
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Skills.SkillGroups",
            "Skill groups")));
        // Keep this ledger beside its controls, not several screens above them.
        var budget = new VerticalStackLayout();
        AddBudget(budget, () => _draft.Preview?.SkillGroupPointBudget ?? state.SkillGroupPointBudget,
            "groups-inline", CharacterCreationBudgetIds.SkillGroups);
        _body.Add(budget);
        foreach (CharacterCreationSkillGroupCatalogEntry source in CreationSkillsPhoneAuthority.AvailableGroups(state)
                     .Where(group => MatchesSearch(group.Name) || MatchesSearch(SkillCatalogStrings.GroupName(group.Name))))
        {
            CharacterCreationSkillGroupAllocation? selected = _draft.Groups.SingleOrDefault(item => item.GroupId == source.GroupId);
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(SkillCatalogStrings.GroupName(source.Name), 18));
            Label detail = NativeTheme.Body(string.Empty, NativeTheme.Muted);
            void UpdateDetail()
            {
                selected = _draft.Groups.SingleOrDefault(item => item.GroupId == source.GroupId);
                detail.Text = CreationAllocationStrings.Format(
                    "Skills.GroupDetail",
                    "Rating {0} · {1} skills",
                    selected?.Rating ?? 0,
                    source.MemberSkillSourceIds.Count);
            }
            UpdateDetail();
            detail.FontAttributes = FontAttributes.Bold;
            _projectionUpdates.Add(UpdateDetail);
            card.Add(detail);
            AddTalentGrant(card, _draft.MinimumRating(source));
            string feedbackKey = $"group-{Token(source.GroupId)}";
            AddRatingSlider(card, state, feedbackKey, SkillCatalogStrings.GroupName(source.Name),
                () => _draft.Groups.SingleOrDefault(item => item.GroupId == source.GroupId)?.Rating ?? 0,
                () => (_draft.MinimumRating(source), state.Authority.MaxSkillGroupRatingCreate, true),
                _ => _draft.Skills,
                () => _draft.Groups,
                target => _draft.WithGroup(source,
                    target - (_draft.Groups.SingleOrDefault(item => item.GroupId == source.GroupId)?.Rating ?? 0)));
            _body.Add(NativeTheme.Card(card));
        }
    }

    private static void AddTalentGrant(VerticalStackLayout card, int minimumRating)
    {
        if (minimumRating > 0)
            card.Add(NativeTheme.Body(CreationAllocationStrings.Format(
                "Skills.TalentGrant",
                "Free starting rating {0} from Talent Priority.",
                minimumRating), NativeTheme.Muted));
    }

    private void AddSpecializationPicker(
        VerticalStackLayout card,
        CharacterCreationSkillsState state,
        CharacterCreationSkillCatalogEntry source,
        CharacterCreationSkillAllocation selected)
    {
        // Preserve the complete admitted catalog and its typed identities. Display
        // names are not keys, and opening/selecting in the picker never spends points.
        CharacterCreationSkillSpecializationOption[] options = source.Specializations.ToArray();
        int currentIndex = selected.SpecializationOptionId is null ? 0
            : Array.FindIndex(options, option => string.Equals(option.OptionId,
                selected.SpecializationOptionId, StringComparison.Ordinal)) + 1;
        Picker picker = new()
        {
            Title = CreationAllocationStrings.Get("SkillsReReview.ChooseSpecialization", "Choose specialization"),
            ItemsSource = new[] { CreationAllocationStrings.Get("SkillsReReview.NoSpecialization", "No specialization") }
                .Concat(options.Select(option => SkillCatalogStrings.SpecializationName(
                    source.Kind, source.SourceSkillId, source.Name, option.Name))).ToArray(),
            SelectedIndex = currentIndex,
            TextColor = NativeTheme.Text,
            TitleColor = NativeTheme.Muted,
            BackgroundColor = NativeTheme.Surface,
            FontSize = 16,
            AutomationId = $"creation-skill-specialization-{Token(source.SourceSkillId)}"
        };
        long renderGeneration = _renderGeneration;
        long appearanceGeneration = CaptureAppearanceGeneration();
        bool updating = false;
        _projectionUpdates.Add(() => selected = _draft.Skills.SingleOrDefault(item =>
            item.Kind == source.Kind && item.SourceSkillId == source.SourceSkillId) ?? selected);
        picker.SelectedIndexChanged += async (_, _) =>
        {
            int index = picker.SelectedIndex;
            if (updating || index < 0 || index > options.Length || index == currentIndex
                || !CanEditAllocation || renderGeneration != _renderGeneration
                || !IsCurrentAppearanceGeneration(appearanceGeneration)
                || !Coordinator.IsCreationSkillsDraftDisplayCurrent(state)
                || !_draft.HasPreparedState(state)) return;
            string? optionId = index == 0 ? selected.SpecializationOptionId : options[index - 1].OptionId;
            if (optionId is null) return;
            await RunWithConditionalRefreshAsync(() =>
            {
                bool accepted = _draft.TryPreviewPrepared(state, _draft.WithSpecialization(source, optionId),
                    _draft.Groups, out var blockers);
                ShowRatingFeedback(blockers);
                if (accepted) { currentIndex = index; DraftChanged(); }
                foreach (var update in _projectionUpdates) update();
                if (!accepted)
                {
                    updating = true;
                    picker.SelectedIndex = currentIndex;
                    updating = false;
                }
                return Task.FromResult(false);
            });
        };
        card.Add(picker);
    }

    private void ShowRatingFeedback(IReadOnlyList<string> blockers)
    {
        // Do not repeat gesture refusals at the top of the scrolling catalog.
        // Authority/load failures continue to use their existing page blockers.
        _blockers = [];
        ShowAllocationFeedback(string.Join("\n", blockers.Select(CreationAllocationStrings.SkillBlocker)
            .Distinct(StringComparer.Ordinal)));
    }

    private void AddRatingSlider(VerticalStackLayout card, CharacterCreationSkillsState state,
        string feedbackKey, string label, Func<int> current,
        Func<(int Minimum, int Maximum, bool Enabled)> range,
        Func<int, IReadOnlyList<CharacterCreationSkillAllocation>> skills,
        Func<IReadOnlyList<CharacterCreationSkillGroupAllocation>> groups,
        Func<int, IReadOnlyList<CharacterCreationSkillGroupAllocation>>? changedGroups = null)
    {
        long render = _renderGeneration;
        long appearance = CaptureAppearanceGeneration();
        bool dragging = false;
        bool refreshAfterDrag = false;
        bool Current() => CanEditAllocation && render == _renderGeneration && IsCurrentAppearanceGeneration(appearance)
            && Coordinator.IsCreationSkillsDraftDisplayCurrent(state) && _draft.HasPreparedState(state);
        var slider = new CreationIntegerSlider($"creation-skills-slider-{feedbackKey}", label, current, range,
            target => RunWithConditionalRefreshAsync(() =>
            {
                if (!Current()) return Task.FromResult(false);
                bool accepted = _draft.TryPreviewPrepared(state, skills(target),
                    changedGroups?.Invoke(target) ?? groups(), out var blockers);
                ShowRatingFeedback(blockers);
                if (accepted) DraftChanged();
                foreach (var update in _projectionUpdates) update();
                bool refresh = accepted && !_ratingShapeChecks.All(check => check());
                // Rating 0→1 adds the specialization picker. Keep the current
                // native slider alive until release so that this does not cut
                // a drag short at its first point. Budget feedback stays live.
                if (dragging)
                {
                    refreshAfterDrag |= refresh;
                    return Task.FromResult(false);
                }
                return Task.FromResult(refresh);
            }), selectionVersion: () => _draft.Preview ?? (object)state,
            prepareSelection: target => target == current() ? () => true : _draft.PrepareSelection(state, skills(target),
                changedGroups?.Invoke(target) ?? groups()));
        slider.DragStarted += (_, _) => { dragging = Current(); if (dragging) BeginAllocationGesture(); };
        slider.DragCompleted += async (_, _) =>
        {
            dragging = false;
            EndAllocationGesture();
            if (!refreshAfterDrag) return;
            refreshAfterDrag = false;
            await RunWithConditionalRefreshAsync(() => Task.FromResult(
                Current() && !_ratingShapeChecks.All(check => check())));
        };
        _projectionUpdates.Add(slider.UpdateProjection);
        _sliders.Add(slider);
        card.Add(slider);
        card.Add(slider.RatingScale);
    }

    private Task PreviewAsync(CharacterCreationSkillsState state,
        IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
        => RunWithConditionalRefreshAsync(() =>
        {
            if (!CanEditAllocation || !Coordinator.IsCreationSkillsDraftDisplayCurrent(state)
                || !_draft.HasPreparedState(state)) return Task.FromResult(false);
            bool accepted = _draft.TryPreviewPrepared(state, skills, groups, out var blockers);
            ShowRatingFeedback(blockers);
            if (accepted) DraftChanged();
            return Task.FromResult(true);
        });

    private bool MatchesSearch(string value) => CultureInfo.CurrentUICulture.CompareInfo.IndexOf(
        value, _searchText, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private bool MatchesSearch(CharacterCreationSkillCatalogEntry skill)
        => MatchesSearch(skill.Name) || MatchesSearch(SkillCatalogStrings.SkillName(
            skill.Kind, skill.SourceSkillId, skill.Name)) || MatchesSearch(SkillCatalogStrings.CategoryName(skill.Category));

    protected override bool HasSavedDraft => _authority?.PendingDraft is not null;

    protected override async Task<SaveResult> SaveDraftAsync()
    {
        if (_authority is not { } state || !Coordinator.IsCreationSkillsStateCurrent(state)
            || !_draft.Matches(state, Coordinator.State)) return new(false, RequiresReopen: true);
        var skills = _draft.Skills.ToArray();
        var groups = _draft.Groups.ToArray();
        var original = Coordinator.State;
        var result = await Task.Run(() => Coordinator.ReadCreationAuthority(original,
            () => Coordinator.PreviewCreationSkills(state.Binding, skills, groups), CancellationToken.None));
        if (result.Value is not { } preview
            || !CreationSkillsPhoneAuthority.CanAdoptPreview(state, Coordinator.State, result, skills, groups))
        {
            if (state.PendingDraft is not null && Coordinator.IsCreationSkillsStateCurrent(state)
                && result.Blockers.SequenceEqual(new[] { CharacterCreationSkillsBlockers.DraftDuplicate }))
                return new(true);
            return new(false, string.Join("\n", result.Blockers.Select(CreationAllocationStrings.SkillBlocker)));
        }
        var confirmed = await Coordinator.ConfirmCreationSkillsAsync(preview, skills, groups,
            CreationSkillsPhoneAuthority.ComputeIdempotencyKey(preview, skills, groups));
        if (confirmed is { Outcome: CharacterCreationFoundationOutcomes.Success, Blockers.Count: 0,
                Receipt: { } receipt, RefreshedState: { } fresh }
            && Coordinator.IsCreationSkillsReceiptCurrent(receipt) && Coordinator.IsCreationSkillsStateCurrent(fresh))
        {
            _authority = _revalidationAuthority = fresh;
            _draft.Bind(fresh, Coordinator.State);
            // Our own save must not jump to catalog page one or erase an adjacent refusal.
            _catalogSnapshotDigest = fresh.SnapshotDigest;
            _blockers = [];
            return new(true);
        }
        return new(false, string.Join("\n", confirmed.Blockers.Select(CreationAllocationStrings.SkillBlocker)),
            confirmed.Receipt is not null || confirmed.Outcome == "outcome-unknown");
    }

    protected override async Task OpenNextStepAsync(long appearance)
    {
        var original = Coordinator.State;
        var loaded = await Coordinator.LoadCreationQualitiesForDisplayAsync(original, CancellationToken.None);
        bool ready = await Task.Run(() => loaded is
            { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state }
            && CreationQualitiesPhoneAuthority.IsReady(state, original));
        if (!IsCurrentAppearanceGeneration(appearance)) return;
        if (!Coordinator.IsCreationSkillsStateCurrent(_authority!))
            throw new InvalidOperationException("Next Creation step unavailable.");
        if (!ready)
        {
            var review = await Coordinator.LoadCreationQualitiesReReviewAsync(original);
            if (!IsCurrentAppearanceGeneration(appearance)) return;
            if (review is null || !Coordinator.IsCreationQualitiesReReviewCurrent(review))
                throw new InvalidOperationException("Next Creation step unavailable.");
            string choices = review.Current.Preview.Selections.Count == 0
                ? CreationAllocationStrings.Get("QualitiesReReview.None", "No purchased qualities")
                : string.Join("\n", review.Current.Preview.Selections.Select(row => QualityCatalogStrings.Name(row.SourceId, row.Name)));
            bool keep = await DisplayAlertAsync(
                CreationAllocationStrings.Get("QualitiesReReview.Title", "Keep saved qualities?"),
                CreationAllocationStrings.Format("QualitiesReReview.Body",
                    "Your attributes changed. These saved qualities still fit and will stay unchanged:\n\n{0}\n\nKarma remaining: {1}",
                    choices, review.Current.Preview.KarmaRemaining),
                CreationAllocationStrings.Get("QualitiesReReview.Keep", "Keep and continue"),
                CreationAllocationStrings.Get("QualitiesReReview.Cancel", "Cancel"));
            if (!keep || !IsCurrentAppearanceGeneration(appearance)
                || !Coordinator.IsCreationQualitiesReReviewCurrent(review)) return;
            var saved = await Coordinator.ConfirmCreationQualitiesReReviewAsync(review, explicitlyReviewed: true,
                isCurrentPage: () => IsCurrentAppearanceGeneration(appearance));
            if (!IsCurrentAppearanceGeneration(appearance)) return;
            if (saved is not { Outcome: CreationQualitiesPhoneOutcomes.Applied, Blockers.Count: 0,
                    RefreshedState: { } fresh })
            {
                // Do not advertise a blind retry after a potentially completed save.
                RequireReopenForContinuation(CreationAllocationStrings.Get("QualitiesReReview.Reopen",
                    "Reopen the runner to check the saved qualities before continuing."));
                return;
            }
            original = Coordinator.State;
            loaded = new(CharacterCreationFoundationOutcomes.Success, fresh, []);
        }
        if (!Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
        await Navigation.PushAsync(new CreationQualitiesPage(Coordinator, original, loaded), animated: false);
    }

    private void AddBlockers(IReadOnlyList<string> blockers, string? automationId = null)
    {
        // Core rejects a duplicate save even when the current choices are valid.
        // Only that sole no-change result is informational; mixed errors stay warnings.
        bool unchanged = blockers.Count > 0
            && blockers.All(blocker => blocker == CharacterCreationSkillsBlockers.DraftDuplicate);
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(unchanged
            ? CreationAllocationStrings.Get("Skills.AlreadySaved", "Already saved")
            : CreationAllocationStrings.Get("Skills.CheckChoices", "Check your choices")));
        foreach (string message in blockers.Select(CreationAllocationStrings.SkillBlocker).Distinct(StringComparer.Ordinal))
            card.Add(NativeTheme.Body(message, unchanged ? NativeTheme.Text : NativeTheme.Danger));
        if (!unchanged)
        {
            VerticalStackLayout technical = new() { Spacing = 5 };
            foreach (string blocker in blockers)
                technical.Add(NativeTheme.Body(blocker, NativeTheme.Muted));
            long generation = _renderGeneration;
            long appearance = CaptureAppearanceGeneration();
            card.Add(NativeTheme.TechnicalDetails(technical, (automationId ?? "creation-skills-blockers") + "-details",
                () => generation == _renderGeneration && IsCurrentAppearanceGeneration(appearance)));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        if (automationId is "creation-skills-review-blockers" or "creation-skills-review-top-blockers")
            _projectionUpdates.Add(() => border.IsVisible = blockers.SequenceEqual(_blockers));
        _body.Add(border);
    }

    private static string Token(string value) => new(value.ToLowerInvariant()
        .Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}

public static class CreationSkillsCatalogPaging
{
    public static int NormalizeOffset(int offset, int count, int pageSize)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (count <= 0)
            return 0;

        int maximumOffset = (count - 1) / pageSize * pageSize;
        return Math.Clamp(offset / pageSize * pageSize, 0, maximumOffset);
    }

    public static int PreviousOffset(int offset, int pageSize)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        return Math.Max(0, offset / pageSize * pageSize - pageSize);
    }

    public static int NextOffset(int offset, int count, int pageSize)
        => NormalizeOffset(offset + pageSize, count, pageSize);
}

/// <summary>One wrapping row per ledger; values always come from the accepted Core projection.</summary>
internal static class CreationSkillsBudgetRow
{
    internal static Grid Create(Func<CharacterCreationBudgetState> readBudget,
        string canonicalBudgetId, string automationId, out Action update)
    {
        Grid row = new()
        {
            AutomationId = automationId,
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };
        Label title = NativeTheme.Body(BuildPageUiProjection.BudgetLabel(readBudget(), canonicalBudgetId));
        title.FontAttributes = FontAttributes.Bold;
        title.VerticalOptions = LayoutOptions.Center;
        Label remaining = NativeTheme.Title(string.Empty, 20);
        remaining.AutomationId = automationId + "-remaining";
        remaining.HorizontalTextAlignment = TextAlignment.End;
        Label used = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        used.AutomationId = automationId + "-used";
        used.HorizontalTextAlignment = TextAlignment.End;
        VerticalStackLayout values = new() { Spacing = 2 };
        values.Add(remaining);
        values.Add(used);
        row.Add(title);
        row.Add(values, 1);
        update = () =>
        {
            var current = readBudget();
            remaining.Text = CreationAllocationStrings.Format("Skills.BudgetLeft", "{0} left",
                current.Remaining.ToString("0.##", CultureInfo.CurrentCulture));
            remaining.TextColor = current.Remaining < 0 ? NativeTheme.Danger : NativeTheme.Text;
            used.Text = CreationAllocationStrings.Format("Skills.BudgetUsed", "{0} / {1} points",
                current.Used.ToString("0.##", CultureInfo.CurrentCulture),
                current.Total.ToString("0.##", CultureInfo.CurrentCulture));
        };
        update();
        return row;
    }
}

/// <summary>Immutable Core preview followed by one explicit digest-bound confirmation.</summary>
public sealed class CreationSkillsPreviewPage : NativePageBase
{
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private readonly CharacterCreationSkillsPreview _preview;
    private readonly IReadOnlyList<CharacterCreationSkillAllocation> _allocations;
    private readonly IReadOnlyList<CharacterCreationSkillGroupAllocation> _groups;
    private readonly string _idempotencyKey;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CreationSkillsPhoneConfirmResult? _confirmation;
    private readonly Action<CreationSkillsPhoneConfirmResult>? _onConfirmed;
    private Button? _confirmButton;
    private bool _saving;
    private CancellationTokenSource? _forwardPreparation;
    private bool _nextStepUnavailable;
    private long _renderGeneration;

    internal CreationSkillsPreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationSkillsPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        Action<CreationSkillsPhoneConfirmResult>? onConfirmed = null) : base(coordinator)
    {
        _onConfirmed = onConfirmed;
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _allocations = allocations?.OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.SourceSkillId, StringComparer.Ordinal).ToArray()
            ?? throw new ArgumentNullException(nameof(allocations));
        _groups = groups?.OrderBy(item => item.GroupId, StringComparer.Ordinal).ToArray()
            ?? throw new ArgumentNullException(nameof(groups));
        _idempotencyKey = CreationSkillsPhoneAuthority.ComputeIdempotencyKey(
            _preview,
            _allocations,
            _groups);
        Title = CreationAllocationStrings.Get("SkillsPreview.PageTitle", "Review Skills");
        AutomationId = "creation-skills-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        if (_saving && Coordinator.CanDisplayCreationSkillsPreview(_preview)) return;
        _renderGeneration++;
        _confirmButton = null;
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.ExplicitReview",
            "Explicit review")));
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get(
            "SkillsPreview.Heading",
            "Skills allocation")));
        if (!Coordinator.CanDisplayCreationSkillsPreview(_preview))
        {
            _body.Add(NativeTheme.Body(CreationAllocationStrings.Get("SkillsPreview.Stale",
                "This review is no longer current. Reopen the runner."), NativeTheme.Danger));
            return;
        }
        Label binding = NativeTheme.Body(
            CreationAllocationStrings.Format(
                "Common.PreviewBinding",
                "Revision {0} · saved {1} · preview {2}",
                _preview.Binding.ContentRevision,
                _preview.Binding.SavedRevision,
                CreationPrerequisiteDigestText.CanonicalPrefix(_preview.PreviewDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-skills-preview-binding";
        _technicalDetails.Add(binding);
        AddDigest("creation-skills-preview-digest", _preview.PreviewDigest);
        AddDigest("creation-skills-preview-raw-character-xml-digest", _preview.Binding.RawCharacterXmlDigest);
        AddDigest("creation-skills-preview-auxiliary-state-digest", _preview.Binding.AuxiliaryStateDigest);
        AddBudgets();
        AddSelections();
        AddBlockers();
        AddConfirmation();
        AddReceipt();
        VerticalStackLayout details = _technicalDetails;
        long appearanceGeneration = CaptureAppearanceGeneration();
        _body.Add(NativeTheme.TechnicalDetails(details, "creation-skills-preview-details",
            () => ReferenceEquals(details, _technicalDetails)
                  && IsCurrentAppearanceGeneration(appearanceGeneration)
                  && Coordinator.CanDisplayCreationSkillsPreview(_preview)));
    }

    protected override void OnDisappearing()
    {
        _confirmButton = null;
        // Cancel the following read, never an admitted write with an uncertain outcome.
        _forwardPreparation?.Cancel();
        base.OnDisappearing();
    }

    private void AddBudgets()
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "SkillsPreview.FinalCoreLedgers",
            "Points after saving")));
        VerticalStackLayout budgets = new() { Spacing = 12 };
        foreach (var (budget, canonicalBudgetId) in new[]
                 {
                     (_preview.ActiveSkillPointBudget, CharacterCreationBudgetIds.ActiveSkills),
                     (_preview.SkillGroupPointBudget, CharacterCreationBudgetIds.SkillGroups),
                     (_preview.KnowledgeSkillPointBudget, CharacterCreationBudgetIds.KnowledgeSkills)
                 })
        {
            budgets.Add(CreationSkillsBudgetRow.Create(() => budget, canonicalBudgetId,
                $"creation-skills-preview-budget-{Token(budget.BudgetId)}", out _));
        }
        Border summary = NativeTheme.Card(budgets);
        summary.AutomationId = "creation-skills-preview-budgets";
        _body.Add(summary);
    }

    private void AddSelections()
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "SkillsPreview.TypedSelections",
            "Your skills")));
        foreach (CharacterCreationSkillProjection skill in _preview.Skills)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(SkillCatalogStrings.SkillName(skill.Kind, skill.SourceSkillId, skill.Name), 18));
            _technicalDetails.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("SkillsPreview.Kind", "Kind"),
                skill.Name + " · " + skill.Kind));
            card.Add(NativeTheme.Metric(CreationAllocationStrings.Get("SkillsPreview.Rating", "Rating"), skill.IsNativeLanguage
                ? CreationAllocationStrings.Get("Skills.NativeValue", "native")
                : skill.Rating.GetValueOrDefault().ToString(CultureInfo.CurrentCulture)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("SkillsPreview.PointCost", "Point cost"),
                skill.PointCost.ToString(CultureInfo.CurrentCulture)));
            if (!string.IsNullOrWhiteSpace(skill.SpecializationName))
                card.Add(NativeTheme.Metric(
                    CreationAllocationStrings.Get("SkillsPreview.Specialization", "Specialization"),
                    SkillCatalogStrings.SpecializationName(skill.Kind, skill.SourceSkillId, skill.Name, skill.SpecializationName)));
            _body.Add(NativeTheme.Card(card));
        }
        foreach (CharacterCreationSkillGroupProjection group in _preview.SkillGroups)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(SkillCatalogStrings.GroupName(group.Name), 18));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("SkillsPreview.GroupRating", "Group rating"),
                group.Rating.ToString(CultureInfo.CurrentCulture)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("SkillsPreview.PointCost", "Point cost"),
                group.PointCost.ToString(CultureInfo.CurrentCulture)));
            _body.Add(NativeTheme.Card(card));
        }
    }

    private void AddBlockers()
    {
        string[] blockers = _preview.Blockers.Concat(_confirmation?.Blockers ?? [])
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (blockers.Length == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Skills.CheckChoices",
            "Check your choices")));
        foreach (string message in blockers.Select(CreationAllocationStrings.SkillBlocker).Distinct(StringComparer.Ordinal))
            card.Add(NativeTheme.Body(message, NativeTheme.Danger));
        foreach (string blocker in blockers)
            _technicalDetails.Add(NativeTheme.Body(blocker, NativeTheme.Muted));
        _body.Add(NativeTheme.Card(card));
    }

    private void AddConfirmation()
    {
        if (_confirmation is
            {
                Outcome: CharacterCreationFoundationOutcomes.Success,
                Receipt: not null
            })
        {
            bool refreshRequired = _confirmation.Blockers.Contains(
                CharacterCreationSkillsBlockers.PostCommitRefreshRequired,
                StringComparer.Ordinal);
            Label complete = NativeTheme.Body(refreshRequired
                ? CreationAllocationStrings.Get(
                    "SkillsPreview.ConfirmedRefreshRequired",
                    "Your skills are saved. Reopen the runner to see the updated values.")
                : CreationAllocationStrings.Get(
                    "SkillsPreview.Confirmed",
                    "Your skills are saved."),
                refreshRequired ? NativeTheme.Danger : NativeTheme.Text);
            complete.AutomationId = "creation-skills-confirmed";
            _body.Add(NativeTheme.Card(complete));
            if (_nextStepUnavailable)
            {
                Label failure = NativeTheme.Body(CreationAllocationStrings.Get(
                    "SkillsPreview.NextUnavailable",
                    "Skills saved. Qualities could not be loaded. Tap Continue to try loading them again."), NativeTheme.Danger);
                failure.AutomationId = "creation-skills-next-unavailable";
                _body.Add(failure);
            }
            return;
        }

        CharacterCreationFoundationResult<CharacterCreationSkillsState> live = Coordinator.LoadCreationSkills();
        bool canConfirm = Coordinator.IsCreationSkillsPreviewCurrent(_preview)
                          && live.Value is { } state
                          && CreationSkillsPhoneAuthority.CanConfirmPreview(
                              state, Coordinator.State, _preview, _allocations, _groups);
        Button confirm = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
            "SkillsPreview.Confirm",
            "Save skills"));
        confirm.AutomationId = "creation-skills-confirm";
        confirm.IsEnabled = canConfirm;
        _confirmButton = confirm;
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (_saving || !ReferenceEquals(_confirmButton, confirm) || !confirm.IsEnabled
                || !Coordinator.IsCreationSkillsPreviewCurrent(_preview)) return;
            long appearance = CaptureAppearanceGeneration();
            long render = _renderGeneration;
            _saving = true;
            string label = confirm.Text;
            confirm.IsEnabled = false;
            confirm.Text = CreationAllocationStrings.Get("SkillsPreview.Saving", "Saving…");
            try
            {
                _confirmation = await Coordinator.ConfirmCreationSkillsAsync(
                    _preview, _allocations, _groups, _idempotencyKey);
                _onConfirmed?.Invoke(_confirmation);
                if (IsCurrentAppearanceGeneration(appearance))
                {
                    confirm.Text = CreationAllocationStrings.Get("SkillsPreview.OpeningQualities", "Opening qualities…");
                    await ContinueToQualitiesAsync(_confirmation, render, appearance);
                }
            }
            finally
            {
                _saving = false;
                confirm.Text = label;
                // Retained Save stays disabled. Only the current render owns actions.
                long currentAppearance = CaptureAppearanceGeneration();
                if (currentAppearance != appearance && IsCurrentAppearanceGeneration(currentAppearance))
                    Refresh();
            }
        });
        _body.Add(confirm);
        Label explicitAction = NativeTheme.Body(
            CreationAllocationStrings.Get(
                "SkillsPreview.ConfirmationBoundary",
                "Check your choices, then save them. This does not finish character creation."),
            NativeTheme.Muted);
        explicitAction.AutomationId = "creation-skills-explicit-confirmation";
        _body.Add(explicitAction);
    }

    private void AddReceipt()
    {
        if (_confirmation is not
            {
                Outcome: CharacterCreationFoundationOutcomes.Success,
                Receipt: { } receipt,
                RefreshedState: { } refreshed
            })
        {
            return;
        }
        if (!Coordinator.IsCreationSkillsReceiptCurrent(receipt)) return;
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "SkillsPreview.SavedHeading",
            "Skills saved")));
        _technicalDetails.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "SkillsPreview.AtomicReceipt",
            "Atomic Skills draft receipt")));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.PreviousRevision", "Previous revision"),
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.ContentRevision", "Content revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.SavedRevision", "Saved revision"),
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.DraftRevision", "Draft revision"),
            receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("SkillsPreview.ActivePointsRemaining", "Active points remaining"),
            receipt.ActivePointsRemaining.ToString(CultureInfo.CurrentCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("SkillsPreview.GroupPointsRemaining", "Group points remaining"),
            receipt.SkillGroupPointsRemaining.ToString(CultureInfo.CurrentCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("SkillsPreview.KnowledgePointsRemaining", "Knowledge points remaining"),
            receipt.KnowledgePointsRemaining.ToString(CultureInfo.CurrentCulture)));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.CharacterDocumentChanged", "Character document changed"),
            receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        AddReceiptDigest(_technicalDetails, "creation-skills-receipt-digest", receipt.ReceiptDigest);
        AddReceiptDigest(_technicalDetails, "creation-skills-receipt-draft-digest", receipt.DraftDigest);
        AddReceiptDigest(_technicalDetails, "creation-skills-receipt-raw-character-xml-digest", refreshed.Binding.RawCharacterXmlDigest);
        card.Add(NativeTheme.Body(
            refreshed.PendingDraft?.CharacterEffectsApplied == false
                ? CreationAllocationStrings.Get(
                    "SkillsPreview.DurablePendingFinalization",
                    "These choices are saved in your creation draft. Finish character creation separately to enter Career.")
                : CreationAllocationStrings.Get(
                    "Common.CharacterEffectStateUnsafe",
                    "Character-effect state is not safe to continue."),
            refreshed.PendingDraft?.CharacterEffectsApplied == false ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-skills-confirm-receipt";
        _body.Add(border);

        if (_confirmation.Blockers.Count == 0 && refreshed.PendingDraft?.CharacterEffectsApplied == false)
        {
            var confirmation = _confirmation;
            long render = _renderGeneration;
            long appearance = CaptureAppearanceGeneration();
            Button next = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
                "SkillsPreview.ContinueQualities", "Continue to qualities"));
            next.AutomationId = "creation-skills-continue-qualities";
            next.Clicked += async (_, _) => await RunAsync(async () =>
            {
                next.IsEnabled = false;
                next.Text = CreationAllocationStrings.Get("SkillsPreview.OpeningQualities", "Opening qualities…");
                await ContinueToQualitiesAsync(confirmation, render, appearance);
            });
            _body.Add(next);
        }
        Button back = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
            "Common.BackToBuild",
            "Back to Build"));
        back.AutomationId = "creation-skills-back-to-build";
        back.Clicked += async (_, _) => await RunAsync(BackToBuildAsync);
        _body.Add(back);
    }

    private async Task ContinueToQualitiesAsync(CreationSkillsPhoneConfirmResult confirmation,
        long render, long appearance)
    {
        if (confirmation is not { Outcome: CharacterCreationFoundationOutcomes.Success,
                Receipt: { } receipt, RefreshedState: { PendingDraft.CharacterEffectsApplied: false } }
            || confirmation.Blockers.Count != 0) return;
        INavigation navigation = Navigation;
        Page[] stack = navigation.NavigationStack.ToArray();
        bool Current() => render == _renderGeneration && IsCurrentAppearanceGeneration(appearance)
            && ReferenceEquals(_confirmation, confirmation)
            && Coordinator.IsCreationSkillsReceiptCurrent(receipt)
            && ReferenceEquals(stack.LastOrDefault(), this)
            && navigation.NavigationStack.SequenceEqual(stack);
        if (!Current()) return;

        var original = Coordinator.State;
        using var lifetime = new CancellationTokenSource();
        _forwardPreparation = lifetime;
        _nextStepUnavailable = false;
        try
        {
            var loaded = await Coordinator.LoadCreationQualitiesForDisplayAsync(original, lifetime.Token);
            bool ready = await Task.Run(() => loaded is
                    { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state }
                && CreationQualitiesPhoneAuthority.IsReady(state, original), lifetime.Token);
            if (!Current() || lifetime.IsCancellationRequested) return;
            if (!ready)
            {
                _nextStepUnavailable = true;
                return;
            }
            var next = new CreationQualitiesPage(Coordinator, original, loaded);
            await navigation.PushAsync(next, animated: false);
            if (navigation.NavigationStack.SequenceEqual(stack.Append(next)))
                navigation.RemovePage(this);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (Current()) _nextStepUnavailable = true;
        }
        finally
        {
            if (ReferenceEquals(_forwardPreparation, lifetime)) _forwardPreparation = null;
        }
    }

    private async Task BackToBuildAsync()
    {
        // Child pops detach this page's navigation proxy before the parent pop.
        // Shell must own the return and attach the authored Runner root.
        if (Shell.Current is not MainShell { UsesTabletComposition: false } shell)
            throw new InvalidOperationException("Creation returns through the phone Runner route.");
        await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute, animate: false);
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _technicalDetails.Add(label);
    }

    private static void AddReceiptDigest(VerticalStackLayout card, string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        card.Add(label);
    }

    private static string Token(string value) => new(value.ToLowerInvariant()
        .Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
