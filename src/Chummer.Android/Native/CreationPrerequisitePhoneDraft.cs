using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed record CreationPrerequisitePhoneRankOption(
    CharacterCreationPriorityOptionProjection Projection,
    bool IsEnabled,
    string? DisableReason);

/// <summary>
/// Holds only the typed Priority/Sum-to-Ten choices for one exact Core snapshot. Core remains the
/// source of every option, cost, budget, preview, and persisted draft.
/// </summary>
internal sealed class CreationPrerequisitePhoneDraft
{
    internal const string PriorityRankExhausted =
        "creation-prerequisite-priority-profile-rank-exhausted";
    internal const string SumToTenTargetUnreachable =
        "creation-prerequisite-sum-to-ten-target-unreachable";

    private CharacterCreationPrerequisiteBinding? _binding;
    private string? _snapshotDigest;
    private string? _rulesetId;
    private string? _buildMethod;
    private readonly Dictionary<string, string> _assignments = new(StringComparer.Ordinal);

    public bool Bind(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(overview);
        if (Matches(state, overview))
            return false;

        _binding = state.Binding;
        _snapshotDigest = state.SnapshotDigest;
        _rulesetId = state.RulesetId;
        _buildMethod = state.BuildMethod;
        _assignments.Clear();
        RestorePendingDraft(state, overview);
        return true;
    }

