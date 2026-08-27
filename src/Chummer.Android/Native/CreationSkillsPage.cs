using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>Phone Skills step; every tap is accepted only after a full Core preview.</summary>
public sealed class CreationSkillsPage : NativePageBase
{
    private readonly CreationSkillsPhoneDraft _draft = new();
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _blockers = [];

    public CreationSkillsPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = CreationAllocationStrings.Get("Skills.PageTitle", "Skills");
        AutomationId = "creation-skills-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
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
        CharacterCreationFoundationResult<CharacterCreationSkillsState> load = Coordinator.LoadCreationSkills();
        if (load.Value is not { } state)
        {
            AddBlockers(load.Blockers.Count == 0 ? [CharacterCreationSkillsBlockers.AuthorityUnavailable] : load.Blockers);
            return;
        }
        _draft.Bind(state, Coordinator.State);
        AddBinding(state);
        CharacterCreationSkillsPreview? projection = _draft.Preview;
        AddBudget(projection?.ActiveSkillPointBudget ?? state.ActiveSkillPointBudget, "active");
        AddBudget(projection?.SkillGroupPointBudget ?? state.SkillGroupPointBudget, "groups");
        AddBudget(projection?.KnowledgeSkillPointBudget ?? state.KnowledgeSkillPointBudget, "knowledge");
        if (!CreationSkillsPhoneAuthority.IsReady(state, Coordinator.State) || !_draft.Matches(state, Coordinator.State))
        {
            AddBlockers(state.Blockers);
            return;
        }
        AddCatalog(
            state,
            state.Authority.ActiveSkills,
            CreationAllocationStrings.Get("Skills.ActiveSkills", "Active skills"));
        AddGroups(state);
        AddCatalog(
            state,
            state.Authority.KnowledgeSkills,
            CreationAllocationStrings.Get("Skills.KnowledgeLanguages", "Knowledge & languages"));
        if (_blockers.Count > 0) AddBlockers(_blockers);
        AddReview(state);
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

