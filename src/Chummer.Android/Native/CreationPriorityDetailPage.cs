using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Phone deep-navigation list for the exact Heritage or Talent choices nested under the selected
/// Core Priority rank. The phone never reads legacy XML or derives rules values.
/// </summary>
public sealed class CreationPriorityDetailPage : NativePageBase
{
    private readonly CreationPrerequisitePhoneDraft _draft;
    private readonly string _categoryId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationPriorityDetailPage(
        RunnerSessionCoordinator coordinator,
        CreationPrerequisitePhoneDraft draft,
        string categoryId) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _categoryId = categoryId is (CharacterCreationPriorityCategoryIds.Heritage
            or CharacterCreationPriorityCategoryIds.Talent)
            ? categoryId
            : throw new ArgumentException(
                "An authority-projected Heritage or Talent category is required.",
                nameof(categoryId));
        Title = string.Equals(
            _categoryId,
            CharacterCreationPriorityCategoryIds.Heritage,
            StringComparison.Ordinal)
            ? WizardStrings.Get("Priority.DetailPage.ChooseHeritage", "Choose heritage")
            : WizardStrings.Get("Priority.DetailPage.ChooseTalent", "Choose talent");
        AutomationId = $"creation-prerequisite-{Token(_categoryId)}-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.DetailPage.Eyebrow", "Core-projected choice")));
        string categoryFallback = RunnerSessionCoordinator.HumanizeId(_categoryId);
        _body.Add(NativeTheme.Title(WizardStrings.PriorityCategory(_categoryId, categoryFallback)));

        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> load =
            Coordinator.LoadCreationPrerequisite();
        if (!string.Equals(
                load.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || load.Value is not { } state
            || !_draft.Matches(state, Coordinator.State))
        {
            AddBlockers(load.Blockers.Count > 0
                ? load.Blockers
                : [CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision]);
            return;
        }

        CharacterCreationPriorityOptionProjection? rank =
            _draft.SelectedOption(state, Coordinator.State, _categoryId);
        if (rank is null)
        {
            AddBlockers([CharacterCreationPrerequisiteBlockers.SelectionIncomplete]);
            return;
        }

        Label binding = NativeTheme.Body(
            WizardStrings.Format(
                "Priority.DetailPage.Binding",
                "Rank {0} · source {1} · snapshot {2}",
                rank.Rank,
                rank.SourceId,
                ShortDigest(state.SnapshotDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-prerequisite-detail-binding";
        _body.Add(binding);

        if (string.Equals(
                _categoryId,
                CharacterCreationPriorityCategoryIds.Heritage,
                StringComparison.Ordinal))
        {
            AddHeritageOptions(state);
        }
        else
        {
            AddTalentOptions(state);
        }
    }

    private void AddHeritageOptions(CharacterCreationPrerequisiteState state)
    {
        IReadOnlyList<CharacterCreationPriorityHeritageOptionProjection> options =
            _draft.HeritageOptions(state, Coordinator.State);
        if (options.Count == 0)
        {
            AddBlockers([CharacterCreationPrerequisiteBlockers.AuthorityUnavailable]);
            return;
        }

        CharacterCreationPriorityHeritageOptionProjection? selected =
            _draft.SelectedHeritage(state, Coordinator.State);
        foreach (CharacterCreationPriorityHeritageOptionProjection option in options)
        {
            bool isSelected = selected is not null
                              && string.Equals(
                                  selected.SelectionId,
                                  option.SelectionId,
                                  StringComparison.Ordinal);
            string title = string.IsNullOrWhiteSpace(option.MetavariantName)
                ? option.MetatypeName
                : $"{option.MetatypeName} · {option.MetavariantName}";
            string detail = JoinDetails(
                isSelected ? WizardStrings.Get("Priority.DetailPage.CurrentSelection", "Current typed draft selection") : null,
                WizardStrings.PriorityHeritageKind(
                    option.Kind,
                    RunnerSessionCoordinator.HumanizeId(option.Kind)),
                WizardStrings.Format(
                    "Common.SpecialAttributePoints",
                    "Special Attribute points {0}",
                    option.SpecialAttributePoints.ToString(CultureInfo.InvariantCulture)),
                WizardStrings.Format(
                    "Priority.DetailPage.Karma",
                    "Karma {0}",
                    option.KarmaCost.ToString(CultureInfo.InvariantCulture)),
                option.HalvesNormalAttributePoints
                    ? WizardStrings.Get("Priority.DetailPage.HalvesAttributes", "Halves normal Attribute points")
                    : null,
                option.Blockers.Count > 0 ? string.Join(" · ", option.Blockers) : null,
                option.SourceAnchorIds.Count > 0
                    ? WizardStrings.Format(
                        "Common.Anchors",
                        "Anchors {0}",
                        string.Join(" · ", option.SourceAnchorIds))
                    : null);
            _body.Add(NativeTheme.NavigationRow(
                title,
                detail,
                () => SelectHeritageAsync(state, option.SelectionId),
                option.IsEnabled && option.Blockers.Count == 0,
                $"creation-prerequisite-heritage-option-{Token(option.SelectionId)}"));
        }
    }

    private void AddTalentOptions(CharacterCreationPrerequisiteState state)
    {
        IReadOnlyList<CharacterCreationPriorityTalentOptionProjection> options =
            _draft.TalentOptions(state, Coordinator.State);
        if (options.Count == 0)
        {
            AddBlockers([CharacterCreationPrerequisiteBlockers.AuthorityUnavailable]);
            return;
        }

        CharacterCreationPriorityTalentOptionProjection? selected =
            _draft.SelectedTalent(state, Coordinator.State);
        foreach (CharacterCreationPriorityTalentOptionProjection option in options)
        {
            bool isSelected = selected is not null
                              && string.Equals(
                                  selected.SelectionId,
                                  option.SelectionId,
                                  StringComparison.Ordinal);
            IReadOnlyList<string> grantBlockers =
                CreationPrerequisitePhoneAuthority.TalentGrantAuthorityBlockers(option);
            int selectedGrantCount = isSelected
                ? option.ActiveSkillGrant is not null
                    ? _draft.TalentActiveSkillSelectionIds(state, Coordinator.State).Count
                    : _draft.TalentSkillGroupSelectionIds(state, Coordinator.State).Count
                : 0;
            int? requiredGrantCount = option.ActiveSkillGrant?.Quantity
                                      ?? option.SkillGroupGrant?.Quantity;
            string detail = JoinDetails(
                isSelected ? WizardStrings.Get("Priority.DetailPage.CurrentSelection", "Current typed draft selection") : null,
                option.Value,
                WizardStrings.Format(
                    "Common.SpecialAttributePoints",
                    "Special Attribute points {0}",
                    option.SpecialAttributePoints.ToString(CultureInfo.InvariantCulture)),
                option.GrantedQualities.Count > 0
                    ? WizardStrings.Format(
                        "Priority.DetailPage.GrantedQualities",
                        "Granted qualities {0}",
                        string.Join(" · ", option.GrantedQualities))
                    : null,
                option.ActiveSkillGrant is { } activeGrant
                    ? WizardStrings.Format(
                        "Priority.DetailPage.ChooseActiveSkills",
                        "Choose {0} active skills at rating {1}",
                        activeGrant.Quantity.ToString(CultureInfo.InvariantCulture),
                        activeGrant.BaseRating.ToString(CultureInfo.InvariantCulture))
                    : option.SkillGroupGrant is { } groupGrant
                        ? WizardStrings.Format(
                            "Priority.DetailPage.ChooseSkillGroups",
                            "Choose {0} skill groups at rating {1}",
                            groupGrant.Quantity.ToString(CultureInfo.InvariantCulture),
                            groupGrant.BaseRating.ToString(CultureInfo.InvariantCulture))
                        : null,
                requiredGrantCount is int required
                    ? WizardStrings.Format(
                        "Priority.DetailPage.GrantProgress",
                        "Grant progress {0} / {1}",
                        selectedGrantCount.ToString(CultureInfo.InvariantCulture),
                        required.ToString(CultureInfo.InvariantCulture))
                    : null,
                grantBlockers.Count > 0 ? string.Join(" · ", grantBlockers) : null,
                option.Blockers.Count > 0 ? string.Join(" · ", option.Blockers) : null,
                option.SourceAnchorIds.Count > 0
                    ? WizardStrings.Format(
                        "Common.Anchors",
                        "Anchors {0}",
                        string.Join(" · ", option.SourceAnchorIds))
                    : null);
            _body.Add(NativeTheme.NavigationRow(
                option.Name,
                detail,
                () => SelectTalentAsync(state, option.SelectionId),
                option.IsEnabled && option.Blockers.Count == 0 && grantBlockers.Count == 0,
                $"creation-prerequisite-talent-option-{Token(option.SelectionId)}"));
        }
    }

    private async Task SelectHeritageAsync(
        CharacterCreationPrerequisiteState state,
        string selectionId)
    {
        if (!_draft.TrySelectHeritage(state, Coordinator.State, selectionId))
        {
            Refresh();
            return;
        }
        await Navigation.PopAsync(animated: false);
    }

    private async Task SelectTalentAsync(
        CharacterCreationPrerequisiteState state,
        string selectionId)
    {
        if (!_draft.TrySelectTalent(state, Coordinator.State, selectionId))
        {
            Refresh();
            return;
        }
        CharacterCreationPriorityTalentOptionProjection? selected =
            _draft.SelectedTalent(state, Coordinator.State);
        if (selected?.ActiveSkillGrant is not null || selected?.SkillGroupGrant is not null)
        {
            await Navigation.PushAsync(new CreationTalentSkillGrantPage(
                Coordinator,
                _draft,
                selectionId));
            return;
        }
        await Navigation.PopAsync(animated: false);
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Common.Blockers", "Blockers")));
        foreach (string blocker in blockers.Where(static value => !string.IsNullOrWhiteSpace(value)))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-detail-blockers";
        _body.Add(border);
    }

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest)
            ? WizardStrings.Get("Common.Unavailable", "unavailable")
            : digest[..Math.Min(12, digest.Length)];

    private static string JoinDetails(params string?[] parts)
        => string.Join(
            " · ",
            parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!));

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