    public bool Matches(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
        => _binding is not null
           && CreationPrerequisitePhoneAuthority.MatchesOverview(state, overview)
           && CreationPrerequisitePhoneAuthority.BindingEquals(_binding, state.Binding)
           && string.Equals(_snapshotDigest, state.SnapshotDigest, StringComparison.Ordinal)
           && string.Equals(_rulesetId, state.RulesetId, StringComparison.Ordinal)
           && string.Equals(_buildMethod, state.BuildMethod, StringComparison.Ordinal);

    public IReadOnlyDictionary<string, string> Assignments(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
        => Matches(state, overview)
            ? new Dictionary<string, string>(_assignments, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    public CharacterCreationPriorityOptionProjection? SelectedOption(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview,
        string categoryId)
        => Matches(state, overview)
           && _assignments.TryGetValue(categoryId, out string? rank)
            ? CreationPrerequisitePhoneAuthority.ResolveUniqueOption(state, categoryId, rank)
            : null;

    public IReadOnlyList<CreationPrerequisitePhoneRankOption> OptionsForCategory(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview,
        string categoryId)
    {
        if (!Matches(state, overview)
            || !CreationPrerequisitePhoneAuthority.IsReady(state, overview)
            || !CharacterCreationPriorityCategoryIds.Ordered.Contains(
                categoryId,
                StringComparer.Ordinal))
        {
            return [];
        }

        return state.Authority.Options
            .Where(option => string.Equals(option.CategoryId, categoryId, StringComparison.Ordinal))
            .OrderBy(option => RankOrder(state.Authority, option.Rank))
            .ThenBy(option => option.Rank, StringComparer.Ordinal)
            .Select(option =>
            {
                string? blocker = CandidateBlocker(state, categoryId, option.Rank);
                return new CreationPrerequisitePhoneRankOption(
                    option,
                    IsEnabled: blocker is null,
                    DisableReason: blocker);
            })
            .ToArray();
    }

    public bool TrySelect(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview,
        string categoryId,
        string rank)
    {
        CreationPrerequisitePhoneRankOption[] matches = OptionsForCategory(state, overview, categoryId)
            .Where(candidate => string.Equals(
                candidate.Projection.Rank,
                rank,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches is not [{ IsEnabled: true }])
            return false;

        _assignments[categoryId] = matches[0].Projection.Rank;
        return true;
    }

    public void Reset(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
    {
        if (Matches(state, overview))
            _assignments.Clear();
    }

    public bool CanPrepare(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
    {
        if (!Matches(state, overview)
            || !CreationPrerequisitePhoneAuthority.IsReady(state, overview)
            || _assignments.Count != CharacterCreationPriorityCategoryIds.Ordered.Count
            || CharacterCreationPriorityCategoryIds.Ordered.Any(category =>
                SelectedOption(state, overview, category) is null))
        {
            return false;
        }

        if (string.Equals(
                state.BuildMethod,
                CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal))
        {
            return _assignments.Values.OrderBy(rank => rank, StringComparer.Ordinal).SequenceEqual(
                state.Authority.PriorityArray.OrderBy(rank => rank, StringComparer.Ordinal),
                StringComparer.Ordinal);
        }

        return string.Equals(
                   state.BuildMethod,
                   CharacterCreationBuildMethods.SumToTen,
                   StringComparison.Ordinal)
               && state.Authority.SumToTenTarget is int target
               && SumToTenUsed(state, overview) == target;
    }

    public int? SumToTenUsed(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
    {
        if (!Matches(state, overview))
            return null;
        long sum = 0;
        foreach (string category in CharacterCreationPriorityCategoryIds.Ordered)
        {
            CharacterCreationPriorityOptionProjection? option = SelectedOption(
                state,
                overview,
                category);
            if (option is not null)
                sum += option.SumToTenValue;
        }
        return sum <= int.MaxValue ? (int)sum : null;
    }

    public int? BaseNormalAttributePoints(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
        => SelectedOption(
                state,
                overview,
                CharacterCreationPriorityCategoryIds.Attributes)
            ?.BaseNormalAttributePoints;

    private void RestorePendingDraft(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
    {
        CharacterCreationPrerequisiteDraft? pending = state.PendingDraft;
        if (!CreationPrerequisitePhoneAuthority.IsReady(state, overview)
            || pending is null
            || !string.Equals(
                pending.Schema,
                CharacterCreationPrerequisiteSchemas.DraftV1,
                StringComparison.Ordinal)
            || pending.WorkspaceId != state.Binding.WorkspaceId
            || pending.DraftRevision <= 0
            || pending.BaseContentRevision <= 0
            || pending.BaseContentRevision >= state.Binding.ContentRevision
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(pending.DraftDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                pending.BaseRawCharacterXmlDigest)
            || !string.Equals(
                pending.BaseRawCharacterXmlDigest,
                state.Binding.RawCharacterXmlDigest,
                StringComparison.Ordinal)
            || !string.Equals(pending.AuthorityDigest, state.Binding.AuthorityDigest, StringComparison.Ordinal)
            || !string.Equals(pending.BuildMethod, state.BuildMethod, StringComparison.Ordinal)
            || !string.Equals(
                pending.SettingsProfileId,
                state.Authority.SettingsProfileId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pending.PriorityTable, state.Authority.PriorityTable, StringComparison.Ordinal)
            || !pending.PriorityArray.SequenceEqual(state.Authority.PriorityArray, StringComparer.Ordinal)
            || pending.SumToTenTarget != state.Authority.SumToTenTarget
            || pending.CreationKarmaTotal != state.CreationKarmaBudget.Total
            || pending.CreationKarmaUsed != state.CreationKarmaBudget.Used
            || pending.Assignments.Count != CharacterCreationPriorityCategoryIds.Ordered.Count)
        {
            return;
        }

        for (int index = 0; index < CharacterCreationPriorityCategoryIds.Ordered.Count; index++)
        {
            CharacterCreationPriorityAssignment assignment = pending.Assignments[index];
            string category = CharacterCreationPriorityCategoryIds.Ordered[index];
            CharacterCreationPriorityOptionProjection? option =
                CreationPrerequisitePhoneAuthority.ResolveUniqueOption(state, category, assignment.Rank);
            if (assignment.Order != index
                || !string.Equals(assignment.CategoryId, category, StringComparison.Ordinal)
                || option is null
                || !CreationPrerequisitePhoneAuthority.AssignmentMatchesOption(assignment, option))
            {
                _assignments.Clear();
                return;
            }
            _assignments[category] = assignment.Rank;
        }

        if (!CanPrepare(state, overview))
            _assignments.Clear();
    }

    private string? CandidateBlocker(
        CharacterCreationPrerequisiteState state,
        string categoryId,
        string rank)
    {
        var candidate = new Dictionary<string, string>(_assignments, StringComparer.Ordinal)
        {
            [categoryId] = rank
        };
        if (string.Equals(
                state.BuildMethod,
                CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal))
        {
            Dictionary<string, int> required = state.Authority.PriorityArray
                .GroupBy(value => value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            bool exceeds = candidate.Values
                .GroupBy(value => value, StringComparer.Ordinal)
                .Any(group => !required.TryGetValue(group.Key, out int count) || group.Count() > count);
            return exceeds ? PriorityRankExhausted : null;
        }

        if (!string.Equals(
                state.BuildMethod,
                CharacterCreationBuildMethods.SumToTen,
                StringComparison.Ordinal)
            || state.Authority.SumToTenTarget is not int target)
        {
            return CharacterCreationPrerequisiteBlockers.BuildMethodUnsupported;
        }

        return CanReachSumToTenTarget(state, candidate, target, 0, 0)
            ? null
            : SumToTenTargetUnreachable;
    }

    private static bool CanReachSumToTenTarget(
        CharacterCreationPrerequisiteState state,
        IReadOnlyDictionary<string, string> assignments,
        int target,
        int index,
        long used)
    {
        if (used > target)
            return false;
        if (index == CharacterCreationPriorityCategoryIds.Ordered.Count)
            return used == target;

        string category = CharacterCreationPriorityCategoryIds.Ordered[index];
        if (assignments.TryGetValue(category, out string? selectedRank))
        {
            CharacterCreationPriorityOptionProjection? selected =
                CreationPrerequisitePhoneAuthority.ResolveUniqueOption(state, category, selectedRank);
            return selected is not null
                   && CanReachSumToTenTarget(
                       state,
                       assignments,
                       target,
                       index + 1,
                       used + selected.SumToTenValue);
        }

        return state.Authority.Options
            .Where(option => string.Equals(option.CategoryId, category, StringComparison.Ordinal))
            .Any(option => CanReachSumToTenTarget(
                state,
                assignments,
                target,
                index + 1,
                used + option.SumToTenValue));
    }

    private static int RankOrder(CharacterCreationPrerequisiteAuthority authority, string rank)
    {
        for (int index = 0; index < authority.PriorityArray.Count; index++)
        {
            if (string.Equals(authority.PriorityArray[index], rank, StringComparison.Ordinal))
                return index;
        }
        return int.MaxValue;
    }
}

internal static class CreationPrerequisitePhoneAuthority
{
    public static bool IsReady(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
        => MatchesOverview(state, overview)
           && string.Equals(
               state.Schema,
               CharacterCreationPrerequisiteSchemas.SnapshotV1,
               StringComparison.Ordinal)
           && string.Equals(
               state.Authority.Schema,
               CharacterCreationPrerequisiteSchemas.AuthorityV1,
               StringComparison.Ordinal)
           && !state.CharacterCreated
           && state.RequiresMetatypeAttributeAdjustment
           && !state.CanEnterAttributes
           && state.Authority.IsAuthoritative
           && state.Blockers.Count == 0
           && state.Authority.Blockers.Count == 0
           && state.CreationKarmaBudget.IsExact
           && state.CreationKarmaBudget.Blockers.Count == 0
           && string.Equals(
               state.CreationKarmaBudget.BudgetId,
               CharacterCreationBudgetIds.Karma,
               StringComparison.Ordinal)
           && state.CreationKarmaBudget.Total >= 0m
           && state.CreationKarmaBudget.Used >= 0m
           && state.CreationKarmaBudget.Remaining
               == state.CreationKarmaBudget.Total - state.CreationKarmaBudget.Used
           && state.BuildMethod is (CharacterCreationBuildMethods.Priority
               or CharacterCreationBuildMethods.SumToTen)
           && string.Equals(state.BuildMethod, state.Authority.BuildMethod, StringComparison.Ordinal)
           && state.Authority.CreationKarmaTotal is int creationKarmaTotal
           && creationKarmaTotal == state.CreationKarmaBudget.Total
           && state.Authority.PriorityArray.Count
               == CharacterCreationPriorityCategoryIds.Ordered.Count
           && state.Authority.PriorityArray.All(rank => !string.IsNullOrWhiteSpace(rank))
           && state.Authority.SumToTenTarget is >= 0
           && state.Authority.SourceAnchorIds.Count > 0
           && state.Authority.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
               state.Authority.RawProfileInputsDigest)
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
               state.Authority.RawPrioritiesXmlDigest)
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
               state.Authority.EffectivePrioritiesInputsDigest)
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
               state.Authority.SelectedPriorityCustomDataInputsDigest)
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(state.SnapshotDigest)
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
               state.Binding.RawCharacterXmlDigest)
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
               state.Binding.AuxiliaryStateDigest)
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
               state.Binding.AuthorityDigest)
           && string.Equals(
               state.Binding.AuthorityDigest,
               state.Authority.AuthorityDigest,
               StringComparison.Ordinal)
           && CharacterCreationPriorityCategoryIds.Ordered.All(category =>
           {
               CharacterCreationPriorityOptionProjection[] categoryOptions = state.Authority.Options
                   .Where(option => string.Equals(
                       option.CategoryId,
                       category,
                       StringComparison.Ordinal))
                   .ToArray();
               return categoryOptions.Select(option => option.CategoryName)
                          .Distinct(StringComparer.Ordinal)
                          .Count() == 1
                      && categoryOptions.Select(option => option.Rank)
                          .OrderBy(rank => rank, StringComparer.Ordinal)
                          .SequenceEqual(
                              state.Authority.PriorityArray.Distinct(StringComparer.Ordinal)
                                  .OrderBy(rank => rank, StringComparer.Ordinal),
                              StringComparer.Ordinal);
           })
           && state.Authority.RankWeights.GroupBy(
                   weight => weight.Rank,
                   StringComparer.Ordinal)
               .All(group => group.Count() == 1)
           && state.Authority.RankWeights.All(weight =>
               !string.IsNullOrWhiteSpace(weight.Rank)
               && weight.Value >= 0
               && weight.SourceAnchorIds.Count > 0
               && weight.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor)))
           && state.Authority.PriorityArray.Distinct(StringComparer.Ordinal).All(rank =>
               state.Authority.RankWeights.Count(weight => string.Equals(
                   weight.Rank,
                   rank,
                   StringComparison.Ordinal)) == 1)
           && state.Authority.Options.All(IsExactProjectedOption)
           && state.Authority.Options.All(option =>
               state.Authority.RankWeights.Count(weight =>
                   string.Equals(weight.Rank, option.Rank, StringComparison.Ordinal)
                   && weight.Value == option.SumToTenValue) == 1)
           && state.Authority.Options.GroupBy(
                   option => $"{option.CategoryId}\0{option.Rank}",
                   StringComparer.Ordinal)
               .All(group => group.Count() == 1);

    public static bool MatchesOverview(
        CharacterCreationPrerequisiteState state,
        CharacterOverviewState overview)
        => overview.Profile?.Created == false
           && overview.WorkspaceId is { } workspaceId
           && state.Binding.WorkspaceId == workspaceId
           && state.Binding.ContentRevision == overview.ContentRevision
           && state.Binding.SavedRevision == overview.SavedRevision
           && overview.CreationWizard is { } wizard
           && string.Equals(wizard.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
           && wizard.WorkspaceRevision == overview.ContentRevision
           && string.Equals(wizard.RulesetId, state.RulesetId, StringComparison.Ordinal)
           && string.Equals(wizard.BuildMethod, state.BuildMethod, StringComparison.Ordinal)
           && !wizard.CharacterCreated;

    public static CharacterCreationPriorityOptionProjection? ResolveUniqueOption(
        CharacterCreationPrerequisiteState state,
        string categoryId,
        string rank)
    {
        CharacterCreationPriorityOptionProjection[] matches = state.Authority.Options
            .Where(option => string.Equals(option.CategoryId, categoryId, StringComparison.Ordinal)
                             && string.Equals(option.Rank, rank, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 && IsExactProjectedOption(matches[0]) ? matches[0] : null;
    }

    public static bool AssignmentMatchesOption(
        CharacterCreationPriorityAssignment assignment,
        CharacterCreationPriorityOptionProjection option)
        => string.Equals(assignment.CategoryId, option.CategoryId, StringComparison.Ordinal)
           && string.Equals(assignment.Rank, option.Rank, StringComparison.Ordinal)
           && string.Equals(assignment.SourceId, option.SourceId, StringComparison.Ordinal)
           && string.Equals(
               assignment.SourceNodeDigest,
               option.SourceNodeDigest,
               StringComparison.Ordinal)
           && assignment.SumToTenValue == option.SumToTenValue
           && assignment.BaseNormalAttributePoints == option.BaseNormalAttributePoints
           && assignment.SourceAnchorIds.SequenceEqual(option.SourceAnchorIds, StringComparer.Ordinal);

    public static bool BindingEquals(
        CharacterCreationPrerequisiteBinding left,
        CharacterCreationPrerequisiteBinding right)
        => left.WorkspaceId == right.WorkspaceId
           && left.ContentRevision == right.ContentRevision
           && left.SavedRevision == right.SavedRevision
           && string.Equals(
               left.RawCharacterXmlDigest,
               right.RawCharacterXmlDigest,
               StringComparison.Ordinal)
           && string.Equals(
               left.AuxiliaryStateDigest,
               right.AuxiliaryStateDigest,
               StringComparison.Ordinal)
           && string.Equals(left.AuthorityDigest, right.AuthorityDigest, StringComparison.Ordinal);

    public static bool ReceiptMatches(
        CharacterCreationPrerequisiteReceipt receipt,
        CharacterCreationPrerequisiteState refreshed,
        CharacterOverviewState overview)
        => receipt.WorkspaceId == refreshed.Binding.WorkspaceId
           && receipt.WorkspaceId == overview.WorkspaceId
           && receipt.PreviousContentRevision > 0
           && receipt.ContentRevision == refreshed.Binding.ContentRevision
           && receipt.ContentRevision == overview.ContentRevision
           && receipt.SavedRevision == refreshed.Binding.SavedRevision
           && receipt.SavedRevision == overview.SavedRevision
           && string.Equals(
               receipt.RawCharacterXmlDigest,
               refreshed.Binding.RawCharacterXmlDigest,
               StringComparison.Ordinal)
           && string.Equals(
               receipt.AuthorityDigest,
               refreshed.Binding.AuthorityDigest,
               StringComparison.Ordinal)
           && receipt.CreationKarmaRemaining == refreshed.CreationKarmaBudget.Remaining
           && receipt.BaseNormalAttributePoints == refreshed.BaseNormalAttributePoints
           && !receipt.CharacterDocumentChanged
           && refreshed.RequiresMetatypeAttributeAdjustment
           && !refreshed.CanEnterAttributes
           && refreshed.PendingDraft is { } pending
           && pending.DraftRevision == receipt.DraftRevision
           && string.Equals(pending.DraftDigest, receipt.DraftDigest, StringComparison.Ordinal)
           && string.Equals(
               pending.BaseRawCharacterXmlDigest,
               receipt.RawCharacterXmlDigest,
               StringComparison.Ordinal)
           && string.Equals(pending.AuthorityDigest, receipt.AuthorityDigest, StringComparison.Ordinal)
           && pending.CreationKarmaTotal - pending.CreationKarmaUsed
               == receipt.CreationKarmaRemaining
           && MatchesOverview(refreshed, overview);

    private static bool IsExactProjectedOption(CharacterCreationPriorityOptionProjection option)
        => CharacterCreationPriorityCategoryIds.Ordered.Contains(
               option.CategoryId,
               StringComparer.Ordinal)
           && !string.IsNullOrWhiteSpace(option.CategoryName)
           && !string.IsNullOrWhiteSpace(option.Rank)
           && Guid.TryParseExact(option.SourceId, "D", out Guid sourceId)
           && sourceId != Guid.Empty
           && !string.IsNullOrWhiteSpace(option.Label)
           && option.SumToTenValue >= 0
           && (string.Equals(
                   option.CategoryId,
                   CharacterCreationPriorityCategoryIds.Attributes,
                   StringComparison.Ordinal)
                   ? option.BaseNormalAttributePoints is >= 0
                   : option.BaseNormalAttributePoints is null)
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(option.SourceNodeDigest)
           && option.SourceAnchorIds.Count > 0
           && option.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor));
}