    private void AddBudget(CharacterCreationBudgetState budget, string token)
    {
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(budget.Label));
        card.Add(NativeTheme.Title(CreationAllocationStrings.Format(
            "Skills.BudgetLeft",
            "{0} left",
            budget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)), 20));
        card.Add(NativeTheme.Body(
            CreationAllocationStrings.Format(
                "Skills.BudgetUsed",
                "{0} / {1} points",
                budget.Used.ToString("0.##", CultureInfo.InvariantCulture),
                budget.Total.ToString("0.##", CultureInfo.InvariantCulture)),
            NativeTheme.Muted));
        Border border = NativeTheme.Card(card);
        border.AutomationId = $"creation-skills-budget-{token}";
        _body.Add(border);
    }

    private void AddCatalog(CharacterCreationSkillsState state,
        IReadOnlyList<CharacterCreationSkillCatalogEntry> catalog, string title)
    {
        _body.Add(NativeTheme.Eyebrow(title));
        foreach (CharacterCreationSkillCatalogEntry source in catalog)
        {
            CharacterCreationSkillAllocation? selected = _draft.Skills.SingleOrDefault(item =>
                item.Kind == source.Kind && item.SourceSkillId == source.SourceSkillId);
            VerticalStackLayout card = new() { Spacing = 7 };
            card.Add(NativeTheme.Title(source.Name, 18));
            card.Add(NativeTheme.Body(CreationAllocationStrings.Format(
                    "Skills.SkillDetail",
                    "{0} · {1} · rating {2}",
                    source.Category,
                    source.DefaultAttribute,
                    selected?.IsNativeLanguage == true
                        ? CreationAllocationStrings.Get("Skills.NativeValue", "native")
                        : (selected?.Rating ?? 0).ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Muted));
            HorizontalStackLayout controls = new() { Spacing = 8 };
            Button minus = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
                "Common.Decrease",
                "−"));
            minus.IsEnabled = selected is { IsNativeLanguage: false, Rating: > 0 };
            minus.Clicked += (_, _) => Preview(state, _draft.WithSkill(source, -1), _draft.Groups);
            Button plus = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
                "Common.Increase",
                "+"));
            plus.Clicked += (_, _) => Preview(state, _draft.WithSkill(source, 1), _draft.Groups);
            controls.Add(minus); controls.Add(plus);
            if (source.CanBeNativeLanguage)
            {
                Button native = NativeTheme.SecondaryButton(selected?.IsNativeLanguage == true
                    ? CreationAllocationStrings.Get("Skills.RemoveNative", "Remove native")
                    : CreationAllocationStrings.Get("Skills.NativeAction", "Native"));
                native.Clicked += (_, _) => Preview(state,
                    selected?.IsNativeLanguage == true
                        ? _draft.Skills.Where(item => item.SourceSkillId != source.SourceSkillId).ToArray()
                        : _draft.WithSkill(source, 0, native: true),
                    _draft.Groups);
                controls.Add(native);
            }
            card.Add(controls);
            if (source.Specializations.Count > 0 && selected is { Rating: > 0, IsNativeLanguage: false })
            {
                FlexLayout specs = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                foreach (CharacterCreationSkillSpecializationOption option in source.Specializations.Take(6))
                {
                    Button button = NativeTheme.SecondaryButton(option.Name);
                    button.Clicked += (_, _) => Preview(state, _draft.WithSpecialization(source, option.OptionId), _draft.Groups);
                    specs.Add(button);
                }
                card.Add(specs);
            }
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-skill-{Token(source.SourceSkillId)}";
            _body.Add(border);
        }
    }

    private void AddGroups(CharacterCreationSkillsState state)
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Skills.SkillGroups",
            "Skill groups")));
        foreach (CharacterCreationSkillGroupCatalogEntry source in state.Authority.SkillGroups)
        {
            CharacterCreationSkillGroupAllocation? selected = _draft.Groups.SingleOrDefault(item => item.GroupId == source.GroupId);
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(source.Name, 18));
            card.Add(NativeTheme.Body(CreationAllocationStrings.Format(
                "Skills.GroupDetail",
                "Rating {0} · {1} skills",
                selected?.Rating ?? 0,
                source.MemberSkillSourceIds.Count), NativeTheme.Muted));
            HorizontalStackLayout controls = new() { Spacing = 8 };
            Button minus = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
                "Common.Decrease",
                "−"));
            minus.IsEnabled = selected?.Rating > 0;
            minus.Clicked += (_, _) => Preview(state, _draft.Skills, _draft.WithGroup(source, -1));
            Button plus = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
                "Common.Increase",
                "+"));
            plus.Clicked += (_, _) => Preview(state, _draft.Skills, _draft.WithGroup(source, 1));
            controls.Add(minus); controls.Add(plus); card.Add(controls);
            _body.Add(NativeTheme.Card(card));
        }
    }

    private void Preview(CharacterCreationSkillsState state,
        IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        CharacterCreationFoundationResult<CharacterCreationSkillsPreview> result =
            Coordinator.PreviewCreationSkills(state.Binding, skills, groups);
        _blockers = result.Blockers;
        _draft.TryAdopt(state, Coordinator.State, result, skills, groups);
        Refresh();
    }

    private void AddReview(CharacterCreationSkillsState state)
    {
        Button review = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
            "Skills.ReviewDraft",
            "Review Skills draft"));
        review.AutomationId = "creation-skills-review";
        review.Clicked += async (_, _) => await RunAsync(async () =>
        {
            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> result =
                Coordinator.PreviewCreationSkills(state.Binding, _draft.Skills, _draft.Groups);
            if (!CreationSkillsPhoneAuthority.CanAdoptPreview(
                    state, Coordinator.State, result, _draft.Skills, _draft.Groups)
                || result.Value is not { } preview)
            {
                _blockers = result.Blockers;
                return;
            }
            await Navigation.PushAsync(new CreationSkillsPreviewPage(
                Coordinator,
                preview,
                _draft.Skills,
                _draft.Groups));
        });
        _body.Add(review);
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.CoreBlockers",
            "Core blockers")));
        foreach (string blocker in blockers) card.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
        _body.Add(NativeTheme.Card(card));
    }

    private static string Token(string value) => new(value.ToLowerInvariant()
        .Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}

/// <summary>Immutable Core preview followed by one explicit digest-bound confirmation.</summary>
public sealed class CreationSkillsPreviewPage : NativePageBase
{
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

