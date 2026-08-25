using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Fail-closed Android boundary checks for Core's immutable creation-Attributes projections.
/// This class validates identity and receipt shape only; Core remains the authority for every
/// limit, cost, enabled state, and budget calculation.
/// </summary>
internal static class CreationAttributesPhoneAuthority
{
    public static bool IsReady(
        CharacterCreationAttributesState state,
        CharacterOverviewState overview)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(overview);
        if (!MatchesOverview(state, overview)
            || !string.Equals(
                state.Schema,
                CharacterCreationAttributesSchemas.SnapshotV1,
                StringComparison.Ordinal)
            || !IsCanonicalDigest(state.SnapshotDigest)
            || !state.CanEdit
            || state.Blockers.Count != 0
            || state.PrerequisiteDraft is not { } prerequisite
            || prerequisite.DraftRevision != state.Binding.PrerequisiteDraftRevision
            || !DigestEquals(
                prerequisite.DraftDigest,
                state.Binding.PrerequisiteDraftDigest)
            || !DigestEquals(
                prerequisite.AuthorityDigest,
                state.Binding.PrerequisiteAuthorityDigest)
            || !BudgetIsExact(state.NormalPointBudget)
            || !BudgetIsExact(state.SpecialPointBudget)
            || !BudgetIsExact(state.CreationKarmaBudget)
            || state.MaxNumberMaxAttributesCreate < 0
            || state.KarmaAttribute < 0
            || state.Attributes.Count == 0
            || state.Attributes.Select(attribute => attribute.AttributeId)
                .Distinct(StringComparer.Ordinal).Count() != state.Attributes.Count
            || state.Attributes.Any(attribute => !ProjectionShapeIsValid(attribute)))
        {
            return false;
        }

        string[] authoritativeAttributeIds = prerequisite.HeritageSelection?.Attributes
            .Select(attribute => attribute.AttributeId)
            .ToArray() ?? [];
        if (authoritativeAttributeIds.Length != state.Attributes.Count
            || !state.Attributes.Select(attribute => attribute.AttributeId)
                .SequenceEqual(authoritativeAttributeIds, StringComparer.Ordinal))
        {
            return false;
        }

