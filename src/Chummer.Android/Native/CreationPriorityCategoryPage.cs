using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Phone deep-navigation list of exact Core-projected ranks for one ordered category.
/// </summary>
public sealed class CreationPriorityCategoryPage : NativePageBase
{
    private readonly CreationPrerequisitePhoneDraft _draft;
    private readonly CharacterCreationPrerequisiteState _state;
    private readonly string _categoryId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationPriorityCategoryPage(
        RunnerSessionCoordinator coordinator,
        CreationPrerequisitePhoneDraft draft,
        CharacterCreationPrerequisiteState state,
        string categoryId) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _categoryId = CharacterCreationPriorityCategoryIds.Ordered.Contains(
            categoryId,
            StringComparer.Ordinal)
            ? categoryId
            : throw new ArgumentException("A typed Priority category ID is required.", nameof(categoryId));
        Title = WizardStrings.Get("Priority.CategoryPage.PageTitle", "Choose rank");
        AutomationId = "creation-prerequisite-category-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.CategoryPage.Eyebrow", "Priority assignments")));
        string categoryFallback = RunnerSessionCoordinator.HumanizeId(_categoryId);
        _body.Add(NativeTheme.Title(WizardStrings.PriorityCategory(_categoryId, categoryFallback)));

        if (!_draft.Matches(_state, Coordinator.State))
        {
            AddBlockers([CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision]);
            return;
        }

        Label binding = NativeTheme.Body(
            WizardStrings.Format(
                "Priority.CategoryPage.Binding",
                "Revision {0} · snapshot {1}",
                _state.Binding.ContentRevision,
                ShortDigest(_state.SnapshotDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-prerequisite-category-binding";
        _body.Add(binding);

        IReadOnlyList<CreationPrerequisitePhoneRankOption> options =
            _draft.OptionsForCategory(_state, Coordinator.State, _categoryId);
        if (options.Count == 0)
        {
            AddBlockers([CharacterCreationPrerequisiteBlockers.AuthorityUnavailable]);
            return;
        }

        CharacterCreationPriorityOptionProjection? selected =
            _draft.SelectedOption(_state, Coordinator.State, _categoryId);
        foreach (CreationPrerequisitePhoneRankOption option in options)
        {
            CharacterCreationPriorityOptionProjection projection = option.Projection;
            bool isSelected = selected is not null
                              && string.Equals(selected.Rank, projection.Rank, StringComparison.Ordinal);
            string detail = JoinDetails(
                isSelected ? WizardStrings.Get("Priority.CategoryPage.CurrentSelection", "Current draft selection") : null,
                projection.Label,
                WizardStrings.Format(
                    "Priority.CategoryPage.SumValue",
                    "Sum value {0}",
                    projection.SumToTenValue.ToString(CultureInfo.InvariantCulture)),
                projection.BaseNormalAttributePoints is int raw
                    ? WizardStrings.Format(
                        "Priority.CategoryPage.RawGrant",
                        "Raw normal Attribute grant {0}",
                        raw.ToString(CultureInfo.InvariantCulture))
                    : null,
                WizardStrings.Format("Common.Source", "Source {0}", projection.SourceId),
                WizardStrings.Format("Common.Node", "Node {0}", ShortDigest(projection.SourceNodeDigest)),
                projection.SourceAnchorIds.Count > 0
                    ? WizardStrings.Format(
                        "Common.Anchors",
                        "Anchors {0}",
                        string.Join(" · ", projection.SourceAnchorIds))
                    : null,
                option.DisableReason);
            _body.Add(NativeTheme.NavigationRow(
                WizardStrings.Format("Common.Rank", "Rank {0}", projection.Rank),
                detail,
                () => SelectAsync(projection.Rank),
                option.IsEnabled,
                $"creation-prerequisite-rank-{Token(_categoryId)}-{Token(projection.Rank)}"));
        }
    }

    private async Task SelectAsync(string rank)
    {
        if (!_draft.TrySelect(_state, Coordinator.State, _categoryId, rank))
        {
            Refresh();
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
        border.AutomationId = "creation-prerequisite-category-blockers";
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