    internal CreationSkillsPreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationSkillsPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups) : base(coordinator)
    {
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
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.ExplicitReview",
            "Explicit review")));
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get(
            "SkillsPreview.Heading",
            "Skills allocation")));
        Label binding = NativeTheme.Body(
            CreationAllocationStrings.Format(
                "Common.PreviewBinding",
                "Revision {0} · saved {1} · preview {2}",
                _preview.Binding.ContentRevision,
                _preview.Binding.SavedRevision,
                CreationPrerequisiteDigestText.CanonicalPrefix(_preview.PreviewDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-skills-preview-binding";
        _body.Add(binding);
        AddDigest("creation-skills-preview-digest", _preview.PreviewDigest);
        AddDigest("creation-skills-preview-raw-character-xml-digest", _preview.Binding.RawCharacterXmlDigest);
        AddDigest("creation-skills-preview-auxiliary-state-digest", _preview.Binding.AuxiliaryStateDigest);
        AddBudgets();
        AddSelections();
        AddBlockers();
        AddConfirmation();
        AddReceipt();
    }

    private void AddBudgets()
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "SkillsPreview.FinalCoreLedgers",
            "Final Core ledgers")));
        foreach (CharacterCreationBudgetState budget in new[]
                 {
                     _preview.ActiveSkillPointBudget,
                     _preview.SkillGroupPointBudget,
                     _preview.KnowledgeSkillPointBudget
                 })
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(budget.Label, 18));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.Total", "Total"),
                budget.Total.ToString("0.##", CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.Used", "Used"),
                budget.Used.ToString("0.##", CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.Remaining", "Remaining"),
                budget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)));
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-skills-preview-budget-{Token(budget.BudgetId)}";
            _body.Add(border);
        }
    }

    private void AddSelections()
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "SkillsPreview.TypedSelections",
            "Typed selections")));
        foreach (CharacterCreationSkillProjection skill in _preview.Skills)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(skill.Name, 18));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("SkillsPreview.Kind", "Kind"),
                skill.Kind));
            card.Add(NativeTheme.Metric(CreationAllocationStrings.Get("SkillsPreview.Rating", "Rating"), skill.IsNativeLanguage
                ? CreationAllocationStrings.Get("Skills.NativeValue", "native")
                : skill.Rating.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("SkillsPreview.PointCost", "Point cost"),
                skill.PointCost.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrWhiteSpace(skill.SpecializationName))
                card.Add(NativeTheme.Metric(
                    CreationAllocationStrings.Get("SkillsPreview.Specialization", "Specialization"),
                    skill.SpecializationName));
            _body.Add(NativeTheme.Card(card));
        }
        foreach (CharacterCreationSkillGroupProjection group in _preview.SkillGroups)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(group.Name, 18));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("SkillsPreview.GroupRating", "Group rating"),
                group.Rating.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("SkillsPreview.PointCost", "Point cost"),
                group.PointCost.ToString(CultureInfo.InvariantCulture)));
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
            "Common.CoreBlockers",
            "Core blockers")));
        foreach (string blocker in blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        _body.Add(NativeTheme.Card(card));
    }

    private void AddConfirmation()
    {
        if (_confirmation is
            {
                Outcome: CharacterCreationFoundationOutcomes.Success,
                Receipt: not null,
                RefreshedState: not null
            })
        {
            bool refreshRequired = _confirmation.Blockers.Contains(
                CharacterCreationSkillsBlockers.PostCommitRefreshRequired,
                StringComparer.Ordinal);
            Label complete = NativeTheme.Body(refreshRequired
                ? CreationAllocationStrings.Get(
                    "SkillsPreview.ConfirmedRefreshRequired",
                    "Skills draft is durably confirmed. Reopen the character to refresh this phone view.")
                : CreationAllocationStrings.Get(
                    "SkillsPreview.Confirmed",
                    "Skills draft confirmed and authoritative state reloaded."),
                refreshRequired ? NativeTheme.Danger : NativeTheme.Text);
            complete.AutomationId = "creation-skills-confirmed";
            _body.Add(NativeTheme.Card(complete));
            return;
        }

        CharacterCreationFoundationResult<CharacterCreationSkillsState> live = Coordinator.LoadCreationSkills();
        bool canConfirm = live.Value is { } state
                          && CreationSkillsPhoneAuthority.CanConfirmPreview(
                              state, Coordinator.State, _preview, _allocations, _groups);
        Button confirm = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
            "SkillsPreview.Confirm",
            "Confirm Skills draft"));
        confirm.AutomationId = "creation-skills-confirm";
        confirm.IsEnabled = canConfirm;
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            _confirmation = await Coordinator.ConfirmCreationSkillsAsync(
                _preview,
                _allocations,
                _groups,
                _idempotencyKey);
        });
        _body.Add(confirm);
        Label explicitAction = NativeTheme.Body(
            CreationAllocationStrings.Get(
                "SkillsPreview.ConfirmationBoundary",
                "Confirmation is bound to this exact Core preview and uses a deterministic retry key after restart."),
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
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "SkillsPreview.AtomicReceipt",
            "Atomic Skills draft receipt")));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.PreviousRevision", "Previous revision"),
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.ContentRevision", "Content revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.SavedRevision", "Saved revision"),
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.DraftRevision", "Draft revision"),
            receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("SkillsPreview.ActivePointsRemaining", "Active points remaining"),
            receipt.ActivePointsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("SkillsPreview.GroupPointsRemaining", "Group points remaining"),
            receipt.SkillGroupPointsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("SkillsPreview.KnowledgePointsRemaining", "Knowledge points remaining"),
            receipt.KnowledgePointsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.CharacterDocumentChanged", "Character document changed"),
            receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        AddReceiptDigest(card, "creation-skills-receipt-digest", receipt.ReceiptDigest);
        AddReceiptDigest(card, "creation-skills-receipt-draft-digest", receipt.DraftDigest);
        AddReceiptDigest(card, "creation-skills-receipt-raw-character-xml-digest", refreshed.Binding.RawCharacterXmlDigest);
        card.Add(NativeTheme.Body(
            refreshed.PendingDraft?.CharacterEffectsApplied == false
                ? CreationAllocationStrings.Get(
                    "SkillsPreview.DurablePendingFinalization",
                    "Typed Skills are durable; character effects remain pending finalization.")
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
        back.Clicked += async (_, _) => await BackToBuildAsync();
        _body.Add(back);
    }

    private async Task BackToBuildAsync()
    {
        await Navigation.PopAsync(animated: false);
        if (Navigation.NavigationStack.LastOrDefault() is CreationSkillsPage)
            await Navigation.PopAsync();
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _body.Add(label);
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
