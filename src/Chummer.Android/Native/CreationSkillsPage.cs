using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>Phone Skills step; every tap is accepted only after a full Core preview.</summary>
public sealed class CreationSkillsPage : NativePageBase
{
    private const int CatalogPageSize = 20;
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
    private CancellationTokenSource? _ratingPreparation;
    private readonly List<Action> _projectionUpdates = [];
    private readonly List<Func<bool>> _ratingShapeChecks = [];
    private readonly List<Button> _ratingButtons = [];

    public CreationSkillsPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationSkillsState? authority = null) : base(coordinator)
    {
        _authority = authority;
        _revalidationAuthority = authority;
        Title = CreationAllocationStrings.Get("Skills.PageTitle", "Skills");
        AutomationId = "creation-skills-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        if (_ratingPreparation is not null) return;
        _renderGeneration++;
        _projectionUpdates.Clear();
        _ratingShapeChecks.Clear();
        _ratingButtons.Clear();
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.CharacterCreation",
            "Character creation")));
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get(
            "Skills.Heading",
            "Choose Skills")));
        _body.Add(NativeTheme.Body(
            CreationAllocationStrings.Get(
                "Skills.Intro",
                "Ratings, groups, specializations, native languages, and all three ledgers come from Core."),
            NativeTheme.Muted));
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
            AddNativeLanguage(state);
            if (_blockers.Count > 0) AddBlockers(_blockers, "creation-skills-review-top-blockers");
            _body.Add(CreateReviewButton(state, "creation-skills-review-top"));
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
            CreationSkillsPhoneAuthority.AvailableActiveSkills(state),
            CreationAllocationStrings.Get("Skills.ActiveSkills", "Active skills"),
            "active");
        AddGroups(state);
        AddCatalog(
            state,
            state.Authority.KnowledgeSkills,
            CreationAllocationStrings.Get("Skills.KnowledgeLanguages", "Knowledge & languages"),
            "knowledge");
        AddReview(state);
    }

    protected override void OnDisappearing()
    {
        _ratingPreparation?.Cancel();
        _ratingPreparation = null;
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
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get("Skills.NativeLanguage", "Native language")));
        string[] chosen = options.Where(option => _draft.Skills.Any(item =>
                item.Kind == option.Kind && item.SourceSkillId == option.SourceSkillId && item.IsNativeLanguage))
            .Select(option => SkillCatalogStrings.SkillName(option.Kind, option.SourceSkillId, option.Name)).ToArray();
        if (chosen.Length > 0)
        {
            card.Add(NativeTheme.Title(string.Join(", ", chosen), 18));
        }
        else
        {
            Picker picker = new()
            {
                Title = CreationAllocationStrings.Get("Skills.ChooseNativeLanguage", "Choose native language"),
                ItemsSource = options.Select(option => SkillCatalogStrings.SkillName(option.Kind, option.SourceSkillId, option.Name)).ToArray(),
                SelectedIndex = -1,
                TextColor = NativeTheme.Text,
                TitleColor = NativeTheme.Muted,
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
            card.Add(detail);
            AddTalentGrant(card, _draft.MinimumRating(source));
            HorizontalStackLayout controls = new() { Spacing = 8 };
            Button minus = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
                "Common.Decrease",
                "−"));
            minus.IsEnabled = selected is { IsNativeLanguage: false }
                && selected.Rating > _draft.MinimumRating(source);
            BindRatingAdjustment(minus, state, () => _draft.WithSkill(source, -1), () => _draft.Groups,
                () => CurrentSelection() is { IsNativeLanguage: false } value && value.Rating > _draft.MinimumRating(source));
            Button plus = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
                "Common.Increase",
                "+"));
            BindRatingAdjustment(plus, state, () => _draft.WithSkill(source, 1), () => _draft.Groups, () => true);
            controls.Add(minus); controls.Add(plus);
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
                controls.Add(native);
            }
            card.Add(controls);
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
            if (_ratingPreparation is not null) return;
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
            if (_ratingPreparation is not null) return;
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
        foreach (CharacterCreationSkillGroupCatalogEntry source in CreationSkillsPhoneAuthority.AvailableGroups(state))
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
            _projectionUpdates.Add(UpdateDetail);
            card.Add(detail);
            AddTalentGrant(card, _draft.MinimumRating(source));
            HorizontalStackLayout controls = new() { Spacing = 8 };
            Button minus = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
                "Common.Decrease",
                "−"));
            minus.IsEnabled = selected?.Rating > _draft.MinimumRating(source);
            BindRatingAdjustment(minus, state, () => _draft.Skills, () => _draft.WithGroup(source, -1),
                () => _draft.Groups.SingleOrDefault(item => item.GroupId == source.GroupId)?.Rating > _draft.MinimumRating(source));
            Button plus = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
                "Common.Increase",
                "+"));
            BindRatingAdjustment(plus, state, () => _draft.Skills, () => _draft.WithGroup(source, 1), () => true);
            controls.Add(minus); controls.Add(plus); card.Add(controls);
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
        Button choose = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
            "SkillsReReview.SetSpecialization", "Preview specialization"));
        choose.AutomationId = $"creation-skill-specialization-preview-{Token(source.SourceSkillId)}";
        choose.IsEnabled = false;
        picker.SelectedIndexChanged += (_, _) => choose.IsEnabled = picker.SelectedIndex >= 0
            && picker.SelectedIndex <= options.Length && picker.SelectedIndex != currentIndex;
        long renderGeneration = _renderGeneration;
        long appearanceGeneration = CaptureAppearanceGeneration();
        // Rating-only previews retain this picker and any unsubmitted selection.
        // Refresh only its accepted allocation, never its pending SelectedIndex.
        _projectionUpdates.Add(() => selected = _draft.Skills.Single(item =>
            item.Kind == source.Kind && item.SourceSkillId == source.SourceSkillId));
        choose.Clicked += async (_, _) =>
        {
            int index = picker.SelectedIndex;
            if (index < 0 || index > options.Length || index == currentIndex
                || renderGeneration != _renderGeneration
                || !IsCurrentAppearanceGeneration(appearanceGeneration)
                || !_draft.Matches(state, Coordinator.State)
                || _draft.Skills.SingleOrDefault(item => item.Kind == source.Kind
                    && item.SourceSkillId == source.SourceSkillId) != selected)
                return;
            // WithSpecialization toggles an existing ID off. Use that path only
            // for the explicit "No specialization" choice, never for a no-op.
            string? optionId = index == 0 ? selected.SpecializationOptionId : options[index - 1].OptionId;
            if (optionId is null) return;
            await PreviewAsync(state, _draft.WithSpecialization(source, optionId), _draft.Groups);
        };
        card.Add(picker);
        card.Add(choose);
    }

    private void BindRatingAdjustment(Button button, CharacterCreationSkillsState state,
        Func<IReadOnlyList<CharacterCreationSkillAllocation>> skills,
        Func<IReadOnlyList<CharacterCreationSkillGroupAllocation>> groups, Func<bool> enabled)
    {
        long render = _renderGeneration;
        long appearance = CaptureAppearanceGeneration();
        string label = button.Text;
        void Update() => button.IsEnabled = enabled();
        Update();
        _projectionUpdates.Add(Update);
        _ratingButtons.Add(button);
        button.Clicked += async (_, _) => await RunWithConditionalRefreshAsync(async () =>
        {
            bool Current() => render == _renderGeneration && IsCurrentAppearanceGeneration(appearance)
                && Coordinator.IsCreationSkillsStateCurrent(state) && _draft.Matches(state, Coordinator.State);
            if (!button.IsEnabled || !Current()) return false;
            var requestedSkills = skills().ToArray();
            var requestedGroups = groups().ToArray();
            var original = Coordinator.State;
            using var lifetime = new CancellationTokenSource();
            _ratingPreparation = lifetime;
            foreach (var control in _ratingButtons) control.IsEnabled = false;
            button.Text = "…";
            try
            {
                var result = await Task.Run(() => Coordinator.ReadCreationAuthority(original,
                    () => Coordinator.PreviewCreationSkills(state.Binding, requestedSkills, requestedGroups), lifetime.Token), lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                if (!Current()) return false;
                bool adopted = _draft.TryAdopt(state, Coordinator.State, result, requestedSkills, requestedGroups);
                bool sameBlockers = _blockers.SequenceEqual(result.Blockers);
                _blockers = result.Blockers;
                // Preserve controls only when Core accepted the proposal and
                // no native-language, specialization or warning surface changes.
                if (!adopted || !sameBlockers || !_ratingShapeChecks.All(check => check())) return true;
                foreach (var update in _projectionUpdates) update();
                return false;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return false; }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (!IsCurrentAppearanceGeneration(appearance)) return false;
                _blockers = [CharacterCreationSkillsBlockers.AuthorityUnavailable];
                return true;
            }
            finally
            {
                button.Text = label;
                if (ReferenceEquals(_ratingPreparation, lifetime)) _ratingPreparation = null;
            }
        });
    }

    private async Task PreviewAsync(
        CharacterCreationSkillsState state,
        IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        CharacterCreationSkillAllocation[] requestedSkills = skills.ToArray();
        CharacterCreationSkillGroupAllocation[] requestedGroups = groups.ToArray();
        await RunAsync(async () =>
        {
            if (!Coordinator.IsCreationSkillsStateCurrent(state)) return;
            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> result =
                await Task.Run(() => Coordinator.PreviewCreationSkills(
                    state.Binding,
                    requestedSkills,
                    requestedGroups));
            _blockers = result.Blockers;
            _draft.TryAdopt(
                state,
                Coordinator.State,
                result,
                requestedSkills,
                requestedGroups);
        });
    }

    private void AddReview(CharacterCreationSkillsState state)
    {
        // The catalogs can span many screens. Keep the reason a review cannot
        // proceed beside its action as well as beside the allocation ledgers.
        if (_blockers.Count > 0)
            AddBlockers(_blockers, "creation-skills-review-blockers");
        _body.Add(CreateReviewButton(state, "creation-skills-review"));
    }

    private Button CreateReviewButton(CharacterCreationSkillsState state, string automationId)
    {
        Button review = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
            "Skills.ReviewDraft",
            "Review Skills draft"));
        review.AutomationId = automationId;
        review.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!Coordinator.IsCreationSkillsStateCurrent(state)) return;
            CharacterCreationSkillAllocation[] requestedSkills = _draft.Skills.ToArray();
            CharacterCreationSkillGroupAllocation[] requestedGroups = _draft.Groups.ToArray();
            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> result =
                await Task.Run(() => Coordinator.PreviewCreationSkills(
                    state.Binding,
                    requestedSkills,
                    requestedGroups));
            if (!CreationSkillsPhoneAuthority.CanAdoptPreview(
                    state, Coordinator.State, result, requestedSkills, requestedGroups)
                || result.Value is not { } preview)
            {
                _blockers = result.Blockers;
                return;
            }
            await Navigation.PushAsync(new CreationSkillsPreviewPage(
                Coordinator,
                preview,
                requestedSkills,
                requestedGroups,
                result =>
                {
                    if (result is { Outcome: CharacterCreationFoundationOutcomes.Success,
                            Receipt: { } receipt, RefreshedState: { } fresh, Blockers.Count: 0 }
                        && Coordinator.IsCreationSkillsReceiptCurrent(receipt)
                        && Coordinator.IsCreationSkillsStateCurrent(fresh))
                    {
                        _authority = fresh;
                        _revalidationAuthority = fresh;
                        _blockers = [];
                    }
                }));
        });
        return review;
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
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            _confirmation = await Coordinator.ConfirmCreationSkillsAsync(
                _preview,
                _allocations,
                _groups,
                _idempotencyKey);
            _onConfirmed?.Invoke(_confirmation);
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

        Button back = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
            "Common.BackToBuild",
            "Back to Build"));
        back.AutomationId = "creation-skills-back-to-build";
        back.Clicked += async (_, _) => await RunAsync(BackToBuildAsync);
        _body.Add(back);
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
