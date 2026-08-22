using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Phone deep-navigation list of exact Core-projected ranks for one ordered category.
/// </summary>
public sealed class CreationPriorityCategoryPage : NativePageBase
{
    private readonly CreationPrerequisitePhoneDraft _draft;
    private readonly string _categoryId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationPriorityCategoryPage(
        RunnerSessionCoordinator coordinator,
        CreationPrerequisitePhoneDraft draft,
        string categoryId) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _categoryId = CharacterCreationPriorityCategoryIds.Ordered.Contains(
            categoryId,
            StringComparer.Ordinal)
            ? categoryId
            : throw new ArgumentException("A typed Priority category ID is required.", nameof(categoryId));
        Title = "Choose rank";
        AutomationId = "creation-prerequisite-category-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Priority assignments"));
        _body.Add(NativeTheme.Title(RunnerSessionCoordinator.HumanizeId(_categoryId)));

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

        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · snapshot {ShortDigest(state.SnapshotDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-prerequisite-category-binding";
        _body.Add(binding);

        IReadOnlyList<CreationPrerequisitePhoneRankOption> options =
            _draft.OptionsForCategory(state, Coordinator.State, _categoryId);
        if (options.Count == 0)
        {
            AddBlockers([CharacterCreationPrerequisiteBlockers.AuthorityUnavailable]);
            return;
        }

        CharacterCreationPriorityOptionProjection? selected =
            _draft.SelectedOption(state, Coordinator.State, _categoryId);
        foreach (CreationPrerequisitePhoneRankOption option in options)
        {
            CharacterCreationPriorityOptionProjection projection = option.Projection;
            bool isSelected = selected is not null
                              && string.Equals(selected.Rank, projection.Rank, StringComparison.Ordinal);
            string detail = JoinDetails(
                isSelected ? "Current draft selection" : null,
                projection.Label,
                $"Sum value {projection.SumToTenValue.ToString(CultureInfo.InvariantCulture)}",
                projection.BaseNormalAttributePoints is int raw
                    ? $"Raw normal Attribute grant {raw.ToString(CultureInfo.InvariantCulture)}"
                    : null,
                $"Source {projection.SourceId}",
                $"Node {ShortDigest(projection.SourceNodeDigest)}",
                projection.SourceAnchorIds.Count > 0
                    ? $"Anchors {string.Join(" · ", projection.SourceAnchorIds)}"
                    : null,
                option.DisableReason);
            _body.Add(NativeTheme.NavigationRow(
                $"Rank {projection.Rank}",
                detail,
                () => SelectAsync(state, projection.Rank),
                option.IsEnabled,
                $"creation-prerequisite-rank-{Token(_categoryId)}-{Token(projection.Rank)}"));
        }
    }

    private async Task SelectAsync(CharacterCreationPrerequisiteState state, string rank)
    {
        if (!_draft.TrySelect(state, Coordinator.State, _categoryId, rank))
        {
            Refresh();
            return;
        }
        await Navigation.PopAsync(animated: false);
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Blockers"));
        foreach (string blocker in blockers.Where(static value => !string.IsNullOrWhiteSpace(value)))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-category-blockers";
        _body.Add(border);
    }

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest) ? "unavailable" : digest[..Math.Min(12, digest.Length)];

    private static string JoinDetails(params string?[] parts)
        => string.Join(
            " · ",
            parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!));

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
