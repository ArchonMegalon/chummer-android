using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Deep-navigation prompt for the exact, digest-bound skill or skill-group choices projected by
/// Core for one selected Talent. It never derives options or mutates character XML.
/// </summary>
public sealed class CreationTalentSkillGrantPage : NativePageBase
{
    private readonly CreationPrerequisitePhoneDraft _draft;
    private readonly string _talentSelectionId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationTalentSkillGrantPage(
        RunnerSessionCoordinator coordinator,
        CreationPrerequisitePhoneDraft draft,
        string talentSelectionId) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _talentSelectionId = string.IsNullOrWhiteSpace(talentSelectionId)
            ? throw new ArgumentException("A Core-projected Talent selection is required.", nameof(talentSelectionId))
            : talentSelectionId;
        Title = "Choose granted skills";
        AutomationId = "creation-prerequisite-talent-grant-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Core-projected Talent grant"));
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> load =
            Coordinator.LoadCreationPrerequisite();
        if (!string.Equals(
                load.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || load.Value is not { } state
            || !_draft.Matches(state, Coordinator.State))
        {
            AddStaleRecovery(load.Blockers.Count > 0
                ? load.Blockers
                : [CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision]);
            return;
        }

        CharacterCreationPriorityTalentOptionProjection? talent =
            _draft.SelectedTalent(state, Coordinator.State);
        if (talent is null
            || !string.Equals(talent.SelectionId, _talentSelectionId, StringComparison.Ordinal))
        {
            AddStaleRecovery([CharacterCreationPrerequisiteBlockers.SelectionIncomplete]);
            return;
        }

        _body.Add(NativeTheme.Title(talent.Name));
        IReadOnlyList<string> authorityBlockers =
            CreationPrerequisitePhoneAuthority.TalentGrantAuthorityBlockers(talent);
        if (authorityBlockers.Count > 0)
        {
            AddBlockers(authorityBlockers, "creation-prerequisite-talent-grant-blockers");
            return;
        }

        if (talent.ActiveSkillGrant is { } activeGrant)
            AddActiveSkillGrant(state, talent, activeGrant);
        else if (talent.SkillGroupGrant is { } groupGrant)
            AddSkillGroupGrant(state, talent, groupGrant);
        else
            AddStaleRecovery([
                CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported
            ]);
    }

    private void AddActiveSkillGrant(
        CharacterCreationPrerequisiteState state,
        CharacterCreationPriorityTalentOptionProjection talent,
        CharacterCreationTalentActiveSkillGrantProjection grant)
    {
        IReadOnlyList<string> selected = _draft.TalentActiveSkillSelectionIds(
            state,
            Coordinator.State);
        AddGrantAuthority(
            "Active skills",
            grant.Quantity,
            grant.BaseRating,
            grant.SkillType,
            grant.ImprovementKind,
            grant.GrantDigest,
            grant.SourceAnchorIds,
            selected.Count);

        foreach (CharacterCreationTalentActiveSkillChoiceProjection choice in grant.Options
                     .OrderBy(option => option.CanonicalName, StringComparer.Ordinal)
                     .ThenBy(option => option.SelectionId, StringComparer.Ordinal))
        {
            int selectedIndex = IndexOf(selected, choice.SelectionId);
            bool isSelected = selectedIndex >= 0;
            bool canAdd = selected.Count < grant.Quantity
                          && choice.IsEnabled
                          && !choice.IsExotic
                          && choice.Blockers.Count == 0;
            string detail = JoinDetails(
                isSelected ? $"Selected slot {(selectedIndex + 1).ToString(CultureInfo.InvariantCulture)}" : null,
                choice.Category,
                string.IsNullOrWhiteSpace(choice.SkillGroup) ? null : $"Group {choice.SkillGroup}",
                string.IsNullOrWhiteSpace(choice.Attribute) ? null : $"Attribute {choice.Attribute}",
                choice.IsExotic
                    ? CharacterCreationPrerequisiteBlockers
                        .TalentExoticSkillSpecializationRequired
                    : null,
                choice.Blockers.Count > 0 ? string.Join(" · ", choice.Blockers) : null,
                $"Source {choice.SourceId}",
                $"Anchors {string.Join(" · ", choice.SourceAnchorIds)}");
            _body.Add(NativeTheme.NavigationRow(
                isSelected ? $"✓ {choice.CanonicalName}" : choice.CanonicalName,
                detail,
                () => ToggleActiveAsync(state, choice.SelectionId),
                enabled: isSelected || canAdd,
                automationId:
                    $"creation-prerequisite-talent-active-skill-option-{Token(choice.SelectionId)}"));
        }

        AddCompletion(
            CreationPrerequisitePhoneAuthority.TalentGrantSelectionsComplete(
                talent,
                selected,
                []),
            selected.Count,
            grant.Quantity);
    }

    private void AddSkillGroupGrant(
        CharacterCreationPrerequisiteState state,
        CharacterCreationPriorityTalentOptionProjection talent,
        CharacterCreationTalentSkillGroupGrantProjection grant)
    {
        IReadOnlyList<string> selected = _draft.TalentSkillGroupSelectionIds(
            state,
            Coordinator.State);
        AddGrantAuthority(
            "Skill groups",
            grant.Quantity,
            grant.BaseRating,
            grant.SkillGroupType,
            grant.ImprovementKind,
            grant.GrantDigest,
            grant.SourceAnchorIds,
            selected.Count);

        foreach (CharacterCreationTalentSkillGroupChoiceProjection choice in grant.Options
                     .OrderBy(option => option.CanonicalName, StringComparer.Ordinal)
                     .ThenBy(option => option.SelectionId, StringComparer.Ordinal))
        {
            int selectedIndex = IndexOf(selected, choice.SelectionId);
            bool isSelected = selectedIndex >= 0;
            string detail = JoinDetails(
                isSelected ? $"Selected slot {(selectedIndex + 1).ToString(CultureInfo.InvariantCulture)}" : null,
                $"Members {choice.MemberSkillSourceIds.Count.ToString(CultureInfo.InvariantCulture)}",
                $"Group digest {ShortDigest(choice.GroupDigest)}",
                $"Anchors {string.Join(" · ", choice.SourceAnchorIds)}");
            _body.Add(NativeTheme.NavigationRow(
                isSelected ? $"✓ {choice.CanonicalName}" : choice.CanonicalName,
                detail,
                () => ToggleGroupAsync(state, choice.SelectionId),
                enabled: isSelected || selected.Count < grant.Quantity,
                automationId:
                    $"creation-prerequisite-talent-skill-group-option-{Token(choice.SelectionId)}"));
        }

        AddCompletion(
            CreationPrerequisitePhoneAuthority.TalentGrantSelectionsComplete(
                talent,
                [],
                selected),
            selected.Count,
            grant.Quantity);
    }

    private void AddGrantAuthority(
        string kind,
        int quantity,
        int rating,
        string selectorType,
        string improvementKind,
        string grantDigest,
        IReadOnlyList<string> anchors,
        int selectedCount)
    {
        string requiredAuthority =
            $"{selectedCount.ToString(CultureInfo.InvariantCulture)} / " +
            $"{quantity.ToString(CultureInfo.InvariantCulture)} {kind}";
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(
            "Required",
            requiredAuthority));
        card.Add(NativeTheme.Metric("Granted rating", rating.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Selector", selectorType));
        card.Add(NativeTheme.Metric("Improvement", improvementKind));
        Label digest = NativeTheme.Body(grantDigest, NativeTheme.Muted);
        digest.AutomationId = "creation-prerequisite-talent-grant-digest";
        card.Add(digest);
        foreach (string anchor in anchors)
            card.Add(NativeTheme.Body($"Source anchor · {anchor}", NativeTheme.Muted));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-talent-grant-authority";
        SemanticProperties.SetDescription(border, requiredAuthority);
        _body.Add(border);
    }

    private async Task ToggleActiveAsync(
        CharacterCreationPrerequisiteState state,
        string selectionId)
    {
        _draft.TryToggleTalentActiveSkill(state, Coordinator.State, selectionId);
        await Task.CompletedTask;
        Refresh();
    }

    private async Task ToggleGroupAsync(
        CharacterCreationPrerequisiteState state,
        string selectionId)
    {
        _draft.TryToggleTalentSkillGroup(state, Coordinator.State, selectionId);
        await Task.CompletedTask;
        Refresh();
    }

    private void AddCompletion(bool complete, int selectedCount, int requiredCount)
    {
        Button done = NativeTheme.PrimaryButton(
            complete
                ? "Continue with exact grant"
                : $"Choose {requiredCount - selectedCount} more");
        done.AutomationId = "creation-prerequisite-talent-grant-complete";
        done.IsEnabled = complete;
        done.Clicked += async (_, _) => await Navigation.PopAsync(animated: false);
        _body.Add(done);
    }

    private void AddStaleRecovery(IReadOnlyList<string> blockers)
    {
        AddBlockers(blockers, "creation-prerequisite-talent-grant-stale");
        Button back = NativeTheme.SecondaryButton("Return to refreshed Talent choices");
        back.AutomationId = "creation-prerequisite-talent-grant-recover";
        back.Clicked += async (_, _) => await Navigation.PopAsync(animated: false);
        _body.Add(back);
    }

    private void AddBlockers(IReadOnlyList<string> blockers, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Blockers"));
        foreach (string blocker in blockers.Where(value => !string.IsNullOrWhiteSpace(value)))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        _body.Add(border);
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.Ordinal))
                return index;
        }
        return -1;
    }

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest) ? "unavailable" : digest[..Math.Min(12, digest.Length)];

    private static string JoinDetails(params string?[] parts)
        => string.Join(
            " · ",
            parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!));

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