        return state.PendingDraft is null || PendingDraftMatches(state, state.PendingDraft);
    }

    public static bool MatchesOverview(
        CharacterCreationAttributesState state,
        CharacterOverviewState overview)
        => overview.Profile?.Created == false
           && overview.WorkspaceId is { } workspaceId
           && workspaceId == state.Binding.WorkspaceId
           && overview.ContentRevision == state.Binding.ContentRevision
           && overview.SavedRevision == state.Binding.SavedRevision
           && IsCanonicalDigest(state.Binding.RawCharacterXmlDigest)
           && IsCanonicalAuxiliaryDigest(state.Binding.AuxiliaryStateDigest)
           && IsCanonicalDigest(state.Binding.PrerequisiteDraftDigest)
           && IsCanonicalDigest(state.Binding.PrerequisiteAuthorityDigest);

    public static bool BindingEquals(
        CharacterCreationAttributesBinding left,
        CharacterCreationAttributesBinding right)
        => left.WorkspaceId == right.WorkspaceId
           && left.ContentRevision == right.ContentRevision
           && left.SavedRevision == right.SavedRevision
           && left.PrerequisiteDraftRevision == right.PrerequisiteDraftRevision
           && DigestEquals(left.RawCharacterXmlDigest, right.RawCharacterXmlDigest)
           && DigestEquals(left.AuxiliaryStateDigest, right.AuxiliaryStateDigest)
           && DigestEquals(left.PrerequisiteDraftDigest, right.PrerequisiteDraftDigest)
           && DigestEquals(left.PrerequisiteAuthorityDigest, right.PrerequisiteAuthorityDigest);

    public static bool CanAdoptPreview(
        CharacterCreationAttributesState state,
        CharacterOverviewState overview,
        CharacterCreationFoundationResult<CharacterCreationAttributesPreview> result,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
    {
        if (!IsReady(state, overview)
            || result.Value is not { } preview
            || !BindingEquals(state.Binding, preview.Binding)
            || !AllocationIdentitiesMatch(state, allocations)
            || !ProjectionIdentitiesMatch(state, preview.Attributes)
            || !PreviewMatchesAllocations(preview, allocations))
        {
            return false;
        }

        return preview.Blockers.Count == 0
               || preview.Blockers.Count == 1
               && string.Equals(
                   preview.Blockers[0],
                   CharacterCreationAttributesBlockers.DraftDuplicate,
                   StringComparison.Ordinal);
    }

    public static bool CanConfirmPreview(
        CharacterCreationAttributesState state,
        CharacterOverviewState overview,
        CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
        => IsReady(state, overview)
           && BindingEquals(state.Binding, preview.Binding)
           && AllocationIdentitiesMatch(state, allocations)
           && ProjectionIdentitiesMatch(state, preview.Attributes)
           && PreviewMatchesAllocations(preview, allocations)
           && preview.RequiresExplicitConfirmation
           && preview.CanConfirm
           && preview.Blockers.Count == 0
           && BudgetIsExact(preview.NormalPointBudget)
           && BudgetIsExact(preview.SpecialPointBudget)
           && BudgetIsExact(preview.CreationKarmaBudget)
           && IsCanonicalDigest(preview.PreviewDigest);

    public static bool ReceiptMatches(
        CharacterCreationAttributesReceipt receipt,
        CharacterCreationAttributesPreview preview,
        CharacterCreationAttributesState refreshed,
        CharacterOverviewState overview)
        => IsReady(refreshed, overview)
           && refreshed.PendingDraft is { } pending
           && receipt.WorkspaceId == preview.Binding.WorkspaceId
           && receipt.WorkspaceId == refreshed.Binding.WorkspaceId
           && receipt.PreviousContentRevision == preview.Binding.ContentRevision
           && receipt.ContentRevision == refreshed.Binding.ContentRevision
           && receipt.SavedRevision == refreshed.Binding.SavedRevision
           && receipt.DraftRevision == pending.DraftRevision
           && DigestEquals(receipt.DraftDigest, pending.DraftDigest)
           && receipt.NormalPointsRemaining == refreshed.NormalPointBudget.Remaining
           && receipt.SpecialPointsRemaining == refreshed.SpecialPointBudget.Remaining
           && receipt.CreationKarmaRemaining == refreshed.CreationKarmaBudget.Remaining
           && !receipt.CharacterDocumentChanged
           && !pending.CharacterEffectsApplied
           && DigestEquals(
               preview.Binding.RawCharacterXmlDigest,
               refreshed.Binding.RawCharacterXmlDigest);

    public static bool IsCanonicalDigest(string? value)
        => CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(value);

    public static bool IsCanonicalAuxiliaryDigest(string? value)
        => value is { Length: 64 }
           && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool PreviewMatchesAllocations(
        CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
    {
        if (!string.Equals(
                preview.Schema,
                CharacterCreationAttributesSchemas.PreviewV1,
                StringComparison.Ordinal)
            || preview.Attributes.Count != allocations.Count
            || allocations.Select(allocation => allocation.AttributeId)
                .Distinct(StringComparer.Ordinal).Count() != allocations.Count
            || preview.Attributes.Any(attribute => !ProjectionShapeIsValid(attribute)))
        {
            return false;
        }

        var requested = allocations.ToDictionary(
            allocation => allocation.AttributeId,
            StringComparer.Ordinal);
        return preview.Attributes.All(attribute =>
            requested.TryGetValue(attribute.AttributeId, out CharacterCreationAttributeAllocation? allocation)
            && allocation.PriorityPoints == attribute.PriorityPointsSpent
            && allocation.KarmaLevels == attribute.KarmaLevels);
    }

    private static bool ProjectionShapeIsValid(CharacterCreationAttributeProjection attribute)
        => !string.IsNullOrWhiteSpace(attribute.AttributeId)
           && attribute.Category is (CharacterCreationAttributeCategories.Normal
               or CharacterCreationAttributeCategories.Special)
           && attribute.Minimum >= 0
           && attribute.Minimum <= attribute.Current
           && attribute.Current <= attribute.Maximum
           && attribute.Maximum <= attribute.AugmentedMaximum
           && attribute.PriorityPointsSpent >= 0
           && attribute.KarmaLevels >= 0
           && attribute.PriorityPointCost >= 0
           && attribute.KarmaCost >= 0
           && (attribute.IsEnabled
               || attribute.PriorityPointsSpent == 0 && attribute.KarmaLevels == 0)
           && attribute.DisableReasons.All(reason => !string.IsNullOrWhiteSpace(reason))
           && attribute.SourceAnchorIds.Count > 0
           && attribute.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor));

    private static bool PendingDraftMatches(
        CharacterCreationAttributesState state,
        CharacterCreationAttributesDraft pending)
        => string.Equals(
               pending.Schema,
               CharacterCreationAttributesSchemas.DraftV1,
               StringComparison.Ordinal)
           && pending.WorkspaceId == state.Binding.WorkspaceId
           && pending.DraftRevision > 0
           && pending.BaseContentRevision < state.Binding.ContentRevision
           && pending.PrerequisiteDraftRevision == state.Binding.PrerequisiteDraftRevision
           && DigestEquals(
               pending.BaseRawCharacterXmlDigest,
               state.Binding.RawCharacterXmlDigest)
           && DigestEquals(
               pending.PrerequisiteDraftDigest,
               state.Binding.PrerequisiteDraftDigest)
           && DigestEquals(
               pending.PrerequisiteAuthorityDigest,
               state.Binding.PrerequisiteAuthorityDigest)
           && IsCanonicalDigest(pending.DraftDigest)
           && !pending.CharacterEffectsApplied
           && pending.Attributes.Count == state.Attributes.Count
           && pending.Allocations.Count == state.Attributes.Count
           && ProjectionIdentitiesMatch(state, pending.Attributes)
           && AllocationIdentitiesMatch(state, pending.Allocations)
           && pending.Allocations.Select(allocation => allocation.AttributeId)
               .Distinct(StringComparer.Ordinal).Count() == pending.Allocations.Count;

    private static bool AllocationIdentitiesMatch(
        CharacterCreationAttributesState state,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
        => allocations.Select(allocation => allocation.AttributeId)
            .SequenceEqual(
                state.Attributes.Select(attribute => attribute.AttributeId),
                StringComparer.Ordinal);

    private static bool ProjectionIdentitiesMatch(
        CharacterCreationAttributesState state,
        IReadOnlyList<CharacterCreationAttributeProjection> attributes)
        => attributes.Select(attribute => attribute.AttributeId)
            .SequenceEqual(
                state.Attributes.Select(attribute => attribute.AttributeId),
                StringComparer.Ordinal);

    private static bool BudgetIsExact(CharacterCreationBudgetState budget)
        => budget.IsExact
           && budget.Blockers.Count == 0
           && budget.Total >= 0m
           && budget.Used >= 0m
           && budget.Remaining >= 0m
           && budget.Used <= budget.Total
           && budget.Remaining == budget.Total - budget.Used;

    private static bool DigestEquals(string? left, string? right)
        => CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(left, right);
}
